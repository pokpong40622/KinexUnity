using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Kinex.Motion;

namespace Kinex.AstroStance
{
    /// <summary>
    /// ASTROSTANCE — behind-the-character 3-lane space game, 3 minutes, camera-controlled.
    /// Side-step (p15) changes lane / dodges meteors, sit-to-stand (p7) grabs a tool and
    /// claims landed treasure, side-kick (p9) pops kick rings from an ADJACENT lane.
    /// Flow: Intro → Framing (FullBodyGate, Thai prompts) → stand-still Calibration →
    /// 3-2-1 Countdown → Play (fixed 28-beat deck) → Results. Losing the body mid-run
    /// safety-pauses back into Framing and resumes through the countdown.
    ///
    /// Lane conventions: gameplay lanes are SCREEN space (-1 left / +1 right behind the
    /// avatar). The raw LaneDetector sign is image space where player-left = frame-right
    /// (verified against recorded tablet feeds in AstroFeedReplayTest), so the live path
    /// inverts it — the avatar then steps the same direction the player does.
    /// </summary>
    public class AstroStanceDirector : MonoBehaviour
    {
        enum State { Intro, Framing, Calibrating, Countdown, Play, Results }

        [Header("Detection")]
        public MediaPipePoseDetector poseDetector;
        [Tooltip("Editor keyboard mode (←/→ lane, S sit/stand, N/M side kick). Forced OFF on device in Awake.")]
        public bool useKeyboardStub = true;
        [Tooltip("Raw image-space lane sign → screen-space lanes for the behind view. " +
                 "True is correct for an unmirrored front camera (verified on real feeds).")]
        public bool invertLaneForBackView = true;

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
        public float sitDipMeters = 0.20f;
        [Tooltip("Seconds for the sit crouch to fully ease in/out. Lower = snappier.")]
        public float sitDipSeconds = 0.35f;

        [Header("Run")]
        public AstroSpawner spawner;
        public float sessionSeconds = 180f;
        [Tooltip("0 = random deck each run; any other value replays the same deck (testing).")]
        public int deckSeed = 0;

        [Header("Camera shake")]
        [Tooltip("Shaken briefly on a meteor hit. Wire the Main Camera transform.")]
        public Transform shakeTarget;

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
        [Tooltip("3 lane indicator dots, left→right.")]
        public Image[] laneDots = new Image[3];
        public TMP_Text countdownText;

        [Header("Results UI")]
        public TMP_Text resultScoreText;
        [Tooltip("3 star images, dimmed/lit by the director.")]
        public Image[] resultStars = new Image[3];
        [Tooltip("Value texts for the rep rows: สมบัติ เตะ หลบ ลุก-นั่ง ก้าว.")]
        public TMP_Text[] resultRepValues = new TMP_Text[5];

        public event Action<AstroResult> OnSessionComplete;
        public event Action OnExitRequested;

        const float FramingHoldSeconds = 1.5f;
        const float BodyLostSeconds = 1.2f;
        const float CalibStillSeconds = 2f;
        const float CalibMoveTolerance = 0.03f;
        const float CountdownSeconds = 3f;
        const float ToastSeconds = 1.5f;

        // Bright sunny-park palette (matches AstroStanceUIBuilder). ChipPass/Green, ChipFail/coral,
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
        readonly AstroSitClassifier _sit = new AstroSitClassifier();
        readonly LegAbductionDetector _sideKick = new LegAbductionDetector();
        readonly LaneDetector _lane = new LaneDetector();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();

        State _state = State.Intro;
        bool _calibrated;
        bool _runStarted;
        bool _manualPaused;
        bool _satThisRep; // a JustStood only counts as a rep if a JustSat preceded it
        float _calibStillTimer;
        Vector2 _calibAnchor;
        bool _calibAnchorSet;
        float _countdownLeft;
        float _timeLeft;
        float _toastTimer;
        float _avatarBaseX;
        float _avatarBaseY;
        float _sitDip01;   // 0 = standing, 1 = fully dipped into the sit crouch (smoothed)
        bool _sitHeld;     // true between JustSat and the following JustStood
        HumanPoseHandler _poseHandler;   // scripted sit crouch (bends knees while the puppet is off)
        HumanPose _crouchPose;
        bool _crouchPoseActive;
        int[] _crouchMuscle;
        float[] _crouchTarget;
        int _lastLane;
        GameObject _tool;
        bool _claimPending; // sat over a treasure, tool grabbed — collect once the crouch bottoms out
        AstroResult _result = new AstroResult();

