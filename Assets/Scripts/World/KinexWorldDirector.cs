using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.MegaDance; // PoseScorer + PoseSignatureBaker (reused unchanged)
using Kinex.Trainer;

namespace Kinex.World
{
    /// <summary>
    /// Kinex World — instructor-led exercise class. Unlike MEGA DANCE (match a static pose,
    /// then advance), this runs on a FIXED TIMELINE and never waits for the player: each
    /// exercise plays for its duration while we continuously sample how closely the player
    /// matches the trainer's LIVE animated pose. The mean of those samples is the
    /// Average Performance Percentage.
    ///
    /// Scoring uses rig-vs-rig: bake the driven AVATAR rig and score it against the trainer
    /// rig (PoseSignatureBaker.BakeFromRig / BakeFromRigInto) — the same approach MegaDance
    /// uses, so the score rewards making the avatar look like the trainer.
    ///
    /// Phase 0: runs headless with useScoreStub=true (random score, no camera) so the whole
    /// timeline is walkable in the editor before any animation or webcam exists.
    /// </summary>
    public class KinexWorldDirector : MonoBehaviour
    {
        public enum State { Idle, Intro, Calibrating, Countdown, Active, Transition, Results }

        [Header("Content")]
        public Routine routine;

        [Header("Scene refs")]
        [Tooltip("Animator on the trainer rig that plays the per-exercise looping clips.")]
        public Animator trainerAnimator;
        [Tooltip("Drives the trainer rig through baked balance poses (one per exercise). When set, the director controls progression instead of letting the controller auto-cycle.")]
        public TrainerPoseController trainer;
        [Tooltip("Player pose source. Optional while useScoreStub is on.")]
        public MediaPipePoseDetector poseDetector;

        [Header("Scoring")]
        [Tooltip("Skip the camera and feed a random score — lets the full timeline run in-editor. " +
                 "OFF by default so scoring routes through the real rig-vs-rig PoseScorer. " +
                 "Turn ON only for camera-free editor walkthroughs.")]
        public bool useScoreStub = false;
        [Range(10f, 70f)] public float toleranceDegrees = 45f;
        [Range(0f, 1f)] public float minConfidence = 0.3f;
        [Tooltip("Seconds between score samples during an exercise.")]
        [Range(0.05f, 0.5f)] public float scoreTickSeconds = 0.1f;
        [Tooltip("Smoothing for the on-screen live bar (0=instant, 1=frozen).")]
        [Range(0f, 0.95f)] public float liveBarSmoothing = 0.6f;

        [Header("Timing")]
        [Range(1, 5)] public int countdownSeconds = 3;
        [Tooltip("Pause on the transition card between exercises.")]
        [Range(0f, 4f)] public float transitionSeconds = 3f;   // was 2 — slightly longer pause so player knows what's next
        [Tooltip("Auto-begin after the intro card (Flutter/UI may call BeginAfterIntro instead). " +
                 "Unused: the play flow skips the intro card entirely.")]
        public bool autoBeginIntro = true;
        [Range(0f, 6f)] public float introSeconds = 3f;        // kept for CalibrateThenMenu path, not used in RunSession

        [Header("UI — Intro / countdown")]
        public GameObject introPanel;
        public TMP_Text routineNameText;
        public GameObject countdownPanel;
        public TMP_Text countdownText;

        [Header("UI — Calibration")]
        public GameObject calibrationPanel;
        public TMP_Text calibPromptText;
        public TMP_Text calibCountdownText;

        [Header("UI — Active HUD")]
        public GameObject hudPanel;
        public TMP_Text exerciseNameText;
        public TMP_Text exerciseCounterText;   // "2/5"
        public TMP_Text percentText;           // live "78%"
        public Image liveBarFill;              // fillAmount 0..1
        public TMP_Text encourageText;         // "Great!" / "Keep going!"
        public Image segmentTimerFill;         // optional segment time ring 0..1
        public GameObject cameraHint;          // shown when current exercise legsRequired
        public TMP_Text cameraHintText;

        [Header("UI — Transition / results")]
        public GameObject transitionPanel;
        public TMP_Text transitionText;
        public GameObject resultsPanel;
        public TMP_Text resultsAverageText;

        [Header("Audio")]
        public AudioSource musicSource;
        public AudioClip folkMusic;
        [Range(0f, 1f)] public float musicVolume = 0.4f;

        /// <summary>Fired once when the class finishes (bridge → Flutter hooks this).</summary>
        public event Action<WorldSessionResult> OnSessionComplete;

