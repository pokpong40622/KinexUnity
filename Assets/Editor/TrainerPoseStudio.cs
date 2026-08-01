#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Kinex.Trainer;

namespace Kinex.MegaDance.EditorTools
{
    /// <summary>
    /// Trainer Pose Studio — author the MegaDance trainer poses ONE AT A TIME on the
    /// real game rig, in edit mode (no Play).
    ///
    /// Loop per pose:
    ///   1. Load pose N        → applies it to the live rig (Animator off) so you see it.
    ///   2. Select a bone      → click a bone button; rotate it in Scene view (E) or nudge below.
    ///   3. Nudge ±degrees     → precise tweaks on the selected bone (local axes).
    ///   4. Capture front+side → writes _posestudio/preview_front.png & preview_side.png.
    ///   5. Save rig → pose N  → bakes the current rig back into TrainerPoseData.
    ///
    /// Open MegaDanceScene first (needs a TrainerPoseController in the scene).
    /// </summary>
    public class TrainerPoseStudio : EditorWindow
    {
        const int Width = 512, Height = 1024;
        const int CaptureLayer = 31;
        const float Padding = 1.20f;
        static readonly string OutDir =
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_posestudio");

        TrainerPoseController _controller;
        TrainerPoseData _poseData;
        Transform _rigRoot;
        readonly Dictionary<string, Transform> _bones = new();

        int _poseIndex;
        string _boneFilter = "";
        float _step = 5f;
        Vector2 _boneScroll;
        bool _animatorsDisabled;

        [MenuItem("Kinex/Trainer Pose Studio")]
        public static void Open() => GetWindow<TrainerPoseStudio>("Pose Studio");

        void OnEnable() { Bind(); }

        void Bind()
        {
            _controller = FindAnyObjectByType<TrainerPoseController>();
            _bones.Clear();
            _poseData = null;
            _rigRoot = null;
            if (_controller == null) return;

            var so = new SerializedObject(_controller);
            _poseData = so.FindProperty("poseData").objectReferenceValue as TrainerPoseData;
            var rr = so.FindProperty("rigRoot").objectReferenceValue as Transform;
            _rigRoot = rr != null ? rr : _controller.transform;

            foreach (var t in _rigRoot.GetComponentsInChildren<Transform>(true))
                if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
        }

        void OnGUI()
        {
            if (_controller == null || _poseData == null)
            {
                EditorGUILayout.HelpBox(
                    "Open MegaDanceScene (needs a TrainerPoseController with TrainerPoseData).",
                    MessageType.Warning);
                if (GUILayout.Button("Find controller")) Bind();
                return;
            }

            int count = _poseData.poses != null ? _poseData.poses.Length : 0;
            EditorGUILayout.LabelField("Rig", _rigRoot.name + $"   ({_bones.Count} bones, {count} poses)");
            EditorGUILayout.Space(4);

            // ── Pose select ─────────────────────────────────────────────
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Pose", EditorStyles.boldLabel);
                if (count > 0)
                {
                    _poseIndex = Mathf.Clamp(_poseIndex, 0, count - 1);
                    _poseIndex = EditorGUILayout.IntSlider($"Pose # (1..{count})", _poseIndex + 1, 1, count) - 1;
                    EditorGUILayout.LabelField("Name", _poseData.poses[_poseIndex].name ?? "");
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(count == 0))
                        if (GUILayout.Button("Load pose → rig")) LoadPose(_poseIndex);
                    if (GUILayout.Button("New pose (append)")) AppendPose();
                }
                _animatorsDisabled = EditorGUILayout.ToggleLeft(
                    "Animator disabled (pose mode)", _animatorsDisabled);
            }

