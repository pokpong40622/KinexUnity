using System;

namespace Kinex.TempleHunt
{
    /// <summary>Final session summary — serialized by JsonUtility and relayed to Flutter by
    /// <see cref="TempleResultBridge"/> (same shape philosophy as MirrorResult/FruitGameResult).</summary>
    [Serializable]
    public class TempleHuntResult
    {
        public int chambersCompleted;
        public int legLifts;
        public int stands;
        public float balanceHoldSeconds;
        public float durationSeconds;
        public int coins;
        public int stars;
    }
}
