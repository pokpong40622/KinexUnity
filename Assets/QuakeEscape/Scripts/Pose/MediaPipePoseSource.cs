using System;
using System.Collections;
using UnityEngine;

using Mediapipe;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;
using TextureFrame = Mediapipe.Unity.Experimental.TextureFrame;

namespace Collapse
{
    /// <summary>
    /// Drives a front-facing WebCamTexture through MediaPipe's PoseLandmarker (Tasks API,
    /// LIVE_STREAM mode) and exposes a smoothed, hysteresis-gated PoseState via IPoseSource.
    ///
    /// Threading: the MediaPipe result callback fires on a background thread. It must never touch
    /// Unity APIs. Results are copied into a lock-guarded "latest wins" buffer and drained on
    /// LateUpdate (main thread), matching the pattern used by the plugin's own
    /// AnnotationController/PoseLandmarkerResultAnnotationController.
    /// </summary>
    public class MediaPipePoseSource : MonoBehaviour, IPoseSource
    {
        [SerializeField] private string modelFileName = "pose_landmarker_lite.bytes";
        [SerializeField] private int webcamWidth = 640;
        [SerializeField] private int webcamHeight = 480;
        [SerializeField] private int webcamFps = 30;
        [SerializeField] private float smoothing = 0.5f;
        [SerializeField] private int requiredStableFrames = 3;

        private WebCamTexture webCamTexture;
        private PoseLandmarker poseLandmarker;
        private TextureFrame textureFrame;

        // Background-thread -> main-thread handoff. Only ever touched under resultLock.
        private readonly object resultLock = new object();
        private PoseSnapshot pendingSnapshot;
        private bool pendingDirty;
        private long pendingTimestampMs;

        // Main-thread-only state.
        private PoseSnapshot smoothedSnapshot;
        private bool hasSmoothedSnapshot;
        private long lastResultReceivedAtMs = long.MinValue;

        private PoseBaseline baseline;

        private PoseState candidatePose = PoseState.None;
        private int candidateStreak;
        private PoseState currentPose = PoseState.None;

        public bool HasBaseline => baseline.isSet;
        public WebCamTexture PreviewTexture => webCamTexture;
        public PoseSnapshot LatestSnapshot { get; private set; }
        public bool BodyVisible { get; private set; }

        public PoseState CurrentPose => currentPose;

        public bool IsTracking =>
            poseLandmarker != null &&
            webCamTexture != null && webCamTexture.isPlaying &&
            hasSmoothedSnapshot &&
            (NowUnixMs() - lastResultReceivedAtMs) < 1000 &&
            BodyVisible;

        private void Start()
        {
            StartCoroutine(StartRoutine());
        }

        private IEnumerator StartRoutine()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string deviceName = null;
            foreach (var device in devices)
            {
                if (device.isFrontFacing)
                {
                    deviceName = device.name;
                    break;
                }
            }
            if (deviceName == null && devices.Length > 0)
            {
                deviceName = devices[0].name;
            }

            if (deviceName == null)
            {
                Debug.LogError("[MediaPipePoseSource] No webcam device found.");
                yield break;
            }

            webCamTexture = new WebCamTexture(deviceName, webcamWidth, webcamHeight, webcamFps);
            webCamTexture.Play();

            IResourceManager resourceManager = new StreamingAssetsResourceManager();
            yield return resourceManager.PrepareAssetAsync(modelFileName, modelFileName);

            // PoseLandmarker construction is wrapped in try/catch below. Note: this can't happen
            // inline in this coroutine because C# forbids a yield statement inside a try block that
            // has a catch clause — so creation is split into a separate non-iterator method.
            CreateLandmarker();
        }

