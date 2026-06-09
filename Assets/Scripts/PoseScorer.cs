using UnityEngine;

namespace Kinex.MegaDance
{
    /// <summary>
    /// Pure, camera-free pose similarity. A pose is reduced to 8 limb angles (2D)
    /// derived from COCO keypoints, then compared to a target signature.
    ///
    /// No scene/MonoBehaviour deps on purpose: callable from execute_code tests.
    /// The same input (Vector2[] keypoints + confidence) comes from a keyboard stub
    /// now and from PoseDetector's live keypoints later — swapping is a one-line connect.
    /// </summary>
    public static class PoseScorer
    {
        public const int NumLimbs = 8;

        // COCO / YOLOv8-pose keypoint indices (must match PoseDetector.cs:29-36).
        const int L_SHOULDER = 5,  R_SHOULDER = 6;
        const int L_ELBOW    = 7,  R_ELBOW    = 8;
        const int L_WRIST    = 9,  R_WRIST    = 10;
        const int L_HIP      = 11, R_HIP      = 12;
        const int L_KNEE     = 13, R_KNEE     = 14;
        const int L_ANKLE    = 15, R_ANKLE    = 16;

        // Per-limb endpoints. Order: L/R upper arm, L/R lower arm, L/R upper leg, L/R lower leg.
        static readonly int[] FromIdx = { L_SHOULDER, R_SHOULDER, L_ELBOW, R_ELBOW, L_HIP, R_HIP, L_KNEE, R_KNEE };
        static readonly int[] ToIdx   = { L_ELBOW,    R_ELBOW,    L_WRIST, R_WRIST, L_KNEE, R_KNEE, L_ANKLE, R_ANKLE };

        /// <summary>
        /// Angle (radians) of a from→to segment. Uses the SAME mirror/Y-negate
        /// convention as PoseDetector.DriveSegment (PoseDetector.cs:173-175) so that
        /// player keypoints and trainer-baked targets live in one consistent space.
        /// </summary>
        public static float SegmentAngle(Vector2 from, Vector2 to)
        {
            float dx = -(to.x - from.x); // negate X for correct mirroring
            float dy = -(to.y - from.y); // negate Y because screen Y is inverted
            return Mathf.Atan2(dy, dx);
        }

        /// <summary>Shortest signed-magnitude distance between two angles (radians).</summary>
        public static float AngleError(float a, float b)
        {
            float d = Mathf.Repeat(a - b + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            return Mathf.Abs(d);
        }

        /// <summary>
        /// Fill <paramref name="anglesOut"/> (length 8) with each limb's angle, and
        /// <paramref name="validOut"/> with whether both endpoints met confidence.
        /// Pass conf=null to treat every keypoint as confident (tests).
        /// </summary>
        public static void ComputeAngles(Vector2[] kp, float[] conf, float minConf,
                                         float[] anglesOut, bool[] validOut)
        {
            for (int i = 0; i < NumLimbs; i++)
            {
                int a = FromIdx[i], b = ToIdx[i];
                bool ok = conf == null || (conf[a] >= minConf && conf[b] >= minConf);
                validOut[i] = ok;
                anglesOut[i] = ok ? SegmentAngle(kp[a], kp[b]) : 0f;
            }
        }

        /// <summary>
        /// Score player keypoints against an 8-angle target signature (radians).
        /// Returns 0..1 = average over confident limbs of max(0, 1 - error/tolerance).
        /// Limbs whose keypoints fall below confidence are skipped (not penalised).
        /// </summary>
        public static float Score(Vector2[] kp, float[] conf, float[] target,
                                  float minConf, float toleranceRad)
        {
            if (target == null || target.Length < NumLimbs || toleranceRad <= 0f) return 0f;

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < NumLimbs; i++)
            {
                int a = FromIdx[i], b = ToIdx[i];
                if (conf != null && (conf[a] < minConf || conf[b] < minConf)) continue;

                float playerAngle = SegmentAngle(kp[a], kp[b]);
                float err = AngleError(playerAngle, target[i]);
                sum += Mathf.Max(0f, 1f - err / toleranceRad);
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }
    }
}
