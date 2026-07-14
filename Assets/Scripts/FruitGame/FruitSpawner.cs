using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kinex.FruitGame
{
    /// <summary>
    /// Spawns food items on a 6–8 s cadence during a set: 70/30 healthy/junk, never two junk
    /// in a row, hard cap per set. Items are plain Destroy-on-exit (no pooling — at this
    /// cadence there are at most ~3 alive). The manager reads CurrentItemInZone and listens
    /// to ItemZoneExpired for the seated outcomes.
    /// </summary>
    public class FruitSpawner : MonoBehaviour
    {
        [Header("Wiring")]
        public HeaderZone headerZone;

        [Header("Schedule")]
        public float spawnIntervalMin = 6f;
        public float spawnIntervalMax = 8f;
        [Range(0f, 1f)] public float healthyChance = 0.7f;
        [Tooltip("Hard cap on items spawned per set — the set ends when they're all resolved.")]
        public int itemCapPerSet = 24;
        [Tooltip("Seconds before the first item of a set appears.")]
        public float firstSpawnDelay = 2f;

        [Header("Travel")]
        [Tooltip("Horizontal world distance from the header zone to the spawn/exit points. " +
                 "Screen-right is world -X (the camera faces -Z), so items spawn at -X.")]
        public float travelHalfWidth = 3.2f;
        [Tooltip("Uniform scale applied to every item so the ~0.25 m props read clearly at camera distance.")]
        public float itemScale = 1.6f;

        /// <summary>Fired when an item's zone window expires unresolved (player stayed seated).</summary>
        public event Action<FruitItem> ItemZoneExpired;

        readonly List<FruitItem> _items = new List<FruitItem>();
        bool _spawning, _paused, _lastWasJunk;
        int _spawnedInSet;
        float _nextSpawnIn;

        /// <summary>True once this set has spawned its full quota (or BeginSet hasn't run).</summary>
        public bool SetSpawnDone => !_spawning;

        public int ActiveCount { get { Prune(); return _items.Count; } }

        public FruitItem CurrentItemInZone
        {
            get
            {
                Prune();
                foreach (var item in _items)
                    if (item.Stage == FruitItem.ItemStage.InZone) return item;
                return null;
            }
        }

        public void BeginSet()
        {
            _spawnedInSet = 0;
            _lastWasJunk = false;
            _nextSpawnIn = firstSpawnDelay;
            _spawning = true;
        }

        /// <summary>Stop spawning and gently clear any leftover items (set ended early).</summary>
        public void EndSet()
        {
            _spawning = false;
            Prune();
            foreach (var item in _items) item.FloatAway();
        }

        /// <summary>Freeze/unfreeze the spawn timer and every live item (PoseGate pause).</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            Prune();
            foreach (var item in _items) item.Paused = paused;
        }

        void Update()
        {
            if (_paused || !_spawning) return;
            _nextSpawnIn -= Time.deltaTime;
            if (_nextSpawnIn > 0f) return;

            Spawn();
            _nextSpawnIn = UnityEngine.Random.Range(spawnIntervalMin, spawnIntervalMax);
            if (_spawnedInSet >= itemCapPerSet) _spawning = false;
        }

        void Spawn()
        {
            if (headerZone == null) return;
            bool healthy = _lastWasJunk || UnityEngine.Random.value < healthyChance;
            _lastWasJunk = !healthy;

            FruitKind kind = FruitCatalogRef.RandomKind(healthy);
            var go = FruitCatalogRef.Build(kind);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * itemScale;

            var item = go.AddComponent<FruitItem>();
            Vector3 zonePos = headerZone.transform.position;
            item.Init(kind,
                      zonePos + new Vector3(-travelHalfWidth, 0.15f, 0f), // screen-right
                      zonePos,
                      zonePos + new Vector3(travelHalfWidth, 0.1f, 0f),   // screen-left
                      headerZone);
            item.Paused = _paused;
            item.ZoneExpired += i => ItemZoneExpired?.Invoke(i);
            _items.Add(item);
            _spawnedInSet++;
        }

        void Prune() => _items.RemoveAll(i => i == null);
    }
}
