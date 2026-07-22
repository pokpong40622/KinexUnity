using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Kinex.Shared;

namespace Collapse
{
    public enum GamePhase
    {
        Ready,
        Framing,
        Countdown,
        Rest,
        Warning,
        Gap,
        Victory,
        GameOver,
        Demo // demo slice finished (Flutter quick tour) — idle terminal state, no results flow
    }

    public enum WarningSide
    {
        None,
        Left,
        Right,
        Both
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private FloorHalf leftFloor;
        [SerializeField] private FloorHalf rightFloor;
        [SerializeField] private PlayerCharacter player;

        [SerializeField] private float warnDuration = 4f;
        [SerializeField] private float gapDuration = 5f;
        [SerializeField] private float restDuration = 6f;
        [SerializeField] private float totalTime = 180f;
        [SerializeField] private float graceWindow = 0.5f;
        [SerializeField] private float countdownTime = 3f;
        [SerializeField] private int maxHearts = 3;
        [SerializeField, Range(0f, 1f)] private float bothSidesChance = 0.25f;
        [Tooltip("If TRUE the countdown starts the instant the scene loads. If FALSE the game waits in " +
                 "Ready until StartFromReady() (the in-scene menu's PLAY button) — used so the menu can " +
                 "show the character's face first, then rotate the camera into play.")]
        [SerializeField] private bool autoStart = true;

        [Header("Framing gate")]
        [Tooltip("Seconds the WHOLE body must stay in frame before the countdown and the timer " +
                 "start — the same 'stand where we can see all of you' gate The Dasher uses, so a " +
                 "run never begins with the player's feet or head cropped off.")]
        [SerializeField] private float framingHoldSeconds = 1.0f;
        [Tooltip("Give up waiting for framing after this long and start anyway, so a camera fault " +
                 "can never leave the player stuck on the gate with no way forward.")]
        [SerializeField] private float framingTimeoutSeconds = 25f;

        private IPoseSource poseSource;
        private DasherPoseSource armSource;
        private OutOfFrameOverlay framingOverlay;
        private SosController sos; // emergency fall-alert overlay, shared with The Dasher

        public int Hearts { get; private set; }
        public float RemainingTime { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Ready;
        // Desktop-only "press space" hint removed — the game is touch/camera driven on
        // tablet and this text used to flash under the menu while it faded out.
        public string Prompt { get; private set; } = "";
        public WarningSide CurrentWarning { get; private set; } = WarningSide.None;
        public float PhaseTimeLeft { get; private set; }

        private Coroutine mainLoopRoutine;

        // Demo mode (Flutter quick tour): a short scripted slice instead of the endless MainLoop —
        // see SceneRouter.PendingDemoBeats, DemoLoop and EmitDemoPose below.
        private bool demoMode;
        private int demoTotal;
        private int demoResolved;
        private int demoPassed;

        // Demo pacing. The demo is a GUIDED tour, not a survival round: instead of MainLoop's
        // "you fail the moment you are in the wrong pose", the player gets a long window in which
        // reaching the required pose and holding it briefly is a pass, and running out is a skip.
        private const float DemoAttemptSeconds = 20f;
        private const float DemoHoldSeconds = 1.5f;
        private const float DemoPoseWaitSeconds = 10f;

        // The pose pipeline is actually tracking a body. Without waiting on this the first challenge
        // failed instantly: DasherPoseSource reports PoseState.None until it has landmarks, and the
        // old demo gap treated "not the required pose" as an immediate fail.
        private bool DemoPoseReady => armSource == null || armSource.IsTracking;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            poseSource = GetComponent<IPoseSource>();
            if (poseSource == null)
            {
                Debug.LogError("GameManager: no IPoseSource component found.");
            }

            armSource = GetComponent<DasherPoseSource>();
            if (armSource == null) armSource = FindFirstObjectByType<DasherPoseSource>();
            framingOverlay = FindFirstObjectByType<OutOfFrameOverlay>(FindObjectsInactive.Include);

            // Emergency fall-alert overlay (SOS), reused from The Dasher. Watches the same
            // MediaPipePoseDetector the arm-mirroring already reads from (via DasherPoseSource) and
            // pops a red "call your relatives" card if the player goes down during a real run.
            sos = gameObject.AddComponent<SosController>();
            sos.poseDetector = armSource != null ? armSource.Detector : null;

            Hearts = maxHearts;
            RemainingTime = totalTime;

            // Demo mode pushed from Flutter via SceneRouter's static pending value — consumed and
            // reset immediately so a normal run started afterwards isn't accidentally a demo.
            demoTotal = Kinex.App.SceneRouter.PendingDemoBeats;
            Kinex.App.SceneRouter.PendingDemoBeats = 0;
            demoMode = demoTotal > 0;

            // The in-scene menu now shows the character's face first and calls StartFromReady()
            // when the player presses PLAY (autoStart=false). If there is no menu (autoStart=true),
            // kick off the countdown straight away.
            if (autoStart)
            {
                BeginCountdown();
            }
        }

