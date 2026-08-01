using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Lateral stepping into 3 lanes (left / center / right) based on hip-midpoint X relative to
    /// a calibrated center. Lane sign is in IMAGE space (+X = right side of the frame) — if the
    /// game mirrors the camera feed for display, flipping Lane's sign is the caller's job.
    /// </summary>
    public class LaneDetector
    {
        const float MinConf = 0.3f;
        const float EnterFrac = 0.8f;
        const float CenterFrac = 0.5f;
        const float SustainSeconds = 0.15f;
        const float DriftTimeConstant = 20f; // seconds

        float _centerXSum, _widthSum; int _calCount;
        float _centerX0, _laneW;
        bool _isCalibrated;

        float _sideTimer, _centerTimer;

        public bool IsCalibrated => _isCalibrated;
        public int Lane { get; private set; }
        public float LaneX01 { get; private set; }
        public bool JustChangedLane { get; private set; }

        /// <summary>Call repeatedly (~1-2s) while the player stands centered.</summary>
        public void CalibrateCenter(Vector2[] kp)
        {
            _centerXSum += MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).x;
            _widthSum += MotionMath.ShoulderWidth(kp);
            _calCount++;
            _centerX0 = _centerXSum / _calCount;
            _laneW = Mathf.Max(0.45f * (_widthSum / _calCount), 0.05f);
            _isCalibrated = true;
        }

        /// <summary>Silent one-shot re-centre (e.g. at a level checkpoint) — does not reset Lane/hysteresis.</summary>
        public void RecalibrateCenter(Vector2[] kp)
        {
            _centerXSum = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).x;
            _widthSum = MotionMath.ShoulderWidth(kp);
            _calCount = 1;
            _centerX0 = _centerXSum;
            _laneW = Mathf.Max(0.45f * _widthSum, 0.05f);
        }

        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            JustChangedLane = false;
            if (!_isCalibrated) return;
            if (!MotionMath.Valid(conf, MinConf, MotionMath.LHip, MotionMath.RHip)) return;

            float hipX = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).x;
            float dx = hipX - _centerX0;
            LaneX01 = Mathf.Clamp(dx / _laneW, -1f, 1f);
            float absDx = Mathf.Abs(dx);

            if (Lane == 0)
            {
                if (absDx > EnterFrac * _laneW)
                {
                    _sideTimer += dt;
                    if (_sideTimer >= SustainSeconds)
                    {
                        Lane = dx > 0f ? 1 : -1;
                        JustChangedLane = true;
                        _sideTimer = 0f;
                    }
                }
                else
                {
                    _sideTimer = 0f;
                }

                // Slow drift correction so a player who was centered but drifted stays "center".
                _centerX0 = MotionMath.Ema(_centerX0, hipX, dt / DriftTimeConstant);
            }
            else
            {
                if (absDx < CenterFrac * _laneW)
                {
                    _centerTimer += dt;
                    if (_centerTimer >= SustainSeconds)
                    {
                        Lane = 0;
                        JustChangedLane = true;
                        _centerTimer = 0f;
                    }
                }
                else
                {
                    _centerTimer = 0f;
                }
            }
        }
    }
}
