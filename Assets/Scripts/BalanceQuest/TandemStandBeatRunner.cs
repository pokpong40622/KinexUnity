using UnityEngine;
using Kinex.Motion;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Heel-to-toe stand. The camera can't verify actual foot placement, so this scores
    /// STILLNESS only: how long the player holds a standing pose with low hip-X sway, on a
    /// narrow glowing ledge (scroll stopped). Real-detector wobble is a LOCAL 1s rolling stddev
    /// of hip-mid X computed with MotionMath helpers (same idea as
    /// SingleLegStanceDetector.ComputeWobble, deliberately kept local here per the design brief
    /// instead of adding another shared detector). heldSeconds decays (doesn't hard-reset) when
    /// the pose is lost, so a brief camera glitch doesn't wipe out progress.
    /// </summary>
    public class TandemStandBeatRunner : IQuestBeatRunner
    {
        const float WobbleWindowSeconds = 1f;
        const float WobbleNormalizer = 0.08f;
        const float HipHeightTolerance = 0.15f;
        const int BufferCapacity = 128;

        struct Sample { public float t; public float x; }
        readonly Sample[] _buffer = new Sample[BufferCapacity];
        int _head, _count;
        float _clock;

        QuestContext _ctx;
        float _hipY0;
        bool _hasHipBaseline;
        float _heldSeconds;
        float _wobbleSum;
        int _wobbleSamples;
        float _score;

        public bool Done => false;
        public float Score01 => _score;

        public void Begin(QuestContext ctx, QuestBeat beat)
        {
            _ctx = ctx;
            _clock = 0f;
            _head = 0;
            _count = 0;
            _hasHipBaseline = false;
            _heldSeconds = 0f;
            _wobbleSum = 0f;
            _wobbleSamples = 0;
            _score = 0f;

            ctx.props.SpawnBridge(2f); // short plank reused as a narrow ledge
            ctx.setCue?.Invoke("stand", "ยืนเท้าต่อเท้า นิ่ง ๆ 10 วิ");
            ctx.voiceLine?.Invoke("ยืนเท้าต่อเท้า นิ่ง ๆ นะครับ");
        }

        public void Tick(float dt)
        {
            _clock += dt;

            if (_ctx.useKeyboardStub)
            {
                bool holding = _ctx.IsTandemHolding;
                float wobble = holding ? 0.15f : 0.5f;
                _wobbleSum += wobble; _wobbleSamples++;
                float avgWobbleStub = _wobbleSum / _wobbleSamples;
                _score = BeatScoring.Tandem(_ctx.TandemHoldSeconds, avgWobbleStub);
                _ctx.wobbleGauge?.SetHold(Mathf.Clamp01(_ctx.TandemHoldSeconds / BeatScoring.TandemHoldTargetSeconds));
                _ctx.wobbleGauge?.SetWobble(wobble);
                return;
            }

            var kp = _ctx.Keypoints;
            var conf = _ctx.Confidence;
            bool visible = _ctx.PoseVisible && kp != null && MotionMath.Valid(conf, 0.3f, MotionMath.LHip, MotionMath.RHip);
            bool heightOk = true;
            float wobble01 = 0f;

            if (visible)
            {
                float torso = Mathf.Max(MotionMath.TorsoLen(kp), 0.02f);
                float hipY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
                if (!_hasHipBaseline) { _hipY0 = hipY; _hasHipBaseline = true; }
                heightOk = Mathf.Abs(hipY - _hipY0) < HipHeightTolerance * torso;

                float hipX = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).x;
                _buffer[_head] = new Sample { t = _clock, x = hipX };
                _head = (_head + 1) % BufferCapacity;
                if (_count < BufferCapacity) _count++;
                wobble01 = ComputeWobble(torso);
            }

            bool held = visible && heightOk;
            _heldSeconds = held ? _heldSeconds + dt : Mathf.Max(0f, _heldSeconds - dt * 2f);

            _wobbleSum += wobble01; _wobbleSamples++;
            float avgWobble = _wobbleSum / _wobbleSamples;
            _score = BeatScoring.Tandem(_heldSeconds, avgWobble);

            _ctx.wobbleGauge?.SetHold(Mathf.Clamp01(_heldSeconds / BeatScoring.TandemHoldTargetSeconds));
            _ctx.wobbleGauge?.SetWobble(wobble01);
        }

        float ComputeWobble(float torso0)
        {
            float cutoff = _clock - WobbleWindowSeconds;
            float sum = 0f, sumSq = 0f; int n = 0;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - 1 - i + BufferCapacity) % BufferCapacity;
                if (_buffer[idx].t < cutoff) break;
                sum += _buffer[idx].x; sumSq += _buffer[idx].x * _buffer[idx].x; n++;
            }
            if (n < 2) return 0f;
            float mean = sum / n;
            float variance = Mathf.Max(0f, sumSq / n - mean * mean);
            float stddev = Mathf.Sqrt(variance);
            return Mathf.Clamp01(stddev / (WobbleNormalizer * torso0));
        }
    }
}
