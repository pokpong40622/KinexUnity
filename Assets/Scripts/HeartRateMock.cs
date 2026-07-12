using UnityEngine;

namespace Kinex
{
    /// <summary>
    /// Mock heart-rate generator: a bounded random walk that produces a believable
    /// BPM signal and beat timing for HUD animation. This is PLACEHOLDER DATA — it
    /// exists so the HUD has something to drive against before the real BLE/EMG
    /// heart-rate feed is wired up. Swap the caller to a real feed later; the
    /// Bpm/Tick/Reset seam is deliberately small so that's a drop-in replacement.
    /// Plain C# (not a MonoBehaviour) — no scene refs, no UI, no allocations per frame.
    /// </summary>
    public class HeartRateMock
    {
        readonly int _min;
        readonly int _max;
        readonly int _start;

        float _current;     // smoothed, exposed value
        float _walkTarget;   // random-walk target the smoothed value chases
        float _walkTimer;    // seconds until the next walk step
        float _beatTimer;    // seconds accumulated since the last beat

        /// <summary>Current heart rate, rounded to the nearest whole BPM.</summary>
        public int Bpm => Mathf.RoundToInt(_current);

        /// <param name="min">Lowest BPM the walk will clamp to.</param>
        /// <param name="max">Highest BPM the walk will clamp to.</param>
        /// <param name="start">Initial BPM.</param>
        public HeartRateMock(int min = 75, int max = 145, int start = 82)
        {
            _min = min;
            _max = max;
            _start = Mathf.Clamp(start, min, max);
            Reset();
        }

        /// <summary>Resets the walk back to the constructor's start value.</summary>
        public void Reset()
        {
            _current = _start;
            _walkTarget = _start;
            _walkTimer = 0f;
            _beatTimer = 0f;
        }

        /// <summary>
        /// Advances the mock by <paramref name="deltaTime"/> seconds. Call once per frame.
        /// Returns true on the exact frame a heartbeat occurs (60/Bpm seconds apart),
        /// so callers can trigger a pulse animation on the beat.
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return false;

            // Pick a new random-walk target every ~0.8-1.2s.
            _walkTimer -= deltaTime;
            if (_walkTimer <= 0f)
            {
                float delta = Random.Range(-4f, 4f);
                _walkTarget = Mathf.Clamp(_walkTarget + delta, _min, _max);
                _walkTimer = Random.Range(0.8f, 1.2f);
            }

            // Smooth the exposed value toward the walk target so Bpm doesn't jump.
            _current = Mathf.Lerp(_current, _walkTarget, 2f * deltaTime);
            _current = Mathf.Clamp(_current, _min, _max);

            // Beat timing: a beat every 60/Bpm seconds.
            _beatTimer += deltaTime;
            float interval = 60f / Mathf.Max(1, Bpm);
            if (_beatTimer >= interval)
            {
                _beatTimer -= interval;
                return true;
            }
            return false;
        }
    }
}
