#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kinex.World;

namespace Kinex.World.EditorTools
{
    /// <summary>
    /// One-click headless setup that wires the KINEX WORLD background music: ensures an
    /// AudioSource exists under GameManager, loads the folk loop clip, and assigns both to
    /// KinexWorldDirector. Re-runnable. Follows the same headless pattern as
    /// KinexWorldUIBuilder — no dialogs, Debug.Log only, safe for batchmode.
    /// </summary>
    public static class KinexWorldMusicSetup
    {
        const string ScenePath = "Assets/Scenes/KinexWorldScene.unity";
        const string ClipPath = "Assets/Audio/World/world_folk_loop.mp3";
        const string MusicChildName = "WorldMusic";

        [MenuItem("Kinex/Setup Kinex World Music")]
        public static void Setup()
        {
            Debug.Log("[KinexWorldMusicSetup] " + Run());
        }

        // No-dialog worker so it can be driven from automation. Returns a status string.
        public static string Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                return $"Could not open scene at {ScenePath}.";

            var director = Object.FindAnyObjectByType<KinexWorldDirector>();
            if (director == null)
                return "No KinexWorldDirector found in the open scene.";

            var gameManager = director.gameObject;

            // Find or create the WorldMusic child under GameManager.
            Transform musicChild = gameManager.transform.Find(MusicChildName);
            GameObject musicGo = musicChild != null ? musicChild.gameObject : null;
            if (musicGo == null)
            {
                musicGo = new GameObject(MusicChildName);
                musicGo.transform.SetParent(gameManager.transform, false);
            }

            var source = musicGo.GetComponent<AudioSource>();
            if (source == null) source = musicGo.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0.4f;
            source.spatialBlend = 0f; // 2D

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (clip == null)
                return $"Could not load AudioClip at {ClipPath}. Setup partially complete (AudioSource ready, clip not assigned).";

            var so = new SerializedObject(director);
            var sourceProp = so.FindProperty("musicSource");
            var clipProp = so.FindProperty("folkMusic");
            if (sourceProp != null) sourceProp.objectReferenceValue = source;
            if (clipProp != null) clipProp.objectReferenceValue = clip;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(musicGo);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            return $"WorldMusic AudioSource wired on '{gameManager.name}/{MusicChildName}', " +
                   $"clip '{clip.name}' assigned to KinexWorldDirector.folkMusic. Scene saved.";
        }
    }
}
#endif
