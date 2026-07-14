using System.Collections.Generic;
using UnityEngine;
using Kinex.FX;

namespace Kinex.FruitGame
{
    /// <summary>
    /// Builds the "Morning Orchard" stage from code: gradient sky dome, trilight ambient, a
    /// two-tone grass disc, a sand path leading to the avatar, a fence arc, three depths of
    /// Kenney trees, hand-clustered scatter dressing (bushes/flowers/mushrooms/stones/log),
    /// distant hills, sky clouds + sun, and the recolored chair. No material or mesh assets for
    /// the base ground/sky (still runtime Unlit primitives — large flat Lit surfaces blow out to
    /// white in this project), but the props themselves are imported Kenney CC0 models, measured
    /// and rescaled to real-world size at spawn time. Called by FruitStage.Awake at runtime and
    /// by the scene builder for the screenshot (so it must work in edit mode too — hence
    /// DestroySafe).
    /// </summary>
    public static class FruitStageBuilder
    {
        // Single source of truth for the dawn gradient's two stops — shared with
        // FruitGameSceneBuilder (camera solid-clear color must match the dome's horizon stop)
        // and with the fog/hill tinting below, so nobody hardcodes a second copy that can drift.
        // Pushed more saturated than round 2's (0.302,0.427,0.663): the WhiteBalance+18 warm
        // grade (see FruitGameSceneBuilder.CreatePostProfile) desaturates whatever reaches it
        // toward yellow, so the on-screen zenith read as flat lavender-grey even once fog/squash
        // let it through. Pre-compensating with a punchier azure here survives that grade.
        public static readonly Color SkyTop = new Color(0.22f, 0.38f, 0.72f);      // soft dusk-blue zenith
        public static readonly Color SkyHorizon = new Color(1.0f, 0.78f, 0.52f);       // warm dawn-gold horizon

