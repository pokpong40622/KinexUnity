using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Kinex.FX;
using Kinex.Motion;
using Kinex.UI;
using Kinex.MegaDance;   // VoiceCoach
using Kinex.Trainer;     // TrainerPoseData

namespace Kinex.TempleHunt
{
    /// <summary>
    /// "ล่าสมบัติวิหารโบราณ" (Ancient Temple Treasure Hunt) — a jungle-explorer adventure where
    /// four temple chambers each map to one rehab movement:
    ///   1 Pumping Station  — seated alternating leg lifts drain the flooding room (KneeRaiseDetector)
    ///   2 Heavy Stone Gate — chair stands (arms crossed) push three gates open (SitStandDetector)
    ///   3 Chasm Bridge     — single-leg balance, arms out like wings, 10 s per side (SingleLegStanceDetector + ArmsOutOk)
    ///   4 Sacred Vault     — single-leg balance, hands on chest, 10 s per side (… + HandsOnChestOk)
    ///
    /// Senior-first: no fail state, no timers against the player, decay-not-reset holds, a parrot
    /// guide that says everything on screen AND out loud, and an amber safety pause whenever the
    /// camera loses the player. Flow (one coroutine, FruitGameManager pattern):
    ///   Intro → CalibSeated → C1 → C2 → StandTransition (standing calib) → C3 → C4 → Victory.
    ///
    /// Motion source is swappable: useKeyboardStub (default TRUE) plays the whole game with
    /// K/J = knees, S = stand/sit, B = balance hold; flip it off for the live MediaPipe detector.
    /// </summary>
    public class TempleHuntDirector : MonoBehaviour
    {
        public enum State { Idle, Intro, Calib, Announce, Pump, Gate, StandTransition, Balance, ChamberClear, Victory }

        [Header("Motion source")]
        [Tooltip("Play with the keyboard: K/J = left/right knee, S = stand/sit, B = balance hold. " +
                 "Bypasses the camera entirely so the whole game runs in the editor.")]
        public bool useKeyboardStub = true;
        [Tooltip("Live keypoint source. Only used when useKeyboardStub is false.")]
        public MediaPipePoseDetector poseDetector;

        [Header("Scene refs")]
        public TempleStage stage;
        [Tooltip("Offline Piper voice for the parrot's lines. Safe to leave unassigned (silent).")]
        public VoiceCoach voice;
        [Tooltip("RehabPoseData — the ghost demos poses 6 (knee up), 23 (sit-stand), 15 (single-leg).")]
        public TrainerPoseData rehabPoseData;
        [Tooltip("MUST be NewTrainerAnimated.fbx — RehabPoseData poses only reproduce on that rig.")]
        public GameObject trainerRigPrefab;

        [Header("Dose (physio-tunable)")]
        public int pumpTarget = 20;
        public int repsPerGate = 5;
        public int gateCount = 3;
        public float gateRestSeconds = 20f;
        public float holdTargetSeconds = 10f;
        [Tooltip("Escape hatch: when false, chambers 3/4 only require the single-leg hold, not the arm pose.")]
        public bool requireArmPose = true;

        [Header("Timing")]
        public float introSeconds = 6f;
        public float announceSeconds = 6f;
        public float clearCelebrateSeconds = 3f;
        public float calibHoldSeconds = 2f;
        public float standCalibSeconds = 2f;
        public float calibTimeoutSeconds = 12f;
        public int calibMaxAttempts = 3;

        [Header("UI — panels")]
        public GameObject introPanel;
        public GameObject calibPanel;
        public GameObject hudPanel;
        public GameObject pauseOverlay;
        public GameObject victoryPanel;

        [Header("UI — texts & widgets")]
        public TMP_Text calibText;
        public Image calibProgressFill;
        public TMP_Text chamberTitleText;
        public TMP_Text chamberCountText;
        public TMP_Text instructionText;
        public TMP_Text restText;
        public GameObject skipRestButton;
        public GameObject balanceMeterPanel;
        public Image balanceMeterFill;
        public GameObject gatePipsPanel;
        public Image[] gatePips;
        public TMP_Text pauseText;
        public TMP_Text victoryStatsText;
        public ParrotGuide parrot;

        /// <summary>Fired once with the final result — TempleResultBridge relays it to Flutter.</summary>
        public event Action<TempleHuntResult> OnSessionComplete;
        /// <summary>Fired by the exit button — the bridge sends {"type":"exit"}.</summary>
        public event Action OnExitRequested;

