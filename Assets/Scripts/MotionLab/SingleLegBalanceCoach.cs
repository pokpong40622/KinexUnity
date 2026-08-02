using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for single-leg balance ("ยืนขาเดียว") — hold 10 s on each leg.
    ///
    /// Thin wrapper over <see cref="SingleLegStanceDetector"/>, which already does all of the work:
    /// exactly-one-knee-raised, hips still at standing height, and a 1-second rolling stddev of hip
    /// X as a wobble measure. This coach only latches the side, runs the hold clock and picks a cue.
    ///
    /// The reported side is the STANCE leg — the leg being balanced ON, not the lifted one.
    ///
    /// Cue ids: get_ready, lift_foot, hold, steady, stand_tall, switch_side, not_in_frame, done.
    /// </summary>
    public class SingleLegBalanceCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Seconds the balance must be held on each leg.")]
        public float holdTargetSeconds = 10f;

        [Header("Form checks")]
        [Tooltip("Wobble (0..1 from SingleLegStanceDetector's rolling hip-X stddev) above which the " +
                 "user is told to steady up. The hold clock keeps running — this is advice, not a fail.")]
        [Range(0f, 1f)] public float steadyWobbleLimit = 0.55f;
        [Tooltip("Sideways offset between shoulder-mid and hip-mid as a fraction of torso length.")]
        public float trunkLeanRatio = 0.22f;

        const string CueLiftFoot = "lift_foot";

        protected override string PoseId => "single_leg_balance";
        protected override string Mode => "hold";
        protected override string SidesMode => "per-side";
        protected override int Target => Mathf.RoundToInt(holdTargetSeconds);

        readonly SingleLegStanceDetector _stance = new SingleLegStanceDetector();
        bool _firstSideDone;

        protected override void OnWarmupComplete(Vector2[] kp) => _stance.CalibrateStanding(kp);

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) { TickHold(false, dt, holdTargetSeconds); return LostCue(); }
            if (Warming(kp, dt)) return CueGetReady;

            _stance.Tick(kp, Confidence, dt);
            angleDeg = Mathf.RoundToInt(_stance.Wobble01 * 100f); // informational: wobble in %

            int stanceSide = _stance.StanceLegIsLeft ? -1 : 1;

            if (!_stance.IsHolding)
            {
                TickHold(false, dt, holdTargetSeconds);
                return CueLiftFoot;
            }

            // Latch on the first legal stance; after the switch, refuse the wrong leg.
            if (SideSign == 0)
            {
                if (RequiredSide != 0 && stanceSide != RequiredSide)
                {
                    TickHold(false, dt, holdTargetSeconds);
                    return CueSwitchSide;
                }
                SideSign = stanceSide;
            }
            else if (stanceSide != SideSign)
            {
                // They swapped legs mid-hold — that is not this side's hold any more.
                TickHold(false, dt, holdTargetSeconds);
                return CueSwitchSide;
            }

            TickHold(true, dt, holdTargetSeconds);

            if (HoldSeconds >= holdTargetSeconds)
            {
                // In hold mode Reps counts COMPLETED HOLDS (0 -> 1 -> 2), not repetitions;
                // Flutter draws the ring from 'hold' and ignores this.
                Reps++;
                ResetHold();
                immediate = true;
                if (_firstSideDone) return FinishSession(Reps, 2);
                _firstSideDone = true;
                RequiredSide = -SideSign;
                SideSign = 0;
                Kinex.Sfx.Play("checkpoint", 0.7f);
                return CueSwitchSide;
            }

            if (_stance.Wobble01 > steadyWobbleLimit) return CueSteady;
            if (TrunkLean(kp) > trunkLeanRatio) return CueStandTall;
            return CueHold;
        }
    }
}
