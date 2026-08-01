using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Hip-extension "back kick" per side, read from MediaPipePoseDetector.Landmarks33 (BlazePose
    /// z has real depth: smaller/more-negative = closer to camera, larger = further away). A
    /// back-extension is a same-side ankle moving AWAY from the camera (z increases) while
    /// staying roughly under the hip in X — that X-gate is what tells a straight-back kick apart
    /// from a sideways abduction kick, which <see cref="LegAbductionDetector"/> already owns.
    /// MediaPipe's z is noisy, so thresholds are generous and z itself is EMA-smoothed before any
    /// comparison. Mirrors LegAbductionDetector's SetBaseline/Tick/JustKicked* shape, but Ticks
    /// on the 33-point landmark array (indices 23/24 hips, 25/26 knees, 27/28 ankles) instead of
    /// the 17-point COCO array everything else in Motion uses.
    /// </summary>
    public class HipExtensionDetector
    {
        const int LShoulder = 11, RShoulder = 12;
        const int LHip = 23, RHip = 24;
        const int LAnkle = 27, RAnkle = 28;

        const float MinVisibility = 0.3f;
        const float ZOutFactor = 0.5f;      // * hip width = how far back the ankle must swing to count
        const float ZInFactor = 0.25f;      // * hip width = re-arm threshold (hysteresis)
        const float XGateFactor = 0.25f;    // * torso = max sideways drift allowed (rules out abduction)
        const float HipHeightTolerance = 0.15f; // * torso, same convention as SingleLegStanceDetector
        const float DebounceSeconds = 0.5f;
        const float ZSmoothing = 0.35f;     // EMA alpha for the noisy z channel

        float _torso0, _hipWidth0, _hipY0;
        bool _hasBaseline;

        float _leftAnkleZ0, _rightAnkleZ0;
        float _leftZSmoothed, _rightZSmoothed;

        public bool LeftBack { get; private set; }
        public bool RightBack { get; private set; }
        public bool JustKickedBackLeft { get; private set; }
        public bool JustKickedBackRight { get; private set; }

        float _leftCooldown, _rightCooldown;

        /// <summary>Call once (or repeatedly) while the player stands still, feet under hips.</summary>
        public void SetBaseline(MediaPipePoseDetector.NormLandmark[] lm)
        {
            if (lm == null || lm.Length <= RAnkle) return;

            Vector2 shMid = new Vector2((lm[LShoulder].x + lm[RShoulder].x) * 0.5f, (lm[LShoulder].y + lm[RShoulder].y) * 0.5f);
            Vector2 hipMid = new Vector2((lm[LHip].x + lm[RHip].x) * 0.5f, (lm[LHip].y + lm[RHip].y) * 0.5f);
            _torso0 = Mathf.Max(Vector2.Distance(shMid, hipMid), 0.02f);
            _hipWidth0 = Mathf.Max(Mathf.Abs(lm[LHip].x - lm[RHip].x), 0.02f);
            _hipY0 = hipMid.y;

            _leftAnkleZ0 = lm[LAnkle].z;
            _rightAnkleZ0 = lm[RAnkle].z;
            _leftZSmoothed = _leftAnkleZ0;
            _rightZSmoothed = _rightAnkleZ0;
            _hasBaseline = true;
        }

        public void Tick(MediaPipePoseDetector.NormLandmark[] lm, float dt)
        {
            JustKickedBackLeft = false;
            JustKickedBackRight = false;
            if (!_hasBaseline || lm == null || lm.Length <= RAnkle) return;
            if (lm[LHip].visibility < MinVisibility || lm[RHip].visibility < MinVisibility) return;

            if (_leftCooldown > 0f) _leftCooldown -= dt;
            if (_rightCooldown > 0f) _rightCooldown -= dt;

            float hipY = (lm[LHip].y + lm[RHip].y) * 0.5f;
            bool heightOk = Mathf.Abs(hipY - _hipY0) < HipHeightTolerance * _torso0;

            TickSide(isLeft: true, lm, heightOk, dt);
            TickSide(isLeft: false, lm, heightOk, dt);
        }

        void TickSide(bool isLeft, MediaPipePoseDetector.NormLandmark[] lm, bool heightOk, float dt)
        {
            int hipIdx = isLeft ? LHip : RHip;
            int ankleIdx = isLeft ? LAnkle : RAnkle;
            if (lm[ankleIdx].visibility < MinVisibility) return; // hold last state

            float z0 = isLeft ? _leftAnkleZ0 : _rightAnkleZ0;
            float smoothed = Mathf.Lerp(isLeft ? _leftZSmoothed : _rightZSmoothed, lm[ankleIdx].z, ZSmoothing);
            if (isLeft) _leftZSmoothed = smoothed; else _rightZSmoothed = smoothed;

            float dz = smoothed - z0; // positive = ankle moved further from camera = swung back
            float dx = Mathf.Abs(lm[ankleIdx].x - lm[hipIdx].x);
            bool straightEnough = dx < XGateFactor * _torso0;

            bool wasBack = isLeft ? LeftBack : RightBack;
            float cooldown = isLeft ? _leftCooldown : _rightCooldown;

            if (!wasBack)
            {
                if (dz > ZOutFactor * _hipWidth0 && straightEnough && heightOk && cooldown <= 0f)
                {
                    if (isLeft) { LeftBack = true; JustKickedBackLeft = true; _leftCooldown = DebounceSeconds; }
                    else { RightBack = true; JustKickedBackRight = true; _rightCooldown = DebounceSeconds; }
                }
            }
            else
            {
                if (dz < ZInFactor * _hipWidth0)
                {
                    if (isLeft) LeftBack = false; else RightBack = false;
                }
            }
        }
    }
}
