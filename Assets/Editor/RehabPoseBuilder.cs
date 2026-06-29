#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kinex.Trainer;

namespace Kinex.EditorTools
{
    /// <summary>
    /// THROWAWAY authoring tool. Bakes 10 senior BALANCE-EXERCISE poses (from the snugsafe
    /// balance-exercises-for-seniors list) into Assets/Animations/RehabPoseData.asset — the pose
    /// set the MEGADANCE (rehab) game now uses. Poses are defined in Humanoid MUSCLE space (robust,
    /// readable) and baked to per-bone local rotations via HumanPoseHandler, matching how the original
    /// TrainerPoseData was captured. Body turns (look-over-shoulder, grapevine) are baked into the Hips
    /// bone so TrainerPoseController reproduces them (it only replays bone local rotations).
    ///
    /// Run editor-CLOSED via batchmode -executeMethod Kinex.EditorTools.RehabPoseBuilder.BuildAndPreview
    /// (authors the asset AND re-renders Assets/Resources/PosePreviews/pose_xx.png for visual checking),
    /// or from the "Kinex/Build Rehab Poses" menu.
    /// </summary>
    public static class RehabPoseBuilder
    {
        const string MegaScene = "Assets/Scenes/MegaDanceScene.unity";

        // One pose = a name + optional whole-body yaw (deg, baked into Hips) + a muscle override table.
        // Muscle values are normalized (-1..1). Names are Unity HumanTrait muscle names.
        class P
        {
            public string name;
            public float yaw;                 // whole-body turn, degrees (0 = face front)
            public Dictionary<string, float> m;
            public P(string n, float y, Dictionary<string, float> muscles) { name = n; yaw = y; m = muscles; }
        }

        // Applied to the neutral AND every pose (unless a pose overrides the same muscle), so the
        // default standing posture has arms hanging at the sides instead of the muscle-zero "zombie".
        static Dictionary<string, float> Baseline() => new Dictionary<string, float> {
            {"Left Arm Down-Up", -0.78f}, {"Right Arm Down-Up", -0.78f},
        };

        static List<P> Poses() => new List<P>
        {
            // 1. Head Rotation — stand tall, turn head to the side.
            new P("HeadRotation", 0, new Dictionary<string, float> {
                {"Head Turn Left-Right", 0.9f}, {"Neck Turn Left-Right", 0.6f}, {"Head Nod Down-Up", 0.1f},
            }),
            // 2. Foot Taps — right foot forward, toe tapping down (thigh slightly forward, slight knee bend).
            new P("FootTaps", 0, new Dictionary<string, float> {
                {"Right Upper Leg Front-Back", 0.22f}, {"Right Lower Leg Stretch", 0.18f},
                {"Right Foot Up-Down", -0.4f},
            }),
            // 3. Marching — right knee lifted high (thigh up + knee bent), opposite arm swung forward.
            new P("Marching", 0, new Dictionary<string, float> {
                {"Right Upper Leg Front-Back", 0.8f}, {"Right Lower Leg Stretch", 0.7f},
                {"Left Arm Front-Back", -0.5f},
            }),
            // 4. Rock the Boat — left leg lifted OUT to the side, arms out for balance.
            new P("RockTheBoat", 0, new Dictionary<string, float> {
                {"Left Upper Leg In-Out", -0.7f}, {"Left Arm Down-Up", 0.55f}, {"Right Arm Down-Up", 0.55f},
            }),
            // 5. Clock Reach — left arm reaching up (to 12), left foot lifted slightly.
            new P("ClockReach", 0, new Dictionary<string, float> {
                {"Left Arm Down-Up", 1.0f}, {"Left Arm Front-Back", -0.25f},
                {"Left Upper Leg Front-Back", 0.18f},
            }),
            // 6. Alternating Vision Walk — step forward while looking over the shoulder (front-facing).
            new P("VisionWalk", 0, new Dictionary<string, float> {
                {"Head Turn Left-Right", -0.85f}, {"Neck Turn Left-Right", -0.5f},
                {"Right Upper Leg Front-Back", 0.32f}, {"Right Lower Leg Stretch", 0.12f},
                {"Left Arm Front-Back", -0.3f},
            }),
            // 7. Single Leg Raise — left leg raised BEHIND, slight forward lean, arms out for balance.
            new P("LegRaiseBack", 0, new Dictionary<string, float> {
                {"Left Upper Leg Front-Back", -0.45f}, {"Spine Front-Back", -0.2f},
                {"Left Arm Down-Up", 0.35f}, {"Right Arm Down-Up", 0.35f},
            }),
            // 8. Body Circles — upper body leaned to the side, one arm raised out.
            new P("BodyCircle", 0, new Dictionary<string, float> {
                {"Spine Left-Right", 0.6f}, {"Spine Front-Back", -0.15f}, {"Left Arm Down-Up", 0.4f},
            }),
            // 9. Grapevine — side-step: left leg out to the side, lean slightly, arms low for balance.
            new P("Grapevine", 0, new Dictionary<string, float> {
                {"Left Upper Leg In-Out", -0.65f}, {"Spine Left-Right", -0.18f},
                {"Left Arm Down-Up", -0.2f}, {"Right Arm Down-Up", -0.2f},
            }),
            // 10. Sit-To-Stand — half-squat: thighs forward, knees bent, lean forward, arms swung forward.
            new P("SitToStand", 0, new Dictionary<string, float> {
                {"Left Upper Leg Front-Back", 0.5f}, {"Right Upper Leg Front-Back", 0.5f},
                {"Left Lower Leg Stretch", 0.7f}, {"Right Lower Leg Stretch", 0.7f},
                {"Spine Front-Back", -0.4f},
                {"Left Arm Front-Back", -0.35f}, {"Right Arm Front-Back", -0.35f},
            }),
        };

