using UnityEngine;
using Kinex.FX;

namespace Kinex.MirrorGame
{
    /// <summary>
    /// Runtime-built "กระจกวิเศษ" Magic Mirror studio at night: a warm wood floor that takes real
    /// shadows, an ornate emissive mirror frame standing behind the ghost with a soft inner glow,
    /// a dark back wall + side walls with glowing night windows and a warm skirting strip, a
    /// low ceiling band so the frame edges don't fall to pure black, drifting fairy-light motes,
    /// warm/cool ambient and tinted fog. Same no-scene-authoring philosophy as
    /// Kinex.BattleGame.BattleStage / Kinex.FruitGame.FruitStage: Awake() builds everything as
    /// children of this transform. Lights + post live in the scene builder (matching the other games).
    /// </summary>
    public class MirrorStage : MonoBehaviour
    {
        static Texture2D s_MirrorGlowTex;
        static Texture2D s_SoftDotTex;
        static Material s_SoftDotMat;

        // Guarded so a device-only failure (e.g. a stripped shader) degrades to a partial stage
        // instead of aborting the whole build mid-way (happened 2026-07-14: BuildFloorSheen threw
        // on device and everything after it — mirror, walls, fairy lights — never got built).
        void Awake()
        {
            try { BuildStage(transform); }
            catch (System.Exception e) { Debug.LogError($"[MirrorStage] stage build failed: {e}"); }
        }

        public static void BuildStage(Transform parent)
        {
            // Warm-but-moody trilight ambient (never flat grey), tinted fog, no washed-out skybox.
            ProceduralSkybox.SetAmbient(
                sky: new Color(0.33f, 0.29f, 0.44f),      // cool violet night from above
                equator: new Color(0.45f, 0.33f, 0.29f),  // warm lamp glow at eye level
                ground: new Color(0.16f, 0.12f, 0.12f));

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.13f, 0.10f, 0.17f);
            RenderSettings.fogStartDistance = 4f;
            RenderSettings.fogEndDistance = 16f;

            BuildFloor(parent);
            BuildBackWall(parent);
            BuildSideWalls(parent);
            BuildMirror(parent);
            BuildFairyLights(parent);

            var cam = Camera.main;
            if (cam != null && cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = new Color(0.10f, 0.08f, 0.15f);
        }

        // Wood floor — URP Lit, smoothness 0 so it receives real cast shadows correctly (same fix
        // BattleStage/FruitStage document). Dark plank grooves break up the flat tone, a warm
        // emissive stage rug marks where the player stands, and a soft unlit sheen quad on top of
        // the rug fakes a varnished-wood highlight without touching the Lit floor material (which
        // has blown out to white on large flat surfaces in this project before).
        static void BuildFloor(Transform parent)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "StudioFloor";
            DestroyColliderSafe(floor);
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 2f);
            floor.transform.localScale = new Vector3(12f, 0.2f, 14f);
            floor.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(new Color(0.30f, 0.20f, 0.13f), smooth: 0f);

            BuildPlankGrooves(parent);

            // A warm circular "stage rug" the player stands on — subtle emissive so it reads as a
            // spotlit floor even in the dark studio.
            var rug = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rug.name = "StageRug";
            DestroyColliderSafe(rug);
            rug.transform.SetParent(parent, false);
            rug.transform.localPosition = new Vector3(0f, 0.005f, 0.2f);
            rug.transform.localScale = new Vector3(2.6f, 0.02f, 2.6f);
            rug.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(new Color(0.46f, 0.29f, 0.19f), smooth: 0.18f,
                               emission: new Color(0.24f, 0.13f, 0.06f));

