using System;

namespace Kinex.BalanceQuest
{
    /// <summary>Every kind of moment the route can throw at the player.</summary>
    public enum BeatType
    {
        Walk,
        Gate,
        Bridge,
        Tiptoe,
        Kick,
        Checkpoint,
        BackKick,
        TandemStand,
        BeamWalk,
        RestStop,
    }

    /// <summary>
    /// One entry in the route. Pure data — <see cref="lane"/> is only meaningful for Gate
    /// (screen-space target lane, -1/0/1) and <see cref="difficulty"/> only for Bridge
    /// (0 = arms out, 1 = hands stacked, 2 = arms crossed). Every other beat ignores both.
    /// </summary>
    [Serializable]
    public class QuestBeat
    {
        public BeatType type;
        public float duration;
        public int lane;
        public int difficulty;

        public QuestBeat(BeatType type, float duration, int lane = 0, int difficulty = 0)
        {
            this.type = type;
            this.duration = duration;
            this.lane = lane;
            this.difficulty = difficulty;
        }
    }
}
