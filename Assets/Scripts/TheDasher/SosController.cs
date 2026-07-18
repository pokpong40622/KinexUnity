using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.Motion;

namespace Kinex.TheDasher
{
    /// <summary>
    /// Emergency fall-alert overlay for The Dasher. On a detected fall (or the score-tap TEST
    /// hook) it pops a red SOS popup built at runtime from the baked sprite Resources/SOS/sos_card
    /// (all Thai text is baked into that image; only the countdown number + draining bar are live,
    /// so no in-Unity Thai font is needed). A [countdownSeconds] countdown runs; tapping the card
    /// ("ฉันปลอดภัย — ยกเลิก") cancels it. If it reaches 0 uncancelled, Flutter is told to auto-dial
    /// the emergency contact via SendToFlutter {"type":"sos_call"} — the contact + the actual call
    /// live on the Flutter side.
    ///
    /// Fall detection is deliberately CONSERVATIVE (torso must go clearly horizontal, with the
    /// shoulders+hips visible, sustained for [fallHoldSeconds]) because a false positive that isn't
    /// cancelled within the countdown places a real phone call. The card-tap cancel is the safety net.
    /// </summary>
    public class SosController : MonoBehaviour
    {
        [Tooltip("Seconds the user has to cancel before the emergency contact is auto-dialled.")]
        public float countdownSeconds = 10f;

        [Tooltip("Run automatic fall detection from the pose. OFF = only the score-tap TEST trigger " +
                 "fires the popup (safe while verifying the call pipeline).")]
        public bool autoFallDetect = true;

        [Tooltip("Pose detector to read the body pose from (set by the director).")]
        public MediaPipePoseDetector poseDetector;

        [Header("Fall heuristic")]
        [Tooltip("Torso counts as 'fallen' when it is this many times more HORIZONTAL than vertical " +
                 "(1.2 ≈ tilted past ~50° from upright). Higher = must be more flat = fewer false alarms.")]
        [Range(1f, 3f)] public float fallTiltFactor = 1.3f;
        [Tooltip("The horizontal torso pose must hold this long before the alert fires.")]
        public float fallHoldSeconds = 0.9f;
        [Tooltip("Min confidence required on both shoulders and both hips to judge a fall at all.")]
        [Range(0f, 1f)] public float fallMinConfidence = 0.3f;

        // COCO-17 indices of the 2D keypoints (match MediaPipePoseDetector's private consts).
        const int L_SHOULDER = 5, R_SHOULDER = 6, L_HIP = 11, R_HIP = 12;

        bool _shown;
        float _fallTimer;
        Coroutine _countdown;

        // Built UI (lazy).
        Canvas _canvas;
        GameObject _root;
        TMP_Text _numberText;
        RectTransform _barFill;
        float _barWidth;

        void Update()
        {
            if (_shown || !autoFallDetect || poseDetector == null || !poseDetector.HasPose)
            {
                if (!_shown) _fallTimer = 0f;
                return;
            }

            if (IsTorsoHorizontal())
            {
                _fallTimer += Time.unscaledDeltaTime;
                if (_fallTimer >= fallHoldSeconds) Show();
            }
            else
            {
                _fallTimer = 0f;
            }
        }

        bool IsTorsoHorizontal()
        {
            var kp = poseDetector.LatestKeypoints;
            var c = poseDetector.LatestConfidence;
            if (kp == null || c == null || kp.Length <= R_HIP) return false;
            if (c[L_SHOULDER] < fallMinConfidence || c[R_SHOULDER] < fallMinConfidence ||
                c[L_HIP] < fallMinConfidence || c[R_HIP] < fallMinConfidence) return false;

            float shX = (kp[L_SHOULDER].x + kp[R_SHOULDER].x) * 0.5f;
            float shY = (kp[L_SHOULDER].y + kp[R_SHOULDER].y) * 0.5f;
            float hipX = (kp[L_HIP].x + kp[R_HIP].x) * 0.5f;
            float hipY = (kp[L_HIP].y + kp[R_HIP].y) * 0.5f;
            float vert = Mathf.Abs(hipY - shY);   // large when standing (torso upright)
            float horiz = Mathf.Abs(hipX - shX);  // large when lying   (torso flat)
            return horiz > vert * fallTiltFactor;
        }

