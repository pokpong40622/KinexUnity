using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for side stepping ("เดินก้าวข้าง") — 5 steps in one direction, then 5 back.
    ///
    /// A side step head-on is a clean two-beat cycle: the ankles come APART as the leading foot goes
    /// out, then back TOGETHER as the trailing foot catches up. That cycle is what gets counted, and
    /// it maps one-to-one onto the two cues this pose owns ('step_side' and 'feet_together').
    ///
    /// <see cref="LaneDetector"/> supplies the DIRECTION: once the player has actually travelled far
    /// enough sideways for it to report a lane, that lane is the truth about which way they are
    /// going. Until then the direction falls back to whichever ankle led the current step. Direction
    /// is in IMAGE space (+X = right of frame); flip it for the user with swapSideLabels.
    ///
    /// Cue ids: get_ready, step_side, feet_together, step_good, stand_tall, switch_side,
    ///          not_in_frame, done.
    /// </summary>
    public class SideWalkCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Steps required in each direction.")]
        public int stepsPerSide = 5;

        [Header("Step cycle (ankle separation, in hip widths)")]
        [Tooltip("Feet counted as APART beyond this. Normal standing sits near 1.0, so this must be " +
                 "comfortably above it.")]
        public float apartRatio = 1.5f;
        [Tooltip("Feet counted as back TOGETHER below this. Must stay under apartRatio — the gap " +
                 "between the two is the hysteresis that stops one step being counted twice.")]
        public float togetherRatio = 0.95f;
        [Tooltip("Smoothing on the measured ankle separation (0 = none, 1 = instant).")]
        [Range(0.05f, 1f)] public float measureSmoothing = 0.35f;
        [Tooltip("Minimum seconds between counted steps. Also the 'go_slower' trigger is not used " +
                 "here — a step faster than this is simply ignored as noise.")]
        public float stepDebounceSeconds = 0.4f;

        [Header("Form checks")]
        [Tooltip("Sideways offset between shoulder-mid and hip-mid as a fraction of torso length.")]
        public float trunkLeanRatio = 0.25f;

        const string CueStepSide = "step_side";
        const string CueFeetTogether = "feet_together";
        const string CueStepGood = "step_good";

        protected override string PoseId => "side_walk";
        protected override string Mode => "reps";
        protected override string SidesMode => "per-side";
        protected override int Target => stepsPerSide;

        readonly LaneDetector _lane = new LaneDetector();

        float _sep;
        bool _hasSep;
        bool _apart;
        int _leadDir;          // image-space direction of the foot that went out (+1 right, -1 left)
        float _sinceStep;
        int _totalSteps;
        bool _firstSideDone;

        protected override void OnWarmupComplete(Vector2[] kp) => _lane.CalibrateCenter(kp);

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) return LostCue();
            if (Warming(kp, dt)) return CueGetReady;

            _lane.Tick(kp, Confidence, dt);
            _sinceStep += dt;

            float hipW = HipWidth(kp);
            float hipMidX = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).x;
            Vector2 la = kp[MotionMath.LAnkle];
            Vector2 ra = kp[MotionMath.RAnkle];
            float rawSep = Mathf.Abs(ra.x - la.x) * horizontalScale / hipW;
            _sep = _hasSep ? MotionMath.Ema(_sep, rawSep, measureSmoothing) : rawSep;
            _hasSep = true;
            angleDeg = Mathf.RoundToInt(_sep * 100f); // informational: ankle spread in % of hip width

            if (!_apart)
            {
                if (_sep > apartRatio)
                {
                    _apart = true;
                    // Whichever ankle is further from the hip line is the one that stepped out.
                    float dl = (la.x - hipMidX) * horizontalScale;
                    float dr = (ra.x - hipMidX) * horizontalScale;
                    float lead = Mathf.Abs(dl) >= Mathf.Abs(dr) ? dl : dr;
                    _leadDir = lead >= 0f ? 1 : -1;
                }
            }
            else if (_sep < togetherRatio)
            {
                _apart = false;
                if (_sinceStep >= stepDebounceSeconds)
                {
                    _sinceStep = 0f;
                    // LaneDetector wins when it has something to say: real lateral travel beats a
                    // single frame's guess about which foot moved.
                    int dir = _lane.Lane != 0 ? _lane.Lane : _leadDir;

                    if (SideSign == 0)
                    {
                        if (RequiredSide != 0 && dir != RequiredSide) return CueSwitchSide;
                        SideSign = dir;
                    }
                    else if (dir != SideSign)
                    {
                        return CueSwitchSide;
                    }

                    Reps++;
                    _totalSteps++;
                    Kinex.Sfx.Play("checkpoint", 0.6f);
                    immediate = true;

                    if (Reps >= stepsPerSide)
                    {
                        if (_firstSideDone) return FinishSession(_totalSteps, stepsPerSide * 2);
                        _firstSideDone = true;
                        RequiredSide = -SideSign;
                        SideSign = 0;
                        Reps = 0;
                        return CueSwitchSide;
                    }
                    return CueStepGood;
                }
            }

            if (TrunkLean(kp) > trunkLeanRatio) return CueStandTall;
            return _apart ? CueFeetTogether : CueStepSide;
        }
    }
}
