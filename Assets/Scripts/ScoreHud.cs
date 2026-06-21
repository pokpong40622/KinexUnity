using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Kinex
{
    /// <summary>
    /// Shared score-HUD styling used by both MEGA DANCE and Kinex World. Keeps the
    /// pass threshold and the three colour bands defined in ONE place so both games
    /// colour the percent text + bar fill identically and show the same pass line.
    /// Pure helpers — no scene refs, callable from either director's per-frame update.
    /// </summary>
    public static class ScoreHud
    {
        /// <summary>Score (0..1) at/above which a pose counts as passed.</summary>
        public const float PassThreshold = 0.70f;

        // Bands: <50% red/orange, 50-69% yellow, >=70% green.
        public static readonly Color Red    = new Color(0.90f, 0.30f, 0.20f); // red/orange
        public static readonly Color Yellow = new Color(0.95f, 0.80f, 0.20f);
        public static readonly Color Green  = new Color(0.30f, 0.80f, 0.35f);

        /// <summary>Pick the band colour for a 0..1 score.</summary>
        public static Color BandColor(float score01)
        {
            if (score01 >= PassThreshold) return Green;
            if (score01 >= 0.50f)         return Yellow;
            return Red;
        }

        /// <summary>Colour a percent label + bar fill by band in one call (null-safe).</summary>
        public static void Apply(TMP_Text percent, Image barFill, float score01)
        {
            Color c = BandColor(score01);
            if (percent != null) percent.color = c;
            if (barFill != null) barFill.color = c;
        }

        /// <summary>
        /// Create a thin vertical pass-line marker as a child of <paramref name="bar"/>,
        /// positioned at x = PassThreshold of the bar's width. Idempotent: returns the
        /// existing marker if it was already created. Built at runtime so we never hand-edit
        /// scene/prefab YAML. Returns null if the bar has no RectTransform.
        /// </summary>
        public static GameObject EnsurePassLine(Image bar)
        {
            if (bar == null) return null;
            var barRt = bar.rectTransform;
            const string markerName = "PassLineMarker";
            var existing = barRt.Find(markerName);
            if (existing != null) return existing.gameObject;

            var go = new GameObject(markerName, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(barRt, false);

            // Anchor a thin full-height vertical line at the 70% mark of the bar.
            rt.anchorMin = new Vector2(PassThreshold, 0f);
            rt.anchorMax = new Vector2(PassThreshold, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(3f, 0f); // 3px wide, full bar height

            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.9f); // white target line
            img.raycastTarget = false;
            return go;
        }
    }
}
