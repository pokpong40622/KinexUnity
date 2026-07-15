#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Kinex.Motion;
using Kinex.TempleHunt;

namespace Kinex.TempleHunt.EditorTools
{
    /// <summary>
    /// Editor/batchmode self-test for Ancient Temple Treasure Hunt. Two halves:
    ///  1. TempleLogic PURE tests — water level, gate lift (incl. the live-rise/committed-rep
    ///     handoff), hold tick decay, the two arm-pose checks on synthetic keypoints (incl. the
    ///     null-on-low-confidence contract), stars/coins, and the result wire format.
    ///  2. Scene integrity — the built scene loads, the director is present with every UI field
    ///     wired, the stage builder produces a full rig, the avatar carries no live FBX lights,
    ///     and Build Settings carries all 8 scenes with Boot at index 0.
    /// Prints one PASS/FAIL line per check; any failure also logs the "SELFTEST FAIL" signal line.
    /// Run: -executeMethod Kinex.TempleHunt.EditorTools.TempleHuntSelfTest.Run
    /// </summary>
    public static class TempleHuntSelfTest
    {
        const string ScenePath = "Assets/Scenes/TempleHuntScene.unity";

        static int s_Passed, s_Failed;

        [MenuItem("Kinex/Run Temple Hunt Self-Test")]
        public static void Run()
        {
            s_Passed = 0; s_Failed = 0;

            TestWaterLevel();
            TestGateLift();
            TestTickHold();
            TestArmsOut();
            TestHandsOnChest();
            TestBalanceMeter();
            TestStarsAndCoins();
            TestResultMessage();
            TestStageBuild();
            TestSceneIntegrity();
            TestBuildSettings();

            if (s_Failed == 0)
                Debug.Log($"[TempleHuntSelfTest] ALL PASS ({s_Passed} checks)");
            else
                Debug.LogError($"SELFTEST FAIL — [TempleHuntSelfTest] {s_Failed} FAILED / {s_Passed} passed");
        }

        static void Check(bool ok, string name, string detail = "")
        {
            if (ok) { s_Passed++; Debug.Log($"[TempleHuntSelfTest] PASS: {name}"); }
            else { s_Failed++; Debug.LogError($"[TempleHuntSelfTest] FAIL: {name} {detail}"); }
        }

        static bool Near(float a, float b, float eps = 0.001f) => Mathf.Abs(a - b) < eps;

        // ---- 1. TempleLogic pure tests --------------------------------------

        static void TestWaterLevel()
        {
            Check(Near(TempleLogic.WaterLevel01(0, 20), 1f), "Water: full at 0 reps");
            Check(Near(TempleLogic.WaterLevel01(10, 20), 0.5f), "Water: half at 10/20");
            Check(Near(TempleLogic.WaterLevel01(20, 20), 0f), "Water: drained at target");
            Check(Near(TempleLogic.WaterLevel01(25, 20), 0f), "Water: clamped past target");
            Check(Near(TempleLogic.WaterLevel01(5, 0), 0f), "Water: zero target degrades safely");
        }

        static void TestGateLift()
        {
            Check(Near(TempleLogic.GateLift01(0, 5, 0f, false), 0f), "Gate: closed at start");
            Check(Near(TempleLogic.GateLift01(2, 5, 0f, false), 0.4f), "Gate: 2 committed reps = 0.4");
            Check(Near(TempleLogic.GateLift01(2, 5, 0.5f, false), 0.5f), "Gate: live rise pushes past committed");
            // The rep just committed while the player is still standing: the live component must
            // drop out so the gate doesn't double-count the same rise.
            Check(Near(TempleLogic.GateLift01(2, 5, 1f, true), 0.4f), "Gate: counted stand contributes no live rise");
            Check(Near(TempleLogic.GateLift01(5, 5, 0f, false), 1f), "Gate: fully open at target");
            Check(Near(TempleLogic.GateLift01(5, 5, 1f, false), 1f), "Gate: clamped past open");
            Check(Near(TempleLogic.GateLift01(0, 5, -1f, false), 0f), "Gate: negative progress clamped");
            Check(Near(TempleLogic.GateLift01(0, 0, 0f, false), 1f), "Gate: zero reps-per-gate degrades safely");
        }

