using UnityEngine;

namespace Kinex.World
{
    /// <summary>
    /// Sends KinexWorldDirector outcomes to the Flutter host via flutter_embed_unity's
    /// SendToFlutter. Messages are JSON strings tagged with a "type" so Flutter's
    /// onMessageFromUnity (WorldGameScreen) can route them:
    ///   {"type":"world_result", routineId, averagePercent, durationSeconds, exercises:[...]}
    ///   {"type":"exit"}
    /// In the editor SendToFlutter.Send just logs (no Flutter host), so this is safe to
    /// run in play mode.
    /// </summary>
    [RequireComponent(typeof(KinexWorldDirector))]
    public class KinexWorldResultBridge : MonoBehaviour
    {
        KinexWorldDirector _director;

        void Awake() => _director = GetComponent<KinexWorldDirector>();

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

        void HandleComplete(WorldSessionResult result)
        {
            // Inject the "type" tag in front of the serialized payload.
            string body = JsonUtility.ToJson(result); // starts with '{'
            string msg = "{\"type\":\"world_result\"," + body.Substring(1);
            SendToFlutter.Send(msg);
        }

        void HandleExit() => SendToFlutter.Send("{\"type\":\"exit\"}");
    }
}
