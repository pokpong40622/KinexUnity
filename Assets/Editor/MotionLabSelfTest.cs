#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Kinex.Motion;
using Kinex.MotionLab;

namespace Kinex.MotionLabEditor
{
    /// <summary>
    /// Motion Lab self-test: pure-logic unit tests for the NEW detectors (FullBodyGate,
    /// HeadYawDetector, FrontKickDetector, OneEuroFilter) on synthetic landmark arrays, plus
    /// scene wiring checks. Run: -executeMethod Kinex.MotionLabEditor.MotionLabSelfTest.Run
    /// </summary>
    public static class MotionLabSelfTest
    {
        static int _pass, _fail;

        [MenuItem("Kinex/Run Motion Lab Self Test")]
        public static void Run()
        {
            _pass = 0; _fail = 0;

            TestOneEuroFilter();
            TestFullBodyGate();
            TestHeadYaw();
            TestFrontKick();
            TestSceneWiring();

            Debug.Log(_fail == 0
                ? $"[MotionLabSelfTest] ALL PASS ({_pass}/{_pass})"
                : $"[MotionLabSelfTest] FAILURES: {_fail} failed, {_pass} passed");
            if (Application.isBatchMode && _fail > 0) EditorApplication.Exit(1);
        }

        static void Check(bool ok, string name)
        {
            if (ok) _pass++;
            else { _fail++; Debug.LogError($"[MotionLabSelfTest] FAIL: {name}"); }
        }

        // ---- OneEuroFilter ----

        static void TestOneEuroFilter()
        {
            var f = new OneEuroFilter(0.05f, 20f);
            Check(Mathf.Approximately(f.Filter(3f, 1f / 30f), 3f), "1€ first sample passes through");

            // Constant input converges (stays) at the constant.
            float v = 0f;
            for (int i = 0; i < 60; i++) v = f.Filter(3f, 1f / 30f);
            Check(Mathf.Abs(v - 3f) < 1e-3f, "1€ constant input holds steady");

            // Fast ramp: speed-adaptive cutoff keeps lag bounded (well under half the ramp span).
            var g = new OneEuroFilter(0.05f, 20f);
            float x = 0f, y = 0f;
            for (int i = 0; i < 30; i++) { x += 0.05f; y = g.Filter(x, 1f / 30f); }
            Check(Mathf.Abs(x - y) < 0.25f, "1€ tracks a fast ramp with bounded lag");

            // Reset forgets history.
            g.Reset();
            Check(Mathf.Approximately(g.Filter(10f, 1f / 30f), 10f), "1€ Reset re-seeds from next sample");
        }

        // ---- FullBodyGate ----

        static MediaPipePoseDetector.NormLandmark[] StandingLm33(float vis = 0.9f)
        {
            var lm = new MediaPipePoseDetector.NormLandmark[33];
            void Set(int i, float px, float py) => lm[i] = new MediaPipePoseDetector.NormLandmark { x = px, y = py, visibility = vis };
            for (int i = 0; i < 33; i++) Set(i, 0.5f, 0.5f);
            Set(0, 0.50f, 0.15f);            // nose
            Set(7, 0.56f, 0.15f);            // ears
            Set(8, 0.44f, 0.15f);
            Set(11, 0.58f, 0.30f);           // shoulders
            Set(12, 0.42f, 0.30f);
            Set(23, 0.55f, 0.55f);           // hips
            Set(24, 0.45f, 0.55f);
            Set(25, 0.54f, 0.75f);           // knees
            Set(26, 0.46f, 0.75f);
            Set(27, 0.54f, 0.92f);           // ankles
            Set(28, 0.46f, 0.92f);
            return lm;
        }

