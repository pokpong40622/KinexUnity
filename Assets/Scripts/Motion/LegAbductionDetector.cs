using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Leg-out-to-side kick, per side. Prefers ankle X vs hip X; falls back to knee X vs hip X
    /// when the ankle is gated (tighter thresholds, since the knee travels less than the ankle).
    /// A knee-raise guard stops an upward knee lift from being misread as a sideways kick.
    /// </summary>
    public class LegAbductionDetector
    {
        const float MinConf = 0.3f;
        const float AnkleOutThreshold = 0.45f;
        const float AnkleInThreshold = 0.25f;
        const float KneeOutThreshold = 0.30f;
        const float KneeInThreshold = 0.18f;
        const float RaiseGuardThreshold = 0.25f;
        const float DebounceSeconds = 0.5f;

        float _torso0;
        bool _hasBaseline;

        public bool LeftOut { get; private set; }
        public bool RightOut { get; private set; }
        public bool JustKickedLeft { get; private set; }
        public bool JustKickedRight { get; private set; }
        public float HoldSeconds { get; private set; }

        float _leftHold, _rightHold;
        float _leftCooldown, _rightCooldown;

        public void SetBaseline(Vector2[] kp) { _torso0 = MotionMath.TorsoLen(kp); _hasBaseline = true; }

        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            JustKickedLeft = false;
            JustKickedRight = false;
            if (!_hasBaseline) return;
            if (!MotionMath.Valid(conf, MinConf, MotionMath.LHip, MotionMath.RHip)) return;

            float hipMidY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            TickSide(isLeft: true, kp, conf, hipMidY, dt);
            TickSide(isLeft: false, kp, conf, hipMidY, dt);

            HoldSeconds = LeftOut ? _leftHold : (RightOut ? _rightHold : 0f);
        }

        void TickSide(bool isLeft, Vector2[] kp, float[] conf, float hipMidY, float dt)
        {
            int hipIdx = isLeft ? MotionMath.LHip : MotionMath.RHip;
            int kneeIdx = isLeft ? MotionMath.LKnee : MotionMath.RKnee;
            int ankleIdx = isLeft ? MotionMath.LAnkle : MotionMath.RAnkle;
            if (!MotionMath.Valid(conf, MinConf, hipIdx, kneeIdx)) return; // hold this side's state

            if (isLeft) { if (_leftCooldown > 0f) _leftCooldown -= dt; }
            else { if (_rightCooldown > 0f) _rightCooldown -= dt; }

            float hipX = kp[hipIdx].x;
            bool useAnkle = MotionMath.Valid(conf, MinConf, ankleIdx);
            float d = useAnkle ? Mathf.Abs(kp[ankleIdx].x - hipX) : Mathf.Abs(kp[kneeIdx].x - hipX);
            float outT = useAnkle ? AnkleOutThreshold : KneeOutThreshold;
            float inT = useAnkle ? AnkleInThreshold : KneeInThreshold;
            bool notKneeRaised = (hipMidY - kp[kneeIdx].y) < RaiseGuardThreshold * _torso0;

            bool wasOut = isLeft ? LeftOut : RightOut;
            float cooldown = isLeft ? _leftCooldown : _rightCooldown;

            if (!wasOut)
            {
                if (d > outT * _torso0 && notKneeRaised && cooldown <= 0f)
                {
                    if (isLeft) { LeftOut = true; JustKickedLeft = true; _leftCooldown = DebounceSeconds; }
                    else { RightOut = true; JustKickedRight = true; _rightCooldown = DebounceSeconds; }
                }
            }
            else
            {
                if (isLeft) _leftHold += dt; else _rightHold += dt;
                if (d < inT * _torso0)
                {
                    if (isLeft) { LeftOut = false; _leftHold = 0f; }
                    else { RightOut = false; _rightHold = 0f; }
                }
            }
        }
    }
}
