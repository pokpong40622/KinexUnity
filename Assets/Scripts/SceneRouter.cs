using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinex.App
{
    /// <summary>
    /// Picks which game scene the embedded Unity player shows. flutter_embed_unity runs
    /// a SINGLE Unity player, so the game on screen is decided by which scene we load —
    /// NOT by which Flutter screen embedded it. Without this, the first scene in Build
    /// Settings always boots, so every Flutter button showed the same game.
    ///
    /// Lives in the neutral Boot scene and survives scene loads (DontDestroyOnLoad).
    /// Flutter selects a game with:
    ///   sendToUnity("SceneRouter", "LoadGame", "megadance" | "world")
    /// On first boot we also announce readiness so Flutter can (re)send its choice once
    /// the player has finished initialising.
    /// </summary>
    public class SceneRouter : MonoBehaviour
    {
        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            // This player renders OFFSCREEN (flutter_embed_unity blits into a Flutter texture),
            // so it never gets normal display-driven VSync pacing. Left unset, Unity's frame rate
            // here is effectively undefined — it can free-run and burn CPU/GPU (thermal throttling
            // reads as "laggy even on a new tablet") or default to an unexpectedly low cap. Pin it
            // explicitly, once, for every game this player can load.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
        }

        void Start() => SendToFlutter.Send("{\"type\":\"unity_ready\"}");

        // Invoked by UnitySendMessage from Flutter. Must be public, single string arg.
        public void LoadGame(string gameId)
        {
            switch (gameId)
            {
                case "megadance":
                    SceneManager.LoadScene("MegaDanceScene");
                    break;
                case "world":
                    SceneManager.LoadScene("KinexWorldScene");
                    break;
                case "templehunt":
                    SceneManager.LoadScene("TempleHuntScene");
                    break;
                case "dancestar":
                    SceneManager.LoadScene("DanceStarScene");
                    break;
                case "motionlab":
                    SceneManager.LoadScene("MotionLabScene");
                    break;
                case "thedasher":
                    SceneManager.LoadScene("TheDasherScene");
                    break;
                case "hangglider":
                    SceneManager.LoadScene("HangGliderScene");
                    break;
                case "quakeescape":
                    // The menu now lives INSIDE the gameplay scene (logo + how-to-play + PLAY/EXIT
                    // over a live view of the character's face). PLAY rotates that camera into the
                    // play view — so we load the gameplay scene directly, not a separate menu scene.
                    SceneManager.LoadScene("QuakeEscapeScene");
                    break;
                default:
                    Debug.LogWarning($"[SceneRouter] Unknown game id: '{gameId}'");
                    break;
            }
        }

        // Pause / resume the currently-loaded game. Driven by the Hang Glider's
        // in-game pause overlay in Flutter. Time.timeScale is global, so this
        // persistent router can freeze any scene it lives above.
        //   sendToUnity("SceneRouter", "SetPaused", "true" | "false")
        public void SetPaused(string paused)
        {
            Time.timeScale = (paused == "true" || paused == "1") ? 0f : 1f;
        }

        // ── The Dasher run config ────────────────────────────────────────────────────────────────
        // Flutter sends these BEFORE LoadGame; the Dasher director reads them in Start(). Static so
        // the value survives the scene load between this persistent router and the scene's director
        // (which doesn't exist yet when Flutter sends the config). Single-string, UnitySendMessage-safe.
        //   sendToUnity("SceneRouter", "SetDifficulty", "easy" | "normal" | "hard")
        //   sendToUnity("SceneRouter", "SetTutorial",   "true" | "false")
        public static string PendingDifficulty = "normal";
        public static bool PendingTutorial = true;

        public void SetDifficulty(string d) =>
            PendingDifficulty = string.IsNullOrEmpty(d) ? "normal" : d.Trim().ToLowerInvariant();

        public void SetTutorial(string on) => PendingTutorial = (on == "true" || on == "1");

        // ── Demo mode (Flutter quick tour) ──────────────────────────────────────────────────────
        // Flutter sends this BEFORE LoadGame to run a SHORT scripted slice of a game instead of the
        // normal session, then reports each scripted beat/pose back via SendToFlutter.Send as
        // {"type":"demo_pose",...} and finishes with {"type":"demo_done",...}. 0 = normal play.
        // Static for the same cross-scene-load reason as PendingDifficulty/PendingTutorial above.
        // Each director MUST reset this to 0 once consumed in Start(), exactly like the pending
        // values above, so a normal game launched afterwards doesn't accidentally run as a demo.
        //   sendToUnity("SceneRouter", "SetDemo", "3")   // 0 = normal mode
        public static int PendingDemoBeats = 0;

        public void SetDemo(string n) =>
            PendingDemoBeats = int.TryParse(n, out int v) && v > 0 ? v : 0;

        // ── Coach mode (เรียนรู้ท่าฝึก live practice) ─────────────────────────────────────────────
        // Flutter sends this BEFORE LoadGame("motionlab") to turn the Motion Lab mirror into a live
        // coach for ONE exercise instead of the free test range. Empty = normal Motion Lab.
        // Static for the same cross-scene-load reason as PendingDifficulty above; MotionLabDirector
        // consumes AND clears it in Start() so a later normal run is never accidentally coached.
        //   sendToUnity("SceneRouter", "SetCoach", "hip_abduction")
        public static string PendingCoachPose = "";

        public void SetCoach(string poseId) =>
            PendingCoachPose = string.IsNullOrEmpty(poseId) ? "" : poseId.Trim().ToLowerInvariant();

        // ── Hand-board tilt (Hang Glider steering) ───────────────────────────────────────────────
        // The MPU6050 hand board talks BLE, which only Flutter can speak, so Flutter parses the
        // board's "TILT:x,y,z" lines and forwards the middle value (y = pitch, +right / -left) here
        // at ~30 Hz. Static for the same reason as the Dasher config above: this router outlives the
        // scene load, and the glider that consumes it doesn't exist yet when the stream starts.
        //   sendToUnity("SceneRouter", "SetHandTilt", "12.7")
        // Consumers must check HandTiltStamp before trusting HandTiltY — if the board is off or has
        // dropped out, the last value would otherwise sit here forever and steer the glider into a
        // wall. See PlayerMovement.handStaleSeconds.
        public static float HandTiltY;
        public static float HandTiltStamp;

        public void SetHandTilt(string v)
        {
            if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float deg))
            {
                HandTiltY = deg;
                HandTiltStamp = Time.unscaledTime;
            }
        }
    }
}
