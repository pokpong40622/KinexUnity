#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Kinex.Trainer;

namespace Kinex.MegaDance.EditorTools
{
    /// <summary>
    /// Editor tool: renders one front-facing PNG per trainer pose into
    /// Assets/Resources/PosePreviews/pose_01.png … pose_NN.png, on a transparent
    /// background, for use as the "next pose" preview on the FirstPose UI screen.
    ///
    /// Runs in edit mode (no Play needed). It poses the rig directly from TrainerPoseData
    /// (the controller only caches bones at runtime), captures with a temporary orthographic
    /// camera that sees only the character, then restores the rig and re-imports the PNGs as
    /// sprites. Re-run any time the poses change.
    /// </summary>
    public static class PosePreviewCapture
    {
        const string OutDir = "Assets/Resources/PosePreviews";
        const int Width = 512, Height = 1024;     // ~ the 300×810 Figma card ratio
        const int CaptureLayer = 31;              // isolate the character for a clean alpha bg
        const float Padding = 1.15f;              // breathing room around the figure

        [MenuItem("Kinex/Capture Pose Previews")]
        public static void Capture()
        {
            var controller = Object.FindAnyObjectByType<TrainerPoseController>();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Pose Previews",
                    "No TrainerPoseController in the open scene. Open MegaDanceScene first.", "OK");
                return;
            }

            // poseData / rigRoot are private [SerializeField] — read them via SerializedObject.
            var so = new SerializedObject(controller);
            var poseData = so.FindProperty("poseData").objectReferenceValue as TrainerPoseData;
            var rigRootProp = so.FindProperty("rigRoot").objectReferenceValue as Transform;
            Transform rigRoot = rigRootProp != null ? rigRootProp : controller.transform;

            if (poseData == null || poseData.poses == null || poseData.poses.Length == 0 ||
                poseData.boneNames == null)
            {
                EditorUtility.DisplayDialog("Pose Previews", "TrainerPoseData is missing or empty.", "OK");
                return;
            }

            // Map bone names → transforms (same resolution the controller does at runtime).
            var map = new Dictionary<string, Transform>();
            foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map[t.name] = t;
            int bn = poseData.boneNames.Length;
            var bones = new Transform[bn];
            for (int i = 0; i < bn; i++) map.TryGetValue(poseData.boneNames[i], out bones[i]);

            // Remember original local TRS + layers so we can fully restore afterwards.
            var origRot = new Quaternion[bn];
            var origPos = new Vector3[bn];
            for (int i = 0; i < bn; i++)
                if (bones[i] != null) { origRot[i] = bones[i].localRotation; origPos[i] = bones[i].localPosition; }

            var hierarchy = rigRoot.GetComponentsInChildren<Transform>(true);
            var origLayers = new int[hierarchy.Length];
            for (int i = 0; i < hierarchy.Length; i++) { origLayers[i] = hierarchy[i].gameObject.layer; hierarchy[i].gameObject.layer = CaptureLayer; }

            // Disable Animators so the Humanoid avatar pass can't re-impose its default A-pose
            // over the bone rotations we set manually.
            var animators = rigRoot.GetComponentsInChildren<Animator>(true);
            var animWasEnabled = new bool[animators.Length];
            for (int i = 0; i < animators.Length; i++) { animWasEnabled[i] = animators[i].enabled; animators[i].enabled = false; }

            // Force the skinned meshes to recompute bone matrices on every render. Without this,
            // a tight synchronous capture loop renders every pose with the previous pose's skin
            // (no editor tick happens between Render() calls to refresh the skinning).
            var skins = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in skins) { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

            // Temp camera (transparent clear) + key light, both culling only the character layer.
            var camGo = new GameObject("~PosePreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << CaptureLayer;
            cam.allowHDR = false;

            var lightGo = new GameObject("~PosePreviewLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 200f, 0f); // front-ish key

            var rends = rigRoot.GetComponentsInChildren<Renderer>();
            Directory.CreateDirectory(OutDir);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            int n = poseData.poses.Length;
            try
            {
                for (int p = 0; p < n; p++)
                {
                    // Apply pose p directly.
                    var pose = poseData.poses[p];
                    for (int i = 0; i < bn; i++)
                    {
                        if (bones[i] == null) continue;
                        bones[i].localRotation = pose.localRotations[i];
                        bones[i].localPosition = pose.localPositions[i];
                    }

                    // Bake the skinned mesh once to force the deformation to update for this pose.
                    foreach (var s in skins) { var bm = new Mesh(); s.BakeMesh(bm, true); Object.DestroyImmediate(bm); }

                    // Frame the posed figure. The rig faces -Z (follow-behind setup), so the
                    // FRONT is on the -Z side: put the camera there looking toward +Z.
                    Bounds b = ComputeBounds(rends);
                    cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x / cam.aspect) * Padding;
                    float dist = b.size.z + 5f;
                    camGo.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - dist);
                    camGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

                    cam.Render();

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;

                    File.WriteAllBytes($"{OutDir}/pose_{p + 1:00}.png", tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                // Restore everything.
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
                for (int i = 0; i < hierarchy.Length; i++) hierarchy[i].gameObject.layer = origLayers[i];
                for (int i = 0; i < animators.Length; i++) animators[i].enabled = animWasEnabled[i];
                foreach (var s in skins) s.forceMatrixRecalculationPerRender = false;
                for (int i = 0; i < bn; i++)
                    if (bones[i] != null) { bones[i].localRotation = origRot[i]; bones[i].localPosition = origPos[i]; }
            }

            AssetDatabase.Refresh();
            for (int p = 0; p < n; p++) ImportAsSprite($"{OutDir}/pose_{p + 1:00}.png");
            AssetDatabase.Refresh();

            Debug.Log($"[PosePreviewCapture] Wrote {n} pose previews to {OutDir}.");
            EditorUtility.DisplayDialog("Pose Previews", $"Captured {n} pose previews to\n{OutDir}", "OK");
        }

        static Bounds ComputeBounds(Renderer[] rends)
        {
            var b = new Bounds(rends.Length > 0 ? rends[0].bounds.center : Vector3.zero, Vector3.zero);
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        static void ImportAsSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
#endif
