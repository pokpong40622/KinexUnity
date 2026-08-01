#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Kinex;
using Kinex.DanceStar;
using PonyuDev.SherpaOnnx.Tts;

namespace Kinex.EditorTools
{
    /// <summary>
    /// One-click / batchmode builder for Assets/Scenes/DanceStarScene.unity — the CLINICAL 2D
    /// redesign of SUPERSTAR STAGE. There is NO 3D character or concert stage any more: the hero is
    /// the live camera feed with the detected skeleton drawn on the user (built by the UI builder),
    /// a static reference figure sits top-left, and text feedback coaches in real time.
    ///
    /// The scene therefore contains only: a Main Camera (solid soft-blue clear, no post), a bare
    /// "PoseDetector" object hosting MediaPipePoseDetector (+ an empty Animator, because the
    /// detector's CacheTpose() dereferences GetComponent&lt;Animator&gt;() — an unmapped Animator just
    /// returns null bones, which CacheTpose null-guards) with drivesAvatar OFF, the portrait UI
    /// canvas, and the wired DanceStarDirector (+ result bridge + TTS voice). Idempotent: reopens
    /// the scene and rebuilds its known roots by name. Ends by appending to Build Settings and
    /// rendering review screenshots (intro / play-HUD / results, with sample content) to the
    /// scratchpad — then restores clean defaults before saving.
    /// </summary>
    public static class DanceStarSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/DanceStarScene.unity";
        const string ScratchDir =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/552c1cb4-f9f4-4e3c-b616-07a08892f6bc/scratchpad";
        const int ShotW = 927, ShotH = 1427;

        static readonly string[] OwnedRoots =
        {
            "Main Camera", "PoseDetector", "Canvas", "EventSystem", "Director",
            // legacy roots from the 3D version — destroyed on rebuild so nothing lingers.
            "KeyLight", "FillLight", "RimLight", "GlobalVolume", "DanceStage",
            "PlayerCharacter", "TrainerRig", "~TempEnv",
        };

        [MenuItem("Kinex/Build Dance Star Scene")]
        public static void Build()
        {
            Scene scene;
            bool existed = File.Exists(ScenePath);
            if (existed) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(OwnedRoots, root.name) >= 0)
                    Object.DestroyImmediate(root);

            // ---- Main Camera. No 3D content renders through it (UI is a Screen-Space-Overlay
            // canvas); the soft-blue clear is just a fallback behind the full-screen UI background. ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 46f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.82f, 0.90f, 0.98f);
            cam.allowHDR = false;
            camGo.AddComponent<AudioListener>();

            // ---- Pose detector on a bare object (no visible avatar). Empty Animator satisfies the
            // detector's CacheTpose(); drivesAvatar OFF means it never tries to move a rig. ----
            var detectorGo = new GameObject("PoseDetector");
            detectorGo.AddComponent<Animator>();
            var detector = detectorGo.AddComponent<MediaPipePoseDetector>();
            ConfigureDetector(detector);

            // ---- Canvas + EventSystem. ----
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ShotW, ShotH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // ---- Director + result bridge + voice. ----
            var directorGo = new GameObject("Director");
            var director = directorGo.AddComponent<DanceStarDirector>();
            directorGo.AddComponent<DanceStarResultBridge>();
            var voice = directorGo.AddComponent<Kinex.MegaDance.VoiceCoach>();
            directorGo.AddComponent<AudioSource>();

            var ttsOrchestrator = directorGo.AddComponent<TtsOrchestrator>();
            var voiceSo = new SerializedObject(voice);
            voiceSo.FindProperty("tts").objectReferenceValue = ttsOrchestrator;
            voiceSo.ApplyModifiedPropertiesWithoutUndo();

            director.poseDetector = detector;
            director.worldCamera = cam;
            director.voice = voice;
            director.trainer = null;        // no trainer rig in the 2D redesign — reference is a sprite
            director.dancePoseData = null;
            director.useKeyboardStub = false; // baked scene is device-ready; flip on in the Inspector for editor play
            EditorUtility.SetDirty(director);

            // ---- UI (chained, like the other game builders). ----
            Debug.Log("[DanceStarSceneBuilder] " + DanceStarUIBuilder.BuildUI());

            // ---- Build Settings: append only (Boot stays index 0). ----
            AppendToBuildSettings(ScenePath);