        // ---- demo pose indices in RehabPoseData (see MirrorGameDirector's decoded ladder) ----
        const int DemoPoseKneeUp = 6;    // ย่ำเท้าอยู่กับที่
        const int DemoPoseSitStand = 23; // ลุก-นั่ง
        const int DemoPoseBalance = 15;  // ยกขาทรงตัว

        State _state = State.Idle;
        readonly KneeRaiseDetector _knee = new KneeRaiseDetector();
        readonly SitStandDetector _sitStand = new SitStandDetector();
        readonly SingleLegStanceDetector _sls = new SingleLegStanceDetector();
        readonly PoseGate _gate = new PoseGate();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();

        readonly TempleHuntResult _result = new TempleHuntResult();
        GameHud _hud;
        RingGauge _ring;
        PoseGhost _ghost;
        float _startTime;
        bool _introSkipped, _skipRest;
        bool _standCounted;             // current chair-stand already committed (GateLift live component)
        bool _armOkLast = true;
        float _armGraceLeft;
        float _lastCoachAt = -99f;

        // ---- Motion facade: one place that switches between stub and live detectors. ----
        bool HasLivePose => poseDetector != null && poseDetector.HasPose;
        bool ShouldPause => !useKeyboardStub && _gate.ShouldPauseGame;
        int KneeCount => useKeyboardStub ? _stub.AlternatingCount : _knee.AlternatingCount;
        bool JustKneeCounted => useKeyboardStub ? _stub.JustCountedAlternating : _knee.JustCountedAlternating;
        bool LeftKneeUp => useKeyboardStub ? _stub.LeftUp : _knee.LeftUp;
        bool RightKneeUp => useKeyboardStub ? _stub.RightUp : _knee.RightUp;
        int StandCount => useKeyboardStub ? _stub.StandCount : _sitStand.StandCount;
        bool JustStood => useKeyboardStub ? _stub.JustStood : _sitStand.JustStood;
        bool JustSat => useKeyboardStub ? _stub.JustSat : _sitStand.JustSat;
        float Progress01 => useKeyboardStub ? _stub.Progress01 : _sitStand.Progress01;
        bool SlsHolding => useKeyboardStub ? _stub.IsHolding : _sls.IsHolding;
        float Wobble01 => useKeyboardStub ? _stub.Wobble01 : _sls.Wobble01;
        bool StanceLegIsLeft => useKeyboardStub || _sls.StanceLegIsLeft;

        void TickMotion(float dt)
        {
            if (useKeyboardStub) { _stub.Tick(dt); return; }
            bool has = HasLivePose;
            _gate.Tick(has, has ? poseDetector.LatestConfidence : null, dt);
            if (has && _gate.Visible)
            {
                var kp = poseDetector.LatestKeypoints;
                var conf = poseDetector.LatestConfidence;
                _knee.Tick(kp, conf, dt);
                _sitStand.Tick(kp, conf, dt);
                _sls.Tick(kp, conf, dt);
            }
        }

        /// <summary>
        /// Arm-pose gate for chambers 3/4 with a 1 s sticky grace: while the wrists aren't
        /// confidently tracked the last known answer stands, so a tracking flicker never drains
        /// an honest hold. Only after a full second of no data does it fail closed.
        /// </summary>
        bool ArmOk(bool handsOnChest, float dt)
        {
            if (useKeyboardStub || !requireArmPose) return true;
            if (!HasLivePose) return false;
            bool? ok = handsOnChest
                ? TempleLogic.HandsOnChestOk(poseDetector.LatestKeypoints, poseDetector.LatestConfidence)
                : TempleLogic.ArmsOutOk(poseDetector.LatestKeypoints, poseDetector.LatestConfidence);
            if (ok.HasValue)
            {
                _armOkLast = ok.Value;
                _armGraceLeft = 1f;
                return _armOkLast;
            }
            _armGraceLeft -= dt;
            return _armGraceLeft > 0f && _armOkLast;
        }

        void Awake()
        {
            // A tablet has no keyboard: stub mode there would soft-lock the first chamber.
            // The baked scene ships with the stub off, but force it off on device regardless.
            if (!Application.isEditor && poseDetector != null) useKeyboardStub = false;
        }

        void Start()
        {
            SetPanels(intro: false, calib: false, hud: false, victory: false);
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            if (balanceMeterPanel != null) balanceMeterPanel.SetActive(false);
            if (gatePipsPanel != null) gatePipsPanel.SetActive(false);
            if (restText != null) restText.gameObject.SetActive(false);
            if (skipRestButton != null) skipRestButton.SetActive(false);
            StartCoroutine(AutoStart());
        }

