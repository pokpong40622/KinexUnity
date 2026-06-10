// MediaPipePoseDetector — ports the pose technique from the "3d-pose-rigging" project
// (MediaPipe Pose Landmarker + 3D worldLandmarks + LERP smoothing) into Kinex.
//
// Drop-in replacement for PoseDetector.cs: it exposes the SAME public surface
// (LatestKeypoints / LatestConfidence / HasPose, 17 COCO keypoints) so PoseScorer
// and MegaDanceManager keep working unchanged — just swap which component you assign.
//
// Why this is better than the old Sentis/YOLOv8 path: MediaPipe gives 33 landmarks
// with REAL 3D depth (worldLandmarks, in meters). The old path was X/Y only (z=0),
// which is why the avatar looked flat. Here bones are driven in full 3D.
//
// SETUP (one time):
//   1. Install the MediaPipe Unity Plugin (homuler): https://github.com/homuler/MediaPipeUnityPlugin
//      Import the release .unitypackage, then add the pose_landmarker model to StreamingAssets.
//   2. Project Settings > Player > Scripting Define Symbols → add:  KINEX_MEDIAPIPE
//      (Until you add it, this file stays inert and the project keeps compiling.)
//   3. Put this component on your humanoid avatar (same place the old PoseDetector lived),
//      then assign it to MegaDanceManager.poseDetector.

using UnityEngine;

#if KINEX_MEDIAPIPE
using System;
using System.Collections.Generic;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.PoseLandmarker;
#endif

public class MediaPipePoseDetector : MonoBehaviour
{
    [Header("Model")]
    [Tooltip("pose_landmarker_full.task placed under Assets/StreamingAssets/ (filename only).")]
    [SerializeField] string modelFileName = "pose_landmarker_full.bytes";

    [Header("Settings")]
    [Tooltip("Mirrors the React project's minPoseDetectionConfidence (0.5).")]
    [SerializeField][Range(0f, 1f)] float minDetectionConfidence = 0.5f;
    [Tooltip("LERP factor toward each new frame. React used 0.28 (smaller = smoother/laggier).")]
    [SerializeField][Range(0.05f, 0.8f)] float smoothingFactor = 0.28f;
    [Tooltip("Extra-strong smoothing for the knee/ankle landmarks (the noisiest). Lower = steadier " +
             "resting legs but slightly more lag on leg moves. This is what stops the still leg " +
             "jittering when the other leg lifts.")]
    [SerializeField][Range(0.05f, 0.5f)] float legSmoothing = 0.13f;
    [Tooltip("Drive the avatar from keypoints. Off = only supply keypoints for scoring.")]
    [SerializeField] bool drivesAvatar = true;
    [Tooltip("A bone only drives while BOTH its endpoints are at least this visible. " +
             "Below it the bone freezes in place instead of following MediaPipe's guess — " +
             "so you can sit / be half out of frame and the upper body still tracks " +
             "without the legs flailing. Lower = tracks more but flails more.")]
    [SerializeField][Range(0f, 1f)] float minBoneVisibility = 0.5f;
    [Tooltip("Single visibility threshold for leg landmarks — used for BOTH the gate and the " +
             "actual drive (no mid-range freeze). Skeleton overlay draws at 0.4; keep this near " +
             "or below that so legs drive whenever the preview shows them. Lower = legs move more.")]
    [SerializeField][Range(0f, 1f)] float legVisThreshold = 0.3f;
    [Tooltip("Max degrees per second a LEG bone may rotate toward its target. Caps sudden " +
             "MediaPipe jumps so legs can't fly or snap-twist on a noisy frame. " +
             "Lower = steadier but laggier. This is the legs' main stabilizer.")]
    [SerializeField][Range(60f, 1080f)] float legMaxDegPerSec = 540f;

    [Header("Test Source (leave EMPTY for normal webcam)")]
    [Tooltip("TEST ONLY: assign a video clip to feed it to pose detection instead of the live " +
             "webcam — handy when you can't stand in front of the camera. Clear this field to " +
             "go back to the real webcam.")]
    [SerializeField] UnityEngine.Video.VideoClip testVideoClip;

    [Header("Avatar Axis Mapping (toggle LIVE in Play mode if a direction looks wrong)")]
    [Tooltip("The model's BACK faces the game camera (follow-behind), so your pose maps " +
             "DIRECTLY onto it — these only correct MediaPipe's raw axis signs vs Unity. " +
             "flipX = left/right (arms on wrong side), flipZ = front/back (leans the wrong way), " +
             "flipY = up/down. Defaults match the validated webcam setup.")]
    [SerializeField] bool flipX = false;
    [SerializeField] bool flipY = false;
    [SerializeField] bool flipZ = false;

