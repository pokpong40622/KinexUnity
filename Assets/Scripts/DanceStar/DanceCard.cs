using System;

namespace Kinex.DanceStar
{
    /// <summary>How a card is judged. Hold = reach + hold a static pose. Beat = brief,
    /// beat-timed pose (side-step). ChairRep = count reps toward a target instead of holding.</summary>
    public enum DanceCardType { Hold, Beat, ChairRep }

    /// <summary>Which Motion detector (or DanceStar-local helper) judges this card. See
    /// DanceStarDirector's detector facade for the mapping to concrete detector instances.</summary>
    public enum DanceDetectorKind
    {
        None,
        ArmsOutHold,      // warm-up T-arms, two-footed (TempleLogic.ArmsOutOk)
        KneeRaise,        // standing march L/R (KneeRaiseDetector.LeftUp/RightUp)
        HipAbduction,     // leg-out side L/R (LegAbductionDetector)
        HipExtension,     // leg-back L/R (HipExtensionDetector, Landmarks33)
        Tiptoe,           // TiptoeDetector.IsRaised
        HeelStand,        // also TiptoeDetector — see design doc's detector table; no dedicated
                           // heel/ankle-invert detector exists in Assets/Scripts/Motion, so this
                           // reuses the same rise signal as Tiptoe (documented deviation).
        SingleLeg,        // SingleLegStanceDetector + ArmPoseSignatures arm-variant check
        TandemStand,      // stillness + ankle-proximity (DanceTandemLogic)
        SideStep,         // LaneDetector
        ChairStand,       // SitStandDetector.StandCount, rep-counted (ChairRep card)
        SeatedKneeRaise,  // KneeRaiseDetector.AlternatingCount, rep-counted (ChairRep card)
    }

    /// <summary>Which leg/side a card targets. Both = no side gating (two-footed / either leg).</summary>
    public enum DanceSide { None, Left, Right, Both }

    /// <summary>Arm shape required alongside a SingleLeg card, scored via ArmPoseSignatures +
    /// PoseScorer.Score (same pattern as BalanceQuest's BridgeBeatRunner).</summary>
    public enum DanceArmVariant { None, TArms, Stacked, Crossed }

    /// <summary>Setlist section, used for section-change events, the song progress bar, and the
    /// chair-verse safety intro.</summary>
    public enum DanceSection { Warmup, Verse1, Chorus1, ChairVerse, Verse2, Finale }

    /// <summary>
    /// One Just-Dance-style pictogram card: which trainer pose to show, how it's judged, and how
    /// long the player has. Plain data — no UnityEngine object refs beyond the enums above — so
    /// DanceSetlist and DanceScoring can be unit-tested without a scene.
    /// </summary>
    [Serializable]
    public class DanceCard
    {
        /// <summary>Pose name looked up BY NAME in DancePoseData.asset at runtime — see the
        /// frozen pose-name contract in DanceSetlist.</summary>
        public string poseAssetName;
        public string displayNameThai;
        public DanceSection section;
        public DanceCardType cardType;
        public DanceDetectorKind detector;
        public DanceSide side;
        public DanceArmVariant armVariant;

        /// <summary>Match window for Hold/Beat cards (seconds). Also used as a safety cap for
        /// ChairRep cards (reps are the real target).</summary>
        public float windowSeconds;

        /// <summary>Rep target for ChairRep cards. Unused (0) for Hold/Beat.</summary>
        public int targetReps;

        public DanceCard(string poseAssetName, string displayNameThai, DanceSection section,
            DanceCardType cardType, DanceDetectorKind detector,
            DanceSide side = DanceSide.None, DanceArmVariant armVariant = DanceArmVariant.None,
            float windowSeconds = 8f, int targetReps = 0)
        {
            this.poseAssetName = poseAssetName;
            this.displayNameThai = displayNameThai;
            this.section = section;
            this.cardType = cardType;
            this.detector = detector;
            this.side = side;
            this.armVariant = armVariant;
            this.windowSeconds = windowSeconds;
            this.targetReps = targetReps;
        }
    }
}
