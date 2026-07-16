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
using Kinex.FX;

namespace Kinex.MotionLab
{
    /// <summary>
    /// One-click / batchmode builder for Assets/Scenes/MotionLabScene.unity: camera + warm/cool
    /// lights + a mild ACES/bloom post volume, a minimal runtime-built "lab" floor (dark disc +
    /// concentric cyan calibration rings) under a gradient sky dome, the MediaPipe-driven player
    /// avatar (same gotcha fixes as MirrorGameSceneBuilder — stray FBX light/camera, light probes,
    /// grey-material recolors), and the MotionLabDirector wired to it. Idempotent: reopens the
    /// scene and rebuilds its own roots by name. Ends by appending the scene to Build Settings and
    /// rendering a 927x1427 screenshot to the scratchpad.
    /// </summary>
    public static class MotionLabSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MotionLabScene.unity";
        const string PostProfilePath = "Assets/MotionLab/MotionLabPost.asset";
        const string CharacterId = CharacterLibrary.DefaultId;
        const string ScreenshotPath =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/552c1cb4-f9f4-4e3c-b616-07a08892f6bc/scratchpad/motionlab_scene.png";
        const int ShotW = 927, ShotH = 1427;
        const float AvatarHeightMeters = 1.7f;

        static readonly string[] OwnedRoots =
        {
            "Main Camera", "KeyLight", "FillLight", "RimLight", "GlobalVolume",
            "LabStage", "PlayerCharacter", "Canvas", "EventSystem", "MotionLabDirector",
        };

        [MenuItem("Kinex/Build Motion Lab Scene")]
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
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
            cam.allowHDR = true; // grid-ring glow relies on HDR values clearing the bloom threshold
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // ---- Warm key light w/ soft shadows + cool fill + rim (same trio as MirrorGame). ----
            var keyGo = new GameObject("KeyLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, 0.86f, 0.62f);
            keyGo.transform.rotation = Quaternion.Euler(40f, 18f, 0f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.65f;

            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.55f, 0.68f, 1f);
            fill.intensity = 2.2f;
            fill.range = 9f;
            fillGo.transform.position = new Vector3(0f, 1.3f, -3.2f);

            var rimGo = new GameObject("RimLight");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1f, 0.78f, 0.5f);
            rim.intensity = 0.85f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(18f, 180f, 0f);

            // ---- Post-processing volume: ACES + mild bloom (a lab test range, not a show). ----
            var volGo = new GameObject("GlobalVolume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreatePostProfile();
            WarnIfNoPostProcessData();

            // ---- Minimal runtime-built lab stage: dark disc floor + calibration rings + sky. ----
            var stageGo = new GameObject("LabStage");
            BuildLabStage(stageGo.transform);

            // ---- Player avatar (origin, facing camera) + MediaPipe detector. ----
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

                // Give every default-grey body part a deliberate warm tone (else the body renders grey).
                var skinTone = new Color(0.86f, 0.67f, 0.53f);
                var hairTone = new Color(0.22f, 0.14f, 0.10f);
                var shirtTone = new Color(0.62f, 0.42f, 0.68f);
                var pantsTone = new Color(0.24f, 0.22f, 0.32f);
                var shoeTone = new Color(0.80f, 0.78f, 0.82f);
                ApplyBodyMaterial(model, "mat", skinTone, 0.28f, "SkinFace");
                ApplyBodyMaterial(model, "tay1", skinTone, 0.28f, "SkinHands");
                ApplyBodyMaterial(model, "ao", shirtTone, 0.12f, "ShirtFabric");
                ApplyBodyMaterial(model, "quan", pantsTone, 0.12f, "PantsFabric");
                ApplyBodyMaterial(model, "toc1", hairTone, 0.3f, "HairWarm");
                ApplyBodyMaterial(model, "giay_UV", shoeTone, 0.25f, "ShoeLight");
                var browTone = new Color(0.16f, 0.10f, 0.07f);
                foreach (var browName in new[] { "may_l", "may_r", "mi_l", "mi_r" })
                    ApplyBodyMaterial(model, browName, browTone, 0.2f, "BrowLashDark");

                FrameCameraOnAvatar(camGo.transform, model);
            }
            else
            {
                Debug.LogWarning($"[MotionLabSceneBuilder] Character prefab missing for id '{CharacterId}'.");
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

            // ---- Director. useKeyboardStub is left at its script default (true) — device Awake
            // forces it off; the editor scene keeps stub mode available for quick testing. ----
            var directorGo = new GameObject("MotionLabDirector");
            var director = directorGo.AddComponent<MotionLabDirector>();
            director.poseDetector = detector;
            director.avatarRoot = player.transform;
            EditorUtility.SetDirty(director);

            // ---- UI (chained, like MirrorGameSceneBuilder → MirrorGameUIBuilder). ----
            Debug.Log("[MotionLabSceneBuilder] " + MotionLabUIBuilder.BuildUI());

            // ---- Build Settings: append only. ----
            AppendToBuildSettings(ScenePath);

            // ScreenSpaceOverlay canvases don't render through cam.Render() into a RenderTexture —
            // temporarily switch to ScreenSpaceCamera so the framing/HUD panels appear in the shot,
            // then restore (the runtime scene keeps the plain Overlay mode the other games use).
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;
            RenderScreenshot(cam, ScreenshotPath);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;

            // ---- Save. ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MotionLabSceneBuilder] Scene built + saved: {ScenePath}. Screenshot: {ScreenshotPath}");
        }

