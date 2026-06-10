#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Kinex.SpikeTools
{
    /// <summary>
    /// SPIKE — throwaway. Non-interactive Android export to the Flutter app, so the camera spike
    /// can be exported without Unity's folder-picker dialogs. Runs in the project's persistent
    /// editor assembly (unlike execute_code's temp assembly), so it survives the player-script
    /// domain reload that BuildPipeline.BuildPlayer triggers. Reuses flutter_embed_unity's own
    /// ProjectExporterAndroid (via reflection) for the post-build transform. Delete after the spike.
    /// </summary>
    public static class SpikeExporter
    {
        const string ExportPath = "D:/kinex_app/android/unityLibrary";

        [MenuItem("Kinex/Spike Export Android (ARM64)")]
        public static void ExportAndroid()
        {
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            // Unity 6 dropped general x86_64 Android support (it's Magic-Leap-only now), so real
            // ARM64 devices are the only target. Emulators must be arm64-v8a images.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            EditorUserBuildSettings.development = false;

            if (Directory.Exists(ExportPath)) Directory.Delete(ExportPath, true);
            Directory.CreateDirectory(ExportPath);

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);

            var opts = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                locationPathName = ExportPath,
                options = BuildOptions.AcceptExternalModificationsToPlayer,
            };

            Type exporterType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("ProjectExporterAndroid");
                if (t != null) { exporterType = t; break; }
            }
            if (exporterType == null)
            {
                Debug.LogError("[SpikeExporter] ProjectExporterAndroid not found (flutter_embed_unity package missing?)");
                return;
            }

            MethodInfo export = null;
            var dt = exporterType;
            while (dt != null && export == null)
            {
                export = dt.GetMethod("Export",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                dt = dt.BaseType;
            }
            if (export == null)
            {
                Debug.LogError("[SpikeExporter] Export method not found on " + exporterType.FullName);
                return;
            }

            var inst = Activator.CreateInstance(exporterType, true);
            Debug.Log("[SpikeExporter] Export starting -> " + ExportPath + " scenes=" + scenes.Count);
            export.Invoke(inst, new object[] { opts, new List<string>() });
            Debug.Log("[SpikeExporter] Export call returned. build.gradle exists=" +
                      File.Exists(Path.Combine(ExportPath, "build.gradle")));
        }
    }
}
#endif
