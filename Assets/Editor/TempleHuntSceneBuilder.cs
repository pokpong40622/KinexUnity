#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Kinex;
using Kinex.TempleHunt;
using Kinex.Trainer;
using PonyuDev.SherpaOnnx.Tts;

namespace Kinex.TempleHunt.EditorTools
{
    /// <summary>
    /// One-click / batchmode builder for Assets/Scenes/TempleHuntScene.unity: camera + torch-mood
    /// lights + ACES post volume, the runtime-built temple stage, the MediaPipe-driven player
    /// avatar dressed as a jungle explorer (same gotcha fixes as MirrorGameSceneBuilder), the
    /// portrait UI canvas with camera feed, and the fully wired TempleHuntDirector (+ result
    /// bridge + TTS voice). Idempotent: reopens the scene and rebuilds its known roots by name.
    /// Ends by appending the scene to Build Settings and rendering a 927x1427 screenshot (with a
    /// temp Chamber-1 stage preview) to the job scratchpad.
    /// </summary>
    public static class TempleHuntSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/TempleHuntScene.unity";
        const string PostProfilePath = "Assets/TempleHunt/TempleHuntPost.asset";
        const string CharacterId = CharacterLibrary.DefaultId;
        const string RehabPoseDataPath = "Assets/Animations/RehabPoseData.asset";
        // The demo ghost must be the rig RehabPoseData was baked on (memory: trainer_pose_rig_pairing).
        const string TrainerRigPath = "Assets/Characters/NewTrainerAnimated.fbx";
        const string ScreenshotPath = "C:/Users/Admin/.claude/jobs/775e158f/tmp/templehunt_scene.png";
        const int ShotW = 927, ShotH = 1427;
        const float AvatarHeightMeters = 1.7f;

        static readonly string[] OwnedRoots =
        {
            "Main Camera", "KeyLight", "FillLight", "RimLight", "GlobalVolume",
            "TempleStage", "PlayerCharacter", "Canvas", "EventSystem", "Director", "~TempEnv",
        };

        [MenuItem("Kinex/Build Temple Hunt Scene")]
        public static void BuildScene()
        {
            Scene scene;
            bool existed = File.Exists(ScenePath);
            if (existed) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(OwnedRoots, root.name) >= 0)
                    Object.DestroyImmediate(root);

            // ---- Main Camera. ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 46f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.08f, 0.06f);
            cam.allowHDR = true; // torch/rune/doorway glow rely on HDR values clearing the bloom threshold
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // ---- Warm amber key (torchlight through the broken roof) — real shadows on the paving. ----
            var keyGo = new GameObject("KeyLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.2f;
            key.color = new Color(1f, 0.78f, 0.5f);
            keyGo.transform.rotation = Quaternion.Euler(42f, 22f, 0f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.65f;

            // ---- Cool teal fill on the camera side so the pose-critical front stays readable. ----
            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.5f, 0.72f, 0.75f);
            fill.intensity = 2.0f;
            fill.range = 9f;
            fillGo.transform.position = new Vector3(0f, 1.3f, -3.2f);

            // ---- Warm rim behind the avatar; also what keeps the sandstone walls readable. ----
            var rimGo = new GameObject("RimLight");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1f, 0.72f, 0.42f);
            rim.intensity = 0.85f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(18f, 180f, 0f);

