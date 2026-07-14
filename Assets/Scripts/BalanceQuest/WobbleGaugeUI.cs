using UnityEngine;
using Kinex.UI;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Hold/wobble gauge shown only during Bridge and TandemStand — thin wrapper around the
    /// shared RingGauge: fill = hold progress toward the beat's hold target, color = wobble-
    /// driven (calm green -> shaky red).
    /// </summary>
    public class WobbleGaugeUI : MonoBehaviour
    {
        RingGauge _ring;

        /// <summary>Attach (or find) a WobbleGaugeUI on the given hudPanel and build once. Idempotent.</summary>
        public static WobbleGaugeUI Ensure(GameObject hudPanel)
        {
            if (hudPanel == null) return null;
            var w = hudPanel.GetComponent<WobbleGaugeUI>();
            if (w == null) w = hudPanel.AddComponent<WobbleGaugeUI>();
            w.Build(hudPanel.GetComponent<RectTransform>());
            return w;
        }

        void Build(RectTransform parent)
        {
            if (_ring != null || parent == null) return;
            _ring = RingGauge.Create(parent, "WobbleGauge", new Vector2(1f, 0.5f), new Vector2(-130f, 0f), 150f);
            if (_ring != null)
            {
                _ring.SetValue("0%", "HOLD");
                _ring.gameObject.SetActive(false);
            }
        }

        public void Show(bool show) { if (_ring != null) _ring.gameObject.SetActive(show); }

        public void SetHold(float hold01)
        {
            if (_ring == null) return;
            _ring.SetFill(hold01);
            _ring.SetValue(Mathf.RoundToInt(hold01 * 100f) + "%", "HOLD");
        }

        public void SetWobble(float wobble01)
        {
            if (_ring == null) return;
            Color c = Color.Lerp(new Color(0.35f, 0.9f, 0.4f), new Color(0.95f, 0.25f, 0.25f), Mathf.Clamp01(wobble01));
            _ring.SetColor(c);
        }
    }
}