        private void CreateLandmarker()
        {
            try
            {
                var options = new PoseLandmarkerOptions(
                    new Mediapipe.Tasks.Core.BaseOptions(
                        Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                        modelAssetPath: modelFileName),
                    runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                    numPoses: 1,
                    resultCallback: OnPoseLandmarkerOutput);

                poseLandmarker = PoseLandmarker.CreateFromOptions(options);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MediaPipePoseSource] Failed to create PoseLandmarker: {e}");
                poseLandmarker = null;
            }
        }

        private void Update()
        {
            if (poseLandmarker == null || webCamTexture == null || !webCamTexture.didUpdateThisFrame)
            {
                return;
            }

            if (textureFrame == null)
            {
                textureFrame = new TextureFrame(webCamTexture.width, webCamTexture.height, TextureFormat.RGBA32);
            }

            textureFrame.ReadTextureOnCPU(webCamTexture, flipHorizontally: false, flipVertically: true);

            using (var image = textureFrame.BuildCPUImage())
            {
                poseLandmarker.DetectAsync(image, NowUnixMs());
            }
        }

        /// <summary>
        /// Runs on a MediaPipe background thread. Must not touch any Unity API — only copies plain
        /// struct data behind the lock.
        /// </summary>
        private void OnPoseLandmarkerOutput(PoseLandmarkerResult result, Image image, long timestampMs)
        {
            if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
            {
                return;
            }

            var landmarks = result.poseLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < 33)
            {
                return;
            }

            var snapshot = new PoseSnapshot
            {
                leftShoulder = ToPoseLandmark(landmarks[11]),
                rightShoulder = ToPoseLandmark(landmarks[12]),
                leftElbow = ToPoseLandmark(landmarks[13]),
                rightElbow = ToPoseLandmark(landmarks[14]),
                leftWrist = ToPoseLandmark(landmarks[15]),
                rightWrist = ToPoseLandmark(landmarks[16]),
                leftHip = ToPoseLandmark(landmarks[23]),
                rightHip = ToPoseLandmark(landmarks[24]),
                leftKnee = ToPoseLandmark(landmarks[25]),
                rightKnee = ToPoseLandmark(landmarks[26]),
                leftAnkle = ToPoseLandmark(landmarks[27]),
                rightAnkle = ToPoseLandmark(landmarks[28]),
                leftHeel = ToPoseLandmark(landmarks[29]),
                rightHeel = ToPoseLandmark(landmarks[30]),
                leftToe = ToPoseLandmark(landmarks[31]),
                rightToe = ToPoseLandmark(landmarks[32]),
            };

            lock (resultLock)
            {
                pendingSnapshot = snapshot;
                pendingDirty = true;
                pendingTimestampMs = timestampMs;
            }
        }

        private static PoseLandmark ToPoseLandmark(NormalizedLandmark lm)
        {
            return new PoseLandmark { x = lm.x, y = lm.y, visibility = lm.visibility ?? 1f };
        }

        private void LateUpdate()
        {
            bool gotNew = false;
            PoseSnapshot drained = default;
            long drainedTimestampMs = 0;

            lock (resultLock)
            {
                if (pendingDirty)
                {
                    drained = pendingSnapshot;
                    drainedTimestampMs = pendingTimestampMs;
                    pendingDirty = false;
                    gotNew = true;
                }
            }

            if (gotNew)
            {
                smoothedSnapshot = hasSmoothedSnapshot
                    ? Smooth(smoothedSnapshot, drained, smoothing)
                    : drained;
                hasSmoothedSnapshot = true;
                LatestSnapshot = smoothedSnapshot;
                lastResultReceivedAtMs = drainedTimestampMs;
                BodyVisible = PoseMath.IsBodyVisible(smoothedSnapshot);
            }

            PoseState classified = hasSmoothedSnapshot
                ? PoseMath.Classify(smoothedSnapshot, baseline)
                : PoseState.None;

            if (classified == candidatePose)
            {
                candidateStreak++;
            }
            else
            {
                candidatePose = classified;
                candidateStreak = 1;
            }

            if (candidateStreak >= requiredStableFrames)
            {
                currentPose = candidatePose;
            }
        }

        public void CaptureBaseline()
        {
            if (!BodyVisible || !hasSmoothedSnapshot)
            {
                return;
            }
            baseline = PoseMath.CaptureBaseline(smoothedSnapshot);
        }

        private static PoseSnapshot Smooth(in PoseSnapshot prev, in PoseSnapshot next, float alpha)
        {
            return new PoseSnapshot
            {
                leftShoulder = SmoothLandmark(prev.leftShoulder, next.leftShoulder, alpha),
                rightShoulder = SmoothLandmark(prev.rightShoulder, next.rightShoulder, alpha),
                leftElbow = SmoothLandmark(prev.leftElbow, next.leftElbow, alpha),
                rightElbow = SmoothLandmark(prev.rightElbow, next.rightElbow, alpha),
                leftWrist = SmoothLandmark(prev.leftWrist, next.leftWrist, alpha),
                rightWrist = SmoothLandmark(prev.rightWrist, next.rightWrist, alpha),
                leftHip = SmoothLandmark(prev.leftHip, next.leftHip, alpha),
                rightHip = SmoothLandmark(prev.rightHip, next.rightHip, alpha),
                leftKnee = SmoothLandmark(prev.leftKnee, next.leftKnee, alpha),
                rightKnee = SmoothLandmark(prev.rightKnee, next.rightKnee, alpha),
                leftAnkle = SmoothLandmark(prev.leftAnkle, next.leftAnkle, alpha),
                rightAnkle = SmoothLandmark(prev.rightAnkle, next.rightAnkle, alpha),
                leftHeel = SmoothLandmark(prev.leftHeel, next.leftHeel, alpha),
                rightHeel = SmoothLandmark(prev.rightHeel, next.rightHeel, alpha),
                leftToe = SmoothLandmark(prev.leftToe, next.leftToe, alpha),
                rightToe = SmoothLandmark(prev.rightToe, next.rightToe, alpha),
            };
        }

        private static PoseLandmark SmoothLandmark(in PoseLandmark prev, in PoseLandmark next, float alpha)
        {
            return new PoseLandmark
            {
                x = Mathf.Lerp(prev.x, next.x, alpha),
                y = Mathf.Lerp(prev.y, next.y, alpha),
                visibility = Mathf.Lerp(prev.visibility, next.visibility, alpha),
            };
        }

        private static long NowUnixMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void OnDestroy()
        {
            poseLandmarker?.Close();
            poseLandmarker = null;

            if (webCamTexture != null)
            {
                webCamTexture.Stop();
            }

            textureFrame?.Dispose();
            textureFrame = null;
        }
    }
}
