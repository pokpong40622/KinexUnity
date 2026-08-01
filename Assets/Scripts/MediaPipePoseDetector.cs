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
using UnityEngine.InputSystem;   // AccelerometerSensor fallback for orientation (NOT legacy Input)
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.PoseLandmarker;
#endif

public class MediaPipePoseDetector : MonoBehaviour
{
    [Header("Model")]
    [Tooltip("MediaPipe model file placed under Assets/StreamingAssets/ (filename only). " +
             "'_lite' is markedly faster than '_full' for the gross-motor tracking these rehab " +
             "games need — prefer it unless a specific scene needs '_full's extra accuracy.")]
    [SerializeField] string modelFileName = "pose_landmarker_lite.bytes";

    [Header("Settings")]
    [Tooltip("Mirrors the React project's minPoseDetectionConfidence (0.5).")]
    [SerializeField][Range(0f, 1f)] float minDetectionConfidence = 0.5f;
    [Tooltip("LERP factor toward each new frame. React used 0.28 (smaller = smoother/laggier); 0.5 matches PoseDetector.cs default.")]
    [SerializeField][Range(0.05f, 0.8f)] float smoothingFactor = 0.5f;
    [Tooltip("Extra-strong smoothing for the knee/ankle landmarks (the noisiest). Lower = steadier " +
             "resting legs but slightly more lag on leg moves. This is what stops the still leg " +
             "jittering when the other leg lifts.")]
    [SerializeField][Range(0.05f, 0.5f)] float legSmoothing = 0.13f;
    [Tooltip("Use the 1€ filter (speed-adaptive smoothing — what MediaPipe itself ships) instead of " +
             "the plain EMA above for ALL landmark channels: steadier while holding still AND less lag " +
             "on fast moves. Opt-in per scene (Motion Lab turns it on); other games keep the proven EMA.")]
    [SerializeField] bool useOneEuroSmoothing = false;
    [Tooltip("1€ min cutoff (Hz). Lower = steadier at rest but laggier. MediaPipe ships 0.1 for world " +
             "landmarks; 0.05 biases toward stillness for slow rehab movement.")]
    [SerializeField][Range(0.01f, 1f)] float oneEuroMinCutoff = 0.05f;
    [Tooltip("1€ speed coefficient. Higher = snappier during fast motion. MediaPipe ships 40 (world).")]
    [SerializeField][Range(0f, 100f)] float oneEuroBeta = 20f;

    public bool UseOneEuroSmoothing => useOneEuroSmoothing;

    // One 1€ filter per channel: 33 image-space + 33 world landmarks × xyz, 17 COCO × xy.
    // Lazily created on first use so scenes with the flag off pay nothing.
    Kinex.Motion.OneEuroFilter[] _euro33, _euroWorld33, _euro2D;

    float EuroFilter(ref Kinex.Motion.OneEuroFilter[] bank, int size, int channel, float value, float dt)
    {
        bank ??= new Kinex.Motion.OneEuroFilter[size];
        var f = bank[channel] ??= new Kinex.Motion.OneEuroFilter(oneEuroMinCutoff, oneEuroBeta);
        return f.Filter(value, dt);
    }
    [Tooltip("Drive the avatar from keypoints. Off = only supply keypoints for scoring.")]
    [SerializeField] bool drivesAvatar = true;
    [Tooltip("A bone only drives while BOTH its endpoints are at least this visible. " +
             "Below it the bone freezes in place instead of following MediaPipe's guess — " +
             "so you can sit / be half out of frame and the upper body still tracks " +
             "without the legs flailing. Lower = tracks more but flails more. 0.3 matches PoseDetector.cs default.")]
    [SerializeField][Range(0f, 1f)] float minBoneVisibility = 0.3f;
    [Tooltip("Single visibility threshold for leg landmarks — used for BOTH the gate and the " +
             "actual drive (no mid-range freeze). Skeleton overlay draws at 0.4; keep this near " +
             "or below that so legs drive whenever the preview shows them. Lower = legs move more.")]
    [SerializeField][Range(0f, 1f)] float legVisThreshold = 0.3f;
    [Tooltip("Max degrees per second a LEG bone may rotate toward its target. Caps sudden " +
             "MediaPipe jumps so legs can't fly or snap-twist on a noisy frame. " +
             "Lower = steadier but laggier. This is the legs' main stabilizer.")]
    [SerializeField][Range(60f, 1080f)] float legMaxDegPerSec = 540f;
    [Tooltip("Max degrees/second ANY non-leg bone may rotate toward its target. Caps sudden " +
             "MediaPipe jumps so a noisy frame can't snap a limb to a broken angle ('bone crack'). " +
             "Legs use legMaxDegPerSec instead (they're noisier). Lower = steadier but laggier.")]
    [SerializeField][Range(120f, 1440f)] float maxDegPerSec = 720f;

    [Header("Test Source (leave EMPTY for normal webcam)")]
    [Tooltip("TEST ONLY: assign a video clip to feed it to pose detection instead of the live " +
             "webcam — handy when you can't stand in front of the camera. Clear this field to " +
             "go back to the real webcam.")]
    [SerializeField] UnityEngine.Video.VideoClip testVideoClip;

    [Tooltip("EDITOR/DESKTOP TEST ONLY: substring of the webcam device name to prefer (e.g. " +
             "\"DroidCam\", \"Iriun\", \"Camo\"). Case-insensitive. Lets Play-mode use a phone " +
             "virtual-cam instead of the laptop's built-in camera. The exact device names are " +
             "printed to the Console at start. Leave EMPTY to auto-pick the front camera. Ignored " +
             "on Android (the tablet uses its own camera).")]
    [SerializeField] string preferredCameraName = "";

    [Header("Avatar Axis Mapping (toggle LIVE in Play mode if a direction looks wrong)")]
    [Tooltip("The model's BACK faces the game camera (follow-behind), so your pose maps " +
             "DIRECTLY onto it — these only correct MediaPipe's raw axis signs vs Unity. " +
             "flipX = left/right (arms on wrong side), flipZ = front/back (leans the wrong way), " +
             "flipY = up/down. Defaults match the validated webcam setup.")]
    [SerializeField] bool flipX = false;  // cross-side arm/leg mapping already corrects laterality; flipX=true was double-mirroring
    [Tooltip("Swap the cross-side (mirror) limb mapping to SAME-side. For scenes that show the " +
             "avatar from BEHIND (TheDasher): viewed from the back, mirror semantics become " +
             "'follow me', so the player's right limb must drive the avatar's right limb. " +
             "Default OFF — front-facing scenes are unchanged.")]
    [SerializeField] bool sameSideRetarget = false;
    // Settable (not just gettable) so a scene's director can push the correct value at runtime —
    // TheDasher does this in Awake() to keep its master lateral-mirror flag (one Inspector bool)
    // in agreement with the lane/kick-side logic that reads the same raw landmarks. Purely a plain
    // field toggle: it only affects THIS detector instance, so other scenes/games are unaffected
    // unless they also choose to call the setter.
    public bool SameSideRetarget { get => sameSideRetarget; set => sameSideRetarget = value; }
    // TheDasher live-tuning: the LEGS can need different laterality than the arms (MediaPipe's
    // left/right side inference is unreliable on a mirrored front cam), so a single global flip
    // can't fix arms + legs together. These two XOR on top of the arm mapping so an on-screen
    // debug panel can correct the legs INDEPENDENTLY. Default OFF = legs follow the arm mapping,
    // exactly as before. Only affects THIS detector instance (other games unaffected).
    [SerializeField] bool legSideSwap = false;   // XOR into leg keypoint side-selection (which leg)
    [SerializeField] bool legFlipX = false;      // XOR into leg horizontal direction (leg pointing)
    public bool LegSideSwap { get => legSideSwap; set => legSideSwap = value; }
    public bool LegFlipX { get => legFlipX; set => legFlipX = value; }
    public void ToggleSameSide() => sameSideRetarget = !sameSideRetarget;
    public void ToggleLegSideSwap() => legSideSwap = !legSideSwap;
    public void ToggleLegFlipX() => legFlipX = !legFlipX;
    [SerializeField] bool flipY = true;   // Android front cam: body needs Y-flip to appear upright
    [SerializeField] bool flipZ = false;

    [Header("Head Tracking")]
    [Tooltip("Multiplier on the lateral (X) tilt of the head. 1 = raw, 2-3 = more responsive.")]
    [SerializeField][Range(1f, 5f)] float headTiltScale = 2.5f;

    [Header("Body Tracking")]
    [Tooltip("Amplifies lateral (X) and depth (Z) lean so small body tilts show clearly. 1 = raw.")]
    [SerializeField][Range(1f, 3f)] float bodyTiltScale = 1.5f;
    [Tooltip("How much of MediaPipe's DEPTH estimate (Z) to use when driving the avatar. The 2D " +
             "x/y landmarks (the skeleton) are reliable; the Z depth is a shaky single-camera guess " +
             "that makes the upper body twist. 0 = drive purely from the stable 2D plane (no weird " +
             "twisting); 1 = full 3D depth. Adjustable in-game via the Camera Setup panel.")]
    [SerializeField][Range(0f, 1f)] float depthStrength = 0f;
    [Tooltip("Lock the torso, legs, neck and head STRAIGHT and drive only the two arms. The arms are " +
             "the most reliable landmarks, so this avoids the torso twisting from shaky 3D depth. " +
             "Used by both games.")]
    [SerializeField] bool armsOnly = false;
    [Tooltip("Keep the CHEST + NECK at their rest pose instead of driving them from the head/ear " +
             "landmarks. The shoulder→ear vector is short and noisy, so it can over-rotate the upper " +
             "torso into a forward fold — very visible in a back-view game. ON for TheDasher; OFF " +
             "keeps the head/neck follow for front-facing games.")]
    [SerializeField] bool steadyUpperSpine = false;
    [Tooltip("Drive the avatar from MediaPipe 3D WORLD landmarks (metric, hip-centered — BlazePose " +
             "GHUM): full-body depth + real left/right rotation. OFF = the legacy 2D screen-plane drive " +
             "(flat, no real turn). Toggle live via the gear panel; persisted.")]
    [SerializeField] bool use3DWorld = true;
    [Tooltip("How strongly the avatar follows you (motion range). 1 = full; lower = gentler, less " +
             "wild extremes. Adjustable in-game (Sensitivity).")]
    [SerializeField][Range(0.2f, 1f)] float followStrength = 1f;
    [Tooltip("Leg follow strength as a FRACTION of followStrength. Legs are the noisiest landmarks, " +
             "so keep this below 1 to make them calmer / less twitchy. 1 = legs follow as hard as arms.")]
    [SerializeField][Range(0.1f, 1f)] float legSensitivity = 0.55f;
    [Tooltip("Let the avatar's hips sway side-to-side with you — translates the root horizontally " +
             "from your hip-center. Disabled in arms-only mode.")]
    [SerializeField] bool hipSway = true;
    [Tooltip("How far the hips translate horizontally (avatar local units). Higher = bigger sway. " +
             "0.5 was imperceptible on device — bumped to 2.5; tune live if it's now too much/little.")]
    [SerializeField] float hipSwayScale = 2.5f;
    [Header("3D depth — 2.5D hybrid")]
    [Tooltip("How much of MediaPipe's noisy world-DEPTH (z) to keep when driving limbs in 3D mode. " +
             "1 = full 3D (limbs foreshorten + lag toward the camera vs the flat 2D skeleton). " +
             "0 = flat like 2D. ~0.35 keeps enough depth for turn/side-on poses to score while the " +
             "limbs stay responsive + match the on-screen skeleton. Tune live in the Inspector.")]
    [SerializeField][Range(0f, 1f)] float worldDepthScale = 0.35f;

