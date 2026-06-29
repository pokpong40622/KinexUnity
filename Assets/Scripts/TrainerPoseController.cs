using System.Collections.Generic;
using UnityEngine;

namespace Kinex.Trainer
{
    /// <summary>
    /// Drives the trainer rig through baked poses by setting bone localRotation/
    /// localPosition directly in LateUpdate every frame. The Animator stays enabled
    /// (needed for GetBoneTransform during baking) but LateUpdate always overwrites
    /// its output, preventing the Humanoid retargeting pass from spinning the Hips 90°.
    /// Slerps between poses; can auto-cycle through all of them on Play.
    /// </summary>
    public class TrainerPoseController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] TrainerPoseData poseData;
        [Tooltip("Root of the bone hierarchy (e.g. TrainerArmature). Defaults to this transform.")]
        [SerializeField] Transform rigRoot;

        [Header("Timing")]
        [Tooltip("Seconds to blend (slerp) from one pose to the next.")]
        public float blendTime = 0.7f;
        [Tooltip("Seconds to hold a pose before auto-advancing.")]
        public float poseHoldTime = 5f;
        [Tooltip("Auto-cycle through all poses on Play.")]
        public bool autoAdvance = true;
        [Tooltip("Loop back to pose 0 after the last pose. False = stop at last pose.")]
        public bool loopPoses = false;

        Animator _animator;
        Transform[] _bones;                 // resolved from poseData.boneNames
        Quaternion[] _fromRot, _toRot;
        Vector3[] _fromPos, _toPos;
        int _currentPose;
        float _blend;                       // 0..1 progress of current transition
        float _holdTimer;

        bool HasData => poseData != null && poseData.poses != null
                        && poseData.poses.Length > 0 && _bones != null;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            if (rigRoot == null) rigRoot = transform;
            ResolveBones();
        }

        void Start()
        {
            // Animator stays enabled so GetBoneTransform works during BakeSignatures.
            // LateUpdate always re-writes bone transforms each frame, so the Humanoid
            // retargeting pass never gets to override them.

            if (!HasData)
            {
                Debug.LogWarning("[TrainerPoseController] No pose data or bones resolved.");
                return;
            }
            SnapToPose(0);
            Debug.Log($"[TrainerPoseController] {PoseCount} poses, {_bones.Length} bones. autoAdvance={autoAdvance}");
        }

        void ResolveBones()
        {
            if (poseData == null || poseData.boneNames == null) return;

            var map = new Dictionary<string, Transform>();
            foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map[t.name] = t;

            int n = poseData.boneNames.Length;
            _bones = new Transform[n];
            for (int i = 0; i < n; i++)
                map.TryGetValue(poseData.boneNames[i], out _bones[i]);

            _fromRot = new Quaternion[n]; _toRot = new Quaternion[n];
            _fromPos = new Vector3[n];    _toPos = new Vector3[n];
        }

        public int PoseCount => poseData != null && poseData.poses != null ? poseData.poses.Length : 0;
        public int CurrentPose => _currentPose;

        /// <summary>Display name of a pose (e.g. "หมุนศีรษะ • 1/4 (หันซ้าย)"), for the HUD/instruction card.</summary>
        public string PoseName(int index) =>
            (poseData != null && poseData.poses != null && poseData.poses.Length > 0)
                ? poseData.poses[Wrap(index)].name : "";
        public string CurrentPoseName => PoseName(_currentPose);

        /// <summary>Humanoid Animator on the rig (kept for FK joint lookups during baking).</summary>
        public Animator Animator => _animator;

        /// <summary>Snap instantly to a pose (no blend). Used to read joint positions while baking.</summary>
        public void ApplyPoseImmediate(int index) { if (HasData) SnapToPose(index); }

        /// <summary>Blend smoothly to the given pose index (wraps).</summary>
        public void ShowPose(int index)
        {
            if (!HasData) return;
            _currentPose = Wrap(index);
            var p = poseData.poses[_currentPose];
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                _fromRot[i] = _bones[i].localRotation;
                _fromPos[i] = _bones[i].localPosition;
                _toRot[i] = p.localRotations[i];
                _toPos[i] = p.localPositions[i];
            }
            _blend = 0f;
            _holdTimer = 0f;
        }

        public void NextPose() => ShowPose(_currentPose + 1);
        public void PrevPose() => ShowPose(_currentPose - 1);

        /// <summary>Snap instantly to a pose (no blend).</summary>
        void SnapToPose(int index)
        {
            _currentPose = Wrap(index);
            var p = poseData.poses[_currentPose];
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                _bones[i].localRotation = p.localRotations[i];
                _bones[i].localPosition = p.localPositions[i];
                _fromRot[i] = _toRot[i] = p.localRotations[i];
                _fromPos[i] = _toPos[i] = p.localPositions[i];
            }
            _blend = 1f;
            _holdTimer = 0f;
        }

        int Wrap(int i) { int n = PoseCount; return ((i % n) + n) % n; }

        void LateUpdate()
        {
            if (!HasData) return;

            // Always write bone transforms every frame — even at blend==1 — so the
            // Humanoid Animator cannot override them with its avatar retargeting pass
            // (which would spin the Hips ~90° and rotate the whole character sideways).
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_blend));
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                _bones[i].localRotation = Quaternion.Slerp(_fromRot[i], _toRot[i], t);
                _bones[i].localPosition = Vector3.Lerp(_fromPos[i], _toPos[i], t);
            }

            if (_blend < 1f)
                _blend += blendTime > 0f ? Time.deltaTime / blendTime : 1f;
            else if (autoAdvance)
            {
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= poseHoldTime)
                {
                    int next = _currentPose + 1;
                    if (next < PoseCount || loopPoses)
                        ShowPose(next); // Wrap() handles the loop-back when loopPoses=true
                    else
                    {
                        autoAdvance = false;
                        Debug.Log($"[TrainerPoseController] All {PoseCount} poses shown. Stopped.");
                    }
                }
            }
        }
    }
}
