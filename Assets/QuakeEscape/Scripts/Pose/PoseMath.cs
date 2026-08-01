using UnityEngine;

namespace Collapse
{
    /// <summary>
    /// Pure pose-classification math. No MediaPipe types, no MonoBehaviour — only UnityEngine.Mathf
    /// is used, so this is safe to unit test directly.
    /// </summary>
    public struct PoseLandmark
    {
        public float x;
        public float y;
        public float visibility;
    }

    /// <summary>
    /// Snapshot of the landmarks needed for pose classification and arm mirroring.
    /// MediaPipe indices: shoulders 11/12, elbows 13/14, wrists 15/16,
    /// hips 23/24, knees 25/26, ankles 27/28, heels 29/30, toes 31/32.
    /// </summary>
    public struct PoseSnapshot
    {
        public PoseLandmark leftShoulder, rightShoulder; // 11, 12
        public PoseLandmark leftElbow, rightElbow;       // 13, 14
        public PoseLandmark leftWrist, rightWrist;       // 15, 16
        public PoseLandmark leftHip, rightHip;           // 23, 24
        public PoseLandmark leftKnee, rightKnee;         // 25, 26
        public PoseLandmark leftAnkle, rightAnkle;       // 27, 28
        public PoseLandmark leftHeel, rightHeel;         // 29, 30
        public PoseLandmark leftToe, rightToe;           // 31, 32
    }

    /// <summary>
    /// Standing-still reference captured once per calibration. Tiptoe is detected as a rise of the
    /// whole upper body away from this reference, measured in TORSO LENGTHS so it works at any
    /// distance from the camera.
    /// </summary>
    public struct PoseBaseline
    {
        public float hipY;
        public float shoulderY;
        public float torsoLen;
        public float leftHeelToeDy;
        public float rightHeelToeDy;
        public bool isSet;
    }

    /// <summary>Normalised arm pose (per side) used to mirror the player's arms onto the avatar.</summary>
    public struct ArmPose
    {
        /// <summary>-1 = hanging at the side, 0 = out horizontal, +1 = straight overhead.</summary>
        public float leftUp, rightUp;
        /// <summary>0 = elbow fully bent, 1 = elbow straight.</summary>
        public float leftStraight, rightStraight;
        public bool isValid;
    }

    public static class PoseMath
    {
        public const float HeelRaiseThreshold = 0.012f;
        /// <summary>
        /// How far apart the two ankles must sit (as a fraction of shin length) before it counts as
        /// one leg being lifted. Lowered from 0.45 so the pose registers earlier in the lift —
        /// players shouldn't have to raise the knee all the way to be credited. Kept comfortably
        /// above the small asymmetry a two-footed tiptoe produces, so it can't steal that pose.
        /// </summary>
        public const float AnkleLiftShinFactor = 0.32f;

        /// <summary>
        /// Body rise that counts as a tiptoe, as a FRACTION OF TORSO LENGTH. Matches the value the
        /// shipping Dasher's TiptoeDetector uses (it is validated on device); measuring in torso
        /// lengths is what makes this work at any distance, which the old absolute normalised-y
        /// threshold did not.
        /// </summary>
        public const float TiptoeRiseFactor = 0.055f;

        /// <summary>
        /// True when hips, knees, and ankles are all tracked with at least minVisibility confidence.
        /// </summary>
        public static bool IsBodyVisible(in PoseSnapshot s, float minVisibility = 0.5f)
        {
            return s.leftHip.visibility >= minVisibility &&
                   s.rightHip.visibility >= minVisibility &&
                   s.leftKnee.visibility >= minVisibility &&
                   s.rightKnee.visibility >= minVisibility &&
                   s.leftAnkle.visibility >= minVisibility &&
                   s.rightAnkle.visibility >= minVisibility;
        }

