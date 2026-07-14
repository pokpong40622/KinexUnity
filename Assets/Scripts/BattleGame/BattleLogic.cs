using System;
using System.Collections.Generic;

namespace Kinex.BattleGame
{
    /// <summary>Quality tier for one player pose-attack attempt. There is deliberately no "fail"
    /// tier — even the floor (Ok) still deals real damage (senior-friendly, never zero).</summary>
    public enum Quality { Ok, Good, Perfect }

    /// <summary>
    /// Pure turn-resolution math for "ผู้พิทักษ์สวนสมดุล" (Guardian of Balance) — damage calc,
    /// quality tiers, combo streak, coin/star totals, defend resolution and the weighted
    /// no-immediate-repeat card draw. No UnityEngine dependency at all (not even Mathf) so
    /// BattleGameSelfTest can exercise every formula directly, exactly like Kinex.FruitGame.
    /// FruitGameLogic and Kinex.BalanceQuest.BeatScoring do for their own games.
    /// </summary>
    public static class BattleLogic
    {
        public const int MaxHearts = 3;
        public const int ComboStreakForBonus = 3;
        public const float ComboMultiplier = 1.5f;

        public const float PerfectQuality = 0.85f;
        public const float GoodQuality = 0.50f;

        /// <summary>A missed/timed-out attempt is never scored as truly zero — this is the floor
        /// every raw quality value is clamped up to before grading (partial credit ALWAYS).</summary>
        public const float NoAttemptQualityFloor = 0.15f;

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>achieved/target, clamped to 0..1. target <= 0 reads as 0 (nothing to divide by).</summary>
        public static float Ratio(float achieved, float target) => target <= 0f ? 0f : Clamp01(achieved / target);

        /// <summary>Applies the senior-friendly floor: raw quality is never allowed to grade as
        /// truly zero-effort. Call this once on the raw metric before Tier/Damage.</summary>
        public static float QualityWithFloor(float raw01) => Math.Max(NoAttemptQualityFloor, Clamp01(raw01));

        public static Quality Tier(float quality01)
        {
            quality01 = Clamp01(quality01);
            if (quality01 >= PerfectQuality) return Quality.Perfect;
            if (quality01 >= GoodQuality) return Quality.Good;
            return Quality.Ok;
        }

        /// <summary>0.5x (Ok floor) .. 1.3x (Perfect) — damage scales continuously with quality,
        /// so "Lightning Charge" (fill-based) and event/count-based skills share one formula.</summary>
        public static float DamageMultiplier(float quality01) => 0.5f + 0.8f * Clamp01(quality01);

        /// <summary>Combo streak: extends on Good/Perfect, resets to 0 on Ok.</summary>
        public static int ComboStreakAfter(int streakBefore, Quality tier) => tier == Quality.Ok ? 0 : streakBefore + 1;

        /// <summary>x1.5 once the streak (BEFORE this hit) has reached 3 consecutive Good+ hits.</summary>
        public static float ComboMultiplierFor(int streakBeforeThisHit) =>
            streakBeforeThisHit >= ComboStreakForBonus ? ComboMultiplier : 1f;

        /// <summary>Final integer damage for one attack. Never less than 1 — a genuine miss still
        /// lands a token hit rather than feeling like nothing happened.</summary>
        public static int Damage(float baseDamage, float quality01, int comboStreakBeforeThisHit)
        {
            float mult = DamageMultiplier(quality01) * ComboMultiplierFor(comboStreakBeforeThisHit);
            int dmg = (int)Math.Round(baseDamage * mult, MidpointRounding.AwayFromZero);
            return Math.Max(1, dmg);
        }

        public static int CoinsForHit(Quality tier) => tier switch
        {
            Quality.Perfect => 5,
            Quality.Good => 3,
            _ => 1,
        };

        public const int MonsterDefeatCoins = 10;
        public const int BossDefeatCoins = 25;

        // ---- Defend (enemy turn) ----
        public static bool ShieldSuccess(bool isHolding) => isHolding;

        /// <summary>Dodge succeeds only by stepping into the SPECIFIC lane the telegraph called
        /// (never lane 0/center — that's "didn't move").</summary>
        public static bool DodgeSuccess(int currentLane, int requiredLane) =>
            requiredLane != 0 && currentLane == requiredLane;

        public static int ApplyDefend(int hearts, bool success) => success ? hearts : Math.Max(0, hearts - 1);

        /// <summary>0 hearts → gentle retry of the CURRENT monster (never a hard game-over).</summary>
        public static bool ShouldRetryMonster(int hearts) => hearts <= 0;

        // ---- Focus Heal ("tandem stand") — drawn only while hearts are below max. ----
        public static bool HealEligible(int hearts, int maxHearts = MaxHearts) => hearts < maxHearts;

        public static int Heal(int hearts, int maxHearts = MaxHearts) => Math.Min(maxHearts, hearts + 1);

        /// <summary>3★ >= 80% average quality, 2★ >= 60%, else 1★ — never 0 (mirrors
        /// BalanceQuestDirector.ComputeStars' "everyone finishes with something to celebrate").</summary>
        public static int Stars(float avgQualityPercent)
        {
            if (avgQualityPercent >= 80f) return 3;
            if (avgQualityPercent >= 60f) return 2;
            return 1;
        }

        /// <summary>
        /// Zeroes out the heal entry's weight in a per-level pool when hearts are already full,
        /// so a full-weight FocusHeal never shows up in the draw. Returns a NEW array — never
        /// mutates the caller's pool weights.
        /// </summary>
        public static float[] ApplyHealGate(float[] baseWeights, int healIndex, int hearts, int maxHearts = MaxHearts)
        {
            if (baseWeights == null) return null;
            var w = (float[])baseWeights.Clone();
            if (healIndex >= 0 && healIndex < w.Length && !HealEligible(hearts, maxHearts))
                w[healIndex] = 0f;
            return w;
        }

        /// <summary>
        /// Weighted random pick that avoids repeating the immediately-previous index whenever the
        /// pool has more than one entry with positive weight. Deterministic for a given Random, so
        /// tests can seed it. Returns -1 if every weight is zero/negative.
        /// </summary>
        public static int DrawWeightedIndex(float[] weights, int previousIndex, Random rng)
        {
            if (weights == null || weights.Length == 0 || rng == null) return -1;

            int distinctPositive = 0;
            for (int i = 0; i < weights.Length; i++) if (weights[i] > 0f) distinctPositive++;
            if (distinctPositive == 0) return -1;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                int idx = WeightedPick(weights, rng);
                if (distinctPositive <= 1 || idx != previousIndex) return idx;
            }
            return WeightedPick(weights, rng);
        }

        /// <summary>
        /// Gates the one-time tutorial card: true only the FIRST time <paramref name="id"/> is seen
        /// this session, and marks it seen as a side effect. HashSet.Add already returns "was this
        /// newly added" — reused directly rather than a separate Contains+Add pair.
        /// </summary>
        public static bool FirstTimeThisSession(HashSet<PoseSkillId> seenSkills, PoseSkillId id) =>
            seenSkills != null && seenSkills.Add(id);

        static int WeightedPick(float[] weights, Random rng)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Math.Max(0f, weights[i]);
            if (total <= 0f) return -1;

            double roll = rng.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += Math.Max(0f, weights[i]);
                if (roll < acc) return i;
            }
            return weights.Length - 1;
        }
    }
}
