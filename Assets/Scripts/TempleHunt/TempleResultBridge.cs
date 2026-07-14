using UnityEngine;

namespace Kinex.TempleHunt
{
    /// <summary>
    /// Sends TempleHuntDirector outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter — same JSON pattern as Kinex.MirrorGame.MirrorResultBridge:
    ///   {"type":"templehunt_result", chambersCompleted, legLifts, stands, ...}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs, so this is safe in play mode.
    /// </summary>
    [RequireComponent(typeof(TempleHuntDirector))]
    public class TempleResultBridge : MonoBehaviour
    {
        TempleHuntDirector _director;

        void Awake() => _director = GetComponent<TempleHuntDirector>();

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

        void HandleComplete(TempleHuntResult result) => SendToFlutter.Send(BuildMessage(result));

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");

        /// <summary>Splice the "type" tag in front of the serialized payload. Static + pure so
        /// the editor self-test can validate the wire format without a scene.</summary>
        public static string BuildMessage(TempleHuntResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"templehunt_result\"," + body.Substring(1);
        }
    }
}