        static void TestTickHold()
        {
            float held = 0f;
            for (int i = 0; i < 600; i++) held = TempleLogic.TickHold(held, true, 1f / 60f, 10f);
            Check(Near(held, 10f, 0.01f), "Hold: 600 ok-ticks at 60fps = exactly 10s", $"got {held}");
            Check(Near(TempleLogic.TickHold(10f, true, 1f, 10f), 10f), "Hold: capped at target");
            Check(Near(TempleLogic.TickHold(5f, false, 0.5f, 10f), 4f), "Hold: decays at 2x while lost");
            Check(Near(TempleLogic.TickHold(0.1f, false, 1f, 10f), 0f), "Hold: floors at zero");
        }

        // Synthetic standing body in normalized image space (y grows DOWN, like the camera feed):
        // shoulders y=0.35, hips y=0.55 → torso = 0.2.
        static Vector2[] Body()
        {
            var kp = new Vector2[17];
            kp[MotionMath.LShoulder] = new Vector2(0.42f, 0.35f);
            kp[MotionMath.RShoulder] = new Vector2(0.58f, 0.35f);
            kp[MotionMath.LHip] = new Vector2(0.44f, 0.55f);
            kp[MotionMath.RHip] = new Vector2(0.56f, 0.55f);
            kp[MotionMath.LKnee] = new Vector2(0.44f, 0.75f);
            kp[MotionMath.RKnee] = new Vector2(0.56f, 0.75f);
            kp[MotionMath.LAnkle] = new Vector2(0.44f, 0.93f);
            kp[MotionMath.RAnkle] = new Vector2(0.56f, 0.93f);
            return kp;
        }

        static float[] Conf(float v = 0.9f)
        {
            var conf = new float[17];
            for (int i = 0; i < conf.Length; i++) conf[i] = v;
            return conf;
        }

        static void TestArmsOut()
        {
            var conf = Conf();

            var t = Body(); // T-pose: wings out at shoulder height
            t[MotionMath.LWrist] = new Vector2(0.20f, 0.35f);
            t[MotionMath.RWrist] = new Vector2(0.80f, 0.35f);
            Check(TempleLogic.ArmsOutOk(t, conf) == true, "ArmsOut: T-pose passes");

            var down = Body(); // arms hanging
            down[MotionMath.LWrist] = new Vector2(0.40f, 0.60f);
            down[MotionMath.RWrist] = new Vector2(0.60f, 0.60f);
            Check(TempleLogic.ArmsOutOk(down, conf) == false, "ArmsOut: arms down fails");

            var one = Body(); // only one wing
            one[MotionMath.LWrist] = new Vector2(0.20f, 0.35f);
            one[MotionMath.RWrist] = new Vector2(0.60f, 0.60f);
            Check(TempleLogic.ArmsOutOk(one, conf) == false, "ArmsOut: one arm out fails");

            var high = Body(); // wide but way above shoulder line
            high[MotionMath.LWrist] = new Vector2(0.20f, 0.25f);
            high[MotionMath.RWrist] = new Vector2(0.80f, 0.25f);
            Check(TempleLogic.ArmsOutOk(high, conf) == false, "ArmsOut: wrists far above shoulders fail");

            var lowConf = Conf();
            lowConf[MotionMath.LWrist] = 0.1f;
            Check(TempleLogic.ArmsOutOk(t, lowConf) == null, "ArmsOut: low wrist confidence returns null");
        }

