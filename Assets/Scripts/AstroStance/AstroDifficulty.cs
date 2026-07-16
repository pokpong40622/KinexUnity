namespace Kinex.AstroStance
{
    /// <summary>
    /// Run difficulty for AstroStance. Chosen on the intro screen (AstroStanceDirector.SetDifficulty),
    /// applied once to the AstroSpawner's fall speed / beat spacing / active windows when the run
    /// actually begins (director's EnterPlay → ApplyDifficulty). Normal reproduces AstroSpawner's own
    /// Inspector defaults unchanged; Easy/Hard scale them more/less forgiving.
    /// </summary>
    public enum AstroDifficulty { Easy, Normal, Hard }
}
