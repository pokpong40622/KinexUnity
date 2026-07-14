namespace Kinex.FruitGame
{
    /// <summary>
    /// Pure, testable score/rep bookkeeping for one Fruit Header session. The manager owns one
    /// instance and calls <see cref="Record"/> for every item resolution; no Unity types so the
    /// editor self-test can drive whole-session scenarios without play mode.
    /// </summary>
    public class FruitScoreState
    {
        public int Reps { get; private set; }              // total stands on healthy items (max 45)
        public int RepsInSet { get; private set; }
        public int SetsCompleted { get; private set; }
        public int SmartChoices { get; private set; }
        public int BonusReps { get; private set; }
        public int Combo { get; private set; }
        public int CorrectDecisions { get; private set; }
        public int TotalItems { get; private set; }

        public float AccuracyPercent => FruitGameLogic.AccuracyPercent(CorrectDecisions, TotalItems);
        public int Stars => FruitGameLogic.Stars(SetsCompleted, AccuracyPercent);

        public void BeginSet() => RepsInSet = 0;
        public void CompleteSet() => SetsCompleted++;
        public void AddBonusReps(int n) { if (n > 0) BonusReps += n; }

        /// <summary>Resolve one item and update every counter. Correct decisions extend the
        /// combo; both miss kinds reset it (gently — nothing is subtracted).</summary>
        public FruitGameLogic.Outcome Record(bool healthy, bool stood)
        {
            var outcome = FruitGameLogic.Resolve(healthy, stood);
            TotalItems++;
            if (FruitGameLogic.IsCorrect(outcome)) { CorrectDecisions++; Combo++; }
            else Combo = 0;
            if (outcome == FruitGameLogic.Outcome.HeadedHealthy) { Reps++; RepsInSet++; }
            if (outcome == FruitGameLogic.Outcome.SmartChoice) SmartChoices++;
            return outcome;
        }

        public FruitGameResult ToResult(float durationSeconds) => new FruitGameResult
        {
            reps = Reps,
            setsCompleted = SetsCompleted,
            accuracyPercent = AccuracyPercent,
            smartChoices = SmartChoices,
            bonusReps = BonusReps,
            stars = Stars,
            durationSeconds = durationSeconds,
        };
    }
}