        static void TestHandsOnChest()
        {
            var conf = Conf();

            var chest = Body(); // wrists folded at the chest midpoint (0.5, 0.45)
            chest[MotionMath.LWrist] = new Vector2(0.48f, 0.44f);
            chest[MotionMath.RWrist] = new Vector2(0.52f, 0.46f);
            Check(TempleLogic.HandsOnChestOk(chest, conf) == true, "HandsOnChest: folded wrists pass");

            var t = Body();
            t[MotionMath.LWrist] = new Vector2(0.20f, 0.35f);
            t[MotionMath.RWrist] = new Vector2(0.80f, 0.35f);
            Check(TempleLogic.HandsOnChestOk(t, conf) == false, "HandsOnChest: T-pose fails");

            var hips = Body(); // hands resting on hips — close, but not the chest
            hips[MotionMath.LWrist] = new Vector2(0.44f, 0.55f);
            hips[MotionMath.RWrist] = new Vector2(0.56f, 0.55f);
            Check(TempleLogic.HandsOnChestOk(hips, conf) == false, "HandsOnChest: hands at hips fail");

            var lowConf = Conf();
            lowConf[MotionMath.RWrist] = 0.1f;
            Check(TempleLogic.HandsOnChestOk(chest, lowConf) == null, "HandsOnChest: low confidence returns null");
        }

        static void TestBalanceMeter()
        {
            Check(Near(TempleLogic.BalanceMeter01(0f), 1f), "Meter: steady = full");
            Check(Near(TempleLogic.BalanceMeter01(1.5f), 0f), "Meter: over-max wobble clamps to empty");
        }

        static void TestStarsAndCoins()
        {
            Check(TempleLogic.Stars(4) == 3 && TempleLogic.Stars(3) == 2 &&
                  TempleLogic.Stars(2) == 2 && TempleLogic.Stars(1) == 1 && TempleLogic.Stars(0) == 1,
                  "Stars: 4→3, 2-3→2, else 1");
            Check(TempleLogic.Coins(20, 15, 40f) == 95, "Coins: 20 base + lifts + stands + hold seconds",
                  $"got {TempleLogic.Coins(20, 15, 40f)}");
            Check(TempleLogic.Coins(0, 0, -5f) == 20, "Coins: negative hold seconds ignored");
        }

        static void TestResultMessage()
        {
            var r = new TempleHuntResult
            {
                chambersCompleted = 4, legLifts = 20, stands = 15,
                balanceHoldSeconds = 40f, durationSeconds = 600f, coins = 95, stars = 3,
            };
            string msg = TempleResultBridge.BuildMessage(r);
            Check(msg.StartsWith("{\"type\":\"templehunt_result\","), "Message: starts with type tag", msg);
            Check(msg.Contains("\"legLifts\":20") && msg.Contains("\"stands\":15"), "Message: carries rep fields", msg);
            Check(msg.EndsWith("}") && msg.Contains("\"stars\":3"), "Message: well-formed JSON tail", msg);
        }

        // ---- 2. Stage + scene integrity --------------------------------------

