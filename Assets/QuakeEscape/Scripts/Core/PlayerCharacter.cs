using System.Collections;
using UnityEngine;

namespace Collapse
{
    public class PlayerCharacter : MonoBehaviour
    {
        [SerializeField] private Transform leftAnchor;
        [SerializeField] private Transform centerAnchor;
        [SerializeField] private Transform rightAnchor;
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float fallDistance = 8f;
        [SerializeField] private float fallTime = 0.7f;
        [SerializeField] private CharacterPoseAnimator poseAnimator;

        private Transform sideTarget;
        private Transform currentTarget;

        private bool isFalling;
        private Coroutine fallRoutine;

        public bool IsHanging { get; private set; }

        private void Awake()
        {
            sideTarget = centerAnchor;
            currentTarget = centerAnchor;

            if (poseAnimator == null)
            {
                poseAnimator = GetComponentInChildren<CharacterPoseAnimator>();
            }
        }

        public void MoveToSide(int side)
        {
            if (isFalling)
            {
                return;
            }

            // Character stays put and lifts a leg instead of sliding to a side anchor.
            sideTarget = centerAnchor;

            if (!IsHanging)
            {
                currentTarget = sideTarget;
                SetPose(side < 0 ? CharacterPose.StandLeftLeg
                    : side > 0 ? CharacterPose.StandRightLeg
                    : CharacterPose.Idle);
            }
        }

        /// <summary>Mirrors the player's tracked arms onto the avatar (or releases them when untracked).</summary>
        public void MirrorArms(in ArmPose arms)
        {
            if (poseAnimator == null)
            {
                return;
            }

            if (arms.isValid)
            {
                poseAnimator.SetArmMirror(arms.leftUp, arms.rightUp, arms.leftStraight, arms.rightStraight);
            }
            else
            {
                poseAnimator.ClearArmMirror();
            }
        }

        public void StartHang()
        {
            if (isFalling || IsHanging)
            {
                return;
            }

            // The root stays put — the pose animator jumps the rig up to the bar itself.
            IsHanging = true;
            currentTarget = centerAnchor;
            if (poseAnimator != null)
            {
                poseAnimator.StartHangJump();
            }
        }

        public void StopHang()
        {
            if (isFalling || !IsHanging)
            {
                return;
            }

            IsHanging = false;
            currentTarget = sideTarget;
            if (poseAnimator != null)
            {
                poseAnimator.ReleaseHang();
            }
        }

        public void TiptoeInPlace()
        {
            if (isFalling || IsHanging)
            {
                return;
            }

            sideTarget = centerAnchor;
            currentTarget = centerAnchor;
            SetPose(CharacterPose.Tiptoe);
        }

        public void Fall()
        {
            StopFallRoutine();
            SetPose(CharacterPose.Falling);
            fallRoutine = StartCoroutine(FallRoutine());
        }

        private void SetPose(CharacterPose pose)
        {
            if (poseAnimator != null)
            {
                poseAnimator.SetPose(pose);
            }
        }

        public void ResetToCenter()
        {
            StopFallRoutine();
            isFalling = false;

            IsHanging = false;
            sideTarget = centerAnchor;
            currentTarget = centerAnchor;

            // If we were still on (or dropping from) the bar, play the release drop
            // instead of teleporting the rig back onto the ground.
            if (poseAnimator != null && poseAnimator.HangActive)
            {
                poseAnimator.ReleaseHang();
            }
            else
            {
                SetPose(CharacterPose.Idle);
            }

            transform.rotation = Quaternion.identity;

            if (centerAnchor != null)
            {
                transform.position = centerAnchor.position;
            }

            gameObject.SetActive(true);
        }

        private void StopFallRoutine()
        {
            if (fallRoutine != null)
            {
                StopCoroutine(fallRoutine);
                fallRoutine = null;
            }
        }

        private IEnumerator FallRoutine()
        {
            isFalling = true;

            Vector3 start = transform.position;
            Vector3 end = start + Vector3.down * fallDistance;

            Quaternion startRotation = transform.rotation;
            Quaternion endRotation = startRotation * Quaternion.Euler(90f, 0f, 0f);

            float t = 0f;
            while (t < fallTime)
            {
                t += Time.deltaTime;
                float normalized = Mathf.Clamp01(t / fallTime);
                float eased = normalized * normalized;
                transform.position = Vector3.Lerp(start, end, eased);
                transform.rotation = Quaternion.Slerp(startRotation, endRotation, normalized);
                yield return null;
            }

            transform.position = end;
            transform.rotation = endRotation;

            fallRoutine = null;
        }

        private void Update()
        {
            if (isFalling)
            {
                return;
            }

            if (currentTarget == null)
            {
                return;
            }

            transform.position = Vector3.MoveTowards(
                transform.position,
                currentTarget.position,
                moveSpeed * Time.deltaTime);
        }
    }
}
