using UnityEngine;
using Kinex.FX;

namespace Kinex.BattleGame
{
    /// <summary>
    /// Procedural monster silhouettes from primitives — same CreatePrimitive + scale/offset +
    /// PropMeshes.Mat philosophy as Kinex.FX.PropMeshes (deliberately kept local to BattleGame
    /// instead of extending that shared file, since these bodies are specific to this game).
    /// Every monster gets big white eyes with black pupils + tiny feet; the boss additionally
    /// gets a crown built from tilted cubes (no cone primitive exists in Unity).
    /// </summary>
    public static class MonsterMeshBuilder
    {
        public static GameObject Build(MonsterShape shape, Color tint)
        {
            return shape switch
            {
                MonsterShape.RoundSlime => BuildRoundSlime(tint),
                MonsterShape.TallGhost => BuildTallGhost(tint),
                MonsterShape.SpikyBoss => BuildSpikyBoss(tint),
                _ => BuildRoundSlime(tint),
            };
        }

        static GameObject BuildRoundSlime(Color tint)
        {
            var root = new GameObject("MonsterBody");
            var body = Prim(PrimitiveType.Sphere, "Body", root.transform, Vector3.zero,
                             new Vector3(1.1f, 0.9f, 1.1f), BodyMat(tint));
            BuildEyes(root.transform, 0.32f, 0.55f, 0.5f);
            BuildFeet(root.transform, 0.42f, -0.42f, 0.18f, tint);
            return root;
        }

        static GameObject BuildTallGhost(Color tint)
        {
            var root = new GameObject("MonsterBody");
            Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.15f, 0f),
                 new Vector3(0.75f, 1.05f, 0.75f), BodyMat(tint));
            // Wispy tail-flare: three shrinking discs beneath the capsule fake a ghost's trailing hem.
            Prim(PrimitiveType.Sphere, "HemA", root.transform, new Vector3(0f, -0.55f, 0f),
                 new Vector3(0.7f, 0.25f, 0.7f), BodyMat(tint));
            Prim(PrimitiveType.Sphere, "HemB", root.transform, new Vector3(0.28f, -0.68f, 0.1f),
                 new Vector3(0.4f, 0.18f, 0.4f), BodyMat(tint));
            Prim(PrimitiveType.Sphere, "HemC", root.transform, new Vector3(-0.3f, -0.66f, -0.08f),
                 new Vector3(0.42f, 0.18f, 0.42f), BodyMat(tint));
            BuildEyes(root.transform, 0.24f, 0.95f, 0.42f);
            return root;
        }

        static GameObject BuildSpikyBoss(Color tint)
        {
            var root = new GameObject("MonsterBody");
            Prim(PrimitiveType.Sphere, "Body", root.transform, Vector3.zero,
                 new Vector3(1.5f, 1.35f, 1.5f), BodyMat(tint));

            // Crown: 5 diamond-shaped spikes (cubes rotated 45deg on Z read as a pointed shape,
            // same trick PropMeshes.Donut/Banana use to fake curves with no cone primitive).
            var goldMat = PropMeshes.Mat(new Color(1f, 0.82f, 0.25f), metallic: 0.6f, smooth: 0.75f,
                                          emission: new Color(0.5f, 0.35f, 0.05f));
            float[] xs = { -0.5f, -0.25f, 0f, 0.25f, 0.5f };
            foreach (var x in xs)
            {
                var spike = Prim(PrimitiveType.Cube, "CrownSpike", root.transform,
                                  new Vector3(x, 1.05f + Mathf.Abs(x) * -0.15f, 0.15f),
                                  new Vector3(0.16f, 0.16f, 0.16f), goldMat,
                                  Quaternion.Euler(0f, 0f, 45f));
            }
            Prim(PrimitiveType.Cylinder, "CrownBand", root.transform, new Vector3(0f, 0.92f, 0.1f),
                 new Vector3(0.62f, 0.05f, 0.5f), goldMat);

            BuildEyes(root.transform, 0.4f, 0.5f, 0.75f);
            BuildFeet(root.transform, 0.6f, -0.65f, 0.26f, tint);
            return root;
        }

        static void BuildEyes(Transform parent, float xOffset, float yOffset, float zOffset)
        {
            var white = PropMeshes.Mat(Color.white, smooth: 0.9f);
            var black = PropMeshes.Mat(new Color(0.05f, 0.05f, 0.08f));
            foreach (var side in new[] { -1f, 1f })
            {
                Prim(PrimitiveType.Sphere, "Eye", parent, new Vector3(side * xOffset, yOffset, zOffset),
                     new Vector3(0.26f, 0.26f, 0.26f), white);
                Prim(PrimitiveType.Sphere, "Pupil", parent,
                     new Vector3(side * xOffset, yOffset, zOffset + 0.13f),
                     new Vector3(0.12f, 0.12f, 0.12f), black);
            }
        }

        static void BuildFeet(Transform parent, float xOffset, float yOffset, float radius, Color tint)
        {
            var mat = BodyMat(tint * 0.7f);
            foreach (var side in new[] { -1f, 1f })
                Prim(PrimitiveType.Sphere, "Foot", parent, new Vector3(side * xOffset, yOffset, 0.15f),
                     new Vector3(radius, radius * 0.7f, radius), mat);
        }

        static Material BodyMat(Color c) =>
            PropMeshes.Mat(c, smooth: 0.55f, emission: c * 0.12f);

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos,
                                Vector3 localScale, Material mat, Quaternion? localRot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}