        // One frame so every other Awake/Start (detector, stage, UI) completes first.
        IEnumerator AutoStart()
        {
            yield return null;
            try { BuildRuntimeHud(); }
            catch (Exception e) { Debug.LogError($"[TempleHuntDirector] HUD build failed — continuing without: {e}"); }
            StartCoroutine(GameFlow());
        }

        void BuildRuntimeHud()
        {
            if (hudPanel == null) return;
            _hud = GameHud.Ensure(hudPanel);
            // Bottom-centre, between the two GameHud corner rings.
            _ring = RingGauge.Create(hudPanel.GetComponent<RectTransform>(), "ProgressRing",
                                     new Vector2(0.5f, 0f), new Vector2(0f, 60f), 250f);
            if (_ring != null) _ring.SetColor(new Color(1f, 0.84f, 0.35f));
        }

        void Update()
        {
            TickMotion(Time.deltaTime);
            if (_hud != null && _state != State.Idle && _state != State.Victory)
                _hud.SetScore(_state == State.Balance ? TempleLogic.BalanceMeter01(Wobble01) : Progress01);
        }

        // ---- Buttons (wired by TempleHuntUIBuilder) ----
        public void SkipIntro() => _introSkipped = true;
        public void SkipRest() => _skipRest = true;
        public void ExitToHome() => OnExitRequested?.Invoke();
        public void PlayAgain() => SceneManager.LoadScene(gameObject.scene.name);

        // =====================================================================
        IEnumerator GameFlow()
        {
            _startTime = Time.time;

            // ---- Intro: the parrot sets the scene. ----
            _state = State.Intro;
            ShowOnly(introPanel);
            Kinex.Sfx.Play("pose_appear");
            Say("สวัสดีครับ ผมโปโล่ นกแก้วนำทาง เราจะผจญภัยในวิหารโบราณด้วยกัน ผ่านห้องลับ 4 ห้อง แล้วสมบัติจะเป็นของคุณ",
                speak: true);
            _introSkipped = false;
            for (float t = 0f; t < introSeconds && !_introSkipped; t += Time.deltaTime)
                yield return null;

            yield return CalibSeatedRoutine();

            Kinex.Music.Play("quest_theme", 0.35f); // TODO: replace with a real temple_theme.mp3

            // ---- Chamber 1: the Pumping Station ----
            yield return AnnounceRoutine(1, "ห้องสูบน้ำโบราณ",
                "นั่งบนเก้าอี้ ยกเข่าขึ้นสลับซ้าย-ขวา เหมือนย่ำเท้าอยู่กับที่",
                "ยกเข่าสลับกัน คันโยกจะสูบน้ำออก น้ำลดเมื่อไหร่ ประตูจะเปิดครับ",
                DemoPoseKneeUp);
            yield return PumpRoutine();
            yield return ChamberClearRoutine(1);

            // ---- Chamber 2: the Heavy Stone Gate ----
            yield return AnnounceRoutine(2, "ประตูหินยักษ์",
                "กอดแขนไว้ที่หน้าอก ลุกขึ้นยืนช้าๆ โดยไม่ใช้มือช่วย แล้วนั่งลง ทำซ้ำ",
                "ลุกหนึ่งครั้ง ประตูเลื่อนขึ้นหนึ่งขั้น มีสามประตู ค่อยๆ ทำนะครับ",
                DemoPoseSitStand);
            yield return GateRoutine();
            yield return ChamberClearRoutine(2);

            // ---- Stand up + standing calibration before the balance chambers. ----
            yield return StandTransitionRoutine();

            // ---- Chamber 3: the Chasm Bridge ----
            yield return AnnounceRoutine(3, "สะพานข้ามเหว",
                "กางแขนออกด้านข้างเหมือนปีกนก ยกเท้าขึ้นหนึ่งข้าง ทรงตัวค้างไว้ 10 วินาที",
                "กางปีกเหมือนนกนะครับ เซได้ ไม่เป็นไร เริ่มใหม่ได้เสมอ",
                DemoPoseBalance);
            yield return BalanceRoutine(handsOnChest: false);
            yield return ChamberClearRoutine(3);

            // ---- Chamber 4: the Sacred Totem Vault ----
            yield return AnnounceRoutine(4, "ห้องสมบัติศักดิ์สิทธิ์",
                "ประสานมือไว้ที่หน้าอก ยกเท้าขึ้นหนึ่งข้าง ทรงตัวค้างไว้ 10 วินาที",
                "ด่านสุดท้าย มือกอดอก ทรงตัวนิ่งๆ สมบัติรอเราอยู่ครับ",
                DemoPoseBalance);
            yield return BalanceRoutine(handsOnChest: true);
            // Chamber 4's celebration IS the treasure finale.
            try
            {
                if (stage != null) stage.OpenChest();
                Vector3 chestPos = stage != null ? stage.ChestPosition : new Vector3(0f, 1.5f, 1.5f);
                KinexFx.ConfettiBurst(chestPos + Vector3.up * 0.3f, 80);
                KinexFx.PopBurst(chestPos, new Color(1f, 0.85f, 0.4f), 30);
            }
            catch (Exception e) { Debug.LogError($"[TempleHuntDirector] finale fx failed — continuing: {e}"); }
            Kinex.Sfx.Play("fanfare");
            _result.chambersCompleted = 4;
            yield return new WaitForSeconds(clearCelebrateSeconds);

            ShowVictory();
        }

