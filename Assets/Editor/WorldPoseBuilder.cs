#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kinex.Trainer;
using Kinex.World;

namespace Kinex.World.EditorTools
{
    /// <summary>
    /// One-click headless builder for the senior-balance exercise class. Authors 10 trainer
    /// poses (in Humanoid MUSCLE space so the values are rig-axis-independent), 10
    /// ExerciseDefinition assets, one Routine, then wires them into KinexWorldScene.
    ///
    /// Follows the same headless pattern as KinexWorldMusicSetup: no dialogs, Debug.Log only,
    /// batchmode-safe, re-runnable (assets are overwritten in place at fixed paths).
    ///
    /// Muscle override values below are FIRST-GUESS normalized values in [-1, 1]; signs and
    /// magnitudes will be tuned later via screenshots. Each pose has its own clearly-labeled,
    /// easy-to-edit block in BuildPoses().
    /// </summary>
    public static class WorldPoseBuilder
    {
        const string ScenePath = "Assets/Scenes/KinexWorldScene.unity";
        const string PoseDataPath = "Assets/Animations/WorldPoseData.asset";
        const string BalanceFolder = "Assets/World/Content/Balance";
        const string RoutinePath = "Assets/World/Content/Routine_WorldBalance.asset";

        // Bone names copied VERBATIM from Assets/Animations/TrainerPoseData.asset (38 bones,
        // same Mixamo Humanoid rig). Order matters — it is the capture order the controller
        // resolves against. Do not reorder.
        static readonly string[] BoneNames =
        {
            "mixamorig:Hips",
            "mixamorig:LeftUpLeg",
            "mixamorig:LeftLeg",
            "mixamorig:LeftFoot",
            "mixamorig:LeftToeBase",
            "mixamorig:LeftToe_End",
            "mixamorig:LeftToe_End_end",
            "mixamorig:RightUpLeg",
            "mixamorig:RightLeg",
            "mixamorig:RightFoot",
            "mixamorig:RightToeBase",
            "mixamorig:RightToe_End",
            "mixamorig:RightToe_End_end",
            "mixamorig:Spine",
            "mixamorig:Spine1",
            "mixamorig:Spine2",
            "mixamorig:LeftShoulder",
            "mixamorig:LeftArm",
            "mixamorig:LeftForeArm",
            "mixamorig:LeftHand",
            "mixamorig:LeftHandIndex1",
            "mixamorig:LeftHandIndex2",
            "mixamorig:LeftHandIndex3",
            "mixamorig:LeftHandIndex4",
            "mixamorig:LeftHandIndex4_end",
            "mixamorig:Neck",
            "mixamorig:Head",
            "mixamorig:HeadTop_End",
            "mixamorig:HeadTop_End_end",
            "mixamorig:RightShoulder",
            "mixamorig:RightArm",
            "mixamorig:RightForeArm",
            "mixamorig:RightHand",
            "mixamorig:RightHandIndex1",
            "mixamorig:RightHandIndex2",
            "mixamorig:RightHandIndex3",
            "mixamorig:RightHandIndex4",
            "mixamorig:RightHandIndex4_end",
        };

        // One exercise = id + English name + a short instruction line. Order is the class order.
        struct ExerciseSpec
        {
            public string id;
            public string englishName;
            public string instruction;
            public ExerciseSpec(string id, string englishName, string instruction)
            {
                this.id = id; this.englishName = englishName; this.instruction = instruction;
            }
        }

        static readonly ExerciseSpec[] Exercises =
        {
            new ExerciseSpec("head_rotation",    "Head Rotation",   "Turn your head slowly to one side, then the other."),
            new ExerciseSpec("foot_taps",        "Foot Taps",       "Tap one foot forward, then return it under you."),
            new ExerciseSpec("march",            "Marching",        "Lift your knee until your thigh is parallel to the floor."),
            new ExerciseSpec("rock_boat",        "Rock the Boat",   "Shift your weight and lift one leg out to the side."),
            new ExerciseSpec("clock_reach",      "Clock Reach",     "Reach one arm overhead like a clock hand."),
            new ExerciseSpec("vision_walk",      "Vision Walks",    "Take a slow step while turning your head to look around."),
            new ExerciseSpec("single_leg_raise", "Single Leg Raise","Raise one straight leg slightly off the floor and hold."),
            new ExerciseSpec("body_circles",     "Body Circles",    "Hold your arms out wide and circle your upper body slowly."),
            new ExerciseSpec("grapevine",        "Grapevine",       "Cross one leg over the other and step sideways."),
            new ExerciseSpec("sit_to_stand",     "Sit-to-Stand",    "Bend your knees into a half-squat, then stand tall."),
        };

