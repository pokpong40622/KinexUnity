using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Calf raise (rise onto the toes). Deliberately ignores ankles — they're often cropped off
    /// the bottom of a tablet's camera frame — and instead reads how far the hips + shoulders
    /// rose together relative to the standing baseline.
    /// </summary>
    public class TiptoeDetector
    {
        const float MinConf = 0.3f;
        const float RiseOnThreshold = 0.06f;
        const float RiseOffThreshold = 0.03f;
        const float SustainSeconds = 0.2f;

        float _hipY0Sum, _shY0Sum, _torso0Sum; int _calCount;
        float _hipY0, _shY0, _torso0;
        bool _isCalibrated;

        float _riseTimer;

        public bool IsCalibrated => _isCalibrated;
        public bool IsRaised { get; private set; }
        public float Progress01 { get; private set; }
        public bool JustRaised { get; private set; }

        /// <summary>Call repeatedly (~1s) while the player stands flat-footed and still.</summary>
        public void CalibrateStanding(Vector2[] kp)
        {
            _hipY0Sum += MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            _shY0Sum += MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]).y;
            _torso0Sum += MotionMath.TorsoLen(kp);
            _calCount++;
            _hipY0 = _hipY0Sum / _calCount;
            _shY0 = _shY0Sum / _calCount;
            _torso0 = _torso0Sum / _calCount;
            _isCalibrated = true;
        }

        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            JustRaised = false;
            if (!_isCalibrated) return;
            if (!MotionMath.Valid(conf, MinConf, MotionMath.LShoulder, MotionMath.RShoulder, MotionMath.LHip, MotionMath.RHip)) return;

            float hipY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            float shY = MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]).y;
            float rise = ((_hipY0 - hipY) + (_shY0 - shY)) * 0.5f; // positive = body rose

            Progress01 = Mathf.Clamp01(rise / (RiseOnThreshold * _torso0));

            if (!IsRaised)
            {
                if (rise > RiseOnThreshold * _torso0)
                {
                    _riseTimer += dt;
                    if (_riseTimer >= SustainSeconds)
                    {
                        IsRaised = true;
                        JustRaised = true;
                        _riseTimer = 0f;
                    }
                }
                else
                {
                    _riseTimer = 0f;
                }
            }
            else if (rise < RiseOffThreshold * _torso0)
            {
                IsRaised = false;
            }
        }
    }
}
