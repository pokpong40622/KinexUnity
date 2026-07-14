#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Kinex.FruitGame;
using Kinex.MegaDance;
using PonyuDev.SherpaOnnx.Tts;

namespace Kinex.FruitGame.EditorTools
{
    /// <summary>
    /// One-click / batchmode builder for FruitGameScene: camera + light + post volume, the
    /// runtime-built orchard stage, the MediaPipe-driven player avatar, the header zone, the
    /// portrait UI canvas with camera feed + skeleton overlay, and the fully wired GameManager.
    /// Idempotent: reopens the existing scene and rebuilds its known roots by name.
    /// Ends by appending the scene to Build Settings (Boot stays index 0) and rendering a
    /// 927×1427 framing screenshot to the scratchpad.
    /// </summary>
    public static class FruitGameSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/FruitGameScene.unity";
        const string PostProfilePath = "Assets/FruitGame/FruitGamePost.asset";
        const string CharacterPrefabPath = "Assets/Characters/KinexUserModel.fbx";
        const string ScreenshotPath =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/3b7b0985-45db-45d2-bfa5-d7f306e07247/scratchpad/fruitgame_scene.png";

        // Every root this builder owns — destroyed and rebuilt on each run.
        static readonly string[] OwnedRoots =
        {
            "Main Camera", "Directional Light", "GlobalVolume", "FruitStage",
            "PlayerCharacter", "HeaderZone", "Canvas", "EventSystem", "GameManager", "~TempEnv",
        };

        [MenuItem("Kinex/Build Fruit Game Scene")]
        public static void BuildScene()
        {
            // ---- Open or create the scene. ----
            Scene scene;
            bool existed = File.Exists(ScenePath);
            if (existed) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(OwnedRoots, root.name) >= 0)
                    Object.DestroyImmediate(root);

