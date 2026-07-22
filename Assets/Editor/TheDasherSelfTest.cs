using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kinex.TheDasher.EditorTools
{
    /// <summary>
    /// TheDasher self-test: (1) pure logic — deck rules, kick adjacency, stars, score
    /// clamp, result wire format; (2) scene integrity — every director/spawner wiring the
    /// UIBuilder promises, back-view avatar orientation, detector config, build settings.
    /// Batch: -executeMethod Kinex.TheDasher.EditorTools.TheDasherSelfTest.RunBatch
    /// </summary>
    public static class TheDasherSelfTest
    {
        const string ScenePath = "Assets/Scenes/TheDasherScene.unity";

        static int _pass, _fail;

        [MenuItem("Kinex/Run TheDasher Self Test")]
        public static void Run()
        {
            _pass = 0; _fail = 0;

            TestDeck();
            TestKickRules();
            TestScoring();
            TestWireFormat();
            TestScene();

            if (_fail == 0) Debug.Log($"ASTRO SELF TEST: ALL PASS ({_pass})");
            else Debug.LogError($"SELFTEST FAIL — ASTRO SELF TEST: {_fail} failed, {_pass} passed");
        }

        public static void RunBatch()
        {
            Run();
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        static void Check(bool ok, string name, string detail = "")
        {
            if (ok) { _pass++; Debug.Log($"PASS {name}"); }
            else { _fail++; Debug.LogError($"FAIL {name} {detail}"); }
        }

        // ---- Pure logic ----

        static void TestDeck()
        {
            bool countsOk = true, firstOk = true, spreadOk = true, lanesOk = true;
            string spreadDetail = "";
            for (int seed = 1; seed <= 200; seed++)
            {
                var deck = DasherLogic.BuildDeck(seed);
                if (deck.Length != DasherLogic.BeatCount) { countsOk = false; break; }
                int meteors = deck.Count(b => b.kind == DasherKind.Meteor);
                int treasures = deck.Count(b => b.kind == DasherKind.Treasure);
                int kicks = deck.Count(b => b.kind == DasherKind.KickRing);
                int rests = deck.Count(b => b.kind == DasherKind.Rest);
                countsOk &= meteors == DasherLogic.MeteorCount && treasures == DasherLogic.TreasureCount &&
                            kicks == DasherLogic.KickCount && rests == DasherLogic.RestCount;
                firstOk &= deck[0].kind == DasherKind.Treasure;
                // The spread property: no beat repeats the previous beat's kind. Strictly stronger
                // than the old "never two meteors back-to-back" rule, and it is what stops the game
                // feeling like "one pose all the time".
                for (int i = 1; i < deck.Length; i++)
                    if (deck[i].kind == deck[i - 1].kind)
                    {
                        spreadOk = false;
                        if (spreadDetail == "") spreadDetail = $"seed {seed}, beat {i} = {deck[i].kind}";
                    }
                lanesOk &= deck.All(b => b.lane >= -1 && b.lane <= 1);
            }
            Check(countsOk, "deck: exact dose counts (8 meteor / 8 treasure / 7 kick / 5 rest), 200 seeds");
            Check(firstOk, "deck: always opens with a treasure");
            Check(spreadOk, "deck: even spread — no two adjacent beats share a kind", spreadDetail);
            Check(lanesOk, "deck: lanes within -1..1");

            var a = DasherLogic.BuildDeck(7);
            var b = DasherLogic.BuildDeck(7);
            bool same = a.Length == b.Length;
            for (int i = 0; same && i < a.Length; i++)
                same = a[i].kind == b[i].kind && a[i].lane == b[i].lane;
            Check(same, "deck: deterministic for a fixed seed");

            // ...but still genuinely random: a different seed must produce a different deck (guards
            // against the no-repeat deal degenerating into a fixed rotation).
            var c = DasherLogic.BuildDeck(8);
            bool differs = false;
            for (int i = 0; i < a.Length && !differs; i++) differs = a[i].kind != c[i].kind;
            Check(differs, "deck: a different seed gives a different deck");
        }

        static void TestKickRules()
        {
            // Ring in middle: kick with the leg nearest it.
            Check(DasherLogic.KickValid(-1, 0, kickedLeft: false), "kick: player left lane, ring middle → right leg");
            Check(!DasherLogic.KickValid(-1, 0, kickedLeft: true), "kick: player left lane, ring middle → left leg rejected");
            Check(DasherLogic.KickValid(1, 0, kickedLeft: true), "kick: player right lane, ring middle → left leg");
            // Ring in an edge lane: only the middle lane is adjacent.
            Check(DasherLogic.KickValid(0, -1, kickedLeft: true), "kick: ring left lane, player middle → left leg");
            Check(DasherLogic.KickValid(0, 1, kickedLeft: false), "kick: ring right lane, player middle → right leg");
            Check(!DasherLogic.KickValid(-1, 1, kickedLeft: false), "kick: across two lanes rejected");
            Check(!DasherLogic.KickValid(1, 1, kickedLeft: true), "kick: same lane rejected");
            Check(DasherLogic.KickFoul(0, 0), "foul: standing in ring lane");
            Check(!DasherLogic.KickFoul(-1, 0), "foul: adjacent lane is safe");
        }

        static void TestScoring()
        {
            Check(DasherLogic.DisplayScore(-3) == 0, "score: display clamps at 0");
            Check(DasherLogic.DisplayScore(5) == 5, "score: positive passes through");
            Check(DasherLogic.MaxScore == DasherLogic.TreasureCount + DasherLogic.KickCount, "score: max = treasures + kicks");
            Check(DasherLogic.Stars(12) == 3, "stars: 12/15 → 3");
            Check(DasherLogic.Stars(8) == 2, "stars: 8/15 → 2");
            Check(DasherLogic.Stars(4) == 1, "stars: 4/15 → 1");
            Check(DasherLogic.Stars(2) == 0, "stars: 2/15 → 0");
            Check(DasherLogic.Stars(-2) == 0, "stars: negative → 0");
        }

        static void TestWireFormat()
        {
            var result = new DasherResult { score = 9, treasures = 5, kicks = 4, dodges = 6, meteorHits = 1, sitStands = 7, laneSteps = 20, stars = 2, durationSeconds = 180f };
            string msg = DasherResultBridge.BuildMessage(result);
            Check(msg.StartsWith("{\"type\":\"thedasher_result\","), "wire: type tag spliced first");
            Check(msg.Contains("\"score\":9") && msg.Contains("\"treasures\":5") && msg.Contains("\"laneSteps\":20"),
                "wire: payload fields serialized", msg);
            Check(!msg.Contains("{{") && msg.EndsWith("}"), "wire: valid splice", msg);
        }

        // ---- Scene integrity ----

        static void TestScene()
        {
            Check(File.Exists(ScenePath), "scene: TheDasherScene.unity exists");
            if (!File.Exists(ScenePath)) return;

            var scenes = EditorBuildSettings.scenes;
            Check(scenes.Length >= 11, "build settings: >= 11 scenes", $"found {scenes.Length}");
            Check(scenes.Length > 0 && scenes[0].path.EndsWith("Boot.unity"), "build settings: Boot stays index 0");
            Check(scenes.Any(s => s.path == ScenePath && s.enabled), "build settings: TheDasherScene enabled");

            EditorSceneManager.OpenScene(ScenePath);

            var director = Object.FindAnyObjectByType<TheDasherDirector>();
            Check(director != null, "scene: director present");
            if (director == null) return;

            Check(director.GetComponent<DasherResultBridge>() != null, "scene: result bridge on director");
            Check(director.poseDetector != null, "wiring: poseDetector (device stub-off guard depends on it)");
            Check(director.avatarRoot != null, "wiring: avatarRoot");
            Check(director.avatarAnimator != null, "wiring: avatarAnimator");
            Check(director.spawner != null, "wiring: spawner");
            Check(director.shakeTarget != null, "wiring: shakeTarget");
            Check(director.introPanel != null && director.framingPanel != null && director.hudPanel != null &&
                  director.resultsPanel != null && director.pausePanel != null && director.cameraPreviewPanel != null,
                "wiring: all six panels");
            Check(director.framingPromptText != null && director.framingHoldRing != null, "wiring: framing prompt + ring");
            Check(director.framingChips != null && director.framingChips.Length == 5 && director.framingChips.All(c => c != null),
                "wiring: 5 framing chips");
            Check(director.calibGroup != null && director.calibText != null && director.calibRing != null, "wiring: calibration UI");
            Check(director.scoreText != null && director.timerText != null && director.timerRing != null &&
                  director.toastText != null && director.countdownText != null, "wiring: HUD texts + rings");
            Check(director.laneDots != null && director.laneDots.Length == 3 && director.laneDots.All(d => d != null),
                "wiring: 3 lane dots");
            Check(director.resultScoreText != null, "wiring: result score");
            Check(director.resultStars != null && director.resultStars.Length == 3 && director.resultStars.All(s => s != null),
                "wiring: 3 result stars");
            Check(director.resultRepValues != null && director.resultRepValues.Length == 6 && director.resultRepValues.All(t => t != null),
                "wiring: 6 result rep values");
            Check(director.useKeyboardStub, "config: stub ON in baked scene (Awake forces off on device)");

            var spawner = director.spawner;
            if (spawner != null)
                Check(spawner.thaiFont != null, "wiring: spawner thai font (kick ring label)");

            // Back view: the model child of the wrapper faces +Z (identity), NOT rotated 180.
            if (director.avatarRoot != null && director.avatarRoot.childCount > 0)
            {
                var model = director.avatarRoot.GetChild(0);
                Check(Quaternion.Angle(model.localRotation, Quaternion.identity) < 1f,
                    "back view: avatar model not rotated (faces away from camera)", model.localRotation.eulerAngles.ToString());
                Check(model.GetComponentsInChildren<Light>(true).All(l => !l.enabled),
                    "avatar: no live lights under the model (stray FBX light gotcha)");
            }

            var cam = Camera.main;
            Check(cam != null, "scene: main camera");
            if (cam != null)
            {
                Check(cam.transform.position.z < -1f, "back view: camera sits behind the avatar", cam.transform.position.ToString());
                Check(cam.allowHDR, "camera: HDR on (bloom feeds off it)");
            }

            var detector = director.poseDetector;
            if (detector != null)
            {
                var so = new SerializedObject(detector);
                Check(so.FindProperty("useOneEuroSmoothing") != null && so.FindProperty("useOneEuroSmoothing").boolValue,
                    "detector: One Euro smoothing ON");
                Check(so.FindProperty("sameSideRetarget") != null && so.FindProperty("sameSideRetarget").boolValue,
                    "detector: same-side retarget ON (back view)");
                Check(so.FindProperty("drivesAvatar").boolValue, "detector: drives avatar");
                Check(so.FindProperty("use3DWorld").boolValue, "detector: 3D world driving");
            }

            var rawImages = Object.FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Check(rawImages.Length == 1, "scene: exactly one RawImage (the camera feed)", $"found {rawImages.Length}");

            var overlay = Object.FindAnyObjectByType<PoseSkeletonOverlay>(FindObjectsInactive.Include);
            Check(overlay != null && overlay.source != null, "scene: skeleton overlay wired to detector");

            Check(Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null, "scene: EventSystem");

            var scaler = Object.FindAnyObjectByType<CanvasScaler>(FindObjectsInactive.Include);
            Check(scaler != null && scaler.referenceResolution == new Vector2(927f, 1427f),
                "scene: canvas reference resolution 927×1427");
        }
    }
}
