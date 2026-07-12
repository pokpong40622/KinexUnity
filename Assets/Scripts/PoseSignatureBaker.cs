using UnityEngine;

namespace Kinex.MegaDance
{
    /// <summary>
    /// Turns a trainer pose (currently applied to a humanoid rig) into an 8-limb-angle
    /// target signature in the SAME space PoseScorer uses for player keypoints.
    ///
    /// Method: read the 12 relevant humanoid joint world positions, project them onto a
    /// canonical frontal image plane, then run PoseScorer.ComputeAngles on the result so
    /// the target and the player are scored by identical math.
    ///
    /// ⚠️ THE FRONTAL PROJECTION + MIRROR CONVENTION IS THE ONE THING THAT NEEDS LIVE
    /// TUNING once the webcam works. Until then the keyboard stub bypasses real scoring.
    /// Everything tunable is contained in Project() and the mirrorX flag below — keep it
    /// that way so calibration is a one-spot change.
    /// </summary>
    public class PoseSignatureBaker
    {
        // ⚠️ TUNING KNOB. Webcam keypoints are normally mirrored (selfie view); a person
        // facing the camera has their physical-right on the viewer's left.
        // 2026-06-20: mirrorX=false fixed the gross arm-angle error, but the player still has to
        // copy the OPPOSITE arm to score — i.e. a pure left/right LABEL swap remained. swapLeftRight
        // exchanges the baked target's left/right limb slots (arms+legs) so a true mirror copy scores.
        public bool mirrorX = false;

        // Left/right limb swap. NO LONGER NEEDED for the rig-vs-rig scoring path (the avatar rig is
        // baked the same way as the trainer rig, so a true copy already lines up). Kept as a knob in
        // case the avatar and trainer rigs are ever authored facing opposite ways.
        public bool swapLeftRight = false;

        // Humanoid bone whose world position best matches each COCO keypoint (indices 5-16).
        // The rig's joint pivots sit at the limb roots, which is what we want for limb angles.
        static readonly (int coco, HumanBodyBones bone)[] JointMap =
        {
            (5,  HumanBodyBones.LeftUpperArm),   (6,  HumanBodyBones.RightUpperArm),
            (7,  HumanBodyBones.LeftLowerArm),   (8,  HumanBodyBones.RightLowerArm),
            (9,  HumanBodyBones.LeftHand),       (10, HumanBodyBones.RightHand),
            (11, HumanBodyBones.LeftUpperLeg),   (12, HumanBodyBones.RightUpperLeg),
            (13, HumanBodyBones.LeftLowerLeg),   (14, HumanBodyBones.RightLowerLeg),
            (15, HumanBodyBones.LeftFoot),       (16, HumanBodyBones.RightFoot),
        };

        readonly Vector2[] _kp = new Vector2[17];
        readonly Vector3[] _world = new Vector3[17];
        readonly bool[] _valid = new bool[PoseScorer.NumLimbs];

        // Canonicalize each body's FACING (rotation about vertical) before the frontal projection,
        // so scoring compares limb SHAPE regardless of which way the rig points in world space.
        // REQUIRED in this scene: the trainer rig faces -X (shoulders run along the DEPTH axis) while
        // the player avatar faces -Z. Project() drops Z, so without this the trainer's whole pose
        // collapsed into the discarded axis → every target was garbage → nothing could score. As a
        // bonus it also neutralises each pose's authored side-on turn. Off = legacy raw projection.
        public bool yawNormalize = true;

        /// <summary>
        /// Bake the signature from whatever pose the rig currently holds.
        /// Caller is responsible for applying the pose to the rig first.
        /// </summary>
        public float[] BakeFromRig(Animator animator)
        {
            var angles = new float[PoseScorer.NumLimbs];
            BakeFromRigInto(animator, angles);
            return angles;
        }

        /// <summary>No-alloc version for per-frame scoring: fills <paramref name="angles"/> (length 8).</summary>
        public void BakeFromRigInto(Animator animator, float[] angles)
        {
            if (animator == null || angles == null) return;

            foreach (var (coco, bone) in JointMap)
            {
                Transform t = animator.GetBoneTransform(bone);
                _world[coco] = t != null ? t.position : Vector3.zero;
            }
            if (yawNormalize) NormalizeFacing();
            foreach (var (coco, bone) in JointMap)
                _kp[coco] = Project(_world[coco]);
            // conf=null → every joint counts; ComputeAngles guarantees identical math
            // to the player-side scoring path.
            PoseScorer.ComputeAngles(_kp, null, 0f, angles, _valid);
            if (swapLeftRight)
            {
                // Limb order: L/R upper arm, L/R forearm, L/R upper leg, L/R shin.
                Swap(angles, 0, 1);
                Swap(angles, 2, 3);
                Swap(angles, 4, 5);
                Swap(angles, 6, 7);
            }
        }

        static void Swap(float[] a, int i, int j) { (a[i], a[j]) = (a[j], a[i]); }

        // Rotate the sampled joints about VERTICAL (through the hip mid-point) so the hip line lies
        // along world -X — i.e. the body faces -Z, the axis the frontal Project() expects. We flatten
        // the hip vector to the X-Z plane first, so this is a PURE yaw: it removes body facing only,
        // never the pose's limb shape. Because we align the right-vector to a single target, a pose
        // and the same pose turned by ANY angle normalise to the same result (front/back included).
        void NormalizeFacing()
        {
            Vector3 hipMid = (_world[11] + _world[12]) * 0.5f;   // L/R upper-leg roots = pelvis line
            Vector3 right  = _world[12] - _world[11];
            Vector3 flat   = new Vector3(right.x, 0f, right.z);
            if (flat.sqrMagnitude < 1e-8f) return;               // hips edge-on → can't read yaw
            Quaternion q = Quaternion.FromToRotation(flat, Vector3.left); // -X
            foreach (var (coco, bone) in JointMap)
                _world[coco] = hipMid + q * (_world[coco] - hipMid);
        }

        /// <summary>
        /// World position → frontal image-plane point. Trainer faces -Z, so a frontal
        /// camera looks along +Z and the image axes are world X (horizontal) and Y
        /// (vertical). v is negated so this lands in the same Y-down image space the
        /// webcam keypoints use (SegmentAngle re-negates both for everyone equally).
        /// Absolute scale/offset are irrelevant — only segment direction feeds the angle.
        /// </summary>
        Vector2 Project(Vector3 world)
        {
            float u = mirrorX ? -world.x : world.x;
            float v = -world.y;
            return new Vector2(u, v);
        }
    }
}
