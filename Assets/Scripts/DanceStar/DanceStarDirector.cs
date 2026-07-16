using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.FX;
using Kinex.Motion;
using Kinex.UI;            // GameHud corner rings (HR mock + live match), shared with all games
using Kinex.MegaDance;     // VoiceCoach, PoseScorer
using Kinex.Trainer;       // TrainerPoseData, TrainerPoseController
using Kinex.TempleHunt;    // TempleLogic.ArmsOutOk (reused for the warm-up T-arms hold)

namespace Kinex.DanceStar
{
    /// <summary>
    /// "เวทีซุปตาร์" (SUPERSTAR STAGE) master director — a Just-Dance-style pictogram-card game.
    /// One coroutine drives the whole session: Intro -> standing calibration -> per-card loop
    /// (show trainer pose, announce, open a match window, poll the detector facade, rate, fire
    /// events/FX) -> a calm chair-verse safety intro mid-song -> Results. Cloned from
    /// Kinex.MirrorGame.MirrorGameDirector's coroutine shape and Kinex.TempleHunt.TempleHuntDirector's
    /// useKeyboardStub facade + calibration pattern.
    ///
    /// Detection is entirely delegated to Assets/Scripts/Motion detectors (see DanceCard's
    /// DanceDetectorKind for the mapping) plus two DanceStar-local pure-logic helpers
    /// (DanceTandemLogic for tandem stand, and TempleLogic.ArmsOutOk reused for the warm-up
    /// T-arms hold) — this class only wires them up and drives UI/FX/scoring.
    ///
    /// No fail state (rehab, senior-first): hearts never end the session, they only cap the
    /// final star rating. See DanceScoring for the pure scoring math.
    /// </summary>
    public class DanceStarDirector : MonoBehaviour
    {
        public enum State { Idle, Intro, Calib, ChairSafety, CardAnnounce, CardWindow, Results }

        [Header("Motion source")]
        [Tooltip("Play with the keyboard: K/J = left/right pose held, T = tiptoe, B = both-side hold, " +
                 "S = chair stand/sit toggle, arrows = side-step lane, V = tandem hold. " +
                 "Forced OFF on device regardless of this value (Awake).")]
        public bool useKeyboardStub = true;
        [Tooltip("Live keypoint source. Only used when useKeyboardStub is false.")]
        public MediaPipePoseDetector poseDetector;

        [Header("Scene refs")]
        public Camera worldCamera;
        [Tooltip("Offline Piper voice for coach lines. Safe to leave unassigned (silent).")]
        public VoiceCoach voice;
        [Tooltip("The trainer/coach-star rig on its podium (NewTrainerAnimated.fbx clone).")]
        public TrainerPoseController trainer;
        [Tooltip("Optional: if set, InitRuntime's the trainer with THIS data at startup — lets " +
                 "this field override whatever TrainerPoseController already has wired in its own " +
                 "Inspector. Leave empty if the trainer prefab is already wired to DancePoseData.")]
        public TrainerPoseData dancePoseData;

        [Header("Timing")]
        public float introSeconds = 5f;
        [Tooltip("Beat between the pose appearing and the match window opening (senior-readable).")]
        public float announceSeconds = 1.3f;
        public float cardCelebrateSeconds = 0.7f;
        public float missPauseSeconds = 0.6f;
        public float calibStandingSeconds = 1.5f;
        public float calibSeatedSeconds = 1.5f;
        public float calibTimeoutSeconds = 8f;
        const float TandemHoldTargetSeconds = 8f;
        // Max time a single card waits (paused) for the camera to see the whole body before it gives
        // up and lets the window run out — stops a misfiring framing gate from soft-locking a card.
        const float FramingPauseCap = 20f;

        [Header("UI - Intro")]
        public GameObject introPanel;
        public TMP_Text introTitleText;
        public TMP_Text introSubtitleText;