        // ---- Lab stage: dark unlit floor disc + 3 concentric cyan calibration rings (centered on
        // where the player stands) under a plain indigo->teal gradient sky dome. No props, no
        // fog-heavy mood lighting — this is a test range, kept deliberately bare. ----
        static void BuildLabStage(Transform parent)
        {
            var sky = ProceduralSkybox.CreateSkyDome(
                top: new Color(0.07f, 0.05f, 0.18f),      // deep indigo
                horizon: new Color(0.04f, 0.16f, 0.17f),  // dark teal
                radius: 30f);
            sky.transform.SetParent(parent, false);

            ProceduralSkybox.SetAmbient(
                sky: new Color(0.24f, 0.26f, 0.38f),
                equator: new Color(0.20f, 0.24f, 0.30f),
                ground: new Color(0.08f, 0.09f, 0.14f));

            // No fog: the sky dome sits ~30 m out, and linear fog ending before that flattened the
            // whole indigo→teal gradient to one dark tone in the first render. A bare lab range
            // has nothing distant to hide anyway.
            RenderSettings.fog = false;

            // Floor: large dark blue-grey disc, UNLIT (URP/Lit blows out large flat surfaces white).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "LabFloor";
            DestroyColliderSafe(floor);
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(12f, 0.05f, 12f); // radius 6m, top surface at y=0
            floor.GetComponent<Renderer>().sharedMaterial = PropMeshes.MatUnlit(new Color(0.102f, 0.118f, 0.180f));
            NoShadow(floor);

            // Concentric grid rings — a calibration-target look, centered on the player.
            var ringColor = new Color(0.4f, 1.35f, 1.55f); // HDR-ish soft cyan, clears the bloom threshold
            var ringMat = PropMeshes.MatUnlit(ringColor);
            // Double-sided: the annulus mesh's winding is only checked against a top-down camera
            // view, not verified against Unity's front-face convention — Cull Off guarantees it
            // renders regardless of winding direction, cheap for 3 small flat rings.
            if (ringMat.HasProperty("_Cull")) ringMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            (float inner, float outer)[] rings = { (1.00f, 1.06f), (1.80f, 1.86f), (2.60f, 2.68f) };
            for (int i = 0; i < rings.Length; i++)
            {
                var ringGo = new GameObject($"GridRing{i}", typeof(MeshFilter), typeof(MeshRenderer));
                ringGo.transform.SetParent(parent, false);
                ringGo.transform.localPosition = new Vector3(0f, 0.006f, 0f);
                ringGo.GetComponent<MeshFilter>().sharedMesh = BuildRingMesh(rings[i].inner, rings[i].outer, 64);
                var ringRenderer = ringGo.GetComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = ringMat;
                ringRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                NoShadow(ringGo);
            }

            var cam = Camera.main;
            if (cam != null && cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
        }

        // Flat annulus (ring) mesh in the XZ plane, normal +Y — no torus primitive exists in Unity,
        // and a stack of solid discs would read as circles, not rings.
        static Mesh BuildRingMesh(float innerRadius, float outerRadius, int segments)
        {
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float theta = t * Mathf.PI * 2f;
                float cx = Mathf.Cos(theta), cz = Mathf.Sin(theta);
                verts[i * 2] = new Vector3(cx * innerRadius, 0f, cz * innerRadius);
                verts[i * 2 + 1] = new Vector3(cx * outerRadius, 0f, cz * outerRadius);
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }
            var tris = new int[segments * 6];
            int ti = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
            }
            var mesh = new Mesh { name = "RingMesh" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void NoShadow(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void DestroyColliderSafe(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            Object.DestroyImmediate(col);
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
            so.FindProperty("autoCalibrate").boolValue = false;
            // Motion Lab is where the speed-adaptive 1€ filter gets turned on (see MediaPipePoseDetector's tooltip).
            so.FindProperty("useOneEuroSmoothing").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static VolumeProfile CreatePostProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MotionLab"))
                AssetDatabase.CreateFolder("Assets", "MotionLab");
            AssetDatabase.DeleteAsset(PostProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            var bloom = AddOverride<Bloom>(profile);
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.5f);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.25f);
            vignette.smoothness.Override(0.6f);

            var colors = AddOverride<ColorAdjustments>(profile);
            colors.saturation.Override(4f);
            colors.contrast.Override(6f);
            colors.postExposure.Override(0.05f);

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
                Debug.LogWarning("[MotionLabSceneBuilder] WARNING: no UniversalRendererData has Post Process Data assigned.");
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
