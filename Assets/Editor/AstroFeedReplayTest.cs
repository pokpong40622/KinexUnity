using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Kinex.Motion;

namespace Kinex.AstroStance.EditorTools
{
    /// <summary>
    /// Replays REAL recorded pose feeds through the C# motion detectors — the AstroStance
    /// de-risk gate. Landmark JSONs are extracted from the user's tablet videos by
    /// tools/extract_landmarks.py (same BlazePose model file the Unity plugin ships) and
    /// live in Assets/AstroStance/TestFeeds/. Feed facts this test encodes:
    ///   - "go left" moves the hip mid toward image +X (unmirrored front camera:
    ///     player-left = frame-right), so raw Lane +1 == player stepped LEFT.
    ///   - SitStandDetector boots in Seated phase, so a standing player fires one
    ///     synthetic JustStood before real reps — the director must swallow it.
    /// </summary>
    public static class AstroFeedReplayTest
    {
        const string FeedDir = "Assets/AstroStance/TestFeeds";

        // Same source→COCO mapping as MediaPipePoseDetector.MP_TO_COCO.
        static readonly (int mp, int coco)[] MpToCoco =
        {
            (0, MotionMath.Nose), (7, 3), (8, 4),
            (11, MotionMath.LShoulder), (12, MotionMath.RShoulder),
            (13, MotionMath.LElbow), (14, MotionMath.RElbow),
            (15, MotionMath.LWrist), (16, MotionMath.RWrist),
            (23, MotionMath.LHip), (24, MotionMath.RHip),
            (25, MotionMath.LKnee), (26, MotionMath.RKnee),
            (27, MotionMath.LAnkle), (28, MotionMath.RAnkle),
        };

        [System.Serializable] class Feed { public string name; public float fps; public Frame[] frames; }
        [System.Serializable] class Frame { public float[] v; }

        static int _pass, _fail;

        [MenuItem("Kinex/Run AstroStance Feed Replay Test")]
        public static void Run()
        {
            _pass = 0; _fail = 0;

            TestLane("goleft", expectedLane: +1);
            TestLane("goright", expectedLane: -1);
            TestSitStand("standsitstand");
            TestKick("kickleft", expectLeft: true);
            TestKick("kickright", expectLeft: false);

            if (_fail == 0) Debug.Log($"ASTRO FEED TEST: ALL PASS ({_pass})");
            else Debug.LogError($"SELFTEST FAIL — ASTRO FEED TEST: {_fail} failed, {_pass} passed");
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

        static Feed Load(string name)
        {
            string path = Path.Combine(FeedDir, name + ".json");
            if (!File.Exists(path)) return null;
            var feed = JsonUtility.FromJson<Feed>(File.ReadAllText(path));
            return feed != null && feed.frames != null && feed.frames.Length > 0 ? feed : null;
        }

        /// <summary>Convert one 33-landmark frame (flat x,y,z,vis ×33) to COCO-17 kp + conf.</summary>
        static bool ToCoco(Frame f, Vector2[] kp, float[] conf)
        {
            if (f.v == null || f.v.Length < 33 * 4) return false;
            for (int i = 0; i < 17; i++) conf[i] = 0f;
            foreach (var (mp, coco) in MpToCoco)
            {
                kp[coco] = new Vector2(f.v[mp * 4], f.v[mp * 4 + 1]);
                conf[coco] = f.v[mp * 4 + 3];
            }
            return true;
        }

        static void TestLane(string name, int expectedLane)
        {
            var feed = Load(name);
            Check(feed != null, $"{name}: feed json loads");
            if (feed == null) return;

            var lane = new LaneDetector();
            var kp = new Vector2[17];
            var conf = new float[17];
            float dt = 1f / Mathf.Max(feed.fps, 1f);

            var lanesSeen = new HashSet<int>();
            for (int i = 0; i < feed.frames.Length; i++)
            {
                if (!ToCoco(feed.frames[i], kp, conf)) continue;
                if (i < 8) { lane.CalibrateCenter(kp); continue; } // player starts centered/still
                lane.Tick(kp, conf, dt);
                lanesSeen.Add(lane.Lane);
            }

            Check(lanesSeen.Contains(expectedLane), $"{name}: reaches lane {expectedLane}",
                $"lanes seen: {string.Join(",", lanesSeen)}");
            Check(!lanesSeen.Contains(-expectedLane), $"{name}: never enters opposite lane",
                $"lanes seen: {string.Join(",", lanesSeen)}");
            Check(lane.Lane == expectedLane, $"{name}: ends in lane {expectedLane}",
                $"final lane: {lane.Lane}");
        }

        static void TestSitStand(string name)
        {
            var feed = Load(name);
            Check(feed != null, $"{name}: feed json loads");
            if (feed == null) return;

            var sitStand = new SitStandDetector();
            var kp = new Vector2[17];
            var conf = new float[17];
            float dt = 1f / Mathf.Max(feed.fps, 1f);

            // Player starts STANDING: calibrate standing on the first second, derive seated.
            // 0.30 = AstroStance's shallow-sit factor (measured chair sits drop ~0.37 torso
            // and seated torso-shrink eats part of that) — MUST match the director's value.
            int calFrames = Mathf.Min(30, feed.frames.Length / 3);
            for (int i = 0; i < calFrames; i++)
                if (ToCoco(feed.frames[i], kp, conf))
                    sitStand.CalibrateStanding(kp);
            sitStand.EstimateSeatedFromStanding(0.30f);
            Check(sitStand.IsCalibrated, $"{name}: detector calibrated");

            // Boot quirk: state machine starts Seated, so the standing player yields one
            // synthetic JustStood almost immediately. Track order, not raw counts.
            int sats = 0, stoodAfterSat = 0, synthetic = 0;
            for (int i = 0; i < feed.frames.Length; i++)
            {
                if (!ToCoco(feed.frames[i], kp, conf)) continue;
                sitStand.Tick(kp, conf, dt);
                if (sitStand.JustStood) { if (sats > 0) stoodAfterSat++; else synthetic++; }
                if (sitStand.JustSat) sats++;
            }

            Check(sats == 1, $"{name}: exactly one sit detected", $"sats={sats}");
            Check(stoodAfterSat == 1, $"{name}: stood back up after the sit", $"stoodAfterSat={stoodAfterSat}");
            Check(synthetic <= 1, $"{name}: at most one synthetic boot-stand", $"synthetic={synthetic}");
        }

        static void TestKick(string name, bool expectLeft)
        {
            var feed = Load(name);
            Check(feed != null, $"{name}: feed json loads");
            if (feed == null) return;

            var abduction = new LegAbductionDetector();
            var kp = new Vector2[17];
            var conf = new float[17];
            float dt = 1f / Mathf.Max(feed.fps, 1f);

            int left = 0, right = 0;
            bool baselined = false;
            for (int i = 0; i < feed.frames.Length; i++)
            {
                if (!ToCoco(feed.frames[i], kp, conf)) continue;
                if (!baselined && i >= 4) { abduction.SetBaseline(kp); baselined = true; continue; }
                if (!baselined) continue;
                abduction.Tick(kp, conf, dt);
                if (abduction.JustKickedLeft) left++;
                if (abduction.JustKickedRight) right++;
            }

            int expected = expectLeft ? left : right;
            int wrongSide = expectLeft ? right : left;
            string side = expectLeft ? "left" : "right";
            Check(expected >= 1, $"{name}: {side} kick detected", $"left={left} right={right}");
            Check(wrongSide == 0, $"{name}: no opposite-side kick", $"left={left} right={right}");
        }
    }
}
