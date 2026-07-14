using System;

namespace Kinex.FruitGame
{
    /// <summary>
    /// End-of-game results payload. JsonUtility-friendly so FruitGameResultBridge can serialize
    /// it straight to Flutter (same pattern as Kinex.World.WorldSessionResult).
    /// </summary>
    [Serializable]
    public class FruitGameResult
    {
        public int reps;               // total stands on healthy items (max 45)
        public int setsCompleted;      // 0..3
        public float accuracyPercent;  // correctDecisions / totalItems × 100
        public int smartChoices;       // junk items sat through
        public int bonusReps;          // knee raises across the bonus rounds
        public int stars;              // 1..3
        public float durationSeconds;
    }
}
