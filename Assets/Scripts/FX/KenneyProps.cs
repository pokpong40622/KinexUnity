using System.Collections.Generic;
using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// Loader for the CC0 Kenney model kits under Assets/Art/Kenney/Resources/Kenney/
    /// (Food/ and Nature/). Spawns an FBX model asset as a plain visual prop: colliders
    /// stripped, optional uniform scale. Models are tiny (~0.3-1 m) and share Kenney's
    /// flat-color style, so they sit well next to the PropMeshes primitives.
    /// </summary>
    public static class KenneyProps
    {
        static readonly Dictionary<string, GameObject> s_Cache = new Dictionary<string, GameObject>();

        /// <summary>
        /// Instantiates "Kenney/Food/apple", "Kenney/Nature/tree_default", etc.
        /// Returns null (with one logged warning) if the asset is missing, so callers
        /// can fall back to a PropMeshes primitive.
        /// </summary>
        public static GameObject Spawn(string path, Transform parent = null, float scale = 1f)
        {
            if (!s_Cache.TryGetValue(path, out var prefab))
            {
                prefab = Resources.Load<GameObject>(path);
                s_Cache[path] = prefab; // cache nulls too, so a bad path warns once
                if (prefab == null) Debug.LogWarning($"[KenneyProps] missing model: {path}");
            }
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab, parent);
            go.name = prefab.name;
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            foreach (var col in go.GetComponentsInChildren<Collider>())
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
            return go;
        }

        public static GameObject Food(string name, Transform parent = null, float scale = 1f)
            => Spawn("Kenney/Food/" + name, parent, scale);

        public static GameObject Nature(string name, Transform parent = null, float scale = 1f)
            => Spawn("Kenney/Nature/" + name, parent, scale);
    }
}
