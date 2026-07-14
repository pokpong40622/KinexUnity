namespace Kinex.BattleGame
{
    /// <summary>Every pose card the player can draw on their turn. Shield/Dodge are NOT here —
    /// those are the two enemy-turn defend actions, chosen by the director, never drawn as a card.</summary>
    public enum PoseSkillId { EarthSlam, StompQuake, FireKick, TailWhip, LightningCharge, FocusHeal }

    /// <summary>How a skill's raw detector signal turns into a 0..1 quality value.</summary>
    public enum SkillMetric
    {
        /// <summary>One binary event (e.g. chair stand): quality = how much of the window was
        /// left when it fired (fast reaction = high quality); never happening = the floor.</summary>
        EventSpeed,
        /// <summary>Achieved-count / target-count (e.g. 3 knee raises, 2 kicks).</summary>
        CountRatio,
        /// <summary>Held-seconds / target-seconds (e.g. tiptoe charge, tandem-stand heal).</summary>
        HoldRatio,
    }

    /// <summary>Static per-skill tuning: damage, target metric, Thai name + TTS line. Plain data —
    /// no Unity types — so BattleGameSelfTest can assert every entry directly.</summary>
    public readonly struct PoseSkillDef
    {
        public readonly PoseSkillId id;
        public readonly string nameThai;
        public readonly string announceLine;
        public readonly float baseDamage;
        public readonly SkillMetric metric;
        public readonly float targetValue; // count target OR hold-seconds target (unused for EventSpeed)

        public PoseSkillDef(PoseSkillId id, string nameThai, string announceLine, float baseDamage,
                             SkillMetric metric, float targetValue)
        {
            this.id = id;
            this.nameThai = nameThai;
            this.announceLine = announceLine;
            this.baseDamage = baseDamage;
            this.metric = metric;
            this.targetValue = targetValue;
        }
    }

    public static class PoseSkillLibrary
    {
        public const float PlayerTurnWindowSeconds = 12f;

        public static readonly PoseSkillDef EarthSlam = new PoseSkillDef(
            PoseSkillId.EarthSlam, "พลังธรณีถล่ม", "ลุกขึ้นยืนแรงๆ!", 34f, SkillMetric.EventSpeed, 0f);

        public static readonly PoseSkillDef StompQuake = new PoseSkillDef(
            PoseSkillId.StompQuake, "แผ่นดินไหวกระทืบ", "ยกเข่าสลับซ้ายขวา 3 ครั้ง!", 12f, SkillMetric.CountRatio, 3f);

        public static readonly PoseSkillDef FireKick = new PoseSkillDef(
            PoseSkillId.FireKick, "เตะไฟ", "เตะขาออกด้านข้าง 2 ครั้ง!", 18f, SkillMetric.CountRatio, 2f);

        public static readonly PoseSkillDef TailWhip = new PoseSkillDef(
            PoseSkillId.TailWhip, "หางฟาด", "เตะขาไปด้านหลัง 2 ครั้ง!", 18f, SkillMetric.CountRatio, 2f);

        public static readonly PoseSkillDef LightningCharge = new PoseSkillDef(
            PoseSkillId.LightningCharge, "สายฟ้าสะสมพลัง", "ยืนเขย่งค้างไว้!", 30f, SkillMetric.HoldRatio, 4f);

        public static readonly PoseSkillDef FocusHeal = new PoseSkillDef(
            PoseSkillId.FocusHeal, "สมาธิบำบัด", "ยืนเท้าต่อเท้า นิ่งๆ!", 0f, SkillMetric.HoldRatio, 6f);

        public static PoseSkillDef Get(PoseSkillId id) => id switch
        {
            PoseSkillId.EarthSlam => EarthSlam,
            PoseSkillId.StompQuake => StompQuake,
            PoseSkillId.FireKick => FireKick,
            PoseSkillId.TailWhip => TailWhip,
            PoseSkillId.LightningCharge => LightningCharge,
            PoseSkillId.FocusHeal => FocusHeal,
            _ => EarthSlam,
        };
    }
}