        // ---- Calibration: seated capture with guided retry (FruitGame pattern). ----
        IEnumerator CalibSeatedRoutine()
        {
            _state = State.Calib;
            ShowOnly(calibPanel);
            if (calibText != null)
                calibText.text = "นั่งบนเก้าอี้ที่มั่นคง มีพนักพิง\nวางเท้าราบกับพื้น ให้กล้องเห็นเต็มตัว";
            Say("นั่งบนเก้าอี้ที่มั่นคง ให้กล้องเห็นเต็มตัว แล้วนั่งนิ่งๆ สักครู่นะครับ", speak: true);

            if (useKeyboardStub) { yield return new WaitForSeconds(1.5f); yield break; }

            bool success = false;
            int samples = 0;
            for (int attempt = 1; attempt <= calibMaxAttempts && !success; attempt++)
            {
                if (attempt > 1)
                {
                    if (calibText != null) calibText.text = $"นั่งตัวตรง ค้างไว้ ({attempt}/{calibMaxAttempts})";
                    Say("ลองอีกครั้งนะครับ นั่งนิ่งๆ ให้กล้องเห็นเต็มตัว", speak: true);
                    yield return new WaitForSeconds(1.5f);
                }
                SetCalibBar(0f);
                _sitStand.ResetSeatedCalibration();
                samples = 0;

                float visible = 0f;
                for (float t = 0f; visible < calibHoldSeconds && t < calibTimeoutSeconds; t += Time.deltaTime)
                {
                    if (HasLivePose && _gate.Visible)
                    {
                        var kp = poseDetector.LatestKeypoints;
                        _sitStand.CalibrateSeated(kp);
                        if (samples == 0) _knee.SetBaseline(kp);
                        samples++;
                        visible += Time.deltaTime;
                        SetCalibBar(visible / calibHoldSeconds);
                    }
                    yield return null;
                }
                success = visible >= calibHoldSeconds;
            }

            // NEVER proceed with zero samples: without a knee baseline / seated baseline the
            // detectors no-op forever and Chamber 1 would freeze at 0 reps with no pause overlay
            // (QA finding #1). Wait — with guidance — for the first visible frames instead.
            if (samples == 0)
            {
                if (calibText != null) calibText.text = "ยังไม่เห็นคุณในกล้อง\nขยับให้เห็นเต็มตัวนะครับ";
                Say("ยังไม่เห็นคุณในกล้อง ขยับให้เห็นเต็มตัวนะครับ", speak: true);
                while (samples < 30) // ~0.5 s of visible frames
                {
                    if (HasLivePose && _gate.Visible)
                    {
                        var kp = poseDetector.LatestKeypoints;
                        _sitStand.CalibrateSeated(kp);
                        if (samples == 0) _knee.SetBaseline(kp);
                        samples++;
                        SetCalibBar(samples / 30f);
                    }
                    yield return null;
                }
            }

            // Standing baseline is estimated from the seated one; the detector's adaptive range
            // corrects it over the first stands of Chamber 2.
            _sitStand.EstimateStandingFromSeated();
            if (!success) Debug.LogWarning("[TempleHuntDirector] seated calibration was partial — proceeding with what was captured");
        }

