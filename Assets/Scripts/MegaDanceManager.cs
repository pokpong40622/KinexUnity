using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Kinex.Trainer;
using Kinex.UI;

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

        [Header("Voice (optional)")]
        [Tooltip("Offline Piper voice that speaks the countdown + coaching hints. " +
                 "Leave unassigned to run silent.")]
        public VoiceCoach voice;

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
        [Tooltip("Score smoothing per frame (0 = frozen, 1 = no smoothing). Lower = steadier % and " +
                 "fewer false passes from keypoint jitter, but slower to react.")]
        [Range(0.05f, 1f)] public float scoreSmoothing = 0.25f;
        [Tooltip("Seconds the smoothed score must stay at/above passThreshold before the pose clears. " +
                 "Stops a single lucky/jittery frame from passing.")]
        public float holdToPassSeconds = 0.4f;
        [Tooltip("Log per-limb player-vs-target angles to logcat (~2x/sec) while Playing, so we can " +
                 "see WHY a held pose scores low (mirror? Y-flip? just a non-matching target pose?).")]
        public bool scoreDebugLog = true;

        [Header("Timing")]
        [Tooltip("Get-ready countdown shown on the FirstPose screen before scoring starts.")]
        public int firstPoseCountdown = 3;
        [Tooltip("Seconds the big 'Correct!' overlay stays up before the next pose.")]
        public float correctHoldSeconds = 1.2f;

        [Header("Game feel")]
        [Tooltip("Pass a pose within this many seconds of it starting to keep/extend your combo. " +
                 "Slower than this resets the combo to 1.")]
        public float comboWindowSeconds = 6f;

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
        public TMP_Text trackingModeLabel;   // gear-popup toggle button label, live "Mode: 2D/3D"
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

        [Header("Camera preview")]
        [Tooltip("Corner webcam + skeleton preview (CameraFeedPanel). Hidden during the get-ready pose " +
                 "popup so it never covers the card; shown again while Playing.")]
        public GameObject cameraPreview;

        [Header("Coaching — turn cue (3D mode only)")]
        [Tooltip("Flip if the spoken 'Turn right' actually sends the player the wrong way on device.")]
        [SerializeField] bool invertTurnCue = false;
        [Tooltip("Ignore turn errors smaller than this (degrees) so the coach doesn't nag near-aligned.")]
        [SerializeField] float turnCueDeadzoneDeg = 20f;
        [Tooltip("Constant offset (deg) added to the avatar hip-yaw before comparing to the trainer, in " +
                 "case the two rigs don't share the same rest facing. Read the [Coach] logcat line on " +
                 "device (with scoreDebugLog on) to dial this in — no rebuild needed.")]
        [SerializeField] float turnCueOffsetDeg = 0f;

        State _state = State.Idle;
        int _poseIndex;                      // current target pose; trainer animates to it on EnterPlaying
        float[][] _signatures;               // [pose][8] baked angle targets
        readonly PoseSignatureBaker _baker = new PoseSignatureBaker();
        float _smoothedScore;                // EMA of ReadScore; what the HUD shows and the pass tests
        float _aboveSince = -1f;             // Time.time the score first crossed passThreshold; -1 = below
        int _combo;                          // consecutive FAST passes (rising chime + "Combo xN!")
        float _poseStartTime;                // Time.time EnterPlaying ran — drives the combo window
        bool _greenCued;                     // played the "you're in the zone" cue once this pose?

        GameHud _hud;                        // corner ring gauges (heart-rate mock + live match %)
        TMP_Text _hintText;                  // bottom-strip coaching line, created at runtime
        float _lastHintAt;                   // throttle the hint refresh
        readonly float[] _avatarAngles = new float[PoseScorer.NumLimbs]; // baked avatar rig, scored vs trainer

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
            trainer.blendTime = 0.8f;    // ~15% slower pose-to-pose blend so it's easier to follow
            toleranceDegrees = 30f;      // stricter per-limb angle match (was 45 — passed too easily)
            passThreshold = 0.75f;       // need a closer overall match to clear (was 0.70)
            // Talk more often (default gap is 2s) so coaching feels responsive.
            if (voice != null) voice.minGapSeconds = 0.7f;
            _combo = 0;
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
            // Show the exercise + checkpoint label baked into the pose name
            // (e.g. "หมุนศีรษะ • 1/4 (หันซ้าย)"); fall back to a generic label if empty.
            if (poseNameText != null)
            {
                string nm = trainer.PoseName(index);
                poseNameText.text = string.IsNullOrEmpty(nm) ? $"ท่าที่ {index + 1}" : nm;
            }
            if (poseCounterText != null)
            {
                // Show the active tracking mode so the tester can verify which driver (3D / 2D) is live.
                string mode = poseDetector != null && poseDetector.Use3DWorld ? "3D" : "2D";
                poseCounterText.text = $"{index + 1}/{trainer.PoseCount}   ·   โหมด {mode}";
            }
            if (poseImage != null)
            {
                var sprite = Resources.Load<Sprite>($"PosePreviews/pose_{index + 1:00}");
                if (sprite != null) poseImage.sprite = sprite;
            }
            SetPanels(instruction: true, hud: false, correct: false, results: false, start: false);
            if (cameraPreview != null) cameraPreview.SetActive(false); // don't cover the pose card
            Kinex.Sfx.Play("pose_appear"); // soft whoosh as the new pose card shows
            StartCoroutine(FirstPoseRoutine());
        }

        IEnumerator FirstPoseRoutine()
        {
            _state = State.FirstPose;
            // Arcade-style get-ready: a beep on each 3-2-1 tick, then a bright "GO!" (sounds
            // only — no spoken "three two one", which the user found annoying).
            // "GO!" is 3 chars wide vs a single digit, so it overran the screen edges — never wrap it
            // and shrink it to ~55% of the digit size so it fits on one line.
            float digitSize = 0f;
            if (countdownText != null)
            {
                digitSize = countdownText.fontSize;
                countdownText.enableWordWrapping = false;
                countdownText.overflowMode = TextOverflowModes.Overflow;
            }
            for (int t = firstPoseCountdown; t > 0; t--)
            {
                if (countdownText != null) countdownText.text = t.ToString();
                Kinex.Sfx.Play("beep");
                yield return new WaitForSeconds(1f);
            }
            if (countdownText != null)
            {
                countdownText.fontSize = digitSize * 0.55f; // "GO!" is wider — shrink so it isn't cut off
                countdownText.text = "ไป!";
            }
            Kinex.Sfx.Play("go");
            yield return new WaitForSeconds(0.35f);
            if (countdownText != null)
            {
                countdownText.text = "";
                countdownText.fontSize = digitSize;         // restore for the next round's digits
            }
            EnterPlaying();
        }

        // ---- Playing: live scoring; first frame at/above passThreshold locks the pose. ----
        void EnterPlaying()
        {
            trainer.ShowPose(_poseIndex); // animate into the pose now that the card is gone (visible)
            SetPanels(instruction: false, hud: true, correct: false, results: false, start: false);
            if (cameraPreview != null) cameraPreview.SetActive(true); // framing preview back during play
            _hud = GameHud.Ensure(hudPanel);
            _hud?.HideLegacy(matchBarFill != null ? matchBarFill.gameObject : null,
                              percentText != null ? percentText.gameObject : null);
            Kinex.ScoreHud.EnsurePassLine(matchBarFill); // 70% target marker on the bar (idempotent)
            EnsureHintText();
            if (_hintText != null) _hintText.text = "";
            if (matchBarFill != null) matchBarFill.fillAmount = 0f;
            if (percentText != null)  percentText.text = "0%";
            _smoothedScore = 0f;
            _aboveSince = -1f;
            _poseStartTime = Time.time;
            _greenCued = false;
            _state = State.Playing;
        }

        void Update()
        {
            RefreshTrackingModeLabel(); // keep the 2D/3D indicator live even in menus / the gear popup
            if (_state != State.Playing) return;

            // EMA so per-frame keypoint jitter doesn't flicker the % or trip a false pass.
            float raw = ReadScore();
            _smoothedScore = Mathf.Lerp(_smoothedScore, raw, scoreSmoothing);

            if (matchBarFill != null) matchBarFill.fillAmount = _smoothedScore;
            if (percentText != null)  percentText.text = $"{Mathf.RoundToInt(_smoothedScore * 100f)}%";
            Kinex.ScoreHud.Apply(percentText, matchBarFill, _smoothedScore); // colour by band (<50 red, <70 yellow, >=70 green)
            _hud?.SetScore(_smoothedScore);
            _hud?.SetSubLabel(poseCounterText != null ? poseCounterText.text : null);

            UpdateHint(); // bottom coaching line: "Move <limb> up/down/left/right" (throttled)

            // Hold-to-pass: the smoothed score must stay at/above threshold continuously.
            if (_smoothedScore >= passThreshold)
            {
                if (_aboveSince < 0f) _aboveSince = Time.time;
                if (!_greenCued) { _greenCued = true; Kinex.Sfx.Play("zone", 1f); } // "you're in the zone — hold it!"
                if (Time.time - _aboveSince >= holdToPassSeconds) StartCoroutine(CorrectSequence());
            }
            else _aboveSince = -1f;
        }

        // 0..1 match for the current pose. Stub: SPACE held = 100%. Real: keypoints vs signature.
        float ReadScore()
        {
            if (useKeyboardStub)
                return (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) ? 1f : 0f;

            if (poseDetector == null || !poseDetector.HasPose || _signatures == null) return 0f;
            // Score the DRIVEN AVATAR rig vs the trainer rig (both baked the same way) so the score
            // rewards making the avatar look like the trainer — not matching the mirrored camera
            // skeleton. The avatar's bones already encode the selfie-mirror/cross-map, so there's no
            // left/right ambiguity here.
            if (poseDetector.AvatarAnimator == null) return 0f;
            _baker.BakeFromRigInto(poseDetector.AvatarAnimator, _avatarAngles);
            float[] target = _signatures[trainer.CurrentPose];
            float score = PoseScorer.ScoreAngles(_avatarAngles, target, toleranceDegrees * Mathf.Deg2Rad);
            if (scoreDebugLog) LogScoreBreakdown(target, score);
            return score;
        }

        // Periodic per-limb diagnostic: for each of the 8 limbs, print the player's angle, the
        // baked target angle, and the error (degrees). Read in `adb logcat -s Unity` while holding
        // a pose to see exactly which limbs disagree — distinguishes a mirror/Y-flip bug (whole
        // sides systematically off) from simply not matching the current target pose.
        float _lastScoreDbg;
        static readonly string[] _limbNames = { "L_uArm", "R_uArm", "L_lArm", "R_lArm",
                                                "L_uLeg", "R_uLeg", "L_lLeg", "R_lLeg" };
        // Per-limb avatar-vs-trainer breakdown (degrees): a=avatar rig, t=trainer target, e=error.
        void LogScoreBreakdown(float[] target, float score)
        {
            if (Time.time - _lastScoreDbg < 0.5f) return;
            _lastScoreDbg = Time.time;
            var sb = new System.Text.StringBuilder();
            sb.Append($"[PoseScore] pose={trainer.CurrentPose + 1} score={score:F2} tol={toleranceDegrees}°");
            for (int i = 0; i < PoseScorer.NumLimbs; i++)
            {
                float errDeg = PoseScorer.AngleError(_avatarAngles[i], target[i]) * Mathf.Rad2Deg;
                sb.Append($" {_limbNames[i]}:a{_avatarAngles[i] * Mathf.Rad2Deg:F0}/t{target[i] * Mathf.Rad2Deg:F0}/e{errDeg:F0}");
            }
            Debug.Log(sb.ToString());
        }

        // ---- Bottom-strip pose-improvement hint ("Move left arm up"). ----
        // Created at runtime so we never hand-edit the HUD prefab/scene. Lives at the bottom-
        // centre of the HUD panel. SemiBold, dark fill + light outline, centred (Figma
        // MegaDancePlaying), kept SIMPLE: the literal limb directive the scorer derives.
        void EnsureHintText()
        {
            if (_hintText != null || hudPanel == null) return;

            var go = new GameObject("PoseHint", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(hudPanel.GetComponent<RectTransform>(), false);
            rt.anchorMin = new Vector2(0.05f, 0.02f);
            rt.anchorMax = new Vector2(0.95f, 0.12f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _hintText = go.AddComponent<TextMeshProUGUI>();
            _hintText.alignment = TextAlignmentOptions.Center;
            _hintText.enableAutoSizing = true;
            _hintText.fontSizeMin = 24f;
            _hintText.fontSizeMax = 48f; // Figma: ~48px
            _hintText.fontStyle = FontStyles.Bold;
            _hintText.color = new Color(0.10f, 0.12f, 0.16f); // dark fill
            _hintText.raycastTarget = false;
            // Light outline (~6px feel) via the label's own material instance.
            var mat = _hintText.fontMaterial;
            if (mat != null)
            {
                if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
                    mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(1f, 1f, 1f, 0.9f));
                if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
                    mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            }
            _hintText.text = "";
        }

        // Refresh the hint ~2x/sec from the worst-error limb. Only meaningful with live keypoints
        // (the keyboard stub has no pose), so it stays blank in stub mode.
        void UpdateHint()
        {
            if (_hintText == null) return;
            if (Time.time - _lastHintAt < 0.35f) return;
            _lastHintAt = Time.time;

            if (useKeyboardStub || poseDetector == null || !poseDetector.HasPose ||
                _signatures == null)
            {
                _hintText.text = "";
                return;
            }

            // In the green zone, stop coaching and shout "hold it" so hitting 70% is unmistakable.
            if (_smoothedScore >= passThreshold)
            {
                _hintText.text = "Great! Hold it!";
                return;
            }

            float[] target = _signatures[trainer.CurrentPose];

            // TURN cue (3D only): yaw can't be read in the flat 2D driver, so only coach turning when
            // the 3D world driver is live. Compare the avatar's hip world-yaw to the trainer's; a big
            // signed gap means the player is facing the wrong way for a side-on pose. The three
            // Inspector knobs (invert / deadzone / offset) tune it on device without a rebuild.
            float turnErr = 0f;
            if (poseDetector.Use3DWorld)
            {
                var avatarHips  = poseDetector.AvatarAnimator != null
                    ? poseDetector.AvatarAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
                var trainerHips = trainer.Animator != null
                    ? trainer.Animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (avatarHips != null && trainerHips != null)
                {
                    turnErr = Mathf.DeltaAngle(avatarHips.rotation.eulerAngles.y + turnCueOffsetDeg,
                                               trainerHips.rotation.eulerAngles.y);
                    if (invertTurnCue) turnErr = -turnErr;
                }
                if (scoreDebugLog) Debug.Log($"[Coach] turnErr={turnErr:F0}° deadzone={turnCueDeadzoneDeg}°");
            }

            // _avatarAngles was baked this frame in ReadScore. Turn cue takes priority over the
            // worst-limb cue; ~12° limb deadzone so we don't nag on near-correct limbs.
            // Turn cue DISABLED: scoring is now facing-invariant (PoseSignatureBaker.yawNormalize),
            // so the player just faces the screen and matches limb shape — "turn" would be wrong
            // advice. Passing a huge deadzone suppresses it; limb cues remain. (turnErr still logged.)
            _hintText.text = PoseHint.Compute(_avatarAngles, target, null, 12f * Mathf.Deg2Rad,
                                              mirrorLR: false,
                                              turnErrorDeg: turnErr, turnDeadzoneDeg: 9999f);

            // Read the line aloud. VoiceCoach only actually speaks when the line changed,
            // nothing is playing, and its min-gap elapsed — so calling every refresh is fine.
            if (voice != null) voice.Speak(_hintText.text);
        }

        // Keep the 2D/3D indicators in sync with the live detector flag so the gear toggle's effect
        // is visible immediately: the toggle's own button label always reflects the current driver,
        // and the HUD counter shows it too while a game runs.
        void RefreshTrackingModeLabel()
        {
            if (poseDetector == null) return;
            bool is3D = poseDetector.Use3DWorld;
            if (trackingModeLabel != null)
                trackingModeLabel.text = is3D ? "Mode: 3D  —  tap to switch" : "Mode: 2D  —  tap to switch";
            if (_state == State.Playing && poseCounterText != null)
                poseCounterText.text = $"{trainer.CurrentPose + 1}/{trainer.PoseCount}   ·   โหมด {(is3D ? "3D" : "2D")}";
        }

        IEnumerator CorrectSequence()
        {
            _state = State.Correct; // set synchronously so Update() can't re-trigger this frame

            // Combo: passing a pose quickly (within comboWindowSeconds of it starting) builds the
            // combo and raises the success-chime pitch; a slow pass resets it to 1.
            bool fast = (Time.time - _poseStartTime) <= comboWindowSeconds;
            _combo = fast ? _combo + 1 : 1;
            float pitch = 1f + 0.05f * Mathf.Min(_combo - 1, 6); // cap the rise so it never gets shrill
            Kinex.Sfx.Play("correct", 1f, pitch); // success chime, higher with each combo

            SetPanels(instruction: false, hud: false, correct: true, results: false, start: false);
            // Figma MegaDanceCorrect: italic gradient text + white stroke + shadow over a dimmed
            // backdrop, popping in. Styled + animated at runtime (no prefab edits).
            CorrectEffect.Style(correctOverlay, out var correctRt, out var correctGroup);
            StartCoroutine(CorrectEffect.Pop(correctRt, correctGroup));
            if (correctPoseNameText != null)
                correctPoseNameText.text = $"ท่าที่ {trainer.CurrentPose + 1}"; // rehab: no combo text
            if (matchBarFill != null) matchBarFill.fillAmount = 1f;
            if (percentText != null)  percentText.text = "100%";
            Kinex.ScoreHud.Apply(percentText, matchBarFill, 1f); // pass = green
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
            Kinex.Sfx.Play("results"); // session-complete sting
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
