namespace Kinex.FruitGame
{
    /// <summary>
    /// Pure decision logic for the Fruit Header game — no Unity types, no scene state, so the
    /// editor self-test can exercise it directly. The manager funnels every item resolution
    /// through <see cref="Resolve"/> and the results screen through <see cref="Stars"/>.
    ///
    /// Decision matrix (senior-friendly: no fail state, misses are gentle):
    ///   healthy + stood   → HeadedHealthy (rep + combo + confetti)
    ///   healthy + seated  → SoftMiss      (item floats away, combo reset, encouragement)
    ///   junk    + stood   → GentleMiss    (combo reset, warm coach line, no penalty)
    ///   junk    + seated  → SmartChoice   (sparkle + small bonus)
    /// </summary>
    public static class FruitGameLogic
    {
        public enum Outcome { HeadedHealthy, SmartChoice, SoftMiss, GentleMiss }

        public static Outcome Resolve(bool healthy, bool stood) =>
            healthy ? (stood ? Outcome.HeadedHealthy : Outcome.SoftMiss)
                    : (stood ? Outcome.GentleMiss : Outcome.SmartChoice);

        /// <summary>Correct decision = headed a healthy item OR sat through a junk item.</summary>
        public static bool IsCorrect(Outcome o) =>
            o == Outcome.HeadedHealthy || o == Outcome.SmartChoice;

        /// <summary>3★ = all 3 sets AND accuracy ≥ 80; 2★ = ≥2 sets OR accuracy ≥ 60; else 1★.</summary>
        public static int Stars(int setsCompleted, float accuracyPercent)
        {
            if (setsCompleted >= 3 && accuracyPercent >= 80f) return 3;
            if (setsCompleted >= 2 || accuracyPercent >= 60f) return 2;
            return 1;
        }

        /// <summary>correctDecisions / totalItems × 100. 0 items → 0 (no decisions were made).</summary>
        public static float AccuracyPercent(int correct, int total) =>
            total <= 0 ? 0f : 100f * correct / total;
    }
}
