using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for tandem walking ("เดินต่อเท้าเป็นเส้นตรง") — 10 steps.
    ///
    /// HONEST SCOPE (the ADR explicitly allows this): this coach COUNTS STEPS AND CHECKS THE LINE.
    /// It does NOT verify that each footfall is genuinely heel-to-toe. Judging heel-to-toe contact
    /// needs the heel and toe of the FRONT foot resolved to a few pixels while the body walks toward
    /// the lens, and a fixed tablet camera does not give that reliably. Rather than emit a
    /// confident-sounding cue off a signal that isn't there, the two things that ARE measurable are:
    ///
    ///   * a STEP — the nearer (lower in frame) ankle swaps from one foot to the other, and
    ///   * the LINE — the sideways gap between the ankles staying small, which is what actually
    ///     fails first when someone abandons the tandem line.
    ///
    /// 'heel_to_toe' is therefore emitted for a broken LINE, not for a broken foot placement.
    ///
    /// Cue ids: get_ready, walk_forward, heel_to_toe, step_good, go_slower, not_in_frame, done.
    /// </summary>
    public class TandemWalkCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Steps required to finish.")]
        public int stepsTarget = 10;

        [Header("Step detection (in hip widths)")]
        [Tooltip("Minimum vertical gap between the ankles before one counts as clearly the NEAR foot. " +
                 "Below this the feet are level and the lead is not switched — this is the whole " +
                 "hysteresis of the step counter, so raise it if steps double-count.")]
        public float leadMinRatio = 0.20f;
        [Tooltip("Minimum seconds between counted steps. A shallow shuffle can flicker the lead.")]
        public float stepDebounceSeconds = 0.45f;
        [Tooltip("A step landing sooner than this still counts, but cues 'go_slower'.")]
        public float fastStepSeconds = 0.7f;
        [Tooltip("Seconds without a step before the user is prompted to keep walking.")]
        public float idleSeconds = 2f;
        [Tooltip("Smoothing on the ankle measurements (0 = none, 1 = instant).")]
        [Range(0.05f, 1f)] public float measureSmoothing = 0.3f;

        [Header("Line check (in hip widths)")]
        [Tooltip("Sideways gap between the ankles beyond which the user has stepped off the line -> " +
                 "'heel_to_toe'. Normal side-by-side standing is about 1.0.")]
        public float lineMaxRatio = 0.55f;

        const string CueWalkForward = "walk_forward";
        const string CueHeelToToe = "heel_to_toe";
        const string CueStepGood = "step_good";

        protected override string PoseId => "tandem_walk";
        protected override string Mode => "reps";
        protected override string SidesMode => "none";
        protected override int Target => stepsTarget;

        float _line, _lead; // in hip widths; _lead is signed (+ = right ankle nearer the camera)
        bool _hasMeasure;
        int _leadSide;      // -1 left foot near, +1 right foot near, 0 = not established yet
        float _sinceStep;

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) return LostCue();
            if (Warming(kp, dt)) return CueGetReady;

            _sinceStep += dt;

            float hipW = HipWidth(kp);
            Vector2 la = kp[MotionMath.LAnkle];
            Vector2 ra = kp[MotionMath.RAnkle];
            float rawLine = Mathf.Abs(ra.x - la.x) * horizontalScale / hipW;
            float rawLead = (ra.y - la.y) / hipW; // + = right ankle lower in frame = nearer the camera

            if (!_hasMeasure) { _line = rawLine; _lead = rawLead; _hasMeasure = true; }
            else
            {
                _line = MotionMath.Ema(_line, rawLine, measureSmoothing);
                _lead = MotionMath.Ema(_lead, rawLead, measureSmoothing);
            }
            angleDeg = Mathf.RoundToInt(_line * 100f); // informational: ankle spread in % of hip width

            // A step is the near foot swapping over, once the gap is unambiguous.
            if (Mathf.Abs(_lead) > leadMinRatio)
            {
                int near = _lead > 0f ? 1 : -1;
                if (near != _leadSide)
                {
                    bool first = _leadSide == 0;
                    _leadSide = near;
                    if (!first && _sinceStep >= stepDebounceSeconds)
                    {
                        bool rushed = _sinceStep < fastStepSeconds;
                        _sinceStep = 0f;
                        Reps++;
                        Kinex.Sfx.Play("checkpoint", 0.6f);
                        immediate = true;
                        if (Reps >= stepsTarget) return FinishSession(Reps, stepsTarget);
                        return rushed ? CueGoSlower : CueStepGood;
                    }
                }
            }

            if (_line > lineMaxRatio) return CueHeelToToe;
            if (_sinceStep > idleSeconds) return CueWalkForward;
            return CueHold;
        }
    }
}
