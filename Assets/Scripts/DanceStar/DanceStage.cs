using System.Collections.Generic;
using UnityEngine;
using Kinex.FX;

namespace Kinex.DanceStar
{
    /// <summary>
    /// Runtime-built "เวทีซุปตาร์" night concert stage: a dark neon-ringed floor, a low glowing
    /// podium for the trainer "coach star" beside the player's spotlit centre-stage mark, three
    /// coloured spotlight beams, a dark crowd of silhouettes with bobbing glowsticks, a starry
    /// night sky with a soft moon, and an emissive starburst behind the stage. Same
    /// no-scene-authoring philosophy as Kinex.MirrorGame.MirrorStage / Kinex.TempleHunt.TempleStage:
    /// Awake() builds everything as children of this transform. Lights + post volume live in the
    /// scene builder (matching the other games).
    ///
    /// SHARED LAYOUT: DanceStarSceneBuilder positions the player avatar and the trainer rig using
    /// PlayerLocalPos / PodiumLocalPos / PodiumTopY below, so the "beside, on a podium" staging is
    /// defined in exactly one place instead of duplicated magic numbers across two files.
    /// </summary>
    public class DanceStage : MonoBehaviour
    {
        // ---- Shared staging layout (read by DanceStarSceneBuilder too) ----
        public static readonly Vector3 PlayerLocalPos = Vector3.zero;
        public static readonly Vector3 PodiumLocalPos = new Vector3(-1.55f, 0f, 0.75f);
        public const float PodiumTopY = 0.36f;
        public const float PodiumRadius = 0.5f;

        static Texture2D s_SoftDotTex;
        static Texture2D s_RingTex;
        static Texture2D s_BeamTex;
        static Material s_SoftDotMat;
        static Shader s_UnlitShader;

        // Materials registered here get brightened by SetMood — cleared + rebuilt every BuildStage
        // call so a rebuild (idempotent re-run, or the scene builder's throwaway ~TempEnv preview)
        // never accumulates stale references from a previous build.
        static readonly List<Material> s_MoodMats = new List<Material>();
        static readonly List<Color> s_MoodBase = new List<Color>();
        static readonly List<string> s_MoodProp = new List<string>();

        // Guarded like MirrorStage: a device-only failure (e.g. a stripped shader) degrades to a
        // partial stage instead of aborting mid-build.
        void Awake()
        {
            try { BuildStage(transform); }
            catch (System.Exception e) { Debug.LogError($"[DanceStage] stage build failed: {e}"); }
        }

        void Start()
        {
            var director = FindAnyObjectByType<DanceStarDirector>();
            if (director != null) director.OnStreakChanged += HandleStreak;
        }

        void OnDestroy()
        {
            var director = FindAnyObjectByType<DanceStarDirector>();
            if (director != null) director.OnStreakChanged -= HandleStreak;
        }

        static void HandleStreak(int streak) => SetMood(Mathf.Clamp01(streak / 10f));

        /// <summary>
        /// 0 = calm baseline neon, 1 = full show-off brightness (x2 streak territory). Static:
        /// only one DanceStage exists per scene, and the scene builder's throwaway preview env
        /// never needs mood control — matches the "tiny MonoBehaviour wires it" plan (Start()
        /// above subscribes this stage to DanceStarDirector.OnStreakChanged).
        /// </summary>
        public static void SetMood(float t01)
        {
            t01 = Mathf.Clamp01(t01);
            float boost = Mathf.Lerp(1f, 2.2f, t01);
            for (int i = 0; i < s_MoodMats.Count; i++)
            {
                var m = s_MoodMats[i];
                if (m == null) continue;
                var c = s_MoodBase[i] * boost;
                var prop = s_MoodProp[i];
                if (m.HasProperty(prop)) m.SetColor(prop, c);
                else m.color = c;
            }
        }

