using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Kinex.Motion;
using Kinex.FX;
using Kinex.Shared;

namespace Kinex.TheDasher
{
    /// <summary>
    /// ASTROSTANCE — behind-the-character 3-lane space game, 3 minutes, camera-controlled.
    /// Side-step (p15) changes lane / dodges meteors, sit-to-stand (p7) grabs a tool and
    /// claims landed treasure, side-kick (p9) pops kick rings from an ADJACENT lane.
    /// Flow: Intro → Framing (FullBodyGate, Thai prompts) → stand-still Calibration →
    /// 3-2-1 Countdown → Play (fixed 28-beat deck) → Results. Losing the body mid-run
    /// safety-pauses back into Framing (Ignore button offered — see HandleBodyLost) and
    /// resumes through the countdown, unless the player has opted out via Ignore.
    ///
    /// Lane conventions: gameplay lanes are SCREEN space (-1 left / +1 right behind the
    /// avatar). The raw LaneDetector sign is image space (player-left = frame-right for an
    /// unmirrored front camera, verified against recorded tablet feeds in DasherFeedReplayTest);
    /// invertLateralForBackView is the single master flag that reconciles that raw sign — and
    /// the matching kick-side and avatar-limb conventions — with what the LIVE camera actually
    /// produces (see its tooltip; the two can differ from a mirroring mismatch).
    /// </summary>
    public class TheDasherDirector : MonoBehaviour
    {
        enum State { Intro, Framing, Calibrating, Tutorial, Countdown, Play, Results }

        [Header("Detection")]
        public MediaPipePoseDetector poseDetector;
        [Tooltip("Editor keyboard mode (←/→ lane, S sit/stand, N/M side kick). Forced OFF on device in Awake.")]
        public bool useKeyboardStub = true;
        [Tooltip("MASTER lateral-mirror correction for the live front camera. TheDasher is a " +
                 "behind-view game, so the player's real left/right must agree across THREE things " +
                 "that all read the same raw MediaPipe landmarks: which lane a side-step lands in, " +
                 "which foot a side-kick registers as, and which of the avatar's own arms/legs a " +
                 "raised limb drives (the latter via poseDetector.SameSideRetarget, pushed in Awake). " +
                 "A camera-mirroring mismatch flips all three together — confirmed on-device: raising " +
                 "the real right hand raised the avatar's LEFT, stepping right moved the LEFT lane, " +
                 "and a right kick registered as a left kick. TRUE (default) applies the correction " +
                 "that fixes that. If it's still backwards after this build, there is only one other " +
                 "possible lateral convention — flip this ONE value to FALSE (restores the original, " +
                 "pre-fix mapping) rather than hunting through the three call sites individually.")]
        public bool invertLateralForBackView = true;

        [Tooltip("TEMP tuning aid: shows an on-screen panel with 4 buttons to flip the avatar's " +
                 "arm-side, arm-direction, leg-side and leg-direction live on device, so the correct " +
                 "retarget mapping can be found in ONE build. Mapping now CONFIRMED + baked — leave " +
                 "OFF. Flip on only if the mapping ever needs re-tuning.")]
        public bool showRetargetDebug = false;

        [Header("Avatar")]
        [Tooltip("Wrapper the lane locomotion slides sideways. The character model is its child.")]
        public Transform avatarRoot;
        [Tooltip("The character's Animator — used to find the hand bone for the grabber tool.")]
        public Animator avatarAnimator;
        public float laneLerpSpeed = 4f;
        [Tooltip("Body lean into a side-step, degrees.")]
        public float leanDegrees = 7f;
        [Tooltip("Scales how far the avatar visually slides between lanes (1 = full lane spacing). " +
                 "Lower = the character moves less side-to-side while the lanes/items stay put. 0.65 " +
                 "= 35% less travel. Cosmetic only — lane logic/scoring is unchanged.")]
        public float avatarLaneMoveScale = 0.65f;
        [Tooltip("How far the avatar's hips drop (metres) while the sit is held. Paired with the " +
                 "scripted knee bend below so the feet stay planted — together they read as a real " +
                 "crouch to pick up the tool. 0 = no dip.")]
        public float sitDipMeters = 0.36f;
        [Tooltip("Seconds for the sit crouch to fully ease in/out. Lower = snappier.")]
        public float sitDipSeconds = 0.35f;

        [Header("Run")]
        public DasherSpawner spawner;
        public float sessionSeconds = 180f;
        [Tooltip("0 = random deck each run; any other value replays the same deck (testing).")]
        public int deckSeed = 0;

        [Header("Body-loss warning")]
        [Tooltip("Ignore button on the framing/lock panel — assigned by the UI builder, hidden by " +
                 "default. Only shown on a MID-GAME body-loss lock, never on the initial pre-Start " +
                 "framing. Pressing it (OnBodyLostIgnorePressed) stops future losses from locking.")]
        public Button bodyLostIgnoreButton;
        [Tooltip("Small warning popup ('ขยับให้เห็นทั้งตัว') shown ~3s at a time once the player has " +
                 "pressed Ignore and the body is lost again mid-run. Hidden by default.")]
        public GameObject bodyLostToast;

        [Header("Camera shake")]
        [Tooltip("Shaken briefly on a meteor hit. Wire the Main Camera transform.")]
        public Transform shakeTarget;

        [Header("Tutorial diagram + button art")]
        [Tooltip("Camera-frame border for the chair-prep diagram (TutorialController.ShowIntro). Assign TheDasher/UI/feed_frame.png.")]
        public Sprite tutorialFrameSprite;
        [Tooltip("Round dot for the diagram's head/lens. Assign TheDasher/UI/dot_on.png.")]
        public Sprite tutorialHeadSprite;
        [Tooltip("เริ่มสอนเล่น button background (idle). Assign TheDasher/UI/btn_primary.png.")]
        public Sprite tutorialButtonSprite;
        [Tooltip("เริ่มสอนเล่น button background (pressed). Assign TheDasher/UI/btn_primary_down.png.")]
        public Sprite tutorialButtonPressedSprite;

        [Header("Panels")]
        public GameObject introPanel;
        public GameObject framingPanel;
        public GameObject hudPanel;
        public GameObject resultsPanel;
        public GameObject pausePanel;
        public GameObject cameraPreviewPanel;
        [Tooltip("The small chevron button that hides/shows the camera preview — retired with the preview at results.")]
        public GameObject previewToggleButton;

        [Header("Framing UI")]
        public TMP_Text framingPromptText;
        [Tooltip("5 checklist chips: หัว ไหล่ สะโพก เข่า เท้า.")]
        public Image[] framingChips = new Image[5];
        public Image framingHoldRing;

        [Header("Calibration UI")]
        public GameObject calibGroup;
        public TMP_Text calibText;
        public Image calibRing;

        [Header("HUD")]
        public TMP_Text scoreText;
        public TMP_Text timerText;
        public Image timerRing;
        public TMP_Text toastText;
        [Tooltip("Optional pill behind the toast text. Fades in/out with the message — leave null for text-only.")]
        public Image toastBg;
        [Tooltip("Floating score-change card (scorechange.png) — popped in briefly by ShowScorePop " +
                 "whenever the score changes. Hidden by default; assigned by the UI builder.")]
        public Image scoreChangePopup;
        [Tooltip("The +1 / −1 number inside scoreChangePopup.")]
        public TMP_Text scoreChangeText;
        [Tooltip("Picture popup shown ~1.2s on a safe meteor dodge (pop_safe.png 'ปลอดภัย!'). Hidden by default.")]
        public Image feedbackSafe;
        [Tooltip("Picture popup shown ~1.2s on a meteor hit / foul (pop_danger.png 'ระวัง!'). Hidden by default.")]
        public Image feedbackDanger;
        [Tooltip("3 lane indicator dots, left→right.")]
        public Image[] laneDots = new Image[3];
        public TMP_Text countdownText;
        [Tooltip("Badge/disc behind the 3-2-1 number so it reads over the bright park. Toggled with " +
                 "the countdown text; hidden by default.")]
        public GameObject countdownBadge;

        [Header("Results UI")]
        public TMP_Text resultScoreText;
        [Tooltip("Dynamic praise headline (ทำได้ดีมาก! etc.) set from the star count at end of run.")]
        public TMP_Text resultPraiseText;
        [Tooltip("3 star images, dimmed/lit by the director.")]
        public Image[] resultStars = new Image[3];
        [Tooltip("Value texts for the rep rows: สมบัติ เตะ หลบ (game actions) then ลุก-นั่ง ก้าว เตะขา (poses).")]
        public TMP_Text[] resultRepValues = new TMP_Text[6];

