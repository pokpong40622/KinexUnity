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

    [Tooltip("Rotate the drawn skeleton (degrees, multiple of 90) to counter the Android camera " +
             "preview's quarter-turn so it lands upright on the body. Android only; 0 in editor. " +
             "270 cancels the preview's clockwise 90; try 90 if it ends up upside-down.")]
    // 270 (CCW90) drew the skeleton UPSIDE-DOWN on device (head at bottom, legs over the top of the
    // preview box). 90 is the 180°-opposite quarter turn that lands it upright. (See Round 11 fix.)
    [SerializeField] int previewRotateCW = 90;

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

        // MediaPipe normalized landmarks are upright image space: y=0 = top (head), y=1 = bottom (feet).
        // The Android camera preview RawImage is rotated a quarter turn (front-cam frames arrive
        // sideways) and THIS overlay is its child, so an upright skeleton would be drawn sideways.
        // We counter that by rotating the mapped points by `previewRotateCW`. The editor/webcam preview
        // isn't rotated, so rot stays 0 there and we map straight through (head at top).
        int rot = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
        rot = ((previewRotateCW % 360) + 360) % 360;
#endif
        bool quarter = rot == 90 || rot == 270;
        // A quarter turn swaps the rect's W/H back to the original on-screen box.
        float boxW = quarter ? r.height : r.width;
        float boxH = quarter ? r.width  : r.height;

        Vector2 Map(int i)
        {
            Vector2 p = kp[i];
            float u = (p.x - off.x) / Mathf.Max(scl.x, 1e-4f);
            float v = (p.y - off.y) / Mathf.Max(scl.y, 1e-4f);
            if (rot == 0)
                return new Vector2(r.xMin + u * r.width, r.yMin + v * r.height);

            // Place head-up in the on-screen box (rect y grows up; image v grows down → use 1-v),
            // centred, then rotate about the rect centre to counter the preview's turn.
            float sx = u * boxW - boxW * 0.5f;
            float sy = (1f - v) * boxH - boxH * 0.5f;
            float lx, ly;
            switch (rot)
            {
                case 90:  lx =  sy; ly = -sx; break;   // CW 90
                case 180: lx = -sx; ly = -sy; break;
                default:  lx = -sy; ly =  sx; break;   // 270 = CCW 90 (cancels preview CW 90)
            }
            return new Vector2(r.xMin + r.width * 0.5f + lx, r.yMin + r.height * 0.5f + ly);
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
