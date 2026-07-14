using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.FX;
using Kinex.Motion;
using Kinex.UI;
using Kinex.MegaDance; // VoiceCoach

namespace Kinex.FruitGame
{
    /// <summary>
    /// Fruit Header (สวนผลไม้) core loop. The player sits on a real chair facing the tablet;
    /// food drifts into a glowing zone above the avatar's head. HEALTHY item in zone → stand up
    /// to head it (pop + confetti + combo). JUNK item → stay seated (smart-choice bonus).
    /// Senior-friendly: no fail state, every outcome is warm and positive.
    ///
    /// Flow: Idle → Intro → CalibSeated → CalibStanding → Countdown →
    ///       SetPlay(1..3) ⇄ Rest → Bonus (knee-raise balloon) → Results.
    ///
    /// Dose: 15 stands = 1 set, 3 sets, 60 s skippable rest between sets, bonus round of 20
    /// alternating seated knee raises (45 s cap) after each rest.
    ///
    /// Motion source is swappable: useKeyboardStub (default TRUE) plays the whole game with
    /// S = stand/sit and K/J = knees; flip it off for the live MediaPipe detector feeding the
    /// frozen Kinex.Motion layer (SitStandDetector / KneeRaiseDetector / PoseGate).
    /// </summary>
    public class FruitGameManager : MonoBehaviour
    {
        public enum State { Idle, Intro, CalibSeated, CalibStanding, Countdown, SetPlay, Rest, Bonus, Results }

        [Header("Motion source")]
        [Tooltip("Play with the keyboard: S = stand/sit toggle, K/J = left/right knee raise. " +
                 "Bypasses the camera entirely so the whole game runs in the editor.")]
        public bool useKeyboardStub = true;
        [Tooltip("Live keypoint source. Only used when useKeyboardStub is false.")]
        public MediaPipePoseDetector poseDetector;

        [Header("Scene refs")]
        public FruitSpawner spawner;
        public HeaderZone headerZone;
        public AvatarRootLift rootLift;
        [Tooltip("Offline Piper voice for the coach lines. Safe to leave unassigned (silent).")]
        public VoiceCoach voice;

        [Header("Dose")]
        public int repsPerSet = 15;
        public int totalSets = 3;
        public float restSeconds = 60f;
        [Tooltip("The rest-skip button appears after this many seconds of rest.")]
        public float restSkippableAfter = 20f;
        public int bonusKneeTarget = 20;
        public float bonusMaxSeconds = 45f;

        [Header("Timing")]
        public float introSeconds = 3f;
        public float calibSeatedTimeout = 12f;
        public float calibStandingTimeout = 10f;
        [Tooltip("Guided calibration retries (seated or standing) before falling back to an estimate.")]
        public int calibMaxAttempts = 3;
        [Tooltip("Progress01 at/above this counts as 'standing now' (zone-entry auto-resolve).")]
        [Range(0.5f, 0.95f)] public float standingNowThreshold = 0.62f;

        [Header("UI — panels")]
        public GameObject introPanel;
        public GameObject calibPanel;
        public GameObject countdownPanel;
        public GameObject hudPanel;
        public GameObject restPanel;
        public GameObject bonusPanel;
        public GameObject resultsPanel;
        public GameObject pauseOverlay;

        [Header("UI — texts & widgets")]
        public TMP_Text calibText;
        public TMP_Text countdownText;
        public TMP_Text setText;
        public TMP_Text repText;
        public TMP_Text comboText;
        public TMP_Text restCountdownText;
        public TMP_Text bonusCountText;
        public TMP_Text resultsStatsText;
        public Image[] resultsStarImages;
        public GameObject skipRestButton;

        [Header("UI — calibration feedback")]
        [Tooltip("Fills 0..1 while a seated/standing capture is being held during calibration.")]
        public Image calibProgressBar;
        [Tooltip("Persistent banner shown for the rest of the session if calibration exhausted every " +
                 "retry and fell back to an estimate — never a silent fallback.")]
        public GameObject calibWarningBanner;

