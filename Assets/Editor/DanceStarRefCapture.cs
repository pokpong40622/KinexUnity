#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Kinex.Trainer;

namespace Kinex.DanceStar.EditorTools
{
    /// <summary>
    /// Editor tool: renders one clean front-facing PNG per SUPERSTAR STAGE pose into
    /// Assets/Resources/DanceStarRef/&lt;poseName&gt;.png (transparent background), for the "ทำท่านี้"
    /// reference figure shown top-left in the redesigned 2D screen. Keyed BY POSE NAME (not index)
    /// so DanceStarDirector loads it directly from card.poseAssetName.
    ///
    /// Self-contained (does NOT need an open scene): instantiates NewTrainerAnimated.fbx, loads
    /// DancePoseData.asset, poses the rig directly from the baked data (same technique as
    /// Kinex.MegaDance.EditorTools.PosePreviewCapture), and captures with a temporary orthographic
    /// camera that sees only the character. Runs in edit mode / batch — no Play needed.
    ///
    /// Batch: -executeMethod Kinex.DanceStar.EditorTools.DanceStarRefCapture.Run
    /// </summary>
    public static class DanceStarRefCapture
    {
        const string OutDir = "Assets/Resources/DanceStarRef";
        // DancePoseData poses only reproduce on THIS exact rig (memory: trainer_pose_rig_pairing).
        const string TrainerRigPath = "Assets/Characters/NewTrainerAnimated.fbx";
        const string DancePoseDataPath = "Assets/Animations/DancePoseData.asset";
        const int Width = 512, Height = 1024;     // portrait figure card
        const int CaptureLayer = 31;              // isolate the character for a clean alpha bg
        const float Padding = 1.18f;              // breathing room around the figure

        [MenuItem("Kinex/Capture Dance Star Reference Figures")]
        public static void Capture() => Do(false);

        public static void Run() => Do(true);

        static void Do(bool batch)
        {
            var trainerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrainerRigPath);
            var poseData = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(DancePoseDataPath);
            if (trainerPrefab == null) { Fail($"Trainer rig not found at {TrainerRigPath}", batch); return; }
            if (poseData == null || poseData.poses == null || poseData.poses.Length == 0 ||
                poseData.boneNames == null)
            { Fail($"DancePoseData missing/empty at {DancePoseDataPath}", batch); return; }

            // Object.Instantiate (not PrefabUtility) — the proven pattern for spawning a
            // TrainerPoseData-driven rig (see DanceStarSceneBuilder / MirrorOutline / PoseGhost).
            var root = Object.Instantiate(trainerPrefab);
            root.name = "~DanceStarRefRig";
            // A fresh identity NewTrainerAnimated faces world -X; -90° yaw turns its front to -Z,
            // toward the capture camera (same fact DanceStarSceneBuilder relies on).
            root.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            root.transform.position = Vector3.zero;

            Transform rigRoot = root.transform;

            // Map bone names → transforms (same resolution the controller does at runtime).
            var map = new Dictionary<string, Transform>();
            foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map[t.name] = t;
            int bn = poseData.boneNames.Length;
            var bones = new Transform[bn];
            for (int i = 0; i < bn; i++) map.TryGetValue(poseData.boneNames[i], out bones[i]);

            var hierarchy = rigRoot.GetComponentsInChildren<Transform>(true);
            foreach (var t in hierarchy) t.gameObject.layer = CaptureLayer;

            // Disable Animators so the Humanoid avatar pass can't re-impose its default A-pose over
            // the bone rotations we set manually.
            foreach (var anim in rigRoot.GetComponentsInChildren<Animator>(true)) anim.enabled = false;

            // Force skinned meshes to recompute bone matrices on every render — a tight synchronous
            // capture loop otherwise renders each pose with the previous pose's skin.
            var skins = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in skins) { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

            // Kill any stray FBX light/camera so they can't leak into the isolate-layer render.
            foreach (var stray in rigRoot.GetComponentsInChildren<Light>(true)) stray.enabled = false;
            foreach (var strayCam in rigRoot.GetComponentsInChildren<Camera>(true)) strayCam.enabled = false;

            var camGo = new GameObject("~DanceStarRefCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << CaptureLayer;
            cam.allowHDR = false;

            var lightGo = new GameObject("~DanceStarRefLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightGo.transform.rotation = Quaternion.Euler(32f, 200f, 0f); // front-ish key

            var rends = rigRoot.GetComponentsInChildren<Renderer>();
            Directory.CreateDirectory(OutDir);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            int n = poseData.poses.Length;
            var written = new List<string>();
            try
            {
                for (int p = 0; p < n; p++)
                {
                    var pose = poseData.poses[p];
                    if (string.IsNullOrEmpty(pose.name)) continue;

                    for (int i = 0; i < bn; i++)
                    {
                        if (bones[i] == null) continue;
                        if (pose.localRotations != null && i < pose.localRotations.Length)
                            bones[i].localRotation = pose.localRotations[i];
                        if (pose.localPositions != null && i < pose.localPositions.Length)
                            bones[i].localPosition = pose.localPositions[i];
                    }

                    // Bake once to force the deformation to update for this pose.
                    foreach (var s in skins) { var bm = new Mesh(); s.BakeMesh(bm, true); Object.DestroyImmediate(bm); }

                    // Frame the posed figure. Rig now faces -Z, so its FRONT is on the -Z side:
                    // put the camera there looking toward +Z.
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

                    string path = $"{OutDir}/{pose.name}.png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    written.Add(path);
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(root);
            }

            AssetDatabase.Refresh();
            foreach (var path in written) ImportAsSprite(path);
            AssetDatabase.Refresh();

            Debug.Log($"[DanceStarRefCapture] Wrote {written.Count} reference figures to {OutDir}.");
            if (batch && Application.isBatchMode) EditorApplication.Exit(0);
            else if (!batch)
                EditorUtility.DisplayDialog("Dance Star Reference Figures",
                    $"Captured {written.Count} figures to\n{OutDir}", "OK");
        }

        static void Fail(string message, bool batch)
        {
            Debug.LogError($"[DanceStarRefCapture] {message}");
            if (batch && Application.isBatchMode) EditorApplication.Exit(1);
            else if (!batch) EditorUtility.DisplayDialog("Dance Star Reference Figures", message, "OK");
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
