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
using TMPro;
using Kinex;
using Kinex.FX;
using Kinex.TheDasher;

namespace Kinex.TheDasher.EditorTools
{
    /// <summary>
    /// FORK of TheDasherSceneBuilder for an independent art pass — builds
    /// Assets/Scenes/TheDasherArtScene.unity instead of the original TheDasherScene.unity, and
    /// writes its own post-processing profile + environment assets under Assets/TheDasherArt/
    /// (NOT Assets/TheDasher/) so it can never delete or overwrite the original scene's assets.
    /// The camera rig, avatar setup, spawner, and director wiring are an unmodified copy of the
    /// original builder — only the ENVIRONMENT is rebuilt: a real HDRI skybox, textured/normal-
    /// mapped URP/Lit ground + lanes, and Quaternius tree/bush/flower/grass/rock meshes in place
    /// of the original's flat-color primitives. Idempotent: reopens the scene and rebuilds its
    /// own roots by name. Ends by appending the scene to Build Settings and rendering a
    /// 927x1427 screenshot to the job scratchpad.
    /// </summary>
    public static class TheDasherArtSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/TheDasherArtScene.unity";
        const string PostProfilePath = "Assets/TheDasherArt/TheDasherArtPost.asset";
        const string SkyboxMatPath = "Assets/TheDasherArt/TheDasherArtSkybox.mat";
        const string CharacterId = CharacterLibrary.DefaultId;
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";
        const string ScreenshotPath =
            "C:/Users/Admin/AppData/Local/Temp/claude/D--Unity-project-Kinex/24f57890-2cd4-458a-8ddd-d00cf886db77/scratchpad/thedasher_art_scene.png";
        const int ShotW = 927, ShotH = 1427;
        const float AvatarHeightMeters = 1.7f;

        // ---- Art-pass source assets (Assets/TheDasherArt/, staged CC0 — see manifest.md). ----
        const string TreesGlb = "Assets/TheDasherArt/Models/Trees_Quaternius.glb";
        const string MapleGlb = "Assets/TheDasherArt/Models/MapleTrees_Quaternius.glb";
        const string BushesGlb = "Assets/TheDasherArt/Models/Bushes_Quaternius.glb";
        const string FlowerBushesGlb = "Assets/TheDasherArt/Models/FlowerBushes_Quaternius.glb";
        const string FlowersGlb = "Assets/TheDasherArt/Models/Flowers_Quaternius.glb";
        const string GrassGlb = "Assets/TheDasherArt/Models/Grass_Quaternius.glb";
        const string RocksGlb = "Assets/TheDasherArt/Models/Rocks_Quaternius.glb";
        const string GrassDiffPath = "Assets/TheDasherArt/Textures/Ground/leafy_grass_diff_1k.jpg";
        const string GrassNormPath = "Assets/TheDasherArt/Textures/Ground/leafy_grass_nor_1k.jpg";
        const string DirtDiffPath = "Assets/TheDasherArt/Textures/Ground/park_dirt_diff_1k.jpg";
        const string DirtNormPath = "Assets/TheDasherArt/Textures/Ground/park_dirt_nor_1k.jpg";
        // Stayed on the blue midday sky ON PURPOSE. A golden-hour HDRI was downloaded and tried
        // (kloppenheim_06 and belfast_sunset, both CC0): both turned the shot drearier, not
        // prettier — the canopy hides most of the sky anyway, so all you get is a grey-white
        // overcast band and duller greens. This is a sunny-park rehab game for older users; the
        // blue reads inviting. The low warm SUN is kept, which is where the beauty actually came
        // from.
        const string SkyHdrPath = "Assets/TheDasherArt/Sky/kloofendal_48d_partly_cloudy_puresky_1k.hdr";
        // Procedural skyline meshes must be saved as assets — a Mesh built in the builder and only
        // referenced by a MeshFilter is not persisted and comes back null after a scene reload.
        const string RidgeMeshFolder = "Assets/TheDasherArt/Meshes";

        // Camera sits at z=-4.1 (see below) — "within 15m of camera" (the Stage-3 shadow-casting
        // budget line) works out to world z < ~11. Anything placed past this stays shadow-off.
        const float NearShadowZ = 11f;

        static readonly Color SkyPale = new Color(0.84f, 0.86f, 0.90f); // fog colour: pale, faintly warm, still sky-blue

        // 10 tree variants (5 broadleaf + 5 maple) — picked deterministically by index so roadside
        // rows and the background forest both read as mixed woodland, not a single repeated mesh.
        // Mostly the 5 green broadleaf meshes, with maple appearing only twice in ten. The maple
        // pack is authored vivid red/orange, so an even 5/5 split made the whole park read as
        // autumn — and clumped in the mid-distance the orange canopies looked like fire, not trees.
        // Two accents per ten keeps a little colour variety without losing the sunny-summer theme.
        static readonly (string glb, string mesh)[] TreeVariants =
        {
            (TreesGlb, "NormalTree_1"), (TreesGlb, "NormalTree_2"), (TreesGlb, "NormalTree_3"),
            (TreesGlb, "NormalTree_4"), (TreesGlb, "NormalTree_5"),
            (MapleGlb, "MapleTree_2"),  (TreesGlb, "NormalTree_1"), (TreesGlb, "NormalTree_3"),
            (MapleGlb, "MapleTree_4"),  (TreesGlb, "NormalTree_5"),
        };

        // Background band: GREEN ONLY. This band is the widest thing on screen, so any maple here
        // dominates the whole frame with orange. Distance + fog flatten the detail anyway.
        // CHEAPEST MESH ONLY. Measured per-mesh cost varies 4x: NormalTree_1 is 8,520 tris,
        // NormalTree_5 is 2,182. This band is 56 trees sitting 30-65m out behind fog, where the
        // extra geometry is invisible — mixing in the expensive variants cost ~208k triangles for
        // no visible gain. Variety here comes from per-instance yaw and scale instead.
        static readonly (string glb, string mesh)[] BgTreeVariants =
        {
            (TreesGlb, "NormalTree_5"),
        };

        // Green Bushes pack ONLY. The FlowerBushes meshes (Plant_2 / Plant_Flowers / Petals_1) share
        // the vivid teal-blue Flowers atlas; at bush scale (~1.8m) they rendered as metre-wide blue
        // succulents sprawled on the lawn — alien-looking, and the single most out-of-place thing in
        // the frame. They stay available for the small flower scatter, where their colour is an
        // asset rather than a distraction.
        static readonly (string glb, string mesh)[] BushVariants =
        {
            (BushesGlb, "Bush"), (BushesGlb, "Plant_1"), (BushesGlb, "Bush_Flowers"),
        };

        static readonly (string glb, string mesh)[] FlowerVariants =
        {
            (FlowersGlb, "Flower_1_Clump"), (FlowersGlb, "Flower_2_Clump"), (FlowersGlb, "Flower_3_Clump"),
            (FlowersGlb, "Flower_4_Clump"), (FlowersGlb, "Flower_5_Clump"), (FlowersGlb, "Flower_1"), (FlowersGlb, "Flower_2"),
        };

        static readonly (string glb, string mesh)[] GrassTuftVariants =
        {
            (GrassGlb, "Grass_Large_Extruded"), (GrassGlb, "Grass_Small"),
        };

        static readonly string[] OwnedRoots =
        {
            "Main Camera", "KeyLight", "FillLight", "RimLight", "GlobalVolume",
            "DasherStage", "PlayerCharacter", "Spawner", "Canvas", "EventSystem", "Director",
        };

        [MenuItem("Kinex/Build TheDasher ART Scene")]
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
            camGo.transform.position = new Vector3(0f, 2.2f, -4.1f);
            camGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 54f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox; // real HDRI now provides the background (was SolidColor)
            cam.allowHDR = true; // emissive lane edges rely on HDR values clearing the bloom threshold
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // ---- Lights: late-afternoon sun, SIDE-ON. ----
            // Sun angle is dictated by the FRAME, not by taste. This is a narrow portrait shot
            // (aspect ~0.56-0.65) whose visible width near the camera is barely a metre, so:
            //   - the old key at 48 deg from BEHIND the camera cast every shadow behind the object
            //     making it — invisible by construction;
            //   - a side sun (probed at yaw 118) throws shadows sideways, and they leave the frame
            //     within about half a metre.
            // Only a low sun roughly BEHIND the scene puts shadows where the camera can see them:
            // they stretch back toward the lens, down the long axis of the frame. Verified by
            // probe at each angle, not reasoned about.
            //
            // Backlight normally silhouettes the subject, and an early probe did exactly that —
            // but that probe had the fill light disabled. With FillLight raised to 1.15 the
            // avatar stays fully readable while the park behind it gets its long shadows.
            var keyGo = new GameObject("KeyLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2.1f;
            key.color = new Color(1.0f, 0.90f, 0.76f); // low-sun warmth
            keyGo.transform.rotation = Quaternion.Euler(22f, 200f, 0f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.8f;   // was 0.45 — at that strength the ambient simply ate it
            key.shadowBias = 0.03f;
            key.shadowNormalBias = 0.25f;

            // Load-bearing, not decorative: the key is now a backlight, so this is the ONLY thing
            // keeping the avatar from reading as a silhouette. Cool, to sit opposite the warm key —
            // the colour split is what stops the shading going grey.
            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.70f, 0.80f, 1f);
            fill.intensity = 1.15f;
            fill.range = 12f;
            fillGo.transform.position = new Vector3(-1.2f, 2.1f, -3.4f);

            // FRONT fill (it used to be a second backlight at yaw 180, pitch 20 — pointing the same
            // way as the key). With BOTH directionals coming from behind the scene, every surface
            // FACING THE CAMERA received nothing but ambient: on device and in the headless render
            // alike, every tree trunk and every low bush read as a flat black silhouette while the
            // canopies above them were properly lit. That is the "dark / dry" complaint.
            //
            // The fix is direction, not exposure. This now points AWAY from the camera (yaw 8) so it
            // lands on exactly those camera-facing surfaces, and it is deliberately kept ALMOST
            // HORIZONTAL (pitch 6): the ground normal is straight up, so at this angle the grass
            // receives ~10% of it and the vertical bark receives ~99%. That opens the trunks without
            // filling the long raking shadows back in — the failure mode that cost a whole round
            // when the old rim was tried at 0.5.
            var rimGo = new GameObject("RimLight");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1.0f, 0.96f, 0.86f);
            // 0.6 turned the trunks from black to dark brown but the near bushes stayed murky; 1.0 is
            // what actually reads as a lit park. Safe to push this high ONLY because of the 6° pitch.
            rim.intensity = 1.0f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(6f, 8f, 0f);

