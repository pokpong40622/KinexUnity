using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for tandem (heel-to-toe) standing ("ยืนต่อเท้าเป็นเส้นตรง") — hold 10 s with each
    /// foot in front.
    ///
    /// New geometry, because no existing detector describes a stance this way. Head-on, a tandem
    /// stance has two signatures at once:
    ///   1. the ankles collapse onto nearly the SAME image X (they are in line with each other), and
    ///   2. one ankle is clearly LOWER in the image than the other — the front foot is nearer the
    ///      camera, and a floor-level point that is nearer projects further down the frame.
    ///
    /// Signature 2 is also what names the side: the reported side is the foot IN FRONT. Ankle Y is
    /// used for it rather than MediaPipe's z, which is far noisier at foot level.
    ///
    /// <see cref="SingleLegStanceDetector"/> is reused purely for its Wobble01 (rolling stddev of
    /// hip X); it is not asked whether anything is "holding".
    ///
    /// Cue ids: get_ready, feet_in_line, hold, steady, stand_tall, switch_side, not_in_frame, done.
    /// </summary>
    public class TandemStandCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Seconds the stance must be held with each foot in front.")]
        public float holdTargetSeconds = 10f;

        [Header("Ankle-line geometry (in hip widths)")]
        [Tooltip("Maximum sideways gap between the two ankles for them to count as 'in line'. " +
                 "Normal standing is about one hip width, so this must be well under 1.")]
        public float alignMaxRatio = 0.45f;
        [Tooltip("Minimum vertical gap between the two ankles before one counts as clearly IN FRONT. " +
                 "Too small and camera noise picks the side at random; too large and a real tandem " +
                 "stance never registers.")]
        public float frontMinRatio = 0.18f;
        [Tooltip("Smoothing on both ankle measurements (0 = none, 1 = instant).")]
        [Range(0.05f, 1f)] public float measureSmoothing = 0.3f;

        [Header("Form checks")]
        [Tooltip("Wobble (0..1) above which the user is told to steady up. Advice only — the hold clock " +
                 "keeps running.")]
        [Range(0f, 1f)] public float steadyWobbleLimit = 0.55f;
        [Tooltip("Sideways offset between shoulder-mid and hip-mid as a fraction of torso length.")]
        public float trunkLeanRatio = 0.22f;

        const string CueFeetInLine = "feet_in_line";

        protected override string PoseId => "tandem_stand";
        protected override string Mode => "hold";
        protected override string SidesMode => "per-side";
        protected override int Target => Mathf.RoundToInt(holdTargetSeconds);

        readonly SingleLegStanceDetector _sway = new SingleLegStanceDetector(); // Wobble01 only

        float _align, _front; // both in hip widths; _front is signed (+ = right foot in front)
        bool _hasMeasure;
        bool _firstSideDone;

        protected override void OnWarmupComplete(Vector2[] kp) => _sway.CalibrateStanding(kp);

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) { TickHold(false, dt, holdTargetSeconds); return LostCue(); }
            if (Warming(kp, dt)) return CueGetReady;

            _sway.Tick(kp, Confidence, dt);

            float hipW = HipWidth(kp);
            Vector2 la = kp[MotionMath.LAnkle];
            Vector2 ra = kp[MotionMath.RAnkle];
            float rawAlign = Mathf.Abs(ra.x - la.x) * horizontalScale / hipW;
            float rawFront = (ra.y - la.y) / hipW; // + = right ankle lower in frame = right foot in front

            if (!_hasMeasure) { _align = rawAlign; _front = rawFront; _hasMeasure = true; }
            else
            {
                _align = MotionMath.Ema(_align, rawAlign, measureSmoothing);
                _front = MotionMath.Ema(_front, rawFront, measureSmoothing);
            }
            angleDeg = Mathf.RoundToInt(_align * 100f); // informational: ankle spread in % of hip width

            bool inLine = _align < alignMaxRatio && Mathf.Abs(_front) > frontMinRatio;
            if (!inLine)
            {
                TickHold(false, dt, holdTargetSeconds);
                return CueFeetInLine;
            }

            int frontSide = _front > 0f ? 1 : -1;

            if (SideSign == 0)
            {
                if (RequiredSide != 0 && frontSide != RequiredSide)
                {
                    TickHold(false, dt, holdTargetSeconds);
                    return CueSwitchSide;
                }
                SideSign = frontSide;
            }
            else if (frontSide != SideSign)
            {
                TickHold(false, dt, holdTargetSeconds);
                return CueSwitchSide;
            }

            TickHold(true, dt, holdTargetSeconds);

            if (HoldSeconds >= holdTargetSeconds)
            {
                Reps++;               // holds completed (0 -> 1 -> 2)
                ResetHold();
                immediate = true;
                if (_firstSideDone) return FinishSession(Reps, 2);
                _firstSideDone = true;
                RequiredSide = -SideSign;
                SideSign = 0;
                Kinex.Sfx.Play("checkpoint", 0.7f);
                return CueSwitchSide;
            }

            if (_sway.Wobble01 > steadyWobbleLimit) return CueSteady;
            if (TrunkLean(kp) > trunkLeanRatio) return CueStandTall;
            return CueHold;
        }
    }
}
