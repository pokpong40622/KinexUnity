using UnityEngine;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Maps a wire pose id (see docs/adr/2026-08-02-pose-coach-all-poses.md) to its coach component.
    /// One switch, no reflection, no registry — adding a pose is one line here plus one class.
    /// Unknown ids return null so the caller can fall back to the normal Motion Lab test range
    /// instead of entering a half-built coach.
    /// </summary>
    public static class PoseCoachFactory
    {
        public static PoseCoachBase Attach(GameObject host, string poseId)
        {
            switch (poseId)
            {
                case "sit_to_stand": return host.AddComponent<SitToStandCoach>();
                case "seated_knee_lift": return host.AddComponent<SeatedKneeLiftCoach>();
                case "hip_abduction": return host.AddComponent<HipAbductionCoach>();
                case "hip_extension": return host.AddComponent<HipExtensionCoach>();
                case "narrow_base_stand": return host.AddComponent<NarrowBaseStandCoach>();
                case "tandem_stand": return host.AddComponent<TandemStandCoach>();
                case "single_leg_balance": return host.AddComponent<SingleLegBalanceCoach>();
                case "tandem_walk": return host.AddComponent<TandemWalkCoach>();
                case "side_walk": return host.AddComponent<SideWalkCoach>();
                default: return null;
            }
        }
    }
}
