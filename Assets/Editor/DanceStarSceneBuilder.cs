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
using Kinex.DanceStar;
using Kinex.Trainer;
using PonyuDev.SherpaOnnx.Tts;

namespace Kinex.EditorTools
{
    /// <summary>
    /// One-click / batchmode builder for Assets/Scenes/DanceStarScene.unity: camera + concert-mood
    /// lights + ACES post volume, the runtime-built DanceStage (neon floor, podium, spotlights,
    /// crowd), the MediaPipe-driven player avatar CENTRE-stage (same gotcha fixes as
    /// MirrorGameSceneBuilder), the trainer "coach star" rig standing ON the podium beside the
    /// player (TrainerPoseController wired to DancePoseData.asset), the portrait UI canvas with
    /// camera feed, and the fully wired DanceStarDirector (+ result bridge + TTS voice). Idempotent:
    /// reopens the scene and rebuilds its known roots by name. Ends by appending the scene to Build
    /// Settings and rendering TWO 927x1427 screenshots to the scratchpad: the bare stage (UI
    /// overlay canvases don't render in cam.Render()) and a second pass with the HUD panel forced
    /// on + the canvas flipped to camera space (SceneShot.cs's -shotUI trick, done inline here so
    /// one batch run produces both) — then reverts both back to their normal saved-scene state
    /// (intro panel active, canvas back to Screen Space Overlay) before saving.
    /// </summary>
    public static class DanceStarSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/DanceStarScene.unity";
        const string PostProfilePath = "Assets/DanceStar/DanceStarPost.asset";
        const string CharacterId = CharacterLibrary.DefaultId;
        const string DancePoseDataPath = "Assets/Animations/DancePoseData.asset";
        // The trainer rig must be NewTrainerAnimated.fbx — DancePoseData poses only reproduce on
        // that exact rig (memory: trainer_pose_rig_pairing).
        const string TrainerRigPath = "Assets/Characters/NewTrainerAnimated.fbx";
        const string ScratchDir =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/552c1cb4-f9f4-4e3c-b616-07a08892f6bc/scratchpad";
        const string ScreenshotPath = ScratchDir + "/dancestar_scene.png";
        const string HudScreenshotPath = ScratchDir + "/dancestar_hud.png";
        const int ShotW = 927, ShotH = 1427;
        const float AvatarHeightMeters = 1.7f;
        const float TrainerHeightMeters = 1.7f;
        const string DemoPoseName = "t_arms"; // reads clearly (arms out) for the stage screenshot

        static readonly string[] OwnedRoots =
        {
            "Main Camera", "KeyLight", "FillLight", "RimLight", "GlobalVolume",
            "DanceStage", "PlayerCharacter", "TrainerRig", "Canvas", "EventSystem", "Director", "~TempEnv",
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

            // ---- Main Camera. ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 42f; // slightly narrower than Mirror/Temple — needs headroom to fit two subjects
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.02f, 0.06f);
            cam.allowHDR = true; // neon/spotlight/podium glow rely on HDR values clearing the bloom threshold
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // ---- Cool "stage wash" key from above-front — real shadows on the floor. ----
            var keyGo = new GameObject("KeyLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.color = new Color(0.75f, 0.82f, 1f);
            keyGo.transform.rotation = Quaternion.Euler(48f, -12f, 0f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.6f;

            // ---- Magenta fill near the camera so the pose-critical front stays readable. ----
            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.45f, 0.75f);
            fill.intensity = 2.4f;
            fill.range = 9f;
            fillGo.transform.position = new Vector3(0f, 1.3f, -3.2f);

            // ---- Cyan rim behind both subjects — keeps the podium/crowd readable past the wash. ----
            var rimGo = new GameObject("RimLight");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.4f, 0.85f, 1f);
            rim.intensity = 0.9f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(18f, 180f, 0f);

