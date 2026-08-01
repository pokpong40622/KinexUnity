#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kinex.Trainer;

namespace Kinex.EditorTools
{
    /// <summary>
    /// THROWAWAY authoring tool for the SUPERSTAR STAGE minigame. Bakes 22 standing/seated/single-leg
    /// poses into a NEW asset, Assets/Animations/DancePoseData.asset, following the exact approach
    /// RehabPoseBuilder uses for MegaDance: poses are authored in Humanoid MUSCLE space via
    /// HumanPoseHandler on Assets/Characters/NewTrainerAnimated.fbx (poses ONLY reproduce correctly on
    /// this exact rig) and baked to per-bone local rotations into a Kinex.Trainer.TrainerPoseData asset.
    ///
    /// Unlike RehabPoseBuilder (which reuses an already-wired TrainerPoseController from
    /// MegaDanceScene), this tool is fully self-contained: it spawns its own throwaway instance of the
    /// rig in a brand-new, never-saved scene, so it never opens or touches any existing scene/asset.
    ///
    /// MIRROR CONVENTION: pose names encode the PLAYER's side (the trainer faces the camera like a
    /// mirror), so e.g. "knee_up_L" is baked onto the TRAINER's RIGHT leg. See TrainerSide().
    ///
    /// Run headless via -executeMethod Kinex.EditorTools.DancePoseBuilder.BuildAndRender (bakes the
    /// asset AND renders one PNG per pose — plus a side-view PNG for leg_back_L/R, tiptoe, and
    /// tandem_stand — into the job scratchpad for visual verification), or from the
    /// "Kinex/Build Dance Poses (+ Preview)" menu.
    /// </summary>
    public static class DancePoseBuilder
    {
        const string RigPath = "Assets/Characters/NewTrainerAnimated.fbx";
        const string OutAsset = "Assets/Animations/DancePoseData.asset";
        const string PreviewDir =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/552c1cb4-f9f4-4e3c-b616-07a08892f6bc/scratchpad/poses";

        // Pose indices that also get a profile (side) render, in addition to the front render —
        // these are the poses where depth (leg raised forward / swung back / staggered stance /
        // heel lift / seated fold) is the whole point and doesn't read from the front.
        static readonly HashSet<int> SideViewIndices = new HashSet<int> { 1, 2, 5, 6, 7, 8, 9, 10, 18, 20, 21 };

        class P
        {
            public string name;
            public float yaw;                 // whole-body turn, degrees (0 = face front)
            public float hipDrop;             // meters, baked into Hips local position; + = lower (seated)
            public Dictionary<string, float> m;
            public P(string n, Dictionary<string, float> muscles, float y = 0f, float hipDrop = 0f)
            { name = n; m = muscles; yaw = y; this.hipDrop = hipDrop; }
        }

        // Anti-robot base posture applied to every pose (unless overridden): arms hanging relaxed at
        // the sides + a small knee micro-flexion so the legs never look electronically locked.
        // Same values as RehabPoseBuilder.Baseline() — proven on this rig.
        static Dictionary<string, float> Baseline() => new Dictionary<string, float> {
            {"Left Arm Down-Up", -0.9f}, {"Right Arm Down-Up", -0.9f},
            {"Left Forearm Stretch", 0.6f}, {"Right Forearm Stretch", 0.6f},
            {"Left Lower Leg Stretch", -0.05f}, {"Right Lower Leg Stretch", -0.05f},
        };

        // PROBE-CALIBRATED LEG SIGNS (probe_* renders, this baker, this rig):
        //   Upper Leg Front-Back:  NEGATIVE = thigh swings FORWARD/UP, positive = backward.
        //   Lower Leg Stretch:     NEGATIVE = knee BENDS, positive = shin kicks forward.
        // (Opposite of the RehabPoseBuilder table comments — that baker read muscles through a
        // differently-oriented rig instance. Trust the probes, not the older comments.)

        // ---- Arm-state building blocks (reused across several poses). Sign conventions copied from
        // RehabPoseBuilder's verified table: Arm Down-Up 0 = literal T-pose (Unity's Humanoid muscle
        // zero IS the T-pose); -1 = arm down; Arm Front-Back - = forward; Forearm Stretch + = straighten
        // elbow, - = bend elbow more. ----