    [Header("Head Tracking")]
    [Tooltip("Multiplier on the lateral (X) tilt of the head. 1 = raw, 2-3 = more responsive.")]
    [SerializeField][Range(1f, 5f)] float headTiltScale = 2.5f;

    [Header("Body Tracking")]
    [Tooltip("Amplifies lateral (X) and depth (Z) lean so small body tilts show clearly. 1 = raw.")]
    [SerializeField][Range(1f, 3f)] float bodyTiltScale = 1.5f;

    [Header("Calibration")]
    [Tooltip("Automatically start calibration when Play begins.")]
    [SerializeField] bool autoCalibrate = true;
    [Tooltip("Seconds to average MediaPipe samples while the user holds the T-pose.")]
    [SerializeField][Range(2f, 10f)] float calibrationDuration = 5f;
    [Tooltip("How strongly calibration overrides the avatar's built-in T-pose directions. " +
             "0 = ignore calibration (use the normal/default logic), 1 = fully use the captured " +
             "T-pose. Lower keeps the rest pose closer to default so an imperfect T-pose can't " +
             "skew your arms/legs to a stuck angle.")]
    [SerializeField][Range(0f, 1f)] float calibrationStrength = 0.5f;

    public enum CalibState { Idle, Prompting, Counting, Done }
    public CalibState CalibrationPhase { get; private set; } = CalibState.Idle;
    public float CalibrationCountdown { get; private set; }
    public bool IsCalibrated { get; private set; }

    // ---- Public surface: identical to PoseDetector.cs so the scorer is a drop-in. ----
    // 17 COCO keypoints (normalized 0..1) + per-keypoint confidence, consumed by PoseScorer.
    public Vector2[] LatestKeypoints { get; private set; }
    public float[] LatestConfidence { get; private set; }
    public bool HasPose { get; private set; }

    // ---- Preview cover-crop, computed at runtime from the real webcam aspect so the
    //      skeleton overlay can map landmarks onto exactly what the preview shows. ----
    [Header("Preview")]
    [Tooltip("Mirror the camera preview like a selfie (matches the reference app).")]
    [SerializeField] bool mirrorPreview = true;
    public Vector2 PreviewCropOffset { get; private set; } = Vector2.zero; // x,y in 0..1
    public Vector2 PreviewCropScale { get; private set; } = Vector2.one;   // w,h in 0..1
    public bool PreviewMirrored => mirrorPreview;

    // COCO-17 layout (must match PoseDetector.cs / PoseScorer.cs indices).
    const int NOSE = 0;
    const int L_EAR = 3, R_EAR = 4;
    const int L_SHOULDER = 5, R_SHOULDER = 6;
    const int L_ELBOW = 7, R_ELBOW = 8;
    const int L_WRIST = 9, R_WRIST = 10;
    const int L_HIP = 11, R_HIP = 12;
    const int L_KNEE = 13, R_KNEE = 14;
    const int L_ANKLE = 15, R_ANKLE = 16;

    // MediaPipe BlazePose 33-landmark index → COCO-17 slot.
    // (MediaPipe order: 0 nose, 2 L_eye, 5 R_eye, 7 L_ear, 8 R_ear, 11/12 shoulders,
    //  13/14 elbows, 15/16 wrists, 23/24 hips, 25/26 knees, 27/28 ankles.)
    static readonly (int mp, int coco)[] MP_TO_COCO =
    {
        (0, NOSE), (7, L_EAR), (8, R_EAR),
        (11, L_SHOULDER), (12, R_SHOULDER),
        (13, L_ELBOW), (14, R_ELBOW),
        (15, L_WRIST), (16, R_WRIST),
        (23, L_HIP), (24, R_HIP),
        (25, L_KNEE), (26, R_KNEE),
        (27, L_ANKLE), (28, R_ANKLE),
    };

#if KINEX_MEDIAPIPE
    // MediaPipe world-landmark indices used for 3D bone driving (BlazePose layout).
    const int MP_NOSE = 0, MP_L_EYE = 2, MP_R_EYE = 5;
    const int MP_L_EAR = 7, MP_R_EAR = 8;
    const int MP_L_SHOULDER = 11, MP_R_SHOULDER = 12;
    const int MP_L_ELBOW = 13, MP_R_ELBOW = 14, MP_L_WRIST = 15, MP_R_WRIST = 16;
    const int MP_L_HIP = 23, MP_R_HIP = 24;
    const int MP_L_KNEE = 25, MP_R_KNEE = 26, MP_L_ANKLE = 27, MP_R_ANKLE = 28;

