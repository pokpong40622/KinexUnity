using UnityEngine;

namespace Kinex.TheDasher
{
    /// <summary>
    /// Sends TheDasher outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter (same JSON pattern as the other game bridges):
    ///   {"type":"thedasher_result", score, treasures, kicks, dodges, ...}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs, so this is safe in play mode.
    /// </summary>
    [RequireComponent(typeof(TheDasherDirector))]
    public class DasherResultBridge : MonoBehaviour
    {
        TheDasherDirector _director;

        void Awake() => _director = GetComponent<TheDasherDirector>();

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

        void HandleComplete(DasherResult result) => SendToFlutter.Send(BuildMessage(result));

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");

        /// <summary>Splice the "type" tag in front of the serialized payload. Static + pure so
        /// the editor self-test can validate the wire format without a scene.</summary>
        public static string BuildMessage(DasherResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"thedasher_result\"," + body.Substring(1);
        }
    }
}