            // ---- Post-processing volume (ACES + bloom for the torch/rune glow). ----
            var volGo = new GameObject("GlobalVolume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreatePostProfile();

            // ---- Runtime-built temple stage. ----
            var stageGo = new GameObject("TempleStage");
            var stage = stageGo.AddComponent<TempleStage>();

            // ---- Player avatar (facing camera) + MediaPipe detector. ----
            var player = new GameObject("PlayerCharacter");
            player.transform.position = Vector3.zero;

            MediaPipePoseDetector detector = null;
            GameObject model = null;
            var prefab = CharacterLibrary.LoadPrefab(CharacterId);
            if (prefab != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                model.transform.SetParent(player.transform, false);
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                detector = model.AddComponent<MediaPipePoseDetector>();
                ConfigureDetector(detector);

                // Strip the FBX's stray intensity-1000 light + any cameras (memory: avatar_fbx_stray_lights).
                foreach (var stray in model.GetComponentsInChildren<Light>(true)) stray.enabled = false;
                foreach (var strayCam in model.GetComponentsInChildren<Camera>(true)) strayCam.enabled = false;

                // Force renderers onto the live Trilight ambient (not stale baked light probes).
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                // Calm the FBX's neon eye rings.
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (r.gameObject.name != "eyes_l" && r.gameObject.name != "eyes_r") continue;
                    var calm = new Material(r.sharedMaterial) { name = "EyeCalm" };
                    calm.color = new Color(0.42f, 0.30f, 0.24f);
                    r.sharedMaterial = calm;
                }

                // Jungle-explorer wardrobe: khaki shirt, brown trousers, dark boots.
                var skinTone = new Color(0.86f, 0.67f, 0.53f);
                var hairTone = new Color(0.22f, 0.14f, 0.10f);
                var shirtTone = new Color(0.72f, 0.62f, 0.42f);   // explorer khaki
                var pantsTone = new Color(0.36f, 0.27f, 0.17f);   // trail brown
                var shoeTone = new Color(0.30f, 0.22f, 0.15f);    // leather boots
                ApplyBodyMaterial(model, "mat", skinTone, 0.28f, "SkinFace");
                ApplyBodyMaterial(model, "tay1", skinTone, 0.28f, "SkinHands");
                ApplyBodyMaterial(model, "ao", shirtTone, 0.12f, "ShirtKhaki");
                ApplyBodyMaterial(model, "quan", pantsTone, 0.12f, "PantsTrail");
                ApplyBodyMaterial(model, "toc1", hairTone, 0.3f, "HairWarm");
                ApplyBodyMaterial(model, "giay_UV", shoeTone, 0.25f, "BootsLeather");
                var browTone = new Color(0.16f, 0.10f, 0.07f);
                foreach (var browName in new[] { "may_l", "may_r", "mi_l", "mi_r" })
                    ApplyBodyMaterial(model, browName, browTone, 0.2f, "BrowLashDark");

                FrameCameraOnAvatar(camGo.transform, model);
            }
            else
            {
                Debug.LogWarning($"[TempleHuntSceneBuilder] Character prefab missing for id '{CharacterId}'.");
                camGo.transform.position = new Vector3(0f, 1.6f, -3.6f);
                camGo.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            }

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

            BuildCameraFeed(canvasGo.transform, detector);

            // ---- Director + result bridge + voice. ----
            var directorGo = new GameObject("Director");
            var director = directorGo.AddComponent<TempleHuntDirector>();
            directorGo.AddComponent<TempleResultBridge>();
            var voice = directorGo.AddComponent<Kinex.MegaDance.VoiceCoach>();
            directorGo.AddComponent<AudioSource>();

            var ttsOrchestrator = directorGo.AddComponent<TtsOrchestrator>();
            var voiceSo = new SerializedObject(voice);
            voiceSo.FindProperty("tts").objectReferenceValue = ttsOrchestrator;
            voiceSo.ApplyModifiedPropertiesWithoutUndo();