        public static void BuildStage(Transform parent)
        {
            s_MoodMats.Clear();
            s_MoodBase.Clear();
            s_MoodProp.Clear();

            ProceduralSkybox.SetAmbient(
                sky: new Color(0.08f, 0.06f, 0.16f),
                equator: new Color(0.14f, 0.08f, 0.20f),
                ground: new Color(0.02f, 0.02f, 0.04f));
            var dome = ProceduralSkybox.CreateSkyDome(new Color(0.06f, 0.03f, 0.14f), new Color(0.02f, 0.01f, 0.05f), 50f);
            dome.transform.SetParent(parent, false);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.05f, 0.03f, 0.10f);
            RenderSettings.fogStartDistance = 6f;
            RenderSettings.fogEndDistance = 22f;

            BuildSkyDecor(parent);
            BuildFloor(parent);
            BuildPodium(parent);
            BuildSpotlights(parent);
            BuildCrowd(parent);
            BuildStarburstBackdrop(parent);
            BuildSetDressing(parent);

            var cam = Camera.main;
            if (cam != null && cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = new Color(0.03f, 0.02f, 0.06f);
        }

        // ---- Sky: soft moon + drifting starfield ----
        static void BuildSkyDecor(Transform parent)
        {
            var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "Moon";
            DestroyColliderSafe(moon);
            moon.transform.SetParent(parent, false);
            moon.transform.localPosition = new Vector3(2.4f, 9.5f, 15f);
            moon.transform.localScale = Vector3.one * 2.6f;
            var moonColor = new Color(0.95f, 0.92f, 0.78f);
            var moonMat = PropMeshes.Mat(moonColor * 0.5f, smooth: 0.1f, emission: moonColor * 1.3f);
            moon.GetComponent<Renderer>().sharedMaterial = moonMat;

            var stars = KinexFx.AmbientMotes(new Vector3(0f, 8.5f, 11f), new Vector3(26f, 9f, 20f),
                                              new Color(1f, 1f, 1f, 0.85f), rate: 10);
            stars.name = "Starfield";
            stars.transform.SetParent(parent, false);
            stars.GetComponent<ParticleSystemRenderer>().sharedMaterial = SoftDotMaterial();
            var main = stars.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            var drift = stars.velocityOverLifetime;
            drift.enabled = true;
            drift.y = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);

            SimulateIfEditor(stars, 6f);
        }

        // ---- Floor: dark unlit disc (URP Lit blows large flats to white in this project) with
        // neon rings, a darker "reflective" inset disc under the player mark, and radiating
        // grid spokes so it doesn't read as a single flat circle. ----
        static void BuildFloor(Transform parent)
        {
            Vector3 floorCenter = new Vector3(-0.6f, -0.05f, 0.9f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "StageFloor";
            DestroyColliderSafe(floor);
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = floorCenter;
            floor.transform.localScale = new Vector3(6.6f, 0.05f, 6.6f);
            // Bright enough to read as a dark floor against the near-black clear colour instead of
            // vanishing into it (round-2 screenshot lesson: near-black-on-black is invisible).
            floor.GetComponent<Renderer>().sharedMaterial = PropMeshes.MatUnlit(new Color(0.09f, 0.075f, 0.13f));

            var inset = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inset.name = "PlayerMark";
            DestroyColliderSafe(inset);
            inset.transform.SetParent(parent, false);
            inset.transform.localPosition = PlayerLocalPos + new Vector3(0f, 0.006f, 0.2f);
            inset.transform.localScale = new Vector3(1.7f, 0.02f, 1.7f);
            inset.GetComponent<Renderer>().sharedMaterial = PropMeshes.MatUnlit(new Color(0.012f, 0.012f, 0.028f));

            BuildFloorSheen(parent, PlayerLocalPos + new Vector3(0f, 0.013f, 0.2f));

            (float diameter, Color color)[] rings =
            {
                (2.7f, new Color(1f, 0.15f, 0.75f)),
                (4.3f, new Color(0.15f, 0.85f, 1f)),
                (5.9f, new Color(1f, 0.65f, 0.1f)),
            };
            foreach (var (diameter, color) in rings)
                BuildNeonRing(parent, floorCenter + new Vector3(0f, 0.007f, 0f), diameter, color);

            var spokeColor = new Color(0.55f, 0.2f, 0.55f) * 0.65f;
            var spokeMat = PropMeshes.MatUnlit(spokeColor);
            RegisterMood(spokeMat, spokeColor);
            const int spokeCount = 8;
            for (int i = 0; i < spokeCount; i++)
            {
                float ang = i * (180f / spokeCount); // half-turn spacing: a spoke + its mirror form one long line
                var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spoke.name = "FloorSpoke";
                DestroyColliderSafe(spoke);
                spoke.transform.SetParent(parent, false);
                spoke.transform.localPosition = floorCenter + new Vector3(0f, 0.009f, 0f);
                spoke.transform.localRotation = Quaternion.Euler(0f, ang, 0f);
                spoke.transform.localScale = new Vector3(0.05f, 0.002f, 6f);
                spoke.GetComponent<Renderer>().sharedMaterial = spokeMat;
            }
        }

        static void BuildNeonRing(Transform parent, Vector3 localPos, float diameter, Color color)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "NeonRing";
            DestroyColliderSafe(ring);
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = localPos;
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * diameter;
            var mat = new Material(UnlitShader()) { name = "NeonRingMat" };
            var glow = color * 1.4f;
            SetTexture(mat, RingTexture());
            SetBaseColor(mat, glow);
            ConfigureTransparent(mat);
            ring.GetComponent<Renderer>().sharedMaterial = mat;
            RegisterMood(mat, glow);
        }

