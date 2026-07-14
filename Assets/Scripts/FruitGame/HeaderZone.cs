using UnityEngine;
using Kinex.FX;

namespace Kinex.FruitGame
{
    /// <summary>
    /// The glowing target ring above the avatar's head. Visual is a flattened emissive disc
    /// (PropMeshes.Coin scaled up) facing the camera, plus a looping sparkle trail — all
    /// runtime-built, no art assets. Pulses (throttled) while an item sits inside it.
    /// </summary>
    public class HeaderZone : MonoBehaviour
    {
        [Tooltip("World radius an item must be within to count as inside the zone.")]
        public float radius = 0.6f;
        [Tooltip("Minimum seconds between visual pulses (also longer than the pop tween, so scale can't drift).")]
        public float pulseCooldown = 0.6f;

        const float VisualScale = 9f; // Coin body is 0.09 m across — ~0.8 m glowing disc

        Transform _visual;
        float _lastPulse = -999f;

        void Awake()
        {
            var ring = PropMeshes.Coin();
            ring.name = "GlowRing";
            ring.transform.SetParent(transform, false);
            ring.transform.localScale = Vector3.one * VisualScale;
            _visual = ring.transform;
            KinexFx.SparkleTrail(transform);
        }

        public bool Contains(FruitItem item) =>
            item != null && Vector3.Distance(item.transform.position, transform.position) <= radius;

        public void Pulse()
        {
            if (_visual == null || Time.time - _lastPulse < pulseCooldown) return;
            _lastPulse = Time.time;
            _visual.localScale = Vector3.one * VisualScale; // reset so overlapping pops can't drift
            SimpleTween.ScalePop(_visual, this, 1.18f, 0.3f);
        }
    }
}
