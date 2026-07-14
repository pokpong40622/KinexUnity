#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kinex.BattleGame;
using Kinex.Trainer;

namespace Kinex.BattleGame.EditorTools
{
    /// <summary>
    /// Pure-logic self test (no Play mode, no camera) for BattleLogic/PoseSkillLibrary/
    /// LevelLibrary/BattleResultBridge, plus a scene-integrity pass over the built
    /// BattleGameScene.unity if it exists. Same structure as FruitGameSelfTest/BalanceQuestSelfTest.
    /// Batch: -executeMethod Kinex.BattleGame.EditorTools.BattleGameSelfTest.RunBatch
    /// </summary>
    public static class BattleGameSelfTest
    {
        static readonly List<string> Failures = new List<string>();
        const string ScenePath = "Assets/Scenes/BattleGameScene.unity";

        [MenuItem("Kinex/Run Battle Game Self Test")]
        public static void Run()
        {
            int failed = RunAll();
            Debug.Log(failed == 0
                ? "[BattleGameSelfTest] ALL TESTS PASSED"
                : $"[BattleGameSelfTest] {failed} FAILURE(S):\n - " + string.Join("\n - ", Failures));
        }

        public static void RunBatch()
        {
            int failed = RunAll();
            if (failed == 0) Debug.Log("[BattleGameSelfTest] ALL TESTS PASSED");
            else Debug.LogError($"[BattleGameSelfTest] {failed} FAILURE(S):\n - " + string.Join("\n - ", Failures));
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        static int RunAll()
        {
            Failures.Clear();
            TestQualityTiers();
            TestDamageMath();
            TestComboMath();
            TestCoinsAndStars();
            TestDefendResolution();
            TestHealGate();
            TestWeightedDrawNoRepeat();
            TestWeightedDrawDegeneratePool();
            TestLevelLibrary();
            TestResultJson();
            TestFirstTimeThisSession();
            TestGhostPoseMapping();
            TestSceneIntegrity();
            return Failures.Count;
        }

        static void Check(bool cond, string label) { if (!cond) Failures.Add(label); }
        static void CheckApprox(float actual, float expected, string label, float eps = 0.001f)
        {
            if (Mathf.Abs(actual - expected) > eps) Failures.Add($"{label}: expected {expected}, got {actual}");
        }

        // ---------------------------------------------------------------- quality tiers

        static void TestQualityTiers()
        {
            Check(BattleLogic.Tier(1f) == Quality.Perfect, "quality 1.0 -> Perfect");
            Check(BattleLogic.Tier(0.85f) == Quality.Perfect, "quality 0.85 -> Perfect (boundary)");
            Check(BattleLogic.Tier(0.84f) == Quality.Good, "quality 0.84 -> Good");
            Check(BattleLogic.Tier(0.5f) == Quality.Good, "quality 0.5 -> Good (boundary)");
            Check(BattleLogic.Tier(0.49f) == Quality.Ok, "quality 0.49 -> Ok");
            Check(BattleLogic.Tier(0f) == Quality.Ok, "quality 0 -> Ok (never a fail tier)");

            CheckApprox(BattleLogic.QualityWithFloor(0f), BattleLogic.NoAttemptQualityFloor, "zero attempt floors up (never truly zero)");
            CheckApprox(BattleLogic.QualityWithFloor(0.5f), 0.5f, "above-floor quality passes through unchanged");
            CheckApprox(BattleLogic.QualityWithFloor(-1f), BattleLogic.NoAttemptQualityFloor, "negative raw quality still floors, never negative");
        }

        // ---------------------------------------------------------------- damage

        static void TestDamageMath()
        {
            CheckApprox(BattleLogic.DamageMultiplier(0f), 0.5f, "damage multiplier floor = 0.5x");
            CheckApprox(BattleLogic.DamageMultiplier(1f), 1.3f, "damage multiplier ceiling = 1.3x");
            Check(BattleLogic.DamageMultiplier(1f) > BattleLogic.DamageMultiplier(0.4f), "damage multiplier increases with quality");

            Check(BattleLogic.Damage(30f, 0f, 0) >= 1, "damage never rounds to 0 even at floor quality");
            Check(BattleLogic.Damage(30f, 1f, 0) > BattleLogic.Damage(30f, 0.2f, 0), "higher quality deals more damage");

            // Combo bonus: same base+quality, streak below vs at/above the bonus threshold.
            int below = BattleLogic.Damage(20f, 1f, BattleLogic.ComboStreakForBonus - 1);
            int atThreshold = BattleLogic.Damage(20f, 1f, BattleLogic.ComboStreakForBonus);
            Check(atThreshold > below, "combo streak >= 3 deals more damage than streak below 3");
            CheckApprox((float)atThreshold / below, BattleLogic.ComboMultiplier, "combo bonus is exactly x1.5", 0.05f);
        }

        // ---------------------------------------------------------------- combo streak

        static void TestComboMath()
        {
            Check(BattleLogic.ComboStreakAfter(0, Quality.Good) == 1, "Good extends streak");
            Check(BattleLogic.ComboStreakAfter(2, Quality.Perfect) == 3, "Perfect extends streak");
            Check(BattleLogic.ComboStreakAfter(5, Quality.Ok) == 0, "Ok resets streak to 0");

            CheckApprox(BattleLogic.ComboMultiplierFor(2), 1f, "streak 2 (below 3) = no bonus");
            CheckApprox(BattleLogic.ComboMultiplierFor(3), BattleLogic.ComboMultiplier, "streak 3 = x1.5 bonus");
            CheckApprox(BattleLogic.ComboMultiplierFor(10), BattleLogic.ComboMultiplier, "streak beyond 3 stays x1.5 (no further stacking)");
        }

        // ---------------------------------------------------------------- coins / stars

        static void TestCoinsAndStars()
        {
            Check(BattleLogic.CoinsForHit(Quality.Perfect) > BattleLogic.CoinsForHit(Quality.Good), "Perfect coins > Good coins");
            Check(BattleLogic.CoinsForHit(Quality.Good) > BattleLogic.CoinsForHit(Quality.Ok), "Good coins > Ok coins");
            Check(BattleLogic.CoinsForHit(Quality.Ok) >= 1, "Ok tier still awards at least 1 coin");
            Check(BattleLogic.BossDefeatCoins > BattleLogic.MonsterDefeatCoins, "boss bonus > regular monster bonus");

            Check(BattleLogic.Stars(100f) == 3, "stars: 100 -> 3");
            Check(BattleLogic.Stars(80f) == 3, "stars: 80 -> 3 (boundary)");
            Check(BattleLogic.Stars(79.9f) == 2, "stars: 79.9 -> 2");
            Check(BattleLogic.Stars(60f) == 2, "stars: 60 -> 2 (boundary)");
            Check(BattleLogic.Stars(59.9f) == 1, "stars: 59.9 -> 1");
            Check(BattleLogic.Stars(0f) == 1, "stars: 0 -> 1 (never zero — no fail state)");
        }

        // ---------------------------------------------------------------- defend

        static void TestDefendResolution()
        {
            Check(BattleLogic.ShieldSuccess(true), "shield succeeds while holding");
            Check(!BattleLogic.ShieldSuccess(false), "shield fails while not holding");

            Check(BattleLogic.DodgeSuccess(-1, -1), "dodge succeeds stepping to the required lane");
            Check(!BattleLogic.DodgeSuccess(1, -1), "dodge fails stepping to the wrong lane");
            Check(!BattleLogic.DodgeSuccess(0, -1), "dodge fails staying centered");
            Check(!BattleLogic.DodgeSuccess(0, 0), "requiredLane 0 never counts as a valid dodge target");

            Check(BattleLogic.ApplyDefend(3, true) == 3, "successful defend costs no heart");
            Check(BattleLogic.ApplyDefend(3, false) == 2, "failed defend costs exactly 1 heart");
            Check(BattleLogic.ApplyDefend(0, false) == 0, "hearts never go negative");

            Check(BattleLogic.ShouldRetryMonster(0), "0 hearts triggers a gentle retry");
            Check(!BattleLogic.ShouldRetryMonster(1), "1+ hearts does not trigger a retry");
        }

        // ---------------------------------------------------------------- heal gate

        static void TestHealGate()
        {
            Check(BattleLogic.HealEligible(2, 3), "heal eligible below max hearts");
            Check(!BattleLogic.HealEligible(3, 3), "heal NOT eligible at max hearts");
            Check(BattleLogic.Heal(2, 3) == 3, "heal adds exactly 1 heart");
            Check(BattleLogic.Heal(3, 3) == 3, "heal clamps at max hearts");

            var baseWeights = new float[] { 3f, 2f, 2f, 2f, 2f, 2f };
            var atMax = BattleLogic.ApplyHealGate(baseWeights, 5, hearts: 3);
            Check(atMax[5] == 0f, "heal weight zeroed at full hearts");
            Check(atMax[0] == 3f && atMax[1] == 2f, "other weights untouched by the heal gate");

            var belowMax = BattleLogic.ApplyHealGate(baseWeights, 5, hearts: 2);
            Check(belowMax[5] == 2f, "heal weight kept below full hearts");
            Check(baseWeights[5] == 2f, "ApplyHealGate never mutates the caller's array");
        }

        // ---------------------------------------------------------------- weighted draw

        static void TestWeightedDrawNoRepeat()
        {
            var weights = new float[] { 1f, 1f, 1f };
            var rng = new System.Random(12345);
            int previous = 0;
            for (int i = 0; i < 300; i++)
            {
                int idx = BattleLogic.DrawWeightedIndex(weights, previous, rng);
                Check(idx != previous, $"draw #{i} repeated the previous index ({previous})");
                previous = idx;
            }
        }

        static void TestWeightedDrawDegeneratePool()
        {
            // Only one entry has positive weight — must still return it every time (no infinite loop).
            var weights = new float[] { 0f, 5f, 0f };
            var rng = new System.Random(999);
            for (int i = 0; i < 20; i++)
                Check(BattleLogic.DrawWeightedIndex(weights, 1, rng) == 1, "single-positive-weight pool always returns that index");

            Check(BattleLogic.DrawWeightedIndex(new float[] { 0f, 0f }, -1, rng) == -1, "all-zero pool returns -1");
            Check(BattleLogic.DrawWeightedIndex(null, -1, rng) == -1, "null pool returns -1");
        }

        // ---------------------------------------------------------------- level content

        static void TestLevelLibrary()
        {
            var poolL1 = LevelLibrary.Pool(1);
            var weightsL1 = LevelLibrary.BaseWeights(1);
            Check(poolL1.Length == weightsL1.Length, "level 1 pool/weights same length");
            int earthSlamIdx = Array.IndexOf(poolL1, PoseSkillId.EarthSlam);
            Check(earthSlamIdx >= 0 && weightsL1[earthSlamIdx] > 0f, "level 1 offers Earth Slam (chair stand)");

            var weightsL2 = LevelLibrary.BaseWeights(2);
            var weightsL3 = LevelLibrary.BaseWeights(3);
            Check(weightsL2[earthSlamIdx] == 0f, "level 2 has no Earth Slam (standing-only)");
            Check(weightsL3[earthSlamIdx] == 0f, "level 3 has no Earth Slam (standing-only)");

            Check(LevelLibrary.Pool(1)[LevelLibrary.HealIndex] == PoseSkillId.FocusHeal, "HealIndex points at FocusHeal");

            for (int lvl = 1; lvl <= 3; lvl++)
            {
                var monsters = LevelLibrary.Monsters(lvl);
                Check(monsters.Length == 3, $"level {lvl} has 2 monsters + 1 boss (3 total)");
                Check(!monsters[0].isBoss && !monsters[1].isBoss, $"level {lvl}: first two are not the boss");
                Check(monsters[2].isBoss, $"level {lvl}: last monster is the boss");
                Check(monsters[2].maxHp > monsters[0].maxHp, $"level {lvl}: boss has more HP than the first monster");
                foreach (var m in monsters) Check(m.maxHp > 0, $"level {lvl}: every monster has positive HP");
                // Senior-friendly pacing: every enemy telegraph gives at least 4s reaction time
                // before the defend window starts (see BattleDirector.RunEnemyTurn).
                foreach (var m in monsters)
                    Check(m.telegraphSeconds >= 4f, $"level {lvl}: '{m.nameThai}' telegraph >= 4s (was {m.telegraphSeconds})");
            }

            Check(PoseSkillLibrary.PlayerTurnWindowSeconds >= 12f,
                "pose card window is >= 12s (senior-friendly reaction time)");
        }

        // ---------------------------------------------------------------- result JSON

        static void TestResultJson()
        {
            var result = new BattleResult
            {
                level = 2, monstersDefeated = 3, coins = 88, totalDamageDealt = 540,
                bestCombo = 6, avgQualityPercent = 91.5f, stars = 3, durationSeconds = 240.5f,
            };
            string msg = BattleResultBridge.BuildMessage(result);

            Check(msg.StartsWith("{\"type\":\"battlegame_result\","), "message starts with type tag");
            Check(msg.EndsWith("}"), "message ends with }");
            foreach (var field in new[]
                     { "\"level\":", "\"monstersDefeated\":", "\"coins\":", "\"totalDamageDealt\":",
                       "\"bestCombo\":", "\"avgQualityPercent\":", "\"stars\":", "\"durationSeconds\":" })
                Check(msg.Contains(field), $"message contains {field}");
            Check(msg.Contains("\"stars\":3") && msg.Contains("\"coins\":88"), "values serialized");
            Check(!msg.Contains("{{"), "no double brace from the splice");
        }

        // ---------------------------------------------------------------- one-time tutorial gating

        static void TestFirstTimeThisSession()
        {
            var seen = new HashSet<PoseSkillId>();
            Check(BattleLogic.FirstTimeThisSession(seen, PoseSkillId.StompQuake), "first draw of a skill is 'first time'");
            Check(!BattleLogic.FirstTimeThisSession(seen, PoseSkillId.StompQuake), "second draw of the SAME skill is not 'first time'");
            Check(BattleLogic.FirstTimeThisSession(seen, PoseSkillId.FireKick), "a DIFFERENT skill is still 'first time'");
            Check(!BattleLogic.FirstTimeThisSession(null, PoseSkillId.EarthSlam), "null seen-set never throws, just reports false");
        }

        // ---------------------------------------------------------------- pose ghost mapping

        // Guards BattleGhostPoses against the real baked asset: if RehabPoseBuilder ever renames an
        // exercise/checkpoint label, this fails loudly instead of the ghost silently vanishing.
        static void TestGhostPoseMapping()
        {
            var data = AssetDatabase.LoadAssetAtPath<TrainerPoseData>("Assets/Animations/RehabPoseData.asset");
            if (data == null)
            {
                Debug.Log("[BattleGameSelfTest] RehabPoseData.asset not found — skipping ghost-mapping checks.");
                return;
            }

            foreach (var id in new[] { PoseSkillId.EarthSlam, PoseSkillId.StompQuake, PoseSkillId.FireKick, PoseSkillId.FocusHeal })
                Check(BattleGhostPoses.FindPoseIndex(data, id) >= 0, $"ghost pose resolves for {id}");

            // Deliberately unmapped — no rehab pose covers a backward kick or a heel raise.
            Check(BattleGhostPoses.FindPoseIndex(data, PoseSkillId.TailWhip) == -1, "TailWhip has no ghost mapping (by design)");
            Check(BattleGhostPoses.FindPoseIndex(data, PoseSkillId.LightningCharge) == -1, "LightningCharge has no ghost mapping (by design)");

            Check(BattleGhostPoses.FindPoseIndex(data, BattleGhostPoses.ShieldExercise, BattleGhostPoses.ShieldCheckpoint) >= 0,
                "enemy-turn Shield ghost pose resolves");
        }

        // ---------------------------------------------------------------- scene integrity

        static void TestSceneIntegrity()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.Log("[BattleGameSelfTest] Scene not built yet — skipping scene-integrity checks.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid(), "scene opens without error");

            var director = UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
            Check(director != null, "scene contains a BattleDirector");
            if (director == null) return;

            Check(UnityEngine.Object.FindAnyObjectByType<BattleStage>() != null, "scene contains a BattleStage");
            Check(UnityEngine.Object.FindAnyObjectByType<BattleResultBridge>() != null, "scene contains a BattleResultBridge");
            Check(director.monsterSlot != null, "director.monsterSlot wired");
            Check(director.introPanel != null, "director.introPanel wired");
            Check(director.calibPanel != null, "director.calibPanel wired");
            Check(director.countdownPanel != null, "director.countdownPanel wired");
            Check(director.hudPanel != null, "director.hudPanel wired");
            Check(director.resultsPanel != null, "director.resultsPanel wired");
            Check(director.monsterHpFill != null, "director.monsterHpFill wired");
            Check(director.heartImages != null && director.heartImages.Length == 3, "director.heartImages has 3 entries");
            Check(director.cardPanel != null && director.cardNameText != null, "director card panel wired");
            Check(director.telegraphPanel != null && director.defendPanel != null, "director telegraph/defend panels wired");
            Check(director.resultsStarImages != null && director.resultsStarImages.Length == 3, "director.resultsStarImages has 3 entries");
            Check(director.cardGlow != null && director.screenGlow != null, "director glow feedback (cardGlow/screenGlow) wired");
            Check(director.successCheckmark != null, "director.successCheckmark wired");
            Check(director.tutorialPanel != null && director.tutorialNameText != null && director.tutorialInstructionText != null,
                "director tutorial panel wired");
            // rehabPoseData/trainerRigPrefab are allowed to be null (ghost quietly disabled if the
            // source assets ever move) — not asserted here, only exercised via TestGhostPoseMapping.

            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            Check(camera != null, "scene has a camera");
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            Check(canvas != null, "scene has a Canvas");

            bool inBuild = false;
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path == ScenePath) { inBuild = true; break; }
            Check(inBuild, "scene is registered in Build Settings");
        }
    }
}
#endif