        static void TestFullBodyGate()
        {
            var gate = new FullBodyGate();

            gate.Tick(false, null, 0.1f);
            Check(!gate.IsFullBodyVisible && gate.Why == FullBodyGate.Reason.NotFound, "gate: no pose = NotFound");

            gate.Tick(true, StandingLm33(), 0.1f);
            Check(gate.IsFullBodyVisible && gate.Why == FullBodyGate.Reason.Ok, "gate: clean standing = Ok");
            Check(gate.GroupOk.All(b => b), "gate: all 5 groups pass on clean standing");

            var feetCut = StandingLm33();
            feetCut[27].y = 0.98f; feetCut[28].y = 0.98f; // ankles at the bottom edge
            gate.Tick(true, feetCut, 0.1f);
            Check(!gate.IsFullBodyVisible && gate.Why == FullBodyGate.Reason.FeetCut, "gate: edge ankles = FeetCut");
            Check(!gate.GroupOk[(int)FullBodyGate.Group.Feet], "gate: feet group flagged");

            var lowVisKnees = StandingLm33();
            lowVisKnees[25].visibility = 0.2f;
            gate.Tick(true, lowVisKnees, 0.1f);
            Check(gate.Why == FullBodyGate.Reason.PartlyHidden, "gate: low-vis knee = PartlyHidden");

            var tooFar = StandingLm33();
            for (int i = 0; i < 33; i++) // shrink the whole body around its center → tiny span
            {
                tooFar[i].x = 0.5f + (tooFar[i].x - 0.5f) * 0.5f;
                tooFar[i].y = 0.5f + (tooFar[i].y - 0.5f) * 0.5f;
            }
            gate.Tick(true, tooFar, 0.1f);
            Check(gate.Why == FullBodyGate.Reason.TooFar, "gate: small span = TooFar");

            var offCenter = StandingLm33();
            for (int i = 0; i < 33; i++) offCenter[i].x += 0.26f;
            gate.Tick(true, offCenter, 0.1f);
            Check(gate.Why == FullBodyGate.Reason.OffCenter, "gate: shifted body = OffCenter");

            // ValidSeconds accumulates only while valid.
            gate.Tick(true, StandingLm33(), 0.8f);
            gate.Tick(true, StandingLm33(), 0.8f);
            Check(gate.ValidSeconds >= 1.6f - 1e-3f, "gate: ValidSeconds accumulates");
            gate.Tick(false, null, 0.5f);
            Check(gate.ValidSeconds == 0f && gate.InvalidSeconds > 0f, "gate: loss resets ValidSeconds");
        }

        // ---- HeadYawDetector ----

        static void TestHeadYaw()
        {
            var head = new HeadYawDetector();
            var lm = StandingLm33();

            for (int i = 0; i < 10; i++) head.Tick(lm, 0.1f);
            Check(head.HasSignal && head.Facing == 0, "head: centered nose = facing 0");

            // Nose toward the player-RIGHT ear (smaller x, index 8) = head turned right → Facing +1.
            var turned = StandingLm33();
            turned[0].x = 0.46f;
            for (int i = 0; i < 10; i++) head.Tick(turned, 0.1f);
            Check(head.Facing == 1, "head: nose toward right ear = facing +1 (turned right)");
            Check(head.Yaw01 > 0.15f, "head: continuous yaw is positive");

            // One-frame spike back to center must NOT flip the state (sustain gate).
            head.Tick(lm, 0.05f);
            Check(head.Facing == 1, "head: one-frame flick doesn't flip state");

            // Sustained re-center returns to 0 (hysteresis exit).
            for (int i = 0; i < 12; i++) head.Tick(lm, 0.1f);
            Check(head.Facing == 0, "head: sustained center returns to 0");

            // Far-ear lost = strong turn evidence even with a centered ratio.
            var earLost = StandingLm33();
            earLost[8].visibility = 0.1f; // right ear fading → treated as one-ear view
            var head2 = new HeadYawDetector();
            for (int i = 0; i < 10; i++) head2.Tick(earLost, 0.1f);
            Check(head2.Facing != 0, "head: far ear lost forces a turn reading");
        }

        // ---- FrontKickDetector ----

        static Vector2[] StandingCoco()
        {
            var kp = new Vector2[17];
            kp[MotionMath.Nose] = new Vector2(0.50f, 0.15f);
            kp[MotionMath.LShoulder] = new Vector2(0.58f, 0.30f);
            kp[MotionMath.RShoulder] = new Vector2(0.42f, 0.30f);
            kp[MotionMath.LHip] = new Vector2(0.55f, 0.55f);
            kp[MotionMath.RHip] = new Vector2(0.45f, 0.55f);
            kp[MotionMath.LKnee] = new Vector2(0.54f, 0.75f);
            kp[MotionMath.RKnee] = new Vector2(0.46f, 0.75f);
            kp[MotionMath.LAnkle] = new Vector2(0.54f, 0.92f);
            kp[MotionMath.RAnkle] = new Vector2(0.46f, 0.92f);
            return kp;
        }

        static float[] FullConf() => Enumerable.Repeat(0.9f, 17).ToArray();

