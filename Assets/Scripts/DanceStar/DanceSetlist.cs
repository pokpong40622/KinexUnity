using System.Collections.Generic;

namespace Kinex.DanceStar
{
    /// <summary>
    /// Static factory for the SUPERSTAR STAGE setlist (design doc: cozy-mapping-puppy.md).
    /// Pose asset names below are a FROZEN CONTRACT — exact strings the (separately-built)
    /// DancePoseData.asset must contain:
    ///   t_arms, knee_up_L/R, leg_side_L/R, leg_back_L/R, tiptoe, heel_stand, tandem_stand,
    ///   sl_tarms_L/R, sl_stack_L/R, sl_cross_L/R, sidestep_L/R, chair_sit, chair_stand,
    ///   seated_knee_L/R.
    ///
    /// Card counts vs the design doc's "~30 cards": the chair verse is implemented as TWO
    /// ChairRep cards (5 chair stands, 6 alternating seated knee raises) rather than 11 discrete
    /// per-rep cards — the doc's own DanceCardType.ChairRep concept implies rep-counted cards,
    /// and 11 one-rep cards would blow well past "~30". Verse 2 lists 7 poses for "8 cards", so a
    /// second (longer) tandem hold was added to round it out. Total: 28 cards
    /// (2+6+6+2+8+4) — see DanceStarSelfTest for the exact breakdown assertion.
    /// </summary>
    public static class DanceSetlist
    {
        public static List<DanceCard> BuildSetlist()
        {
            var cards = new List<DanceCard>();

            // ---- Warm-up (2): arms-out T hold, tutorial pacing ----
            cards.Add(new DanceCard("t_arms", "ยืนกางแขนตรง", DanceSection.Warmup,
                DanceCardType.Hold, DanceDetectorKind.ArmsOutHold, DanceSide.Both, windowSeconds: 6f));
            cards.Add(new DanceCard("t_arms", "ยืนกางแขนตรง ค้างไว้", DanceSection.Warmup,
                DanceCardType.Hold, DanceDetectorKind.ArmsOutHold, DanceSide.Both, windowSeconds: 8f));

            // ---- Verse 1 (6): knee raise L/R, hip abduction L/R, single-leg T L/R ----
            cards.Add(new DanceCard("knee_up_L", "ยกเข่าซ้าย", DanceSection.Verse1,
                DanceCardType.Hold, DanceDetectorKind.KneeRaise, DanceSide.Left, windowSeconds: 6f));
            cards.Add(new DanceCard("knee_up_R", "ยกเข่าขวา", DanceSection.Verse1,
                DanceCardType.Hold, DanceDetectorKind.KneeRaise, DanceSide.Right, windowSeconds: 6f));
            cards.Add(new DanceCard("leg_side_L", "กางขาซ้ายออกข้าง", DanceSection.Verse1,
                DanceCardType.Hold, DanceDetectorKind.HipAbduction, DanceSide.Left, windowSeconds: 6f));
            cards.Add(new DanceCard("leg_side_R", "กางขาขวาออกข้าง", DanceSection.Verse1,
                DanceCardType.Hold, DanceDetectorKind.HipAbduction, DanceSide.Right, windowSeconds: 6f));
            cards.Add(new DanceCard("sl_tarms_L", "ยืนขาซ้าย กางแขน", DanceSection.Verse1,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Left, DanceArmVariant.TArms, windowSeconds: 8f));
            cards.Add(new DanceCard("sl_tarms_R", "ยืนขาขวา กางแขน", DanceSection.Verse1,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Right, DanceArmVariant.TArms, windowSeconds: 8f));

            // ---- Chorus 1 (6): side-step L/R x2 to beat, tiptoe hold, heel-stand hold ----
            cards.Add(new DanceCard("sidestep_L", "ก้าวเท้าไปทางซ้าย", DanceSection.Chorus1,
                DanceCardType.Beat, DanceDetectorKind.SideStep, DanceSide.Left, windowSeconds: 4f));
            cards.Add(new DanceCard("sidestep_R", "ก้าวเท้าไปทางขวา", DanceSection.Chorus1,
                DanceCardType.Beat, DanceDetectorKind.SideStep, DanceSide.Right, windowSeconds: 4f));
            cards.Add(new DanceCard("sidestep_L", "ก้าวเท้าไปทางซ้าย", DanceSection.Chorus1,
                DanceCardType.Beat, DanceDetectorKind.SideStep, DanceSide.Left, windowSeconds: 4f));
            cards.Add(new DanceCard("sidestep_R", "ก้าวเท้าไปทางขวา", DanceSection.Chorus1,
                DanceCardType.Beat, DanceDetectorKind.SideStep, DanceSide.Right, windowSeconds: 4f));
            cards.Add(new DanceCard("tiptoe", "ยืนเขย่งปลายเท้า", DanceSection.Chorus1,
                DanceCardType.Hold, DanceDetectorKind.Tiptoe, DanceSide.Both, windowSeconds: 6f));
            cards.Add(new DanceCard("heel_stand", "ยืนยกปลายเท้าขึ้น", DanceSection.Chorus1,
                DanceCardType.Hold, DanceDetectorKind.HeelStand, DanceSide.Both, windowSeconds: 6f));

            // ---- Chair verse (2, bridge, calm): chair stand x5, seated knee raise x6 alternating ----
            cards.Add(new DanceCard("chair_stand", "ลุกยืนจากเก้าอี้", DanceSection.ChairVerse,
                DanceCardType.ChairRep, DanceDetectorKind.ChairStand, DanceSide.Both,
                windowSeconds: 45f, targetReps: 5));
            cards.Add(new DanceCard("seated_knee_L", "นั่งยกเข่าสลับข้าง", DanceSection.ChairVerse,
                DanceCardType.ChairRep, DanceDetectorKind.SeatedKneeRaise, DanceSide.Both,
                windowSeconds: 45f, targetReps: 6));

            // ---- Verse 2 (8): stacked L/R, crossed L/R, hip extension L/R, tandem hold x2 ----
            cards.Add(new DanceCard("sl_stack_L", "ยืนขาซ้าย มือซ้อนกัน", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Left, DanceArmVariant.Stacked, windowSeconds: 8f));
            cards.Add(new DanceCard("sl_stack_R", "ยืนขาขวา มือซ้อนกัน", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Right, DanceArmVariant.Stacked, windowSeconds: 8f));
            cards.Add(new DanceCard("sl_cross_L", "ยืนขาซ้าย กอดอก", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Left, DanceArmVariant.Crossed, windowSeconds: 8f));
            cards.Add(new DanceCard("sl_cross_R", "ยืนขาขวา กอดอก", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Right, DanceArmVariant.Crossed, windowSeconds: 8f));
            cards.Add(new DanceCard("leg_back_L", "เหยียดขาซ้ายไปข้างหลัง", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.HipExtension, DanceSide.Left, windowSeconds: 6f));
            cards.Add(new DanceCard("leg_back_R", "เหยียดขาขวาไปข้างหลัง", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.HipExtension, DanceSide.Right, windowSeconds: 6f));
            cards.Add(new DanceCard("tandem_stand", "ยืนเท้าต่อเท้า", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.TandemStand, DanceSide.Both, windowSeconds: 8f));
            cards.Add(new DanceCard("tandem_stand", "ยืนเท้าต่อเท้า ค้างไว้", DanceSection.Verse2,
                DanceCardType.Hold, DanceDetectorKind.TandemStand, DanceSide.Both, windowSeconds: 10f));

            // ---- Finale (4): side-step combo + big single-leg T hold ----
            cards.Add(new DanceCard("sidestep_L", "ก้าวเท้าไปทางซ้าย", DanceSection.Finale,
                DanceCardType.Beat, DanceDetectorKind.SideStep, DanceSide.Left, windowSeconds: 4f));
            cards.Add(new DanceCard("sidestep_R", "ก้าวเท้าไปทางขวา", DanceSection.Finale,
                DanceCardType.Beat, DanceDetectorKind.SideStep, DanceSide.Right, windowSeconds: 4f));
            cards.Add(new DanceCard("sl_tarms_L", "ยืนขาซ้าย กางแขน สุดท้าย!", DanceSection.Finale,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Left, DanceArmVariant.TArms, windowSeconds: 10f));
            cards.Add(new DanceCard("sl_tarms_R", "ยืนขาขวา กางแขน สุดท้าย!", DanceSection.Finale,
                DanceCardType.Hold, DanceDetectorKind.SingleLeg, DanceSide.Right, DanceArmVariant.TArms, windowSeconds: 10f));

            return cards;
        }

        /// <summary>All 22 frozen pose names, for integrity checks / lookups.</summary>
        public static readonly string[] FrozenPoseNames =
        {
            "t_arms",
            "knee_up_L", "knee_up_R",
            "leg_side_L", "leg_side_R",
            "leg_back_L", "leg_back_R",
            "tiptoe", "heel_stand", "tandem_stand",
            "sl_tarms_L", "sl_tarms_R",
            "sl_stack_L", "sl_stack_R",
            "sl_cross_L", "sl_cross_R",
            "sidestep_L", "sidestep_R",
            "chair_sit", "chair_stand",
            "seated_knee_L", "seated_knee_R",
        };
    }
}