        [Header("UI - Calibration")]
        public GameObject calibPanel;
        public TMP_Text calibText;
        public Image calibProgressFill;

        [Header("UI - Chair Safety (mid-song bridge)")]
        public GameObject chairSafetyPanel;
        public TMP_Text chairSafetyText;

        [Header("UI - HUD")]
        public GameObject hudPanel;
        public TMP_Text scoreText;
        public TMP_Text streakText;
        public Image streakFlameIcon;
        public TMP_Text cardCountText;
        public TMP_Text poseNameText;
        public TMP_Text sectionNameText;
        public Image songProgressFill;
        public Image countdownRingFill;
        public Image[] heartImages;
        public TMP_Text ratingPopupText;
        [Tooltip("Optional stillness/wobble meter for tandem-stand cards.")]
        public Image tandemWobbleFill;

        [Header("UI - Reference & feedback (2D redesign)")]
        [Tooltip("Top-left reference figure — the pose to copy (Resources/DanceStarRef/<poseName>).")]
        public Image referenceImage;
        [Tooltip("Live coaching line shown near the camera feed during a card window.")]
        public TMP_Text feedbackText;
        [Tooltip("Top-right meter that fills while the current pose is held / reps accumulate.")]
        public Image matchMeterFill;
        [Tooltip("Dim overlay shown when the camera can't see the whole body; pauses the card timer.")]
        public GameObject framingPanel;
        public TMP_Text framingPromptText;

        [Header("UI - Results")]
        public GameObject resultsPanel;
        public Image[] resultsStarImages;
        public TMP_Text resultsStatsText;

        public event Action<DanceScoring.Rating> OnCardResult;
        public event Action<int> OnStreakChanged;
        public event Action<int> OnHeartsChanged;
        public event Action<DanceSection> OnSectionChanged;
        public event Action<DanceStarResult> OnSessionComplete;
        public event Action OnExitRequested;

        static readonly Color StarOn = new Color(1f, 0.84f, 0.2f);
        static readonly Color StarOff = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color HeartOn = new Color(1f, 0.35f, 0.45f);
        static readonly Color HeartOff = new Color(1f, 1f, 1f, 0.2f);

        State _state = State.Idle;

        // ---- Detector layer (Assets/Scripts/Motion) ----
        readonly KneeRaiseDetector _knee = new KneeRaiseDetector();
        readonly LegAbductionDetector _abduction = new LegAbductionDetector();
        readonly HipExtensionDetector _hipExt = new HipExtensionDetector();
        readonly TiptoeDetector _tiptoe = new TiptoeDetector();
        readonly SingleLegStanceDetector _sls = new SingleLegStanceDetector();
        readonly LaneDetector _lane = new LaneDetector();
        readonly SitStandDetector _sitStand = new SitStandDetector();
        readonly PoseGate _gate = new PoseGate();
        readonly FullBodyGate _fullBody = new FullBodyGate();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();

        // DanceStar-local checks (not in the frozen Motion layer — same precedent as TempleLogic).
        float _standingHipY0;
        bool _tandemLastOk = true;
        float _tandemHeldSeconds;
        bool _armsOutLastOk = true;

        int _totalScore, _streak, _maxStreak, _hearts, _cardsCompleted;
        GameHud _gameHud;
        bool _heartsHitZero;
        float _startTime;
        DanceScoring.Rating _lastCardRating;

        public State CurrentState => _state;
        bool HasLivePose => poseDetector != null && poseDetector.HasPose;

        void Awake()
        {
            // A tablet has no keyboard: stub mode there would soft-lock every card.
            if (!Application.isEditor && poseDetector != null) useKeyboardStub = false;
        }

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            ShowOnly(introPanel);
            UpdateHeartsUI();
            StartCoroutine(AutoStart());
        }

