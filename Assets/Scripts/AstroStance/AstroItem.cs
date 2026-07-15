using System;
using System.Collections;
using UnityEngine;
using Kinex.FX;

namespace Kinex.AstroStance
{
    /// <summary>
    /// One falling object in a lane. Life cycle: Falling (accelerating drop, telegraph ring
    /// shrinking on the floor) → Arrived (meteor: impact, resolved immediately by the
    /// director; treasure: rests on the floor; kick ring: hovers at knee height) → the
    /// active window expires OR the director resolves it (Collect / Kicked). Paused freezes
    /// all motion and timers (full-body-lost safety pause).
    /// </summary>
    public class AstroItem : MonoBehaviour
    {
        public enum Phase { Falling, Active, Resolved }

        public AstroKind Kind { get; private set; }
        /// <summary>Screen-space lane: -1 left / 0 middle / +1 right.</summary>
        public int Lane { get; private set; }
        public Phase Stage { get; private set; } = Phase.Falling;
        public bool Paused { get; set; }
        /// <summary>Set once when a ring arrives with the player standing in its lane.</summary>
        public bool Fouled { get; set; }

        /// <summary>Fired once when the item reaches the floor / hover height.</summary>
        public event Action<AstroItem> Arrived;
        /// <summary>Fired once when the active window runs out unresolved.</summary>
        public event Action<AstroItem> Expired;

        float _fallSeconds, _windowSeconds;
        Vector3 _spawnPos, _endPos;
        float _t;
        GameObject _telegraph;
        Transform _ring;
        float _spinDegPerSec;

        public void Init(AstroKind kind, int lane, Vector3 spawnPos, Vector3 endPos,
                         float fallSeconds, float windowSeconds, GameObject telegraph)
        {
            Kind = kind;
            Lane = lane;
            _spawnPos = spawnPos;
            _endPos = endPos;
            _fallSeconds = Mathf.Max(0.1f, fallSeconds);
            _windowSeconds = windowSeconds;
            _telegraph = telegraph;
            if (telegraph != null)
            {
                var r = telegraph.transform.Find("Ring");
                _ring = r != null ? r : null;
            }
            _spinDegPerSec = kind == AstroKind.Meteor ? 55f : (kind == AstroKind.Treasure ? 0f : 30f);
            transform.position = spawnPos;
        }

        void Update()
        {
            if (Paused || Stage == Phase.Resolved) return;
            _t += Time.deltaTime;

            if (_spinDegPerSec > 0f)
                transform.Rotate(Vector3.up, _spinDegPerSec * Time.deltaTime, Space.World);

            switch (Stage)
            {
                case Phase.Falling:
                    float k = Mathf.Clamp01(_t / _fallSeconds);
                    float eased = k * k; // gravity feel: slow start, fast landing
                    transform.position = Vector3.Lerp(_spawnPos, _endPos, eased);
                    if (_ring != null)
                        _ring.localScale = Vector3.one * Mathf.Lerp(1.6f, 0.55f, k);
                    if (_t >= _fallSeconds)
                    {
                        Stage = Phase.Active;
                        _t = 0f;
                        transform.position = _endPos;
                        Arrived?.Invoke(this);
                        // Meteors resolve at impact (the director scores hit/dodge on Arrived).
                        if (Kind == AstroKind.Meteor) Impact();
                    }
                    break;

                case Phase.Active:
                    // Treasure sits still; the ring bobs gently at hover height.
                    if (Kind == AstroKind.KickRing)
                        transform.position = _endPos + Vector3.up * (Mathf.Sin(_t * 2.4f) * 0.05f);
                    if (_t >= _windowSeconds)
                    {
                        Stage = Phase.Resolved;
                        Expired?.Invoke(this);
                        FadeAway();
                    }
                    break;
            }
        }

        /// <summary>Meteor ground impact: burst + scorch flash, then gone. Scoring is the director's.</summary>
        void Impact()
        {
            Stage = Phase.Resolved;
            KinexFx.PopBurst(transform.position + Vector3.up * 0.3f, AstroProps.MeteorOrange, 36);
            KinexFx.PopBurst(transform.position + Vector3.up * 0.15f, new Color(1f, 0.85f, 0.4f), 18);
            KillTelegraph();
            Vector3 s0 = transform.localScale;
            SimpleTween.Float(1f, 0f, 0.30f, v => transform.localScale = s0 * v, this);
            Destroy(gameObject, 0.4f);
        }

        /// <summary>Treasure collected: gold burst, flies to the player, gone.</summary>
        public void Collect(Transform flyTo)
        {
            if (Stage == Phase.Resolved) return;
            Stage = Phase.Resolved;
            KillTelegraph();
            KinexFx.PopBurst(transform.position + Vector3.up * 0.4f, AstroProps.StarGold, 30);
            KinexFx.ConfettiBurst(transform.position + Vector3.up * 0.4f, 30);
            StartCoroutine(FlyToRoutine(flyTo));
        }

        /// <summary>Kick ring hit: cyan burst, ring punched away, gone.</summary>
        public void Kicked(int fromLane)
        {
            if (Stage == Phase.Resolved) return;
            Stage = Phase.Resolved;
            KillTelegraph();
            KinexFx.PopBurst(transform.position, AstroProps.CyanGlow, 34);
            // Punt it away from the kicker, upward and out.
            Vector3 dir = new Vector3(Lane - fromLane, 1.2f, 1.6f).normalized;
            StartCoroutine(PuntRoutine(dir));
        }

        /// <summary>Soft removal (expiry / run ended): rise a little and fade.</summary>
        public void FadeAway()
        {
            Stage = Phase.Resolved;
            KillTelegraph();
            StartCoroutine(FadeRoutine());
        }

        void KillTelegraph()
        {
            if (_telegraph != null) Destroy(_telegraph);
        }

        IEnumerator FlyToRoutine(Transform target)
        {
            Vector3 p0 = transform.position;
            Vector3 s0 = transform.localScale;
            const float duration = 0.45f;
            float t = 0f;
            while (t < duration)
            {
                if (!Paused)
                {
                    t += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, t / duration);
                    Vector3 goal = target != null ? target.position + Vector3.up * 1.1f : p0 + Vector3.up * 1.5f;
                    transform.position = Vector3.Lerp(p0, goal, k);
                    transform.localScale = s0 * (1f - 0.9f * k);
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        IEnumerator PuntRoutine(Vector3 dir)
        {
            Vector3 v = dir * 7f;
            float t = 0f;
            while (t < 0.8f)
            {
                if (!Paused)
                {
                    t += Time.deltaTime;
                    transform.position += v * Time.deltaTime;
                    v += Vector3.down * 9f * Time.deltaTime;
                    transform.Rotate(Vector3.forward, 420f * Time.deltaTime);
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        IEnumerator FadeRoutine()
        {
            Vector3 p0 = transform.position;
            Vector3 s0 = transform.localScale;
            const float duration = 0.9f;
            float t = 0f;
            while (t < duration)
            {
                if (!Paused)
                {
                    t += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, t / duration);
                    transform.position = p0 + Vector3.up * (k * 0.8f);
                    transform.localScale = s0 * (1f - 0.85f * k);
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
