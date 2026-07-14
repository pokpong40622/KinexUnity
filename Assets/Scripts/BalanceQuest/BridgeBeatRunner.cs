using UnityEngine;
using Kinex.MegaDance;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Single-leg balance over a glowing chasm. 20s window to accumulate
    /// SingleLegStanceDetector.HoldSeconds >= 10, while a per-beat arm-shape variant (0 = arms
    /// out, 1 = hands stacked, 2 = arms crossed) is scored every SampleTickSeconds against
    /// ArmPoseSignatures via the same PoseScorer.Score the whole project uses. In keyboard-stub
    /// mode there are no keypoints to score, so arm match falls back to a fixed "neutral" value
    /// (mirrors KinexWorldDirector's useScoreStub philosophy) so the whole route stays walkable
    /// with a keyboard.
    /// </summary>
    public class BridgeBeatRunner : IQuestBeatRunner
    {
        const float MinConf = 0.3f;
        const float ToleranceRad = 45f * Mathf.Deg2Rad;
        const float SampleTickSeconds = 0.15f;
        const float StubArmMatch = 0.75f;

        QuestContext _ctx;
        QuestBeat _beat;
        float _tickClock;
        float _armSum, _wobbleSum;
        int _samples;
        float _score;

        public bool Done => false;
        public float Score01 => _score;

        public void Begin(QuestContext ctx, QuestBeat beat)
        {
            _ctx = ctx;
            _beat = beat;
            _tickClock = 0f;
            _armSum = 0f;
            _wobbleSum = 0f;
            _samples = 0;
            _score = 0f;

            ctx.props.SpawnBridge(4f); // chasm plank (factory owns cleanup via DespawnAll)
            ctx.setCue?.Invoke("leg", "ยืนขาเดียว ค้างไว้ 10 วินาที!");
            ctx.voiceLine?.Invoke("ยืนขาเดียว ค้างไว้ 10 วินาที!");
        }

        public void Tick(float dt)
        {
            _tickClock += dt;
            if (_tickClock >= SampleTickSeconds)
            {
                _tickClock -= SampleTickSeconds;
                var kp = _ctx.Keypoints;
                var conf = _ctx.Confidence;
                float armMatch = kp != null
                    ? PoseScorer.Score(kp, conf, Target(_beat.difficulty), MinConf, ToleranceRad)
                    : StubArmMatch;
                _armSum += armMatch;
                _wobbleSum += _ctx.Wobble01;
                _samples++;
            }

            float avgArm = _samples > 0 ? _armSum / _samples : StubArmMatch;
            float avgWobble = _samples > 0 ? _wobbleSum / _samples : 0f;
            _score = BeatScoring.Bridge(_ctx.HoldSeconds, avgArm, avgWobble);

            _ctx.wobbleGauge?.SetHold(Mathf.Clamp01(_ctx.HoldSeconds / BeatScoring.BridgeHoldTargetSeconds));
            _ctx.wobbleGauge?.SetWobble(_ctx.Wobble01);
        }

        static float[] Target(int difficulty) => difficulty switch
        {
            1 => ArmPoseSignatures.HandsStacked,
            2 => ArmPoseSignatures.ArmsCrossed,
            _ => ArmPoseSignatures.ArmsOut,
        };
    }
}
