using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Kinex.Trainer;

namespace Kinex.MegaDance
{
    /// <summary>
    /// MEGA DANCE core loop. The trainer demos a target pose; the player copies it.
    ///
    /// Flow:  Idle(Start) → FirstPose(shows target image + 3-2-1 countdown) →
    ///        Playing(live camera, 0–100% closeness) → match ≥ passThreshold locks instantly →
    ///        Correct! → FirstPose(next pose) → … → Results after the last pose.
    ///
    /// Only "Correct" feedback ever — there is no "wrong" state. Scoring source is swappable:
    /// keyboard stub now (SPACE = 100%, runs with no camera), or live PoseDetector keypoints
    /// by flipping useKeyboardStub.
    /// </summary>
    public class MegaDanceManager : MonoBehaviour
    {
        enum State { Idle, FirstPose, Playing, Correct, Results }

        [Header("Trainer")]
        public TrainerPoseController trainer;

        [Header("Scoring source")]
        [Tooltip("Hold SPACE = 100% match. Bypasses real pose scoring until the webcam is wired.")]
        public bool useKeyboardStub = true;
        [Tooltip("Live keypoint source. Only used when useKeyboardStub is false.")]
        public MediaPipePoseDetector poseDetector;
        [Tooltip("Closeness needed to clear a pose. 0.95 = within ±5% of the target.")]
        [Range(0f, 1f)] public float passThreshold = 0.95f;
        [Tooltip("Per-limb angle tolerance (degrees). Bigger = more forgiving.")]
        public float toleranceDegrees = 45f;
        [Range(0f, 1f)] public float minConfidence = 0.3f;

        [Header("Timing")]
        [Tooltip("Get-ready countdown shown on the FirstPose screen before scoring starts.")]
        public int firstPoseCountdown = 3;
        [Tooltip("Seconds the big 'Correct!' overlay stays up before the next pose.")]
        public float correctHoldSeconds = 1.2f;

        [Header("UI — panels")]
        public GameObject startPanel;
        public GameObject poseInstructionPanel; // FirstPose screen
        public GameObject hudPanel;             // Playing screen
        public GameObject correctOverlay;       // Correct! screen
        public GameObject resultsPanel;

        [Header("UI — texts, image & bar")]
        public TMP_Text poseNameText;        // FirstPose card, e.g. "Pose 3"
        public Image poseImage;              // FirstPose card, target-pose preview sprite
        public TMP_Text countdownText;       // FirstPose 3-2-1 countdown
        public TMP_Text poseCounterText;     // HUD "3/10"
        public TMP_Text percentText;         // HUD bottom strip, "0%".."100%" closeness
        public Image matchBarFill;           // HUD bar, fillAmount 0..1 = live score
        public TMP_Text correctPoseNameText; // small name under big "Correct!"

        State _state = State.Idle;
        float[][] _signatures;               // [pose][8] baked angle targets
        readonly PoseSignatureBaker _baker = new PoseSignatureBaker();

        void Start()
        {
            ShowOnly(startPanel);
            _state = State.Idle;
        }

        /// <summary>Hooked to the green Start button (and the results Retry button).</summary>
        public void StartGame()
        {
            if (trainer == null) { Debug.LogError("[MegaDanceManager] trainer not assigned."); return; }
            trainer.autoAdvance = false; // the manager controls progression, not the trainer
            BakeSignatures();
            GoToFirstPose(0);
        }

        // Bake all target signatures once by snapping the rig through every pose and
        // reading joint positions. Done before the first pose is shown, so no visible flicker.
        void BakeSignatures()
        {
            int n = trainer.PoseCount;
            _signatures = new float[n][];
            for (int i = 0; i < n; i++)
            {
                trainer.ApplyPoseImmediate(i);
                _signatures[i] = _baker.BakeFromRig(trainer.Animator);
            }
            trainer.ApplyPoseImmediate(0); // leave the rig on pose 0
        }

        // ---- FirstPose: show the target pose image, run the get-ready countdown. ----
        void GoToFirstPose(int index)
        {
            trainer.ShowPose(index);
            if (poseNameText != null)    poseNameText.text = $"Pose {index + 1}";
            if (poseCounterText != null) poseCounterText.text = $"{index + 1}/{trainer.PoseCount}";
            if (poseImage != null)
            {
                var sprite = Resources.Load<Sprite>($"PosePreviews/pose_{index + 1:00}");
                if (sprite != null) poseImage.sprite = sprite;
            }
            SetPanels(instruction: true, hud: false, correct: false, results: false, start: false);
            StartCoroutine(FirstPoseRoutine());
        }

        IEnumerator FirstPoseRoutine()
        {
            _state = State.FirstPose;
            for (int t = firstPoseCountdown; t > 0; t--)
            {
                if (countdownText != null) countdownText.text = t.ToString();
                yield return new WaitForSeconds(1f);
            }
            if (countdownText != null) countdownText.text = "";
            EnterPlaying();
        }

        // ---- Playing: live scoring; first frame at/above passThreshold locks the pose. ----
        void EnterPlaying()
        {
            SetPanels(instruction: false, hud: true, correct: false, results: false, start: false);
            if (matchBarFill != null) matchBarFill.fillAmount = 0f;
            if (percentText != null)  percentText.text = "0%";
            _state = State.Playing;
        }

        void Update()
        {
            if (_state != State.Playing) return;

            float score = ReadScore();
            if (matchBarFill != null) matchBarFill.fillAmount = score;
            if (percentText != null)  percentText.text = $"{Mathf.RoundToInt(score * 100f)}%";

            if (score >= passThreshold) StartCoroutine(CorrectSequence());
        }

        // 0..1 match for the current pose. Stub: SPACE held = 100%. Real: keypoints vs signature.
        float ReadScore()
        {
            if (useKeyboardStub)
                return (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) ? 1f : 0f;

            if (poseDetector == null || !poseDetector.HasPose || _signatures == null) return 0f;
            return PoseScorer.Score(poseDetector.LatestKeypoints, poseDetector.LatestConfidence,
                                    _signatures[trainer.CurrentPose], minConfidence,
                                    toleranceDegrees * Mathf.Deg2Rad);
        }

        IEnumerator CorrectSequence()
        {
            _state = State.Correct; // set synchronously so Update() can't re-trigger this frame
            SetPanels(instruction: false, hud: false, correct: true, results: false, start: false);
            if (correctPoseNameText != null) correctPoseNameText.text = $"Pose {trainer.CurrentPose + 1}";
            if (matchBarFill != null) matchBarFill.fillAmount = 1f;
            if (percentText != null)  percentText.text = "100%";
            yield return new WaitForSeconds(correctHoldSeconds);

            int next = trainer.CurrentPose + 1;
            if (next >= trainer.PoseCount) ShowResults();
            else GoToFirstPose(next);
        }

        void ShowResults()
        {
            _state = State.Results;
            SetPanels(instruction: false, hud: false, correct: false, results: true, start: false);
            Debug.Log("[MegaDanceManager] All poses complete!");
        }

        void SetPanels(bool instruction, bool hud, bool correct, bool results, bool start)
        {
            if (poseInstructionPanel != null) poseInstructionPanel.SetActive(instruction);
            if (hudPanel != null)             hudPanel.SetActive(hud);
            if (correctOverlay != null)       correctOverlay.SetActive(correct);
            if (resultsPanel != null)         resultsPanel.SetActive(results);
            if (startPanel != null)           startPanel.SetActive(start);
        }

        void ShowOnly(GameObject panel)
        {
            SetPanels(false, false, false, false, false);
            if (panel != null) panel.SetActive(true);
        }
    }
}