    PoseLandmarker _landmarker;
    Coroutine _calibCoroutine;
    WebCamTexture _webcam;
    UnityEngine.Video.VideoPlayer _videoPlayer; // TEST ONLY: optional recorded-clip source
    RenderTexture _videoRT;
    bool UsingVideo => _videoPlayer != null;
    int SrcWidth  => UsingVideo ? _videoRT.width  : _webcam.width;
    int SrcHeight => UsingVideo ? _videoRT.height : _webcam.height;
    Texture2D _frame;                       // CPU copy fed to MediaPipe each tick
    Animator _animator;
    UnityEngine.UI.RawImage _rawImage;      // camera preview in the game UI
    bool _previewReady;

    // T-pose cache (same proven technique as PoseDetector.cs, now in 3D).
    readonly Quaternion[] _tPoseRot = new Quaternion[55];
    readonly Dictionary<HumanBodyBones, Vector3> _tPoseDir = new();
    // Pristine geometric directions (avatar bind T-pose). Kept so calibration can BLEND toward the
    // user's captured T-pose instead of fully replacing it — an imperfect T-pose can't then skew rest.
    readonly Dictionary<HumanBodyBones, Vector3> _geomDir = new();

    // Smoothed 3D world positions per MediaPipe landmark (the LERP from ThreeCanvas.tsx).
    readonly Vector3[] _smoothed = new Vector3[33];
    readonly bool[] _hasSmoothed = new bool[33];
    // Live per-landmark visibility (0..1) from the latest frame. Used to freeze bones
    // whose joints are off-camera (e.g. legs when you sit) instead of driving them
    // from MediaPipe's extrapolated guess.
    readonly float[] _visibility = new float[33];

    void Start()
    {
        _animator = GetComponent<Animator>();
        CacheTpose();

        LatestKeypoints = new Vector2[17];
        LatestConfidence = new float[17];

        _rawImage = FindAnyObjectByType<UnityEngine.UI.RawImage>();

        if (testVideoClip != null)
        {
            // TEST ONLY: feed a recorded clip instead of the live webcam.
            _videoPlayer = gameObject.AddComponent<UnityEngine.Video.VideoPlayer>();
            _videoPlayer.clip = testVideoClip;
            _videoPlayer.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            _videoPlayer.isLooping = true;
            _videoPlayer.playOnAwake = false;
            _videoRT = new RenderTexture((int)testVideoClip.width, (int)testVideoClip.height, 0);
            _videoPlayer.targetTexture = _videoRT;
            _videoPlayer.Play();
            if (_rawImage != null) _rawImage.texture = _videoRT;
            Debug.Log($"[MediaPipePoseDetector] TEST MODE: feeding video '{testVideoClip.name}' " +
                      "instead of the webcam. Clear the Test Video Clip field to use the live camera.");
        }
        else
        {
            // Prefer the front camera at 640x480x30 — the SAME capture scale the reference
            // app requested. MediaPipe is fed the full frame (no pre-crop), exactly like the
            // reference; cropping happens only for on-screen preview.
            string frontCam = null;
            foreach (var d in WebCamTexture.devices)
                if (d.isFrontFacing) { frontCam = d.name; break; }
            _webcam = frontCam != null
                ? new WebCamTexture(frontCam, 640, 480, 30)
                : new WebCamTexture(640, 480, 30);
            _webcam.Play();
            if (_rawImage != null) _rawImage.texture = _webcam;
        }

        // Model load is async-capable so it works on Android, where StreamingAssets live inside the
        // APK and can't be read with System.IO. See InitLandmarker.
        StartCoroutine(InitLandmarker());
    }

