using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kinex.HangGlider.EditorTools
{
    /// <summary>
    /// Exports the Hang Glider game as a .unitypackage so it can be handed to another developer.
    /// IncludeDependencies pulls in whatever the scene references outside Assets/HangGlider
    /// (shared pose-detection scripts, fonts, the avatar model, ...) so the package compiles
    /// and opens on the other side.
    /// </summary>
    public static class HangGliderExporter
    {
        const string OutPath = "C:/Users/Admin/Downloads/HangGlider.unitypackage";

        static readonly string[] Roots =
        {
            "Assets/HangGlider",
            "Assets/Editor/HangGliderPolish.cs",
            // Code deps the asset-dependency scan can't see: PlayerMovement calls Kinex.Sfx
            // and reads Kinex.App.SceneRouter.HandTiltY, and both Sfx and GliderWind load
            // their clips by name out of Resources/Sfx.
            "Assets/Scripts/Sfx.cs",
            "Assets/Scripts/SceneRouter.cs",
            "Assets/Resources/Sfx",
        };

        [MenuItem("Kinex/Export Hang Glider Package")]
        public static void Export()
        {
            var roots = new List<string>();
            foreach (var r in Roots)
            {
                if (File.Exists(r) || Directory.Exists(r)) roots.Add(r);
                else Debug.LogWarning("[HangGliderExporter] missing, skipped: " + r);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));

            AssetDatabase.ExportPackage(
                roots.ToArray(),
                OutPath,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

            var info = new FileInfo(OutPath);
            Debug.Log("[HangGliderExporter] wrote " + OutPath + " exists=" + info.Exists +
                      " sizeMB=" + (info.Exists ? (info.Length / 1048576f).ToString("0.0") : "-"));
        }
    }
}