        [MenuItem("Kinex/Build World Balance Class")]
        public static void Build()
        {
            Debug.Log("[WorldPoseBuilder] " + Run());
        }

        // Headless one-shot for batchmode iteration: build the class (leaves KinexWorldScene
        // open) then screenshot all 10 poses via the existing PosePreviewCapture, which reads
        // the scene's TrainerPoseController.poseData (now WorldPoseData). Lets us eyeball pose
        // shapes by reading Assets/Resources/PosePreviews/pose_01..10.png — no APK build needed.
        public static void BuildAndCapture()
        {
            Debug.Log("[WorldPoseBuilder] " + Run());

            // The shared PosePreviewCapture camera frames the FRONT on the rig's -Z side (the
            // MegaDance rig faces -Z). This World character faces +Z, so straight capture shows the
            // BACK. Rotate the whole figure 180° about Y so its front faces the camera, capture,
            // then restore so the saved scene is untouched.
            var controller = Object.FindAnyObjectByType<TrainerPoseController>();
            var animator = FindHumanoidAnimator(controller);
            Transform root = animator != null ? animator.transform
                                              : (controller != null ? controller.transform : null);
            // Rotate about WORLD up (not the object's local Y — this Mixamo rig is imported with a
            // tilted local frame, so local-Y rotation skews the figure). AngleAxis pre-multiply
            // flips the facing around true vertical, no tilt, no lift.
            Quaternion orig = root != null ? root.rotation : Quaternion.identity;
            if (root != null) root.rotation = Quaternion.AngleAxis(180f, Vector3.up) * orig;

            // Capture twice: the first pass warms the skinned-mesh bounds (a fresh batchmode
            // process mis-frames pose 1 on its very first render → blank PNG); the second pass
            // is the keeper with all 10 framed correctly.
            Kinex.MegaDance.EditorTools.PosePreviewCapture.Capture();
            Kinex.MegaDance.EditorTools.PosePreviewCapture.Capture();

            if (root != null) root.rotation = orig;
        }

        // No-dialog worker so it can be driven from automation. Returns a status string.
        public static string Run()
        {
            var log = new StringBuilder();
            var warnings = new List<string>();

            // ---- open the scene ----------------------------------------------------------
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                return $"ERROR: could not open scene at {ScenePath}.";

            // ---- find the Humanoid trainer Animator --------------------------------------
            var controller = Object.FindAnyObjectByType<TrainerPoseController>();
            Animator animator = FindHumanoidAnimator(controller);
            if (animator == null)
                return "ERROR: no Humanoid Animator (isHuman && avatar != null) found in scene.";

            // rigRoot for bone lookup: prefer the controller's transform, else animator's.
            Transform rigRoot = controller != null ? controller.transform : animator.transform;

            // ---- (1) author the 10 poses into WorldPoseData.asset ------------------------
            var poses = BuildPoses(animator, rigRoot, warnings);
            var poseData = LoadOrCreate<TrainerPoseData>(PoseDataPath);
            poseData.boneNames = (string[])BoneNames.Clone();
            poseData.poses = poses;
            EditorUtility.SetDirty(poseData);
            log.Append($"Authored {poses.Length} poses → {PoseDataPath}. ");

            // ---- (2) create the 10 ExerciseDefinition assets -----------------------------
            EnsureFolder(BalanceFolder);
            var defs = new ExerciseDefinition[Exercises.Length];
            for (int i = 0; i < Exercises.Length; i++)
            {
                var spec = Exercises[i];
                string path = $"{BalanceFolder}/Exercise_{spec.id}.asset";
                var def = LoadOrCreate<ExerciseDefinition>(path);
                def.id = spec.id;
                def.thaiName = "";
                def.englishName = spec.englishName;
                def.category = ExerciseCategory.Balance;
                def.clip = null;
                def.durationSeconds = 30f;
                def.instruction = spec.instruction;
                def.legsRequired = true;
                EditorUtility.SetDirty(def);
                defs[i] = def;
            }
            log.Append($"Created {defs.Length} ExerciseDefinitions in {BalanceFolder}/. ");

            // ---- (3) create the routine --------------------------------------------------
            var routine = LoadOrCreate<Routine>(RoutinePath);
            routine.id = "world_balance";
            routine.thaiName = "";
            routine.englishName = "Balance Class";
            routine.description = "A gentle standing balance class for seniors: ten slow, " +
                                  "low-impact moves to build steadiness and confidence.";
            routine.exercises = defs;
            EditorUtility.SetDirty(routine);
            log.Append($"Created routine → {RoutinePath}. ");

            // ---- (4) wire the scene ------------------------------------------------------
            if (controller != null)
            {
                var soController = new SerializedObject(controller);
                var poseDataProp = soController.FindProperty("poseData");
                if (poseDataProp != null)
                {
                    poseDataProp.objectReferenceValue = poseData;
                    soController.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(controller);
                    log.Append("Wired TrainerPoseController.poseData. ");
                }
                else warnings.Add("TrainerPoseController.poseData property not found.");
            }
            else warnings.Add("No TrainerPoseController in scene — poseData not wired.");

            var director = Object.FindAnyObjectByType<KinexWorldDirector>();
            if (director != null)
            {
                var soDir = new SerializedObject(director);
                var routineProp = soDir.FindProperty("routine");
                if (routineProp != null) routineProp.objectReferenceValue = routine;
                else warnings.Add("KinexWorldDirector.routine property not found.");

                var trainerProp = soDir.FindProperty("trainer");
                if (trainerProp != null) trainerProp.objectReferenceValue = controller;
                else warnings.Add("KinexWorldDirector.trainer property not found (another agent may still be adding it).");

                soDir.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(director);
                log.Append("Wired KinexWorldDirector (routine + trainer). ");
            }
            else warnings.Add("No KinexWorldDirector in scene — routine/trainer not wired.");

            // ---- save everything ---------------------------------------------------------
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.Refresh();

            if (warnings.Count > 0)
            {
                log.Append("WARNINGS: ");
                foreach (var w in warnings) log.Append(w + " ");
            }
            else log.Append("No warnings. ");

            return log.ToString();
        }

