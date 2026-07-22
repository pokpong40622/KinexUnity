using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Collapse;

namespace Kinex.QuakeEscape.EditorTools
{
    /// <summary>
    /// One-shot repair for the imported Quake Escape scene. The friend's
    /// Collapse.unitypackage was exported incomplete/oddly in two ways this fixes:
    ///
    ///  1. The player's visible avatar was a "KinexUserModel" PREFAB that was
    ///     never included in the package, so the Player object has scripts but no
    ///     mesh (invisible character). We substitute the project's own
    ///     Assets/Characters/KinexUserModel.fbx — CharacterPoseAnimator drives the
    ///     rig through HumanPoseHandler, so any *humanoid* model works, and ours is
    ///     humanoid. Re-wire CharacterPoseAnimator.animator to the new instance.
    ///
    ///  2. The sky material uses a built-in-RP skybox shader that URP renders as
    ///     flat magenta. Swap in a plain Skybox/Procedural sky so the scene reads
    ///     as an outdoor daytime environment instead of broken pink.
    ///
    /// Idempotent: safe to re-run (keys off a named child + a saved sky asset).
    /// </summary>
    public static class QuakeEscapeFixup
    {
        const string ScenePath = "Assets/QuakeEscape/QuakeEscapeScene.unity";
        const string ModelPath = "Assets/Characters/KinexUserModel.fbx";
        const string SkyPath = "Assets/QuakeEscape/QuakeSky.mat";
        const string ModelChildName = "PlayerAvatar";

        const string DetectorRigName = "PoseDetectionRig";
        const string DetectorAvatarName = "DetectorAvatar";

        [MenuItem("Kinex/Quake Escape/Fix Imported Scene")]
        public static void Fix()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            FixPlayerAvatar();
            FixSky();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[QEFix] Scene repaired and saved.");
        }

        /// <summary>
        /// Swaps the pose-detection engine to The Dasher's MediaPipePoseDetector (validated on
        /// device, handles the Android front-cam rotation the friend's LIVE_STREAM source did not).
        /// Adds a hidden humanoid rig carrying the detector + a DasherPoseSource that classifies its
        /// Landmarks33 into the three PoseStates, repoints PoseSourceMux.primarySource to it, and
        /// disables the friend's MediaPipePoseSource so only one webcam is opened. Idempotent.
        /// </summary>
        [MenuItem("Kinex/Quake Escape/Integrate Dasher Pose")]
        public static void IntegratePose()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var mux = Object.FindFirstObjectByType<PoseSourceMux>(FindObjectsInactive.Include);
            if (mux == null)
            {
                Debug.LogError("[QEFix] No PoseSourceMux in scene — cannot wire pose source.");
                return;
            }

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (fbx == null)
            {
                Debug.LogError("[QEFix] Missing " + ModelPath);
                return;
            }

            // Idempotent: reuse an existing rig if a prior run made one.
            var existing = GameObject.Find(DetectorRigName);
            GameObject rig = existing != null ? existing : new GameObject(DetectorRigName);
            // Park it well away from the play area; its renderers are disabled anyway.
            rig.transform.position = new Vector3(0f, -100f, 0f);

            GameObject avatar = null;
            var avatarTf = rig.transform.Find(DetectorAvatarName);
            if (avatarTf != null) avatar = avatarTf.gameObject;

