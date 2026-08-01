using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Shared math + COCO keypoint indices for the whole Motion layer. Plain, camera-free
    /// helpers (mirrors the style of <see cref="Kinex.MegaDance.PoseScorer"/>) so every
    /// detector below reads/derives thresholds the same way.
    /// </summary>
    public static class MotionMath
    {
        // COCO / YOLOv8-pose keypoint indices (must match MediaPipePoseDetector's BuildCocoKeypoints).
        public const int Nose = 0;
        public const int LShoulder = 5, RShoulder = 6;
        public const int LElbow = 7, RElbow = 8;
        public const int LWrist = 9, RWrist = 10;
        public const int LHip = 11, RHip = 12;
        public const int LKnee = 13, RKnee = 14;
        public const int LAnkle = 15, RAnkle = 16;

        public static Vector2 Mid(Vector2 a, Vector2 b) => (a + b) * 0.5f;

        /// <summary>Hip-mid to shoulder-mid distance — the per-player scale unit every detector's thresholds ride on.</summary>
        public static float TorsoLen(Vector2[] kp) => Vector2.Distance(Mid(kp[LShoulder], kp[RShoulder]), Mid(kp[LHip], kp[RHip]));

        public static float ShoulderWidth(Vector2[] kp) => Vector2.Distance(kp[LShoulder], kp[RShoulder]);

        /// <summary>True only if every listed joint's confidence is at least <paramref name="min"/>.</summary>
        public static bool Valid(float[] conf, float min, params int[] joints)
        {
            for (int i = 0; i < joints.Length; i++)
                if (conf[joints[i]] < min) return false;
            return true;
        }

        // Fixed-arity overloads for the per-frame detector hot path — the params[] overload above
        // allocates a fresh array on every call; these let the ~7 calls/frame in DasherSitClassifier/
        // LaneDetector/LegAbductionDetector skip that allocation entirely.
        public static bool Valid(float[] conf, float min, int a) => conf[a] >= min;
        public static bool Valid(float[] conf, float min, int a, int b) => conf[a] >= min && conf[b] >= min;
        public static bool Valid(float[] conf, float min, int a, int b, int c) =>
            conf[a] >= min && conf[b] >= min && conf[c] >= min;
        public static bool Valid(float[] conf, float min, int a, int b, int c, int d) =>
            conf[a] >= min && conf[b] >= min && conf[c] >= min && conf[d] >= min;
        public static bool Valid(float[] conf, float min, int a, int b, int c, int d, int e, int f) =>
            conf[a] >= min && conf[b] >= min && conf[c] >= min &&
            conf[d] >= min && conf[e] >= min && conf[f] >= min;

        /// <summary>Exponential moving average: state += alpha * (value - state).</summary>
        public static float Ema(float state, float value, float alpha) => state + alpha * (value - state);
    }
}