            // ---- Main Camera: portrait framing — seated avatar in the lower ~55%, header zone
            //      in the upper third. Solid clear (the sky dome covers the background). ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Matches the sky dome's horizon stop — the dome is a hemisphere, so the strip below
            // the horizon line falls through to this color. Shared constant with
            // FruitStageBuilder so the two can't drift out of sync.
            cam.backgroundColor = FruitStageBuilder.SkyHorizon;
            camGo.transform.position = new Vector3(0f, 2.0f, 3.8f);
            camGo.transform.rotation = Quaternion.Euler(10f, 180f, 0f);
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // ---- Directional light. ----
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            // Golden-hour key: warm #FFE8C0, boosted intensity to compensate for ACES' extra
            // contrast (Neutral->ACES darkens midtones — see CreatePostProfile).
            light.intensity = 1.35f;
            light.color = new Color(1.0f, 0.910f, 0.753f); // #FFE8C0
            // Key light comes over the camera's shoulder — the avatar faces the camera (Y=180),
            // so at the old (50,-30,0) the sun lit her back and the face rendered black. Pitch
            // dropped from 42 to 28 (still safely above the avatar's front) for a lower
            // golden-hour sun angle and longer, softer cast shadows.
            lightGo.transform.rotation = Quaternion.Euler(28f, 205f, 0f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.65f;

            // ---- Global post-processing volume (Bloom / Vignette / ColorAdjustments). ----
            var volGo = new GameObject("GlobalVolume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreatePostProfile();
            WarnIfNoPostProcessData();

            // ---- Runtime-built orchard. ----
            new GameObject("FruitStage").AddComponent<FruitStage>();

            // ---- Player avatar + MediaPipe detector + root lift. ----
            var player = new GameObject("PlayerCharacter");
            player.transform.position = Vector3.zero;
            player.transform.localScale = Vector3.one * 0.32f; // matches MegaDanceScene's avatar scale
            var rootLift = player.AddComponent<AvatarRootLift>();

            MediaPipePoseDetector detector = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            if (prefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                model.transform.SetParent(player.transform, false);
                detector = model.AddComponent<MediaPipePoseDetector>();
                ConfigureDetector(detector);

                // The character FBX carries a Blender-export leftover: a Point light named
                // "Light" at intensity 1000. It nuked every Lit surface in the scene toward
                // red/white (Unlit surfaces rendered fine — that asymmetry is how it was found;
                // see the [LightDiag] investigation). Disable every imported light (and any
                // stray camera) so the only lights are the ones this builder owns.
                foreach (var stray in model.GetComponentsInChildren<Light>(true))
                    stray.enabled = false;
                foreach (var strayCam in model.GetComponentsInChildren<Camera>(true))
                    strayCam.enabled = false;

                // Same fix as BattleGameSceneBuilder/BalanceQuestSceneBuilder: the avatar's
                // renderers default to BlendProbes and sample stale baked light-probe ambient
                // instead of the live per-stage Trilight ambient FruitStageBuilder sets — leaving
                // the character's colors flat/off against the scene mood. Force them onto the
                // live ambient.
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                // The FBX's eye material renders as glowing orange-red rings at this camera
                // distance. Scene-scoped: clone the material and pull its tint down to a calm
                // dark brown — texture detail stays, the neon ring goes. Same fix as
                // BattleGameSceneBuilder.
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (r.gameObject.name != "eyes_l" && r.gameObject.name != "eyes_r") continue;
                    var calm = new Material(r.sharedMaterial) { name = "EyeCalm" };
                    calm.color = new Color(0.42f, 0.30f, 0.24f);
                    r.sharedMaterial = calm;
                }
            }
            else Debug.LogWarning($"[FruitGameSceneBuilder] Character prefab missing: {CharacterPrefabPath}");

            // ---- Header zone above the avatar's head. ----
            var zoneGo = new GameObject("HeaderZone");
            zoneGo.transform.position = new Vector3(0f, 1.9f, 0.2f);
            var headerZone = zoneGo.AddComponent<HeaderZone>();

            // ---- Canvas + EventSystem. ----
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(927f, 1427f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // ---- Camera feed preview (top-right) + skeleton overlay. ----
            // The RawImage here is THE ONLY one in the scene — MediaPipePoseDetector finds it
            // via FindAnyObjectByType and streams the webcam into it.
            var feed = NewUiObject(canvasGo.transform, "CameraFeedPanel");
            var feedImg = feed.AddComponent<Image>();
            feedImg.color = Color.black;
            PlaceFrame(feed.GetComponent<RectTransform>(), 648, 55, 234, 300);

            var rawGo = NewUiObject(feed.transform, "CameraFeedImage");
            rawGo.AddComponent<RawImage>();
            var rawRt = rawGo.GetComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(6, 6);
            rawRt.offsetMax = new Vector2(-6, -6);

            var overlayGo = NewUiObject(rawGo.transform, "PoseSkeletonOverlay");
            var overlay = overlayGo.AddComponent<PoseSkeletonOverlay>();
            overlay.source = detector;
            var overlayRt = overlayGo.GetComponent<RectTransform>();
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;

            // ---- GameManager. ----
            var mgrGo = new GameObject("GameManager");
            var manager = mgrGo.AddComponent<FruitGameManager>();
            mgrGo.AddComponent<FruitGameResultBridge>();
            var spawner = mgrGo.AddComponent<FruitSpawner>();
            var voice = mgrGo.AddComponent<VoiceCoach>();
            mgrGo.AddComponent<AudioSource>();

            // TTS: mirrors MegaDanceScene's wiring (VoiceCoach + TtsOrchestrator on the same
            // GameObject, VoiceCoach.tts pointing at it). Without this the field stays
            // unassigned and VoiceCoach.Speak() is silently a no-op forever.
            var ttsOrchestrator = mgrGo.AddComponent<TtsOrchestrator>();
            var voiceSo = new SerializedObject(voice);
            voiceSo.FindProperty("tts").objectReferenceValue = ttsOrchestrator;
            voiceSo.ApplyModifiedPropertiesWithoutUndo();

            var mso = new SerializedObject(manager);
            mso.FindProperty("poseDetector").objectReferenceValue = detector;
            mso.FindProperty("spawner").objectReferenceValue = spawner;
            mso.FindProperty("headerZone").objectReferenceValue = headerZone;
            mso.FindProperty("rootLift").objectReferenceValue = rootLift;
            mso.FindProperty("voice").objectReferenceValue = voice;
            mso.ApplyModifiedPropertiesWithoutUndo();

            var sso = new SerializedObject(spawner);
            sso.FindProperty("headerZone").objectReferenceValue = headerZone;
            sso.ApplyModifiedPropertiesWithoutUndo();

            // ---- UI panels + full manager wiring. ----
            Debug.Log("[FruitGameSceneBuilder] " + FruitGameUIBuilder.BuildUI());

            // ---- Build Settings: append only, never reorder (Boot stays index 0). ----
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool present = buildScenes.Exists(s => s.path == ScenePath);
            if (!present)
            {
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }

            // ---- Framing screenshot (temp env — FruitStage only builds at runtime). ----
            var tempEnv = new GameObject("~TempEnv");
            FruitStageBuilder.BuildStage(tempEnv.transform);
            RenderScreenshot(cam);
            Object.DestroyImmediate(tempEnv);

            // ---- Save. ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[FruitGameSceneBuilder] Scene built + saved: {ScenePath} " +
                      $"(build settings entry {(present ? "already present" : "appended")}). " +
                      $"Screenshot: {ScreenshotPath}");
        }

