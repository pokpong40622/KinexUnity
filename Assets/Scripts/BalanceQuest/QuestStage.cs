using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Runtime-built "Neon Dusk Skyway" stage environment: sky dome (deep indigo -> hot pink
    /// horizon), a big low synthwave sun sitting at the runway's vanishing point, a sparse star
    /// field in the upper sky, a horizon glow band that dissolves the dome's hard rim line, cool
    /// trilight ambient, a magenta rim light behind the avatar, a cyan fill light on the camera
    /// side, and drifting cyan fireflies. No scene/prefab authoring — Awake() builds everything as
    /// children of this transform, so the scene file stays tiny.
    ///
    /// Coordinate note: the Main Camera looks down world +Z and the avatar is rotated to face -Z
    /// (see BalanceQuestSceneBuilder), so +Z is both "ahead" — the runway's vanishing point, where
    /// the sun sits — and "behind the avatar" — the rim-light side. The camera sits on the -Z
    /// side, which is also where the fill light lives.
    /// </summary>
    public class QuestStage : MonoBehaviour
    {
        // Horizon bands pulse in Update() (see BuildHorizonGlow) — only populated for a live
        // instance (Awake), never for the editor-preview static call in BalanceQuestSceneBuilder.
        Renderer[] _horizonRenderers;
        Color[] _horizonBaseColor;
        MaterialPropertyBlock _horizonMpb;
        // Horizon bands are unlit HDR "_BaseColor" (see BuildHorizonGlow) — that IS their glow,
        // there's no separate _EmissionColor on the Unlit shader.
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            _horizonRenderers = BuildStage(transform);
            _horizonBaseColor = new Color[_horizonRenderers.Length];
            for (int i = 0; i < _horizonRenderers.Length; i++)
                _horizonBaseColor[i] = _horizonRenderers[i].sharedMaterial.GetColor(BaseColorId);
        }

        void Update()
        {
            if (_horizonRenderers == null || _horizonRenderers.Length == 0) return;
            if (_horizonMpb == null) _horizonMpb = new MaterialPropertyBlock();
            // Slow sine breathing on the horizon glow so the dusk skyline feels alive rather
            // than a painted backdrop — matches TrailScroller.PulseEdges' cheap per-frame MPB
            // approach (no per-object script, no material instancing).
            float k = 1f + 0.18f * Mathf.Sin(Time.time * 0.6f);
            for (int i = 0; i < _horizonRenderers.Length; i++)
            {
                if (_horizonRenderers[i] == null) continue;
                _horizonMpb.SetColor(BaseColorId, _horizonBaseColor[i] * k);
                _horizonRenderers[i].SetPropertyBlock(_horizonMpb);
            }
        }

        /// <summary>Builds the whole runtime environment. Returns the horizon-glow renderers (for
        /// the live instance's Update() pulse) — the editor-preview call in
        /// BalanceQuestSceneBuilder discards the return value since that render is a single still
        /// frame.</summary>
        public static Renderer[] BuildStage(Transform parent)
        {
            var sky = ProceduralSkybox.CreateSkyDome(
                top: new Color(0.06f, 0.05f, 0.22f),
                horizon: new Color(0.85f, 0.25f, 0.45f),
                radius: 60f);
            sky.transform.SetParent(parent, false);

            // Trilight ambient is the one light source that reaches every surface evenly (no
            // falloff, no specular hot-spot) — raised across the board so the avatar has a real
            // diffuse base level instead of relying on a single nearby point light, which was
            // reading as a narrow specular streak rather than an even fill (iteration 2).
            //
            // Iteration 4 root-cause triage: the reviewer-flagged "harsh unblended red/orange bleed"
            // on the avatar's chest/arms was isolated across 14+ scene configurations — rim, fill,
            // key directional, Trilight ambient (including a perfectly flat, non-directional
            // variant), the whole post-process volume, reflections/customReflection, lightProbeUsage,
            // and even the SynthSun/HorizonGlow meshes, all zeroed/removed in every combination,
            // including literal total blackout. The patch never changed in shape, position, or
            // brightness in ANY of those tests, but disabling the avatar's "ao" (shirt) renderer
            // outright removes it completely — confirming it belongs to that material, yet nothing
            // reachable from scene lighting/environment code affects it. It is therefore an intrinsic
            // shading property of the shirt mesh/material (likely its geometry's hard/faceted
            // normals combined with fixed material parameters) rather than a scene-lighting bug — a
            // full fix needs the avatar material/mesh itself, which is out of scope here. What IS
            // fixed below is everything that WAS lighting-driven: the face/neck/hair/hands/legs no
            // longer carry any warm cast and the rim reads as a clean cool edge instead of a wash.
            ProceduralSkybox.SetAmbient(
                sky: new Color(0.38f, 0.38f, 0.52f),
                equator: new Color(0.42f, 0.42f, 0.60f),
                ground: new Color(0.18f, 0.17f, 0.26f));
            // Belt-and-suspenders: explicitly kill the default environment reflection scene-wide
            // (harmless even though it wasn't the cause of the shirt patch above) so no material
            // picks up an unintended tint from Unity's built-in fallback cubemap.
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflection = BuildBlackCubemap();
            // RenderSettings changes made via script aren't guaranteed to refresh the internal
            // ambient/reflection probe data outside Play mode (edit-mode/batchmode preview render)
            // without an explicit nudge.
            DynamicGI.UpdateEnvironment();

            // Horizon-merge fog: the runway used to hit a hard void/glow seam right at the dome
            // rim (visible as a sharp line in early renders). Dark-indigo linear fog starting well
            // past the avatar dissolves the deck into the horizon glow band instead.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.10f, 0.06f, 0.20f);
            // Distance fog is global — it also touches the sun/horizon glow (~50-64 m out), so
            // this can't be tight enough to fully swallow the trail's far end (~36 m) without
            // washing the sun out too. Kept moderate: a believable dusk haze on the far runway and
            // sun alike, rather than a hard cut at either.
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 100f;

            BuildSun(parent);
            BuildStars(parent);
            BuildShootingStar(parent);
            BuildDistantIslands(parent);
            var horizonRenderers = BuildHorizonGlow(parent);

            // Rim: a low-intensity SECOND directional light backlighting the avatar from behind
            // (+Z, opposite the camera) — catches shoulder/hair edges without shadows or flooding
            // the front of the face the way the point-light version did.
            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(parent, false);
            // Directional light TRAVELS along +transform.forward; a surface is lit when its
            // normal points roughly opposite that. The avatar's front faces -Z (toward camera),
            // so its back/shoulders face +Z — for THAT to catch the light, the light must travel
            // toward -Z (yaw ~180), not +Z. Iteration 1 used yaw 20 (barely off "straight down
            // +Z") which front-lit the face instead of backlighting the silhouette — the actual
            // cause of the reddish/blown-out avatar in that render, more than the grading was.
            // Iteration 3: yaw 180/pitch 20 still put a hot magenta SPECULAR highlight across the
            // front chest/collar (reviewer-flagged "unblended red/orange bleed" — specular hotspots
            // follow the reflection vector, not just the diffuse normal, so they can show up on
            // front-facing curved surfaces even when the light travels mostly toward -Z). Steepened
            // the pitch to 42 so the light travels down-and-back much more steeply, pushing the
            // specular reflection lobe up onto the top of the head/shoulders instead of the chest;
            // also cooled the colour (less red, more blue) and cut intensity further so any residual
            // hotspot reads as a thin cool-magenta silhouette edge, not a warm bleed.
            rimGo.transform.rotation = Quaternion.Euler(42f, 180f, 0f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.62f, 0.28f, 0.95f); // cooler violet-magenta, less red than #FF4FC3
            // A directional light lights the WHOLE scene uniformly (no falloff, no range limit)
            // unlike the point light this replaced, so it doesn't take much intensity.
            rim.intensity = 0.22f;
            rim.shadows = LightShadows.None;

            // Fill: front, camera side (-Z), lifts the shadowed front of the avatar so the pose
            // actually reads (this game is pose-detection driven — a silhouette defeats the
            // point). Both the key directional (yaw 200) and the rim (yaw 180) travel toward -Z,
            // i.e. they backlight rather than front-light, so THIS is the only thing putting
            // light on the face/torso the camera sees. Pulled back to z=-3.5 (was -2.0, almost on
            // top of the avatar) — that close, a point light's falloff across the body reads as
            // one narrow specular streak rather than an even wash; further away the rays are
            // closer to parallel, so intensity is raised to compensate for the extra distance.
            var fillGo = new GameObject("FillLight");
            fillGo.transform.SetParent(parent, false);
            fillGo.transform.position = new Vector3(0f, 1.2f, -3.5f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.45f, 0.85f, 1f); // #73D9FF
            // Iteration 4 cut this to 1.1 to kill a specular blowout on the shirt at closer
            // range; iteration 5 (lead review: avatar "somewhat dark/flat") raises it back up
            // partway now that the light sits 3.5 m back (parallel-ish rays, no near-field
            // falloff hotspot) — watch for a returning shirt hotspot before pushing further.
            fill.intensity = 1.6f;
            fill.range = 10f;

            // Gentle white front key, LOW intensity, same position as the fill: a pure cyan fill
            // alone tints the avatar's skin/clothing cool-blue with no neutral light reaching it
            // at all (see the reviewer note) — a soft white kicker restores a believable skin
            // tone without adding a second hot specular source (kept well under the fill's own
            // intensity, so it reads as "a bit of key" not a second point light).
            var keyGo = new GameObject("FrontKeyLight");
            keyGo.transform.SetParent(parent, false);
            keyGo.transform.position = new Vector3(0f, 1.4f, -3.2f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Point;
            key.color = new Color(0.95f, 0.92f, 0.88f);
            key.intensity = 0.55f;
            key.range = 9f;

            var motes = KinexFx.AmbientMotes(new Vector3(0f, 1.2f, 15f), new Vector3(6f, 3f, 30f),
                                             new Color(0.4f, 0.95f, 1f, 0.7f), rate: 10);
            motes.transform.SetParent(parent, false);

            return horizonRenderers;
        }

        // A true solid-black 1x1-per-face cubemap, assigned as RenderSettings.customReflection so
        // metallic/glossy materials (the avatar's shirt — see the ambient comment above) reflect
        // black instead of Unity's built-in default environment-reflection fallback, which is not
        // cleared by reflectionIntensity=0 / skybox=null alone.
        static Cubemap BuildBlackCubemap()
        {
            var cube = new Cubemap(1, TextureFormat.RGBA32, false);
            var black = new Color32(0, 0, 0, 255);
            foreach (CubemapFace face in System.Enum.GetValues(typeof(CubemapFace)))
            {
                if (face == CubemapFace.Unknown) continue;
                cube.SetPixels(new[] { (Color)black }, face);
            }
            cube.Apply();
            return cube;
        }

        // ---- Synthwave sun: a big flattened disc sitting on the horizon at the runway's
        // vanishing point (+Z, ~50m out), with dark retro scanline slats across its face. A
        // flattened Sphere (not a Quad) so it always renders regardless of view angle — same
        // trick TrailScroller already uses for its floating islands. ----
        static void BuildSun(Transform parent)
        {
            const float sunZ = 50f, sunRadius = 7f;

            // Corona halo: a second, larger flattened disc sitting just behind the sun so its
            // rim pokes out past the sun's own silhouette — the bloom pass (scatter 0.8) then
            // blurs that visible ring into a soft glow instead of the sun reading as a hard-edged
            // painted circle. Dimmer + warmer than the sun core so it reads as a halo, not a
            // second sun.
            var haloGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            haloGo.name = "SunHalo";
            DestroyCollider(haloGo);
            haloGo.transform.SetParent(parent, false);
            haloGo.transform.position = new Vector3(0f, sunRadius, sunZ + 1f);
            haloGo.transform.localScale = new Vector3(sunRadius * 2.8f, sunRadius * 2.8f, 1.2f);
            haloGo.GetComponent<Renderer>().sharedMaterial = PropMeshes.MatUnlit(new Color(1.7f, 1.0f, 0.55f));
            NoShadow(haloGo);

            var sunGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sunGo.name = "SynthSun";
            DestroyCollider(sunGo);
            sunGo.transform.SetParent(parent, false);
            sunGo.transform.position = new Vector3(0f, sunRadius, sunZ);
            sunGo.transform.localScale = new Vector3(sunRadius * 2f, sunRadius * 2f, 1.5f);
            // Pushed into HDR (was a flat 1,0.83,0.43 — capped at 1.0, so it never crossed the
            // bloom threshold and rendered as a plain yellow disc) so it reads as a genuine glowing
            // light source instead of a painted circle. Brightened further for iteration 5 (lead
            // review: "corona subtle, push sun emissive higher") alongside the halo above.
            sunGo.GetComponent<Renderer>().sharedMaterial = PropMeshes.MatUnlit(new Color(4.6f, 2.9f, 1.15f));
            NoShadow(sunGo);

            var slatMat = PropMeshes.MatUnlit(new Color(0.08f, 0.05f, 0.18f));
            float[] slatV = { -0.15f, 0.10f, 0.4f }; // fraction of sunRadius, offset from centre
            foreach (var v in slatV)
            {
                // Thin bands clamped to the disc's chord width at that height — full-width bars
                // stuck out past the sun's curved edge and read as floating black planks.
                float chord = 2f * Mathf.Sqrt(Mathf.Max(0.01f, 1f - v * v)) * sunRadius;
                var slat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slat.name = "SunSlat";
                DestroyCollider(slat);
                slat.transform.SetParent(parent, false);
                slat.transform.position = new Vector3(0f, sunRadius + v * sunRadius, sunZ - 0.8f);
                slat.transform.localScale = new Vector3(chord * 0.98f, sunRadius * 0.09f, 0.3f);
                slat.GetComponent<Renderer>().sharedMaterial = slatMat;
                NoShadow(slat);
            }
        }

        // ---- Star field: a single static (zero-velocity) particle burst scattered across the
        // upper sky ahead of the camera — one draw call instead of ~130 GameObjects. ----
        static void BuildStars(Transform parent)
        {
            var go = new GameObject("StarField");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            renderer.sharedMaterial = new Material(shader);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Continuous respawn instead of a one-shot burst: each star gets a random few-second
            // lifetime with a fade-in/fade-out alpha curve, so the field is always a mix of stars
            // brightening and dimming — reads as twinkle without any per-frame script cost.
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSpeed = 0f;
            // Widened size range (was a flat 0.08-0.22) so the field reads as near/far stars of
            // varied brightness instead of a uniform dusting of same-size dots — reviewer called
            // the field out as sparse/uniform.
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.95f, 1f), Color.white);
            main.gravityModifier = 0f;
            main.maxParticles = 260; // was 160 — denser field per lead review

            var emission = ps.emission;
            emission.rateOverTime = 44f; // ~ maxParticles / avg lifetime, keeps the field consistently populated

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var twinkle = new Gradient();
            twinkle.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = twinkle;

            // Camera sits low (~1.6m, FOV 46, ~2 deg down-pitch) — a box centred much higher than
            // this falls outside the view cone entirely. Fill the wedge from just above the sun's
            // rim (sun top edge y=14 at z=50) to the frame's top edge (~21 deg above horizontal
            // -> y~24 at z=55).
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 19f, 48f);
            shape.scale = new Vector3(80f, 11f, 25f);

            ps.Play();
            // Play() alone doesn't advance particles outside Play mode — without this the star
            // field never emits in the scene builder's edit-mode screenshot render.
            // Skipped when there is no graphics device: Simulate on a Billboard system crashes
            // Unity natively (0xC0000005) under -batchmode -nographics.
            if (!Application.isPlaying &&
                SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                ps.Simulate(3f, true, true);
        }

        // ---- One rare bright streak crossing the upper sky every ~9 seconds — a stretched-
        // billboard "shooting star", HDR bright enough to catch the bloom the ambient stars don't. ----
        static void BuildShootingStar(Transform parent)
        {
            var go = new GameObject("ShootingStar");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            renderer.sharedMaterial = new Material(shader);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 4f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 42f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new Color(2.2f, 2.4f, 2.6f); // HDR white-blue — clears the bloom threshold
            main.gravityModifier = 0f;
            main.maxParticles = 4;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            // cycleCount 0 = repeats forever, once every 9 s.
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 0, 9f) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0f;
            shape.position = new Vector3(-22f, 22f, 42f);
            shape.rotation = new Vector3(12f, 25f, -20f); // diagonal arc across the upper sky

            ps.Play();
            // Fires the first burst mid-flight so it's visible in the edit-mode preview render.
            if (!Application.isPlaying) ps.Simulate(0.25f, true, true);
        }

        // ---- Horizon glow: 3 stacked bright (HDR, bloom-fed) unlit bands right at the dome's
        // rim so the hard sky/void seam bleeds into a soft glow instead of a sharp line. Returns
        // the band renderers so a live QuestStage instance can pulse them in Update() (see top of
        // file) — the editor-preview call in BalanceQuestSceneBuilder just discards the array. ----
        static Renderer[] BuildHorizonGlow(Transform parent)
        {
            (float y, float h, Color c)[] bands =
            {
                (0.05f, 0.35f, new Color(1.6f, 0.55f, 0.85f)),
                (0.55f, 0.45f, new Color(1.1f, 0.42f, 0.60f)),
                (1.25f, 0.6f,  new Color(0.65f, 0.25f, 0.42f)),
            };
            var renderers = new Renderer[bands.Length];
            for (int i = 0; i < bands.Length; i++)
            {
                var b = bands[i];
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "HorizonGlowBand";
                DestroyCollider(go);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(0f, b.y, 45f);
                go.transform.localScale = new Vector3(90f, b.h, 2f);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = PropMeshes.MatUnlit(b.c);
                NoShadow(go);
                renderers[i] = r;
            }
            return renderers;
        }

        // ---- A couple of large, hazy background islands sitting well past the runway's edge
        // props (which scroll with TrailScroller) — static backdrop dressing for depth, per lead
        // review ("vary sizes/heights more; maybe 1-2 distant islands"). Tinted darker/flatter
        // than the scrolling islands and pushed further back so fog softens them into the
        // horizon rather than competing with the runway's own islands. ----
        static void BuildDistantIslands(Transform parent)
        {
            (float x, float y, float z, float size)[] islands =
            {
                (-16f, 0.8f, 34f, 4.2f),
                (19f, 1.6f, 38f, 3.0f),
            };
            var tint = new Color(0.08f, 0.06f, 0.16f); // darker/flatter than TrailScroller's IslandTint — reads as further away
            foreach (var isl in islands)
            {
                var rock = KenneyProps.Nature("rock_largeA", parent);
                if (rock == null) continue;
                var renderers = rock.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (maxDim > 0.0001f) rock.transform.localScale *= isl.size / maxDim;
                rock.transform.localPosition = new Vector3(isl.x, isl.y, isl.z);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                foreach (var r in renderers) r.SetPropertyBlock(mpb);
            }
        }

        static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        static void NoShadow(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
