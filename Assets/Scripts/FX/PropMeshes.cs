using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// Procedural prop builders from Unity primitives — shared by both mini-games so neither
    /// needs its own art pipeline for common pickups/obstacles. No textures, no imported meshes;
    /// everything is CreatePrimitive + scale/offset + a code-built URP Lit material.
    /// Roughly real-world scale (fruit ~0.25m, chair seat 0.45m, tree ~2.5m tall).
    /// </summary>
    public static class PropMeshes
    {
        static Shader s_LitShader;

        static Shader LitShader()
        {
            if (s_LitShader != null) return s_LitShader;
            s_LitShader = Shader.Find("Universal Render Pipeline/Lit");
            if (s_LitShader == null) s_LitShader = Shader.Find("Sprites/Default");
            return s_LitShader;
        }

        static Shader s_UnlitShader;

        static Shader UnlitShader()
        {
            if (s_UnlitShader != null) return s_UnlitShader;
            s_UnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (s_UnlitShader == null) s_UnlitShader = Shader.Find("Sprites/Default");
            return s_UnlitShader;
        }

        /// <summary>
        /// Flat unlit color material. Use for LARGE flat environment surfaces (ground discs,
        /// stage rings): the URP Lit path blows those out to pure white in this project
        /// (verified empirically — same green albedo renders 255-white Lit, perfect Unlit),
        /// and the flat look suits the low-poly style anyway.
        /// </summary>
        public static Material MatUnlit(Color c)
        {
            var mat = new Material(UnlitShader());
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
            return mat;
        }

        /// <summary>Cached-per-call URP Lit material helper (each call makes a new instance — callers may cache the result themselves).</summary>
        public static Material Mat(Color c, float metallic = 0f, float smooth = 0.5f, Color? emission = null)
        {
            var mat = new Material(LitShader());
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            // Grazing-angle fresnel on URP Lit reflects the bright sky and washes large floor
            // surfaces out to white — these props are flat-shaded stylized, so kill environment
            // reflections + speculars outright.
            if (mat.HasProperty("_EnvironmentReflections"))
            {
                mat.SetFloat("_EnvironmentReflections", 0f);
                mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            if (mat.HasProperty("_SpecularHighlights"))
            {
                mat.SetFloat("_SpecularHighlights", 0f);
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
            if (emission.HasValue && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return mat;
        }

        // Works both in-editor (DestroyImmediate, no frame delay) and at runtime (Destroy).
        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, Quaternion? localRot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject NewRoot(string name) => new GameObject(name);

        // ---- Fruit / food pickups --------------------------------------------------------

        public static GameObject Apple()
        {
            var root = NewRoot("Apple");
            Primitive(PrimitiveType.Sphere, "Body", root.transform, Vector3.zero, new Vector3(0.22f, 0.24f, 0.22f), Mat(new Color(0.85f, 0.12f, 0.12f), smooth: 0.6f));
            Primitive(PrimitiveType.Cylinder, "Stem", root.transform, new Vector3(0f, 0.14f, 0f), new Vector3(0.015f, 0.05f, 0.015f), Mat(new Color(0.35f, 0.22f, 0.1f)));
            return root;
        }

        public static GameObject Orange()
        {
            var root = NewRoot("Orange");
            Primitive(PrimitiveType.Sphere, "Body", root.transform, Vector3.zero, new Vector3(0.24f, 0.24f, 0.24f), Mat(new Color(0.95f, 0.55f, 0.08f), smooth: 0.4f));
            Primitive(PrimitiveType.Sphere, "Stem", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.02f, 0.02f, 0.02f), Mat(new Color(0.35f, 0.5f, 0.15f)));
            return root;
        }

        public static GameObject Banana()
        {
            var root = NewRoot("Banana");
            var mat = Mat(new Color(0.95f, 0.85f, 0.15f), smooth: 0.5f);
            // No bent-capsule primitive exists — 3 tilted squashed capsules chained in an arc fake the curve.
            Primitive(PrimitiveType.Capsule, "Seg0", root.transform, new Vector3(-0.08f, 0.02f, 0f), new Vector3(0.06f, 0.14f, 0.06f), mat, Quaternion.Euler(0f, 0f, 20f));
            Primitive(PrimitiveType.Capsule, "Seg1", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(0.06f, 0.15f, 0.06f), mat, Quaternion.identity);
            Primitive(PrimitiveType.Capsule, "Seg2", root.transform, new Vector3(0.08f, 0.02f, 0f), new Vector3(0.06f, 0.14f, 0.06f), mat, Quaternion.Euler(0f, 0f, -20f));
            return root;
        }

        public static GameObject WatermelonSlice()
        {
            var root = NewRoot("WatermelonSlice");
            Primitive(PrimitiveType.Sphere, "Rind", root.transform, Vector3.zero, new Vector3(0.28f, 0.14f, 0.28f), Mat(new Color(0.15f, 0.55f, 0.2f)));
            Primitive(PrimitiveType.Sphere, "Flesh", root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.22f, 0.09f, 0.22f), Mat(new Color(0.9f, 0.2f, 0.25f)));
            return root;
        }

        public static GameObject Broccoli()
        {
            var root = NewRoot("Broccoli");
            var stemMat = Mat(new Color(0.65f, 0.75f, 0.4f));
            var headMat = Mat(new Color(0.15f, 0.4f, 0.12f));
            Primitive(PrimitiveType.Cylinder, "Stem", root.transform, Vector3.zero, new Vector3(0.03f, 0.08f, 0.03f), stemMat);
            Primitive(PrimitiveType.Sphere, "Head0", root.transform, new Vector3(0f, 0.14f, 0f), new Vector3(0.09f, 0.09f, 0.09f), headMat);
            Primitive(PrimitiveType.Sphere, "Head1", root.transform, new Vector3(0.05f, 0.11f, 0.03f), new Vector3(0.07f, 0.07f, 0.07f), headMat);
            Primitive(PrimitiveType.Sphere, "Head2", root.transform, new Vector3(-0.05f, 0.11f, -0.03f), new Vector3(0.07f, 0.07f, 0.07f), headMat);
            return root;
        }

        public static GameObject SodaCup()
        {
            var root = NewRoot("SodaCup");
            Primitive(PrimitiveType.Cylinder, "Cup", root.transform, Vector3.zero, new Vector3(0.06f, 0.09f, 0.06f), Mat(Color.white, smooth: 0.3f));
            Primitive(PrimitiveType.Cylinder, "Straw", root.transform, new Vector3(0.02f, 0.16f, 0f), new Vector3(0.006f, 0.08f, 0.006f), Mat(new Color(0.9f, 0.1f, 0.15f)));
            return root;
        }

        public static GameObject Burger()
        {
            var root = NewRoot("Burger");
            var bunMat = Mat(new Color(0.8f, 0.6f, 0.3f));
            Primitive(PrimitiveType.Cylinder, "BunTop", root.transform, new Vector3(0f, 0.075f, 0f), new Vector3(0.11f, 0.03f, 0.11f), bunMat);
            Primitive(PrimitiveType.Cylinder, "Patty", root.transform, new Vector3(0f, 0.035f, 0f), new Vector3(0.1f, 0.02f, 0.1f), Mat(new Color(0.3f, 0.15f, 0.08f)));
            Primitive(PrimitiveType.Cylinder, "BunBottom", root.transform, Vector3.zero, new Vector3(0.11f, 0.025f, 0.11f), bunMat);
            return root;
        }

        public static GameObject Donut()
        {
            var root = NewRoot("Donut");
            // No torus primitive — a flattened base cylinder plus a slightly smaller icing cylinder reads as a donut from above.
            Primitive(PrimitiveType.Cylinder, "Base", root.transform, Vector3.zero, new Vector3(0.1f, 0.025f, 0.1f), Mat(new Color(0.75f, 0.55f, 0.35f)));
            Primitive(PrimitiveType.Cylinder, "Icing", root.transform, new Vector3(0f, 0.015f, 0f), new Vector3(0.095f, 0.012f, 0.095f), Mat(new Color(0.95f, 0.5f, 0.65f)));
            return root;
        }

        public static GameObject FriesBox()
        {
            var root = NewRoot("FriesBox");
            Primitive(PrimitiveType.Cube, "Box", root.transform, Vector3.zero, new Vector3(0.08f, 0.1f, 0.06f), Mat(new Color(0.85f, 0.1f, 0.1f)));
            var friesMat = Mat(new Color(0.95f, 0.75f, 0.25f));
            float[] tilts = { -15f, -7f, 0f, 7f, 15f };
            for (int i = 0; i < tilts.Length; i++)
            {
                float x = (i - 2) * 0.015f;
                Primitive(PrimitiveType.Cube, $"Fry{i}", root.transform, new Vector3(x, 0.12f, 0f), new Vector3(0.01f, 0.09f, 0.01f), friesMat, Quaternion.Euler(0f, 0f, tilts[i]));
            }
            return root;
        }

        // ---- Game objects / obstacles ------------------------------------------------------

        public static GameObject Coin()
        {
            var root = NewRoot("Coin");
            var mat = Mat(new Color(1f, 0.85f, 0.2f), metallic: 0.8f, smooth: 0.85f, emission: new Color(0.5f, 0.4f, 0.05f));
            Primitive(PrimitiveType.Cylinder, "Body", root.transform, Vector3.zero, new Vector3(0.09f, 0.01f, 0.09f), mat, Quaternion.Euler(90f, 0f, 0f));
            return root;
        }

        /// <summary>Emissive box-frame arch spanning 3 lanes, with a gap over <paramref name="openLane"/> (-1/0/1) for the player to pass through.</summary>
        public static GameObject GateArch(float laneWidth, int openLane)
        {
            var root = NewRoot("GateArch");
            var mat = Mat(new Color(0.2f, 0.7f, 0.95f), emission: new Color(0.1f, 0.5f, 0.8f));
            const float height = 2.2f;
            float halfSpan = 1.5f * laneWidth;

            Primitive(PrimitiveType.Cube, "PostL", root.transform, new Vector3(-halfSpan, height * 0.5f, 0f), new Vector3(0.08f, height, 0.08f), mat);
            Primitive(PrimitiveType.Cube, "PostR", root.transform, new Vector3(halfSpan, height * 0.5f, 0f), new Vector3(0.08f, height, 0.08f), mat);

            for (int lane = -1; lane <= 1; lane++)
            {
                if (lane == openLane) continue; // gap the player passes through
                float x = lane * laneWidth;
                Primitive(PrimitiveType.Cube, $"Beam{lane}", root.transform, new Vector3(x, height, 0f), new Vector3(laneWidth * 0.95f, 0.08f, 0.08f), mat);
            }
            return root;
        }

        public static GameObject KickTarget()
        {
            var root = NewRoot("KickTarget");
            const float targetY = 0.85f;
            Primitive(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 0.4f, 0f), new Vector3(0.01f, 0.4f, 0.01f), Mat(new Color(0.3f, 0.3f, 0.3f)));
            // 3 concentric discs facing the player (rotated to stand upright), stacked with tiny Z offsets to avoid z-fighting.
            Primitive(PrimitiveType.Cylinder, "Ring0", root.transform, new Vector3(0f, targetY, 0f), new Vector3(0.18f, 0.01f, 0.18f), Mat(new Color(0.9f, 0.1f, 0.1f)), Quaternion.Euler(90f, 0f, 0f));
            Primitive(PrimitiveType.Cylinder, "Ring1", root.transform, new Vector3(0f, targetY, 0.006f), new Vector3(0.12f, 0.01f, 0.12f), Mat(Color.white), Quaternion.Euler(90f, 0f, 0f));
            Primitive(PrimitiveType.Cylinder, "Ring2", root.transform, new Vector3(0f, targetY, 0.012f), new Vector3(0.06f, 0.01f, 0.06f), Mat(new Color(0.9f, 0.1f, 0.1f)), Quaternion.Euler(90f, 0f, 0f));
            return root;
        }

        public static GameObject BridgePlank(float length)
        {
            var root = NewRoot("BridgePlank");
            Primitive(PrimitiveType.Cube, "Plank", root.transform, Vector3.zero, new Vector3(0.4f, 0.05f, length), Mat(new Color(0.6f, 0.45f, 0.25f), emission: new Color(0.15f, 0.35f, 0.5f)));
            return root;
        }

        public static GameObject Chair()
        {
            var root = NewRoot("Chair");
            var wood = Mat(new Color(0.55f, 0.35f, 0.2f));
            Primitive(PrimitiveType.Cube, "Seat", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.05f, 0.45f), wood);
            Vector3[] legOffsets =
            {
                new Vector3( 0.18f, 0.2f,  0.18f), new Vector3(-0.18f, 0.2f,  0.18f),
                new Vector3( 0.18f, 0.2f, -0.18f), new Vector3(-0.18f, 0.2f, -0.18f)
            };
            for (int i = 0; i < legOffsets.Length; i++)
                Primitive(PrimitiveType.Cube, $"Leg{i}", root.transform, legOffsets[i], new Vector3(0.04f, 0.4f, 0.04f), wood);
            Primitive(PrimitiveType.Cube, "Backrest", root.transform, new Vector3(0f, 0.7f, -0.2f), new Vector3(0.45f, 0.5f, 0.05f), wood);
            return root;
        }

        public static GameObject Tree()
        {
            var root = NewRoot("Tree");
            Primitive(PrimitiveType.Cylinder, "Trunk", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.15f, 0.6f, 0.15f), Mat(new Color(0.4f, 0.28f, 0.15f)));
            var leafMat = Mat(new Color(0.2f, 0.5f, 0.2f));
            Primitive(PrimitiveType.Sphere, "Leaves0", root.transform, new Vector3(0f, 1.7f, 0f), new Vector3(0.9f, 0.9f, 0.9f), leafMat);
            Primitive(PrimitiveType.Sphere, "Leaves1", root.transform, new Vector3(0.4f, 1.4f, 0.1f), new Vector3(0.65f, 0.65f, 0.65f), leafMat);
            Primitive(PrimitiveType.Sphere, "Leaves2", root.transform, new Vector3(-0.35f, 1.5f, -0.2f), new Vector3(0.6f, 0.6f, 0.6f), leafMat);
            return root;
        }

        public static GameObject Balloon(Color c)
        {
            var root = NewRoot("Balloon");
            Primitive(PrimitiveType.Sphere, "Body", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.16f, 0.2f, 0.16f), Mat(c, smooth: 0.7f));
            // No cone primitive — a small squashed cylinder approximates the tie-off knot.
            Primitive(PrimitiveType.Cylinder, "Knot", root.transform, Vector3.zero, new Vector3(0.02f, 0.015f, 0.02f), Mat(c));

            var stringGo = new GameObject("String");
            stringGo.transform.SetParent(root.transform, false);
            var line = stringGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(0f, -0.02f, 0f));
            line.SetPosition(1, new Vector3(0f, -0.5f, 0f));
            line.startWidth = 0.004f;
            line.endWidth = 0.004f;
            line.sharedMaterial = Mat(new Color(0.85f, 0.85f, 0.85f));

            return root;
        }
    }
}