        static void BuildFloorSheen(Transform parent, Vector3 pos)
        {
            var sheen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheen.name = "FloorSheen";
            DestroyColliderSafe(sheen);
            sheen.transform.SetParent(parent, false);
            sheen.transform.localPosition = pos;
            sheen.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sheen.transform.localScale = Vector3.one * 2.3f;
            var mat = new Material(UnlitShader()) { name = "FloorSheenMat" };
            SetTexture(mat, SoftDotTexture());
            SetBaseColor(mat, new Color(0.75f, 0.85f, 1f, 0.26f));
            ConfigureTransparent(mat);
            sheen.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ---- Trainer podium: low glowing cylinder at PodiumLocalPos; the scene builder stands
        // the trainer rig on top of it (PodiumLocalPos + Vector3.up * PodiumTopY). ----
        static void BuildPodium(Transform parent)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "TrainerPodium";
            DestroyColliderSafe(body);
            body.transform.SetParent(parent, false);
            body.transform.localPosition = PodiumLocalPos + new Vector3(0f, PodiumTopY * 0.5f, 0f);
            body.transform.localScale = new Vector3(PodiumRadius * 2f, PodiumTopY * 0.5f, PodiumRadius * 2f);
            body.GetComponent<Renderer>().sharedMaterial = PropMeshes.Mat(new Color(0.22f, 0.18f, 0.26f), smooth: 0.3f);

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "TrainerPodiumRim";
            DestroyColliderSafe(rim);
            rim.transform.SetParent(parent, false);
            rim.transform.localPosition = PodiumLocalPos + new Vector3(0f, PodiumTopY - 0.025f, 0f);
            rim.transform.localScale = new Vector3(PodiumRadius * 2.04f, 0.018f, PodiumRadius * 2.04f);
            var rimColor = new Color(0.35f, 0.75f, 1f);
            var rimMat = PropMeshes.Mat(rimColor * 0.35f, smooth: 0.3f, emission: rimColor * 1.6f);
            rim.GetComponent<Renderer>().sharedMaterial = rimMat;
            RegisterMood(rimMat, rimColor * 1.6f, "_EmissionColor");
        }

        // ---- 3 tilted, additive, colour-tinted "shaft of light" quads (a soft cone-gradient
        // texture fakes the volumetric taper) — hand-placed like every other stage's static
        // props rather than derived via 3D look-at math, then eyeballed against the screenshots. ----
        static void BuildSpotlights(Transform parent)
        {
            // Narrower + dimmer than the first pass: round-2 screenshot showed a full-width beam
            // washing out the trainer standing directly in its path. These read as background
            // light shafts crossing behind/above both subjects rather than glaring on top of them.
            BuildSpotlightBeam(parent, new Vector3(-1.0f, 3.1f, -0.6f), new Vector3(30f, 22f, 0f), 3.4f, 0.85f,
                                new Color(0.25f, 0.9f, 1f));
            BuildSpotlightBeam(parent, new Vector3(0.7f, 3.0f, -0.3f), new Vector3(30f, -22f, 0f), 3.2f, 0.8f,
                                new Color(1f, 0.2f, 0.85f));
            BuildSpotlightBeam(parent, new Vector3(-1.9f, 2.7f, 0.4f), new Vector3(26f, 6f, 0f), 2.6f, 0.65f,
                                new Color(1f, 0.7f, 0.2f));
        }

