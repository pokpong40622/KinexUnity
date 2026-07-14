using System.Collections.Generic;
using UnityEngine;

namespace Kinex.MirrorGame
{
    /// <summary>
    /// Mode-2 border: a "TV-show" wall of dark translucent cells filling the screen, with a
    /// pose-shaped HOLE punched through it. Purely procedural — a flat grid of quads at the ghost's
    /// depth; any cell whose centre lands within a capsule threshold of a ghost bone segment is
    /// switched off, leaving a person-shaped opening the player steps through. Rebuilt whenever the
    /// target pose changes. The zone circles (owned by MirrorOutline) stay visible in this mode too,
    /// so per-limb feedback is identical to the outline mode.
    /// </summary>
    public class MirrorWall : MonoBehaviour
    {
        const float CellSize = 0.17f;
        const float Width = 3.0f;
        const float Height = 2.4f;
        const float HoleThreshold = 0.22f; // metres from a bone segment that counts as "inside the body"

        // Bone-segment skeleton the hole is carved around (2D XY silhouette).
        static readonly (HumanBodyBones a, HumanBodyBones b)[] Segments =
        {
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
            (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
            (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm),   // shoulders
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg),   // hips
            (HumanBodyBones.Hips, HumanBodyBones.Neck),                    // torso midline
            (HumanBodyBones.Neck, HumanBodyBones.Head),                    // head/neck
        };

        MirrorOutline _outline;
        readonly List<Renderer> _cells = new List<Renderer>();
        readonly List<Vector2> _cellXY = new List<Vector2>();
        float _wallZ;

        static Material s_CellTemplate;

        public static MirrorWall Create(MirrorOutline outline, Vector3 center)
        {
            var go = new GameObject("MirrorWall");
            go.transform.position = center;
            var wall = go.AddComponent<MirrorWall>();
            wall._outline = outline;
            wall._wallZ = center.z + 0.1f; // just behind the avatar plane
            wall.BuildGrid(center);
            wall.SetVisible(false);
            return wall;
        }

        void BuildGrid(Vector3 center)
        {
            var template = CellTemplate();
            int cols = Mathf.CeilToInt(Width / CellSize);
            int rows = Mathf.CeilToInt(Height / CellSize);
            float x0 = center.x - Width * 0.5f;
            float y0 = center.y - Height * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float cx = x0 + (c + 0.5f) * CellSize;
                    float cy = y0 + (r + 0.5f) * CellSize;

                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "WallCell";
                    DestroyColliderSafe(quad);
                    quad.transform.SetParent(transform, false);
                    quad.transform.position = new Vector3(cx, cy, _wallZ);
                    quad.transform.localScale = new Vector3(CellSize * 0.98f, CellSize * 0.98f, 1f);
                    quad.GetComponent<Renderer>().sharedMaterial = template;

                    _cells.Add(quad.GetComponent<Renderer>());
                    _cellXY.Add(new Vector2(cx, cy));
                }
            }
        }

        /// <summary>Re-punch the hole for the ghost's CURRENT pose. Returns how many cells were
        /// carved out (used by the self-test to confirm a hole actually forms).</summary>
        public int Rebuild()
        {
            if (_outline == null) return 0;

            var segs = new (Vector2 a, Vector2 b)[Segments.Length];
            for (int i = 0; i < Segments.Length; i++)
            {
                Vector3 a = _outline.BonePos(Segments[i].a);
                Vector3 b = _outline.BonePos(Segments[i].b);
                segs[i] = (new Vector2(a.x, a.y), new Vector2(b.x, b.y));
            }

            int holes = 0;
            for (int i = 0; i < _cells.Count; i++)
            {
                bool inside = false;
                Vector2 p = _cellXY[i];
                foreach (var s in segs)
                {
                    if (DistToSegment(p, s.a, s.b) < HoleThreshold) { inside = true; break; }
                }
                if (_cells[i] != null) _cells[i].enabled = !inside; // hole = cell off
                if (inside) holes++;
            }
            return holes;
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        static void DestroyColliderSafe(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        static Material CellTemplate()
        {
            if (s_CellTemplate != null) return s_CellTemplate;
            // Fallback chain for stripped builds — see Editor/AlwaysIncludedShaders.cs.
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "MirrorWallCell" };
            var c = new Color(0.03f, 0.04f, 0.08f, 0.9f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            s_CellTemplate = mat;
            return mat;
        }
    }
}