        public event Action<DasherResult> OnSessionComplete;
        public event Action OnExitRequested;

        const float FramingHoldSeconds = 1.5f;
        const float BodyLostSeconds = 1.2f;
        const float CalibStillSeconds = 2f;
        const float CalibMoveTolerance = 0.03f;
        const float CountdownSeconds = 3f;
        const float ToastSeconds = 1.5f;
        const float BodyLostToastSeconds = 3f; // how long the post-ignore "keep moving" popup stays up

        // Bright sunny-park palette (matches TheDasherUIBuilder). ChipPass/Green, ChipFail/coral,
        // and the toast/star accents are kept saturated since they sit on solid fills or always
        // carry a dark outline — StarDim is the one that needed to flip from white to dark ink
        // since results stars sit on the now-cream ResultsPanel card.
        static readonly Color ChipPass = new Color(0.32f, 0.70f, 0.36f, 0.95f);
        static readonly Color ChipFail = new Color(0.95f, 0.45f, 0.34f, 0.95f);
        static readonly Color LaneDotOn = new Color(0.28f, 0.62f, 0.92f);
        static readonly Color LaneDotOff = new Color(1f, 1f, 1f, 0.25f);
        static readonly Color ToastGold = new Color(1.00f, 0.76f, 0.20f);
        static readonly Color ToastOrange = new Color(0.95f, 0.45f, 0.34f);
        static readonly Color ToastCyan = new Color(0.28f, 0.62f, 0.92f);
        static readonly Color StarLit = new Color(1.00f, 0.76f, 0.20f);
        static readonly Color StarDim = new Color(0.14f, 0.20f, 0.12f, 0.18f);

        readonly FullBodyGate _fullBody = new FullBodyGate();
        readonly DasherSitClassifier _sit = new DasherSitClassifier();
        readonly LegAbductionDetector _sideKick = new LegAbductionDetector();
        readonly LaneDetector _lane = new LaneDetector();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();

        State _state = State.Intro;
        bool _calibrated;
        bool _runStarted;
        bool _tutorialEnabled; // pushed from Flutter (settings toggle) via SceneRouter, read in Start
        TutorialController _tut;
        int _tutStep;          // 0 = step/dodge, 1 = sit, 2 = kick
        bool _tutorialDone;
        float _tutSuccessTimer; // brief success hold between steps
        bool _tutCueLive;       // a real cue is currently falling for this step (miss detection)
        int _tutCueLane;        // lane the current cue is in (dodge = step OFF it; kick = its side)
        float _tutArm;          // seconds the cue has been up — gates only accept AFTER a short arm
        bool _tutIntroShown;    // "place a chair" get-ready modal already shown (once per session)
        bool _tutIntroPending;  // modal is up now — hold step logic until user taps เริ่ม
        bool _manualPaused;
        bool _satThisRep; // a JustStood only counts as a rep if a JustSat preceded it
        float _calibStillTimer;
        Vector2 _calibAnchor;
        bool _calibAnchorSet;
        float _countdownLeft;
        float _timeLeft;
        float _toastTimer;
        Coroutine _scoreChangeRoutine;
        Coroutine _feedbackRoutine;
        SosController _sos;   // emergency fall-alert overlay
        float _avatarBaseX;
        float _avatarBaseY;
        float _sitDip01;   // 0 = standing, 1 = fully dipped into the sit crouch (smoothed)
        bool _sitHeld;     // true between JustSat and the following JustStood
        HumanPoseHandler _poseHandler;   // scripted sit crouch (bends knees while the puppet is off)
        HumanPose _crouchPose;
        bool _crouchPoseActive;
        int[] _crouchMuscle;
        float[] _crouchTarget;
        int _standingCaptureCounter; // throttles the standing-pose GetHumanPose capture — see TickSitCrouch
        int _lastLane;
        GameObject _tool;
        bool _claimPending; // sat over a treasure, tool grabbed — collect once the crouch bottoms out
        DasherResult _result = new DasherResult();

        float _bodyLostTimer; // seconds every joint group has been MISSING (drives the play→framing bounce)
        bool _bodyWarnIgnored;    // true once the player has pressed Ignore — later losses toast, not lock
        float _bodyLostToastTimer; // counts down while bodyLostToast is showing

        DasherDifficulty _difficulty = DasherDifficulty.Normal;
        // DasherSpawner's own Inspector values, captured once at Start so ApplyDifficulty can scale
        // FROM the designer's actual numbers instead of hard-coded duplicates (Normal always == these).
        float _baseFallSeconds, _baseBeatInterval, _baseTreasureWindow, _baseRingWindow;

        // ---- Motion facade: the one place that switches stub vs live detectors. ----
        bool HasLivePose => poseDetector != null && poseDetector.HasPose;
        bool FullBodyOk => useKeyboardStub ? _stub.IsFullBodyVisible : _fullBody.IsFullBodyVisible;

        // Looser than IsFullBodyVisible: every tracked joint GROUP is in frame, but WITHOUT the
        // standing-height span requirement. Sitting shrinks the nose→ankle span ~30% (below the
        // shared FullBodyGate's MinBodySpan), which would otherwise read as "too far" and eject the
        // player to the framing screen the instant they sit — fatal for a sit-to-stand game. During
        // Play we only care that the body is still THERE, not that it fills a standing frame.
        bool BodyPresentForPlay
        {
            get
            {
                if (useKeyboardStub) return _stub.IsFullBodyVisible;
                var g = _fullBody.GroupOk;
                for (int i = 0; i < g.Length; i++) if (!g[i]) return false;
                return true;
            }
        }
        bool JustStood => useKeyboardStub ? _stub.JustStood : _sit.JustStood;
        bool JustSat => useKeyboardStub ? _stub.JustSat : _sit.JustSat;
        // Kick side, gated by the same master flag as PlayerLane below — see invertLateralForBackView's
        // tooltip. When the correction is on, the raw detector's Left/Right are swapped so JustKickedLeft
        // means the player's real left foot kicked, not the raw (possibly camera-mirrored) detector side.
        bool JustKickedLeft => useKeyboardStub ? _stub.JustKickedLeft
            : (invertLateralForBackView ? _sideKick.JustKickedRight : _sideKick.JustKickedLeft);
        bool JustKickedRight => useKeyboardStub ? _stub.JustKickedRight
            : (invertLateralForBackView ? _sideKick.JustKickedLeft : _sideKick.JustKickedRight);
        int PlayerLane => useKeyboardStub
            ? _stub.Lane
            : (invertLateralForBackView ? _lane.Lane : -_lane.Lane);

        void Awake()
        {
            // A tablet has no keyboard: stub mode there would soft-lock the game. Force off.
            if (!Application.isEditor && poseDetector != null) useKeyboardStub = false;

            // Third leg of the master lateral-mirror correction (see invertLateralForBackView's
            // tooltip): keep the avatar's own limb mapping in agreement with PlayerLane/kick-side
            // above. This ONLY touches the detector instance THIS scene's director points at, so
            // other games (their own MediaPipePoseDetector, their own sameSideRetarget default)
            // are unaffected. TheDasherSceneBuilder.ConfigureDetector still seeds an editor-time
            // default, but this runtime push is authoritative from here on.
            // CONFIRMED ON DEVICE (via the live 4-button tuning panel): the correct mapping is
            // SameSideRetarget = FALSE together with scene flipX = 0 and legs at their default
            // (legSideSwap/legFlipX off). One tap of "ARM swap L/R" from the previous (same-side,
            // flipX 0) build fixed both arms AND legs, i.e. the winning combo is !same-side. Lane/kick
            // above keep reading invertLateralForBackView directly, so they stay correct.
            if (poseDetector != null) poseDetector.SameSideRetarget = !invertLateralForBackView;

            // Drive the avatar from the reliable 2D screen-plane keypoints — the SAME data the
            // skeleton overlay draws (which tracks the kick correctly). The metric GHUM world
            // landmarks collapse for the legs when the tablet can't see the lower body well
            // (on-device logcat: visAnkle ~0.1, knee sitting at hip height), so the 3D driver left
            // the avatar's leg unmoved even though detection/scoring were correct. Pinning here beats
            // a stale "3D" PlayerPref left on the device from the in-game gear toggle.
            if (poseDetector != null) poseDetector.PinDriveMode2D();
        }

#if UNITY_EDITOR
        // EDITOR TEST ONLY: called by DasherTestClipSwitcher when a recording is replayed, so the
        // replayed pose is actually SCORED. The editor otherwise runs the keyboard stub (no tablet
        // keyboard on device), which ignores the pose feed — that's why a replayed sit looked
        // undetected. Jumps straight to a calibrated, live-detector Play state.
        public void BeginReplayTest()
        {
            useKeyboardStub = false;
            if (poseDetector != null && poseDetector.HasPose)
            {
                var kp = poseDetector.LatestKeypoints;
                _sideKick.SetBaseline(kp);
                _lane.CalibrateCenter(kp);
            }
            _calibrated = true;
            _bodyLostTimer = 0f;
            if (framingPanel != null) framingPanel.SetActive(false);
            if (calibGroup != null) calibGroup.SetActive(false);
            if (introPanel != null) introPanel.SetActive(false);
            if (_state != State.Play) { _runStarted = true; EnterPlay(); }
        }
#endif