        public static void BuildStage(Transform parent)
        {
            // Morning-orchard sky: warm gold horizon into a soft blue zenith. Round 1-2 kept a
            // full, unsquashed hemisphere — this portrait camera only sees elevations up to
            // ~11 degrees (see BuildSky's frustum note below), so the visible wedge only reached
            // v~0.12 of the gradient, almost pure horizon color (the flat-beige upper frame in
            // every earlier screenshot). Squashing the dome to 0.35x height, like BattleStage
            // does for the exact same reason, pushes that same 11-degree ray to v~0.29 — real
            // zenith blue actually reaches the frame. Radius (60) is far bigger than BattleStage's
            // 14, so there's no risk of the shell clipping the tallest (5.2m) trees: the dome
            // surface above any tree's footprint stays >15m up even after the squash.
            var sky = ProceduralSkybox.CreateSkyDome(SkyTop, SkyHorizon, 60f);
            sky.transform.SetParent(parent, false);
            // Squashed further than round 2 (0.35->0.18): at 0.35 the visible elevation band
            // (~0-11deg, this camera's frustum) only reached v~0.29 of the gradient — still 70%+
            // horizon-gold, which is why the "zenith" read as flat warm yellow with zero blue in
            // the screenshot. At 0.18 the same 11-degree ray reaches v~0.55, deep enough into the
            // blue half to actually read as sky. Dome surface still clears every tree/hill/sun by
            // a wide margin at this squash (checked: >9m above the tallest 5.2m tree canopy).
            sky.transform.localScale = new Vector3(1f, 0.13f, 1f);
            // Trilight ambient tracks the same two stops (bluer sky bounce, warmer equator
            // bounce) scaled to 0.55x magnitude: at full strength they sum with the
            // 1.05-intensity sun to ~1.5x on lit faces, pushing every Lit surface past the bloom
            // threshold and out to white/salmon (verified in the builder screenshot — fence,
            // bushes and canopies all washed out). NOTE RenderSettings.ambientIntensity does NOT
            // apply to Trilight mode, so the scale has to be baked into the colors themselves.
            const float ambientScale = 0.55f;
            ProceduralSkybox.SetAmbient(new Color(0.42f, 0.52f, 0.72f) * ambientScale,   // sky (zenith-blue)
                                        new Color(0.90f, 0.75f, 0.58f) * ambientScale,   // equator (dawn-gold)
                                        new Color(0.420f, 0.561f, 0.322f) * ambientScale); // ground #6B8F52

            // Tinted linear fog toward the sky's horizon colour so the far tree row / hills melt
            // into the background instead of hard-cutting against it (aerial perspective). Start
            // just past the mid tree row so foreground/avatar are never touched.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyHorizon; // matches the dawn-gold horizon stop
            // Pushed further out than the first pass (was 9/34): that range fully engulfed the
            // sun disc and clouds (z -20..-31, same depth as the far hills), pulling their HDR
            // brightness down into flat fog color and making the sun invisible instead of glowing.
            // Round 2 (14/70) still fogged the sky dome's own zenith band back to flat horizon-gold
            // — the visible part of the dome sits ~37-55 units out (well inside a 70 end), so its
            // blue gradient got blended straight back to fogColor (=SkyHorizon) and never read on
            // screen. Pushed end to 170 so that band only picks up a light haze; hills/far trees
            // still fade (they're tinted toward SkyHorizon in their own base color already, so
            // losing some fog-fade there costs little) but the sky's actual blue survives.
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 170f;

            // ---- Ground: darker outer ring peeking out from under a brighter sunlit disc. ----
            // GROUND-LIT RETRY (art direction step 8): URP Lit on a large flat disc previously
            // blew out to pure white — smoothness 0 + specular/env-reflections keywords off
            // wasn't enough on its own back then, but that pass predates ACES tonemapping.
            // Re-tested here with ACES active; if either disc still reads white/blown in the
            // screenshot, both calls fall back to LitMat's Unlit-only sibling (MatUnlit) and this
            // comment gets a follow-up. Lit is what buys real cast shadows on the ground.
            Disc(parent, "GroundOuter", new Vector3(0f, -0.04f, 0f), new Vector3(34f, 0.05f, 34f),
                 new Color(0.369f, 0.620f, 0.243f), lit: true); // #5E9E3E
            Disc(parent, "Ground", new Vector3(0f, -0.03f, 0f), new Vector3(26f, 0.05f, 26f),
                 new Color(0.498f, 0.749f, 0.302f), lit: true); // #7FBF4D
            // Small warm-sand landing pad under the chair the path visually flows into.
            // LIT on purpose (unlike the big discs): shadows don't render on Unlit surfaces,
            // and this pad is what catches the avatar/chair soft shadow that grounds them.
            // Small enough (2 m) not to trip the large-flat-Lit-surface washout.
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "StagePad";
            DestroySafe(pad.GetComponent<Collider>());
            pad.transform.SetParent(parent, false);
            pad.transform.localPosition = new Vector3(0f, 0.005f, -0.1f);
            pad.transform.localScale = new Vector3(2.0f, 0.02f, 2.0f);
            pad.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.851f, 0.702f, 0.502f)); // #D9B380

            BuildPath(parent);
            BuildFence(parent);
            BuildTrees(parent);
            BuildScatter(parent);

            // Distant hills, pushed out beyond the far tree row, lifted toward the sky's horizon
            // color (not just their own green-grey) so they read as atmospheric depth melting
            // into the dawn haze rather than a hard green wall. Kept close enough to center (see
            // the frustum note above BuildTrees) that their huge radius still clips into the
            // visible band instead of sitting entirely off-frame.
            Hill(parent, new Vector3(-4f, -2f, -30f), new Vector3(22f, 7f, 22f),
                 Color.Lerp(new Color(0.60f, 0.72f, 0.62f), SkyHorizon, 0.35f));
            Hill(parent, new Vector3(4f, -2.5f, -34f), new Vector3(26f, 8f, 26f),
                 Color.Lerp(new Color(0.58f, 0.70f, 0.64f), SkyHorizon, 0.35f));

            BuildMistBand(parent, SkyHorizon);
            BuildSky(parent);

            // Chair right behind the avatar origin (backrest away from the camera), recolored to
            // warm wood with a soft-red seat cushion.
            var chair = PropMeshes.Chair();
            chair.transform.SetParent(parent, false);
            chair.transform.localPosition = new Vector3(0f, 0f, -0.18f);
            RecolorChair(chair);

