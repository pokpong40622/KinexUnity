using UnityEngine;

namespace Collapse
{
    /// <summary>
    /// IPoseSource driven by The Dasher's proven camera+AI pipeline (the global
    /// <see cref="MediaPipePoseDetector"/> — the same component the shipping Dasher game uses).
    ///
    /// The friend's original <see cref="MediaPipePoseSource"/> ran its own LIVE_STREAM MediaPipe
    /// loop but did NOT canonicalise the Android front-camera's sensor rotation, so on the tablet
    /// it sees a sideways person. MediaPipePoseDetector already solves that (RotateFlip to upright,
    /// preview crop, calibration) and is validated on device, so we reuse its landmark stream and
    /// only re-do the Quake-specific classification here.
    ///
    /// This reads MediaPipePoseDetector.Landmarks33 (raw BlazePose image-space, top-left origin,
    /// y-down — exactly what PoseMath expects), builds a PoseSnapshot, and classifies into the same
    /// three PoseStates via the unchanged <see cref="PoseMath"/>. Hysteresis + auto-baseline mean
    /// no separate calibration screen is needed (the game direct-launches).
    ///
    /// Lives in Assembly-CSharp (Scripts/Core), NOT the Collapse.Pose asmdef, because it references
    /// MediaPipePoseDetector which is in Assembly-CSharp — an asmdef cannot reference the predefined
    /// assembly, so the classifier that needs both has to sit here (like GameManager/PoseSourceMux).
    /// </summary>
    public class DasherPoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Source")]
        [Tooltip("The Dasher camera+AI detector to read landmarks from. Wired by QuakeEscapeFixup.")]
        [SerializeField] private MediaPipePoseDetector detector;

        [Header("Classification")]
        [Tooltip("Frames a pose must hold before it becomes CurrentPose (de-jitter). 2 rather than 3 " +
                 "so a leg raise is credited a frame sooner; still enough to reject single-frame noise.")]
        [SerializeField] private int requiredStableFrames = 2;
        [Tooltip("Min landmark visibility for the body to count as tracked.")]
        [SerializeField] private float minVisibility = 0.5f;

        [Header("Device tuning (flip if a pose maps to the wrong side)")]
        [Tooltip("Swap which physical side maps to left/right. The tablet's front camera is a " +
                 "MIRROR, so BlazePose's anatomical left lands on the player's right — confirmed on " +
                 "device (lifting one leg raised the avatar's other leg). ON by default; flip back " +
                 "only if a future camera change un-mirrors the feed. Applies to arms as well as " +
                 "legs so the avatar mirrors the player consistently.")]
        [SerializeField] private bool swapLegSide = true;

        [Header("Baseline (standing-flat reference)")]
        [Tooltip("Seconds the body must be continuously visible before we auto-capture the " +
                 "standing-flat baseline that tiptoe detection needs. Player is expected to be " +
                 "standing normally during the game's opening countdown.")]
        [SerializeField] private float autoBaselineSettleSeconds = 1.0f;

        [Tooltip("How fast the standing baseline eases toward the player while they read as standing " +
                 "flat (0 = frozen forever, which goes stale and kills tiptoe detection).")]
        [SerializeField] private float baselineDriftPerSecond = 0.35f;

        // BlazePose 33-landmark indices.
        private const int L_SHOULDER = 11, R_SHOULDER = 12, L_ELBOW = 13, R_ELBOW = 14,
                          L_WRIST = 15, R_WRIST = 16,
                          L_HIP = 23, R_HIP = 24, L_KNEE = 25, R_KNEE = 26,
                          L_ANKLE = 27, R_ANKLE = 28, L_HEEL = 29, R_HEEL = 30,
                          L_TOE = 31, R_TOE = 32;

        private PoseBaseline baseline;
        private float bodyVisibleFor;

        private PoseState candidatePose = PoseState.None;
        private int candidateStreak;
        private PoseState currentPose = PoseState.None;

        public PoseState CurrentPose => currentPose;

        public bool HasBaseline => baseline.isSet;

        /// <summary>The underlying camera+AI detector, for systems that need raw landmarks directly
        /// (e.g. Kinex.Shared.SosController's fall detection).</summary>
        public MediaPipePoseDetector Detector => detector;

        /// <summary>The player's arms this frame, for the avatar to mirror. Invalid until tracked.</summary>
        public ArmPose Arms { get; private set; }

        public bool IsTracking =>
            detector != null && detector.HasPose && BuildSnapshot(out var s) && PoseMath.IsBodyVisible(s, minVisibility);

