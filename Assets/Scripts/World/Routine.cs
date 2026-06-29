using UnityEngine;

namespace Kinex.World
{
    /// <summary>
    /// An ordered playlist of exercises = one instructor-led class. The shipped slice is
    /// "Daily Class" (warm-up stretch → cardio → strength → balance → cool-down stretch).
    /// More classes / exercises are added as assets, no code change.
    /// </summary>
    [CreateAssetMenu(fileName = "Routine", menuName = "Kinex/World/Routine")]
    public class Routine : ScriptableObject
    {
        [Tooltip("Stable id used in results JSON sent to Flutter. e.g. \"daily_class\".")]
        public string id;

        public string thaiName;
        public string englishName;

        /// <summary>Name to show the user — Thai first, else English, else the id.</summary>
        public string DisplayName =>
            !string.IsNullOrEmpty(thaiName) ? thaiName
            : (!string.IsNullOrEmpty(englishName) ? englishName : id);

        [TextArea]
        public string description;

        public ExerciseDefinition[] exercises;

        /// <summary>Total class length in seconds (sum of every segment).</summary>
        public float TotalSeconds
        {
            get
            {
                float total = 0f;
                if (exercises != null)
                {
                    foreach (var ex in exercises)
                        if (ex != null) total += ex.durationSeconds;
                }
                return total;
            }
        }

        public int ExerciseCount => exercises != null ? exercises.Length : 0;
    }
}