        void Start()
        {
            if (avatarRoot != null) { _avatarBaseX = avatarRoot.position.x; _avatarBaseY = avatarRoot.position.y; }
            _timeLeft = sessionSeconds;
            if (toastText != null) toastText.text = "";
            SetToastBgAlpha(0f); // no message yet — the pill starts invisible
            if (spawner != null)
            {
                spawner.ItemArrived += OnItemArrived;
                spawner.ItemExpired += OnItemExpired;
                _baseFallSeconds = spawner.fallSeconds;
                _baseBeatInterval = spawner.beatInterval;
                _baseTreasureWindow = spawner.treasureWindowSeconds;
                _baseRingWindow = spawner.ringWindowSeconds;
            }
            if (hudPanel != null) hudPanel.SetActive(false);
            if (bodyLostIgnoreButton != null) bodyLostIgnoreButton.gameObject.SetActive(false);
            if (bodyLostToast != null) bodyLostToast.SetActive(false);
            UpdateScoreText();
            UpdateTimerText();

            // Emergency fall-alert overlay (SOS). Lives on this director and watches the pose for a
            // fall; if one fires it counts down and asks Flutter to auto-dial the emergency contact.
            _sos = gameObject.AddComponent<SosController>();
            _sos.poseDetector = poseDetector;

            // DEBUG HOOK: tapping the on-screen clock/timer opens the SOS card immediately, so QA
            // can preview the whole flow without staging a real fall. Remove if this ever needs to
            // ship without a debug trigger.
            if (timerText != null)
            {
                var sosTestBtn = timerText.gameObject.GetComponent<Button>();
                if (sosTestBtn == null) sosTestBtn = timerText.gameObject.AddComponent<Button>();
                sosTestBtn.transition = Selectable.Transition.None;
                sosTestBtn.onClick.AddListener(() => _sos.Show());
            }

            // Run config pushed from Flutter (difficulty picker + tutorial toggle) via SceneRouter's
            // static pending values — read once here (SceneRouter persists across the scene load, and
            // the statics default to normal/tutorial-on so this is safe even when run straight from the
            // editor with no SceneRouter instance).
            _tutorialEnabled = Kinex.App.SceneRouter.PendingTutorial;
            SetDifficulty(ParseDifficulty(Kinex.App.SceneRouter.PendingDifficulty));

            // The start/mission screen (logo, subtitle, instruction cards) now lives in Flutter,
            // shown before Unity is even embedded — so introPanel is retired and every entry into
            // this scene goes straight to Framing instead of waiting on OnStartPressed().
            EnterFraming();

            // Background music for the whole session. Drop your own CC0 track at
            // Assets/Resources/Music/dasher_theme.* to replace it — no code change needed.
            Music.Play("dasher_theme", 0.30f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // SOS fall-detection only runs during actual play — never in framing/countdown/results.
            // (A false fall there would freeze the game via Time.timeScale=0 — e.g. the 3-2-1
            // countdown stuck at 3 while the player is still getting into frame.)
            if (_sos != null) _sos.autoFallDetect = (_state == State.Play && !_manualPaused);
            if (!_manualPaused) TickMotion(dt);

            switch (_state)
            {
                case State.Intro: break;
                case State.Framing: TickFraming(); break;
                case State.Calibrating: TickCalibrating(dt); break;
                case State.Tutorial: TickTutorial(dt); break;
                case State.Countdown: TickCountdown(dt); break;
                case State.Play: if (!_manualPaused) TickPlay(dt); break;
                case State.Results: break;
            }

            TickToast(dt);
            TickBodyLostToast(dt);
        }

        void TickMotion(float dt)
        {
            if (useKeyboardStub) { _stub.Tick(dt); return; }
            bool has = HasLivePose;
            _fullBody.Tick(has, has ? poseDetector.Landmarks33 : null, dt);
            _bodyLostTimer = (has && BodyPresentForPlay) ? 0f : _bodyLostTimer + dt;
            if (!has) return;
            // Gate on BodyPresentForPlay (groups visible) NOT IsFullBodyVisible (standing span) so the
            // detectors keep running while the player is seated — otherwise the sit is never seen.
            if (_calibrated && BodyPresentForPlay)
            {
                var kp = poseDetector.LatestKeypoints;
                var conf = poseDetector.LatestConfidence;
                _sit.Tick(kp, conf, dt);
                _sideKick.Tick(kp, conf, dt);
                _lane.Tick(kp, conf, dt);
            }
        }

        // ---- Framing: guide the player into full-body view (also the safety pause). ----

        void EnterFraming()
        {
            EndSitPose(); // never leave the body frozen in the scripted crouch
            if (_tut != null) _tut.Hide(); // hide the tutorial card while re-framing (resumes on return)
            _state = State.Framing;
            // Hide the 3-2-1 countdown when we (re)enter framing — otherwise, if the body is lost
            // just as the countdown starts, a frozen "3" stays stuck on screen instead of the
            // framing prompts ("get in frame"). Bug: press เริ่มเลย without the full body visible →
            // countdown pops but freezes at 3.
            if (countdownText != null) countdownText.gameObject.SetActive(false);
            if (countdownBadge != null) countdownBadge.SetActive(false);
            if (framingPanel != null) framingPanel.SetActive(true);
            if (calibGroup != null) calibGroup.SetActive(false);
            if (introPanel != null) introPanel.SetActive(false);
            SetPreviewVisible(true); // show the live camera preview from here on (its toggle BUTTON
                                     // is kept hidden in SetPreviewVisible — user wanted the button gone, not the preview)
            if (_runStarted && spawner != null) spawner.SetPaused(true);
            // Default OFF every time framing is entered — the initial pre-Start pass NEVER shows it;
            // HandleBodyLost turns it on right after calling this, ONLY for a mid-game lock.
            if (bodyLostIgnoreButton != null) bodyLostIgnoreButton.gameObject.SetActive(false);
        }

        void TickFraming()
        {
            if (useKeyboardStub) { OnFramingPassed(); return; }

            for (int i = 0; i < framingChips.Length && i < _fullBody.GroupOk.Length; i++)
                if (framingChips[i] != null) framingChips[i].color = _fullBody.GroupOk[i] ? ChipPass : ChipFail;
            if (framingPromptText != null) framingPromptText.text = PromptFor(_fullBody.Why);
            if (framingHoldRing != null)
                framingHoldRing.fillAmount = Mathf.Clamp01(_fullBody.ValidSeconds / FramingHoldSeconds);

            if (_fullBody.ValidSeconds >= FramingHoldSeconds) OnFramingPassed();
        }

        static string PromptFor(FullBodyGate.Reason why) => why switch
        {
            FullBodyGate.Reason.Ok => "ดีมาก! ยืนแบบนี้ค้างไว้",
            FullBodyGate.Reason.NotFound => "ยังไม่เห็นตัวคุณ — มายืนหน้ากล้องได้เลย",
            FullBodyGate.Reason.FeetCut => "ถอยหลังอีกนิด — มองไม่เห็นเท้า",
            FullBodyGate.Reason.HeadCut => "ถอยหลังอีกนิด — มองไม่เห็นศีรษะ",
            FullBodyGate.Reason.TooClose => "ถอยหลังอีกนิด ให้เห็นทั้งตัว",
            FullBodyGate.Reason.TooFar => "เดินเข้ามาใกล้อีกนิด",
            FullBodyGate.Reason.OffCenter => "ขยับมายืนตรงกลางภาพ",
            _ => "มีบางส่วนถูกบัง — ยืนให้เห็นทั้งตัว",
        };

        void OnFramingPassed()
        {
            if (framingPanel != null) framingPanel.SetActive(false);
            Kinex.Sfx.Play("go", 0.6f);
            if (_calibrated) { ProceedAfterCalibration(); return; }
            _state = State.Calibrating;
            _calibStillTimer = 0f;
            _calibAnchorSet = false;
            if (calibGroup != null) calibGroup.SetActive(true);
            if (calibText != null) calibText.text = "ยืนตรง นิ่ง ๆ 2 วินาที";
        }

        // ---- Calibration: stand still 2 s → every detector gets its baseline. ----

        void TickCalibrating(float dt)
        {
            if (useKeyboardStub) { FinishCalibration(); return; }
            if (!HasLivePose || !_fullBody.IsFullBodyVisible) { EnterFraming(); return; }

            var kp = poseDetector.LatestKeypoints;
            Vector2 hipMid = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]);
            if (!_calibAnchorSet || Vector2.Distance(hipMid, _calibAnchor) > CalibMoveTolerance)
            {
                _calibAnchor = hipMid;
                _calibAnchorSet = true;
                _calibStillTimer = 0f;
            }
            else _calibStillTimer += dt;