        // Empirical: on this rig muscle-zero is NOT the T-pose — Down-Up 0 renders arms drooping
        // ~40 deg. tandem_stand's 0.55 renders a true horizontal T (verified in preview QA).
        static Dictionary<string, float> ArmsT() => new Dictionary<string, float> {
            {"Left Arm Down-Up", 0.55f}, {"Right Arm Down-Up", 0.55f},
            {"Left Forearm Stretch", 0.85f}, {"Right Forearm Stretch", 0.85f},
        };

        static Dictionary<string, float> ArmsSlightOut() => new Dictionary<string, float> {
            {"Left Arm Down-Up", 0.22f}, {"Right Arm Down-Up", 0.22f},
        };

        static Dictionary<string, float> ArmsForwardBalance() => new Dictionary<string, float> {
            {"Left Arm Down-Up", 0.05f}, {"Right Arm Down-Up", 0.05f},
            {"Left Arm Front-Back", -0.35f}, {"Right Arm Front-Back", -0.35f},
        };

        // Hands stacked palm-down at chest. Down-Up must stay BELOW horizontal (0 = T-pose) or the
        // combination with a forward reach swings the hand up toward the jaw instead of the chest —
        // verified by preview.
        // Rounds 2-3 both drove the hand toward the jaw/eyes instead of the chest: dropping Down-Up
        // further while heavily bending the elbow behaves like a bicep curl on this rig (forearm swings
        // UP, not across). Round 4: rebuilt from ArmsForwardBalance's proven Down-Up/Front-Back (reads
        // at waist/chest height — see tiptoe/heel_stand previews) plus only a MILD elbow bend, no twist.
        // Elbow bend curls the forearm UP on this rig, so hands land where (upper-arm droop) +
        // (curl) ends: to hit the sternum, drop the upper arm well below horizontal and keep the
        // curl modest. (-0.2 droop + -0.35 curl still read at the chin — round-3 fix.)
        static Dictionary<string, float> ArmsStacked() => new Dictionary<string, float> {
            {"Left Arm Down-Up", -0.55f}, {"Right Arm Down-Up", -0.55f},
            {"Left Arm Front-Back", -0.5f}, {"Right Arm Front-Back", -0.5f},
            {"Left Forearm Stretch", -0.1f}, {"Right Forearm Stretch", -0.1f},
            {"Left Hand Down-Up", -0.5f}, {"Right Hand Down-Up", -0.5f},
        };

        // Arms crossed over the chest — same ArmsForwardBalance-derived base as ArmsStacked, slightly
        // more forward reach + elbow bend to suggest the cross, no twist (twist was implicated in the
        // hand-to-face swing too).
        static Dictionary<string, float> ArmsCrossed() => new Dictionary<string, float> {
            {"Left Arm Down-Up", -0.55f}, {"Right Arm Down-Up", -0.55f},
            {"Left Arm Front-Back", -0.55f}, {"Right Arm Front-Back", -0.55f},
            {"Left Forearm Stretch", -0.2f}, {"Right Forearm Stretch", -0.2f},
            {"Left Hand Down-Up", -0.5f}, {"Right Hand Down-Up", -0.5f},
        };

        // ---- Leg-state building blocks. `side` is the TRAINER's physical side ("Left"/"Right"). ----

        static Dictionary<string, float> KneeUp(string side, float front, float bend) => new Dictionary<string, float> {
            {$"{side} Upper Leg Front-Back", front},
            {$"{side} Lower Leg Stretch", bend},
        };

        // Hip abduction (leg out to the side). Preview-calibrated: POSITIVE = out for BOTH legs
        // on this baker (Left at -0.55 rendered the leg crossing INWARD — see round-5 sheet).
        static Dictionary<string, float> LegOut(string side, float amt)
        {
            return new Dictionary<string, float> { { $"{side} Upper Leg In-Out", amt } };
        }

        // Hip extension (leg straight out behind). Probe-calibrated: + = thigh backward.
        static Dictionary<string, float> LegBack(string side, float amt) => new Dictionary<string, float> {
            {$"{side} Upper Leg Front-Back", amt},
        };

        static Dictionary<string, float> CounterArm(string side, float amt) => new Dictionary<string, float> {
            {$"{side} Arm Front-Back", -amt},
        };