    // Loads the pose model and creates the landmarker. On Android, Application.streamingAssetsPath
    // is a "jar:file://...apk!/assets" URL that System.IO can't open, so the bytes must come through
    // UnityWebRequest; desktop/editor read the file directly. Either way we hand MediaPipe the raw
    // bytes via modelAssetBuffer, which is platform-independent.
    System.Collections.IEnumerator InitLandmarker()
    {
        string modelPath = System.IO.Path.Combine(Application.streamingAssetsPath, modelFileName);
        byte[] modelBytes = null;

        if (modelPath.Contains("://"))
        {
            // Inside the APK (Android) — read via UnityWebRequest.
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(modelPath))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[MediaPipePoseDetector] Model load FAILED from '{modelPath}': {req.error}");
                    yield break;
                }
                modelBytes = req.downloadHandler.data;
            }
        }
        else
        {
            if (!System.IO.File.Exists(modelPath))
            {
                Debug.LogError($"[MediaPipePoseDetector] Model file NOT FOUND at '{modelPath}'. " +
                               "Copy pose_landmarker_full.bytes into Assets/StreamingAssets/ and try again.");
                yield break;
            }
            modelBytes = System.IO.File.ReadAllBytes(modelPath);
        }
        Debug.Log($"[MediaPipePoseDetector] Model bytes loaded: {(modelBytes != null ? modelBytes.Length.ToString() : "null")} from '{modelPath}'");

        try
        {
            var baseOptions = new BaseOptions(modelAssetBuffer: modelBytes);
            var options = new PoseLandmarkerOptions(
                baseOptions,
                runningMode: RunningMode.VIDEO,
                numPoses: 1,
                minPoseDetectionConfidence: minDetectionConfidence,
                minPosePresenceConfidence: minDetectionConfidence,
                minTrackingConfidence: minDetectionConfidence,
                outputSegmentationMasks: false);
            _landmarker = PoseLandmarker.CreateFromOptions(options);
            Debug.Log($"[MediaPipePoseDetector] Landmarker created: {(_landmarker != null ? "OK" : "null (CreateFromOptions returned null)")}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MediaPipePoseDetector] CreateFromOptions FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            yield break;
        }

        if (autoCalibrate) StartCalibration();
    }

    public void StartCalibration()
    {
        if (_calibCoroutine != null) StopCoroutine(_calibCoroutine);
        _calibCoroutine = StartCoroutine(CalibrationRoutine());
    }

    void Update()
    {
        if (_landmarker == null) return;
        if (UsingVideo) { if (_videoPlayer == null || !_videoPlayer.isPrepared) return; }
        else if (_webcam == null || !_webcam.didUpdateThisFrame) return;

        if (!_previewReady) TryApplyPreviewCrop();

        // ---- Feed the current source frame to MediaPipe. ----
        // NOTE: this is the one plugin-version-sensitive spot. If your installed plugin's
        // Image API differs, copy the feeding code from its PoseLandmarker sample scene.
        int srcW = SrcWidth, srcH = SrcHeight;
        if (_frame == null || _frame.width != srcW || _frame.height != srcH)
            _frame = new Texture2D(srcW, srcH, TextureFormat.RGBA32, false);
        if (UsingVideo)
        {
            // On Windows/DirectX, ReadPixels from a RenderTexture returns top-down data
            // (the DX convention matches what MediaPipe expects). No vertical flip needed.
            var prevActive = RenderTexture.active;
            RenderTexture.active = _videoRT;
            _frame.ReadPixels(new UnityEngine.Rect(0, 0, srcW, srcH), 0, 0);
            RenderTexture.active = prevActive;
            _frame.Apply();
        }
        else
        {
            // WebCamTexture data is bottom-up (y=0 = bottom of frame = feet).
            // Don't flip — keep consistent with the video path so the overlay
            // always maps poseLandmarks y=0 → bottom of rect (feet).
            _frame.SetPixels32(_webcam.GetPixels32());
            _frame.Apply();
        }

        using var image = new Image(ImageFormat.Types.Format.Srgba, _frame);
        var result = _landmarker.DetectForVideo(image, GetTimestampMs());

        if (result.poseWorldLandmarks == null || result.poseWorldLandmarks.Count == 0)
        {
            HasPose = false;
            return;
        }

        var world = result.poseWorldLandmarks[0].landmarks;        // 33 × (x,y,z meters)
        var norm = result.poseLandmarks[0].landmarks;              // 33 × (x,y normalized)

        // LERP-smooth the 3D world points (ThreeCanvas.tsx:452 technique).
        for (int i = 0; i < world.Count && i < 33; i++)
        {
            var p = ToUnity(world[i]);
            if (!_hasSmoothed[i]) { _smoothed[i] = p; _hasSmoothed[i] = true; }
            else
            {
                // Knees/ankles (25..28) are the noisiest landmarks — smooth them harder so the
                // resting leg doesn't jitter when the other leg moves.
                float f = (i >= MP_L_KNEE && i <= MP_R_ANKLE) ? legSmoothing : smoothingFactor;
                _smoothed[i] = Vector3.Lerp(_smoothed[i], p, f);
            }
        }

        // Capture this frame's visibility so ApplyToBones can freeze off-camera bones.
        for (int i = 0; i < norm.Count && i < 33; i++)
            _visibility[i] = norm[i].visibility ?? 1f;

        // Build the COCO-17 keypoints (2D) + confidence for PoseScorer.
        BuildCocoKeypoints(norm);
        HasPose = true;
    }

    // LateUpdate runs AFTER Unity's Animator evaluation, so our bone rotations
    // win over whatever the Animator's default/idle pose sets each frame.
    void LateUpdate()
    {
        if (drivesAvatar && HasPose) ApplyToBones();
    }

    // Cover-fit the landscape webcam into the (portrait) preview rect without distortion,
    // and expose the crop so PoseSkeletonOverlay maps landmarks onto the same visible region.
    void TryApplyPreviewCrop()
    {
        if (SrcWidth <= 16 || _rawImage == null) return;

        float camAspect = (float)SrcWidth / SrcHeight;
        UnityEngine.Rect r = _rawImage.rectTransform.rect;
        float rectAspect = r.height > 0f ? r.width / r.height : camAspect;

        float ox = 0f, oy = 0f, sw = 1f, sh = 1f;
        if (camAspect > rectAspect) { sw = rectAspect / camAspect; ox = (1f - sw) * 0.5f; } // crop sides
        else                        { sh = camAspect / rectAspect; oy = (1f - sh) * 0.5f; } // crop top/bottom

        // WebCamTexture is often stored bottom-up on Windows (videoVerticallyMirrored=true).
        // If so, flip the uvRect Y so the preview is right-side up, and adjust the crop
        // offset so the skeleton overlay's Map() still aligns with the visible region.
        bool vFlip = !UsingVideo && _webcam != null && _webcam.videoVerticallyMirrored;
        PreviewCropOffset = new Vector2(ox, vFlip && oy > 0f ? 1f - oy - sh : oy);
        PreviewCropScale  = new Vector2(sw, sh);
        _rawImage.uvRect  = new UnityEngine.Rect(ox, vFlip ? oy + sh : oy, sw, vFlip ? -sh : sh);

        if (mirrorPreview && !UsingVideo) // don't mirror a recorded clip — it's not a selfie
        {
            var s = _rawImage.rectTransform.localScale;
            s.x = -Mathf.Abs(s.x);
            _rawImage.rectTransform.localScale = s;
        }
        _previewReady = true;
    }

    // MediaPipe world landmark → Unity direction space. The model's back faces the game
    // camera (follow-behind), so the mapping is DIRECT, not mirrored — the per-axis flips
    // below just reconcile MediaPipe's axis signs with Unity's. Tune them in the Inspector.
    Vector3 ToUnity(Mediapipe.Tasks.Components.Containers.Landmark lm)
    {
        float x = flipX ? -lm.x : lm.x;
        float y = flipY ? -lm.y : lm.y;
        float z = flipZ ? -lm.z : lm.z;
        return new Vector3(x, y, z);
    }

    void BuildCocoKeypoints(IReadOnlyList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> norm)
    {
        // Default everything to low confidence, then fill the mapped joints.
        for (int i = 0; i < 17; i++) { LatestKeypoints[i] = Vector2.zero; LatestConfidence[i] = 0f; }
        foreach (var (mp, coco) in MP_TO_COCO)
        {
            if (mp >= norm.Count) continue;
            var lm = norm[mp];
            LatestKeypoints[coco] = new Vector2(lm.x, lm.y);
            // BlazePose normalized landmarks carry visibility ~ how confident/visible the joint is.
            LatestConfidence[coco] = lm.visibility ?? 1f;
        }
    }

    // ---- 3D bone driving (the old DriveSegment technique, now with real depth). ----
    void ApplyToBones()
    {
        DriveMidMid(HumanBodyBones.Hips, MP_L_HIP, MP_R_HIP, MP_L_SHOULDER, MP_R_SHOULDER);
        DriveSpineWithTilt();
        // Drive Neck toward ear midpoint. X is negated because flipX (correct for limbs)
        // inverts the tilt direction in the follow-behind setup. headTiltScale boosts it.
        DriveNeckWithTilt();

        Drive(HumanBodyBones.LeftUpperArm, MP_R_SHOULDER, MP_R_ELBOW);
        Drive(HumanBodyBones.LeftLowerArm, MP_R_ELBOW, MP_R_WRIST);
        Drive(HumanBodyBones.RightUpperArm, MP_L_SHOULDER, MP_L_ELBOW);
        Drive(HumanBodyBones.RightLowerArm, MP_L_ELBOW, MP_L_WRIST);
        // Drive each leg SEGMENT independently so the thigh keeps tracking when the ankle is out
        // of frame — only the shin rests. A segment rests when either of its two landmarks isn't
        // visible; both segments rest (whole leg) when no lower-body joints are seen.
        DriveOrRestLeg(HumanBodyBones.LeftUpperLeg,  MP_R_HIP,  MP_R_KNEE);
        DriveOrRestLeg(HumanBodyBones.LeftLowerLeg,  MP_R_KNEE, MP_R_ANKLE);
        DriveOrRestLeg(HumanBodyBones.RightUpperLeg, MP_L_HIP,  MP_L_KNEE);
        DriveOrRestLeg(HumanBodyBones.RightLowerLeg, MP_L_KNEE, MP_L_ANKLE);
    }

    // A landmark is usable only if it has been seen AND is currently visible enough.
    // The visibility gate is what lets you sit / go half out of frame: the moment a
    // joint drops below the threshold its bone stops driving and holds its last pose.
    bool Usable(int i) => _hasSmoothed[i] && _visibility[i] >= minBoneVisibility;

    void Drive(HumanBodyBones bone, int from, int to)
    {
        if (!Usable(from) || !Usable(to)) return;
        DriveSegment(bone, _smoothed[from], _smoothed[to]);
    }

    void DriveMid(HumanBodyBones bone, int fromA, int fromB, int to)
    {
        if (!Usable(fromA) || !Usable(fromB) || !Usable(to)) return;
        DriveSegment(bone, (_smoothed[fromA] + _smoothed[fromB]) * 0.5f, _smoothed[to]);
    }

    void DriveMidMid(HumanBodyBones bone, int fromA, int fromB, int toA, int toB)
    {
        if (!Usable(fromA) || !Usable(fromB) || !Usable(toA) || !Usable(toB)) return;
        DriveSegment(bone, (_smoothed[fromA] + _smoothed[fromB]) * 0.5f,
                           (_smoothed[toA] + _smoothed[toB]) * 0.5f);
    }

    void DriveSpineWithTilt()
    {
        if (!Usable(MP_L_HIP) || !Usable(MP_R_HIP) ||
            !Usable(MP_L_SHOULDER) || !Usable(MP_R_SHOULDER)) return;
        Vector3 from  = (_smoothed[MP_L_HIP]      + _smoothed[MP_R_HIP])      * 0.5f;
        Vector3 to    = (_smoothed[MP_L_SHOULDER]  + _smoothed[MP_R_SHOULDER]) * 0.5f;
        Vector3 delta = to - from;
        // Amplify only the lean components (X = left/right, Z = front/back).
        // Y is left untouched so the "up" direction is not distorted.
        delta.x *= bodyTiltScale;
        delta.z *= bodyTiltScale;
        DriveSegment(HumanBodyBones.Spine, from, from + delta);
    }

    // A leg landmark is usable when it has been seen AND clears legVisThreshold. This is the
    // ONE threshold legs use — gate and drive agree, so a visible leg never freezes mid-range.
    bool LegUsable(int i) => _hasSmoothed[i] && _visibility[i] >= legVisThreshold;

    // Drive a leg segment if BOTH its landmarks are visible (same threshold as the gate,
    // rate-limited so jitter can't fly/snap-twist); otherwise ease it back to rest. Applied
    // per segment, so the thigh (hip→knee) keeps tracking even when the ankle is off-camera.
    void DriveOrRestLeg(HumanBodyBones bone, int from, int to)
    {
        if (LegUsable(from) && LegUsable(to))
            DriveSegmentClamped(bone, _smoothed[from], _smoothed[to], legMaxDegPerSec * Time.deltaTime);
        else
            ResetBone(bone);
    }

    // Ease the leg back to its rest pose instead of snapping when it leaves the frame.
    void ResetBone(HumanBodyBones bone)
    {
        Transform t = _animator.GetBoneTransform(bone);
        if (t != null)
            t.rotation = Quaternion.RotateTowards(t.rotation, _tPoseRot[(int)bone],
                                                  legMaxDegPerSec * Time.deltaTime);
    }

    void DriveNeckWithTilt()
    {
        if (!Usable(MP_L_SHOULDER) || !Usable(MP_R_SHOULDER) ||
            !Usable(MP_L_EAR)      || !Usable(MP_R_EAR)) return;
        Vector3 from  = (_smoothed[MP_L_SHOULDER] + _smoothed[MP_R_SHOULDER]) * 0.5f;
        Vector3 to    = (_smoothed[MP_L_EAR]      + _smoothed[MP_R_EAR])      * 0.5f;
        Vector3 delta = to - from;
        // headTiltScale amplifies the lateral component so small tilts are clearly visible.
        // With flipX=false the raw X sign is already correct for follow-behind — no negation needed.
        delta.x *= headTiltScale;
        DriveSegment(HumanBodyBones.Neck, from, from + delta);
    }

    void DriveSegment(HumanBodyBones bone, Vector3 from, Vector3 to)
    {
        if (!_tPoseDir.ContainsKey(bone)) return;
        Transform t = _animator.GetBoneTransform(bone);
        if (t == null) return;

        Vector3 target = (to - from).normalized;
        if (target.sqrMagnitude < 0.01f) return;

        t.rotation = Quaternion.FromToRotation(_tPoseDir[bone], target) * _tPoseRot[(int)bone];
    }

    // Same as DriveSegment but eases toward the target by at most maxDeg this frame, so a
    // sudden MediaPipe jump can't snap the bone (used for legs, the noisiest landmarks).
    void DriveSegmentClamped(HumanBodyBones bone, Vector3 from, Vector3 to, float maxDeg)
    {
        if (!_tPoseDir.ContainsKey(bone)) return;
        Transform t = _animator.GetBoneTransform(bone);
        if (t == null) return;

        Vector3 target = (to - from).normalized;
        if (target.sqrMagnitude < 0.01f) return;

        Quaternion goal = Quaternion.FromToRotation(_tPoseDir[bone], target) * _tPoseRot[(int)bone];
        t.rotation = Quaternion.RotateTowards(t.rotation, goal, maxDeg);
    }

    // ---- T-pose caching (identical approach to PoseDetector.cs:194). ----
    void CacheTpose()
    {
        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            Transform t = _animator.GetBoneTransform(bone);
            if (t != null) _tPoseRot[(int)bone] = t.rotation;
        }
        CacheDirFallback(HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest);
        CacheDirFallback(HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck);
        CacheDirFallback(HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck);
        CacheDir(HumanBodyBones.Neck, HumanBodyBones.Head);
        CacheDir(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
        CacheDir(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        CacheDir(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
        CacheDir(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        CacheDir(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
        CacheDir(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        CacheDir(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
        CacheDir(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
    }

    void CacheDir(HumanBodyBones bone, HumanBodyBones child)
    {
        Transform t = _animator.GetBoneTransform(bone);
        Transform c = _animator.GetBoneTransform(child);
        if (t != null && c != null) { _tPoseDir[bone] = (c.position - t.position).normalized; _geomDir[bone] = _tPoseDir[bone]; }
    }

    void CacheDirFallback(HumanBodyBones bone, HumanBodyBones primary, HumanBodyBones fallback)
    {
        Transform t = _animator.GetBoneTransform(bone);
        if (t == null) return;
        Transform c = _animator.GetBoneTransform(primary) ?? _animator.GetBoneTransform(fallback);
        if (c != null) { _tPoseDir[bone] = (c.position - t.position).normalized; _geomDir[bone] = _tPoseDir[bone]; }
    }

    // Blend the calibrated direction toward the avatar's pristine geometric direction by
    // calibrationStrength (0 = ignore calibration entirely, 1 = use it fully).
    Vector3 BlendDir(HumanBodyBones bone, Vector3 calibrated) =>
        _geomDir.ContainsKey(bone)
            ? Vector3.Slerp(_geomDir[bone], calibrated, calibrationStrength).normalized
            : calibrated;

    // Flip a Texture2D vertically (row swap). Only used for the video test source, where
    // ReadPixels delivers the frame bottom-up vs the top-down frames the webcam supplies.
    static void FlipVerticalInPlace(Texture2D tex, int w, int h)
    {
        var px = tex.GetPixels32();
        for (int y = 0; y < h / 2; y++)
        {
            int top = y * w, bot = (h - 1 - y) * w;
            for (int x = 0; x < w; x++)
            {
                (px[top + x], px[bot + x]) = (px[bot + x], px[top + x]);
            }
        }
        tex.SetPixels32(px);
    }

    long GetTimestampMs() => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

    // ---- Calibration ----
    System.Collections.IEnumerator CalibrationRoutine()
    {
        // Give camera + MediaPipe time to warm up before prompting.
        CalibrationPhase = CalibState.Prompting;
        yield return new WaitForSeconds(2f);

        // Average landmark positions over calibrationDuration seconds.
        var acc = new Vector3[33];
        int sampleCount = 0;
        float remaining = calibrationDuration;
        CalibrationPhase = CalibState.Counting;

        while (remaining > 0f)
        {
            CalibrationCountdown = remaining;
            if (HasPose)
            {
                for (int i = 0; i < 33; i++) acc[i] += _smoothed[i];
                sampleCount++;
            }
            yield return null;
            remaining -= Time.deltaTime;
        }
        CalibrationCountdown = 0f;

        if (sampleCount >= 10)
        {
            for (int i = 0; i < 33; i++) acc[i] /= sampleCount;
            RecalibrateTPoseDirs(acc);
            IsCalibrated = true;
            Debug.Log($"[Calibration] Done — averaged {sampleCount} frames.");
        }
        else
        {
            Debug.LogWarning($"[Calibration] Too few frames ({sampleCount}); keeping avatar T-pose dirs. " +
                             "Make sure the full body is in frame.");
        }

        CalibrationPhase = CalibState.Done;
        yield return new WaitForSeconds(2f);
        CalibrationPhase = CalibState.Idle;
        _calibCoroutine = null;
    }

    void RecalibrateTPoseDirs(Vector3[] avg)
    {
        // Replace the avatar's geometric T-pose directions with directions derived from
        // the user's own body proportions captured during calibration. This makes every
        // bone drive body-proportion-aware: deviations are measured from the user's T-pose,
        // not the avatar's — so arm/leg length differences stop causing mis-drives.
        void SetDir(HumanBodyBones bone, int from, int to)
        {
            Vector3 d = (avg[to] - avg[from]).normalized;
            if (d.sqrMagnitude > 0.01f) _tPoseDir[bone] = BlendDir(bone, d);
        }
        void SetMidMidDir(HumanBodyBones bone, int fA, int fB, int tA, int tB)
        {
            Vector3 d = (((avg[tA] + avg[tB]) * 0.5f) - ((avg[fA] + avg[fB]) * 0.5f)).normalized;
            if (d.sqrMagnitude > 0.01f) _tPoseDir[bone] = BlendDir(bone, d);
        }

        // Torso — hip→shoulder direction.
        SetMidMidDir(HumanBodyBones.Hips,  MP_L_HIP, MP_R_HIP, MP_L_SHOULDER, MP_R_SHOULDER);
        SetMidMidDir(HumanBodyBones.Spine, MP_L_HIP, MP_R_HIP, MP_L_SHOULDER, MP_R_SHOULDER);

        // Arms (L/R swapped to match the drive swap in ApplyToBones).
        SetDir(HumanBodyBones.LeftUpperArm,  MP_R_SHOULDER, MP_R_ELBOW);
        SetDir(HumanBodyBones.LeftLowerArm,  MP_R_ELBOW,    MP_R_WRIST);
        SetDir(HumanBodyBones.RightUpperArm, MP_L_SHOULDER, MP_L_ELBOW);
        SetDir(HumanBodyBones.RightLowerArm, MP_L_ELBOW,    MP_L_WRIST);

        // Legs (also swapped).
        SetDir(HumanBodyBones.LeftUpperLeg,  MP_R_HIP,   MP_R_KNEE);
        SetDir(HumanBodyBones.LeftLowerLeg,  MP_R_KNEE,  MP_R_ANKLE);
        SetDir(HumanBodyBones.RightUpperLeg, MP_L_HIP,   MP_L_KNEE);
        SetDir(HumanBodyBones.RightLowerLeg, MP_L_KNEE,  MP_L_ANKLE);

        // Neck/head — shoulder midpoint → ear midpoint.
        Vector3 shoulderMid = (avg[MP_L_SHOULDER] + avg[MP_R_SHOULDER]) * 0.5f;
        Vector3 earMid      = (avg[MP_L_EAR]      + avg[MP_R_EAR])      * 0.5f;
        Vector3 neckDir     = (earMid - shoulderMid).normalized;
        if (neckDir.sqrMagnitude > 0.01f) _tPoseDir[HumanBodyBones.Neck] = BlendDir(HumanBodyBones.Neck, neckDir);
    }

    void OnDestroy()
    {
        _landmarker?.Close();
        if (_webcam != null && _webcam.isPlaying) _webcam.Stop();
        if (_videoPlayer != null) _videoPlayer.Stop();
        if (_videoRT != null) _videoRT.Release();
    }
#else
    void Start()
    {
        Debug.LogWarning("[MediaPipePoseDetector] Inactive. Install the MediaPipe Unity Plugin, " +
                         "then add the scripting define symbol KINEX_MEDIAPIPE to enable pose tracking.");
    }

    // No-op when MediaPipe is disabled (e.g. Android build without KINEX_MEDIAPIPE). Keeps callers
    // such as CalibrationPanel compiling; the calibration properties stay at their Idle defaults.
    public void StartCalibration() { }
#endif
}
