#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Kinex.TheDasher.EditorTools
{
    /// <summary>
    /// Renders each TheDasher UI state (intro / framing / hud / results) to a PNG so the
    /// look of every screen can be reviewed without a device. The runtime director controls
    /// panel visibility at play time; this just force-activates one panel group, fills in
    /// representative sample values, renders, then restores — it never saves the scene.
    /// Batch: -executeMethod Kinex.TheDasher.EditorTools.TheDasherPreview.RenderAll
    /// </summary>
    public static class TheDasherPreview
    {
        const string ScenePath = "Assets/Scenes/TheDasherScene.unity";
        const string OutDir =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/552c1cb4-f9f4-4e3c-b616-07a08892f6bc/scratchpad/";
        const int W = 927, H = 1427;

        /// <summary>Instantiate one of each runtime prop (meteor, treasure, kick target, grabber)
        /// across the lanes at the impact line and render — the only way to SEE the runtime models
        /// (they're spawned during play, never baked into the scene).</summary>
        public static void RenderProps()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas != null) canvas.gameObject.SetActive(false); // clear UI, just show props
            var cam = Camera.main;
            var thai = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/FCIconic-SemiBold SDF.asset");

            void Place(GameObject go, Vector3 pos) { if (go != null) go.transform.position = pos; }
            Place(Kinex.TheDasher.DasherProps.Meteor(), new Vector3(-1.5f, 0.5f, 2.3f));
            Place(Kinex.TheDasher.DasherProps.Treasure(), new Vector3(0f, 0f, 2.0f));
            Place(Kinex.TheDasher.DasherProps.KickRing(thai), new Vector3(1.5f, 0.4f, 2.3f));
            Place(Kinex.TheDasher.DasherProps.GrabberTool(), new Vector3(0.95f, 1.05f, 0.5f));

            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    ps.Simulate(3f, true, true);

            Directory.CreateDirectory(OutDir);
            RenderNoCanvas(cam, OutDir + "astro_props.png");
            Debug.Log("[TheDasherPreview] rendered props parade");
            EditorApplication.Exit(0);
        }

        public static void RenderAll()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var director = Object.FindAnyObjectByType<TheDasherDirector>();
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (director == null || canvas == null) { EditorApplication.Exit(1); return; }

            // Reopening the scene resets every ParticleSystem to zero live particles (Simulate
            // state isn't serialized), so the starfield/ember systems would render empty. At
            // runtime they Play() continuously; here we pre-simulate them for a truthful shot.
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    ps.Simulate(12f, true, true);

            var cam = Camera.main;

            Transform Find(string n) => FindChild(canvas.transform, n);

            var intro = Find("IntroPanel");
            var framing = Find("FramingPanel");
            var hud = Find("HudPanel");
            var results = Find("ResultsPanel");
            var feed = Find("CameraFeedPanel");
            var preview = Find("PreviewToggleButton");

            void Only(Transform on)
            {
                foreach (var t in new[] { intro, framing, hud, results })
                    if (t != null) t.gameObject.SetActive(t == on);
            }

            // Framing: tint chips + set a prompt so the state reads truthfully.
            void PrepFraming()
            {
                var promptField = typeof(TheDasherDirector).GetField("framingPromptText",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var chipsField = typeof(TheDasherDirector).GetField("framingChips",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (chipsField?.GetValue(director) is Image[] chips)
                    for (int i = 0; i < chips.Length; i++)
                        if (chips[i] != null)
                            chips[i].color = i < 4 ? new Color(0.35f, 0.88f, 0.54f, 0.95f)
                                                   : new Color(0.96f, 0.42f, 0.33f, 0.95f);
            }

            // Results: fill sample numbers + light two stars.
            void PrepResults()
            {
                SetText(results, "0", "9");                       // big score
                var starsField = typeof(TheDasherDirector).GetField("resultStars",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (starsField?.GetValue(director) is Image[] stars)
                    for (int i = 0; i < stars.Length; i++)
                        if (stars[i] != null)
                            stars[i].color = i < 2 ? new Color(1f, 0.82f, 0.33f) : new Color(1f, 1f, 1f, 0.18f);
                var repField = typeof(TheDasherDirector).GetField("resultRepValues",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (repField?.GetValue(director) is TMP_Text[] reps)
                {
                    string[] vals = { "5", "4", "6", "7", "20" };
                    for (int i = 0; i < reps.Length && i < vals.Length; i++)
                        if (reps[i] != null) reps[i].text = vals[i];
                }
            }

            Directory.CreateDirectory(OutDir);

            Only(intro); if (feed) feed.gameObject.SetActive(true); if (preview) preview.gameObject.SetActive(true);
            Render(cam, canvas, OutDir + "astro_intro.png");

            Only(framing); PrepFraming();
            Render(cam, canvas, OutDir + "astro_framing.png");

            Only(hud);
            Render(cam, canvas, OutDir + "astro_hud.png");

            Only(results); PrepResults();
            // EndRun() retires the camera preview + toggle at results — mirror that here so the
            // preview matches the real screen (otherwise the feed box sits over the buttons).
            if (feed) feed.gameObject.SetActive(false);
            if (preview) preview.gameObject.SetActive(false);
            Render(cam, canvas, OutDir + "astro_results.png");

            Debug.Log("[TheDasherPreview] rendered intro/framing/hud/results");
            EditorApplication.Exit(0);
        }

        static void SetText(Transform root, string from, string to)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t.text == from) { t.text = to; return; }
        }

        static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform c in parent)
                if (c.name == name) return c;
            return null;
        }

        // Render just the 3D (no canvas swap) — for the props parade.
        static void RenderNoCanvas(Camera cam, string path)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void Render(Camera cam, Canvas canvas, string path)
        {
            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 0.5f;
                Canvas.ForceUpdateCanvases();
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prevActive;
                canvas.renderMode = prevMode;
                canvas.worldCamera = prevCam;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
#endif
