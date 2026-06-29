using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Kinex.MegaDance
{
    /// <summary>
    /// Styles + animates the big "Correct!" overlay to match the Figma MegaDanceCorrect frame:
    /// large ITALIC text with a gradient fill, thick white outline, drop shadow, over a dimmed
    /// full-screen backdrop, popping in with a quick scale + fade.
    ///
    /// Everything is built/applied at RUNTIME (no prefab/scene YAML edits): point it at the
    /// existing correct-overlay GameObject and call Play(). The backdrop is created once and
    /// reused. Styling is idempotent.
    /// </summary>
    public static class CorrectEffect
    {
        // Restyle the TMP text inside the overlay and ensure a dim backdrop sits behind it.
        // Returns the text transform so the caller can drive the pop animation.
        public static void Style(GameObject overlay, out RectTransform textRt, out CanvasGroup textGroup)
        {
            textRt = null;
            textGroup = null;
            if (overlay == null) return;

            EnsureBackdrop(overlay);

            var tmp = overlay.GetComponentInChildren<TMP_Text>(true);
            if (tmp == null) return;

            // Big italic Montserrat-ish look (uses whatever TMP font is already assigned).
            tmp.fontStyle = FontStyles.Italic | FontStyles.Bold;
            tmp.enableAutoSizing = false;
            tmp.fontSize = 100f;                       // smaller so "Correct!" fits one line
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;            // never break onto a second line
            tmp.overflowMode = TextOverflowModes.Overflow;

            // Gradient fill (white→light green, top→bottom).
            tmp.enableVertexGradient = true;
            tmp.colorGradient = new VertexGradient(
                new Color(1f, 1f, 1f),       // top-left
                new Color(1f, 1f, 1f),       // top-right
                new Color(0.55f, 0.95f, 0.6f), // bottom-left
                new Color(0.40f, 0.85f, 0.5f)  // bottom-right
            );

            // Thick white outline (~7px feel) via the shared TMP material's outline channel.
            // fontMaterial gives this label its own material instance so we don't tint others.
            var mat = tmp.fontMaterial;
            if (mat != null)
            {
                if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
                    mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.white);
                if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
                    mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.25f); // 0..1, thick
                // Soft drop shadow (offset 0,4 feel; blurred; black @45%).
                if (mat.HasProperty(ShaderUtilities.ID_UnderlayColor))
                    mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.45f));
                if (mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
                    mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                if (mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetY))
                    mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.5f);
                if (mat.HasProperty(ShaderUtilities.ID_UnderlaySoftness))
                    mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f); // blur
            }

            textRt = tmp.rectTransform;
            // A CanvasGroup on the text lets us fade just the label independent of the backdrop.
            textGroup = tmp.GetComponent<CanvasGroup>();
            if (textGroup == null) textGroup = tmp.gameObject.AddComponent<CanvasGroup>();
        }

        // Full-screen dim panel behind the text. Uses a blur material if the project has one
        // named "UIBlur"; otherwise a 60%-black translucent panel (the safe, always-available
        // fallback). Created once as the first sibling so it renders behind the text.
        static void EnsureBackdrop(GameObject overlay)
        {
            var overlayRt = overlay.GetComponent<RectTransform>();
            if (overlayRt == null) return;
            const string name = "CorrectBackdrop";
            if (overlayRt.Find(name) != null) return;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(overlayRt, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling(); // behind the "Correct!" text

            var img = go.GetComponent<Image>();
            var blur = Resources.Load<Material>("UIBlur");
            if (blur != null) { img.material = blur; img.color = new Color(0f, 0f, 0f, 0.35f); }
            else              { img.color = new Color(0f, 0f, 0f, 0.6f); } // dark translucent fallback
            img.raycastTarget = true; // swallow taps under the overlay
        }

        /// <summary>Pop animation: scale 0.8→1.0 + fade 0→1 over ~duration seconds.</summary>
        public static IEnumerator Pop(RectTransform textRt, CanvasGroup group, float duration = 0.3f)
        {
            if (textRt == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float e = 1f - (1f - k) * (1f - k); // ease-out
                float s = Mathf.Lerp(0.8f, 1f, e);
                textRt.localScale = new Vector3(s, s, 1f);
                if (group != null) group.alpha = e;
                yield return null;
            }
            textRt.localScale = Vector3.one;
            if (group != null) group.alpha = 1f;
        }
    }
}
