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

        // Wired by TheDasherDirector from its own Inspector fields (same pattern as thaiFont) —
        // this component is added at runtime via AddComponent, so it has no scene presence of its
        // own to assign sprites to directly.
        [Header("Chair-prep diagram + button art (set by TheDasherDirector)")]
        [Tooltip("Rounded camera-frame border for the chair diagram. Reuses TheDasher/UI/feed_frame.png.")]
        public Sprite frameSprite;
        [Tooltip("Round dot used for the diagram's head/lens. Reuses TheDasher/UI/dot_on.png.")]
        public Sprite headSprite;
        [Tooltip("เริ่มสอนเล่น button background (idle). Reuses TheDasher/UI/btn_primary.png.")]
        public Sprite buttonSprite;
        [Tooltip("เริ่มสอนเล่น button background (pressed). Reuses TheDasher/UI/btn_primary_down.png.")]
        public Sprite buttonPressedSprite;

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

                // Chair-prep diagram, drawn from plain UI shapes instead of a baked photo (see
                // BuildChairDiagram). The old baked banner showed the player standing face-on next
                // to the chair — the WRONG angle — and was a wide/squashed landscape image. This is
                // a portrait card (taller than wide) showing the correct side-on/¾ seated pose.
                const float diagramW = 560f, diagramH = 760f;
                const float diagramY = 300f;
                BuildChairDiagram(_intro.transform, diagramW, diagramH, diagramY);

                float diagramBottom = diagramY - diagramH * 0.5f;

                var headline = NewTmp("IntroHeadline", _intro.transform, 44, FontStyles.Bold, Color.white);
                headline.text = "วางเก้าอี้ให้เอียงข้างเข้าหากล้อง";
                var hRt = headline.rectTransform;
                hRt.anchorMin = hRt.anchorMax = new Vector2(0.5f, 0.5f);
                hRt.pivot = new Vector2(0.5f, 0.5f);
                hRt.sizeDelta = new Vector2(920f, 60f);
                float headlineY = diagramBottom - 24f - 30f;
                hRt.anchoredPosition = new Vector2(0f, headlineY);
                headline.raycastTarget = false;

                var subtext = NewTmp("IntroSubtext", _intro.transform, 32, FontStyles.Normal,
                    new Color(0.86f, 0.88f, 0.96f));
                subtext.text = "นั่งให้เห็นทั้งตัวตั้งแต่หัวถึงเท้าในกล้อง ห่างจากแท็บเล็ตประมาณ 2 เมตร";
                var subRt = subtext.rectTransform;
                subRt.anchorMin = subRt.anchorMax = new Vector2(0.5f, 0.5f);
                subRt.pivot = new Vector2(0.5f, 0.5f);
                subRt.sizeDelta = new Vector2(880f, 96f);
                float subtextY = headlineY - 30f - 12f - 48f;
                subRt.anchoredPosition = new Vector2(0f, subtextY);
                subtext.raycastTarget = false;

                // เริ่มสอนเล่น start button — a proper sliced button sprite instead of a flat
                // colour rect, with a pressed-state swap when the art is wired.
                float buttonY = subtextY - 48f - 30f - 60f;
                var btn = new GameObject("IntroStart");
                btn.transform.SetParent(_intro.transform, false);
                var btnImg = btn.AddComponent<Image>();
                btnImg.sprite = buttonSprite;
                btnImg.type = Image.Type.Sliced;
                btnImg.color = buttonSprite != null ? Color.white : new Color(0.09f, 0.72f, 0.53f, 1f);
                var bg = btnImg.rectTransform;
                bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0.5f);
                bg.pivot = new Vector2(0.5f, 0.5f);
                bg.sizeDelta = new Vector2(520f, 120f);
                bg.anchoredPosition = new Vector2(0f, buttonY);
                var b = btn.AddComponent<Button>();
                b.targetGraphic = btnImg;
                if (buttonPressedSprite != null)
                {
                    b.transition = Selectable.Transition.SpriteSwap;
                    var ss = b.spriteState;
                    ss.pressedSprite = buttonPressedSprite;
                    b.spriteState = ss;
                }
                else
                {
                    b.transition = Selectable.Transition.None;
                }
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

        /// <summary>
        /// Draws the "how to sit for the camera" diagram from plain UI shapes: a seated silhouette
        /// viewed side-on/¾ inside a camera-frame border, with a small tablet/camera icon showing
        /// where the device sits. This is the ACTUAL setup for The Dasher — chair angled toward the
        /// camera, whole body head-to-feet visible — unlike the old baked photo, which showed
        /// someone standing face-on next to the chair.
        /// </summary>
        void BuildChairDiagram(Transform parent, float w, float h, float centerY)
        {
            var box = new GameObject("ChairDiagram", typeof(RectTransform));
            box.transform.SetParent(parent, false);
            var boxRt = (RectTransform)box.transform;
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(w, h);
            boxRt.anchoredPosition = new Vector2(0f, centerY);

            // Dark card behind the frame border so the border reads clearly over the modal's own
            // dim backdrop.
            var bg = NewImage("Bg", box.transform, new Color(0.07f, 0.09f, 0.16f, 1f));
            Stretch(bg.rectTransform);

            // Camera-frame border — reuses the HUD's own feed_frame art (a rounded rect with a
            // green outline) instead of drawing fresh corner brackets.
            var frameImg = NewImage("Frame", box.transform, Color.white);
            frameImg.sprite = frameSprite;
            frameImg.type = Image.Type.Sliced;
            Stretch(frameImg.rectTransform);
            frameImg.raycastTarget = false;

            // ---- Chair, side profile: backrest toward the back of the frame, front leg forward.
            Color chairColor = new Color(0.42f, 0.5f, 0.86f);
            float seatY = -h * 0.06f;
            float chairLeftX = -w * 0.16f;  // backrest x
            float chairRightX = w * 0.06f;  // front-leg x

            NewRect(box.transform, "Backrest", chairColor, new Vector2(18f, h * 0.24f),
                new Vector2(chairLeftX, seatY + h * 0.12f));
            NewRect(box.transform, "Seat", chairColor, new Vector2(chairRightX - chairLeftX + 40f, 18f),
                new Vector2((chairLeftX + chairRightX) * 0.5f, seatY));
            NewRect(box.transform, "BackLeg", chairColor, new Vector2(14f, h * 0.16f),
                new Vector2(chairLeftX, seatY - h * 0.08f - 9f));
            NewRect(box.transform, "FrontLeg", chairColor, new Vector2(14f, h * 0.16f),
                new Vector2(chairRightX, seatY - h * 0.08f - 9f));

            // ---- Seated person, side-on: torso leaning back on the backrest, knee bent ~90°,
            // whole body (head to feet) inside the frame — exactly how the camera should see them.
            Color bodyColor = Color.white;
            float hipX = chairLeftX + 14f;
            float hipY = seatY + 9f;
            float thighLen = (chairRightX - chairLeftX) + 26f;
            float shinLen = h * 0.26f;

            NewRect(box.transform, "Torso", bodyColor, new Vector2(20f, h * 0.24f),
                new Vector2(hipX - 6f, hipY + h * 0.12f));

            var head = NewImage("Head", box.transform, Color.white);
            head.sprite = headSprite;
            var headRt = head.rectTransform;
            headRt.anchorMin = headRt.anchorMax = new Vector2(0.5f, 0.5f);
            headRt.pivot = new Vector2(0.5f, 0.5f);
            headRt.sizeDelta = new Vector2(46f, 46f);
            headRt.anchoredPosition = new Vector2(hipX - 6f, hipY + h * 0.24f + 30f);

            NewRect(box.transform, "Thigh", bodyColor, new Vector2(thighLen, 18f),
                new Vector2(hipX + thighLen * 0.5f, hipY));
            NewRect(box.transform, "Shin", bodyColor, new Vector2(18f, shinLen),
                new Vector2(hipX + thighLen, hipY - shinLen * 0.5f));
            NewRect(box.transform, "Foot", bodyColor, new Vector2(34f, 12f),
                new Vector2(hipX + thighLen + 8f, hipY - shinLen - 6f));

            // Small device icon inside the bottom edge of the frame — the tablet/camera sits here;
            // the chair should be a couple of metres out, fully inside the frame above. Kept INSIDE
            // the frame bounds (rather than hanging below it) so it can never collide with the
            // caption text sitting underneath the whole diagram.
            var tablet = NewRect(box.transform, "Tablet", new Color(0.15f, 0.16f, 0.22f),
                new Vector2(46f, 64f), new Vector2(0f, -h * 0.5f + 42f));
            var lens = NewImage("Lens", tablet.transform, new Color(0.6f, 0.85f, 1f));
            lens.sprite = headSprite; // reuse the round sprite as a simple camera-dot
            var lensRt = lens.rectTransform;
            lensRt.anchorMin = lensRt.anchorMax = new Vector2(0.5f, 1f);
            lensRt.pivot = new Vector2(0.5f, 1f);
            lensRt.sizeDelta = new Vector2(14f, 14f);
            lensRt.anchoredPosition = new Vector2(0f, -10f);
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

        /// <summary>A plain colour rectangle at a given size/position — the building block for the
        /// chair diagram's chair and body parts.</summary>
        static Image NewRect(Transform parent, string name, Color color, Vector2 size, Vector2 pos)
        {
            var img = NewImage(name, parent, color);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
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