        // ---------------------------------------------------------------- pose authoring

        // Build all 10 poses by applying muscle overrides on top of the captured neutral pose,
        // then reading back every bone's local transform. Restores the base pose at the end so
        // the scene rig is not left deformed.
        static TrainerPoseData.Pose[] BuildPoses(Animator animator, Transform rigRoot, List<string> warnings)
        {
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);

            var basePose = new HumanPose();
            handler.GetHumanPose(ref basePose);
            // Build the authoring base pose. NOTE: for THIS avatar a zeroed muscle array is NOT a clean
            // stand — it's a crouch (bent knees, leaned back), which made the trainer look seated and
            // float off the floor. The rig's CAPTURED saved pose, however, has correct standing legs +
            // upright spine (it's just got an arm raised). So we keep the captured lower-body/spine
            // muscles for a proper stand and zero only the arm/hand/shoulder/neck/head muscles, giving
            // an arms-down, head-forward standing base. Each exercise's overrides apply on top of this.
            float[] capturedMuscles = (float[])basePose.muscles.Clone();
            float[] baseMuscles = (float[])capturedMuscles.Clone();
            for (int mi = 0; mi < baseMuscles.Length; mi++)
                if (IsUpperBodyMuscle(HumanTrait.MuscleName[mi])) baseMuscles[mi] = 0f;
            basePose.muscles = baseMuscles;

            // Snapshot EVERY rig transform's local rotation/position BEFORE we drive the rig with
            // SetHumanPose. We restore these verbatim at the end (below) instead of round-tripping
            // through SetHumanPose — Humanoid muscle conversion is lossy and the zeroed neutral pose
            // would otherwise get baked into the trainer prefab overrides when the scene is saved,
            // pushing the character through the floor. Raw transform restore leaves the scene clean.
            var allTransforms = rigRoot.GetComponentsInChildren<Transform>(true);
            var savedLocalRot = new Quaternion[allTransforms.Length];
            var savedLocalPos = new Vector3[allTransforms.Length];
            for (int i = 0; i < allTransforms.Length; i++)
            {
                savedLocalRot[i] = allTransforms[i].localRotation;
                savedLocalPos[i] = allTransforms[i].localPosition;
            }

            // Track which muscle names came back -1 so we can report them.
            var missingMuscles = new HashSet<string>();

