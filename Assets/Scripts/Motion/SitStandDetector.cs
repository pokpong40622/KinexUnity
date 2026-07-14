using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Chair-stand state machine: Seated → Rising → Standing → Sitting → Seated, driven by
    /// EMA-smoothed hip-midpoint Y. Falls back to shoulder-midpoint Y when hips are gated but
    /// shoulders are confident (e.g. a table occludes the lower body while seated).
    /// </summary>
    public class SitStandDetector
    {
        public enum SitStandPhase { Seated, Rising, Standing, Sitting }

        const float MinConf = 0.3f;
        const float HipEmaAlpha = 0.3f;
        const float RiseThreshold = 0.35f;
        const float StandThreshold = 0.62f;
        const float StandHoldSeconds = 0.25f;
        const float SitThreshold = 0.65f;
        const float SeatedThreshold = 0.35f;
        const float SitHoldSeconds = 0.25f;
        const float StandingOffsetFactor = 0.55f; // EstimateStandingFromSeated: up = smaller y

        // Adaptive range (hip signal only): recovers from a bad/estimated calibration or the
        // player drifting closer/further from the camera mid-session. New extremes (a deeper
        // sit, a taller stand) snap in immediately; anything sustainably short of the calibrated
        // target slowly releases toward what the player actually achieves. Clamped to +/-20% of
        // the calibrated range so a single bad reading can't run away.
        const float AdaptTauSeconds = 30f;
        const float AdaptClampFactor = 0.2f;

        float _seatedHipYSum, _seatedShYSum; int _seatedCount;
        float _standingHipYSum, _standingShYSum; int _standingCount;
        float _torso0Sum; int _torso0Count;

        float _seatedHipY, _standingHipY, _seatedShY, _standingShY, _torso0;
        bool _hasSeated, _hasStanding;

        float _adaptSeatedY, _adaptStandingY;
        bool _adaptInit;

        float _smoothedY;
        bool _smoothedInit;
        float _standHoldTimer, _sitHoldTimer;

        public bool IsCalibrated => _hasSeated && _hasStanding;
        public SitStandPhase Current { get; private set; } = SitStandPhase.Seated;
        public float Progress01 { get; private set; }
        public bool JustStood { get; private set; }
        public bool JustSat { get; private set; }
        public int StandCount { get; private set; }

        /// <summary>Call repeatedly (~2s) while the player sits still in the seated pose.</summary>
        public void CalibrateSeated(Vector2[] kp)
        {
            _seatedHipYSum += MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            _seatedShYSum += MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]).y;
            _torso0Sum += MotionMath.TorsoLen(kp);
            _seatedCount++; _torso0Count++;
            _seatedHipY = _seatedHipYSum / _seatedCount;
            _seatedShY = _seatedShYSum / _seatedCount;
            _torso0 = _torso0Sum / _torso0Count;
            _hasSeated = true;
        }

        /// <summary>Call repeatedly (~2s) while the player stands still upright.</summary>
        public void CalibrateStanding(Vector2[] kp)
        {
            _standingHipYSum += MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            _standingShYSum += MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]).y;
            _torso0Sum += MotionMath.TorsoLen(kp);
            _standingCount++; _torso0Count++;
            _standingHipY = _standingHipYSum / _standingCount;
            _standingShY = _standingShYSum / _standingCount;
            _torso0 = _torso0Sum / _torso0Count;
            _hasStanding = true;
        }

        /// <summary>
        /// One-shot alternative to CalibrateStanding: estimates the standing baseline from the
        /// seated one (up = smaller y) for setups where the player can't be asked to stand yet.
        /// </summary>
        public void EstimateStandingFromSeated()
        {
            _standingHipY = _seatedHipY - StandingOffsetFactor * _torso0;
            _standingShY = _seatedShY - StandingOffsetFactor * _torso0;
            _hasStanding = true;
        }

        /// <summary>
        /// One-shot alternative to CalibrateSeated: estimates the seated baseline from the
        /// standing one (down = larger y) — mirrors EstimateStandingFromSeated above. Used by
        /// Balance Quest's RestStop beat, which captures a fresh standing baseline right as the
        /// player reaches the rest bench and derives "seated" from it instead of interrupting
        /// play with a second sit-still calibration. Additive only; does not change any existing
        /// call path.
        /// </summary>
        public void EstimateSeatedFromStanding()
        {
            _seatedHipY = _standingHipY + StandingOffsetFactor * _torso0;
            _seatedShY = _standingShY + StandingOffsetFactor * _torso0;
            _hasSeated = true;
        }

        /// <summary>
        /// Clears the seated-calibration accumulators so a fresh <see cref="CalibrateSeated"/>
        /// pass starts from zero. Used by a guided calibration retry (a timed-out attempt must
        /// not leave stale partial samples mixed into the next attempt's average).
        /// </summary>
        public void ResetSeatedCalibration()
        {
            _seatedHipYSum = 0f; _seatedShYSum = 0f; _seatedCount = 0;
            _hasSeated = false;
        }

        /// <summary>Same as <see cref="ResetSeatedCalibration"/>, for the standing pass.</summary>
        public void ResetStandingCalibration()
        {
            _standingHipYSum = 0f; _standingShYSum = 0f; _standingCount = 0;
            _hasStanding = false;
        }

        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            JustStood = false;
            JustSat = false;
            if (!IsCalibrated) return;

            float rawY, seatedY, standingY;
            bool usingHip;
            if (MotionMath.Valid(conf, MinConf, MotionMath.LHip, MotionMath.RHip))
            {
                rawY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
                seatedY = _seatedHipY;
                standingY = _standingHipY;
                usingHip = true;
            }
            else if (MotionMath.Valid(conf, MinConf, MotionMath.LShoulder, MotionMath.RShoulder))
            {
                rawY = MotionMath.Mid(kp[MotionMath.LShoulder], kp[MotionMath.RShoulder]).y;
                seatedY = _seatedShY;
                standingY = _standingShY;
                usingHip = false;
            }
            else
            {
                return; // both hips and shoulders gated — hold last state, timers pause
            }

            // Torso-normalize: rescale the live sample into calibration-distance space so moving
            // closer to / further from the camera mid-session doesn't shrink or stretch the range.
            float torsoNow = MotionMath.TorsoLen(kp);
            float scale = (torsoNow > 0.001f && _torso0 > 0.001f) ? torsoNow / _torso0 : 1f;
            float normalizedY = seatedY + (rawY - seatedY) / scale;

            _smoothedY = _smoothedInit ? MotionMath.Ema(_smoothedY, normalizedY, HipEmaAlpha) : normalizedY;
            _smoothedInit = true;

            if (usingHip)
            {
                if (!_adaptInit) { _adaptSeatedY = _seatedHipY; _adaptStandingY = _standingHipY; _adaptInit = true; }

                float range = _seatedHipY - _standingHipY; // positive: up = smaller y
                float clamp = Mathf.Abs(range) * AdaptClampFactor;
                float step = Mathf.Abs(range) * (dt / AdaptTauSeconds);

                // Only learn from samples on the side of the state machine they're plausible for —
                // a mid-rise frame shouldn't redefine either baseline.
                bool seatSide = Current == SitStandPhase.Seated || Current == SitStandPhase.Sitting;
                bool standSide = Current == SitStandPhase.Standing || Current == SitStandPhase.Rising;

                if (seatSide)
                {
                    _adaptSeatedY = _smoothedY > _adaptSeatedY
                        ? _smoothedY
                        : Mathf.MoveTowards(_adaptSeatedY, _smoothedY, step);
                    _adaptSeatedY = Mathf.Clamp(_adaptSeatedY, _seatedHipY - clamp, _seatedHipY + clamp);
                }
                if (standSide)
                {
                    _adaptStandingY = _smoothedY < _adaptStandingY
                        ? _smoothedY
                        : Mathf.MoveTowards(_adaptStandingY, _smoothedY, step);
                    _adaptStandingY = Mathf.Clamp(_adaptStandingY, _standingHipY - clamp, _standingHipY + clamp);
                }

                seatedY = _adaptSeatedY;
                standingY = _adaptStandingY;
            }

            Progress01 = Mathf.Clamp01(Mathf.InverseLerp(seatedY, standingY, _smoothedY));

            switch (Current)
            {
                case SitStandPhase.Seated:
                    _standHoldTimer = 0f;
                    if (Progress01 > RiseThreshold) Current = SitStandPhase.Rising;
                    break;

                case SitStandPhase.Rising:
                    if (Progress01 > StandThreshold)
                    {
                        _standHoldTimer += dt;
                        if (_standHoldTimer >= StandHoldSeconds)
                        {
                            Current = SitStandPhase.Standing;
                            JustStood = true;
                            StandCount++;
                            _standHoldTimer = 0f;
                        }
                    }
                    else
                    {
                        _standHoldTimer = 0f;
                    }
                    break;

                case SitStandPhase.Standing:
                    _sitHoldTimer = 0f;
                    if (Progress01 < SitThreshold) Current = SitStandPhase.Sitting;
                    break;

                case SitStandPhase.Sitting:
                    if (Progress01 < SeatedThreshold)
                    {
                        _sitHoldTimer += dt;
                        if (_sitHoldTimer >= SitHoldSeconds)
                        {
                            Current = SitStandPhase.Seated;
                            JustSat = true;
                            _sitHoldTimer = 0f;
                        }
                    }
                    else
                    {
                        _sitHoldTimer = 0f;
                    }
                    break;
            }
        }
    }
}
