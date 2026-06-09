using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

public class PoseDetector : MonoBehaviour
{
    [Header("Model")]
    [SerializeField] ModelAsset modelAsset;

    [Header("Settings")]
    [SerializeField][Range(0f, 1f)] float confidenceThreshold = 0.3f;
    [SerializeField][Range(0f, 1f)] float smoothing = 0.5f;
    [Tooltip("Puppet this avatar from the keypoints. Turn off to only supply keypoints for scoring.")]
    [SerializeField] bool drivesAvatar = true;

    // Read-only latest detection, consumed by MegaDanceManager for pose scoring.
    // Same 17 COCO keypoints (normalized 0..1) + per-keypoint confidence.
    public Vector2[] LatestKeypoints { get; private set; }
    public float[] LatestConfidence { get; private set; }
    public bool HasPose { get; private set; }

    private Animator animator;
    private Worker worker;
    private WebCamTexture webcam;
    private RenderTexture renderTex;
    private UnityEngine.UI.RawImage rawImage;
    private Vector2 cropScale  = Vector2.one;
    private Vector2 cropOffset = Vector2.zero;
    private bool cropReady;

    private Quaternion[] tPoseRot = new Quaternion[55];
    private Dictionary<HumanBodyBones, Vector3> tPoseDir = new();
    private Vector2[] smoothedKp = new Vector2[17];

    // YOLOv8-pose / MoveNet shared keypoint indices (COCO format)
    const int NOSE       = 0;
    const int L_EAR      = 3,  R_EAR      = 4;
    const int L_SHOULDER = 5,  R_SHOULDER = 6;
    const int L_ELBOW    = 7,  R_ELBOW    = 8;
    const int L_WRIST    = 9,  R_WRIST    = 10;
    const int L_HIP      = 11, R_HIP      = 12;
    const int L_KNEE     = 13, R_KNEE     = 14;
    const int L_ANKLE    = 15, R_ANKLE    = 16;

    const int INPUT_SIZE = 320;
    const int NUM_DETECTIONS = 2100; // for 320x320 input

    void Start()
    {
        animator = GetComponent<Animator>();
        CacheTpose();

        if (modelAsset == null) { Debug.LogError("[PoseDetector] modelAsset is not assigned!"); return; }
        var model = ModelLoader.Load(modelAsset);
        worker = new Worker(model, BackendType.GPUCompute);

        webcam = new WebCamTexture();
        webcam.Play();

        var bg = FindAnyObjectByType<CameraBackground>();
        if (bg != null) Destroy(bg);

        rawImage = FindAnyObjectByType<UnityEngine.UI.RawImage>();
        if (rawImage != null) rawImage.texture = webcam;

        renderTex = new RenderTexture(INPUT_SIZE, INPUT_SIZE, 0, RenderTextureFormat.ARGB32);
    }

    void ComputeCrop()
    {
        if (webcam.width <= 16) return; // not ready yet
        float camAspect = (float)webcam.width / webcam.height;
        if (camAspect > 1f) // landscape webcam → center-crop to square
        {
            cropScale  = new Vector2(1f / camAspect, 1f);
            cropOffset = new Vector2((1f - cropScale.x) * 0.5f, 0f);
        }
        if (rawImage != null)
            rawImage.uvRect = new Rect(cropOffset.x, cropOffset.y, cropScale.x, cropScale.y);
        cropReady = true;
    }

    void Update()
    {
        if (worker == null || webcam == null) return;
        if (!webcam.didUpdateThisFrame) return;

        if (!cropReady) ComputeCrop();

        // Center-crop to square so body proportions are preserved for inference
        Graphics.Blit(webcam, renderTex, cropScale, cropOffset);

        using var inputTensor = TextureConverter.ToTensor(renderTex, INPUT_SIZE, INPUT_SIZE, 3);
        worker.Schedule(inputTensor);

        // Output shape: [1, 56, 2100]  (4 bbox + 1 conf + 17*3 keypoints)
        using var output = (worker.PeekOutput() as Tensor<float>).ReadbackAndClone();

        // Find the detection with highest confidence
        float bestConf = 0f;
        int bestIdx = 0;
        for (int i = 0; i < NUM_DETECTIONS; i++)
        {
            float detConf = output[0, 4, i];
            if (detConf > bestConf) { bestConf = detConf; bestIdx = i; }
        }

        if (bestConf < confidenceThreshold) return;

        // Extract 17 keypoints and normalize to [0,1]
        var kp   = new Vector2[17];
        var conf = new float[17];
        for (int k = 0; k < 17; k++)
        {
            kp[k]   = new Vector2(output[0, 5 + k * 3,     bestIdx] / INPUT_SIZE,
                                  output[0, 5 + k * 3 + 1, bestIdx] / INPUT_SIZE);
            conf[k] = output[0, 5 + k * 3 + 2, bestIdx];
        }

        // Smooth to remove jitter
        for (int i = 0; i < 17; i++)
            smoothedKp[i] = Vector2.Lerp(smoothedKp[i], kp[i], 1f - smoothing);

        // Expose latest result for scoring (read-only consumers like MegaDanceManager).
        LatestKeypoints = smoothedKp;
        LatestConfidence = conf;
        HasPose = true;

        if (drivesAvatar) ApplyToBones(smoothedKp, conf);
    }