        static void TestStageBuild()
        {
            var temp = new GameObject("~SelfTestStage");
            try
            {
                var rig = TempleStage.Build(temp.transform, showChamber: 1);
                Check(rig.water != null, "Stage: water plane built");
                Check(rig.leverArm != null, "Stage: pump lever built");
                Check(rig.gateSlab != null && rig.gateSlabRenderer != null, "Stage: gate slab built");
                Check(rig.beamGlowMat != null, "Stage: beam glow material built");
                Check(rig.runeMats != null && rig.runeMats.Length == 5, "Stage: 5 totem runes built");
                Check(rig.chestLidPivot != null && rig.chestGlow != null, "Stage: treasure chest built");
                Check(rig.torches != null && rig.torches.Length == 4 && rig.torches[3] != null, "Stage: 4 torches built");
                Check(rig.chamberRoots[1] != null && rig.chamberRoots[1].activeSelf &&
                      rig.chamberRoots[3] != null && !rig.chamberRoots[3].activeSelf,
                      "Stage: only the requested chamber starts active");
            }
            catch (System.Exception e)
            {
                Check(false, "Stage: Build threw", e.Message);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        static void TestSceneIntegrity()
        {
            if (!File.Exists(ScenePath))
            {
                Check(false, "Scene: file exists", ScenePath);
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var director = Object.FindAnyObjectByType<TempleHuntDirector>();
            Check(director != null, "Scene: director present");
            if (director == null) return;

            Check(director.poseDetector != null, "Wiring: poseDetector");
            Check(director.stage != null, "Wiring: stage");
            Check(director.voice != null, "Wiring: voice");
            Check(director.rehabPoseData != null, "Wiring: rehabPoseData");
            Check(director.trainerRigPrefab != null, "Wiring: trainerRigPrefab");
            Check(!director.useKeyboardStub, "Wiring: keyboard stub OFF in the baked scene");

            Check(director.introPanel != null && director.calibPanel != null && director.hudPanel != null &&
                  director.pauseOverlay != null && director.victoryPanel != null, "Wiring: all 5 panels");
            Check(director.calibText != null && director.calibProgressFill != null, "Wiring: calibration UI");
            Check(director.chamberTitleText != null && director.chamberCountText != null &&
                  director.instructionText != null, "Wiring: chamber banner texts");
            Check(director.restText != null && director.skipRestButton != null, "Wiring: gate rest UI");
            Check(director.balanceMeterPanel != null && director.balanceMeterFill != null, "Wiring: balance meter");
            Check(director.gatePipsPanel != null && director.gatePips != null &&
                  director.gatePips.Length == 3 && director.gatePips[2] != null, "Wiring: 3 gate pips");
            Check(director.pauseText != null && director.victoryStatsText != null, "Wiring: pause + victory texts");
            Check(director.parrot != null && director.parrot.body != null &&
                  director.parrot.bubbleText != null, "Wiring: parrot guide");

            Check(director.GetComponent<TempleResultBridge>() != null, "Scene: result bridge on director");
            Check(director.GetComponent<PonyuDev.SherpaOnnx.Tts.TtsOrchestrator>() != null, "Scene: TTS orchestrator");
            Check(director.GetComponent<AudioSource>() != null, "Scene: audio source");

            var canvas = Object.FindAnyObjectByType<Canvas>();
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            Check(scaler != null && scaler.referenceResolution == new Vector2(927f, 1427f),
                  "Scene: canvas 927x1427");
            Check(Object.FindObjectsByType<RawImage>(FindObjectsSortMode.None).Length == 1,
                  "Scene: exactly one RawImage (camera feed)");
            Check(Object.FindAnyObjectByType<PoseSkeletonOverlay>() != null, "Scene: skeleton overlay");
            Check(Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null, "Scene: EventSystem");
            Check(Object.FindAnyObjectByType<TempleStage>() != null, "Scene: TempleStage root");

            // The avatar FBX ships a stray intensity-1000 light — must never be live in a scene.
            var player = GameObject.Find("PlayerCharacter");
            bool strayLight = false;
            if (player != null)
                foreach (var l in player.GetComponentsInChildren<Light>(true))
                    if (l.enabled) strayLight = true;
            Check(player != null && !strayLight, "Scene: no live lights under the avatar");
        }

        static void TestBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            // At least the 8 scenes that existed when Temple Hunt shipped (later games append more);
            // an exact count here broke every time a new game landed.
            Check(scenes.Length >= 8, "BuildSettings: >= 8 scenes", $"got {scenes.Length}");
            bool hasTemple = false;
            foreach (var s in scenes) if (s.path == ScenePath) hasTemple = true;
            Check(hasTemple, "BuildSettings: TempleHuntScene registered");
            if (scenes.Length > 0)
                Check(scenes[0].path.Contains("Boot"), "BuildSettings: Boot stays index 0", scenes[0].path);
        }
    }
}
#endif