        /// <summary>Fired when the user exits mid-class (bridge → Flutter returns home).</summary>
        public event Action OnExitRequested;

        // --- runtime read-only state (for tests + HUD) ---
        public State Current { get; private set; } = State.Idle;
        public int CurrentIndex { get; private set; } = -1;
        public float LiveScore01 { get; private set; }
        public WorldSessionResult LastResult { get; private set; }

        readonly PoseSignatureBaker _baker = new PoseSignatureBaker();
        readonly float[] _avatarAngles = new float[PoseScorer.NumLimbs]; // Task A: rig-vs-rig scratch buffer
        Coroutine _run;
        float _stubBias = 0.78f; // centre of the random stub band

        // Task C: Correct popup runtime state
        bool _correctCued;                   // true once the "Correct!" popup has fired this segment
        GameObject _correctPopupGo;          // the runtime full-screen canvas overlay
        TMP_Text _correctPopupText;          // the "Correct!" TMP label inside it

        float ToleranceRad => toleranceDegrees * Mathf.Deg2Rad;

        // ---------------------------------------------------------------- public API

        /// <summary>Start the whole class from the top (button / Flutter entry).</summary>
        public void StartSession()
        {
            if (routine == null || routine.ExerciseCount == 0)
            {
                Debug.LogWarning("[KinexWorld] No routine assigned.");
                return;
            }
            if (_run != null) StopCoroutine(_run);
            _run = StartCoroutine(RunSession());
        }

        /// <summary>Abort and reset to Idle (in-game exit button).</summary>
        public void ExitSession()
        {
            if (_run != null) { StopCoroutine(_run); _run = null; }
            Current = State.Idle;
            ShowOnly(null);
            StopMusic();
            OnExitRequested?.Invoke();
        }

        // ---------------------------------------------------------------- main loop

        IEnumerator RunSession()
        {
            LastResult = new WorldSessionResult { routineId = routine.id };
            CurrentIndex = -1;
            LiveScore01 = 0f;

            // Task B: Skip the Unity intro card entirely — go straight into the first exercise
            // so the trainer animation begins as soon as Flutter's Start tap arrives.
            // StartMusic and routineNameText are still set here; the intro panel is never shown.
            StartMusic();
            if (routineNameText) routineNameText.text = routine.englishName;
            // (introPanel intentionally NOT shown, introSeconds wait intentionally skipped)

            if (trainer != null) { trainer.autoAdvance = false; trainer.blendTime = 0.7f; }

            float totalDuration = 0f;
            float weightedScoreSum = 0f;

            for (int i = 0; i < routine.exercises.Length; i++)
            {
                var ex = routine.exercises[i];
                if (ex == null) continue;
                CurrentIndex = i;

                if (trainer != null) trainer.ShowPose(i); // exercise i ↔ trainer pose i (ShowPose wraps, so safe)

                yield return Countdown(ex);

                float segAvg = 0f;
                yield return ActiveSegment(ex, i, v => segAvg = v);

                LastResult.exercises.Add(new WorldSessionResult.ExerciseScore
                {
                    id = ex.id,
                    name = string.IsNullOrEmpty(ex.englishName) ? ex.id : ex.englishName,
                    score = Mathf.Round(segAvg * 1000f) / 10f, // 0..100, 1 dp
                });
                weightedScoreSum += segAvg * ex.durationSeconds;
                totalDuration += ex.durationSeconds;

                if (i < routine.exercises.Length - 1)
                    yield return Transition(routine.exercises[i + 1]);
            }

            // Results
            float overall01 = totalDuration > 0f ? weightedScoreSum / totalDuration : 0f;
            LastResult.averagePercent = Mathf.Round(overall01 * 1000f) / 10f;
            LastResult.durationSeconds = totalDuration;

            Current = State.Results;
            ShowOnly(resultsPanel);
            StopMusic();
            if (resultsAverageText) resultsAverageText.text = $"{LastResult.averagePercent:0.#}%";
            _run = null;
            OnSessionComplete?.Invoke(LastResult);
        }

        /// <summary>Optional T-pose calibration launched from the start-menu settings gear.
        /// Runs the guided capture, then returns to the menu. Ignored during a running class.</summary>
        public void CalibrateFromMenu()
        {
            if (_run != null || poseDetector == null) return;
            StartCoroutine(CalibrateThenMenu());
        }

        IEnumerator CalibrateThenMenu()
        {
            yield return Calibrate();
            Current = State.Idle;
            ShowOnly(introPanel); // CalibrateThenMenu path: show intro as a "ready" landing screen
        }

