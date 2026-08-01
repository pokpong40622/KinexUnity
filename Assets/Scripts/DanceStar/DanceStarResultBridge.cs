using UnityEngine;

namespace Kinex.DanceStar
{
    /// <summary>
    /// Sends DanceStarDirector outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter — same JSON pattern as Kinex.MirrorGame.MirrorResultBridge:
    ///   {"type":"dancestar_result", cardsCompleted, cardCount, totalScore, maxStreak,
    ///    accuracyPct, stars, coins, durationSeconds}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs, so this is safe in play mode.
    /// </summary>
    [RequireComponent(typeof(DanceStarDirector))]
    public class DanceStarResultBridge : MonoBehaviour
    {
        DanceStarDirector _director;

        void Awake() => _director = GetComponent<DanceStarDirector>();

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

        void HandleComplete(DanceStarResult result) => SendToFlutter.Send(BuildMessage(result));

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");

        /// <summary>Splice the "type" tag in front of the serialized payload. Static + pure so
        /// the editor self-test can validate the wire format without a scene.</summary>
        public static string BuildMessage(DanceStarResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"dancestar_result\"," + body.Substring(1);
        }
    }
}
