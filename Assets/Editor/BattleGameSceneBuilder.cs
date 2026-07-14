#if UNITY_EDITOR
using System.Collections.Generic;
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
using Kinex.BattleGame;
using Kinex.MegaDance;
using PonyuDev.SherpaOnnx.Tts;

namespace Kinex.BattleGame.EditorTools
{
    /// <summary>
    /// One-click / batchmode builder for Assets/Scenes/BattleGameScene.unity: camera + lights +
    /// post volume, the runtime-built floating-island stage, the MediaPipe-driven player avatar
    /// (bottom-center facing camera), a monster slot (mid-upper across the arena), the portrait
    /// UI canvas with camera feed + skeleton overlay, and the fully wired BattleDirector.
    /// Idempotent: reopens the existing scene and rebuilds its known roots by name. Ends by
    /// appending the scene to Build Settings (Boot stays index 0) and rendering a 927x1427
    /// framing screenshot to the scratchpad.
    /// </summary>
    public static class BattleGameSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/BattleGameScene.unity";
        const string PostProfilePath = "Assets/BattleGame/BattleGamePost.asset";
        // Which CharacterLibrary entry the battle avatar uses. Swap-in/shop candidates live in
        // Assets/Characters/Alt/ (see LICENSE-note.txt there) — flip this to spot-check one,
        // but "default" is what ships.
        const string CharacterId = CharacterLibrary.DefaultId;
        const string ScreenshotPath =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/3b7b0985-45db-45d2-bfa5-d7f306e07247/scratchpad/battlegame_scene.png";
        const int ShotW = 927, ShotH = 1427;
        const float AvatarHeightMeters = 1.7f;

        static readonly string[] OwnedRoots =
        {
            "Main Camera", "Directional Light", "RimLight", "FillLight", "GlobalVolume",
            "BattleStage", "PlayerCharacter", "MonsterSlot", "Canvas", "EventSystem", "Director", "~TempEnv",
        };

        [MenuItem("Kinex/Build Battle Game Scene")]
        public static void BuildScene()
        {
            Scene scene;
            bool existed = File.Exists(ScenePath);
            if (existed) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(OwnedRoots, root.name) >= 0)
                    Object.DestroyImmediate(root);