        private void Update()
        {
            // Fall detection only runs during the actual survive phases (Warning/Gap/Rest) — never
            // while the player is still framing up, mid-countdown, or on a results screen, so a
            // stumble while getting into position can't lock into a false SOS.
            if (sos != null)
            {
                sos.autoFallDetect = Phase == GamePhase.Warning || Phase == GamePhase.Gap || Phase == GamePhase.Rest;
            }

            if (Time.timeScale == 0f)
            {
                return;
            }

            // Mirror the arms through the pre-run phases too, so the player can see the avatar
            // answering them while they are still getting into frame.
            if (armSource != null && player != null &&
                (Phase == GamePhase.Framing || Phase == GamePhase.Countdown))
            {
                player.MirrorArms(armSource.Arms);
            }

            var keyboard = Keyboard.current;
            bool spacePressed = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;

            switch (Phase)
            {
                case GamePhase.Ready:
                    if (spacePressed)
                    {
                        BeginCountdown();
                    }
                    break;

                case GamePhase.Victory:
                case GamePhase.GameOver:
                    if (spacePressed)
                    {
                        RestartGame();
                    }
                    break;
            }
        }

        public void StartFromReady()
        {
            if (Phase == GamePhase.Ready)
            {
                BeginCountdown();
            }
        }

        // DEBUG HOOK: wired to the on-screen timer tap (see GameHUD) so QA can preview the SOS
        // card immediately without staging a real fall. Remove if this ever needs to ship without
        // a debug trigger.
        public void DebugShowSos()
        {
            sos?.Show();
        }

        private void BeginCountdown()
        {
            mainLoopRoutine = StartCoroutine(FrameThenCountdownThenLoop());
        }