        static void TestFrontKick()
        {
            var kick = new FrontKickDetector();
            var conf = FullConf();
            var stand = StandingCoco();
            kick.SetBaseline(stand); // torso = 0.25

            kick.Tick(stand, conf, 0.1f);
            Check(!kick.JustKickedFrontLeft && !kick.JustKickedFrontRight, "kick: standing = no fire");

            // Extended front kick: ankle lifted well up, folded near the knee, under the hip in X.
            var front = StandingCoco();
            front[MotionMath.LKnee] = new Vector2(0.55f, 0.70f);
            front[MotionMath.LAnkle] = new Vector2(0.55f, 0.76f); // rise 0.16 > 0.45*0.25; gap 0.06 < 0.45*0.25
            kick.Tick(front, conf, 0.1f);
            Check(kick.JustKickedFrontLeft, "kick: extended lift fires left front kick");
            Check(!kick.JustKickedFrontRight, "kick: only the moving side fires");

            // Held up: no re-fire until it comes back down (hysteresis).
            kick.Tick(front, conf, 0.1f);
            Check(!kick.JustKickedFrontLeft, "kick: held leg doesn't re-fire");
            kick.Tick(stand, conf, 1.0f);  // back down + cooldown elapses
            kick.Tick(front, conf, 0.1f);
            Check(kick.JustKickedFrontLeft, "kick: re-arms after returning down");

            // Marching knee raise: knee up but shin hanging (big ankle-below-knee gap) must NOT fire.
            var march = StandingCoco();
            march[MotionMath.RKnee] = new Vector2(0.46f, 0.55f);
            march[MotionMath.RAnkle] = new Vector2(0.46f, 0.78f); // rise 0.14 but gap 0.23 > 0.1125
            var kick2 = new FrontKickDetector();
            kick2.SetBaseline(stand);
            kick2.Tick(march, conf, 0.1f);
            Check(!kick2.JustKickedFrontRight, "kick: marching knee raise rejected");

            // Sideways kick (ankle far from hip in X) must NOT fire the FRONT kick.
            var side = StandingCoco();
            side[MotionMath.LKnee] = new Vector2(0.66f, 0.72f);
            side[MotionMath.LAnkle] = new Vector2(0.70f, 0.74f); // rise 0.18 but |x-hip|=0.15 > 0.0875
            var kick3 = new FrontKickDetector();
            kick3.SetBaseline(stand);
            kick3.Tick(side, conf, 0.1f);
            Check(!kick3.JustKickedFrontLeft, "kick: sideways kick rejected (abduction owns it)");
        }

        // ---- Scene wiring ----

        static void TestSceneWiring()
        {
            const string scenePath = "Assets/Scenes/MotionLabScene.unity";
            Check(System.IO.File.Exists(scenePath), "scene file exists");
            Check(EditorBuildSettings.scenes.Any(s => s.path == scenePath && s.enabled), "scene in Build Settings");

            string router = System.IO.File.ReadAllText("Assets/Scripts/SceneRouter.cs");
            Check(router.Contains("\"motionlab\"") && router.Contains("MotionLabScene"), "SceneRouter case motionlab");

            if (!System.IO.File.Exists(scenePath)) return;
            EditorSceneManager.OpenScene(scenePath);

            var director = Object.FindAnyObjectByType<MotionLabDirector>();
            Check(director != null, "director in scene");
            if (director == null) return;

            Check(director.poseDetector != null, "director.poseDetector wired");
            Check(director.avatarRoot != null, "director.avatarRoot wired");
            Check(director.framingPanel != null, "director.framingPanel wired");
            Check(director.hudPanel != null, "director.hudPanel wired");
            Check(director.framingPromptText != null, "director.framingPromptText wired");
            Check(director.framingChips != null && director.framingChips.Length == 5 &&
                  director.framingChips.All(c => c != null), "director.framingChips[5] wired");
            Check(director.framingHoldRing != null, "director.framingHoldRing wired");
            Check(director.calibGroup != null && director.calibText != null && director.calibRing != null,
                  "director calibration UI wired");
            Check(director.sitStandText != null && director.sitStandChip != null, "director sit/stand UI wired");
            Check(director.facingText != null, "director.facingText wired");
            Check(director.toastText != null, "director.toastText wired");
            Check(director.debugPanel != null && director.debugText != null, "director debug UI wired");
            Check(director.useKeyboardStub, "stub defaults ON in editor scene (device Awake forces off)");

            if (director.poseDetector != null)
            {
                var so = new SerializedObject(director.poseDetector);
                Check(so.FindProperty("useOneEuroSmoothing").boolValue, "detector 1€ smoothing enabled");
                Check(so.FindProperty("drivesAvatar").boolValue, "detector drives avatar");
                Check(so.FindProperty("use3DWorld").boolValue, "detector 3D world tracking on");
            }

            var overlay = Object.FindAnyObjectByType<PoseSkeletonOverlay>(FindObjectsInactive.Include);
            Check(overlay != null && overlay.source != null, "skeleton overlay present + source wired");
            Check(overlay != null && overlay.confidenceColors, "skeleton overlay confidence colors on");

            var feed = GameObject.Find("CameraFeedPanel");
            Check(feed != null && feed.GetComponentInChildren<RawImage>(true) != null, "camera feed RawImage present");
        }
    }
}
#endif