            // ---- Post-processing volume: bloom-forward for the neon lanes/rings. ----
            var volGo = new GameObject("GlobalVolume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreatePostProfile();

            // ---- Runtime-built park stage: skybox, textured floor/lanes/pads, trees, verge
            // greenery, rocks. ----
            var stageGo = new GameObject("DasherStage");
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

                // The camera sits BEHIND this character and never moves, so the face interior is
                // pure cost: eyes, eyelashes, brows, teeth and tongue are 9,000 tris that can
                // never be seen, and each one was also a separate shadow-caster submission. They
                // still RENDER (a future result screen or camera orbit would need them) — only
                // their shadow casting is dropped, which is invisible: the head's own silhouette
                // is cast by "mat" and "toc1" regardless.
                foreach (var part in new[] { "eyes_l", "eyes_r", "mi_l", "mi_r", "may_l", "may_r",
                                             "rangtren", "rangduoi", "luoi", "light_l", "light_r" })
                {
                    var t = FindChildRecursive(model.transform, part);
                    if (t == null) continue;
                    var pr = t.GetComponent<Renderer>();
                    if (pr != null) pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                RescaleAndGroundAvatar(model, player.transform);
                avatarAnimator = model.GetComponent<Animator>();
            }
            else
            {
                Debug.LogWarning($"[TheDasherArtSceneBuilder] Character prefab missing for id '{CharacterId}'.");
            }

            // ---- Spawner. ----
            var spawnerGo = new GameObject("Spawner");
            var spawner = spawnerGo.AddComponent<DasherSpawner>();
            spawner.thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiSemiPath);
            if (spawner.thaiFont == null)
                Debug.LogWarning($"[TheDasherArtSceneBuilder] Font not found: {ThaiSemiPath}");

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
            var director = directorGo.AddComponent<TheDasherDirector>();
            directorGo.AddComponent<DasherResultBridge>();
            director.poseDetector = detector;
            director.avatarRoot = player.transform;
            director.avatarAnimator = avatarAnimator;
            director.spawner = spawner;
            director.shakeTarget = camGo.transform;
            EditorUtility.SetDirty(director);

            // ---- Editor-only numpad clip switcher for Play-mode pose testing (see
            // DasherTestClipSwitcher). Child of Director so it isn't a new scene root. ----
            var switchGo = new GameObject("TestClipSwitcher");
            switchGo.transform.SetParent(directorGo.transform, false);
            switchGo.AddComponent<DasherTestClipSwitcher>().detector = detector;