        IEnumerator AutoStart()
        {
            yield return null; // let every other Awake/Start (detector, stage, UI) finish first
            try
            {
                if (dancePoseData != null && trainer != null)
                    trainer.InitRuntime(dancePoseData, trainer.transform);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DanceStarDirector] trainer InitRuntime failed — continuing without demo poses: {e}");
            }
            StartCoroutine(GameFlow());
        }

        void Update() => TickMotion(Time.deltaTime);

        // ---- Buttons (wired by the DanceStar UI builder) ----
        public void PlayAgain() { StopAllCoroutines(); StartCoroutine(GameFlow()); }
        public void ExitToHome() => OnExitRequested?.Invoke();

        // =====================================================================
        IEnumerator GameFlow()
        {
            _startTime = Time.time;
            _totalScore = 0; _streak = 0; _maxStreak = 0;
            _hearts = DanceScoring.MaxHearts; _heartsHitZero = false; _cardsCompleted = 0;
            UpdateHeartsUI();

            yield return IntroRoutine();
            yield return StandingCalibRoutine();

            var cards = DanceSetlist.BuildSetlist();
            ShowOnly(hudPanel);
            _gameHud = GameHud.Ensure(hudPanel); // bottom-corner HR + match rings, like every game
            DanceSection? lastSection = null;

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (lastSection != card.section)
                {
                    lastSection = card.section;
                    OnSectionChanged?.Invoke(card.section);
                    if (sectionNameText != null) sectionNameText.text = SectionLabel(card.section);
                    if (card.section == DanceSection.ChairVerse)
                        yield return ChairSafetyIntroRoutine();
                }
                yield return RunCard(card, i, cards.Count);
            }

            yield return ResultsRoutine(cards.Count);
        }

        IEnumerator IntroRoutine()
        {
            _state = State.Intro;
            ShowOnly(introPanel);
            Music.Play("dance_theme", 0.35f); // CC0 funky disco (Assets/Resources/Music/dance_theme.ogg)
            if (introTitleText != null) introTitleText.text = "เวทีซุปตาร์";
            if (introSubtitleText != null)
                introSubtitleText.text = "ทำท่าตามโค้ชบนเวทีให้ตรงจังหวะ\nยิ่งแม่น ยิ่งไว ยิ่งได้คะแนนเยอะ\nค่อยๆ ทำ ไม่ต้องรีบ";
            Speak("ยินดีต้อนรับสู่เวทีซุปตาร์ ทำท่าตามโค้ชให้ตรงจังหวะนะครับ ค่อยๆ ทำ ไม่ต้องรีบ");
            yield return new WaitForSeconds(introSeconds);
        }

        // Captures a standing baseline for every detector that needs one. Best-effort like
        // TempleHuntDirector's calibration passes — proceeds with whatever was captured rather
        // than hard-blocking (senior-first: never strand the player on a calibration screen).
        IEnumerator StandingCalibRoutine()
        {
            _state = State.Calib;
            ShowOnly(calibPanel);
            if (calibText != null) calibText.text = "ยืนตรง ให้กล้องเห็นเต็มตัว\nยืนนิ่งๆ สักครู่นะครับ";
            Speak("ยืนนิ่งๆ ให้กล้องเห็นเต็มตัวก่อนเริ่มนะครับ");

            if (useKeyboardStub) { yield return new WaitForSeconds(1f); yield break; }

            float captured = 0f;
            for (float t = 0f; captured < calibStandingSeconds && t < calibTimeoutSeconds; t += Time.deltaTime)
            {
                if (HasLivePose && _gate.Visible)
                {
                    var kp = poseDetector.LatestKeypoints;
                    _knee.SetBaseline(kp);
                    _abduction.SetBaseline(kp);
                    _tiptoe.CalibrateStanding(kp);
                    _sls.CalibrateStanding(kp);
                    _lane.CalibrateCenter(kp);
                    _sitStand.CalibrateStanding(kp);
                    _standingHipY0 = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
                    var lm = poseDetector.Landmarks33;
                    if (lm != null) _hipExt.SetBaseline(lm);

                    captured += Time.deltaTime;
                    if (calibProgressFill != null) calibProgressFill.fillAmount = captured / calibStandingSeconds;
                }
                yield return null;
            }
        }

        // Mid-song bridge before the chair verse: safety copy (RestStop pattern) + a brief seated
        // calibration so SitStandDetector has both baselines before the ChairRep card runs.
        IEnumerator ChairSafetyIntroRoutine()
        {
            _state = State.ChairSafety;
            ShowOnly(chairSafetyPanel);
            Kinex.Sfx.Play("checkpoint");
            if (chairSafetyText != null)
                chairSafetyText.text = "เอาเก้าอี้มาวางไว้ด้านหลังคุณ\nนั่งลงช้าๆ เตรียมพร้อมสำหรับท่านั่งนะครับ";
            Speak("ตอนนี้ให้เอาเก้าอี้มาวางไว้ด้านหลังคุณ นั่งลงช้าๆ นะครับ");
            yield return new WaitForSeconds(6f);

            ShowOnly(calibPanel);
            if (calibText != null) calibText.text = "นั่งนิ่งๆ บนเก้าอี้สักครู่";
            if (!useKeyboardStub)
            {
                float captured = 0f;
                for (float t = 0f; captured < calibSeatedSeconds && t < calibTimeoutSeconds; t += Time.deltaTime)
                {
                    if (HasLivePose && _gate.Visible)
                    {
                        _sitStand.CalibrateSeated(poseDetector.LatestKeypoints);
                        captured += Time.deltaTime;
                        if (calibProgressFill != null) calibProgressFill.fillAmount = captured / calibSeatedSeconds;
                    }
                    yield return null;
                }
                if (!_sitStand.IsCalibrated) _sitStand.EstimateSeatedFromStanding();
            }
            else
            {
                yield return new WaitForSeconds(1f);
            }
            ShowOnly(hudPanel);
        }

        // ---- One card: show pose, announce, open the match window, rate, celebrate/console. ----
        IEnumerator RunCard(DanceCard card, int index, int total)
        {
            _state = State.CardAnnounce;
            ShowReference(card);
            // The runtime scene has no trainer rig in the 2D redesign; keep the demo hook for any
            // scene that still wires one, but never warn when there simply isn't a trainer.
            if (trainer != null)
            {
                int poseIdx = FindPoseIndex(card.poseAssetName);
                if (poseIdx >= 0) trainer.ShowPose(poseIdx);
            }

            if (cardCountText != null) cardCountText.text = $"ท่าที่ {index + 1}/{total}";
            if (_gameHud != null) _gameHud.SetSubLabel($"{index + 1}/{total}");
            if (poseNameText != null) poseNameText.text = card.displayNameThai;
            if (songProgressFill != null) songProgressFill.fillAmount = index / (float)Mathf.Max(1, total);
            if (matchMeterFill != null) matchMeterFill.fillAmount = 0f;
            if (ratingPopupText != null) ratingPopupText.gameObject.SetActive(false);
            SetFeedback("ทำท่าตามรูป");
            Kinex.Sfx.Play("whoosh", 0.6f);
            Speak(card.displayNameThai);
            yield return new WaitForSeconds(announceSeconds);

            _state = State.CardWindow;
            Kinex.Sfx.Play("go", 0.5f);

            if (card.cardType == DanceCardType.ChairRep) yield return RunChairRepCard(card);
            else yield return RunHoldOrBeatCard(card);

            if (framingPanel != null) framingPanel.SetActive(false);
            SetFeedback("");
            ApplyRating(_lastCardRating);
            yield return new WaitForSeconds(_lastCardRating == DanceScoring.Rating.Miss ? missPauseSeconds : cardCelebrateSeconds);
        }

        IEnumerator RunHoldOrBeatCard(DanceCard card)
        {
            float requiredHold = card.cardType == DanceCardType.Beat ? 0.15f : 0.6f;
            float t = 0f, matchStartTime = -1f, holdAccum = 0f, paused = 0f;
            bool matched = false;

            while (t < card.windowSeconds)
            {
                // Pause the window while the camera can't see the whole body — don't auto-miss —
                // but give up after FramingPauseCap so a misfiring gate can't hang the card.
                if (UpdateFramingPanel())
                {
                    paused += Time.deltaTime;
                    if (paused < FramingPauseCap) { yield return null; continue; }
                }

                bool cond = EvaluateCondition(card);
                if (cond)
                {
                    if (matchStartTime < 0f) matchStartTime = t;
                    holdAccum += Time.deltaTime;
                }
                SetFeedback(cond ? "ค้างไว้!" : "ทำท่าตามรูป");
                if (matchMeterFill != null) matchMeterFill.fillAmount = Mathf.Clamp01(holdAccum / Mathf.Max(requiredHold, 0.01f));
                if (countdownRingFill != null) countdownRingFill.fillAmount = Mathf.Clamp01(1f - t / card.windowSeconds);
                if (holdAccum >= requiredHold) { matched = true; break; }
                t += Time.deltaTime;
                yield return null;
            }

            float stability01 = Mathf.Clamp01(holdAccum / Mathf.Max(requiredHold, 0.01f));
            float timeToMatch = matched ? Mathf.Max(0f, matchStartTime) : card.windowSeconds;
            _lastCardRating = DanceScoring.RateCard(matched, timeToMatch, card.windowSeconds, stability01);
            if (countdownRingFill != null) countdownRingFill.fillAmount = matched ? 1f : 0f;
        }

        IEnumerator RunChairRepCard(DanceCard card)
        {
            int baseline = RawRepCount(card.detector);
            float t = 0f, paused = 0f;
            int reps = 0;

            while (t < card.windowSeconds && reps < card.targetReps)
            {
                if (UpdateFramingPanel())
                {
                    paused += Time.deltaTime;
                    if (paused < FramingPauseCap) { yield return null; continue; }
                }

                reps = Mathf.Max(0, RawRepCount(card.detector) - baseline);
                int remain = Mathf.Max(0, card.targetReps - reps);
                SetFeedback(remain > 0 ? $"อีก {remain} ครั้ง" : "เยี่ยมมาก!");
                float rep01 = Mathf.Clamp01(reps / (float)Mathf.Max(1, card.targetReps));
                if (countdownRingFill != null) countdownRingFill.fillAmount = rep01;
                if (matchMeterFill != null) matchMeterFill.fillAmount = rep01;
                t += Time.deltaTime;
                yield return null;
            }

            _lastCardRating = DanceScoring.RateChairRep(reps, card.targetReps, t, card.windowSeconds);
        }

        int RawRepCount(DanceDetectorKind kind) => kind == DanceDetectorKind.ChairStand
            ? (useKeyboardStub ? _stub.StandCount : _sitStand.StandCount)
            : (useKeyboardStub ? _stub.AlternatingCount : _knee.AlternatingCount);

        void ApplyRating(DanceScoring.Rating rating)
        {
            int streakBefore = _streak;
            _totalScore += DanceScoring.CardScore(rating, streakBefore);

            if (rating == DanceScoring.Rating.Miss) _streak = 0;
            else { _streak++; if (_streak > _maxStreak) _maxStreak = _streak; }
            OnStreakChanged?.Invoke(_streak);

            _hearts = DanceScoring.ApplyRatingToHearts(_hearts, rating);
            if (_hearts <= 0) _heartsHitZero = true;
            OnHeartsChanged?.Invoke(_hearts);
            UpdateHeartsUI();

            if (rating != DanceScoring.Rating.Miss) _cardsCompleted++;

            if (scoreText != null) scoreText.text = _totalScore.ToString();
            if (streakText != null) streakText.text = _streak > 0 ? $"x{_streak}" : "";
            if (ratingPopupText != null)
            {
                ratingPopupText.gameObject.SetActive(true);
                ratingPopupText.text = DanceScoring.ThaiLabel(rating);
            }

            switch (rating)
            {
                case DanceScoring.Rating.Perfect:
                    Kinex.Sfx.Play("star");
                    Kinex.Sfx.Play("coin", 0.7f);
                    KinexFx.PopBurst(Vector3.up * 1.4f, new Color(1f, 0.85f, 0.3f), 24);
                    break;
                case DanceScoring.Rating.Good:
                    Kinex.Sfx.Play("correct");
                    break;
                case DanceScoring.Rating.Ok:
                    Kinex.Sfx.Play("pop", 0.6f);
                    break;
                default:
                    Kinex.Sfx.Play("beep", 0.4f);
                    break;
            }
            if (_gameHud != null) _gameHud.SetScore(DanceScoring.ScoreOf(rating) / 100f);
            OnCardResult?.Invoke(rating);
        }

        IEnumerator ResultsRoutine(int cardCount)
        {
            _state = State.Results;
            ShowOnly(resultsPanel);
            Music.Stop();
            Kinex.Sfx.Play("fanfare");
            if (_maxStreak >= 10) KinexFx.ConfettiBurst(Vector3.up * 1.6f, 80);

            int stars = DanceScoring.Stars(_totalScore, cardCount, _heartsHitZero);
            int coins = DanceScoring.Coins(_totalScore, stars);
            int accuracy = DanceScoring.AccuracyPct(_cardsCompleted, cardCount);

            var result = new DanceStarResult
            {
                cardsCompleted = _cardsCompleted,
                cardCount = cardCount,
                totalScore = _totalScore,
                maxStreak = _maxStreak,
                accuracyPct = accuracy,
                stars = stars,
                coins = coins,
                durationSeconds = Time.time - _startTime,
            };

            if (resultsStarImages != null)
                for (int i = 0; i < resultsStarImages.Length; i++)
                    if (resultsStarImages[i] != null)
                        resultsStarImages[i].color = i < stars ? StarOn : StarOff;

            if (resultsStatsText != null)
                resultsStatsText.text =
                    $"ทำได้ {result.cardsCompleted}/{result.cardCount} ท่า\n" +
                    $"คะแนนรวม {result.totalScore}  •  คอมโบสูงสุด x{result.maxStreak}\n" +
                    $"ความแม่นยำ {result.accuracyPct}%  •  เหรียญ {result.coins}";

            Speak(result.stars >= 3 ? "สุดยอดมากครับ คุณคือซุปตาร์ตัวจริง" : "เก่งมากครับ วันนี้ทำได้ดีมากเลย");
            yield return null; // keep this an iterator block
            OnSessionComplete?.Invoke(result);
        }

        // ---- Detector facade -------------------------------------------------------------

        void TickMotion(float dt)
        {
            if (useKeyboardStub) { _stub.Tick(dt); return; }

            bool has = HasLivePose;
            _gate.Tick(has, has ? poseDetector.LatestConfidence : null, dt);
            _fullBody.Tick(has, has ? poseDetector.Landmarks33 : null, dt);
            if (!has || !_gate.Visible) return;

            var kp = poseDetector.LatestKeypoints;
            var conf = poseDetector.LatestConfidence;
            _knee.Tick(kp, conf, dt);
            _abduction.Tick(kp, conf, dt);
            _tiptoe.Tick(kp, conf, dt);
            _sls.Tick(kp, conf, dt);
            _lane.Tick(kp, conf, dt);
            _sitStand.Tick(kp, conf, dt);
            var lm = poseDetector.Landmarks33;
            if (lm != null) _hipExt.Tick(lm, dt);

            bool? tandemOk = DanceTandemLogic.IsTandemPose(kp, conf, _standingHipY0);
            if (tandemOk.HasValue) _tandemLastOk = tandemOk.Value;
            _tandemHeldSeconds = DanceTandemLogic.TickHold(_tandemHeldSeconds, _tandemLastOk, dt, TandemHoldTargetSeconds);
            if (tandemWobbleFill != null) tandemWobbleFill.fillAmount = Mathf.Clamp01(_tandemHeldSeconds / TandemHoldTargetSeconds);

            bool? armsOk = TempleLogic.ArmsOutOk(kp, conf);
            if (armsOk.HasValue) _armsOutLastOk = armsOk.Value;
        }

        bool EvaluateCondition(DanceCard card)
        {
            switch (card.detector)
            {
                case DanceDetectorKind.ArmsOutHold:
                    return useKeyboardStub ? _stub.IsHolding : _armsOutLastOk;
                case DanceDetectorKind.KneeRaise:
                    return useKeyboardStub ? StubSideMatch(card.side) : SideBool(card.side, _knee.LeftUp, _knee.RightUp);
                case DanceDetectorKind.HipAbduction:
                    return useKeyboardStub ? StubSideMatch(card.side) : SideBool(card.side, _abduction.LeftOut, _abduction.RightOut);
                case DanceDetectorKind.HipExtension:
                    return useKeyboardStub ? StubSideMatch(card.side) : SideBool(card.side, _hipExt.LeftBack, _hipExt.RightBack);
                case DanceDetectorKind.Tiptoe:
                case DanceDetectorKind.HeelStand:
                    return useKeyboardStub ? _stub.IsRaised : _tiptoe.IsRaised;
                case DanceDetectorKind.SingleLeg:
                    return SingleLegOk(card);
                case DanceDetectorKind.TandemStand:
                    return useKeyboardStub ? _stub.IsTandemHolding : _tandemLastOk;
                case DanceDetectorKind.SideStep:
                    return useKeyboardStub
                        ? (card.side == DanceSide.Left ? _stub.Lane == -1 : _stub.Lane == 1)
                        : (card.side == DanceSide.Left ? _lane.Lane == -1 : _lane.Lane == 1);
                default:
                    return false;
            }
        }

        bool SingleLegOk(DanceCard card)
        {
            if (useKeyboardStub) return StubSideMatch(card.side) && ArmVariantOk(card.armVariant);

            bool holding = _sls.IsHolding;
            bool correctSide = card.side == DanceSide.Left ? _sls.StanceLegIsLeft
                              : card.side == DanceSide.Right ? !_sls.StanceLegIsLeft
                              : true;
            return holding && correctSide && ArmVariantOk(card.armVariant);
        }

        bool ArmVariantOk(DanceArmVariant variant)
        {
            if (variant == DanceArmVariant.None) return true;
            // Keyboard testing can't shape arms — treat as satisfied (same fallback philosophy
            // as BalanceQuest's BridgeBeatRunner StubArmMatch).
            if (useKeyboardStub) return true;
            if (!HasLivePose) return false;

            float[] target = variant switch
            {
                DanceArmVariant.TArms => ArmPoseSignatures.ArmsOut,
                DanceArmVariant.Stacked => ArmPoseSignatures.HandsStacked,
                DanceArmVariant.Crossed => ArmPoseSignatures.ArmsCrossed,
                _ => null,
            };
            if (target == null) return true;

            float match = PoseScorer.Score(poseDetector.LatestKeypoints, poseDetector.LatestConfidence,
                                            target, 0.3f, 45f * Mathf.Deg2Rad);
            return match >= 0.55f;
        }

        bool StubSideMatch(DanceSide side) => side switch
        {
            DanceSide.Left => _stub.LeftUp,
            DanceSide.Right => _stub.RightUp,
            DanceSide.Both => _stub.LeftUp || _stub.RightUp || _stub.IsHolding,
            _ => _stub.LeftUp || _stub.RightUp,
        };

        static bool SideBool(DanceSide side, bool left, bool right) => side switch
        {
            DanceSide.Left => left,
            DanceSide.Right => right,
            DanceSide.Both => left || right,
            _ => left || right,
        };

        // ---- Small helpers ----------------------------------------------------------------

        int FindPoseIndex(string poseAssetName)
        {
            if (trainer == null || string.IsNullOrEmpty(poseAssetName)) return -1;
            int count = trainer.PoseCount;
            for (int i = 0; i < count; i++)
                if (trainer.PoseName(i) == poseAssetName) return i;
            return -1;
        }

        // Loads the top-left reference figure for a card (Resources/DanceStarRef/<poseName>).
        void ShowReference(DanceCard card)
        {
            if (referenceImage == null) return;
            var sprite = Resources.Load<Sprite>($"DanceStarRef/{card.poseAssetName}");
            referenceImage.sprite = sprite;
            referenceImage.enabled = sprite != null;
            if (sprite == null)
                Debug.LogWarning($"[DanceStarDirector] reference figure 'DanceStarRef/{card.poseAssetName}' " +
                                 "not found — run Kinex/Capture Dance Star Reference Figures.");
        }

        void SetFeedback(string line)
        {
            if (feedbackText != null) feedbackText.text = line;
        }

        // Toggles the framing overlay from the full-body gate. Returns TRUE when the card timer should
        // PAUSE (body not fully visible) so a card can't auto-miss while the camera has lost the user.
        // A short invalid grace (0.6s) avoids flicker on a single dropped frame.
        bool UpdateFramingPanel()
        {
            bool blocked = !useKeyboardStub && poseDetector != null && _fullBody.InvalidSeconds > 0.6f;
            if (framingPanel != null) framingPanel.SetActive(blocked);
            if (blocked && framingPromptText != null) framingPromptText.text = FramingPrompt(_fullBody.Why);
            return blocked;
        }

        static string FramingPrompt(FullBodyGate.Reason why) => why switch
        {
            FullBodyGate.Reason.NotFound => "ยืนให้กล้องเห็นตัวคุณ",
            FullBodyGate.Reason.FeetCut => "ถอยหลังอีกนิด ให้เห็นถึงเท้า",
            FullBodyGate.Reason.HeadCut => "ก้มกล้องลงให้เห็นศีรษะ",
            FullBodyGate.Reason.TooClose => "ถอยหลังอีกนิดนะครับ",
            FullBodyGate.Reason.TooFar => "เข้าใกล้กล้องอีกนิด",
            FullBodyGate.Reason.OffCenter => "ขยับมาตรงกลางจอ",
            FullBodyGate.Reason.PartlyHidden => "ยืนให้กล้องเห็นเต็มตัว",
            _ => "ยืนให้กล้องเห็นเต็มตัว",
        };

        void UpdateHeartsUI()
        {
            if (heartImages == null) return;
            for (int i = 0; i < heartImages.Length; i++)
                if (heartImages[i] != null)
                    heartImages[i].color = i < _hearts ? HeartOn : HeartOff;
        }

        static string SectionLabel(DanceSection s) => s switch
        {
            DanceSection.Warmup => "อบอุ่นร่างกาย",
            DanceSection.Verse1 => "ท่อนที่ 1",
            DanceSection.Chorus1 => "ท่อนฮุค",
            DanceSection.ChairVerse => "ท่อนเก้าอี้",
            DanceSection.Verse2 => "ท่อนที่ 2",
            DanceSection.Finale => "ไฟนอล",
            _ => "",
        };

        void Speak(string line)
        {
            if (voice == null) return;
            Music.Duck(2.5f);
            voice.Speak(line);
        }

        void ShowOnly(GameObject panel)
        {
            GameObject[] all = { introPanel, calibPanel, chairSafetyPanel, hudPanel, resultsPanel };
            foreach (var p in all)
                if (p != null) p.SetActive(p == panel);
        }
    }
}
