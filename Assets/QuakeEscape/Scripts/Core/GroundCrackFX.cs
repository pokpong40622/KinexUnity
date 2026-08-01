using System.Collections;
using UnityEngine;

namespace Collapse
{
    // Ground fissure copied from Gabriel Aguiar's "Ground Cracks | Fissure | Hole"
    // tutorial (youtu.be/qiAiVa0HtyE): a crack-shaped depth mask punches a real
    // hole through the street so the camera looks down into a trench — dark
    // V-walls dropping to a glowing lava seam — then the crack shoots along the
    // ground and yawns open with dust, embers and rock chips.
    // Render order: trench walls (1980) + lava seam (1981) draw first; the
    // Collapse/DepthMask mesh (1990) writes depth only; street opaques (2000)
    // then fail the depth test inside the crack outline and leave the trench
    // visible. No changes to the street materials are needed.
    public class GroundCrackFX : MonoBehaviour
    {
        private const int WallQueue = 1980;
        private const int LavaQueue = 1981;
        private const int MaskQueue = 1990;

        private float length;
        private float maxWidth;
        private Material lavaSource;
        private Material dustMat;
        private Mesh[] debrisMeshes;
        private bool branch;
        private float wanderLimit = 1.2f;

        private float[] xs, halfL, halfR, depth, zs, surf;
        private Material lavaInstance;
        private Color lavaBaseEmission;
        private bool hasEmission;
        private bool lit = true;
        private float glowScale = 1f;
        private float flashBoost;
        private readonly System.Collections.Generic.List<Light> glowLights =
            new System.Collections.Generic.List<Light>();
        private float[] lightBase;
        private Material beamMat;
        private Color beamColor;
        private float beamAlpha;
        private static Texture2D beamTex;
        private Material stripMat;
        private Color stripColor;
        private Material wallMat;
        private static Texture2D stripTex;
        private static Texture2D lavaGradTex;
        private static Texture2D heatNoiseTex;
        private readonly System.Collections.Generic.List<GameObject> spawnedChips =
            new System.Collections.Generic.List<GameObject>();

        private void OnDestroy()
        {
            foreach (var chip in spawnedChips)
            {
                if (chip != null)
                {
                    Destroy(chip);
                }
            }
        }

        public static GroundCrackFX Spawn(
            Vector3 origin, Vector3 dir, float length, float maxWidth,
            Material lavaMat, Material dustMat, Mesh[] debrisMeshes, bool branch = true,
            float wanderLimit = 1.2f, bool avoidPlayArea = true)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
            {
                dir = Vector3.forward;
            }
            if (avoidPlayArea)
            {
                // street cracks stop AT the 6x4 play floor's edge — close enough
                // to feel like the ground breaks toward the player, but a fissure
                // under the feet stays FloorHalf's job and gameplay signal
                float entry = EntryIntoPlayArea(origin, dir.normalized, length);
                length = Mathf.Max(1.5f, entry - 0.15f);
            }

