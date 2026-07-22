using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// MOTION LAB (ห้องทดลองท่าทาง) — a one-page camera-control test range. The player's
    /// character stands on a floor and mirrors them live; a framing gate walks them into full-body
    /// view first, a 2-second stand-still calibration captures baselines, then a status board
    /// shows everything the detectors see: sit/stand, head facing, side/front/back kicks, and
    /// side-step locomotion. No score, no fail — this page exists to test and tune detection.
    /// Simple three-state machine in Update(); no coroutines needed.
    /// </summary>
    public class MotionLabDirector : MonoBehaviour
    {
        enum State { Framing, Calibrating, Play }

        [Header("Detection")]
        public MediaPipePoseDetector poseDetector;
        [Tooltip("Editor keyboard mode (S sit/stand, N/M side kick, F/D front kick, H/G back kick, " +
                 "Y/U head turn, ←/→ side-step). Forced OFF on device in Awake — tablets have no keyboard.")]
        public bool useKeyboardStub = true;
        [Tooltip("Flip the head-turn left/right reading if it comes out mirrored on device.")]
        public bool invertHeadYaw = false;

        [Header("Avatar")]
        [Tooltip("Wrapper the side-step locomotion slides left/right. The character model is its child.")]
        public Transform avatarRoot;
        public float sideStepMeters = 0.6f;
        public float sideStepLerpSpeed = 4f;

        [Header("Panels")]
        public GameObject framingPanel;
        public GameObject hudPanel;

        [Header("Framing UI")]
        public TMP_Text framingPromptText;
        [Tooltip("5 checklist chips: หัว ไหล่ สะโพก เข่า เท้า — tinted pass/fail by the director.")]
        public Image[] framingChips = new Image[5];
        public Image framingHoldRing;

        [Header("Calibration UI")]
        public GameObject calibGroup;
        public TMP_Text calibText;
        public Image calibRing;

        [Header("Status board")]
        public TMP_Text sitStandText;
        public Image sitStandChip;
        public TMP_Text facingText;
        public TMP_Text toastText;

        [Header("Debug")]
        public GameObject debugPanel;
        public TMP_Text debugText;

        const float FramingHoldSeconds = 1.5f;
        const float FramingLostSeconds = 1.5f;
        const float CalibStillSeconds = 2f;
        const float CalibMoveTolerance = 0.03f; // normalized hip drift that resets the stillness timer
        const float ToastSeconds = 1.4f;

        static readonly Color ChipPass = new Color(0.30f, 0.85f, 0.45f, 0.95f);
        static readonly Color ChipFail = new Color(0.95f, 0.35f, 0.30f, 0.95f);
        static readonly Color StandColor = new Color(0.30f, 0.80f, 0.45f, 0.92f);
        static readonly Color SitColor = new Color(1.00f, 0.72f, 0.25f, 0.92f);

        State _state = State.Framing;
        bool _calibrated;
        bool _coachMode;

        readonly FullBodyGate _fullBody = new FullBodyGate();
        readonly SitStandDetector _sitStand = new SitStandDetector();
        readonly LegAbductionDetector _sideKick = new LegAbductionDetector();
        readonly FrontKickDetector _frontKick = new FrontKickDetector();
        readonly HipExtensionDetector _backKick = new HipExtensionDetector();
        readonly LaneDetector _lane = new LaneDetector();
        readonly HeadYawDetector _headYaw = new HeadYawDetector();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();

        float _calibStillTimer;
        Vector2 _calibAnchor;
        bool _calibAnchorSet;
        float _toastTimer;
        float _avatarBaseX;

        // ---- Motion facade: one place that switches between stub and live detectors. ----
        bool HasLivePose => poseDetector != null && poseDetector.HasPose;
        bool FullBodyOk => useKeyboardStub ? _stub.IsFullBodyVisible : _fullBody.IsFullBodyVisible;
        bool JustStood => useKeyboardStub ? _stub.JustStood : _sitStand.JustStood;
        bool JustSat => useKeyboardStub ? _stub.JustSat : _sitStand.JustSat;
        float SitProgress01 => useKeyboardStub ? _stub.Progress01 : _sitStand.Progress01;
        bool JustSideKickL => useKeyboardStub ? _stub.JustKickedLeft : _sideKick.JustKickedLeft;
        bool JustSideKickR => useKeyboardStub ? _stub.JustKickedRight : _sideKick.JustKickedRight;
        bool JustFrontKickL => useKeyboardStub ? _stub.JustKickedFrontLeft : _frontKick.JustKickedFrontLeft;
        bool JustFrontKickR => useKeyboardStub ? _stub.JustKickedFrontRight : _frontKick.JustKickedFrontRight;
        bool JustBackKickL => useKeyboardStub ? _stub.JustKickedBackLeft : _backKick.JustKickedBackLeft;
        bool JustBackKickR => useKeyboardStub ? _stub.JustKickedBackRight : _backKick.JustKickedBackRight;
        int Lane => useKeyboardStub ? _stub.Lane : _lane.Lane;

        int HeadFacing
        {
            get
            {
                int f = useKeyboardStub ? _stub.HeadFacing : (_headYaw.HasSignal ? _headYaw.Facing : 0);
                return invertHeadYaw ? -f : f;
            }
        }

        void Awake()
        {
            // A tablet has no keyboard: stub mode there would show nothing. Force it off on device.
            if (!Application.isEditor && poseDetector != null) useKeyboardStub = false;
        }

        void Start()
        {
            if (avatarRoot != null) _avatarBaseX = avatarRoot.position.x;
            if (toastText != null) toastText.text = "";
            if (debugPanel != null) debugPanel.SetActive(false);

            // Coach mode: Flutter asked for a guided exercise instead of the free test range.
            // Consume AND clear the pending value so a later normal Motion Lab run isn't coached.
            string coachPose = Kinex.App.SceneRouter.PendingCoachPose;
            Kinex.App.SceneRouter.PendingCoachPose = "";
            if (coachPose == "hip_abduction") { EnterCoachMode(); return; }

            EnterFraming();
        }

        // The avatar mirror keeps running (the detector drives it directly); everything else is
        // handed to the coach, which emits cue IDs to Flutter. Flutter draws the Thai UI on top —
        // no Unity panel is used, because these runtime-built panels have no Thai font budget for
        // the coaching copy and Flutter owns all wording + speech.
        void EnterCoachMode()
        {
            _coachMode = true;
            if (framingPanel != null) framingPanel.SetActive(false);
            if (hudPanel != null) hudPanel.SetActive(false);
            if (calibGroup != null) calibGroup.SetActive(false);
            var coach = gameObject.AddComponent<HipAbductionCoach>();
            coach.poseDetector = poseDetector;
        }

        void Update()
        {
            if (_coachMode) return; // HipAbductionCoach owns the frame in coach mode
            float dt = Time.deltaTime;
            TickMotion(dt);

            switch (_state)
            {
                case State.Framing: TickFraming(); break;
                case State.Calibrating: TickCalibrating(dt); break;
                case State.Play: TickPlay(dt); break;
            }

            TickToast(dt);
            if (debugPanel != null && debugPanel.activeSelf) UpdateDebugText();
        }

        void TickMotion(float dt)
        {
            if (useKeyboardStub) { _stub.Tick(dt); return; }
            bool has = HasLivePose;
            _fullBody.Tick(has, has ? poseDetector.Landmarks33 : null, dt);
            if (!has) return;

            var kp = poseDetector.LatestKeypoints;
            var conf = poseDetector.LatestConfidence;
            var lm = poseDetector.Landmarks33;
            _headYaw.Tick(lm, dt); // head turn needs no calibration — track it from frame one
            if (_calibrated && _fullBody.IsFullBodyVisible)
            {
                _sitStand.Tick(kp, conf, dt);
                _sideKick.Tick(kp, conf, dt);
                _frontKick.Tick(kp, conf, dt);
                _backKick.Tick(lm, dt);
                _lane.Tick(kp, conf, dt);
            }
        }

        // ---- Framing: walk the player into full-body view with one specific instruction. ----

        void EnterFraming()
        {
            _state = State.Framing;
            if (framingPanel != null) framingPanel.SetActive(true);
            if (hudPanel != null) hudPanel.SetActive(_calibrated); // keep the board visible on re-framing
            if (calibGroup != null) calibGroup.SetActive(false);
        }

        void TickFraming()
        {
            if (useKeyboardStub)
            {
                OnFramingPassed();
                return;
            }

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
            if (_calibrated) { EnterPlay(); return; }
            _state = State.Calibrating;
            _calibStillTimer = 0f;
            _calibAnchorSet = false;
            if (hudPanel != null) hudPanel.SetActive(true);
            if (calibGroup != null) calibGroup.SetActive(true);
            if (calibText != null) calibText.text = "ยืนตรง นิ่ง ๆ 2 วินาที";
        }

        // ---- Calibration: stand still 2 s, then every detector gets its baseline at once. ----

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
                _sitStand.EstimateSeatedFromStanding();
                _sideKick.SetBaseline(kp);
                _frontKick.SetBaseline(kp);
                _backKick.SetBaseline(poseDetector.Landmarks33);
                _lane.CalibrateCenter(kp);
            }
            _calibrated = true;
            if (calibGroup != null) calibGroup.SetActive(false);
            Kinex.Sfx.Play("checkpoint", 0.7f);
            ShowToast("พร้อมแล้ว! ลองขยับได้เลย", Color.white);
            EnterPlay();
        }

        // ---- Play: free test range — the board just mirrors whatever the detectors say. ----

        void EnterPlay()
        {
            _state = State.Play;
            if (hudPanel != null) hudPanel.SetActive(true);
        }

        void TickPlay(float dt)
        {
            if (!useKeyboardStub && _fullBody.InvalidSeconds > FramingLostSeconds)
            {
                EnterFraming();
                return;
            }

            // Sit/stand chip.
            bool standing = SitProgress01 > 0.5f;
            if (sitStandText != null) sitStandText.text = standing ? "ยืน" : "นั่ง";
            if (sitStandChip != null) sitStandChip.color = standing ? StandColor : SitColor;
            if (JustStood) ShowToast("ลุกยืน!", StandColor);
            else if (JustSat) ShowToast("นั่งลง!", SitColor);

            // Head facing indicator (+ body yaw readout from the detector itself).
            if (facingText != null)
            {
                int f = HeadFacing;
                string head = f < 0 ? "◀ หันซ้าย" : (f > 0 ? "หันขวา ▶" : "หน้าตรง");
                float bodyYaw = poseDetector != null ? poseDetector.YawDeg : 0f;
                facingText.text = $"{head}   ตัว {bodyYaw:0}°";
            }

            // Kick toasts.
            if (JustSideKickL) KickToast("เตะข้างซ้าย!");
            else if (JustSideKickR) KickToast("เตะข้างขวา!");
            else if (JustFrontKickL) KickToast("เตะหน้าซ้าย!");
            else if (JustFrontKickR) KickToast("เตะหน้าขวา!");
            else if (JustBackKickL) KickToast("เตะหลังซ้าย!");
            else if (JustBackKickR) KickToast("เตะหลังขวา!");

            // Side-step locomotion: lane -1/0/+1 slides the character across the floor.
            if (avatarRoot != null)
            {
                float targetX = _avatarBaseX + Lane * sideStepMeters;
                Vector3 p = avatarRoot.position;
                p.x = Mathf.Lerp(p.x, targetX, 1f - Mathf.Exp(-sideStepLerpSpeed * dt));
                avatarRoot.position = p;
            }
        }

        void KickToast(string msg)
        {
            ShowToast(msg, new Color(1f, 0.55f, 0.85f));
            Kinex.Sfx.Play("whoosh", 0.6f);
        }

        // ---- Toast: one line that pops in and fades. ----

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
            toastText.transform.localScale = Vector3.one * (1f + 0.15f * Mathf.SmoothStep(0f, 1f, t) * t);
            if (_toastTimer <= 0f) toastText.text = "";
        }

        void UpdateDebugText()
        {
            if (debugText == null) return;
            if (useKeyboardStub)
            {
                debugText.text = "โหมดคีย์บอร์ด (stub)\nS นั่ง/ยืน · N/M เตะข้าง · F/D เตะหน้า\nH/G เตะหลัง · Y/U หันหัว · ←/→ ก้าวข้าง";
                return;
            }
            var lm = poseDetector != null ? poseDetector.Landmarks33 : null;
            float GroupConf(params int[] ids)
            {
                if (lm == null) return 0f;
                float s = 0f;
                foreach (int i in ids) s += lm[i].visibility;
                return s / ids.Length;
            }
            debugText.text =
                $"pose: {(HasLivePose ? "YES" : "no")}   fps: {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0}\n" +
                $"หัว {GroupConf(0):0.00}  ไหล่ {GroupConf(11, 12):0.00}  สะโพก {GroupConf(23, 24):0.00}  " +
                $"เข่า {GroupConf(25, 26):0.00}  เท้า {GroupConf(27, 28):0.00}\n" +
                $"body yaw {(poseDetector != null ? poseDetector.YawDeg : 0f):0.0}°   head {_headYaw.Yaw01:0.00} ({_headYaw.Facing})\n" +
                $"sit {SitProgress01:0.00}   lane {Lane}   frame: {_fullBody.Why}";
        }

        // ---- Buttons (wired by MotionLabUIBuilder). ----

        public void OnDebugTogglePressed()
        {
            if (debugPanel != null) debugPanel.SetActive(!debugPanel.activeSelf);
        }

        public void OnExitPressed() => SendToFlutter.Send("{\"type\":\"exit\"}");
    }
}