        // ---- Chamber announce: banner + ghost demo + parrot line + TTS. ----
        IEnumerator AnnounceRoutine(int chamber, string title, string instruction, string parrotLine, int demoPose)
        {
            _state = State.Announce;
            ShowOnly(hudPanel);
            try
            {
                if (stage != null) stage.ShowChamber(chamber);
                EnsureGhost(demoPose);
            }
            catch (Exception e) { Debug.LogError($"[TempleHuntDirector] announce setup failed — continuing: {e}"); }

            if (chamberTitleText != null) chamberTitleText.text = title;
            if (chamberCountText != null) chamberCountText.text = $"ห้องที่ {chamber}/4";
            if (instructionText != null) instructionText.text = instruction;
            if (_ring != null) { _ring.SetFill(0f); _ring.SetValue("", ""); }
            Kinex.Sfx.Play("pose_appear");
            Say(parrotLine, speak: false);
            Speak(instruction);

            if (_ghost != null) _ghost.FadeTo(0.9f);
            yield return new WaitForSeconds(announceSeconds);
            if (_ghost != null) _ghost.FadeTo(0f);
        }

        void EnsureGhost(int demoPose)
        {
            if (trainerRigPrefab == null || rehabPoseData == null) return;
            if (_ghost == null)
            {
                Transform parent = stage != null ? stage.transform : transform;
                // Left of the avatar, inside the narrow portrait camera cone (~±1.4 m at z=1.4).
                _ghost = PoseGhost.Create(parent, trainerRigPrefab, rehabPoseData, demoPose,
                                          new Vector3(-1.05f, 0f, 1.4f));
            }
            else
            {
                _ghost.ShowPoseIndex(demoPose);
            }
        }

        // ---- Chamber 1: pump the water out with alternating knee raises. ----
        IEnumerator PumpRoutine()
        {
            _state = State.Pump;
            int baseline = KneeCount;
            int reps = 0;
            bool halfSaid = false;
            if (_ring != null) _ring.SetValue("0", $"/ {pumpTarget}");

            while (reps < pumpTarget)
            {
                yield return SafetyPause();

                reps = Mathf.Min(KneeCount - baseline, pumpTarget);
                if (stage != null)
                {
                    stage.SetWaterLevel01(TempleLogic.WaterLevel01(reps, pumpTarget));
                    stage.SetLever(LeftKneeUp, RightKneeUp);
                }
                if (JustKneeCounted)
                {
                    Kinex.Sfx.Play("pop", 0.9f, 1f + 0.01f * reps);
                    KinexFx.PopBurst(new Vector3(1.5f, 0.7f, 0.2f), new Color(0.5f, 0.9f, 1f), 12);
                    if (_ring != null)
                    {
                        _ring.SetFill(reps / (float)pumpTarget);
                        _ring.SetValue(reps.ToString(), $"/ {pumpTarget}");
                        _ring.Pulse();
                    }
                    if (!halfSaid && reps >= pumpTarget / 2)
                    {
                        halfSaid = true;
                        Say("ครึ่งทางแล้ว เยี่ยมมากครับ", speak: true);
                    }
                }
                yield return null;
            }

            _result.legLifts = pumpTarget;
            if (stage != null) { stage.SetWaterLevel01(0f); stage.SetLever(false, false); }
            Kinex.Sfx.Play("zone");
            yield return new WaitForSeconds(1.2f);
        }

        // ---- Chamber 2: three stone gates, each lifted by chair stands. ----
        IEnumerator GateRoutine()
        {
            _state = State.Gate;
            if (gatePipsPanel != null) gatePipsPanel.SetActive(true);

            for (int gate = 1; gate <= gateCount; gate++)
            {
                if (stage != null) stage.SetGateStage(gate);
                UpdateGatePips(gate - 1);
                if (instructionText != null)
                    instructionText.text = $"ประตูที่ {gate}/{gateCount} — ลุกขึ้นยืน {repsPerGate} ครั้ง";

                int baseline = StandCount;
                int repsInGate = 0;
                _standCounted = false;
                if (_ring != null) { _ring.SetFill(0f); _ring.SetValue("0", $"/ {repsPerGate}"); }

                while (repsInGate < repsPerGate)
                {
                    yield return SafetyPause();

                    if (JustStood)
                    {
                        _standCounted = true;
                        Kinex.Sfx.Play("hit_2");
                        Kinex.Sfx.Play("stone_grind"); // silent no-op until the clip is added
                        KinexFx.PopBurst(new Vector3(0f, 0.4f, 4.6f), new Color(0.75f, 0.65f, 0.5f), 18);
                        if (_ring != null) _ring.Pulse();
                    }
                    if (JustSat) _standCounted = false;

                    repsInGate = Mathf.Min(StandCount - baseline, repsPerGate);
                    if (stage != null)
                        stage.SetGateLift01(TempleLogic.GateLift01(repsInGate, repsPerGate, Progress01, _standCounted));
                    if (_ring != null)
                    {
                        _ring.SetFill(TempleLogic.GateLift01(repsInGate, repsPerGate, Progress01, _standCounted));
                        _ring.SetValue(repsInGate.ToString(), $"/ {repsPerGate}");
                    }
                    yield return null;
                }

                if (stage != null) stage.SetGateLift01(1f);
                UpdateGatePips(gate);
                Kinex.Sfx.Play("levelup");
                Say($"ประตูที่ {gate} เปิดแล้ว เก่งมากครับ", speak: true);

                if (gate < gateCount)
                    yield return GateRestRoutine();
            }

            _result.stands = gateCount * repsPerGate;
            if (gatePipsPanel != null) gatePipsPanel.SetActive(false);
        }

