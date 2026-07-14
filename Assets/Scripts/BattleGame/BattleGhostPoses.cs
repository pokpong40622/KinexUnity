using System.Collections.Generic;
using Kinex.Trainer;

namespace Kinex.BattleGame
{
    /// <summary>
    /// Maps a battle pose-skill (or enemy-turn defend action) to the nearest existing rehab pose
    /// baked in Assets/Animations/RehabPoseData.asset (see Assets/Editor/RehabPoseBuilder.cs — the
    /// 10 senior balance exercises). Pure string lookup, no Unity dependency beyond TrainerPoseData,
    /// so BattleGameSelfTest can assert every mapped skill actually resolves against the real asset.
    ///
    /// Not every skill has a match: RehabPoseData's leg-raise poses all go FORWARD (front-back +),
    /// and there is no heel-raise/tiptoe pose at all — so TailWhip (back kick) and LightningCharge
    /// (calf-raise hold) are deliberately absent from the map. Callers must treat a -1 result as
    /// "skip the ghost for this skill", not an error.
    /// </summary>
    public static class BattleGhostPoses
    {
        /// <summary>Enemy-turn "Shield" defend (single-leg hold) — reuses the same balance pose as
        /// FocusHeal since both ask for a still, weight-shifted stance.</summary>
        public static readonly string ShieldExercise = "ยกขาทรงตัว";
        public static readonly string ShieldCheckpoint = "ยกขาซ้าย";

        static readonly Dictionary<PoseSkillId, (string exercise, string checkpoint)> SkillMap =
            new Dictionary<PoseSkillId, (string exercise, string checkpoint)>
            {
                { PoseSkillId.EarthSlam,  ("ลุก-นั่ง", "ยืนขึ้น") },
                { PoseSkillId.StompQuake, ("ย่ำเท้าอยู่กับที่", "เข่าซ้ายขึ้น") },
                { PoseSkillId.FireKick,   ("โยกตัวออกข้าง", "ขาซ้ายออกข้าง") },
                { PoseSkillId.FocusHeal,  ("ยกขาทรงตัว", "ยกขาซ้าย") },
                // PoseSkillId.TailWhip and PoseSkillId.LightningCharge intentionally omitted.
            };

        /// <summary>Index into <paramref name="data"/>.poses for the given skill's ghost pose, or -1
        /// if this skill has no matching rehab pose (caller should skip showing a ghost).</summary>
        public static int FindPoseIndex(TrainerPoseData data, PoseSkillId id) =>
            SkillMap.TryGetValue(id, out var m) ? FindPoseIndex(data, m.exercise, m.checkpoint) : -1;

        /// <summary>Index of the first pose whose baked name contains both substrings, or -1.</summary>
        public static int FindPoseIndex(TrainerPoseData data, string exerciseSubstr, string checkpointSubstr)
        {
            if (data == null || data.poses == null) return -1;
            for (int i = 0; i < data.poses.Length; i++)
            {
                string name = data.poses[i]?.name;
                if (name != null && name.Contains(exerciseSubstr) && name.Contains(checkpointSubstr)) return i;
            }
            return -1;
        }
    }
}
