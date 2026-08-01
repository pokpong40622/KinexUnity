using UnityEngine;
using TMPro;

namespace Kinex.UI
{
    /// <summary>
    /// Owns the two corner ring gauges for the live gameplay HUD: a mocked heart-rate
    /// ring (bottom-left) and the live pose-match ring (bottom-right). Shared by both
    /// MEGA DANCE and Kinex World. Built entirely at runtime as children of the given
    /// hudPanel — no scene/prefab edits, no new Inspector wiring required.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        static readonly Color HrColor = new Color(0.93f, 0.26f, 0.34f);
        const float HrMin = 60f;  // Bpm normalisation range for the ring fill
        const float HrMax = 160f;

        RingGauge _hr;
        RingGauge _match;
        TMP_Text _matchSubLabel;   // "3/10" style label under the match ring
        Kinex.HeartRateMock _hrMock;
        string _matchBigText = "0%";
        bool _built;

        /// <summary>Attach (or find) a GameHud on the given hudPanel and build the rings once. Idempotent.</summary>
        public static GameHud Ensure(GameObject hudPanel, bool hideLegacyBar = true)
        {
            if (hudPanel == null) return null;
            var hud = hudPanel.GetComponent<GameHud>();
            if (hud == null) hud = hudPanel.AddComponent<GameHud>();
            hud.Build(hudPanel.GetComponent<RectTransform>());
            return hud;
        }

        void Build(RectTransform parent)
        {
            if (_built || parent == null) return;
            _built = true;

            _hrMock = new Kinex.HeartRateMock();

            _hr = RingGauge.Create(parent, "HeartRateRing", new Vector2(0f, 0f), new Vector2(110f, 110f), 190f);
            if (_hr != null)
            {
                _hr.SetColor(HrColor);
                _hr.SetValue(_hrMock.Bpm.ToString(), "BPM");
            }

            _match = RingGauge.Create(parent, "MatchRing", new Vector2(1f, 0f), new Vector2(-110f, 110f), 190f);
            if (_match != null)
            {
                _match.SetValue(_matchBigText, "MATCH");
                BuildMatchSubLabel(_match.GetComponent<RectTransform>());
            }
        }

        // Small label ("3/10") anchored just below the match ring itself — separate from the
        // ring's own centre number + "MATCH" caption.
        void BuildMatchSubLabel(RectTransform ringRt)
        {
            if (ringRt == null) return;

            var go = new GameObject("SubLabel", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(ringRt, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -6f);
            rt.sizeDelta = new Vector2(0f, 26f);

            _matchSubLabel = go.AddComponent<TextMeshProUGUI>();
            _matchSubLabel.alignment = TextAlignmentOptions.Center;
            _matchSubLabel.fontStyle = FontStyles.Bold;
            _matchSubLabel.enableAutoSizing = true;
            _matchSubLabel.fontSizeMin = 4f;
            _matchSubLabel.fontSizeMax = 22f;
            _matchSubLabel.color = new Color(1f, 1f, 1f, 0.85f);
            _matchSubLabel.raycastTarget = false;
            _matchSubLabel.text = "";
        }

        void Update()
        {
            if (_hr == null || _hrMock == null) return;
            bool beat = _hrMock.Tick(Time.deltaTime);
            _hr.SetFill(Mathf.InverseLerp(HrMin, HrMax, _hrMock.Bpm));
            _hr.SetValue(_hrMock.Bpm.ToString(), "BPM");
            if (beat) _hr.Pulse();
        }

        /// <summary>Push the live 0..1 match score: fills + colours the right-hand ring (via ScoreHud.BandColor).</summary>
        public void SetScore(float score01)
        {
            if (_match == null) return;
            score01 = Mathf.Clamp01(score01);
            _matchBigText = Mathf.RoundToInt(score01 * 100f) + "%";
            _match.SetFill(score01);
            _match.SetValue(_matchBigText, "MATCH");
            _match.SetColor(Kinex.ScoreHud.BandColor(score01));
        }

        /// <summary>Small label under the match ring, e.g. "3/10".</summary>
        public void SetSubLabel(string s)
        {
            if (_matchSubLabel != null) _matchSubLabel.text = s ?? "";
        }

        /// <summary>
        /// Hides legacy HUD widgets (old flat bar / percent strip), null-safe. Call once after
        /// Ensure() with each director's own bar/percent GameObjects — kept as an explicit call
        /// (rather than baked into Ensure) since GameHud has no scene-path knowledge of those refs.
        /// </summary>
        public void HideLegacy(params GameObject[] gos)
        {
            if (gos == null) return;
            foreach (var go in gos)
                if (go != null) go.SetActive(false);
        }
    }
}
