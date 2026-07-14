#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Kinex.BalanceQuest;
using Kinex.MegaDance;

namespace Kinex.BalanceQuest.EditorTools
{
    /// <summary>
    /// Pure-logic self test — no Play mode, no camera, no scene needed. Exercises the exact
    /// static functions gameplay uses (BeatScoring, RouteLibrary, BalanceQuestDirector.MakeRunner
    /// / ComputeStars, BalanceQuestResultBridge.ToJsonMessage, ArmPoseSignatures,
    /// HipExtensionDetector) rather than re-typed copies.
    /// Batch: -executeMethod Kinex.BalanceQuest.EditorTools.BalanceQuestSelfTest.RunBatch
    /// </summary>
    public static class BalanceQuestSelfTest
    {
        static readonly List<string> Failures = new List<string>();

        [MenuItem("Kinex/Run Balance Quest Self Test")]
        public static void Run()
        {
            int fails = RunAll();
            Debug.Log(fails == 0
                ? "[BalanceQuestSelfTest] ALL TESTS PASSED."
                : $"[BalanceQuestSelfTest] {fails} FAILURE(S):\n - " + string.Join("\n - ", Failures));
        }

        public static void RunBatch()
        {
            int fails = RunAll();
            if (fails == 0) Debug.Log("[BalanceQuestSelfTest] ALL TESTS PASSED.");
            else Debug.LogError($"[BalanceQuestSelfTest] {fails} FAILURE(S):\n - " + string.Join("\n - ", Failures));
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static int RunAll()
        {
            Failures.Clear();
            TestRoute();
            TestBeatScoreMath();
            TestWeightedAverageExcludesWalk();
            TestStars();
            TestResultJson();
            TestArmPoseSignatures();
            TestHipExtensionDetector();
            return Failures.Count;
        }

        static void Check(bool cond, string name)
        {
            if (!cond) Failures.Add(name);
        }

        static void CheckApprox(float actual, float expected, string name, float eps = 0.001f)
        {
            if (Mathf.Abs(actual - expected) > eps) Failures.Add($"{name}: expected {expected}, got {actual}");
        }

        // ---------------------------------------------------------------- route

        static void TestRoute()
        {
            var route = RouteLibrary.DefaultRoute;
            Check(route != null && route.Length > 0, "route non-empty");

            float total = RouteLibrary.TotalSeconds;
            // Fixed windows from the design brief alone (bridge 20, kick 20x2, rest stop 75, ...)
            // already sum past 5 minutes; walks/gates are pinned to their design minimum. Accept a
            // tolerant band around the ~5-6 min target instead of an exact number.
            Check(total >= 280f && total <= 420f, $"route total ~5min band (280-420s), got {total}");

            Check(route[route.Length - 1].type == BeatType.Checkpoint, "route ends with a Checkpoint");

            var present = new HashSet<BeatType>();
            foreach (var b in route) present.Add(b.type);
            foreach (BeatType t in Enum.GetValues(typeof(BeatType)))
                Check(present.Contains(t), $"route contains at least one {t}");

            foreach (var b in route)
                Check(b.duration > 0f, $"beat {b.type} duration > 0");
        }

        // ---------------------------------------------------------------- score math

        static void TestBeatScoreMath()
        {
            CheckApprox(BeatScoring.Gate(true), 1f, "gate pass = 1.0");
            CheckApprox(BeatScoring.Gate(false), 0.25f, "gate fail = 0.25");

            CheckApprox(BeatScoring.Bridge(10f, 1f, 0f), 1f, "bridge perfect = 1.0");
            CheckApprox(BeatScoring.Bridge(5f, 0.8f, 0.4f), 0.6f * 0.5f + 0.25f * 0.8f + 0.15f * 0.6f, "bridge formula");
            CheckApprox(BeatScoring.Bridge(20f, 1f, 0f), 1f, "bridge hold clamps at target");
            CheckApprox(BeatScoring.Bridge(0f, 0f, 1f), 0f, "bridge floor = 0");

            CheckApprox(BeatScoring.Tiptoe(0), 0f, "tiptoe 0/3");
            CheckApprox(BeatScoring.Tiptoe(2), 2f / 3f, "tiptoe 2/3");
            CheckApprox(BeatScoring.Tiptoe(3), 1f, "tiptoe 3/3");

            CheckApprox(BeatScoring.Kick(3), 0.75f, "kick 3/4");
            CheckApprox(BeatScoring.BackKick(4), 1f, "backkick 4/4");

            CheckApprox(BeatScoring.Tandem(10f, 0f), 1f, "tandem perfect");
            CheckApprox(BeatScoring.Tandem(5f, 0.5f), 0.5f * 0.5f + 0.5f * 0.5f, "tandem formula");

            CheckApprox(BeatScoring.BeamWalk(10, 1f), 1f, "beamwalk perfect");
            CheckApprox(BeatScoring.BeamWalk(5, 0.5f), 0.7f * 0.5f + 0.3f * 0.5f, "beamwalk formula");

            CheckApprox(BeatScoring.RestStop(5, 6), 1f, "reststop perfect");
            CheckApprox(BeatScoring.RestStop(3, 3), 0.7f * 0.6f + 0.3f * 0.5f, "reststop formula");
        }

        // ---------------------------------------------------------------- aggregation

        static void TestWeightedAverageExcludesWalk()
        {
            // MakeRunner is the SAME gate RunSession uses: null runner => excluded from scoring.
            Check(BalanceQuestDirector.MakeRunner(BeatType.Walk) == null, "Walk excluded from scoring");
            Check(BalanceQuestDirector.MakeRunner(BeatType.Checkpoint) == null, "Checkpoint excluded from scoring");
            foreach (var t in new[] { BeatType.Gate, BeatType.Bridge, BeatType.Tiptoe, BeatType.Kick,
                                      BeatType.BackKick, BeatType.TandemStand, BeatType.BeamWalk, BeatType.RestStop })
                Check(BalanceQuestDirector.MakeRunner(t) != null, $"{t} has a runner (scored)");

            // Replicate RunSession's aggregation over a mini route: a perfect-score Walk beat must
            // NOT drag the average toward 1 because it never enters the weighted sum at all.
            var beats = new[]
            {
                new QuestBeat(BeatType.Walk, 100f),      // huge duration — would dominate if included
                new QuestBeat(BeatType.Gate, 4f),        // score 1.0
                new QuestBeat(BeatType.Bridge, 20f),     // score 0.5
            };
            var scores = new Dictionary<BeatType, float> { { BeatType.Gate, 1f }, { BeatType.Bridge, 0.5f } };

            float weightedSum = 0f, weightedDuration = 0f;
            foreach (var b in beats)
            {
                if (BalanceQuestDirector.MakeRunner(b.type) == null) continue;
                weightedSum += scores[b.type] * b.duration;
                weightedDuration += b.duration;
            }
            float avg = weightedSum / weightedDuration;
            CheckApprox(avg, (1f * 4f + 0.5f * 20f) / 24f, "weighted average excludes Walk");
        }

        // ---------------------------------------------------------------- stars

        static void TestStars()
        {
            Check(BalanceQuestDirector.ComputeStars(100f) == 3, "stars: 100 -> 3");
            Check(BalanceQuestDirector.ComputeStars(80f) == 3, "stars: 80 -> 3");
            Check(BalanceQuestDirector.ComputeStars(79.9f) == 2, "stars: 79.9 -> 2");
            Check(BalanceQuestDirector.ComputeStars(60f) == 2, "stars: 60 -> 2");
            Check(BalanceQuestDirector.ComputeStars(59.9f) == 1, "stars: 59.9 -> 1");
            Check(BalanceQuestDirector.ComputeStars(40f) == 1, "stars: 40 -> 1");
            Check(BalanceQuestDirector.ComputeStars(0f) == 1, "stars: 0 -> 1 (never zero)");
        }

        // ---------------------------------------------------------------- result JSON

        static void TestResultJson()
        {
            var result = new BalanceQuestResult
            {
                averagePercent = 72.5f,
                coins = 14,
                stars = 2,
                durationSeconds = 348f,
            };
            result.beats.Add(new BalanceQuestResult.BeatResult { type = "Gate", score = 100f });
            result.beats.Add(new BalanceQuestResult.BeatResult { type = "Bridge", score = 55f });

            string json = BalanceQuestResultBridge.ToJsonMessage(result);
            Check(json.Contains("\"type\":\"balancequest_result\""), "json has balancequest_result type tag");
            Check(json.Contains("averagePercent"), "json has averagePercent");
            Check(json.Contains("coins"), "json has coins");
            Check(json.Contains("stars"), "json has stars");
            Check(json.Contains("durationSeconds"), "json has durationSeconds");
            Check(json.Contains("beats"), "json has beats array");
            Check(json.Contains("Gate") && json.Contains("Bridge"), "json beats include entries");
            Check(json.StartsWith("{") && json.EndsWith("}"), "json braces balanced at ends");
        }

        // ---------------------------------------------------------------- arm signatures

        static void TestArmPoseSignatures()
        {
            float tol = 45f * Mathf.Deg2Rad;
            var a = ArmPoseSignatures.ArmsOut;
            var b = ArmPoseSignatures.HandsStacked;
            var c = ArmPoseSignatures.ArmsCrossed;

            Check(a != null && a.Length == PoseScorer.NumLimbs, "ArmsOut is 8 angles");
            Check(b != null && b.Length == PoseScorer.NumLimbs, "HandsStacked is 8 angles");
            Check(c != null && c.Length == PoseScorer.NumLimbs, "ArmsCrossed is 8 angles");

            float selfA = PoseScorer.ScoreAngles(a, a, tol);
            float selfB = PoseScorer.ScoreAngles(b, b, tol);
            float selfC = PoseScorer.ScoreAngles(c, c, tol);
            CheckApprox(selfA, 1f, "ArmsOut vs itself = 1");
            CheckApprox(selfB, 1f, "HandsStacked vs itself = 1");
            CheckApprox(selfC, 1f, "ArmsCrossed vs itself = 1");

            // Legs are identical across the three variants (4 of 8 limbs), so cross scores sit
            // well above 0 — distinctness just requires them clearly below the identical score.
            float ab = PoseScorer.ScoreAngles(a, b, tol);
            float ac = PoseScorer.ScoreAngles(a, c, tol);
            float bc = PoseScorer.ScoreAngles(b, c, tol);
            Check(ab < selfA - 0.05f, $"ArmsOut vs HandsStacked distinct (got {ab})");
            Check(ac < selfA - 0.05f, $"ArmsOut vs ArmsCrossed distinct (got {ac})");
            Check(bc < selfB - 0.05f, $"HandsStacked vs ArmsCrossed distinct (got {bc})");
        }

        // ---------------------------------------------------------------- hip extension detector

        static void TestHipExtensionDetector()
        {
            var det = new Kinex.Motion.HipExtensionDetector();
            var lm = MakeStandingLandmarks();
            det.SetBaseline(lm);

            // Left ankle swings back (z increases well past 0.5 * hipWidth): should fire once.
            lm[27].z = 0.15f; // hipWidth = 0.1 -> threshold dz > 0.05 (z is EMA-smoothed, so tick a few times)
            bool fired = false;
            for (int i = 0; i < 10; i++)
            {
                det.Tick(lm, 0.1f);
                fired |= det.JustKickedBackLeft;
            }
            Check(fired, "hip extension: back-swing fires JustKickedBackLeft");
            Check(!det.JustKickedBackRight && !det.RightBack, "hip extension: right side untouched");

            // Sideways kick (big x offset) must NOT fire: the X-gate rules out abduction.
            det = new Kinex.Motion.HipExtensionDetector();
            lm = MakeStandingLandmarks();
            det.SetBaseline(lm);
            lm[27].z = 0.15f;
            lm[27].x = lm[23].x + 0.2f; // way outside XGateFactor * torso (0.25 * 0.25 = 0.0625)
            fired = false;
            for (int i = 0; i < 10; i++)
            {
                det.Tick(lm, 0.1f);
                fired |= det.JustKickedBackLeft;
            }
            Check(!fired, "hip extension: sideways kick gated out (abduction is not a back-kick)");
        }

        static MediaPipePoseDetector.NormLandmark[] MakeStandingLandmarks()
        {
            var lm = new MediaPipePoseDetector.NormLandmark[33];
            for (int i = 0; i < 33; i++) lm[i] = new MediaPipePoseDetector.NormLandmark { visibility = 1f };
            lm[11] = new MediaPipePoseDetector.NormLandmark { x = 0.40f, y = 0.30f, visibility = 1f }; // L shoulder
            lm[12] = new MediaPipePoseDetector.NormLandmark { x = 0.60f, y = 0.30f, visibility = 1f }; // R shoulder
            lm[23] = new MediaPipePoseDetector.NormLandmark { x = 0.45f, y = 0.55f, visibility = 1f }; // L hip
            lm[24] = new MediaPipePoseDetector.NormLandmark { x = 0.55f, y = 0.55f, visibility = 1f }; // R hip
            lm[25] = new MediaPipePoseDetector.NormLandmark { x = 0.45f, y = 0.75f, visibility = 1f }; // L knee
            lm[26] = new MediaPipePoseDetector.NormLandmark { x = 0.55f, y = 0.75f, visibility = 1f }; // R knee
            lm[27] = new MediaPipePoseDetector.NormLandmark { x = 0.45f, y = 0.95f, visibility = 1f }; // L ankle
            lm[28] = new MediaPipePoseDetector.NormLandmark { x = 0.55f, y = 0.95f, visibility = 1f }; // R ankle
            return lm;
        }
    }
}
#endif
