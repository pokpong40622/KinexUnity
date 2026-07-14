using UnityEngine;

namespace Kinex.MirrorGame
{
    /// <summary>
    /// Sends MirrorGameDirector outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter — same JSON pattern as Kinex.BattleGame.BattleResultBridge:
    ///   {"type":"mirrorgame_result", posesCompleted, coins, stars, ...}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs, so this is safe in play mode.
    /// </summary>
    [RequireComponent(typeof(MirrorGameDirector))]
    public class MirrorResultBridge : MonoBehaviour
    {
        MirrorGameDirector _director;

        void Awake() => _director = GetComponent<MirrorGameDirector>();

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

        void HandleComplete(MirrorResult result) => SendToFlutter.Send(BuildMessage(result));

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");

        /// <summary>Splice the "type" tag in front of the serialized payload. Static + pure so
        /// the editor self-test can validate the wire format without a scene.</summary>
        public static string BuildMessage(MirrorResult result)
        {
            string body = JsonUtility.ToJson(result); // starts with '{'
            return "{\"type\":\"mirrorgame_result\"," + body.Substring(1);
        }
    }
}