        static void BuildSpotlightBeam(Transform parent, Vector3 localPos, Vector3 eulerAngles,
                                        float length, float width, Color color)
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Quad);
            beam.name = "SpotlightBeam";
            DestroyColliderSafe(beam);
            beam.transform.SetParent(parent, false);
            beam.transform.localPosition = localPos;
            beam.transform.localRotation = Quaternion.Euler(eulerAngles);
            beam.transform.localScale = new Vector3(width, length, 1f);

            var mat = new Material(UnlitShader()) { name = "SpotlightBeamMat" };
            var glow = color * 0.7f;
            SetTexture(mat, BeamTexture());
            SetBaseColor(mat, glow);
            ConfigureTransparent(mat);
            beam.GetComponent<Renderer>().sharedMaterial = mat;
            RegisterMood(mat, glow);
        }

        // ---- Dark crowd rows beyond the stage edge, deterministic jitter (no UnityEngine.Random
        // — matches the rest of the project's reproducible-build convention), plus 3 bobbing
        // multi-colour "glowstick" mote clusters over the crowd. ----
        static void BuildCrowd(Transform parent)
        {
            // Brighter than pure black (round-2 lesson: near-black silhouettes vanish into the
            // near-black clear colour) and a narrower spread so more rows land inside the
            // portrait camera's narrow horizontal FOV instead of running off-frame.
            var crowdMat = PropMeshes.Mat(new Color(0.08f, 0.07f, 0.12f), smooth: 0.05f);
            const int rows = 3;
            for (int row = 0; row < rows; row++)
            {
                float z = 3.6f + row * 1.15f;
                float height = 0.55f + row * 0.1f;
                int count = 9 + row * 2;
                float spanX = 4.2f + row * 0.9f;
                for (int i = 0; i < count; i++)
                {
                    float t = count > 1 ? i / (float)(count - 1) : 0.5f;
                    float x = Mathf.Lerp(-spanX * 0.5f, spanX * 0.5f, t) - 0.6f;
                    float jitterY = Mathf.Sin(i * 12.9898f + row * 3.7f) * 0.05f;
                    float jitterZ = Mathf.Cos(i * 7.233f + row * 1.9f) * 0.18f;

                    var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    person.name = "CrowdSilhouette";
                    DestroyColliderSafe(person);
                    person.transform.SetParent(parent, false);
                    person.transform.localPosition = new Vector3(x, height + jitterY, z + jitterZ);
                    person.transform.localScale = new Vector3(0.26f, height * 0.95f, 0.26f);
                    person.GetComponent<Renderer>().sharedMaterial = crowdMat;
                }
            }

            BuildGlowsticks(parent);
        }

        static void BuildGlowsticks(Transform parent)
        {
            var dotMat = SoftDotMaterial();
            (Vector3 center, Color color)[] clusters =
            {
                (new Vector3(-2.4f, 1.15f, 4.1f), new Color(0.3f, 1f, 0.5f, 0.9f)),
                (new Vector3(0.1f, 1.2f, 4.7f), new Color(1f, 0.35f, 0.65f, 0.9f)),
                (new Vector3(2.3f, 1.1f, 4.3f), new Color(0.35f, 0.65f, 1f, 0.9f)),
            };
            foreach (var (center, color) in clusters)
            {
                var glow = KinexFx.AmbientMotes(center, new Vector3(3.2f, 0.5f, 2.2f), color, rate: 6);
                glow.name = "Glowsticks";
                glow.transform.SetParent(parent, false);
                glow.GetComponent<ParticleSystemRenderer>().sharedMaterial = dotMat;
                var main = glow.main;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                var vel = glow.velocityOverLifetime;
                vel.enabled = true;
                vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.12f); // gentle bob

                SimulateIfEditor(glow, 3f);
            }
        }

        // ---- Emissive star-motif backdrop behind the stage (radiating spikes in the camera-facing
        // plane, like a concert sunburst logo). ----
        static void BuildStarburstBackdrop(Transform parent)
        {
            var burst = new GameObject("StarburstBackdrop");
            burst.transform.SetParent(parent, false);
            burst.transform.localPosition = new Vector3(-0.6f, 4.3f, 6.4f); // higher + further back: a subtle
                                                                             // accent above the heads, not a
                                                                             // dominant shape at eye level
            burst.transform.localScale = Vector3.one * 0.6f;

            var spikeColor = new Color(0.7f, 0.55f, 1f) * 0.4f;
            var mat = PropMeshes.MatUnlit(spikeColor);
            RegisterMood(mat, spikeColor);
            const int spikes = 10;
            for (int i = 0; i < spikes; i++)
            {
                float ang = i * (360f / spikes);
                var spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spike.name = "StarSpike";
                DestroyColliderSafe(spike);
                spike.transform.SetParent(burst.transform, false);
                spike.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
                spike.transform.localScale = new Vector3(0.06f, 2.5f, 0.02f);
                spike.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        // ---- Wooden chair behind the player's spawn mark: set dressing for the mid-song chair
        // verse (players use their REAL chair; this one just reads as "waiting" set dressing). ----
        static void BuildSetDressing(Transform parent)
        {
            var chair = PropMeshes.Chair();
            chair.transform.SetParent(parent, false);
            chair.transform.localPosition = PlayerLocalPos + new Vector3(0.6f, 0f, 0.65f);
            chair.transform.localRotation = Quaternion.Euler(0f, 200f, 0f);
        }

        // ---- mood registry ----
        static void RegisterMood(Material mat, Color baseValue, string prop = "_BaseColor")
        {
            s_MoodMats.Add(mat);
            s_MoodBase.Add(baseValue);
            s_MoodProp.Add(prop);
        }

        // ---- small local unlit-texture helpers (kept private, same pattern as MirrorStage) ----

        static Shader UnlitShader()
        {
            if (s_UnlitShader != null) return s_UnlitShader;
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
            s_SoftDotMat = new Material(shader) { name = "DanceStarDotMat" };
            SetTexture(s_SoftDotMat, SoftDotTexture());
            ConfigureTransparent(s_SoftDotMat);
            return s_SoftDotMat;
        }

        // Hollow ring: soft-edged annulus, used for the floor's neon rings.
        static Texture2D RingTexture()
        {
            if (s_RingTex != null) return s_RingTex;
            const int size = 96;
            const float innerR = 0.72f, outerR = 0.92f, feather = 0.06f;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(Mathf.Min((r - innerR) / feather, (outerR - r) / feather));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            s_RingTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_RingTex.SetPixels32(px);
            s_RingTex.Apply();
            return s_RingTex;
        }

        // Tapered "shaft of light": narrow + bright near row 0 (the light source end), widening
        // and fading toward row (h-1) (the floor end) — a 2D stand-in for a volumetric cone.
        static Texture2D BeamTexture()
        {
            if (s_BeamTex != null) return s_BeamTex;
            const int w = 32, h = 64;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                float halfWidth = Mathf.Lerp(0.06f, 0.5f, v);
                float fadeV = Mathf.Lerp(1f, 0.35f, v);
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1) - 0.5f;
                    float edge = halfWidth - Mathf.Abs(u);
                    float a = Mathf.Clamp01(edge / 0.08f) * fadeV;
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            s_BeamTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_BeamTex.SetPixels32(px);
            s_BeamTex.Apply();
            return s_BeamTex;
        }

        static void SimulateIfEditor(ParticleSystem ps, float seconds)
        {
            // Play() alone doesn't advance particles outside play mode, so pre-simulate for the
            // edit-mode screenshot — same fix MirrorStage's fairy lights use. Guard against the
            // null graphics device in a headless run.
            if (!Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                ps.Simulate(seconds, true, true);
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