        static bool _restDebug;
        static bool _calib;

        // Single-muscle test poses (each at full +1) to learn this rig's sign conventions.
        static List<P> CalibPoses() => new List<P>
        {
            new P("L-ArmDownUp+1",   0, new Dictionary<string,float>{{"Left Arm Down-Up", 1f}}),
            new P("L-ArmFrontBack+1",0, new Dictionary<string,float>{{"Left Arm Front-Back", 1f}}),
            new P("L-Elbow+1",       0, new Dictionary<string,float>{{"Left Lower Arm Stretch", 1f}}),
            new P("R-ArmDownUp+1",   0, new Dictionary<string,float>{{"Right Arm Down-Up", 1f}}),
            new P("L-LegFrontBack+1",0, new Dictionary<string,float>{{"Left Upper Leg Front-Back", 1f}}),
            new P("L-LegInOut+1",    0, new Dictionary<string,float>{{"Left Upper Leg In-Out", 1f}}),
            new P("L-Knee+1",        0, new Dictionary<string,float>{{"Left Lower Leg Stretch", 1f}}),
            new P("Spine-FrontBack+1",0,new Dictionary<string,float>{{"Spine Front-Back", 1f}}),
            new P("Spine-LeftRight+1",0,new Dictionary<string,float>{{"Spine Left-Right", 1f}}),
            new P("Spine-Twist+1",   0, new Dictionary<string,float>{{"Spine Twist Left-Right", 1f}}),
        };

        [MenuItem("Kinex/Calibrate Muscles + Preview")]
        public static void Calibrate()
        {
            _restDebug = false; _calib = true;
            if (!BuildInternal()) return;
            Kinex.MegaDance.EditorTools.PosePreviewCapture.Capture();
        }

        [MenuItem("Kinex/Build Rehab Poses")]
        public static void Build() { _restDebug = false; _calib = false; BuildInternal(); }

        [MenuItem("Kinex/Build Rehab Poses + Preview")]
        public static void BuildAndPreview()
        {
            _restDebug = false; _calib = false;
            if (!BuildInternal()) return;
            // Re-render the pose previews from the just-authored asset (front-facing PNGs).
            Kinex.MegaDance.EditorTools.PosePreviewCapture.Capture();
        }

