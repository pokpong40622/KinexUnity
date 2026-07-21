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
