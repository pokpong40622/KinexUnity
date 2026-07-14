using UnityEngine;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Sends BalanceQuestDirector outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter, mirroring Kinex.World.KinexWorldResultBridge exactly:
    ///   {"type":"balancequest_result", averagePercent, coins, stars, durationSeconds, beats:[...]}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs (no Flutter host), so this is safe in Play mode.
    /// </summary>
    [RequireComponent(typeof(BalanceQuestDirector))]
    public class BalanceQuestResultBridge : MonoBehaviour
    {
        BalanceQuestDirector _director;

        void Awake() => _director = GetComponent<BalanceQuestDirector>();

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

        void HandleComplete(BalanceQuestResult result) => SendToFlutter.Send(ToJsonMessage(result));

        /// <summary>Public + static so BalanceQuestSelfTest validates the exact message Flutter
        /// will receive (type-tag splice included), not a re-typed copy.</summary>
        public static string ToJsonMessage(BalanceQuestResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"balancequest_result\"," + body.Substring(1);
        }

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");
    }
}
