using UnityEngine;
using TMPro;

namespace Kinex.TempleHunt
{
    /// <summary>
    /// The friendly parrot guide "โปโล่" — a UI mascot card in the corner of the HUD, not a 3D
    /// character. The UIBuilder constructs the body (a few Image primitives) and the speech
    /// bubble; this component just makes it feel alive: a gentle sine bob on the body and a
    /// scale-pulse whenever it says a new line. The director calls Say() alongside every TTS
    /// Speak() so what the player hears is always also on screen.
    /// </summary>
    public class ParrotGuide : MonoBehaviour
    {
        const float BobAmplitude = 7f;   // pixels
        const float BobSpeed = 1.7f;
        const float PulseScale = 1.12f;
        const float PulseSeconds = 0.22f;

        [Tooltip("The parrot body root — bobs up and down.")]
        public RectTransform body;
        [Tooltip("Speech bubble text next to the parrot.")]
        public TMP_Text bubbleText;
        [Tooltip("Speech bubble background — hidden while there is no line.")]
        public GameObject bubble;

        Vector2 _bodyBasePos;
        float _bobClock;
        float _pulseLeft;

        void Awake()
        {
            if (body != null) _bodyBasePos = body.anchoredPosition;
            if (bubble != null && bubbleText != null && string.IsNullOrEmpty(bubbleText.text))
                bubble.SetActive(false);
        }

        /// <summary>Show a line in the speech bubble and pulse the parrot.</summary>
        public void Say(string line)
        {
            if (bubbleText != null) bubbleText.text = line;
            if (bubble != null) bubble.SetActive(!string.IsNullOrEmpty(line));
            _pulseLeft = PulseSeconds;
        }

        void Update()
        {
            if (body == null) return;

            _bobClock += Time.deltaTime * BobSpeed;
            body.anchoredPosition = _bodyBasePos + Vector2.up * (Mathf.Sin(_bobClock * Mathf.PI * 2f) * BobAmplitude);

            if (_pulseLeft > 0f)
            {
                _pulseLeft -= Time.deltaTime;
                float t = Mathf.Clamp01(_pulseLeft / PulseSeconds);
                // Quick out, ease back: peak at the start of the pulse, settle to 1.
                float s = 1f + (PulseScale - 1f) * Mathf.Sin(t * Mathf.PI);
                body.localScale = Vector3.one * s;
            }
            else if (body.localScale != Vector3.one)
            {
                body.localScale = Vector3.one;
            }
        }
    }
}