        void UpdateGatePips(int opened)
        {
            if (gatePips == null) return;
            for (int i = 0; i < gatePips.Length; i++)
                if (gatePips[i] != null)
                    gatePips[i].color = i < opened
                        ? new Color(1f, 0.84f, 0.35f)
                        : new Color(1f, 1f, 1f, 0.25f);
        }

        // Short breather between gates — auto-continues, skippable immediately.
        IEnumerator GateRestRoutine()
        {
            if (restText != null) restText.gameObject.SetActive(true);
            if (skipRestButton != null) skipRestButton.SetActive(true);
            _skipRest = false;
            Say("พักหายใจสักครู่นะครับ", speak: true);

            for (float t = 0f; t < gateRestSeconds && !_skipRest; t += Time.deltaTime)
            {
                if (restText != null)
                    restText.text = $"พักหายใจ {Mathf.CeilToInt(gateRestSeconds - t)} วินาที";
                yield return null;
            }
            if (restText != null) restText.gameObject.SetActive(false);
            if (skipRestButton != null) skipRestButton.SetActive(false);
        }

        // ---- Between chambers 2 and 3: stand up for good + standing calibration. ----
        IEnumerator StandTransitionRoutine()
        {
            _state = State.StandTransition;
            if (instructionText != null)
                instructionText.text = "ยืนขึ้นช้าๆ จับพนักเก้าอี้ได้ถ้าต้องการ\nแล้วยืนนิ่งๆ ให้เห็นทั้งตัว";
            Say("ต่อไปเป็นด่านทรงตัว ยืนขึ้นช้าๆ แล้วยืนนิ่งๆ นะครับ", speak: true);

            if (useKeyboardStub) { yield return new WaitForSeconds(1.5f); yield break; }

            // Wait for a sustained stand — but bounded (QA finding #2): a player whose chair-stand
            // reads shallow (estimated standing baseline + clamped adaptation) may never cross 0.8
            // even though they're upright. After the timeout, trust the spoken instruction and
            // refresh the standing baseline from whatever they're doing now.
            float upTime = 0f;
            for (float waited = 0f; upTime < 1f && waited < 20f; waited += Time.deltaTime)
            {
                yield return SafetyPause();
                upTime = Progress01 > 0.8f ? upTime + Time.deltaTime : 0f;
                yield return null;
            }
            if (upTime < 1f)
            {
                bool reset = false;
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                {
                    if (HasLivePose && _gate.Visible)
                    {
                        if (!reset) { _sitStand.ResetStandingCalibration(); reset = true; }
                        _sitStand.CalibrateStanding(poseDetector.LatestKeypoints);
                    }
                    yield return null;
                }
            }

            // Standing capture for the balance detector — guided retry like the seated one.
            if (instructionText != null) instructionText.text = "ยืนนิ่งๆ สักครู่ กำลังปรับกล้อง";
            bool success = false;
            for (int attempt = 1; attempt <= calibMaxAttempts && !success; attempt++)
            {
                if (attempt > 1)
                {
                    Say("ยืนนิ่งๆ อีกครั้งนะครับ", speak: true);
                    yield return new WaitForSeconds(1f);
                }
                SetCalibBar(0f);
                float captured = 0f;
                for (float t = 0f; captured < standCalibSeconds && t < calibTimeoutSeconds; t += Time.deltaTime)
                {
                    if (HasLivePose && _gate.Visible && Progress01 > 0.7f)
                    {
                        _sls.CalibrateStanding(poseDetector.LatestKeypoints);
                        captured += Time.deltaTime;
                        SetCalibBar(captured / standCalibSeconds);
                        if (_ring != null) _ring.SetFill(captured / standCalibSeconds);
                    }
                    yield return null;
                }
                success = captured >= standCalibSeconds;
            }
            // Last resort (QA finding #3): the balance chambers cannot work uncalibrated, so wait
            // for the first visible frame rather than testing a single instant and giving up.
            while (!_sls.IsCalibrated)
            {
                if (HasLivePose && _gate.Visible)
                    _sls.CalibrateStanding(poseDetector.LatestKeypoints);
                yield return null;
            }
        }

