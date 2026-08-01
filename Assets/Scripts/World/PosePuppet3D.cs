using System.Collections.Generic;
using UnityEngine;

namespace Kinex.World
{
    /// <summary>
    /// A procedural 3D stick-figure "puppet" driven directly by MediaPipe landmark points —
    /// a faithful port of the reference web app's ThreeCanvas.tsx. Each tracked joint becomes a
    /// sphere placed at a raw 3D point built from the landmark's image-space x,y,z; each bone is a
    /// cylinder spanning two joints. The body TURNS naturally because the landmark z (depth) moves
    /// the points — no humanoid rig, no IK, no yaw math, no calibration.
    ///
    /// Lives on a GameObject parented under the scene's player anchor; everything is built in LOCAL
    /// space so the anchor's transform handles where the figure sits in the camera framing.
    /// </summary>
    public class PosePuppet3D : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The pose detector supplying Landmarks33 (lives on the old player model in the scene).")]
        public MediaPipePoseDetector detector;

        [Header("Landmark → 3D mapping (reference: scale 11/8/8, yOffset 2.4)")]
        public float scaleX = 11f;
        public float scaleY = 8f;
        public float scaleZ = 8f;
        public float yOffset = 0f;     // anchor handles vertical placement; tune if the figure floats
        [Tooltip("A joint only updates while this visible; below it the joint holds its rest pose.")]
        [Range(0f, 1f)] public float visibilityThreshold = 0.45f;
        [Tooltip("Depth (z) multiplier for the LEGS only. MediaPipe leg depth is very noisy and makes " +
                 "the lower legs flip back/forward. Lower = flatter, steadier legs. 1 = full reference depth.")]
        [Range(0f, 1f)] public float legDepthScale = 0.15f;

        [Header("Look")]
        public float jointRadius = 0.18f;
        public float headRadius = 0.45f;
        public float boneThickness = 0.12f;
        public Color jointColor = new Color(0f, 1f, 0.8f);   // neon teal
        public Color boneColor = new Color(1f, 0f, 1f);      // magenta
        public Color headColor = Color.white;

        // MediaPipe joint ids we track (same set as the reference).
        static readonly int[] TrackedIds = { 0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28 };

        // Bone pairs (parent, child) — copied from the reference's bones[] list.
        static readonly int[,] Bones =
        {
            {11, 12},            // shoulders
            {11, 13}, {13, 15},  // left arm
            {12, 14}, {14, 16},  // right arm
            {11, 23}, {12, 24},  // torso sides
            {23, 24},            // hips
            {23, 25}, {25, 27},  // left leg
            {24, 26}, {26, 28},  // right leg
        };

        // Rest fallback (reference restPositions), used when a joint isn't visible.
        static readonly Dictionary<int, Vector3> Rest = new()
        {
            { 0,  new Vector3( 0.0f, 3.8f, 0f) },
            { 11, new Vector3(-0.9f, 3.1f, 0f) }, { 12, new Vector3( 0.9f, 3.1f, 0f) },
            { 13, new Vector3(-1.4f, 2.3f, 0f) }, { 14, new Vector3( 1.4f, 2.3f, 0f) },
            { 15, new Vector3(-1.6f, 1.6f, 0.2f) }, { 16, new Vector3( 1.6f, 1.6f, 0.2f) },
            { 23, new Vector3(-0.5f, 1.8f, 0f) }, { 24, new Vector3( 0.5f, 1.8f, 0f) },
            { 25, new Vector3(-0.6f, 1.0f, 0f) }, { 26, new Vector3( 0.6f, 1.0f, 0f) },
            { 27, new Vector3(-0.6f, 0.18f, 0f) }, { 28, new Vector3( 0.6f, 0.18f, 0f) },
        };

        readonly Dictionary<int, Transform> _joints = new();
        readonly List<Transform> _boneXf = new();
        Transform _chest, _neck;

        // Current local points (for the scorer). _valid[id] = the joint was placed this frame.
        readonly Vector3[] _pts = new Vector3[33];
        readonly bool[] _valid = new bool[33];

        void Start() => Build();

