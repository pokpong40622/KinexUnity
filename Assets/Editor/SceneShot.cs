#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kinex.EditorTools
{
    /// <summary>
    /// Headless screenshot tool: opens any scene, renders its camera to a PNG, quits.
    /// Lets Claude visually inspect scenes without opening the Unity editor window.
    ///
    /// Usage (single line; must run WITHOUT -nographics or the render is blank grey):
    ///   Unity.exe -batchmode -quit -projectPath "D:\Unity project\Kinex"
    ///     -executeMethod Kinex.EditorTools.SceneShot.Capture
    ///     -shotScene Assets/Scenes/TempleHuntScene.unity
    ///     -shotOut C:/path/out.png
    ///     [-shotW 927] [-shotH 1427]            resolution, default portrait 927x1427
    ///     [-shotCamPos x,y,z] [-shotCamRot x,y,z] [-shotFov 42]   override scene camera
    ///     [-shotUI 1]                            also render Screen Space Overlay canvases
    /// Nothing is saved — the scene on disk is untouched.
    /// </summary>
    public static class SceneShot
    {
        public static void Capture()
        {
            string scenePath = Arg("-shotScene");
            string outPath = Arg("-shotOut");
            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(outPath))
                throw new Exception("[SceneShot] required args: -shotScene <asset path> -shotOut <png path>");

            int w = IntArg("-shotW", 927);
            int h = IntArg("-shotH", 1427);

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Camera cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null) throw new Exception($"[SceneShot] no camera found in {scenePath}");

            Vector3 v;
            if (VecArg("-shotCamPos", out v)) cam.transform.position = v;
            if (VecArg("-shotCamRot", out v)) cam.transform.rotation = Quaternion.Euler(v);
            float fov = IntArg("-shotFov", 0);
            if (fov > 0) cam.fieldOfView = fov;

            if (IntArg("-shotUI", 0) == 1)
            {
                // Screen Space Overlay canvases don't show in cam.Render(); flip them to camera space.
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = cam.nearClipPlane + 0.1f;
                }
                Canvas.ForceUpdateCanvases();
            }

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.Render(); // URP warm-up: first render can miss post/shadows
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }

            Debug.Log($"[SceneShot] OK {scenePath} -> {outPath} ({w}x{h})");
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static int IntArg(string name, int fallback)
        {
            var s = Arg(name);
            return int.TryParse(s, out int v) ? v : fallback;
        }

        static bool VecArg(string name, out Vector3 v)
        {
            v = Vector3.zero;
            var s = Arg(name);
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(',');
            if (parts.Length != 3) throw new Exception($"[SceneShot] {name} must be x,y,z — got '{s}'");
            v = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
            return true;
        }
    }
}
#endif