            // ---- Review screenshots (with sample content), then restore clean defaults. ----
            try { RenderReviewShots(canvas, cam, director); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DanceStarSceneBuilder] screenshot failed — scene still saved: {e.Message}");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DanceStarSceneBuilder] Scene built + saved: {ScenePath}. Screenshots in {ScratchDir}.");
        }

        static void ConfigureDetector(MediaPipePoseDetector detector)
        {
            var so = new SerializedObject(detector);
            so.FindProperty("modelFileName").stringValue = "pose_landmarker_lite.bytes";
            so.FindProperty("flipX").boolValue = true;
            so.FindProperty("flipY").boolValue = true;
            so.FindProperty("use3DWorld").boolValue = false;   // 2D detectors only — no avatar to drive
            so.FindProperty("drivesAvatar").boolValue = false; // NEVER drive a rig (there is none)
            so.FindProperty("mirrorPreview").boolValue = true;
            so.FindProperty("previewRotationCW").intValue = 270;
            so.FindProperty("minBoneVisibility").floatValue = 0.3f;
            so.FindProperty("legVisThreshold").floatValue = 0.2f;
            so.FindProperty("legMaxDegPerSec").floatValue = 320f;
            so.FindProperty("autoCalibrate").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Review screenshots -----------------------------------------------------------------
        // Fills representative sample values, renders intro / play-HUD / results, then puts every
        // value back to the UI builder's clean default so the saved scene is untouched.
        static void RenderReviewShots(Canvas canvas, Camera cam, DanceStarDirector director)
        {
            Transform T(string n) => canvas.transform.Find(n);
            var intro = T("IntroPanel");
            var hud = T("HudPanel");
            var results = T("ResultsPanel");
            var calib = T("CalibPanel");
            var chair = T("ChairSafetyPanel");
            var framing = T("FramingPanel");
            var feed = T("CameraFeedPanel");

            void Only(Transform on)
            {
                foreach (var t in new[] { intro, hud, results, calib, chair, framing })
                    if (t != null) t.gameObject.SetActive(t == on);
            }

            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;
            var prevPlane = canvas.planeDistance;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.1f;

            Directory.CreateDirectory(ScratchDir);

            // Intro.
            Only(intro);
            if (feed != null) feed.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            RenderScreenshot(cam, ScratchDir + "/dancestar_intro.png");

            // Play HUD, with sample content.
            Only(hud);
            if (feed != null) feed.gameObject.SetActive(true);
            SetSample(director, true);
            Canvas.ForceUpdateCanvases();
            RenderScreenshot(cam, ScratchDir + "/dancestar_hud.png");

            // Results, with sample content.
            Only(results);
            if (feed != null) feed.gameObject.SetActive(false);
            SetSample(director, false);
            Canvas.ForceUpdateCanvases();
            RenderScreenshot(cam, ScratchDir + "/dancestar_results.png");

            // ---- Restore clean defaults (mirror DanceStarUIBuilder / director.Start). ----
            SetSampleClear(director);
            Only(intro);
            if (feed != null) feed.gameObject.SetActive(false);
            canvas.renderMode = prevMode;
            canvas.worldCamera = prevCam;
            canvas.planeDistance = prevPlane;
            Canvas.ForceUpdateCanvases();
        }

        static void SetSample(DanceStarDirector d, bool hud)
        {
            if (hud)
            {
                if (d.poseNameText != null) d.poseNameText.text = "ยกเข่าซ้าย";
                if (d.feedbackText != null) d.feedbackText.text = "ค้างไว้!";
                if (d.cardCountText != null) d.cardCountText.text = "ท่าที่ 3/28";
                if (d.sectionNameText != null) d.sectionNameText.text = "ท่อนที่ 1";
                if (d.matchMeterFill != null) d.matchMeterFill.fillAmount = 0.65f;
                if (d.countdownRingFill != null) d.countdownRingFill.fillAmount = 0.7f;
                if (d.songProgressFill != null) d.songProgressFill.fillAmount = 0.3f;
                if (d.referenceImage != null)
                {
                    var s = Resources.Load<Sprite>("DanceStarRef/knee_up_L");
                    d.referenceImage.sprite = s;
                    d.referenceImage.enabled = s != null;
                }
            }
            else
            {
                if (d.resultsStatsText != null)
                    d.resultsStatsText.text = "ทำได้ 24/28 ท่า\nความแม่นยำ 86%";
                if (d.resultsStarImages != null)
                    for (int i = 0; i < d.resultsStarImages.Length; i++)
                        if (d.resultsStarImages[i] != null)
                            d.resultsStarImages[i].color = i < 2
                                ? new Color(1f, 0.84f, 0.2f)
                                : new Color(1f, 1f, 1f, 0.22f);
            }
        }

        static void SetSampleClear(DanceStarDirector d)
        {
            if (d.poseNameText != null) d.poseNameText.text = "";
            if (d.feedbackText != null) d.feedbackText.text = "";
            if (d.cardCountText != null) d.cardCountText.text = "";
            if (d.sectionNameText != null) d.sectionNameText.text = "";
            if (d.matchMeterFill != null) d.matchMeterFill.fillAmount = 0f;
            if (d.countdownRingFill != null) d.countdownRingFill.fillAmount = 0f;
            if (d.songProgressFill != null) d.songProgressFill.fillAmount = 0f;
            if (d.referenceImage != null) { d.referenceImage.sprite = null; d.referenceImage.enabled = false; }
            if (d.resultsStatsText != null) d.resultsStatsText.text = "";
            if (d.resultsStarImages != null)
                foreach (var s in d.resultsStarImages)
                    if (s != null) s.color = new Color(1f, 1f, 1f, 0.22f);
        }

        static void RenderScreenshot(Camera cam, string path)
        {
            var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void AppendToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
