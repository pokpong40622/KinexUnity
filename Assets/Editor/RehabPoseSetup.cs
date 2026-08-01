using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Kinex.Trainer;

namespace Kinex.EditorTools
{
    /// <summary>
    /// THROWAWAY decouple tool. MegaDance (rehab) and KINEX World (exercise) currently share ONE
    /// pose asset (Assets/Animations/TrainerPoseData.asset). This duplicates it to RehabPoseData.asset
    /// and points the MEGADANCE scene's TrainerPoseController at the new copy, so MegaDance's poses
    /// can be edited into rehab poses without changing World. World keeps the original asset.
    /// Idempotent: re-running won't re-copy. Run editor CLOSED via batchmode -executeMethod, or from
    /// the "Kinex/Setup Rehab Pose Set (decouple)" menu.
    /// </summary>
    public static class RehabPoseSetup
    {
        const string SrcAsset = "Assets/Animations/TrainerPoseData.asset";
        const string RehabAsset = "Assets/Animations/RehabPoseData.asset";
        const string MegaScene = "Assets/Scenes/MegaDanceScene.unity";

        [MenuItem("Kinex/Setup Rehab Pose Set (decouple)")]
        public static void Run()
        {
            // 1) Duplicate the shared pose asset for MegaDance (skip if already present).
            if (AssetDatabase.LoadAssetAtPath<TrainerPoseData>(RehabAsset) == null)
            {
                if (!AssetDatabase.CopyAsset(SrcAsset, RehabAsset))
                {
                    Debug.LogError($"[RehabPoseSetup] CopyAsset failed: {SrcAsset} -> {RehabAsset}");
                    return;
                }
                AssetDatabase.ImportAsset(RehabAsset, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.Refresh();
                Debug.Log($"[RehabPoseSetup] Created {RehabAsset} (copy of the shared arm poses; replace with rehab poses next).");
            }

            // 2) Open the scene FIRST, THEN load the asset — OpenScene(Single) unloads assets and turns
            // a previously-loaded reference into a "fake-null" UnityObject, which silently nulls the
            // assignment. Loading after the scene is open keeps the reference valid.
            var scene = EditorSceneManager.OpenScene(MegaScene, OpenSceneMode.Single);
            var rehab = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(RehabAsset);
            if (rehab == null)
            {
                Debug.LogError($"[RehabPoseSetup] Could not load {RehabAsset} after opening scene — ABORTING (scene left untouched).");
                return;
            }
            var controllers = Object.FindObjectsByType<TrainerPoseController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[RehabPoseSetup] Found {controllers.Length} TrainerPoseController(s) in MegaDance scene.");
            int changed = 0;
            foreach (var c in controllers)
            {
                var so = new SerializedObject(c);
                var prop = so.FindProperty("poseData");
                if (prop == null) continue;
                if (prop.objectReferenceValue == rehab) continue;
                prop.objectReferenceValue = rehab;
                so.ApplyModifiedPropertiesWithoutUndo();
                // Verify the assignment actually took (objectReferenceValue can read back null if
                // the asset wasn't resolvable).
                so.Update();
                if (so.FindProperty("poseData").objectReferenceValue == null)
                {
                    Debug.LogError("[RehabPoseSetup] poseData read back null after assign — ABORTING without save.");
                    return;
                }
                changed++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RehabPoseSetup] DONE. MegaDance scene now uses {RehabAsset}; World still uses {SrcAsset}. " +
                      $"TrainerPoseController(s) repointed: {changed}.");
        }
    }
}
