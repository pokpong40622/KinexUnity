using TMPro;
using UnityEngine;
using Kinex.FX;

namespace Kinex.AstroStance
{
    /// <summary>
    /// Procedural placeholder props for AstroStance — meteor, treasure crate, kick ring,
    /// grabber tool, telegraph shadow. Built from primitives + HDR emissive materials so
    /// the scene bloom carries them visually. This class is the single swap point when
    /// the Meshy-generated models (meteor.glb, treasure_crate.glb, grabber_tool.glb …)
    /// arrive: each builder returns one root GameObject.
    /// </summary>
    public static class AstroProps
    {
        // Palette (matches the UI spec in the plan).
        public static readonly Color CyanGlow = new Color(0.30f, 0.89f, 1.00f);
        public static readonly Color StarGold = new Color(1.00f, 0.82f, 0.33f);
        public static readonly Color MeteorOrange = new Color(1.00f, 0.42f, 0.21f);

        // ---- Meshy GLB models (Resources/AstroModels/*), with the procedural builders below as
        // fallback if a model is missing. Loaded prefabs are normalized to a target size and
        // grounded (base at local y=0) so the spawner's fall/hover offsets line up. ----

        /// <summary>Instantiate a Meshy model from Resources inside a fresh wrapper root (kept at
        /// origin), scaled so its largest axis == targetSize and offset so the model is centered
        /// X/Z with its base at the wrapper's local y=0 — so the spawner's fall/hover offsets line
        /// up regardless of the GLB's own pivot. Null if the model isn't present (caller falls
        /// back to the procedural placeholder).</summary>
        static GameObject LoadModel(string name, float targetSize)
        {
            var prefab = Resources.Load<GameObject>("AstroModels/" + name);
            if (prefab == null) return null;

            var root = new GameObject(name);
            var inner = Object.Instantiate(prefab);
            inner.name = "model";
            inner.transform.SetParent(root.transform, false);
            foreach (var l in inner.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in inner.GetComponentsInChildren<Camera>(true)) c.enabled = false;

            var rends = inner.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return root;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (maxDim > 1e-4f) inner.transform.localScale *= targetSize / maxDim;

            b = rends[0].bounds; // recompute after scaling (root at origin → world ≈ local)
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            inner.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            return root;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent,
            Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (Application.isPlaying) Object.Destroy(go.GetComponent<Collider>());
            else Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>~0.9 m meteor — Meshy model if present (with an ember trail on top), else a
        /// procedural cratered rock with glowing lava cracks.</summary>
        public static GameObject Meteor()
        {
            var model = LoadModel("meteor", 0.9f);
            if (model != null) { EmberTrail(model.transform); return model; }

            var root = new GameObject("Meteor");
            var rock = PropMeshes.Mat(new Color(0.16f, 0.13f, 0.12f), 0f, 0.25f);
            var lava = PropMeshes.Mat(MeteorOrange, 0f, 0.4f, MeteorOrange * 3.5f);

            Primitive(PrimitiveType.Sphere, "Body", root.transform,
                Vector3.zero, new Vector3(0.8f, 0.68f, 0.74f), rock);
            // Crater bumps — off-axis spheres poking through the body.
            Primitive(PrimitiveType.Sphere, "Bump1", root.transform,
                new Vector3(0.22f, 0.18f, 0.10f), Vector3.one * 0.34f, rock);
            Primitive(PrimitiveType.Sphere, "Bump2", root.transform,
                new Vector3(-0.24f, -0.10f, -0.14f), Vector3.one * 0.28f, rock);
            // Lava cracks — thin emissive slabs crossing the surface.
            Primitive(PrimitiveType.Cube, "Crack1", root.transform,
                new Vector3(0f, 0.02f, 0f), new Vector3(0.82f, 0.035f, 0.10f), lava,
                Quaternion.Euler(12f, 25f, 8f));
            Primitive(PrimitiveType.Cube, "Crack2", root.transform,
                new Vector3(0.05f, -0.08f, 0.05f), new Vector3(0.10f, 0.035f, 0.76f), lava,
                Quaternion.Euler(-8f, -15f, 14f));
            Primitive(PrimitiveType.Sphere, "Core", root.transform,
                new Vector3(0.16f, -0.14f, 0.20f), Vector3.one * 0.18f, lava);

            EmberTrail(root.transform);
            return root;
        }

        static void EmberTrail(Transform parent)
        {
            var go = new GameObject("EmberTrail");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.7f;
            main.startSpeed = 0.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(MeteorOrange, StarGold);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var emission = ps.emission;
            emission.rateOverTime = 26f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.30f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(StarGold, 0f), new GradientColorKey(MeteorOrange, 0.6f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = ParticleMat();
        }

        static Material ParticleMat()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            return new Material(shader);
        }

        /// <summary>~0.7 m treasure — Meshy sci-fi cargo crate if present, else a procedural
        /// gold crate with glowing seams on a hover pallet.</summary>
        public static GameObject Treasure()
        {
            var model = LoadModel("treasure_crate", 0.7f);
            if (model != null) return model;

            var root = new GameObject("Treasure");
            var gold = PropMeshes.Mat(new Color(0.85f, 0.63f, 0.18f), 0.7f, 0.75f);
            var seam = PropMeshes.Mat(StarGold, 0.2f, 0.5f, StarGold * 3.2f);
            var pallet = PropMeshes.Mat(new Color(0.18f, 0.20f, 0.28f), 0.5f, 0.55f);
            var jet = PropMeshes.Mat(CyanGlow, 0f, 0.4f, CyanGlow * 2.6f);

            Primitive(PrimitiveType.Cube, "Crate", root.transform,
                new Vector3(0f, 0.36f, 0f), new Vector3(0.52f, 0.42f, 0.52f), gold);
            Primitive(PrimitiveType.Cube, "Lid", root.transform,
                new Vector3(0f, 0.60f, 0f), new Vector3(0.56f, 0.08f, 0.56f), gold);
            Primitive(PrimitiveType.Cube, "SeamX", root.transform,
                new Vector3(0f, 0.36f, 0f), new Vector3(0.55f, 0.05f, 0.05f), seam);
            Primitive(PrimitiveType.Cube, "SeamZ", root.transform,
                new Vector3(0f, 0.36f, 0f), new Vector3(0.05f, 0.05f, 0.55f), seam);
            Primitive(PrimitiveType.Cube, "Pallet", root.transform,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.62f, 0.10f, 0.62f), pallet);
            Primitive(PrimitiveType.Sphere, "JetL", root.transform,
                new Vector3(-0.24f, 0.03f, 0f), new Vector3(0.10f, 0.05f, 0.10f), jet);
            Primitive(PrimitiveType.Sphere, "JetR", root.transform,
                new Vector3(0.24f, 0.03f, 0f), new Vector3(0.10f, 0.05f, 0.10f), jet);
            return root;
        }

