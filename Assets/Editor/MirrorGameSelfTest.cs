#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kinex.MirrorGame;
using Kinex.Trainer;

namespace Kinex.MirrorGame.EditorTools
{
    /// <summary>
    /// Editor/batchmode self-test for the Magic Mirror game. Two halves:
    ///  1. ZoneMatcher PURE tests — in/out boundary, Evaluate with Invalid joints, PoseProgress
    ///     hold fill + dropout + drain, assist growth timing + cap, and the score/stars/coins math.
    ///  2. Scene integrity — the built scene loads, the director is present + wired, the outline
    ///     ghost spawns with 10 zone circles, the wall carves a pose-shaped hole, and Build
    ///     Settings carries all 7 scenes.
    /// Prints one PASS/FAIL line per check; any failure also logs the "SELFTEST FAIL" signal line.
    /// Run: -executeMethod Kinex.MirrorGame.EditorTools.MirrorGameSelfTest.Run
    /// </summary>
    public static class MirrorGameSelfTest
    {
        const string ScenePath = "Assets/Scenes/MirrorGameScene.unity";
        const string RehabPoseDataPath = "Assets/Animations/RehabPoseData.asset";
        const string PlayerRigPath = "Assets/Characters/KinexUserModel.fbx";
        // The ghost must be the rig RehabPoseData was baked on — see MirrorOutline's class doc.
        const string GhostRigPath = "Assets/Characters/NewTrainerAnimated.fbx";

        static int s_Passed, s_Failed;

        [MenuItem("Kinex/Run Mirror Game Self-Test")]
        public static void Run()
        {
            s_Passed = 0; s_Failed = 0;

            TestZoneBoundary();
            TestEvaluateWithInvalid();
            TestHoldFillAndPass();
            TestDropoutAndDrain();
            TestAssistGrowthAndCap();
            TestScoring();
            TestResultMessage();
            TestSceneIntegrity();
            TestBuildSettings();

            if (s_Failed == 0)
                Debug.Log($"[MirrorGameSelfTest] ALL PASS ({s_Passed} checks)");
            else
                Debug.LogError($"SELFTEST FAIL — [MirrorGameSelfTest] {s_Failed} FAILED / {s_Passed} passed");
        }

        static void Check(bool ok, string name, string detail = "")
        {
            if (ok) { s_Passed++; Debug.Log($"[MirrorGameSelfTest] PASS: {name}"); }
            else { s_Failed++; Debug.LogError($"[MirrorGameSelfTest] FAIL: {name} {detail}"); }
        }

        // ---- 1. ZoneMatcher pure tests --------------------------------------

        static void TestZoneBoundary()
        {
            var c = Vector3.one;
            Check(ZoneMatcher.InZone(c, c + new Vector3(0.09f, 0f, 0f), 0.10f), "InZone: inside radius");
            // A hair inside the boundary (exact equality is float-precision territory, not behavior).
            Check(ZoneMatcher.InZone(c, c + new Vector3(0.0999f, 0f, 0f), 0.10f), "InZone: just inside boundary counts as in");
            Check(!ZoneMatcher.InZone(c, c + new Vector3(0.11f, 0f, 0f), 0.10f), "InZone: outside radius");
        }

        static void TestEvaluateWithInvalid()
        {
            int n = ZoneMatcher.JointCount;
            var targets = new Vector3[n];
            var players = new Vector3[n];
            var inZone = new bool[n];
            for (int i = 0; i < n; i++) { targets[i] = new Vector3(i, 0f, 0f); players[i] = targets[i]; }

            int count = ZoneMatcher.Evaluate(targets, players, 1f, inZone);
            Check(count == n, "Evaluate: perfect match = all 10 in", $"got {count}");

            players[3] = ZoneMatcher.Invalid;                     // untracked joint
            players[7] = targets[7] + new Vector3(5f, 0f, 0f);    // way out
            count = ZoneMatcher.Evaluate(targets, players, 1f, inZone);
            Check(count == n - 2 && !inZone[3] && !inZone[7],
                  "Evaluate: Invalid + far joints count as out", $"got {count}");

            // Radius multiplier: a joint just past base radius comes IN at 2x.
            players[7] = targets[7] + new Vector3(ZoneMatcher.BaseRadius[7] * 1.5f, 0f, 0f);
            count = ZoneMatcher.Evaluate(targets, players, 2f, inZone);
            Check(inZone[7] && count == n - 1, "Evaluate: assist multiplier widens zones", $"got {count}");
        }