        /// <summary>
        /// Holds before the countdown until the player's WHOLE body is in frame, exactly like The
        /// Dasher's opening gate. The out-of-frame card (with its per-body-part lights) does the
        /// talking; this only waits on it. Starting the 180 s timer while the player is still
        /// half-cropped is what made runs begin unfairly — and it is also the one moment we know
        /// the player is standing flat and fully visible, so the tiptoe baseline is captured here.
        /// </summary>
        private IEnumerator FrameThenCountdownThenLoop()
        {
            // Let the opening camera move finish first. Phase stays Ready here, and the overlay is
            // suppressed in Ready, so the framing card cannot pop up over the rotation.
            var pan = Camera.main != null ? Camera.main.GetComponent<IntroCameraPan>() : null;
            while (pan != null && pan.IsPanning)
            {
                yield return null;
            }

            if (framingOverlay != null)
            {
                Phase = GamePhase.Framing;
                Prompt = "ยืนให้เห็นเต็มตัวในกล้อง";

                float held = 0f;
                float waited = 0f;
                while (held < framingHoldSeconds && waited < framingTimeoutSeconds)
                {
                    held = framingOverlay.FullBodyOk ? held + Time.deltaTime : 0f;
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (armSource != null)
                {
                    armSource.CaptureBaseline();
                }
            }

            Phase = GamePhase.Countdown;
            Prompt = "";
            yield return CountdownThenLoop();
        }

        private IEnumerator CountdownThenLoop()
        {
            float t = countdownTime;
            while (t > 0f)
            {
                PhaseTimeLeft = t;
                t -= Time.deltaTime;
                yield return null;
            }
            PhaseTimeLeft = 0f;

            RemainingTime = totalTime;
            mainLoopRoutine = StartCoroutine(demoMode ? DemoLoop() : MainLoop());
        }

        private IEnumerator MainLoop()
        {
            if (player != null)
            {
                player.ResetToCenter();
            }

            while (true)
            {
                // Rest phase
                Phase = GamePhase.Rest;
                Prompt = "";
                CurrentWarning = WarningSide.None;;
                float restTimer = restDuration;
                while (restTimer > 0f)
                {
                    ApplyPoseToCharacter(false);

                    RemainingTime -= Time.deltaTime;
                    if (RemainingTime <= 0f)
                    {
                        yield return HandleVictory();
                        yield break;
                    }

                    restTimer -= Time.deltaTime;
                    yield return null;
                }

                // Pick event
                bool bothEvent = Random.value < bothSidesChance;
                bool leftCollapses;
                bool rightCollapses;
                int safeSide;

                if (bothEvent)
                {
                    leftCollapses = true;
                    rightCollapses = true;
                    safeSide = 0;
                }
                else if (Random.value < 0.5f)
                {
                    leftCollapses = true;
                    rightCollapses = false;
                    safeSide = 1; // left collapses -> safe side is right
                }
                else
                {
                    leftCollapses = false;
                    rightCollapses = true;
                    safeSide = -1; // right collapses -> safe side is left
                }

                // Warning phase
                Phase = GamePhase.Warning;
                if (leftCollapses && leftFloor != null)
                {
                    leftFloor.BeginWarning();
                }
                if (rightCollapses && rightFloor != null)
                {
                    rightFloor.BeginWarning();
                }

                if (bothEvent)
                {
                    Prompt = "เขย่งปลายเท้า ค้างไว้!";
                    CurrentWarning = WarningSide.Both;
                }
                else if (safeSide < 0)
                {
                    // safeSide < 0 means the LEFT floor survives, so the player must balance on
                    // their LEFT leg — which is done by RAISING the right one. The prompt now names
                    // the leg to lift (the action) rather than the leg to stand on, so the sides
                    // read as swapped against the old wording on purpose.
                    Prompt = "ยกขาขวาขึ้น ค้างไว้!";
                    CurrentWarning = WarningSide.Left;
                }
                else
                {
                    Prompt = "ยกขาซ้ายขึ้น ค้างไว้!";
                    CurrentWarning = WarningSide.Right;
                }

                float warnTimer = warnDuration;
                while (warnTimer > 0f)
                {
                    PhaseTimeLeft = warnTimer;

                    ApplyPoseToCharacter(bothEvent);

                    RemainingTime -= Time.deltaTime;
                    if (RemainingTime <= 0f)
                    {
                        yield return HandleVictory();
                        yield break;
                    }

                    warnTimer -= Time.deltaTime;
                    yield return null;
                }
                PhaseTimeLeft = 0f;

                // Gap phase
                Phase = GamePhase.Gap;
                if (leftCollapses && leftFloor != null)
                {
                    leftFloor.Collapse();
                }
                if (rightCollapses && rightFloor != null)
                {
                    rightFloor.Collapse();
                }

                PoseState requiredPose = bothEvent
                    ? PoseState.Tiptoe
                    : (safeSide < 0 ? PoseState.OneLegLeft : PoseState.OneLegRight);

                float wrongTime = 0f;
                float gapTimer = gapDuration;
                bool failed = false;

                while (gapTimer > 0f)
                {
                    PhaseTimeLeft = gapTimer;

                    ApplyPoseToCharacter(bothEvent);

                    PoseState currentPose = poseSource != null ? poseSource.CurrentPose : PoseState.None;
                    if (currentPose != requiredPose)
                    {
                        wrongTime += Time.deltaTime;
                    }

                    if (wrongTime > graceWindow)
                    {
                        failed = true;
                        break;
                    }

                    RemainingTime -= Time.deltaTime;
                    if (RemainingTime <= 0f)
                    {
                        yield return HandleVictory();
                        yield break;
                    }

                    gapTimer -= Time.deltaTime;
                    yield return null;
                }
                PhaseTimeLeft = 0f;

                if (failed)
                {
                    if (player != null)
                    {
                        player.Fall();
                    }

                    LoseHeart();

                    if (leftCollapses && leftFloor != null)
                    {
                        leftFloor.Rebuild();
                    }
                    if (rightCollapses && rightFloor != null)
                    {
                        rightFloor.Rebuild();
                    }

                    if (Hearts <= 0)
                    {
                        yield return new WaitForSeconds(1.5f);
                        HandleGameOver();
                        yield break;
                    }

                    yield return new WaitForSeconds(1.5f);

                    if (player != null)
                    {
                        player.ResetToCenter();
                    }
                }
                else
                {
                    if (leftCollapses && leftFloor != null)
                    {
                        leftFloor.Rebuild();
                    }
                    if (rightCollapses && rightFloor != null)
                    {
                        rightFloor.Rebuild();
                    }

                    if (player != null)
                    {
                        player.StopHang();
                        player.ResetToCenter();
                    }

                    Prompt = "รอดแล้ว!";
                    yield return new WaitForSeconds(1f);
                }
            }
        }

        // ---- Demo mode: a short scripted slice instead of MainLoop's endless cycle. ----

        // Demo slice = one one-leg-stand challenge, then one tiptoe challenge (repeats that pair if
        // Flutter ever asks for more than 2 beats). Reuses the SAME Warning/Gap mechanics — floor
        // collapse, grace window, ApplyPoseToCharacter — as the real game, just without
        // hearts/RemainingTime/game-over: each result is reported to Flutter instead.
        private IEnumerator DemoLoop()
        {
            if (player != null)
            {
                player.ResetToCenter();
            }

            // Don't open the first challenge until the camera pipeline is actually seeing someone.
            // Capped so a camera fault reports skipped beats instead of hanging the tour.
            float waited = 0f;
            while (waited < DemoPoseWaitSeconds && !DemoPoseReady)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            for (int i = 0; i < demoTotal; i++)
            {
                yield return RunDemoChallenge(i % 2 == 1);
            }

            Phase = GamePhase.Demo; // idle terminal state — Update()'s switch has no case for it
            // `completed` = challenges the player PASSED (matches TheDasherDirector.EndDemoRun).
            SendToFlutter.Send("{\"type\":\"demo_done\",\"game\":\"quakeescape\",\"completed\":" +
                                demoPassed + ",\"total\":" + demoTotal + "}");
        }

        // One demo challenge: tiptoe=false is a one-leg-stand (right leg raised, left floor
        // collapses); tiptoe=true collapses both floors and requires PoseState.Tiptoe.
        private IEnumerator RunDemoChallenge(bool tiptoe)
        {
            PoseState requiredPose = tiptoe ? PoseState.Tiptoe : PoseState.OneLegRight;

            Phase = GamePhase.Warning;
            Prompt = tiptoe ? "เขย่งปลายเท้า ค้างไว้!" : "ยกขาขวาขึ้น ค้างไว้!";
            CurrentWarning = tiptoe ? WarningSide.Both : WarningSide.Left;
            if (leftFloor != null) leftFloor.BeginWarning();
            if (tiptoe && rightFloor != null) rightFloor.BeginWarning();

            float warnTimer = warnDuration;
            while (warnTimer > 0f)
            {
                PhaseTimeLeft = warnTimer;
                ApplyPoseToCharacter(tiptoe);
                warnTimer -= Time.deltaTime;
                yield return null;
            }
            PhaseTimeLeft = 0f;

            Phase = GamePhase.Gap;
            if (leftFloor != null) leftFloor.Collapse();
            if (tiptoe && rightFloor != null) rightFloor.Collapse();

            // Attempt window, the INVERSE of MainLoop's gap: hold the required pose for
            // DemoHoldSeconds at any point and the challenge is passed; run out of window and it is
            // skipped. Never an instant fail — the old "wrongTime > graceWindow" test failed on the
            // very first frame, before the player had even seen the prompt.
            float held = 0f;
            float attempt = DemoAttemptSeconds;
            bool ok = false;
            while (attempt > 0f)
            {
                PhaseTimeLeft = attempt;
                ApplyPoseToCharacter(tiptoe);

                PoseState currentPose = poseSource != null ? poseSource.CurrentPose : PoseState.None;
                held = currentPose == requiredPose ? held + Time.deltaTime : 0f;
                if (held >= DemoHoldSeconds)
                {
                    ok = true;
                    break;
                }

                attempt -= Time.deltaTime;
                yield return null;
            }
            PhaseTimeLeft = 0f;

            if (!ok && player != null)
            {
                player.Fall();
            }
            if (leftFloor != null) leftFloor.Rebuild();
            if (tiptoe && rightFloor != null) rightFloor.Rebuild();
            if (ok && player != null)
            {
                player.StopHang();
                player.ResetToCenter();
            }

            EmitDemoPose(ok);
            yield return new WaitForSeconds(1f); // brief settle before the next challenge
        }

        // Reports one resolved demo challenge to Flutter. See SceneRouter.PendingDemoBeats for the
        // contract.
        private void EmitDemoPose(bool ok)
        {
            int index = demoResolved;
            demoResolved++;
            if (ok) demoPassed++;
            SendToFlutter.Send("{\"type\":\"demo_pose\",\"game\":\"quakeescape\",\"index\":" + index +
                                ",\"ok\":" + (ok ? "true" : "false") + "}");
        }

        private void ApplyPoseToCharacter(bool bothEvent)
        {
            if (poseSource == null || player == null)
            {
                return;
            }

            // The legs drive the game; the arms are pure mirroring, so the avatar's hands follow the
            // player's instead of sitting frozen in whatever the canned pose put them in.
            if (armSource != null)
            {
                player.MirrorArms(armSource.Arms);
            }

            PoseState pose = poseSource.CurrentPose;

            switch (pose)
            {
                case PoseState.OneLegLeft:
                    player.MoveToSide(-1);
                    break;
                case PoseState.OneLegRight:
                    player.MoveToSide(1);
                    break;
                case PoseState.Tiptoe:
                    player.StartHang();
                    break;
                case PoseState.None:
                default:
                    player.MoveToSide(0);
                    player.StopHang();
                    break;
            }
        }

        private void LoseHeart()
        {
            Hearts = Mathf.Max(0, Hearts - 1);
        }

        private IEnumerator HandleVictory()
        {
            Phase = GamePhase.Victory;
            Prompt = "สำเร็จ!";
            PhaseTimeLeft = 0f;
            NotifyResult(true);
            yield return null;
        }

        private void HandleGameOver()
        {
            Phase = GamePhase.GameOver;
            Prompt = "จบเกม";
            PhaseTimeLeft = 0f;
            NotifyResult(false);
        }

        // Report the finished run to the Flutter host (flutter_embed_unity). Same
        // JSON shape the Flutter QuakeEscapeGameScreen parses: survived-the-timer
        // flag, hearts left, and seconds survived. Done here rather than in a
        // separate bridge component so no scene surgery is needed to wire it.
        private void NotifyResult(bool success)
        {
            float elapsed = Mathf.Round(totalTime - RemainingTime);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string json = "{\"type\":\"quakeescape_result\"," +
                "\"success\":" + (success ? "true" : "false") + "," +
                "\"heartsRemaining\":" + Hearts + "," +
                "\"durationSeconds\":" + elapsed.ToString(inv) + "}";
            SendToFlutter.Send(json);
        }

        public void RestartGame()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex);
        }
    }
}
