using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Kinex
{
    /// <summary>
    /// Tiny static registry mapping a character id to its prefab (FBX) path. One flat lookup
    /// table instead of Resources/Addressables — scene builders just need
    /// CharacterLibrary.LoadPrefab(id) at edit time. All entries are humanoid-rigged
    /// (ModelImporter animationType = Human), so any of them can drive the same
    /// MediaPipePoseDetector -> HumanBodyBones animator used across the games.
    /// </summary>
    public static class CharacterLibrary
    {
        public const string DefaultId = "default";

        static readonly Dictionary<string, string> PrefabPaths = new()
        {
            { "default", "Assets/Characters/KinexUserModel.fbx" },
            { "casual_female", "Assets/Characters/Alt/Casual_Female.fbx" },
            { "casual_male", "Assets/Characters/Alt/Casual_Male.fbx" },
            { "worker_female", "Assets/Characters/Alt/Worker_Female.fbx" },
        };

#if UNITY_EDITOR
        /// <summary>Editor-side load only (AssetDatabase). Falls back to "default" (with a
        /// warning) if the id is unknown.</summary>
        public static GameObject LoadPrefab(string id)
        {
            if (!PrefabPaths.TryGetValue(id, out var path))
            {
                Debug.LogWarning($"[CharacterLibrary] Unknown character id '{id}', falling back to '{DefaultId}'.");
                path = PrefabPaths[DefaultId];
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogError($"[CharacterLibrary] Prefab missing at '{path}' for id '{id}'.");
            return prefab;
        }
#endif
    }
}