            // ---- Post-processing volume (ACES + strong bloom for the neon). ----
            var volGo = new GameObject("GlobalVolume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreatePostProfile();

            // ---- Runtime-built concert stage. ----
            var stageGo = new GameObject("DanceStage");
            stageGo.AddComponent<DanceStage>();

            // ---- Player avatar: centre stage, in the spotlight pool. ----
            var player = new GameObject("PlayerCharacter");
            player.transform.position = DanceStage.PlayerLocalPos;

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

                // "Superstar" wardrobe: a bit more sparkle-friendly than Mirror's studio outfit.
                var skinTone = new Color(0.86f, 0.67f, 0.53f);
                var hairTone = new Color(0.22f, 0.14f, 0.10f);
                var shirtTone = new Color(0.75f, 0.35f, 0.6f);   // stage-lit magenta top
                var pantsTone = new Color(0.18f, 0.18f, 0.28f);  // dark so it doesn't compete with neon
                var shoeTone = new Color(0.85f, 0.85f, 0.9f);
                ApplyBodyMaterial(model, "mat", skinTone, 0.28f, "SkinFace");
                ApplyBodyMaterial(model, "tay1", skinTone, 0.28f, "SkinHands");
                ApplyBodyMaterial(model, "ao", shirtTone, 0.35f, "ShirtStage");
                ApplyBodyMaterial(model, "quan", pantsTone, 0.12f, "PantsStage");
                ApplyBodyMaterial(model, "toc1", hairTone, 0.3f, "HairWarm");
                ApplyBodyMaterial(model, "giay_UV", shoeTone, 0.3f, "ShoeStage");
                var browTone = new Color(0.16f, 0.10f, 0.07f);
                foreach (var browName in new[] { "may_l", "may_r", "mi_l", "mi_r" })
                    ApplyBodyMaterial(model, browName, browTone, 0.2f, "BrowLashDark");

                FrameHeightTo(model, AvatarHeightMeters);
            }
            else
            {
                Debug.LogWarning($"[DanceStarSceneBuilder] Character prefab missing for id '{CharacterId}'.");
            }

