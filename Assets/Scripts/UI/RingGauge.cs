using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Kinex.UI
{
    /// <summary>
    /// Reusable circular ring gauge for the game HUD (heart-rate, score, etc.).
    /// Built entirely at runtime from code — no art assets, no scene edits — following
    /// the same procedural-UI pattern as <see cref="Kinex.ScoreHud.EnsurePassLine"/>.
    /// Shows a big centre number, a small label, and a ring that fills clockwise from
    /// the top to represent a 0..1 value.
    /// </summary>
    public class RingGauge : MonoBehaviour
    {
        const int TextureSize = 256;
        const float RingThicknessFrac = 0.12f; // fraction of radius

        static Sprite s_DiscSprite;
        static Sprite s_RingSprite;

        Image _fillImage;
        TMP_Text _bigText;
        TMP_Text _smallText;
        RectTransform _root;

        float _targetFill;
        Coroutine _pulseRoutine;
        Vector3 _baseScale = Vector3.one;

        /// <summary>
        /// Builds a new ring gauge as a child of <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">Parent RectTransform (a Canvas or panel).</param>
        /// <param name="name">GameObject name for the gauge root.</param>
        /// <param name="anchor">Used for anchorMin, anchorMax AND pivot (e.g. (0,0) = bottom-left).</param>
        /// <param name="anchoredPos">Offset from the anchor corner, in pixels.</param>
        /// <param name="diameter">Gauge width/height in pixels.</param>
        public static RingGauge Create(RectTransform parent, string name, Vector2 anchor, Vector2 anchoredPos, float diameter)
        {
            EnsureSprites();

            var rootGo = new GameObject(name, typeof(RectTransform));
            var rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = anchor;
            rootRt.anchorMax = anchor;
            rootRt.pivot = anchor;
            rootRt.anchoredPosition = anchoredPos;
            rootRt.sizeDelta = new Vector2(diameter, diameter);

            // Backdrop disc — dark translucent so the gauge reads on any background.
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            var backdropRt = backdropGo.GetComponent<RectTransform>();
            backdropRt.SetParent(rootRt, false);
            StretchFull(backdropRt);
            var backdropImg = backdropGo.GetComponent<Image>();
            backdropImg.sprite = s_DiscSprite;
            backdropImg.type = Image.Type.Simple;
            backdropImg.color = new Color(0f, 0f, 0f, 0.55f);
            backdropImg.raycastTarget = false;

            // Ring track — dark grey, always full circle, sits behind the fill.
            var trackGo = new GameObject("Track", typeof(RectTransform), typeof(Image));
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.SetParent(rootRt, false);
            StretchFull(trackRt);
            var trackImg = trackGo.GetComponent<Image>();
            trackImg.sprite = s_RingSprite;
            trackImg.type = Image.Type.Simple;
            trackImg.color = new Color(0.6f, 0.6f, 0.6f, 0.35f);
            trackImg.raycastTarget = false;

            // Ring fill — radial 360, sweeps clockwise from the top.
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.SetParent(rootRt, false);
            StretchFull(fillRt);
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.sprite = s_RingSprite;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Radial360;
            fillImg.fillOrigin = (int)Image.Origin360.Top;
            fillImg.fillClockwise = true;
            fillImg.fillAmount = 0f;
            fillImg.color = Color.white;
            fillImg.raycastTarget = false;

            // Subtle white rim on top so the disc edge looks crisp.
            var rimGo = new GameObject("Rim", typeof(RectTransform), typeof(Image));
            var rimRt = rimGo.GetComponent<RectTransform>();
            rimRt.SetParent(rootRt, false);
            StretchFull(rimRt);
            var rimImg = rimGo.GetComponent<Image>();
            rimImg.sprite = s_RingSprite;
            rimImg.type = Image.Type.Simple;
            rimImg.color = new Color(1f, 1f, 1f, 0.18f);
            rimImg.raycastTarget = false;

            // Big centre number.
            var bigGo = new GameObject("BigText", typeof(RectTransform));
            var bigRt = bigGo.GetComponent<RectTransform>();
            bigRt.SetParent(rootRt, false);
            bigRt.anchorMin = new Vector2(0.5f, 0.55f);
            bigRt.anchorMax = new Vector2(0.5f, 0.55f);
            bigRt.pivot = new Vector2(0.5f, 0.5f);
            bigRt.anchoredPosition = Vector2.zero;
            bigRt.sizeDelta = new Vector2(diameter * 0.8f, diameter * 0.45f);
            var bigText = bigGo.AddComponent<TextMeshProUGUI>();
            bigText.text = "0";
            bigText.alignment = TextAlignmentOptions.Center;
            bigText.fontStyle = FontStyles.Bold;
            bigText.enableAutoSizing = true;
            bigText.fontSizeMin = 8f;
            bigText.fontSizeMax = diameter * 0.4f;
            bigText.color = Color.white;
            bigText.raycastTarget = false;

            // Small uppercase label beneath the number.
            var smallGo = new GameObject("SmallText", typeof(RectTransform));
            var smallRt = smallGo.GetComponent<RectTransform>();
            smallRt.SetParent(rootRt, false);
            smallRt.anchorMin = new Vector2(0.5f, 0.28f);
            smallRt.anchorMax = new Vector2(0.5f, 0.28f);
            smallRt.pivot = new Vector2(0.5f, 0.5f);
            smallRt.anchoredPosition = Vector2.zero;
            smallRt.sizeDelta = new Vector2(diameter * 0.85f, diameter * 0.18f);
            var smallText = smallGo.AddComponent<TextMeshProUGUI>();
            smallText.text = "";
            smallText.alignment = TextAlignmentOptions.Center;
            smallText.fontStyle = FontStyles.Normal;
            smallText.enableAutoSizing = true;
            smallText.fontSizeMin = 4f;
            smallText.fontSizeMax = diameter * 0.12f;
            smallText.color = new Color(1f, 1f, 1f, 0.75f);
            smallText.raycastTarget = false;

            var gauge = rootGo.AddComponent<RingGauge>();
            gauge._root = rootRt;
            gauge._fillImage = fillImg;
            gauge._bigText = bigText;
            gauge._smallText = smallText;
            gauge._baseScale = rootRt.localScale;
            return gauge;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Sets the fill target (0..1). Eases smoothly toward it in Update().</summary>
        public void SetFill(float t01)
        {
            _targetFill = Mathf.Clamp01(t01);
        }

        /// <summary>Sets the centre number and the small label beneath it.</summary>
        public void SetValue(string big, string small)
        {
            if (_bigText != null) _bigText.text = big;
            if (_smallText != null) _smallText.text = small;
        }

        /// <summary>Tints the ring fill and the big number to the given colour.</summary>
        public void SetColor(Color c)
        {
            if (_fillImage != null) _fillImage.color = c;
            if (_bigText != null) _bigText.color = c;
        }

        /// <summary>Quick scale-up-and-back "heartbeat" pulse on the gauge root.</summary>
        public void Pulse(float scale = 1.08f, float dur = 0.14f)
        {
            if (!isActiveAndEnabled) return;
            if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
            _pulseRoutine = StartCoroutine(PulseRoutine(scale, dur));
        }

        IEnumerator PulseRoutine(float scale, float dur)
        {
            if (_root == null) yield break;
            float half = Mathf.Max(0.01f, dur * 0.5f);
            Vector3 big = _baseScale * scale;

            float t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                _root.localScale = Vector3.Lerp(_baseScale, big, t / half);
                yield return null;
            }
            t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                _root.localScale = Vector3.Lerp(big, _baseScale, t / half);
                yield return null;
            }
            _root.localScale = _baseScale;
            _pulseRoutine = null;
        }

        void Update()
        {
            if (_fillImage == null) return;
            _fillImage.fillAmount = Mathf.MoveTowards(_fillImage.fillAmount, _targetFill, 10f * Time.deltaTime);
        }

        static void EnsureSprites()
        {
            if (s_DiscSprite != null && s_RingSprite != null) return;

            float radius = TextureSize * 0.5f;
            float ringThickness = radius * RingThicknessFrac;
            const float aa = 1.5f; // antialias width in pixels

            var discPixels = new Color32[TextureSize * TextureSize];
            var ringPixels = new Color32[TextureSize * TextureSize];
            Vector2 center = new Vector2(radius, radius);

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    int idx = y * TextureSize + x;

                    // Disc: solid white inside radius, faded over the last `aa` px.
                    float discAlpha = 1f - Mathf.InverseLerp(radius - aa, radius, dist);
                    discAlpha = Mathf.Clamp01(discAlpha);
                    discPixels[idx] = new Color(1f, 1f, 1f, discAlpha);

                    // Ring: annulus between (radius - ringThickness) and radius, both edges antialiased.
                    float outerAlpha = 1f - Mathf.InverseLerp(radius - aa, radius, dist);
                    float innerAlpha = Mathf.InverseLerp(radius - ringThickness - aa, radius - ringThickness, dist);
                    float ringAlpha = Mathf.Clamp01(Mathf.Min(outerAlpha, innerAlpha));
                    ringPixels[idx] = new Color(1f, 1f, 1f, ringAlpha);
                }
            }

            var discTex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            discTex.SetPixels32(discPixels);
            discTex.Apply();
            discTex.wrapMode = TextureWrapMode.Clamp;
            discTex.filterMode = FilterMode.Bilinear;

            var ringTex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            ringTex.SetPixels32(ringPixels);
            ringTex.Apply();
            ringTex.wrapMode = TextureWrapMode.Clamp;
            ringTex.filterMode = FilterMode.Bilinear;

            var rect = new Rect(0f, 0f, TextureSize, TextureSize);
            var pivot = new Vector2(0.5f, 0.5f);
            s_DiscSprite = Sprite.Create(discTex, rect, pivot);
            s_RingSprite = Sprite.Create(ringTex, rect, pivot);
        }
    }
}