    [Header("Body Turn (yaw) — rotate the WHOLE body when you turn left/right")]
    [Tooltip("Turn the avatar's body to match you turning sideways. Derived from the two shoulders' " +
             "angle in the horizontal plane (asin of their depth gap). Heavily smoothed. One toggle " +
             "to disable if it ever flickers.")]
    [SerializeField] bool bodyTurn = true;
    [Tooltip("Amplify the detected turn. The front-cam depth tends to under-read the real turn, so >1.")]
    [SerializeField] float bodyTurnGain = 1.4f;
    [Tooltip("Clamp the turn so a bad frame can't spin the body past this many degrees. ~90 lets the " +
             "player turn far enough to match a side-on trainer pose (toe-touch/march/leg-raise/squat).")]
    [SerializeField] float bodyTurnMaxDeg = 92f;
    [Tooltip("EMA toward the new yaw each frame. LOW = smoother/steadier (kills flicker) but laggier.")]
    [SerializeField][Range(0.02f, 0.5f)] float bodyTurnSmoothing = 0.12f;
    [Tooltip("Ignore turns smaller than this (keeps the body dead-steady when you face forward).")]
    [SerializeField] float bodyTurnDeadzoneDeg = 6f;
    [Tooltip("Flip if the body turns the WRONG way (turn right → body turns left).")]
    [SerializeField] bool bodyTurnInvert = false;
    float _yawDeg;                         // smoothed body yaw in degrees
    Quaternion _bodyYaw = Quaternion.identity;
    [Tooltip("Degrees/second the whole body eases back to its rest pose when NOBODY is detected.")]
    [SerializeField] float restReturnDegPerSec = 300f;
    [Tooltip("Log periodic tracking values to the Android console (adb logcat) for debugging.")]
    [SerializeField] bool debugLog = true;
    float _lastDbg;
    float _lastHipDbg;

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

    // ---- Raw MediaPipe landmarks (image-space), for the Kinex World 3D puppet. ----
    // The reference web app builds its turning stick-figure directly from these points, so we
    // expose them verbatim: x,y in 0..1 image space; z is image-space depth (~same scale as x;
    // smaller/more-negative = closer to camera) which is what makes the puppet turn. Lightly
    // EMA-smoothed. All-zero until HasPose. PosePuppet3D reads this; nothing else depends on it.
    public struct NormLandmark { public float x, y, z, visibility; }
    public NormLandmark[] Landmarks33 => _landmarks33;
    readonly NormLandmark[] _landmarks33 = new NormLandmark[33];
    bool _landmarks33Init;

    // ---- Metric 3D WORLD landmarks (BlazePose GHUM), hip-centered, in meters: x right / y down /
    //      z toward-camera. EMA-smoothed (legs harder). All-zero until HasPose. Drives the 3D avatar. ----
    readonly NormLandmark[] _world33 = new NormLandmark[33];
    bool _world33Init;

    /// <summary>The driven avatar's humanoid Animator (this component lives on the avatar).
    /// Scoring bakes this rig and compares it to the trainer rig, so "avatar looks like the
    /// trainer = high score" — independent of camera mirror / skeleton-overlay orientation.</summary>
    public Animator AvatarAnimator => _animator;

    /// <summary>Turn the live pose puppet on/off. A game can disable it to pose the avatar itself
    /// (e.g. TheDasher plays a scripted sit crouch during the sit rep) and re-enable it after.</summary>
    public bool AvatarDriving { get => drivesAvatar; set => drivesAvatar = value; }

    // ---- Preview cover-crop, computed at runtime from the real webcam aspect so the
    //      skeleton overlay can map landmarks onto exactly what the preview shows. ----
    [Header("Preview")]
    [Tooltip("Mirror the camera preview like a selfie (matches the reference app).")]
    [SerializeField] bool mirrorPreview = true;
    [Tooltip("PREVIEW-ONLY clockwise rotation (degrees, multiple of 90). The Android front camera " +
             "delivers frames rotated, so the on-screen preview looks sideways. This rotates the preview " +
             "DISPLAY only — NOT the frame fed to MediaPipe (tracking is already correct). Applied on " +
             "Android only; ignored in the editor/desktop. Try 90 or 270 if the direction is wrong.")]
    [SerializeField] int previewRotationCW = 90;

    [Header("Orientation-aware correction (portrait flip)")]
    [Tooltip("Master switch. When ON, the preview, the skeleton overlay, and the frame fed to " +
             "MediaPipe all rotate to follow the tablet's PHYSICAL orientation, so flipping the tablet " +
             "180° (charging port on the other side) keeps the camera + skeleton upright to match " +
             "Flutter's both-ways portrait UI. Android only — editor/desktop are unchanged. The exact " +
             "behaviour of the embedded Unity surface under Flutter's portraitDown is UNVERIFIED, so " +
             "every knob below is adjustable and debugLog prints one [Orient] line per second — a " +
             "single on-device test is diagnostic.")]
    [SerializeField] bool orientationAware = true;
    [Tooltip("Rotate the on-screen PREVIEW + skeleton with the device orientation. Turn OFF if Flutter " +
             "already rotates the whole embedded Unity texture (then the preview would double-rotate). " +
             "Compare the [Orient] log's detected= against how the preview actually looks on device.")]
    [SerializeField] bool rotatePreviewWithOrientation = true;
    [Tooltip("Rotate the frame fed to MediaPipe with the device orientation so TRACKING stays upright " +
             "when flipped. Turn OFF if the [Orient] log shows videoRot already changes by ~180 on flip " +
             "(then the camera driver already compensates and this would over-rotate the tracking).")]
    [SerializeField] bool rotateTrackingWithOrientation = true;
    [Tooltip("When flipped 180°, also invert the selfie mirror. A 180° rotation usually keeps mirroring " +
             "correct, so default OFF; turn ON if the flipped preview puts your body on the wrong side.")]
    [SerializeField] bool flippedInvertMirror = false;
    [Tooltip("Also correct the two landscape ('on its side') orientations (90°/270°). The 180° portrait " +
             "flip is the priority; landscape is best-effort — flip the rotate toggles above off if it " +
             "comes out wrong on your tablet.")]
    [SerializeField] bool handleLandscape = true;
    [Tooltip("Screen.orientation may be pinned to Portrait when Unity is embedded under Flutter (it " +
             "can't see Flutter's rotation). If so, turn this ON to read orientation from the Input " +
             "System accelerometer gravity vector INSTEAD. Default OFF = trust Screen.orientation and " +
             "only fall back to the accelerometer when it reports Unknown/AutoRotation.")]
    [SerializeField] bool preferAccelerometer = false;

    public enum DeviceOrient { Portrait, PortraitFlipped, LandscapeLeft, LandscapeRight }
    DeviceOrient _orient = DeviceOrient.Portrait;
    int _extraDisplayCW;      // added to previewRotationCW + the skeleton's previewRotateCW
    int _extraFrameCW;        // added to the MediaPipe-fed frame rotation (keeps tracking upright)
    bool _extraInvertMirror;  // XOR into the preview + skeleton horizontal mirror
    float _lastOrientDbg;

    /// <summary>Extra clockwise degrees the skeleton overlay must ADD to its own previewRotateCW so it
    /// stays matched to the orientation-corrected preview. 0 unless the tablet is flipped / on its side
    /// (and orientationAware is on, Android only).</summary>
    public int OrientationExtraRotationCW => _extraDisplayCW;
    /// <summary>Whether the skeleton overlay must invert its horizontal mirror to match the
    /// orientation-corrected preview.</summary>
    public bool OrientationInvertMirror => _extraInvertMirror;

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
    // EDITOR TEST ONLY: when true, Update() skips all live capture and the avatar is driven purely
    // from InjectRecordedFrame() (a saved .json landmark recording). Used to replay the recorded
    // pose clips without a camera or a decodable video (see DasherTestClipSwitcher).
    bool _replayActive;
    // The live webcam feeds MediaPipe a BOTTOM-UP frame (flipY default is tuned for that); a test
    // VIDEO clip feeds an UPRIGHT frame, so the avatar's vertical convention must be inverted for
    // video only. Use this instead of flipY for avatar limb/torso Y direction. Replayed recordings
    // (InjectRecordedFrame) use the plain webcam convention — verified upright on screen.
    bool FlipYEff => flipY ^ UsingVideo;
    int SrcWidth  => UsingVideo ? _videoRT.width  : _webcam.width;
    int SrcHeight => UsingVideo ? _videoRT.height : _webcam.height;
    Texture2D _frame;                       // CPU copy fed to MediaPipe each tick
    Color32[] _rawBuffer;                   // reused GetPixels32 destination — was allocated fresh every frame
    Color32[] _rotateBuffer;                // reused RotateFlip destination — was allocated fresh every frame
    int _inferenceSkipCounter;              // throttles inference — see Update()'s InferenceThrottle
    Animator _animator;
    UnityEngine.UI.RawImage _rawImage;      // camera preview in the game UI
    bool _previewReady;

    // T-pose cache (same proven technique as PoseDetector.cs, now in 3D).
    readonly Quaternion[] _tPoseRot = new Quaternion[55];
    readonly Dictionary<HumanBodyBones, Vector3> _tPoseDir = new();
    // Pristine geometric directions (avatar bind T-pose). Kept so calibration can BLEND toward the
    // user's captured T-pose instead of fully replacing it — an imperfect T-pose can't then skew rest.
    readonly Dictionary<HumanBodyBones, Vector3> _geomDir = new();

    // Base hips local position captured at bind pose, so live hip-sway translates RELATIVE to it.
    Vector3 _hipsBaseLocalPos;
    bool _hipsBaseCached;

