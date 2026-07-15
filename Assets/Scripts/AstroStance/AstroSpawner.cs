using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Kinex.AstroStance
{
    /// <summary>
    /// Plays a fixed AstroLogic deck on a steady beat: one object falls per beat into its
    /// lane, telegraphed by a floor ring while it drops. The director listens to
    /// ItemArrived/ItemExpired for scoring and queries the live items for treasure/kick
    /// resolution. No pooling — at this cadence at most ~2 items are alive.
    /// </summary>
    public class AstroSpawner : MonoBehaviour
    {
        [Header("Lanes (screen space: -1 left, +1 right)")]
        [Tooltip("World X distance between adjacent lanes. Lane world x = lane * laneSpacing.")]
        public float laneSpacing = 1.5f;
        [Tooltip("World Z where objects land — ahead of the player line so the behind view reads depth.")]
        public float impactZ = 1.8f;

        [Header("Fall")]
        public float spawnHeight = 7f;
        [Tooltip("Seconds from sky to floor — the player's reaction window.")]
        public float fallSeconds = 4f;
        [Tooltip("Kick rings hover at knee height instead of landing.")]
        public float ringHoverHeight = 0.45f;

        [Header("Beat schedule")]
        [Tooltip("Seconds between beats. 28 beats × 6.2 s ≈ a 3-minute session.")]
        public float beatInterval = 6.2f;
        public float firstBeatDelay = 2.5f;

        [Header("Active windows")]
        [Tooltip("Seconds a landed treasure waits to be collected.")]
        public float treasureWindowSeconds = 7f;
        [Tooltip("Seconds a kick ring hovers waiting to be kicked.")]
        public float ringWindowSeconds = 6f;

        [Header("Wiring")]
        [Tooltip("FC Iconic SDF — the kick ring's Thai label. Wired by the scene builder.")]
        public TMP_FontAsset thaiFont;

        public event Action<AstroItem> ItemArrived;
        public event Action<AstroItem> ItemExpired;
        /// <summary>Fired when a new beat spawns (HUD lane-warning icons).</summary>
        public event Action<AstroItem> ItemSpawned;

        readonly List<AstroItem> _items = new List<AstroItem>();
        AstroLogic.Beat[] _deck;
        int _next;
        float _nextBeatIn;
        bool _running, _paused;

        public bool DeckDone => _deck != null && _next >= _deck.Length && ActiveCount == 0;
        public int ActiveCount { get { Prune(); return _items.Count; } }
        public int BeatsSpawned => _next;
        public int BeatsTotal => _deck?.Length ?? 0;

        public void BeginRun(AstroLogic.Beat[] deck)
        {
            _deck = deck;
            _next = 0;
            _nextBeatIn = firstBeatDelay;
            _running = true;
        }

        public void StopRun()
        {
            _running = false;
            Prune();
            foreach (var item in _items) item.FadeAway();
        }

        /// <summary>Freeze the beat clock and every live item (safety pause).</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            Prune();
            foreach (var item in _items) item.Paused = paused;
        }

        /// <summary>First live item of a kind in the Active window, or null.</summary>
        public AstroItem ActiveItem(AstroKind kind, int lane = int.MinValue)
        {
            Prune();
            foreach (var item in _items)
                if (item.Kind == kind && item.Stage == AstroItem.Phase.Active &&
                    (lane == int.MinValue || item.Lane == lane))
                    return item;
            return null;
        }

        public float LaneWorldX(int lane) => lane * laneSpacing;

        void Update()
        {
            if (!_running || _paused || _deck == null || _next >= _deck.Length) return;
            _nextBeatIn -= Time.deltaTime;
            if (_nextBeatIn > 0f) return;
            SpawnBeat(_deck[_next]);
            _next++;
            _nextBeatIn = beatInterval;
        }

        void SpawnBeat(AstroLogic.Beat beat)
        {
            if (beat.kind == AstroKind.Rest) return;

            GameObject go;
            Color telegraphColor;
            float endY, window;
            switch (beat.kind)
            {
                case AstroKind.Meteor:
                    go = AstroProps.Meteor();
                    telegraphColor = AstroProps.MeteorOrange;
                    endY = 0.35f;
                    window = 0f; // meteors resolve at impact
                    break;
                case AstroKind.Treasure:
                    go = AstroProps.Treasure();
                    telegraphColor = AstroProps.StarGold;
                    endY = 0f;
                    window = treasureWindowSeconds;
                    break;
                default:
                    go = AstroProps.KickRing(thaiFont);
                    telegraphColor = AstroProps.CyanGlow;
                    endY = ringHoverHeight + 0.1f; // ring center ≈ shin height — an easy, low side-kick
                    window = ringWindowSeconds;
                    break;
            }

            float x = LaneWorldX(beat.lane);
            var telegraph = AstroProps.TelegraphRing(telegraphColor);
            telegraph.transform.SetParent(transform, false);
            telegraph.transform.position = new Vector3(x, 0f, impactZ);

            go.transform.SetParent(transform, false);
            var item = go.AddComponent<AstroItem>();
            item.Init(beat.kind, beat.lane,
                      new Vector3(x, spawnHeight, impactZ),
                      new Vector3(x, endY, impactZ),
                      fallSeconds, window, telegraph);
            item.Paused = _paused;
            item.Arrived += i => ItemArrived?.Invoke(i);
            item.Expired += i => ItemExpired?.Invoke(i);
            _items.Add(item);
            ItemSpawned?.Invoke(item);
        }

        // Unity's destroyed-object fake-null covers items whose GameObject is gone.
        void Prune() => _items.RemoveAll(i => i == null);
    }
}