        static void TestHoldFillAndPass()
        {
            var p = ZoneMatcher.PoseProgress.Start();
            const float dt = 1f / 60f;
            int frames = 0;
            while (!p.Passed && frames < 60 * 10) { p.Tick(true, dt); frames++; }
            float held = frames * dt;
            Check(p.Passed, "PoseProgress: passes after continuous hold");
            Check(Mathf.Abs(held - ZoneMatcher.HoldSeconds) < 0.1f,
                  "PoseProgress: hold duration ~= 2s", $"took {held:0.00}s");
            Check(p.Dropouts == 0, "PoseProgress: clean hold has 0 dropouts", $"got {p.Dropouts}");
            Check(p.LockInSeconds <= dt * 2f, "PoseProgress: LockInSeconds records hold start", $"got {p.LockInSeconds:0.00}");

            // After passing, further ticks are inert.
            float fill = p.HoldFill01;
            p.Tick(false, 1f);
            Check(p.Passed && p.HoldFill01 == fill, "PoseProgress: inert after pass");
        }

        static void TestDropoutAndDrain()
        {
            var p = ZoneMatcher.PoseProgress.Start();
            const float dt = 1f / 60f;
            for (int i = 0; i < 30; i++) p.Tick(true, dt);   // 0.5s in → fill 0.25
            float fillBefore = p.HoldFill01;
            Check(fillBefore > 0.2f && fillBefore < 0.3f, "PoseProgress: fill rises with hold", $"got {fillBefore:0.00}");

            p.Tick(false, dt);                                // break the hold
            Check(p.Dropouts == 1, "PoseProgress: dropout counted on hold break", $"got {p.Dropouts}");
            Check(p.HoldFill01 < fillBefore && p.HoldFill01 > 0f,
                  "PoseProgress: ring drains (not snaps) on break", $"got {p.HoldFill01:0.00}");

            // Drain rate = 2x fill rate → empties in ~0.25s from 0.25.
            for (int i = 0; i < 20; i++) p.Tick(false, dt);
            Check(p.HoldFill01 == 0f, "PoseProgress: ring fully drains", $"got {p.HoldFill01:0.00}");

            // Re-hold after a dropout still passes.
            int frames = 0;
            while (!p.Passed && frames < 600) { p.Tick(true, dt); frames++; }
            Check(p.Passed && p.Dropouts == 1, "PoseProgress: passes after recovering from dropout");
        }

        static void TestAssistGrowthAndCap()
        {
            var p = ZoneMatcher.PoseProgress.Start();
            const float dt = 0.1f;
            // 0.1f isn't exact in float, so StuckTimer's accumulated sum drifts from tick*0.1 —
            // stop a full second short of the threshold, then tick until the growth step fires.
            for (int i = 0; i < (int)((ZoneMatcher.AssistStuckSeconds - 1f) / dt); i++) p.Tick(false, dt);
            Check(Mathf.Approximately(p.RadiusMultiplier, 1f),
                  "Assist: no growth before 30s", $"got {p.RadiusMultiplier:0.000}");

            bool sawTrigger = false;
            for (int i = 0; i < 15 && !sawTrigger; i++) { p.Tick(false, dt); sawTrigger = p.AssistTriggered; }
            Check(Mathf.Abs(p.RadiusMultiplier - ZoneMatcher.AssistGrowFactor) < 1e-4f,
                  "Assist: radius x1.2 at ~30s stuck", $"got {p.RadiusMultiplier:0.000}");
            Check(sawTrigger, "Assist: AssistTriggered raised on growth step");
            p.Tick(false, dt);
            Check(!p.AssistTriggered, "Assist: AssistTriggered is one-tick only");

            // Keep failing forever → multiplier caps at 2.0.
            for (int i = 0; i < 4 * (int)(ZoneMatcher.AssistStuckSeconds / dt) + 100; i++) p.Tick(false, dt);
            Check(p.RadiusMultiplier <= ZoneMatcher.AssistMaxMultiplier + 1e-4f &&
                  p.RadiusMultiplier > ZoneMatcher.AssistMaxMultiplier - 0.15f,
                  "Assist: multiplier caps at 2.0", $"got {p.RadiusMultiplier:0.000}");
        }