    // Smoothed 2D COCO-17 keypoints (normalized). This is what drives the avatar AND
    // what the skeleton overlay draws — one shared, stable screen-plane source.
    readonly Vector2[] _smoothed2D = new Vector2[17];
    readonly bool[] _has2D = new bool[17];
    // Live per-landmark visibility (0..1) from the latest frame. Used to freeze bones
    // whose joints are off-camera (e.g. legs when you sit) instead of driving them
    // from MediaPipe's extrapolated guess.
    readonly float[] _visibility = new float[33];

    void Start()
    {
        _animator = GetComponent<Animator>();
        CacheTpose();
        LoadFlips();

        LatestKeypoints = new Vector2[17];
        LatestConfidence = new float[17];

        EnableAccelerometerIfNeeded(); // orientation fallback (Task A) — safe no-op if no sensor

        // Only auto-find if nobody assigned one explicitly (coach mode calls SetPreviewSurface, which
        // may run before OR after this Start — don't clobber that assignment).
        if (_rawImage == null) _rawImage = FindAnyObjectByType<UnityEngine.UI.RawImage>();

        if (testVideoClip != null)
        {
            StartVideo(testVideoClip); // TEST ONLY: feed a recorded clip instead of the live webcam
        }
        else
        {
            // Pick the capture device at 640x480x30 — the SAME capture scale the reference app
            // requested. MediaPipe is fed the full frame (no pre-crop); cropping is preview-only.
            // Selection order: (1) preferredCameraName substring match (desktop phone-cam testing),
            // (2) the front-facing camera, (3) Unity's default device.
            var devices = WebCamTexture.devices;
            string chosen = null;
            if (!string.IsNullOrEmpty(preferredCameraName))
            {
                foreach (var d in devices)
                    if (d.name.IndexOf(preferredCameraName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { chosen = d.name; break; }
                if (chosen == null)
                    Debug.LogWarning($"[MediaPipePoseDetector] preferredCameraName '{preferredCameraName}' " +
                                     "matched no device — falling back to the front/default camera.");
            }
            if (chosen == null)
                foreach (var d in devices)
                    if (d.isFrontFacing) { chosen = d.name; break; }

            // Log every available device so the exact name to type into preferredCameraName is visible.
            var names = new System.Text.StringBuilder();
            foreach (var d in devices) names.Append($"'{d.name}'{(d.isFrontFacing ? "(front)" : "")} ");
            Debug.Log($"[MediaPipePoseDetector] Webcams: {(devices.Length == 0 ? "(none)" : names.ToString())}" +
                      $"→ using {(chosen != null ? $"'{chosen}'" : "default device 0")}.");

            _webcam = chosen != null
                ? new WebCamTexture(chosen, 640, 480, 30)
                : new WebCamTexture(640, 480, 30);
            _webcam.Play();
            if (_rawImage != null) _rawImage.texture = _webcam;
        }

        // Model load is async-capable so it works on Android, where StreamingAssets live inside the
        // APK and can't be read with System.IO. See InitLandmarker.
        StartCoroutine(InitLandmarker());
    }

    // TEST ONLY: (re)start the recorded-clip source, reusing the VideoPlayer/RenderTexture. Public so
    // DasherTestClipSwitcher can hot-swap clips at runtime (numpad). Stops the live webcam if running,
    // so UsingVideo becomes true from here on.
    public void SetTestClip(UnityEngine.Video.VideoClip clip)
    {
        if (clip == null) return;
        if (_rawImage == null) _rawImage = FindAnyObjectByType<UnityEngine.UI.RawImage>();
        if (_webcam != null && _webcam.isPlaying) _webcam.Stop();
        StartVideo(clip);
    }

    // EDITOR TEST ONLY: drive the avatar + detectors from ONE recorded frame of 33 image-space
    // MediaPipe landmarks (flat x,y,z,visibility ×33 — the upright/top-origin format that
    // tools/extract_landmarks.py writes, identical to what the live video path feeds MediaPipe).
    // Lets the recorded pose clips be replayed with NO camera and NO video decode (the tablet
    // clips don't decode in Windows Media Foundation). Populates the same public surface the live
    // path does; drives the 2D avatar path (a recording carries no metric GHUM world landmarks).
    public void InjectRecordedFrame(float[] v)
    {
        if (v == null || v.Length < 33 * 4) return;
        _replayActive = true;
        use3DWorld = false;   // no world landmarks in a recording → 2D screen-plane drive
        _world33Init = false;
        if (LatestKeypoints == null) LatestKeypoints = new Vector2[17];
        if (LatestConfidence == null) LatestConfidence = new float[17];

        for (int i = 0; i < 33; i++)
        {
            _landmarks33[i].x = v[i * 4 + 0];
            _landmarks33[i].y = v[i * 4 + 1];
            _landmarks33[i].z = v[i * 4 + 2];
            _landmarks33[i].visibility = v[i * 4 + 3];
        }
        _landmarks33Init = true;

        for (int i = 0; i < 17; i++) LatestConfidence[i] = 0f;
        foreach (var (mp, coco) in MP_TO_COCO)
        {
            LatestKeypoints[coco] = new Vector2(v[mp * 4 + 0], v[mp * 4 + 1]);
            LatestConfidence[coco] = v[mp * 4 + 3];
        }
        HasPose = true;
    }

    // EDITOR TEST ONLY: leave replay mode; live capture resumes on the next Update.
    public void StopReplay() { _replayActive = false; }

    void StartVideo(UnityEngine.Video.VideoClip clip)
    {
        if (_videoPlayer == null)
        {
            _videoPlayer = gameObject.AddComponent<UnityEngine.Video.VideoPlayer>();
            _videoPlayer.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            _videoPlayer.isLooping = true;
            _videoPlayer.playOnAwake = false;
        }
        _replayActive = false; // a real clip source takes over from any recording replay
        _videoPlayer.clip = clip;
        int w = (int)clip.width, h = (int)clip.height;
        if (_videoRT == null || _videoRT.width != w || _videoRT.height != h)
        {
            if (_videoRT != null) _videoRT.Release();
            _videoRT = new RenderTexture(w, h, 0);
        }
        _videoPlayer.targetTexture = _videoRT;
        _videoPlayer.Play();
        if (_rawImage != null) _rawImage.texture = _videoRT;
        Debug.Log($"[MediaPipePoseDetector] TEST MODE: feeding video '{clip.name}'.");
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

    // One-time log flag: true once we've logged the webcam orientation on first valid frame.
    bool _webcamOrientationLogged;

    // Rotate and optionally vertically flip a Color32 pixel buffer to produce an upright,
    // canonically-oriented frame for MediaPipe. WebCamTexture pixels are bottom-up (row 0 = image
    // bottom). The rotation is applied in that bottom-up coordinate space, then the result is also
    // bottom-up, which is consistent with how the unmodified path always fed frames.
    //
    // Identity guarantee: rotCW==0 && !vFlip → returns src unchanged (same pixel order, no copy).
    //
    // Dimension swap: 90° and 270° rotations swap width and height, as documented below.
    // `dst` is a caller-owned reusable buffer (was a fresh `new Color32[]` allocated every single
    // frame here — on Android, rot is normally 90/270, so this ran every frame on device) —
    // reallocated only when the required size actually changes.
    static Color32[] RotateFlip(Color32[] src, int w, int h, int rotCW, bool vFlip,
                                ref Color32[] dst, out int outW, out int outH)
    {
        // Normalise rotation to 0/90/180/270.
        rotCW = ((rotCW % 360) + 360) % 360;

        // Fast identity path — no allocation, no copy.
        if (rotCW == 0 && !vFlip) { outW = w; outH = h; return src; }

        // For 90/270° rotations the output dimensions are swapped.
        bool quarter = (rotCW == 90 || rotCW == 270);
        outW = quarter ? h : w;
        outH = quarter ? w : h;

        int needed = outW * outH;
        if (dst == null || dst.Length != needed) dst = new Color32[needed];

        // WebCamTexture pixels are bottom-up: pixel at (x, y) where y=0 is the bottom row of the
        // image lives at src[y * w + x]. We keep the same bottom-up convention in dst so the
        // downstream MediaPipe feed is consistent with the unmodified path.
        //
        // For each destination pixel (dx, dy) we compute the source pixel (sx, sy):
        //
        //   rotCW=90  (CW 90°):  sx = dy,          sy = (outW-1) - dx   → outW=h, outH=w
        //   rotCW=180:            sx = (w-1) - dx,  sy = (h-1) - dy
        //   rotCW=270 (CW 270°): sx = (outH-1)-dy,  sy = dx              → outW=h, outH=w
        // (For 90/270 the valid source bounds are sx∈[0,w-1]=[0,outH-1], sy∈[0,h-1]=[0,outW-1].)
        //
        // Vertical flip (vFlip): applied AFTER rotation by reflecting dy → (outH-1-dy) in dst space,
        // which is equivalent to negating sy (pre-flip) but simpler to reason about as a post-step.

        for (int dy = 0; dy < outH; dy++)
        {
            for (int dx = 0; dx < outW; dx++)
            {
                int sx, sy;
                switch (rotCW)
                {
                    case 90:
                        sx = dy;
                        sy = (outW - 1) - dx;
                        break;
                    case 180:
                        sx = (w - 1) - dx;
                        sy = (h - 1) - dy;
                        break;
                    case 270:
                        sx = (outH - 1) - dy;
                        sy = dx;
                        break;
                    default: // 0 — handled by fast path above, but safe fallback
                        sx = dx;
                        sy = dy;
                        break;
                }

                // Apply vertical flip: reflect the source row.
                if (vFlip) sy = (h - 1) - sy;

                dst[dy * outW + dx] = src[sy * w + sx];
            }
        }
        return dst;
    }

    // Runs full pose inference on 1 out of every N webcam frames the device delivers — a real
    // MediaPipe landmarker pass is synchronous/blocking on the main thread, and this is a slow
    // gross-motor rehab game (side-step/sit-stand/kick), not a twitch game, so halving the sample
    // rate costs no meaningful responsiveness. Skipped frames simply keep last frame's HasPose/
    // landmarks (this component just doesn't touch them — no separate "hold" logic needed).
    // Video-test replay tooling is left untouched (only the live webcam path is throttled).
    const int InferenceEveryNFrames = 2;

    void Update()
    {
        if (_replayActive) return; // driven by InjectRecordedFrame instead of live capture
        if (_landmarker == null) return;
        if (UsingVideo) { if (_videoPlayer == null || !_videoPlayer.isPrepared) return; }
        else
        {
            if (_webcam == null || !_webcam.didUpdateThisFrame) return;
            _inferenceSkipCounter++;
            if (_inferenceSkipCounter < InferenceEveryNFrames) return;
            _inferenceSkipCounter = 0;
        }

        UpdateOrientationCorrection();
        // Re-run the cover-crop the first time AND whenever the orientation correction changes (the
        // user flipped the tablet mid-session) — no longer latched forever by _previewReady alone.
        if (!_previewReady || _appliedDisplayCW != _extraDisplayCW || _appliedInvertMirror != _extraInvertMirror)
            TryApplyPreviewCrop();

        // ---- Feed the current source frame to MediaPipe. ----
        // NOTE: this is the one plugin-version-sensitive spot. If your installed plugin's
        // Image API differs, copy the feeding code from its PoseLandmarker sample scene.
        int srcW = SrcWidth, srcH = SrcHeight;
        if (UsingVideo)
        {
            // Video path (TEST ONLY — device always uses the webcam below). ReadPixels gives an
            // UPRIGHT frame, which is exactly what MediaPipe wants, so feed it as-is (NO flip —
            // flipping the image would make MediaPipe detect an upside-down person and mangle the
            // torso/limbs). The avatar's vertical convention is corrected separately via FlipYEff
            // (flipY is XOR'd with UsingVideo), since the webcam's flipY is tuned for its bottom-up frames.
            if (_frame == null || _frame.width != srcW || _frame.height != srcH)
                _frame = new Texture2D(srcW, srcH, TextureFormat.RGBA32, false);
            var prevActive = RenderTexture.active;
            RenderTexture.active = _videoRT;
            _frame.ReadPixels(new UnityEngine.Rect(0, 0, srcW, srcH), 0, 0);
            RenderTexture.active = prevActive;
            _frame.Apply();
        }
        else
        {
            // Webcam path: canonicalize the frame to UPRIGHT orientation before MediaPipe sees it.
            //
            // The Android front camera delivers sensor-rotated frames (videoRotationAngle is
            // typically 270°), so without this correction MediaPipe sees a sideways person and
            // the avatar limbs point wrong. We rotate the raw pixel buffer by the angle the
            // webcam reports and apply a vertical flip if videoVerticallyMirrored is true.
            //
            // IDENTITY CASE (laptop/editor): videoRotationAngle==0 && videoVerticallyMirrored==false
            // → RotateFlip returns the original array unchanged (no allocation, no copy) and
            //   _frame is allocated with the same srcW × srcH dimensions as before — pixel-identical
            //   to the old _frame.SetPixels32(_webcam.GetPixels32()) path. ✓
            // videoRotationAngle makes the sensor frame upright; _extraFrameCW adds the orientation
            // correction (Task A) so a 180° tablet flip doesn't feed MediaPipe an upside-down person.
            // _extraFrameCW is 0 on desktop/editor and in normal upright portrait, so the identity
            // case below is preserved exactly.
            int rot = _webcam.videoRotationAngle + _extraFrameCW;   // degrees CW to make the image upright
            bool vMirror = _webcam.videoVerticallyMirrored;

            // Reused destination buffers — GetPixels32() and RotateFlip used to each allocate a
            // fresh Color32[] (~300 KB combined for a 640x480 feed) every single frame on device.
            int rawNeeded = srcW * srcH;
            if (_rawBuffer == null || _rawBuffer.Length != rawNeeded) _rawBuffer = new Color32[rawNeeded];
            _webcam.GetPixels32(_rawBuffer);
            Color32[] canonical = RotateFlip(_rawBuffer, srcW, srcH, rot, vMirror,
                                             ref _rotateBuffer, out int canW, out int canH);

            // Log once when the webcam first delivers frames — lets on-device logcat reveal the
            // phone's actual rotation so we can verify the fix.
            if (!_webcamOrientationLogged)
            {
                Debug.Log($"[MediaPipePoseDetector] Webcam rot={rot} vMirror={vMirror} " +
                          $"canonical {canW}x{canH}");
                _webcamOrientationLogged = true;
            }

            // Reallocate _frame only when dimensions change (90/270° swaps w/h).
            if (_frame == null || _frame.width != canW || _frame.height != canH)
                _frame = new Texture2D(canW, canH, TextureFormat.RGBA32, false);

            _frame.SetPixels32(canonical);
            _frame.Apply();
        }

        using var image = new Image(ImageFormat.Types.Format.Srgba, _frame);
        var result = _landmarker.DetectForVideo(image, GetTimestampMs());

        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
        {
            HasPose = false;
            return;
        }

        var norm = result.poseLandmarks[0].landmarks;              // 33 × (x,y normalized)

        // Capture this frame's visibility (used as the per-keypoint confidence/gate) and the
        // raw image-space landmarks (x,y,z) for the 3D puppet — the latter lightly EMA-smoothed
        // since raw MediaPipe depth is jittery.
        for (int i = 0; i < norm.Count && i < 33; i++)
        {
            var l = norm[i];
            float vis = l.visibility ?? 1f;
            _visibility[i] = vis;

            var cur = new NormLandmark { x = l.x, y = l.y, z = l.z, visibility = vis };
            if (useOneEuroSmoothing)
            {
                float fdt = Time.deltaTime;
                _landmarks33[i].x = EuroFilter(ref _euro33, 33 * 3, i * 3 + 0, cur.x, fdt);
                _landmarks33[i].y = EuroFilter(ref _euro33, 33 * 3, i * 3 + 1, cur.y, fdt);
                _landmarks33[i].z = EuroFilter(ref _euro33, 33 * 3, i * 3 + 2, cur.z, fdt);
                _landmarks33[i].visibility = vis;
            }
            else if (_landmarks33Init)
            {
                // Legs (knees/ankles) are the noisiest landmarks — smooth them harder so the
                // puppet's legs stop flying around on jittery frames.
                bool isLeg = i == 25 || i == 26 || i == 27 || i == 28;
                float s = isLeg ? legSmoothing : smoothingFactor;
                _landmarks33[i].x = Mathf.Lerp(_landmarks33[i].x, cur.x, s);
                _landmarks33[i].y = Mathf.Lerp(_landmarks33[i].y, cur.y, s);
                _landmarks33[i].z = Mathf.Lerp(_landmarks33[i].z, cur.z, s);
                _landmarks33[i].visibility = vis;
            }
            else _landmarks33[i] = cur;
        }
        _landmarks33Init = true;

        // Build + smooth the COCO-17 2D keypoints — the SAME data the skeleton overlay
        // draws. The avatar is driven from this so it matches the on-screen skeleton 1:1.
        BuildCocoKeypoints(norm);

        // Capture + smooth the metric 3D WORLD landmarks (BlazePose GHUM) for the 3D driver. Same
        // EMA scheme as the image-space landmarks above (legs smoothed harder — they're noisiest).
        if (result.poseWorldLandmarks != null && result.poseWorldLandmarks.Count > 0)
        {
            var w = result.poseWorldLandmarks[0].landmarks;
            for (int i = 0; i < w.Count && i < 33; i++)
            {
                var l = w[i];
                float vis = _visibility[i]; // reuse this joint's normalized-landmark visibility
                if (useOneEuroSmoothing)
                {
                    float fdt = Time.deltaTime;
                    _world33[i].x = EuroFilter(ref _euroWorld33, 33 * 3, i * 3 + 0, l.x, fdt);
                    _world33[i].y = EuroFilter(ref _euroWorld33, 33 * 3, i * 3 + 1, l.y, fdt);
                    _world33[i].z = EuroFilter(ref _euroWorld33, 33 * 3, i * 3 + 2, l.z, fdt);
                    _world33[i].visibility = vis;
                }
                else if (_world33Init)
                {
                    bool isLeg = i == 25 || i == 26 || i == 27 || i == 28;
                    float s = isLeg ? legSmoothing : smoothingFactor;
                    _world33[i].x = Mathf.Lerp(_world33[i].x, l.x, s);
                    _world33[i].y = Mathf.Lerp(_world33[i].y, l.y, s);
                    _world33[i].z = Mathf.Lerp(_world33[i].z, l.z, s);
                    _world33[i].visibility = vis;
                }
                else _world33[i] = new NormLandmark { x = l.x, y = l.y, z = l.z, visibility = vis };
            }
            _world33Init = true;
        }

        HasPose = true;
    }

    // LateUpdate runs AFTER Unity's Animator evaluation, so our bone rotations
    // win over whatever the Animator's default/idle pose sets each frame.
    void LateUpdate()
    {
        if (!drivesAvatar) return;
        if (HasPose)
        {
            if (use3DWorld && _world33Init) ApplyToBones3D(); // BlazePose GHUM 3D world landmarks
            else ApplyToBones();                              // legacy 2D screen-plane fallback
        }
        else RestAll(); // nobody in frame → ease the body back to its rest pose
    }

    // Bones we puppet — used to ease everything home when tracking is lost.
    static readonly HumanBodyBones[] DrivenBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Neck,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
    };

    void RestAll()
    {
        if (_animator == null) return;
        foreach (var b in DrivenBones)
        {
            var t = _animator.GetBoneTransform(b);
            if (t != null)
                t.rotation = Quaternion.RotateTowards(t.rotation, _tPoseRot[(int)b],
                                                      restReturnDegPerSec * Time.deltaTime);
        }
        EaseHipsHome();
    }

    // ---- Orientation correction (Task A). Once per (inference) frame, work out how much extra
    //      rotation the preview, the skeleton overlay, and the MediaPipe-fed frame each need so that
    //      everything follows the tablet's physical orientation. All extras are 0 in normal upright
    //      portrait and on desktop/editor, so the default behaviour is byte-for-byte unchanged. ----
    void UpdateOrientationCorrection()
    {
        _orient = DetectOrientation();

        int extra; bool invert = false;
        switch (_orient)
        {
            case DeviceOrient.PortraitFlipped: extra = 180; invert = flippedInvertMirror; break;
            case DeviceOrient.LandscapeLeft:   extra = handleLandscape ? 90  : 0; break;
            case DeviceOrient.LandscapeRight:  extra = handleLandscape ? 270 : 0; break;
            default:                           extra = 0; break;   // Portrait (design orientation)
        }

        bool active = orientationAware;
#if !UNITY_ANDROID || UNITY_EDITOR
        active = false; // preview rotation is Android-only; leave editor/desktop exactly as before
#endif
        if (!active) { _extraDisplayCW = 0; _extraFrameCW = 0; _extraInvertMirror = false; }
        else
        {
            _extraDisplayCW    = rotatePreviewWithOrientation  ? extra : 0;
            _extraFrameCW      = rotateTrackingWithOrientation ? extra : 0;
            _extraInvertMirror = invert;
        }

        if (debugLog && Time.time - _lastOrientDbg > 1f)
        {
            _lastOrientDbg = Time.time;
            int va = _webcam != null ? _webcam.videoRotationAngle : 0;
            bool vm = _webcam != null && _webcam.videoVerticallyMirrored;
            Debug.Log($"[Orient] screen={Screen.orientation} {Screen.width}x{Screen.height} " +
                      $"detected={_orient} extraDisplayCW={_extraDisplayCW} extraFrameCW={_extraFrameCW} " +
                      $"invMirror={_extraInvertMirror} videoRot={va} vMirror={vm}");
        }
    }

    // Prefer Screen.orientation (a display API, allowed by CLAUDE.md); fall back to the Input System
    // accelerometer when Screen.orientation is Unknown/AutoRotation — or always, if preferAccelerometer
    // is set because the embedded surface pins Screen.orientation to Portrait.
    DeviceOrient DetectOrientation()
    {
        if (preferAccelerometer)
        {
            var g = ReadGravity();
            if (g.HasValue) return OrientFromGravity(g.Value);
        }
        switch (Screen.orientation)
        {
            case ScreenOrientation.Portrait:           return DeviceOrient.Portrait;
            case ScreenOrientation.PortraitUpsideDown: return DeviceOrient.PortraitFlipped;
            case ScreenOrientation.LandscapeLeft:      return DeviceOrient.LandscapeLeft;
            case ScreenOrientation.LandscapeRight:     return DeviceOrient.LandscapeRight;
        }
        var grav = ReadGravity();
        return grav.HasValue ? OrientFromGravity(grav.Value) : DeviceOrient.Portrait;
    }

    // Gravity from the Input System accelerometer (NOT legacy Input). Null if unavailable/near-zero.
    Vector3? ReadGravity()
    {
        var a = Accelerometer.current;
        if (a == null) return null;
        Vector3 v = a.acceleration.ReadValue();
        return v.sqrMagnitude < 1e-4f ? (Vector3?)null : v;
    }

    // Classify orientation from the gravity vector. Portrait-up reads ~ (0,-1,0); a 180° flip ~ (0,+1,0);
    // landscape has |x| dominant. The landscape left/right sign is a best-effort guess — verify on device.
    static DeviceOrient OrientFromGravity(Vector3 g)
    {
        if (Mathf.Abs(g.y) >= Mathf.Abs(g.x))
            return g.y <= 0f ? DeviceOrient.Portrait : DeviceOrient.PortraitFlipped;
        return g.x < 0f ? DeviceOrient.LandscapeLeft : DeviceOrient.LandscapeRight;
    }

    void EnableAccelerometerIfNeeded()
    {
        var a = Accelerometer.current;
        if (a != null && !a.enabled) InputSystem.EnableDevice(a);
    }

    // Cover-fit the landscape webcam into the (portrait) preview rect without distortion,
    // and expose the crop so PoseSkeletonOverlay maps landmarks onto the same visible region.
    //
    // Idempotent + re-runnable: the authored rect is captured once and restored at the top of every
    // call, so re-applying it on an orientation change (see Update) can't compound the size-swap or
    // the mirror flip below.
    bool _origRectCached;
    Vector2 _origAnchorMin, _origAnchorMax, _origPivot, _origSizeDelta, _origAnchoredPos;
    Vector3 _origLocalScale;
    int _appliedDisplayCW = int.MinValue;   // last _extraDisplayCW actually baked into the rect
    bool _appliedInvertMirror;

    void TryApplyPreviewCrop()
    {
        if (SrcWidth <= 16 || _rawImage == null) return;

        var rt = _rawImage.rectTransform;

        if (!_origRectCached)
        {
            _origAnchorMin = rt.anchorMin; _origAnchorMax = rt.anchorMax;
            _origPivot = rt.pivot; _origSizeDelta = rt.sizeDelta; _origAnchoredPos = rt.anchoredPosition;
            _origLocalScale = rt.localScale;
            _origRectCached = true;
        }
        // Restore the authored rect before (re)applying, so this method is idempotent.
        rt.anchorMin = _origAnchorMin; rt.anchorMax = _origAnchorMax;
        rt.pivot = _origPivot; rt.sizeDelta = _origSizeDelta; rt.anchoredPosition = _origAnchoredPos;
        rt.localScale = _origLocalScale;
        rt.localEulerAngles = Vector3.zero;

        // Preview-only rotation (Android front cam delivers rotated frames) PLUS the orientation
        // correction (_extraDisplayCW: +180 flipped, 90/270 on its side). A quarter turn swaps the
        // box's axes, so re-anchor to a centred, size-swapped rect BEFORE rotating, so the rotated
        // image fills the box instead of overflowing. Editor/desktop: rotCW stays 0.
        int rotCW = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
        rotCW = previewRotationCW + _extraDisplayCW;
#endif
        rotCW = ((rotCW % 360) + 360) % 360;
        bool quarter = (rotCW % 180) == 90;
        if (quarter)
        {
            Vector2 box = rt.rect.size;                       // current on-screen box size
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(box.y, box.x);         // swap W/H so a 90° turn fills the box
            rt.anchoredPosition = Vector2.zero;
        }
        rt.localEulerAngles = new Vector3(0f, 0f, -rotCW);    // negative Z = clockwise

        float camAspect = (float)SrcWidth / SrcHeight;
        UnityEngine.Rect r = rt.rect;                         // post-swap rect
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

        // Selfie mirror, inverted when the orientation correction calls for it (flipped 180°).
        bool mirror = mirrorPreview ^ _extraInvertMirror;
        if (mirror && !UsingVideo) // don't mirror a recorded clip — it's not a selfie
        {
            var s = rt.localScale;
            // After a quarter turn the screen-horizontal axis is the rect's local Y, so mirror that.
            if (quarter) s.y = -Mathf.Abs(s.y);
            else         s.x = -Mathf.Abs(s.x);
            rt.localScale = s;
        }

        _appliedDisplayCW = _extraDisplayCW;
        _appliedInvertMirror = _extraInvertMirror;
        _previewReady = true;
    }

    // Coach mode (MotionLabDirector) builds the camera preview RawImage at RUNTIME and hands it here,
    // because MotionLabScene has no authored preview. Safe regardless of Start() order: if this runs
    // BEFORE Start(), Start() keeps this surface (it only auto-finds when _rawImage is still null); if
    // Start() already ran, _webcam exists and we bind its texture now.
    public void SetPreviewSurface(UnityEngine.UI.RawImage img)
    {
        _rawImage = img;
        if (img != null)
        {
            if (_webcam != null)       img.texture = _webcam;
            else if (_videoRT != null) img.texture = _videoRT;
        }
        _origRectCached = false; // capture the new surface's authored rect
        _previewReady = false;   // re-run the cover-crop for it
    }

    // --- Runtime controls (driven by the in-game Camera Setup / gear panel) ---
    // The correct flips for the live Android FRONT camera (mirrored + sensor-rotated) can't be
    // known until the app runs on the device — the standalone webcam/test-video that "worked"
    // isn't mirrored the same way. So these are adjustable in-game and persisted across runs.
    const string PrefX = "kinex_flipX", PrefY = "kinex_flipY",
                 PrefSens = "kinex_sens", PrefArms = "kinex_armsonly",
                 PrefTrack3D = "kinex_use3dworld";

    public bool FlipX => flipX;
    public bool FlipY => flipY;
    public bool ArmsOnly => armsOnly;

    // Sensitivity = how strongly the avatar follows you. Down = gentler/less wild.
    public void SensDown() { followStrength = Mathf.Clamp(followStrength - 0.15f, 0.2f, 1f); PlayerPrefs.SetFloat(PrefSens, followStrength); PlayerPrefs.Save(); }
    public void SensUp()   { followStrength = Mathf.Clamp(followStrength + 0.15f, 0.2f, 1f); PlayerPrefs.SetFloat(PrefSens, followStrength); PlayerPrefs.Save(); }

    public void ToggleMirror() { flipX = !flipX; PlayerPrefs.SetInt(PrefX, flipX ? 1 : 0); PlayerPrefs.Save(); }
    public void ToggleUpDown() { flipY = !flipY; PlayerPrefs.SetInt(PrefY, flipY ? 1 : 0); PlayerPrefs.Save(); }
    public void Recalibrate()  { StartCalibration(); }

    // DEMO switch: flip between full-body 2D tracking and arms-only (locked body).
    public void ToggleArmsOnly() { armsOnly = !armsOnly; PlayerPrefs.SetInt(PrefArms, armsOnly ? 1 : 0); PlayerPrefs.Save(); }

    public bool Use3DWorld => use3DWorld;
    // A scene director can PIN the driving mode to the reliable 2D screen-plane path, so a stale
    // 3D PlayerPref from the in-game gear toggle can't override it (TheDasher does this — its avatar
    // is driven best from the same 2D keypoints the skeleton uses; GHUM world legs collapse when the
    // lower body is only marginally visible). When pinned, LoadFlips leaves use3DWorld alone.
    bool _driveModePinned;
    // Also forces FULL-BODY + FULL-STRENGTH and makes LoadFlips ignore the stale gear-panel
    // PlayerPrefs (kinex_armsonly / kinex_sens). On-device logcat proved the real leg/arm bug: a
    // stale kinex_armsonly=1 forced ARMS-ONLY mode every launch (so `if(!armsOnly)` skipped ALL leg
    // driving — the legs literally never moved), and a stale kinex_sens=0.40 pinned followStrength at
    // 40% (weak arms). Both carried over from a previous session's gear toggles. TheDasher needs
    // full-body, full-strength, so it pins them here and LoadFlips leaves them alone.
    public void PinDriveMode2D()
    {
        use3DWorld = false;
        armsOnly = false;
        followStrength = 0.85f; // TheDasher: slightly softer follow (user: "a little less sensitive")
        _driveModePinned = true;
    }
    // Smoothed whole-body yaw (degrees) the avatar is currently turned by. Used by the coach to
    // tell the player to turn toward a side-on trainer pose (yaw can't be seen in 2D image space).
    public float YawDeg => _yawDeg;
    // Flip between the 3D world-landmark driver and the 2D screen-plane fallback (default 2D).
    public void ToggleTracking3D() { use3DWorld = !use3DWorld; PlayerPrefs.SetInt(PrefTrack3D, use3DWorld ? 1 : 0); PlayerPrefs.Save(); }

    void LoadFlips()
    {
        // flipX and flipY are NO LONGER loaded from PlayerPrefs. The serialized Inspector defaults
        // (flipX=false, flipY=true) are the authoritative values for the Android front camera.
        // Stale keys written by previous builds (when flip=11 caused the arms to invert) are
        // deleted here so they can never override the serialized values again.
        PlayerPrefs.DeleteKey(PrefX);
        PlayerPrefs.DeleteKey(PrefY);

        // Sensitivity and arms-only mode are persisted (user-adjustable in-game) — but NOT when a
        // director has PINNED the mode (TheDasher). A stale kinex_armsonly=1 / kinex_sens=0.40 from a
        // previous session was forcing arms-only + weak arms on every launch (see PinDriveMode2D).
        // The live gear Sens+/- / ArmsOnly toggles still work within a session; they just no longer
        // carry a stale value into the next launch for a pinned scene.
        if (!_driveModePinned)
        {
            if (PlayerPrefs.HasKey(PrefSens)) followStrength = PlayerPrefs.GetFloat(PrefSens);
            if (PlayerPrefs.HasKey(PrefArms)) armsOnly = PlayerPrefs.GetInt(PrefArms) == 1;
        }

        // Tracking mode: DEFAULT 2D (flat screen-plane driver), but the player's last gear choice
        // persists across relaunches so the toggle actually sticks. (Previously we force-reset to 3D
        // every boot, which made the toggle look broken — 2D never survived a relaunch.)
        // If a director has PINNED 2D (PinDriveMode2D), leave use3DWorld alone so a stale 3D pref
        // can't override the pin.
        if (!_driveModePinned)
            use3DWorld = PlayerPrefs.HasKey(PrefTrack3D) ? PlayerPrefs.GetInt(PrefTrack3D) == 1 : false;
    }

    void BuildCocoKeypoints(IReadOnlyList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> norm)
    {
        // Read this frame's raw mapped joints; everything else stays low-confidence.
        var raw = new Vector2[17];
        var seen = new bool[17];
        for (int i = 0; i < 17; i++) LatestConfidence[i] = 0f;
        foreach (var (mp, coco) in MP_TO_COCO)
        {
            if (mp >= norm.Count) continue;
            var lm = norm[mp];
            raw[coco] = new Vector2(lm.x, lm.y);
            seen[coco] = true;
            // BlazePose normalized landmarks carry visibility ~ how confident/visible the joint is.
            LatestConfidence[coco] = lm.visibility ?? 1f;
        }

        // Smooth each seen joint to take the jitter out of the avatar + overlay (1€ when enabled,
        // else the legacy LERP EMA).
        for (int i = 0; i < 17; i++)
        {
            if (!seen[i]) { LatestKeypoints[i] = _smoothed2D[i]; continue; }
            if (useOneEuroSmoothing)
            {
                float fdt = Time.deltaTime;
                _smoothed2D[i] = new Vector2(
                    EuroFilter(ref _euro2D, 17 * 2, i * 2 + 0, raw[i].x, fdt),
                    EuroFilter(ref _euro2D, 17 * 2, i * 2 + 1, raw[i].y, fdt));
                _has2D[i] = true;
            }
            else if (!_has2D[i]) { _smoothed2D[i] = raw[i]; _has2D[i] = true; }
            else _smoothed2D[i] = Vector2.Lerp(_smoothed2D[i], raw[i], smoothingFactor);
            LatestKeypoints[i] = _smoothed2D[i];
        }
    }

    // ---- 2D bone driving (the proven PoseDetector.cs technique). ----
    // The avatar is driven from the SAME normalized 2D keypoints the skeleton overlay
    // draws (LatestKeypoints), so it matches the on-screen skeleton 1:1. No 3D depth is
    // used — single-camera depth was the shaky guess that twisted the body; we stay in
    // the stable screen plane. flipX/flipY (gear panel) correct mirroring + y-convention.
    void ApplyToBones()
    {
        var kp = LatestKeypoints;
        var conf = LatestConfidence;
        if (kp == null || conf == null) return;
        float t = minBoneVisibility;

        if (debugLog && Time.time - _lastDbg > 0.5f)
        {
            _lastDbg = Time.time;
            Vector2 rsh = kp[R_SHOULDER], rel = kp[R_ELBOW], rwr = kp[R_WRIST];
            Debug.Log("[PoseDebug2D] armsOnly=" + (armsOnly ? 1 : 0) +
                      " foll=" + followStrength.ToString("0.00") +
                      " legSens=" + legSensitivity.ToString("0.00") +
                      " flip=" + (flipX ? 1 : 0) + (flipY ? 1 : 0) +
                      " confRsh=" + conf[R_SHOULDER].ToString("0.00") +
                      " Rsh=" + rsh.ToString("F2") + " Rel=" + rel.ToString("F2") + " Rwr=" + rwr.ToString("F2") +
                      // Leg diagnostics for the side-kick (does the avatar leg move?): confidence gate +
                      // hip/knee/ankle positions. If confL/R < minBoneVisibility the leg is frozen home.
                      " | LEG confHip=" + conf[R_HIP].ToString("0.00") + " confKnee=" + conf[R_KNEE].ToString("0.00") +
                      " confAnk=" + conf[R_ANKLE].ToString("0.00") + " gate=" + minBoneVisibility.ToString("0.00") +
                      " Rhip=" + kp[R_HIP].ToString("F2") + " Rkne=" + kp[R_KNEE].ToString("F2") + " Rank=" + kp[R_ANKLE].ToString("F2"));
        }

        // Body turn (yaw) from the shoulders' horizontal-plane angle — applied to every driven bone
        // in DriveSegment2D so the whole body rotates coherently when you turn sideways.
        ComputeBodyYaw();

        if (armsOnly)
        {
            // Hold the whole body upright and steady — only the arms follow the user.
            LockBody();
        }
        else
        {
            // Torso: when steadyUpperSpine, keep the WHOLE torso (hips→spine→chest→neck) steady so
            // the avatar stays upright and front-facing — TheDasher wants limb motion (arms + legs
            // still follow below), NOT torso bend/turn (user: "just face the front, don't bend the
            // body too much"). Otherwise drive the full chain.
            if (steadyUpperSpine)
            {
                RestBone(HumanBodyBones.Hips);
                RestBone(HumanBodyBones.Spine);
                RestBone(HumanBodyBones.Chest);
                RestBone(HumanBodyBones.Neck);
            }
            else
            {
                // Hips/pelvis first (root): knee-mid → hip-mid captures pelvic tilt.
                TryDriveMidMid(HumanBodyBones.Hips,  kp, conf, L_KNEE, R_KNEE, L_HIP, R_HIP, t, followStrength);
                TryDriveMidMid(HumanBodyBones.Spine, kp, conf, L_HIP, R_HIP, L_SHOULDER, R_SHOULDER, t, followStrength);
                TryDriveMidMid(HumanBodyBones.Chest, kp, conf, L_SHOULDER, R_SHOULDER, L_EAR, R_EAR, t, followStrength);
                TryDriveMid  (HumanBodyBones.Neck,  kp, conf, L_SHOULDER, R_SHOULDER, NOSE, t, followStrength);
            }
        }

        // Arms — cross-side mapping so the avatar copies the user same-side (selfie/front cam).
        // On a front camera the image is already mirrored, so the COCO "left" shoulder is on
        // the user's RIGHT side in the frame. Driving avatar-Left from COCO-Right (and vice
        // versa) makes the avatar echo what the user is actually doing.
        // sameSideRetarget (back-view scenes) swaps which COCO side feeds each avatar limb.
        int aSho2 = sameSideRetarget ? L_SHOULDER : R_SHOULDER, aElb2 = sameSideRetarget ? L_ELBOW : R_ELBOW, aWri2 = sameSideRetarget ? L_WRIST : R_WRIST;
        int bSho2 = sameSideRetarget ? R_SHOULDER : L_SHOULDER, bElb2 = sameSideRetarget ? R_ELBOW : L_ELBOW, bWri2 = sameSideRetarget ? R_WRIST : L_WRIST;
        TryDrive(HumanBodyBones.LeftUpperArm,  kp, conf, aSho2, aElb2, t, followStrength);
        TryDrive(HumanBodyBones.LeftLowerArm,  kp, conf, aElb2, aWri2, t, followStrength);
        TryDrive(HumanBodyBones.RightUpperArm, kp, conf, bSho2, bElb2, t, followStrength);
        TryDrive(HumanBodyBones.RightLowerArm, kp, conf, bElb2, bWri2, t, followStrength);

        if (!armsOnly)
        {
            // Legs follow at a reduced strength (legSensitivity) — they're the noisiest joints.
            float legStr = followStrength * legSensitivity;
            bool legSame = sameSideRetarget ^ legSideSwap; // legs can differ from arms — see legSideSwap
            int aHip2 = legSame ? L_HIP : R_HIP, aKne2 = legSame ? L_KNEE : R_KNEE, aAnk2 = legSame ? L_ANKLE : R_ANKLE;
            int bHip2 = legSame ? R_HIP : L_HIP, bKne2 = legSame ? R_KNEE : L_KNEE, bAnk2 = legSame ? R_ANKLE : L_ANKLE;
            // Legs use their OWN (lower) visibility gate — the tablet reads the ankle at only ~0.1
            // confidence even during a clean kick (on-device logcat), so gating legs at the general
            // 0.25 froze the shin every frame. legVisThreshold lets the leg drive at low confidence
            // (noisier, but it actually MOVES — which is what the kick needs).
            float legT = legVisThreshold;
            TryDrive(HumanBodyBones.LeftUpperLeg,  kp, conf, aHip2, aKne2, legT, legStr);
            TryDrive(HumanBodyBones.LeftLowerLeg,  kp, conf, aKne2, aAnk2, legT, legStr);
            TryDrive(HumanBodyBones.RightUpperLeg, kp, conf, bHip2, bKne2, legT, legStr);
            TryDrive(HumanBodyBones.RightLowerLeg, kp, conf, bKne2, bAnk2, legT, legStr);
            ApplyHipSway(kp, conf, t);
        }
    }

    // Translate the hips (root) horizontally to mirror the user's side-to-side hip motion — the
    // lateral SWAY that bone-rotation alone can't show. Same X convention as the limbs (flipX).
    void ApplyHipSway(Vector2[] kp, float[] conf, float minConf)
    {
        if (!hipSway || !_hipsBaseCached) return;
        var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null) return;
        if (conf[L_HIP] < minConf || conf[R_HIP] < minConf) { EaseHipsHome(); return; }
        float cx = (kp[L_HIP].x + kp[R_HIP].x) * 0.5f;   // hip-center, 0..1 across the frame
        float off = (cx - 0.5f) * hipSwayScale;
        if (flipX) off = -off;
        Vector3 target = _hipsBaseLocalPos + new Vector3(off, 0f, 0f);
        hips.localPosition = Vector3.Lerp(hips.localPosition, target, 0.35f);
        if (debugLog && Time.time - _lastHipDbg > 0.5f)
        {
            _lastHipDbg = Time.time;
            Debug.Log($"[HipSway] cx={cx:F2} off={off:F3} scale={hipSwayScale} " +
                      $"hipsX={hips.localPosition.x:F3} baseX={_hipsBaseLocalPos.x:F3}");
        }
    }

    // Ease the hips back to their base position (no pose / arms-only / low confidence).
    void EaseHipsHome()
    {
        if (!_hipsBaseCached) return;
        var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null) hips.localPosition = Vector3.Lerp(hips.localPosition, _hipsBaseLocalPos, 0.2f);
    }

