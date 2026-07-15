using UnityEngine;
#if UNITY_EDITOR && KINEX_MEDIAPIPE
using System.IO;
using UnityEngine.InputSystem;
#endif

namespace Kinex.AstroStance
{
    /// <summary>
    /// EDITOR-ONLY Play-mode test aid. While the game is playing, press the NUMPAD to REPLAY one of
    /// the recorded pose clips into the pose detector INSTEAD of the live camera — so every pose can
    /// be exercised at a desk with no webcam:
    ///
    ///   numpad 1 = stand-sit-stand   2 = step left   3 = step right   4 = kick left   5 = kick right
    ///   numpad 0 = stop replay (hand control back to the live camera)
    ///
    /// It feeds the RECORDED LANDMARK JSON (Assets/AstroStance/TestFeeds/*.json), not the .mp4, because
    /// the tablet clips don't decode in Windows Media Foundation. The JSON holds the exact per-frame
    /// pose the clip captured, so playback is deterministic and needs no codec. All of this is compiled
    /// out of player builds (#if UNITY_EDITOR), so the shipped game always uses the live camera.
    /// Uses Keyboard.current (Input System), never legacy Input.
    /// </summary>
    public class AstroTestClipSwitcher : MonoBehaviour
    {
        [Tooltip("The MediaPipePoseDetector the numpad keys feed recorded frames into. Wired by the scene builder.")]
        public MediaPipePoseDetector detector;

#if UNITY_EDITOR && KINEX_MEDIAPIPE
        const string FeedDir = "Assets/AstroStance/TestFeeds";
        static readonly string[] ClipNames = { "standsitstand", "goleft", "goright", "kickleft", "kickright" };

        [System.Serializable] class Feed { public string name; public float fps; public Frame[] frames; }
        [System.Serializable] class Frame { public float[] v; }

        Feed[] _feeds;
        int _active = -1;   // index into _feeds currently replaying, or -1 for none
        float _elapsed;
        bool _replayScored;  // has the director been switched to live-detector mode yet?

        void Awake()
        {
            _feeds = new Feed[ClipNames.Length];
            for (int i = 0; i < ClipNames.Length; i++)
            {
                string path = Path.Combine(FeedDir, ClipNames[i] + ".json");
                if (File.Exists(path))
                {
                    var f = JsonUtility.FromJson<Feed>(File.ReadAllText(path));
                    if (f != null && f.frames != null && f.frames.Length > 0) _feeds[i] = f;
                }
                if (_feeds[i] == null)
                    Debug.LogWarning($"[AstroTestClipSwitcher] no landmark json for '{ClipNames[i]}' at {path}");
            }
        }

        void Update()
        {
            if (detector == null || _feeds == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            var keys = new[] { kb.numpad1Key, kb.numpad2Key, kb.numpad3Key, kb.numpad4Key, kb.numpad5Key };
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].wasPressedThisFrame && _feeds[i] != null)
                {
                    _active = i;
                    _elapsed = 0f;
                    // First replay: feed one standing frame, then flip the director to live-detector
                    // mode so the replayed pose is actually scored (editor default is keyboard stub).
                    if (!_replayScored)
                    {
                        detector.InjectRecordedFrame(_feeds[i].frames[0].v);
                        var dir = FindObjectOfType<AstroStanceDirector>();
                        if (dir != null) dir.BeginReplayTest();
                        _replayScored = true;
                    }
                    Debug.Log($"[AstroTestClipSwitcher] numpad{i + 1} → replay {ClipNames[i]} ({_feeds[i].frames.Length} frames)");
                }

            if (kb.numpad0Key.wasPressedThisFrame)
            {
                _active = -1;
                detector.StopReplay();
                Debug.Log("[AstroTestClipSwitcher] numpad0 → stop replay (live camera)");
            }

            if (_active < 0) return;

            // Feed the frame at the current playback time; loop the recording.
            var feed = _feeds[_active];
            _elapsed += Time.deltaTime;
            int idx = Mathf.FloorToInt(_elapsed * Mathf.Max(feed.fps, 1f)) % feed.frames.Length;
            detector.InjectRecordedFrame(feed.frames[idx].v);
        }
#endif
    }
}
