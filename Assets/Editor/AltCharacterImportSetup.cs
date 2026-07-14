#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kinex.EditorTools
{
    /// <summary>
    /// One-time importer fixup for the alternative CC0 character FBX files in
    /// Assets/Characters/Alt/ (see LICENSE-note.txt there for sources): forces
    /// ModelImporter.animationType = Human with an auto-mapped avatar, same as
    /// Assets/Characters/KinexUserModel.fbx, so MediaPipePoseDetector's HumanBodyBones
    /// driver works identically on any of them.
    /// Batch: -executeMethod Kinex.EditorTools.AltCharacterImportSetup.Run
    /// </summary>
    public static class AltCharacterImportSetup
    {
        const string AltFolder = "Assets/Characters/Alt";

        [MenuItem("Kinex/Setup Alt Character Imports")]
        public static void Run()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { AltFolder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[AltCharacterImportSetup] No model assets found under {AltFolder}.");
                return;
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                bool ok = avatar != null && avatar.isValid && avatar.isHuman;
                if (ok)
                    Debug.Log($"[AltCharacterImportSetup] {Path.GetFileName(path)}: humanoid avatar OK.");
                else
                    Debug.LogError($"[AltCharacterImportSetup] {Path.GetFileName(path)}: FAILED to produce a valid humanoid avatar.");
            }
        }
    }
}
#endif
