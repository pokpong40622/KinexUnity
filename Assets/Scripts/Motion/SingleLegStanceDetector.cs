using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// One-leg balance hold. Holding requires exactly one knee raised and hips near standing
    /// height. A short grace window lets HoldSeconds survive a brief wobble/drop without
    /// resetting the streak, while Wobble01 tracks lateral sway via a 1s rolling stddev of
    /// hip-midpoint X (fixed-size ring buffer — no per-frame allocation).
    /// </summary>
    public class SingleLegStanceDetector
    {
        const float MinConf = 0.3f;
        const float KneeRaiseThreshold = 0.25f;
        const float HipHeightTolerance = 0.15f;
        const float GraceSeconds = 0.75f;
        const float WobbleWindowSeconds = 1f;
        const float WobbleNormalizer = 0.08f;
        const int BufferCapacity = 128;

        struct Sample { public float t; public float x; }
        readonly Sample[] _buffer = new Sample[BufferCapacity];
        int _head;
        int _count;
        float _clock;

        float _hipY0Sum, _hipX0Sum, _torso0Sum; int _calCount;
        float _hipY0, _hipX0, _torso0;
        bool _isCalibrated;

        float _notHoldingTimer;

        public bool IsCalibrated => _isCalibrated;
        public bool IsHolding { get; private set; }
        public float HoldSeconds { get; private set; }
        public float Wobble01 { get; private set; }
        public bool StanceLegIsLeft { get; private set; }

        /// <summary>Call repeatedly (~1-2s) while the player stands still on both feet.</summary>
        public void CalibrateStanding(Vector2[] kp)
        {
            _hipY0Sum += MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            _hipX0Sum += MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).x;
            _torso0Sum += MotionMath.TorsoLen(kp);
            _calCount++;
            _hipY0 = _hipY0Sum / _calCount;
            _hipX0 = _hipX0Sum / _calCount;
            _torso0 = _torso0Sum / _calCount;
            _isCalibrated = true;
        }

        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            if (!_isCalibrated) return;
            if (!MotionMath.Valid(conf, MinConf, MotionMath.LHip, MotionMath.RHip, MotionMath.LKnee, MotionMath.RKnee)) return;

            _clock += dt;

            Vector2 hipMid = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]);
            PushSample(hipMid.x);
            Wobble01 = ComputeWobble();

            bool leftRaised = (hipMid.y - kp[MotionMath.LKnee].y) > KneeRaiseThreshold * _torso0;
            bool rightRaised = (hipMid.y - kp[MotionMath.RKnee].y) > KneeRaiseThreshold * _torso0;
            bool heightOk = Mathf.Abs(hipMid.y - _hipY0) < HipHeightTolerance * _torso0;
            bool holdingNow = (leftRaised ^ rightRaised) && heightOk;

            if (holdingNow)
            {
                IsHolding = true;
                HoldSeconds += dt;
                _notHoldingTimer = 0f;
                StanceLegIsLeft = rightRaised; // right knee raised => standing on the left leg
            }
            else
            {
                IsHolding = false;
                _notHoldingTimer += dt;
                if (_notHoldingTimer > GraceSeconds) HoldSeconds = 0f;
            }
        }

        void PushSample(float x)
        {
            _buffer[_head] = new Sample { t = _clock, x = x };
            _head = (_head + 1) % BufferCapacity;
            if (_count < BufferCapacity) _count++;
        }

        float ComputeWobble()
        {
            float cutoff = _clock - WobbleWindowSeconds;
            float sum = 0f, sumSq = 0f; int n = 0;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - 1 - i + BufferCapacity) % BufferCapacity;
                if (_buffer[idx].t < cutoff) break;
                sum += _buffer[idx].x;
                sumSq += _buffer[idx].x * _buffer[idx].x;
                n++;
            }
            if (n < 2) return 0f;
            float mean = sum / n;
            float variance = Mathf.Max(0f, sumSq / n - mean * mean);
            float stddev = Mathf.Sqrt(variance);
            return Mathf.Clamp01(stddev / (WobbleNormalizer * _torso0));
        }
    }
}
