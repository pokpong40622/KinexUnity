using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Kinex.Trainer;

namespace Kinex.MegaDance
{
    /// <summary>
    /// MEGA DANCE core loop (mirror & hold): trainer demos a pose, player copies it,
    /// holding a good-enough match for holdSeconds clears it → "Correct!" → next pose →
    /// results after the last one.
    ///
    /// State: Idle → Scoring → Correct → (next Scoring | Results).
    /// Scoring source is swappable: keyboard stub now (SPACE = perfect match, runs with
    /// no camera), or live PoseDetector keypoints later by flipping useKeyboardStub.
    /// </summary>
    public class MegaDanceManager : MonoBehaviour
    {
        enum State { Idle, Scoring, Correct, Results }

        [Header("Trainer")]
        public TrainerPoseController trainer;

        [Header("Scoring source")]
        [Tooltip("Hold SPACE = perfect match. Bypasses real pose scoring until the webcam is wired.")]
        public bool useKeyboardStub = true;
        [Tooltip("Live keypoint source. Only used when useKeyboardStub is false.")]
        public MediaPipePoseDetector poseDetector;
        [Range(0f, 1f)] public float passThreshold = 0.6f;
        [Tooltip("Seconds the match must stay above passThreshold to clear a pose.")]
        public float holdSeconds = 2f;
        [Tooltip("Per-limb angle tolerance (degrees). Bigger = more forgiving.")]
        public float toleranceDegrees = 45f;
        [Range(0f, 1f)] public float minConfidence = 0.3f;

        [Header("UI — panels")]
        public GameObject startPanel;
        public GameObject poseInstructionPanel;
        public GameObject hudPanel;
        public GameObject correctOverlay;
        public GameObject resultsPanel;

        [Header("UI — texts & bar")]
        public TMP_Text poseNameText;        // instruction card, e.g. "Pose 3"
        public TMP_Text poseCounterText;     // HUD "3/10"
        public TMP_Text correctPoseNameText; // small name under big "Correct!"
        public Image matchBarFill;           // fillAmount 0..1 = live score

        State _state = State.Idle;
        float _holdTimer;
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
            GoToPose(0);
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

        void GoToPose(int index)
        {
            trainer.ShowPose(index);
            if (poseNameText != null)    poseNameText.text = $"Pose {index + 1}";
            if (poseCounterText != null) poseCounterText.text = $"{index + 1}/{trainer.PoseCount}";
            SetPanels(instruction: true, hud: true, correct: false, results: false, start: false);
            _holdTimer = 0f;
            _state = State.Scoring;
        }

        void Update()
        {
            if (_state != State.Scoring) return;

            float score = ReadScore();
            if (matchBarFill != null) matchBarFill.fillAmount = score;

            _holdTimer = score >= passThreshold ? _holdTimer + Time.deltaTime : 0f;
            if (_holdTimer >= holdSeconds) StartCoroutine(CorrectSequence());
        }

        // 0..1 match for the current pose. Stub: SPACE held = perfect. Real: keypoints vs signature.
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
            _state = State.Correct;
            SetPanels(instruction: false, hud: false, correct: true, results: false, start: false);
            if (correctPoseNameText != null) correctPoseNameText.text = $"Pose {trainer.CurrentPose + 1}";
            if (matchBarFill != null) matchBarFill.fillAmount = 1f;
            yield return new WaitForSeconds(1.2f);

            int next = trainer.CurrentPose + 1;
            if (next >= trainer.PoseCount) ShowResults();
            else GoToPose(next);
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