            if (calibRing != null) calibRing.fillAmount = Mathf.Clamp01(_calibStillTimer / CalibStillSeconds);
            if (_calibStillTimer >= CalibStillSeconds) FinishCalibration();
        }

        void FinishCalibration()
        {
            if (!useKeyboardStub && HasLivePose)
            {
                // Sit/stand is calibration-free now (DasherSitClassifier reads geometry directly),
                // so only the kick + lane detectors still need a standing baseline captured here.
                var kp = poseDetector.LatestKeypoints;
                _sideKick.SetBaseline(kp);
                _lane.CalibrateCenter(kp);
            }
            _calibrated = true;
            if (calibGroup != null) calibGroup.SetActive(false);
            Kinex.Sfx.Play("checkpoint", 0.7f);
            ProceedAfterCalibration();
        }

        // ---- Countdown: 3-2-1 into (or back into) the run. ----

        // ---- Tutorial: teach the 3 core moves in the real scene before the first run. ----

        void ProceedAfterCalibration()
        {
            if (_tutorialEnabled && !_tutorialDone) EnterTutorial();
            else EnterCountdown();
        }

        void EnterTutorial()
        {
            _state = State.Tutorial;
            if (spawner != null)
            {
                spawner.ClearItems(); // fresh scene each entry (also after a body-lost bounce)
                // Gentle, generous fall while teaching so seniors have plenty of time to react.
                // ApplyDifficulty() resets this from _baseFallSeconds when the real run begins.
                spawner.fallSeconds = _baseFallSeconds * 1.6f;
                spawner.SetPaused(false);
            }
            if (_tut == null)
            {
                _tut = gameObject.AddComponent<TutorialController>();
                _tut.OnSkip = FinishTutorial;
            }
            // Give the banner the scene's Thai TMP font (same one the toast pill uses).
            _tut.thaiFont = toastText != null ? toastText.font
                          : (spawner != null ? spawner.thaiFont : null);
            // Chair-diagram + button art (see TutorialController.ShowIntro/BuildChairDiagram).
            _tut.frameSprite = tutorialFrameSprite;
            _tut.headSprite = tutorialHeadSprite;
            _tut.buttonSprite = tutorialButtonSprite;
            _tut.buttonPressedSprite = tutorialButtonPressedSprite;
            _tutSuccessTimer = 0f;
            _tutCueLive = false;
            _tutArm = 0f;
            // First entry: show the "place a chair in the middle of the frame" get-ready modal, and
            // hold the step logic until the user taps เริ่ม. On a body-lost bounce (already shown),
            // resume straight at the current step's card.
            if (!_tutIntroShown)
            {
                _tutIntroShown = true;
                _tutIntroPending = true;
                _tut.ShowIntro(() =>
                {
                    _tutIntroPending = false;
                    _tut.SetStep(_tutStep);
                });
            }
            else
            {
                _tutIntroPending = false;
                _tut.SetStep(_tutStep); // resume at the current step if we bounced back through framing
            }
        }

        void TickTutorial(float dt)
        {
            // While the "place a chair" get-ready modal is up, the player is setting up (and may be
            // out of frame) — don't run gates or bounce on body-loss until they tap เริ่ม.
            if (_tutIntroPending) return;

            // Lose the body → bounce to framing (same as countdown/play) so the tutorial pauses
            // cleanly rather than advancing on a phantom pose; it resumes at the same step.
            if (!useKeyboardStub && _bodyLostTimer > BodyLostSeconds)
            {
                HandleBodyLost();
                if (_state != State.Tutorial) return;
            }

            // Let the avatar visually RESPOND to the player's motion while teaching — the real
            // TickPlay (which slides the avatar between lanes and drives the sit crouch) isn't
            // running yet, so without this the character stays frozen when the player steps aside,
            // making the dodge feel broken. Reported: "when I move, the character doesn't change lane".
            TickTutorialAvatar(dt);

            // Brief "สำเร็จ" hold, then move to the next step.
            if (_tutSuccessTimer > 0f)
            {
                _tutSuccessTimer -= dt;
                if (_tutSuccessTimer <= 0f)
                {
                    _tutStep++;
                    if (_tutStep >= 3) { FinishTutorial(); return; }
                    _tutCueLive = false;
                    _tutArm = 0f;
                    _tut.SetStep(_tutStep);
                }
                return;
            }

            // Keep the REAL prop for this step falling in the scene so the player learns what a
            // meteor / treasure / kick-ring actually looks like. When the previous one lands or
            // expires WITHOUT the pose being done, that's a miss → gentle "ลองอีกครั้ง" + respawn.
            if (spawner != null && spawner.ActiveCount == 0)
            {
                if (_tutCueLive)
                {
                    ShowToast("ยังไม่ทันนะ ลองอีกครั้ง!", ToastOrange);
                    _tut.ShowRetry(); // big red "ลองอีกครั้ง!" flash so the miss is unmistakable
                    Kinex.Sfx.Play("error", 0.5f);
                }
                TutorialSpawnCurrentStep();
                _tutCueLive = true;
                _tutArm = 0f;
            }
            _tutArm += dt;

            // Only accept the pose AFTER the cue has been up a moment: stops a stray/jittery reading
            // (or an off-centre stance) from instantly "passing" before the player has actually done
            // anything, and gives seniors a beat to read the card first.
            bool armed = _tutArm > 0.5f;
            bool done = armed && _tutStep switch
            {
                // Dodge: the meteor targets the player's OWN lane — success = actually stepping OFF it.
                0 => PlayerLane != _tutCueLane,
                // Sit: a genuine sit-down.
                1 => JustSat,
                // Kick: must kick the leg on the RING'S side (a wrong-leg kick no longer counts).
                _ => _tutCueLane > 0 ? JustKickedRight : JustKickedLeft,
            };
            if (done)
            {
                Kinex.Sfx.Play("coin", 0.7f);
                ShowToast("สำเร็จ! เก่งมาก", ToastGold);
                _tut.ShowSuccess();
                _tutSuccessTimer = 0.9f;
                _tutCueLive = false;
                if (spawner != null) spawner.ClearItems(); // clear the cue; success flash → next step
            }
        }

        // Drive the avatar's VISUALS during the tutorial so the player's move visibly lands
        // (TickPlay isn't running yet). Slides to the stepped lane + leans, and crouches on a sit.
        // Counts accrued here (laneSteps etc.) are discarded when the real run starts (EnterPlay).
        void TickTutorialAvatar(float dt)
        {
            if (JustSat) _sitHeld = true;
            if (JustStood) _sitHeld = false;
            TickLaneMovement(dt); // lateral slide + lean + sit dip + lane-dot highlight
            TickSitCrouch();      // scripted knee bend while the sit is held
        }

        // The real prop for the current tutorial step, recording its lane in _tutCueLane.
        // Step 0 drops a meteor in the player's CURRENT lane (so "step aside" is a real dodge);
        // step 1 a treasure ahead to sit for; step 2 a kick-ring on the RIGHT to kick toward.
        void TutorialSpawnCurrentStep()
        {
            switch (_tutStep)
            {
                case 0:
                    _tutCueLane = PlayerLane;
                    spawner.SpawnOne(DasherKind.Meteor, _tutCueLane);
                    break;
                case 1:
                    _tutCueLane = 0;
                    spawner.SpawnOne(DasherKind.Treasure, 0);
                    break;
                default:
                    _tutCueLane = 1;
                    spawner.SpawnOne(DasherKind.KickRing, 1);
                    break;
            }
        }

