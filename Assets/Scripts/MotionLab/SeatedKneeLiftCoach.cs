using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for seated alternating knee lifts ("ยกเข่าสลับซ้าย-ขวา") — 20 lifts in total,
    /// alternating legs.
    ///
    /// <see cref="KneeRaiseDetector"/> owns the COUNT (it already refuses to count the same leg
    /// twice in a row, which is exactly the alternating rule in the ADR). This coach re-measures the
    /// raise ratio itself only to drive the DISPLAY cue — "you have started to lift" is a different,
    /// lower bar than "that counts", and the detector does not expose a partial progress value.
    ///
    /// Cue ids: get_ready, lift_knee, lift_more, lower_slow, rep_good, switch_side, not_in_frame, done.
    /// </summary>
    public class SeatedKneeLiftCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Total lifts (both legs together) required to finish the set.")]
        public int repsTarget = 20;

        [Header("Display thresholds (knee height above the hip line, in torso lengths)")]
        [Tooltip("Above this the user has clearly started lifting -> 'lift_more' instead of 'lift_knee'.")]
        public float liftStartedRatio = 0.18f;
        [Tooltip("MIRRORS KneeRaiseDetector's internal raise threshold (0.35). Used only to decide when " +
                 "to say 'switch_side'; the detector still owns whether a lift counts. Keep the two in " +
                 "step if that constant is ever retuned.")]
        public float countedRatio = 0.35f;
        [Tooltip("Dropping the knee faster than this (torso lengths per second) -> 'lower_slow'.")]
        public float lowerFastRatePerSec = 1.4f;

        const string CueLiftKnee = "lift_knee";
        const string CueLiftMore = "lift_more";
        const string CueLowerSlow = "lower_slow";

        // Seated: ankles are frequently below the frame or behind a chair leg. Knees are enough.
        static readonly int[] Joints =
        {
            MotionMath.LShoulder, MotionMath.RShoulder,
            MotionMath.LHip, MotionMath.RHip,
            MotionMath.LKnee, MotionMath.RKnee,
        };

        protected override string PoseId => "seated_knee_lift";
        protected override string Mode => "reps";
        protected override string SidesMode => "alternating";
        protected override int Target => repsTarget;
        protected override int[] RequiredJoints => Joints;

        readonly KneeRaiseDetector _kneeRaise = new KneeRaiseDetector();
        int _lastCountedLeg;   // -1 left, +1 right, 0 none — our mirror of the detector's rule
        float _prevBestRatio;
        bool _rateValid;

        protected override void OnWarmupComplete(Vector2[] kp) => _kneeRaise.SetBaseline(kp);

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) { _rateValid = false; return LostCue(); }
            if (Warming(kp, dt)) return CueGetReady;

            _kneeRaise.Tick(kp, Confidence, dt);

            float torso = Mathf.Max(MotionMath.TorsoLen(kp), 0.02f);
            float hipY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            float ratioL = (hipY - kp[MotionMath.LKnee].y) / torso;
            float ratioR = (hipY - kp[MotionMath.RKnee].y) / torso;

            int leadLeg = ratioL >= ratioR ? -1 : 1;
            float best = Mathf.Max(ratioL, ratioR);
            angleDeg = Mathf.RoundToInt(best * 100f); // informational: knee height in % of torso

            float rate = (_rateValid && dt > 0f) ? (best - _prevBestRatio) / dt : 0f;
            _prevBestRatio = best;
            _rateValid = true;

            if (best > liftStartedRatio) SideSign = leadLeg;

            if (_kneeRaise.JustCountedAlternating)
            {
                _lastCountedLeg = leadLeg;
                SideSign = leadLeg;
                Reps = _kneeRaise.AlternatingCount;
                Kinex.Sfx.Play("checkpoint", 0.6f);
                immediate = true;
                if (Reps >= repsTarget) return FinishSession(Reps, repsTarget);
                return CueRepGood;
            }

            // Lifting the SAME leg again: the detector silently refuses to count it, so say why.
            if (best >= countedRatio && leadLeg == _lastCountedLeg) return CueSwitchSide;

            if (best >= countedRatio && rate < -lowerFastRatePerSec) return CueLowerSlow;
            if (best >= liftStartedRatio) return CueLiftMore;
            return CueLiftKnee;
        }
    }
}