        // DEBUG: bake every pose as the EXACT rest pose, then preview. If the figure renders, the
        // capture pipeline is healthy and the SetHumanPose roundtrip is the problem.
        [MenuItem("Kinex/Build Rest Debug + Preview")]
        public static void BuildRestDebug()
        {
            _restDebug = true;
            if (!BuildInternal()) return;
            Kinex.MegaDance.EditorTools.PosePreviewCapture.Capture();
        }

        // Build a muscle array = baseline overlaid with pose overrides. Unknown muscle names are
        // counted via onMissing.
        static float[] MakeMuscles(int count, Dictionary<string, int> idx,
            Dictionary<string, float> baseline, Dictionary<string, float> overrides, System.Action<int> onMissing = null)
        {
            var m = new float[count];
            void Apply(Dictionary<string, float> d)
            {
                if (d == null) return;
                foreach (var kv in d)
                {
                    if (idx.TryGetValue(kv.Key, out int mi)) m[mi] = kv.Value;
                    else { Debug.LogWarning($"[RehabPoseBuilder] Unknown muscle '{kv.Key}'"); onMissing?.Invoke(1); }
                }
            }
            Apply(baseline);
            Apply(overrides);
            return m;
        }

        static void LogBounds(string label, Renderer[] rends)
        {
            if (rends == null || rends.Length == 0) { Debug.Log($"[RehabPoseBuilder] {label}: NO renderers"); return; }
            var b = new Bounds(rends[0].bounds.center, Vector3.zero);
            foreach (var r in rends) b.Encapsulate(r.bounds);
            Debug.Log($"[RehabPoseBuilder] {label}: center={b.center} size={b.size} (renderers={rends.Length})");
        }

