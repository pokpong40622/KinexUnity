using UnityEngine;
using UnityEditor;
using Kinex.MegaDance;

namespace Kinex.MegaDance.EditorTools
{
    /// <summary>
    /// OFFLINE self-test for the pure pose scorer/baker math. No camera, no scene, no Play
    /// mode needed — runs in the editor via the menu and logs the result. Proves that an ideal
    /// T-pose scores ~100% against a baker-generated target, and a clearly wrong pose scores low.
    ///
    /// Run: menu  Kinex → Tests → Pose Scorer Self-Test  (results in the Console).
    ///
    /// KEY CONVENTION (verified by this test): PoseScorer expects RAW, un-mirrored MediaPipe
    /// image keypoints (person's anatomical-LEFT on the LEFT half of the image). That is exactly
    /// what MediaPipePoseDetector.BuildCocoKeypoints stores (lm.x with no mirror). With that
    /// convention every limb matches at err=0 and the ideal T-pose scores 1.000. If the live
    /// keypoints were mirrored (selfie), the arms would land 180° off and the score would cap
    /// near 0.50 — so a low live score points at the camera→keypoint orientation, NOT this math.
    /// </summary>
    public static class PoseScorerSelfTest
    {
        static readonly string[] LimbNames =
            { "L_uArm", "R_uArm", "L_lArm", "R_lArm", "L_uLeg", "R_uLeg", "L_lLeg", "R_lLeg" };

        [MenuItem("Kinex/Tests/Pose Scorer Self-Test")]
        public static void Run()
        {
            float[] target = BakeCanonicalTpose();

            Vector2[] ideal = RawTposeKeypoints();
            float[] conf = Ones(17);
            float tol = 45f * Mathf.Deg2Rad;

            float idealScore = PoseScorer.Score(ideal, conf, target, 0.3f, tol);
            LogBreakdown("IDEAL raw T-pose", ideal, conf, target, idealScore);

            // Clearly wrong: arms down at the sides (everything else identical).
            Vector2[] wrong = (Vector2[])ideal.Clone();
            wrong[7]  = new Vector2(0.43f, 0.45f); // L_ELBOW below L_SHOULDER
            wrong[8]  = new Vector2(0.57f, 0.45f); // R_ELBOW below R_SHOULDER
            wrong[9]  = new Vector2(0.43f, 0.60f); // L_WRIST further down
            wrong[10] = new Vector2(0.57f, 0.60f); // R_WRIST further down
            float wrongScore = PoseScorer.Score(wrong, conf, target, 0.3f, tol);

            bool idealPass = idealScore >= 0.9f;
            bool wrongPass = wrongScore < 0.6f;
            Debug.Log($"[PoseScorerSelfTest] IDEAL={idealScore:F3} (expect >=0.9 -> {(idealPass ? "PASS" : "FAIL")})   " +
                      $"WRONG(arms down)={wrongScore:F3} (expect <0.6 -> {(wrongPass ? "PASS" : "FAIL")})");
            if (idealPass && wrongPass)
                Debug.Log("[PoseScorerSelfTest] RESULT: PASS — scorer/baker math is correct. A low LIVE score is " +
                          "therefore in the camera→keypoint path (orientation/confidence), not in this math.");
            else
                Debug.LogError("[PoseScorerSelfTest] RESULT: FAIL — the bug is in the pure scorer/baker space.");
        }

        // Build the "pose 1" target the SAME way the game does: project rig joint world positions
        // through PoseSignatureBaker, then ComputeAngles. We feed a canonical T-pose rig pose
        // (arms straight out along world X, legs straight down along -Y) so the baker output is
        // the exact target the scorer is compared against.
        static float[] BakeCanonicalTpose()
        {
            // COCO 5..16 humanoid joint world positions for a frontal T-pose. Character-left arm
            // points to world +X (baker mirrorX then maps it into image space identically for all).
            var w = new Vector3[17];
            w[5]  = new Vector3( 0.2f, 1.4f, 0); w[6]  = new Vector3(-0.2f, 1.4f, 0); // shoulders
            w[7]  = new Vector3( 0.5f, 1.4f, 0); w[8]  = new Vector3(-0.5f, 1.4f, 0); // elbows
            w[9]  = new Vector3( 0.8f, 1.4f, 0); w[10] = new Vector3(-0.8f, 1.4f, 0); // wrists
            w[11] = new Vector3( 0.1f, 0.9f, 0); w[12] = new Vector3(-0.1f, 0.9f, 0); // hips
            w[13] = new Vector3( 0.1f, 0.5f, 0); w[14] = new Vector3(-0.1f, 0.5f, 0); // knees
            w[15] = new Vector3( 0.1f, 0.1f, 0); w[16] = new Vector3(-0.1f, 0.1f, 0); // ankles

            // Mirror the baker's Project() + ComputeAngles path exactly (it's a private detail of
            // the baker; we reproduce it here from the joint world positions so no rig is needed).
            var kp = new Vector2[17];
            for (int coco = 5; coco <= 16; coco++)
            {
                // PoseSignatureBaker.Project with mirrorX=true: u=-x, v=-y.
                kp[coco] = new Vector2(-w[coco].x, -w[coco].y);
            }
            var angles = new float[PoseScorer.NumLimbs];
            var valid = new bool[PoseScorer.NumLimbs];
            PoseScorer.ComputeAngles(kp, null, 0f, angles, valid);
            return angles;
        }

        // Ideal player T-pose in RAW MediaPipe normalized image coords (y DOWN, NOT mirrored):
        // anatomical-LEFT on the LEFT half of the image.
        static Vector2[] RawTposeKeypoints()
        {
            var kp = new Vector2[17];
            kp[5]  = new Vector2(0.40f, 0.30f); kp[6]  = new Vector2(0.60f, 0.30f); // shoulders
            kp[7]  = new Vector2(0.25f, 0.30f); kp[8]  = new Vector2(0.75f, 0.30f); // elbows
            kp[9]  = new Vector2(0.10f, 0.30f); kp[10] = new Vector2(0.90f, 0.30f); // wrists
            kp[11] = new Vector2(0.43f, 0.60f); kp[12] = new Vector2(0.57f, 0.60f); // hips
            kp[13] = new Vector2(0.43f, 0.80f); kp[14] = new Vector2(0.57f, 0.80f); // knees
            kp[15] = new Vector2(0.43f, 0.95f); kp[16] = new Vector2(0.57f, 0.95f); // ankles
            return kp;
        }

        static float[] Ones(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = 1f; return a; }

        static void LogBreakdown(string label, Vector2[] kp, float[] conf, float[] target, float score)
        {
            var a = new float[PoseScorer.NumLimbs];
            var v = new bool[PoseScorer.NumLimbs];
            PoseScorer.ComputeAngles(kp, conf, 0.3f, a, v);
            var sb = new System.Text.StringBuilder();
            sb.Append($"[PoseScorerSelfTest] {label} score={score:F3}");
            for (int i = 0; i < PoseScorer.NumLimbs; i++)
            {
                float err = PoseScorer.AngleError(a[i], target[i]) * Mathf.Rad2Deg;
                sb.Append($"  {LimbNames[i]}:p{a[i] * Mathf.Rad2Deg:F0}/t{target[i] * Mathf.Rad2Deg:F0}/e{err:F0}");
            }
            Debug.Log(sb.ToString());
        }
    }
}