            // ---- UI (chained, like the other game builders). ----
            Debug.Log("[Builder] " + TheDasherUIBuilder.BuildUI());

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
                Debug.LogWarning($"[TheDasherArtSceneBuilder] screenshot failed — scene still saved: {e.Message}");
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
            Debug.Log($"[TheDasherArtSceneBuilder] Scene built + saved: {ScenePath}. Screenshot: {ScreenshotPath}");
        }

        // ---- ParkStage: real HDRI sky, textured/normal-mapped floor + lanes + pads, Quaternius
        // trees/bushes/flowers/grass/rocks, pollen + birds + butterflies for a touch of life. ----
        static void BuildAstroStage(Transform parent)
        {
            BuildSkybox();
            BuildGroundAndLanes(parent);
            BuildSideScenery(parent);       // roadside tree rows, near the play corridor
            BuildBackgroundForest(parent);  // dense, cheap-variant tree band filling the mid-distance
            BuildDistantHills(parent);      // rolling meadow hills behind the woodland
            BuildHillTreeline(parent);      // sparse trees breaking the hills' bald silhouette
            BuildFarRidge(parent);          // hazy blue-grey ridge line on the skyline
            BuildVergeGreenery(parent);     // bushes + flower clumps + grass tufts on the verges
            BuildMidLawnScatter(parent);    // breaks up the open lawn between lanes and woodland
            BuildVergeRocks(parent);        // a handful of rocks for verge variety
            BuildPollen(parent);
            BuildBirds(parent);             // bird silhouettes gliding across the sky

            // A few butterflies drifting over the lanes for a touch of life (runtime motion).
            var flutter1 = Butterfly.Spawn(new Vector3(-1.2f, 1.1f, 3.5f), new Vector3(1.2f, 0.5f, 1.5f), new Color(1f, 0.62f, 0.2f));
            flutter1.transform.SetParent(parent, false);
            var flutter2 = Butterfly.Spawn(new Vector3(1.4f, 1.3f, 5.5f), new Vector3(1.0f, 0.6f, 1.4f), new Color(0.92f, 0.32f, 0.5f));
            flutter2.transform.SetParent(parent, false);
            var flutter3 = Butterfly.Spawn(new Vector3(-2.3f, 0.9f, 7.0f), new Vector3(1.3f, 0.6f, 1.2f), new Color(0.55f, 0.75f, 0.95f));
            flutter3.transform.SetParent(parent, false);
            var flutter4 = Butterfly.Spawn(new Vector3(2.6f, 1.5f, 4.2f), new Vector3(1.1f, 0.7f, 1.3f), new Color(1f, 0.85f, 0.35f));
            flutter4.transform.SetParent(parent, false);

            BuildSala(parent);              // landmark closing the avenue

            CullOffscreen(parent);
            MarkStaticBatchable(parent);
        }

        // The avenue used to lead to nothing: the eye ran down the lanes, past the trees, and
        // landed on empty lawn. A sala (ศาลา) — the open pavilion in every Thai park — sits at the
        // vanishing point and gives it a destination. Built from primitives rather than downloaded
        // because at ~44 m it is barely 13% of frame height: silhouette is all that survives, and a
        // silhouette costs ~250 tris. It also carries the scene's only warm colour accent besides
        // the one red maple.
        static void BuildSala(Transform parent)
        {
            var root = new GameObject("Sala");
            root.transform.SetParent(parent, false);
            const float z = 44f;
            root.transform.localPosition = new Vector3(0f, HillHeightAt(0f, z), z);

            var timber = PropMeshes.Mat(new Color(0.42f, 0.29f, 0.19f), 0f, 0.10f);
            timber.name = "SalaTimber";
            var roofMat = PropMeshes.Mat(new Color(0.60f, 0.17f, 0.14f), 0f, 0.12f); // temple red
            roofMat.name = "SalaRoof";
            var trimMat = PropMeshes.Mat(new Color(0.83f, 0.66f, 0.28f), 0f, 0.25f); // gold trim
            trimMat.name = "SalaTrim";

            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "SalaDeck";
            DestroyColliderSafe(deck);
            deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            deck.transform.localScale = new Vector3(6f, 0.5f, 6f);
            deck.GetComponent<Renderer>().sharedMaterial = timber;

            for (int i = 0; i < 4; i++)
            {
                float px = (i < 2 ? -1f : 1f) * 2.4f;
                float pz = (i % 2 == 0 ? -1f : 1f) * 2.4f;
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "SalaPost";
                DestroyColliderSafe(post);
                post.transform.SetParent(root.transform, false);
                post.transform.localPosition = new Vector3(px, 1.9f, pz);
                post.transform.localScale = new Vector3(0.3f, 1.4f, 0.3f); // cylinder is 2 units tall
                post.GetComponent<Renderer>().sharedMaterial = timber;
            }

            // Two-tier hip roof + finial. Tiering is what reads as "Thai" at this distance —
            // a single pyramid reads as a tent.
            AddPyramid(root.transform, "SalaRoofLower", new Vector3(0f, 3.3f, 0f), 7.2f, 1.5f, roofMat);
            AddPyramid(root.transform, "SalaRoofUpper", new Vector3(0f, 4.5f, 0f), 4.6f, 1.7f, roofMat);
            var finial = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            finial.name = "SalaFinial";
            DestroyColliderSafe(finial);
            finial.transform.SetParent(root.transform, false);
            finial.transform.localPosition = new Vector3(0f, 6.5f, 0f);
            finial.transform.localScale = new Vector3(0.16f, 0.7f, 0.16f);
            finial.GetComponent<Renderer>().sharedMaterial = trimMat;

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 44 m out, past shadow distance anyway
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
        }

        // Square pyramid, 6 tris, apex up. Built inline rather than reusing a primitive because
        // Unity ships no cone/pyramid and a scaled Cylinder reads as a drum at this size.
        static void AddPyramid(Transform parent, string name, Vector3 localPos, float width, float height, Material mat)
        {
            float h = width * 0.5f;
            var verts = new Vector3[]
            {
                new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h), new Vector3(h, 0f, h), new Vector3(-h, 0f, h),
                new Vector3(0f, height, 0f),
            };
            var tris = new int[] { 0,4,1, 1,4,2, 2,4,3, 3,4,0, 0,1,2, 0,2,3 };
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new List<Vector3>(verts));
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string path = $"{RidgeMeshFolder}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // Nothing in this stage was flagged static, so Unity was re-submitting every hill, ridge,
        // rock, bush and background tree as its own draw call — on a scene whose camera is bolted
        // to one spot. Static-batch everything that genuinely never moves.
        //
        // The test is deliberately structural rather than a name list: a prop qualifies only if it
        // carries nothing but Transform/MeshFilter/MeshRenderer. That automatically excludes the
        // roadside trees (GentleSway breeze), the butterflies, the birds and the pollen system,
        // and it stays correct if someone later adds a script to a prop — a name list would not.
        static void MarkStaticBatchable(Transform stage)
        {
            int marked = 0, moving = 0;
            foreach (var r in stage.GetComponentsInChildren<MeshRenderer>(true))
            {
                var go = r.gameObject;
                bool onlyStaticParts = true;
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                    onlyStaticParts = false;
                    break;
                }
                if (!onlyStaticParts) { moving++; continue; }
                GameObjectUtility.SetStaticEditorFlags(go,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                marked++;
            }
            Debug.Log($"[TheDasherArtSceneBuilder] static-batched {marked} props ({moving} left dynamic — animated)");
        }

        // Delete every stage prop the play camera can never see. The Dasher camera is bolted to one
        // spot — the only movement in the whole game is TheDasherDirector.ShakeRoutine, a 0.06m
        // decaying jitter on a meteor hit — so anything outside its frustum is dead weight that
        // ships, loads, and gets culled again at runtime every frame for nothing.
        //
        // Done as a build-time pass rather than by trimming the placement tables by hand: the tables
        // exist to describe a landscape, and hand-pruning them to the frustum would have to be
        // redone from scratch every time a row moves. This stays correct automatically.
        static void CullOffscreen(Transform stage)
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[TheDasherArtSceneBuilder] cull skipped — no Main Camera"); return; }

            // Margin, deliberately generous. The Editor's game-view aspect is NOT the device's, and
            // a WIDER (more square) portrait aspect sees MORE horizontally: 0.85 covers past a 4:3
            // tablet's 0.75. The FOV padding on top absorbs the shake and any future rig nudge.
            // Horizontal is what matters: tan(hHalf) = tan(vHalf) * aspect. Shipping rig (54, 0.65)
            // gives 18.3 deg either side; (58, 0.78) gives 23.4 deg, a 28% horizontal and 7%
            // vertical cushion. That still covers a 4:3 widget (0.75 aspect at fov 54 = 20.9 deg),
            // which is the widest the embedded Flutter view is plausibly resized to.
            float fov0 = cam.fieldOfView;
            cam.fieldOfView = 58f;   // real 54
            cam.aspect = 0.78f;      // real ~0.65 (927x1427)
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            cam.fieldOfView = fov0;
            cam.ResetAspect();

            int culled = 0, tris = 0;
            var doomed = new List<GameObject>();
            foreach (var r in stage.GetComponentsInChildren<Renderer>(true))
            {
                // A shadow caster outside the frustum can still throw a shadow INTO it — the key
                // light comes from behind-left-above, so props off the left edge cast rightward
                // across the lanes. Only the near set (z < NearShadowZ) casts at all, and those are
                // in frame anyway, so skipping them costs nothing.
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                var go = r.gameObject;
                // Never delete a parent that still has visible children under it.
                if (go.GetComponentsInChildren<Renderer>(true).Length != 1) continue;
                if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;

                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                doomed.Add(go);
                culled++;
            }
            foreach (var go in doomed) Object.DestroyImmediate(go);
            Debug.Log($"[TheDasherArtSceneBuilder] culled {culled} off-camera props ({tris:N0} tris)");
        }

        // Real daylight HDRI skybox (replaces the old procedural gradient dome). Also drives
        // scene ambient (Trilight, tuned to the HDRI's tones — simpler and more predictable than
        // baking a skybox-derived ambient probe) and the outdoor haze fog.
        static void BuildSkybox()
        {
            var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(SkyHdrPath);
            if (cubemap == null)
            {
                Debug.LogWarning($"[TheDasherArtSceneBuilder] sky cubemap missing/not Cube-shaped: {SkyHdrPath}");
            }
            else
            {
                AssetDatabase.DeleteAsset(SkyboxMatPath);
                var shader = Shader.Find("Skybox/Cubemap");
                var skyMat = new Material(shader) { name = "TheDasherArtSkybox" };
                skyMat.SetTexture("_Tex", cubemap);
                skyMat.SetFloat("_Exposure", 1.15f);
                // Spin the cubemap so its baked sun sits roughly where KeyLight is pointing from
                // (yaw 118), otherwise the sky says one time of day and the shadows say another.
                skyMat.SetFloat("_Rotation", 200f);
                AssetDatabase.CreateAsset(skyMat, SkyboxMatPath);
                RenderSettings.skybox = skyMat;
            }

            // Ambient is deliberately DIMMER than it looks like it should be. The scene used to run
            // Trilight at full intensity with bright sky colours, and it was drowning the shadows
            // outright: with a diagnostic near-black ambient the same scene renders long raking
            // shadows from the avatar and every tree, so nothing was ever broken — the fill was
            // simply strong enough to erase the contrast that gives the park depth. Verified by
            // probe, not guessed.
            // ⚠ ambientIntensity does NOTHING in Trilight mode — it only scales Skybox ambient.
            // Half an hour went into turning it 0.65 -> 0.45 -> 0.35 with no effect on screen.
            // In Trilight the COLOURS are the intensity, so darken these instead.
            // …but it was taken ~20% too far. Device-verified 2026-08-07: the shadows landed
            // correctly, and the backlit treeline on both sides went nearly black with them.
            // Lifted 25% from the halved values — enough to open the foliage back up, still
            // well under the level that erased the raking shadows in the first place.
            // …and lifted again (+20% on the two lower bands) alongside turning the rim into a front
            // fill. The equator band is the one that reaches VERTICAL surfaces, which is where the
            // black-silhouette bark problem lives; the sky band is left alone because it mostly
            // lands on the grass, which was already reading fine.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.33f, 0.40f, 0.52f);    // cool sky fill
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.47f, 0.40f); // warm bounce
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.15f);

            // Outdoor haze so the background forest / verges melt into the horizon instead of
            // ending in a hard line. No opaque dome anymore, so nothing to clip against — the far
            // clip plane (300) and fog end (260) are the only distance limits now.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyPale;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 235f;

            DynamicGI.UpdateEnvironment();
        }

        // Floor: leafy-grass URP/Lit, tiled ~1 repeat/2m. Lanes + landing pads: park-dirt URP/Lit
        // (a worn jogging track through the lawn) with a cream emissive edge line for crisp lane
        // boundaries. Geometry/positions are UNCHANGED from the original — DasherSpawner depends
        // on them — only the materials are new.
        static void BuildGroundAndLanes(Transform parent)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            DestroyColliderSafe(floor);
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(54f, 1f, 54f);
            const float floorWorldSize = 10f * 54f; // 540m across
            const float floorRepeatMeters = 2f;
            // The raw "leafy_grass" photo is a patchy autumn lawn (lots of brown leaf litter, not
            // vivid green) — tinted here to read as a tended bright-park lawn instead of dead grass.
            // Tint kept under 1.0 on every channel: the previous 1.25 green multiplier clipped the
            // green channel flat, erasing the texture's own variation so the lawn rendered as plain
            // paint. A gentler push keeps it a bright park green while the grass detail survives.
            var floorMat = BuildTexturedLitMat("GrassFloor", new Color(0.62f, 0.92f, 0.48f), GrassDiffPath, GrassNormPath,
                new Vector2(floorWorldSize / floorRepeatMeters, floorWorldSize / floorRepeatMeters));
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            ReceiveOnly(floor);

            // laneZ0 was -0.8, which ENDED THE LANES INSIDE THE SHOT. The camera sits at z=-4.1,
            // 2.2 m up, pitched 10° down with a 54° vertical FOV, so the bottom edge of the frame
            // meets the ground at z = -4.1 + 2.2/tan(37°) ≈ -1.18 — nearly half a metre in FRONT of
            // where the lanes began. The result was a hard horizontal seam across the foreground:
            // the mown strips and their cream edge lines just stopped, with bare floor below them.
            // The strips are thin CUBES, so their end faces were lit head-on by the front fill and
            // drew a bright line along the cut. Start them well behind the frame edge instead.
            const float laneWidth = 0.92f, laneZ0 = -2.6f, laneZ1 = 7.2f;
            float laneLen = laneZ1 - laneZ0, laneCenterZ = (laneZ0 + laneZ1) * 0.5f;
            float[] laneXs = { -1.0f, 0f, 1.0f }; // must match DasherSpawner.laneSpacing
            // Warmed/lightened so the worn-dirt lane reads as clearly NOT-green against the
            // tinted-green floor above — park_dirt's raw tone is close enough in value to
            // leafy_grass that an untinted pairing loses lane/floor contrast.
            // GREEN lanes, matching the original's mown-strip look (the brown dirt track read as a
            // running track and lost the park feel). Same grass texture as the floor but a lighter,
            // slightly yellower tint — the value gap is what separates lane from lawn, exactly the
            // GrassBase-vs-LaneGrass relationship the original used, just textured now.
            var laneMat = BuildTexturedLitMat("LaneGrassMown", new Color(0.82f, 1.0f, 0.62f),
                GrassDiffPath, GrassNormPath, new Vector2(laneWidth / 1.6f, laneLen / 1.6f));
            var edgeMat = PropMeshes.Mat(new Color(0.96f, 0.97f, 0.88f), 0f, 0.2f, new Color(0.30f, 0.31f, 0.26f));
            // Landing pads stay sandy/earthy — the original used a sandy patch here too, and the
            // warm tone is what makes the three target pads pop against an all-green field.
            var padMat = BuildTexturedLitMat("PadDirt", new Color(1.15f, 0.98f, 0.68f),
                DirtDiffPath, DirtNormPath, new Vector2(1.1f, 1.1f));

            foreach (var x in laneXs)
            {
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "LaneStrip";
                DestroyColliderSafe(strip);
                strip.transform.SetParent(parent, false);
                strip.transform.localPosition = new Vector3(x, 0.011f, laneCenterZ);
                strip.transform.localScale = new Vector3(laneWidth, 0.02f, laneLen);
                strip.GetComponent<Renderer>().sharedMaterial = laneMat;
                ReceiveOnly(strip);

                foreach (var side in new[] { -1f, 1f })
                {
                    var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    edge.name = "LaneEdge";
                    DestroyColliderSafe(edge);
                    edge.transform.SetParent(parent, false);
                    edge.transform.localPosition = new Vector3(x + side * laneWidth * 0.5f, 0.022f, laneCenterZ);
                    edge.transform.localScale = new Vector3(0.04f, 0.01f, laneLen);
                    edge.GetComponent<Renderer>().sharedMaterial = edgeMat;
                    ReceiveOnly(edge);
                }

                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = "LandingPad";
                DestroyColliderSafe(pad);
                pad.transform.SetParent(parent, false);
                pad.transform.localPosition = new Vector3(x, 0.015f, 1.8f);
                pad.transform.localScale = new Vector3(1.1f, 0.01f, 1.1f); // r=0.55, h=0.02
                pad.GetComponent<Renderer>().sharedMaterial = padMat;
                ReceiveOnly(pad);
                BuildRing(parent, "PadRim", new Vector3(x, 0.017f, 1.8f), Quaternion.identity, 0.6f, 0.045f, 16, edgeMat);
            }
        }

        // Textured URP/Lit material builder: starts from PropMeshes.Mat (already disables
        // env-reflections/specular so flat surfaces don't blow out white — memory:
        // PropMeshes.MatUnlit doc) then layers on a base-colour + normal map with explicit tiling.
        static Material BuildTexturedLitMat(string name, Color tint, string diffusePath, string normalPath, Vector2 tiling)
        {
            var mat = PropMeshes.Mat(tint, metallic: 0f, smooth: 0.12f);
            mat.name = name;
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
            if (diff != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", diff);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            var norm = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (norm != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", norm);
                mat.SetTextureScale("_BumpMap", tiling);
                mat.EnableKeyword("_NORMALMAP");
                mat.SetFloat("_BumpScale", 1f);
            }
            return mat;
        }

        // Roadside trees framing both sides of the lanes, receding into the distance. TWO rows
        // per side (inner hugging the lanes, outer set further back) so the avenue reads as a
        // tree-lined path. Index-based (not Random) variant/scale/rotation keeps the build
        // deterministic; each gets a GentleSway breeze. Trees within NearShadowZ cast real shadows.
        static void BuildSideScenery(Transform parent)
        {
            // Rows pushed well clear of the play corridor (|x|<1.9) and the verge greenery
            // (|x|<~3.5): these GLB canopies run 3-9m wide at their natural authored scale, wider
            // than the old slender primitive trees the original x-offsets were tuned for — placing
            // them at the old ±3.6/±6.3 buried the lane view in foliage. scaleMul is also more
            // conservative here (0.35-0.55, vs. the background band's 1.5-2.8) to keep roadside
            // trees proportioned like young park trees next to a 1.7m avatar, not a canopy wall.
            // Framing maths: portrait FOV 54 at aspect ~0.65 gives a horizontal half-width of only
            // ~0.33*(z + 4.1) metres. So a tree at |x|=5.2 does not enter frame until z~11, which is
            // why pushing the rows that far out emptied the shot — the avenue was there, just
            // off-screen. Inner row returns to ±3.7 (still clear of the |x|<1.9 play corridor and
            // the ~3.3 verge greenery), with a third row added back for depth.
            // Rows pulled INWARD so the near trees actually land in frame. Half-width is only
            // 0.33*(z+4.1), so at |x|=3.7 nothing entered the picture until z~7 and the first three
            // z-slots rendered entirely off-screen. At |x|=2.7 trees enter from z~4, giving real
            // foreground framing. Still clear of the lanes (which span x -1.46..1.46), and canopy
            // overhang is harmless: hazards fall at z=1.8, NEARER than any tree, so they always
            // render in front of the foliage rather than being hidden by it.
            float[] zs = { 1.5f, 4f, 6.5f, 9f, 12f, 16f, 21f, 27f };
            (float x, float jitter)[] rows =
            {
                (-2.7f, -0.4f), (2.7f, 0.5f), (-4.9f, 0.3f), (4.9f, -0.4f), (-7.6f, 0.4f), (7.6f, -0.5f),
            };
            int i = 0;
            int zi = -1;
            foreach (var z in zs)
            {
                zi++;
                foreach (var row in rows)
                {
                    bool isInner = Mathf.Abs(row.x) < 5f;
                    // Thin the INNER row to every other z. At full density its ~3-5m canopies
                    // overlapped into one continuous hedge — no gaps, no visible trunks, so the
                    // avenue read as a green wall rather than as trees. Spacing them out lets the
                    // trunks show and gives the eye separate objects, which is what makes the
                    // Quake Escape frames read as built scenery instead of a mass.
                    if (isInner && zi % 2 == 1) { i++; continue; }
                    float zz = z + (Mathf.Abs(row.x) > 5f ? 1.4f : 0f);
                    // Detailed meshes only where the camera can actually resolve them. Variant cost
                    // spans 4x (NormalTree_1 = 8,520 tris, NormalTree_5 = 2,182), and past ~14m the
                    // difference is invisible — so the far two-thirds of the avenue uses the cheap
                    // meshes. Saves ~80k triangles with no perceptible change.
                    (string glb, string mesh) variant = zz > 14f
                        ? (((i & 1) == 0) ? (glb: TreesGlb, mesh: "NormalTree_5")
                                          : (glb: MapleGlb, mesh: "MapleTree_4"))
                        : TreeVariants[i % TreeVariants.Length];
                    // Inner row MUST stay small. Tried 0.88, and again 1.15 on the mid row — both
                    // buried the left lane under canopy, because these Quaternius trees are much
                    // BROADER than they are tall, so scaling for height also reaches across the
                    // track. Height instead comes from the mid/outer rows, which (given the narrow
                    // portrait frustum, half-width ~0.33*(z+4.1)) only enter frame from z~14 and
                    // z~24 — well past the play area, so they add skyline without blocking anything.
                    // Thresholds follow the new row positions (2.7 / 4.9 / 7.6). Inner scale trimmed
                    // slightly since these trees are now closer to camera and so read larger anyway.
                    float inner = Mathf.Abs(row.x) < 4f ? 0.48f : (Mathf.Abs(row.x) < 6.5f ? 0.82f : 1.1f);
                    float scaleMul = inner + (i % 5) * 0.06f;
                    float x = row.x + row.jitter * ((i % 2 == 0) ? 0.3f : -0.3f);
                    var tree = SpawnGlbMesh(variant.glb, variant.mesh, parent, new Vector3(x, HillHeightAt(x, zz), zz),
                        Quaternion.Euler(0f, i * 47f, 0f), scaleMul, zz < NearShadowZ, "Tree");
                    if (tree != null) tree.AddComponent<GentleSway>().amplitudeDeg = 1.6f;
                    i++;
                }
            }
        }

        // A dense tree band filling the mid-distance between the roadside rows and the horizon, so
        // it reads as woodland rather than bare grass. Cheap variants only (BgTreeVariants) — fog
        // and distance hide the extra detail of the pricier meshes, so there's no reason to pay for
        // it. Deterministic jittered grid, skipping the central lane corridor. No sway (too far to
        // notice) and never casts shadows (beyond NearShadowZ).
        static void BuildBackgroundForest(Transform parent)
        {
            int i = 0;
            for (int zi = 0; zi < 6; zi++)
            {
                // Starts at z=20 (was 30) and steps 6m, so woodland reaches forward into the band
                // that was reading as a plain green field in gameplay. Only xi==0 is skipped now:
                // hazards fall vertically at z=1.8, so there is no approach sight-line down the
                // lane that needs protecting, and the old 2-column gap left a ~10m corridor —
                // wider than the whole portrait frustum out there, hence the emptiness.
                float z = 20f + zi * 6f;
                for (int xi = -6; xi <= 6; xi++)
                {
                    // Centre column stays open only for the nearest rows, so nothing appears to
                    // sprout from the avatar's head; from z=32 back it fills in, closing the plain
                    // green gap that ran straight up the middle of the gameplay frame.
                    if (xi == 0 && z < 32f) continue;
                    var variant = BgTreeVariants[i % BgTreeVariants.Length];
                    // Was 1.5-2.55, which produced canopies up to 23m wide — single trees spanning
                    // a third of the horizon read as blobs rather than woodland. Tightened so the
                    // band reads as many trees instead of a few giants.
                    float scaleMul = 1.05f + (i % 4) * 0.2f;
                    float x = xi * 3.8f + ((i % 3) - 1) * 1.2f;
                    float zz2 = z + (i % 2) * 2f;
                    // Follow the terrain: the hill band overlaps this z range, and anything left at
                    // y=0 gets swallowed by the slope above it — the same buried-model class of bug
                    // as the pivot issue. This is why the far slope looked like a bare green face.
                    SpawnGlbMesh(variant.glb, variant.mesh, parent, new Vector3(x, HillHeightAt(x, zz2), zz2),
                        Quaternion.Euler(0f, i * 53f, 0f), scaleMul, false, "BgTree");
                    i++;
                }
            }
        }

        // Scatter across the open lawn between the end of the lanes (z=7.2) and the woodland band,
        // which otherwise renders as a plain green sheet filling the middle of the gameplay frame.
        // Deliberately ground-level clutter, NOT more trees: bushes/tufts/rocks are 60-430 tris each
        // (a tree is 2,182), so this whole function costs less than six background trees, and low
        // scatter breaks the emptiness without walling off the view. Safe to place here because
        // hazards fall vertically at impactZ=1.8 — nothing gameplay-relevant occupies this band.
        static void BuildMidLawnScatter(Transform parent)
        {
            (float x, float z, int kind)[] spots =
            {
                (-3.1f, 9.5f, 0),  (3.4f, 10.8f, 1),  (-5.2f, 12.4f, 0),  (5.6f, 9.2f, 2),
                (-2.6f, 14.6f, 1), (2.9f, 16.2f, 0),  (-7.1f, 15.8f, 0),  (7.4f, 13.5f, 1),
                (-4.3f, 18.9f, 2), (4.8f, 20.4f, 0),  (-8.6f, 21.2f, 1),  (8.2f, 18.1f, 0),
                (-2.2f, 23.5f, 0), (3.6f, 25.1f, 1),  (-6.4f, 26.3f, 0),  (6.9f, 24.0f, 2),
                (-10.3f, 11.6f, 1),(10.1f, 16.9f, 0), (-11.2f, 22.7f, 0), (11.6f, 26.8f, 1),
                (-1.8f, 28.4f, 1), (2.4f, 30.2f, 0),  (-5.7f, 31.5f, 1),  (5.1f, 29.3f, 0),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var s = spots[i];
                float y = HillHeightAt(s.x, s.z); // terrain-follow, same reason as the bg trees
                switch (s.kind)
                {
                    case 0: // bush clump
                        var bv = BushVariants[i % BushVariants.Length];
                        SpawnGlbMesh(bv.glb, bv.mesh, parent, new Vector3(s.x, y, s.z),
                            Quaternion.Euler(0f, i * 57f, 0f), 0.7f + (i % 3) * 0.25f,
                            s.z < NearShadowZ, "LawnBush");
                        break;
                    case 1: // grass tuft
                        var gv = GrassTuftVariants[i % GrassTuftVariants.Length];
                        SpawnGlbMesh(gv.glb, gv.mesh, parent, new Vector3(s.x, y, s.z),
                            Quaternion.Euler(0f, i * 83f, 0f), 0.8f + (i % 2) * 0.35f, false, "LawnTuft");
                        break;
                    default: // rock, part-buried so it sits in the turf
                        SpawnGlbMesh(RocksGlb, "Rock_" + (1 + i % 5), parent, new Vector3(s.x, y, s.z),
                            Quaternion.Euler(0f, i * 71f, 0f), 0.45f + (i % 3) * 0.2f, false, "LawnRock", 0.12f);
                        break;
                }
            }
        }

        // Rolling meadow hills sitting behind the woodland band, so the horizon reads as receding
        // countryside instead of a flat green plane meeting the sky. Flattened spheres sunk into the
        // ground so only a rounded cap shows — a sphere is 768 tris, so a dozen of these cost less
        // than two background trees while carrying most of the depth. Lit + grass-textured; the
        // original scene used unlit flat colour here, which is exactly what made its horizon read
        // as flat paper. Sink is 0.15*h: the sphere radius is h/2, so a deeper sink than 0.5*h would
        // bury the hill completely.
        // Shared so BuildHillTreeline can sample the hill surface — trees must sit ON the slopes,
        // not at y=0 where the hill would swallow them.
        // Pulled MUCH closer than the first pass (was z 76-138). Because hazards FALL FROM THE SKY
        // at impactZ=1.8 rather than travelling down the lane, everything past z~10 is pure backdrop
        // — so the distance can be filled aggressively without hurting hazard readability. At z 44-88
        // these read as real landscape instead of a thin smudge on the horizon.
        // Depth-to-width ratio of each hill ellipsoid. Was 0.7, which at these closer z values gave
        // a z-radius of 0.35*w — a 110m-wide hill then reached ~38m FORWARD of its centre, sprawling
        // right into the play field and bulging up behind the lanes. 0.5 keeps them as ridges lying
        // across the view rather than domes swelling toward the camera.
        // MUST stay in sync with HillHeightAt below.
        const float HillDepthRatio = 0.5f;

        static readonly (float x, float z, float w, float h, bool isFar)[] Hills =
        {
            (-55f, 58f, 110f, 15f, false), (52f, 62f, 120f, 17f, false), (0f, 68f, 100f, 13f, false),
            (-118f, 70f, 150f, 23f, true), (112f, 72f, 140f, 21f, true), (-32f, 78f, 125f, 19f, true),
            (72f, 84f, 145f, 25f, true), (-168f, 80f, 160f, 25f, true), (158f, 90f, 150f, 27f, true),
        };

        // Height of the hill surface at (x,z), or 0 on open ground. Each hill is a squashed sphere,
        // i.e. an ellipsoid with radii (w/2, h/2, 0.35w) centred at y = -0.15h; solving it for y
        // gives the cap height. Takes the max so overlapping hills stack correctly.
        static float HillHeightAt(float x, float z)
        {
            float best = 0f;
            foreach (var h in Hills)
            {
                float rx = h.w * 0.5f, ry = h.h * 0.5f, rz = h.w * HillDepthRatio * 0.5f;
                float dx = (x - h.x) / rx, dz = (z - h.z) / rz;
                float inside = 1f - dx * dx - dz * dz;
                if (inside <= 0f) continue;
                best = Mathf.Max(best, -h.h * 0.15f + ry * Mathf.Sqrt(inside));
            }
            return best;
        }

        static void BuildDistantHills(Transform parent)
        {
            var nearMat = BuildTexturedLitMat("HillNear", new Color(0.54f, 0.82f, 0.44f),
                GrassDiffPath, GrassNormPath, new Vector2(26f, 26f));
            var farMat = BuildTexturedLitMat("HillFar", new Color(0.60f, 0.80f, 0.56f),
                GrassDiffPath, GrassNormPath, new Vector2(32f, 32f));
            foreach (var h in Hills)
            {
                var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Hill";
                DestroyColliderSafe(hill);
                hill.transform.SetParent(parent, false);
                hill.transform.localPosition = new Vector3(h.x, -h.h * 0.15f, h.z);
                hill.transform.localScale = new Vector3(h.w, h.h, h.w * HillDepthRatio);
                hill.GetComponent<Renderer>().sharedMaterial = h.isFar ? farMat : nearMat;
                NoShadow(hill);
            }
        }

        // A sparse scatter of trees across the gap between the woodland band and the hills, and up
        // onto the hill slopes. Without these the hills are bald domes and there's a bare green gap
        // at z~65-80 between forest and hills. Deliberately few and cheap (NormalTree_5, 2,182 tris)
        // and scaled up rather than multiplied — at 70-115m, fog does most of the work, so silhouette
        // is all that survives and paying for more instances buys nothing.
        static void BuildHillTreeline(Transform parent)
        {
            // Re-sited to match the hills' new closer positions (z 44-88). Scales are smaller too:
            // at half the previous distance the same multiplier read as giant.
            (float x, float z, float s)[] spots =
            {
                (-46f, 33f, 1.7f),  (38f, 36f, 1.8f),  (-10f, 42f, 1.5f),  (74f, 44f, 2.0f),
                (-86f, 45f, 2.0f),  (16f, 50f, 1.8f),  (-38f, 53f, 2.2f),  (104f, 52f, 2.2f),
                (-128f, 55f, 2.2f), (58f, 59f, 2.4f),  (-18f, 62f, 2.2f),  (142f, 64f, 2.6f),
                (-172f, 60f, 2.4f), (92f, 69f, 2.6f),  (-64f, 67f, 2.4f),  (8f, 70f, 2.4f),
                // Extra scatter to mottle the hill faces — a smooth lit dome reads as a bare green
                // slope, which is the "plain" Pokpong flagged. Broken silhouettes sell distance.
                (-100f, 38f, 1.8f), (68f, 35f, 1.7f),  (-24f, 48f, 1.9f),  (128f, 58f, 2.3f),
                (-152f, 49f, 2.1f), (34f, 66f, 2.3f),  (-92f, 58f, 2.2f),  (112f, 72f, 2.5f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var s = spots[i];
                // Sit on the hill surface, not on y=0 — on a 30m dome the difference is the whole
                // tree. A small negative embed digs the trunk into the slope so it doesn't appear
                // to balance on a single point where the ground curves away beneath it.
                float y = HillHeightAt(s.x, s.z);
                SpawnGlbMesh(TreesGlb, "NormalTree_5", parent, new Vector3(s.x, y, s.z),
                    Quaternion.Euler(0f, i * 67f, 0f), s.s, false, "HillTree", 0.35f);
            }
        }

        // A jagged mountain range on the skyline behind the hills, giving a fourth depth layer
        // (lawn -> woodland -> hills -> mountains -> sky).
        //
        // Was built from flattened spheres and read as INVISIBLE in the game camera, twice. Moving
        // them closer never helped, because distance was never the problem — measured against the
        // camera rig (y=2.2, pitch +10, vFOV 54, so the frame spans -37..+17 deg) the old summits
        // already sat 14-16% down from the top edge, which is where a range should be. Two real
        // causes:
        //   1. COLOUR. The old base (0.55,0.66,0.74) is itself sky-coloured, and linear fog then
        //      blended it another 28% toward SkyPale, landing on ~(0.61,0.72,0.80) against a sky of
        //      ~(0.70,0.80,0.95). Almost no contrast edge, so nothing to see. The bases below are
        //      picked so that AFTER the fog lerp at each range's own distance they still land well
        //      below the sky — atmospheric perspective is preserved by the gradient between the
        //      three ranges, not by starting each one at sky colour.
        //   2. SILHOUETTE. A sphere scaled 200x92x110 shows only its top ~18m, i.e. a very shallow
        //      arc. A smooth dome carries no silhouette information, so the eye never reads
        //      "mountain". Saw-tooth peaks do.
        // Untextured on purpose: at 100-170m fog eats any detail, and a flat lit tone reads as haze.
        static void BuildFarRidge(Transform parent)
        {
            if (!AssetDatabase.IsValidFolder(RidgeMeshFolder))
                AssetDatabase.CreateFolder("Assets/TheDasherArt", "Meshes");

            // halfW is generous: the portrait frustum is only ~0.33*(z+4.1) wide either side, so
            // z=102 needs 34m and z=168 needs 57m. The surplus keeps the ends off-screen.
            // Segment count is what sets peak WIDTH, and it is the difference between mountains and
            // shark fins: 26 segments across halfW=85 gave a 6.5m half-width under a 28m summit, a
            // 1:4 spike. 14 segments puts the base:height nearer 1:1.3 while still showing ~3 peaks
            // inside the frustum's ~70-113m visible width at these depths.
            (float z, float halfW, float lo, float hi, Color c, int seed)[] ranges =
            {
                // Ceiling on the far range: the frame's top edge is +17 deg, and a 47m summit at
                // 172m sits at 14.6 deg. Much taller and the peaks clip out of frame.
                (168f, 140f, 33f, 47f, new Color(0.46f, 0.54f, 0.64f), 37),
                (132f, 110f, 25f, 37f, new Color(0.38f, 0.46f, 0.56f), 23),
                (102f,  85f, 16f, 26f, new Color(0.30f, 0.37f, 0.47f), 11),
            };
            for (int i = 0; i < ranges.Length; i++)
            {
                var r = ranges[i];
                var mesh = BuildRidgeMesh(r.halfW, r.lo, r.hi, r.halfW * 0.09f, 14, r.seed);
                string path = $"{RidgeMeshFolder}/Ridge_{i}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);

                var go = new GameObject("Ridge");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(0f, 0f, r.z);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mat = PropMeshes.Mat(r.c, 0f, 0.03f, Color.black);
                mat.name = "Ridge" + i;
                mr.sharedMaterial = mat;
                NoShadowR(mr);
            }
        }

        // A faceted "curtain": a polyline of alternating blunt-crowned peaks and saddles, filled down
        // to BaseY. The Dasher camera is bolted to one spot, so a wall with a jagged top edge is
        // everything a mountain range needs — ~52 tris each, against ~768 for one of the spheres
        // this replaced. Peaks step toward the camera and saddles away from it, so
        // RecalculateNormals gives every facet its own normal and the range shades in planes
        // instead of reading as a single flat cut-out. Seeded System.Random, not Random.value, so
        // the skyline is identical on every rebuild.
        // One flank of a peak, emitted left-to-right. `span` is signed (negative = the left flank).
        // `innerHy` is the height of the INNERMOST point, i.e. the summit end — the left flank's
        // and the right flank's innermost points together form the short tilted facet across the
        // top, so there is no separate "cap" that could overshoot them and fold the outline back.
        // Facet count, the heights of the shoulders, and the bend of the slope are all drawn per
        // flank, so no two flanks in the range are alike.
        static void AddPeakFlank(List<Vector3> top, System.Random rnd, float x, float h,
                                 float span, float fold, bool ascending, float innerHy)
        {
            int steps = 2 + (int)(rnd.NextDouble() * 2.99); // 2-4 shoulders
            var hs = new float[steps];
            for (int k = 0; k < steps; k++) hs[k] = 0.22f + (float)rnd.NextDouble() * 0.66f;
            System.Array.Sort(hs);       // low at the outside, climbing toward the summit
            hs[steps - 1] = innerHy;

            // bend < 1 bows the flank out (convex, buttressed); bend > 1 pulls it in (concave,
            // flared base). Drawn per flank, so one mountain can have a convex left and concave
            // right — the single biggest thing stopping the range reading as one repeated shape.
            float bend = 0.55f + (float)rnd.NextDouble() * 1.1f;
            for (int k = 0; k < steps; k++)
            {
                int idx = ascending ? k : steps - 1 - k;
                // frac must run 0 -> 0.9 across the flank. 0 puts the outermost point at the FULL
                // span so the flank actually reaches down to its saddle; stopping at 0.9 rather
                // than 1.0 leaves the innermost point just off centre, and the left and right
                // flanks' innermost points are then what open up the summit facet.
                // (Using (idx+1)/(steps+1) here was a bug: its smallest value is 1/(steps+1), so
                // with bend<1 the outermost point only reached ~45% of span and every mountain
                // came out a narrow spire floating above a wide gap.)
                float frac = 0.90f * idx / (steps - 1);
                float factor = 1f - Mathf.Pow(frac, bend);
                // The innermost point is half the summit facet, so its offset sets how blunt the
                // top is. Left to the curve it collapses toward zero whenever bend < 1, which is
                // what let sharp peaks back in — floor it so every summit keeps a facet.
                if (idx == steps - 1) factor = Mathf.Max(factor, 0.16f);
                float hy = hs[idx];
                top.Add(new Vector3(x + span * factor,
                                    h * hy,
                                    -fold * (0.5f + 0.5f * hy)));
            }
        }

        static Mesh BuildRidgeMesh(float halfWidth, float peakLo, float peakHi, float zFold, int segments, int seed)
        {
            const float BaseY = -40f; // well under the horizon; the ground plane hides the bottom edge

            var rnd = new System.Random(seed);
            float step = 2f * halfWidth / segments;

            // Pass 1 — node positions, IRREGULARLY spaced. A fixed grid puts a peak every two
            // steps, and that regular beat is itself a pattern: once the peak shapes stopped being
            // random noise, the even spacing became the thing the eye picked up. Walk the cursor
            // forward by a random 0.62-1.38 of a step instead. The loop runs past halfWidth rather
            // than for a fixed count, so a run of short steps can't leave the range short.
            var nx = new List<float>();
            for (float cursor = -halfWidth; cursor < halfWidth + step; cursor += step * (0.62f + (float)rnd.NextDouble() * 0.76f))
                nx.Add(cursor);

            int n = nx.Count;
            var nh = new float[n];
            var isPeak = new bool[n];
            for (int i = 0; i < n; i++)
            {
                isPeak[i] = (i & 1) == 1 && i > 0 && i < n - 1 && rnd.NextDouble() > 0.18;
                nh[i] = isPeak[i]
                    ? Mathf.Lerp(peakLo, peakHi, (float)rnd.NextDouble())
                    : Mathf.Lerp(peakLo * 0.24f, peakLo * 0.66f, (float)rnd.NextDouble());
            }

            // Pass 2 — expand each node into the silhouette. Every peak's profile is generated
            // fresh. Round 10 used ONE shared profile table scaled by height, which made every
            // mountain the same shape at a different size — that is what read as "too much
            // pattern". Flank spans are taken as a fraction of the ACTUAL gap to each neighbour,
            // so irregular spacing can never make a profile overrun its saddle.
            var top = new List<Vector3>(n * 8);
            for (int i = 0; i < n; i++)
            {
                float x = nx[i], h = nh[i];
                float fold = zFold * (0.45f + (float)rnd.NextDouble() * 0.8f);
                if (!isPeak[i]) { top.Add(new Vector3(x, h, fold)); continue; }

                float gapL = x - nx[i - 1], gapR = nx[i + 1] - x;
                float tilt = 0.93f + (float)rnd.NextDouble() * 0.06f;
                bool leftHigh = rnd.NextDouble() > 0.5;
                // Spans reach 0.70-0.92 of the real gap: broad mountains that nearly meet their
                // saddles. Anything under ~0.6 leaves a conspicuous flat gap between peaks.
                AddPeakFlank(top, rnd, x, h, -gapL * (0.70f + (float)rnd.NextDouble() * 0.22f),
                             fold, true, leftHigh ? 1f : tilt);
                AddPeakFlank(top, rnd, x, h, gapR * (0.70f + (float)rnd.NextDouble() * 0.22f),
                             fold, false, leftHigh ? tilt : 1f);
            }

            var verts = new List<Vector3>(top.Count * 4);
            var tris = new List<int>(top.Count * 6);
            for (int i = 0; i < top.Count - 1; i++)
            {
                Vector3 a = top[i], b = top[i + 1];
                int v = verts.Count;
                // a=top-left, b=top-right, then bottom-right, bottom-left. Viewed from -Z that
                // winding is clockwise, i.e. front-facing toward the camera.
                verts.Add(a);
                verts.Add(b);
                verts.Add(new Vector3(b.x, BaseY, b.z));
                verts.Add(new Vector3(a.x, BaseY, a.z));
                tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
            }

            var mesh = new Mesh { name = "RidgeMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Bushes + flower clumps + grass tufts along the grass verges either side of the lanes,
        // skipping the |x|<1.9 play corridor — replaces the old sphere lobes / sphere flower dots.
        static void BuildVergeGreenery(Transform parent)
        {
            (float x, float z)[] bushSpots =
            {
                (-2.6f, 0.6f), (2.7f, 1.3f), (-3.2f, 3.1f), (3.3f, 4.4f),
                (-2.4f, 6.2f), (2.5f, 7.6f), (-4.7f, 2.2f), (4.8f, 5.2f),
            };
            for (int i = 0; i < bushSpots.Length; i++)
            {
                var s = bushSpots[i];
                var v = BushVariants[i % BushVariants.Length];
                float scaleMul = 0.8f + (i % 3) * 0.15f;
                SpawnGlbMesh(v.glb, v.mesh, parent, new Vector3(s.x, 0f, s.z),
                    Quaternion.Euler(0f, i * 61f, 0f), scaleMul, s.z < NearShadowZ, "Bush");
            }

            int idx = 0;
            for (int side = -1; side <= 1; side += 2)
                for (int zi = 0; zi < 6; zi++)
                {
                    float z = 0.6f + zi * 1.6f;
                    bool near = z < NearShadowZ;

                    float xFlower = side * (2.1f + (zi % 2) * 0.4f);
                    var fv = FlowerVariants[idx % FlowerVariants.Length];
                    // Was 1.0-1.3, which put single blooms at 0.43-0.93m across — flowers the size
                    // of the avatar's torso. Roughly a third of that reads as a real flower clump.
                    SpawnGlbMesh(fv.glb, fv.mesh, parent, new Vector3(xFlower, 0f, z),
                        Quaternion.Euler(0f, idx * 39f, 0f), 0.34f + (idx % 3) * 0.05f, near, "Flower");
                    idx++;

                    float xGrass = side * (2.9f + (zi % 3) * 0.35f);
                    var gv = GrassTuftVariants[idx % GrassTuftVariants.Length];
                    SpawnGlbMesh(gv.glb, gv.mesh, parent, new Vector3(xGrass, 0f, z + 0.5f),
                        Quaternion.Euler(0f, idx * 71f, 0f), 0.55f + (idx % 2) * 0.12f, near, "GrassTuft");
                    idx++;
                }
        }

        // A handful of rocks scattered on the verges for variety — replaces nothing from the
        // original (new addition per the art-pass brief).
        static void BuildVergeRocks(Transform parent)
        {
            (float x, float z, int variant)[] rocks =
            {
                (-4.2f, 2.0f, 1), (4.4f, 3.4f, 2), (-5.6f, 6.8f, 3), (5.8f, 8.5f, 4),
                (-3.1f, 10.5f, 5), (3.4f, 12.0f, 1), (-6.4f, 15.0f, 2), (6.6f, 17.5f, 3),
            };
            for (int i = 0; i < rocks.Length; i++)
            {
                var r = rocks[i];
                // Small embed so rocks sit partly buried in the turf rather than perched on top of
                // it — a boulder resting exactly on the ground plane reads as a floating prop.
                SpawnGlbMesh(RocksGlb, "Rock_" + r.variant, parent, new Vector3(r.x, 0f, r.z),
                    Quaternion.Euler(0f, i * 83f, 0f), 0.5f + (i % 3) * 0.15f, r.z < NearShadowZ, "Rock", 0.1f);
            }
        }

        // A few bird silhouettes gliding across the sky — two angled dark wings each, drifting on
        // the same CloudDrift used elsewhere. High and far so they never cross the play field.
        static void BuildBirds(Transform parent)
        {
            var birdMat = PropMeshes.MatUnlit(new Color(0.24f, 0.26f, 0.32f));
            (float x, float y, float z, float s, float speed)[] birds =
            {
                (-18f, 16f, 70f, 1.4f, 0.9f), (6f, 21f, 90f, 1.1f, 0.7f),
                (22f, 14f, 62f, 1.2f, 1.1f),  (-9f, 24f, 118f, 1.6f, 0.55f),
            };
            foreach (var b in birds)
            {
                var bird = new GameObject("Bird");
                bird.transform.SetParent(parent, false);
                bird.transform.localPosition = new Vector3(b.x, b.y, b.z);
                AddWing(bird.transform, 1f, b.s, birdMat);
                AddWing(bird.transform, -1f, b.s, birdMat);
                var drift = bird.AddComponent<CloudDrift>();
                drift.speed = b.speed;
                drift.range = 44f;
            }
        }

        static void AddWing(Transform parent, float side, float size, Material mat)
        {
            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = "Wing";
            DestroyColliderSafe(wing);
            wing.transform.SetParent(parent, false);
            wing.transform.localPosition = new Vector3(side * size * 0.45f, 0f, 0f);
            wing.transform.localRotation = Quaternion.Euler(0f, 0f, side * 22f); // shallow V
            wing.transform.localScale = new Vector3(size, size * 0.12f, size * 0.03f);
            wing.GetComponent<Renderer>().sharedMaterial = mat;
            NoShadow(wing);
        }

        // Gentle warm pollen motes drifting low over the grass. Reuses the soft additive dot +
        // pre-Simulate so it shows in the edit shot.
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
            { name = "DasherStarDot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_SoftDotTex.SetPixels32(px);
            s_SoftDotTex.Apply();
            return s_SoftDotTex;
        }

        static Material SoftDotMaterial()
        {
            if (s_SoftDotMat != null) return s_SoftDotMat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            s_SoftDotMat = new Material(shader) { name = "DasherStarMat" };
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

        // A ring made of small emissive cube segments (same technique as DasherProps.TelegraphRing's
        // Ring child) — no torus primitive exists in Unity. Used for the landing-pad rims.
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

        // ---- GLB prop instancing: pulls a mesh + its materials directly out of an imported
        // .glb's sub-assets (both are persisted, stable asset references — safe to reuse across
        // any number of placements with zero extra material/mesh instances) and wraps them in a
        // bare MeshFilter+MeshRenderer. Cheaper than instantiating the whole prefab hierarchy per
        // placement, and keeps material count down since every placement shares the exact same
        // Material reference the GLB importer created. ----

        static readonly Dictionary<string, GameObject> s_ModelCache = new Dictionary<string, GameObject>();

        static GameObject LoadModelRoot(string glbPath)
        {
            if (s_ModelCache.TryGetValue(glbPath, out var go)) return go;
            go = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            s_ModelCache[glbPath] = go;
            if (go == null) Debug.LogWarning($"[TheDasherArtSceneBuilder] GLB not found: {glbPath}");
            return go;
        }

        static Transform FindChildRecursive(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Spawns a standalone instance of one named mesh from a GLB pack at <paramref name="parent"/>.
        /// <paramref name="scaleMul"/> is a variety multiplier ON TOP of the mesh's own authored
        /// scale (Quaternius nodes bake a real-world scale of ~75-100x into the node transform —
        /// read from the source node so placed props come out at the size the artist intended).
        /// <paramref name="castShadow"/> also controls receiveShadows (both on or both off) per
        /// the near/far shadow budget (Stage 3: only objects within ~15m of the camera cast/receive).
        /// </summary>
        // ---- GLB material conversion ------------------------------------------------------
        // The Quaternius GLBs arrive with Shader Graph glTF-pbrMetallicRoughness materials, and
        // every foliage one is ALPHA-BLENDED (render queue 3000, alphaCutoff 0). That is wrong
        // three ways on a phone: blended geometry writes no depth, so ~90 dense canopies pile up
        // enormous overdraw next to MediaPipe's inference budget; blended geometry does not cast
        // or receive proper shadows, which is why the whole park rendered without a single one;
        // and it sorts per object, so canopies flicker against each other.
        //
        // Their textures are also glb SUB-ASSETS. A .glb is read by GLTFast's ScriptedImporter,
        // so those textures have no TextureImporter and therefore no compression settings at all:
        // they shipped as ARGB32, ~102 MB of VRAM (Rocks alone was 43 MB at 2048²). Extracted
        // copies now live in Textures/Extracted as normal PNGs at 512² ASTC_6x6 — 1.55 MB total.
        //
        // So rebuild each glb material as URP/Lit against the extracted textures. PropMeshes.Mat
        // additionally kills environment reflections + specular highlights, which is what was
        // blowing the canopies out into white glowing patches under Bloom.
        static readonly Dictionary<string, (string tex, string normal, bool cutout)> GlbMaterialMap =
            new Dictionary<string, (string, string, bool)>
            {
                { "NormalTree_Bark",   ("NormalTree_Bark",   "NormalTree_Bark_Normal", false) },
                { "NormalTree_Leaves", ("NormalTree_Leaves", null,                     true)  },
                { "MapleTree_Bark",    ("MapleTree_Bark",    "MapleTree_Bark_Normal",  false) },
                { "MapleTree_Leaves",  ("MapleTree_Leaves",  null,                     true)  },
                { "Bush_Leaves",       ("Bush_Leaves",       null,                     true)  },
                { "Flowers",           ("Flowers",           null,                     true)  },
                { "Grass",             ("Grass",             null,                     false) }, // measured: zero transparent texels
                { "Rock",              ("Rocks",             null,                     false) },
            };

        const string ExtractedTexFolder = "Assets/TheDasherArt/Textures/Extracted/";
        static readonly Dictionary<string, Material> ConvertedGlbMats = new Dictionary<string, Material>();

        static Material[] ConvertGlbMaterials(Material[] src)
        {
            var result = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var s = src[i];
                if (s == null) continue;
                if (!GlbMaterialMap.TryGetValue(s.name, out var entry)) { result[i] = s; continue; }
                if (ConvertedGlbMats.TryGetValue(s.name, out var cached)) { result[i] = cached; continue; }

                var mat = PropMeshes.Mat(Color.white, metallic: 0f, smooth: entry.cutout ? 0.06f : 0.10f);
                mat.name = s.name;

                var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(ExtractedTexFolder + entry.tex + ".png");
                if (diff != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", diff);
                if (entry.normal != null)
                {
                    var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(ExtractedTexFolder + entry.normal + ".png");
                    if (nrm != null && mat.HasProperty("_BumpMap"))
                    {
                        mat.SetTexture("_BumpMap", nrm);
                        mat.EnableKeyword("_NORMALMAP");
                        mat.SetFloat("_BumpScale", 0.8f);
                    }
                }

                if (entry.cutout)
                {
                    // Alpha CLIP, not blend — keeps the depth write, so the canopy occludes
                    // properly, casts a real shadow, and costs one pass instead of sorted overdraw.
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    // These leaf textures are ~64% fully transparent texels: the foliage is built
                    // from CARDS, and a card seen from its back face was being culled away. That
                    // is what made scattered trees look bare and dead. Draw both faces.
                    mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    mat.doubleSidedGI = true;
                }

                ConvertedGlbMats[s.name] = mat;
                result[i] = mat;
            }
            return result;
        }

        static GameObject SpawnGlbMesh(string glbPath, string meshName, Transform parent, Vector3 localPos,
            Quaternion localRot, float scaleMul, bool castShadow, string nameOverride = null, float embed = 0f)
        {
            var root = LoadModelRoot(glbPath);
            if (root == null) return null;
            var srcT = FindChildRecursive(root.transform, meshName);
            if (srcT == null)
            {
                Debug.LogWarning($"[TheDasherArtSceneBuilder] mesh '{meshName}' not found in {glbPath}");
                return null;
            }
            var mf = srcT.GetComponent<MeshFilter>();
            var mr = srcT.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null) return null;

            var go = new GameObject(nameOverride ?? meshName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // COMPOSE with the source node's own rotation — do not overwrite it. These GLBs come in
            // from glTF's Z-up convention, so every mesh node carries a corrective localRot of
            // (270,0,0) that stands the model upright. Assigning only the yaw here left every tree,
            // bush, rock and flower LYING ON ITS SIDE: what looked like absurdly wide canopies
            // blocking the lanes was actually tree HEIGHT sprawled along Z. Yaw must be applied
            // after the correction, hence localRot * srcT.localRotation (not the reverse).
            go.transform.localRotation = localRot * srcT.localRotation;
            go.transform.localScale = Vector3.one * (srcT.localScale.x * scaleMul);

            var newMf = go.AddComponent<MeshFilter>();
            newMf.sharedMesh = mf.sharedMesh;
            var newMr = go.AddComponent<MeshRenderer>();
            newMr.sharedMaterials = ConvertGlbMaterials(mr.sharedMaterials);
            newMr.shadowCastingMode = castShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            newMr.receiveShadows = castShadow;
            newMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            // GROUNDING (do not remove). These Quaternius GLB meshes are authored with the pivot at
            // the mesh CENTRE, not the base — so dropping one at y=0 buries half of it. Measured
            // before this fix: trees sat up to 3.02m underground, bushes 0.61m, rocks 0.48m. Shift
            // the object so the bottom of its bounds rests exactly on the intended ground height.
            // `embed` then pushes it back down a little where partial burial looks right (rocks).
            float groundY = parent != null ? parent.TransformPoint(localPos).y : localPos.y;
            float sink = newMr.bounds.min.y - groundY;
            if (Mathf.Abs(sink) > 0.0005f)
                go.transform.position -= new Vector3(0f, sink + embed, 0f);
            return go;
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

        // Ground/lane/pad surfaces: never worth casting (they're flat at y=0), but always worth
        // receiving so the avatar's shadow actually lands on something.
        static void ReceiveOnly(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
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
            so.FindProperty("modelFileName").stringValue = "pose_landmarker_lite.bytes";
            so.FindProperty("flipX").boolValue = true;
            so.FindProperty("flipY").boolValue = true;
            so.FindProperty("use3DWorld").boolValue = true;
            so.FindProperty("drivesAvatar").boolValue = true;
            so.FindProperty("mirrorPreview").boolValue = true;
            so.FindProperty("previewRotationCW").intValue = 270;
            so.FindProperty("autoCalibrate").boolValue = false;
            so.FindProperty("useOneEuroSmoothing").boolValue = true;
            // Back-view scene: the avatar "follows" the player's own side instead of mirroring.
            // This is only the EDITOR-TIME default — TheDasherDirector.Awake() overrides it at
            // runtime from its invertLateralForBackView master flag (keeps the avatar's limb
            // mapping in agreement with the lane/kick-side laterality fix; see that flag's tooltip).
            so.FindProperty("sameSideRetarget").boolValue = true;
            // Back view: keep the upper spine steady (head/ear drive folds the torso, looks wrong).
            so.FindProperty("steadyUpperSpine").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Writes into Assets/TheDasherArt/ — DELIBERATELY a different folder/asset from the
        // original TheDasherSceneBuilder's Assets/TheDasher/TheDasherPost.asset, so re-running this
        // fork's builder can never delete or corrupt the original scene's post-processing profile.
        static VolumeProfile CreatePostProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TheDasherArt"))
                AssetDatabase.CreateFolder("Assets", "TheDasherArt");
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
            // Halved from 0.12: it was darkening exactly the frame corners where the
            // backlit treeline already falls away to near-black on device.
            vignette.intensity.Override(0.06f);
            vignette.smoothness.Override(0.8f);

            var colors = AddOverride<ColorAdjustments>(profile);
            // Saturation went +6 -> -4 when real foliage textures replaced the flat-colour
            // primitives, because +6 pushed the near leaves to a neon lime. -4 overcorrected:
            // on device the park reads DRY and joyless, which is the opposite failure. +8 is
            // the middle - enough to make a sunny park look sunny, short of the neon.
            colors.saturation.Override(11f);
            // Contrast eased 3 -> 2 so the exposure lift below is not immediately re-crushed
            // back into the shadows.
            colors.contrast.Override(2f);
            // Was -0.05 to stop the sunlit canopy clipping. With Neutral tonemapping and the
            // lower bloom threshold there is headroom, and the scene simply read dark.
            // 0.20 in the headless render, +0.08 of deliberate headroom on top: the device has
            // consistently read DARKER than the editor render, and this scene has now been called
            // too dark twice.
            colors.postExposure.Override(0.28f);

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
