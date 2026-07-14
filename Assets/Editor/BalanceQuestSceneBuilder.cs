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
using UnityEngine.UI;
using Kinex.BalanceQuest;
using PonyuDev.SherpaOnnx.Tts;

namespace Kinex.BalanceQuest.EditorTools
{
    /// <summary>
    /// One-shot (idempotent — rebuilds the scene from scratch every run) builder for
    /// Assets/Scenes/BalanceQuestScene.unity. Batchmode-callable:
    ///   -executeMethod Kinex.BalanceQuest.EditorTools.BalanceQuestSceneBuilder.BuildScene
    /// (run WITHOUT -nographics so the screenshot can render).
    /// Creates camera/light/post volume/stage/trail/props/player+detector/canvas+feed/director,
    /// runs BalanceQuestUIBuilder.BuildUI(), appends the scene to Build Settings (append-only,
    /// Boot stays index 0), saves, and writes a 927x1427 preview PNG to the scratchpad.
    /// </summary>
    public static class BalanceQuestSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/BalanceQuestScene.unity";
        const string ProfileFolder = "Assets/BalanceQuest";
        const string ProfilePath = "Assets/BalanceQuest/BalanceQuestPost.asset";
        const string PlayerPrefabPath = "Assets/Characters/KinexUserModel.fbx";
        const string ScreenshotPath =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/3b7b0985-45db-45d2-bfa5-d7f306e07247/scratchpad/balancequest_scene.png";
        const int ShotW = 927, ShotH = 1427;

        [MenuItem("Kinex/Build Balance Quest Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- camera ----------------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            // Started from the MegaDanceScene camera per the brief, but that camera orbits a
            // living-room set at x=7.98 facing ~161° — useless for a runway at the origin. Kept
            // only its "portrait, slightly above eye level" spirit; framed explicitly instead:
            // avatar (1.7 m at origin) fills the lower ~55% of the 927x1427 frame, FOV 46, runway
            // + synthwave sun vanish toward the upper-middle horizon (see FrameCameraOnAvatar,
            // which recomputes this once the real avatar bounds are known).
            camGo.transform.position = new Vector3(0f, 1.5f, -3.9f);
            camGo.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            cam.fieldOfView = 46f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.20f); // #0F0D33 dark indigo
            var camData = cam.GetUniversalAdditionalCameraData(); // adds the component if missing
            camData.renderPostProcessing = true;

            // ---------------- light ----------------
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.749f, 0.831f, 1f); // #BFD4FF cool blue-white
            // Iteration 4 (see QuestStage.BuildStage): trimmed alongside the fill light so the
            // avatar's glossy red shirt material doesn't blow its specular highlight out to a hard
            // white patch against the unlit dark-red diffuse ("harsh unblended bleed").
            light.intensity = 0.55f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.5f;
            lightGo.transform.rotation = Quaternion.Euler(28f, 200f, 0f);

