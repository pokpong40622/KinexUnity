using System;
using UnityEngine;

namespace Kinex.Trainer
{
    /// <summary>
    /// Baked full-body pose data for the trainer rig.
    /// Captured from frame 0 of the 10 Pose_xx humanoid clips so the rig can be
    /// driven directly (Animator disabled) without the Avatar retarget spin.
    /// Bones are matched by name under the controller's rig root.
    /// </summary>
    [CreateAssetMenu(fileName = "TrainerPoseData", menuName = "Kinex/Trainer Pose Data")]
    public class TrainerPoseData : ScriptableObject
    {
        [Serializable]
        public class Pose
        {
            public string name;
            public Vector3[] localPositions;    // per bone, same order as boneNames
            public Quaternion[] localRotations; // per bone, same order as boneNames
        }

        [Tooltip("Bone names in capture order. Resolved by name under the rig root at runtime.")]
        public string[] boneNames;

        public Pose[] poses;
    }
}
