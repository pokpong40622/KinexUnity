using System;

namespace Kinex.DanceStar
{
    /// <summary>End-of-session results payload for SUPERSTAR STAGE. JsonUtility-friendly, same
    /// shape convention as Kinex.MirrorGame.MirrorResult / Kinex.TempleHunt.TempleHuntResult so
    /// the Flutter host parses every game's result the same way.</summary>
    [Serializable]
    public class DanceStarResult
    {
        public int cardsCompleted;   // cards rated better than MISS
        public int cardCount;        // total cards in the setlist
        public int totalScore;       // sum of per-card CardScore (rating * streak multiplier)
        public int maxStreak;
        public int accuracyPct;      // 0..100
        public int stars;            // 1..3, never 0
        public int coins;
        public float durationSeconds;
    }
}
