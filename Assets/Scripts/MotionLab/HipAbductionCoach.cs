using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for ONE exercise: standing hip abduction ("กางสะโพกออกด้านข้าง").
    /// Hosted by MotionLabScene (the avatar mirror) when Flutter asks for coach mode —
    /// see SceneRouter.PendingCoachPose / MotionLabDirector.
    ///
    /// Division of labour (deliberate, see CLAUDE.md): Unity does the GEOMETRY and decides
    /// which single cue is active; Flutter owns every Thai string and all speech. This class
    /// therefore emits cue **IDs** only — there is intentionally no Thai text anywhere in here,
    /// because these runtime-built Unity UIs have no Thai font (same reason SosController bakes
    /// its Thai into a sprite).
    ///
    /// Wire protocol (Unity -> Flutter), emitted ONLY when something changes:
    ///   {"type":"coach_ready"}
    ///   {"type":"coach","cue":"lift_more","side":"left","reps":3,"target":10,"angle":18}
    ///   {"type":"coach_done","reps":20,"target":20}
    ///
    /// Cue ids: not_in_frame, stand_tall, knee_straight, lift_more, too_high, hold,
    ///          lower_slow, rep_good, switch_side, done.
    ///
    /// Keypoints are the COCO-17 set from MediaPipePoseDetector (normalized 0..1 image space,
    /// y grows DOWNWARD). Every threshold below is a serialized field on purpose: they are
    /// educated guesses that have to be tuned on-device with a real user.
    /// </summary>
    public class HipAbductionCoach : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Pose detector to read the body from (wired by MotionLabDirector).")]
        public MediaPipePoseDetector poseDetector;

        [Header("Confidence gate")]
        [Tooltip("Every keypoint used (shoulders, hips, knees, ankles) must reach this confidence, " +
                 "otherwise the coach refuses to judge and emits 'not_in_frame'. Never coach off a guess.")]
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
        [Tooltip("Smoothing on the measured abduction angle (0 = none, 1 = instant). The 1€ filter " +
                 "already smooths the keypoints; this just calms the last bit of angle jitter.")]
        [Range(0.05f, 1f)] public float angleSmoothing = 0.35f;

        [Header("Target band (degrees from vertical)")]
        [Tooltip("Below this the leg is not out far enough -> 'lift_more'.")]
        public float targetMinDeg = 25f;
        [Tooltip("Above this the leg has gone past the safe ceiling -> 'too_high'.")]
        public float targetMaxDeg = 45f;
        [Tooltip("The leg must fall back below this to complete a rep (and to release the side latch).")]
        public float repResetDeg = 10f;
        [Tooltip("Seconds the leg must stay inside the target band before the rep is armed.")]
        public float holdSeconds = 0.6f;
        [Tooltip("Lowering faster than this (deg/sec) while a rep is armed -> 'lower_slow'.")]
        public float lowerFastDegPerSec = 90f;

        [Header("Form checks")]
        [Tooltip("Horizontal offset between shoulder-mid and hip-mid, as a fraction of torso length. " +
                 "Beyond this the user is leaning to cheat the lift -> 'stand_tall'.")]
        public float trunkLeanRatio = 0.18f;
        [Tooltip("Hip-knee-ankle angle on the working leg. Below this the knee is bent -> 'knee_straight'. " +
                 "180 would be perfectly straight.")]
        public float kneeStraightMinDeg = 155f;

        [Header("Session")]
        [Tooltip("Reps required on each leg before switching sides.")]
        public int repsPerSide = 10;
        [Tooltip("Which leg is worked first is auto-detected. Once a leg exceeds this angle it is LATCHED " +
                 "as the working leg so keypoint noise cannot flip sides mid-rep.")]
        public float sideLatchDeg = 12f;
        [Tooltip("Before the first rep of a side is counted, the latch releases if both legs stay below " +
                 "repResetDeg for this long (lets the user change their mind about which leg to start on).")]
        public float sideReleaseSeconds = 1f;
        [Tooltip("A candidate cue must persist this long before it becomes the active cue. Stops the " +
                 "cue text (and the spoken line) from flapping between two states on the boundary.")]
        public float cueHoldSeconds = 0.35f;

        // ---- Cue ids. Order matters only for readability; precedence is explicit in Evaluate(). ----
        const string CueNotInFrame = "not_in_frame";
        const string CueStandTall = "stand_tall";
        const string CueKneeStraight = "knee_straight";
        const string CueLiftMore = "lift_more";
        const string CueTooHigh = "too_high";
        const string CueHold = "hold";
        const string CueLowerSlow = "lower_slow";
        const string CueRepGood = "rep_good";
        const string CueSwitchSide = "switch_side";
        const string CueDone = "done";

        // ---- Runtime state. ----
        int _side;          // -1 = left leg working, +1 = right, 0 = not yet latched
        int _requiredSide;  // 0 = either (first half), otherwise the leg that MUST be used now
        int _repsThisSide;
        int _totalReps;
        bool _armed;        // leg reached + held the target band; a return to low completes the rep
        bool _finished;

        float _angleL, _angleR;
        bool _hasAngles;
        float _prevWorkingAngle;
        bool _rateValid;
        float _holdTimer;
        float _lowTimer;
        float _lostTimer;

        string _candidateCue = "";
        float _candidateTimer;
        string _activeCue = "";

        // Last payload actually sent — the change filter that keeps Flutter from being firehosed.
        string _sentCue = "";
        string _sentSide = "";
        int _sentReps = -1;

        void Start()
        {
            SendToFlutter.Send("{\"type\":\"coach_ready\"}");
        }

        void Update()
        {
            if (_finished) return;
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

        /// <summary>
        /// One frame of geometry + the rep state machine. Returns the cue that SHOULD be active.
        /// <paramref name="immediate"/> marks event cues (rep counted, side switch, finished) that
        /// must bypass the debounce.
        /// </summary>
        string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp))
            {
                _holdTimer = 0f;
                _armed = false;
                _rateValid = false;
                // Short dropouts (a single missed frame) keep the current cue instead of flapping
                // to not_in_frame and back.
                if (_lostTimer < lostPoseSeconds && !string.IsNullOrEmpty(_activeCue)) return _activeCue;
                return CueNotInFrame;
            }

            Vector2 shMid = MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]);
            Vector2 hipMid = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]);
            float torso = MotionMath.TorsoLen(kp);
            if (torso < 0.05f) { _holdTimer = 0f; _armed = false; return CueNotInFrame; }

            float rawL = AbductionDeg(kp[MotionMath.LHip], kp[MotionMath.LAnkle]);
            float rawR = AbductionDeg(kp[MotionMath.RHip], kp[MotionMath.RAnkle]);
            if (!_hasAngles) { _angleL = rawL; _angleR = rawR; _hasAngles = true; }
            else
            {
                _angleL = MotionMath.Ema(_angleL, rawL, angleSmoothing);
                _angleR = MotionMath.Ema(_angleR, rawR, angleSmoothing);
            }

            // ---- Working-leg latch. ----
            string wrongLegCue = UpdateSideLatch(dt);

            float angle = _side < 0 ? _angleL : (_side > 0 ? _angleR : Mathf.Max(_angleL, _angleR));
            angleDeg = Mathf.RoundToInt(angle);
            float rate = (_rateValid && dt > 0f) ? (angle - _prevWorkingAngle) / dt : 0f;
            _prevWorkingAngle = angle;
            _rateValid = true;

            // ---- Rep state machine (runs regardless of which cue wins the display). ----
            if (_side != 0)
            {
                if (angle >= targetMinDeg && angle <= targetMaxDeg)
                {
                    _holdTimer += dt;
                    if (_holdTimer >= holdSeconds) _armed = true;
                }
                else if (angle < targetMinDeg) _holdTimer = 0f;
                // Above the ceiling the hold timer is simply frozen — no credit, no reset.

                if (_armed && angle < repResetDeg)
                {
                    _armed = false;
                    _holdTimer = 0f;
                    _repsThisSide++;
                    _totalReps++;
                    Kinex.Sfx.Play("checkpoint", 0.6f);

                    if (_repsThisSide >= repsPerSide)
                    {
                        if (_requiredSide == 0)
                        {
                            // First leg finished -> demand the other one.
                            _requiredSide = -_side;
                            _side = 0;
                            _repsThisSide = 0;
                            immediate = true;
                            return CueSwitchSide;
                        }
                        _finished = true;
                        immediate = true;
                        Kinex.Sfx.Play("go", 0.7f);
                        SendToFlutter.Send(
                            $"{{\"type\":\"coach_done\",\"reps\":{_totalReps},\"target\":{repsPerSide * 2}}}");
                        return CueDone;
                    }

                    immediate = true;
                    return CueRepGood;
                }
            }

            // ---- Cue precedence: safety, then form, then range. ----
            if (wrongLegCue != null) return wrongLegCue;
            if (angle > targetMaxDeg) return CueTooHigh;

            float lean = Mathf.Abs((shMid.x - hipMid.x) * horizontalScale) / torso;
            if (lean > trunkLeanRatio) return CueStandTall;

            if (_side != 0 && KneeAngleDeg(kp, _side) < kneeStraightMinDeg) return CueKneeStraight;
            if (_armed && rate < -lowerFastDegPerSec) return CueLowerSlow;
            if (angle < targetMinDeg) return CueLiftMore;
            return CueHold;
        }

        /// <summary>
        /// Latches the working leg the first time one crosses <see cref="sideLatchDeg"/>, and holds
        /// that choice until the side's reps are done. Returns a cue when the user is lifting the
        /// WRONG leg after a switch, otherwise null.
        /// </summary>
        string UpdateSideLatch(float dt)
        {
            if (_side != 0)
            {
                float a = _side < 0 ? _angleL : _angleR;
                // Only an un-started side may be released — once a rep is banked (or one is in
                // progress) the latch is sticky, so noise cannot flip sides mid-rep.
                if (a < repResetDeg && !_armed && _repsThisSide == 0)
                {
                    _lowTimer += dt;
                    if (_lowTimer >= sideReleaseSeconds) { _side = 0; _lowTimer = 0f; }
                }
                else _lowTimer = 0f;
                return null;
            }

            int candidate = _angleL >= _angleR ? -1 : 1;
            float candAngle = candidate < 0 ? _angleL : _angleR;
            if (candAngle < sideLatchDeg) return null;

            if (_requiredSide != 0 && candidate != _requiredSide) return CueSwitchSide;
            _side = candidate;
            _lowTimer = 0f;
            _holdTimer = 0f;
            _armed = false;
            return null;
        }

        /// <summary>Reads this frame's keypoints, gating on pose presence + per-joint confidence.</summary>
        bool ReadPose(float dt, out Vector2[] kp)
        {
            kp = null;
            bool ok = poseDetector != null && poseDetector.HasPose;
            if (ok)
            {
                kp = poseDetector.LatestKeypoints;
                var conf = poseDetector.LatestConfidence;
                ok = kp != null && conf != null && kp.Length > MotionMath.RAnkle &&
                     MotionMath.Valid(conf, minConfidence,
                         MotionMath.LShoulder, MotionMath.RShoulder,
                         MotionMath.LHip, MotionMath.RHip) &&
                     MotionMath.Valid(conf, minConfidence,
                         MotionMath.LKnee, MotionMath.RKnee,
                         MotionMath.LAnkle, MotionMath.RAnkle);
            }

            if (ok) { _lostTimer = 0f; return true; }
            _lostTimer += dt;
            kp = null;
            return false;
        }

        /// <summary>
        /// Abduction angle of one leg: hip-&gt;ankle measured from straight DOWN the image.
        /// 0 = leg hanging under the hip, 90 = leg straight out sideways.
        /// </summary>
        float AbductionDeg(Vector2 hip, Vector2 ankle)
        {
            Vector2 d = new Vector2((ankle.x - hip.x) * horizontalScale, ankle.y - hip.y);
            if (d.sqrMagnitude < 1e-6f) return 0f;
            return Vector2.Angle(d, Vector2.up); // image y grows downward, so (0,1) IS "down the body"
        }

        /// <summary>Hip-knee-ankle angle on the working leg; 180 = perfectly straight.</summary>
        float KneeAngleDeg(Vector2[] kp, int side)
        {
            int hipIdx = side < 0 ? MotionMath.LHip : MotionMath.RHip;
            int kneeIdx = side < 0 ? MotionMath.LKnee : MotionMath.RKnee;
            int ankleIdx = side < 0 ? MotionMath.LAnkle : MotionMath.RAnkle;
            Vector2 a = new Vector2((kp[hipIdx].x - kp[kneeIdx].x) * horizontalScale,
                                    kp[hipIdx].y - kp[kneeIdx].y);
            Vector2 b = new Vector2((kp[ankleIdx].x - kp[kneeIdx].x) * horizontalScale,
                                    kp[ankleIdx].y - kp[kneeIdx].y);
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 180f;
            return Vector2.Angle(a, b);
        }

        /// <summary>Sends a coach message only when the cue, the side, or the rep count changed.</summary>
        void Emit(string cue, int angleDeg)
        {
            if (string.IsNullOrEmpty(cue)) return;
            string side = SideLabel();
            if (cue == _sentCue && side == _sentSide && _repsThisSide == _sentReps) return;
            _sentCue = cue;
            _sentSide = side;
            _sentReps = _repsThisSide;
            SendToFlutter.Send(
                $"{{\"type\":\"coach\",\"cue\":\"{cue}\",\"side\":\"{side}\"," +
                $"\"reps\":{_repsThisSide},\"target\":{repsPerSide},\"angle\":{angleDeg}}}");
        }

        string SideLabel()
        {
            int s = _side != 0 ? _side : _requiredSide;
            if (s == 0) return "";
            if (swapSideLabels) s = -s;
            return s < 0 ? "left" : "right";
        }
    }
}