        /// <summary>Show the SOS alert and start the countdown. Called by fall-detect or the test tap.</summary>
        public void Show()
        {
            if (_shown) return;
            _shown = true;
            _fallTimer = 0f;
            if (_root == null) BuildUi();
            _root.SetActive(true);
            Time.timeScale = 0f; // freeze the game while the emergency is on screen
            _countdown = StartCoroutine(CountdownRoutine());
        }

        void Cancel()
        {
            if (!_shown) return;
            Hide();
        }

        void Hide()
        {
            _shown = false;
            if (_countdown != null) { StopCoroutine(_countdown); _countdown = null; }
            if (_root != null) _root.SetActive(false);
            Time.timeScale = 1f;
        }

        IEnumerator CountdownRoutine()
        {
            float remaining = countdownSeconds;
            while (remaining > 0f)
            {
                if (_numberText != null) _numberText.text = Mathf.CeilToInt(remaining).ToString();
                if (_barFill != null)
                    _barFill.sizeDelta = new Vector2(_barWidth * (remaining / countdownSeconds), _barFill.sizeDelta.y);
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (_numberText != null) _numberText.text = "0";
            _countdown = null;
            Expire();
        }

        void Expire()
        {
            // Hand off to Flutter to auto-dial the emergency contact, then close.
            SendToFlutter.Send("{\"type\":\"sos_call\"}");
            Hide();
        }

        // ── UI (built once from the baked sprite; number + bar are the only live elements) ──────
        void BuildUi()
        {
            var red = new Color32(0xD6, 0x28, 0x28, 0xFF);
            var redDeep = new Color32(0xA1, 0x1D, 0x1D, 0xFF);

            _root = new GameObject("SosOverlay");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000; // above all gameplay UI
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Full-screen dim scrim (blocks input to the game behind; not a cancel target).
            var scrim = NewImage("Scrim", _root.transform, new Color(0f, 0f, 0f, 0.6f));
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;

            // Card sprite (630x870). Tapping the card = cancel (the card's button says so).
            var tex = Resources.Load<Texture2D>("SOS/sos_card");
            float cardW = 780f, cardH = cardW * 870f / 630f; // preserve the baked aspect
            var card = NewImage("Card", _root.transform, Color.white);
            if (tex != null)
                card.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            else
                card.color = red; // fallback so the popup still works if the sprite is missing
            var cardRt = card.rectTransform;
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(cardW, cardH);
            var cardBtn = card.gameObject.AddComponent<Button>();
            cardBtn.transition = Selectable.Transition.None;
            cardBtn.onClick.AddListener(Cancel);

            // Live countdown number — baked position: centre x, 45.9% down the card.
            _numberText = NewText("Number", cardRt, 150, Color.white);
            var nRt = _numberText.rectTransform;
            nRt.anchorMin = nRt.anchorMax = new Vector2(0.5f, 0.5f);
            nRt.pivot = new Vector2(0.5f, 0.5f);
            nRt.sizeDelta = new Vector2(cardW * 0.5f, cardH * 0.16f);
            nRt.anchoredPosition = new Vector2(0f, (0.5f - 0.459f) * cardH);
            _numberText.raycastTarget = false;

            // Live draining bar — baked position: centre x, 60.8% down; width 87.6% of the card.
            _barWidth = cardW * 0.876f;
            float barH = 14f;
            var track = NewImage("BarTrack", cardRt, redDeep);
            var tRt = track.rectTransform;
            tRt.anchorMin = tRt.anchorMax = new Vector2(0.5f, 0.5f);
            tRt.pivot = new Vector2(0.5f, 0.5f);
            tRt.sizeDelta = new Vector2(_barWidth, barH);
            tRt.anchoredPosition = new Vector2(0f, (0.5f - 0.608f) * cardH);
            track.raycastTarget = false;

            var fill = NewImage("BarFill", tRt, Color.white);
            _barFill = fill.rectTransform;
            _barFill.anchorMin = new Vector2(0f, 0.5f);
            _barFill.anchorMax = new Vector2(0f, 0.5f);
            _barFill.pivot = new Vector2(0f, 0.5f);
            _barFill.sizeDelta = new Vector2(_barWidth, barH);
            _barFill.anchoredPosition = Vector2.zero;
            fill.raycastTarget = false;

            _root.SetActive(false);
        }

        static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static TMP_Text NewText(string name, Transform parent, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.text = "";
            return t;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
