using UnityEngine;

namespace Kinex.AstroStance
{
    /// <summary>
    /// Sends AstroStance outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter (same JSON pattern as the other game bridges):
    ///   {"type":"astrostance_result", score, treasures, kicks, dodges, ...}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs, so this is safe in play mode.
    /// </summary>
    [RequireComponent(typeof(AstroStanceDirector))]
    public class AstroResultBridge : MonoBehaviour
    {
        AstroStanceDirector _director;

        void Awake() => _director = GetComponent<AstroStanceDirector>();

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

        void HandleComplete(AstroResult result) => SendToFlutter.Send(BuildMessage(result));

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");

        /// <summary>Splice the "type" tag in front of the serialized payload. Static + pure so
        /// the editor self-test can validate the wire format without a scene.</summary>
        public static string BuildMessage(AstroResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"astrostance_result\"," + body.Substring(1);
        }
    }
}
