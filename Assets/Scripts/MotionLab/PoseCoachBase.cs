using System.Globalization;
using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Shared plumbing for every live pose coach. One subclass per exercise (see
    /// <see cref="PoseCoachFactory"/>); the subclass writes ONLY the geometry and the rep/hold
    /// state machine, this base owns everything that must behave the same for all nine poses:
    ///
    ///   * the confidence gate + dropout absorption (a single missed frame must not flap the cue),
    ///   * the cue debounce (a candidate cue must survive <see cref="cueHoldSeconds"/>),
    ///   * the change filter in <see cref="Emit"/> that keeps Flutter from being firehosed,
    ///   * the coach_ready / coach / coach_done JSON, and the left/right/phase side labels,
    ///   * an optional stand-still warm-up used to baseline the Kinex.Motion detectors.
    ///
    /// Division of labour (see docs/adr/2026-08-02-pose-coach-all-poses.md): Unity does the
    /// GEOMETRY and decides which SINGLE cue is active; Flutter owns every Thai string and all
    /// speech. Coaches therefore emit cue **IDs** only — there is deliberately no Thai anywhere in
    /// this folder, because these runtime-built Unity UIs have no Thai font.
    ///
    /// Keypoints are the COCO-17 set from MediaPipePoseDetector: normalized 0..1 image space,
    /// **y grows DOWNWARD**, and X must be multiplied by <see cref="horizontalScale"/> before ANY
    /// angle math because the frame is 480x640 and one X unit is not one Y unit. Every threshold in
    /// this folder is a serialized field on purpose: they are educated guesses that have to be
    /// tuned on-device with a real user.
    /// </summary>
    public abstract class PoseCoachBase : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Pose detector to read the body from (wired by MotionLabDirector).")]
        public MediaPipePoseDetector poseDetector;

        [Header("Confidence gate")]
        [Tooltip("Every keypoint this coach needs must reach this confidence, otherwise the coach " +
                 "refuses to judge and emits 'not_in_frame'. Never coach off a guess.")]
        [Range(0f, 1f)] public float minConfidence = 0.35f;
        [Tooltip("How long the pose may be missing/low-confidence before 'not_in_frame' is announced. " +
                 "Absorbs single dropped frames.")]
        public float lostPoseSeconds = 0.5f;

        [Header("Geometry")]
        [Tooltip("Multiplies normalized X before any angle math. Keypoints are 0..1 on BOTH axes, so on " +
                 "a non-square camera frame one X unit is NOT one Y unit. The frame fed to MediaPipe is " +
                 "rotated to portrait 480x640, hence 480/640 = 0.75. If measured angles read consistently " +
                 "too large (or too small) on device, this is the first knob to turn.")]
        public float horizontalScale = 0.75f;
        [Tooltip("Swap the left/right labels sent to Flutter, in case the camera image is mirrored " +
                 "relative to the user. Purely cosmetic — the geometry is unaffected.")]
        public bool swapSideLabels = false;

        [Header("Cue smoothing")]
        [Tooltip("A candidate cue must persist this long before it becomes the active cue. Stops the " +
                 "cue text (and the spoken line) from flapping between two states on the boundary.")]
        public float cueHoldSeconds = 0.35f;

        [Header("Hold poses")]
        [Tooltip("Seconds the hold may be broken before the timer resets to ZERO. A brief wobble must " +
                 "not erase a good hold; a real break must. Inside the window the timer freezes.")]
        public float holdGraceSeconds = 0.75f;

        [Header("Warm-up")]
        [Tooltip("Seconds the user must hold still, well-tracked, before judging starts. Coaches whose " +
                 "detectors need a baseline capture it at the end of this window. Coaches that measure " +
                 "absolute geometry (hip abduction) never call it and are unaffected.")]
        public float warmupSeconds = 1.5f;
        [Tooltip("Hip-midpoint drift (normalized) that restarts the warm-up timer. Same idea as the " +
                 "Motion Lab's 2-second stand-still calibration.")]
        public float warmupMoveTolerance = 0.03f;

        // ---- Cue ids shared by every pose. Pose-specific ids live on the subclass. ----
        protected const string CueGetReady = "get_ready";
        protected const string CueNotInFrame = "not_in_frame";
        protected const string CueStandTall = "stand_tall";
        protected const string CueHold = "hold";
        protected const string CueSteady = "steady";
        protected const string CueGoSlower = "go_slower";
        protected const string CueRepGood = "rep_good";
        protected const string CueSwitchSide = "switch_side";
        protected const string CueDone = "done";

        // Shoulders + hips + knees + ankles: what most of these exercises need to be judged at all.
        static readonly int[] DefaultJoints =
        {
            MotionMath.LShoulder, MotionMath.RShoulder,
            MotionMath.LHip, MotionMath.RHip,
            MotionMath.LKnee, MotionMath.RKnee,
            MotionMath.LAnkle, MotionMath.RAnkle,
        };

        // ---- Subclass contract. ----

        /// <summary>Wire id, e.g. "sit_to_stand". Sent in coach_ready.</summary>
        protected abstract string PoseId { get; }
        /// <summary>"reps" (Flutter draws an n/target counter) or "hold" (Flutter draws a fill ring).</summary>
        protected abstract string Mode { get; }
        /// <summary>"none" | "per-side" | "alternating" | "phases".</summary>
        protected abstract string SidesMode { get; }
        /// <summary>Target for the CURRENT side: reps in reps mode, seconds in hold mode.</summary>
        protected abstract int Target { get; }

        /// <summary>
        /// One frame of geometry + the rep/hold state machine. Returns the cue that SHOULD be active.
        /// <paramref name="immediate"/> marks event cues (rep counted, side switch, finished) that
        /// must bypass the debounce.
        /// </summary>
        protected abstract string Evaluate(float dt, out int angleDeg, out bool immediate);

        /// <summary>Joints <see cref="ReadPose"/> gates on. Override when a pose needs fewer/more.</summary>
        protected virtual int[] RequiredJoints => DefaultJoints;

        /// <summary>Called once, on the frame the stand-still warm-up completes. Baseline detectors here.</summary>
        protected virtual void OnWarmupComplete(Vector2[] kp) { }

        // ---- State the subclass writes; the base turns it into wire fields. ----

        /// <summary>Reps completed on the current side (or in total when unsided / in phases).</summary>
        protected int Reps;
        /// <summary>0..1 progress of the current hold. Only read in "hold" mode.</summary>
        protected float Hold01;
        /// <summary>-1 = left, +1 = right, 0 = not latched yet.</summary>
        protected int SideSign;
        /// <summary>After a switch: the side that MUST be used now. Used for the label before re-latching.</summary>
        protected int RequiredSide;
        /// <summary>"phase0"/"phase1" for narrow_base_stand; null for every other pose.</summary>
        protected string PhaseLabel;
        /// <summary>Set by <see cref="FinishSession"/>; freezes Update.</summary>
        protected bool Finished { get; private set; }

        protected string ActiveCue => _activeCue;
        protected float LostSeconds => _lostTimer;
        protected bool WarmupDone => _warmDone;
        protected MediaPipePoseDetector.NormLandmark[] Landmarks =>
            poseDetector != null ? poseDetector.Landmarks33 : null;

        // ---- Runtime. ----
        float _lostTimer;
        float _warmTimer;
        Vector2 _warmAnchor;
        bool _warmAnchorSet;
        bool _warmDone;

        string _candidateCue = "";
        float _candidateTimer;
        string _activeCue = "";

        // Last payload actually sent — the change filter.
        string _sentCue = "";
        string _sentSide = "";
        int _sentReps = -1;
        int _sentHoldStep = -1;

        void Start()
        {
            SendToFlutter.Send(
                $"{{\"type\":\"coach_ready\",\"pose\":\"{PoseId}\",\"mode\":\"{Mode}\"," +
                $"\"target\":{Target},\"sides\":\"{SidesMode}\"}}");
        }

        void Update()
        {
            if (Finished) return;
            float dt = Time.deltaTime;

            string cue = Evaluate(dt, out int angleDeg, out bool immediate);

            if (immediate)
            {
                _activeCue = cue;
                _candidateCue = cue;
                _candidateTimer = 0f;
            }
            else if (cue != _activeCue)
            {
                // Debounce: a new cue has to survive cueHoldSeconds before it takes over.
                if (cue != _candidateCue) { _candidateCue = cue; _candidateTimer = 0f; }
                else
                {
                    _candidateTimer += dt;
                    if (_candidateTimer >= cueHoldSeconds) _activeCue = cue;
                }
            }
            else _candidateTimer = 0f;

            Emit(_activeCue, angleDeg);
        }

        /// <summary>Reads this frame's keypoints, gating on pose presence + per-joint confidence.</summary>
        protected bool ReadPose(float dt, out Vector2[] kp)
        {
            kp = null;
            bool ok = poseDetector != null && poseDetector.HasPose;
            if (ok)
            {
                kp = poseDetector.LatestKeypoints;
                var conf = poseDetector.LatestConfidence;
                ok = kp != null && conf != null && kp.Length > MotionMath.RAnkle &&
                     MotionMath.Valid(conf, minConfidence, RequiredJoints);
            }

            if (ok) { _lostTimer = 0f; return true; }
            _lostTimer += dt;
            kp = null;
            return false;
        }

        /// <summary>Confidence array for the current frame (null when there is no pose).</summary>
        protected float[] Confidence => poseDetector != null ? poseDetector.LatestConfidence : null;

        /// <summary>
        /// What to show when <see cref="ReadPose"/> failed: hold the current cue through short
        /// dropouts (a single missed frame must not flap to not_in_frame and back), then give up.
        /// </summary>
        protected string LostCue() =>
            LostSeconds < lostPoseSeconds && !string.IsNullOrEmpty(ActiveCue) ? ActiveCue : CueNotInFrame;

        /// <summary>
        /// Ticks the stand-still warm-up. Returns TRUE while still warming up — the caller should
        /// return its "get ready" cue and judge nothing. Calls <see cref="OnWarmupComplete"/> exactly
        /// once, on the frame it finishes, with a steady frame of keypoints to baseline from.
        /// </summary>
        protected bool Warming(Vector2[] kp, float dt)
        {
            if (_warmDone) return false;

            Vector2 hip = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]);
            if (!_warmAnchorSet || Vector2.Distance(hip, _warmAnchor) > warmupMoveTolerance)
            {
                _warmAnchor = hip;
                _warmAnchorSet = true;
                _warmTimer = 0f;
                return true;
            }

            _warmTimer += dt;
            if (_warmTimer < warmupSeconds) return true;

            _warmDone = true;
            OnWarmupComplete(kp);
            return false;
        }

        // ---- Hold timer (hold-mode poses only). ----

        /// <summary>Seconds banked on the current hold.</summary>
        protected float HoldSeconds { get; private set; }
        float _holdBreak;

        /// <summary>
        /// Advances (or breaks) the current hold and republishes <see cref="Hold01"/>.
        ///
        /// Hold01 is STRICTLY LINEAR IN TIME — elapsed / target, clamped — never eased and never
        /// weighted by pose quality, because Flutter derives the displayed seconds as
        /// round(hold * target). Easing it would make that counter stall or skip.
        ///
        /// Breaking the pose FREEZES the timer for <see cref="holdGraceSeconds"/>, then RESETS it to
        /// zero (it does not decay gradually).
        /// </summary>
        protected void TickHold(bool conditionMet, float dt, float targetSeconds)
        {
            if (conditionMet) { HoldSeconds += dt; _holdBreak = 0f; }
            else
            {
                _holdBreak += dt;
                if (_holdBreak > holdGraceSeconds) HoldSeconds = 0f;
            }
            Hold01 = targetSeconds > 0f ? Mathf.Clamp01(HoldSeconds / targetSeconds) : 0f;
        }

        /// <summary>Clears the hold outright — used when moving to the next side or phase.</summary>
        protected void ResetHold()
        {
            HoldSeconds = 0f;
            _holdBreak = 0f;
            Hold01 = 0f;
        }

        // ---- Geometry helpers. Every one of these applies horizontalScale to X. ----

        /// <summary>from -&gt; to as a vector with X rescaled into Y units.</summary>
        protected Vector2 Delta(Vector2 from, Vector2 to) =>
            new Vector2((to.x - from.x) * horizontalScale, to.y - from.y);

        /// <summary>
        /// Angle of from-&gt;to measured from straight DOWN the image. 0 = hanging straight down,
        /// 90 = horizontal. (Image y grows downward, so (0,1) IS "down the body".)
        /// </summary>
        protected float AngleFromDownDeg(Vector2 from, Vector2 to)
        {
            Vector2 d = Delta(from, to);
            if (d.sqrMagnitude < 1e-6f) return 0f;
            return Vector2.Angle(d, Vector2.up);
        }

        /// <summary>Interior angle at <paramref name="pivot"/>; 180 = perfectly straight.</summary>
        protected float JointAngleDeg(Vector2 a, Vector2 pivot, Vector2 c)
        {
            Vector2 u = Delta(pivot, a);
            Vector2 v = Delta(pivot, c);
            if (u.sqrMagnitude < 1e-6f || v.sqrMagnitude < 1e-6f) return 180f;
            return Vector2.Angle(u, v);
        }

        /// <summary>Hip-knee-ankle angle on one leg (-1 = left, +1 = right); 180 = perfectly straight.</summary>
        protected float KneeAngleDeg(Vector2[] kp, int side)
        {
            int hip = side < 0 ? MotionMath.LHip : MotionMath.RHip;
            int knee = side < 0 ? MotionMath.LKnee : MotionMath.RKnee;
            int ankle = side < 0 ? MotionMath.LAnkle : MotionMath.RAnkle;
            return JointAngleDeg(kp[hip], kp[knee], kp[ankle]);
        }

        /// <summary>
        /// Sideways offset between shoulder-mid and hip-mid as a fraction of torso length —
        /// how far the user is leaning to cheat a lift. 0 = upright.
        /// </summary>
        protected float TrunkLean(Vector2[] kp)
        {
            Vector2 shMid = MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]);
            Vector2 hipMid = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]);
            float torso = MotionMath.TorsoLen(kp);
            if (torso < 0.05f) return 0f;
            return Mathf.Abs((shMid.x - hipMid.x) * horizontalScale) / torso;
        }

        /// <summary>Distance between the two ankles, with X rescaled into Y units.</summary>
        protected float AnkleGap(Vector2[] kp) =>
            Delta(kp[MotionMath.LAnkle], kp[MotionMath.RAnkle]).magnitude;

        /// <summary>Hip width, with X rescaled — the per-player scale unit for stance-width thresholds.</summary>
        protected float HipWidth(Vector2[] kp) =>
            Mathf.Max(Mathf.Abs(kp[MotionMath.LHip].x - kp[MotionMath.RHip].x) * horizontalScale, 0.01f);

        // ---- Session end + wire. ----

        /// <summary>Ends the session: plays the finish sting, sends coach_done, returns the "done" cue.</summary>
        protected string FinishSession(int totalReps, int totalTarget)
        {
            Finished = true;
            Kinex.Sfx.Play("go", 0.7f);
            SendToFlutter.Send(
                $"{{\"type\":\"coach_done\",\"reps\":{totalReps},\"target\":{totalTarget}}}");
            return CueDone;
        }

        /// <summary>
        /// Sends a coach message only when something the UI shows actually changed: the cue, the
        /// side, the rep count, or (hold mode only) the hold ring by a visible step.
        /// </summary>
        void Emit(string cue, int angleDeg)
        {
            if (string.IsNullOrEmpty(cue)) return;
            string side = SideLabel();
            bool holdMode = Mode == "hold";
            // 50 steps over the whole ring = one message per 2% of the hold. Over a 10 s hold that is
            // ~5 messages a second at most, instead of one per frame.
            int holdStep = holdMode ? Mathf.RoundToInt(Mathf.Clamp01(Hold01) * 50f) : 0;

            if (cue == _sentCue && side == _sentSide && Reps == _sentReps && holdStep == _sentHoldStep)
                return;

            _sentCue = cue;
            _sentSide = side;
            _sentReps = Reps;
            _sentHoldStep = holdStep;

            string holdField = holdMode
                ? ",\"hold\":" + Mathf.Clamp01(Hold01).ToString("0.00", CultureInfo.InvariantCulture)
                : "";
            SendToFlutter.Send(
                $"{{\"type\":\"coach\",\"cue\":\"{cue}\",\"side\":\"{side}\"," +
                $"\"reps\":{Reps},\"target\":{Target}{holdField},\"angle\":{angleDeg}}}");
        }

        /// <summary>"left" / "right" / "phase0" / "phase1" / "" (unsided).</summary>
        protected virtual string SideLabel()
        {
            if (PhaseLabel != null) return PhaseLabel;
            int s = SideSign != 0 ? SideSign : RequiredSide;
            if (s == 0) return "";
            if (swapSideLabels) s = -s;
            return s < 0 ? "left" : "right";
        }
    }
}
