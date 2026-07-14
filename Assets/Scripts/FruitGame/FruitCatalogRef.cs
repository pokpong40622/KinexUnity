using UnityEngine;
using Kinex.FX;

namespace Kinex.FruitGame
{
    /// <summary>All spawnable food kinds. Order matters: everything up to Broccoli is healthy.</summary>
    public enum FruitKind
    {
        Apple, Orange, Banana, Watermelon, Broccoli, // healthy
        Soda, Burger, Donut, Fries,                  // junk
    }

    /// <summary>
    /// Thin mapping from FruitKind to its visual builder + healthy/junk flag. Each kind loads a
    /// Kenney CC0 food model (Assets/Art/Kenney/.../Food/) and rescales it for overhead
    /// readability; if the model is missing, falls back to the old PropMeshes primitive so the
    /// game never spawns an empty item.
    /// </summary>
    public static class FruitCatalogRef
    {
        static readonly FruitKind[] Healthy =
            { FruitKind.Apple, FruitKind.Orange, FruitKind.Banana, FruitKind.Watermelon, FruitKind.Broccoli };
        static readonly FruitKind[] Junk =
            { FruitKind.Soda, FruitKind.Burger, FruitKind.Donut, FruitKind.Fries };

        // Items float overhead in the header zone's approach lane and should read clearly from
        // ~2m away without dwarfing the glow ring.
        const float TargetDiameter = 0.55f;

        // FruitSpawner overwrites the root's localScale to this value after Build() returns (see
        // FruitSpawner.itemScale, default 1.6) — the model's own local scale is set here so the
        // FINAL on-screen size still lands on TargetDiameter once that multiply happens.
        const float SpawnerItemScale = 1.6f;

        public static bool IsHealthy(FruitKind kind) => kind <= FruitKind.Broccoli;

        public static FruitKind RandomKind(bool healthy)
        {
            var pool = healthy ? Healthy : Junk;
            return pool[Random.Range(0, pool.Length)];
        }

        public static GameObject Build(FruitKind kind)
        {
            switch (kind)
            {
                case FruitKind.Apple:      return BuildFood("apple", PropMeshes.Apple);
                case FruitKind.Orange:     return BuildFood("orange", PropMeshes.Orange);
                case FruitKind.Banana:     return BuildFood("banana", PropMeshes.Banana);
                case FruitKind.Watermelon: return BuildFood("watermelon", PropMeshes.WatermelonSlice);
                case FruitKind.Broccoli:   return BuildFood("broccoli", PropMeshes.Broccoli);
                case FruitKind.Soda:       return BuildFood("soda", PropMeshes.SodaCup);
                case FruitKind.Burger:     return BuildFood("burger-cheese", PropMeshes.Burger);
                case FruitKind.Donut:      return BuildFood("donut-sprinkles", PropMeshes.Donut);
                default:                   return BuildFood("fries", PropMeshes.FriesBox);
            }
        }

        static GameObject BuildFood(string kenneyName, System.Func<GameObject> fallback)
        {
            var root = new GameObject(kenneyName);
            var model = KenneyProps.Food(kenneyName, root.transform, 1f);
            if (model == null)
            {
                DestroySafe(root);
                return fallback();
            }

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (maxDim > 0.0001f)
                    model.transform.localScale *= TargetDiameter / (SpawnerItemScale * maxDim);
            }
            return root;
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
