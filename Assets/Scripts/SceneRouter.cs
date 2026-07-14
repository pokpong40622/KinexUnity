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
        void Awake() => DontDestroyOnLoad(gameObject);

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
                case "fruitgame":
                    SceneManager.LoadScene("FruitGameScene");
                    break;
                case "balancequest":
                    SceneManager.LoadScene("BalanceQuestScene");
                    break;
                case "battlegame":
                    SceneManager.LoadScene("BattleGameScene");
                    break;
                case "mirrorgame":
                    SceneManager.LoadScene("MirrorGameScene");
                    break;
                case "templehunt":
                    SceneManager.LoadScene("TempleHuntScene");
                    break;
                default:
                    Debug.LogWarning($"[SceneRouter] Unknown game id: '{gameId}'");
                    break;
            }
        }
    }
}