        void FinishTutorial()
        {
            _tutorialDone = true;
            if (_tut != null) _tut.Hide();
            if (spawner != null) spawner.ClearItems(); // clear any teaching prop before the real run
            EnterCountdown(); // straight into the real game (3-2-1 → play)
        }

        void EnterCountdown()
        {
            _state = State.Countdown;
            _countdownLeft = CountdownSeconds;
            if (hudPanel != null) hudPanel.SetActive(true);
            if (countdownBadge != null) countdownBadge.SetActive(true);
            if (countdownText != null) countdownText.gameObject.SetActive(true);
        }

        void TickCountdown(float dt)
        {
            // Losing the body during the countdown bounces back to framing (seated span-shrink is
            // tolerated — only a genuinely missing joint group counts as lost). Once the player has
            // pressed Ignore, HandleBodyLost toasts instead and leaves the countdown running.
            if (!useKeyboardStub && _bodyLostTimer > BodyLostSeconds)
            {
                HandleBodyLost();
                if (_state != State.Countdown) return; // bounced to Framing
            }

            _countdownLeft -= dt;
            if (countdownText != null)
                countdownText.text = _countdownLeft > 0f ? Mathf.Ceil(_countdownLeft).ToString("0") : "ไป!";
            if (_countdownLeft <= -0.5f)
            {
                if (countdownText != null) countdownText.gameObject.SetActive(false);
                if (countdownBadge != null) countdownBadge.SetActive(false);
                EnterPlay();
            }
        }

        void EnterPlay()
        {
            _state = State.Play;
            if (!_runStarted)
            {
                _runStarted = true;
                // Discard anything the visuals accrued while teaching (lane steps, a stray sit) so
                // the real run starts from a clean zero.
                _result = new DasherResult();
                _lastLane = 0;
                _satThisRep = false;
                UpdateScoreText();
                ApplyDifficulty(); // tune the spawner BEFORE the deck starts (Easy/Normal/Hard)
                int seed = deckSeed != 0 ? deckSeed : UnityEngine.Random.Range(1, int.MaxValue);
                if (spawner != null) spawner.BeginRun(DasherLogic.BuildDeck(seed));
            }
            else if (spawner != null) spawner.SetPaused(false);
        }

        // ---- Difficulty: chosen on the intro screen, applied once at the true start of the run. ----

        /// <summary>Safe to call anytime (e.g. from the intro screen's Easy/Normal/Hard buttons);
        /// only takes effect on the spawner when the run actually begins (EnterPlay).</summary>
        public void SetDifficulty(DasherDifficulty d) => _difficulty = d;
        public DasherDifficulty CurrentDifficulty => _difficulty;
        static DasherDifficulty ParseDifficulty(string s) => s switch
        {
            "easy" => DasherDifficulty.Easy,
            "hard" => DasherDifficulty.Hard,
            _ => DasherDifficulty.Normal,
        };

        // Difficulty → spawner tuning. Scales the DESIGNER'S OWN Inspector numbers (captured in
        // Start) rather than hard-coding duplicates, so Normal is always exactly "whatever the
        // spawner is set to" and Easy/Hard stay proportionally more/less forgiving if those base
        // numbers are ever retuned. Easy = slower falls, more space between beats, more time to
        // react/claim/kick. Hard = the opposite. A small explicit table — no new manager class.
        void ApplyDifficulty()
        {
            if (spawner == null) return;
            float fallMul, beatMul, windowMul, multiMeteor;
            switch (_difficulty)
            {
                case DasherDifficulty.Easy: fallMul = 1.3f; beatMul = 1.05f; windowMul = 1.3f; multiMeteor = 0f; break;
                case DasherDifficulty.Hard: fallMul = 0.85f; beatMul = 0.55f; windowMul = 0.7f; multiMeteor = 0.65f; break;
                // Normal keeps the harder CADENCE (beats close together + frequent meteor storms so
                // the player must step to a safe lane) but the fall SPEED is eased a touch (fallMul
                // 0.88→1.0) — objects drop a little more gently everywhere per Pokpong's feedback.
                default /* Normal */:      fallMul = 1.0f;  beatMul = 0.6f;  windowMul = 0.88f; multiMeteor = 0.45f; break;
            }
            spawner.fallSeconds = _baseFallSeconds * fallMul;
            spawner.beatInterval = _baseBeatInterval * beatMul;
            spawner.treasureWindowSeconds = _baseTreasureWindow * windowMul;
            spawner.ringWindowSeconds = _baseRingWindow * windowMul;
            spawner.multiMeteorChance = multiMeteor;
        }

        // ---- Play: the 3-minute run. ----

        void TickPlay(float dt)
        {
            // Same ignore-aware handling as TickCountdown — see HandleBodyLost.
            if (!useKeyboardStub && _bodyLostTimer > BodyLostSeconds)
            {
                HandleBodyLost();
                if (_state != State.Play) return; // bounced to Framing
            }

            _timeLeft -= dt;
            UpdateTimerText();
            if (_timeLeft <= 0f) { EndRun(); return; }

            TickLaneMovement(dt);
            TickSitStand();
            TickSitCrouch();
            TickKicks();
        }

        void TickLaneMovement(float dt)
        {
            // Sitting locks the avatar to the MIDDLE lane: treasures only ever land center (the
            // player sits in a chair fixed at the middle lane), so a held sit means "I'm sitting in
            // the middle to grab the treasure" regardless of any lane drift the detector reports.
            int lane = _sitHeld ? 0 : PlayerLane;
            if (lane != _lastLane)
            {
                _result.laneSteps++;
                _lastLane = lane;
                Kinex.Sfx.Play("pop", 0.3f);
            }
            for (int i = 0; i < laneDots.Length; i++)
                if (laneDots[i] != null) laneDots[i].color = (i - 1) == lane ? LaneDotOn : LaneDotOff;

            if (avatarRoot == null || spawner == null) return;
            float targetX = _avatarBaseX + spawner.LaneWorldX(lane) * avatarLaneMoveScale;
            Vector3 p = avatarRoot.position;
            float newX = Mathf.Lerp(p.x, targetX, 1f - Mathf.Exp(-laneLerpSpeed * dt));
            float vx = (newX - p.x) / Mathf.Max(dt, 1e-4f);
            p.x = newX;
            // Sit crouch: ease the hips DOWN while the sit is held (the scripted knee bend in
            // TickSitCrouch bends the legs so the feet stay planted), then rise on standing.
            _sitDip01 = Mathf.MoveTowards(_sitDip01, _sitHeld ? 1f : 0f, dt / Mathf.Max(sitDipSeconds, 0.01f));
            p.y = _avatarBaseY - _sitDip01 * sitDipMeters;
            avatarRoot.position = p;
            // Lean into the step, ease back upright when settled.
            float lean = Mathf.Clamp(-vx / 2.5f, -1f, 1f) * leanDegrees;
            avatarRoot.localRotation = Quaternion.Slerp(avatarRoot.localRotation,
                Quaternion.Euler(0f, 0f, lean), 1f - Mathf.Exp(-6f * dt));
        }

        void TickSitStand()
        {
            if (JustSat)
            {
                _satThisRep = true;
                _sitHeld = true; // drives the visible crouch until the next stand
                // Sit down over a treasure that landed in this lane: grab the tool now. The actual
                // pickup happens once the crouch bottoms out (below) so the character is visibly
                // seated and reaching the floor before the treasure is claimed.
                var treasure = spawner != null ? spawner.ActiveItem(DasherKind.Treasure, 0) : null;
                if (treasure != null && _tool == null)
                {
                    AttachTool();
                    _claimPending = true;
                    ShowToast("นั่งลงหยิบสมบัติ!", ToastCyan);
                }
            }

            // Claim at the bottom of the sit — the character has now reached the treasure on the
            // floor. Collect it while STILL SEATED; standing back up just drops the tool.
            if (_claimPending && _sitDip01 >= 0.85f)
            {
                _claimPending = false;
                var treasure = spawner != null ? spawner.ActiveItem(DasherKind.Treasure, 0) : null;
                if (treasure != null)
                {
                    treasure.Collect(avatarRoot);
                    _result.treasures++;
                    _result.score++;
                    UpdateScoreText();
                    ShowScorePop(+1);
                    ShowToast("+1 เก็บสมบัติ!", ToastGold);
                    Kinex.Sfx.Play("coin", 0.8f);
                }
            }

            if (JustStood)
            {
                _sitHeld = false; // stood up → release the crouch (avatar eases back upright)
                _claimPending = false;
                // A real rep always has a preceding sit. SitStandDetector boots in Seated with a
                // standing baseline, so it emits one synthetic JustStood shortly after calibration
                // — which lands during the Countdown (stands ignored there) OR, if the body wasn't
                // visible then, on the first Play frame. Gating on _satThisRep discards that phantom
                // stand without ever eating a genuine sit→stand (the earlier boot-swallow flag ate
                // the first real rep when the synthetic one fell in the countdown).
                if (!_satThisRep) return;
                _satThisRep = false;
                _result.sitStands++;
                if (_tool != null) DropTool(); // stood up → let the tool fall back to the ground
            }
        }