        // Seated legs: thighs ~horizontal, shins folded. Anchored to RehabPoseBuilder's PROVEN
        // ลุก-นั่ง "ย่อตัวลง" values (0.55/0.75) pushed slightly deeper for a full chair sit.
        static Dictionary<string, float> ChairLegs() => new Dictionary<string, float> {
            {"Left Upper Leg Front-Back", -0.9f}, {"Right Upper Leg Front-Back", -0.9f},
            {"Left Lower Leg Stretch", -0.9f}, {"Right Lower Leg Stretch", -0.9f},
        };

        static string TrainerSide(char playerSide) => playerSide == 'L' ? "Right" : "Left";
        static string OtherSide(string side) => side == "Left" ? "Right" : "Left";

        // Later dictionaries win on key collisions (lets a pose override a building block).
        static Dictionary<string, float> Combine(params Dictionary<string, float>[] parts)
        {
            var outD = new Dictionary<string, float>();
            foreach (var d in parts)
                if (d != null)
                    foreach (var kv in d) outD[kv.Key] = kv.Value;
            return outD;
        }

        // The 22 poses, frozen contract order/names (see class doc for the mirror convention).
        static List<P> Poses()
        {
            var list = new List<P>();

            // 0 — t_arms: T-pose arms, legs together.
            list.Add(new P("t_arms", Combine(ArmsT())));

            // 1,2 — knee_up_L/R: standing march, knee raised ~90 deg, opposite arm swings forward
            // slightly for a natural counter-balance. Values copied from RehabPoseBuilder's PROVEN
            // march (ย่ำเท้า 0.95/0.85/counter -0.45) — the earlier 0.75/0.7 barely read in renders.
            {
                string ts = TrainerSide('L');
                list.Add(new P("knee_up_L", Combine(KneeUp(ts, -0.95f, -0.85f), CounterArm(OtherSide(ts), 0.45f))));
            }
            {
                string ts = TrainerSide('R');
                list.Add(new P("knee_up_R", Combine(KneeUp(ts, -0.95f, -0.85f), CounterArm(OtherSide(ts), 0.45f))));
            }

            // 3,4 — leg_side_L/R: hip abduction ~30-40 deg, arms slightly out for balance.
            {
                string ts = TrainerSide('L');
                list.Add(new P("leg_side_L", Combine(LegOut(ts, 0.55f), ArmsSlightOut())));
            }
            {
                string ts = TrainerSide('R');
                list.Add(new P("leg_side_R", Combine(LegOut(ts, 0.55f), ArmsSlightOut())));
            }

            // 5,6 — leg_back_L/R: hip extension ~20-30 deg, slight forward torso lean.
            {
                string ts = TrainerSide('L');
                list.Add(new P("leg_back_L", Combine(LegBack(ts, 0.4f),
                    new Dictionary<string, float> { { "Spine Front-Back", -0.12f } },
                    CounterArm(OtherSide(ts), 0.2f))));
            }
            {
                string ts = TrainerSide('R');
                list.Add(new P("leg_back_R", Combine(LegBack(ts, 0.4f),
                    new Dictionary<string, float> { { "Spine Front-Back", -0.12f } },
                    CounterArm(OtherSide(ts), 0.2f))));
            }

            // 7 — tiptoe: both heels raised, arms slightly forward.
            list.Add(new P("tiptoe", Combine(
                new Dictionary<string, float> { { "Left Foot Up-Down", 0.6f }, { "Right Foot Up-Down", 0.6f } },
                ArmsForwardBalance())));

            // 8 — heel_stand: standing on heels, toes lifted, arms forward for balance.
            list.Add(new P("heel_stand", Combine(
                new Dictionary<string, float> { { "Left Foot Up-Down", -0.6f }, { "Right Foot Up-Down", -0.6f } },
                ArmsForwardBalance())));

            // 9 — tandem_stand: heel-to-toe stance, arms out (reference slide shows near-T arms).
            list.Add(new P("tandem_stand", Combine(
                new Dictionary<string, float> { { "Left Upper Leg Front-Back", -0.16f }, { "Right Upper Leg Front-Back", 0.16f } },
                new Dictionary<string, float> {
                    {"Left Arm Down-Up", 0.55f}, {"Right Arm Down-Up", 0.55f},
                    {"Left Forearm Stretch", 0.85f}, {"Right Forearm Stretch", 0.85f},
                })));

            // 10-15 — single-leg stands (RehabPoseBuilder's proven ยกขาทรงตัว leg values 0.7/0.85),
            // free knee clearly bent so the tuck reads from the front, with the 3 arm variants.
            {
                string ts = TrainerSide('L');
                list.Add(new P("sl_tarms_L", Combine(KneeUp(ts, -0.7f, -0.85f), ArmsT())));
            }
            {
                string ts = TrainerSide('R');
                list.Add(new P("sl_tarms_R", Combine(KneeUp(ts, -0.7f, -0.85f), ArmsT())));
            }

            // 12,13 — sl_stack_L/R: single-leg stand, hands stacked at chest.
            {
                string ts = TrainerSide('L');
                list.Add(new P("sl_stack_L", Combine(KneeUp(ts, -0.7f, -0.85f), ArmsStacked())));
            }
            {
                string ts = TrainerSide('R');
                list.Add(new P("sl_stack_R", Combine(KneeUp(ts, -0.7f, -0.85f), ArmsStacked())));
            }

            // 14,15 — sl_cross_L/R: single-leg stand, arms crossed over chest.
            {
                string ts = TrainerSide('L');
                list.Add(new P("sl_cross_L", Combine(KneeUp(ts, -0.7f, -0.85f), ArmsCrossed())));
            }
            {
                string ts = TrainerSide('R');
                list.Add(new P("sl_cross_R", Combine(KneeUp(ts, -0.7f, -0.85f), ArmsCrossed())));
            }

            // 16,17 — sidestep_L/R: wide stance, weight shifted to that side, arms trail that way.
            {
                string ts = TrainerSide('L'); string other = OtherSide(ts);
                list.Add(new P("sidestep_L", Combine(
                    LegOut(ts, 0.6f), LegOut(other, 0.25f),
                    new Dictionary<string, float> {
                        { $"{ts} Arm Down-Up", 0.45f }, { $"{other} Arm Down-Up", -0.4f },
                        // Torso leans toward the stepping side (trainer's physical side; sign per
                        // Rehab เอนไปซ้าย/ขวา: Spine Left-Right + leans toward trainer-left).
                        { "Spine Left-Right", ts == "Left" ? 0.3f : -0.3f },
                    })));
            }
            {
                string ts = TrainerSide('R'); string other = OtherSide(ts);
                list.Add(new P("sidestep_R", Combine(
                    LegOut(ts, 0.6f), LegOut(other, 0.25f),
                    new Dictionary<string, float> {
                        { $"{ts} Arm Down-Up", 0.45f }, { $"{other} Arm Down-Up", -0.4f },
                        { "Spine Left-Right", ts == "Left" ? 0.3f : -0.3f },
                    })));
            }

            // 18 — chair_sit: seated posture (hips/knees ~90 deg), arms crossed, hips lowered.
            list.Add(new P("chair_sit", Combine(ChairLegs(),
                new Dictionary<string, float> { { "Spine Front-Back", -0.15f } }, ArmsCrossed()),
                hipDrop: 0.42f));

            // 19 — chair_stand: standing upright, arms crossed.
            list.Add(new P("chair_stand", Combine(ArmsCrossed())));

            // 20,21 — seated_knee_L/R: seated posture with one knee raised further.
            {
                string ts = TrainerSide('L');
                list.Add(new P("seated_knee_L", Combine(ChairLegs(),
                    new Dictionary<string, float> { { $"{ts} Upper Leg Front-Back", -1.15f }, { $"{ts} Lower Leg Stretch", -0.7f } },
                    new Dictionary<string, float> { { "Spine Front-Back", -0.1f } },
                    ArmsForwardBalance()), hipDrop: 0.42f));
            }
            {
                string ts = TrainerSide('R');
                list.Add(new P("seated_knee_R", Combine(ChairLegs(),
                    new Dictionary<string, float> { { $"{ts} Upper Leg Front-Back", -1.15f }, { $"{ts} Lower Leg Stretch", -0.7f } },
                    new Dictionary<string, float> { { "Spine Front-Back", -0.1f } },
                    ArmsForwardBalance()), hipDrop: 0.42f));
            }

            return list;
        }

