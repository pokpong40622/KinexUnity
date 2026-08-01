using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// Slow, small-angle rotational wobble for a static prop (bushes/flowers) — sells a gentle
    /// breeze without touching a single vertex or mesh. Cheap Transform-only Update, same
    /// philosophy as CloudDrift's position nudge.
    /// </summary>
    public class GentleSway : MonoBehaviour
    {
        public float amplitudeDeg = 2.5f;
        public float speed = 1f;

        Quaternion _baseRotation;
        float _phase;

        void Awake()
        {
            _baseRotation = transform.localRotation;
            _phase = Random.Range(0f, Mathf.PI * 2f); // desync props so they don't sway in lockstep
        }

        void Update()
        {
            // Tilt around Z (a lean, not a spin) — reads as wind pushing the plant sideways,
            // pivoting from its root the way a real stem/branch would.
            float angle = Mathf.Sin(Time.time * speed + _phase) * amplitudeDeg;
            transform.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, angle);
        }
    }
}