            // Drifting golden pollen.
            var motes = KinexFx.AmbientMotes(new Vector3(0f, 2.2f, 0f), new Vector3(9f, 3.5f, 7f),
                                             new Color(1f, 0.95f, 0.6f, 0.8f), 8);
            motes.transform.SetParent(parent, true);

            // Occasional falling leaf — reuses AmbientMotes' slow-drift/noise recipe but with
            // gravity + a mid-tree-canopy color so it reads as leaf-fall, not pollen. Low rate
            // (1/sec) keeps it a background detail, not a snow flurry.
            var leaves = BuildFallingLeaves(new Vector3(0f, 4.2f, -4f), new Vector3(7f, 0.3f, 8f));
            leaves.transform.SetParent(parent, true);

            // A couple of butterflies drifting near the fence/path — the orchard needed a second
            // life cue beyond falling leaves per the art review. Small, close, off-center so they
            // don't compete with the avatar's silhouette.
            var b1 = Butterfly.Spawn(new Vector3(-1.0f, 1.15f, 0.6f), new Vector3(0.6f, 0.25f, 0.5f),
                                      new Color(0.95f, 0.55f, 0.15f));
            b1.transform.SetParent(parent, true);
            var b2 = Butterfly.Spawn(new Vector3(1.1f, 1.35f, -1.2f), new Vector3(0.5f, 0.2f, 0.6f),
                                      new Color(0.85f, 0.35f, 0.65f));
            b2.transform.SetParent(parent, true);
        }

        // Warm haze band right at the treeline, echoing BattleStage's HorizonGlowBand trick —
        // the linear fog above reads too subtly at this camera's shallow viewing angle on its
        // own; a couple of soft HDR-tinted planes stacked at the far tree row's depth (z=-19,
        // just past the last far tree at z=-18) sell a much stronger "morning mist" without
        // touching the fog curve everything else already relies on.
        static void BuildMistBand(Transform parent, Color horizonColor)
        {
            (float y, float h, float boost)[] bands =
            {
                (0.3f, 1.4f, 1.15f),
                (1.2f, 1.2f, 0.9f),
            };
            foreach (var b in bands)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "MistBand";
                DestroySafe(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(0f, b.y, -19f);
                go.transform.localScale = new Vector3(20f, b.h, 1.2f);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = PropMeshes.MatUnlit(horizonColor * b.boost);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        // ---- Path: sand tiles running from the avatar/chair out toward the camera/foreground. ----
        // A constant real-world tile size stops well short of the camera on purpose: the visible
        // ground half-width shrinks to ~0.2-0.3m within ~1.2m of the lens (see the frustum note
        // above BuildTrees), so a 0.85m tile placed that close overflows the frame entirely —
        // that's what produced the giant clipped shards in an earlier pass. Ending the path at
        // Z=2.6 (~1.2m from the camera) keeps every tile inside a sane render budget while still
        // reading as "leads into the foreground".
        static void BuildPath(Transform parent)
        {
            const float tileSize = 0.55f;
            const float startZ = 0.30f, endZ = 2.60f;
            int count = Mathf.CeilToInt((endZ - startZ) / tileSize);
            for (int i = 0; i < count; i++)
            {
                float z = startZ + i * tileSize;
                Prop(parent, "ground_pathTile", new Vector3(0f, 0.01f, z), tileSize, byHeight: false);
            }
        }

        // ---- Fence: a shallow arc behind the avatar, facing the camera. ----
        // Radius/arc tuned against the camera's actual (narrow, portrait) frustum — see the
        // FrameHalfWidth note above BuildTrees. At radius 2.4/90 degrees the center strut sits
        // comfortably inside frame and the outer struts crop softly at the edges.
        static void BuildFence(Transform parent)
        {
            const float radius = 2.4f;
            const float segSize = 1.0f;
            const int count = 7;
            const float totalArcDeg = 90f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1) - 0.5f; // -0.5 .. 0.5
                float theta = t * totalArcDeg * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(theta) * radius, 0f, -Mathf.Cos(theta) * radius);
                float yawDeg = theta * Mathf.Rad2Deg;
                Prop(parent, "fence_simple", pos, segSize, byHeight: false, yaw: yawDeg);
            }
        }

        // ---- Trees: three depths for layered composition (near framing / mid / far). ----
        //
        // The scene camera sits at world Z=3.8 with a 42 degree VERTICAL FOV and a portrait
        // (927x1427) aspect, and is yawed 180 degrees to face the avatar — which mirrors world
        // +X onto screen-LEFT. That combination gives a much narrower horizontal FOV than a
        // landscape camera would (~28 degrees full angle), so props placed at "photographer's
        // intuition" offsets (5-10m to the side) land completely outside the frustum. The usable
        // half-width at a given depth is FrameHalfWidth(z) = (3.8 - z) * 0.2494 (0.2494 = tan of
        // the ~14 degree horizontal half-FOV). Every X below is chosen as a fraction of that
        // budget at its Z so the composition is actually visible: near pair at ~1.1x (intentionally
        // just past the edge, cropped), mid/far rows at ~0.8x (comfortably inside, spread across
        // the frame).
        struct TreePlacement
        {
            public string kind; public Vector3 pos; public float height; public float yaw;
            public TreePlacement(string k, float x, float z, float h, float yaw)
            { kind = k; pos = new Vector3(x, 0f, z); height = h; this.yaw = yaw; }
        }

        static readonly TreePlacement[] Trees =
        {
            // Near framing pair. Their canopies are ~2m in radius, so the trunk must sit at
            // half-width + most of that radius or the canopy walls off the whole top of frame
            // (that was the giant washed-out mass in an earlier screenshot) — at z=-3.5 the
            // half-width is 1.8m, so x=+/-3.0 leaves only the inner canopy edge cropping the
            // top corners.
            new TreePlacement("tree_oak",      -3.0f, -3.5f, 5.2f,  15f),
            new TreePlacement("tree_detailed",  3.0f, -3.5f, 5.0f, -20f),

            // Mid row — spread across the frame behind the fence, canopies kept off the
            // avatar's screen silhouette and the overhead conveyor band.
            new TreePlacement("tree_default",      -2.2f, -5.0f, 3.4f,  10f),
            new TreePlacement("tree_fat",            2.5f, -5.5f, 3.6f, -15f),
            new TreePlacement("tree_cone",          -2.6f, -7.0f, 3.4f,  25f),
            new TreePlacement("tree_default_fall",   2.9f, -8.0f, 3.5f, -10f),
            // Kept off the exact center axis: the HeaderZone glow ring renders at screen
            // center above the avatar, and a dead-center canopy cluttered that band.
            new TreePlacement("tree_oak",            1.5f, -11.0f, 3.7f,   5f),

            // Far row — small, atmospheric backdrop, widest spread (most half-width budget).
            new TreePlacement("tree_default",      -3.1f, -12.0f, 2.6f, 0f),
            new TreePlacement("tree_default_fall",  3.5f, -14.0f, 2.5f, 0f),
            new TreePlacement("tree_fat",           -3.9f, -16.0f, 2.7f, 0f),
            new TreePlacement("tree_cone",           4.3f, -18.0f, 2.4f, 0f),
        };

        static void BuildTrees(Transform parent)
        {
            foreach (var t in Trees)
            {
                var go = ScaledNature(t.kind, parent, t.height, byHeight: true);
                if (go == null)
                {
                    // Fallback to the procedural tree so the composition never goes empty.
                    go = PropMeshes.Tree();
                    go.transform.SetParent(parent, false);
                    go.transform.localScale = Vector3.one * (t.height / 2.5f);
                }
                go.transform.localPosition = t.pos;
                go.transform.localRotation = Quaternion.Euler(0f, t.yaw, 0f);
            }
        }

        // ---- Scatter dressing: hand-clustered near path edges and tree bases. ----
        struct ScatterPlacement
        {
            public string kind; public Vector3 pos; public float size; public bool byHeight; public float yaw;
            public ScatterPlacement(string k, float x, float z, float size, bool byHeight, float yaw = 0f)
            { kind = k; pos = new Vector3(x, 0f, z); this.size = size; this.byHeight = byHeight; this.yaw = yaw; }
        }

        // Same frustum-budget logic as the tree table: at shallow depth (near the path, close to
        // camera) FrameHalfWidth(z) is tiny, so path-edge dressing sits within ~0.5-0.8m of the
        // centerline, not the 2-3m a flat-ground guess would suggest. Tree-base clusters key off
        // the (now-corrected) mid-tree positions above instead of guessing independently.
        static readonly ScatterPlacement[] Scatter =
        {
            // Bushes — 4 hugging the path edges (kept 2.5m+ from the camera and small: a 0.55m
            // bush at 2.4m already spans a third of this narrow frame, which is what produced
            // the giant washed shapes in an earlier pass), 4 at mid-tree bases.
            new ScatterPlacement("plant_bush",         -0.65f,  0.40f, 0.45f, true),
            new ScatterPlacement("plant_bushDetailed",   0.68f,  0.30f, 0.42f, true),
            new ScatterPlacement("plant_bushLarge",     -0.60f,  1.10f, 0.42f, true),
            new ScatterPlacement("plant_bush",           0.62f,  1.20f, 0.38f, true),
            new ScatterPlacement("plant_bush",          -1.90f, -4.50f, 0.7f, true),
            new ScatterPlacement("plant_bushDetailed",   2.20f, -5.30f, 0.7f, true),
            new ScatterPlacement("plant_bushLarge",     -2.60f, -6.90f, 0.8f, true),
            new ScatterPlacement("plant_bush",           2.80f, -7.90f, 0.7f, true),

            // Flowers — small clusters flanking the path, plus a few at mid-tree bases.
            new ScatterPlacement("flower_purpleA", -0.35f,  0.30f, 0.22f, false),
            new ScatterPlacement("flower_redA",     0.36f,  0.35f, 0.23f, false),
            new ScatterPlacement("flower_purpleC", -0.40f,  0.70f, 0.21f, false),
            new ScatterPlacement("flower_redA",     0.42f,  0.75f, 0.22f, false),
            new ScatterPlacement("flower_purpleA", -0.48f,  1.05f, 0.20f, false),
            new ScatterPlacement("flower_redA",     0.50f,  1.10f, 0.20f, false),
            new ScatterPlacement("flower_purpleC", -2.00f, -4.30f, 0.22f, false),
            new ScatterPlacement("flower_redA",      2.15f, -5.25f, 0.22f, false),
            new ScatterPlacement("flower_purpleA", -2.50f, -6.85f, 0.22f, false),
            new ScatterPlacement("flower_redA",      2.70f, -7.85f, 0.22f, false),

            // Mushrooms — tucked beside the near bush clusters.
            new ScatterPlacement("mushroom_red", -0.52f,  0.85f, 0.18f, true),
            new ScatterPlacement("mushroom_red",  0.50f,  0.90f, 0.18f, true),
            new ScatterPlacement("mushroom_red", -1.85f, -4.70f, 0.24f, true),

            // Stones — path edge (small: they sit closest to the camera).
            new ScatterPlacement("stone_largeA", -0.50f,  1.00f, 0.30f, false),
            new ScatterPlacement("rock_largeA",   0.52f,  1.05f, 0.28f, false),

            // Log — near the fence.
            new ScatterPlacement("stump_round",   0.55f, -1.80f, 0.5f, false),
        };

        static void BuildScatter(Transform parent)
        {
            foreach (var s in Scatter)
            {
                var go = Prop(parent, s.kind, s.pos, s.size, s.byHeight, s.yaw);
                // Gentle breeze wobble on bushes/flowers — vertex-less rotation nudge, not a
                // shader or mesh change. Stones/mushrooms/logs stay rigid (nothing to sway).
                if (go != null && (s.kind.StartsWith("plant_") || s.kind.StartsWith("flower_")))
                {
                    var sway = go.AddComponent<GentleSway>();
                    sway.amplitudeDeg = s.kind.StartsWith("flower_") ? 4f : 2f;
                    sway.speed = Random.Range(0.6f, 1.1f);
                }
            }
        }

        // ---- Sky details: soft clouds + a warm sun disc. ----
        // The camera's pitch (10 degrees down) caps how high in Y anything can be and still sit
        // inside the top of frame: visible top world-Y at depth d is roughly 2.0 + d*tan(11deg).
        // The art direction's "y 8-12" reads as too high once you also need "distance 20-30" —
        // those two numbers aren't simultaneously visible with this camera, so height was pulled
        // down (~5-6.5) to actually land in frame; distance was kept in-spec. Everything also has
        // to stay inside the 60-radius sky dome or the dome's (opaque, inward-facing) shell
        // occludes it.
        static void BuildSky(Transform parent)
        {
            // Cloud scale is capped well below the art-direction-suggested sizes: a 3.5m puff
            // cluster at 22m spans ~30% of this narrow frame and reads as a white ceiling, not
            // a cloud (verified in an earlier screenshot).
            // y trimmed ~0.6-0.8 on each (same frame-clip math as the sun/halo below): their
            // puffy, irregular silhouette hid the clipping better than the sun's hard circle did,
            // but they were still losing their topmost puff past the frame edge.
            Cloud(parent, new Vector3(-3.2f, 5.0f, -20.2f), 1.7f);
            Cloud(parent, new Vector3(3.4f, 5.6f, -23.2f), 1.5f);
            Cloud(parent, new Vector3(0.6f, 5.8f, -25.2f), 2.0f);

            // Soft outer halo BEHIND the sun core (further z, bigger scale): the depth test lets
            // it show only in the ring outside the core's silhouette, so it reads as a gradient
            // glow aura even where the bloom kernel is too tight to spread convincingly at this
            // resolution — same idea as HeaderZone's glow ring, applied to the sky.
            var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "SunHalo";
            DestroySafe(halo.GetComponent<Collider>());
            halo.transform.SetParent(parent, false);
            // y dropped from 6.5: at this depth (d~24-25) the frame's top edge only reaches
            // world-Y ~6.6-6.9 (2.0 + d*tan(11deg) — see the frustum note above). The sun/halo's
            // own radius pushed their tops past that, so both were quietly clipped flat by the
            // frame edge in every earlier round — invisible while the sky was uniform cream, but
            // a glaring hard-edged cutoff now that real blue sky sits behind it. 4.9 keeps the
            // full disc (radius ~1.7-2.2) inside frame with margin.
            halo.transform.localPosition = new Vector3(0.4f, 4.9f, -24.6f);
            // Round 2's 6.5 scale filled almost the entire visible sky band (only an ~11-degree
            // elevation sliver is even in frame — see the frustum note above), crowding out the
            // blue zenith the sky-dome fix just made visible. Shrunk so the halo reads as a glow
            // AROUND the sun disc, not a second sky.
            halo.transform.localScale = new Vector3(4.4f, 4.4f, 1f);
            halo.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.MatUnlit(new Color(1.22f, 1.0f, 0.68f)); // just past bloom threshold — soft, not a second sun

            var sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "Sun";
            DestroySafe(sun.GetComponent<Collider>());
            sun.transform.SetParent(parent, false);
            // Recentered into the open gap between the two near-tree canopies (was x=1.8, which
            // sat right at the frustum's horizontal edge and partly behind the corner-cropping
            // near tree). x=0.4 keeps it slightly off the dead-center HeaderZone glow axis while
            // staying clear of both canopies.
            sun.transform.localPosition = new Vector3(0.4f, 4.9f, -24f);
            sun.transform.localScale = new Vector3(3.4f, 3.4f, 1f);
            // HDR-boosted past 1.0 on purpose: the bloom threshold (see CreatePostProfile) is
            // tuned so ONLY this disc — not the rest of the (all <=1 albedo) scene — glows.
            // Brightened from 3.2/2.9/2.2 so the disc actually separates from the now-visible
            // blue zenith instead of just matching the halo's own brightness.
            sun.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.MatUnlit(new Color(3.8f, 3.3f, 2.3f)); // HDR warm-white core
        }

        static readonly Vector3[] CloudOffsets =
        {
            new Vector3(0f, 0f, 0f), new Vector3(0.6f, 0.05f, 0.1f),
            new Vector3(-0.55f, 0.02f, -0.05f), new Vector3(0.15f, 0.15f, 0.2f),
        };
        static readonly float[] CloudPuffScales = { 1f, 0.75f, 0.7f, 0.55f };

        static void Cloud(Transform parent, Vector3 center, float scale)
        {
            var root = new GameObject("Cloud");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = center;
            // Slow drift so the sky doesn't read as a painted backdrop at runtime. No effect
            // on the single-frame builder screenshot, but the wrap radius is scaled so its
            // silhouette never re-enters frame from an odd angle.
            var drift = root.AddComponent<CloudDrift>();
            drift.speed = 0.12f;
            drift.range = 5f;
            // Brighter warm-white than plain Color.white so the puffs actually separate from the
            // (now much darker/bluer) zenith instead of reading washed-out and flat.
            var mat = PropMeshes.MatUnlit(new Color(1.08f, 1.05f, 0.98f));
            // The smallest puff in the cluster gets a warm gold rim tint — a cheap stand-in for
            // sunlight catching its topside edge, without per-puff shading.
            var rimMat = PropMeshes.MatUnlit(new Color(1.15f, 0.92f, 0.62f));
            for (int i = 0; i < CloudOffsets.Length; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Puff" + i;
                DestroySafe(puff.GetComponent<Collider>());
                puff.transform.SetParent(root.transform, false);
                puff.transform.localPosition = CloudOffsets[i] * scale;
                float s = scale * CloudPuffScales[i];
                puff.transform.localScale = new Vector3(s, s * 0.55f, s);
                puff.GetComponent<Renderer>().sharedMaterial = i == CloudOffsets.Length - 1 ? rimMat : mat;
            }
        }

        // ---- Chair recolor: PropMeshes.Chair() ships plain brown wood — retint + add a cushion. ----
        static void RecolorChair(GameObject chair)
        {
            var wood = LitMat(new Color(0.690f, 0.478f, 0.271f)); // #B07A45
            foreach (var r in chair.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = wood;

            var cushion = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cushion.name = "Cushion";
            DestroySafe(cushion.GetComponent<Collider>());
            cushion.transform.SetParent(chair.transform, false);
            cushion.transform.localPosition = new Vector3(0f, 0.49f, 0f);
            cushion.transform.localScale = new Vector3(0.42f, 0.06f, 0.42f);
            cushion.GetComponent<Renderer>().sharedMaterial = LitMat(new Color(0.851f, 0.373f, 0.306f)); // #D95F4E
        }

        // ---- Kenney prop helpers: load, measure, rescale to a real-world target size. ----

        /// <summary>Loads a Nature prop at native scale, places it, and rescales it uniformly so
        /// its measured height (or footprint) matches <paramref name="targetSize"/>. Returns null
        /// (already warned by KenneyProps) if the model is missing — callers decide on a fallback.</summary>
        static GameObject Prop(Transform parent, string kind, Vector3 pos, float targetSize, bool byHeight, float yaw = 0f)
        {
            var go = ScaledNature(kind, parent, targetSize, byHeight);
            if (go == null) return null;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        static GameObject ScaledNature(string kind, Transform parent, float targetSize, bool byHeight)
        {
            var go = KenneyProps.Nature(kind, parent, 1f);
            if (go == null) return null;
            RecolorNatureProp(go);
            float measured = MeasureSize(go, byHeight);
            if (measured > 0.0001f) go.transform.localScale *= targetSize / measured;
            return go;
        }

        // Kenney's Nature-kit FBX materials extract into Unity with the right slot NAMES
        // (woodBark, leafsGreen, grass, dirt, stone, colorPurple, ...) but the wrong COLORS —
        // Unity's FBX Diffuse->_BaseColor conversion double-gamma-corrects them into pale
        // pastels (verified: FBX leafsGreen (0.16,0.79,0.67) imports as (0.44,0.90,0.84), the
        // sqrt of the original — same story for every slot). Re-tinting by matching each slot's
        // NAME against a hand-picked "Morning Orchard" palette sidesteps that import bug and
        // doubles as our art-direction color pass. Longest/most specific keys are listed first
        // since matching is first-hit substring.
        static readonly (string key, Color color)[] NaturePalette =
        {
            ("leafsorange", new Color(0.85f, 0.55f, 0.25f)),
            ("leaforange",  new Color(0.85f, 0.55f, 0.25f)),
            ("leafsyellow", new Color(0.90f, 0.75f, 0.25f)),
            ("leafyellow",  new Color(0.90f, 0.75f, 0.25f)),
            ("leafsred",    new Color(0.75f, 0.33f, 0.25f)),
            ("leafred",     new Color(0.75f, 0.33f, 0.25f)),
            ("leaf",        new Color(0.33f, 0.58f, 0.24f)),
            ("woodbark",    new Color(0.45f, 0.30f, 0.18f)),
            ("wooddark",    new Color(0.55f, 0.40f, 0.25f)),
            ("bark",        new Color(0.45f, 0.30f, 0.18f)),
            ("trunk",       new Color(0.45f, 0.30f, 0.18f)),
            ("wood",        new Color(0.690f, 0.478f, 0.271f)), // matches the recolored chair (#B07A45)
            // Both a step darker than the #D9B380 StagePad so the path tiles read as distinct
            // pavers instead of merging into the pad.
            ("dirtdark",    new Color(0.62f, 0.47f, 0.30f)),
            ("dirt",        new Color(0.76f, 0.60f, 0.41f)),
            ("grass",       new Color(0.498f, 0.749f, 0.302f)), // #7FBF4D, matches the ground disc
            ("stone",       new Color(0.62f, 0.60f, 0.56f)),
            ("rock",        new Color(0.58f, 0.55f, 0.51f)),
            ("colorpurple", new Color(0.55f, 0.40f, 0.78f)),
            ("purple",      new Color(0.55f, 0.40f, 0.78f)),
            ("colorred",    new Color(0.82f, 0.28f, 0.28f)),
            ("colorpink",   new Color(0.90f, 0.55f, 0.65f)),
            ("pink",        new Color(0.90f, 0.55f, 0.65f)),
            ("red",         new Color(0.82f, 0.28f, 0.28f)),
            ("coloryellow", new Color(0.95f, 0.80f, 0.35f)),
            ("yellow",      new Color(0.95f, 0.80f, 0.35f)),
            ("colorwhite",  new Color(0.92f, 0.90f, 0.82f)),
            ("_defaultmat", new Color(0.92f, 0.90f, 0.82f)), // e.g. mushroom stems
            ("white",       new Color(0.92f, 0.90f, 0.82f)),
        };

        static readonly Dictionary<string, Material> s_NatureMatCache = new Dictionary<string, Material>();

        /// <summary>
        /// Plain URP Lit material: _BaseColor + _Smoothness only, no keyword changes.
        /// PropMeshes.Mat additionally force-disables environment reflections and specular
        /// highlights via shader keywords, and in this project's batchmode renders every such
        /// material as uniform brick red regardless of its _BaseColor (the avatar's imported
        /// materials — no such keywords — render fine, which is how this was isolated). So the
        /// stage builds its own vanilla Lit materials instead.
        /// </summary>
        static Material LitMat(Color c, float smooth = 0.25f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            return mat;
        }

        static void RecolorNatureProp(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string srcName = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                    if (!s_NatureMatCache.TryGetValue(srcName, out var mat))
                    {
                        Color color = new Color(0.55f, 0.60f, 0.45f); // neutral fallback
                        foreach (var (key, c) in NaturePalette)
                            if (srcName.Contains(key)) { color = c; break; }
                        mat = LitMat(color);
                        s_NatureMatCache[srcName] = mat;
                    }
                    mats[i] = mat;
                }
                r.sharedMaterials = mats;
            }
        }

        static float MeasureSize(GameObject go, bool byHeight)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
        }

        static void Disc(Transform parent, string name, Vector3 pos, Vector3 scale, Color color, bool lit = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial =
                lit ? LitMat(color, smooth: 0f) : PropMeshes.MatUnlit(color);
        }

        static void Hill(Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Hill";
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = LitMat(color, smooth: 0.2f);
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        // Self-contained particle system (own material, not KinexFx.ParticleMaterial — that
        // helper is private to KinexFx and this stage doesn't own that file) — a handful of
        // leaves drifting down with gravity + gentle spin. Kept separate from AmbientMotes so
        // the two effects can be tuned independently.
        static GameObject BuildFallingLeaves(Vector3 center, Vector3 boxSize)
        {
            var go = new GameObject("FallingLeaves");
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            renderer.sharedMaterial = new Material(shader);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new Color(0.82f, 0.55f, 0.22f, 0.9f); // autumn-orange, distinct from green canopy
            main.gravityModifier = 0.06f; // slow, floaty fall — not a heavy drop
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20;

            var emission = ps.emission;
            emission.rateOverTime = 1f; // background detail, not a flurry

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-90f, 90f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.2f;
            noise.frequency = 0.15f;

            ps.Play();
            return go;
        }
    }
}