        [MenuItem("Kinex/Build Dance Poses (+ Preview)")]
        public static void BuildAndRender()
        {
            if (!BuildInternal())
            {
                Debug.LogError("[DancePoseBuilder] Build failed — skipping preview render.");
                return;
            }
            RenderAllPreviews();
        }

        [MenuItem("Kinex/Build Dance Poses")]
        public static void Build() => BuildInternal();

        [MenuItem("Kinex/Render Dance Pose Previews")]
        public static void RenderAllPreviewsMenu() => RenderAllPreviews();

        // One-off muscle-sign calibration: renders single-muscle probe poses (side view) straight
        // from HumanPoseHandler — no asset writes. Ground-truth for the leg sign conventions.
        public static void RenderProbes()
        {
            var probes = new List<P> {
                new P("probe_LegFB_plus",  new Dictionary<string,float>{{"Left Upper Leg Front-Back",  1f}}),
                new P("probe_LegFB_minus", new Dictionary<string,float>{{"Left Upper Leg Front-Back", -1f}}),
                new P("probe_Knee_plus",   new Dictionary<string,float>{{"Left Lower Leg Stretch",  1f}}),
                new P("probe_Knee_minus",  new Dictionary<string,float>{{"Left Lower Leg Stretch", -1f}}),
                new P("probe_LegFB_p6_Knee_p8", new Dictionary<string,float>{
                    {"Left Upper Leg Front-Back", 0.6f}, {"Left Lower Leg Stretch", 0.8f}}),
                new P("probe_LegFB_m6_Knee_m8", new Dictionary<string,float>{
                    {"Left Upper Leg Front-Back", -0.6f}, {"Left Lower Leg Stretch", -0.8f}}),
            };

            GameObject rig = SpawnRig();
            if (rig == null) return;
            var animator = rig.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null) { Object.DestroyImmediate(rig); return; }

