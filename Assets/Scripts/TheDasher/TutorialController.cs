using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Kinex.TheDasher
{
    /// <summary>
    /// In-scene "สอนเล่น" tutorial UI. Shows one professionally-designed BAKED instruction
    /// card (Resources/Tutorial/step1..3 — a wide banner with the illustration + Thai text baked
    /// in) pinned to the TOP, compact enough that the real meteor / treasure / kick-ring the
    /// director spawns stay visible falling below it. Plus a small ข้าม (skip) button and a green
    /// "สำเร็จ!" flash. All step LOGIC + pose gating lives in TheDasherDirector.TickTutorial — this
    /// class is display only, driven via SetStep / ShowSuccess / Hide.
    /// </summary>
    public class TutorialController : MonoBehaviour
    {
        public System.Action OnSkip;   // director ends the tutorial early
        public TMP_FontAsset thaiFont; // scene's Thai TMP font (skip + success labels)

        // On-screen banner width; the HEIGHT is derived from whatever the baked PNG's real aspect
        // is (see FitCardToSprite) rather than a hardcoded ratio. A constant here silently goes
        // stale the moment the cards are re-baked at a different size, which is exactly how the
        // banner ended up mismatched with its art.
        const float CardWidth = 1052f;

        Canvas _canvas;
        GameObject _root;
        Image _card;
        GameObject _success;
        GameObject _retry;
        GameObject _intro;
        System.Action _onIntroStart;
        float _retryTimer;
        readonly Sprite[] _stepSprites = new Sprite[3];
        RectTransform _skipRect;

        void BuildUi()
        {
            _root = new GameObject("TutorialOverlay");
            _root.transform.SetParent(transform, false);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 4000; // above gameplay HUD, below the SOS overlay (5000)
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Big instruction banner, pinned to the top edge (baked aspect below). Enlarged so the
            // Thai instruction text reads easily for seniors across the room.
            _card = NewImage("Card", _root.transform, Color.white);
            _card.preserveAspect = true;
            var rt = _card.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(CardWidth, CardWidth * 0.43f); // provisional; FitCardToSprite corrects it
            rt.anchoredPosition = new Vector2(0f, -26f);

            // ข้าม skip — top-right, just under the banner.
            var skip = new GameObject("Skip");
            skip.transform.SetParent(_root.transform, false);
            var skipBg = skip.AddComponent<Image>();
            skipBg.color = new Color(0f, 0f, 0f, 0.38f);
            var srt = skipBg.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 1f);
            srt.sizeDelta = new Vector2(150f, 74f);
            _skipRect = srt;   // repositioned under the card by FitCardToSprite
            var skipBtn = skip.AddComponent<Button>();
            skipBtn.transition = Selectable.Transition.None;
            skipBtn.onClick.AddListener(() => OnSkip?.Invoke());
            var skipTxt = NewTmp("SkipLabel", skip.transform, 30, FontStyles.Bold, Color.white);
            skipTxt.text = "ข้าม ›";
            Stretch(skipTxt.rectTransform);
            skipTxt.raycastTarget = false;

            // ✓ "สำเร็จ!" flash — centred, hidden by default.
            _success = new GameObject("Success");
            _success.transform.SetParent(_root.transform, false);
            var sImg = _success.AddComponent<Image>();
            sImg.color = new Color(0.09f, 0.72f, 0.53f, 0.96f);
            var g = sImg.rectTransform;
            g.anchorMin = g.anchorMax = new Vector2(0.5f, 0.5f);
            g.pivot = new Vector2(0.5f, 0.5f);
            g.sizeDelta = new Vector2(360f, 130f);
            g.anchoredPosition = new Vector2(0f, 60f);
            var sTxt = NewTmp("SuccessLabel", g.transform, 54, FontStyles.Bold, Color.white);
            sTxt.text = "สำเร็จ!";
            Stretch(sTxt.rectTransform);
            sTxt.raycastTarget = false;
            _success.SetActive(false);

            // ✗ "ลองอีกครั้ง!" retry flash — red, centred, hidden by default. Shown on a miss so the
            // player clearly understands they didn't complete the move in time and can try again.
            _retry = new GameObject("Retry");
            _retry.transform.SetParent(_root.transform, false);
            var rImg = _retry.AddComponent<Image>();
            rImg.color = new Color(0.90f, 0.30f, 0.24f, 0.96f);
            var rg = rImg.rectTransform;
            rg.anchorMin = rg.anchorMax = new Vector2(0.5f, 0.5f);
            rg.pivot = new Vector2(0.5f, 0.5f);
            rg.sizeDelta = new Vector2(520f, 140f);
            rg.anchoredPosition = new Vector2(0f, 60f);
            var rTxt = NewTmp("RetryLabel", rg.transform, 52, FontStyles.Bold, Color.white);
            rTxt.text = "ลองอีกครั้ง!";
            Stretch(rTxt.rectTransform);
            rTxt.raycastTarget = false;
            _retry.SetActive(false);
        }

        void Update()
        {
            if (_retryTimer > 0f)
            {
                _retryTimer -= Time.deltaTime;
                if (_retryTimer <= 0f && _retry != null) _retry.SetActive(false);
            }
        }

        /// <summary>Show the card for step index i (0..2). Hides any success flash.</summary>
        public void SetStep(int i)
        {
            if (_root == null) BuildUi();
            _root.SetActive(true);
            if (_success != null) _success.SetActive(false);
            if (_retry != null) _retry.SetActive(false);
            if (_intro != null) _intro.SetActive(false);
            _card.enabled = true; // re-show the banner if the intro modal had hidden it
            _card.sprite = StepSprite(i);
            FitCardToSprite();
        }

        /// <summary>
        /// Sizes the banner to the baked PNG's own aspect and drops the ข้าม button just below it.
        /// Driven by the sprite so re-baking the cards taller needs no code change.
        /// </summary>
        void FitCardToSprite()
        {
            if (_card == null || _card.sprite == null) return;
            var tex = _card.sprite.rect;
            if (tex.width <= 0f) return;

            float cardH = CardWidth * tex.height / tex.width;
            _card.rectTransform.sizeDelta = new Vector2(CardWidth, cardH);
            if (_skipRect != null)
            {
                _skipRect.anchoredPosition = new Vector2(-28f, -(cardH + 44f));
            }
        }

        /// <summary>
        /// One-time "place a chair" get-ready modal shown before step 1. Full-screen dim + a centred
        /// panel with big Thai instructions and a start button. [onStart] fires when the user taps เริ่ม.
        /// </summary>
        public void ShowIntro(System.Action onStart)
        {
            if (_root == null) BuildUi();
            _root.SetActive(true);
            if (_card != null) _card.enabled = false; // hide the step banner behind the modal

            if (_intro == null)
            {
                // Dim full-screen backdrop so it reads as a modal (also swallows stray taps).
                _intro = new GameObject("Intro");
                _intro.transform.SetParent(_root.transform, false);
                var dim = _intro.AddComponent<Image>();
                dim.color = new Color(0f, 0f, 0f, 0.72f);
                Stretch(dim.rectTransform);

                // Baked intro picture (same web-baked style as the step1..3 cards):
                // Resources/Tutorial/intro.png — camera frame + chair centred + Thai text.
                float cardW = 940f, cardH = cardW * 0.7778f;
                var pic = NewImage("IntroPic", _intro.transform, Color.white);
                pic.preserveAspect = true;
                var tex = Resources.Load<Texture2D>("Tutorial/intro");
                if (tex != null)
                {
                    pic.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    cardH = cardW * tex.height / tex.width;
                }
                var prt = pic.rectTransform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(cardW, cardH);
                prt.anchoredPosition = new Vector2(0f, 80f);

                // เริ่มสอนเล่น start button, just below the card.
                var btn = new GameObject("IntroStart");
                btn.transform.SetParent(_intro.transform, false);
                var btnBg = btn.AddComponent<Image>();
                btnBg.color = new Color(0.09f, 0.72f, 0.53f, 1f);
                var bg = btnBg.rectTransform;
                bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0.5f);
                bg.pivot = new Vector2(0.5f, 0.5f);
                bg.sizeDelta = new Vector2(520f, 120f);
                bg.anchoredPosition = new Vector2(0f, 80f - cardH * 0.5f - 82f);
                var b = btn.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() =>
                {
                    if (_intro != null) _intro.SetActive(false);
                    if (_card != null) _card.enabled = true;
                    _onIntroStart?.Invoke();
                });
                var btnTxt = NewTmp("IntroStartLabel", btn.transform, 44, FontStyles.Bold, Color.white);
                btnTxt.text = "เริ่มสอนเล่น";
                Stretch(btnTxt.rectTransform);
                btnTxt.raycastTarget = false;
            }

            _onIntroStart = onStart;
            _intro.SetActive(true);
        }

        public void ShowSuccess()
        {
            if (_success != null) _success.SetActive(true);
            if (_retry != null) _retry.SetActive(false);
        }

        /// <summary>Flash a red "ลองอีกครั้ง!" for ~1.4s when the player misses the step's cue.</summary>
        public void ShowRetry()
        {
            if (_root == null) BuildUi();
            if (_retry != null) _retry.SetActive(true);
            if (_success != null) _success.SetActive(false);
            _retryTimer = 1.4f;
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        Sprite StepSprite(int i)
        {
            i = Mathf.Clamp(i, 0, 2);
            if (_stepSprites[i] == null)
            {
                var tex = Resources.Load<Texture2D>("Tutorial/step" + (i + 1));
                if (tex != null)
                    _stepSprites[i] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }
            return _stepSprites[i];
        }

        static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        TMP_Text NewTmp(string name, Transform parent, float size, FontStyles style, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (thaiFont != null) t.font = thaiFont;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.enableAutoSizing = false;
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