        [Header("UI — debug meter")]
        [Tooltip("Small always-on Progress01 bar + phase label for tuning on device. Safe to leave ON.")]
        public bool debugMeter = true;
        public GameObject debugMeterPanel;
        public Image debugMeterFill;
        public TMP_Text debugMeterLabel;

        /// <summary>Fired once with the final result — FruitGameResultBridge relays it to Flutter.</summary>
        public event Action<FruitGameResult> OnSessionComplete;
        /// <summary>Fired by the exit button — the bridge sends {"type":"exit"}.</summary>
        public event Action OnExitRequested;

        State _state = State.Idle;
        readonly FruitScoreState _score = new FruitScoreState();
        readonly SitStandDetector _sitStand = new SitStandDetector();
        readonly KneeRaiseDetector _knee = new KneeRaiseDetector();
        readonly PoseGate _gate = new PoseGate();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();

        GameHud _hud;
        BreathingCue _breathing;
        int _currentSet;
        float _startTime;
        float _seatedHipY, _seatedTorso;
        int _seatedSamples;
        bool _introSkipped, _skipRest;
        Coroutine _comboFlash;

        // ---- Motion facade: one place that switches between stub and live detectors. ----
        bool HasLivePose => poseDetector != null && poseDetector.HasPose;
        float Progress01 => useKeyboardStub ? _stub.Progress01 : _sitStand.Progress01;
        bool JustStood => useKeyboardStub ? _stub.JustStood : _sitStand.JustStood;
        bool IsStandingNow => Progress01 >= standingNowThreshold;
        bool ShouldPause => !useKeyboardStub && _gate.ShouldPauseGame;
        int KneeCount => useKeyboardStub ? _stub.AlternatingCount : _knee.AlternatingCount;

        void TickMotion(float dt)
        {
            if (useKeyboardStub) { _stub.Tick(dt); return; }
            bool has = HasLivePose;
            _gate.Tick(has, has ? poseDetector.LatestConfidence : null, dt);
            if (has && _gate.Visible)
            {
                _sitStand.Tick(poseDetector.LatestKeypoints, poseDetector.LatestConfidence, dt);
                _knee.Tick(poseDetector.LatestKeypoints, poseDetector.LatestConfidence, dt);
            }
        }

        void Start()
        {
            SetPanels(intro: false, calib: false, countdown: false, hud: false,
                      rest: false, bonus: false, results: false);
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            if (spawner != null) spawner.ItemZoneExpired += HandleZoneExpired;
            StartCoroutine(AutoStart());
        }

        // One frame so every other Awake/Start (detector, stage, UI) completes first.
        IEnumerator AutoStart()
        {
            yield return null;
            StartCoroutine(GameFlow());
        }

        void Update()
        {
            TickMotion(Time.deltaTime);
            if (rootLift != null) rootLift.SetProgress01(Progress01);
            if (_state == State.SetPlay && _hud != null)
            {
                _hud.SetScore(Progress01);
                _hud.SetSubLabel($"เซต {_currentSet}/{totalSets}");
            }
            UpdateDebugMeter();
        }

        // ---- Debug meter: live Progress01 bar + phase label, gated behind debugMeter. ----
        void UpdateDebugMeter()
        {
            if (debugMeterPanel != null && debugMeterPanel.activeSelf != debugMeter)
                debugMeterPanel.SetActive(debugMeter);
            if (!debugMeter) return;

            if (debugMeterFill != null) debugMeterFill.fillAmount = Progress01;
            if (debugMeterLabel != null)
            {
                string phase = useKeyboardStub ? "stub" : _sitStand.Current.ToString();
                debugMeterLabel.text = $"{phase} {Progress01:0.00}";
            }
        }

        // ---- Buttons (wired by FruitGameUIBuilder) ----
        public void SkipIntro() => _introSkipped = true;
        public void SkipRest() => _skipRest = true;
        public void ExitToHome() => OnExitRequested?.Invoke();