            // ---- Trainer "coach star" rig: on the podium, beside the player. ----
            var trainerGo = new GameObject("TrainerRig");
            trainerGo.transform.position = DanceStage.PodiumLocalPos + new Vector3(0f, DanceStage.PodiumTopY, 0f);
            // A fresh identity-rotation NewTrainerAnimated instance faces world -X (verified by
            // DanceTrainerDiag's 4-angle sweep — NOT +Z like most rigs), so -90° turns its front
            // to -Z toward the stage camera. 180° here left the trainer in profile ("lopsided"
            // in earlier screenshots — the pose data itself was always fine).
            trainerGo.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            TrainerPoseController trainerController = null;
            var trainerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrainerRigPath);
            var dancePoseData = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(DancePoseDataPath);
            if (trainerPrefab != null)
            {
                // Object.Instantiate, NOT PrefabUtility.InstantiatePrefab — matches the proven-working
                // Kinex.BattleGame.PoseGhost / Kinex.MirrorGame.MirrorOutline pattern for spawning a
                // TrainerPoseController-driven rig at runtime. Round-3/4 screenshots showed the
                // PrefabUtility.InstantiatePrefab instance rendering a wrong (partially-posed) stance
                // even with the Animator disabled — worth a second look from whoever owns
                // TrainerPoseController/DancePoseData if this doesn't fix it (see report).
                var trainerModel = Object.Instantiate(trainerPrefab, trainerGo.transform);
                trainerModel.transform.localPosition = Vector3.zero;
                trainerModel.transform.localRotation = Quaternion.identity;
                FrameHeightTo(trainerModel, TrainerHeightMeters);

                foreach (var stray in trainerModel.GetComponentsInChildren<Light>(true)) stray.enabled = false;
                foreach (var strayCam in trainerModel.GetComponentsInChildren<Camera>(true)) strayCam.enabled = false;
                foreach (var r in trainerModel.GetComponentsInChildren<Renderer>())
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                // The Humanoid Animator applies its own default-state pose on enable/render, on
                // top of whatever TrainerPoseController just wrote — normally TrainerPoseController
                // .LateUpdate() re-overwrites it every frame and wins the fight, but that only runs
                // during Play, so it never fires for this batchmode screenshot (or for a scene that
                // never enters Play at all). DancePoseBuilder's own bake tool disables every
                // Animator for exactly this reason; DanceStarDirector never reads trainer.Animator,
                // so disabling it here is safe for real gameplay too — not just this screenshot.
                foreach (var anim in trainerModel.GetComponentsInChildren<Animator>(true)) anim.enabled = false;

                trainerController = trainerModel.AddComponent<TrainerPoseController>();
                trainerController.autoAdvance = false;
                trainerController.blendTime = 0.7f;
                if (dancePoseData != null)
                {
                    int startIndex = FindPoseIndex(dancePoseData, DemoPoseName);
                    // Snap immediately (PoseGhost pattern) — TrainerPoseController.Start() only
                    // runs in Play mode, so relying on it would leave the rig in bind pose for
                    // this batchmode screenshot.
                    trainerController.InitRuntime(dancePoseData, trainerModel.transform, startIndex >= 0 ? startIndex : 0);

                    // Ground the feet on the podium. Renderer.bounds is STALE bind-pose data after
                    // direct bone writes in edit mode, so measure the true posed silhouette by
                    // baking each skinned mesh (same trick the pose-preview renderer uses). One
                    // shift on the parent covers all standing poses (hips height is baked per pose).
                    float podiumTop = DanceStage.PodiumLocalPos.y + DanceStage.PodiumTopY;
                    float minY = float.PositiveInfinity;
                    foreach (var smr in trainerModel.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        var verts = baked.vertices;
                        for (int vi = 0; vi < verts.Length; vi++)
                        {
                            float y = smr.transform.TransformPoint(verts[vi]).y;
                            if (y < minY) minY = y;
                        }
                        Object.DestroyImmediate(baked);
                    }
                    if (!float.IsPositiveInfinity(minY))
                        trainerGo.transform.position += Vector3.down * (minY - podiumTop);
                }
                else
                {
                    Debug.LogWarning($"[DanceStarSceneBuilder] {DancePoseDataPath} not found — trainer will show bind pose.");
                }
            }
            else
            {
                Debug.LogWarning($"[DanceStarSceneBuilder] Trainer rig prefab missing at '{TrainerRigPath}'.");
            }

            FrameCameraOnPair(camGo.transform, cam, model, trainerGo);

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
            director.trainer = trainerController;
            director.dancePoseData = dancePoseData;
            director.useKeyboardStub = false; // baked scene is device-ready; flip on in the Inspector for editor play
            EditorUtility.SetDirty(director);

            // ---- UI (chained, like the other game builders). ----
            Debug.Log("[DanceStarSceneBuilder] " + DanceStarUIBuilder.BuildUI());

            // ---- Build Settings: append only (Boot stays index 0). ----
            AppendToBuildSettings(ScenePath);

            // ---- Screenshots: the REAL "DanceStage" component's Awake() never fires in a
            // batchmode build with no Play (same lesson as Mirror/Temple), so a throwaway ~TempEnv
            // gets the stage built explicitly for the render, then destroyed before the scene is
            // saved (the saved DanceStage component builds itself normally once the scene actually
            // loads via Play/device). First shot is the bare stage (UI overlay canvas doesn't
            // render in cam.Render()); second forces the HUD on + flips the canvas to camera space.
            // Guarded so a screenshot/IO failure never aborts the build before the scene is saved. ----
            var tempEnv = new GameObject("~TempEnv");
            try
            {
                DanceStage.BuildStage(tempEnv.transform);
                RenderScreenshot(cam, ScreenshotPath);
                RenderHudScreenshot(cam, canvas, director);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DanceStarSceneBuilder] screenshot failed — scene still saved: {e.Message}");
            }
            finally
            {
                Object.DestroyImmediate(tempEnv);
            }

            // ---- Save. ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DanceStarSceneBuilder] Scene built + saved: {ScenePath}. " +
                      $"Screenshots: {ScreenshotPath} , {HudScreenshotPath}");
        }

        // Frames the camera to keep BOTH the centre-stage player AND the podium trainer fully
        // visible head-to-toe in the narrow portrait frustum. Computed from real renderer bounds
        // and the camera's actual FOV/aspect (not eyeballed constants) — a portrait aspect makes
        // the HORIZONTAL FOV the binding constraint once two side-by-side subjects are involved.
        //
        // The camera's HORIZONTAL AIM stays locked to the PLAYER's own centre (not the midpoint
        // between player and trainer) — every HUD panel assumes a centred player like every other
        // Kinex game, and detection/composition both want the player dead-centre. The trainer
        // just needs to fit inside whatever half-width that leaves; the distance is pulled back
        // until it does.
        static void FrameCameraOnPair(Transform camT, Camera cam, GameObject player, GameObject trainerRoot)
        {
            Bounds? combined = null;
            if (player != null) combined = Encapsulate(combined, ComputeBounds(player));
            if (trainerRoot != null) combined = Encapsulate(combined, ComputeBounds(trainerRoot));
            if (!combined.HasValue)
            {
                camT.position = new Vector3(0f, 1.6f, -5.2f);
                camT.rotation = Quaternion.Euler(3.5f, 0f, 0f);
                return;
            }

            var bounds = combined.Value;
            float centerX = player != null ? ComputeBounds(player).center.x : bounds.center.x;

            float aspect = ShotW / (float)ShotH;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * aspect);

            const float marginY = 0.4f;  // headroom above the taller subject
            const float marginX = 0.35f; // breathing room past the podium/arm-reach edges
            float halfWidthNeeded = Mathf.Max(centerX - bounds.min.x, bounds.max.x - centerX) + marginX;

            float distV = (bounds.extents.y + marginY) / Mathf.Tan(vHalf);
            float distH = halfWidthNeeded / Mathf.Tan(hHalf);
            float dist = Mathf.Max(distV, distH);

            camT.position = new Vector3(centerX, bounds.min.y + 0.82f * bounds.size.y, bounds.min.z - dist);
            // A steeper downward tilt than Mirror/Temple's single-subject 3.5-4deg: round-2's
            // near-level camera cropped the near floor (and its neon rings) out of frame entirely.
            camT.rotation = Quaternion.Euler(7f, 0f, 0f);
        }

        static Bounds Encapsulate(Bounds? existing, Bounds add)
        {
            if (!existing.HasValue) return add;
            var b = existing.Value;
            b.Encapsulate(add);
            return b;
        }

        static void FrameHeightTo(GameObject model, float heightMeters)
        {
            var b = ComputeBounds(model);
            if (b.size.y > 0.01f && Mathf.Abs(b.size.y - heightMeters) > 0.05f)
                model.transform.localScale *= heightMeters / b.size.y;
        }

        static Bounds ComputeBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(model.transform.position, Vector3.one * AvatarHeightMeters);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static int FindPoseIndex(TrainerPoseData data, string poseName)
        {
            if (data == null || data.poses == null) return -1;
            for (int i = 0; i < data.poses.Length; i++)
                if (data.poses[i].name == poseName) return i;
            return -1;
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
            if (!AssetDatabase.IsValidFolder("Assets/DanceStar"))
                AssetDatabase.CreateFolder("Assets", "DanceStar");
            AssetDatabase.DeleteAsset(PostProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            var bloom = AddOverride<Bloom>(profile);
            // Same empirically-tuned kernel as Mirror/TempleHunt: tight scatter + strong intensity
            // so the neon rings/spotlights/podium rim read clearly without blooming the dark floor.
            bloom.intensity.Override(1.5f);
            bloom.threshold.Override(0.72f);
            bloom.scatter.Override(0.6f);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.42f);
            vignette.smoothness.Override(0.6f);

            var colors = AddOverride<ColorAdjustments>(profile);
            colors.saturation.Override(14f); // punchier neon than Mirror's warm studio
            colors.contrast.Override(12f);
            colors.postExposure.Override(0.1f);

            var whiteBalance = AddOverride<WhiteBalance>(profile);
            whiteBalance.temperature.Override(-4f); // cooler than Mirror — night club, not moonlight lamp

            var smh = AddOverride<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.82f, 0.85f, 1.15f, 0.1f));
            smh.highlights.Override(new Vector4(1.05f, 0.95f, 1.15f, 0.12f)); // magenta-leaning highlights

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

        // Flips the ScreenSpaceOverlay canvas to camera space (SceneShot.cs's -shotUI trick) and
        // forces the HUD panel on for one extra render, so the saved DanceStarScene can still be
        // verified for HUD layout — then puts everything back exactly as the UI builder left it
        // (intro panel active, canvas back to overlay) before the scene is saved to disk.
        static void RenderHudScreenshot(Camera cam, Canvas canvas, DanceStarDirector director)
        {
            if (canvas == null || director == null) return;
            // Transform.Find (not GameObject.Find) — HudPanel starts INACTIVE, and GameObject.Find
            // only searches active objects.
            var hudT = canvas.transform.Find("HudPanel");
            var introT = canvas.transform.Find("IntroPanel");
            var hud = hudT != null ? hudT.gameObject : null;
            var intro = introT != null ? introT.gameObject : null;
            if (hud == null) { Debug.LogWarning("[DanceStarSceneBuilder] HudPanel not found — skipping HUD screenshot."); return; }

            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;
            var prevPlaneDist = canvas.planeDistance;
            bool hudWasActive = hud.activeSelf;
            bool introWasActive = intro != null && intro.activeSelf;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.1f;
            if (intro != null) intro.SetActive(false);
            hud.SetActive(true);
            Canvas.ForceUpdateCanvases();

            RenderScreenshot(cam, HudScreenshotPath);

            hud.SetActive(hudWasActive);
            if (intro != null) intro.SetActive(introWasActive);
            canvas.renderMode = prevMode;
            canvas.worldCamera = prevCam;
            canvas.planeDistance = prevPlaneDist;
            Canvas.ForceUpdateCanvases();
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