        private void LateUpdate()
        {
            if (detector == null || !detector.HasPose || !BuildSnapshot(out PoseSnapshot snap))
            {
                bodyVisibleFor = 0f;
                Arms = default;
                Settle(PoseState.None);
                return;
            }

            Arms = PoseMath.ReadArms(snap);

            bool visible = PoseMath.IsBodyVisible(snap, minVisibility);
            if (!visible)
            {
                bodyVisibleFor = 0f;
                Settle(PoseState.None);
                return;
            }

            // Auto-capture the standing-flat baseline once the player has been steadily visible.
            // Re-captures after any tracking dropout (bodyVisibleFor resets above).
            bodyVisibleFor += Time.deltaTime;
            if (!baseline.isSet && bodyVisibleFor >= autoBaselineSettleSeconds)
            {
                baseline = PoseMath.CaptureBaseline(snap);
            }

            PoseState classified = PoseMath.Classify(snap, baseline);

            // While the player is standing flat (nothing detected), let the baseline follow them.
            // A frozen baseline is what made tiptoe undetectable: step nearer the camera, or let
            // posture settle, and the stored hip/shoulder heights no longer describe "standing".
            if (classified == PoseState.None && baseline.isSet)
            {
                baseline = PoseMath.DriftBaseline(baseline, snap,
                    Mathf.Clamp01(baselineDriftPerSecond * Time.deltaTime));
            }

            Settle(classified);
        }

        /// <summary>Re-arm the standing-flat baseline (e.g. from a "recalibrate" button).</summary>
        public void CaptureBaseline()
        {
            if (detector != null && detector.HasPose && BuildSnapshot(out PoseSnapshot snap) &&
                PoseMath.IsBodyVisible(snap, minVisibility))
            {
                baseline = PoseMath.CaptureBaseline(snap);
            }
        }

        private void Settle(PoseState classified)
        {
            if (classified == candidatePose)
            {
                candidateStreak++;
            }
            else
            {
                candidatePose = classified;
                candidateStreak = 1;
            }

            if (candidateStreak >= requiredStableFrames)
            {
                currentPose = candidatePose;
            }
        }

        /// <summary>
        /// Maps the detector's 33 image-space landmarks to the 10-point PoseSnapshot. Returns false
        /// until the detector has produced at least one frame. swapLegSide exchanges the left/right
        /// landmark pairs so the OneLegLeft/OneLegRight mapping can be corrected on device.
        /// </summary>
        private bool BuildSnapshot(out PoseSnapshot s)
        {
            s = default;
            var lm = detector != null ? detector.Landmarks33 : null;
            if (lm == null || lm.Length < 33) return false;

            // One flag flips EVERY left/right pair, so legs and arms always agree on which side is
            // which. Reading each pair through this helper keeps that impossible to get half-right.
            s.leftShoulder = Pick(lm, L_SHOULDER, R_SHOULDER); s.rightShoulder = Pick(lm, R_SHOULDER, L_SHOULDER);
            s.leftElbow = Pick(lm, L_ELBOW, R_ELBOW);          s.rightElbow = Pick(lm, R_ELBOW, L_ELBOW);
            s.leftWrist = Pick(lm, L_WRIST, R_WRIST);          s.rightWrist = Pick(lm, R_WRIST, L_WRIST);
            s.leftHip = Pick(lm, L_HIP, R_HIP);                s.rightHip = Pick(lm, R_HIP, L_HIP);
            s.leftKnee = Pick(lm, L_KNEE, R_KNEE);             s.rightKnee = Pick(lm, R_KNEE, L_KNEE);
            s.leftAnkle = Pick(lm, L_ANKLE, R_ANKLE);          s.rightAnkle = Pick(lm, R_ANKLE, L_ANKLE);
            s.leftHeel = Pick(lm, L_HEEL, R_HEEL);             s.rightHeel = Pick(lm, R_HEEL, L_HEEL);
            s.leftToe = Pick(lm, L_TOE, R_TOE);                s.rightToe = Pick(lm, R_TOE, L_TOE);
            return true;
        }

        /// <summary>Reads landmark <paramref name="normal"/>, or <paramref name="mirrored"/> when swapped.</summary>
        private PoseLandmark Pick(MediaPipePoseDetector.NormLandmark[] lm, int normal, int mirrored) =>
            P(lm[swapLegSide ? mirrored : normal]);

        private static PoseLandmark P(MediaPipePoseDetector.NormLandmark n) =>
            new PoseLandmark { x = n.x, y = n.y, visibility = n.visibility };
    }
}