            var go = new GameObject("GroundCrack");
            // parent sits at y=0 so per-station ground heights are plain local y
            origin.y = 0f;
            go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir.normalized));
            var fx = go.AddComponent<GroundCrackFX>();
            fx.length = length;
            fx.maxWidth = maxWidth;
            fx.lavaSource = lavaMat;
            fx.dustMat = dustMat;
            fx.debrisMeshes = debrisMeshes;
            fx.branch = branch;
            fx.wanderLimit = wanderLimit;
            return fx;
        }

        // 2D ray-vs-AABB: distance along dir where the crack line first enters the
        // play-floor box (with margin), or maxLen if it never does
        private static float EntryIntoPlayArea(Vector3 o, Vector3 d, float maxLen)
        {
            const float xExt = 3.1f, zExt = 2.55f;
            float tEnter = 0f, tExit = maxLen;
            foreach (var (oc, dc, ext) in new[] { (o.x, d.x, xExt), (o.z, d.z, zExt) })
            {
                if (Mathf.Abs(dc) < 1e-5f)
                {
                    if (Mathf.Abs(oc) > ext)
                    {
                        return maxLen; // parallel outside the slab: never enters
                    }
                    continue;
                }
                float t1 = (-ext - oc) / dc;
                float t2 = (ext - oc) / dc;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }
                tEnter = Mathf.Max(tEnter, t1);
                tExit = Mathf.Min(tExit, t2);
            }
            return tEnter < tExit ? Mathf.Min(tEnter, maxLen) : maxLen;
        }

        // scales the lava seam's emission against its authored value — lets the
        // floor-warning crack pulse as a "lift your leg" signal
        public void SetGlow(float k)
        {
            if (hasEmission && lavaInstance != null)
            {
                lavaInstance.SetColor("_EmissionColor", lavaBaseEmission * k);
            }
            glowScale = k;
        }

        private void Start()
        {
            BuildProfile();
            BuildMeshes();
            SpawnAOStrip();
            SpawnGlowStrip();
            SpawnEmbers();
            SpawnSparks();
            SpawnSmoke();
            SpawnAsh();
            SpawnHeatDust();
            SpawnHeatShimmer();
            SpawnBeam();
            SpawnLights();
            // runs on every crack (branches too): drives strip + beam + lights
            StartCoroutine(Flicker());
            StartCoroutine(Open());
        }

        // upward spotlights sitting at the bottom of the trench, just above the
        // lava seam: the light source IS the lava below, so everything it touches
        // — V-walls, rim edges, nearby debris, the player's lifted leg — is lit
        // from underneath, and nothing above the crack casts light back down.
        // Wide cone spills up through the opening onto the street; flickers like
        // firelight and follows the SetGlow pulse. Spread along the length so the
        // whole fissure reads as the source, not one hotspot; none on branches —
        // Android light budget, the glow strip carries branch glow instead.
        private void SpawnLights()
        {
            if (!lit)
            {
                return;
            }
            int count = Mathf.Clamp(Mathf.RoundToInt(length / 2.5f), 2, 3);
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                int idx = Mathf.RoundToInt(t * (zs.Length - 1));
                var go = new GameObject("CrackLight");
                // parented on purpose: root scale animates, so lights sweep out
                // along the crack as it shoots forward (range is scale-immune)
                go.transform.SetParent(transform, false);
                // just above the lava seam (depth*0.62) so the beam starts where
                // the glow visually originates and shines straight up and out
                go.transform.localPosition = new Vector3(
                    xs[idx], surf[idx] - depth[idx] * 0.55f, zs[idx]);
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 100f;
                l.innerSpotAngle = 40f;
                l.color = new Color(1f, 0.42f, 0.1f).linear;
                l.range = Mathf.Clamp(3.2f + maxWidth * 4f, 3.2f, 6.5f);
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                glowLights.Add(l);
            }

            // one broad, dim point light hovering over the widest part: fake GI
            // bounce so nearby debris, cars and walls pick up a soft warm wash
            // that the tight up-spots alone can't give them
            int midIdx = zs.Length / 2;
            var spillGo = new GameObject("CrackSpillLight");
            spillGo.transform.SetParent(transform, false);
            spillGo.transform.localPosition = new Vector3(
                xs[midIdx], surf[midIdx] + 0.35f, zs[midIdx]);
            var spill = spillGo.AddComponent<Light>();
            spill.type = LightType.Point;
            spill.color = new Color(1f, 0.52f, 0.16f).linear;
            spill.range = Mathf.Clamp(2.5f + length * 0.4f, 3f, 7f);
            spill.intensity = 0f;
            spill.shadows = LightShadows.None;
            glowLights.Add(spill);

            lightBase = new float[glowLights.Count];
            for (int i = 0; i < lightBase.Length; i++)
            {
                // restrained: the walls carry their own gradient glow now, so the
                // spots only need to underlight the player/rim shards — anything
                // hotter floods the street and flattens the effect; the point
                // spill is the "faint ambient glow" on the surrounding ground
                lightBase[i] = glowLights[i].type == LightType.Spot
                    ? 3.8f + maxWidth * 4.8f
                    : 0.7f + maxWidth * 1f;
            }
        }

        private IEnumerator Flicker()
        {
            while (true)
            {
                flashBoost = Mathf.MoveTowards(flashBoost, 0f, Time.deltaTime * 6f);
                for (int i = 0; i < glowLights.Count; i++)
                {
                    if (glowLights[i] == null)
                    {
                        continue;
                    }
                    float n = Mathf.PerlinNoise(i * 7.31f, Time.time * 6f);
                    glowLights[i].intensity =
                        lightBase[i] * glowScale * (0.75f + 0.5f * n) + flashBoost;
                }
                if (beamMat != null)
                {
                    float bn = Mathf.PerlinNoise(3.7f, Time.time * 5f);
                    var c = beamColor;
                    c.a = Mathf.Clamp01(
                        beamAlpha * glowScale * (0.7f + 0.5f * bn) + flashBoost * 0.15f);
                    beamMat.color = c;
                }
                if (stripMat != null)
                {
                    // slow simmer, not firelight strobe — the strip is only a
                    // faint ambient wash now; the walls own the glow
                    float sn = Mathf.PerlinNoise(9.1f, Time.time * 3f);
                    var sc = stripColor;
                    sc.a = Mathf.Clamp01(
                        0.2f * glowScale * (0.85f + 0.3f * sn) + flashBoost * 0.08f);
                    stripMat.color = sc;
                }
                if (wallMat != null)
                {
                    // walls breathe with the same pulse the seam/lights follow, a
                    // touch slower than firelight so the gradient stays serene
                    float wn = Mathf.PerlinNoise(5.3f, Time.time * 2.2f);
                    wallMat.SetFloat("_GlowMul",
                        glowScale * (0.85f + 0.3f * wn) + flashBoost * 0.25f);
                }
                yield return null;
            }
        }

        // soft volumetric glow INSIDE the trench: a both-winding vertical ribbon
        // riding the crack centreline from just above the molten seam up to a
        // little past the rim. Vertex alpha runs bright at the bottom to zero at
        // the top (plus tip fades), so the light visibly originates deep in the
        // crack and dissolves as it rises — no hard-edged energy column
        private void SpawnBeam()
        {
            if (!lit)
            {
                return;
            }
            Material src = FindEmberMaterial();
            if (src == null)
            {
                return;
            }
            var go = new GameObject("CrackBeam");
            go.transform.SetParent(transform, false);

            int n = zs.Length;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var cols = new Color[n * 2];
            // how far the glow pokes above the street before it has fully faded
            float rise = Mathf.Clamp(0.18f + maxWidth * 0.9f, 0.2f, 0.55f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.8f);
                verts[i * 2] = new Vector3(xs[i], surf[i] - depth[i] * 0.7f, zs[i]);
                verts[i * 2 + 1] = new Vector3(
                    xs[i], surf[i] + rise * (0.35f + 0.65f * env), zs[i]);
                uvs[i * 2] = new Vector2(0.5f, 0f);
                uvs[i * 2 + 1] = new Vector2(0.5f, 1f);
                cols[i * 2] = new Color(1f, 1f, 1f, env);
                cols[i * 2 + 1] = new Color(1f, 1f, 1f, 0f);
            }
            var tris = new int[(n - 1) * 12];
            for (int i = 0; i < n - 1; i++)
            {
                int m = i * 12;
                int v = i * 2;
                // both windings — visible from every side, additive so no dark face
                tris[m] = v; tris[m + 1] = v + 1; tris[m + 2] = v + 3;
                tris[m + 3] = v; tris[m + 4] = v + 3; tris[m + 5] = v + 2;
                tris[m + 6] = v; tris[m + 7] = v + 3; tris[m + 8] = v + 1;
                tris[m + 9] = v; tris[m + 10] = v + 2; tris[m + 11] = v + 3;
            }
            var mesh = new Mesh { vertices = verts, uv = uvs, colors = cols, triangles = tris };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            beamMat = new Material(src) { mainTexture = BeamTexture() };
            if (hasEmission)
            {
                float m = Mathf.Max(lavaBaseEmission.r,
                    Mathf.Max(lavaBaseEmission.g, Mathf.Max(lavaBaseEmission.b, 0.001f)));
                beamColor = lavaBaseEmission / m;
            }
            else
            {
                beamColor = new Color(1f, 0.5f, 0.18f);
            }
            // barely whitened, no HDR push: the ribbon is haze catching light from
            // below, not a light source of its own — the seam stays the hot point
            beamColor = Color.Lerp(beamColor, Color.white, 0.15f) * 0.95f;
            beamColor.a = 1f;
            beamAlpha = 0f;
            var c0 = beamColor;
            c0.a = 0f;
            beamMat.color = c0;
            mr.sharedMaterial = beamMat;
        }

        // brightness/height spike as the trench yawns open, then settle into a
        // simmering column (Flicker keeps modulating alpha with glowScale)
        private IEnumerator BeamBurst()
        {
            float t = 0f;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                beamAlpha = Mathf.Clamp01(t / 0.25f) * 0.5f;
                yield return null;
            }
            while (t < 1.2f)
            {
                t += Time.deltaTime;
                beamAlpha = Mathf.Lerp(0.5f, 0.16f, (t - 0.25f) / 0.95f);
                yield return null;
            }
            beamAlpha = 0.16f;
        }

        private static Texture2D BeamTexture()
        {
            // survives domain-reload-off play sessions via the destroyed-object
            // check; brightness baked into rgb AND alpha so both additive
            // conventions (One One / SrcAlpha One) read the same shape
            if (beamTex == null)
            {
                const int size = 64;
                beamTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    float v = y / (size - 1f);
                    // steep fade: most of the energy lives in the lower third so
                    // the glow visibly dissolves on its way up out of the crack
                    float fade = Mathf.Pow(1f - v, 2f);
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (size - 1f);
                        float d = Mathf.Abs(u - 0.5f) * 2f;
                        float core = Mathf.Pow(1f - d, 1.2f);
                        float a = core * fade;
                        // warm amber at the base cooling to deep red as it rises —
                        // no white-hot core, the molten seam keeps that job
                        Color hue = Color.Lerp(
                            new Color(1f, 0.72f, 0.28f), new Color(0.85f, 0.3f, 0.06f), v);
                        px[y * size + x] = new Color(hue.r * a, hue.g * a, hue.b * a, a);
                    }
                }
                beamTex.SetPixels(px);
                beamTex.Apply();
            }
            return beamTex;
        }

        // faint additive wash hugging the crack outline (branches included — not
        // gated on `lit`): only a whisper of warm spill past the rim so the
        // surrounding street stays dark and the glow reads as coming from below,
        // never as a painted outline. Flicker simmers alpha with SetGlow/flashBoost.
        private void SpawnGlowStrip()
        {
            Material src = FindEmberMaterial();
            if (src == null)
            {
                return;
            }
            var go = new GameObject("CrackGlowStrip");
            go.transform.SetParent(transform, false);

            int n = zs.Length;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var cols = new Color32[n * 2];
            // how far the wash reaches past the rim onto the street — tight, so
            // the crack keeps a clean edge instead of a glowing halo
            float spread = 0.09f + maxWidth * 0.35f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                // fade toward the tips with the same pinch as the trench itself
                float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.8f);
                float y = surf[i] + 0.03f; // just above the mask/rim, no z-fight
                verts[i * 2] = new Vector3(xs[i] - halfL[i] - spread, y, zs[i]);
                verts[i * 2 + 1] = new Vector3(xs[i] + halfR[i] + spread, y, zs[i]);
                uvs[i * 2] = new Vector2(0f, t);
                uvs[i * 2 + 1] = new Vector2(1f, t);
                byte a = (byte)(env * 255f);
                cols[i * 2] = new Color32(255, 255, 255, a);
                cols[i * 2 + 1] = new Color32(255, 255, 255, a);
            }
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int m = i * 6;
                int v = i * 2;
                tris[m] = v; tris[m + 1] = v + 2; tris[m + 2] = v + 3;
                tris[m + 3] = v; tris[m + 4] = v + 3; tris[m + 5] = v + 1;
            }
            var mesh = new Mesh { vertices = verts, uv = uvs, colors32 = cols, triangles = tris };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            stripMat = new Material(src) { mainTexture = StripTexture() };
            Color c;
            if (hasEmission)
            {
                float mx = Mathf.Max(lavaBaseEmission.r,
                    Mathf.Max(lavaBaseEmission.g, Mathf.Max(lavaBaseEmission.b, 0.001f)));
                c = lavaBaseEmission / mx;
            }
            else
            {
                c = new Color(1f, 0.35f, 0.1f);
            }
            // dim and saturated: the wash is residual heat bleeding past the rim,
            // not a light source (HDR white here bloomed the whole frame milky)
            stripColor = Color.Lerp(c, Color.white, 0.3f) * 0.6f;
            if (!lit)
            {
                // branches/capillaries: half-strength wash, or a whole network's
                // worth of additive strips blooms the entire frame milky
                stripColor *= 0.55f;
            }
            stripColor.a = 1f;
            var c0 = stripColor;
            c0.a = 0f;
            stripMat.color = c0;
            mr.sharedMaterial = stripMat;
        }

        private static Texture2D StripTexture()
        {
            // bright core over the crack line fading to nothing at the outer
            // edges; constant along the length (v) — the tip fade lives in the
            // mesh vertex alpha. Same static-cache pattern as BeamTexture.
            if (stripTex == null)
            {
                const int size = 64;
                stripTex = new Texture2D(size, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color[size];
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f);
                    float d = Mathf.Abs(u - 0.5f) * 2f;
                    float a = Mathf.Pow(1f - d, 1.6f);
                    // heat hue baked in: near-white over the crack line, orange
                    // mid-falloff, deep red at the outer feather
                    Color hue = d < 0.4f
                        ? Color.Lerp(new Color(1f, 0.9f, 0.72f), new Color(1f, 0.5f, 0.12f), d / 0.4f)
                        : Color.Lerp(new Color(1f, 0.5f, 0.12f), new Color(0.6f, 0.1f, 0.02f), (d - 0.4f) / 0.6f);
                    px[x] = new Color(hue.r * a, hue.g * a, hue.b * a, a);
                }
                stripTex.SetPixels(px);
                stripTex.Apply();
            }
            return stripTex;
        }

        // soft dark contact wash hugging the rim just under the glow strip —
        // fake ambient occlusion that grounds the broken edges and rim shards
        // and gives the trench visual depth before the additive glow lands on top
        private void SpawnAOStrip()
        {
            var go = new GameObject("CrackAOStrip");
            go.transform.SetParent(transform, false);

            int n = zs.Length;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var cols = new Color32[n * 2];
            float spread = 0.12f + maxWidth * 0.55f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.8f);
                float y = surf[i] + 0.025f;
                verts[i * 2] = new Vector3(xs[i] - halfL[i] - spread, y, zs[i]);
                verts[i * 2 + 1] = new Vector3(xs[i] + halfR[i] + spread, y, zs[i]);
                uvs[i * 2] = new Vector2(0f, t);
                uvs[i * 2 + 1] = new Vector2(1f, t);
                byte a = (byte)(env * 255f);
                cols[i * 2] = new Color32(255, 255, 255, a);
                cols[i * 2 + 1] = new Color32(255, 255, 255, a);
            }
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int m = i * 6;
                int v = i * 2;
                tris[m] = v; tris[m + 1] = v + 2; tris[m + 2] = v + 3;
                tris[m + 3] = v; tris[m + 4] = v + 3; tris[m + 5] = v + 1;
            }
            var mesh = new Mesh { vertices = verts, uv = uvs, colors32 = cols, triangles = tris };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // alpha-blended black using the dust material's shader; must draw
            // before every additive glow (they sit at 3000+)
            Material src = dustMat != null ? dustMat : FindEmberMaterial();
            if (src == null)
            {
                return;
            }
            var mat = new Material(src) { mainTexture = StripTexture() };
            mat.color = new Color(0f, 0f, 0f, 0.5f);
            mat.renderQueue = 2995;
            mr.sharedMaterial = mat;
        }

        // slow grey ash flakes floating up out of the fissure and drifting on
        // noise — the quiet layer between the bright embers and the dark smoke
        private void SpawnAsh()
        {
            if (!lit)
            {
                return;
            }
            var go = new GameObject("CrackAsh");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(
                0f, surf[surf.Length / 2] + 0.05f, length * 0.5f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new Color(0.32f, 0.30f, 0.29f, 0.8f);
            main.gravityModifier = -0.008f;
            main.maxParticles = 40;

            var em = ps.emission;
            em.rateOverTime = Mathf.Clamp(length * 1.2f, 4f, 10f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(maxWidth, 0.05f, length * 0.8f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.16f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.3f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.3f, 0.29f, 0.28f), 0f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.7f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            if (dustMat != null)
            {
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMat;
            }
            ps.Play();
        }

        // faint glowing dust: dim warm motes seeping up out of the seam and
        // hanging low over the crack, like heated air carrying fine embers
        private void SpawnHeatDust()
        {
            if (!lit)
            {
                return;
            }
            var go = new GameObject("CrackHeatDust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(
                0f, surf[surf.Length / 2] + 0.02f, length * 0.5f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new Color(1f, 0.42f, 0.12f, 0.22f);
            main.gravityModifier = -0.012f;
            main.maxParticles = 30;

            var em = ps.emission;
            em.rateOverTime = Mathf.Clamp(length * 1.4f, 5f, 12f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(maxWidth * 0.9f, 0.04f, length * 0.85f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.6f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0f),
                    new GradientColorKey(new Color(0.8f, 0.2f, 0.04f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.5f, 0.25f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            Material m = FindEmberMaterial();
            if (m != null)
            {
                psr.sharedMaterial = m;
            }
            ps.Play();
        }

        // heat-haze ribbon over the seam: a vertical strip along the crack that
        // re-samples the opaque scene colour through scrolling noise. Only when
        // the active URP asset provides the opaque texture (PC tier) — on the
        // mobile tier the copy costs more than the shimmer is worth
        private void SpawnHeatShimmer()
        {
            if (!lit)
            {
                return;
            }
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (rp == null || !rp.supportsCameraOpaqueTexture)
            {
                return;
            }
            var shader = Shader.Find("Collapse/HeatDistortion");
            if (shader == null)
            {
                return;
            }

            var go = new GameObject("CrackHeatShimmer");
            go.transform.SetParent(transform, false);
            int n = zs.Length;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var cols = new Color[n * 2];
            float h = Mathf.Clamp(0.3f + maxWidth * 1.4f, 0.35f, 0.9f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.8f);
                verts[i * 2] = new Vector3(xs[i], surf[i] + 0.02f, zs[i]);
                verts[i * 2 + 1] = new Vector3(xs[i], surf[i] + 0.02f + h * (0.4f + 0.6f * env), zs[i]);
                uvs[i * 2] = new Vector2(t * 3f, 0f);
                uvs[i * 2 + 1] = new Vector2(t * 3f, 1f);
                cols[i * 2] = new Color(1f, 1f, 1f, env);
                cols[i * 2 + 1] = new Color(1f, 1f, 1f, env);
            }
            var tris = new int[(n - 1) * 12];
            for (int i = 0; i < n - 1; i++)
            {
                int m = i * 12;
                int v = i * 2;
                // both windings — the ribbon must show from every side
                tris[m] = v; tris[m + 1] = v + 1; tris[m + 2] = v + 3;
                tris[m + 3] = v; tris[m + 4] = v + 3; tris[m + 5] = v + 2;
                tris[m + 6] = v; tris[m + 7] = v + 3; tris[m + 8] = v + 1;
                tris[m + 9] = v; tris[m + 10] = v + 2; tris[m + 11] = v + 3;
            }
            var mesh = new Mesh { vertices = verts, uv = uvs, colors = cols, triangles = tris };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = new Material(shader) { mainTexture = null };
            mat.SetTexture("_NoiseTex", HeatNoiseTexture());
            mat.SetFloat("_Strength", 0.012f);
            mr.sharedMaterial = mat;
        }

        private static Texture2D HeatNoiseTexture()
        {
            if (heatNoiseTex == null)
            {
                const int size = 128;
                heatNoiseTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Repeat,
                };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float r = Mathf.PerlinNoise(x * 0.11f, y * 0.11f);
                        float g = Mathf.PerlinNoise(x * 0.11f + 37.2f, y * 0.11f + 11.8f);
                        px[y * size + x] = new Color(r, g, 0f, 1f);
                    }
                }
                heatNoiseTex.SetPixels(px);
                heatNoiseTex.Apply();
            }
            return heatNoiseTex;
        }

        // jagged centreline + independent left/right widths and depth per station;
        // envelope pinches everything to a point at both ends
        private void BuildProfile()
        {
            // fine stations + Perlin-driven widths: the outline flows and wanders
            // like a natural fracture instead of the old coarse angular zigzag
            // (per-station Random widths at 0.45m spacing read as big flat facets)
            int n = Mathf.Max(14, Mathf.CeilToInt(length / 0.22f));
            xs = new float[n];
            halfL = new float[n];
            halfR = new float[n];
            depth = new float[n];
            zs = new float[n];
            surf = new float[n];
            float seedBend = Random.value * 100f;
            float seedL = Random.value * 100f;
            float seedR = Random.value * 100f;
            float seedD = Random.value * 100f;
            float bendFreq = Mathf.Clamp(length * 0.35f, 1.5f, 4f);   // a few slow bends
            float toothFreq = Mathf.Clamp(length * 1.1f, 6f, 14f);    // ~0.7m teeth
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                zs[i] = t * length;
                // clamp: Sin(PI) is a hair below zero and Pow(negative) is NaN
                float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.6f);
                float bend = (Mathf.PerlinNoise(seedBend, t * bendFreq) - 0.5f) * 2f;
                xs[i] = bend * wanderLimit * Mathf.Sin(t * Mathf.PI)
                    + Random.Range(-0.04f, 0.04f) * env;
                // tips close to (near) zero width and depth — the trench has no end
                // caps, so an open tip would show the skybox straight through.
                // Pow sharpens the tooth contrast (some stations pinch to slivers,
                // others gape) and the |2n-1| term adds fine hard notches, so the
                // rim reads as sharp fracture teeth with strongly varied width
                float toothL = Mathf.Pow(Mathf.PerlinNoise(seedL, t * toothFreq), 1.5f);
                float toothR = Mathf.Pow(Mathf.PerlinNoise(seedR, t * toothFreq), 1.5f);
                float notchL = Mathf.Abs(Mathf.PerlinNoise(seedL * 1.7f + 3.1f, t * toothFreq * 3.2f) * 2f - 1f);
                float notchR = Mathf.Abs(Mathf.PerlinNoise(seedR * 1.7f + 5.9f, t * toothFreq * 3.2f) * 2f - 1f);
                halfL[i] = Mathf.Max(0.002f, env * maxWidth * 0.5f
                    * (0.3f + 1.05f * toothL + 0.35f * notchL));
                halfR[i] = Mathf.Max(0.002f, env * maxWidth * 0.5f
                    * (0.3f + 1.05f * toothR + 0.35f * notchR));
                depth[i] = 0.05f + env
                    * (0.75f + 0.85f * Mathf.PerlinNoise(seedD, t * toothFreq * 0.7f));
                surf[i] = SampleGround(transform.TransformPoint(new Vector3(xs[i], 0f, zs[i])));
            }
        }

        // the street is a patchwork of surfaces at different heights (side asphalt
        // y=0, centre slab field y=-0.25, sunken mid-street y=-0.65), so each
        // station snaps to the ground actually under it — ignoring debris lying on
        // top — and the crack follows the terrain instead of sinking below it
        private float SampleGround(Vector3 worldPos)
        {
            float best = float.MinValue;
            var hits = Physics.RaycastAll(
                new Vector3(worldPos.x, 4f, worldPos.z), Vector3.down, 10f);
            foreach (var hit in hits)
            {
                if (hit.point.y <= 0.15f && hit.point.y >= -0.8f && hit.point.y > best)
                {
                    best = hit.point.y;
                }
            }
            if (best == float.MinValue)
            {
                best = -0.25f;
            }
            // visual-only centre slabs (top y=-0.25) have no colliders; never sit
            // below them or the crack vanishes under the slab field
            return Mathf.Max(best, -0.25f);
        }

        private void BuildMeshes()
        {
            int n = zs.Length;

            var maskV = new Vector3[n * 2];
            var wallV = new Vector3[n * 3];
            var wallUV = new Vector2[n * 3];
            var lavaV = new Vector3[n * 2];
            var lavaUV = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                // mask floats a hair above the local surface; the rim sits just
                // under it and overhangs the mask outline so no background sliver
                // peeks through the gap at grazing angles
                float maskY = surf[i] + 0.02f;
                float rimY = surf[i] + 0.015f;
                var l = new Vector3(xs[i] - halfL[i] - 0.06f, rimY, zs[i]);
                var r = new Vector3(xs[i] + halfR[i] + 0.06f, rimY, zs[i]);
                var b = new Vector3(xs[i], surf[i] - depth[i], zs[i]);
                // mask ends a touch short of the rim so nothing peeks through the
                // hover gap at the very tips
                float maskZ = zs[i] + (i == 0 ? 0.08f : i == n - 1 ? -0.08f : 0f);
                maskV[i * 2] = new Vector3(l.x, maskY, maskZ);
                maskV[i * 2 + 1] = new Vector3(r.x, maskY, maskZ);
                wallV[i * 3] = l;
                wallV[i * 3 + 1] = r;
                wallV[i * 3 + 2] = b;
                // wall gradient: uv.y 0 at the rim verts, 1 at the bottom vert —
                // the CrackWall shader pools its glow at the trench bottom and
                // leaves the rim a thin dark lip; uv.x = position along the crack
                wallUV[i * 3] = new Vector2(t, 0f);
                wallUV[i * 3 + 1] = new Vector2(t, 0f);
                wallUV[i * 3 + 2] = new Vector2(t, 1f);
                // seam sits DEEP in the trench and stays narrow: a molten pool at
                // the bottom of the crack, glimpsed through the opening rather
                // than a bright band riding near the surface
                lavaV[i * 2] = new Vector3(
                    xs[i] - halfL[i] * 0.45f, surf[i] - depth[i] * 0.78f, zs[i]);
                lavaV[i * 2 + 1] = new Vector3(
                    xs[i] + halfR[i] * 0.45f, surf[i] - depth[i] * 0.78f, zs[i]);
                // u spans the seam width so the heat-gradient texture puts the
                // white-hot core down the centreline, red at the rims
                lavaUV[i * 2] = new Vector2(0f, i / (n - 1f));
                lavaUV[i * 2 + 1] = new Vector2(1f, i / (n - 1f));
            }

            var maskT = new int[(n - 1) * 6];
            var lavaT = new int[(n - 1) * 6];
            var wallT = new int[(n - 1) * 12];
            for (int i = 0; i < n - 1; i++)
            {
                int m = i * 6;
                int v = i * 2;
                // flat strip facing up
                maskT[m] = v; maskT[m + 1] = v + 2; maskT[m + 2] = v + 3;
                maskT[m + 3] = v; maskT[m + 4] = v + 3; maskT[m + 5] = v + 1;
                lavaT[m] = v; lavaT[m + 1] = v + 2; lavaT[m + 2] = v + 3;
                lavaT[m + 3] = v; lavaT[m + 4] = v + 3; lavaT[m + 5] = v + 1;
                // V-walls facing inward
                int w = i * 12;
                int a = i * 3;
                wallT[w] = a; wallT[w + 1] = a + 5; wallT[w + 2] = a + 2;         // left lower
                wallT[w + 3] = a; wallT[w + 4] = a + 3; wallT[w + 5] = a + 5;     // left upper
                wallT[w + 6] = a + 1; wallT[w + 7] = a + 2; wallT[w + 8] = a + 5; // right lower
                wallT[w + 9] = a + 1; wallT[w + 10] = a + 5; wallT[w + 11] = a + 4; // right upper
            }

            // authored gradient instead of real lighting: near-black at the rim,
            // warm emissive pooling at the bottom — the glow originates deep in
            // the trench and fades out on the way up, and the surface keeps a
            // thin clean lit inner edge instead of a fully illuminated wall
            var wallShader = Shader.Find("Collapse/CrackWall");
            if (wallShader != null)
            {
                wallMat = new Material(wallShader);
                if (hasEmission || lavaSource == null)
                {
                    // tint the wall glow from the lava so seam + walls agree
                    Color e = lavaSource != null && lavaSource.HasProperty("_EmissionColor")
                        ? lavaSource.GetColor("_EmissionColor")
                        : new Color(1f, 0.42f, 0.1f);
                    float m = Mathf.Max(e.r, Mathf.Max(e.g, Mathf.Max(e.b, 0.001f)));
                    Color w = Color.Lerp(e / m, new Color(1f, 0.62f, 0.2f), 0.5f);
                    wallMat.SetColor("_GlowColor", w * 2.6f);
                }
            }
            else
            {
                wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                wallMat.SetColor("_BaseColor", new Color(0.09f, 0.075f, 0.065f));
                wallMat.SetFloat("_Smoothness", 0f);
            }
            wallMat.renderQueue = WallQueue;

            Material lavaInst = lavaSource != null
                ? new Material(lavaSource)
                : wallMat;
            lavaInst.renderQueue = LavaQueue;
            lavaInstance = lavaInst;
            if (lavaInst.HasProperty("_EmissionColor"))
            {
                lavaBaseEmission = lavaInst.GetColor("_EmissionColor");
                hasEmission = true;
                // layered heat: gradient texture modulates both base and emission
                // so the seam burns white-hot down the middle, orange, then deep
                // red at the rims instead of one flat colour
                var grad = LavaGradientTexture();
                if (lavaInst.HasProperty("_BaseMap"))
                {
                    lavaInst.SetTexture("_BaseMap", grad);
                }
                if (lavaInst.HasProperty("_EmissionMap"))
                {
                    lavaInst.SetTexture("_EmissionMap", grad);
                }
            }

            var maskMat = new Material(Shader.Find("Collapse/DepthMask"));
            maskMat.renderQueue = MaskQueue;

            MakePart("Walls", wallV, wallT, wallMat, wallUV);
            MakePart("Lava", lavaV, lavaT, lavaInst, lavaUV);
            MakePart("Mask", maskV, maskT, maskMat);
        }

        // u: white-hot core -> vibrant orange -> deep red at the edges.
        // v (position along the crack): brightness envelope that pools the heat
        // at the CENTRE of the fissure and cools toward the tips, so the seam
        // reads as one subtle molten pool at the bottom rather than a uniform
        // bright band. Multiplied by the authored HDR emission, SetGlow still works.
        private static Texture2D LavaGradientTexture()
        {
            if (lavaGradTex == null)
            {
                const int size = 64;
                lavaGradTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    float v = y / (size - 1f);
                    float pool = 0.25f + 0.75f * Mathf.Pow(
                        Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)), 1.6f);
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Abs(x / (size - 1f) * 2f - 1f); // 0 core, 1 edge
                        Color c;
                        if (d < 0.35f)
                        {
                            c = Color.Lerp(new Color(1f, 0.97f, 0.88f), new Color(1f, 0.62f, 0.16f), d / 0.35f);
                        }
                        else if (d < 0.75f)
                        {
                            c = Color.Lerp(new Color(1f, 0.62f, 0.16f), new Color(0.75f, 0.16f, 0.02f), (d - 0.35f) / 0.4f);
                        }
                        else
                        {
                            c = Color.Lerp(new Color(0.75f, 0.16f, 0.02f), new Color(0.35f, 0.04f, 0.01f), (d - 0.75f) / 0.25f);
                        }
                        c *= pool;
                        c.a = 1f;
                        px[y * size + x] = c;
                    }
                }
                lavaGradTex.SetPixels(px);
                lavaGradTex.Apply();
            }
            return lavaGradTex;
        }

        private void MakePart(string name, Vector3[] verts, int[] tris, Material mat, Vector2[] uvs = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { vertices = verts, triangles = tris };
            if (uvs != null)
            {
                mesh.uv = uvs;
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // slow ember drift out of the fissure, borrowing the scene's ember material
        private void SpawnEmbers()
        {
            var go = new GameObject("CrackEmbers");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(
                0f, surf[surf.Length / 2] + 0.05f, length * 0.5f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
            main.startColor = new Color(1f, 0.45f, 0.12f);
            main.gravityModifier = -0.03f;
            main.maxParticles = 60;

            var em = ps.emission;
            em.rateOverTime = Mathf.Clamp(length * 1.2f, 5f, 12f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(maxWidth * 0.8f, 0.05f, length * 0.85f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0f),
                    new GradientColorKey(new Color(0.7f, 0.12f, 0.02f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.1f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            Material emberMat = FindEmberMaterial();
            if (emberMat != null)
            {
                psr.sharedMaterial = emberMat;
            }
            ps.Play();
        }

        private Material FindEmberMaterial()
        {
            var embersRef = GameObject.Find("Embers");
            if (embersRef != null && embersRef.TryGetComponent(out ParticleSystemRenderer src))
            {
                return src.sharedMaterial;
            }
            return dustMat;
        }

        // hot sparks popping up out of the seam — faster and brighter than the
        // ember drift, arcing back down under gravity with short glowing trails
        private void SpawnSparks()
        {
            var go = new GameObject("CrackSparks");
            go.transform.SetParent(transform, false);
            // -90 on X points local +Z (the emit direction) straight up
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localPosition = new Vector3(
                0f, surf[surf.Length / 2] + 0.05f, length * 0.5f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = new Color(1f, 0.75f, 0.3f);
            main.gravityModifier = 0.55f;
            main.maxParticles = 30;

            var em = ps.emission;
            em.rateOverTime = Mathf.Clamp(length * 0.5f, 2f, 5f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // after the -90 X spin, local x = crack width, local y = along crack
            shape.scale = new Vector3(maxWidth * 0.7f, length * 0.85f, 0.05f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.8f, 0.35f), 0f),
                    new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            // stretched billboards turn each spark into a motion streak (like the
            // reference) — cheaper than the trails module on mobile
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.lengthScale = 5f;
            psr.velocityScale = 0.02f;
            Material sparkMat = FindEmberMaterial();
            if (sparkMat != null)
            {
                psr.sharedMaterial = sparkMat;
            }
            ps.Play();
        }

        // soft dark smoke simmering out of the widest part of the crack, hanging
        // around the beam like in the reference
        private void SpawnSmoke()
        {
            if (!lit)
            {
                return;
            }
            var go = new GameObject("CrackSmoke");
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            int mid = zs.Length / 2;
            go.transform.localPosition = new Vector3(xs[mid], surf[mid] + 0.05f, zs[mid]);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.16f, 0.14f, 0.13f, 0.25f);
            main.maxParticles = 20;

            var em = ps.emission;
            em.rateOverTime = Mathf.Clamp(length * 0.8f, 3f, 6f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // after the -90 X spin, local x = crack width, local y = along crack
            shape.scale = new Vector3(maxWidth, length * 0.35f, 0.05f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.15f, 0.13f, 0.12f), 0f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.25f, 0.2f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            if (dustMat != null)
            {
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMat;
            }
            ps.Play();
        }

        // the crack shoots along the ground, then yawns open with a jolt
        private IEnumerator Open()
        {
            transform.localScale = new Vector3(0.04f, 1f, 0.1f);
            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.18f), 3f);
                transform.localScale = new Vector3(0.04f, 1f, Mathf.Lerp(0.1f, 1f, k));
                yield return null;
            }

            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.02f, 0.45f);
            }
            flashBoost = 1.2f; // brief surge as the trench yawns open — restrained
            StartCoroutine(BeamBurst());
            BurstDressing();

            t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.35f), 3f);
                transform.localScale = new Vector3(Mathf.Lerp(0.04f, 1f, k), 1f, 1f);
                yield return null;
            }
            transform.localScale = Vector3.one;
            SpawnRimShards();

            if (branch && length > 5f)
            {
                int branches = Random.Range(2, 4);
                for (int i = 0; i < branches; i++)
                {
                    float bt = Random.Range(0.25f, 0.75f);
                    int bi = Mathf.RoundToInt(bt * (zs.Length - 1));
                    Vector3 bpos = transform.TransformPoint(new Vector3(xs[bi], 0f, zs[bi]));
                    Vector3 bdir = transform.rotation
                        * Quaternion.Euler(0f, Random.Range(35f, 70f) * (Random.value < 0.5f ? -1f : 1f), 0f)
                        * Vector3.forward;
                    var bfx = Spawn(bpos, bdir, length * Random.Range(0.3f, 0.5f),
                        maxWidth * 0.6f, lavaSource, dustMat, debrisMeshes, false);
                    bfx.lit = false; // main crack's lights cover the branch area
                }
                // hairline offshoots — short, thin, barely-glowing capillaries
                // that make the network read as naturally fractured
                int micro = Random.Range(2, 4);
                for (int i = 0; i < micro; i++)
                {
                    float bt = Random.Range(0.15f, 0.85f);
                    int bi = Mathf.RoundToInt(bt * (zs.Length - 1));
                    Vector3 bpos = transform.TransformPoint(new Vector3(xs[bi], 0f, zs[bi]));
                    Vector3 bdir = transform.rotation
                        * Quaternion.Euler(0f, Random.Range(20f, 85f) * (Random.value < 0.5f ? -1f : 1f), 0f)
                        * Vector3.forward;
                    var mfx = Spawn(bpos, bdir, length * Random.Range(0.12f, 0.22f),
                        maxWidth * 0.3f, lavaSource, dustMat, null, false, 0.35f);
                    mfx.lit = false;
                }
            }
        }

        // dust puffs and a few rock chips popping off the rim as it opens
        private void BurstDressing()
        {
            int puffs = Mathf.Clamp(Mathf.RoundToInt(length / 3f), 2, 4);
            for (int i = 0; i < puffs; i++)
            {
                float t = (i + 0.5f) / puffs;
                int idx = Mathf.RoundToInt(t * (zs.Length - 1));
                SpawnDustPuff(transform.TransformPoint(
                    new Vector3(xs[idx], surf[idx] + 0.05f, zs[idx])));
            }

            if (debrisMeshes == null || debrisMeshes.Length == 0)
            {
                return;
            }
            int chips = Random.Range(4, 8);
            for (int i = 0; i < chips; i++)
            {
                int idx = Random.Range(1, zs.Length - 1);
                var chip = new GameObject("CrackChip");
                // not parented (the root's scale animates); tracked for cleanup
                spawnedChips.Add(chip);
                var mf = chip.AddComponent<MeshFilter>();
                Mesh mesh = debrisMeshes[Random.Range(0, debrisMeshes.Length)];
                mf.sharedMesh = mesh;
                var mr = chip.AddComponent<MeshRenderer>();
                mr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                float shade = Random.Range(0.12f, 0.2f);
                mr.material.SetColor("_BaseColor", new Color(shade, shade * 0.9f, shade * 0.82f));
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // meshes are ~1-2 units wide, so this lands chips at 1-4 cm
                float s = Random.Range(0.02f, 0.05f);
                chip.transform.localScale = Vector3.one * s;
                chip.transform.position = transform.TransformPoint(
                    new Vector3(xs[idx] + Random.Range(-0.4f, 0.4f), surf[idx] + 0.15f, zs[idx]));
                chip.transform.rotation = Random.rotation;
                var box = chip.AddComponent<BoxCollider>();
                box.size = mesh.bounds.size;
                box.center = mesh.bounds.center;
                var rb = chip.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                rb.linearVelocity = new Vector3(
                    Random.Range(-1f, 1f), Random.Range(1.5f, 3.5f), Random.Range(-1f, 1f));
                rb.angularVelocity = Random.insideUnitSphere * 8f;
                StartCoroutine(FreezeChip(rb));
            }
        }

        // broken street plates lifted along the rim, tilted up toward the crack so
        // their raised inner edges catch the underglow — the reference's displaced
        // shards. Spawned after Open() finishes, so safe to parent (scale is 1).
        private void SpawnRimShards()
        {
            if (debrisMeshes == null || debrisMeshes.Length == 0)
            {
                return;
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            int count = Mathf.Clamp(Mathf.RoundToInt(length / 1.2f), 3, 8);
            for (int i = 0; i < count; i++)
            {
                int idx = Random.Range(2, zs.Length - 2);
                float side = Random.value < 0.5f ? -1f : 1f;
                float half = side < 0f ? halfL[idx] : halfR[idx];
                var shard = new GameObject("RimShard");
                shard.transform.SetParent(transform, false);
                var mf = shard.AddComponent<MeshFilter>();
                Mesh mesh = debrisMeshes[Random.Range(0, debrisMeshes.Length)];
                mf.sharedMesh = mesh;
                var mr = shard.AddComponent<MeshRenderer>();
                // per-shard material: subtle albedo/roughness variation plus real
                // shadows, so the plates stop reading as one uniform grey stamp
                var shardMat = new Material(shader);
                float shade = Random.Range(0.24f, 0.36f);
                shardMat.SetColor("_BaseColor", new Color(
                    shade + Random.Range(0f, 0.04f), shade * 0.94f, shade * 0.86f));
                shardMat.SetFloat("_Smoothness", Random.Range(0.04f, 0.22f));
                mr.sharedMaterial = shardMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
                // meshes are ~1-2 units wide → plates land at 0.2-0.5 world units
                float s = Random.Range(0.18f, 0.4f) * Mathf.Clamp(maxWidth + 0.6f, 0.7f, 1.4f);
                shard.transform.localScale = Vector3.one * s;
                // X-90 lays the plate flat; roll about the crack axis lifts the
                // inner (crack-side) edge so it silhouettes against the glow
                var rot = Quaternion.Euler(
                    -90f + Random.Range(-5f, 5f),
                    Random.Range(-20f, 20f),
                    -side * Random.Range(6f, 16f));
                shard.transform.localRotation = rot;
                var pos = new Vector3(
                    xs[idx] + side * (half + 0.05f), surf[idx] + 0.015f, zs[idx]);
                // debris meshes aren't pivot-centred (up to 1.6 off origin) —
                // re-centre so the plate actually sits on its station
                pos -= rot * (mesh.bounds.center * s);
                shard.transform.localPosition = pos;
            }
        }

        private IEnumerator FreezeChip(Rigidbody rb)
        {
            yield return new WaitForSeconds(2.5f);
            if (rb != null)
            {
                rb.isKinematic = true;
            }
        }

        private void SpawnDustPuff(Vector3 pos)
        {
            var go = new GameObject("CrackDust");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startColor = new Color(0.3f, 0.27f, 0.25f, 0.45f);
            main.maxParticles = 8;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)8) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.25f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.32f, 0.29f, 0.27f), 0f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.45f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            if (dustMat != null)
            {
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMat;
            }
            ps.Play();
        }
    }
}