        float _bodyLostTimer; // seconds every joint group has been MISSING (drives the play→framing bounce)

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
        bool JustKickedLeft => useKeyboardStub ? _stub.JustKickedLeft : _sideKick.JustKickedLeft;
        bool JustKickedRight => useKeyboardStub ? _stub.JustKickedRight : _sideKick.JustKickedRight;
        int PlayerLane => useKeyboardStub
            ? _stub.Lane
            : (invertLaneForBackView ? -_lane.Lane : _lane.Lane);

        void Awake()
        {
            // A tablet has no keyboard: stub mode there would soft-lock the game. Force off.
            if (!Application.isEditor && poseDetector != null) useKeyboardStub = false;
        }

#if UNITY_EDITOR
        // EDITOR TEST ONLY: called by AstroTestClipSwitcher when a recording is replayed, so the
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
            }
            ShowOnly(introPanel);
            if (hudPanel != null) hudPanel.SetActive(false);
            SetPreviewVisible(false); // hide the camera preview until the player presses Start
            UpdateScoreText();
            UpdateTimerText();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!_manualPaused) TickMotion(dt);

            switch (_state)
            {
                case State.Intro: break;
                case State.Framing: TickFraming(); break;
                case State.Calibrating: TickCalibrating(dt); break;
                case State.Countdown: TickCountdown(dt); break;
                case State.Play: if (!_manualPaused) TickPlay(dt); break;
                case State.Results: break;
            }

            TickToast(dt);
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
            _state = State.Framing;
            if (framingPanel != null) framingPanel.SetActive(true);
            if (calibGroup != null) calibGroup.SetActive(false);
            if (introPanel != null) introPanel.SetActive(false);
            SetPreviewVisible(true); // the run has begun — show the live camera preview from here on
            if (_runStarted && spawner != null) spawner.SetPaused(true);
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
            if (_calibrated) { EnterCountdown(); return; }
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
                // Sit/stand is calibration-free now (AstroSitClassifier reads geometry directly),
                // so only the kick + lane detectors still need a standing baseline captured here.
                var kp = poseDetector.LatestKeypoints;
                _sideKick.SetBaseline(kp);
                _lane.CalibrateCenter(kp);
            }
            _calibrated = true;
            if (calibGroup != null) calibGroup.SetActive(false);
            Kinex.Sfx.Play("checkpoint", 0.7f);
            EnterCountdown();
        }

        // ---- Countdown: 3-2-1 into (or back into) the run. ----

        void EnterCountdown()
        {
            _state = State.Countdown;
            _countdownLeft = CountdownSeconds;
            if (hudPanel != null) hudPanel.SetActive(true);
            if (countdownText != null) countdownText.gameObject.SetActive(true);
        }

        void TickCountdown(float dt)
        {
            // Losing the body during the countdown bounces back to framing (seated span-shrink is
            // tolerated — only a genuinely missing joint group counts as lost).
            if (!useKeyboardStub && _bodyLostTimer > BodyLostSeconds) { EnterFraming(); return; }

            _countdownLeft -= dt;
            if (countdownText != null)
                countdownText.text = _countdownLeft > 0f ? Mathf.Ceil(_countdownLeft).ToString("0") : "ไป!";
            if (_countdownLeft <= -0.5f)
            {
                if (countdownText != null) countdownText.gameObject.SetActive(false);
                EnterPlay();
            }
        }

        void EnterPlay()
        {
            _state = State.Play;
            if (!_runStarted)
            {
                _runStarted = true;
                int seed = deckSeed != 0 ? deckSeed : UnityEngine.Random.Range(1, int.MaxValue);
                if (spawner != null) spawner.BeginRun(AstroLogic.BuildDeck(seed));
            }
            else if (spawner != null) spawner.SetPaused(false);
        }

        // ---- Play: the 3-minute run. ----

        void TickPlay(float dt)
        {
            if (!useKeyboardStub && _bodyLostTimer > BodyLostSeconds) { EnterFraming(); return; }

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
            int lane = PlayerLane;
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
                var treasure = spawner != null ? spawner.ActiveItem(AstroKind.Treasure, PlayerLane) : null;
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
                var treasure = spawner != null ? spawner.ActiveItem(AstroKind.Treasure, PlayerLane) : null;
                if (treasure != null)
                {
                    treasure.Collect(avatarRoot);
                    _result.treasures++;
                    _result.score++;
                    UpdateScoreText();
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
                // next sit inherits the player's real arms/torso instead of a stiff T-pose.
                _poseHandler.GetHumanPose(ref _crouchPose);
            }
        }

        void BuildCrouchMuscles()
        {
            var names = HumanTrait.MuscleName;
            System.Func<string, int> mi = s => { for (int i = 0; i < names.Length; i++) if (names[i] == s) return i; return -1; };
            // A gentle crouch to pick the treasure off the floor (behind-view): thighs partway
            // forward, a mild knee bend so the feet stay planted (a deeper fold tucks the shins up
            // behind = a kneel), and a slight forward spine so the character reads as reaching down.
            var map = new (string name, float val)[]
            {
                ("Left Upper Leg Front-Back", 0.45f), ("Right Upper Leg Front-Back", 0.45f),
                ("Left Lower Leg Stretch", -0.35f),   ("Right Lower Leg Stretch", -0.35f),
                ("Spine Front-Back", 0.12f),
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

        void OnDestroy() => _poseHandler?.Dispose();

        void TickKicks()
        {
            bool left = JustKickedLeft;
            bool right = JustKickedRight;
            if (!left && !right) return;

            var ring = spawner != null ? spawner.ActiveItem(AstroKind.KickRing) : null;
            if (ring == null) return;
            if (AstroLogic.KickValid(PlayerLane, ring.Lane, left))
            {
                ring.Kicked(PlayerLane);
                _result.kicks++;
                _result.score++;
                UpdateScoreText();
                ShowToast("เตะโดน! +1", ToastCyan);
                Kinex.Sfx.Play("whoosh", 0.7f);
            }
        }

        void OnItemArrived(AstroItem item)
        {
            if (_state != State.Play) return;
            switch (item.Kind)
            {
                case AstroKind.Meteor:
                    if (item.Lane == PlayerLane)
                    {
                        _result.meteorHits++;
                        _result.score--;
                        UpdateScoreText();
                        ShowToast("−1 โดนก้อนหิน!", ToastOrange);
                        Kinex.Sfx.Play("hit_1", 0.8f);
                        if (shakeTarget != null) StartCoroutine(ShakeRoutine());
                    }
                    else
                    {
                        _result.dodges++;
                        ShowToast("ปลอดภัย!", ChipPass);
                        Kinex.Sfx.Play("dodge", 0.5f);
                    }
                    break;

                case AstroKind.KickRing:
                    if (AstroLogic.KickFoul(PlayerLane, item.Lane))
                    {
                        // Standing in the ring's own lane at arrival is the terminal outcome:
                        // take the −1 and consume the ring so it can't also be kicked for +1.
                        item.Fouled = true;
                        _result.kickFouls++;
                        _result.score--;
                        UpdateScoreText();
                        ShowToast("−1 ยืนทับวงเตะ! ขยับไปเลนข้าง ๆ", ToastOrange);
                        Kinex.Sfx.Play("hit_2", 0.6f);
                        item.FadeAway();
                    }
                    break;
            }
        }

        void OnItemExpired(AstroItem item)
        {
            // No penalty — a missed treasure/ring just leaves. Drop the grabbed tool only when the
            // treasure that expired is the one in the player's current lane: two treasures can be
            // Active at once in different lanes (beat 6.2s < fall 4s + window 7s), and a treasure
            // leaving a lane you're NOT working shouldn't knock the tool out of your hand.
            if (item.Kind == AstroKind.Treasure && _tool != null && item.Lane == PlayerLane) DropTool();
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
            _tool = AstroProps.GrabberTool();
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

            _result.stars = AstroLogic.Stars(_result.score);
            _result.durationSeconds = sessionSeconds;

            if (resultScoreText != null) resultScoreText.text = AstroLogic.DisplayScore(_result.score).ToString();
            for (int i = 0; i < resultStars.Length; i++)
                if (resultStars[i] != null) resultStars[i].color = i < _result.stars ? StarLit : StarDim;
            int[] reps = { _result.treasures, _result.kicks, _result.dodges, _result.sitStands, _result.laneSteps };
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

        // ---- HUD helpers. ----

        void UpdateScoreText()
        {
            if (scoreText != null) scoreText.text = AstroLogic.DisplayScore(_result.score).ToString();
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

        void ShowOnly(GameObject panel)
        {
            if (introPanel != null) introPanel.SetActive(panel == introPanel);
            if (framingPanel != null) framingPanel.SetActive(panel == framingPanel);
            if (resultsPanel != null) resultsPanel.SetActive(panel == resultsPanel);
            if (pausePanel != null) pausePanel.SetActive(panel == pausePanel);
        }

        // ---- Buttons (wired by AstroStanceUIBuilder). ----

        public void OnStartPressed()
        {
            if (_state != State.Intro) return;
            EnterFraming();
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
            if (previewToggleButton != null) previewToggleButton.SetActive(show);
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
    }
}