            if (avatar == null)
            {
                avatar = (GameObject)PrefabUtility.InstantiatePrefab(fbx, rig.transform);
                avatar.name = DetectorAvatarName;
                avatar.transform.localPosition = Vector3.zero;
                avatar.transform.localRotation = Quaternion.identity;
                avatar.transform.localScale = Vector3.one;

                // Same stray-light strip as the gameplay avatar (this fbx carries an
                // intensity-1000 Light + a Camera that would wreck the scene).
                foreach (var l in avatar.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
                foreach (var c in avatar.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c.gameObject);
                // Hide it — it exists only to host the detector + supply a humanoid Animator.
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

                var anim = avatar.GetComponent<Animator>();
                if (anim == null) anim = avatar.AddComponent<Animator>();
                var srcAnim = fbx.GetComponent<Animator>();
                if (srcAnim != null && srcAnim.avatar != null) anim.avatar = srcAnim.avatar;
                anim.applyRootMotion = false;
            }

            // Detector lives on the avatar root (it does GetComponent<Animator>() + CacheTpose()).
            var detector = avatar.GetComponent<MediaPipePoseDetector>();
            if (detector == null) detector = avatar.AddComponent<MediaPipePoseDetector>();
            var dso = new SerializedObject(detector);
            SetBool(dso, "drivesAvatar", false);   // detection only — never puppet this hidden rig
            SetBool(dso, "autoCalibrate", false);   // T-pose calib is for avatar driving; unused here
            SetBool(dso, "debugLog", false);
            dso.ApplyModifiedProperties();

            // The classifier that turns Landmarks33 into PoseState.
            var source = rig.GetComponent<DasherPoseSource>();
            if (source == null) source = rig.AddComponent<DasherPoseSource>();
            var sso = new SerializedObject(source);
            var detProp = sso.FindProperty("detector");
            if (detProp != null) { detProp.objectReferenceValue = detector; sso.ApplyModifiedProperties(); }

            // Repoint the mux's primary source to our new classifier.
            var mso = new SerializedObject(mux);
            var prim = mso.FindProperty("primarySource");
            if (prim != null) { prim.objectReferenceValue = source; mso.ApplyModifiedProperties(); }

            // Silence the friend's original source so it doesn't fight for the webcam.
            foreach (var old in Object.FindObjectsByType<MediaPipePoseSource>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                old.enabled = false;
                EditorUtility.SetDirty(old);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[QEFix] Dasher pose detection integrated: PoseSourceMux.primary → DasherPoseSource; " +
                      "friend's MediaPipePoseSource disabled.");
        }

        static void SetBool(SerializedObject so, string prop, bool value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = value;
        }

        static void FixPlayerAvatar()
        {
            var pose = Object.FindFirstObjectByType<CharacterPoseAnimator>(FindObjectsInactive.Include);
            if (pose == null)
            {
                Debug.LogError("[QEFix] No CharacterPoseAnimator in scene — cannot place avatar.");
                return;
            }

            var host = pose.transform;
            if (host.Find(ModelChildName) != null)
            {
                Debug.Log("[QEFix] Player avatar already present — skipping.");
                return;
            }

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (fbx == null)
            {
                Debug.LogError("[QEFix] Missing " + ModelPath);
                return;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx, host);
            inst.name = ModelChildName;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            // 0.36 matched the friend's missing prefab-instance scale override.
            inst.transform.localScale = Vector3.one * 0.36f;

            // This fbx ships with an embedded intensity-1000 Light (and a Camera)
            // that otherwise blows the scene to white — strip both from the clone.
            foreach (var l in inst.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            foreach (var c in inst.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c.gameObject);

            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            var srcAnim = fbx.GetComponent<Animator>();
            if (srcAnim != null && srcAnim.avatar != null) anim.avatar = srcAnim.avatar;
            anim.applyRootMotion = false;

            var so = new SerializedObject(pose);
            var animProp = so.FindProperty("animator");
            if (animProp != null)
            {
                animProp.objectReferenceValue = anim;
                so.ApplyModifiedProperties();
            }
            Debug.Log("[QEFix] Player avatar substituted + animator re-wired.");
        }

        // HangGlider's daytime skybox (built-in "Skybox/6 Sided", shader fileID 104).
        // The friend's space skybox (fileID 103) and a Procedural sky (106) both
        // render flat magenta in this project's URP setup, while this 6-Sided one is
        // already proven to render correctly. Reuse it rather than fight the others.
        const string GoodSkyPath = "Assets/HangGlider/Skybox/6SidedFluffball.mat";

        static void FixSky()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(GoodSkyPath);
            if (sky == null)
            {
                Debug.LogError("[QEFix] Missing " + GoodSkyPath);
                return;
            }

            var cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;

            RenderSettings.skybox = sky;
            DampenReflections();
            DynamicGI.UpdateEnvironment();
            Debug.Log("[QEFix] Sky set to HangGlider's 6-Sided daytime skybox.");
        }

        // The DowntownCity buildings/cars/roads are reflective and the scene sources reflections
        // from the SKYBOX (m_DefaultReflectionMode=Skybox). With the bright fluffball sky swapped
        // in, every glossy surface now mirrors that bright sky → the "reflection too bright" the
        // user reported. The friend's dark space sky hid this. Turn the global reflection
        // contribution WAY down (1 → 0.2) so surfaces read as matte concrete, not chrome. Ambient
        // is Gradient mode (unaffected by this), so the scene stays lit — just not shiny.
        static void DampenReflections()
        {
            RenderSettings.reflectionIntensity = 0.2f;
        }