        public static float HipY(in PoseSnapshot s) => (s.leftHip.y + s.rightHip.y) * 0.5f;
        public static float ShoulderY(in PoseSnapshot s) => (s.leftShoulder.y + s.rightShoulder.y) * 0.5f;

        /// <summary>Shoulder-mid to hip-mid distance: the per-player scale unit thresholds ride on.</summary>
        public static float TorsoLen(in PoseSnapshot s)
        {
            float dx = (s.leftShoulder.x + s.rightShoulder.x) * 0.5f - (s.leftHip.x + s.rightHip.x) * 0.5f;
            float dy = ShoulderY(s) - HipY(s);
            return Mathf.Max(Mathf.Sqrt(dx * dx + dy * dy), 0.01f);
        }

        /// <summary>
        /// Captures a standing-flat reference from the current snapshot: hip/shoulder height, torso
        /// length, and each foot's heel-to-toe y delta.
        /// </summary>
        public static PoseBaseline CaptureBaseline(in PoseSnapshot s)
        {
            PoseBaseline b;
            b.hipY = HipY(s);
            b.shoulderY = ShoulderY(s);
            b.torsoLen = TorsoLen(s);
            b.leftHeelToeDy = s.leftHeel.y - s.leftToe.y;
            b.rightHeelToeDy = s.rightHeel.y - s.rightToe.y;
            b.isSet = true;
            return b;
        }

        /// <summary>
        /// Eases the baseline toward the current snapshot. Called only while the player reads as
        /// standing flat, so the reference tracks slow drift (stepping nearer the camera, posture
        /// settling) instead of going stale the moment they move — the failure that stopped tiptoe
        /// from ever firing on device.
        /// </summary>
        public static PoseBaseline DriftBaseline(in PoseBaseline b, in PoseSnapshot s, float alpha)
        {
            if (!b.isSet) return CaptureBaseline(s);
            PoseBaseline o = b;
            o.hipY = Mathf.Lerp(b.hipY, HipY(s), alpha);
            o.shoulderY = Mathf.Lerp(b.shoulderY, ShoulderY(s), alpha);
            o.torsoLen = Mathf.Lerp(b.torsoLen, TorsoLen(s), alpha);
            o.leftHeelToeDy = Mathf.Lerp(b.leftHeelToeDy, s.leftHeel.y - s.leftToe.y, alpha);
            o.rightHeelToeDy = Mathf.Lerp(b.rightHeelToeDy, s.rightHeel.y - s.rightToe.y, alpha);
            return o;
        }

        /// <summary>How far the body has risen above the baseline, in torso lengths.</summary>
        public static float BodyRise(in PoseSnapshot s, in PoseBaseline b)
        {
            if (!b.isSet) return 0f;
            // y is top-left origin (y-down), so a SMALLER y is higher on screen.
            float rise = ((b.hipY - HipY(s)) + (b.shoulderY - ShoulderY(s))) * 0.5f;
            return rise / Mathf.Max(b.torsoLen, 0.01f);
        }