            BuildFloorSheen(parent, rug.transform.localPosition);
        }

        // Thin dark strips laid across the floor width at plank intervals — cheap read of "wood
        // planks" instead of one flat slab. Non-emissive, matches PropMeshes' flat-Lit floor fix.
        static void BuildPlankGrooves(Transform parent)
        {
            var grooveMat = PropMeshes.Mat(new Color(0.20f, 0.13f, 0.08f), smooth: 0f);
            float floorFrontZ = 2f - 7f, floorBackZ = 2f + 7f;
            for (float z = floorFrontZ + 0.6f; z < floorBackZ; z += 1.15f)
            {
                var groove = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groove.name = "PlankGroove";
                DestroyColliderSafe(groove);
                groove.transform.SetParent(parent, false);
                groove.transform.localPosition = new Vector3(0f, 0.002f, z);
                groove.transform.localScale = new Vector3(12f, 0.002f, 0.035f);
                groove.GetComponent<Renderer>().sharedMaterial = grooveMat;
            }
        }

        // Soft radial highlight sitting flush on the rug — an unlit additive-ish glow, not a
        // material smoothness/reflection trick, so it can't blow out like the Lit floor did.
        static void BuildFloorSheen(Transform parent, Vector3 rugLocalPos)
        {
            var sheen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheen.name = "FloorSheen";
            DestroyColliderSafe(sheen);
            sheen.transform.SetParent(parent, false);
            sheen.transform.localPosition = rugLocalPos + new Vector3(0f, 0.01f, 0f);
            sheen.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sheen.transform.localScale = Vector3.one * 3.6f;
            var mat = new Material(UnlitShader()) { name = "FloorSheenMat" };
            SetTexture(mat, SoftDotTexture());
            SetBaseColor(mat, new Color(1f, 0.85f, 0.55f, 0.22f));
            ConfigureTransparent(mat);
            sheen.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static void BuildBackWall(Transform parent)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "BackWall";
            DestroyColliderSafe(wall);
            wall.transform.SetParent(parent, false);
            // Tall enough that its top clears the camera's vertical frustum at this depth — a
            // separate ceiling slab tried earlier sat above the visible cone no matter how it was
            // placed (the visible height shrinks with distance) and never actually rendered, so the
            // simplest fix is to just extend the wall itself past the frame-top edge.
            wall.transform.localPosition = new Vector3(0f, 2.9f, 5.2f);
            wall.transform.localScale = new Vector3(13f, 7.4f, 0.3f);
            wall.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(new Color(0.12f, 0.10f, 0.16f), smooth: 0.05f);

            // Thin warm "cove light" strip near the top of the wall so the upper frame edge reads
            // as a lit ceiling line instead of just more flat dark wall.
            var cove = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cove.name = "CoveStrip";
            DestroyColliderSafe(cove);
            cove.transform.SetParent(parent, false);
            cove.transform.localPosition = new Vector3(0f, 5.6f, 5.02f);
            cove.transform.localScale = new Vector3(6.5f, 0.06f, 0.1f);
            cove.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(new Color(0.6f, 0.5f, 0.3f), smooth: 0.2f,
                               emission: new Color(1f, 0.82f, 0.5f) * 1.3f);

            // Two tall "night windows" — cool moonlit blue emissive panes flanking the mirror.
            var pane = new Color(0.22f, 0.34f, 0.62f);
            foreach (float x in new[] { -4.2f, 4.2f })
            {
                var win = GameObject.CreatePrimitive(PrimitiveType.Cube);
                win.name = "NightWindow";
                DestroyColliderSafe(win);
                win.transform.SetParent(parent, false);
                win.transform.localPosition = new Vector3(x, 2.3f, 5.05f);
                win.transform.localScale = new Vector3(1.5f, 2.8f, 0.1f);
                win.GetComponent<Renderer>().sharedMaterial =
                    PropMeshes.Mat(pane * 0.4f, smooth: 0.4f, emission: pane * 0.9f);

                // Warm sill glow under each window.
                var sill = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sill.name = "WindowSill";
                DestroyColliderSafe(sill);
                sill.transform.SetParent(parent, false);
                sill.transform.localPosition = new Vector3(x, 0.85f, 4.95f);
                sill.transform.localScale = new Vector3(1.7f, 0.12f, 0.25f);
                sill.GetComponent<Renderer>().sharedMaterial =
                    PropMeshes.Mat(new Color(0.4f, 0.28f, 0.18f), smooth: 0.1f);
            }

            // Warm skirting strip lights along the base of the back wall — the "neon accent" that
            // keeps the room from falling to black at floor level.
            var skirt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skirt.name = "Skirting";
            DestroyColliderSafe(skirt);
            skirt.transform.SetParent(parent, false);
            skirt.transform.localPosition = new Vector3(0f, 0.10f, 4.98f);
            skirt.transform.localScale = new Vector3(12.6f, 0.05f, 0.06f);
            skirt.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(new Color(0.5f, 0.25f, 0.08f), smooth: 0.2f,
                               emission: new Color(0.9f, 0.45f, 0.12f));
        }

        // Two flanking walls close the room off the sides instead of leaving void past the back
        // wall's width, and give the corners something to plant the fairy-light-lit potted bushes
        // against, per the "cozy studio" art direction.
        static void BuildSideWalls(Transform parent)
        {
            var wallMat = PropMeshes.Mat(new Color(0.11f, 0.09f, 0.14f), smooth: 0.05f);
            var skirtMat = PropMeshes.Mat(new Color(0.5f, 0.25f, 0.08f), smooth: 0.2f,
                                           emission: new Color(0.7f, 0.35f, 0.10f));
            foreach (float x in new[] { -6.5f, 6.5f })
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "SideWall";
                DestroyColliderSafe(wall);
                wall.transform.SetParent(parent, false);
                wall.transform.localPosition = new Vector3(x, 2.2f, 3f);
                wall.transform.localScale = new Vector3(0.3f, 6f, 10f);
                wall.GetComponent<Renderer>().sharedMaterial = wallMat;

                var skirt = GameObject.CreatePrimitive(PrimitiveType.Cube);
                skirt.name = "SideSkirting";
                DestroyColliderSafe(skirt);
                skirt.transform.SetParent(parent, false);
                skirt.transform.localPosition = new Vector3(x - Mathf.Sign(x) * 0.02f, 0.10f, 3f);
                skirt.transform.localScale = new Vector3(0.06f, 0.05f, 9.6f);
                skirt.GetComponent<Renderer>().sharedMaterial = skirtMat;

                var plant = KenneyProps.Nature("plant_bushDetailed", parent, scale: 1.4f);
                if (plant != null)
                    plant.transform.localPosition = new Vector3(x - Mathf.Sign(x) * 0.7f, 0f, 4.5f);
            }
        }

        // Ornate mirror standing behind the ghost — dark glass inside a glowing gold frame, with a
        // soft inner radial glow + star-dust so it reads as a magical surface rather than a flat
        // navy rectangle.
        static void BuildMirror(Transform parent)
        {
            const float z = 1.7f, cy = 1.15f, w = 1.9f, h = 3.0f, t = 0.12f;
            var gold = new Color(1f, 0.82f, 0.42f);
            var frameMat = PropMeshes.Mat(gold * 0.5f, metallic: 0.6f, smooth: 0.7f, emission: gold * 1.4f);

            // Dark reflective glass.
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "MirrorGlass";
            DestroyColliderSafe(glass);
            glass.transform.SetParent(parent, false);
            glass.transform.localPosition = new Vector3(0f, cy, z + 0.05f);
            glass.transform.localScale = new Vector3(w, h, 0.06f);
            glass.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(new Color(0.10f, 0.10f, 0.16f), smooth: 0.2f, emission: new Color(0.06f, 0.07f, 0.14f));

            // Soft inner glow + star-dust quad, sitting just in front of the glass so it reads as
            // depth inside the mirror rather than a flat colour.
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "MirrorGlow";
            DestroyColliderSafe(glow);
            glow.transform.SetParent(parent, false);
            glow.transform.localPosition = new Vector3(0f, cy, z + 0.02f);
            glow.transform.localScale = new Vector3(w - 0.08f, h - 0.08f, 1f);
            var glowMat = new Material(UnlitShader()) { name = "MirrorGlowMat" };
            SetTexture(glowMat, MirrorGlowTexture());
            SetBaseColor(glowMat, new Color(0.55f, 0.42f, 0.95f, 1f) * 1.4f);
            ConfigureTransparent(glowMat);
            glow.GetComponent<Renderer>().sharedMaterial = glowMat;

            // Four frame bars.
            (Vector3 pos, Vector3 scale)[] bars =
            {
                (new Vector3(0f, cy + h * 0.5f, z), new Vector3(w + t * 2f, t, t)), // top
                (new Vector3(0f, cy - h * 0.5f, z), new Vector3(w + t * 2f, t, t)), // bottom
                (new Vector3(-w * 0.5f - t * 0.5f, cy, z), new Vector3(t, h, t)),   // left
                (new Vector3(w * 0.5f + t * 0.5f, cy, z), new Vector3(t, h, t)),    // right
            };
            foreach (var (pos, scale) in bars)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "MirrorFrameBar";
                DestroyColliderSafe(bar);
                bar.transform.SetParent(parent, false);
                bar.transform.localPosition = pos;
                bar.transform.localScale = scale;
                bar.GetComponent<Renderer>().sharedMaterial = frameMat;
            }

            // Small corner orbs make the frame read as "ornate" rather than a plain rectangle.
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                {
                    var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    orb.name = "MirrorCornerOrb";
                    DestroyColliderSafe(orb);
                    orb.transform.SetParent(parent, false);
                    orb.transform.localPosition = new Vector3(sx * (w * 0.5f + t * 0.5f), cy + sy * h * 0.5f, z);
                    orb.transform.localScale = Vector3.one * (t * 2.1f);
                    orb.GetComponent<Renderer>().sharedMaterial = frameMat;
                }
        }

        // Two layers: a denser near field of small soft fairy-light motes, and a sparser far field
        // of bigger softer "bokeh" orbs for depth. Both use a round soft-falloff sprite (built
        // locally) instead of the default particle shader's hard-edged square.
        static void BuildFairyLights(Transform parent)
        {
            var dotMat = SoftDotMaterial();

            var warm = new Color(1f, 0.85f, 0.55f, 0.85f);
            var motes = KinexFx.AmbientMotes(new Vector3(0f, 1.9f, 1.5f), new Vector3(6f, 2.6f, 4f), warm, rate: 16);
            motes.transform.SetParent(parent, false);
            motes.GetComponent<ParticleSystemRenderer>().sharedMaterial = dotMat;
            var main = motes.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            var drift = motes.velocityOverLifetime;
            drift.enabled = true;
            drift.y = new ParticleSystem.MinMaxCurve(0.03f, 0.09f); // gentle upward float

            var bokehColor = new Color(0.95f, 0.8f, 1f, 0.5f);
            var bokeh = KinexFx.AmbientMotes(new Vector3(0f, 2.6f, 3.5f), new Vector3(9f, 3.5f, 6f), bokehColor, rate: 4);
            bokeh.name = "FairyBokeh";
            bokeh.transform.SetParent(parent, false);
            bokeh.GetComponent<ParticleSystemRenderer>().sharedMaterial = dotMat;
            var bokehMain = bokeh.main;
            bokehMain.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.2f);
            bokehMain.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            var bokehDrift = bokeh.velocityOverLifetime;
            bokehDrift.enabled = true;
            bokehDrift.y = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);

            // Same batch-render fix the other stages use: Play() alone doesn't advance particles
            // outside play mode, so pre-simulate for the edit-mode screenshot. Guard against the
            // null graphics device in a headless run.
            if (!Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                motes.Simulate(3f, true, true);
                bokeh.Simulate(5f, true, true);
            }
        }

        // ---- small local unlit-texture helpers (kept private to this file; the shared
        // KenneyProps/PropMeshes/KinexFx helpers are used elsewhere in this project) ----

        static Shader s_UnlitShader;
        static Shader UnlitShader()
        {
            if (s_UnlitShader != null) return s_UnlitShader;
            // Fallback chain for stripped builds — see Editor/AlwaysIncludedShaders.cs.
            s_UnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (s_UnlitShader == null) s_UnlitShader = Shader.Find("Unlit/Transparent");
            if (s_UnlitShader == null) s_UnlitShader = Shader.Find("Sprites/Default");
            if (s_UnlitShader == null) s_UnlitShader = Shader.Find("Unlit/Color");
            return s_UnlitShader;
        }

        static void SetTexture(Material mat, Texture2D tex)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }

        static void SetBaseColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
        }

        static void ConfigureTransparent(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive glow
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        // Soft round dot: quadratic falloff from opaque centre to transparent edge — used for both
        // fairy-light particles and the floor sheen quad. Deterministic (no randomness), built once.
        static Texture2D SoftDotTexture()
        {
            if (s_SoftDotTex != null) return s_SoftDotTex;
            const int size = 48;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = 1f - r * r;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            s_SoftDotTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_SoftDotTex.SetPixels32(px);
            s_SoftDotTex.Apply();
            return s_SoftDotTex;
        }

        static Material SoftDotMaterial()
        {
            if (s_SoftDotMat != null) return s_SoftDotMat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            s_SoftDotMat = new Material(shader) { name = "FairyDotMat" };
            SetTexture(s_SoftDotMat, SoftDotTexture());
            // The default particle material this shader produces is Opaque, which ignores the
            // dot texture's alpha falloff entirely and renders a hard-edged square (the bug we're
            // fixing here) — must explicitly switch it to alpha-blended transparent.
            ConfigureTransparent(s_SoftDotMat);
            return s_SoftDotMat;
        }

        // Radial violet-white glow with sparse bright "star-dust" flecks — deterministic pattern
        // (fixed hash, not UnityEngine.Random) so the mirror looks identical every build.
        static Texture2D MirrorGlowTexture()
        {
            if (s_MirrorGlowTex != null) return s_MirrorGlowTex;
            const int size = 128;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float glow = Mathf.Clamp01(1f - r) ;
                    glow *= glow;
                    float a = glow * 0.8f;

                    // Cheap deterministic "star" hash: a handful of bright points scattered by a
                    // fixed integer hash of the pixel coords (no Random state, no per-build drift).
                    int hash = (x * 928371 + y * 123457) & 0xFFFF;
                    bool star = hash < 26; // ~0.04% of pixels
                    if (star) a = Mathf.Max(a, 0.9f);

                    // Small deterministic dither on the smooth radial falloff — an RGBA32 gradient
                    // this size otherwise shows visible contour banding once alpha-blended and
                    // ACES-tonemapped (verified in the round-1 screenshot).
                    float dither = ((hash % 11) / 11f - 0.5f) * 0.05f;
                    a = Mathf.Clamp01(a + dither);

                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            s_MirrorGlowTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_MirrorGlowTex.SetPixels32(px);
            s_MirrorGlowTex.Apply();
            return s_MirrorGlowTex;
        }

        static void DestroyColliderSafe(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }
    }
}
