using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// Slow one-axis drift for a background prop (clouds), wrapping back to its start once it
    /// has moved <see cref="range"/> past the origin. Cheap "sky is alive" cue for static
    /// low-poly backdrops — no physics, just a Transform nudge in Update.
    /// </summary>
    public class CloudDrift : MonoBehaviour
    {
        public float speed = 0.15f;   // world units / second
        public float range = 6f;      // distance before wrapping back

        Vector3 _start;

        void Awake() => _start = transform.localPosition;

        void Update()
        {
            float offset = (Time.time * speed) % range;
            transform.localPosition = _start + new Vector3(offset, 0f, 0f);
        }
    }
}