        const string ConcretePath = "Assets/QuakeEscape/ThirdParty/DowntownCity/Materials/MI_Concrete.mat";
        const string PreviewName = "CameraPreview";

        /// <summary>Fixes the two on-device bugs: (1) 25 skyline buildings ship with NULL material
        /// slots and render magenta (reads as a "purple sky"); fill every empty slot with the
        /// DowntownCity concrete material. (2) no RawImage exists, so the pose detector has nothing
        /// to show the camera on; add one to the HUD so the player can see themselves. Idempotent.</summary>
        [MenuItem("Kinex/Quake Escape/Fix Device Bugs (buildings + preview)")]
        public static void FixDeviceBugs()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            FixCityMaterials();
            DampenReflections();          // reflection-too-bright fix
            DynamicGI.UpdateEnvironment();
            AddCameraPreview();           // rebuilt bottom-right + skeleton overlay
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[QEFix] Device bugs fixed and saved.");
        }

        const string MenuScenePath = "Assets/QuakeEscape/QuakeEscapeMenuScene.unity";

        /// <summary>Makes the imported MainMenu scene render standalone. As authored, its MenuCamera
        /// clears DEPTH-only and its UI camera was meant to STACK over the gameplay camera (additive
        /// live-background trick). Embedded under Flutter that shows nothing, so switch the camera to a
        /// Base camera that clears to a solid colour — the scene's own MenuBackground art then fills
        /// the frame. Idempotent.</summary>
        [MenuItem("Kinex/Quake Escape/Fix Menu Scene (camera)")]
        public static void FixMenuScene()
        {
            var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

            int fixedCams = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
                EditorUtility.SetDirty(cam);
                fixedCams++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[QEFix] Menu scene camera(s) set to Base/SolidColor: {fixedCams}.");
        }

        static void FixCityMaterials()
        {
            var concrete = AssetDatabase.LoadAssetAtPath<Material>(ConcretePath);
            if (concrete == null) { Debug.LogError("[QEFix] Missing " + ConcretePath); return; }

            int fixedRenderers = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].shader == null ||
                        mats[i].shader.name == "Hidden/InternalErrorShader")
                    {
                        mats[i] = concrete;
                        changed = true;
                    }
                }
                if (changed)
                {
                    r.sharedMaterials = mats;
                    EditorUtility.SetDirty(r);
                    fixedRenderers++;
                }
            }
            Debug.Log($"[QEFix] Filled null/magenta material slots on {fixedRenderers} renderers with MI_Concrete.");
        }

        const string PreviewFrameName = "CameraPreviewFrame";

        static void AddCameraPreview()
        {
            var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null) { Debug.LogError("[QEFix] No Canvas for the camera preview."); return; }

            // Rebuild every run: the first attempt landed the preview dead-center (the detector's
            // Android quarter-turn re-anchors the RawImage to the CENTER of ITS PARENT). The fix is
            // to give the RawImage a small bottom-right PARENT frame — the detector then centres it
            // inside that small frame, so it reads as a small corner preview instead of covering the
            // play area. Tear down any prior CameraPreview/Frame so this is idempotent.
            var oldFrame = canvas.transform.Find(PreviewFrameName);
            if (oldFrame != null) Object.DestroyImmediate(oldFrame.gameObject);
            var oldLoose = canvas.transform.Find(PreviewName);
            if (oldLoose != null) Object.DestroyImmediate(oldLoose.gameObject);

            // Bottom-LEFT frame, ~19% wide × 21% tall (small). The detector re-anchors the RawImage to
            // the centre of THIS rect, so the preview stays pinned in the corner.
            var frame = new GameObject(PreviewFrameName, typeof(RectTransform));
            frame.transform.SetParent(canvas.transform, false);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = new Vector2(0.02f, 0.03f);
            frt.anchorMax = new Vector2(0.21f, 0.24f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            // The RawImage the MediaPipePoseDetector auto-finds (FindAnyObjectByType<RawImage>) and
            // assigns the live webcam to. Full-stretch of the frame; the detector crops/rotates it.
            var go = new GameObject(PreviewName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.RawImage));
            go.transform.SetParent(frame.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<UnityEngine.UI.RawImage>().color = Color.white;

            // Skeleton overlay so the player can SEE the AI is tracking them (the user reported "no
            // AI skeleton"). Full-stretch CHILD of the RawImage (per PoseSkeletonOverlay's setup doc)
            // so it inherits the preview's rotation/mirror; its own previewRotateCW/mirror land the
            // joints on the body. Wire it to the hidden detection rig's detector.
            var det = GetDetector();
            var ov = new GameObject("PoseSkeletonOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(PoseSkeletonOverlay));
            ov.transform.SetParent(go.transform, false);
            var ort = (RectTransform)ov.transform;
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = Vector2.zero;
            ort.offsetMax = Vector2.zero;
            var overlay = ov.GetComponent<PoseSkeletonOverlay>();
            overlay.source = det;
            overlay.raycastTarget = false;
            // The overlay is a CHILD of the RawImage, which the detector already rotates -90° and
            // mirrors at runtime (Android front cam). If the overlay ALSO rotates/mirrors in its own
            // normalized space, the two compound AND a rotation in [0,1]² scaled to a non-square rect
            // shears the skeleton into a squished horizontal blob (seen on device). Zero the overlay's
            // own rotate+mirror so it draws in the RawImage's upright local space and INHERITS the
            // image's on-screen transform — landing the joints straight on the body.
            var oso = new SerializedObject(overlay);
            var rotP = oso.FindProperty("previewRotateCW");
            if (rotP != null) rotP.intValue = 0;
            var mirP = oso.FindProperty("mirrorSkeletonX");
            if (mirP != null) mirP.boolValue = false;
            oso.ApplyModifiedProperties();
            if (det == null)
                Debug.LogWarning("[QEFix] No MediaPipePoseDetector found — skeleton overlay left unwired. Run 'Integrate Dasher Pose' first.");

            frame.transform.SetAsLastSibling(); // draw the corner preview OVER the gameplay
            Debug.Log("[QEFix] Camera preview rebuilt bottom-left (19%×21%) with skeleton overlay.");
        }

        static MediaPipePoseDetector GetDetector()
        {
            var rig = GameObject.Find(DetectorRigName);
            if (rig != null)
            {
                var d = rig.GetComponentInChildren<MediaPipePoseDetector>(true);
                if (d != null) return d;
            }
            return Object.FindFirstObjectByType<MediaPipePoseDetector>(FindObjectsInactive.Include);
        }

        /// <summary>Read-only: dumps what could be rendering magenta on device and the
        /// camera-preview state, so fixes aren't guesses. Log lines prefixed [QEDiag].</summary>
        [MenuItem("Kinex/Quake Escape/Diagnose Scene")]
        public static void Diagnose()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var sky = RenderSettings.skybox;
            Debug.Log($"[QEDiag] skybox={(sky ? sky.name : "NULL")} shader={(sky && sky.shader ? sky.shader.name : "NULL")} " +
                      $"ambientMode={RenderSettings.ambientMode} reflMode={RenderSettings.defaultReflectionMode}");
            var cam = Camera.main;
            Debug.Log($"[QEDiag] Camera.main={(cam ? cam.name : "NULL")} clearFlags={(cam ? cam.clearFlags.ToString() : "-")} " +
                      $"bg={(cam ? cam.backgroundColor.ToString() : "-")}");

            // Every renderer whose material shader is null or the magenta error shader.
            int bad = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var m in r.sharedMaterials)
                {
                    bool magenta = m == null || m.shader == null ||
                                   m.shader.name == "Hidden/InternalErrorShader";
                    if (magenta)
                    {
                        var b = r.bounds;
                        Debug.Log($"[QEDiag] MAGENTA/NULL-MAT: '{FullPath(r.transform)}' " +
                                  $"mat={(m ? m.name : "NULL")} shader={(m && m.shader ? m.shader.name : "NULL")} " +
                                  $"size={b.size} enabled={r.enabled}");
                        bad++;
                    }
                }
            }
            Debug.Log($"[QEDiag] renderers with magenta/null material: {bad}");

            // Big sky-ish renderers (huge bounds) even if their shader resolves — a giant
            // transparent plane can still read as a purple wall if its material is odd.
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var s = r.bounds.size;
                if (s.x > 200f || s.y > 200f || s.z > 200f)
                    Debug.Log($"[QEDiag] BIG: '{FullPath(r.transform)}' size={s} mat0={(r.sharedMaterial ? r.sharedMaterial.name : "NULL")} " +
                              $"shader={(r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "NULL")}");
            }

            var raws = Object.FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[QEDiag] RawImage count={raws.Length}");
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in canvases)
                Debug.Log($"[QEDiag] Canvas '{c.name}' renderMode={c.renderMode} active={c.gameObject.activeInHierarchy}");
        }

        static string FullPath(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
