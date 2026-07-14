using System;

namespace Kinex.BattleGame
{
    /// <summary>End-of-level results payload. JsonUtility-friendly, same shape convention as
    /// Kinex.FruitGame.FruitGameResult / Kinex.BalanceQuest.BalanceQuestResult.</summary>
    [Serializable]
    public class BattleResult
    {
        public int level;              // 1..3
        public int monstersDefeated;   // 0..3 (2 regular + boss)
        public int coins;
        public int totalDamageDealt;
        public int bestCombo;
        public float avgQualityPercent;
        public int stars;              // 1..3, never 0
        public float durationSeconds;
    }
}
