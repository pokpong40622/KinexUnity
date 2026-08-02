using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for the chair stand ("ลุก-นั่งจากเก้าอี้") — 15 reps, unsided.
    ///
    /// Built on <see cref="SitStandDetector"/>, the same state machine TheDasher and the Motion Lab
    /// test range use. The user is expected to START SEATED (Flutter's wizard says so): the warm-up
    /// captures the seated baseline and derives the standing one from it, which is exactly the
    /// calibration path SitStandDetector was designed for.
    ///
    /// Cue ids: get_ready, sit_first, stand_up, sit_down, go_slower, rep_good, not_in_frame, done.
    ///
    /// NOT judged: 'knees_behind_toes'. Knee-over-toe travel is only measurable from a SIDE view and
    /// this coach is framed head-on, so emitting it would be guessing. The cue id stays reserved.
    /// </summary>
    public class SitToStandCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Stands required to finish the set.")]
        public int repsTarget = 15;
        [Tooltip("A full stand banked faster than this is still counted, but cues 'go_slower' — " +
                 "popping out of a chair is how people fall.")]
        public float fastRepSeconds = 1.2f;

        const string CueSitFirst = "sit_first";
        const string CueStandUp = "stand_up";
        const string CueSitDown = "sit_down";

        // Seated + shoulders is the minimum this needs: a table or the chair back routinely hides
        // knees and ankles, and SitStandDetector already falls back to the shoulder line.
        static readonly int[] Joints =
        {
            MotionMath.LShoulder, MotionMath.RShoulder,
            MotionMath.LHip, MotionMath.RHip,
        };

        protected override string PoseId => "sit_to_stand";
        protected override string Mode => "reps";
        protected override string SidesMode => "none";
        protected override int Target => repsTarget;
        protected override int[] RequiredJoints => Joints;

        readonly SitStandDetector _sitStand = new SitStandDetector();
        float _sinceLastRep;

        protected override void OnWarmupComplete(Vector2[] kp)
        {
            _sitStand.CalibrateSeated(kp);
            _sitStand.EstimateStandingFromSeated();
        }

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) return LostCue();

            // Warm-up doubles as the "sit down first" instruction: we cannot measure a chair stand
            // until we have seen what this user's seated hip line looks like.
            if (Warming(kp, dt)) return CueSitFirst;

            _sitStand.Tick(kp, Confidence, dt);
            angleDeg = Mathf.RoundToInt(_sitStand.Progress01 * 100f); // informational: 0 seated, 100 standing
            _sinceLastRep += dt;

            if (_sitStand.JustStood)
            {
                Reps++;
                Kinex.Sfx.Play("checkpoint", 0.6f);
                bool rushed = _sinceLastRep < fastRepSeconds;
                _sinceLastRep = 0f;
                immediate = true;
                if (Reps >= repsTarget) return FinishSession(Reps, repsTarget);
                return rushed ? CueGoSlower : CueRepGood;
            }

            switch (_sitStand.Current)
            {
                case SitStandDetector.SitStandPhase.Standing:
                case SitStandDetector.SitStandPhase.Sitting:
                    return CueSitDown;
                default:
                    return CueStandUp;
            }
        }
    }
}