        static void TestScoring()
        {
            Check(ZoneMatcher.PoseScore(5f, 0) == 1000, "Score: fast clean pass = 1000",
                  $"got {ZoneMatcher.PoseScore(5f, 0)}");
            Check(ZoneMatcher.PoseScore(60f, 10) == 400, "Score: slow shaky pass still earns base 400",
                  $"got {ZoneMatcher.PoseScore(60f, 10)}");
            int mid = ZoneMatcher.PoseScore(26.5f, 2);
            Check(mid > 400 && mid < 1000, "Score: mid effort lands between", $"got {mid}");

            Check(ZoneMatcher.Stars(25 * 1000, 25) == 3, "Stars: perfect session = 3");
            Check(ZoneMatcher.Stars((int)(25 * 1000 * 0.6f), 25) == 2, "Stars: 60% = 2");
            Check(ZoneMatcher.Stars((int)(25 * 1000 * 0.3f), 25) == 1, "Stars: 30% = 1");
            Check(ZoneMatcher.Stars(0, 0) == 1, "Stars: degenerate pose count still 1");

            Check(ZoneMatcher.Coins(20000, 3) == 230, "Coins: 20000pts + 3 stars = 230",
                  $"got {ZoneMatcher.Coins(20000, 3)}");
        }

        static void TestResultMessage()
        {
            var msg = MirrorResultBridge.BuildMessage(new MirrorResult
            { posesCompleted = 25, poseCount = 25, totalScore = 20000, coins = 230, stars = 3, durationSeconds = 300f });
            Check(msg.StartsWith("{\"type\":\"mirrorgame_result\",") && msg.Contains("\"coins\":230") && msg.EndsWith("}"),
                  "Bridge: mirrorgame_result wire format", msg);
        }

        // ---- 2. Scene integrity ----------------------------------------------

        static void TestSceneIntegrity()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Check(false, "Scene: file exists", $"{ScenePath} missing — run the scene builder first");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(true, "Scene: loads");

            var director = Object.FindAnyObjectByType<MirrorGameDirector>();
            Check(director != null, "Scene: MirrorGameDirector present");
            if (director == null) return;

            Check(director.GetComponent<MirrorResultBridge>() != null, "Scene: MirrorResultBridge attached");
            Check(director.rehabPoseData != null, "Scene: rehabPoseData wired");
            Check(director.ghostRigPrefab != null, "Scene: ghostRigPrefab wired");
            Check(director.poseDetector != null, "Scene: poseDetector wired");
            Check(director.holdRingFill != null && director.poseNameText != null && director.modeToggleLabel != null,
                  "Scene: HUD fields wired");
            Check(director.poseInstructionText != null && director.firstPoseHintText != null,
                  "Scene: instruction + first-pose hint texts wired");
            Check(director.announceSeconds >= 3f, "Scene: announce pace senior-readable (>=3s)",
                  $"got {director.announceSeconds}");
            Check(Object.FindAnyObjectByType<MirrorStage>() != null, "Scene: MirrorStage present");
            Check(Camera.main != null, "Scene: Main Camera present");

            // Ghost + zones + wall: exercise the same runtime path the director uses, in edit mode
            // (InitRuntime snaps bones synchronously, so positions are valid without a player loop).
            var poseData = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(RehabPoseDataPath);
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(GhostRigPath);
            var playerRig = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRigPath);
            if (poseData == null || rig == null || playerRig == null)
            {
                Check(false, "Ghost: assets loadable",
                      $"poseData={(poseData != null)} rig={(rig != null)} playerRig={(playerRig != null)}");
                return;
            }

