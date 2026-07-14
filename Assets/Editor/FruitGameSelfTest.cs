#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Kinex.FruitGame;

namespace Kinex.FruitGame.EditorTools
{
    /// <summary>
    /// Unit tests for the pure Fruit Header decision logic (no play mode needed):
    /// the item resolution matrix, combo/rep/set bookkeeping, star calculation and the
    /// result JSON wire format. Batchmode entry (RunBatch) exits 0 on success, 1 on failure.
    /// </summary>
    public static class FruitGameSelfTest
    {
        static readonly List<string> Failures = new List<string>();

        [MenuItem("Kinex/Run Fruit Game Self Test")]
        public static void Run()
        {
            int failed = RunAll();
            Debug.Log(failed == 0
                ? "[FruitGameSelfTest] ALL TESTS PASSED"
                : $"[FruitGameSelfTest] {failed} FAILURE(S):\n - " + string.Join("\n - ", Failures));
        }

        public static void RunBatch()
        {
            int failed = RunAll();
            if (failed == 0) Debug.Log("[FruitGameSelfTest] ALL TESTS PASSED");
            else Debug.LogError($"[FruitGameSelfTest] {failed} FAILURE(S):\n - " + string.Join("\n - ", Failures));
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        static int RunAll()
        {
            Failures.Clear();
            TestResolutionMatrix();
            TestStars();
            TestAccuracy();
            TestScoreStateSession();
            TestResultJson();
            return Failures.Count;
        }

        static void Check(bool condition, string label)
        {
            if (!condition) Failures.Add(label);
        }

        // ---- The 4-cell decision matrix. ----
        static void TestResolutionMatrix()
        {
            Check(FruitGameLogic.Resolve(healthy: true, stood: true) == FruitGameLogic.Outcome.HeadedHealthy,
                  "healthy+stood → HeadedHealthy");
            Check(FruitGameLogic.Resolve(healthy: true, stood: false) == FruitGameLogic.Outcome.SoftMiss,
                  "healthy+seated → SoftMiss");
            Check(FruitGameLogic.Resolve(healthy: false, stood: true) == FruitGameLogic.Outcome.GentleMiss,
                  "junk+stood → GentleMiss");
            Check(FruitGameLogic.Resolve(healthy: false, stood: false) == FruitGameLogic.Outcome.SmartChoice,
                  "junk+seated → SmartChoice");

            Check(FruitGameLogic.IsCorrect(FruitGameLogic.Outcome.HeadedHealthy), "HeadedHealthy is correct");
            Check(FruitGameLogic.IsCorrect(FruitGameLogic.Outcome.SmartChoice), "SmartChoice is correct");
            Check(!FruitGameLogic.IsCorrect(FruitGameLogic.Outcome.SoftMiss), "SoftMiss is not correct");
            Check(!FruitGameLogic.IsCorrect(FruitGameLogic.Outcome.GentleMiss), "GentleMiss is not correct");
        }

        // ---- 3★ = 3 sets AND ≥80; 2★ = ≥2 sets OR ≥60; else 1★. ----
        static void TestStars()
        {
            Check(FruitGameLogic.Stars(3, 80f) == 3, "3 sets + 80% → 3★");
            Check(FruitGameLogic.Stars(3, 100f) == 3, "3 sets + 100% → 3★");
            Check(FruitGameLogic.Stars(3, 79.9f) == 2, "3 sets + 79.9% → 2★ (accuracy gate)");
            Check(FruitGameLogic.Stars(2, 100f) == 2, "2 sets + 100% → 2★ (needs 3 sets for 3★)");
            Check(FruitGameLogic.Stars(2, 10f) == 2, "2 sets + 10% → 2★ (sets alone qualify)");
            Check(FruitGameLogic.Stars(1, 60f) == 2, "1 set + 60% → 2★ (accuracy alone qualifies)");
            Check(FruitGameLogic.Stars(1, 59f) == 1, "1 set + 59% → 1★");
            Check(FruitGameLogic.Stars(0, 0f) == 1, "nothing → 1★ (never zero — no fail state)");
        }

        static void TestAccuracy()
        {
            Check(Mathf.Approximately(FruitGameLogic.AccuracyPercent(8, 10), 80f), "8/10 → 80%");
            Check(Mathf.Approximately(FruitGameLogic.AccuracyPercent(0, 10), 0f), "0/10 → 0%");
            Check(Mathf.Approximately(FruitGameLogic.AccuracyPercent(10, 10), 100f), "10/10 → 100%");
            Check(Mathf.Approximately(FruitGameLogic.AccuracyPercent(0, 0), 0f), "0 items → 0%");
        }

        // ---- Whole-session bookkeeping through FruitScoreState. ----
        static void TestScoreStateSession()
        {
            var s = new FruitScoreState();

            // Set 1: 15 perfect heads.
            s.BeginSet();
            for (int i = 0; i < 15; i++)
                Check(s.Record(healthy: true, stood: true) == FruitGameLogic.Outcome.HeadedHealthy,
                      $"set1 rep {i + 1} outcome");
            Check(s.RepsInSet == 15, "set1 RepsInSet == 15");
            Check(s.Reps == 15, "Reps == 15 after set1");
            Check(s.Combo == 15, "Combo == 15 after 15 straight");
            s.CompleteSet();
            Check(s.SetsCompleted == 1, "SetsCompleted == 1");

            // Bonus round 1.
            s.AddBonusReps(20);
            Check(s.BonusReps == 20, "BonusReps == 20");

            // Set 2: mixed — a junk stood (combo reset), a smart choice, a soft miss, then reps.
            s.BeginSet();
            Check(s.RepsInSet == 0, "BeginSet resets RepsInSet");
            Check(s.Record(false, true) == FruitGameLogic.Outcome.GentleMiss, "set2 junk+stood");
            Check(s.Combo == 0, "combo reset by GentleMiss");
            Check(s.Record(false, false) == FruitGameLogic.Outcome.SmartChoice, "set2 smart choice");
            Check(s.SmartChoices == 1, "SmartChoices == 1");
            Check(s.Combo == 1, "combo extends on SmartChoice");
            Check(s.Record(true, false) == FruitGameLogic.Outcome.SoftMiss, "set2 soft miss");
            Check(s.Combo == 0, "combo reset by SoftMiss");
            for (int i = 0; i < 15; i++) s.Record(true, true);
            Check(s.RepsInSet == 15, "set2 RepsInSet == 15");
            Check(s.Reps == 30, "Reps == 30 total");
            s.CompleteSet();
            s.AddBonusReps(12); // partial balloon
            Check(s.BonusReps == 32, "BonusReps accumulates (20+12)");

            // Set 3: all 15.
            s.BeginSet();
            for (int i = 0; i < 15; i++) s.Record(true, true);
            s.CompleteSet();

            Check(s.SetsCompleted == 3, "SetsCompleted == 3");
            Check(s.Reps == 45, "Reps == 45 max");
            Check(s.TotalItems == 48, "TotalItems == 48 (45 heads + 3 set-2 extras)");
            Check(s.CorrectDecisions == 46, "CorrectDecisions == 46 (45 heads + 1 smart)");
            float expected = 100f * 46f / 48f; // ≈ 95.8
            Check(Mathf.Approximately(s.AccuracyPercent, expected), "accuracy ≈ 95.8%");
            Check(s.Stars == 3, "3 sets + ≥80% → 3★");

            var result = s.ToResult(612f);
            Check(result.reps == 45 && result.setsCompleted == 3 && result.smartChoices == 1 &&
                  result.bonusReps == 32 && result.stars == 3 &&
                  Mathf.Approximately(result.durationSeconds, 612f) &&
                  Mathf.Approximately(result.accuracyPercent, expected),
                  "ToResult copies every field");
        }

        // ---- Wire format: {"type":"fruitgame_result", ...all fields...}. ----
        static void TestResultJson()
        {
            var result = new FruitGameResult
            {
                reps = 45, setsCompleted = 3, accuracyPercent = 91.5f,
                smartChoices = 7, bonusReps = 40, stars = 3, durationSeconds = 600.5f,
            };
            string msg = FruitGameResultBridge.BuildMessage(result);

            Check(msg.StartsWith("{\"type\":\"fruitgame_result\","), "message starts with type tag");
            Check(msg.EndsWith("}"), "message ends with }");
            foreach (var field in new[]
                     { "\"reps\":", "\"setsCompleted\":", "\"accuracyPercent\":",
                       "\"smartChoices\":", "\"bonusReps\":", "\"stars\":", "\"durationSeconds\":" })
                Check(msg.Contains(field), $"message contains {field}");
            Check(msg.Contains("\"reps\":45") && msg.Contains("\"stars\":3"), "values serialized");
            // Exactly one opening brace at the start — the splice must not double-wrap.
            Check(!msg.Contains("{{"), "no double brace from the splice");
        }
    }
}
#endif
