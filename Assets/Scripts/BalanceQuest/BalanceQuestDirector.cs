using System;
using System.Collections;
using UnityEngine;
using TMPro;
using Kinex.Motion;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// "Balance Quest" (เส้นทางนักสมดุล) — a Just-Dance-style glow stage where a fixed ~5-minute
    /// route of "beats" plays on a single continuous timeline. Mirrors Kinex.World.
    /// KinexWorldDirector's "never hard-blocks" philosophy: every beat times out with partial
    /// credit and an encouraging line, never waits for the player. Unlike KinexWorldDirector
    /// there is only ONE countdown (before the whole route), not one per beat — the route plays
    /// continuously, Just-Dance style.
    ///
    /// Scoring: averagePercent is the duration-weighted mean of every SCORED beat's Score01 x 100.
    /// Walk and Checkpoint beats are EXCLUDED from that average entirely (not counted as 1.0) —
    /// they aren't challenges, so folding them in would just dilute the number toward 100 for
    /// free. They ARE included in durationSeconds (the total route length) but NOT in the
    /// beats[] results list, which only reports beats that were actually scored.
    /// </summary>
    public class BalanceQuestDirector : MonoBehaviour
    {
        public enum State { Idle, Intro, Calibrating, Countdown, Active, Results }

        [Header("Content")]
        public QuestBeat[] route;

        [Header("Scene refs")]
        public MediaPipePoseDetector poseDetector;
        public TrailScroller trail;
        public QuestPropFactory props;
        public AvatarLaneMover avatarMover;

        [Header("Detectors / Stub")]
        [Tooltip("ON by default: swap every detector read for KeyboardMotionStub so the whole " +
                 "route is walkable with a keyboard before the camera pipeline is verified on device.")]
        public bool useKeyboardStub = true;

        [Header("Timing")]
        public float introSeconds = 3f;
        [Range(1, 5)] public int countdownSeconds = 3;
        [Tooltip("Minimum seconds of calibration samples before an early proceed is allowed.")]
        public float calibSeconds = 3f;
        [Tooltip("Hard cap — calibration proceeds anyway after this even with no pose.")]
        public float calibTimeoutSeconds = 15f;

        [Header("Speeds")]
        public float walkSpeed = 2.2f;

        [Header("UI - Intro")]
        public GameObject introPanel;
        public TMP_Text introTitleText;
        public TMP_Text introSubtitleText;

        [Header("UI - Calibration")]
        public GameObject calibPanel;
        public TMP_Text calibPromptText;

        [Header("UI - Countdown")]
        public GameObject countdownPanel;
        public TMP_Text countdownText;

        [Header("UI - HUD")]
        public GameObject hudPanel;
        public TMP_Text coinCounterText;
        public TMP_Text progressText;
        public GameObject cueCard;
        public TMP_Text cueArrowText;
        public TMP_Text cueInstructionText;

        [Header("UI - Checkpoint")]
        public GameObject checkpointPanel;
        public TMP_Text checkpointText;

        [Header("UI - Results")]
        public GameObject resultsPanel;
        public TMP_Text resultsStarsText;
        public TMP_Text resultsPercentText;
        public TMP_Text resultsCoinsText;

        [Header("UI - Pause")]
        public GameObject pauseOverlay;

        [Header("Audio / Voice")]
        public AudioSource musicSource;
        public AudioClip music;
        [Range(0f, 1f)] public float musicVolume = 0.4f;
        public Kinex.MegaDance.VoiceCoach voice;

        public event Action<BalanceQuestResult> OnSessionComplete;
        public event Action OnExitRequested;

        public State Current { get; private set; } = State.Idle;
        public BalanceQuestResult LastResult { get; private set; }

        readonly LaneDetector _laneDetector = new LaneDetector();
        readonly SingleLegStanceDetector _stanceDetector = new SingleLegStanceDetector();
        readonly TiptoeDetector _tiptoeDetector = new TiptoeDetector();
        readonly LegAbductionDetector _abductionDetector = new LegAbductionDetector();
        readonly HipExtensionDetector _hipExtensionDetector = new HipExtensionDetector();
        readonly PoseGate _poseGate = new PoseGate();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();

        QuestContext _ctx;
        WobbleGaugeUI _wobbleGauge;
        Coroutine _run;
        int _coins;

        void Awake()
        {
            if (route == null || route.Length == 0) route = RouteLibrary.DefaultRoute;
            _wobbleGauge = WobbleGaugeUI.Ensure(hudPanel);
        }

        // ---------------------------------------------------------------- public API

        public void StartSession()
        {
            if (_run != null) StopCoroutine(_run);
            _run = StartCoroutine(RunSession());
        }

        public void ExitSession()
        {
            if (_run != null) { StopCoroutine(_run); _run = null; }
            props?.DespawnAll();
            Current = State.Idle;
            ShowOnly(null);
            StopMusic();
            OnExitRequested?.Invoke();
        }

        // ---------------------------------------------------------------- main loop

        IEnumerator RunSession()
        {
            BuildContext();
            _coins = 0;
            LastResult = new BalanceQuestResult();

            var beats = (route != null && route.Length > 0) ? route : RouteLibrary.DefaultRoute;
            int totalStages = CountCheckpoints(beats);
            int stage = 0;

            if (coinCounterText) coinCounterText.text = "0";
            if (progressText) progressText.text = $"ด่าน 1/{Mathf.Max(totalStages, 1)}";

            Current = State.Intro;
            ShowOnly(introPanel);
            StartMusic();
            if (introTitleText) introTitleText.text = "เส้นทางนักสมดุล";
            if (introSubtitleText) introSubtitleText.text = "เตรียมเก้าอี้มั่นคงไว้ด้านหลังก่อนเริ่มเล่นนะครับ";
            yield return new WaitForSeconds(introSeconds);

            yield return Calibrate();
            yield return CountdownRoutine();

            Current = State.Active;
            ShowOnly(hudPanel);
            avatarMover?.SnapToCenter();

            float weightedSum = 0f, weightedDuration = 0f;

            for (int i = 0; i < beats.Length; i++)
            {
                var beat = beats[i];
                switch (beat.type)
                {
                    case BeatType.Walk:
                        yield return RunWalkBeat(beat);
                        break;

                    case BeatType.Checkpoint:
                        stage++;
                        yield return RunCheckpointBeat(beat, Mathf.Min(stage, Mathf.Max(totalStages, 1)));
                        if (progressText) progressText.text = $"ด่าน {Mathf.Min(stage, Mathf.Max(totalStages, 1))}/{Mathf.Max(totalStages, 1)}";
                        break;

                    default:
                        var runner = MakeRunner(beat.type);
                        if (runner == null) break;
                        yield return RunScoredBeat(beat, runner);
                        float scorePct = runner.Score01 * 100f;
                        LastResult.beats.Add(new BalanceQuestResult.BeatResult { type = beat.type.ToString(), score = scorePct });
                        weightedSum += runner.Score01 * beat.duration;
                        weightedDuration += beat.duration;
                        break;
                }

                if (coinCounterText) coinCounterText.text = _coins.ToString();
            }

            float avg01 = weightedDuration > 0f ? weightedSum / weightedDuration : 0f;
            LastResult.averagePercent = Mathf.Round(avg01 * 1000f) / 10f;
            LastResult.coins = _coins;
            LastResult.durationSeconds = SumDurations(beats);
            LastResult.stars = ComputeStars(LastResult.averagePercent);

            Current = State.Results;
            ShowOnly(resultsPanel);
            StopMusic();
            if (resultsPercentText) resultsPercentText.text = $"{LastResult.averagePercent:0.#}%";
            if (resultsCoinsText) resultsCoinsText.text = LastResult.coins.ToString();
            if (resultsStarsText) resultsStarsText.text = new string('*', Mathf.Clamp(LastResult.stars, 1, 3));

            _run = null;
            OnSessionComplete?.Invoke(LastResult);
        }

        IEnumerator Calibrate()
        {
            Current = State.Calibrating;
            ShowOnly(calibPanel);
            if (calibPromptText) calibPromptText.text = "ยืนตรงกลางให้เห็นเต็มตัว 3 วินาที\nเตรียมเก้าอี้มั่นคงไว้ด้านหลัง";

            if (useKeyboardStub)
            {
                yield return new WaitForSeconds(1f);
                yield break;
            }

            float t = 0f;
            while (t < calibTimeoutSeconds)
            {
                float dt = Time.deltaTime;
                t += dt;

                var kp = poseDetector != null ? poseDetector.LatestKeypoints : null;
                var conf = poseDetector != null ? poseDetector.LatestConfidence : null;
                bool hasPose = poseDetector != null && poseDetector.HasPose;
                _poseGate.Tick(hasPose, conf, dt);

                if (_poseGate.Visible && kp != null)
                {
                    _laneDetector.CalibrateCenter(kp);
                    _stanceDetector.CalibrateStanding(kp);
                    _tiptoeDetector.CalibrateStanding(kp);
                    _abductionDetector.SetBaseline(kp);
                    var lm = poseDetector != null ? poseDetector.Landmarks33 : null;
                    if (lm != null) _hipExtensionDetector.SetBaseline(lm);

                    if (t >= calibSeconds && _laneDetector.IsCalibrated) break;
                }
                yield return null;
            }
        }

        IEnumerator CountdownRoutine()
        {
            Current = State.Countdown;
            ShowOnly(countdownPanel);
            for (int n = countdownSeconds; n > 0; n--)
            {
                if (countdownText) countdownText.text = n.ToString();
                yield return new WaitForSeconds(1f);
            }
        }

        IEnumerator RunWalkBeat(QuestBeat beat)
        {
            trail.Speed = walkSpeed;
            const float CoinIntervalSeconds = 1.2f;
            const float CoinTravelSeconds = 2f;

            float t = 0f, spawnClock = 0f;
            var activeCoins = new System.Collections.Generic.List<Transform>();

            while (t < beat.duration)
            {
                float dt = Time.deltaTime;
                TickDetectors(dt);
                bool paused = _ctx.ShouldPauseGame;
                if (pauseOverlay) pauseOverlay.SetActive(paused);

                if (!paused)
                {
                    t += dt;
                    avatarMover?.Tick(_ctx);
                    spawnClock += dt;
                    if (spawnClock >= CoinIntervalSeconds)
                    {
                        spawnClock = 0f;
                        float z = trail.SpawnZ(CoinTravelSeconds);
                        var coin = props.SpawnCoin(_ctx.CurrentScreenLane, z);
                        trail.Attach(coin);
                        activeCoins.Add(coin);
                    }

                    for (int i = activeCoins.Count - 1; i >= 0; i--)
                    {
                        var c = activeCoins[i];
                        if (c == null) { activeCoins.RemoveAt(i); continue; }
                        if (c.position.z <= 0.3f)
                        {
                            trail.Detach(c);
                            props.Despawn(c);
                            activeCoins.RemoveAt(i);
                            _coins++;
                            Sfx.Play("coin");
                            KinexFx.PopBurst(c.position, new Color(1f, 0.85f, 0.2f));
                        }
                    }
                }
                yield return null;
            }

            props.DespawnAll();
        }

        IEnumerator RunCheckpointBeat(QuestBeat beat, int stageNum)
        {
            trail.Speed = 0f;
            if (checkpointPanel) checkpointPanel.SetActive(true);
            if (checkpointText) checkpointText.text = $"ผ่านด่านที่ {stageNum}!";
            Sfx.Play("checkpoint");
            KinexFx.ConfettiBurst(avatarMover != null ? avatarMover.transform.position + Vector3.up : Vector3.up);

            if (!useKeyboardStub && poseDetector != null)
            {
                var kp = poseDetector.LatestKeypoints;
                if (kp != null) _laneDetector.RecalibrateCenter(kp);
            }

            float t = 0f;
            while (t < beat.duration) { t += Time.deltaTime; yield return null; }
            if (checkpointPanel) checkpointPanel.SetActive(false);
        }

        IEnumerator RunScoredBeat(QuestBeat beat, IQuestBeatRunner runner)
        {
            trail.Speed = StopsScroll(beat.type) ? 0f : walkSpeed;
            bool usesWobbleGauge = beat.type == BeatType.Bridge || beat.type == BeatType.TandemStand;
            _wobbleGauge?.Show(usesWobbleGauge);
            if (cueCard) cueCard.SetActive(true);

            runner.Begin(_ctx, beat);
            float t = 0f;
            while (t < beat.duration)
            {
                float dt = Time.deltaTime;
                TickDetectors(dt);
                bool paused = _ctx.ShouldPauseGame;
                if (pauseOverlay) pauseOverlay.SetActive(paused);

                if (!paused)
                {
                    t += dt;
                    avatarMover?.Tick(_ctx);
                    runner.Tick(dt);
                }
                yield return null;
            }

            props.DespawnAll();
            _wobbleGauge?.Show(false);
            if (cueCard) cueCard.SetActive(false);
        }

        // ---------------------------------------------------------------- detector plumbing

        void TickDetectors(float dt)
        {
            if (useKeyboardStub) { _stub.Tick(dt); return; }

            var kp = poseDetector != null ? poseDetector.LatestKeypoints : null;
            var conf = poseDetector != null ? poseDetector.LatestConfidence : null;
            bool hasPose = poseDetector != null && poseDetector.HasPose;
            _poseGate.Tick(hasPose, conf, dt);

            if (kp != null && conf != null)
            {
                _laneDetector.Tick(kp, conf, dt);
                _stanceDetector.Tick(kp, conf, dt);
                _tiptoeDetector.Tick(kp, conf, dt);
                _abductionDetector.Tick(kp, conf, dt);
            }

            var lm = poseDetector != null ? poseDetector.Landmarks33 : null;
            if (lm != null) _hipExtensionDetector.Tick(lm, dt);
        }

        void BuildContext()
        {
            _ctx = new QuestContext
            {
                useKeyboardStub = useKeyboardStub,
                laneDetector = _laneDetector,
                stanceDetector = _stanceDetector,
                tiptoeDetector = _tiptoeDetector,
                abductionDetector = _abductionDetector,
                hipExtensionDetector = _hipExtensionDetector,
                poseGate = _poseGate,
                stub = _stub,
                poseDetector = poseDetector,
                trail = trail,
                props = props,
                avatarMover = avatarMover,
                host = this,
                wobbleGauge = _wobbleGauge,
                setCue = (icon, text) =>
                {
                    if (cueArrowText) cueArrowText.text = icon;
                    if (cueInstructionText) cueInstructionText.text = text;
                    if (cueCard) cueCard.SetActive(true);
                },
                voiceLine = s => { if (voice != null) { Music.Duck(2.5f); voice.Speak(s); } },
                addCoin = () => _coins++,
            };
        }

        /// <summary>Public + static so BalanceQuestSelfTest exercises the SAME beat-type gate the
        /// session loop uses: a null runner (Walk / Checkpoint) is exactly what excludes a beat
        /// from scoring — there is no second exclusion list to drift out of sync.</summary>
        public static IQuestBeatRunner MakeRunner(BeatType type) => type switch
        {
            BeatType.Gate => new GateBeatRunner(),
            BeatType.Bridge => new BridgeBeatRunner(),
            BeatType.Tiptoe => new TiptoeBeatRunner(),
            BeatType.Kick => new KickBeatRunner(),
            BeatType.BackKick => new BackKickBeatRunner(),
            BeatType.TandemStand => new TandemStandBeatRunner(),
            BeatType.BeamWalk => new BeamWalkBeatRunner(),
            BeatType.RestStop => new RestStopBeatRunner(),
            _ => null,
        };

        static bool StopsScroll(BeatType t) =>
            t == BeatType.Bridge || t == BeatType.TandemStand || t == BeatType.BeamWalk || t == BeatType.RestStop;

        static int CountCheckpoints(QuestBeat[] beats)
        {
            int n = 0;
            foreach (var b in beats) if (b.type == BeatType.Checkpoint) n++;
            return n;
        }

        static float SumDurations(QuestBeat[] beats)
        {
            float s = 0f;
            foreach (var b in beats) s += b.duration;
            return s;
        }

        /// <summary>Senior-friendly: NEVER zero — even a rough run still earns 1 star (matches
        /// the "never hard-blocks" philosophy: everyone finishes with something to celebrate).
        /// Public + static so BalanceQuestSelfTest checks the exact thresholds gameplay uses.</summary>
        public static int ComputeStars(float pct)
        {
            if (pct >= 80f) return 3;
            if (pct >= 60f) return 2;
            return 1;
        }

        // ---------------------------------------------------------------- helpers

        void StartMusic()
        {
            if (musicSource == null || music == null)
            {
                // No hand-assigned clip — use the shared music player (Resources/Music).
                Music.Play("quest_theme", musicVolume);
                return;
            }
            if (musicSource.isPlaying && musicSource.clip == music) return;
            musicSource.clip = music;
            musicSource.loop = true;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        void StopMusic()
        {
            if (musicSource != null) musicSource.Stop();
            Music.Stop();
        }

        void ShowOnly(GameObject panel)
        {
            GameObject[] all = { introPanel, calibPanel, countdownPanel, hudPanel, resultsPanel };
            foreach (var p in all)
                if (p != null) p.SetActive(p == panel);
        }
    }
}
