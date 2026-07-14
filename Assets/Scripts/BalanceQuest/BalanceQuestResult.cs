using System;
using System.Collections.Generic;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// End-of-quest results. JsonUtility-friendly so the bridge can serialize it straight to
    /// Flutter. Walk and Checkpoint beats are excluded from both <see cref="averagePercent"/>
    /// AND the <see cref="beats"/> list — see BalanceQuestDirector.RunSession for why.
    /// </summary>
    [Serializable]
    public class BalanceQuestResult
    {
        public float averagePercent;   // 0..100, duration-weighted mean of SCORED beats
        public int coins;
        public int stars;              // 1..3, never 0 — see BalanceQuestDirector.ComputeStars
        public float durationSeconds;  // sum of every beat's duration, including Walk/Checkpoint
        public List<BeatResult> beats = new List<BeatResult>();

        [Serializable]
        public class BeatResult
        {
            public string type;
            public float score; // 0..100
        }
    }
}