            var playerStandIn = (GameObject)Object.Instantiate(playerRig);
            playerStandIn.name = "~SelfTestPlayer";
            playerStandIn.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            NormalizeHeight(playerStandIn, 1.7f); // scene builder does this to the real avatar — the
                                                  // raw FBX is far from 1.7m and would dwarf/starve the wall hole
            {
                var rens = playerStandIn.GetComponentsInChildren<Renderer>();
                var bb = rens[0].bounds; foreach (var r in rens) bb.Encapsulate(r.bounds);
                var anim2 = playerStandIn.GetComponentInChildren<Animator>();
                var hips = anim2 != null ? anim2.GetBoneTransform(HumanBodyBones.Hips) : null;
                var head = anim2 != null ? anim2.GetBoneTransform(HumanBodyBones.Head) : null;
                Debug.Log($"[MirrorGameSelfTest] DIAG standIn bounds.y={bb.size.y:F4} scale={playerStandIn.transform.localScale.x:F4} " +
                          $"hipsY={(hips != null ? hips.position.y : -99f):F4} headY={(head != null ? head.position.y : -99f):F4}");
            }

            var outline = MirrorOutline.Create(playerStandIn.transform, rig, poseData, 0);
            Check(outline != null, "Ghost: MirrorOutline spawns");
            if (outline != null)
            {
                Check(outline.GhostAnimator != null, "Ghost: humanoid Animator resolved");
                Check(outline.ZonesHolder != null && outline.ZonesHolder.childCount == ZoneMatcher.JointCount,
                      "Ghost: 10 zone circles built",
                      $"got {(outline.ZonesHolder != null ? outline.ZonesHolder.childCount : -1)}");

                var targets = new Vector3[ZoneMatcher.JointCount];
                outline.TargetPositions(targets);
                bool distinct = true;
                for (int i = 1; i < targets.Length; i++)
                    if ((targets[i] - targets[0]).sqrMagnitude < 1e-6f) { distinct = false; break; }
                if (!distinct)
                {
                    var anim = outline.GhostAnimator;
                    Debug.Log($"[MirrorGameSelfTest] DIAG ghostAnimator={(anim != null ? anim.name : "NULL")} " +
                              $"isHuman={(anim != null && anim.isHuman)} " +
                              $"boneLUA={(anim != null ? (object)anim.GetBoneTransform(HumanBodyBones.LeftUpperArm) : "n/a")}");
                    for (int i = 0; i < targets.Length; i++)
                        Debug.Log($"[MirrorGameSelfTest] DIAG target[{i}] {(ZoneMatcher.Joint)i} = {targets[i]:F3}");
                }
                Check(distinct, "Ghost: joint targets are distinct world positions");

                var wall = MirrorWall.Create(outline, playerStandIn.transform.position + Vector3.up * 1.1f);
                int holes = wall.Rebuild();
                if (holes <= 10)
                {
                    foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Neck, HumanBodyBones.Head,
                                              HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand,
                                              HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftFoot })
                        Debug.Log($"[MirrorGameSelfTest] DIAG wall bone {b} = {outline.BonePos(b):F3}");
                }
                Check(holes > 10, "Wall: pose-shaped hole carved (>10 cells)", $"got {holes} hole cells");
                int totalCells = wall.transform.childCount;
                Check(holes < totalCells, "Wall: hole doesn't consume the whole wall", $"{holes}/{totalCells}");

                var holder = outline.ZonesHolder;
                Object.DestroyImmediate(wall.gameObject);
                Object.DestroyImmediate(outline.gameObject);
                if (holder != null) Object.DestroyImmediate(holder.gameObject);
            }
            Object.DestroyImmediate(playerStandIn);
        }

        static void NormalizeHeight(GameObject model, float targetMeters)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y > 0.01f)
                model.transform.localScale *= targetMeters / b.size.y;
        }

        static void TestBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            Check(scenes.Length == 7, "BuildSettings: 7 scenes", $"got {scenes.Length}");
            bool hasMirror = false;
            foreach (var s in scenes) if (s.path == ScenePath) hasMirror = true;
            Check(hasMirror, "BuildSettings: MirrorGameScene registered");
            if (scenes.Length > 0)
                Check(scenes[0].path.Contains("Boot"), "BuildSettings: Boot stays index 0", $"index0={scenes[0].path}");
        }
    }
}
#endif