            // name → child Transform map under the rig root (same approach as
            // TrainerPoseController.ResolveBones).
            var boneMap = new Dictionary<string, Transform>();
            foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
                if (!boneMap.ContainsKey(t.name)) boneMap[t.name] = t;

            int n = BoneNames.Length;
            var poses = new TrainerPoseData.Pose[Exercises.Length];

            // Capture the rig's REAL base Hips transform (bone 0) before driving the rig. SetHumanPose
            // rebuilds the Hips orientation from bodyRotation and gets it wrong for this rig — it spins
            // the whole figure sideways/reclined (Hips came back ~(0,.707,.707,0) instead of the rig's
            // ~(-.5,.5,.5,-.5)). The balance exercises only move limbs/neck, never the Hips, so we pin
            // bone 0 to this captured base in every pose to keep the body upright and correctly facing.
            Quaternion baseHipsRot = Quaternion.identity;
            Vector3 baseHipsPos = Vector3.zero;
            if (boneMap.TryGetValue(BoneNames[0], out var hipsBone) && hipsBone != null)
            {
                baseHipsRot = hipsBone.localRotation;
                baseHipsPos = hipsBone.localPosition;
            }

            for (int p = 0; p < Exercises.Length; p++)
            {
                // Start from a fresh copy of the neutral muscle array, apply this pose's overrides.
                float[] muscles = (float[])baseMuscles.Clone();
                ApplyOverrides(p, muscles, missingMuscles);

                // Push muscles to the rig; transforms update immediately after SetHumanPose.
                var pose = basePose;            // struct copy keeps bodyPosition/rotation neutral
                pose.muscles = muscles;
                handler.SetHumanPose(ref pose);

                // Read back local transforms for every bone, in BoneNames order.
                var localRotations = new Quaternion[n];
                var localPositions = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    if (boneMap.TryGetValue(BoneNames[i], out var bone) && bone != null)
                    {
                        localRotations[i] = bone.localRotation;
                        localPositions[i] = bone.localPosition;
                    }
                    else
                    {
                        // Bone not found — identity/zero so the array stays parallel to boneNames.
                        localRotations[i] = Quaternion.identity;
                        localPositions[i] = Vector3.zero;
                    }
                }

                // Pin the Hips (bone 0) to the rig's real base transform so the body keeps its
                // correct upright orientation/placement; only the limbs+neck move per exercise.
                localRotations[0] = baseHipsRot;
                localPositions[0] = baseHipsPos;

                poses[p] = new TrainerPoseData.Pose
                {
                    name = $"Pose_{(p + 1):00}",
                    localRotations = localRotations,
                    localPositions = localPositions,
                };
            }

            // IMPORTANT: restore every rig transform EXACTLY as captured so saving the scene does
            // not bake the authoring pose into the trainer prefab instance (that sank the body).
            for (int i = 0; i < allTransforms.Length; i++)
            {
                allTransforms[i].localRotation = savedLocalRot[i];
                allTransforms[i].localPosition = savedLocalPos[i];
            }

            if (missingMuscles.Count > 0)
                foreach (var m in missingMuscles)
                    warnings.Add($"muscle name not found (-1): \"{m}\"");