            // ── Bone select ─────────────────────────────────────────────
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Select a bone (then rotate in Scene view or nudge below)",
                    EditorStyles.boldLabel);
                _boneFilter = EditorGUILayout.TextField("Filter", _boneFilter);
                _boneScroll = EditorGUILayout.BeginScrollView(_boneScroll, GUILayout.Height(150));
                foreach (var name in _poseData.boneNames)
                {
                    if (!string.IsNullOrEmpty(_boneFilter) &&
                        name.IndexOf(_boneFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool sel = Selection.activeTransform != null && Selection.activeTransform.name == name;
                    if (GUILayout.Toggle(sel, name, "Button") && !sel)
                        if (_bones.TryGetValue(name, out var t)) Selection.activeTransform = t;
                }
                EditorGUILayout.EndScrollView();
            }

            // ── Nudge selected bone ─────────────────────────────────────
            using (new EditorGUILayout.VerticalScope("box"))
            {
                var t = Selection.activeTransform;
                EditorGUILayout.LabelField("Nudge selected: " + (t != null ? t.name : "—"),
                    EditorStyles.boldLabel);
                _step = EditorGUILayout.FloatField("Step (°)", _step);
                using (new EditorGUI.DisabledScope(t == null))
                {
                    NudgeRow("Pitch (X)", Vector3.right);
                    NudgeRow("Yaw   (Y)", Vector3.up);
                    NudgeRow("Roll  (Z)", Vector3.forward);
                }
            }

            // ── Capture / Save ──────────────────────────────────────────
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (GUILayout.Button("📸  Capture front + side", GUILayout.Height(30)))
                    CaptureFrontSide();
                using (new EditorGUI.DisabledScope(count == 0))
                    if (GUILayout.Button($"💾  Save rig → pose {_poseIndex + 1}", GUILayout.Height(30)))
                        SaveRigToPose(_poseIndex);
            }
        }

        void NudgeRow(string label, Vector3 axis)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(80));
                if (GUILayout.Button("−")) Nudge(axis, -_step);
                if (GUILayout.Button("+")) Nudge(axis, _step);
            }
        }

        void Nudge(Vector3 localAxis, float degrees)
        {
            var t = Selection.activeTransform;
            if (t == null) return;
            Undo.RecordObject(t, "Nudge bone");
            t.localRotation = t.localRotation * Quaternion.AngleAxis(degrees, localAxis);
        }

        // ── Apply a stored pose to the live rig ─────────────────────────
        void LoadPose(int index)
        {
            DisableAnimators();
            var pose = _poseData.poses[index];
            for (int i = 0; i < _poseData.boneNames.Length; i++)
            {
                if (!_bones.TryGetValue(_poseData.boneNames[i], out var t) || t == null) continue;
                Undo.RecordObject(t, "Load pose");
                t.localRotation = pose.localRotations[i];
                t.localPosition = pose.localPositions[i];
            }
            _poseIndex = index;
        }

        void AppendPose()
        {
            DisableAnimators();
            int bn = _poseData.boneNames.Length;
            var pose = new TrainerPoseData.Pose
            {
                name = $"Pose {(_poseData.poses?.Length ?? 0) + 1}",
                localRotations = new Quaternion[bn],
                localPositions = new Vector3[bn],
            };
            for (int i = 0; i < bn; i++)
            {
                _bones.TryGetValue(_poseData.boneNames[i], out var t);
                pose.localRotations[i] = t != null ? t.localRotation : Quaternion.identity;
                pose.localPositions[i] = t != null ? t.localPosition : Vector3.zero;
            }
            var list = new List<TrainerPoseData.Pose>(_poseData.poses ?? new TrainerPoseData.Pose[0]) { pose };
            Undo.RecordObject(_poseData, "Append pose");
            _poseData.poses = list.ToArray();
            EditorUtility.SetDirty(_poseData);
            AssetDatabase.SaveAssets();
            _poseIndex = _poseData.poses.Length - 1;
        }

        // ── Bake the current live rig back into pose N ──────────────────
        void SaveRigToPose(int index)
        {
            if (!EditorUtility.DisplayDialog("Save pose",
                $"Overwrite pose {index + 1} (\"{_poseData.poses[index].name}\") with the current rig?",
                "Save", "Cancel")) return;

            var pose = _poseData.poses[index];
            for (int i = 0; i < _poseData.boneNames.Length; i++)
            {
                if (!_bones.TryGetValue(_poseData.boneNames[i], out var t) || t == null) continue;
                pose.localRotations[i] = t.localRotation;
                pose.localPositions[i] = t.localPosition;
            }
            Undo.RecordObject(_poseData, "Save pose");
            EditorUtility.SetDirty(_poseData);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PoseStudio] Saved current rig → pose {index + 1} (\"{pose.name}\").");
        }

        void DisableAnimators()
        {
            foreach (var a in _rigRoot.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            _animatorsDisabled = true;
        }

        // ── Capture front + right-side PNGs of whatever is on the rig now ──
        void CaptureFrontSide()
        {
            Directory.CreateDirectory(OutDir);
            // Front: rig faces -Z, so the camera sits on the -Z side looking +Z.
            CaptureOrtho(new Vector3(0, 0, -1), Vector3.forward, Path.Combine(OutDir, "preview_front.png"));
            // Side: camera on +X looking toward -X (right-hand profile).
            CaptureOrtho(new Vector3(1, 0, 0), Vector3.left, Path.Combine(OutDir, "preview_side.png"));
            Debug.Log($"[PoseStudio] Captured front + side to {OutDir}");
        }

        void CaptureOrtho(Vector3 dirFromCenter, Vector3 lookDir, string path)
        {
            var hierarchy = _rigRoot.GetComponentsInChildren<Transform>(true);
            var origLayers = new int[hierarchy.Length];
            for (int i = 0; i < hierarchy.Length; i++)
            { origLayers[i] = hierarchy[i].gameObject.layer; hierarchy[i].gameObject.layer = CaptureLayer; }

            var skins = _rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in skins)
            { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; var bm = new Mesh(); s.BakeMesh(bm, true); Object.DestroyImmediate(bm); }

            var camGo = new GameObject("~PoseStudioCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << CaptureLayer;
            cam.allowHDR = false;

            var lightGo = new GameObject("~PoseStudioLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up) * Quaternion.Euler(35f, 20f, 0f);

            var rends = _rigRoot.GetComponentsInChildren<Renderer>();
            var b = new Bounds(rends.Length > 0 ? rends[0].bounds.center : Vector3.zero, Vector3.zero);
            foreach (var r in rends) b.Encapsulate(r.bounds);

            float perp = Mathf.Max(b.extents.x, b.extents.z);
            cam.orthographicSize = Mathf.Max(b.extents.y, perp / cam.aspect) * Padding;
            float dist = b.size.magnitude + 5f;
            camGo.transform.position = b.center + dirFromCenter.normalized * dist;
            camGo.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());

            // Restore.
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            foreach (var s in skins) s.forceMatrixRecalculationPerRender = false;
            for (int i = 0; i < hierarchy.Length; i++) hierarchy[i].gameObject.layer = origLayers[i];
        }
    }
}
#endif