        // Serialized values copied from the validated MegaDanceScene detector instance.
        static void ConfigureDetector(MediaPipePoseDetector detector)
        {
            var so = new SerializedObject(detector);
            so.FindProperty("modelFileName").stringValue = "pose_landmarker_full.bytes";
            so.FindProperty("flipX").boolValue = true;
            so.FindProperty("flipY").boolValue = true;
            so.FindProperty("use3DWorld").boolValue = true;
            so.FindProperty("drivesAvatar").boolValue = true;
            so.FindProperty("mirrorPreview").boolValue = true;
            so.FindProperty("previewRotationCW").intValue = 270;
            so.FindProperty("minBoneVisibility").floatValue = 0.3f;
            so.FindProperty("legVisThreshold").floatValue = 0.2f;
            so.FindProperty("legMaxDegPerSec").floatValue = 320f;
            so.FindProperty("autoCalibrate").boolValue = false; // this game runs its own calib flow
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static VolumeProfile CreatePostProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FruitGame"))
                AssetDatabase.CreateFolder("Assets", "FruitGame");
            AssetDatabase.DeleteAsset(PostProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            // Sun disc + brightest highlights only — everything else in the scene is
            // sub-1.0 albedo, the sun sphere is deliberately HDR-boosted past 1.0 (see
            // FruitStageBuilder.BuildSky) so it's the only thing that reliably clears this.
            var bloom = AddOverride<Bloom>(profile);
            bloom.intensity.Override(0.42f);
            bloom.threshold.Override(1.15f);
            // Tightened from 0.55 — combined with the shrunk sky halo mesh (see FruitStageBuilder.
            // BuildSky), a wider scatter was re-spreading the glow right back over the now-visible
            // blue zenith band.
            bloom.scatter.Override(0.42f); // soft halo instead of a hard-edged glow ring

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.22f);

            var colors = AddOverride<ColorAdjustments>(profile);
            colors.saturation.Override(16f);
            colors.contrast.Override(8f);
            // ACES' filmic curve darkens the midrange vs. Neutral — compensate so the scene
            // doesn't read as underexposed relative to the earlier grade.
            colors.postExposure.Override(0.5f);

            // Round 1 screenshot read cool/flat, not "golden" — pushed warmer.
            var whiteBalance = AddOverride<WhiteBalance>(profile);
            whiteBalance.temperature.Override(18f);

            // Cool teal shadows / warm golden highlights split-tone — the "Golden Morning
            // Orchard" grade. Round 1's split (0.85-1.05 / 1.08-0.85) barely read on screen;
            // widened it. Midtones left near-neutral so skin/wood don't shift off-color.
            var smh = AddOverride<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.78f, 0.92f, 1.10f, 0f));
            smh.midtones.Override(new Vector4(1.0f, 0.99f, 0.96f, 0f));
            smh.highlights.Override(new Vector4(1.18f, 1.02f, 0.78f, 0f));

            // Without a tonemapper, HDR highlights (sunlit skin/wood/ground) clip straight to
            // white — the whole lower frame washed out in the builder screenshot until this
            // was added. ACES gives a filmic shoulder (softer highlight rolloff, richer
            // contrast) than Neutral, which is what actually reads as "premium" vs. flat/cheap.
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

        // Post-processing only renders if the active URP renderer has PostProcessData assigned.
        static void WarnIfNoPostProcessData()
        {
            bool anyPost = false;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && data.postProcessData != null) { anyPost = true; break; }
            }
            if (!anyPost)
                Debug.LogWarning("[FruitGameSceneBuilder] WARNING: no UniversalRendererData in the " +
                                 "project has Post Process Data assigned — Bloom/Vignette/ColorAdjustments " +
                                 "will NOT render. Assign PostProcessData on the Universal Renderer asset.");
        }

        static GameObject NewUiObject(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        // 927×1427 frame coords (origin top-left) → fractional anchors.
        static void PlaceFrame(RectTransform rt, float x, float y, float w, float h)
        {
            const float fw = 927f, fh = 1427f;
            rt.anchorMin = new Vector2(x / fw, 1f - (y + h) / fh);
            rt.anchorMax = new Vector2((x + w) / fw, 1f - y / fh);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void RenderScreenshot(Camera cam)
        {
            string path = ScreenshotPath;
            const int width = 927, height = 1427;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                // The FIRST render after a batchmode scene load draws every runtime-created
                // material with uninitialized per-material data — albedo comes out as garbage
                // (white, or a random solid tint that drifted between runs). A throwaway
                // warm-up render primes the SRP per-material buffers; the second render is
                // correct. Diagnosed by bisection: identical scenes rendered wrong on shot 1
                // and perfectly on shot 3.
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
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
    }
}
#endif