    // Snap the torso, legs, neck and head to their rest (bind) rotations so the body stays
    // upright and still — used by the arms-only switch. Arms are driven separately afterward.
    static readonly HumanBodyBones[] BodyBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
    };

    void LockBody()
    {
        foreach (var b in BodyBones)
        {
            var t = _animator.GetBoneTransform(b);
            if (t != null) t.rotation = _tPoseRot[(int)b];
        }
        if (_hipsBaseCached)
        {
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) hips.localPosition = _hipsBaseLocalPos;
        }
    }

    // strength = how strongly to drive this bone (followStrength for arms/torso; reduced by
    // legSensitivity for legs). On low confidence the bone eases back to rest (RestBone) instead
    // of freezing where it last was.
    void TryDrive(HumanBodyBones bone, Vector2[] kp, float[] conf, int from, int to, float minConf, float strength)
    {
        if (conf[from] < minConf || conf[to] < minConf) { RestBone(bone); return; }
        DriveSegment2D(bone, kp[from], kp[to], strength);
    }

    void TryDriveMid(HumanBodyBones bone, Vector2[] kp, float[] conf, int fromA, int fromB, int to, float minConf, float strength)
    {
        if (conf[fromA] < minConf || conf[fromB] < minConf || conf[to] < minConf) { RestBone(bone); return; }
        DriveSegment2D(bone, (kp[fromA] + kp[fromB]) * 0.5f, kp[to], strength);
    }

    void TryDriveMidMid(HumanBodyBones bone, Vector2[] kp, float[] conf, int fromA, int fromB, int toA, int toB, float minConf, float strength)
    {
        if (conf[fromA] < minConf || conf[fromB] < minConf || conf[toA] < minConf || conf[toB] < minConf) { RestBone(bone); return; }
        DriveSegment2D(bone, (kp[fromA] + kp[fromB]) * 0.5f, (kp[toA] + kp[toB]) * 0.5f, strength);
    }

    // The proven 2D drive: build a screen-plane direction (z=0) from two keypoints and
    // rotate the bone from its rest direction to match (same rotation math + full-strength
    // rotation + pure-geometric T-pose as PoseDetector.cs). followStrength scales the motion
    // range (Sensitivity). NOTE: the direction is NOT negated by default — MediaPipe's
    // normalized image landmarks already use the y-down/x convention that maps directly to the
    // avatar (verified in-editor: negating inverts the body head-down). The gear-panel buttons
    // (ToggleMirror / ToggleUpDown) flip X / Y to correct the live front-camera mirror on device.
    void DriveSegment2D(HumanBodyBones bone, Vector2 from2D, Vector2 to2D, float strength)
    {
        if (!_tPoseDir.ContainsKey(bone)) return;
        Transform t = _animator.GetBoneTransform(bone);
        if (t == null) return;

        bool fx = flipX; if (IsLegBone(bone)) fx ^= legFlipX; // legs can flip direction independently
        float dx = to2D.x - from2D.x; if (fx) dx = -dx;
        float dy = to2D.y - from2D.y; if (FlipYEff) dy = -dy;
        Vector3 screenTarget = new Vector3(dx, dy, 0f);
        if (screenTarget.sqrMagnitude < 0.01f) return;
        // De-yaw the screen-plane target into the body-local (un-turned) frame BEFORE matching the bone,
        // then re-apply _bodyYaw — exactly like DriveSegment3D. Without this de-yaw the bend was matched
        // in the un-rotated frame and then spun by _bodyYaw, so "turn right + bend forward" bent the
        // WRONG side. With it, the bend stays correct regardless of how far the body is turned.
        Vector3 target = (Quaternion.Inverse(_bodyYaw) * screenTarget).normalized;

        Quaternion full = Quaternion.FromToRotation(_tPoseDir[bone], target) * _tPoseRot[(int)bone];
        // Pre-multiply the body yaw so this bone turns WITH the body (all driven bones get the same
        // yaw → the figure rotates coherently). Identity when bodyTurn is off / facing forward.
        Quaternion desired = _bodyYaw * Quaternion.Slerp(_tPoseRot[(int)bone], full, strength);
        // Rate-limit toward the target so a single noisy MediaPipe frame can't snap the limb to a
        // broken angle ('bone crack'). Legs get their own (lower) cap — they're the noisiest joints.
        float cap = (IsLegBone(bone) ? legMaxDegPerSec : maxDegPerSec) * Time.deltaTime;
        t.rotation = Quaternion.RotateTowards(t.rotation, desired, cap);
    }

    // Body yaw from the two shoulders: their vector in the horizontal (x–z) plane tilts as you turn,
    // so asin(Δz / shoulderSpan) IS the yaw angle. MediaPipe's z uses ~the same scale as x, so this
    // is geometrically meaningful. Heavily smoothed + deadzoned; eases to 0 when shoulders aren't
    // clearly visible. Result drives _bodyYaw (rotation about world-up).
    void ComputeBodyYaw()
    {
        if (!bodyTurn) { _yawDeg = 0f; _bodyYaw = Quaternion.identity; return; }

        var a = _landmarks33[MP_L_SHOULDER];
        var b = _landmarks33[MP_R_SHOULDER];
        float raw;
        if (a.visibility < minBoneVisibility || b.visibility < minBoneVisibility)
        {
            raw = 0f; // shoulders unclear → ease back to facing forward
        }
        else
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z; // restored: negating this (a speculative "match-3D" tweak) flipped the 2D turn the WRONG way on device
            float span = Mathf.Sqrt(dx * dx + dz * dz);
            raw = span > 1e-4f ? Mathf.Asin(Mathf.Clamp(dz / span, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            raw *= bodyTurnGain;
            if (flipX) raw = -raw;
            if (bodyTurnInvert) raw = -raw;
            raw = Mathf.Clamp(raw, -bodyTurnMaxDeg, bodyTurnMaxDeg);
            if (Mathf.Abs(raw) < bodyTurnDeadzoneDeg) raw = 0f;
        }

        _yawDeg = Mathf.Lerp(_yawDeg, raw, bodyTurnSmoothing);
        _bodyYaw = Quaternion.AngleAxis(_yawDeg, Vector3.up);
    }

    // ==================== 3D WORLD-LANDMARK DRIVING (BlazePose GHUM) ====================
    // Same FK as the 2D path (rotate each bone from its bind direction to the segment direction),
    // but the target is a FULL 3D vector from the metric world landmarks — so depth and a limb
    // pointing toward/away from the camera are represented, and the body reads as truly turned, not
    // flat. The whole figure yaws by _bodyYaw; per-bone targets are de-yawed first so the turn is
    // applied exactly once (no double-rotation). Mirrors ApplyToBones() structure 1:1.
    void ApplyToBones3D()
    {
        if (_animator == null || !_world33Init) return;

        ComputeBodyYaw3D();

        if (debugLog && Time.time - _lastDbg > 0.5f)
        {
            _lastDbg = Time.time;
            // 3D-path leg diagnostics for the side-kick: world-landmark visibility gate + knee/ankle.
            // If visKnee/visAnk < minBoneVisibility the avatar leg is frozen home (won't kick).
            Debug.Log("[PoseDebug3D] legSens=" + legSensitivity.ToString("0.00") +
                      " gate=" + minBoneVisibility.ToString("0.00") + " depthScale=" + worldDepthScale.ToString("0.00") +
                      " visHip=" + _world33[MP_R_HIP].visibility.ToString("0.00") +
                      " visKnee=" + _world33[MP_R_KNEE].visibility.ToString("0.00") +
                      " visAnk=" + _world33[MP_R_ANKLE].visibility.ToString("0.00") +
                      " Rknee=" + World(MP_R_KNEE).ToString("F2") + " Rank=" + World(MP_R_ANKLE).ToString("F2"));
        }

        if (armsOnly)
        {
            LockBody();
        }
        else
        {
            // Mirror of the 2D path: steadyUpperSpine keeps the whole torso upright/front-facing
            // (only arms + legs follow); otherwise drive the full chain.
            if (steadyUpperSpine)
            {
                RestBone(HumanBodyBones.Hips);
                RestBone(HumanBodyBones.Spine);
                RestBone(HumanBodyBones.Chest);
                RestBone(HumanBodyBones.Neck);
            }
            else
            {
                TryDriveMidMid3D(HumanBodyBones.Hips,  MP_L_KNEE, MP_R_KNEE, MP_L_HIP, MP_R_HIP, followStrength);
                TryDriveMidMid3D(HumanBodyBones.Spine, MP_L_HIP, MP_R_HIP, MP_L_SHOULDER, MP_R_SHOULDER, followStrength);
                TryDriveMidMid3D(HumanBodyBones.Chest, MP_L_SHOULDER, MP_R_SHOULDER, MP_L_EAR, MP_R_EAR, followStrength);
                TryDriveMid3D  (HumanBodyBones.Neck,  MP_L_SHOULDER, MP_R_SHOULDER, MP_NOSE, followStrength);
            }
        }

        // Arms — cross-side (selfie/front cam) by default: avatar-Left from MediaPipe-Right.
        // sameSideRetarget (back-view scenes) swaps to same-side so the avatar 'follows' instead.
        int aSho = sameSideRetarget ? MP_L_SHOULDER : MP_R_SHOULDER, aElb = sameSideRetarget ? MP_L_ELBOW : MP_R_ELBOW, aWri = sameSideRetarget ? MP_L_WRIST : MP_R_WRIST;
        int bSho = sameSideRetarget ? MP_R_SHOULDER : MP_L_SHOULDER, bElb = sameSideRetarget ? MP_R_ELBOW : MP_L_ELBOW, bWri = sameSideRetarget ? MP_R_WRIST : MP_L_WRIST;
        TryDrive3D(HumanBodyBones.LeftUpperArm,  aSho, aElb, followStrength);
        TryDrive3D(HumanBodyBones.LeftLowerArm,  aElb, aWri, followStrength);
        TryDrive3D(HumanBodyBones.RightUpperArm, bSho, bElb, followStrength);
        TryDrive3D(HumanBodyBones.RightLowerArm, bElb, bWri, followStrength);

        if (!armsOnly)
        {
            float legStr = followStrength * legSensitivity;
            int aHip = sameSideRetarget ? MP_L_HIP : MP_R_HIP, aKne = sameSideRetarget ? MP_L_KNEE : MP_R_KNEE, aAnk = sameSideRetarget ? MP_L_ANKLE : MP_R_ANKLE;
            int bHip = sameSideRetarget ? MP_R_HIP : MP_L_HIP, bKne = sameSideRetarget ? MP_R_KNEE : MP_L_KNEE, bAnk = sameSideRetarget ? MP_R_ANKLE : MP_L_ANKLE;
            TryDrive3D(HumanBodyBones.LeftUpperLeg,  aHip, aKne, legStr);
            TryDrive3D(HumanBodyBones.LeftLowerLeg,  aKne, aAnk, legStr);
            TryDrive3D(HumanBodyBones.RightUpperLeg, bHip, bKne, legStr);
            TryDrive3D(HumanBodyBones.RightLowerLeg, bKne, bAnk, legStr);
            ApplyHipSway(LatestKeypoints, LatestConfidence, minBoneVisibility); // sway still from the stable 2D hip-center
        }
    }

    Vector3 World(int i) => new Vector3(_world33[i].x, _world33[i].y, _world33[i].z);

    void TryDrive3D(HumanBodyBones bone, int from, int to, float strength)
    {
        if (_world33[from].visibility < minBoneVisibility || _world33[to].visibility < minBoneVisibility) { RestBone(bone); return; }
        DriveSegment3D(bone, World(from), World(to), strength);
    }

    void TryDriveMid3D(HumanBodyBones bone, int fromA, int fromB, int to, float strength)
    {
        if (_world33[fromA].visibility < minBoneVisibility || _world33[fromB].visibility < minBoneVisibility ||
            _world33[to].visibility < minBoneVisibility) { RestBone(bone); return; }
        DriveSegment3D(bone, (World(fromA) + World(fromB)) * 0.5f, World(to), strength);
    }

    void TryDriveMidMid3D(HumanBodyBones bone, int fromA, int fromB, int toA, int toB, float strength)
    {
        if (_world33[fromA].visibility < minBoneVisibility || _world33[fromB].visibility < minBoneVisibility ||
            _world33[toA].visibility < minBoneVisibility || _world33[toB].visibility < minBoneVisibility) { RestBone(bone); return; }
        DriveSegment3D(bone, (World(fromA) + World(fromB)) * 0.5f, (World(toA) + World(toB)) * 0.5f, strength);
    }

    // Build a FULL 3D direction (MediaPipe world axes → Unity, corrected by flipX/Y/Z), de-yaw it so
    // the whole-body turn (_bodyYaw) isn't double-applied, rotate the bone to match, then re-apply the
    // body yaw so every driven bone turns together coherently.
    void DriveSegment3D(HumanBodyBones bone, Vector3 from, Vector3 to, float strength)
    {
        if (!_tPoseDir.ContainsKey(bone)) return;
        Transform t = _animator.GetBoneTransform(bone);
        if (t == null) return;

        Vector3 d = to - from;
        float dx = d.x; if (flipX) dx = -dx;
        float dy = d.y; if (FlipYEff) dy = -dy;
        float dz = d.z; if (flipZ) dz = -dz;
        // 2.5D hybrid: MediaPipe's world DEPTH (z) is noisy and makes a side-extended limb point
        // toward/away from the camera, so from the front view it looks foreshortened + laggy vs the
        // flat 2D skeleton. Shrink only the per-limb depth (the whole-body turn _bodyYaw stays full,
        // so side-on poses still turn + score). worldDepthScale=1 → full 3D, 0 → flat like 2D.
        dz *= worldDepthScale;
        Vector3 worldTarget = new Vector3(dx, dy, dz);
        if (worldTarget.sqrMagnitude < 1e-6f) return;
        Vector3 target = (Quaternion.Inverse(_bodyYaw) * worldTarget).normalized;

        Quaternion full = Quaternion.FromToRotation(_tPoseDir[bone], target) * _tPoseRot[(int)bone];
        Quaternion desired = _bodyYaw * Quaternion.Slerp(_tPoseRot[(int)bone], full, strength);
        // Rate-limit toward the target (see DriveSegment2D) so a noisy frame can't 'bone-crack' a limb.
        float cap = (IsLegBone(bone) ? legMaxDegPerSec : maxDegPerSec) * Time.deltaTime;
        t.rotation = Quaternion.RotateTowards(t.rotation, desired, cap);
    }

    // Which way are you facing? Detect it from the skeleton: how far the shoulder line (and hip line)
    // have rotated OUT of the camera-facing plane. asin(depthGap / span) is 0 when you face the camera
    // (shoulders span x, no depth gap) and ±90° when you're fully sideways — so REST = 0 (no constant
    // offset, unlike the old atan2 which read ~90° when Δx was negative on a mirrored cam), and the sign
    // tells left vs right. Averaged over shoulders + hips for stability. Heavily smoothed + deadzoned.
    void ComputeBodyYaw3D()
    {
        if (!bodyTurn) { _yawDeg = 0f; _bodyYaw = Quaternion.identity; return; }

        float sy = YawFromLine3D(MP_L_SHOULDER, MP_R_SHOULDER);
        float hy = YawFromLine3D(MP_L_HIP, MP_R_HIP);
        int n = 0; float sum = 0f;
        if (!float.IsNaN(sy)) { sum += sy; n++; }
        if (!float.IsNaN(hy)) { sum += hy; n++; }

        float raw;
        if (n == 0) raw = 0f; // can't see shoulders or hips → ease back to facing forward
        else
        {
            raw = (sum / n) * bodyTurnGain;
            if (flipX) raw = -raw;
            if (bodyTurnInvert) raw = -raw;
            raw = Mathf.Clamp(raw, -bodyTurnMaxDeg, bodyTurnMaxDeg);
            if (Mathf.Abs(raw) < bodyTurnDeadzoneDeg) raw = 0f;
        }

        _yawDeg = Mathf.Lerp(_yawDeg, raw, bodyTurnSmoothing);
        _bodyYaw = Quaternion.AngleAxis(_yawDeg, Vector3.up);
    }

    // Facing angle (deg) of a left→right landmark line out of the camera plane: asin(depth / span).
    // 0 = the line faces the camera, ±90 = edge-on (you're sideways). NaN if an endpoint isn't visible.
    float YawFromLine3D(int li, int ri)
    {
        var l = _world33[li]; var r = _world33[ri];
        if (l.visibility < minBoneVisibility || r.visibility < minBoneVisibility) return float.NaN;
        float dx = r.x - l.x;
        float dz = r.z - l.z;
        float span = Mathf.Sqrt(dx * dx + dz * dz);
        return span > 1e-4f ? Mathf.Asin(Mathf.Clamp(dz / span, -1f, 1f)) * Mathf.Rad2Deg : 0f;
    }

    // Ease a single bone back toward its rest (bind T-pose) rotation. Used when a joint is
    // not confidently detected this frame: instead of freezing the bone at its last driven
    // angle (which leaves a limb stuck where it was when tracking dropped), we relax it home,
    // so an off-camera / occluded leg or arm returns to the default pose. (Round 11.)
    void RestBone(HumanBodyBones bone)
    {
        // Undetected part → ease it back to its default rest pose (user: "if it didn't detect a
        // part of the body, set that to default position"). Arms ease at HALF rate: a brief wrist
        // self-occlusion during play (arm แนบตัว / hanging by the side) shouldn't snap the arm out
        // to the T-rest, but a sustained loss still returns it to the default stance like the legs.
        var t = _animator.GetBoneTransform(bone);
        if (t == null) return;
        float rate = IsArmBone(bone) ? restReturnDegPerSec * 0.5f : restReturnDegPerSec;
        t.rotation = Quaternion.RotateTowards(t.rotation, _bodyYaw * _tPoseRot[(int)bone],
                                              rate * Time.deltaTime);
    }

    static bool IsArmBone(HumanBodyBones b) =>
        b == HumanBodyBones.LeftUpperArm || b == HumanBodyBones.LeftLowerArm ||
        b == HumanBodyBones.RightUpperArm || b == HumanBodyBones.RightLowerArm;

    static bool IsLegBone(HumanBodyBones b) =>
        b == HumanBodyBones.LeftUpperLeg || b == HumanBodyBones.LeftLowerLeg ||
        b == HumanBodyBones.RightUpperLeg || b == HumanBodyBones.RightLowerLeg;

    // ---- T-pose caching (identical approach to PoseDetector.cs:194). ----
    void CacheTpose()
    {
        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            Transform t = _animator.GetBoneTransform(bone);
            if (t != null) _tPoseRot[(int)bone] = t.rotation;
        }
        var hipsBase = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hipsBase != null) { _hipsBaseLocalPos = hipsBase.localPosition; _hipsBaseCached = true; }
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

        // Average the 2D COCO keypoints over calibrationDuration seconds (per-joint, only
        // while that joint is confidently visible) — these feed the 2D rest directions.
        var acc = new Vector2[17];
        var cnt = new int[17];
        int sampleCount = 0;
        float remaining = calibrationDuration;
        CalibrationPhase = CalibState.Counting;

        while (remaining > 0f)
        {
            CalibrationCountdown = remaining;
            if (HasPose)
            {
                for (int i = 0; i < 17; i++)
                    if (LatestConfidence[i] >= minBoneVisibility) { acc[i] += LatestKeypoints[i]; cnt[i]++; }
                sampleCount++;
            }
            yield return null;
            remaining -= Time.deltaTime;
        }
        CalibrationCountdown = 0f;

        // RecalibrateTPoseDirs2D is now a no-op — _tPoseDir is driven purely from the geometric
        // bind T-pose (CacheTpose). We always set IsCalibrated=true so MegaDanceManager and
        // KinexWorldDirector can proceed even when no one was in frame during the countdown.
        if (sampleCount >= 10)
        {
            for (int i = 0; i < 17; i++) if (cnt[i] > 0) acc[i] /= cnt[i];
            RecalibrateTPoseDirs2D(acc, cnt); // no-op — kept for symmetry
            Debug.Log($"[Calibration] Done — averaged {sampleCount} frames (2D). Rest dirs from bind T-pose.");
        }
        else
        {
            Debug.LogWarning($"[Calibration] Too few frames ({sampleCount}); proceeding with bind T-pose dirs. " +
                             "Make sure the full body is in frame next time.");
        }
        IsCalibrated = true;

        CalibrationPhase = CalibState.Done;
        yield return new WaitForSeconds(2f);
        CalibrationPhase = CalibState.Idle;
        _calibCoroutine = null;
    }

    // No-op: _tPoseDir is set once by CacheTpose() from the avatar's geometric bind pose and
    // never mutated at runtime. The old live-recalibration path (BlendDir / captured T-pose
    // averaging) caused tracking inaccuracy when the user's stance wasn't a perfect T, so it
    // was removed. The calibration state machine still runs (CalibrationRoutine still fires) so
    // MegaDanceManager.CalibrateThenStart and KinexWorldDirector.Calibrate keep working; this
    // method is called after the countdown but does nothing to rest directions.
    void RecalibrateTPoseDirs2D(Vector2[] avg, int[] cnt) { }

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

    // No-op stub so MotionLabDirector's coach mode compiles without MediaPipe.
    public void SetPreviewSurface(UnityEngine.UI.RawImage img) { }
#endif
}
