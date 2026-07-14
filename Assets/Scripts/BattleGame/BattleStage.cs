using UnityEngine;
using Kinex.FX;

namespace Kinex.BattleGame
{
    /// <summary>
    /// Runtime-built floating-island battle arena. One shared composition (island disc, sky
    /// dome, ambient motes) redressed per level with a distinct palette + props: ทุ่งหญ้า Meadow
    /// (dawn warm, Kenney nature props), ถ้ำคริสตัล Crystal Cave (teal/purple glowing "crystals" —
    /// tinted emissive rocks), ปราสาทเมฆ Sky Castle (sunset gold/pink, drifting cloud blobs).
    /// No scene/prefab authoring — Awake() builds everything as children of this transform, same
    /// philosophy as Kinex.FruitGame.FruitStage / Kinex.BalanceQuest.QuestStage.
    /// </summary>
    public class BattleStage : MonoBehaviour
    {
        [Range(1, 3)] public int level = 1;

        readonly struct Palette
        {
            public readonly Color skyTop, skyHorizon, ambientSky, ambientEquator, ambientGround, groundTint, moteColor, fogColor;
            public Palette(Color skyTop, Color skyHorizon, Color ambientSky, Color ambientEquator,
                           Color ambientGround, Color groundTint, Color moteColor, Color fogColor)
            {
                this.skyTop = skyTop; this.skyHorizon = skyHorizon;
                this.ambientSky = ambientSky; this.ambientEquator = ambientEquator; this.ambientGround = ambientGround;
                this.groundTint = groundTint; this.moteColor = moteColor; this.fogColor = fogColor;
            }
        }

        static Palette PaletteFor(int level) => level switch
        {
            // Meadow Dawn: pushed toward a genuine sunrise read — saturated warm-orange horizon
            // vs. a deep cool blue zenith (the sky dome's own gradient only survives partially
            // once fog blends it, see BuildStage's dome-radius comment, so the two ends need to
            // start further apart than a "normal" flat-lit palette would use), plus a dusty
            // rose-tan fog that reads as dawn haze without dragging the whole scene warm OR cool.
            1 => new Palette(
                skyTop: new Color(0.22f, 0.34f, 0.56f), skyHorizon: new Color(1f, 0.62f, 0.34f),
                ambientSky: new Color(0.4f, 0.46f, 0.6f), ambientEquator: new Color(0.85f, 0.55f, 0.38f), ambientGround: new Color(0.3f, 0.34f, 0.2f),
                groundTint: new Color(0.32f, 0.5f, 0.28f), moteColor: new Color(1f, 0.85f, 0.5f, 0.55f), fogColor: new Color(0.85f, 0.68f, 0.62f)),
            2 => new Palette(
                skyTop: new Color(0.08f, 0.06f, 0.22f), skyHorizon: new Color(0.35f, 0.15f, 0.5f),
                ambientSky: new Color(0.35f, 0.4f, 0.6f), ambientEquator: new Color(0.4f, 0.3f, 0.55f), ambientGround: new Color(0.1f, 0.15f, 0.25f),
                groundTint: new Color(0.2f, 0.3f, 0.45f), moteColor: new Color(0.4f, 0.85f, 0.95f, 0.75f), fogColor: new Color(0.18f, 0.1f, 0.3f)),
            _ => new Palette(
                skyTop: new Color(0.55f, 0.35f, 0.55f), skyHorizon: new Color(1f, 0.65f, 0.55f),
                ambientSky: new Color(0.7f, 0.55f, 0.6f), ambientEquator: new Color(0.85f, 0.55f, 0.6f), ambientGround: new Color(0.4f, 0.3f, 0.4f),
                groundTint: new Color(0.74f, 0.62f, 0.76f), moteColor: new Color(1f, 0.85f, 0.75f, 0.75f), fogColor: new Color(0.85f, 0.6f, 0.62f)),
        };

        void Awake() => BuildStage(transform, level);

