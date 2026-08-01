using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// A cheap gradient sky dome for game scenes that don't need a full skybox pipeline —
    /// static inward-facing hemisphere mesh, one small gradient texture, one Unlit material.
    /// No art assets, no scene edits — same runtime-built philosophy as RingGauge/CorrectEffect.
    /// </summary>
    public static class ProceduralSkybox
    {
        const int LatSegments = 12;
        const int LonSegments = 24;
        const int GradientTexHeight = 64;

        /// <summary>Builds a dome mesh + material and returns the GameObject holding them. Static geometry — no per-frame cost.</summary>
        public static GameObject CreateSkyDome(Color top, Color horizon, float radius = 60f)
        {
            var go = new GameObject("SkyDome");
            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = BuildDomeMesh(radius);
            meshRenderer.sharedMaterial = BuildDomeMaterial(top, horizon);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return go;
        }

        /// <summary>Sets RenderSettings to trilight ambient (sky/equator/ground) so the dome colours also tint scene lighting.</summary>
        public static void SetAmbient(Color sky, Color equator, Color ground)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            // The scene otherwise keeps Unity's default (bright) skybox as the environment
            // reflection source — at grazing angles it washes every upward-facing surface,
            // including imported avatar materials, out to white. These stages are flat-shaded
            // stylized; kill reflections scene-wide.
            RenderSettings.skybox = null;
            RenderSettings.reflectionIntensity = 0f;
        }

        // Upper hemisphere, apex at +radius Y, rim at Y=0. Winding is reversed vs. an outward
        // sphere so the recalculated normals face INWARD, toward a camera standing at the origin.
        static Mesh BuildDomeMesh(float radius)
        {
            var verts = new Vector3[(LatSegments + 1) * (LonSegments + 1)];
            var uvs = new Vector2[verts.Length];

            for (int i = 0; i <= LatSegments; i++)
            {
                float v = (float)i / LatSegments; // 0 at rim (horizon) .. 1 at apex
                float phi = v * Mathf.PI * 0.5f;
                float y = radius * Mathf.Sin(phi);
                float ringR = radius * Mathf.Cos(phi);
                for (int j = 0; j <= LonSegments; j++)
                {
                    float u = (float)j / LonSegments;
                    float theta = u * Mathf.PI * 2f;
                    int idx = i * (LonSegments + 1) + j;
                    verts[idx] = new Vector3(ringR * Mathf.Cos(theta), y, ringR * Mathf.Sin(theta));
                    uvs[idx] = new Vector2(u, v);
                }
            }

            var tris = new int[LatSegments * LonSegments * 6];
            int t = 0;
            for (int i = 0; i < LatSegments; i++)
            {
                for (int j = 0; j < LonSegments; j++)
                {
                    int a = i * (LonSegments + 1) + j;
                    int b = a + 1;
                    int c = a + (LonSegments + 1);
                    int d = c + 1;
                    // Wound so the INSIDE of the dome is the front face — the camera sits inside
                    // it, and the first winding rendered the sky invisible (backface-culled).
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = d; tris[t++] = c;
                }
            }

            var mesh = new Mesh { name = "SkyDome" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Vertical gradient: bottom row (v=0) = horizon, top row (v=1) = top colour, matching the dome's UVs.
        static Texture2D BuildGradientTexture(Color top, Color horizon)
        {
            var tex = new Texture2D(1, GradientTexHeight, TextureFormat.RGBA32, false);
            for (int y = 0; y < GradientTexHeight; y++)
            {
                float v = (float)y / (GradientTexHeight - 1);
                tex.SetPixel(0, y, Color.Lerp(horizon, top, v));
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        static Material BuildDomeMaterial(Color top, Color horizon)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);

            var tex = BuildGradientTexture(top, horizon);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            return mat;
        }
    }
}
