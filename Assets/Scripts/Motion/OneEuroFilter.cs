using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Standard 1€ filter (Casiez et al.) — speed-adaptive low-pass: heavy smoothing while the
    /// signal is slow (kills rest jitter), light smoothing while it moves fast (kills lag).
    /// This is what MediaPipe itself ships for landmark smoothing; our plain EMA either shivers
    /// or drags. One instance filters ONE scalar channel — callers keep one per x/y/z per landmark.
    ///
    /// cutoff = minCutoff + beta * |velocity|; alpha(cutoff) = 1 / (1 + tau/dt), tau = 1/(2π·cutoff).
    /// Lower minCutoff = steadier at rest but laggier; higher beta = snappier during fast motion.
    /// </summary>
    public class OneEuroFilter
    {
        float _minCutoff;
        float _beta;
        float _dCutoff;

        bool _init;
        float _prev;
        float _dPrev;

        public OneEuroFilter(float minCutoff, float beta, float dCutoff = 1f)
        {
            Configure(minCutoff, beta, dCutoff);
        }

        /// <summary>Retune without losing filter state (safe to call every frame from inspector-backed fields).</summary>
        public void Configure(float minCutoff, float beta, float dCutoff)
        {
            _minCutoff = Mathf.Max(minCutoff, 1e-4f);
            _beta = Mathf.Max(beta, 0f);
            _dCutoff = Mathf.Max(dCutoff, 1e-4f);
        }

        /// <summary>Forget history — call after tracking is lost so re-acquire doesn't glide in from a stale value.</summary>
        public void Reset() => _init = false;

        public float Filter(float x, float dt)
        {
            if (dt <= 0f) return _init ? _prev : x;
            if (!_init)
            {
                _init = true;
                _prev = x;
                _dPrev = 0f;
                return x;
            }

            // Derivative, smoothed at the (fixed) derivative cutoff.
            float dx = (x - _prev) / dt;
            _dPrev = Mathf.Lerp(_dPrev, dx, Alpha(_dCutoff, dt));

            // Speed-adaptive cutoff, then the actual value low-pass.
            float cutoff = _minCutoff + _beta * Mathf.Abs(_dPrev);
            _prev = Mathf.Lerp(_prev, x, Alpha(cutoff, dt));
            return _prev;
        }

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }
    }
}
