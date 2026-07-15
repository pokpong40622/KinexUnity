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
using TMPro;
using Kinex;
using Kinex.FX;
using Kinex.AstroStance;

namespace Kinex.AstroStance.EditorTools
{
    /// <summary>
    /// One-click / batchmode builder for Assets/Scenes/AstroStanceScene.unity: a BEHIND-the-
    /// character camera (this project's first back view) + a cool sci-fi light rig + an ACES/
    /// bloom post volume, a runtime-built 3-lane space stage (dark floor, glowing lane strips,
    /// hex landing pads, starfield, a stylized ringed planet on the skyline), the MediaPipe-
    /// driven player avatar seen from behind (same gotcha fixes as MotionLabSceneBuilder — stray
    /// FBX light/camera, light probes, grey-material recolors — but NOT rotated 180, since this
    /// view faces the avatar away from the camera), the AstroSpawner, and the fully wired
    /// AstroStanceDirector (+ result bridge). Idempotent: reopens the scene and rebuilds its own
    /// roots by name. Ends by appending the scene to Build Settings and rendering a 927x1427
    /// screenshot to the job scratchpad.
    /// </summary>
    public static class AstroStanceSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/AstroStanceScene.unity";
        const string PostProfilePath = "Assets/AstroStance/AstroStancePost.asset";
        const string CharacterId = CharacterLibrary.DefaultId;
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";
        const string ScreenshotPath =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/552c1cb4-f9f4-4e3c-b616-07a08892f6bc/scratchpad/astrostance_scene.png";
        const int ShotW = 927, ShotH = 1427;
        const float AvatarHeightMeters = 1.7f;

        // Palette — sunny-park daylight (re-theme from the original dark space look).
        static readonly Color SkyPale = new Color(0.74f, 0.86f, 0.96f);   // bright hazy-blue horizon / camera bg
        static readonly Color GrassBase = new Color(0.33f, 0.53f, 0.24f); // ground plane
        static readonly Color LaneGrass = new Color(0.46f, 0.66f, 0.33f); // mown lane strips (lighter)
        static readonly Color SkyTop = new Color(0.35f, 0.60f, 0.90f);    // dome zenith blue

        static readonly string[] OwnedRoots =
        {
            "Main Camera", "KeyLight", "FillLight", "RimLight", "GlobalVolume",
            "AstroStage", "PlayerCharacter", "Spawner", "Canvas", "EventSystem", "Director",
        };

        [MenuItem("Kinex/Build AstroStance Scene")]
        public static void BuildScene()
        {
            Scene scene;
            bool existed = File.Exists(ScenePath);
            if (existed) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(OwnedRoots, root.name) >= 0)
                    Object.DestroyImmediate(root);

            // ---- Main Camera (BACK VIEW): behind and above the avatar, looking forward/down
            // the lanes. No yaw — the avatar model itself is left unrotated (faces +Z), so a
            // plain pitch-only camera behind it at -Z already looks over its shoulder. ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 2.05f, -3.4f);
            camGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyPale;
            cam.allowHDR = true; // sun disc / emissive lane lines rely on HDR values clearing the bloom threshold
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // ---- Lights (back view!): key from behind-left-above the camera lights the
            // avatar's BACK, fill sits near the camera, rim points back toward the camera from
            // +Z to catch a silhouette edge on the avatar. ----
            var keyGo = new GameObject("KeyLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.45f;
            key.color = new Color(1.0f, 0.97f, 0.88f); // warm sunlight
            keyGo.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.45f;

            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.78f, 0.86f, 1f); // soft sky fill
            fill.intensity = 0.9f;
            fill.range = 12f;
            fillGo.transform.position = new Vector3(0f, 2.0f, -3.2f);