        // =====================================================================
        IEnumerator GameFlow()
        {
            _startTime = Time.time;

            // ---- Intro ----
            _state = State.Intro;
            ShowOnly(introPanel);
            Kinex.Sfx.Play("pose_appear");
            Speak("ยินดีต้อนรับสู่สวนผลไม้");
            _introSkipped = false;
            for (float t = 0f; t < introSeconds && !_introSkipped; t += Time.deltaTime)
                yield return null;

            yield return CalibSeatedRoutine();
            yield return CalibStandingRoutine();
            yield return CountdownRoutine();

            Kinex.Music.Play("fruit_theme");

            for (int set = 1; set <= totalSets; set++)
            {
                yield return SetPlayRoutine(set);
                if (set < totalSets)
                {
                    yield return RestRoutine();
                    yield return BonusRoutine();
                }
            }

            ShowResults();
        }

        // ---- Calibration: seated baseline (average while visible ~2 s). Guided retry — never a
        // silent fallback: up to calibMaxAttempts tries, only then does the game proceed with
        // whatever partial data it has and shows the persistent warning banner. ----
        IEnumerator CalibSeatedRoutine()
        {
            _state = State.CalibSeated;
            ShowOnly(calibPanel);
            if (calibText != null) calibText.text = "นั่งบนเก้าอี้ให้เห็นเต็มตัว";
            Speak("นั่งบนเก้าอี้ ให้กล้องเห็นเต็มตัวนะ");

            if (useKeyboardStub) { yield return new WaitForSeconds(1.5f); yield break; }

            bool success = false;
            for (int attempt = 1; attempt <= calibMaxAttempts && !success; attempt++)
            {
                if (attempt > 1)
                {
                    if (calibText != null) calibText.text = $"นั่งตัวตรง ค้างไว้ ({attempt}/{calibMaxAttempts})";
                    Speak("นั่งตัวตรง ค้างไว้นะ");
                }
                SetCalibBar(0f);
                _sitStand.ResetSeatedCalibration();
                _seatedHipY = 0f; _seatedTorso = 0f; _seatedSamples = 0;

                float visible = 0f;
                for (float t = 0f; visible < 2f && t < calibSeatedTimeout; t += Time.deltaTime)
                {
                    if (HasLivePose && _gate.Visible)
                    {
                        var kp = poseDetector.LatestKeypoints;
                        _sitStand.CalibrateSeated(kp);
                        _knee.SetBaseline(kp);
                        _seatedHipY += MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
                        _seatedTorso += MotionMath.TorsoLen(kp);
                        _seatedSamples++;
                        visible += Time.deltaTime;
                        SetCalibBar(visible / 2f);
                    }
                    yield return null;
                }
                if (_seatedSamples > 0)
                {
                    _seatedHipY /= _seatedSamples;
                    _seatedTorso /= _seatedSamples;
                }
                success = visible >= 2f;

                if (!success && attempt < calibMaxAttempts)
                {
                    if (calibText != null) calibText.text = $"ลองอีกครั้งนะ ({attempt}/{calibMaxAttempts})";
                    Speak("ลองอีกครั้งนะ กล้องยังไม่เห็นเต็มตัว");
                    yield return new WaitForSeconds(1.5f);
                }
            }
            if (!success) ShowCalibWarning();
        }

