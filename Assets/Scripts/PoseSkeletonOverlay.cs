// PoseSkeletonOverlay — draws the live pose skeleton (lines + joints) over the camera
// preview in the game UI, like the 2D overlay in the "3d-pose-rigging" reference app.
//
// It's a UI Graphic: GPU-rendered line/joint quads rebuilt each frame from the detector's
// COCO-17 normalized keypoints, mapped through the same cover-crop the preview uses so the
// skeleton lands exactly on the body in the feed.
//
// SETUP: place on a full-stretch child of the camera preview RawImage, assign `source`.

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class PoseSkeletonOverlay : Graphic
{
    [Tooltip("The pose detector to read keypoints from.")]
    public MediaPipePoseDetector source;

    [Header("Style")]
    [Range(0f, 1f)] public float minVisibility = 0.4f;
    public float lineThickness = 6f;
    public float jointRadius = 7f;
    public Color lineColor = new Color(0f, 1f, 0.8f, 0.85f);
    public Color jointColor = new Color(1f, 0f, 0.8f, 1f);
    public Color headColor = new Color(1f, 0.85f, 0.2f, 1f);

    // COCO-17 indices — must match MediaPipePoseDetector's MP_TO_COCO mapping.
    const int NOSE = 0, L_SH = 5, R_SH = 6, L_EL = 7, R_EL = 8, L_WR = 9, R_WR = 10,
              L_HIP = 11, R_HIP = 12, L_KN = 13, R_KN = 14, L_AN = 15, R_AN = 16;

    static readonly int[,] CONNECTIONS =
    {
        {L_SH, R_SH}, {L_SH, L_HIP}, {R_SH, R_HIP}, {L_HIP, R_HIP},   // torso
        {L_SH, L_EL}, {L_EL, L_WR}, {R_SH, R_EL}, {R_EL, R_WR},        // arms
        {L_HIP, L_KN}, {L_KN, L_AN}, {R_HIP, R_KN}, {R_KN, R_AN},      // legs
        {NOSE, L_SH}, {NOSE, R_SH},                                    // neck
    };
    static readonly int[] JOINTS = { NOSE, L_SH, R_SH, L_EL, R_EL, L_WR, R_WR,
                                     L_HIP, R_HIP, L_KN, R_KN, L_AN, R_AN };

    void Update()
    {
        // Rebuild the mesh each frame while a pose is being tracked.
        if (source != null && source.HasPose) SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (source == null || !source.HasPose || source.LatestKeypoints == null) return;

        var kp = source.LatestKeypoints;
        var conf = source.LatestConfidence;
        Vector2 off = source.PreviewCropOffset;
        Vector2 scl = source.PreviewCropScale;
        Rect r = rectTransform.rect;

        Vector2 Map(int i)
        {
            Vector2 p = kp[i];
            float u = (p.x - off.x) / Mathf.Max(scl.x, 1e-4f);
            float v = (p.y - off.y) / Mathf.Max(scl.y, 1e-4f);
            // Unity textures are bottom-up (y=0 = bottom of frame = feet).
            // MediaPipe poseLandmarks inherit that: y=0 = feet, y=1 = head.
            // UI also grows upward, so v maps directly: y=0 → bottom of rect, y=1 → top.
            // Horizontal mirror is handled by the preview RawImage's negative localScale.
            return new Vector2(r.xMin + u * r.width, r.yMin + v * r.height);
        }

        bool Vis(int i) => conf == null || i >= conf.Length || conf[i] >= minVisibility;

        for (int c = 0; c < CONNECTIONS.GetLength(0); c++)
        {
            int a = CONNECTIONS[c, 0], b = CONNECTIONS[c, 1];
            if (Vis(a) && Vis(b)) AddLine(vh, Map(a), Map(b), lineThickness, lineColor);
        }

        foreach (int j in JOINTS)
        {
            if (!Vis(j)) continue;
            float rad = j == NOSE ? jointRadius * 1.6f : jointRadius;
            AddQuad(vh, Map(j), rad, j == NOSE ? headColor : jointColor);
        }
    }

    void AddLine(VertexHelper vh, Vector2 p0, Vector2 p1, float thick, Color col)
    {
        Vector2 d = p1 - p0;
        if (d.sqrMagnitude < 1e-4f) return;
        d.Normalize();
        Vector2 n = new Vector2(-d.y, d.x) * (thick * 0.5f);
        int i = vh.currentVertCount;
        AddVert(vh, p0 - n, col); AddVert(vh, p0 + n, col);
        AddVert(vh, p1 + n, col); AddVert(vh, p1 - n, col);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    void AddQuad(VertexHelper vh, Vector2 c, float rad, Color col)
    {
        int i = vh.currentVertCount;
        AddVert(vh, c + new Vector2(-rad, -rad), col); AddVert(vh, c + new Vector2(-rad, rad), col);
        AddVert(vh, c + new Vector2(rad, rad), col);  AddVert(vh, c + new Vector2(rad, -rad), col);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    void AddVert(VertexHelper vh, Vector2 pos, Color col)
    {
        UIVertex v = UIVertex.simpleVert;
        v.color = col;
        v.position = pos;
        v.uv0 = new Vector2(0.5f, 0.5f); // sample white texel
        vh.AddVert(v);
    }
}
