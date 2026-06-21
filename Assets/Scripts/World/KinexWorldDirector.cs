using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.MegaDance; // PoseScorer + PoseSignatureBaker (reused unchanged)

namespace Kinex.World
{
    /// <summary>
    /// Kinex World — instructor-led exercise class. Unlike MEGA DANCE (match a static pose,
    /// then advance), this runs on a FIXED TIMELINE and never waits for the player: each
    /// exercise plays for its duration while we continuously sample how closely the player
    /// matches the trainer's LIVE animated pose. The mean of those samples is the
    /// Average Performance Percentage.
    ///
    /// Scoring reuses PoseScorer.Score against a target baked every tick from the trainer's
    /// current rig pose (PoseSignatureBaker.BakeFromRig) — so a continuously-animating
    /// trainer feeds scoring for free, with no pre-baked static targets.
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
        [Tooltip("Player pose source. Optional while useScoreStub is on.")]
        public MediaPipePoseDetector poseDetector;

        [Header("Scoring")]
        [Tooltip("Skip the camera and feed a random score — lets the full timeline run in-editor. " +
                 "OFF by default so scoring routes through the real PoseScorer vs the live trainer pose " +
                 "(the random stub made every pose read 70-80%). Turn ON only for camera-free editor walkthroughs.")]
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
        [Range(0f, 4f)] public float transitionSeconds = 2f;
        [Tooltip("Auto-begin after the intro card (Flutter/UI may call BeginAfterIntro instead).")]
        public bool autoBeginIntro = true;
        [Range(0f, 6f)] public float introSeconds = 3f;

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
        Coroutine _run;
        float _stubBias = 0.78f; // centre of the random stub band

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

            // Intro card
            Current = State.Intro;
            ShowOnly(introPanel);
            StartMusic();
            if (routineNameText) routineNameText.text = routine.englishName;
            if (autoBeginIntro) yield return new WaitForSeconds(introSeconds);

            // Calibration removed — the class starts straight from the intro into the first
            // exercise. (The avatar is driven directly from live 2D landmarks, no per-user
            // T-pose capture needed.)

            float totalDuration = 0f;
            float weightedScoreSum = 0f;

            for (int i = 0; i < routine.exercises.Length; i++)
            {
                var ex = routine.exercises[i];
                if (ex == null) continue;
                CurrentIndex = i;

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
            ShowOnly(introPanel);
        }

        IEnumerator Calibrate()
        {
            Current = State.Calibrating;
            ShowOnly(calibrationPanel);
            if (calibPromptText) calibPromptText.text = "Stand in a T-pose\narms straight out to the sides";
            if (calibCountdownText) calibCountdownText.text = "";

            poseDetector.StartCalibration();

            // The detector's routine sets Prompting synchronously, then drives
            // Prompting → Counting → Done → Idle. In the editor (no webcam) StartCalibration is a
            // no-op so the phase stays Idle and we fall straight through — no hang.
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

            // Unity HUD uses the Latin (Montserrat) TMP fonts — show English here; the polished
            // Thai copy lives in the Flutter shell.
            if (exerciseNameText) exerciseNameText.text =
                string.IsNullOrEmpty(ex.englishName) ? ex.id : ex.englishName;
            if (exerciseCounterText) exerciseCounterText.text = $"{index + 1}/{routine.ExerciseCount}";
            if (cameraHint) cameraHint.SetActive(ex.legsRequired);
            if (ex.legsRequired && cameraHintText)
                cameraHintText.text = "Step back ~2m — full body in view";

            float elapsed = 0f, tick = 0f, acc = 0f;
            int samples = 0;
            LiveScore01 = 0f;

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

        float SampleScore()
        {
            if (useScoreStub)
            {
                // Random band centred on _stubBias so the timeline produces a believable
                // non-zero Average % without a camera.
                return Mathf.Clamp01(_stubBias + UnityEngine.Random.Range(-0.15f, 0.15f));
            }

            if (poseDetector == null || !poseDetector.HasPose || trainerAnimator == null)
                return 0f;

            float[] target = _baker.BakeFromRig(trainerAnimator);
            return PoseScorer.Score(poseDetector.LatestKeypoints, poseDetector.LatestConfidence,
                                    target, minConfidence, ToleranceRad);
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

        void PlayClip(ExerciseDefinition ex)
        {
            // If an exercise AnimationClip is authored, CrossFade to it (Animator state named
            // after the clip). Otherwise the trainer is animated by TrainerPoseController
            // (continuous baked-pose demonstration) independently of the director.
            if (trainerAnimator == null || ex == null || ex.clip == null) return;
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
