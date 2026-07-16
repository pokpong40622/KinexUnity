namespace Kinex.TheDasher
{
    /// <summary>
    /// Run difficulty for TheDasher. Chosen on the intro screen (TheDasherDirector.SetDifficulty),
    /// applied once to the DasherSpawner's fall speed / beat spacing / active windows when the run
    /// actually begins (director's EnterPlay → ApplyDifficulty). Normal reproduces DasherSpawner's own
    /// Inspector defaults unchanged; Easy/Hard scale them more/less forgiving.
    /// </summary>
    public enum DasherDifficulty { Easy, Normal, Hard }
}