        // ---- Calibration: one real stand (hips high 1.5 s). Guided retry — same policy as
        // seated: only after calibMaxAttempts failures does it fall back to
        // EstimateStandingFromSeated, and only then with the warning banner shown. This is the
        // fix for the main field bug: a silent 10 s-timeout fallback produced a standing estimate
        // too crude for Progress01 to ever reach the stand threshold. ----
        IEnumerator CalibStandingRoutine()
        {
            _state = State.CalibStanding;
            if (calibText != null) calibText.text = "ลุกขึ้นยืนช้าๆ";
            Speak("ลุกขึ้นยืนช้าๆนะ");

            if (useKeyboardStub) { yield return new WaitForSeconds(1.5f); yield break; }

            bool success = false;
            for (int attempt = 1; attempt <= calibMaxAttempts && !success; attempt++)
            {
                if (attempt > 1)
                {
                    if (calibText != null) calibText.text = $"ลุกขึ้นยืนช้าๆ ({attempt}/{calibMaxAttempts})";
                    Speak("ลุกขึ้นยืนช้าๆนะ");
                }
                SetCalibBar(0f);
                _sitStand.ResetStandingCalibration();

                float high = 0f;
                for (float t = 0f; high < 1.5f && t < calibStandingTimeout; t += Time.deltaTime)
                {
                    if (HasLivePose && _gate.Visible && _seatedSamples > 0)
                    {
                        var kp = poseDetector.LatestKeypoints;
                        float hipY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
                        if (hipY < _seatedHipY - 0.3f * _seatedTorso) // up = smaller y
                        {
                            _sitStand.CalibrateStanding(kp);
                            high += Time.deltaTime;
                            SetCalibBar(high / 1.5f);
                        }
                    }
                    yield return null;
                }
                success = high >= 1.5f;

                if (!success && attempt < calibMaxAttempts)
                {
                    if (calibText != null) calibText.text = $"ลองอีกครั้งนะ ({attempt}/{calibMaxAttempts})";
                    Speak("ลองอีกครั้งนะ ลุกขึ้นยืนให้เต็มตัวนะ");
                    yield return new WaitForSeconds(1.5f);
                }
            }
            if (!success)
            {
                if (_seatedSamples > 0) _sitStand.EstimateStandingFromSeated();
                ShowCalibWarning();
            }
        }

        void SetCalibBar(float t)
        {
            if (calibProgressBar != null) calibProgressBar.fillAmount = Mathf.Clamp01(t);
        }

        // Persists across every panel from here on — calibration never silently degrades.
        void ShowCalibWarning()
        {
            if (calibWarningBanner != null) calibWarningBanner.SetActive(true);
        }

        IEnumerator CountdownRoutine()
        {
            _state = State.Countdown;
            ShowOnly(countdownPanel);
            for (int n = 3; n >= 1; n--)
            {
                if (countdownText != null)
                {
                    countdownText.text = n.ToString();
                    countdownText.transform.localScale = Vector3.one;
                    SimpleTween.ScalePop(countdownText.transform, this, 1.25f, 0.3f);
                }
                Kinex.Sfx.Play("beep");
                yield return new WaitForSeconds(1f);
            }
            if (countdownText != null)
            {
                countdownText.text = "ไป!";
                countdownText.transform.localScale = Vector3.one;
                SimpleTween.ScalePop(countdownText.transform, this, 1.25f, 0.3f);
            }
            Kinex.Sfx.Play("go");
            yield return new WaitForSeconds(0.6f);
        }

        // ---- One set: spawn items until 15 reps or the item cap is exhausted. ----
        IEnumerator SetPlayRoutine(int setNumber)
        {
            _state = State.SetPlay;
            _currentSet = setNumber;
            _score.BeginSet();
            ShowOnly(hudPanel);
            _hud = GameHud.Ensure(hudPanel);
            if (setText != null) setText.text = $"เซตที่ {setNumber}/{totalSets}";
            if (comboText != null) comboText.text = "";
            UpdateRepText();
            Kinex.Sfx.Play("pose_appear");
            Speak(setNumber == 1 ? "ของดีลอยมาถึงวงแหวน ให้ลุกยืนโหม่งเลย" : $"เซตที่ {setNumber} เริ่ม");

            spawner.BeginSet();
            FruitItem lastZoneItem = null;

            while (_score.RepsInSet < repsPerSet && !(spawner.SetSpawnDone && spawner.ActiveCount == 0))
            {
                // Player out of frame → freeze everything until they're back.
                if (ShouldPause)
                {
                    spawner.SetPaused(true);
                    if (pauseOverlay != null) pauseOverlay.SetActive(true);
                    while (ShouldPause) yield return null;
                    if (pauseOverlay != null) pauseOverlay.SetActive(false);
                    spawner.SetPaused(false);
                }

                var item = spawner.CurrentItemInZone;
                if (item != null)
                {
                    // Already standing as the item arrives, or stood during the window — both count.
                    if ((item != lastZoneItem && IsStandingNow) || JustStood)
                        ResolveStand(item);
                }
                lastZoneItem = item;
                yield return null;
            }

            spawner.EndSet();
            _score.CompleteSet();
            Kinex.Sfx.Play("zone");
            FlashCombo($"จบเซตที่ {setNumber}!");
            yield return new WaitForSeconds(1.5f);
        }

