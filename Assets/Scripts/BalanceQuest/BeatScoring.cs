using UnityEngine;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Every beat's 0..1 score formula lives here as a pure static function — called by the beat
    /// runner during Tick AND directly by BalanceQuestSelfTest with known inputs, so the test
    /// exercises the EXACT formula gameplay uses instead of a re-typed copy that could drift.
    /// </summary>
    public static class BeatScoring
    {
        public const float GatePassScore = 1f;
        public const float GateFailScore = 0.25f;
        public static float Gate(bool pass) => pass ? GatePassScore : GateFailScore;

        public const float BridgeHoldTargetSeconds = 10f;
        public static float Bridge(float holdSeconds, float avgArmMatch01, float avgWobble01)
        {
            float holdTerm = Mathf.Clamp01(holdSeconds / BridgeHoldTargetSeconds);
            return 0.6f * holdTerm + 0.25f * Mathf.Clamp01(avgArmMatch01) + 0.15f * (1f - Mathf.Clamp01(avgWobble01));
        }

        public static float Ratio(int achieved, int target) => target > 0 ? Mathf.Clamp01(achieved / (float)target) : 0f;

        public const int TiptoeFruitCount = 3;
        public static float Tiptoe(int grabbed) => Ratio(grabbed, TiptoeFruitCount);

        public const int KickTargetCount = 4;
        public static float Kick(int hits) => Ratio(hits, KickTargetCount);

        public const int BackKickTargetCount = 4;
        public static float BackKick(int hits) => Ratio(hits, BackKickTargetCount);

        public const float TandemHoldTargetSeconds = 10f;
        public static float Tandem(float heldSeconds, float avgWobble01)
        {
            float holdTerm = Mathf.Clamp01(heldSeconds / TandemHoldTargetSeconds);
            return 0.5f * holdTerm + 0.5f * (1f - Mathf.Clamp01(avgWobble01));
        }

        public const int BeamWalkStepTarget = 10;
        public static float BeamWalk(int steps, float centeredFraction01) =>
            0.7f * Ratio(steps, BeamWalkStepTarget) + 0.3f * Mathf.Clamp01(centeredFraction01);

        public const int RestStopStandTarget = 5;
        public const int RestStopRaiseTarget = 6;
        public static float RestStop(int stands, int raises) =>
            0.7f * Ratio(stands, RestStopStandTarget) + 0.3f * Ratio(raises, RestStopRaiseTarget);
    }
}
