namespace Kinex.Motion
{
    /// <summary>
    /// Tracks whether a usable pose is currently on screen, independent of any single detector.
    /// Games poll <see cref="ShouldPauseGame"/> to freeze/dim gameplay when the player has
    /// stepped out of frame instead of letting every detector guess independently.
    /// </summary>
    public class PoseGate
    {
        const float MinConf = 0.3f;
        const float PauseAfterSeconds = 2f;

        public bool Visible { get; private set; }
        public float LostSeconds { get; private set; }
        public bool ShouldPauseGame => LostSeconds > PauseAfterSeconds;

        /// <summary>Visible = hasPose AND (both hips confident OR both shoulders confident).</summary>
        public void Tick(bool hasPose, float[] conf, float dt)
        {
            bool visible = hasPose &&
                           (MotionMath.Valid(conf, MinConf, MotionMath.LHip, MotionMath.RHip) ||
                            MotionMath.Valid(conf, MinConf, MotionMath.LShoulder, MotionMath.RShoulder));
            Visible = visible;
            LostSeconds = visible ? 0f : LostSeconds + dt;
        }
    }
}
