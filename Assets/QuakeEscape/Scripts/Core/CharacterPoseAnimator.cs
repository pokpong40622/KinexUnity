using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Collapse
{
    public enum CharacterPose
    {
        Idle,
        StandLeftLeg,
        StandRightLeg,
        Tiptoe,
        Falling,
        Hang,
        JumpCrouch,
        JumpReach
    }

    /// <summary>
    /// Procedural humanoid poser. Blends the rig between canned muscle-space poses through a
    /// HumanPoseHandler, so no animation clips or AnimatorController are required.
    /// </summary>
    public class CharacterPoseAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float blendSpeed = 6f;
        [SerializeField] private float tiptoeBodyLift = 0.10f;
        [SerializeField] private float flailSpeed = 9f;
        [SerializeField] private float flailAmount = 0.45f;
        [Header("Breathing")]
        // Mocap breathing: a humanoid idle clip baked into muscle space at Start and played
        // back additively on top of whatever pose is held (legs masked out so raised knees
        // and tiptoe are untouched). When unset, the procedural sine fallback below runs.
        [SerializeField] private AnimationClip breathClip;
        [SerializeField] private float breathClipWeight = 1f;
        [SerializeField] private float breathsPerMinute = 14f;
        [SerializeField] private float breathChestAmount = 0.35f;
        [SerializeField] private float breathShoulderAmount = 0.25f;
        [SerializeField] private float breathArmAmount = 0.12f;
        [SerializeField] private float breathBodyBob = 0.025f;
        [SerializeField] private float breathHeadAmount = 0.1f;
        // whole-figure weight shift at half the breath rate: the body leans and
        // drifts a little instead of only the chest pumping
        [SerializeField] private float swayDegrees = 1.4f;
        [SerializeField] private float swayShift = 0.01f;
        [Header("Ground snap")]
        [SerializeField] private float groundY = 0f;
        [Header("Jump to hang")]
        // The bar is auto-placed at hang-pose reach + jumpClearance above the ground, so the
        // grab always needs a real jump and the feet clear the floor while hanging.
        [SerializeField] private Transform overheadBar;
        [SerializeField] private float jumpClearance = 0.45f;
        [SerializeField] private float jumpCrouchTime = 0.16f;
        [SerializeField] private float jumpRiseTime = 0.36f;
        [SerializeField] private float jumpBlendSpeed = 16f;
        [SerializeField] private float dropGravity = 14f;
        [SerializeField] private float landCrouchTime = 0.12f;

        private HumanPoseHandler handler;
        private HumanPose humanPose;
        private float[] currentMuscles;
        private float[] targetMuscles;
        private Vector3 baseBodyPosition;
        private Quaternion baseBodyRotation;
        private float currentBodyLift;
        private float targetBodyLift;
        private float flailTime;
        private float breathTime;

        private readonly Dictionary<CharacterPose, float[]> poses = new Dictionary<CharacterPose, float[]>();
        private int[] flailIndices = new int[0];
        private Transform leftFootBone, rightFootBone, leftToesBone, rightToesBone;
        private Vector3 heelLocalLeft, heelLocalRight, toeLocalLeft, toeLocalRight;
        private bool groundSnapReady;
        private int chestMuscle = -1, spineMuscle = -1;
        private int leftShoulderMuscle = -1, rightShoulderMuscle = -1;
        private int leftArmMuscle = -1, rightArmMuscle = -1;
        private int spineLRMuscle = -1, headNodMuscle = -1, headTiltMuscle = -1;

        // Jump-to-hang state. While the routine or the latch is active the rig's vertical
        // placement is owned by the jump (sole lift) or the bar (hand snap), not the ground.
        // Live arm mirroring. The canned poses only ever move the arms in fixed ways, so the
        // player's own arms read as dead on screen; these targets are written every frame from the
        // camera landmarks and blended over the held pose's arm muscles.
        private float mirrorLeftUp = -1f, mirrorRightUp = -1f;
        private float mirrorLeftStraight = 0.5f, mirrorRightStraight = 0.5f;
        private float mirrorWeight;
        private float mirrorWeightTarget;
        private int leftForearmMuscle = -1, rightForearmMuscle = -1;
        [SerializeField] private float armMirrorSmoothing = 12f;

        private Coroutine hangRoutine;
        private bool hangLatched;
        private bool hangDropping;
        private float hangLift;
        private float barGrabY;
        private bool hangReady;
        private bool releaseQueued;
        private float activeBlendSpeed;
        private CharacterPose? pendingPose;
        private Transform leftHandBone, rightHandBone, leftKnuckleBone, rightKnuckleBone;

        public bool HangActive => hangRoutine != null || hangLatched;

        // Baked breathing clip: per-frame muscle deltas (mean removed, legs zeroed) plus
        // body position/rotation deltas, looped by interpolation in LateUpdate.
        private float[][] breathFrames;
        private Vector3[] breathPosDeltas;
        private Quaternion[] breathRotDeltas;
        private float breathClipLength;
        private const float BreathBakeRate = 30f;

        public CharacterPose Current { get; private set; } = CharacterPose.Idle;

        private void Start()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[CharacterPoseAnimator] No humanoid Animator found.", this);
                enabled = false;
                return;
            }

            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            handler.GetHumanPose(ref humanPose);
            baseBodyPosition = humanPose.bodyPosition;
            baseBodyRotation = humanPose.bodyRotation;

            int muscleCount = HumanTrait.MuscleCount;
            currentMuscles = new float[muscleCount];
            targetMuscles = new float[muscleCount];

            BuildPoses();
            BakeBreathClip();

            poses[CharacterPose.Idle].CopyTo(currentMuscles, 0);
            poses[CharacterPose.Idle].CopyTo(targetMuscles, 0);

            ApplyPoseToRig();
            CalibrateGroundSnap();
            CalibrateHangReach();
            activeBlendSpeed = blendSpeed;
        }

        /// <summary>
        /// Pushes the current muscle state through the handler once, outside LateUpdate,
        /// so the rig is in a known pose for calibration.
        /// </summary>
        private void ApplyPoseToRig()
        {
            humanPose.bodyPosition = baseBodyPosition;
            humanPose.bodyRotation = baseBodyRotation;
            if (humanPose.muscles == null || humanPose.muscles.Length != currentMuscles.Length)
            {
                humanPose.muscles = new float[currentMuscles.Length];
            }
            currentMuscles.CopyTo(humanPose.muscles, 0);
            handler.SetHumanPose(ref humanPose);
        }

        /// <summary>
        /// Samples the breathing clip through a manual PlayableGraph into muscle space and
        /// stores per-frame deltas from the clip's own mean pose. Deltas (not absolutes) so
        /// the motion layers over any held pose; leg/foot muscles are zeroed so knee raises
        /// and tiptoe stay exactly as authored. Runs before ApplyPoseToRig, which restores
        /// the rig from whatever pose the last evaluated frame left it in.
        /// </summary>
        private void BakeBreathClip()
        {
            if (breathClip == null || !breathClip.humanMotion)
            {
                return;
            }

            // The renderer hasn't been drawn yet at Start, so with the default
            // CullUpdateTransforms the animator counts as invisible and graph evaluation
            // writes nothing (silently bakes a static pose) — force animation for the bake.
            var prevCulling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var graph = PlayableGraph.Create("BreathBake");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "BreathBakeOut", animator);
            var playable = AnimationClipPlayable.Create(graph, breathClip);
            playable.SetApplyFootIK(false);
            output.SetSourcePlayable(playable);

            int frameCount = Mathf.Max(2, Mathf.CeilToInt(breathClip.length * BreathBakeRate));
            breathClipLength = breathClip.length;
            int muscleCount = HumanTrait.MuscleCount;
            breathFrames = new float[frameCount][];
            breathPosDeltas = new Vector3[frameCount];
            breathRotDeltas = new Quaternion[frameCount];
            var rawRots = new Quaternion[frameCount];
            var mean = new float[muscleCount];
            Vector3 meanPos = Vector3.zero;
            var bakePose = new HumanPose();

            for (int f = 0; f < frameCount; f++)
            {
                playable.SetTime(f / (float)frameCount * breathClip.length);
                graph.Evaluate(0f);
                handler.GetHumanPose(ref bakePose);
                breathFrames[f] = (float[])bakePose.muscles.Clone();
                breathPosDeltas[f] = bakePose.bodyPosition;
                rawRots[f] = bakePose.bodyRotation;
                for (int i = 0; i < muscleCount; i++)
                {
                    mean[i] += bakePose.muscles[i];
                }
                meanPos += bakePose.bodyPosition;
            }
            graph.Destroy();
            animator.cullingMode = prevCulling;

            for (int i = 0; i < muscleCount; i++)
            {
                mean[i] /= frameCount;
            }
            meanPos /= frameCount;

            // Mean rotation: sign-aligned quaternion component average — all the clip's
            // body rotations sit close together, so this is safe.
            Vector4 rotAcc = Vector4.zero;
            foreach (var q in rawRots)
            {
                float sign = Quaternion.Dot(q, rawRots[0]) < 0f ? -1f : 1f;
                rotAcc += new Vector4(q.x, q.y, q.z, q.w) * sign;
            }
            var meanRot = new Quaternion(rotAcc.x, rotAcc.y, rotAcc.z, rotAcc.w).normalized;
            var invMeanRot = Quaternion.Inverse(meanRot);

            string[] names = HumanTrait.MuscleName;
            for (int f = 0; f < frameCount; f++)
            {
                for (int i = 0; i < muscleCount; i++)
                {
                    bool isLeg = names[i].Contains("Leg") || names[i].Contains("Foot")
                        || names[i].Contains("Toes");
                    breathFrames[f][i] = isLeg ? 0f : breathFrames[f][i] - mean[i];
                }
                breathPosDeltas[f] -= meanPos;
                breathRotDeltas[f] = invMeanRot * rawRots[f];
            }
        }

        /// <summary>
        /// Bakes the skinned meshes in the idle pose to find the true sole height, then stores
        /// how far each foot/toe bone sits above it. LateUpdate uses those offsets to plant
        /// the lowest foot exactly on groundY — the muscle poses alone leave the feet hovering
        /// because bodyPosition is pinned while the leg muscles reshape under it.
        /// </summary>
        private void CalibrateGroundSnap()
        {
            leftFootBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFootBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            leftToesBone = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            rightToesBone = animator.GetBoneTransform(HumanBodyBones.RightToes);
            if (leftFootBone == null || rightFootBone == null)
            {
                return;
            }

            var bottomVerts = new List<Vector3>();
            float soleY = float.MaxValue;
            var baked = new Mesh();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(baked, true);
                foreach (var v in baked.vertices)
                {
                    Vector3 w = smr.transform.TransformPoint(v);
                    if (w.y < soleY)
                    {
                        soleY = w.y;
                    }
                    bottomVerts.Add(w);
                }
            }
            Destroy(baked);
            if (soleY == float.MaxValue)
            {
                return;
            }

            // Real heel/toe-tip contact points, picked from the sole vertices of each foot
            // (rear-most and front-most within a thin band above the lowest point). They are
            // stored in bone-local space so they rotate with the foot: on tiptoe the heel
            // swings up and the toe tip stays down, and the snap lands on the true lowest.
            float band = soleY + 0.02f * animator.transform.lossyScale.y;
            float centerX = animator.transform.position.x;
            Vector3 heelL = leftFootBone.position, heelR = rightFootBone.position;
            Vector3 toeL = heelL, toeR = heelR;
            bool foundL = false, foundR = false;
            foreach (var w in bottomVerts)
            {
                if (w.y > band)
                {
                    continue;
                }
                if (w.x < centerX)
                {
                    if (!foundL || w.z < heelL.z) heelL = w;
                    if (!foundL || w.z > toeL.z) toeL = w;
                    foundL = true;
                }
                else
                {
                    if (!foundR || w.z < heelR.z) heelR = w;
                    if (!foundR || w.z > toeR.z) toeR = w;
                    foundR = true;
                }
            }

            heelLocalLeft = leftFootBone.InverseTransformPoint(heelL);
            heelLocalRight = rightFootBone.InverseTransformPoint(heelR);
            Transform toeBoneL = leftToesBone != null ? leftToesBone : leftFootBone;
            Transform toeBoneR = rightToesBone != null ? rightToesBone : rightFootBone;
            toeLocalLeft = toeBoneL.InverseTransformPoint(toeL);
            toeLocalRight = toeBoneR.InverseTransformPoint(toeR);
            groundSnapReady = true;
        }

        private float LowestSoleY()
        {
            float lowest = Mathf.Min(
                leftFootBone.TransformPoint(heelLocalLeft).y,
                rightFootBone.TransformPoint(heelLocalRight).y);
            if (leftToesBone != null && rightToesBone != null)
            {
                lowest = Mathf.Min(lowest,
                    Mathf.Min(
                        leftToesBone.TransformPoint(toeLocalLeft).y,
                        rightToesBone.TransformPoint(toeLocalRight).y));
            }
            return lowest;
        }

        /// <summary>
        /// Mirrors the player's arms onto the avatar. <paramref name="up"/> values run −1 (hanging at
        /// the side) → 0 (out horizontal) → +1 (straight overhead); <paramref name="straight"/> runs
        /// 0 (elbow folded) → 1 (arm straight). Call every frame while tracked.
        /// </summary>
        public void SetArmMirror(float leftUp, float rightUp, float leftStraight, float rightStraight)
        {
            mirrorLeftUp = leftUp;
            mirrorRightUp = rightUp;
            mirrorLeftStraight = leftStraight;
            mirrorRightStraight = rightStraight;
            mirrorWeightTarget = 1f;
        }

        /// <summary>Hands the arms back to the canned poses (tracking lost, or a scripted move owns them).</summary>
        public void ClearArmMirror()
        {
            mirrorWeightTarget = 0f;
        }

        public void SetPose(CharacterPose pose)
        {
            if (handler == null)
            {
                Current = pose;
                return;
            }

            if (HangActive)
            {
                if (pose == CharacterPose.Falling)
                {
                    CancelHang();
                }
                else
                {
                    // The jump/hang owns the rig until it resolves; remember the request
                    // so the landing blends straight into it.
                    pendingPose = pose;
                    return;
                }
            }

            if (Current == pose)
            {
                return;
            }

            Current = pose;
            ApplyTarget(pose);
        }

        private void ApplyTarget(CharacterPose pose)
        {
            poses[pose].CopyTo(targetMuscles, 0);
            targetBodyLift = pose == CharacterPose.Tiptoe ? tiptoeBodyLift : 0f;
        }

        private void CancelHang()
        {
            if (hangRoutine != null)
            {
                StopCoroutine(hangRoutine);
                hangRoutine = null;
            }
            hangLatched = false;
            hangDropping = false;
            hangLift = 0f;
            pendingPose = null;
            activeBlendSpeed = blendSpeed;
        }

        /// <summary>
        /// Crouch, leap, and latch both hands onto the overhead bar. While latched the root is
        /// re-snapped every frame so the hands stay pinned to the bar through breathing.
        /// </summary>
        public void StartHangJump()
        {
            if (handler == null || HangActive)
            {
                // Re-grabbing while the jump is still in flight cancels a queued release.
                releaseQueued = false;
                return;
            }

            if (!hangReady)
            {
                SetPose(CharacterPose.Tiptoe);
                return;
            }

            Current = CharacterPose.Hang;
            releaseQueued = false;
            hangRoutine = StartCoroutine(JumpToHangRoutine());
        }

        /// <summary>
        /// Let go of the bar (or abort a jump in flight): gravity drop, land in a short
        /// crouch, then blend to the most recently requested pose.
        /// </summary>
        public void ReleaseHang()
        {
            if (handler == null || hangDropping)
            {
                return;
            }

            if (!HangActive)
            {
                SetPose(CharacterPose.Idle);
                return;
            }

            if (!hangLatched)
            {
                // Still in flight: commit to the catch and let go right after the latch —
                // aborting mid-air (a momentary pose flicker) reads as a failed jump.
                releaseQueued = true;
                return;
            }

            if (hangRoutine != null)
            {
                StopCoroutine(hangRoutine);
            }
            hangRoutine = StartCoroutine(DropRoutine());
        }

        private IEnumerator JumpToHangRoutine()
        {
            activeBlendSpeed = jumpBlendSpeed;

            poses[CharacterPose.JumpCrouch].CopyTo(targetMuscles, 0);
            targetBodyLift = 0f;
            yield return new WaitForSeconds(jumpCrouchTime);

            poses[CharacterPose.JumpReach].CopyTo(targetMuscles, 0);
            float overshoot = jumpClearance * 1.12f;
            float t = 0f;
            while (t < jumpRiseTime)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / jumpRiseTime);
                hangLift = overshoot * (1f - (1f - u) * (1f - u));
                yield return null;
            }

            // Catch: the hand snap takes over vertical placement, and the JumpReach→Hang
            // blend under pinned hands reads as the body settling into the grip.
            poses[CharacterPose.Hang].CopyTo(targetMuscles, 0);
            hangLatched = true;
            hangLift = jumpClearance;
            activeBlendSpeed = blendSpeed;

            // A release requested mid-flight resolves here: hold the bar for a beat so
            // the grab reads, then drop (unless the pose came back and re-grabbed).
            if (releaseQueued)
            {
                yield return new WaitForSeconds(0.15f);
                if (releaseQueued)
                {
                    releaseQueued = false;
                    hangRoutine = StartCoroutine(DropRoutine());
                    yield break;
                }
            }
            hangRoutine = null;
        }

        private IEnumerator DropRoutine()
        {
            hangDropping = true;
            releaseQueued = false;
            activeBlendSpeed = jumpBlendSpeed;
            hangLift = LowestSoleY() - groundY;
            hangLatched = false;

            float v = 0f;
            while (hangLift > 0f)
            {
                v += dropGravity * Time.deltaTime;
                hangLift = Mathf.Max(0f, hangLift - v * Time.deltaTime);
                yield return null;
            }

            poses[CharacterPose.JumpCrouch].CopyTo(targetMuscles, 0);
            yield return new WaitForSeconds(landCrouchTime);

            CharacterPose next = pendingPose ?? CharacterPose.Idle;
            pendingPose = null;
            Current = next;
            ApplyTarget(next);
            activeBlendSpeed = blendSpeed;
            hangDropping = false;
            hangRoutine = null;
        }

        /// <summary>
        /// Measures how high the hands sit above the soles in the full hang pose, then parks
        /// the bar at that reach + jumpClearance so grabbing it always takes a real jump.
        /// </summary>
        private void CalibrateHangReach()
        {
            leftHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftKnuckleBone = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            rightKnuckleBone = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (!groundSnapReady || leftHandBone == null || rightHandBone == null)
            {
                return;
            }

            var saved = (float[])currentMuscles.Clone();
            poses[CharacterPose.Hang].CopyTo(currentMuscles, 0);
            ApplyPoseToRig();
            float reachAboveSole = HandGripY() - LowestSoleY();
            saved.CopyTo(currentMuscles, 0);
            ApplyPoseToRig();

            barGrabY = groundY + reachAboveSole + jumpClearance;
            if (overheadBar != null)
            {
                Vector3 p = overheadBar.position;
                overheadBar.position = new Vector3(p.x, barGrabY, p.z);
            }
            hangReady = true;
        }

        private float HandGripY() => HandGripPoint().y;

        private Vector3 HandGripPoint()
        {
            Vector3 l = leftKnuckleBone != null
                ? (leftHandBone.position + leftKnuckleBone.position) * 0.5f
                : leftHandBone.position;
            Vector3 r = rightKnuckleBone != null
                ? (rightHandBone.position + rightKnuckleBone.position) * 0.5f
                : rightHandBone.position;
            return (l + r) * 0.5f;
        }

        private void LateUpdate()
        {
            if (handler == null)
            {
                return;
            }

            float t = 1f - Mathf.Exp(-activeBlendSpeed * Time.deltaTime);
            for (int i = 0; i < currentMuscles.Length; i++)
            {
                currentMuscles[i] = Mathf.Lerp(currentMuscles[i], targetMuscles[i], t);
            }
            currentBodyLift = Mathf.Lerp(currentBodyLift, targetBodyLift, t);

            humanPose.bodyPosition = baseBodyPosition + Vector3.up * currentBodyLift;
            humanPose.bodyRotation = baseBodyRotation;

            if (humanPose.muscles == null || humanPose.muscles.Length != currentMuscles.Length)
            {
                humanPose.muscles = new float[currentMuscles.Length];
            }
            currentMuscles.CopyTo(humanPose.muscles, 0);

            if (Current != CharacterPose.Falling && breathFrames != null)
            {
                // Mocap breathing: loop the baked clip deltas over the held pose. Legs were
                // masked at bake time, so lifts and tiptoe keep their exact shape.
                breathTime += Time.deltaTime;
                float ft = Mathf.Repeat(breathTime, breathClipLength)
                    / breathClipLength * breathFrames.Length;
                int f0 = Mathf.Min((int)ft, breathFrames.Length - 1);
                int f1 = (f0 + 1) % breathFrames.Length;
                float u = ft - f0;
                for (int i = 0; i < humanPose.muscles.Length; i++)
                {
                    humanPose.muscles[i] += Mathf.Lerp(
                        breathFrames[f0][i], breathFrames[f1][i], u) * breathClipWeight;
                }
                humanPose.bodyRotation = baseBodyRotation * Quaternion.Slerp(
                    Quaternion.identity,
                    Quaternion.Slerp(breathRotDeltas[f0], breathRotDeltas[f1], u),
                    breathClipWeight);
                humanPose.bodyPosition += Vector3.Lerp(
                    breathPosDeltas[f0], breathPosDeltas[f1], u) * breathClipWeight;
            }
            else if (Current != CharacterPose.Falling)
            {
                // Breathing overlay: slow chest/shoulder rise with a hint of arm sway and body bob,
                // additive on top of whatever pose is held so it never fights the blend.
                breathTime += Time.deltaTime * breathsPerMinute / 60f * Mathf.PI * 2f;
                float inhale = (Mathf.Sin(breathTime) + 1f) * 0.5f;
                AddMuscle(chestMuscle, inhale * breathChestAmount);
                AddMuscle(spineMuscle, inhale * breathChestAmount * 0.4f);
                AddMuscle(leftShoulderMuscle, inhale * breathShoulderAmount);
                AddMuscle(rightShoulderMuscle, inhale * breathShoulderAmount);
                AddMuscle(leftArmMuscle, inhale * breathArmAmount);
                AddMuscle(rightArmMuscle, inhale * breathArmAmount);
                // head settles down on the exhale, drifts up on the inhale
                AddMuscle(headNodMuscle, (inhale - 0.5f) * breathHeadAmount);

                // whole-figure life: weight shifts side to side at half the breath
                // rate — the root itself leans and drifts, so the entire body
                // (hips, legs, head) moves, not just the torso muscles
                float sway = Mathf.Sin(breathTime * 0.5f);
                AddMuscle(spineLRMuscle, sway * 0.05f);
                AddMuscle(headTiltMuscle, -sway * 0.04f);
                humanPose.bodyRotation = baseBodyRotation * Quaternion.Euler(
                    (inhale - 0.5f) * -1.2f, 0f, sway * swayDegrees);
                humanPose.bodyPosition += Vector3.up * (inhale * breathBodyBob)
                    + Vector3.right * (sway * swayShift);
            }

            if (Current == CharacterPose.Falling)
            {
                flailTime += Time.deltaTime * flailSpeed;
                for (int i = 0; i < flailIndices.Length; i++)
                {
                    int idx = flailIndices[i];
                    if (idx >= 0)
                    {
                        humanPose.muscles[idx] += Mathf.Sin(flailTime + i * 1.7f) * flailAmount;
                    }
                }
            }

            ApplyArmMirror();

            handler.SetHumanPose(ref humanPose);

            // Vertical placement: hanging pins the hands to the bar, a jump/drop in flight
            // plants the soles at the animated lift height, otherwise the lowest foot goes
            // on the ground. While falling the rig keeps its last offset.
            if (groundSnapReady && Current != CharacterPose.Falling)
            {
                if (hangLatched)
                {
                    // Snap the whole grip point (hand+knuckle midpoint) onto the bar, not just
                    // its height — the Hang pose's natural reach lands the hands several cm
                    // forward/back of the bar's own depth, which reads as missing the rail
                    // entirely since it's a thin cylinder. X is left alone (pose is already
                    // near-symmetric there); Z is pinned to the bar so the hands always close
                    // on it, through breathing sway and all.
                    Vector3 grip = HandGripPoint();
                    float barZ = overheadBar != null ? overheadBar.position.z : grip.z;
                    animator.transform.position += new Vector3(0f, barGrabY - grip.y, barZ - grip.z);
                }
                else
                {
                    animator.transform.position += Vector3.up * (groundY + hangLift - LowestSoleY());
                }
            }
        }

        private void OnDestroy()
        {
            handler?.Dispose();
            handler = null;
        }

        /// <summary>
        /// Blends the player's live arm pose over whatever the held pose put in the arm muscles.
        /// The jump/hang and the fall own the arms outright (they are the whole point of those
        /// animations), so the mirror fades out for their duration rather than fighting them.
        /// </summary>
        private void ApplyArmMirror()
        {
            bool armsOwned = HangActive || Current == CharacterPose.Falling;
            float want = armsOwned ? 0f : mirrorWeightTarget;
            mirrorWeight = Mathf.Lerp(mirrorWeight, want,
                1f - Mathf.Exp(-armMirrorSmoothing * Time.deltaTime));
            if (mirrorWeight < 0.001f) return;

            BlendMuscle(leftArmMuscle, ArmDownUpFor(mirrorLeftUp), mirrorWeight);
            BlendMuscle(rightArmMuscle, ArmDownUpFor(mirrorRightUp), mirrorWeight);
            BlendMuscle(leftForearmMuscle, ForearmFor(mirrorLeftStraight), mirrorWeight);
            BlendMuscle(rightForearmMuscle, ForearmFor(mirrorRightStraight), mirrorWeight);
            // The shoulder rides up with a raised arm, which is what stops a high reach from
            // looking like the arm is dislocating out of a locked shoulder.
            BlendMuscle(leftShoulderMuscle, ShoulderFor(mirrorLeftUp), mirrorWeight);
            BlendMuscle(rightShoulderMuscle, ShoulderFor(mirrorRightUp), mirrorWeight);
        }

        // −1 (hanging) maps onto the idle pose's own arm value, +1 onto the full overhead reach,
        // so the mirror's rest position is exactly the pose the avatar already stands in.
        private static float ArmDownUpFor(float up) => Mathf.Lerp(-0.55f, 1f, (Mathf.Clamp(up, -1f, 1f) + 1f) * 0.5f);
        private static float ForearmFor(float straight) => Mathf.Lerp(-0.9f, 0.45f, Mathf.Clamp01(straight));
        private static float ShoulderFor(float up) => Mathf.Lerp(-0.1f, 0.6f, Mathf.Clamp01(up));

        private void BlendMuscle(int index, float value, float weight)
        {
            if (index >= 0)
            {
                humanPose.muscles[index] = Mathf.Lerp(humanPose.muscles[index], value, weight);
            }
        }

        private void AddMuscle(int index, float delta)
        {
            if (index >= 0)
            {
                humanPose.muscles[index] += delta;
            }
        }

        private int FindMuscle(string name)
        {
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }
            Debug.LogWarning($"[CharacterPoseAnimator] Muscle not found: {name}", this);
            return -1;
        }

        private float[] BuildPose(float[] basePose, params (string muscle, float value)[] overrides)
        {
            float[] result = basePose != null ? (float[])basePose.Clone() : new float[HumanTrait.MuscleCount];
            foreach (var (muscle, value) in overrides)
            {
                int i = FindMuscle(muscle);
                if (i >= 0)
                {
                    result[i] = value;
                }
            }
            return result;
        }

        private void BuildPoses()
        {
            // This avatar's muscle-zero reference is NOT a straight stand (all-zero legs read
            // back as an ~86° knee crouch), so every pose is built on the scene's authored
            // standing pose instead — its muscles were captured by GetHumanPose in Start.
            float[] standing = null;
            if (humanPose.muscles != null && humanPose.muscles.Length == HumanTrait.MuscleCount)
            {
                standing = (float[])humanPose.muscles.Clone();
            }

            // Neutral standing: arms relaxed at the sides, slight elbow bend.
            float[] idle = BuildPose(standing,
                ("Left Arm Down-Up", -0.55f),
                ("Right Arm Down-Up", -0.55f),
                ("Left Forearm Stretch", -0.25f),
                ("Right Forearm Stretch", -0.25f),
                ("Left Shoulder Down-Up", -0.1f),
                ("Right Shoulder Down-Up", -0.1f));
            poses[CharacterPose.Idle] = idle;

            // Stand on LEFT leg: right knee raised high (thigh near horizontal, knee bent ~90°),
            // toes pointing down, arms out to the sides, slight forward lean for balance.
            poses[CharacterPose.StandLeftLeg] = BuildPose(idle,
                ("Right Upper Leg Front-Back", 0.9f),
                ("Right Lower Leg Stretch", -0.95f),
                ("Right Foot Up-Down", -0.5f),
                ("Spine Front-Back", -0.1f),
                ("Left Arm Down-Up", 0.15f),
                ("Right Arm Down-Up", 0.15f),
                ("Left Forearm Stretch", -0.1f),
                ("Right Forearm Stretch", -0.1f));

            // Stand on RIGHT leg: mirror.
            poses[CharacterPose.StandRightLeg] = BuildPose(idle,
                ("Left Upper Leg Front-Back", 0.9f),
                ("Left Lower Leg Stretch", -0.95f),
                ("Left Foot Up-Down", -0.5f),
                ("Spine Front-Back", -0.1f),
                ("Left Arm Down-Up", 0.15f),
                ("Right Arm Down-Up", 0.15f),
                ("Left Forearm Stretch", -0.1f),
                ("Right Forearm Stretch", -0.1f));

            // Tiptoe: heels raised (toes pointed), arms reaching overhead for the bar.
            poses[CharacterPose.Tiptoe] = BuildPose(idle,
                ("Left Foot Up-Down", -1f),
                ("Right Foot Up-Down", -1f),
                ("Left Arm Down-Up", 0.9f),
                ("Right Arm Down-Up", 0.9f),
                ("Left Forearm Stretch", 0.2f),
                ("Right Forearm Stretch", 0.2f));

            // Jump anticipation: knees bent, hips back, torso pitched forward, arms swung
            // low behind, eyes up at the bar.
            poses[CharacterPose.JumpCrouch] = BuildPose(idle,
                ("Left Upper Leg Front-Back", 0.5f),
                ("Right Upper Leg Front-Back", 0.5f),
                ("Left Lower Leg Stretch", -0.65f),
                ("Right Lower Leg Stretch", -0.65f),
                ("Spine Front-Back", -0.4f),
                ("Chest Front-Back", -0.15f),
                ("Left Arm Down-Up", -0.45f),
                ("Right Arm Down-Up", -0.45f),
                ("Left Arm Front-Back", -0.3f),
                ("Right Arm Front-Back", -0.3f),
                ("Left Forearm Stretch", 0.1f),
                ("Right Forearm Stretch", 0.1f),
                ("Head Nod Down-Up", 0.35f));

            // Airborne reach: arms shooting overhead, back slightly arched, toes pointed,
            // knees trailing in a light tuck.
            poses[CharacterPose.JumpReach] = BuildPose(idle,
                ("Left Arm Down-Up", 1f),
                ("Right Arm Down-Up", 1f),
                ("Left Forearm Stretch", 0.45f),
                ("Right Forearm Stretch", 0.45f),
                ("Left Shoulder Down-Up", 0.55f),
                ("Right Shoulder Down-Up", 0.55f),
                ("Spine Front-Back", 0.1f),
                ("Left Upper Leg Front-Back", 0.25f),
                ("Right Upper Leg Front-Back", 0.25f),
                ("Left Lower Leg Stretch", -0.5f),
                ("Right Lower Leg Stretch", -0.5f),
                ("Left Foot Up-Down", -0.9f),
                ("Right Foot Up-Down", -0.9f),
                ("Head Nod Down-Up", 0.4f));

            // Hanging: arms long overhead with shoulders stretched into the shrug, legs
            // dangling with soft knees and drooped toes.
            poses[CharacterPose.Hang] = BuildPose(idle,
                ("Left Arm Down-Up", 1f),
                ("Right Arm Down-Up", 1f),
                ("Left Forearm Stretch", 0.35f),
                ("Right Forearm Stretch", 0.35f),
                ("Left Shoulder Down-Up", 0.7f),
                ("Right Shoulder Down-Up", 0.7f),
                ("Spine Front-Back", -0.05f),
                ("Left Upper Leg Front-Back", 0.12f),
                ("Right Upper Leg Front-Back", 0.12f),
                ("Left Lower Leg Stretch", -0.35f),
                ("Right Lower Leg Stretch", -0.35f),
                ("Left Foot Up-Down", -0.7f),
                ("Right Foot Up-Down", -0.7f),
                ("Head Nod Down-Up", 0.25f));

            // Falling: arms thrown up, knees tucked; flail is added on top each frame.
            poses[CharacterPose.Falling] = BuildPose(idle,
                ("Left Arm Down-Up", 0.9f),
                ("Right Arm Down-Up", 0.9f),
                ("Left Upper Leg Front-Back", 0.45f),
                ("Right Upper Leg Front-Back", 0.45f),
                ("Left Lower Leg Stretch", -0.6f),
                ("Right Lower Leg Stretch", -0.6f));

            chestMuscle = FindMuscle("Chest Front-Back");
            spineMuscle = FindMuscle("Spine Front-Back");
            leftShoulderMuscle = FindMuscle("Left Shoulder Down-Up");
            rightShoulderMuscle = FindMuscle("Right Shoulder Down-Up");
            leftArmMuscle = FindMuscle("Left Arm Down-Up");
            rightArmMuscle = FindMuscle("Right Arm Down-Up");
            leftForearmMuscle = FindMuscle("Left Forearm Stretch");
            rightForearmMuscle = FindMuscle("Right Forearm Stretch");
            spineLRMuscle = FindMuscle("Spine Left-Right");
            headNodMuscle = FindMuscle("Head Nod Down-Up");
            headTiltMuscle = FindMuscle("Head Tilt Left-Right");

            flailIndices = new[]
            {
                FindMuscle("Left Arm Front-Back"),
                FindMuscle("Right Arm Front-Back"),
                FindMuscle("Left Upper Leg Front-Back"),
                FindMuscle("Right Upper Leg Front-Back")
            };
        }
    }
}
