using System;
using System.Collections;
using UnityEngine;
using Kinex.FX;

namespace Kinex.FruitGame
{
    /// <summary>
    /// One drifting food item: approaches the header zone from screen-right, bobs inside the
    /// zone for a short decision window, then drifts out screen-left if nobody resolves it.
    /// The spawner creates it; the manager resolves it (Pop / FloatAway) or lets the zone
    /// window expire (ZoneExpired event → seated outcome).
    /// </summary>
    public class FruitItem : MonoBehaviour
    {
        public enum ItemStage { Approaching, InZone, Leaving, Resolved }

        [Tooltip("Seconds of visible travel from spawn to the header zone.")]
        public float approachSeconds = 4f;
        [Tooltip("Seconds the item sits in the header zone (the decision window).")]
        public float zoneSeconds = 2.5f;
        [Tooltip("Seconds to drift from the zone off screen-left.")]
        public float leaveSeconds = 3f;
        public float bobAmplitude = 0.07f;
        public float bobSpeed = 2.2f;
        [Tooltip("Slow overhead yaw spin so items read as 3D, not flat billboards.")]
        public float spinDegPerSec = 40f;

        public FruitKind Kind { get; private set; }
        public bool IsHealthy => FruitCatalogRef.IsHealthy(Kind);
        public ItemStage Stage { get; private set; } = ItemStage.Approaching;
        /// <summary>Set by the spawner while PoseGate has the game paused — freezes all motion + timers.</summary>
        public bool Paused { get; set; }

        /// <summary>Fired once when the zone window expires without the item being resolved.</summary>
        public event Action<FruitItem> ZoneExpired;

        Vector3 _spawn, _zone, _exit;
        HeaderZone _zoneRef;
        float _t, _bobPhase;

        public void Init(FruitKind kind, Vector3 spawn, Vector3 zonePos, Vector3 exit, HeaderZone zone)
        {
            Kind = kind;
            _spawn = spawn;
            _zone = zonePos;
            _exit = exit;
            _zoneRef = zone;
            _bobPhase = UnityEngine.Random.value * Mathf.PI * 2f;
            transform.position = spawn;
        }

        void Update()
        {
            if (Paused || Stage == ItemStage.Resolved) return;
            _t += Time.deltaTime;
            transform.Rotate(Vector3.up, spinDegPerSec * Time.deltaTime, Space.World);

            switch (Stage)
            {
                case ItemStage.Approaching:
                    transform.position = Vector3.Lerp(_spawn, _zone, _t / approachSeconds) + Bob();
                    if (_t >= approachSeconds)
                    {
                        Stage = ItemStage.InZone;
                        _t = 0f;
                        if (_zoneRef != null) _zoneRef.Pulse();
                    }
                    break;

                case ItemStage.InZone:
                    transform.position = _zone + Bob();
                    if (_zoneRef != null) _zoneRef.Pulse(); // throttled inside HeaderZone
                    if (_t >= zoneSeconds)
                    {
                        Stage = ItemStage.Leaving;
                        _t = 0f;
                        ZoneExpired?.Invoke(this);
                    }
                    break;

                case ItemStage.Leaving:
                    transform.position = Vector3.Lerp(_zone, _exit, _t / leaveSeconds) + Bob();
                    if (_t >= leaveSeconds) Destroy(gameObject);
                    break;
            }
        }

        Vector3 Bob() => new Vector3(0f, Mathf.Sin(Time.time * bobSpeed + _bobPhase) * bobAmplitude, 0f);

        /// <summary>Headed! Sparkle + confetti burst, quick shrink, gone.</summary>
        public void Pop()
        {
            if (Stage == ItemStage.Resolved) return;
            Stage = ItemStage.Resolved;
            KinexFx.PopBurst(transform.position, new Color(1f, 0.95f, 0.5f), 24);
            KinexFx.ConfettiBurst(transform.position, 40);
            Vector3 s0 = transform.localScale;
            SimpleTween.Float(1f, 0f, 0.22f, v => transform.localScale = s0 * v, this);
            Destroy(gameObject, 0.3f);
        }

        /// <summary>Gentle exit: rises and fades out over ~a second. Used for soft/gentle misses.</summary>
        public void FloatAway()
        {
            if (Stage == ItemStage.Resolved) return;
            Stage = ItemStage.Resolved;
            StartCoroutine(FloatRoutine());
        }

        IEnumerator FloatRoutine()
        {
            Vector3 p0 = transform.position;
            Vector3 s0 = transform.localScale;
            const float duration = 1.1f;
            float t = 0f;
            while (t < duration)
            {
                if (!Paused)
                {
                    t += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, t / duration);
                    transform.position = p0 + Vector3.up * (k * 1.2f);
                    transform.localScale = s0 * (1f - 0.8f * k);
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
