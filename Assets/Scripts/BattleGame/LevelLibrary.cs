namespace Kinex.BattleGame
{
    /// <summary>
    /// Static per-level content: the weighted pose-card pool (level 1 only includes the
    /// chair-based Earth Slam; levels 2-3 are standing-only) and the 2-monsters-then-boss fight
    /// sequence. Plain data, no Unity types, so BattleGameSelfTest can assert pool/monster shape
    /// directly.
    /// </summary>
    public static class LevelLibrary
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 3;

        public static string ThemeNameThai(int level) => level switch
        {
            1 => "ทุ่งหญ้า",
            2 => "ถ้ำคริสตัล",
            3 => "ปราสาทเมฆ",
            _ => "ทุ่งหญ้า",
        };

        /// <summary>Pool order is fixed across levels so HealIndex/ApplyHealGate line up; a level
        /// that doesn't offer a skill simply gives it weight 0 (kept in the array so the UI/test
        /// code has one stable index per PoseSkillId rather than a per-level remap).</summary>
        static readonly PoseSkillId[] PoolOrder =
        {
            PoseSkillId.EarthSlam, PoseSkillId.StompQuake, PoseSkillId.FireKick,
            PoseSkillId.TailWhip, PoseSkillId.LightningCharge, PoseSkillId.FocusHeal,
        };

        public const int HealIndex = 5; // PoolOrder[5] == FocusHeal

        public static PoseSkillId[] Pool(int level) => PoolOrder;

        /// <summary>Base weights (before the heal gate is applied) — level 1 offers the chair
        /// exercise, levels 2-3 are standing-only.</summary>
        public static float[] BaseWeights(int level)
        {
            bool hasChair = level <= 1;
            return new[]
            {
                hasChair ? 3f : 0f, // EarthSlam
                2f,                 // StompQuake
                2f,                 // FireKick
                2f,                 // TailWhip
                2f,                 // LightningCharge
                2f,                 // FocusHeal (further gated by hearts via BattleLogic.ApplyHealGate)
            };
        }

        /// <summary>2 regular monsters + 1 boss, HP rising with level for a light difficulty ramp.
        /// telegraphSeconds are all >= 4s (senior-friendly reaction window — see BattleDirector's
        /// enemy-turn telegraph, which also shows the DEFEND pose ghost during this wait).</summary>
        public static MonsterDef[] Monsters(int level)
        {
            int tier = level <= MinLevel ? 0 : (level >= MaxLevel ? 2 : 1);
            int hpStep = 18 * tier;
            return new[]
            {
                new MonsterDef("สไลม์เขียว", 70 + hpStep, MonsterShape.RoundSlime, false, 4f),
                new MonsterDef("ผีม่วง", 90 + hpStep, MonsterShape.TallGhost, false, 4f),
                new MonsterDef("ราชันหนามทอง", 150 + hpStep * 2, MonsterShape.SpikyBoss, true, 4.5f),
            };
        }
    }
}
