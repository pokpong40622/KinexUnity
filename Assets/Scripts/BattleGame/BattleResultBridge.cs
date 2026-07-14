using UnityEngine;

namespace Kinex.BattleGame
{
    /// <summary>
    /// Sends BattleDirector outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter (same JSON pattern as FruitGameResultBridge / BalanceQuestResultBridge):
    ///   {"type":"battlegame_result", level, monstersDefeated, coins, ...}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs, so this is safe in play mode.
    /// </summary>
    [RequireComponent(typeof(BattleDirector))]
    public class BattleResultBridge : MonoBehaviour
    {
        BattleDirector _director;

        void Awake() => _director = GetComponent<BattleDirector>();

        void OnEnable()
        {
            _director.OnSessionComplete += HandleComplete;
            _director.OnExitRequested += HandleExit;
        }

        void OnDisable()
        {
            _director.OnSessionComplete -= HandleComplete;
            _director.OnExitRequested -= HandleExit;
        }

        void HandleComplete(BattleResult result) => SendToFlutter.Send(BuildMessage(result));

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");

        /// <summary>Splice the "type" tag in front of the serialized payload. Static + pure so
        /// the editor self-test can validate the wire format without a scene.</summary>
        public static string BuildMessage(BattleResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"battlegame_result\"," + body.Substring(1);
        }
    }
}
