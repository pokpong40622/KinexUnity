using UnityEngine;
using PonyuDev.SherpaOnnx.Tts;

namespace Kinex.MegaDance
{
    /// <summary>
    /// Speaks the short coaching lines (from PoseHint) using the offline Piper voice
    /// via sherpa-onnx. Thin wrapper over the package's
    /// <see cref="TtsOrchestrator"/> that adds the gameplay cadence we want:
    /// say a line only when it CHANGES, never talk over the current line, and keep a
    /// minimum gap between lines.
    ///
    /// Safe before the voice model / native libs are installed: every call is a no-op
    /// until the engine reports ready, so the game just stays quiet (no errors).
    ///
    /// Setup: drop this on a GameObject next to a <see cref="TtsOrchestrator"/>, assign
    /// the orchestrator, and assign this to MegaDanceManager.voice.
    /// </summary>
    public class VoiceCoach : MonoBehaviour
    {
        [Tooltip("The sherpa-onnx TTS orchestrator that owns the loaded Piper voice.")]
        [SerializeField] TtsOrchestrator tts;

        [Tooltip("Minimum seconds between spoken coaching lines.")]
        public float minGapSeconds = 2f;

        string _lastSpoken = "";
        float _lastSpokeAt = -999f;

        // Engine is loaded AND idle. ActivePlaybackCount > 0 means a line is playing.
        bool Ready    => tts != null && tts.IsInitialized && tts.Service != null && tts.Service.IsReady;
        bool Speaking => tts != null && tts.ActivePlaybackCount > 0;

        /// <summary>
        /// Speak a coaching line — but only if it differs from the last line, nothing is
        /// currently playing, and at least <see cref="minGapSeconds"/> has elapsed.
        /// Stays silent until the engine is ready.
        /// </summary>
        public async void Speak(string text)
        {
            if (!Ready || string.IsNullOrEmpty(text)) return;
            if (Speaking) return;                                 // don't talk over the current line
            if (text == _lastSpoken) return;                      // only speak on change
            if (Time.unscaledTime - _lastSpokeAt < minGapSeconds) return;

            _lastSpoken = text;
            _lastSpokeAt = Time.unscaledTime;
            try { await tts.GenerateAndPlayWithHandleAsync(text); }
            catch (System.Exception e) { Debug.LogWarning($"[VoiceCoach] {e.Message}"); }
        }
    }
}