            var muscleNames = HumanTrait.MuscleName;
            var muscleIdx = new Dictionary<string, int>();
            for (int i = 0; i < muscleNames.Length; i++) muscleIdx[muscleNames[i]] = i;

            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var hp = new HumanPose();
            handler.GetHumanPose(ref hp);
            var basePos = hp.bodyPosition; var baseRot = hp.bodyRotation;
            int mc = hp.muscles.Length;
            foreach (var a in rig.GetComponentsInChildren<Animator>(true)) a.enabled = false;

            var camGo = new GameObject("~ProbeCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.55f, 0.58f); cam.allowHDR = false;
            var lightGo = new GameObject("~ProbeLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.15f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

            var rends = rig.GetComponentsInChildren<Renderer>();
            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in skins) { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }
            Directory.CreateDirectory(PreviewDir);
            const int W = 640, H = 960;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            try
            {
                foreach (var probe in probes)
                {
                    hp.bodyPosition = basePos; hp.bodyRotation = baseRot;
                    hp.muscles = MakeMuscles(mc, muscleIdx, null, probe.m);
                    handler.SetHumanPose(ref hp);
                    foreach (var s in skins) { var bm = new Mesh(); s.BakeMesh(bm, true); Object.DestroyImmediate(bm); }
                    Bounds b = ComputeBounds(rends);
                    RenderView(cam, camGo.transform, b, 0f, rt, W, H, $"{PreviewDir}/{probe.name}_side.png");
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo); Object.DestroyImmediate(rig);
            }
            Debug.Log($"[DancePoseBuilder] Rendered {probes.Count} probes to {PreviewDir}.");
        }