        // Scripted sit crouch. The live puppet drives the bones in WORLD space, which cancels any
        // rotation we add on the root — so to actually bend the knees we hand the body over to the
        // humanoid MUSCLE system while the sit is held: hips flex, knees bend, a touch of forward
        // spine = a real squat to pick up the tool. Blends in/out with _sitDip01 (which also lowers
        // the hips so the bent-knee feet stay planted), then returns the body to the live puppet.
        void TickSitCrouch()
        {
            if (poseDetector == null) return;
            var anim = poseDetector.AvatarAnimator;
            if (anim == null || !anim.isHuman) return;
            if (_poseHandler == null)
            {
                _poseHandler = new HumanPoseHandler(anim.avatar, anim.transform);
                BuildCrouchMuscles();
            }
            if (_crouchPose.muscles == null) _poseHandler.GetHumanPose(ref _crouchPose);

            if (_sitDip01 > 0.01f)
            {
                if (!_crouchPoseActive) { poseDetector.AvatarDriving = false; _crouchPoseActive = true; }
                // _crouchPose holds the last STANDING capture (natural arms) — override only the
                // legs + spine toward the seated pose, blended by the dip so it eases in/out.
                for (int i = 0; i < _crouchMuscle.Length; i++)
                    if (_crouchMuscle[i] >= 0)
                        _crouchPose.muscles[_crouchMuscle[i]] = _crouchTarget[i] * _sitDip01;
                _poseHandler.SetHumanPose(ref _crouchPose);
            }
            else
            {
                EndSitPose();
                // While the puppet drives (standing), keep capturing its pose as the base so the
                // next sit inherits the player's real arms/torso instead of a stiff T-pose. This
                // used to run every single frame — GetHumanPose solves the whole ~90-muscle
                // humanoid rig, a real cost paid continuously even though the player is standing
                // (not sitting) for nearly all of a 3-minute run. Throttled to every 4th frame
                // (~15 Hz at 60 fps): still fresh enough that a sit starting mid-throttle picks up
                // an arm pose at most ~4 frames old — visually identical, 4x cheaper.
                _standingCaptureCounter++;
                if (_standingCaptureCounter >= 4)
                {
                    _standingCaptureCounter = 0;
                    _poseHandler.GetHumanPose(ref _crouchPose);
                }
            }
        }

        void BuildCrouchMuscles()
        {
            var names = HumanTrait.MuscleName;
            System.Func<string, int> mi = s => { for (int i = 0; i < names.Length; i++) if (names[i] == s) return i; return -1; };
            // A deep นั่งยองๆ squat to pick the treasure off the floor (behind-view): thighs fold
            // well forward + a strong knee bend so the character drops onto its haunches, with a
            // clear forward spine so it reads as reaching down. Paired with a bigger sitDipMeters
            // hip drop so the lowered body keeps the feet near the ground.
            var map = new (string name, float val)[]
            {
                ("Left Upper Leg Front-Back", 0.85f), ("Right Upper Leg Front-Back", 0.85f),
                ("Left Lower Leg Stretch", -0.7f),    ("Right Lower Leg Stretch", -0.7f),
                ("Spine Front-Back", 0.22f),
            };
            _crouchMuscle = new int[map.Length];
            _crouchTarget = new float[map.Length];
            for (int i = 0; i < map.Length; i++) { _crouchMuscle[i] = mi(map[i].name); _crouchTarget[i] = map[i].val; }
        }

        // Hand the body back to the live puppet (called when the crouch fully eases out, or when
        // play is left mid-sit so the avatar never freezes in the crouch).
        void EndSitPose()
        {
            if (!_crouchPoseActive) return;
            if (poseDetector != null) poseDetector.AvatarDriving = true;
            _crouchPoseActive = false;
        }

        void OnDestroy()
        {
            Music.Stop();
            _poseHandler?.Dispose();
        }

        void TickKicks()
        {
            bool left = JustKickedLeft;
            bool right = JustKickedRight;
            if (!left && !right) return;
            _result.kickReps++; // every side-kick motion counts toward the rehab dose, hit or not

            var ring = spawner != null ? spawner.ActiveItem(DasherKind.KickRing) : null;
            if (ring == null) return;
            if (DasherLogic.KickValid(PlayerLane, ring.Lane, left))
            {
                ring.Kicked(PlayerLane);
                _result.kicks++;
                _result.score++;
                UpdateScoreText();
                ShowScorePop(+1);
                ShowToast("เตะโดน! +1", ToastCyan);
                Kinex.Sfx.Play("whoosh", 0.7f);
            }
        }

        void OnItemArrived(DasherItem item)
        {
            if (_state != State.Play) return;
            switch (item.Kind)
            {
                case DasherKind.Meteor:
                    if (item.Lane == PlayerLane)
                    {
                        _result.meteorHits++;
                        _result.score--;
                        UpdateScoreText();
                        ShowScorePop(-1);
                        ShowFeedback(feedbackDanger); // picture popup "ระวัง!" (the −1 shows via ShowScorePop)
                        Kinex.Sfx.Play("hit_1", 0.8f);
                        if (shakeTarget != null) StartCoroutine(ShakeRoutine());
                        // Contact feedback: debris burst at the player + a red damage flash.
                        if (avatarRoot != null)
                        {
                            KinexFx.PopBurst(avatarRoot.position + Vector3.up * 1.0f, new Color(1f, 0.30f, 0.22f), 28);
                            KinexFx.PopBurst(avatarRoot.position + Vector3.up * 0.6f, new Color(0.35f, 0.35f, 0.4f), 16);
                        }
                        StartCoroutine(DamageFlash());
                    }
                    else
                    {
                        _result.dodges++;
                        ShowFeedback(feedbackSafe); // picture popup "ปลอดภัย!"
                        Kinex.Sfx.Play("dodge", 0.5f);
                    }
                    break;

                case DasherKind.KickRing:
                    if (DasherLogic.KickFoul(PlayerLane, item.Lane))
                    {
                        // Standing in the ring's own lane at arrival is the terminal outcome:
                        // take the −1 and consume the ring so it can't also be kicked for +1.
                        item.Fouled = true;
                        _result.kickFouls++;
                        _result.score--;
                        UpdateScoreText();
                        ShowScorePop(-1);
                        ShowToast("−1 ยืนทับวงเตะ! ขยับไปเลนข้าง ๆ", ToastOrange);
                        Kinex.Sfx.Play("hit_2", 0.6f);
                        item.FadeAway();
                    }
                    break;
            }
        }

        void OnItemExpired(DasherItem item)
        {
            // No penalty — a missed treasure/ring just leaves. Drop the grabbed tool only when the
            // treasure that expired is the one in the player's current lane: two treasures can be
            // Active at once in different lanes (beat 6.2s < fall 4s + window 7s), and a treasure
            // leaving a lane you're NOT working shouldn't knock the tool out of your hand.
            if (item.Kind == DasherKind.Treasure && _tool != null && item.Lane == PlayerLane) DropTool();
        }

        // Cached runtime Vignette from the global post-processing volume, pulsed red on a hit.
        UnityEngine.Rendering.Universal.Vignette _dmgVig;
        float _vigBaseIntensity;
        Color _vigBaseColor;
        bool _dmgVigResolved;

