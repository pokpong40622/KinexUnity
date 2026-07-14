namespace Kinex.BalanceQuest
{
    /// <summary>
    /// The single default route. Durations for Gate/Bridge/Tiptoe/Kick/BackKick/TandemStand/
    /// BeamWalk/RestStop/Checkpoint are the EXPLICIT windows called out in the design brief
    /// (e.g. Kick = 4 targets x 5s = 20s) and are not trimmed. Those alone already sum to ~4.6
    /// minutes, so Walk and Gate are pinned to the LOW end of their design range (Walk 4-6s,
    /// Gate "approaches over 4s") to land as close to the "~5 minute" target as the fixed-window
    /// beats allow — the actual total is documented in <see cref="TotalSeconds"/> and checked
    /// against a tolerant band in BalanceQuestSelfTest, not an exact 300s.
    /// </summary>
    public static class RouteLibrary
    {
        const float WalkSeconds = 4f;
        const float GateSeconds = 4f;         // arch approach only
        const float BridgeSeconds = 20f;      // full hold-accumulation window
        const float TiptoeSeconds = 15f;      // full 3-fruit window
        const float KickSeconds = 20f;        // 4 targets x 5s window
        const float BackKickSeconds = 20f;    // 4 targets x 5s window
        const float TandemStandSeconds = 20f; // 10s hold target inside a 20s window
        const float BeamWalkSeconds = 25f;    // explicit window
        const float RestStopSeconds = 75f;    // 45s chair-stands + 30s seated knee raises
        const float CheckpointSeconds = 2f;

        public static QuestBeat[] DefaultRoute => new[]
        {
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Gate, GateSeconds, lane: -1),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Gate, GateSeconds, lane: 1),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Tiptoe, TiptoeSeconds),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.BackKick, BackKickSeconds),
            new QuestBeat(BeatType.Gate, GateSeconds, lane: 0),
            new QuestBeat(BeatType.Checkpoint, CheckpointSeconds),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Bridge, BridgeSeconds, difficulty: 0),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.BeamWalk, BeamWalkSeconds),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Kick, KickSeconds),
            new QuestBeat(BeatType.Gate, GateSeconds, lane: -1),
            new QuestBeat(BeatType.Checkpoint, CheckpointSeconds),
            new QuestBeat(BeatType.RestStop, RestStopSeconds),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.TandemStand, TandemStandSeconds),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Tiptoe, TiptoeSeconds),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Gate, GateSeconds, lane: 1),
            new QuestBeat(BeatType.Bridge, BridgeSeconds, difficulty: 1),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.BackKick, BackKickSeconds),
            new QuestBeat(BeatType.Walk, WalkSeconds),
            new QuestBeat(BeatType.Kick, KickSeconds),
            new QuestBeat(BeatType.Gate, GateSeconds, lane: 0),
            new QuestBeat(BeatType.Checkpoint, CheckpointSeconds),
        };

        public static float TotalSeconds
        {
            get
            {
                float s = 0f;
                foreach (var b in DefaultRoute) s += b.duration;
                return s;
            }
        }
    }
}