        // Player stood while this item was in the zone.
        void ResolveStand(FruitItem item)
        {
            var outcome = _score.Record(item.IsHealthy, stood: true);
            if (outcome == FruitGameLogic.Outcome.HeadedHealthy)
            {
                if (headerZone != null) headerZone.Pulse();
                if (rootLift != null) rootLift.HeadNudge();
                item.Pop();
                float pitch = 1f + 0.05f * Mathf.Min(_score.Combo - 1, 6);
                Kinex.Sfx.Play("correct", 1f, pitch);
                FlashCombo(_score.Combo >= 2 ? $"คอมโบ x{_score.Combo}!" : "เยี่ยมมาก!");
            }
            else // GentleMiss: stood for junk — warm, no harsh sound, no penalty.
            {
                item.FloatAway();
                FlashCombo("อันนั้นของหวาน ปล่อยผ่านก็ได้นะ");
                Speak("ไม่เป็นไรนะ ของหวานปล่อยให้ลอยผ่านไป");
            }
            UpdateRepText();
        }

        // Zone window expired with the player seated.
        void HandleZoneExpired(FruitItem item)
        {
            if (_state != State.SetPlay || item == null) return;
            var outcome = _score.Record(item.IsHealthy, stood: false);
            if (outcome == FruitGameLogic.Outcome.SmartChoice)
            {
                KinexFx.PopBurst(item.transform.position, new Color(0.6f, 0.9f, 1f), 20);
                Kinex.Sfx.Play("zone");
                FlashCombo("เลือกได้ฉลาด!");
                // The junk item just drifts on out — nothing to clean up.
            }
            else // SoftMiss: a healthy one floated past — encourage, never punish.
            {
                item.FloatAway();
                FlashCombo("ไม่เป็นไร ครั้งหน้าลองใหม่นะ");
                Speak("ไม่เป็นไรนะ ค่อยๆ ทำ");
            }
            UpdateRepText();
        }

        // ---- Rest: 60 s breathing pacer, skippable after 20 s. ----
        IEnumerator RestRoutine()
        {
            _state = State.Rest;
            ShowOnly(restPanel);
            // Recreate the cue each rest: its loop coroutine dies when the panel deactivates.
            if (_breathing != null) Destroy(_breathing.gameObject);
            if (restPanel != null) _breathing = BreathingCue.Create(restPanel.GetComponent<RectTransform>());
            if (skipRestButton != null) skipRestButton.SetActive(false);
            _skipRest = false;
            Speak("พักหายใจลึกๆ ตามวงกลมนะ");

            for (float t = 0f; t < restSeconds && !_skipRest; t += Time.deltaTime)
            {
                if (restCountdownText != null)
                    restCountdownText.text = $"พัก {Mathf.CeilToInt(restSeconds - t)} วินาที";
                if (skipRestButton != null && t >= restSkippableAfter && !skipRestButton.activeSelf)
                    skipRestButton.SetActive(true);
                yield return null;
            }
        }

