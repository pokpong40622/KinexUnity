using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Kinex.FX
{
    /// <summary>
    /// Slow breathing pacer: a soft pulsing circle + a TMP label alternating inhale/exhale on a
    /// 4s/4s loop. Built entirely at runtime, RingGauge.Create-style — no art assets, no scene
    /// edits, default TMP font (RingGauge/GameHud never assign one either).
    /// </summary>
    public class BreathingCue : MonoBehaviour
    {
        const int TextureSize = 128;
        const float CycleSeconds = 4f;
        const float ScaleIn = 0.7f;
        const float ScaleOut = 1f;

        static Sprite s_DiscSprite;

        Image _circle;
        TMP_Text _label;
        RectTransform _root;

        public static BreathingCue Create(RectTransform parent)
        {
            EnsureSprite();

            var rootGo = new GameObject("BreathingCue", typeof(RectTransform));
            var rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(220f, 220f);

            var circleGo = new GameObject("Circle", typeof(RectTransform), typeof(Image));
            var circleRt = circleGo.GetComponent<RectTransform>();
            circleRt.SetParent(rootRt, false);
            StretchFull(circleRt);
            var circleImg = circleGo.GetComponent<Image>();
            circleImg.sprite = s_DiscSprite;
            circleImg.color = new Color(0.4f, 0.75f, 0.95f, 0.55f);
            circleImg.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.SetParent(rootRt, false);
            StretchFull(labelRt);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "หายใจเข้า…";
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = 32f;
            label.color = Color.white;
            label.raycastTarget = false;

            var cue = rootGo.AddComponent<BreathingCue>();
            cue._root = rootRt;
            cue._circle = circleImg;
            cue._label = label;
            cue.StartCoroutine(cue.Loop());
            return cue;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void SetVisible(bool v) => gameObject.SetActive(v);

        IEnumerator Loop()
        {
            Color baseColor = _circle.color;
            while (true)
            {
                yield return Phase("หายใจเข้า…", ScaleIn, ScaleOut, CycleSeconds, baseColor);
                yield return Phase("หายใจออก…", ScaleOut, ScaleIn, CycleSeconds, baseColor);
            }
        }

        IEnumerator Phase(string text, float fromScale, float toScale, float duration, Color baseColor)
        {
            _label.text = text;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                float s = Mathf.Lerp(fromScale, toScale, k);
                _root.localScale = new Vector3(s, s, 1f);
                float pulse = Mathf.Lerp(0.45f, 0.65f, k * (1f - k) * 4f); // subtle mid-breath brighten
                _circle.color = new Color(baseColor.r, baseColor.g, baseColor.b, pulse);
                yield return null;
            }
        }

        static void EnsureSprite()
        {
            if (s_DiscSprite != null) return;
            float radius = TextureSize * 0.5f;
            const float aa = 1.5f;
            var pixels = new Color32[TextureSize * TextureSize];
            var center = new Vector2(radius, radius);
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(radius - aa, radius, dist));
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            s_DiscSprite = Sprite.Create(tex, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
        }
    }
}
