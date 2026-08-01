using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Alternating knee raises — works seated or standing since it only compares knee Y to
    /// hip-midpoint Y (no absolute height baseline needed beyond torso scale).
    /// </summary>
    public class KneeRaiseDetector
    {
        const float MinConf = 0.3f;
        const float RaiseThreshold = 0.35f;
        const float RearmThreshold = 0.20f;
        const float DebounceSeconds = 0.4f;

        float _torso0;
        bool _hasBaseline;

        public bool LeftUp { get; private set; }
        public bool RightUp { get; private set; }
        public bool JustCountedAlternating { get; private set; }
        public int AlternatingCount { get; private set; }

        bool _leftArmed = true, _rightArmed = true;
        float _leftCooldown, _rightCooldown;
        int _lastCountedLeg; // 0 = none, 1 = left, 2 = right

        public void SetBaseline(Vector2[] kp) { _torso0 = MotionMath.TorsoLen(kp); _hasBaseline = true; }

        public void ResetCount()
        {
            AlternatingCount = 0;
            _lastCountedLeg = 0;
        }

        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            JustCountedAlternating = false;
            if (!_hasBaseline) return;
            if (!MotionMath.Valid(conf, MinConf, MotionMath.LHip, MotionMath.RHip, MotionMath.LKnee, MotionMath.RKnee)) return;

            if (_leftCooldown > 0f) _leftCooldown -= dt;
            if (_rightCooldown > 0f) _rightCooldown -= dt;

            float hipMidY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            TickLeg(hipMidY, kp[MotionMath.LKnee].y, isLeft: true);
            TickLeg(hipMidY, kp[MotionMath.RKnee].y, isLeft: false);
        }

        void TickLeg(float hipMidY, float kneeY, bool isLeft)
        {
            float raise = hipMidY - kneeY; // knee rising toward/above the hip line increases this
            bool raisedNow = raise > RaiseThreshold * _torso0;
            bool rearmedNow = raise < RearmThreshold * _torso0;

            if (raisedNow)
            {
                if (isLeft) LeftUp = true; else RightUp = true;

                bool armed = isLeft ? _leftArmed : _rightArmed;
                float cooldown = isLeft ? _leftCooldown : _rightCooldown;
                if (armed && cooldown <= 0f)
                {
                    int leg = isLeft ? 1 : 2;
                    if (leg != _lastCountedLeg)
                    {
                        _lastCountedLeg = leg;
                        AlternatingCount++;
                        JustCountedAlternating = true;
                    }
                    if (isLeft) { _leftArmed = false; _leftCooldown = DebounceSeconds; }
                    else { _rightArmed = false; _rightCooldown = DebounceSeconds; }
                }
            }
            else if (rearmedNow)
            {
                if (isLeft) { LeftUp = false; _leftArmed = true; }
                else { RightUp = false; _rightArmed = true; }
            }
            // else: in the hysteresis band between rearm and raise thresholds — hold current state.
        }
    }
}
