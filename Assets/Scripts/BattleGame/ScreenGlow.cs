using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Kinex.BattleGame
{
    /// <summary>
    /// Procedural soft-edge "glow frame" — a UI Image whose alpha rises toward the rect's border
    /// and fades to nothing in the middle (same generate-a-gradient-texture-once technique
    /// Kinex.UI.RingGauge uses for its ring/disc sprites, applied to a rectangle instead of a
    /// circle). One texture, two jobs:
    ///  1. A full-screen instance: a transient colored PULSE for success/fail/enemy-hit feedback.
    ///  2. A card-sized instance: a continuously-tinted border whose alpha tracks how close the
    ///     player is to clearing the current pose card ("getting warmer").
    /// </summary>
    public class ScreenGlow : MonoBehaviour
    {
        const int TextureSize = 256;
        const float InnerFrac = 0.62f; // fully transparent inside this fraction of the half-extent

        static Sprite s_FrameSprite;

        Image _image;
        Coroutine _pulseRoutine;

        public static ScreenGlow Create(RectTransform parent, string name)
        {
            EnsureSprite();
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.sprite = s_FrameSprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);

            var glow = go.AddComponent<ScreenGlow>();
            glow._image = img;
            return glow;
        }

        /// <summary>Instant color+alpha set — drives the continuous card-border "getting warmer" read.</summary>
        public void SetGlow(Color color, float alpha01)
        {
            if (_image == null) return;
            var c = color;
            c.a = Mathf.Clamp01(alpha01);
            _image.color = c;
        }

        /// <summary>Attack/hold/release pulse — drives the transient full-screen success/fail/hit flash.</summary>
        public void Pulse(Color color, float peakAlpha, float attack = 0.18f, float hold = 0.25f, float release = 0.6f)
        {
            if (!isActiveAndEnabled) return;
            if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
            _pulseRoutine = StartCoroutine(PulseRoutine(color, peakAlpha, attack, hold, release));
        }

        IEnumerator PulseRoutine(Color color, float peakAlpha, float attack, float hold, float release)
        {
            float t = 0f;
            while (t < attack)
            {
                t += Time.deltaTime;
                SetGlow(color, Mathf.Lerp(0f, peakAlpha, attack > 0f ? t / attack : 1f));
                yield return null;
            }
            SetGlow(color, peakAlpha);
            yield return new WaitForSeconds(hold);
            t = 0f;
            while (t < release)
            {
                t += Time.deltaTime;
                SetGlow(color, Mathf.Lerp(peakAlpha, 0f, release > 0f ? t / release : 1f));
                yield return null;
            }
            SetGlow(color, 0f);
            _pulseRoutine = null;
        }

        static void EnsureSprite()
        {
            if (s_FrameSprite != null) return;

            var pixels = new Color32[TextureSize * TextureSize];
            float half = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) - half) / half;
                    float ny = Mathf.Abs((y + 0.5f) - half) / half;
                    float edge = Mathf.Max(nx, ny); // 0 at center, 1 at the rect border
                    float alpha = Mathf.Clamp01((edge - InnerFrac) / (1f - InnerFrac));
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha * alpha); // ease-in toward the edge
                }
            }

            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            s_FrameSprite = Sprite.Create(tex, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
        }
    }
}
