using UnityEngine;

namespace Kinex.DanceStar
{
    /// <summary>
    /// PURE scoring math for SUPERSTAR STAGE — no UnityEngine object refs, no MonoBehaviour,
    /// unit-testable like Kinex.MirrorGame.ZoneMatcher / Kinex.BalanceQuest.BeatScoring. The
    /// director calls these exact functions during gameplay AND DanceStarSelfTest calls them
    /// directly with known inputs, so the test exercises the real formula.
    /// </summary>
    public static class DanceScoring
    {
        public enum Rating { Miss, Ok, Good, Perfect }

        public const int ScoreMiss = 0;
        public const int ScoreOk = 40;
        public const int ScoreGood = 70;
        public const int ScorePerfect = 100;

        public static int ScoreOf(Rating r) => r switch
        {
            Rating.Perfect => ScorePerfect,
            Rating.Good => ScoreGood,
            Rating.Ok => ScoreOk,
            _ => ScoreMiss,
        };

        /// <summary>Ratings are NEVER harsh — senior-first copy per the design doc.</summary>
        public static string ThaiLabel(Rating r) => r switch
        {
            Rating.Perfect => "เป๊ะเวอร์!",
            Rating.Good => "ดีมาก!",
            Rating.Ok => "พอใช้",
            _ => "พลาดจ้า",
        };

        /// <summary>
        /// Rate a Hold/Beat card. matched = the pose condition was held long enough to count
        /// within the window. timeToMatchSeconds = how long it took to FIRST reach the pose
        /// (0 = instant). holdStability01 = fraction of the window the pose stayed matched once
        /// found (0 = flickered in/out constantly, 1 = rock solid). Speed and stability are
        /// weighted evenly; a fast-but-wobbly or slow-but-steady hold both land on GOOD, only a
        /// fast AND steady hold reaches PERFECT.
        /// </summary>
        public static Rating RateCard(bool matched, float timeToMatchSeconds, float windowSeconds, float holdStability01)
        {
            if (!matched) return Rating.Miss;
            float speed01 = 1f - Mathf.Clamp01(timeToMatchSeconds / Mathf.Max(windowSeconds, 0.01f));
            float combined = 0.5f * speed01 + 0.5f * Mathf.Clamp01(holdStability01);
            if (combined >= 0.75f) return Rating.Perfect;
            if (combined >= 0.40f) return Rating.Good;
            return Rating.Ok;
        }

        /// <summary>
        /// Rate a ChairRep card (chair stands / seated knee raises): judged on reps achieved
        /// vs target, with pace as a tiebreaker for the top rating. Below 1/3 of the target
        /// reps counts as a MISS — never harsher than that (no-fail philosophy).
        /// </summary>
        public static Rating RateChairRep(int repsAchieved, int targetReps, float elapsedSeconds, float windowSeconds)
        {
            if (targetReps <= 0) return Rating.Miss;
            float frac01 = Mathf.Clamp01(repsAchieved / (float)targetReps);
            if (frac01 < 1f / 3f) return Rating.Miss;
            if (frac01 < 1f) return Rating.Ok;
            float pace01 = 1f - Mathf.Clamp01(elapsedSeconds / Mathf.Max(windowSeconds, 0.01f));
            return pace01 > 0.4f ? Rating.Perfect : Rating.Good;
        }

        // ---- Streak multiplier: x1 -> x1.2 (3) -> x1.5 (5) -> x2 (10) ----
        public static float StreakMultiplier(int streak)
        {
            if (streak >= 10) return 2.0f;
            if (streak >= 5) return 1.5f;
            if (streak >= 3) return 1.2f;
            return 1.0f;
        }

        /// <summary>Final per-card score = rating score * streak multiplier (streak BEFORE this card).</summary>
        public static int CardScore(Rating rating, int streakBeforeCard) =>
            Mathf.RoundToInt(ScoreOf(rating) * StreakMultiplier(streakBeforeCard));

        // ---- Hearts: 5 star-hearts. MISS -1, PERFECT +1 (cap 5). Never a fail state. ----
        public const int MaxHearts = 5;
        public static int ApplyRatingToHearts(int hearts, Rating rating)
        {
            if (rating == Rating.Miss) return Mathf.Max(0, hearts - 1);
            if (rating == Rating.Perfect) return Mathf.Min(MaxHearts, hearts + 1);
            return hearts;
        }

        /// <summary>
        /// Session stars from average per-card % of max possible (100 * multiplier at the time,
        /// but callers should pass the SIMPLE average against ScorePerfect for a stable target).
        /// 3 stars >=85%, 2 stars >=60%, else 1. Hitting zero hearts at any point never fails the
        /// session, but caps the result at 2 stars (design doc: rehab, no fail state).
        /// </summary>
        public static int Stars(int totalScore, int cardCount, bool heartsHitZero)
        {
            if (cardCount <= 0) return 1;
            float avg01 = totalScore / (float)(cardCount * ScorePerfect);
            int stars = avg01 >= 0.85f ? 3 : (avg01 >= 0.60f ? 2 : 1);
            return heartsHitZero ? Mathf.Min(stars, 2) : stars;
        }

        /// <summary>Coins for the shop economy — same order of magnitude as the other games.</summary>
        public static int Coins(int totalScore, int stars) => totalScore / 20 + stars * 10;

        /// <summary>Accuracy % = cards that weren't MISS, over total cards.</summary>
        public static int AccuracyPct(int cardsCompleted, int cardCount) =>
            cardCount > 0 ? Mathf.RoundToInt(100f * cardsCompleted / cardCount) : 0;
    }
}
