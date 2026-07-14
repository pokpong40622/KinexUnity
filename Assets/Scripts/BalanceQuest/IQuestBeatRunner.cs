using UnityEngine;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Contract every non-trivial beat (Gate/Bridge/Tiptoe/Kick/BackKick/TandemStand/BeamWalk/
    /// RestStop) implements. Walk and Checkpoint are simple enough that BalanceQuestDirector
    /// runs them inline instead. Beats never early-exit in this design (every beat runs its full
    /// fixed duration — "never hard-blocks"), so <see cref="Done"/> is always false in every
    /// current runner; it's kept in the contract for a future beat that legitimately wants to
    /// finish early. <see cref="Score01"/> should be a continuously-updated running estimate:
    /// the director reads it once, after the fixed duration elapses.
    /// </summary>
    public interface IQuestBeatRunner
    {
        void Begin(QuestContext ctx, QuestBeat beat);
        void Tick(float dt);
        bool Done { get; }
        float Score01 { get; }
    }

    /// <summary>
    /// Everything a beat runner needs, built fresh each session by BalanceQuestDirector. Wraps
    /// BOTH the real Motion detectors and the KeyboardMotionStub behind ONE set of properties so
    /// runners never branch on <see cref="useKeyboardStub"/> themselves for the shared detectors
    /// (a few beats — RestStop, BeamWalk — still own a private detector instance directly,
    /// documented on those runners).
    /// </summary>
    public class QuestContext
    {
        public bool useKeyboardStub;

        public Kinex.Motion.LaneDetector laneDetector;
        public Kinex.Motion.SingleLegStanceDetector stanceDetector;
        public Kinex.Motion.TiptoeDetector tiptoeDetector;
        public Kinex.Motion.LegAbductionDetector abductionDetector;
        public Kinex.Motion.HipExtensionDetector hipExtensionDetector;
        public Kinex.Motion.PoseGate poseGate;
        public Kinex.Motion.KeyboardMotionStub stub;
        public MediaPipePoseDetector poseDetector;

        public TrailScroller trail;
        public QuestPropFactory props;
        public AvatarLaneMover avatarMover;
        public MonoBehaviour host;

        /// <summary>(pictogram glyph, Thai instruction) -> beat-cue card.</summary>
        public System.Action<string, string> setCue;
        public WobbleGaugeUI wobbleGauge;
        /// <summary>Safe no-op if the voice engine isn't ready — see VoiceCoach.</summary>
        public System.Action<string> voiceLine;
        public System.Action addCoin;

        public int Lane => useKeyboardStub ? stub.Lane : laneDetector.Lane;
        public bool JustChangedLane => useKeyboardStub ? stub.JustChangedLane : laneDetector.JustChangedLane;

        public bool IsHolding => useKeyboardStub ? stub.IsHolding : stanceDetector.IsHolding;
        public float HoldSeconds => useKeyboardStub ? stub.HoldSeconds : stanceDetector.HoldSeconds;
        public float Wobble01 => useKeyboardStub ? stub.Wobble01 : stanceDetector.Wobble01;

        public bool IsRaised => useKeyboardStub ? stub.IsRaised : tiptoeDetector.IsRaised;
        public bool JustRaised => useKeyboardStub ? stub.JustRaised : tiptoeDetector.JustRaised;

        public bool JustKickedLeft => useKeyboardStub ? stub.JustKickedLeft : abductionDetector.JustKickedLeft;
        public bool JustKickedRight => useKeyboardStub ? stub.JustKickedRight : abductionDetector.JustKickedRight;

        public bool JustKickedBackLeft => useKeyboardStub ? stub.JustKickedBackLeft : hipExtensionDetector.JustKickedBackLeft;
        public bool JustKickedBackRight => useKeyboardStub ? stub.JustKickedBackRight : hipExtensionDetector.JustKickedBackRight;

        public bool IsTandemHolding => useKeyboardStub && stub.IsTandemHolding;
        public float TandemHoldSeconds => useKeyboardStub ? stub.TandemHoldSeconds : 0f;

        /// <summary>Only meaningful in stub mode — real-mode RestStop/BeamWalk own a private
        /// SitStandDetector/KneeRaiseDetector instance instead of a shared one.</summary>
        public int StandCount => useKeyboardStub ? stub.StandCount : 0;
        public int AlternatingCount => useKeyboardStub ? stub.AlternatingCount : 0;

        public bool PoseVisible => useKeyboardStub || (poseGate != null && poseGate.Visible);
        public bool ShouldPauseGame => !useKeyboardStub && poseGate != null && poseGate.ShouldPauseGame;

        public Vector2[] Keypoints => useKeyboardStub ? null : (poseDetector != null ? poseDetector.LatestKeypoints : null);
        public float[] Confidence => useKeyboardStub ? null : (poseDetector != null ? poseDetector.LatestConfidence : null);
        public bool HasPose => useKeyboardStub || (poseDetector != null && poseDetector.HasPose);
        public MediaPipePoseDetector.NormLandmark[] Landmarks33 =>
            useKeyboardStub ? null : (poseDetector != null ? poseDetector.Landmarks33 : null);

        /// <summary>
        /// MIRRORING DECISION: LaneDetector.Lane is in RAW camera-image space (+1 = hip moved
        /// toward the raw frame's right edge). A front camera not yet mirrored for display shows
        /// the player as if in a video call, not a mirror — stepping to the player's OWN right
        /// moves their hip toward the frame's LEFT (subject and camera face each other). So
        /// Lane==+1 means the player stepped to THEIR OWN LEFT. We want the avatar (and every
        /// lane-based visual: gates, coins) to move screen-LEFT when that happens, matching a
        /// real mirror, so ScreenLane flips the sign. Every runner/prop reads lanes through here
        /// — if live testing on device shows this is backwards, flip this one line.
        /// </summary>
        public int ScreenLane(int detectorLane) => -detectorLane;

        /// <summary>Stub lanes are already screen-space (left arrow = -1 = screen left), so the
        /// mirror flip only applies to the real camera detector.</summary>
        public int CurrentScreenLane => useKeyboardStub ? Lane : ScreenLane(Lane);
    }
}