        public static void BuildStage(Transform parent, int level)
        {
            var p = PaletteFor(level);

            // Radius shrunk from an earlier 55 -> 14: this scene's fog (below) has to start close
            // (the whole playable arena is only ~9m deep) to give the ground/props any depth cue
            // at all, and linear fog fraction only depends on DISTANCE — a dome that far out sat
            // entirely past fogEnd and rendered as one flat fog-colored wash, throwing away the
            // whole warm-horizon/cool-zenith gradient (round 2 tried 26: the apex still landed at
            // ~100% fog and the upper sky read rose-gray instead of cool blue). At 14 the apex is
            // only ~40% fogged, so the zenith blue actually survives to the screen.
            var sky = ProceduralSkybox.CreateSkyDome(p.skyTop, p.skyHorizon, radius: 14f);
            sky.transform.SetParent(parent, false);
            // Squash the dome vertically: this portrait camera only sees elevations ~0-21deg, so
            // on a full hemisphere the visible wedge covers just v 0-0.25 of the gradient and the
            // cool zenith color NEVER reaches the screen (the flat-beige upper frame in every
            // early render). At 0.4x height the top of frame lands around v~0.7 of the gradient —
            // an actual warm-to-cool dawn sweep. 0.4 (not lower) keeps the tallest props (trees
            // ~4m, level-3 towers ~4.2m) inside the shell so they aren't occluded by it.
            sky.transform.localScale = new Vector3(1f, 0.4f, 1f);
            ProceduralSkybox.SetAmbient(p.ambientSky, p.ambientEquator, p.ambientGround);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogStartDistance = 5f;
            RenderSettings.fogEndDistance = 26f;

            BuildIsland(parent, p.groundTint);
            BuildProps(parent, level);
            BuildHorizonGlow(parent, p.skyHorizon);

            // Ambient dust/pollen motes drifting in front of the avatar. KinexFx.AmbientMotes only
            // Play()s the system — that doesn't advance a ParticleSystem outside Play mode, so the
            // batchmode/edit-mode screenshot render below always caught it before any particle had
            // spawned (invisible in the graded screenshot). Same fix QuestStage's StarField uses.
            // Box kept low/close (y 0.6-2.2, z -1..3) so motes stay a foreground-near-the-avatar
            // cue rather than freckling the whole sky backdrop — a wider box read as background
            // clutter competing with the horizon glow in an earlier pass.
            var motes = KinexFx.AmbientMotes(new Vector3(0f, 1.4f, 1f), new Vector3(4f, 1.6f, 4f), p.moteColor, rate: 7);
            motes.transform.SetParent(parent, false);
            // AmbientMotes' default 3-8cm particles rendered as chunky squares at this camera's
            // close distance — shrink them to true dust-glint scale.
            var motesMain = motes.main;
            motesMain.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            if (!Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                motes.Simulate(2f, true, true);

            // The dome's rim sits at world Y=0 while the camera eye sits above that, so the
            // camera's own solid-color clear (visible in the sliver between the rim and the
            // ground) must track whichever level is actually selected — a value baked once at
            // scene-build time would only be right for that one level.
            var cam = Camera.main;
            if (cam != null && cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = p.skyHorizon;
        }

        // Flattened disc floating in the sky dome, smoothness 0 so URP Lit picks up real cast
        // shadows correctly (see FruitStageBuilder's ground fix). Two concentric tones (bright
        // inner field, darker outer ring) instead of one flat color — same trick
        // FruitStageBuilder's GroundOuter/Ground pair uses — so the ground reads as a gradient
        // instead of a flat solid green even though this camera's shallow angle mostly shows the
        // top face rather than a side profile. Plus a thin glowing rim ring and a soft under-glow
        // so the "floating" read is obvious.
        static void BuildIsland(Transform parent, Color tint)
        {
            var outer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            outer.name = "FloatingIslandOuter";
            DestroyColliderSafe(outer);
            outer.transform.SetParent(parent, false);
            outer.transform.position = new Vector3(0f, -0.16f, 0f);
            // 0.82, not lower: ACES tonemapping crushes dark greens hard, and an earlier 0.55-0.65
            // ring rendered as a near-black band across the whole horizon instead of the intended
            // subtle edge darkening.
            outer.transform.localScale = new Vector3(18f, 0.3f, 18f);
            outer.GetComponent<Renderer>().sharedMaterial = PropMeshes.Mat(tint * 0.82f, smooth: 0f);

            // Kept small (radius 3) on purpose — sized so the bright/dark transition falls in the
            // MIDGROUND (around where the mid-band rocks/bushes sit, z~3), not hidden right at the
            // horizon line under everything else. Only a 1cm step down to the outer ring (matches
            // FruitStageBuilder's Ground/GroundOuter offset) — a wider gap read as a shadowed
            // crease line right across the horizon in an earlier pass.
            var island = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            island.name = "FloatingIsland";
            DestroyColliderSafe(island);
            island.transform.SetParent(parent, false);
            island.transform.position = new Vector3(0f, -0.15f, 0f);
            island.transform.localScale = new Vector3(6f, 0.3f, 6f);
            island.GetComponent<Renderer>().sharedMaterial = PropMeshes.Mat(tint, smooth: 0f);

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "IslandRim";
            DestroyColliderSafe(rim);
            rim.transform.SetParent(parent, false);
            rim.transform.position = new Vector3(0f, -0.34f, 0f);
            rim.transform.localScale = new Vector3(18.3f, 0.12f, 18.3f);
            rim.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(tint * 0.55f, smooth: 0.2f, emission: tint * 0.3f);

            var underGlow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            underGlow.name = "IslandUnderGlow";
            DestroyColliderSafe(underGlow);
            underGlow.transform.SetParent(parent, false);
            underGlow.transform.position = new Vector3(0f, -3.5f, 0f);
            underGlow.transform.localScale = new Vector3(7f, 5f, 7f);
            underGlow.GetComponent<Renderer>().sharedMaterial = PropMeshes.MatUnlit(tint * 0.5f);
            var r = underGlow.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // HDR-boosted bands stacked right at the sky dome's rim, echoing QuestStage's
        // HorizonGlowBand trick: with fog tuned tight enough to give the ground/props real depth
        // (see BuildStage), the dome itself sits mostly beyond fogEnd and reads as a near-flat
        // wash — this is what actually sells "warm glow at the horizon" regardless of what
        // survives of the dome's own gradient, and it also hides the dome's hard rim seam.
        static void BuildHorizonGlow(Transform parent, Color horizonColor)
        {
            // z=10 keeps the bands INSIDE the radius-14 dome (beyond it the dome shell occludes
            // them). The lowest band dips to y=-0.4 so its bottom edge hides behind the ground
            // disc's far silhouette from the camera's eye height — otherwise a sliver of the
            // camera clear color peeks between ground edge and glow.
            (float y, float h, float boost)[] bands =
            {
                (0.0f, 0.8f, 2.0f),
                (0.7f, 0.7f, 1.4f),
                (1.5f, 0.8f, 0.95f),
            };
            foreach (var b in bands)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "HorizonGlowBand";
                DestroyColliderSafe(go);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(0f, b.y, 10f);
                go.transform.localScale = new Vector3(26f, b.h, 1.5f);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = PropMeshes.MatUnlit(horizonColor * b.boost);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        static void BuildProps(Transform parent, int level)
        {
            switch (level)
            {
                case 1: BuildMeadowProps(parent); break;
                case 2: BuildCrystalCaveProps(parent); break;
                default: BuildSkyCastleProps(parent); break;
            }
        }

        // Camera sits close (~3.9m back, see BattleGameSceneBuilder.FrameCameraOnAvatar: avatar
        // height 1.7m * -2.3 offset) with a narrow portrait FOV (46 deg vertical, 927x1427
        // aspect), so a wide prop ring sized for a top-down view falls WAY outside the visible
        // frustum and simply never renders (round 1's bug — nothing but flat ground showed).
        // Round 2 tried close cropped foreground trees (FruitStageBuilder's trick) but at this
        // camera's distance that put the camera INSIDE the scaled-up canopy mesh — big flat
        // facets filling the corners, not a tree silhouette. Every entry's x below is chosen as a
        // fraction of the same frustum-budget formula FruitStageBuilder documents for its own
        // trees — visible half-width at depth z is (z - camZ) * tan(halfHorizontalFOV), camZ≈-3.9,
        // tan(halfHorizontalFOV)≈0.276 for this camera's 46deg-vertical/927x1427 setup — so a
        // denser layout still stays actually visible instead of falling off both edges.
        static void BuildMeadowProps(Transform parent)
        {
            (string name, float x, float z, float scale)[] ring =
            {
                // Near band (z 1.6-2.4): low clutter framing the avatar's feet — kept off the
                // center so the avatar silhouette stays clean.
                ("flower_purpleA",   -0.55f, 1.6f, 1.0f), ("flower_redA",       0.58f, 1.65f, 1.0f),
                ("mushroom_red",     -0.85f, 1.9f, 0.9f), ("grass_large",       0.9f,  1.95f, 1.0f),
                ("plant_bush",       -1.25f, 2.2f, 1.3f), ("plant_bushLarge",   1.3f,  2.2f, 1.4f),
                ("flower_purpleC",   -0.62f, 2.0f, 1.0f), ("flower_redA",       0.65f, 2.05f, 1.0f),

                // Mid band (z 3.0-3.8): rocks + a second bush row, spread wider using the
                // frustum budget at this depth (~1.9m half-width).
                ("stone_largeA",     -1.55f, 3.0f, 1.2f), ("rock_largeA",       1.6f,  3.1f, 1.2f),
                ("plant_bushDetailed", -1.85f, 3.5f, 1.3f), ("stump_round",     1.9f,  3.6f, 1.1f),
                ("mushroom_red",      0.15f, 3.3f, 0.8f),

                // Far band (z 4.2-4.8): the tree row, framing the top corners (~2.3m half-width,
                // trees intentionally sit near that edge so their canopies crop softly).
                ("tree_default", -2.1f, 4.4f, 1.6f), ("tree_oak", 2.2f, 4.6f, 1.6f),
                ("tree_fat",     -1.15f, 4.7f, 1.3f), ("tree_cone", 1.2f, 4.85f, 1.3f),
                ("rock_largeA",  -0.5f, 4.5f, 0.9f), ("stone_largeA", 0.5f, 4.55f, 0.9f),
            };
            foreach (var (name, x, z, scale) in ring)
            {
                var go = KenneyProps.Nature(name, parent, scale);
                if (go != null) go.transform.localPosition = new Vector3(x, 0f, z);
            }

            // Backdrop band (z 6.5-7.4): small far silhouettes for depth — fog (see BuildStage)
            // fades these toward the horizon haze rather than hard-cutting. Shadows OFF: these
            // sit right at the ground's darker outer ring, and every backdrop tree casting a real
            // shadow that far out stacked into one solid dark band across the whole horizon (the
            // actual cause of an earlier round's "muddy line" — not the sun angle, which is what
            // round 2 wrongly chased).
            (string name, float x, float z, float scale)[] backdrop =
            {
                ("tree_default", -2.6f, 6.8f, 1.1f), ("tree_cone", 2.7f, 7.0f, 1.1f),
                ("tree_oak",      1.8f, 7.4f, 1.0f),
            };
            foreach (var (name, x, z, scale) in backdrop)
            {
                var go = KenneyProps.Nature(name, parent, scale);
                if (go == null) continue;
                go.transform.localPosition = new Vector3(x, 0f, z);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static void BuildCrystalCaveProps(Transform parent)
        {
            // No dedicated crystal asset — tinted, emissive Kenney rocks read as glowing shards.
            // cliff_top_rock is NOT usable here: seen from this camera it renders as one giant
            // flat quad filling the frame behind the avatar (verified in a level-2 preview) —
            // stick to the chunky rock/stone models at varied scales instead.
            Color[] crystalTints = { new Color(0.35f, 0.85f, 0.95f), new Color(0.65f, 0.35f, 0.95f) };
            (string name, float x, float z, float scale, int tint)[] ring =
            {
                ("rock_largeA", -1.0f, 1.3f, 1.0f, 0), ("stone_largeA", 1.0f, 1.4f, 1.0f, 1),
                ("rock_largeA", -1.3f, 2.6f, 1.3f, 1), ("stone_largeA", 1.3f, 2.7f, 1.3f, 0),
                ("rock_largeA", -0.65f, 4.8f, 1.7f, 0), ("stone_largeA", 0.75f, 5.0f, 1.5f, 1),
                ("rock_largeA", 2.1f, 4.2f, 1.2f, 0), ("stone_largeA", -2.05f, 4.1f, 1.2f, 1),
            };
            foreach (var (name, x, z, scale, tintIdx) in ring)
            {
                var go = KenneyProps.Nature(name, parent, scale);
                if (go == null) continue;
                go.transform.localPosition = new Vector3(x, 0f, z);
                var tint = crystalTints[tintIdx];
                // Emission kept just UNDER the bloom threshold (1.1): at 1.3x the shards bloomed
                // out to near-white slabs and lost the cyan/purple identity entirely — 0.85x
                // reads as saturated inner glow instead. Replace EVERY material slot — these
                // models have multi-slot renderers, and assigning .sharedMaterial only swapped
                // slot 0, leaving pastel green/pink Kenney colors on the rest (verified in a
                // level-2 preview).
                var crystalMat = PropMeshes.Mat(tint * 0.45f, metallic: 0.3f, smooth: 0.8f, emission: tint * 0.85f);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = crystalMat;
                    r.sharedMaterials = mats;
                }
            }
        }

        static void BuildSkyCastleProps(Transform parent)
        {
            // Drifting cloud blobs (flattened stretched spheres) resting ON the island surface
            // like mist banks, tinted warm to match the sunset dome, each with a slow independent
            // drift. Y raised from the original below-rim ring (-0.5..-1.3): once the ground disc
            // grew to radius 9 those all sat fully UNDER it — a level-3 preview showed no clouds
            // at all.
            (float x, float y, float z, float scale)[] clouds =
            {
                (-1.7f, 0.15f, 1.2f, 1.3f), (1.8f, 0.1f, 1.5f, 1.5f),
                (-1.6f, 0.25f, 3.4f, 1.1f), (1.7f, 0.2f, 3.6f, 1.3f),
                (0f, 0.3f, 5.8f, 1.8f), (-2.4f, 0.2f, 5.2f, 1.4f), (2.5f, 0.25f, 5.4f, 1.4f),
            };
            var mat = PropMeshes.MatUnlit(new Color(1f, 0.92f, 0.85f));
            foreach (var (x, y, z, scale) in clouds)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "CloudBlob";
                DestroyColliderSafe(go);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(x, y, z);
                go.transform.localScale = new Vector3(scale * 1.6f, scale * 0.7f, scale * 1.4f);
                go.GetComponent<Renderer>().sharedMaterial = mat;
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var drift = go.AddComponent<CloudDrift>();
                drift.speed = Random.Range(0.08f, 0.18f);
                drift.range = 3f;
            }

            // Simple pillar silhouettes suggest distant castle towers — each capped with a wider
            // short drum + a small dome so they read as turrets rather than bare cylinders.
            var pillarMat = PropMeshes.Mat(new Color(0.62f, 0.48f, 0.58f), smooth: 0.3f);
            (float x, float z, float h)[] towers = { (-2.2f, 7.0f, 4.2f), (2.3f, 7.2f, 3.6f) };
            foreach (var (x, z, h) in towers)
            {
                var tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tower.name = "CastleTower";
                DestroyColliderSafe(tower);
                tower.transform.SetParent(parent, false);
                tower.transform.localPosition = new Vector3(x, h * 0.5f - 0.15f, z);
                tower.transform.localScale = new Vector3(0.5f, h * 0.5f, 0.5f);
                tower.GetComponent<Renderer>().sharedMaterial = pillarMat;

                var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cap.name = "CastleTowerCap";
                DestroyColliderSafe(cap);
                cap.transform.SetParent(parent, false);
                cap.transform.localPosition = new Vector3(x, h - 0.15f, z);
                cap.transform.localScale = new Vector3(0.72f, 0.12f, 0.72f);
                cap.GetComponent<Renderer>().sharedMaterial = pillarMat;

                var domeTop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                domeTop.name = "CastleTowerDome";
                DestroyColliderSafe(domeTop);
                domeTop.transform.SetParent(parent, false);
                domeTop.transform.localPosition = new Vector3(x, h + 0.05f, z);
                domeTop.transform.localScale = new Vector3(0.55f, 0.5f, 0.55f);
                domeTop.GetComponent<Renderer>().sharedMaterial =
                    PropMeshes.Mat(new Color(0.85f, 0.45f, 0.5f), smooth: 0.4f);
            }
        }

        // Works both at runtime (Destroy) and in the edit-mode preview render the scene builder
        // uses (DestroyImmediate) — same split PropMeshes.DestroySafe uses.
        static void DestroyColliderSafe(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }
    }
}