        // Brief red vignette pulse when a meteor hits the player — a screen-space "damage" cue
        // that reads clearly even in peripheral vision. Uses the volume's runtime profile copy,
        // so the TheDasherPost asset on disk is never modified.
        System.Collections.IEnumerator DamageFlash()
        {
            if (!_dmgVigResolved)
            {
                _dmgVigResolved = true;
                var vols = UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(UnityEngine.FindObjectsSortMode.None);
                foreach (var v in vols)
                {
                    if (v.isGlobal && v.profile != null &&
                        v.profile.TryGet(out UnityEngine.Rendering.Universal.Vignette vig))
                    {
                        _dmgVig = vig;
                        _vigBaseIntensity = vig.intensity.value;
                        _vigBaseColor = vig.color.value;
                        break;
                    }
                }
            }
            if (_dmgVig == null) yield break;

            var damageColor = new Color(0.75f, 0.05f, 0.05f);
            const float dur = 0.45f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = 1f - (t / dur); // 1 → 0
                _dmgVig.intensity.value = Mathf.Lerp(_vigBaseIntensity, 0.5f, k);
                _dmgVig.color.value = Color.Lerp(_vigBaseColor, damageColor, k);
                yield return null;
            }
            _dmgVig.intensity.value = _vigBaseIntensity;
            _dmgVig.color.value = _vigBaseColor;
        }

        System.Collections.IEnumerator ShakeRoutine()
        {
            Vector3 basePos = shakeTarget.localPosition;
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                float amp = 0.06f * (1f - t / 0.4f);
                shakeTarget.localPosition = basePos + (Vector3)(UnityEngine.Random.insideUnitCircle * amp);
                yield return null;
            }
            shakeTarget.localPosition = basePos;
        }

        // ---- Grabber tool: attaches to the right hand on sit, drops to the floor on stand. ----

        void AttachTool()
        {
            var hand = avatarAnimator != null ? avatarAnimator.GetBoneTransform(HumanBodyBones.RightHand) : avatarRoot;
            _tool = DasherProps.GrabberTool();
            _tool.transform.SetParent(hand != null ? hand : transform, false);
            _tool.transform.localPosition = new Vector3(0f, 0.05f, 0.02f);
            _tool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        void DropTool()
        {
            var tool = _tool;
            _tool = null;
            if (tool == null) return;
            tool.transform.SetParent(null, true);
            Vector3 p0 = tool.transform.position;
            float floorDrop = Mathf.Max(0.1f, p0.y);
            Kinex.FX.SimpleTween.Float(0f, 1f, 0.5f, k =>
            {
                if (tool == null) return;
                tool.transform.position = p0 + Vector3.down * (k * k * floorDrop);
                tool.transform.Rotate(Vector3.right, 240f * Time.deltaTime);
            }, this);
            Destroy(tool, 1.6f);
        }

        // ---- End of run → results. ----

        void EndRun()
        {
            EndSitPose();
            _state = State.Results;
            if (spawner != null) spawner.StopRun();
            if (_tool != null) DropTool();

            _result.stars = DasherLogic.Stars(_result.score);
            _result.durationSeconds = sessionSeconds;

            if (resultScoreText != null) resultScoreText.text = DasherLogic.DisplayScore(_result.score).ToString();
            if (resultPraiseText != null) resultPraiseText.text = PraiseFor(_result.stars);
            for (int i = 0; i < resultStars.Length; i++)
                if (resultStars[i] != null) resultStars[i].color = i < _result.stars ? StarLit : StarDim;
            int[] reps = { _result.treasures, _result.kicks, _result.dodges, _result.sitStands, _result.laneSteps, _result.kickReps };
            for (int i = 0; i < resultRepValues.Length && i < reps.Length; i++)
                if (resultRepValues[i] != null) resultRepValues[i].text = reps[i].ToString();

            ShowOnly(resultsPanel);
            if (hudPanel != null) hudPanel.SetActive(false);
            // The bottom-left camera preview would sit on top of the results card/buttons — the
            // run is over, so retire it (and its toggle) for a clean results screen.
            if (cameraPreviewPanel != null) cameraPreviewPanel.SetActive(false);
            if (previewToggleButton != null) previewToggleButton.SetActive(false);
            Kinex.Sfx.Play("fanfare", 0.8f);
            OnSessionComplete?.Invoke(_result);
        }

        // Encouraging headline shown on the results card, tuned to how well the run went.
        static string PraiseFor(int stars) => stars switch
        {
            3 => "ทำได้ดีมาก!",
            2 => "เก่งมากเลย!",
            1 => "ดีขึ้นเรื่อย ๆ นะ!",
            _ => "สู้ ๆ นะ ครั้งหน้าทำได้!",
        };

        // ---- Body-loss warning: first mid-game loss locks (with Ignore offered); after Ignore,
        //      later losses just toast and keep playing. See OnBodyLostIgnorePressed. ----

        void HandleBodyLost()
        {
            if (!_bodyWarnIgnored)
            {
                // First mid-game body-loss (or the player never pressed Ignore): the existing
                // safety lock, but with the Ignore button visible this time.
                EnterFraming();
                if (bodyLostIgnoreButton != null) bodyLostIgnoreButton.gameObject.SetActive(true);
                return;
            }

            // Already ignored once this session: don't bounce back to Framing again — a brief,
            // throttled toast instead (won't re-trigger while one is still showing), and keep playing.
            if (_bodyLostToastTimer <= 0f)
            {
                _bodyLostToastTimer = BodyLostToastSeconds;
                if (bodyLostToast != null) bodyLostToast.SetActive(true);
            }
        }

        void TickBodyLostToast(float dt)
        {
            if (_bodyLostToastTimer <= 0f) return;
            _bodyLostToastTimer -= dt;
            if (_bodyLostToastTimer <= 0f && bodyLostToast != null) bodyLostToast.SetActive(false);
        }

        // ---- HUD helpers. ----

        void UpdateScoreText()
        {
            if (scoreText != null) scoreText.text = DasherLogic.DisplayScore(_result.score).ToString();
        }

        void UpdateTimerText()
        {
            float t = Mathf.Max(0f, _timeLeft);
            if (timerText != null) timerText.text = $"{(int)(t / 60f)}:{(int)(t % 60f):00}";
            if (timerRing != null) timerRing.fillAmount = sessionSeconds > 0f ? t / sessionSeconds : 0f;
        }

        void ShowToast(string msg, Color color)
        {
            if (toastText == null) return;
            toastText.text = msg;
            toastText.color = color;
            _toastTimer = ToastSeconds;
        }

        void TickToast(float dt)
        {
            if (toastText == null || _toastTimer <= 0f) return;
            _toastTimer -= dt;
            float t = Mathf.Clamp01(_toastTimer / ToastSeconds);
            float a = Mathf.SmoothStep(0f, 1f, t);
            var c = toastText.color;
            c.a = a;
            toastText.color = c;
            toastText.transform.localScale = Vector3.one * (1f + 0.12f * Mathf.SmoothStep(0f, 1f, t) * t);
            SetToastBgAlpha(a); // the pill rides the text's fade, so it only shows during a message
            if (_toastTimer <= 0f) toastText.text = "";
        }

        // The toast pill is a plain Image the director never enables/disables — it lives at alpha 0 and
        // is faded in only while a message is up. Null when the UI is text-only (no toast sprite).
        void SetToastBgAlpha(float a)
        {
            if (toastBg == null) return;
            var c = toastBg.color;
            c.a = a;
            toastBg.color = c;
        }

        // Small floating "+1"/"−1" card (scorechange.png) — separate from ShowToast's text message,
        // this is the numeric badge. Pops in, holds, fades out (~0.8s total). Re-triggering while
        // one is already showing kills the old animation and restarts clean rather than stacking.
        void ShowScorePop(int delta)
        {
            if (scoreChangePopup == null || scoreChangeText == null) return;
            if (_scoreChangeRoutine != null) StopCoroutine(_scoreChangeRoutine);

            scoreChangeText.text = (delta > 0 ? "+" : "−") + Mathf.Abs(delta);
            var tint = delta > 0 ? ToastGold : ToastOrange;
            scoreChangeText.color = tint;
            SetScorePopAlpha(1f);
            scoreChangePopup.gameObject.SetActive(true);
            _scoreChangeRoutine = StartCoroutine(ScoreChangePopRoutine());
        }

        void SetScorePopAlpha(float a)
        {
            if (scoreChangePopup != null)
            {
                var c = scoreChangePopup.color;
                c.a = a;
                scoreChangePopup.color = c;
            }
            if (scoreChangeText != null)
            {
                var c = scoreChangeText.color;
                c.a = a;
                scoreChangeText.color = c;
            }
        }

        System.Collections.IEnumerator ScoreChangePopRoutine()
        {
            var t = scoreChangePopup.transform;
            const float popTime = 0.15f, settleTime = 0.08f, holdTime = 0.35f, fadeTime = 0.3f;

            float time = 0f;
            while (time < popTime)
            {
                time += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / popTime));
                t.localScale = Vector3.LerpUnclamped(Vector3.one * 0.6f, Vector3.one * 1.08f, k);
                yield return null;
            }
            time = 0f;
            while (time < settleTime)
            {
                time += Time.deltaTime;
                t.localScale = Vector3.Lerp(Vector3.one * 1.08f, Vector3.one, Mathf.Clamp01(time / settleTime));
                yield return null;
            }
            t.localScale = Vector3.one;

            yield return new WaitForSeconds(holdTime);

            time = 0f;
            while (time < fadeTime)
            {
                time += Time.deltaTime;
                SetScorePopAlpha(1f - Mathf.Clamp01(time / fadeTime));
                yield return null;
            }
            scoreChangePopup.gameObject.SetActive(false);
            _scoreChangeRoutine = null;
        }

        // Picture feedback (ปลอดภัย! / ระวัง!) — pops the given popup in for ~1.2s. Only one shows at a
        // time; a new call replaces the current one.
        void ShowFeedback(Image which)
        {
            if (which == null) return;
            if (_feedbackRoutine != null) StopCoroutine(_feedbackRoutine);
            if (feedbackSafe != null) feedbackSafe.gameObject.SetActive(false);
            if (feedbackDanger != null) feedbackDanger.gameObject.SetActive(false);
            _feedbackRoutine = StartCoroutine(FeedbackRoutine(which));
        }

        System.Collections.IEnumerator FeedbackRoutine(Image which)
        {
            which.gameObject.SetActive(true);
            var t = which.transform;
            float time = 0f;
            const float popTime = 0.14f, holdTime = 0.9f;
            while (time < popTime)
            {
                time += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / popTime));
                t.localScale = Vector3.LerpUnclamped(Vector3.one * 0.6f, Vector3.one, k);
                yield return null;
            }
            t.localScale = Vector3.one;
            yield return new WaitForSeconds(holdTime);
            which.gameObject.SetActive(false);
            _feedbackRoutine = null;
        }

        void ShowOnly(GameObject panel)
        {
            if (introPanel != null) introPanel.SetActive(panel == introPanel);
            if (framingPanel != null) framingPanel.SetActive(panel == framingPanel);
            if (resultsPanel != null) resultsPanel.SetActive(panel == resultsPanel);
            if (pausePanel != null) pausePanel.SetActive(panel == pausePanel);
        }

        // ---- Buttons (wired by TheDasherUIBuilder). ----

        public void OnStartPressed()
        {
            if (_state != State.Intro) return;
            EnterFraming();
        }

        /// <summary>Framing-screen "เริ่มเลย" (start anyway). The full-body gate is a GUIDE, not a
        /// hard requirement (user request: don't trap me if the camera can't see all of me). Capture
        /// a best-effort baseline from whatever pose is visible right now and go straight into the
        /// countdown — detection self-corrects from there. Safe if no pose yet (baseline is skipped).</summary>
        public void OnFramingSkipPressed()
        {
            if (_state != State.Framing) return;
            if (framingPanel != null) framingPanel.SetActive(false);
            if (calibGroup != null) calibGroup.SetActive(false);
            if (bodyLostIgnoreButton != null) bodyLostIgnoreButton.gameObject.SetActive(false);
            if (!useKeyboardStub && HasLivePose)
            {
                var kp = poseDetector.LatestKeypoints;
                _sideKick.SetBaseline(kp);
                _lane.CalibrateCenter(kp);
            }
            _calibrated = true;
            Kinex.Sfx.Play("go", 0.6f);
            EnterCountdown();
        }

        /// <summary>Wired to bodyLostIgnoreButton.onClick. Opts out of future body-loss LOCKS for
        /// the rest of this session (later losses just toast — see HandleBodyLost) and immediately
        /// resumes play, matching the contract's "hides the lock, resumes play".</summary>
        public void OnBodyLostIgnorePressed()
        {
            _bodyWarnIgnored = true;
            if (bodyLostIgnoreButton != null) bodyLostIgnoreButton.gameObject.SetActive(false);
            _bodyLostTimer = 0f; // don't immediately re-trigger this same frame
            if (framingPanel != null) framingPanel.SetActive(false);
            if (calibGroup != null) calibGroup.SetActive(false);
            EnterPlay(); // _runStarted is already true here, so this just unpauses + resumes Play
        }

        public void OnPausePressed()
        {
            if (_state != State.Play || _manualPaused) return;
            _manualPaused = true;
            if (spawner != null) spawner.SetPaused(true);
            if (pausePanel != null) pausePanel.SetActive(true);
        }

        public void OnResumePressed()
        {
            if (!_manualPaused) return;
            _manualPaused = false;
            if (pausePanel != null) pausePanel.SetActive(false);
            if (spawner != null) spawner.SetPaused(false);
        }

        /// <summary>Wired to the pause panel's "จบเกม" button — ends the run early and jumps straight
        /// to the results screen (EndRun tallies the score/stars from whatever was collected so far).</summary>
        public void OnEndGamePressed()
        {
            if (_state != State.Play) return;
            _manualPaused = false;
            if (spawner != null) spawner.SetPaused(false);
            if (pausePanel != null) pausePanel.SetActive(false);
            EndRun();
        }

        public void OnTogglePreviewPressed()
        {
            if (cameraPreviewPanel != null) cameraPreviewPanel.SetActive(!cameraPreviewPanel.activeSelf);
        }

        // Show/hide the camera preview + its toggle WITHOUT deactivating the panel: the detector binds
        // the feed's RawImage via FindAnyObjectByType (inactive objects are skipped), so the panel must
        // stay active for that bind to happen. A CanvasGroup hides it visually instead. The toggle
        // button plays no part in the bind, so it can be deactivated outright.
        void SetPreviewVisible(bool show)
        {
            if (cameraPreviewPanel != null)
            {
                var cg = cameraPreviewPanel.GetComponent<CanvasGroup>();
                if (cg == null) cg = cameraPreviewPanel.AddComponent<CanvasGroup>();
                cg.alpha = show ? 1f : 0f;
                cg.blocksRaycasts = show;
            }
            // Toggle button removed per user request — the camera preview stays, but its on-screen
            // "debug pose" toggle is never shown.
            if (previewToggleButton != null) previewToggleButton.SetActive(false);
        }

        public void OnPlayAgainPressed()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
        }

        public void OnExitPressed()
        {
            OnExitRequested?.Invoke();
            SendToFlutter.Send("{\"type\":\"exit\"}");
        }

        // TEMP retarget-tuning panel (gated by showRetargetDebug). Four big buttons to flip the
        // avatar's arm/leg side + direction LIVE on device, so the correct mapping is found in one
        // build. Read the top label for the final combo, then bake it and turn showRetargetDebug off.
        void OnGUI()
        {
            if (!showRetargetDebug || poseDetector == null) return;
            GUI.skin.button.fontSize = 32;
            GUI.skin.label.fontSize = 30;
            float w = 540f, h = 92f, x = 30f, y = 240f, gap = 14f;
            GUI.color = Color.yellow;
            GUI.Label(new Rect(x, y, w, 74f),
                $"ARM side:{(poseDetector.SameSideRetarget ? "same" : "cross")}  dir:{(poseDetector.FlipX ? "flip" : "norm")}\n" +
                $"LEG side:{(poseDetector.LegSideSwap ? "swap" : "follow")}  dir:{(poseDetector.LegFlipX ? "flip" : "norm")}");
            GUI.color = Color.white;
            y += 84f;
            if (GUI.Button(new Rect(x, y, w, h), "1) ARM  swap L/R")) poseDetector.ToggleSameSide();
            y += h + gap;
            if (GUI.Button(new Rect(x, y, w, h), "2) ARM  flip direction")) poseDetector.ToggleMirror();
            y += h + gap;
            if (GUI.Button(new Rect(x, y, w, h), "3) LEG  swap L/R")) poseDetector.ToggleLegSideSwap();
            y += h + gap;
            if (GUI.Button(new Rect(x, y, w, h), "4) LEG  flip direction")) poseDetector.ToggleLegFlipX();
        }
    }
}