        // Build a muscle array = baseline overlaid with pose overrides.
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
                    else { Debug.LogWarning($"[DancePoseBuilder] Unknown muscle '{kv.Key}'"); onMissing?.Invoke(1); }
                }
            }
            Apply(baseline);
            Apply(overrides);
            return m;
        }

        // Spawns a fresh, un-parented instance of the trainer rig in a brand-new (never-saved) scene.
        // Never touches any existing scene or asset. Strips the FBX's stray embedded Light/Camera
        // children (memory: avatar_fbx_stray_lights — intensity-1000 stray light).
        static GameObject SpawnRig()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (prefab == null)
            {
                Debug.LogError($"[DancePoseBuilder] Rig prefab not found at {RigPath}.");
                return null;
            }

            GameObject rig = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (rig == null) rig = Object.Instantiate(prefab);
            rig.transform.position = Vector3.zero;
            rig.transform.rotation = Quaternion.identity;

            foreach (var l in rig.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in rig.GetComponentsInChildren<Camera>(true)) c.enabled = false;

            return rig;
        }

        static bool BuildInternal()
        {
            GameObject rig = SpawnRig();
            if (rig == null) return false;

            var animator = rig.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[DancePoseBuilder] Spawned rig has no Humanoid Animator/avatar.");
                Object.DestroyImmediate(rig);
                return false;
            }

            // Full bone list (dedup by name), same convention TrainerPoseController resolves against.
            var allT = rig.transform.GetComponentsInChildren<Transform>(true);
            var seen = new HashSet<string>();
            var boneNamesList = new List<string>();
            var bonesList = new List<Transform>();
            foreach (var t in allT)
            {
                if (!seen.Add(t.name)) continue;
                boneNamesList.Add(t.name);
                bonesList.Add(t);
            }
            int bn = boneNamesList.Count;
            var bones = bonesList.ToArray();

            // REST local positions/rotations (bind pose — fresh instantiate, never posed yet).
            var restPos = new Vector3[bn];
            var restRot = new Quaternion[bn];
            for (int i = 0; i < bn; i++) { restPos[i] = bones[i].localPosition; restRot[i] = bones[i].localRotation; }

            int hipsIdx = -1;
            for (int i = 0; i < bn; i++)
                if (boneNamesList[i].EndsWith(":Hips")) { hipsIdx = i; break; }
            Quaternion hipsParentWorld = (hipsIdx >= 0 && bones[hipsIdx].parent != null)
                ? bones[hipsIdx].parent.rotation : Quaternion.identity;

            var muscleNames = HumanTrait.MuscleName;
            var muscleIdx = new Dictionary<string, int>();
            for (int i = 0; i < muscleNames.Length; i++) muscleIdx[muscleNames[i]] = i;

            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var hp = new HumanPose();
            handler.GetHumanPose(ref hp);
            var baseBodyPos = hp.bodyPosition;
            var baseBodyRot = hp.bodyRotation;
            int muscleCount = hp.muscles.Length;

            var animators = rig.GetComponentsInChildren<Animator>(true);
            var animWas = new bool[animators.Length];
            for (int i = 0; i < animators.Length; i++) { animWas[i] = animators[i].enabled; animators[i].enabled = false; }

            var neutralRot = new Quaternion[bn];
            hp.bodyPosition = baseBodyPos; hp.bodyRotation = baseBodyRot;
            var baseline = Baseline();
            hp.muscles = MakeMuscles(muscleCount, muscleIdx, baseline, null);
            handler.SetHumanPose(ref hp);
            for (int i = 0; i < bn; i++) neutralRot[i] = bones[i].localRotation;

            var anchorRot = new Quaternion[bn];
            for (int i = 0; i < bn; i++) anchorRot[i] = neutralRot[i];
            if (hipsIdx >= 0) anchorRot[hipsIdx] = restRot[hipsIdx];

            var defs = Poses();
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
                    Quaternion delta = bones[i].localRotation * Quaternion.Inverse(neutralRot[i]);
                    pose.localRotations[i] = delta * anchorRot[i];
                    pose.localPositions[i] = restPos[i];
                }
                if (def.yaw != 0f && hipsIdx >= 0)
                    pose.localRotations[hipsIdx] =
                        Quaternion.Inverse(hipsParentWorld) * Quaternion.AngleAxis(def.yaw, Vector3.up)
                        * hipsParentWorld * pose.localRotations[hipsIdx];

                // Seated poses: lower the hips. Done in WORLD space via the live bone (handles the
                // FBX import scale + parent orientation), then read back as a local position.
                if (def.hipDrop != 0f && hipsIdx >= 0)
                {
                    var hips = bones[hipsIdx];
                    Vector3 savedLocal = hips.localPosition;
                    hips.localPosition = restPos[hipsIdx];
                    hips.position += Vector3.down * def.hipDrop;
                    pose.localPositions[hipsIdx] = hips.localPosition;
                    hips.localPosition = savedLocal;
                }

                outPoses[p] = pose;
            }

            var data = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(OutAsset);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<TrainerPoseData>();
                AssetDatabase.CreateAsset(data, OutAsset);
            }
            data.boneNames = boneNamesList.ToArray();
            data.poses = outPoses;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            for (int i = 0; i < animators.Length; i++) animators[i].enabled = animWas[i];
            Object.DestroyImmediate(rig);

            Debug.Log($"[DancePoseBuilder] DONE. Authored {outPoses.Length} dance poses into {OutAsset} " +
                      $"({bn} bones; unknown muscles: {missing}). Hips idx={hipsIdx}.");
            return true;
        }

        // Renders one front-view PNG per pose (plus a side-view PNG for SideViewIndices) into
        // PreviewDir, from the just-baked (or previously-baked) DancePoseData.asset. Spawns its own
        // fresh rig instance — independently runnable without BuildInternal having just run.
        public static void RenderAllPreviews()
        {
            var data = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(OutAsset);
            if (data == null || data.poses == null || data.poses.Length == 0 || data.boneNames == null)
            {
                Debug.LogError($"[DancePoseBuilder] {OutAsset} missing or empty — run Build first.");
                return;
            }

            GameObject rig = SpawnRig();
            if (rig == null) return;

            var animator = rig.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;

            var map = new Dictionary<string, Transform>();
            foreach (var t in rig.transform.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map[t.name] = t;
            int bn = data.boneNames.Length;
            var bones = new Transform[bn];
            for (int i = 0; i < bn; i++) map.TryGetValue(data.boneNames[i], out bones[i]);

            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in skins) { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

            // Neutral grey backdrop (solid clear colour) + a simple ground plane so foot height reads.
            var camGo = new GameObject("~DancePosePreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.55f, 0.58f);
            cam.allowHDR = false;

            var lightGo = new GameObject("~DancePosePreviewLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "~DancePosePreviewGround";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = Vector3.one * 2f;
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (groundMat.shader == null) groundMat = new Material(Shader.Find("Standard"));
            groundMat.color = new Color(0.4f, 0.4f, 0.42f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            var rends = rig.GetComponentsInChildren<Renderer>();
            Directory.CreateDirectory(PreviewDir);

            const int W = 640, H = 960;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            try
            {
                for (int p = 0; p < data.poses.Length; p++)
                {
                    var pose = data.poses[p];
                    for (int i = 0; i < bn; i++)
                    {
                        if (bones[i] == null) continue;
                        bones[i].localRotation = pose.localRotations[i];
                        bones[i].localPosition = pose.localPositions[i];
                    }
                    foreach (var s in skins) { var bm = new Mesh(); s.BakeMesh(bm, true); Object.DestroyImmediate(bm); }

                    Bounds b = ComputeBounds(rends);
                    string baseName = $"pose_{p:00}_{pose.name}";

                    // NOTE: this freshly-spawned (identity-rotation) prefab instance faces world +X,
                    // not -Z like the scene-placed rig PosePreviewCapture reads from — verified
                    // empirically (yaw=0 renders a profile, yaw=90 renders front-on). So "front" here
                    // is yaw=90 and "side" is yaw=0.
                    RenderView(cam, camGo.transform, b, 90f, rt, W, H, $"{PreviewDir}/{baseName}_front.png");
                    if (SideViewIndices.Contains(p))
                        RenderView(cam, camGo.transform, b, 0f, rt, W, H, $"{PreviewDir}/{baseName}_side.png");
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(ground);
                Object.DestroyImmediate(rig);
            }

            Debug.Log($"[DancePoseBuilder] Rendered {data.poses.Length} pose previews to {PreviewDir}.");
        }

        static void RenderView(Camera cam, Transform camT, Bounds b, float yawDeg, RenderTexture rt, int w, int h, string path)
        {
            cam.orthographicSize = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z) / cam.aspect) * 1.25f;
            float dist = b.size.magnitude + 4f;
            Vector3 dirToCenter = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            camT.position = b.center - dirToCenter * dist;
            camT.rotation = Quaternion.LookRotation(dirToCenter, Vector3.up);

            cam.Render();
            cam.Render();

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static Bounds ComputeBounds(Renderer[] rends)
        {
            var b = new Bounds(rends.Length > 0 ? rends[0].bounds.center : Vector3.zero, Vector3.zero);
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
#endif
