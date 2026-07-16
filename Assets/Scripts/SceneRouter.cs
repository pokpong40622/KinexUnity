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
    }
}
