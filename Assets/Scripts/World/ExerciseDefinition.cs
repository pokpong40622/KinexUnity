using UnityEngine;

namespace Kinex.World
{
    /// <summary>
    /// One exercise from the Kinex fitness booklet (the PDF source of truth).
    /// Pure data: a looping trainer animation + how long the class spends on it.
    /// Reps/holds are expressed as animation + duration, NOT as separate scoring logic
    /// (the director scores one way for every category — see KinexWorldDirector).
    /// </summary>
    [CreateAssetMenu(fileName = "Exercise", menuName = "Kinex/World/Exercise Definition")]
    public class ExerciseDefinition : ScriptableObject
    {
        [Tooltip("Stable id used in results JSON sent to Flutter. e.g. \"march\".")]
        public string id;

        public string thaiName;
        public string englishName;

        /// <summary>Name to show the user — Thai first, else English, else the id.</summary>
        public string DisplayName =>
            !string.IsNullOrEmpty(thaiName) ? thaiName
            : (!string.IsNullOrEmpty(englishName) ? englishName : id);

        public ExerciseCategory category;

        [Tooltip("Looping trainer animation clip for this exercise. May be null in early phases.")]
        public AnimationClip clip;

        [Min(3f)]
        [Tooltip("Seconds the class spends on this exercise before moving on (it never waits for the player).")]
        public float durationSeconds = 30f;

        [TextArea]
        [Tooltip("Short instruction shown on the overview / first-pose card.")]
        public string instruction;

        [Tooltip("Optional preview thumbnail (captured by the pose-preview editor tool).")]
        public Sprite preview;

        [Tooltip("Legs must be in the camera frame (cardio / balance). Drives the camera-setup hint.")]
        public bool legsRequired;
    }
}