            director.poseDetector = detector;
            director.stage = stage;
            director.voice = voice;
            director.rehabPoseData = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(RehabPoseDataPath);
            director.trainerRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrainerRigPath);
            director.useKeyboardStub = false; // baked scene is device-ready; flip on in the Inspector for editor play
            EditorUtility.SetDirty(director);

            // ---- UI (chained, like the other game builders). ----
            Debug.Log("[TempleHuntSceneBuilder] " + TempleHuntUIBuilder.BuildUI());

            // ---- Build Settings: append only (Boot stays index 0). ----
            AppendToBuildSettings(ScenePath);

            // ---- Framing screenshot: temp Chamber-1 stage so water + lever + torches show.
            // Guarded: a screenshot/IO failure must not abort the build before SaveScene. ----
            var tempEnv = new GameObject("~TempEnv");
            try
            {
                TempleStage.Build(tempEnv.transform, showChamber: 1);
                RenderScreenshot(cam, ScreenshotPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TempleHuntSceneBuilder] screenshot failed — scene still saved: {e.Message}");
            }
            finally
            {
                Object.DestroyImmediate(tempEnv);
            }

            // ---- Save. ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TempleHuntSceneBuilder] Scene built + saved: {ScenePath}. Screenshot: {ScreenshotPath}");
        }

        static void FrameCameraOnAvatar(Transform cam, GameObject model)
        {
            var b = ComputeBounds(model);
            if (b.size.y > 0.01f && Mathf.Abs(b.size.y - AvatarHeightMeters) > 0.05f)
            {
                model.transform.localScale *= AvatarHeightMeters / b.size.y;
                b = ComputeBounds(model);
            }
            float h = Mathf.Max(b.size.y, 0.5f);
            cam.position = new Vector3(b.center.x, b.min.y + 0.85f * h, b.center.z - 2.3f * h);
            cam.rotation = Quaternion.Euler(4f, 0f, 0f);
        }

        static void ApplyBodyMaterial(GameObject model, string rendererName, Color color, float smoothness, string cloneName)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (r.gameObject.name != rendererName) continue;
                var mat = new Material(r.sharedMaterial) { name = cloneName };
                mat.color = color;
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
                r.sharedMaterial = mat;
            }
        }

        static Bounds ComputeBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(model.transform.position, Vector3.one * AvatarHeightMeters);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static void ConfigureDetector(MediaPipePoseDetector detector)
        {
            var so = new SerializedObject(detector);
            so.FindProperty("modelFileName").stringValue = "pose_landmarker_lite.bytes";
            so.FindProperty("flipX").boolValue = true;
            so.FindProperty("flipY").boolValue = true;
            so.FindProperty("use3DWorld").boolValue = true;
            so.FindProperty("drivesAvatar").boolValue = true;
            so.FindProperty("mirrorPreview").boolValue = true;
            so.FindProperty("previewRotationCW").intValue = 270;
            so.FindProperty("minBoneVisibility").floatValue = 0.3f;
            so.FindProperty("legVisThreshold").floatValue = 0.2f;
            so.FindProperty("legMaxDegPerSec").floatValue = 320f;
            so.FindProperty("autoCalibrate").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildCameraFeed(Transform canvas, MediaPipePoseDetector detector)
        {
            var panelGo = new GameObject("CameraFeedPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var panelRt = (RectTransform)panelGo.transform;
            panelRt.SetParent(canvas, false);
            panelRt.anchorMin = new Vector2(648f / 927f, 1f - (55f + 300f) / 1427f);
            panelRt.anchorMax = new Vector2((648f + 234f) / 927f, 1f - 55f / 1427f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            panelGo.GetComponent<Image>().color = Color.black;

            var rawGo = new GameObject("CameraFeedImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rawRt = (RectTransform)rawGo.transform;
            rawRt.SetParent(panelRt, false);
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(6f, 6f);
            rawRt.offsetMax = new Vector2(-6f, -6f);

            var overlayGo = new GameObject("PoseSkeletonOverlay", typeof(RectTransform), typeof(CanvasRenderer));
            var overlayRt = (RectTransform)overlayGo.transform;
            overlayRt.SetParent(rawRt, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            var overlay = overlayGo.AddComponent<PoseSkeletonOverlay>();
            overlay.source = detector;
        }

        static VolumeProfile CreatePostProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TempleHunt"))
                AssetDatabase.CreateFolder("Assets", "TempleHunt");
            AssetDatabase.DeleteAsset(PostProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            var bloom = AddOverride<Bloom>(profile);
            // Same empirically-tuned kernel as the Mirror game: tight scatter + strong intensity
            // so torch bowls / runes / the doorway glow read clearly without blooming the walls.
            bloom.intensity.Override(1.4f);
            bloom.threshold.Override(0.75f);
            bloom.scatter.Override(0.55f);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.6f);

            var colors = AddOverride<ColorAdjustments>(profile);
            colors.saturation.Override(10f);
            colors.contrast.Override(10f);
            colors.postExposure.Override(0.2f);

            var whiteBalance = AddOverride<WhiteBalance>(profile);
            whiteBalance.temperature.Override(12f); // warmer than Mirror — firelight, not moonlight

            var smh = AddOverride<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.85f, 0.88f, 1.05f, 0.1f));    // cool stone shadows
            smh.highlights.Override(new Vector4(1.15f, 1.02f, 0.8f, 0.12f)); // warm torch highlights

            var tonemap = AddOverride<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.ACES);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var comp = profile.Add<T>(true);
            comp.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(comp, profile);
            return comp;
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