            // ---- Main Camera (framed against the avatar once its real bounds are known). ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 46f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Must match the sky dome's HORIZON stop (not its top) — the dome's rim sits at
            // world Y=0 while the camera eye is above that, so it looks slightly OVER the rim
            // into the void beyond; matching colors makes that strip read as more sky instead of
            // a visible seam (same trick FruitGameSceneBuilder uses for its own sky dome).
            cam.backgroundColor = new Color(1f, 0.62f, 0.34f); // level 1 (meadow) horizon default
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // ---- Key light (warm dawn, level 1 default palette). Color carries the "dawn" read
            // now (was 1,0.93,0.8, quite pale) rather than elevation — rounds 1-2 tried 18/24deg
            // (down from the original 32) to get longer dawn-like shadows, but every tree's shadow
            // then reached all the way to the far/backdrop row and stacked with the ground's own
            // darker outer ring into one solid dark band across the horizon. Kept close to the
            // original elevation so shadows stay legible without smearing together.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.8f, 0.55f);
            lightGo.transform.rotation = Quaternion.Euler(30f, 205f, 0f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.6f;

            // ---- Rim light behind the avatar (edge separation from the sky) + a cyan/violet fill
            //      on the camera side so the pose-detection-critical front still reads.
            // NOTE: yaw 20 (as this used to be) travels toward roughly +Z — that FRONT-lights the
            // avatar (whose front faces -Z, toward camera) instead of backlighting it; QuestStage
            // hit the exact same bug and documents the fix as yaw ~180 (light must travel toward
            // -Z to catch the back/shoulder edges facing the sky). Recolored warm gold instead of
            // lavender/violet too — a cool-toned rim reads as dusk/neon, not dawn. ----
            var rimGo = new GameObject("RimLight");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1f, 0.78f, 0.5f);
            rim.intensity = 0.5f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(20f, 180f, 0f);

            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.7f, 0.85f, 1f);
            fill.intensity = 2.2f;
            fill.range = 9f;
            fillGo.transform.position = new Vector3(0f, 1.3f, -3.2f);

            // ---- Post-processing volume. ----
            var volGo = new GameObject("GlobalVolume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreatePostProfile();
            WarnIfNoPostProcessData();

            // ---- Runtime-built floating-island arena (level set to match the director below). ----
            var stageGo = new GameObject("BattleStage");
            var stage = stageGo.AddComponent<BattleStage>();

            // ---- Player avatar (bottom-center, facing camera) + MediaPipe detector. ----
            var player = new GameObject("PlayerCharacter");
            player.transform.position = Vector3.zero;

            MediaPipePoseDetector detector = null;
            var prefab = CharacterLibrary.LoadPrefab(CharacterId);
            if (prefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                model.transform.SetParent(player.transform, false);
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the camera
                detector = model.AddComponent<MediaPipePoseDetector>();
                ConfigureDetector(detector);

                // Strip any imported lights/cameras baked into the FBX — same fix as
                // FruitGameSceneBuilder/BalanceQuestSceneBuilder (a stray Blender-export Point
                // light at intensity 1000 blows every Lit surface toward white/red).
                foreach (var stray in model.GetComponentsInChildren<Light>(true)) stray.enabled = false;
                foreach (var strayCam in model.GetComponentsInChildren<Camera>(true)) strayCam.enabled = false;

                // Same fix as BalanceQuestSceneBuilder: the avatar's renderers default to
                // BlendProbes and sample stale baked light-probe ambient instead of the live
                // per-level Trilight ambient BattleStage sets — leaving the character's colors
                // flat/off against the scene mood. Force them onto the live ambient.
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                // The FBX's eye material renders as glowing orange-red rings (looks like a bug
                // at this camera distance). Scene-scoped: clone the material and pull its tint
                // down to a calm dark brown — texture detail stays, the neon ring goes.
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (r.gameObject.name != "eyes_l" && r.gameObject.name != "eyes_r") continue;
                    var calm = new Material(r.sharedMaterial) { name = "EyeCalm" };
                    calm.color = new Color(0.42f, 0.30f, 0.24f);
                    r.sharedMaterial = calm;
                }

                // DumpAvatarMaterials showed almost every body part (face "mat"/"da1", hands
                // "tay1", pants "quan", hair "toc1", shoes "giay_UV") ships as the plain Unity
                // default grey (0.735,0.735,0.735 @ smoothness 0.5) - nobody ever authored a
                // color for them. Only the shirt ("ao"/"ao1", flat red) and eyes had real
                // tints. That untouched default grey is what reads as a "pale grey-ish face"
                // once ambient tints it, while limbs/edges catching warmer key/rim light look
                // pinker by comparison - same material, inconsistent read. Scene-scoped clones
                // give every part a deliberate, consistent warm tone instead. Skin (face+hands)
                // share one tone so they read as the same person; shirt/pants smoothness comes
                // down to fabric-flat (was 0.5, plasticky).
                var skinTone = new Color(0.86f, 0.67f, 0.53f);
                var hairTone = new Color(0.22f, 0.14f, 0.10f);
                var shirtTone = new Color(0.76f, 0.33f, 0.24f);
                var pantsTone = new Color(0.27f, 0.32f, 0.40f);
                var shoeTone = new Color(0.88f, 0.86f, 0.82f);
                ApplyBodyMaterial(model, "mat", skinTone, 0.28f, "SkinFace");
                ApplyBodyMaterial(model, "tay1", skinTone, 0.28f, "SkinHands");
                ApplyBodyMaterial(model, "ao", shirtTone, 0.12f, "ShirtFabric");
                ApplyBodyMaterial(model, "quan", pantsTone, 0.12f, "PantsFabric");
                ApplyBodyMaterial(model, "toc1", hairTone, 0.3f, "HairWarm");
                ApplyBodyMaterial(model, "giay_UV", shoeTone, 0.25f, "ShoeLight");

                // Eyebrows + eyelashes ("may_l"/"may_r"/"mi_l"/"mi_r", all sharing the same
                // untouched default-grey "may_mi" material) read as pale whitish streaks
                // against the new warm skin + dark hair — tint them to match the hair.
                var browTone = new Color(0.16f, 0.10f, 0.07f);
                foreach (var browName in new[] { "may_l", "may_r", "mi_l", "mi_r" })
                    ApplyBodyMaterial(model, browName, browTone, 0.2f, "BrowLashDark");

                FrameCameraOnAvatar(camGo.transform, model);
            }
            else
            {
                Debug.LogWarning($"[BattleGameSceneBuilder] Character prefab missing for id '{CharacterId}'.");
                camGo.transform.position = new Vector3(0f, 1.6f, -3.6f);
                camGo.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            }

            // ---- Monster slot: mid-upper across the arena from the avatar. ----
            var monsterSlotGo = new GameObject("MonsterSlot");
            monsterSlotGo.transform.position = new Vector3(0f, 1.1f, 4.6f);

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

            // ---- Director (state machine) + result bridge + voice. ----
            var directorGo = new GameObject("Director");
            var director = directorGo.AddComponent<BattleDirector>();
            directorGo.AddComponent<BattleResultBridge>();
            var voice = directorGo.AddComponent<VoiceCoach>();
            directorGo.AddComponent<AudioSource>();

            var ttsOrchestrator = directorGo.AddComponent<TtsOrchestrator>();
            var voiceSo = new SerializedObject(voice);
            voiceSo.FindProperty("tts").objectReferenceValue = ttsOrchestrator;
            voiceSo.ApplyModifiedPropertiesWithoutUndo();

            director.level = 1;
            stage.level = 1;
            director.poseDetector = detector;
            director.monsterSlot = monsterSlotGo.transform;
            director.worldCamera = cam;
            director.voice = voice;
            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(stage);

            // ---- UI. ----
            Debug.Log("[BattleGameSceneBuilder] " + BattleGameUIBuilder.BuildUI());

            // ---- Build Settings: append only, never reorder (Boot stays index 0). ----
            AppendToBuildSettings(ScenePath);

            // ---- Framing screenshot (temp env — BattleStage only builds at runtime). ----
            var tempEnv = new GameObject("~TempEnv");
            BattleStage.BuildStage(tempEnv.transform, 1);
            RenderScreenshot(cam, ScreenshotPath);
            Object.DestroyImmediate(tempEnv);

            // ---- Save. ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BattleGameSceneBuilder] Scene built + saved: {ScenePath}. Screenshot: {ScreenshotPath}");
        }

        // Same analytical framing as BalanceQuestSceneBuilder.FrameCameraOnAvatar: normalize the
        // model to 1.7m, then place the camera from its real renderer bounds so the avatar fills
        // the lower ~55% of frame with room above for the monster slot + sky.
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

        // Scene-scoped material clone by exact renderer name (the FBX asset itself stays
        // untouched for the other games/scenes sharing it). Sets both the base color and
        // smoothness so fabric parts stop reading as plastic.
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
            if (!AssetDatabase.IsValidFolder("Assets/BattleGame"))
                AssetDatabase.CreateFolder("Assets", "BattleGame");
            AssetDatabase.DeleteAsset(PostProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            var bloom = AddOverride<Bloom>(profile);
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.7f);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.55f);

            var colors = AddOverride<ColorAdjustments>(profile);
            colors.saturation.Override(12f);
            colors.contrast.Override(8f);
            colors.postExposure.Override(0.2f);

            var whiteBalance = AddOverride<WhiteBalance>(profile);
            whiteBalance.temperature.Override(10f);

            var smh = AddOverride<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.85f, 0.9f, 1.05f, 0.15f));
            smh.midtones.Override(new Vector4(1.0f, 0.98f, 0.98f, 0.1f));
            smh.highlights.Override(new Vector4(1.12f, 1.02f, 0.85f, 0.15f));

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

        static void WarnIfNoPostProcessData()
        {
            bool anyPost = false;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && data.postProcessData != null) { anyPost = true; break; }
            }
            if (!anyPost)
                Debug.LogWarning("[BattleGameSceneBuilder] WARNING: no UniversalRendererData in the project " +
                                 "has Post Process Data assigned — Bloom/Vignette/ColorAdjustments will NOT render.");
        }

        // ---- QA-only: level 2/3 spot-check screenshots, reusing the already-built scene's camera
        // + lights without touching the saved scene file (no BuildScene rebuild, no save). Just
        // swaps the temp-env stage level and renders to a level-tagged path. ----
        public static void PreviewLevel2()
        {
            RenderLevelPreview(2, ScreenshotPath.Replace("battlegame_scene.png", "battlegame_scene_level2.png"));
        }

        public static void PreviewLevel3()
        {
            RenderLevelPreview(3, ScreenshotPath.Replace("battlegame_scene.png", "battlegame_scene_level3.png"));
        }

        static void RenderLevelPreview(int level, string path)
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError($"[BattleGameSceneBuilder] {ScenePath} doesn't exist yet — run Build Battle Game Scene first.");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[BattleGameSceneBuilder] no Main Camera in the built scene.");
                return;
            }
            var tempEnv = new GameObject("~TempEnv");
            BattleStage.BuildStage(tempEnv.transform, level);
            RenderScreenshot(cam, path);
            Object.DestroyImmediate(tempEnv);
            Debug.Log($"[BattleGameSceneBuilder] Level {level} preview screenshot: {path}");
        }

        static void RenderScreenshot(Camera cam, string path)
        {
            var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render(); // warm-up render — first frame after a batchmode scene load renders
                cam.Render(); // runtime materials wrong; the second render is correct.
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
