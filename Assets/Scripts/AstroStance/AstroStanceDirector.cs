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
        [Tooltip("Estimated seated hip-drop in torso lengths. Real chair sits measured ~0.37 " +
                 "with seated torso-shrink eating part of it — 0.30 keeps JustSat reliable. " +
                 "Must match AstroFeedReplayTest.")]
        public float shallowSitFactor = 0.30f;

        [Header("Avatar")]
        [Tooltip("Wrapper the lane locomotion slides sideways. The character model is its child.")]
        public Transform avatarRoot;
        [Tooltip("The character's Animator — used to find the hand bone for the grabber tool.")]
        public Animator avatarAnimator;
        public float laneLerpSpeed = 4f;
        [Tooltip("Body lean into a side-step, degrees.")]
        public float leanDegrees = 7f;

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

        static readonly Color ChipPass = new Color(0.35f, 0.88f, 0.54f, 0.95f);
        static readonly Color ChipFail = new Color(0.96f, 0.42f, 0.33f, 0.95f);
        static readonly Color LaneDotOn = new Color(0.30f, 0.89f, 1.00f);
        static readonly Color LaneDotOff = new Color(1f, 1f, 1f, 0.25f);
        static readonly Color ToastGold = new Color(1.00f, 0.82f, 0.33f);
        static readonly Color ToastOrange = new Color(1.00f, 0.48f, 0.28f);
        static readonly Color ToastCyan = new Color(0.45f, 0.92f, 1.00f);
        static readonly Color StarLit = new Color(1.00f, 0.82f, 0.33f);
        static readonly Color StarDim = new Color(1f, 1f, 1f, 0.18f);

        readonly FullBodyGate _fullBody = new FullBodyGate();
        readonly SitStandDetector _sitStand = new SitStandDetector();
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
        int _lastLane;
        GameObject _tool;
        AstroResult _result = new AstroResult();

        // ---- Motion facade: the one place that switches stub vs live detectors. ----
        bool HasLivePose => poseDetector != null && poseDetector.HasPose;
        bool FullBodyOk => useKeyboardStub ? _stub.IsFullBodyVisible : _fullBody.IsFullBodyVisible;
        bool JustStood => useKeyboardStub ? _stub.JustStood : _sitStand.JustStood;
        bool JustSat => useKeyboardStub ? _stub.JustSat : _sitStand.JustSat;
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

        void Start()
        {
            if (avatarRoot != null) _avatarBaseX = avatarRoot.position.x;
            _timeLeft = sessionSeconds;
            if (toastText != null) toastText.text = "";
            if (spawner != null)
            {
                spawner.ItemArrived += OnItemArrived;
                spawner.ItemExpired += OnItemExpired;
            }
            ShowOnly(introPanel);
            if (hudPanel != null) hudPanel.SetActive(false);
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
            if (!has) return;
            if (_calibrated && _fullBody.IsFullBodyVisible)
            {
                var kp = poseDetector.LatestKeypoints;
                var conf = poseDetector.LatestConfidence;
                _sitStand.Tick(kp, conf, dt);
                _sideKick.Tick(kp, conf, dt);
                _lane.Tick(kp, conf, dt);
            }
        }

        // ---- Framing: guide the player into full-body view (also the safety pause). ----

        void EnterFraming()
        {
            _state = State.Framing;
            if (framingPanel != null) framingPanel.SetActive(true);
            if (calibGroup != null) calibGroup.SetActive(false);
            if (introPanel != null) introPanel.SetActive(false);
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
                var kp = poseDetector.LatestKeypoints;
                _sitStand.CalibrateStanding(kp);
                _sitStand.EstimateSeatedFromStanding(shallowSitFactor);
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
            // Losing the body during the countdown bounces back to framing.
            if (!useKeyboardStub && _fullBody.InvalidSeconds > BodyLostSeconds) { EnterFraming(); return; }

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
            if (!useKeyboardStub && _fullBody.InvalidSeconds > BodyLostSeconds) { EnterFraming(); return; }

            _timeLeft -= dt;
            UpdateTimerText();
            if (_timeLeft <= 0f) { EndRun(); return; }

            TickLaneMovement(dt);
            TickSitStand();
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
            float targetX = _avatarBaseX + spawner.LaneWorldX(lane);
            Vector3 p = avatarRoot.position;
            float newX = Mathf.Lerp(p.x, targetX, 1f - Mathf.Exp(-laneLerpSpeed * dt));
            float vx = (newX - p.x) / Mathf.Max(dt, 1e-4f);
            p.x = newX;
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
                var treasure = spawner != null ? spawner.ActiveItem(AstroKind.Treasure, PlayerLane) : null;
                if (treasure != null && _tool == null)
                {
                    AttachTool();
                    ShowToast("หยิบเครื่องมือ!", ToastCyan);
                }
            }

            if (JustStood)
            {
                // A real rep always has a preceding sit. SitStandDetector boots in Seated with a
                // standing baseline, so it emits one synthetic JustStood shortly after calibration
                // — which lands during the Countdown (stands ignored there) OR, if the body wasn't
                // visible then, on the first Play frame. Gating on _satThisRep discards that phantom
                // stand without ever eating a genuine sit→stand (the earlier boot-swallow flag ate
                // the first real rep when the synthetic one fell in the countdown).
                if (!_satThisRep) return;
                _satThisRep = false;
                _result.sitStands++;
                var treasure = spawner != null ? spawner.ActiveItem(AstroKind.Treasure, PlayerLane) : null;
                if (_tool != null && treasure != null)
                {
                    treasure.Collect(avatarRoot);
                    _result.treasures++;
                    _result.score++;
                    UpdateScoreText();
                    ShowToast("+1 เก็บสมบัติ!", ToastGold);
                    Kinex.Sfx.Play("coin", 0.8f);
                }
                if (_tool != null) DropTool();
            }
        }

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
                        ShowToast("−1 โดนอุกกาบาต!", ToastOrange);
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
            var c = toastText.color;
            c.a = Mathf.SmoothStep(0f, 1f, t);
            toastText.color = c;
            toastText.transform.localScale = Vector3.one * (1f + 0.12f * Mathf.SmoothStep(0f, 1f, t) * t);
            if (_toastTimer <= 0f) toastText.text = "";
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