        /// <summary>
        /// Classifies the current snapshot against the captured baseline. y is normalized [0,1],
        /// top-left origin (y-down): smaller y = higher on screen.
        /// </summary>
        public static PoseState Classify(in PoseSnapshot s, in PoseBaseline b)
        {
            if (!IsBodyVisible(s) || !b.isSet)
            {
                return PoseState.None;
            }

            float leftShin = Mathf.Abs(s.leftKnee.y - s.leftAnkle.y);
            float rightShin = Mathf.Abs(s.rightKnee.y - s.rightAnkle.y);
            float shin = Mathf.Max((leftShin + rightShin) * 0.5f, 0.01f);

            float ankleDy = Mathf.Abs(s.leftAnkle.y - s.rightAnkle.y);
            if (ankleDy > AnkleLiftShinFactor * shin)
            {
                // The lifted leg's ankle is higher on screen (smaller y). The other leg is standing.
                bool leftAnkleHigher = s.leftAnkle.y < s.rightAnkle.y;
                bool standingIsLeft = !leftAnkleHigher; // standing leg is the one NOT lifted
                return standingIsLeft ? PoseState.OneLegLeft : PoseState.OneLegRight;
            }

            // Tiptoe: the whole body rises onto the balls of both feet. Measure that rise on the
            // HIPS AND SHOULDERS — they are BlazePose's best-tracked landmarks — and scale it by
            // torso length so the same threshold holds at any distance from the camera. The heels
            // and toes (29-32) are the noisiest points in the model and are frequently cropped off
            // the bottom of a tablet frame, so they only ever ADD confidence here; they can never
            // veto a tiptoe the body rise already agrees on.
            float rise = BodyRise(s, b);
            if (rise > TiptoeRiseFactor)
            {
                return PoseState.Tiptoe;
            }

            // Feet-only fallback: both heels clearly lifted even though the body barely rose
            // (short calf raise, or the player leaning back as they rise).
            float leftHeelRaise = b.leftHeelToeDy - (s.leftHeel.y - s.leftToe.y);
            float rightHeelRaise = b.rightHeelToeDy - (s.rightHeel.y - s.rightToe.y);
            bool heelsTracked = s.leftHeel.visibility >= 0.5f && s.rightHeel.visibility >= 0.5f;
            if (heelsTracked && rise > TiptoeRiseFactor * 0.4f &&
                (leftHeelRaise + rightHeelRaise) * 0.5f > HeelRaiseThreshold)
            {
                return PoseState.Tiptoe;
            }

            return PoseState.None;
        }

        /// <summary>
        /// Reads the player's arms out of the snapshot so the avatar can mirror them. Everything is
        /// normalised by torso length, so it is distance-independent like the tiptoe test.
        /// </summary>
        public static ArmPose ReadArms(in PoseSnapshot s, float minVisibility = 0.4f)
        {
            ArmPose a = default;
            bool leftOk = s.leftShoulder.visibility >= minVisibility &&
                          s.leftElbow.visibility >= minVisibility &&
                          s.leftWrist.visibility >= minVisibility;
            bool rightOk = s.rightShoulder.visibility >= minVisibility &&
                           s.rightElbow.visibility >= minVisibility &&
                           s.rightWrist.visibility >= minVisibility;
            if (!leftOk && !rightOk) return a;

            float torso = TorsoLen(s);

            // Height of the wrist above its own shoulder, in torso lengths. A relaxed arm hangs
            // roughly one torso below the shoulder; straight overhead is roughly one above.
            a.leftUp = leftOk ? Mathf.Clamp((s.leftShoulder.y - s.leftWrist.y) / torso, -1f, 1f) : -1f;
            a.rightUp = rightOk ? Mathf.Clamp((s.rightShoulder.y - s.rightWrist.y) / torso, -1f, 1f) : -1f;

            a.leftStraight = leftOk ? ElbowStraightness(s.leftShoulder, s.leftElbow, s.leftWrist) : 0.5f;
            a.rightStraight = rightOk ? ElbowStraightness(s.rightShoulder, s.rightElbow, s.rightWrist) : 0.5f;
            a.isValid = true;
            return a;
        }

        /// <summary>0 when the elbow is folded shut, 1 when the arm is straight.</summary>
        static float ElbowStraightness(in PoseLandmark shoulder, in PoseLandmark elbow, in PoseLandmark wrist)
        {
            Vector2 up = new Vector2(shoulder.x - elbow.x, shoulder.y - elbow.y);
            Vector2 low = new Vector2(wrist.x - elbow.x, wrist.y - elbow.y);
            if (up.sqrMagnitude < 1e-6f || low.sqrMagnitude < 1e-6f) return 0.5f;
            float cos = Vector2.Dot(up.normalized, low.normalized);
            // cos = -1 when straight (the two bones point opposite ways), +1 when folded shut.
            return Mathf.Clamp01((-cos + 1f) * 0.5f);
        }
    }
}
