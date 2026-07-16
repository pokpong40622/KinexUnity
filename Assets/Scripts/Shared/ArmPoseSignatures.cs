using UnityEngine;

namespace Kinex
{
    /// <summary>
    /// Three 8-limb-angle targets for Bridge-beat arm scoring, derived ONCE (lazy, cached) from
    /// synthetic 17-keypoint skeletons run through the SAME PoseScorer.ComputeAngles math the
    /// live player is scored with — no ScriptableObject, no editor baking, just data + math.
    /// Every synthetic pose stands with the LEFT knee raised (Bridge is a single-leg-stance
    /// beat), so the leg-angle half of the signature reinforces the balance pose too — a player
    /// who does the right arm shape but is standing on both feet will score lower on the leg
    /// limbs, which is the intended behaviour.
    ///
    /// Coordinates are a generic "camera facing the player" image space (x right, y down) —
    /// self-consistent with how PoseScorer.SegmentAngle/ComputeAngles are used everywhere else in
    /// the project. Exact units/scale don't matter, only the relative angles between joints do.
    /// </summary>
    public static class ArmPoseSignatures
    {
        static float[] _armsOut, _handsStacked, _armsCrossed;

        public static float[] ArmsOut => _armsOut ??= Bake(
            elbowL: new Vector2(0.55f, 0.35f), elbowR: new Vector2(-0.55f, 0.35f),
            wristL: new Vector2(0.85f, 0.35f), wristR: new Vector2(-0.85f, 0.35f));

        public static float[] HandsStacked => _handsStacked ??= Bake(
            elbowL: new Vector2(0.35f, 0.55f), elbowR: new Vector2(-0.35f, 0.55f),
            wristL: new Vector2(0.05f, 0.50f), wristR: new Vector2(-0.05f, 0.50f));

        public static float[] ArmsCrossed => _armsCrossed ??= Bake(
            elbowL: new Vector2(0.45f, 0.45f), elbowR: new Vector2(-0.45f, 0.45f),
            wristL: new Vector2(-0.15f, 0.40f), wristR: new Vector2(0.15f, 0.40f));

        static float[] Bake(Vector2 elbowL, Vector2 elbowR, Vector2 wristL, Vector2 wristR)
        {
            var kp = new Vector2[17];
            kp[0] = new Vector2(0f, 0.15f);                  // nose
            kp[5] = new Vector2(0.22f, 0.35f);                // L shoulder
            kp[6] = new Vector2(-0.22f, 0.35f);               // R shoulder
            kp[7] = elbowL;                                   // L elbow
            kp[8] = elbowR;                                   // R elbow
            kp[9] = wristL;                                   // L wrist
            kp[10] = wristR;                                  // R wrist
            kp[11] = new Vector2(0.15f, 0.95f);               // L hip
            kp[12] = new Vector2(-0.15f, 0.95f);              // R hip
            kp[13] = new Vector2(0.18f, 0.80f);               // L knee (raised — Bridge is single-leg)
            kp[14] = new Vector2(-0.16f, 1.35f);              // R knee (standing leg)
            kp[15] = new Vector2(0.16f, 0.55f);               // L ankle (tucked, raised leg)
            kp[16] = new Vector2(-0.17f, 1.75f);              // R ankle (standing leg)

            var angles = new float[Kinex.MegaDance.PoseScorer.NumLimbs];
            var valid = new bool[Kinex.MegaDance.PoseScorer.NumLimbs];
            Kinex.MegaDance.PoseScorer.ComputeAngles(kp, null, 0f, angles, valid);
            return angles;
        }
    }
}
