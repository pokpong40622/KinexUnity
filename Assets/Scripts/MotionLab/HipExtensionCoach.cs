using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for standing hip extension / back kick ("เหยียดสะโพกไปด้านหลัง") — 10 reps per leg.
    ///
    /// Front-on, a leg swinging straight BACK barely moves in the image plane at all, so this is the
    /// one pose that has to lean on MediaPipe's depth channel. <see cref="HipExtensionDetector"/>
    /// owns the count (it already smooths z and gates on sideways drift so an abduction kick cannot
    /// be mistaken for an extension); this coach re-derives its own normalized depth travel purely
    /// to drive the range cues, because the detector exposes only the boolean.
    ///
    /// Cue ids: get_ready, extend_more, too_high, knee_straight, lower_slow, stand_tall, hold,
    ///          rep_good, switch_side, not_in_frame, done.
    ///
    /// NOT judged: 'no_arch'. Lumbar arching is a sagittal-plane fault and is invisible to a
    /// head-on camera. The cue id stays reserved.
    ///
    /// CONFIDENCE WARNING: MediaPipe z is the noisiest signal in this whole folder. Expect
    /// extendTargetDepth / extendMaxDepth to need real tuning on device before this feels right.
    /// </summary>
    public class HipExtensionCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Reps required on each leg before switching sides.")]
        public int repsPerSide = 10;

        [Header("Depth band (ankle travel away from the camera, in hip widths)")]
        [Tooltip("Below this the leg has not gone back far enough -> 'extend_more'. Should sit at or " +
                 "just under HipExtensionDetector's own 0.5 trigger so the cue agrees with the count.")]
        public float extendTargetDepth = 0.5f;
        [Tooltip("Beyond this the user is throwing the leg back and arching -> 'too_high'.")]
        public float extendMaxDepth = 1.1f;
        [Tooltip("Returning faster than this (hip widths per second) -> 'lower_slow'.")]
        public float lowerFastRatePerSec = 2.5f;
        [Tooltip("Smoothing on the measured depth travel (0 = none, 1 = instant).")]
        [Range(0.05f, 1f)] public float depthSmoothing = 0.3f;

        [Header("Form checks")]
        [Tooltip("Sideways offset between shoulder-mid and hip-mid as a fraction of torso length. " +
                 "Beyond this the user is leaning to fake the kick -> 'stand_tall'.")]
        public float trunkLeanRatio = 0.18f;
        [Tooltip("Hip-knee-ankle angle on the working leg. Below this the knee is bent -> 'knee_straight'.")]
        public float kneeStraightMinDeg = 150f;

        const string CueExtendMore = "extend_more";
        const string CueTooHigh = "too_high";
        const string CueKneeStraight = "knee_straight";
        const string CueLowerSlow = "lower_slow";

        const int LmLHip = 23, LmRHip = 24, LmLAnkle = 27, LmRAnkle = 28;

        protected override string PoseId => "hip_extension";
        protected override string Mode => "reps";
        protected override string SidesMode => "per-side";
        protected override int Target => repsPerSide;

        readonly HipExtensionDetector _hipExt = new HipExtensionDetector();

        int _totalReps;
        float _zL0, _zR0, _hipWidth0;
        float _depthL, _depthR;
        bool _hasDepth;
        float _prevWorking;
        bool _rateValid;
        bool _wasBack;

        protected override void OnWarmupComplete(Vector2[] kp)
        {
            var lm = Landmarks;
            if (lm == null || lm.Length <= LmRAnkle) return;
            _hipExt.SetBaseline(lm);
            _zL0 = lm[LmLAnkle].z;
            _zR0 = lm[LmRAnkle].z;
            _hipWidth0 = Mathf.Max(Mathf.Abs(lm[LmLHip].x - lm[LmRHip].x), 0.02f);
        }

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) { _rateValid = false; return LostCue(); }
            var lm = Landmarks;
            if (lm == null || lm.Length <= LmRAnkle) return LostCue();
            if (Warming(kp, dt)) return CueGetReady;

            _hipExt.Tick(lm, dt);

            // Display-only depth travel: positive = ankle moved away from the camera = swung back.
            float rawL = (lm[LmLAnkle].z - _zL0) / _hipWidth0;
            float rawR = (lm[LmRAnkle].z - _zR0) / _hipWidth0;
            if (!_hasDepth) { _depthL = rawL; _depthR = rawR; _hasDepth = true; }
            else
            {
                _depthL = MotionMath.Ema(_depthL, rawL, depthSmoothing);
                _depthR = MotionMath.Ema(_depthR, rawR, depthSmoothing);
            }

            // ---- Working-leg latch: whichever leg the detector says went back first. ----
            if (SideSign == 0)
            {
                int candidate = 0;
                if (_hipExt.LeftBack) candidate = -1;
                else if (_hipExt.RightBack) candidate = 1;

                if (candidate != 0)
                {
                    if (RequiredSide != 0 && candidate != RequiredSide) return CueSwitchSide;
                    SideSign = candidate;
                    _wasBack = true;
                }
            }

            float depth = SideSign < 0 ? _depthL : (SideSign > 0 ? _depthR : Mathf.Max(_depthL, _depthR));
            angleDeg = Mathf.RoundToInt(depth * 100f); // informational: depth travel in % of hip width
            float rate = (_rateValid && dt > 0f) ? (depth - _prevWorking) / dt : 0f;
            _prevWorking = depth;
            _rateValid = true;

            // ---- Rep completes when the extended leg comes back under the body. ----
            if (SideSign != 0)
            {
                bool backNow = SideSign < 0 ? _hipExt.LeftBack : _hipExt.RightBack;
                if (_wasBack && !backNow)
                {
                    _wasBack = false;
                    Reps++;
                    _totalReps++;
                    Kinex.Sfx.Play("checkpoint", 0.6f);

                    if (Reps >= repsPerSide)
                    {
                        if (RequiredSide == 0)
                        {
                            RequiredSide = -SideSign;
                            SideSign = 0;
                            Reps = 0;
                            immediate = true;
                            return CueSwitchSide;
                        }
                        immediate = true;
                        return FinishSession(_totalReps, repsPerSide * 2);
                    }

                    immediate = true;
                    return CueRepGood;
                }
                if (backNow) _wasBack = true;
            }

            // ---- Cue precedence: safety, then form, then range. ----
            if (depth > extendMaxDepth) return CueTooHigh;
            if (TrunkLean(kp) > trunkLeanRatio) return CueStandTall;
            if (SideSign != 0 && KneeAngleDeg(kp, SideSign) < kneeStraightMinDeg) return CueKneeStraight;
            if (_wasBack && rate < -lowerFastRatePerSec) return CueLowerSlow;
            if (depth < extendTargetDepth) return CueExtendMore;
            return CueHold;
        }
    }
}