        /// <summary>
        /// ~0.9 m glowing cyan ring the player kicks — 12 emissive orbs in a circle facing
        /// the camera, with a Thai "เตะ!" label (font supplied by the scene builder).
        /// </summary>
        public static GameObject KickRing(TMP_FontAsset thaiFont)
        {
            var model = LoadModel("kick_drone", 0.9f);
            if (model != null) { AddKickLabel(model.transform, thaiFont, 0.7f); return model; }

            var root = new GameObject("KickRing");
            var glow = PropMeshes.Mat(CyanGlow, 0f, 0.5f, CyanGlow * 3.8f);
            const int orbs = 12;
            const float radius = 0.42f;
            for (int i = 0; i < orbs; i++)
            {
                float a = i * Mathf.PI * 2f / orbs;
                // Ring stands upright facing -Z (toward the behind-the-player camera).
                Primitive(PrimitiveType.Sphere, $"Orb{i}", root.transform,
                    new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f),
                    Vector3.one * 0.11f, glow);
            }
            AddKickLabel(root.transform, thaiFont, 0.55f);
            return root;
        }

        // Camera-facing "เตะ!" label floating just above the kick target. A FaceCamera billboard
        // keeps it readable (a fixed 180° yaw mirror-reverses the Thai text on the back camera).
        static void AddKickLabel(Transform parent, TMP_FontAsset thaiFont, float y)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, false);
            labelGo.transform.localPosition = new Vector3(0f, y, 0f);
            labelGo.AddComponent<FaceCamera>();
            var label = labelGo.AddComponent<TextMeshPro>();
            if (thaiFont != null) label.font = thaiFont;
            label.text = "เตะ!";
            label.fontSize = 4.2f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            var rt = label.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1.4f, 0.7f);
        }

        /// <summary>~0.4 m sci-fi grabber the character holds during the sit-grab — Meshy model if
        /// present, else a procedural capsule+claw.</summary>
        public static GameObject GrabberTool()
        {
            var model = LoadModel("grabber_tool", 0.4f);
            if (model != null) return model;

            var root = new GameObject("GrabberTool");
            var metal = PropMeshes.Mat(new Color(0.55f, 0.58f, 0.66f), 0.85f, 0.8f);
            var accent = PropMeshes.Mat(CyanGlow, 0.3f, 0.6f, CyanGlow * 2.4f);

            Primitive(PrimitiveType.Capsule, "Handle", root.transform,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.05f, 0.10f, 0.05f), metal);
            Primitive(PrimitiveType.Cube, "Grip", root.transform,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.07f, 0.05f, 0.07f), accent);
            // Two claw prongs angled outward from the head.
            Primitive(PrimitiveType.Cube, "ProngL", root.transform,
                new Vector3(-0.05f, 0.32f, 0f), new Vector3(0.025f, 0.16f, 0.025f), metal,
                Quaternion.Euler(0f, 0f, 18f));
            Primitive(PrimitiveType.Cube, "ProngR", root.transform,
                new Vector3(0.05f, 0.32f, 0f), new Vector3(0.025f, 0.16f, 0.025f), metal,
                Quaternion.Euler(0f, 0f, -18f));
            Primitive(PrimitiveType.Sphere, "TipL", root.transform,
                new Vector3(-0.075f, 0.40f, 0f), Vector3.one * 0.04f, accent);
            Primitive(PrimitiveType.Sphere, "TipR", root.transform,
                new Vector3(0.075f, 0.40f, 0f), Vector3.one * 0.04f, accent);
            return root;
        }

        /// <summary>
        /// Impact telegraph on the floor: dark disc + emissive warning ring. The item
        /// scales the ring down as it falls (shrinking ring = time left).
        /// </summary>
        public static GameObject TelegraphRing(Color color)
        {
            var root = new GameObject("Telegraph");
            var dark = PropMeshes.MatUnlit(new Color(0f, 0f, 0f, 0.55f));
            var glow = PropMeshes.Mat(color, 0f, 0.5f, color * 3.0f);

            Primitive(PrimitiveType.Cylinder, "Shadow", root.transform,
                new Vector3(0f, 0.010f, 0f), new Vector3(0.9f, 0.004f, 0.9f), dark);
            var ring = new GameObject("Ring");
            ring.transform.SetParent(root.transform, false);
            const int segs = 20;
            const float radius = 0.55f;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                Primitive(PrimitiveType.Cube, $"Seg{i}", ring.transform,
                    new Vector3(Mathf.Cos(a) * radius, 0.015f, Mathf.Sin(a) * radius),
                    new Vector3(0.10f, 0.02f, 0.045f), glow,
                    Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
            }
            return root;
        }
    }
}