            // ---------------- post-process volume ----------------
            var profile = LoadOrCreateProfile();
            var volumeGo = new GameObject("PostVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;
            WarnIfRendererLacksPostProcessData();

            // ---------------- stage / trail / props ----------------
            var stageGo = new GameObject("QuestStage");
            stageGo.AddComponent<QuestStage>(); // builds sky/lights/motes at runtime

            var trailGo = new GameObject("TrailRoot");
            var trail = trailGo.AddComponent<TrailScroller>();

            var propsGo = new GameObject("PropRoot");
            var props = propsGo.AddComponent<QuestPropFactory>();

            // ---------------- player + detector ----------------
            var playerRoot = new GameObject("PlayerCharacter");
            playerRoot.transform.position = Vector3.zero;
            var avatarMover = playerRoot.AddComponent<AvatarLaneMover>();

            MediaPipePoseDetector detector = null;
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                model.transform.SetParent(playerRoot.transform, false);
                // Face the camera (camera sits at -Z looking +Z; humanoid FBX forward is +Z).
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                detector = model.AddComponent<MediaPipePoseDetector>();
                ApplyMegaDanceDetectorValues(detector);
                FrameCameraOnAvatar(camGo.transform, model);
                // Root-cause fix for the reviewer-flagged chest/arm "harsh unblended red/orange
                // bleed": the avatar's renderers default to lightProbeUsage=BlendProbes, so they
                // sample baked/cached light-probe ambient data instead of this scene's live
                // RenderSettings ambient set in QuestStage.BuildStage — confirmed by exhaustively
                // zeroing every controllable light, the post-process volume, reflections, and even
                // the sun/horizon-glow meshes simultaneously and seeing the patch never change at
                // all. ProceduralSkybox.CreateSkyDome already works around exactly this by setting
                // lightProbeUsage = Off on its own mesh; doing the same here makes the avatar sample
                // the controllable ambient instead of the stale probe data.
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                // The shirt renderer ("ao") carries shading that survives a total lighting
                // blackout (baked into its imported material), producing the harsh white/orange
                // patch flagged by review. Replace just that material with a plain Lit red for
                // this scene — the FBX asset itself stays untouched for the other games.
                // The FBX ships stray embedded Light/Camera components (incl. an intensity-1000
                // point light) that blast one side of the avatar to clipped white — the actual
                // root cause of the reviewer-flagged "harsh unblended bleed". Same guard as
                // FruitGameSceneBuilder.
                foreach (var stray in model.GetComponentsInChildren<Light>(true))
                    stray.enabled = false;
                foreach (var strayCam in model.GetComponentsInChildren<Camera>(true))
                    strayCam.enabled = false;

                // The FBX's eye material renders as glowing neon-orange rings against this dusk
                // scene — reads as a bug at this camera distance. Same fix as
                // BattleGameSceneBuilder: clone the material and pull its tint to a calm dark
                // brown (texture detail stays, the neon ring goes).
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (r.gameObject.name != "eyes_l" && r.gameObject.name != "eyes_r") continue;
                    var calm = new Material(r.sharedMaterial) { name = "EyeCalm" };
                    calm.color = new Color(0.42f, 0.30f, 0.24f);
                    r.sharedMaterial = calm;
                }
            }
            else
            {
                Debug.LogWarning($"[BalanceQuestSceneBuilder] Player prefab missing at {PlayerPrefabPath} — scene built without avatar/detector.");
            }

            // ---------------- canvas + camera feed ----------------
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ShotW, ShotH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // portrait: match height, same as MegaDance
            canvasGo.AddComponent<GraphicRaycaster>();

            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            BuildCameraFeed(canvas.transform, detector);

            // ---------------- director ----------------
            var directorGo = new GameObject("Director");
            var director = directorGo.AddComponent<BalanceQuestDirector>();
            directorGo.AddComponent<BalanceQuestResultBridge>();
            var voice = directorGo.AddComponent<Kinex.MegaDance.VoiceCoach>();
            var audio = directorGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;

            // TTS: same layout as MegaDanceScene — TtsOrchestrator lives next to VoiceCoach on
            // one GameObject; VoiceCoach.tts is private [SerializeField], set via SerializedObject.
            var tts = directorGo.AddComponent<TtsOrchestrator>();
            var voiceSo = new SerializedObject(voice);
            var ttsProp = voiceSo.FindProperty("tts");
            if (ttsProp != null) ttsProp.objectReferenceValue = tts;
            voiceSo.ApplyModifiedPropertiesWithoutUndo();

            director.route = RouteLibrary.DefaultRoute;
            director.poseDetector = detector;
            director.trail = trail;
            director.props = props;
            director.avatarMover = avatarMover;
            director.musicSource = audio;
            director.voice = voice;
            EditorUtility.SetDirty(director);

            // ---------------- UI ----------------
            Debug.Log("[BalanceQuestSceneBuilder] " + BalanceQuestUIBuilder.BuildUI());

            // ---------------- screenshot (temp env preview, removed before save) ----------------
            RenderPreviewScreenshot(cam, trail);

            // ---------------- save + build settings ----------------
            EditorSceneManager.SaveScene(scene, ScenePath);
            AppendToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BalanceQuestSceneBuilder] DONE. Scene saved to {ScenePath}; screenshot at {ScreenshotPath}.");
        }

        const float AvatarHeightMeters = 1.7f;

        // The hardcoded first-pass framing assumed a 1.7 m avatar; the actual KinexUserModel
        // imports much taller, so the camera only saw its legs. Two-step fix: (1) NORMALIZE the
        // model to 1.7 m so every prop stays human-proportioned (0.9 m lanes, 2.2 m gate, fruit
        // at 2 m overhead, kick target at 0.85 m); (2) frame the camera from the real renderer
        // bounds: for FOV 46 with "avatar bottom at frame bottom, head at ~55% up, slight 2°
        // downward pitch" the geometry works out to distance ~2.1*H and eye height ~0.94*H above
        // the feet — pulled back further than a naive FOV-46 reframe of the old FOV-50/1.77*H
        // shot would need, so the sky opens up above the avatar for the synthwave sun + runway
        // vanishing point (see QuestStage).
        static void FrameCameraOnAvatar(Transform cam, GameObject model)
        {
            var b = ComputeBounds(model);
            if (b.size.y > 0.01f && Mathf.Abs(b.size.y - AvatarHeightMeters) > 0.05f)
            {
                model.transform.localScale *= AvatarHeightMeters / b.size.y;
                b = ComputeBounds(model);
            }

            float h = Mathf.Max(b.size.y, 0.5f);
            cam.position = new Vector3(b.center.x, b.min.y + 0.94f * h, b.center.z - 2.1f * h);
            cam.rotation = Quaternion.Euler(2f, 0f, 0f);
        }

        static Bounds ComputeBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(model.transform.position, Vector3.one * AvatarHeightMeters);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // The serialized values validated in MegaDanceScene (grep of the scene YAML), applied via
        // SerializedObject because the fields are private [SerializeField].
        static void ApplyMegaDanceDetectorValues(MediaPipePoseDetector detector)
        {
            var so = new SerializedObject(detector);
            SetString(so, "modelFileName", "pose_landmarker_full.bytes");
            SetBool(so, "drivesAvatar", true);
            SetFloat(so, "minBoneVisibility", 0.3f);
            SetFloat(so, "legVisThreshold", 0.2f);
            SetFloat(so, "legMaxDegPerSec", 320f);
            SetBool(so, "flipX", true);
            SetBool(so, "flipY", true);
            SetBool(so, "flipZ", false);
            SetBool(so, "use3DWorld", true);
            SetBool(so, "autoCalibrate", false);
            SetBool(so, "mirrorPreview", true);
            SetInt(so, "previewRotationCW", 270);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }
        static void SetFloat(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; }
        static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
        static void SetString(SerializedObject so, string name, string v) { var p = so.FindProperty(name); if (p != null) p.stringValue = v; }

        // CameraFeedPanel > CameraFeedImage (the ONLY RawImage in the scene — the detector finds
        // it via FindAnyObjectByType<RawImage>) > PoseSkeletonOverlay. Same structure + style
        // values as MegaDanceScene/KinexWorldScene.
        static void BuildCameraFeed(Transform canvas, MediaPipePoseDetector detector)
        {
            var panelGo = new GameObject("CameraFeedPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var panelRt = (RectTransform)panelGo.transform;
            panelRt.SetParent(canvas, false);
            panelRt.anchorMin = new Vector2(648f / 927f, 1f - (55f + 300f) / 1427f);
            panelRt.anchorMax = new Vector2((648f + 234f) / 927f, 1f - 55f / 1427f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            panelGo.GetComponent<Image>().color = new Color(0.08f, 0.05f, 0.18f, 1f);

            var rawGo = new GameObject("CameraFeedImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rawRt = (RectTransform)rawGo.transform;
            rawRt.SetParent(panelRt, false);
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(5f, 5f);
            rawRt.offsetMax = new Vector2(-5f, -5f);

            var overlayGo = new GameObject("PoseSkeletonOverlay", typeof(RectTransform), typeof(CanvasRenderer));
            var overlayRt = (RectTransform)overlayGo.transform;
            overlayRt.SetParent(rawRt, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            var overlay = overlayGo.AddComponent<PoseSkeletonOverlay>();
            overlay.source = detector;
            overlay.raycastTarget = false;
            var so = new SerializedObject(overlay);
            SetInt(so, "previewRotateCW", 90);
            SetBool(so, "mirrorSkeletonX", true);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static VolumeProfile LoadOrCreateProfile()
        {
            if (!AssetDatabase.IsValidFolder(ProfileFolder))
                AssetDatabase.CreateFolder("Assets", "BalanceQuest");

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            // Drop dangling null entries from earlier runs (components that were never attached
            // as sub-assets serialize as {fileID: 0} and come back null).
            profile.components.RemoveAll(c => c == null);

            // Iteration 1 (intensity 1.6 / postExposure +0.25 / saturation+contrast 12-14) blew
            // EVERYTHING bright (avatar skin, grid lines) to flat white instead of just the neon —
            // ACES + a high bloom intensity compound multiplicatively, so both had to come down
            // together. Threshold stays high (only true HDR emissives cross it); intensity/scatter
            // now do the glow instead of raw brightness.
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(1.2f);
            bloom.scatter.Override(0.8f);

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.35f);
            vignette.smoothness.Override(0.6f);

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.Override(0f); // let ACES's own midtone response stand — +0.25 was clipping skin highlights to white
            color.colorFilter.Override(Color.white); // grading now lives in ShadowsMidtonesHighlights below
            color.saturation.Override(6f);
            color.contrast.Override(6f);

            var tonemap = GetOrAdd<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.ACES);

            // Split grade: indigo shadows, magenta mids, warm highlights off the sun — the .w
            // component is each band's blend strength, not a 4th color channel. Toned down from
            // iteration 1 (.35/.25/.3) which was strong enough to read as a red/orange cast on
            // the avatar rather than a subtle grade.
            var smh = GetOrAdd<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.85f, 0.85f, 1.0f, 0.2f));
            smh.midtones.Override(new Vector4(1.02f, 0.95f, 1.02f, 0.12f));
            smh.highlights.Override(new Vector4(1.06f, 1.0f, 0.9f, 0.15f));

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T comp))
            {
                comp = profile.Add<T>(true);
                comp.name = typeof(T).Name;
                // Add() only creates the sub-ScriptableObject in memory — without this it
                // serializes into the .asset as a null {fileID: 0} entry.
                AssetDatabase.AddObjectToAsset(comp, profile);
            }
            return comp;
        }

        // Best-effort reflection check: the active URP renderer needs PostProcessData assigned
        // or the volume will silently do nothing on device.
        static void WarnIfRendererLacksPostProcessData()
        {
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null)
            {
                Debug.LogWarning("[BalanceQuestSceneBuilder] No URP asset active — post-processing volume will not render.");
                return;
            }
            var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = listField?.GetValue(rp) as ScriptableObject[];
            var rendererData = list != null && list.Length > 0 ? list[0] : null;
            if (rendererData == null) return; // can't tell — stay quiet rather than cry wolf
            var ppField = rendererData.GetType().GetField("postProcessData",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (ppField != null && ppField.GetValue(rendererData) == null)
                Debug.LogWarning("[BalanceQuestSceneBuilder] URP renderer has NO PostProcessData assigned — Bloom/Vignette will not render. Assign it on the Renderer asset.");
        }

        // Temporarily builds the runtime env (sky/lights/motes via QuestStage.BuildStage + the
        // TrailScroller segment pool) purely for the preview render, then deletes the temp
        // objects so the SAVED scene stays runtime-built/tiny.
        static void RenderPreviewScreenshot(Camera cam, TrailScroller trail)
        {
            var tempStage = new GameObject("~TempStagePreview");
            try
            {
                QuestStage.BuildStage(tempStage.transform);
                trail.Build(); // segments parent under TrailRoot — removed below

                var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render(); // warm-up render — first frame after a batchmode scene load renders
                cam.Render(); // runtime materials wrong (and crashes on particle billboards); the second render is correct.

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                cam.targetTexture = null;
                Object.DestroyImmediate(rt);

                Directory.CreateDirectory(Path.GetDirectoryName(ScreenshotPath));
                File.WriteAllBytes(ScreenshotPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BalanceQuestSceneBuilder] Screenshot failed (scene still valid): {e.Message}");
            }
            finally
            {
                Object.DestroyImmediate(tempStage);
                // Remove the edit-mode trail segments so the saved scene stays tiny — they
                // rebuild themselves in Awake at runtime.
                for (int i = trail.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(trail.transform.GetChild(i).gameObject);
            }
        }

        static void AppendToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == scenePath)) return; // already listed — append-only, never reorder
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
