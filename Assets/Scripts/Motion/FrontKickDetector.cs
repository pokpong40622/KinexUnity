using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Forward/front kick per side, from the COCO-17 2D keypoints: the ankle rises well off the
    /// floor toward hip height with the foot staying roughly UNDER/IN FRONT of the body in X —
    /// the X-dominance gate is what separates it from the sideways abduction kick, which
    /// <see cref="LegAbductionDetector"/> already owns (and a straight-back kick barely rises,
    /// so <see cref="HipExtensionDetector"/> keeps that one). Height-biased rather than
    /// velocity-biased on purpose: elderly players kick slowly, so we count "foot lifted clearly
    /// forward/up" instead of demanding a fast strike. Same SetBaseline/Tick/JustKicked* shape
    /// as the other kick detectors.
    /// </summary>
    public class FrontKickDetector
    {
        const float MinConf = 0.3f;
        const float RiseOutFactor = 0.45f;   // * torso = ankle rise above its baseline to fire
        const float RiseInFactor = 0.22f;    // * torso = re-arm threshold (hysteresis)
        const float SideGateFactor = 0.35f;  // * torso = max |ankle.x - hip.x| (rules out abduction)
        const float ShinFoldFactor = 0.45f;  // * torso = max ankle-below-knee gap. A marching knee
                                             // raise leaves the shin hanging (~a full torso-length
                                             // gap); an extended front kick foreshortens it.
        const float DebounceSeconds = 0.5f;

        float _torso0;
        float _leftAnkleY0, _rightAnkleY0;
        bool _hasBaseline;

        public bool LeftUp { get; private set; }
        public bool RightUp { get; private set; }
        public bool JustKickedFrontLeft { get; private set; }
        public bool JustKickedFrontRight { get; private set; }

        float _leftCooldown, _rightCooldown;

        /// <summary>Call while the player stands still, both feet on the floor.</summary>
        public void SetBaseline(Vector2[] kp)
        {
            _torso0 = Mathf.Max(MotionMath.TorsoLen(kp), 0.02f);
            _leftAnkleY0 = kp[MotionMath.LAnkle].y;
            _rightAnkleY0 = kp[MotionMath.RAnkle].y;
            _hasBaseline = true;
        }

        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            JustKickedFrontLeft = false;
            JustKickedFrontRight = false;
            if (!_hasBaseline) return;
            if (!MotionMath.Valid(conf, MinConf, MotionMath.LHip, MotionMath.RHip)) return;

            if (_leftCooldown > 0f) _leftCooldown -= dt;
            if (_rightCooldown > 0f) _rightCooldown -= dt;

            TickSide(isLeft: true, kp, conf);
            TickSide(isLeft: false, kp, conf);
        }

        void TickSide(bool isLeft, Vector2[] kp, float[] conf)
        {
            int hipIdx = isLeft ? MotionMath.LHip : MotionMath.RHip;
            int kneeIdx = isLeft ? MotionMath.LKnee : MotionMath.RKnee;
            int ankleIdx = isLeft ? MotionMath.LAnkle : MotionMath.RAnkle;
            if (!MotionMath.Valid(conf, MinConf, ankleIdx, kneeIdx)) return; // hold this side's state

            float y0 = isLeft ? _leftAnkleY0 : _rightAnkleY0;
            float rise = y0 - kp[ankleIdx].y; // image y grows downward → positive = foot lifted
            float side = Mathf.Abs(kp[ankleIdx].x - kp[hipIdx].x);
            bool forwardNotSide = side < SideGateFactor * _torso0;
            bool kneeExtended = (kp[ankleIdx].y - kp[kneeIdx].y) < ShinFoldFactor * _torso0;

            bool wasUp = isLeft ? LeftUp : RightUp;
            float cooldown = isLeft ? _leftCooldown : _rightCooldown;

            if (!wasUp)
            {
                if (rise > RiseOutFactor * _torso0 && forwardNotSide && kneeExtended && cooldown <= 0f)
                {
                    if (isLeft) { LeftUp = true; JustKickedFrontLeft = true; _leftCooldown = DebounceSeconds; }
                    else { RightUp = true; JustKickedFrontRight = true; _rightCooldown = DebounceSeconds; }
                }
            }
            else
            {
                if (rise < RiseInFactor * _torso0)
                {
                    if (isLeft) LeftUp = false; else RightUp = false;
                }
            }
        }
    }
}
