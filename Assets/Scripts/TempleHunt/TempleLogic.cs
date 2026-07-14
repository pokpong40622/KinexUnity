using UnityEngine;
using Kinex.Motion;

namespace Kinex.TempleHunt
{
    /// <summary>
    /// Pure game math for "ล่าสมบัติวิหารโบราณ" (Ancient Temple Treasure Hunt) — no scene deps so
    /// the editor self-test can hit every rule directly (same split as Kinex.FruitGame.FruitGameLogic
    /// and Kinex.MirrorGame.ZoneMatcher). The two arm-pose checks live here rather than in the
    /// frozen Assets/Scripts/Motion/ layer: they are Temple-specific gates layered on top of
    /// SingleLegStanceDetector, not general detectors.
    /// </summary>
    public static class TempleLogic
    {
        public const int ChamberCount = 4;

        // Arm-pose thresholds, all in torso-length units (MotionMath.TorsoLen) so they are
        // distance-invariant like every threshold in the Motion layer.
        const float MinConf = 0.3f;
        const float ArmsOutSpread = 0.5f;      // wrist must be this far outboard of its shoulder
        const float ArmsOutHeightTol = 0.35f;  // wrist within this of shoulder height
        const float ChestRadius = 0.35f;       // wrist within this of the chest midpoint

        /// <summary>Chamber 1: water level starts full (1) and drains to 0 as pump reps land.</summary>
        public static float WaterLevel01(int reps, int target)
        {
            if (target <= 0) return 0f;
            return Mathf.Clamp01(1f - reps / (float)target);
        }

        /// <summary>
        /// Chamber 2: how far up the stone gate sits. Committed reps ratchet it; while the current
        /// stand hasn't been counted yet, the player's live rise (SitStandDetector.Progress01)
        /// pushes it further so the slab visibly rides their body. Once the rep is committed
        /// (standCounted) the live component is dropped — the rep itself now carries the lift —
        /// so the gate never dips back when the player sits down for the next rep.
        /// </summary>
        public static float GateLift01(int repsInGate, int repsPerGate, float progress01, bool standCounted)
        {
            if (repsPerGate <= 0) return 1f;
            float live = standCounted ? 0f : Mathf.Clamp01(progress01);
            return Mathf.Clamp01((repsInGate + live) / repsPerGate);
        }

        /// <summary>
        /// Chambers 3-4: hold timer with gentle decay — credit builds at 1x while the pose is held
        /// and drains at 2x while it's lost, never below 0 and never above target. A stumble costs
        /// a little instead of everything (same feel as Balance Quest's TandemStandBeatRunner).
        /// </summary>
        public static float TickHold(float held, bool ok, float dt, float target)
        {
            return ok
                ? Mathf.Min(target, held + dt)
                : Mathf.Max(0f, held - 2f * dt);
        }

        /// <summary>Balance meter fill: full when steady, empty at max wobble.</summary>
        public static float BalanceMeter01(float wobble01) => 1f - Mathf.Clamp01(wobble01);

        /// <summary>
        /// Chamber 3 "wings": both wrists spread wide of their own shoulder and near shoulder
        /// height. Returns null when the wrists/shoulders aren't confidently tracked — the caller
        /// holds its last known answer instead of failing the hold on a tracking dropout.
        /// </summary>
        public static bool? ArmsOutOk(Vector2[] kp, float[] conf)
        {
            if (!MotionMath.Valid(conf, MinConf,
                    MotionMath.LShoulder, MotionMath.RShoulder,
                    MotionMath.LWrist, MotionMath.RWrist,
                    MotionMath.LHip, MotionMath.RHip))
                return null;

            float torso = MotionMath.TorsoLen(kp);
            if (torso < 0.001f) return null;
            float midX = MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]).x;

            // Sign-free "outboard" test (robust to camera mirroring): the wrist must sit further
            // from the body's centre line than its own shoulder does, by at least the spread.
            bool leftOut = Mathf.Abs(kp[MotionMath.LWrist].x - midX) >
                           Mathf.Abs(kp[MotionMath.LShoulder].x - midX) + ArmsOutSpread * torso;
            bool rightOut = Mathf.Abs(kp[MotionMath.RWrist].x - midX) >
                            Mathf.Abs(kp[MotionMath.RShoulder].x - midX) + ArmsOutSpread * torso;

            bool leftLevel = Mathf.Abs(kp[MotionMath.LWrist].y - kp[MotionMath.LShoulder].y) < ArmsOutHeightTol * torso;
            bool rightLevel = Mathf.Abs(kp[MotionMath.RWrist].y - kp[MotionMath.RShoulder].y) < ArmsOutHeightTol * torso;

            return leftOut && rightOut && leftLevel && rightLevel;
        }

        /// <summary>
        /// Chamber 4 "sacred pose": both wrists folded in over the chest midpoint. Same null
        /// convention as <see cref="ArmsOutOk"/>.
        /// </summary>
        public static bool? HandsOnChestOk(Vector2[] kp, float[] conf)
        {
            if (!MotionMath.Valid(conf, MinConf,
                    MotionMath.LShoulder, MotionMath.RShoulder,
                    MotionMath.LWrist, MotionMath.RWrist,
                    MotionMath.LHip, MotionMath.RHip))
                return null;

            float torso = MotionMath.TorsoLen(kp);
            if (torso < 0.001f) return null;
            Vector2 chest = MotionMath.Mid(
                MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]),
                MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]));

            bool leftIn = Vector2.Distance(kp[MotionMath.LWrist], chest) < ChestRadius * torso;
            bool rightIn = Vector2.Distance(kp[MotionMath.RWrist], chest) < ChestRadius * torso;
            return leftIn && rightIn;
        }

        /// <summary>No-fail philosophy: finishing the temple = full stars.</summary>
        public static int Stars(int chambersCompleted)
        {
            if (chambersCompleted >= 4) return 3;
            if (chambersCompleted >= 2) return 2;
            return 1;
        }

        public static int Coins(int legLifts, int stands, float balanceSeconds)
        {
            return 20 + legLifts + stands + Mathf.RoundToInt(Mathf.Max(0f, balanceSeconds));
        }
    }
}
