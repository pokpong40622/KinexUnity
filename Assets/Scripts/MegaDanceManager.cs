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
        [Tooltip("Closeness needed to clear a pose. 0.70 = within ~30% of the target (real poses are " +
                 "hard to hit at 90%+).")]
        [Range(0f, 1f)] public float passThreshold = 0.70f;
        [Tooltip("Per-limb angle tolerance (degrees). Bigger = more forgiving.")]
        public float toleranceDegrees = 45f;
        [Range(0f, 1f)] public float minConfidence = 0.3f;
        [Tooltip("Log per-limb player-vs-target angles to logcat (~2x/sec) while Playing, so we can " +
                 "see WHY a held pose scores low (mirror? Y-flip? just a non-matching target pose?).")]
        public bool scoreDebugLog = true;

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

        [Header("Debug (temporary)")]
        [Tooltip("The 'NEXT' skip button — shown only while a game is in progress.")]
        public GameObject debugNextButton;

        [Header("Settings gear (Camera/Calibration)")]
        [Tooltip("The gear / settings button. Assign the SettingsGear object from the StartPanel so it " +
                 "stays visible as a small corner icon after auto-start (the StartPanel itself is hidden).")]
        public GameObject settingsGearButton;

        State _state = State.Idle;
        int _poseIndex;                      // current target pose; trainer animates to it on EnterPlaying
        float[][] _signatures;               // [pose][8] baked angle targets
        readonly PoseSignatureBaker _baker = new PoseSignatureBaker();

        void Start()
        {
            // Auto-start: skip the StartPanel so Flutter's tap is the only start needed.
            // The StartPanel is never shown; gameplay begins immediately after one frame
            // (gives Unity time to finish scene initialisation before baking signatures).
            SetPanels(instruction: false, hud: false, correct: false, results: false, start: false);
            if (debugNextButton != null) debugNextButton.SetActive(false);
            _state = State.Idle;
            StartCoroutine(AutoStart());
        }

        // Wait one frame so all Awake/Start calls on other objects (trainer, detector) complete,
        // then kick off gameplay automatically — no second "Start" tap required on the Unity side.
        IEnumerator AutoStart()
        {
            yield return null; // one frame
            // Re-show the settings gear as a standalone corner icon so Mirror/Sens/Calibrate
            // remain accessible during gameplay even though the StartPanel is hidden.
            if (settingsGearButton != null) settingsGearButton.SetActive(true);
            StartGame();
        }

        /// <summary>Hooked to the green Start button (and the results Retry button).
        /// Begins gameplay immediately — no calibration gate. The avatar drives from its
        /// geometric T-pose so calibration is not required for correct tracking.</summary>
        public void StartGame()
        {
            if (trainer == null) { Debug.LogError("[MegaDanceManager] trainer not assigned."); return; }
            trainer.autoAdvance = false; // the manager controls progression, not the trainer
            if (debugNextButton != null) debugNextButton.SetActive(true); // visible once the game starts
            BakeSignatures();
            GoToFirstPose(0);
        }

        // Kept so external references (UI buttons, other scripts) that call CalibrateThenStart
        // don't break at compile time. It delegates straight to StartGame so the behaviour is
        // identical — no calibration countdown is run.
        IEnumerator CalibrateThenStart()
        {
            StartGame();
            yield break;
        }

        /// <summary>
        /// Back button → return to the Flutter home screen. flutter_embed_unity routes this
        /// to MegaDanceGameScreen.onMessageFromUnity, which does context.go('/home'). Same
        /// {"type":"exit"} contract Kinex World uses. In the editor SendToFlutter just logs.
        /// </summary>
        public void ExitToHome()
        {
            SendToFlutter.Send("{\"type\":\"exit\"}");
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
            // Leave the rig on the LAST pose (not pose 0) so the very first pose also animates
            // (pose 0's target would otherwise already be showing). It's hidden behind the
            // FirstPose card anyway, then blends into pose 0 when Playing starts.
            trainer.ApplyPoseImmediate(n - 1);
        }

        // ---- FirstPose: show the target pose image, run the get-ready countdown. ----
        // The trainer does NOT move here — it animates into the pose when the popup closes
        // (EnterPlaying), so the player actually watches the movement instead of missing it
        // behind the card.
        void GoToFirstPose(int index)
        {
            _poseIndex = index;
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
            trainer.ShowPose(_poseIndex); // animate into the pose now that the card is gone (visible)
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
            float[] target = _signatures[trainer.CurrentPose];
            float score = PoseScorer.Score(poseDetector.LatestKeypoints, poseDetector.LatestConfidence,
                                           target, minConfidence, toleranceDegrees * Mathf.Deg2Rad);
            if (scoreDebugLog) LogScoreBreakdown(target, score);
            return score;
        }

        // Periodic per-limb diagnostic: for each of the 8 limbs, print the player's angle, the
        // baked target angle, and the error (degrees). Read in `adb logcat -s Unity` while holding
        // a pose to see exactly which limbs disagree — distinguishes a mirror/Y-flip bug (whole
        // sides systematically off) from simply not matching the current target pose.
        float _lastScoreDbg;
        readonly float[] _dbgAngles = new float[PoseScorer.NumLimbs];
        readonly bool[] _dbgValid = new bool[PoseScorer.NumLimbs];
        static readonly string[] _limbNames = { "L_uArm", "R_uArm", "L_lArm", "R_lArm",
                                                "L_uLeg", "R_uLeg", "L_lLeg", "R_lLeg" };
        void LogScoreBreakdown(float[] target, float score)
        {
            if (Time.time - _lastScoreDbg < 0.5f) return;
            _lastScoreDbg = Time.time;
            PoseScorer.ComputeAngles(poseDetector.LatestKeypoints, poseDetector.LatestConfidence,
                                     minConfidence, _dbgAngles, _dbgValid);
            var sb = new System.Text.StringBuilder();
            sb.Append($"[PoseScore] pose={trainer.CurrentPose + 1} score={score:F2} tol={toleranceDegrees}°");
            for (int i = 0; i < PoseScorer.NumLimbs; i++)
            {
                if (!_dbgValid[i]) { sb.Append($" {_limbNames[i]}=offcam"); continue; }
                float errDeg = PoseScorer.AngleError(_dbgAngles[i], target[i]) * Mathf.Rad2Deg;
                sb.Append($" {_limbNames[i]}:p{_dbgAngles[i] * Mathf.Rad2Deg:F0}/t{target[i] * Mathf.Rad2Deg:F0}/e{errDeg:F0}");
            }
            Debug.Log(sb.ToString());
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

        /// <summary>DEBUG: jump straight to the next pose from any state (skips scoring). Wired to a
        /// temporary on-screen "NEXT" button for testing animations/flow. Safe to remove later.</summary>
        public void SkipPose()
        {
            if (trainer == null) return;
            StopAllCoroutines();
            int next = trainer.CurrentPose + 1;
            if (next >= trainer.PoseCount) ShowResults();
            else GoToFirstPose(next);
        }

        void ShowResults()
        {
            _state = State.Results;
            SetPanels(instruction: false, hud: false, correct: false, results: true, start: false);
            if (debugNextButton != null) debugNextButton.SetActive(false); // game over — hide skip
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