        static bool BuildInternal()
        {
            EditorSceneManager.OpenScene(MegaScene, OpenSceneMode.Single);

            var controller = Object.FindAnyObjectByType<TrainerPoseController>();
            if (controller == null) { Debug.LogError("[RehabPoseBuilder] No TrainerPoseController in MegaDance scene."); return false; }

            var so = new SerializedObject(controller);
            var poseData = so.FindProperty("poseData").objectReferenceValue as TrainerPoseData;
            var rigRootProp = so.FindProperty("rigRoot").objectReferenceValue as Transform;
            Transform rigRoot = rigRootProp != null ? rigRootProp : controller.transform;
            if (poseData == null || poseData.boneNames == null || poseData.boneNames.Length == 0)
            { Debug.LogError("[RehabPoseBuilder] poseData/boneNames missing."); return false; }

            var animator = rigRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            { Debug.LogError("[RehabPoseBuilder] No Humanoid Animator/avatar under rig root."); return false; }

            // Resolve bones by name (same as the controller/runtime).
            var map = new Dictionary<string, Transform>();
            foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map[t.name] = t;
            int bn = poseData.boneNames.Length;
            var bones = new Transform[bn];
            for (int i = 0; i < bn; i++) map.TryGetValue(poseData.boneNames[i], out bones[i]);

            // REST local positions (bind). Rotation-only poses must not move bone positions —
            // SetHumanPose can translate the Hips via bodyPosition, which shoves the figure off-frame.
            var restPos = new Vector3[bn];
            var restRot = new Quaternion[bn];
            for (int i = 0; i < bn; i++) if (bones[i] != null) { restPos[i] = bones[i].localPosition; restRot[i] = bones[i].localRotation; }

            // Locate the Hips bone index (turn is baked here so the controller replays it).
            int hipsIdx = -1;
            for (int i = 0; i < bn; i++)
                if (poseData.boneNames[i] != null && poseData.boneNames[i].EndsWith(":Hips")) { hipsIdx = i; break; }
            // Hips' parent world rotation (armature root, stable) — used to yaw the body about TRUE world up.
            Quaternion hipsParentWorld = (hipsIdx >= 0 && bones[hipsIdx] != null && bones[hipsIdx].parent != null)
                ? bones[hipsIdx].parent.rotation : Quaternion.identity;

            // Muscle name -> index.
            var muscleNames = HumanTrait.MuscleName;
            var muscleIdx = new Dictionary<string, int>();
            for (int i = 0; i < muscleNames.Length; i++) muscleIdx[muscleNames[i]] = i;

            // HumanPoseHandler drives the rig from normalized muscle values.
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var hp = new HumanPose();
            handler.GetHumanPose(ref hp);
            var baseBodyPos = hp.bodyPosition;
            var baseBodyRot = hp.bodyRotation;
            int muscleCount = hp.muscles.Length;

            // Disable animators so the avatar pass can't re-impose the A-pose over what we set.
            var animators = rigRoot.GetComponentsInChildren<Animator>(true);
            var animWas = new bool[animators.Length];
            for (int i = 0; i < animators.Length; i++) { animWas[i] = animators[i].enabled; animators[i].enabled = false; }

            // NEUTRAL (muscle-zero) bone rotations. SetHumanPose's neutral faces sideways/seated vs the
            // rig's REST pose (which stands and faces the camera). We measure each muscle pose as a DELTA
            // from neutral and graft that articulation onto REST — keeping correct facing while applying
            // the limb movements.
            var neutralRot = new Quaternion[bn];
            hp.bodyPosition = baseBodyPos; hp.bodyRotation = baseBodyRot;
            var baseline = _calib ? new Dictionary<string, float>() : Baseline();
            hp.muscles = MakeMuscles(muscleCount, muscleIdx, baseline, null);
            handler.SetHumanPose(ref hp);
            for (int i = 0; i < bn; i++) if (bones[i] != null) neutralRot[i] = bones[i].localRotation;

            // CLEAN anchor: muscle-zero articulation (arms down, legs straight, symmetric) but the
            // FRONT-FACING, UPRIGHT Hips from the rig's rest pose. The muscle-zero neutral Hips faces
            // sideways and is seated, so we only borrow its limb articulation, not its Hips orientation.
            var anchorRot = new Quaternion[bn];
            for (int i = 0; i < bn; i++) anchorRot[i] = neutralRot[i];
            if (hipsIdx >= 0) anchorRot[hipsIdx] = restRot[hipsIdx];

            var defs = _calib ? CalibPoses() : Poses();
            var outPoses = new TrainerPoseData.Pose[defs.Count];
            int missing = 0;

            for (int p = 0; p < defs.Count; p++)
            {
                var def = defs[p];
                hp.bodyPosition = baseBodyPos;
                hp.bodyRotation = baseBodyRot;
                hp.muscles = MakeMuscles(muscleCount, muscleIdx, baseline, def.m, n => missing += n);
                handler.SetHumanPose(ref hp);

                var pose = new TrainerPoseData.Pose
                {
                    name = def.name,
                    localRotations = new Quaternion[bn],
                    localPositions = new Vector3[bn],
                };
                for (int i = 0; i < bn; i++)
                {
                    if (bones[i] == null) continue;
                    // articulation delta (neutral -> pose), grafted onto the rest pose.
                    Quaternion delta = bones[i].localRotation * Quaternion.Inverse(neutralRot[i]);
                    pose.localRotations[i] = _restDebug ? anchorRot[i] : delta * anchorRot[i];
                    pose.localPositions[i] = restPos[i];        // bind positions; only rotations vary
                }
                // Bake whole-body yaw into the Hips about TRUE world up (convert world yaw into the
                // Hips' local space via its parent's world rotation — the Hips' own axes aren't upright).
                if (def.yaw != 0f && hipsIdx >= 0)
                    pose.localRotations[hipsIdx] =
                        Quaternion.Inverse(hipsParentWorld) * Quaternion.AngleAxis(def.yaw, Vector3.up)
                        * hipsParentWorld * pose.localRotations[hipsIdx];

                outPoses[p] = pose;
            }

            poseData.poses = outPoses;
            EditorUtility.SetDirty(poseData);
            AssetDatabase.SaveAssets();

            for (int i = 0; i < animators.Length; i++) animators[i].enabled = animWas[i];
            Debug.Log($"[RehabPoseBuilder] DONE. Authored {outPoses.Length} rehab poses into RehabPoseData.asset " +
                      $"(unknown muscles: {missing}). Hips idx={hipsIdx}.");
            return true;
        }
    }
}
#endif