            return poses;
        }

        // ----------------------------------------------------------------------------------
        // MUSCLE OVERRIDE TABLE — FIRST-GUESS values in [-1, 1]. Easy to edit: one labeled
        // block per pose, one Set() call per muscle. Tune signs/magnitudes later via
        // screenshots. Muscle names must match HumanTrait.MuscleName exactly; any miss is
        // logged (a -1 index from M()).
        // ----------------------------------------------------------------------------------
        static void ApplyOverrides(int poseIndex, float[] m, HashSet<string> missing)
        {
            switch (poseIndex)
            {
                case 0: // 1. Head Rotation — weak/cosmetic, mostly upright stance.
                    Set(m, "Neck Turn Left-Right", +0.60f, missing);
                    break;

                case 1: // 2. Foot Taps — one foot tapped slightly forward.
                    Set(m, "Right Upper Leg Front-Back", +0.30f, missing);
                    Set(m, "Right Lower Leg Stretch",    +0.10f, missing);
                    break;

                case 2: // 3. Marching — knee up to hip height, opposite arm swing.
                    Set(m, "Right Upper Leg Front-Back", +0.70f, missing);
                    Set(m, "Right Lower Leg Stretch",    -0.55f, missing);
                    Set(m, "Left Arm Front-Back",        +0.30f, missing);
                    Set(m, "Right Arm Front-Back",       -0.30f, missing);
                    break;

                case 3: // 4. Rock the Boat — side leg raise.
                    Set(m, "Right Upper Leg In-Out", +0.60f, missing);
                    Set(m, "Left Arm Down-Up",       +0.20f, missing);
                    break;

                case 4: // 5. Clock Reach — one arm reaches overhead.
                    Set(m, "Left Arm Down-Up",           +0.70f, missing);
                    Set(m, "Left Arm Front-Back",        +0.30f, missing);
                    Set(m, "Left Upper Leg Front-Back",  +0.35f, missing);
                    break;

                case 5: // 6. Vision Walks — mid-stride, head turned.
                    Set(m, "Right Upper Leg Front-Back", +0.40f, missing);
                    Set(m, "Left Upper Leg Front-Back",  -0.40f, missing);
                    Set(m, "Neck Turn Left-Right",       +0.50f, missing);
                    break;

                case 6: // 7. Single Leg Raise — leg straight, foot just off floor.
                    Set(m, "Left Upper Leg Front-Back", +0.30f, missing);
                    break;

                case 7: // 8. Body Circles — airplane arms, slight lean.
                    Set(m, "Left Arm Down-Up",  +0.55f, missing);
                    Set(m, "Right Arm Down-Up", +0.55f, missing);
                    Set(m, "Spine Front-Back",  +0.15f, missing);
                    break;

                case 8: // 9. Grapevine — legs crossed.
                    Set(m, "Right Upper Leg In-Out", -0.55f, missing);
                    Set(m, "Left Upper Leg In-Out",  +0.30f, missing);
                    break;

                case 9: // 10. Sit-to-Stand — squat hold, arms reaching forward.
                    Set(m, "Left Upper Leg Front-Back",  +0.70f, missing);
                    Set(m, "Right Upper Leg Front-Back", +0.70f, missing);
                    Set(m, "Left Lower Leg Stretch",     -0.70f, missing);
                    Set(m, "Right Lower Leg Stretch",    -0.70f, missing);
                    Set(m, "Left Arm Front-Back",        +0.50f, missing);
                    Set(m, "Right Arm Front-Back",       +0.50f, missing);
                    break;
            }
        }

        // ---------------------------------------------------------------- muscle helpers

        // True for arm/hand/finger/shoulder/neck/head muscles — the ones we zero in the base pose so
        // the trainer starts arms-down with a neutral head (the legs/spine come from the standing
        // capture). Leg/foot/toe/spine/chest muscles return false and are kept from the capture.
        static bool IsUpperBodyMuscle(string name)
        {
            string[] keys =
            {
                "Arm", "Forearm", "Hand", "Shoulder", "Neck", "Head",
                "Thumb", "Index", "Middle", "Ring", "Little", "Finger",
            };
            foreach (var k in keys)
                if (name.Contains(k)) return true;
            return false;
        }

        // Index of a Humanoid muscle by name, or -1 if not found. Linear scan over
        // HumanTrait.MuscleName is fine — it runs once per Set() at author time only.
        static int M(string name)
        {
            int count = HumanTrait.MuscleCount;
            for (int i = 0; i < count; i++)
                if (HumanTrait.MuscleName[i] == name) return i;
            return -1;
        }

        // Clamp to [-1, 1] and assign if the muscle exists; record the name if it doesn't.
        static void Set(float[] muscles, string name, float value, HashSet<string> missing)
        {
            int idx = M(name);
            if (idx < 0) { missing.Add(name); return; }
            muscles[idx] = Mathf.Clamp(value, -1f, 1f);
        }

        // ---------------------------------------------------------------- asset / scene utils

        // Pick the Humanoid Animator. Prefer one on/under the TrainerPoseController object,
        // else any Humanoid Animator in the scene.
        static Animator FindHumanoidAnimator(TrainerPoseController controller)
        {
            if (controller != null)
            {
                foreach (var a in controller.GetComponentsInChildren<Animator>(true))
                    if (a != null && a.isHuman && a.avatar != null) return a;
            }
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                if (a != null && a.isHuman && a.avatar != null) return a;
            return null;
        }

        // Load an existing asset at path, or create a fresh ScriptableObject there. Idempotent:
        // re-running overwrites the existing asset's fields (callers set them after this).
        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        // Create the folder chain for a path like "Assets/World/Content/Balance" if missing.
        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
