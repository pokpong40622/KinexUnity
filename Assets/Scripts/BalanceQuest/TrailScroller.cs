using System.Collections.Generic;
using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Endless-runway illusion: a pool of 16 segments (3 m each, 3 lanes wide) recycles from far
    /// away back to the front as they scroll toward the camera at <see cref="Speed"/>. Beat
    /// runners <see cref="Attach"/> their prop transforms so gates/coins/fruit/targets scroll at
    /// the exact same rate as the ground with zero per-runner movement code.
    /// </summary>
    public class TrailScroller : MonoBehaviour
    {
        public const float SegmentLength = 3f;
        // 16 * 3 = 48 m: reaches close to the horizon glow band (z=45, see QuestStage) so the
        // floor doesn't visibly end 9 m short of it — that gap was the "hard void edge" between
        // the dark deck and the bright horizon the art brief called out, more than anything fog
        // could fix without also erasing the sun (both sit at a similar distance from camera).
        public const int SegmentCount = 16;
        public const float LaneWidth = 0.9f;

        /// <summary>Meters/second the world scrolls toward the camera. 0 = stopped (Bridge,
        /// TandemStand, BeamWalk, RestStop, Checkpoint all park the scroll while active).</summary>
        public float Speed;

        readonly List<Transform> _segments = new List<Transform>();
        readonly List<Transform> _attached = new List<Transform>();
        readonly List<Renderer> _edgeRenderers = new List<Renderer>();
        float _recycleZ, _totalLength;
        MaterialPropertyBlock _pulseMpb;
        ParticleSystem _speedStreakL, _speedStreakR;
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Pushed well past the post volume's bloom threshold (1.15, tuned for ACES) so the edge
        // strips read as true neon tubes instead of merely bright plastic.
        static readonly Color EdgeEmissionBase = new Color(0.5f, 2.4f, 3.1f);
        // cliff_top_rock deliberately excluded — it's a square-sided cliff TILE whose cube
        // silhouette reads as a black building slab against the sky, not a floating rock.
        static readonly string[] IslandRockNames = { "rock_largeA", "stone_largeA" };
        static readonly Color IslandTint = new Color(0.14f, 0.11f, 0.29f); // #241B4A

        void Awake() => Build();

        /// <summary>Builds the segment pool. Public + idempotent so BalanceQuestSceneBuilder can
        /// call it directly in edit mode (Awake doesn't fire outside Play mode) to preview the
        /// runway for the scene-build screenshot, then discard the temporary GameObject.</summary>
        public void Build()
        {
            if (_segments.Count > 0) return;
            _totalLength = SegmentLength * SegmentCount;
            _recycleZ = -2f; // just behind the avatar/camera

            // Deck kept very dark + UNLIT (large flat up-facing surfaces blow out to white under
            // URP Lit in this project) so the emissive edge strips read as the bright element.
            var floorMat = PropMeshes.MatUnlit(new Color(0.078f, 0.070f, 0.188f)); // #141230
            var edgeMat = PropMeshes.Mat(new Color(0.2f, 0.9f, 0.95f), emission: EdgeEmissionBase);
            var postMagentaMat = PropMeshes.Mat(new Color(0.95f, 0.25f, 0.85f), emission: new Color(2.8f, 0.55f, 2.15f));
            // Faint centreline sheen — a slightly-brighter-than-floor unlit strip running down the
            // middle of every segment, faking a polished-floor reflection of the neon without an
            // actual reflection pass. Kept under the bloom threshold so it reads as a soft sheen,
            // not another glowing tube.
            var sheenMat = PropMeshes.MatUnlit(new Color(0.22f, 0.28f, 0.55f));
            // Grid rungs sit UNDER the bloom threshold (unlike edgeMat) — iteration 1 reused
            // edgeMat for these and it blew the whole deck to a solid white grid instead of a
            // faint distance marker.
            var rungMat = PropMeshes.Mat(new Color(0.2f, 0.6f, 0.7f), emission: new Color(0.1f, 0.45f, 0.6f));
            // Floor "reflection" decals under the posts — see PostGlowMat below.
            var glowMatCyan = PostGlowMat(new Color(0.2f, 0.9f, 0.95f));
            var glowMatMagenta = PostGlowMat(new Color(0.95f, 0.25f, 0.85f));

            for (int i = 0; i < SegmentCount; i++)
            {
                var seg = new GameObject($"Segment{i}");
                seg.transform.SetParent(transform, false);
                seg.transform.localPosition = new Vector3(0f, 0f, i * SegmentLength);

                var floor = Prim(PrimitiveType.Cube, "Floor", seg.transform,
                    new Vector3(0f, -0.05f, 0f), new Vector3(LaneWidth * 3f, 0.1f, SegmentLength), floorMat);

                Prim(PrimitiveType.Cube, "Sheen", seg.transform,
                    new Vector3(0f, 0.001f, 0f), new Vector3(LaneWidth * 0.5f, 0.02f, SegmentLength), sheenMat);

                float[] edgeXs = { -1.5f * LaneWidth, -0.5f * LaneWidth, 0.5f * LaneWidth, 1.5f * LaneWidth };
                foreach (var x in edgeXs)
                {
                    var edge = Prim(PrimitiveType.Cube, "Edge", seg.transform, new Vector3(x, 0.01f, 0f), new Vector3(0.04f, 0.04f, SegmentLength), edgeMat);
                    _edgeRenderers.Add(edge.GetComponent<Renderer>());
                }

                // One dim cross-tie per segment (not pulsed with the lane edges) — a quiet
                // distance marker, not a second glowing grid.
                Prim(PrimitiveType.Cube, "Rung", seg.transform, new Vector3(0f, 0.005f, -SegmentLength * 0.5f), new Vector3(LaneWidth * 3f, 0.01f, 0.025f), rungMat);

                // Edge light posts on both outer lane edges, alternating cyan/magenta per segment.
                var postMat = (i % 2 == 0) ? edgeMat : postMagentaMat;
                float postX = 1.5f * LaneWidth;
                Prim(PrimitiveType.Cylinder, "PostL", seg.transform, new Vector3(-postX, 0.25f, 0f), new Vector3(0.06f, 0.25f, 0.06f), postMat);
                Prim(PrimitiveType.Cylinder, "PostR", seg.transform, new Vector3(postX, 0.25f, 0f), new Vector3(0.06f, 0.25f, 0.06f), postMat);

                // Faint scrolling "reflection" shimmer under each post — a flat decal on the deck,
                // same colour as that segment's posts, alpha-gradient-faded lengthwise (see
                // PostGlowMat) so it reads as a soft mirror sheen bleeding onto the floor rather
                // than a second hard-edged neon tube.
                var glowMat = (i % 2 == 0) ? glowMatCyan : glowMatMagenta;
                Prim(PrimitiveType.Cube, "PostGlowL", seg.transform, new Vector3(-postX, 0.006f, 0f), new Vector3(0.3f, 0.006f, SegmentLength * 0.95f), glowMat);
                Prim(PrimitiveType.Cube, "PostGlowR", seg.transform, new Vector3(postX, 0.006f, 0f), new Vector3(0.3f, 0.006f, SegmentLength * 0.95f), glowMat);

                // Floating island silhouette on alternating flanks — dark Kenney rock chunks
                // (with an occasional palm on the bigger ones), scrolling with the segment.
                BuildIsland(seg.transform, i);

                _segments.Add(seg.transform);
            }

            // Speed-line streaks just outside the lane-readable area (past the outer posts at
            // ±1.35 m) so they read as motion, not clutter the gameplay lanes. Toggled on/off in
            // Update() to match Speed rather than the actual scroll rate — a rough motion cue, not
            // a physically exact one.
            _speedStreakL = KinexFx.SpeedStreaks(new Vector3(-2.2f, 1.0f, -1f), new Vector3(0.5f, 1.3f, 3.5f), Vector3.back, 7f, new Color(0.3f, 0.85f, 1f));
            _speedStreakR = KinexFx.SpeedStreaks(new Vector3(2.2f, 1.0f, -1f), new Vector3(0.5f, 1.3f, 3.5f), Vector3.back, 7f, new Color(1f, 0.3f, 0.85f));
            _speedStreakL.transform.SetParent(transform, false);
            _speedStreakR.transform.SetParent(transform, false);
        }

        void BuildIsland(Transform seg, int index)
        {
            float side = (index % 2 == 0) ? -1f : 1f;
            var rock = KenneyProps.Nature(IslandRockNames[index % IslandRockNames.Length], seg);
            if (rock == null) return; // model missing — skip rather than fall back, purely decorative

            // Sized/placed so islands stay accents, not monoliths: iteration-2 render showed
            // 4 m chunks at x~4-5 towering over the runway and blacking out half the sky.
            // Widened from 1.4-2.6 for iteration 5 (lead review: "vary sizes/heights more") — the
            // upper bound stays under the old monolith-warning ceiling.
            float size = Random.Range(1.1f, 3.0f);
            ScaleToWorldSize(rock, size);
            TintFlat(rock, IslandTint);
            rock.transform.localPosition = new Vector3(side * Random.Range(5.5f, 10f), Random.Range(0.2f, 2.6f), Random.Range(-0.8f, 0.8f));

            if (size > 2f) // larger islands get a palm silhouette
            {
                // Measure the rock's top BEFORE the palm is parented (its renderers would join
                // the bounds), and anchor the palm there in WORLD space: a local offset would be
                // multiplied by the rock's large compensating scale factor and leave the palm
                // hovering in mid-air (visible in the iteration-3 render).
                Vector3 rockTop = TopOf(rock);
                var palm = KenneyProps.Nature("tree_palm", rock.transform);
                if (palm != null)
                {
                    ScaleToWorldSize(palm, Random.Range(2.2f, 3f));
                    TintFlat(palm, IslandTint);
                    palm.transform.position = rockTop - Vector3.up * 0.15f;
                }
            }
        }

        static Vector3 TopOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return go.transform.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        static void ScaleToWorldSize(GameObject go, float targetSize)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (maxDim < 0.0001f) return;
            go.transform.localScale *= targetSize / maxDim;
        }

        // Thin vertical-alpha-gradient texture (opaque at the post end, fading to nothing) on a
        // simple alpha-blended sprite shader — the cheapest way to fake a soft mirror sheen on
        // the deck without an actual reflection pass or a second render target. RGBA32 is fine
        // here (not HDR): it's meant to read as a soft, sub-bloom-threshold sheen, not a glow.
        static Material PostGlowMat(Color tint)
        {
            const int h = 24;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                float a = Mathf.Pow(1f - t, 2f) * 0.35f;
                tex.SetPixel(0, y, new Color(tint.r, tint.g, tint.b, a));
            }
            tex.Apply();
            var shader = Shader.Find("Sprites/Default"); // simple alpha-blended unlit — already used for particles in this project
            var mat = new Material(shader) { mainTexture = tex };
            return mat;
        }

        static void TintFlat(GameObject go, Color tint)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", tint);
            mpb.SetColor("_Color", tint);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(mpb);
        }

        static Transform Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                // Edit-mode safe: the scene builder calls Build() outside Play mode for the
                // screenshot preview, where Destroy() is illegal.
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        // Self-ticks every frame off Time.deltaTime — the director and beat runners only ever
        // set Speed; they must NOT also call Tick() themselves or the scroll would double-advance.
        void Update()
        {
            Tick(Time.deltaTime);
            PulseEdges(); // cosmetic-only, does not touch scroll/Speed state
            SetStreaksActive(Speed > 0.01f);
        }

        void SetStreaksActive(bool active)
        {
            if (_speedStreakL == null || _speedStreakR == null) return;
            var emL = _speedStreakL.emission;
            var emR = _speedStreakR.emission;
            emL.enabled = active;
            emR.enabled = active;
        }

        // Subtle ±30% sine pulse on the lane-edge emission so the strips feel alive rather than
        // static neon tubes. One shared MaterialPropertyBlock reused every frame — cheap.
        void PulseEdges()
        {
            if (_edgeRenderers.Count == 0) return;
            if (_pulseMpb == null) _pulseMpb = new MaterialPropertyBlock();
            float k = 1f + 0.3f * Mathf.Sin(Time.time * 2.2f);
            _pulseMpb.SetColor(EmissionColorId, EdgeEmissionBase * k);
            for (int i = 0; i < _edgeRenderers.Count; i++)
                if (_edgeRenderers[i] != null) _edgeRenderers[i].SetPropertyBlock(_pulseMpb);
        }

        /// <summary>Advances the scroll by one frame. Internal to Update(); public only so
        /// BalanceQuestSelfTest can drive it deterministically outside of Play mode.</summary>
        public void Tick(float dt)
        {
            if (Speed == 0f) return;
            float delta = Speed * dt;

            foreach (var seg in _segments)
            {
                seg.position += Vector3.back * delta;
                if (seg.position.z < _recycleZ) seg.position += Vector3.forward * _totalLength;
            }

            for (int i = _attached.Count - 1; i >= 0; i--)
            {
                var t = _attached[i];
                if (t == null) { _attached.RemoveAt(i); continue; }
                t.position += Vector3.back * delta;
            }
        }

        /// <summary>World Z an object should spawn at to arrive at the origin after
        /// <paramref name="seconds"/> at the CURRENT speed (assumes speed stays constant for the
        /// approach — true for every beat that uses this).</summary>
        public float SpawnZ(float seconds) => Speed * seconds;

        public void Attach(Transform t) { if (t != null) _attached.Add(t); }
        public void Detach(Transform t) { _attached.Remove(t); }
    }
}
