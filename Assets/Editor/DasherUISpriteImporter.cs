#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kinex.TheDasher.EditorTools
{
    /// <summary>
    /// Applies the import settings from Assets/TheDasher/UI/manifest.json (contract:
    /// docs/astro_ui_assets.md) to every listed sprite PNG — Sprite/Single, alpha transparency,
    /// no mips, clamp/bilinear, uncompressed, correct pixels-per-unit and 9-slice border.
    /// Re-runnable. Safe to run before the PNGs exist yet (another stream is producing them
    /// concurrently): a missing manifest or PNG is a warning, never an exception.
    /// </summary>
    public static class DasherUISpriteImporter
    {
        const string UiFolder = "Assets/TheDasher/UI/";
        const string ManifestPath = UiFolder + "manifest.json";

        [Serializable]
        class SpriteEntry
        {
            public string file;
            public int[] border; // [left, bottom, right, top], source px
        }

        [Serializable]
        class Manifest
        {
            public int pixelsPerUnit;
            public SpriteEntry[] sprites;
        }

        [MenuItem("Kinex/Import TheDasher UI Sprites")]
        public static void Import() => Debug.Log("[DasherUISpriteImporter] " + Run());

        public static string Run()
        {
            if (!File.Exists(ManifestPath))
                return $"No manifest at {ManifestPath} yet — nothing to import.";

            Manifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DasherUISpriteImporter] Failed to parse {ManifestPath}: {e.Message}");
                return "Manifest parse failed — see warning above.";
            }
            if (manifest == null || manifest.sprites == null || manifest.sprites.Length == 0)
                return "Manifest has no sprites listed — nothing to import.";

            int imported = 0, missing = 0;
            foreach (var entry in manifest.sprites)
            {
                if (entry == null || string.IsNullOrEmpty(entry.file))
                {
                    missing++;
                    continue;
                }

                string path = UiFolder + entry.file;
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[DasherUISpriteImporter] Missing PNG (skipped): {path}");
                    missing++;
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    // Freshly-dropped file the AssetDatabase hasn't seen yet — force an import
                    // before giving up on it.
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                }
                if (importer == null)
                {
                    Debug.LogWarning($"[DasherUISpriteImporter] No TextureImporter for: {path}");
                    missing++;
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.spritePixelsPerUnit = manifest.pixelsPerUnit;

                var b = entry.border;
                importer.spriteBorder = (b != null && b.Length == 4)
                    ? new Vector4(b[0], b[1], b[2], b[3])
                    : Vector4.zero;

                importer.SaveAndReimport();
                imported++;
            }

            AssetDatabase.Refresh();
            return $"Imported {imported} sprite(s), {missing} missing/skipped.";
        }
    }
}
#endif
