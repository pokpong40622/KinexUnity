using System;
using System.Collections.Generic;

namespace Kinex.World
{
    /// <summary>
    /// End-of-class results. Headline metric is <see cref="averagePercent"/> (the
    /// "Average Performance Percentage" required by the design). JsonUtility-friendly so
    /// the bridge can serialize it straight to Flutter.
    /// </summary>
    [Serializable]
    public class WorldSessionResult
    {
        public string routineId;
        public float averagePercent;     // 0..100, mean across segments
        public float durationSeconds;
        public List<ExerciseScore> exercises = new List<ExerciseScore>();

        [Serializable]
        public class ExerciseScore
        {
            public string id;
            public string name;
            public float score;          // 0..100, this exercise's average
        }
    }
}
