using UnityEngine;
using Kinex.Motion;

namespace Kinex.AstroStance
{
    /// <summary>
    /// Calibration-free sit/stand classifier used by AstroStance ONLY (the shared
    /// <see cref="SitStandDetector"/> needs a standing baseline, which was fragile when the
    /// player couldn't be asked to sit-calibrate). This decides the pose purely from the live
    /// skeleton's geometry, so it works the instant the body is visible — no calibration frame.
    ///
    /// Signal: <c>sitRatio = (kneeY - hipY) / (ankleY - kneeY)</c>. Standing, the hip rides well
    /// above the knee, so the ratio is ~1. Sitting, the hip folds DOWN toward knee level while the
    /// feet stay planted, so the numerator collapses and the ratio drops. It is a ratio of two
    /// vertical gaps, so it is invariant to camera distance AND to the y-origin convention (both
    /// gaps flip sign together) — the same formula reads correctly whether y is top- or bottom-origin.
    ///
    /// Measured on the recorded tablet feeds (Assets/AstroStance/TestFeeds): the sit dips the ratio
    /// to ~0.37, while every OTHER move — step-left/right, kick-left/right — never drops below ~0.98.
    /// That wide margin is why a front-on sit (where the 2D knee ANGLE barely bends because the thigh
    /// foreshortens toward the camera) is still caught cleanly, and why steps/kicks never false-fire.
    /// Hysteresis (separate enter/exit thresholds) + a short hold debounce the single transition.
    /// </summary>
    public class AstroSitClassifier
    {
        public enum Phase { Standing, Sitting }
        public Phase Current { get; private set; } = Phase.Standing;
        public bool JustSat { get; private set; }
        public bool JustStood { get; private set; }
        public int StandCount { get; private set; }
        public int SitCount { get; private set; }
        public float SitRatio { get; private set; } = 1f;
        public bool HasSignal { get; private set; }

        const float MinConf = 0.4f;
        const float SitEnter = 0.62f;   // smoothed ratio below this while standing → sit down
        const float StandEnter = 0.82f; // smoothed ratio above this while seated   → stand up
        const float HoldSeconds = 0.18f;
        const float EmaAlpha = 0.35f;

        float _ema;
        bool _emaInit;
        float _sitTimer, _standTimer;

        /// <summary>Feed the latest COCO-17 keypoints + confidence every frame.</summary>
        public void Tick(Vector2[] kp, float[] conf, float dt)
        {
            JustSat = false;
            JustStood = false;
            HasSignal = false;
            if (kp == null || conf == null) return;

            // The ratio needs both legs top-to-bottom; if any is hidden, hold the last state
            // (timers pause) rather than guess.
            if (!MotionMath.Valid(conf, MinConf,
                    MotionMath.LHip, MotionMath.RHip,
                    MotionMath.LKnee, MotionMath.RKnee,
                    MotionMath.LAnkle, MotionMath.RAnkle))
                return;

            float hipY = (kp[MotionMath.LHip].y + kp[MotionMath.RHip].y) * 0.5f;
            float kneeY = (kp[MotionMath.LKnee].y + kp[MotionMath.RKnee].y) * 0.5f;
            float ankY = (kp[MotionMath.LAnkle].y + kp[MotionMath.RAnkle].y) * 0.5f;

            float shin = ankY - kneeY; // knee→ankle vertical gap (feet planted → stable scale ref)
            if (Mathf.Abs(shin) < 1e-3f) return;
            float ratio = (kneeY - hipY) / shin;

            _ema = _emaInit ? MotionMath.Ema(_ema, ratio, EmaAlpha) : ratio;
            _emaInit = true;
            SitRatio = _ema;
            HasSignal = true;

            if (Current == Phase.Standing)
            {
                _standTimer = 0f;
                if (_ema < SitEnter)
                {
                    _sitTimer += dt;
                    if (_sitTimer >= HoldSeconds)
                    {
                        Current = Phase.Sitting;
                        JustSat = true;
                        SitCount++;
                        _sitTimer = 0f;
                    }
                }
                else _sitTimer = 0f;
            }
            else // Sitting
            {
                _sitTimer = 0f;
                if (_ema > StandEnter)
                {
                    _standTimer += dt;
                    if (_standTimer >= HoldSeconds)
                    {
                        Current = Phase.Standing;
                        JustStood = true;
                        StandCount++;
                        _standTimer = 0f;
                    }
                }
                else _standTimer = 0f;
            }
        }
    }
}
