#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Kinex.World;

namespace Kinex.World.EditorTools
{
    /// <summary>
    /// Headless one-click setup for the Kinex World 3D turning puppet. Creates a "PlayerPuppet"
    /// GameObject under the player anchor, adds PosePuppet3D, wires the scene's pose detector, then
    /// hides the old humanoid player model (disables its renderers + the detector's avatar driving).
    /// Re-runnable, no dialogs, batchmode-safe — same pattern as KinexWorldMusicSetup. THROWAWAY:
    /// delete before any final commit.
    /// </summary>
    public static class WorldPuppetBuilder
    {
        const string ScenePath = "Assets/Scenes/KinexWorldScene.unity";
        const string PuppetName = "PlayerPuppet";

        [MenuItem("Kinex/Build World 3D Puppet")]
        public static void Build()
        {
            Debug.Log("[WorldPuppetBuilder] " + Run());
        }

        // Throwaway diagnostic: prints camera + trainer + old-model + puppet world transforms/bounds so
        // we can place the puppet where the camera is actually looking. No rebuild needed.
        [MenuItem("Kinex/Diagnose World Puppet")]
        public static void Diagnose()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cam = Object.FindAnyObjectByType<Camera>();
            if (cam != null)
                Debug.Log($"[Diag] Camera '{cam.name}' pos={cam.transform.position} fwd={cam.transform.forward} fov={cam.fieldOfView} ortho={cam.orthographic}");

            var detector = Object.FindAnyObjectByType<MediaPipePoseDetector>();
            if (detector != null)
            {
                var t = detector.transform;
                Debug.Log($"[Diag] OldModel '{t.name}' worldPos={t.position} lossyScale={t.lossyScale} parent='{t.parent?.name}'");
                LogBounds("OldModel", detector.gameObject);
            }

            var trainer = GameObject.Find("Trainer_Body (Instance)") ?? GameObject.Find("Trainer_Body");
            if (trainer != null) { Debug.Log($"[Diag] Trainer worldPos={trainer.transform.position} lossyScale={trainer.transform.lossyScale}"); LogBounds("Trainer", trainer); }

            var puppet = GameObject.Find(PuppetName);
            if (puppet != null)
                Debug.Log($"[Diag] Puppet worldPos={puppet.transform.position} lossyScale={puppet.transform.lossyScale} children={puppet.transform.childCount}");
            else
                Debug.Log("[Diag] Puppet NOT FOUND in scene.");
        }

        static void LogBounds(string label, GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.Log($"[Diag] {label}: no renderers"); return; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"[Diag] {label} bounds center={b.center} size={b.size} (renderers={rs.Length})");
        }

        // Far-away "studio" so the puppet has the reference's own clean head-on view, untouched by
        // the room's angled game camera (the room camera never looks 1000 units away).
        static readonly Vector3 StudioPos = new Vector3(1000f, 0f, 1000f);

        public static string Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                return $"Could not open scene at {ScenePath}.";

            var detector = Object.FindAnyObjectByType<MediaPipePoseDetector>();
            if (detector == null)
                return "No MediaPipePoseDetector found in the open scene.";

            // 1. Studio root far from the room.
            var studio = GameObject.Find("PuppetStudio") ?? new GameObject("PuppetStudio");
            studio.transform.position = StudioPos;

            // 2. The puppet, at the studio origin, at the reference's NATIVE size (scale 1) and EXACT
            //    reference mapping values — it's framed by its own camera, not the room.
            var puppetGo = GameObject.Find(PuppetName) ?? new GameObject(PuppetName);
            puppetGo.transform.SetParent(studio.transform, false);
            puppetGo.transform.localPosition = Vector3.zero;
            puppetGo.transform.localRotation = Quaternion.identity;
            puppetGo.transform.localScale = Vector3.one;

            var puppet = puppetGo.GetComponent<PosePuppet3D>();
            if (puppet == null) puppet = puppetGo.AddComponent<PosePuppet3D>();
            puppet.detector = detector;
            // EXACT reference values (ThreeCanvas.tsx). Set explicitly so re-runs apply them.
            puppet.scaleX = 11f; puppet.scaleY = 8f; puppet.yOffset = 2.4f;
            // Reference scaleZ=8 looms the head + scrambles the body on this close perspective camera
            // (nose lands ~4u in front of a camera only 8u away). 3.5 keeps it clean and still turns.
            puppet.scaleZ = 3.5f;
            puppet.jointRadius = 0.18f; puppet.headRadius = 0.45f; puppet.boneThickness = 0.08f;
            puppet.legDepthScale = 0.15f; // flatten noisy leg depth so legs don't flip back

            // 3. Dedicated head-on camera (reference: pos (0,3,8) lookAt (0,2.5,0)), dark backdrop,
            //    renders only the studio (nothing else is out here). Renders to a RenderTexture.
            var camGo = GameObject.Find("PuppetCam") ?? new GameObject("PuppetCam");
            camGo.transform.SetParent(studio.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 3f, 8f);
            camGo.transform.LookAt(StudioPos + new Vector3(0f, 2.5f, 0f), Vector3.up);
            var cam = camGo.GetComponent<Camera>();
            if (cam == null) cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f); // reference grid bg
            cam.fieldOfView = 60f;       // reference camera fov
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;      // can't reach back to the room
            cam.tag = "Untagged";        // not the MainCamera

            // 4. UI panel showing the camera, on the HUD canvas.
            Canvas canvas = null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.renderMode != RenderMode.WorldSpace) { canvas = c.rootCanvas; break; }
            if (canvas == null) return "No screen-space Canvas found to host the puppet view.";

            var panel = GameObject.Find("PuppetViewImage");
            if (panel == null)
            {
                panel = new GameObject("PuppetViewImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                panel.transform.SetParent(canvas.transform, false);
            }
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(480f, 640f);   // 3:4, matches the 600x800 RT
            prt.anchoredPosition = new Vector2(0f, 60f);
            var rawImg = panel.GetComponent<RawImage>();
            rawImg.raycastTarget = false;              // don't block buttons under it

            var view = panel.GetComponent<PuppetView>();
            if (view == null) view = panel.AddComponent<PuppetView>();
            view.puppetCamera = cam;
            view.targetImage = rawImg;

            // 5. Keep the old humanoid model hidden + not driving (it stays only as the detector host).
            int hidden = 0;
            foreach (var r in detector.gameObject.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; hidden++; }
            var dso = new SerializedObject(detector);
            var drives = dso.FindProperty("drivesAvatar");
            if (drives != null) drives.boolValue = false;
            dso.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(detector);
            EditorUtility.SetDirty(puppetGo);
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            return $"PuppetStudio @ {StudioPos}: puppet (native scale) + PuppetCam (head-on) → PuppetViewImage panel " +
                   $"on '{canvas.name}'; hid {hidden} old renderer(s), drivesAvatar=false. Scene saved.";
        }
    }
}
#endif