    void ApplyToBones(Vector2[] kp, float[] conf)
    {
        float t = confidenceThreshold;

        // Hips/pelvis: knee-mid → hip-mid captures pelvic tilt (set first — it's the root bone)
        TryDriveMidMid(HumanBodyBones.Hips,  kp, conf, L_KNEE, R_KNEE, L_HIP, R_HIP, t);

        // Torso: hip mid → shoulder mid drives spine lean; shoulder mid → ear mid drives chest/upper spine
        TryDriveMidMid(HumanBodyBones.Spine, kp, conf, L_HIP, R_HIP, L_SHOULDER, R_SHOULDER, t);
        TryDriveMidMid(HumanBodyBones.Chest, kp, conf, L_SHOULDER, R_SHOULDER, L_EAR, R_EAR, t);

        TryDriveMid(HumanBodyBones.Neck, kp, conf, L_SHOULDER, R_SHOULDER, NOSE, t);

        TryDrive(HumanBodyBones.LeftUpperArm,  kp, conf, L_SHOULDER, L_ELBOW, t);
        TryDrive(HumanBodyBones.LeftLowerArm,  kp, conf, L_ELBOW,    L_WRIST, t);
        TryDrive(HumanBodyBones.RightUpperArm, kp, conf, R_SHOULDER, R_ELBOW, t);
        TryDrive(HumanBodyBones.RightLowerArm, kp, conf, R_ELBOW,    R_WRIST, t);
        TryDrive(HumanBodyBones.LeftUpperLeg,  kp, conf, L_HIP,      L_KNEE,  t);
        TryDrive(HumanBodyBones.LeftLowerLeg,  kp, conf, L_KNEE,     L_ANKLE, t);
        TryDrive(HumanBodyBones.RightUpperLeg, kp, conf, R_HIP,      R_KNEE,  t);
        TryDrive(HumanBodyBones.RightLowerLeg, kp, conf, R_KNEE,     R_ANKLE, t);
    }

    void TryDrive(HumanBodyBones bone, Vector2[] kp, float[] conf, int fromIdx, int toIdx, float minConf)
    {
        if (conf[fromIdx] < minConf || conf[toIdx] < minConf) return;
        DriveSegment(bone, kp[fromIdx], kp[toIdx]);
    }

    // Drive a bone using midpoint-of-two-from → midpoint-of-two-to (e.g. spine: hip-mid → shoulder-mid)
    void TryDriveMidMid(HumanBodyBones bone, Vector2[] kp, float[] conf, int fromA, int fromB, int toA, int toB, float minConf)
    {
        if (conf[fromA] < minConf || conf[fromB] < minConf || conf[toA] < minConf || conf[toB] < minConf) return;
        Vector2 from = (kp[fromA] + kp[fromB]) * 0.5f;
        Vector2 to   = (kp[toA]   + kp[toB])   * 0.5f;
        DriveSegment(bone, from, to);
    }

    // Drive a bone using the midpoint of two "from" keypoints → one "to" keypoint
    void TryDriveMid(HumanBodyBones bone, Vector2[] kp, float[] conf, int fromA, int fromB, int toIdx, float minConf)
    {
        if (conf[fromA] < minConf || conf[fromB] < minConf || conf[toIdx] < minConf) return;
        Vector2 mid = (kp[fromA] + kp[fromB]) * 0.5f;
        DriveSegment(bone, mid, kp[toIdx]);
    }

    void DriveSegment(HumanBodyBones bone, Vector2 from2D, Vector2 to2D)
    {
        if (!tPoseDir.ContainsKey(bone)) return;
        Transform t = animator.GetBoneTransform(bone);
        if (t == null) return;

        // Convert 2D screen direction to 3D (no depth from single camera)
        float dx = -(to2D.x - from2D.x); // negate X for correct mirroring
        float dy = -(to2D.y - from2D.y); // negate Y because screen Y is inverted
        Vector3 target = new Vector3(dx, dy, 0f).normalized;
        if (target.sqrMagnitude < 0.01f) return;

        t.rotation = Quaternion.FromToRotation(tPoseDir[bone], target) * tPoseRot[(int)bone];
    }

    void CacheTpose()
    {
        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            Transform t = animator.GetBoneTransform(bone);
            if (t != null) tPoseRot[(int)bone] = t.rotation;
        }
        CacheDirFallback(HumanBodyBones.Hips,  HumanBodyBones.Spine,      HumanBodyBones.Chest);
        CacheDirFallback(HumanBodyBones.Spine, HumanBodyBones.Chest,      HumanBodyBones.Neck);
        CacheDirFallback(HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck);
        CacheDir(HumanBodyBones.Neck,          HumanBodyBones.Head);
        CacheDir(HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm);
        CacheDir(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand);
        CacheDir(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
        CacheDir(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        CacheDir(HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg);
        CacheDir(HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot);
        CacheDir(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
        CacheDir(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
    }

    void CacheDir(HumanBodyBones bone, HumanBodyBones child)
    {
        Transform t = animator.GetBoneTransform(bone);
        Transform c = animator.GetBoneTransform(child);
        if (t != null && c != null)
            tPoseDir[bone] = (c.position - t.position).normalized;
    }

    // Try primary child first, fall back to secondary if the rig lacks it
    void CacheDirFallback(HumanBodyBones bone, HumanBodyBones primary, HumanBodyBones fallback)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t == null) return;
        Transform c = animator.GetBoneTransform(primary) ?? animator.GetBoneTransform(fallback);
        if (c != null) tPoseDir[bone] = (c.position - t.position).normalized;
    }

    void OnDestroy()
    {
        worker?.Dispose();
        renderTex?.Release();
        if (webcam != null && webcam.isPlaying) webcam.Stop();
    }
}
