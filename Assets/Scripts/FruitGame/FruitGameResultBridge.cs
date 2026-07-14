using UnityEngine;

namespace Kinex.FruitGame
{
    /// <summary>
    /// Sends FruitGameManager outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter (same JSON pattern as KinexWorldResultBridge):
    ///   {"type":"fruitgame_result", reps, setsCompleted, accuracyPercent, ...}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs, so this is safe in play mode.
    /// </summary>
    [RequireComponent(typeof(FruitGameManager))]
    public class FruitGameResultBridge : MonoBehaviour
    {
        FruitGameManager _manager;

        void Awake() => _manager = GetComponent<FruitGameManager>();

        void OnEnable()
        {
            _manager.OnSessionComplete += HandleComplete;
            _manager.OnExitRequested += HandleExit;
        }

        void OnDisable()
        {
            _manager.OnSessionComplete -= HandleComplete;
            _manager.OnExitRequested -= HandleExit;
        }

        void HandleComplete(FruitGameResult result) => SendToFlutter.Send(BuildMessage(result));

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");

        /// <summary>Splice the "type" tag in front of the serialized payload. Static + pure so
        /// the editor self-test can validate the wire format without a scene.</summary>
        public static string BuildMessage(FruitGameResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"fruitgame_result\"," + body.Substring(1);
        }
    }
}