        void Build()
        {
            var jointMat = MakeMat(jointColor);
            var boneMat = MakeMat(boneColor);
            var headMat = MakeMat(headColor);

            foreach (int id in TrackedIds)
            {
                bool isHead = id == 0;
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "joint_" + id;
                Strip(s);
                s.transform.SetParent(transform, false);
                float r = isHead ? headRadius : jointRadius;
                s.transform.localScale = Vector3.one * (r * 2f);   // primitive sphere is unit-diameter
                s.GetComponent<Renderer>().sharedMaterial = isHead ? headMat : jointMat;
                _joints[id] = s.transform;
            }

            for (int i = 0; i < Bones.GetLength(0); i++)
                _boneXf.Add(NewBone("bone_" + i, boneMat));
            _chest = NewBone("chest", boneMat);
            _neck = NewBone("neck", boneMat);
        }

        Transform NewBone(string name, Material mat)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = name;
            Strip(c);
            c.transform.SetParent(transform, false);
            c.GetComponent<Renderer>().sharedMaterial = mat;
            return c.transform;
        }

        void Update()
        {
            if (detector == null) return;
            var lm = detector.Landmarks33;
            bool hasPose = detector.HasPose && lm != null && lm.Length >= 33;

            System.Array.Clear(_valid, 0, _valid.Length);

            // 1. Place each tracked joint at its 3D point (or rest if not visible) — EXACT reference
            //    recipe: map includes yOffset, then clamp so nothing sinks below the floor.
            foreach (int id in TrackedIds)
            {
                Vector3 p = (hasPose && lm[id].visibility > visibilityThreshold)
                    ? Map(lm[id])
                    : (Rest.TryGetValue(id, out var rp) ? rp : Vector3.zero);
                // Flatten the noisy leg depth so the lower legs stop flipping back/forward.
                if (id == 25 || id == 26 || id == 27 || id == 28) p.z *= legDepthScale;
                if (p.y < 0.1f) p.y = 0.1f;
                _joints[id].localPosition = p;
                _pts[id] = p;
                _valid[id] = true;
            }

            // 2. Stretch each bone between its two joints.
            for (int i = 0; i < _boneXf.Count; i++)
                Orient(_boneXf[i], _pts[Bones[i, 0]], _pts[Bones[i, 1]]);

            // 3. Chest (hip-mid → shoulder-mid) and neck (shoulder-mid → nose).
            Vector3 shMid = (_pts[11] + _pts[12]) * 0.5f;
            Vector3 hipMid = (_pts[23] + _pts[24]) * 0.5f;
            Orient(_chest, hipMid, shMid);
            Orient(_neck, shMid, _pts[0]);
        }

        // Reference mapping (ThreeCanvas.tsx): mirror X, flip Y + yOffset, depth on Z (makes it turn).
        Vector3 Map(MediaPipePoseDetector.NormLandmark l) => new Vector3(
            (0.5f - l.x) * scaleX,
            (0.5f - l.y) * scaleY + yOffset,
            -l.z * scaleZ);

        // Position a cylinder at the midpoint of A→B, scaled to its length, pointing along it.
        // Unity's primitive cylinder is 2 units tall, so localScale.y = length / 2.
        void Orient(Transform bone, Vector3 a, Vector3 b)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-4f) { bone.gameObject.SetActive(false); return; }
            bone.gameObject.SetActive(true);
            bone.localPosition = (a + b) * 0.5f;
            bone.localScale = new Vector3(boneThickness, len * 0.5f, boneThickness);
            bone.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        }

        /// <summary>Latest local 3D point for a MediaPipe joint id (placed this frame). For the scorer.</summary>
        public bool TryGetPoint(int mpId, out Vector3 p)
        {
            if (mpId >= 0 && mpId < 33 && _valid[mpId]) { p = _pts[mpId]; return true; }
            p = Vector3.zero; return false;
        }

        static Material MakeMat(Color c)
        {
            // Unlit so the figure renders at full, flat colour against the dark studio backdrop
            // regardless of scene lighting — matches the reference's bright neon look.
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        // Drop the auto-added primitive collider (we only want visuals).
        static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
    }
}