        // ---- Chambers 3 & 4: single-leg balance holds, left side then right. ----
        IEnumerator BalanceRoutine(bool handsOnChest)
        {
            _state = State.Balance;
            if (balanceMeterPanel != null) balanceMeterPanel.SetActive(true);

            for (int side = 0; side < 2; side++)
            {
                bool liftLeftFoot = side == 0;                 // lift left foot = stand on right leg
                bool wantStanceLeft = !liftLeftFoot;
                string footName = liftLeftFoot ? "ซ้าย" : "ขวา";

                if (instructionText != null)
                    instructionText.text = side == 0
                        ? $"ยกเท้า{footName}ขึ้น ทรงตัวค้างไว้ {Mathf.RoundToInt(holdTargetSeconds)} วินาที"
                        : $"เปลี่ยนข้าง ยกเท้า{footName}ขึ้น ค้างไว้ {Mathf.RoundToInt(holdTargetSeconds)} วินาที";
                Say(side == 0 ? $"ยกเท้า{footName}ขึ้นครับ" : $"เปลี่ยนข้าง ยกเท้า{footName}ครับ", speak: true);
                yield return new WaitForSeconds(2f);

                float held = 0f;
                int lastWholeSecond = 0;
                float wrongLegTime = 0f, wrongArmTime = 0f, highWobbleTime = 0f;
                bool wrongLegCoached = false, wrongArmCoached = false;

                while (held < holdTargetSeconds)
                {
                    yield return SafetyPause();
                    float dt = Time.deltaTime;

                    bool correctLeg = !SlsHolding || useKeyboardStub || _sls.StanceLegIsLeft == wantStanceLeft;
                    bool armOk = ArmOk(handsOnChest, dt);
                    bool ok = SlsHolding && correctLeg && armOk;
                    held = TempleLogic.TickHold(held, ok, dt, holdTargetSeconds);

                    if (_ring != null)
                    {
                        _ring.SetFill(held / holdTargetSeconds);
                        _ring.SetValue(Mathf.CeilToInt(holdTargetSeconds - held).ToString(), "วินาที");
                    }
                    int whole = Mathf.FloorToInt(held);
                    if (whole > lastWholeSecond)
                    {
                        lastWholeSecond = whole;
                        Kinex.Sfx.Play("beep", 0.5f, 1f + 0.03f * whole);
                        if (_ring != null) _ring.Pulse(1.05f, 0.1f);
                    }

                    if (balanceMeterFill != null)
                    {
                        float meter = TempleLogic.BalanceMeter01(Wobble01);
                        balanceMeterFill.fillAmount = meter;
                        balanceMeterFill.color = Color.Lerp(new Color(1f, 0.7f, 0.25f), new Color(0.45f, 0.9f, 0.45f), meter);
                    }
                    if (stage != null)
                    {
                        float chamberProgress = (side + held / holdTargetSeconds) * 0.5f;
                        if (handsOnChest) stage.SetTotemLit01(chamberProgress);
                        else stage.SetBeamGlow01(held / holdTargetSeconds);
                    }

                    // Gentle coaching — advisory only, never blocks or pauses.
                    wrongLegTime = (SlsHolding && !correctLeg) ? wrongLegTime + dt : 0f;
                    if (wrongLegTime > 4f && !wrongLegCoached)
                    {
                        wrongLegCoached = true;
                        Say("อีกข้างหนึ่งนะครับ", speak: true);
                    }
                    wrongArmTime = (SlsHolding && correctLeg && !armOk) ? wrongArmTime + dt : 0f;
                    if (wrongArmTime > 3f && !wrongArmCoached)
                    {
                        wrongArmCoached = true;
                        Say(handsOnChest ? "เอามือมาประสานที่หน้าอกครับ" : "กางแขนออกกว้างๆ ครับ", speak: true);
                    }
                    highWobbleTime = Wobble01 > 0.85f ? highWobbleTime + dt : 0f;
                    if (highWobbleTime > 1.5f && Time.time - _lastCoachAt > 8f)
                    {
                        _lastCoachAt = Time.time;
                        Say("ค่อยๆ ทรงตัว ไม่ต้องรีบนะครับ", speak: true);
                    }

                    yield return null;
                }

                _result.balanceHoldSeconds += holdTargetSeconds;
                Kinex.Sfx.Play("correct");
                KinexFx.PopBurst(new Vector3(0f, 1.4f, 0.4f), new Color(1f, 0.9f, 0.5f), 24);
                Say(side == 0 ? "เยี่ยมมาก พักขาสักครู่ แล้วเปลี่ยนข้างครับ" : "สุดยอดครับ", speak: true);

                if (side == 0)
                {
                    if (instructionText != null) instructionText.text = "วางเท้าลง พักขาสักครู่";
                    yield return new WaitForSeconds(5f);
                }
            }

            if (balanceMeterPanel != null) balanceMeterPanel.SetActive(false);
        }

