using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for standing hip abduction ("กางสะโพกออกด้านข้าง") — 10 reps per leg.
    ///
    /// Judges the angle of hip-&gt;ankle from straight down the image, which is an ABSOLUTE
    /// measurement: this is the one coach that needs no warm-up baseline at all.
    ///
    /// Cue ids: not_in_frame, stand_tall, knee_straight, lift_more, too_high, hold,
    ///          lower_slow, rep_good, switch_side, done.
    ///
    /// See <see cref="PoseCoachBase"/> for the shared plumbing and the keypoint conventions.
    /// </summary>
    public class HipAbductionCoach : PoseCoachBase
    {
        [Header("Abduction")]
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

        const string CueKneeStraight = "knee_straight";
        const string CueLiftMore = "lift_more";
        const string CueTooHigh = "too_high";
        const string CueLowerSlow = "lower_slow";

        protected override string PoseId => "hip_abduction";
        protected override string Mode => "reps";
        protected override string SidesMode => "per-side";
        protected override int Target => repsPerSide;

        int _totalReps;
        bool _armed;        // leg reached + held the target band; a return to low completes the rep

        float _angleL, _angleR;
        bool _hasAngles;
        float _prevWorkingAngle;
        bool _rateValid;
        float _holdTimer;
        float _lowTimer;

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
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
                if (LostSeconds < lostPoseSeconds && !string.IsNullOrEmpty(ActiveCue)) return ActiveCue;
                return CueNotInFrame;
            }

            float torso = MotionMath.TorsoLen(kp);
            if (torso < 0.05f) { _holdTimer = 0f; _armed = false; return CueNotInFrame; }

            float rawL = AngleFromDownDeg(kp[MotionMath.LHip], kp[MotionMath.LAnkle]);
            float rawR = AngleFromDownDeg(kp[MotionMath.RHip], kp[MotionMath.RAnkle]);
            if (!_hasAngles) { _angleL = rawL; _angleR = rawR; _hasAngles = true; }
            else
            {
                _angleL = MotionMath.Ema(_angleL, rawL, angleSmoothing);
                _angleR = MotionMath.Ema(_angleR, rawR, angleSmoothing);
            }

            // ---- Working-leg latch. ----
            string wrongLegCue = UpdateSideLatch(dt);

            float angle = SideSign < 0 ? _angleL : (SideSign > 0 ? _angleR : Mathf.Max(_angleL, _angleR));
            angleDeg = Mathf.RoundToInt(angle);
            float rate = (_rateValid && dt > 0f) ? (angle - _prevWorkingAngle) / dt : 0f;
            _prevWorkingAngle = angle;
            _rateValid = true;

            // ---- Rep state machine (runs regardless of which cue wins the display). ----
            if (SideSign != 0)
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
                    Reps++;
                    _totalReps++;
                    Kinex.Sfx.Play("checkpoint", 0.6f);

                    if (Reps >= repsPerSide)
                    {
                        if (RequiredSide == 0)
                        {
                            // First leg finished -> demand the other one.
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
            }

            // ---- Cue precedence: safety, then form, then range. ----
            if (wrongLegCue != null) return wrongLegCue;
            if (angle > targetMaxDeg) return CueTooHigh;
            if (TrunkLean(kp) > trunkLeanRatio) return CueStandTall;
            if (SideSign != 0 && KneeAngleDeg(kp, SideSign) < kneeStraightMinDeg) return CueKneeStraight;
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
            if (SideSign != 0)
            {
                float a = SideSign < 0 ? _angleL : _angleR;
                // Only an un-started side may be released — once a rep is banked (or one is in
                // progress) the latch is sticky, so noise cannot flip sides mid-rep.
                if (a < repResetDeg && !_armed && Reps == 0)
                {
                    _lowTimer += dt;
                    if (_lowTimer >= sideReleaseSeconds) { SideSign = 0; _lowTimer = 0f; }
                }
                else _lowTimer = 0f;
                return null;
            }

            int candidate = _angleL >= _angleR ? -1 : 1;
            float candAngle = candidate < 0 ? _angleL : _angleR;
            if (candAngle < sideLatchDeg) return null;

            if (RequiredSide != 0 && candidate != RequiredSide) return CueSwitchSide;
            SideSign = candidate;
            _lowTimer = 0f;
            _holdTimer = 0f;
            _armed = false;
            return null;
        }
    }
}