            var rimGo = new GameObject("RimLight");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1.0f, 0.96f, 0.86f);
            rim.intensity = 0.5f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(20f, 180f, 0f);

            // ---- Post-processing volume: bloom-forward for the neon lanes/rings. ----
            var volGo = new GameObject("GlobalVolume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreatePostProfile();

            // ---- Runtime-built space stage: floor, lanes, pads, sky, starfield, planet. ----
            var stageGo = new GameObject("AstroStage");
            BuildAstroStage(stageGo.transform);

            // ---- Player avatar (origin, facing AWAY from camera) + MediaPipe detector. ----
            var player = new GameObject("PlayerCharacter");
            player.transform.position = Vector3.zero;

            MediaPipePoseDetector detector = null;
            GameObject model = null;
            Animator avatarAnimator = null;
            var prefab = CharacterLibrary.LoadPrefab(CharacterId);
            if (prefab != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                model.transform.SetParent(player.transform, false);
                // DO NOT rotate 180 here — every other scene faces the camera, but this is a
                // behind-the-character view: the avatar must face +Z, away from the camera.
                model.transform.localRotation = Quaternion.identity;
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

                // Give every default-grey body part a deliberate tone (else the body renders grey).
                var skinTone = new Color(0.86f, 0.67f, 0.53f);
                var hairTone = new Color(0.22f, 0.14f, 0.10f);
                var shirtTone = new Color(0.26f, 0.58f, 0.82f);  // bright friendly sky-blue
                var pantsTone = new Color(0.18f, 0.19f, 0.26f);
                var shoeTone = new Color(0.70f, 0.74f, 0.82f);
                ApplyBodyMaterial(model, "mat", skinTone, 0.28f, "SkinFace");
                ApplyBodyMaterial(model, "tay1", skinTone, 0.28f, "SkinHands");
                ApplyBodyMaterial(model, "ao", shirtTone, 0.12f, "ShirtFabric");
                ApplyBodyMaterial(model, "quan", pantsTone, 0.12f, "PantsFabric");
                ApplyBodyMaterial(model, "toc1", hairTone, 0.3f, "HairWarm");
                ApplyBodyMaterial(model, "giay_UV", shoeTone, 0.25f, "ShoeLight");
                var browTone = new Color(0.16f, 0.10f, 0.07f);
                foreach (var browName in new[] { "may_l", "may_r", "mi_l", "mi_r" })
                    ApplyBodyMaterial(model, browName, browTone, 0.2f, "BrowLashDark");

                RescaleAndGroundAvatar(model, player.transform);
                avatarAnimator = model.GetComponent<Animator>();
            }
            else
            {
                Debug.LogWarning($"[AstroStanceSceneBuilder] Character prefab missing for id '{CharacterId}'.");
            }

            // ---- Spawner. ----
            var spawnerGo = new GameObject("Spawner");
            var spawner = spawnerGo.AddComponent<AstroSpawner>();
            spawner.thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiSemiPath);
            if (spawner.thaiFont == null)
                Debug.LogWarning($"[AstroStanceSceneBuilder] Font not found: {ThaiSemiPath}");

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

            // ---- Director + result bridge. useKeyboardStub is left at its script default
            // (true) — device Awake forces it off; the editor scene keeps stub mode available
            // for quick testing (same convention as MotionLabSceneBuilder). ----
            var directorGo = new GameObject("Director");
            var director = directorGo.AddComponent<AstroStanceDirector>();
            directorGo.AddComponent<AstroResultBridge>();
            director.poseDetector = detector;
            director.avatarRoot = player.transform;
            director.avatarAnimator = avatarAnimator;
            director.spawner = spawner;
            director.shakeTarget = camGo.transform;
            EditorUtility.SetDirty(director);

            // ---- Editor-only numpad clip switcher for Play-mode pose testing (see
            // AstroTestClipSwitcher). Child of Director so it isn't a new scene root. ----
            var switchGo = new GameObject("TestClipSwitcher");
            switchGo.transform.SetParent(directorGo.transform, false);
            switchGo.AddComponent<AstroTestClipSwitcher>().detector = detector;

            // ---- UI (chained, like the other game builders). ----
            Debug.Log("[Builder] " + AstroStanceUIBuilder.BuildUI());

            // ---- Build Settings: append only (Boot stays index 0). ----
            AppendToBuildSettings(ScenePath);

            // ScreenSpaceOverlay canvases don't render through cam.Render() into a RenderTexture —
            // temporarily switch to ScreenSpaceCamera so the intro/HUD panels appear in the shot,
            // then restore. Try/finally: a screenshot failure must never abort the save. ----
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 0.5f;
                RenderScreenshot(cam, ScreenshotPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AstroStanceSceneBuilder] screenshot failed — scene still saved: {e.Message}");
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }

            // ---- Save. ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AstroStanceSceneBuilder] Scene built + saved: {ScenePath}. Screenshot: {ScreenshotPath}");
        }

        // ---- ParkStage: grass floor, 3 mown lane strips w/ cream edges, sandy landing pads,
        // bright sky dome, a warm sun, drifting clouds, roadside trees, pollen + butterflies. ----
        static void BuildAstroStage(Transform parent)
        {
            var sky = ProceduralSkybox.CreateSkyDome(top: SkyTop, horizon: SkyPale, radius: 120f);
            sky.transform.SetParent(parent, false);
            ProceduralSkybox.SetAmbient(
                sky: new Color(0.60f, 0.70f, 0.82f),
                equator: new Color(0.58f, 0.60f, 0.54f),
                ground: new Color(0.32f, 0.38f, 0.24f));
            // Light outdoor haze so far trees / sky decor melt into the horizon (depth + hides the
            // dome seam). Kept faint + far so the sun and clouds still read crisply.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyPale;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 260f;

            // Floor: Plane primitive is already 10x10 facing +Y — no rotation needed. Unlit:
            // URP/Lit blows out large flat surfaces white (memory: PropMeshes.MatUnlit doc).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            DestroyColliderSafe(floor);
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(2.4f, 1f, 2.4f); // 10 * 2.4 = 24m
            floor.GetComponent<Renderer>().sharedMaterial = PropMeshes.MatUnlit(GrassBase);
            NoShadow(floor);

            // 3 mown lanes, cream-lined, with a sandy landing patch each.
            var laneMat = PropMeshes.MatUnlit(LaneGrass);
            // Soft cream lane lines (was neon cyan) — mild emission so they still read in daylight.
            var edgeMat = PropMeshes.Mat(new Color(0.96f, 0.97f, 0.88f), 0f, 0.2f, new Color(0.30f, 0.31f, 0.26f));
            var padMat = PropMeshes.MatUnlit(new Color(0.66f, 0.58f, 0.42f)); // sandy landing patch
            const float laneWidth = 0.92f, laneZ0 = -0.8f, laneZ1 = 7.2f;
            float laneLen = laneZ1 - laneZ0, laneCenterZ = (laneZ0 + laneZ1) * 0.5f;
            float[] laneXs = { -1.0f, 0f, 1.0f }; // must match AstroSpawner.laneSpacing
            foreach (var x in laneXs)
            {
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "LaneStrip";
                DestroyColliderSafe(strip);
                strip.transform.SetParent(parent, false);
                strip.transform.localPosition = new Vector3(x, 0.011f, laneCenterZ);
                strip.transform.localScale = new Vector3(laneWidth, 0.02f, laneLen);
                strip.GetComponent<Renderer>().sharedMaterial = laneMat;
                NoShadow(strip);

                foreach (var side in new[] { -1f, 1f })
                {
                    var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    edge.name = "LaneEdge";
                    DestroyColliderSafe(edge);
                    edge.transform.SetParent(parent, false);
                    edge.transform.localPosition = new Vector3(x + side * laneWidth * 0.5f, 0.022f, laneCenterZ);
                    edge.transform.localScale = new Vector3(0.04f, 0.01f, laneLen);
                    edge.GetComponent<Renderer>().sharedMaterial = edgeMat;
                    NoShadow(edge);
                }

                // Hex landing pad: flattened cylinder + a thin emissive rim ring.
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = "LandingPad";
                DestroyColliderSafe(pad);
                pad.transform.SetParent(parent, false);
                pad.transform.localPosition = new Vector3(x, 0.015f, 1.8f);
                pad.transform.localScale = new Vector3(1.1f, 0.01f, 1.1f); // r=0.55, h=0.02
                pad.GetComponent<Renderer>().sharedMaterial = padMat;
                NoShadow(pad);
                BuildRing(parent, "PadRim", new Vector3(x, 0.017f, 1.8f), Quaternion.identity, 0.6f, 0.045f, 16, edgeMat);
            }

            BuildSun(parent);
            BuildClouds(parent);
            BuildSideScenery(parent);
            BuildPollen(parent);

            // A couple of butterflies drifting over the lanes for a touch of life (runtime motion).
            var flutter1 = Butterfly.Spawn(new Vector3(-1.2f, 1.1f, 3.5f), new Vector3(1.2f, 0.5f, 1.5f), new Color(1f, 0.62f, 0.2f));
            flutter1.transform.SetParent(parent, false);
            var flutter2 = Butterfly.Spawn(new Vector3(1.4f, 1.3f, 5.5f), new Vector3(1.0f, 0.6f, 1.4f), new Color(0.92f, 0.32f, 0.5f));
            flutter2.transform.SetParent(parent, false);
        }

        // ---- Sunny-park sky decor: a warm sun disc, drifting cloud clusters, roadside trees,
        // and a gentle pollen mote field. Replaces the original starfield/planet/moon. ----

        static void BuildSun(Transform parent)
        {
            var sunColor = new Color(1.0f, 0.95f, 0.76f);
            var sunMat = PropMeshes.Mat(sunColor, 0f, 0.2f, sunColor * 2.2f); // emissive → blooms
            var sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "Sun";
            DestroyColliderSafe(sun);
            sun.transform.SetParent(parent, false);
            sun.transform.localPosition = new Vector3(-19f, 26f, 108f); // upper-left, balances the HUD
            sun.transform.localScale = Vector3.one * 7f;
            sun.GetComponent<Renderer>().sharedMaterial = sunMat;
            NoShadow(sun);
        }

        static void BuildClouds(Transform parent)
        {
            var cloudMat = PropMeshes.Mat(new Color(0.99f, 0.99f, 1.0f), 0f, 0.1f, new Color(0.22f, 0.24f, 0.28f));
            // x, y, z, size
            var pts = new[]
            {
                new Vector4(-24f, 19f, 92f, 7f),
                new Vector4(16f, 23f, 104f, 8f),
                new Vector4(-6f, 26f, 120f, 9f),
                new Vector4(28f, 17f, 88f, 6f),
                new Vector4(3f, 21f, 132f, 10f),
            };
            foreach (var p in pts)
            {
                var cloud = new GameObject("Cloud");
                cloud.transform.SetParent(parent, false);
                cloud.transform.localPosition = new Vector3(p.x, p.y, p.z);
                float s = p.w;
                AddCloudPuff(cloud.transform, Vector3.zero, s, cloudMat);
                AddCloudPuff(cloud.transform, new Vector3(s * 0.5f, -s * 0.08f, 0f), s * 0.7f, cloudMat);
                AddCloudPuff(cloud.transform, new Vector3(-s * 0.55f, -s * 0.05f, s * 0.1f), s * 0.65f, cloudMat);
                var drift = cloud.AddComponent<CloudDrift>();
                drift.speed = 0.25f;
                drift.range = 12f;
            }
        }

        static void AddCloudPuff(Transform parent, Vector3 pos, float size, Material mat)
        {
            var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "Puff";
            DestroyColliderSafe(puff);
            puff.transform.SetParent(parent, false);
            puff.transform.localPosition = pos;
            puff.transform.localScale = new Vector3(size, size * 0.5f, size * 0.7f);
            puff.GetComponent<Renderer>().sharedMaterial = mat;
            NoShadow(puff);
        }

        // Roadside trees framing both sides of the lanes, receding into the distance. Index-based
        // (not Random) jitter keeps the build deterministic; each gets a GentleSway breeze.
        static void BuildSideScenery(Transform parent)
        {
            float[] zs = { 1.5f, 5f, 9f, 14f, 20f };
            float[] xs = { -3.7f, 3.7f };
            int i = 0;
            foreach (var z in zs)
                foreach (var x in xs)
                {
                    var tree = PropMeshes.Tree();
                    tree.name = "Tree";
                    tree.transform.SetParent(parent, false);
                    float scale = 1.0f + (i % 3) * 0.2f + z * 0.03f;
                    tree.transform.localScale = Vector3.one * scale;
                    tree.transform.localPosition = new Vector3(x + ((i % 2 == 0) ? -0.5f : 0.6f), 0f, z);
                    tree.transform.localRotation = Quaternion.Euler(0f, i * 47f, 0f);
                    foreach (var r in tree.GetComponentsInChildren<Renderer>()) NoShadowR(r);
                    tree.AddComponent<GentleSway>().amplitudeDeg = 1.6f;
                    i++;
                }
        }

        // Gentle warm pollen motes drifting low over the grass — the sunlit-park counterpart to the
        // old starfield. Reuses the soft additive dot + pre-Simulate so it shows in the edit shot.
        static void BuildPollen(Transform parent)
        {
            var motes = KinexFx.AmbientMotes(
                center: new Vector3(0f, 2.2f, 5f),
                boxSize: new Vector3(13f, 5f, 12f),
                color: new Color(1f, 0.97f, 0.72f, 0.5f),
                rate: 10);
            motes.name = "Pollen";
            motes.transform.SetParent(parent, false);
            motes.GetComponent<ParticleSystemRenderer>().sharedMaterial = SoftDotMaterial();

            var main = motes.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.98f, 0.78f), new Color(1f, 0.9f, 0.55f));
            main.maxParticles = 120;
            var drift = motes.velocityOverLifetime;
            drift.enabled = true;
            // All three axes must share the same MinMaxCurve mode (two-constants) or Unity warns.
            drift.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            drift.y = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            drift.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            if (!Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                motes.Simulate(6f, true, true);
        }

        static Texture2D s_SoftDotTex;
        static Material s_SoftDotMat;

        // Soft round dot (DanceStage.SoftDotTexture) — a raw particle quad shows a hard square.
        static Texture2D SoftDotTexture()
        {
            if (s_SoftDotTex != null) return s_SoftDotTex;
            const int size = 48;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - r * r));
                }
            s_SoftDotTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "AstroStarDot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_SoftDotTex.SetPixels32(px);
            s_SoftDotTex.Apply();
            return s_SoftDotTex;
        }

        static Material SoftDotMaterial()
        {
            if (s_SoftDotMat != null) return s_SoftDotMat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            s_SoftDotMat = new Material(shader) { name = "AstroStarMat" };
            if (s_SoftDotMat.HasProperty("_BaseMap")) s_SoftDotMat.SetTexture("_BaseMap", SoftDotTexture());
            else s_SoftDotMat.mainTexture = SoftDotTexture();
            if (s_SoftDotMat.HasProperty("_Surface")) s_SoftDotMat.SetFloat("_Surface", 1f);
            s_SoftDotMat.SetOverrideTag("RenderType", "Transparent");
            s_SoftDotMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_SoftDotMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive glow
            s_SoftDotMat.SetInt("_ZWrite", 0);
            s_SoftDotMat.EnableKeyword("_ALPHABLEND_ON");
            s_SoftDotMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_SoftDotMat;
        }

        // A ring made of small emissive cube segments (same technique as AstroProps.TelegraphRing's
        // Ring child) — no torus primitive exists in Unity. Reused for hex-pad rims and the planet.
        static GameObject BuildRing(Transform parent, string name, Vector3 localPos, Quaternion localRot,
            float radius, float thickness, int segments, Material mat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = $"Seg{i}";
                DestroyColliderSafe(seg);
                seg.transform.SetParent(root.transform, false);
                seg.transform.localPosition = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                seg.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                seg.transform.localScale = new Vector3(thickness * 2.2f, thickness * 0.5f, thickness);
                seg.GetComponent<Renderer>().sharedMaterial = mat;
                NoShadow(seg);
            }
            return root;
        }

        static void NoShadow(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            NoShadowR(r);
        }

        static void NoShadowR(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        // Edit-time load of a Meshy GLB from Resources for scene decor (planet), scaled so its
        // largest axis == targetSize, centered on a fresh wrapper root. Null if not imported.
        static GameObject LoadStageModel(string name, float targetSize)
        {
            var prefab = Resources.Load<GameObject>("AstroModels/" + name);
            if (prefab == null) return null;
            var root = new GameObject(name);
            var inner = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inner.transform.SetParent(root.transform, false);
            foreach (var l in inner.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in inner.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            var rends = inner.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (maxDim > 1e-4f) inner.transform.localScale *= targetSize / maxDim;
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                inner.transform.localPosition = -b.center;
            }
            return root;
        }

        static void DestroyColliderSafe(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            Object.DestroyImmediate(col);
        }

        // Rescales the model so its bounds height = 1.7m, then grounds its feet at y=0 relative
        // to the (world-origin) PlayerCharacter wrapper.
        static void RescaleAndGroundAvatar(GameObject model, Transform playerRoot)
        {
            var b = ComputeBounds(model);
            if (b.size.y > 0.01f && Mathf.Abs(b.size.y - AvatarHeightMeters) > 0.05f)
            {
                model.transform.localScale *= AvatarHeightMeters / b.size.y;
                b = ComputeBounds(model);
            }
            float footOffset = b.min.y - playerRoot.position.y;
            if (Mathf.Abs(footOffset) > 0.001f)
                model.transform.localPosition -= new Vector3(0f, footOffset, 0f);
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

        // Same as MotionLabSceneBuilder's ConfigureDetector, plus the back-view same-side
        // retarget flag and speed-adaptive smoothing turned on.
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
            so.FindProperty("autoCalibrate").boolValue = false;
            so.FindProperty("useOneEuroSmoothing").boolValue = true;
            // Back-view scene: the avatar "follows" the player's own side instead of mirroring.
            so.FindProperty("sameSideRetarget").boolValue = true;
            // Back view: keep the upper spine steady (head/ear drive folds the torso, looks wrong).
            so.FindProperty("steadyUpperSpine").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static VolumeProfile CreatePostProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/AstroStance"))
                AssetDatabase.CreateFolder("Assets", "AstroStance");
            AssetDatabase.DeleteAsset(PostProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            // Daylight grade: gentle bloom only on the sun/emissive lines, no darkening vignette,
            // brighter exposure, and Neutral tonemapping (ACES crushes highlights toward a moody
            // dark look — wrong for a bright park).
            var bloom = AddOverride<Bloom>(profile);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.5f);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.8f);

            var colors = AddOverride<ColorAdjustments>(profile);
            colors.saturation.Override(6f);
            colors.contrast.Override(0f);
            colors.postExposure.Override(0.15f);

            var tonemap = AddOverride<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.Neutral);

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