        // ---- Shared chamber-clear celebration. ----
        IEnumerator ChamberClearRoutine(int chamber)
        {
            _state = State.ChamberClear;
            _result.chambersCompleted = chamber;
            Kinex.Sfx.Play("checkpoint");
            Kinex.Sfx.Play("star");
            KinexFx.ConfettiBurst(new Vector3(0f, 1.6f, 0.4f), 50);
            Say("เก่งมากครับ ไปห้องต่อไปกัน", speak: true);
            yield return new WaitForSeconds(clearCelebrateSeconds);
        }

        // ---- Safety pause: amber overlay while the camera can't see the player. ----
        IEnumerator SafetyPause()
        {
            if (!ShouldPause) yield break;

            if (pauseOverlay != null) pauseOverlay.SetActive(true);
            if (pauseText != null) pauseText.text = "มองไม่เห็นคุณแล้ว\nขยับกลับมาหน้ากล้องนะครับ";
            Say("ค่อยๆ ทำ ไม่ต้องรีบนะครับ ขยับกลับมาหน้ากล้องก่อน", speak: true);

            while (ShouldPause) yield return null;

            if (pauseText != null) pauseText.text = "เห็นคุณแล้ว พร้อมแล้วไปต่อกันครับ";
            Kinex.Sfx.Play("correct", 0.7f);
            yield return new WaitForSeconds(1.5f);
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
        }

        // ---- Victory: badge + achievement summary. ----
        void ShowVictory()
        {
            _state = State.Victory;
            ShowOnly(victoryPanel);
            Kinex.Music.Stop();
            Kinex.Sfx.Play("results");

            _result.durationSeconds = Time.time - _startTime;
            _result.stars = TempleLogic.Stars(_result.chambersCompleted);
            _result.coins = TempleLogic.Coins(_result.legLifts, _result.stands, _result.balanceHoldSeconds);

            if (victoryStatsText != null)
                victoryStatsText.text =
                    $"ปั๊มน้ำ {_result.legLifts} ครั้ง  •  ลุกยืน {_result.stands} ครั้ง\n" +
                    $"ทรงตัวรวม {Mathf.RoundToInt(_result.balanceHoldSeconds)} วินาที\n" +
                    $"ใช้เวลา {Mathf.RoundToInt(_result.durationSeconds / 60f)} นาที  •  เหรียญ {_result.coins}";

            Speak("สุดยอดมากครับ คุณคือนักสำรวจตัวจริง สมบัติเป็นของคุณแล้ว");
            OnSessionComplete?.Invoke(_result);
        }

        // ---- Small helpers ----
        void SetCalibBar(float t)
        {
            if (calibProgressFill != null) calibProgressFill.fillAmount = Mathf.Clamp01(t);
        }

        /// <summary>One call = the parrot bubble always matches what's (optionally) spoken.</summary>
        void Say(string line, bool speak)
        {
            if (parrot != null) parrot.Say(line);
            if (speak) Speak(line);
        }

        void Speak(string line)
        {
            if (voice == null) return;
            Kinex.Music.Duck(2.5f);
            voice.Speak(line);
        }

        void ShowOnly(GameObject panel)
        {
            SetPanels(false, false, false, false);
            if (panel != null) panel.SetActive(true);
            // The HUD stays up behind the pause overlay; every other state is exclusive.
        }

        void SetPanels(bool intro, bool calib, bool hud, bool victory)
        {
            if (introPanel != null) introPanel.SetActive(intro);
            if (calibPanel != null) calibPanel.SetActive(calib);
            if (hudPanel != null) hudPanel.SetActive(hud);
            if (victoryPanel != null) victoryPanel.SetActive(victory);
        }
    }
}