        // ---- Bonus: 20 alternating seated knee raises inflate a balloon until it pops. ----
        IEnumerator BonusRoutine()
        {
            _state = State.Bonus;
            ShowOnly(bonusPanel);
            Kinex.Sfx.Play("pose_appear");
            Speak("รอบโบนัส ยกเข่าสลับซ้ายขวา เป่าลูกโป่งให้แตก");

            int baseCount = KneeCount;
            var balloon = PropMeshes.Balloon(new Color(0.95f, 0.35f, 0.4f));
            balloon.transform.position = new Vector3(0f, 1.5f, 0.35f);

            int progress = 0;
            int lastProgress = 0;
            float t = 0f;
            while (t < bonusMaxSeconds && progress < bonusKneeTarget)
            {
                t += Time.deltaTime;
                progress = Mathf.Min(KneeCount - baseCount, bonusKneeTarget);
                if (progress > lastProgress)
                {
                    Kinex.Sfx.Play("beep", 0.6f, 1f + 0.02f * progress);
                    lastProgress = progress;
                }
                float target = 1f + 4f * (progress / (float)bonusKneeTarget);
                balloon.transform.localScale = Vector3.Lerp(balloon.transform.localScale,
                                                            Vector3.one * target, Time.deltaTime * 6f);
                if (bonusCountText != null) bonusCountText.text = $"{progress}/{bonusKneeTarget}";
                yield return null;
            }

            bool popped = progress >= bonusKneeTarget;
            if (popped)
            {
                KinexFx.ConfettiBurst(balloon.transform.position + Vector3.up * 0.5f, 80);
                Kinex.Sfx.Play("balloon_pop");
                if (bonusCountText != null) bonusCountText.text = "แตกแล้ว!";
                Speak("เก่งมาก ลูกโป่งแตกแล้ว");
            }
            Destroy(balloon);
            _score.AddBonusReps(progress);
            yield return new WaitForSeconds(popped ? 1.5f : 0.5f);
        }

        void ShowResults()
        {
            _state = State.Results;
            ShowOnly(resultsPanel);
            Kinex.Sfx.Play("results");

            var result = _score.ToResult(Time.time - _startTime);
            if (resultsStatsText != null)
                resultsStatsText.text =
                    $"ลุกยืนโหม่งของดี {result.reps} ครั้ง\n" +
                    $"ความแม่นยำ {Mathf.RoundToInt(result.accuracyPercent)}%\n" +
                    $"เลือกฉลาด {result.smartChoices} ครั้ง\n" +
                    $"โบนัสยกเข่า {result.bonusReps} ครั้ง";
            if (resultsStarImages != null)
                for (int i = 0; i < resultsStarImages.Length; i++)
                    if (resultsStarImages[i] != null)
                        resultsStarImages[i].color = i < result.stars
                            ? new Color(1f, 0.84f, 0.2f)
                            : new Color(1f, 1f, 1f, 0.22f);
            Speak("เก่งมาก จบเกมแล้ว");
            OnSessionComplete?.Invoke(result);
        }

        // ---- Small helpers ----
        void UpdateRepText()
        {
            if (repText != null) repText.text = $"{_score.RepsInSet}/{repsPerSet}";
        }

        void FlashCombo(string message)
        {
            if (comboText == null) return;
            if (_comboFlash != null) StopCoroutine(_comboFlash);
            _comboFlash = StartCoroutine(ComboFlashRoutine(message));
        }

        IEnumerator ComboFlashRoutine(string message)
        {
            comboText.text = message;
            comboText.transform.localScale = Vector3.one; // reset in case a pop was interrupted
            SimpleTween.ScalePop(comboText.transform, this, 1.2f, 0.25f);
            yield return new WaitForSeconds(1.8f);
            comboText.text = "";
            _comboFlash = null;
        }

        void Speak(string line)
        {
            if (voice == null) return;
            Kinex.Music.Duck(2.5f);
            voice.Speak(line);
        }

        void ShowOnly(GameObject panel)
        {
            SetPanels(false, false, false, false, false, false, false);
            if (panel != null) panel.SetActive(true);
        }

        void SetPanels(bool intro, bool calib, bool countdown, bool hud, bool rest, bool bonus, bool results)
        {
            if (introPanel != null) introPanel.SetActive(intro);
            if (calibPanel != null) calibPanel.SetActive(calib);
            if (countdownPanel != null) countdownPanel.SetActive(countdown);
            if (hudPanel != null) hudPanel.SetActive(hud);
            if (restPanel != null) restPanel.SetActive(rest);
            if (bonusPanel != null) bonusPanel.SetActive(bonus);
            if (resultsPanel != null) resultsPanel.SetActive(results);
        }
    }
}