        IEnumerator Calibrate()
        {
            Current = State.Calibrating;
            ShowOnly(calibrationPanel);
            if (calibPromptText) calibPromptText.text = "Stand in a T-pose\narms straight out to the sides";
            if (calibCountdownText) calibCountdownText.text = "";

            poseDetector.StartCalibration();

            while (poseDetector.CalibrationPhase != MediaPipePoseDetector.CalibState.Idle)
            {
                var phase = poseDetector.CalibrationPhase;
                if (calibPromptText)
                    calibPromptText.text =
                        phase == MediaPipePoseDetector.CalibState.Counting ? "Hold your T-pose!" :
                        phase == MediaPipePoseDetector.CalibState.Done     ? "Calibrated!" :
                                                                             "Stand in a T-pose\narms straight out to the sides";
                if (calibCountdownText)
                    calibCountdownText.text =
                        phase == MediaPipePoseDetector.CalibState.Counting
                            ? Mathf.CeilToInt(poseDetector.CalibrationCountdown).ToString() : "";
                yield return null;
            }
        }

        IEnumerator Countdown(ExerciseDefinition ex)
        {
            Current = State.Countdown;
            ShowOnly(countdownPanel);
            PlayClip(ex); // trainer already demoing the move during the count-in
            for (int n = countdownSeconds; n > 0; n--)
            {
                if (countdownText) countdownText.text = n.ToString();
                yield return new WaitForSeconds(1f);
            }
        }

        IEnumerator ActiveSegment(ExerciseDefinition ex, int index, Action<float> reportAvg)
        {
            Current = State.Active;
            ShowOnly(hudPanel);
            Kinex.ScoreHud.EnsurePassLine(liveBarFill); // 70% target marker on the live bar (idempotent)
            PlayClip(ex);

            if (exerciseNameText) exerciseNameText.text =
                string.IsNullOrEmpty(ex.englishName) ? ex.id : ex.englishName;
            if (exerciseCounterText) exerciseCounterText.text = $"{index + 1}/{routine.ExerciseCount}";
            if (cameraHint) cameraHint.SetActive(ex.legsRequired);
            if (ex.legsRequired && cameraHintText)
                cameraHintText.text = "Step back ~2m — full body in view";

            float elapsed = 0f, tick = 0f, acc = 0f;
            int samples = 0;
            LiveScore01 = 0f;

            // Task C: reset the "Correct!" debounce flag at the start of each segment
            _correctCued = false;

            while (elapsed < ex.durationSeconds)
            {
                elapsed += Time.deltaTime;
                tick += Time.deltaTime;
                if (tick >= scoreTickSeconds)
                {
                    tick = 0f;
                    float s = SampleScore();
                    acc += s; samples++;
                    LiveScore01 = Mathf.Lerp(s, LiveScore01, liveBarSmoothing);
                    UpdateLiveHud();

                    // Task C: fire "Correct!" overlay once per segment when score first crosses 70%
                    if (!_correctCued && LiveScore01 >= Kinex.ScoreHud.PassThreshold)
                    {
                        _correctCued = true;
                        StartCoroutine(ShowCorrectPopup());
                    }
                }
                if (segmentTimerFill) segmentTimerFill.fillAmount = elapsed / ex.durationSeconds;
                yield return null;
            }

            reportAvg(samples > 0 ? acc / samples : 0f);
        }

        IEnumerator Transition(ExerciseDefinition next)
        {
            Current = State.Transition;
            ShowOnly(transitionPanel);
            if (transitionText) transitionText.text =
                "Next: " + (string.IsNullOrEmpty(next.englishName) ? next.id : next.englishName);
            yield return new WaitForSeconds(transitionSeconds);
        }

        // ---------------------------------------------------------------- scoring

        // Task A: rig-vs-rig scoring — matches MegaDanceManager.ReadScore exactly in spirit.
        float SampleScore()
        {
            if (useScoreStub)
            {
                // Random band centred on _stubBias so the timeline produces a believable
                // non-zero Average % without a camera.
                return Mathf.Clamp01(_stubBias + UnityEngine.Random.Range(-0.15f, 0.15f));
            }

            // Rig-vs-rig: score the driven avatar rig against the live trainer rig.
            // Both are baked the same way (PoseSignatureBaker), so no left/right ambiguity.
            if (poseDetector == null || !poseDetector.HasPose || trainerAnimator == null
                || poseDetector.AvatarAnimator == null)
                return 0f;

            float[] target = _baker.BakeFromRig(trainerAnimator);
            _baker.BakeFromRigInto(poseDetector.AvatarAnimator, _avatarAngles);
            return PoseScorer.ScoreAngles(_avatarAngles, target, ToleranceRad);
        }

