using System;

namespace Kinex.MirrorGame
{
    /// <summary>End-of-session results payload for the Magic Mirror game. JsonUtility-friendly,
    /// same shape convention as Kinex.BattleGame.BattleResult / Kinex.FruitGame.FruitGameResult
    /// so the Flutter host parses them all the same way.</summary>
    [Serializable]
    public class MirrorResult
    {
        public int posesCompleted;   // how many target poses the player cleared
        public int poseCount;        // total poses in the session (the whole ladder)
        public int totalScore;       // sum of per-pose scores (ZoneMatcher.PoseScore)
        public int coins;            // shop economy — same magnitude as the battle game
        public int stars;            // 1..3, never 0
        public float durationSeconds;
    }
}