        // ---------------------------------------------------------------- correct popup (Task C)

        // Show a brief self-contained "Correct!" overlay that fades out after ~0.6 s.
        // Built once at runtime (full-screen Canvas child) and reused each time.
        IEnumerator ShowCorrectPopup()
        {
            EnsureCorrectPopup();
            if (_correctPopupGo == null) yield break;

            _correctPopupGo.SetActive(true);
            if (_correctPopupText != null) _correctPopupText.text = "Correct!";

            // Style + pop via the shared CorrectEffect (null-safe: only if there's a TMP child)
            var tmp = _correctPopupGo.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                CorrectEffect.Style(_correctPopupGo, out var rt, out var grp);
                yield return StartCoroutine(CorrectEffect.Pop(rt, grp, 0.25f)); // 0.25 s pop-in
                yield return new WaitForSeconds(0.6f);                          // hold visible

                // Fade out over 0.25 s
                if (grp != null)
                {
                    float t = 0f;
                    while (t < 0.25f)
                    {
                        t += Time.deltaTime;
                        grp.alpha = Mathf.Lerp(1f, 0f, t / 0.25f);
                        yield return null;
                    }
                }
            }
            else
            {
                // Fallback: no TMP child — just show briefly
                yield return new WaitForSeconds(0.8f);
            }

            if (_correctPopupGo != null) _correctPopupGo.SetActive(false);
        }

        // Build the correct-popup overlay once: a full-screen Canvas child with a TMP label.
        void EnsureCorrectPopup()
        {
            if (_correctPopupGo != null) return;

            // Find the first Canvas in the scene to parent under
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            var go = new GameObject("WorldCorrectOverlay", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // TMP label
            var textGo = new GameObject("CorrectText", typeof(RectTransform));
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.SetParent(rt, false);
            textRt.anchorMin = new Vector2(0.1f, 0.35f);
            textRt.anchorMax = new Vector2(0.9f, 0.65f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            _correctPopupText = textGo.AddComponent<TextMeshProUGUI>();
            _correctPopupText.text = "Correct!";
            _correctPopupText.alignment = TextAlignmentOptions.Center;
            _correctPopupText.fontSize = 160f;
            _correctPopupText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _correctPopupText.color = Color.white;
            _correctPopupText.raycastTarget = false;

            _correctPopupGo = go;
            go.SetActive(false);
        }

        // ---------------------------------------------------------------- helpers

        void StartMusic()
        {
            if (musicSource == null || folkMusic == null) return;
            if (musicSource.isPlaying && musicSource.clip == folkMusic) return;
            musicSource.clip = folkMusic;
            musicSource.loop = true;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        void StopMusic()
        {
            if (musicSource == null) return;
            musicSource.Stop();
        }

        // Task C+D: trainer playback speed reduced to 0.75x so held poses are easier to follow.
        // This slows down the trainer animation globally (countdown + active segment), giving the
        // player more time to read and copy each held position.
        void PlayClip(ExerciseDefinition ex)
        {
            if (trainerAnimator == null || ex == null || ex.clip == null) return;
            trainerAnimator.speed = 0.75f; // Task D: ~25% slower than real-time — easier to follow
            trainerAnimator.CrossFade(ex.clip.name, 0.25f);
        }

        void UpdateLiveHud()
        {
            if (liveBarFill) liveBarFill.fillAmount = LiveScore01;
            if (percentText) percentText.text = $"{Mathf.RoundToInt(LiveScore01 * 100f)}%";
            Kinex.ScoreHud.Apply(percentText, liveBarFill, LiveScore01); // colour by band (<50 red, <70 yellow, >=70 green)
            if (encourageText)
                encourageText.text = LiveScore01 >= 0.75f ? "Great!" :
                                     LiveScore01 >= 0.5f ? "Nice!" : "Keep going!";
        }

        /// <summary>Show exactly one panel (or none), hide the rest.</summary>
        void ShowOnly(GameObject panel)
        {
            GameObject[] all = { introPanel, calibrationPanel, countdownPanel, hudPanel, transitionPanel, resultsPanel };
            foreach (var p in all)
                if (p != null) p.SetActive(p == panel);
        }
    }
}
