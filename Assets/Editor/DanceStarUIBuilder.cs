#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.DanceStar;

namespace Kinex.EditorTools
{
    /// <summary>
    /// Builder for the CLINICAL 2D SUPERSTAR STAGE UI (bright/friendly theme — soft blue, white
    /// rounded cards, colorful skeleton). Layout: a large live CAMERA FEED with the detected
    /// skeleton drawn on the user is the hero; a static reference figure ("ทำท่านี้") sits top-left;
    /// the pose name + card counter + a green "match" meter sit top-right; a live feedback line
    /// coaches over the feed; a framing overlay pauses the card when the body leaves frame. No
    /// hearts / streak / score during play — stars appear only on the results screen.
    ///
    /// The camera feed lives in its OWN always-active panel (not inside HudPanel) because
    /// MediaPipePoseDetector.Start() binds to the RawImage via FindAnyObjectByType, which skips
    /// inactive objects. Intro/Calib/Chair/Results panels are opaque and cover the feed; HudPanel is
    /// transparent and overlays it. Re-runnable: destroys its own panels by name and rebuilds, then
    /// wires every DanceStarDirector field it owns.
    /// </summary>
    public static class DanceStarUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        // ---- Bright / clinical palette ----
        static readonly Color BgBlue     = new Color32(0xDD, 0xEB, 0xFB, 0xFF); // soft page blue (opaque)
        static readonly Color CardWhite  = new Color32(0xFF, 0xFF, 0xFF, 0xF7);
        static readonly Color CardWarm   = new Color32(0xFF, 0xF4, 0xE2, 0xFF);
        static readonly Color Ink        = new Color32(0x22, 0x31, 0x4A, 0xFF);
        static readonly Color InkSoft    = new Color32(0x5A, 0x6B, 0x85, 0xFF);
        static readonly Color Blue       = new Color32(0x2E, 0x86, 0xDE, 0xFF);
        static readonly Color BlueDeep   = new Color32(0x1B, 0x5F, 0xA8, 0xFF);
        static readonly Color Green      = new Color32(0x34, 0xC7, 0x59, 0xFF);
        static readonly Color Gold       = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        static readonly Color Coral      = new Color32(0xFF, 0x6B, 0x6B, 0xFF);
        static readonly Color FeedFrame  = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Color FeedbackBg = new Color32(0x14, 0x20, 0x36, 0xB8); // dark translucent band over video
        static readonly Color TrackDim   = new Color32(0x2E, 0x86, 0xDE, 0x30);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";

        static readonly string[] PanelNames =
        {
            "BackgroundPanel", "CameraFeedPanel", "HudPanel",
            "CalibPanel", "ChairSafetyPanel", "IntroPanel", "ResultsPanel",
            "FramingPanel", "ExitButton",
        };

        [MenuItem("Kinex/Build Dance Star UI")]
        public static void Build() => Debug.Log("[DanceStarUIBuilder] " + BuildUI());

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<DanceStarDirector>();
            if (director == null) return "No DanceStarDirector in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";
            var detector = Object.FindAnyObjectByType<MediaPipePoseDetector>();

            var thaiBlack = LoadFont(ThaiBlackPath);
            var thaiSemi = LoadFont(ThaiSemiPath);
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i);
                foreach (var n in PanelNames)
                    if (child.name == n) { Object.DestroyImmediate(child.gameObject); break; }
            }

            // =================== BACKGROUND (bottom) ===================
            var bg = NewPanel(canvas.transform, "BackgroundPanel");
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = BgBlue;
            bgImg.raycastTarget = false;

            // =================== CAMERA FEED (hero, always active) ===================
            // White rounded frame + inset RawImage + skeleton overlay. Kept in its own panel so the
            // detector's FindAnyObjectByType<RawImage> binds even while HudPanel is inactive.
            var feed = NewPanel(canvas.transform, "CameraFeedPanel");
            feed.GetComponent<Image>().enabled = false;
            var frame = AddImage(feed.transform, "FeedFrame", null, FeedFrame);
            Round(frame);
            Place(frame.rectTransform, 40, 430, 847, 842);
            var frameShadow = frame.gameObject.AddComponent<Shadow>();
            frameShadow.effectColor = new Color(0.11f, 0.19f, 0.33f, 0.28f);
            frameShadow.effectDistance = new Vector2(0, 6);

            var rawGo = new GameObject("CameraFeedImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rawRt = (RectTransform)rawGo.transform;
            rawRt.SetParent(frame.rectTransform, false);
            rawRt.anchorMin = Vector2.zero; rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(10f, 10f); rawRt.offsetMax = new Vector2(-10f, -10f);
            var raw = rawGo.GetComponent<RawImage>();
            raw.color = new Color(0.05f, 0.07f, 0.11f); // dark placeholder before the webcam opens

            var overlayGo = new GameObject("PoseSkeletonOverlay", typeof(RectTransform), typeof(CanvasRenderer));
            var overlayRt = (RectTransform)overlayGo.transform;
            overlayRt.SetParent(rawRt, false);
            overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero; overlayRt.offsetMax = Vector2.zero;
            var overlay = overlayGo.AddComponent<PoseSkeletonOverlay>();
            overlay.source = detector;
            overlay.confidenceColors = true;

            // =================== HUD (transparent overlay on the feed) ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Reference figure card, top-left (portrait slot so the figure reads large). --
            AddCard(hud.transform, 40, 28, 300, 384);
            var refLabel = AddText(hud.transform, "ทำท่านี้", thaiSemi, 36, Blue, TextAlignmentOptions.Center);
            Place(refLabel.rectTransform, 56, 40, 268, 44);
            var referenceImage = AddImage(hud.transform, "ReferenceImage", null, Color.white);
            referenceImage.preserveAspect = true;
            referenceImage.enabled = false; // sprite assigned per-card at runtime
            Place(referenceImage.rectTransform, 90, 96, 200, 306);

            // -- Pose name + counter + match meter, top-right column (x372..782). --
            var poseNameText = AddText(hud.transform, "", thaiBlack, 60, Ink, TextAlignmentOptions.Center);
            Place(poseNameText.rectTransform, 366, 40, 416, 116);
            var cardCountText = AddText(hud.transform, "", thaiSemi, 34, InkSoft, TextAlignmentOptions.Center);
            Place(cardCountText.rectTransform, 366, 158, 416, 46);

            var matchLabel = AddText(hud.transform, "ตรงท่า", thaiSemi, 30, InkSoft, TextAlignmentOptions.Left);
            Place(matchLabel.rectTransform, 372, 214, 120, 40);
            var (matchTrack, matchMeterFill) = AddProgressBar(hud.transform, 500, 218, 282, 30, Green);
            matchTrack.color = new Color(0.20f, 0.78f, 0.35f, 0.18f);

            var sectionNameText = AddText(hud.transform, "", thaiSemi, 30, InkSoft, TextAlignmentOptions.Center);
            Place(sectionNameText.rectTransform, 366, 296, 416, 40);

            // -- Countdown ring floating over the feed's top-right corner. --
            var (ringTrack, countdownRingFill) = AddRadialRing(hud.transform, 762, 450, 104, Blue);
            ringTrack.color = TrackDim;

            // -- Live feedback band over the feed's lower area. --
            var fbBand = AddImage(hud.transform, "FeedbackBand", null, FeedbackBg);
            Round(fbBand);
            Place(fbBand.rectTransform, 96, 1150, 735, 92);
            var feedbackText = AddText(hud.transform, "", thaiBlack, 48, Color.white, TextAlignmentOptions.Center);
            Place(feedbackText.rectTransform, 112, 1150, 703, 92);

            // -- Song progress, bottom. --
            var (songTrack, songFill) = AddProgressBar(hud.transform, 40, 1300, 847, 16, Blue);
            songTrack.color = TrackDim;

            // -- Rating popup, big + centred over the feed. --
            var ratingPopupText = AddText(hud.transform, "", thaiBlack, 104, Gold, TextAlignmentOptions.Center);
            Place(ratingPopupText.rectTransform, 64, 560, 800, 170);
            Outline(ratingPopupText, new Color(0.11f, 0.19f, 0.33f, 0.85f), 0.3f);
            ratingPopupText.gameObject.SetActive(false);

            // =================== CALIBRATION (opaque) ===================
            var calib = NewPanel(canvas.transform, "CalibPanel");
            calib.GetComponent<Image>().color = BgBlue;
            AddCard(calib.transform, 64, 540, 800, 320);
            var calibText = AddText(calib.transform, "", thaiSemi, 48, Ink, TextAlignmentOptions.Center);
            Place(calibText.rectTransform, 104, 590, 720, 160);
            var (calibTrack, calibFill) = AddProgressBar(calib.transform, 124, 780, 680, 26, Blue);
            calibTrack.color = TrackDim;

            // =================== CHAIR SAFETY (opaque, warm) ===================
            var chairSafety = NewPanel(canvas.transform, "ChairSafetyPanel");
            chairSafety.GetComponent<Image>().color = BgBlue;
            AddCard(chairSafety.transform, 64, 500, 800, 400, CardWarm);
            var chairIcon = AddImage(chairSafety.transform, "ChairIcon", knob, new Color32(0xF0, 0x9A, 0x3C, 0xFF));
            Place(chairIcon.rectTransform, 413, 540, 100, 100);
            var chairSafetyText = AddText(chairSafety.transform, "", thaiSemi, 46,
                                          new Color(0.35f, 0.22f, 0.08f), TextAlignmentOptions.Center);
            Place(chairSafetyText.rectTransform, 114, 670, 700, 200);

            // =================== INTRO (opaque splash) ===================
            var intro = NewPanel(canvas.transform, "IntroPanel");
            intro.GetComponent<Image>().color = BgBlue;
            AddCard(intro.transform, 64, 300, 800, 640);
            var introTitle = AddText(intro.transform, "เวทีซุปตาร์", thaiBlack, 90, Ink, TextAlignmentOptions.Center);
            Place(introTitle.rectTransform, 94, 360, 740, 130);
            var superLabel = AddText(intro.transform, "SUPERSTAR STAGE", thaiSemi, 34, Blue, TextAlignmentOptions.Center);
            Place(superLabel.rectTransform, 94, 486, 740, 52);
            var introSubtitle = AddText(intro.transform, "", thaiSemi, 42, InkSoft, TextAlignmentOptions.Center);
            Place(introSubtitle.rectTransform, 124, 590, 680, 300);

            // =================== RESULTS (opaque) ===================
            var results = NewPanel(canvas.transform, "ResultsPanel");
            results.GetComponent<Image>().color = BgBlue;
            var resTitle = AddText(results.transform, "เยี่ยมมาก!", thaiBlack, 88, Ink, TextAlignmentOptions.Center);
            Place(resTitle.rectTransform, 94, 190, 740, 150);
            var starImages = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                starImages[i] = AddImage(results.transform, $"Star{i + 1}", knob, StarOff());
                Place(starImages[i].rectTransform, 288 + i * 140, 380, 110, 110);
            }
            var statsText = AddText(results.transform, "", thaiSemi, 46, Ink, TextAlignmentOptions.Center);
            Place(statsText.rectTransform, 114, 560, 700, 320);
            var againBtn = AddPillButton(results.transform, "PlayAgainButton", "เล่นอีกครั้ง", thaiSemi, Green, 160, 980, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(againBtn.onClick, director.PlayAgain);
            var homeBtn = AddPillButton(results.transform, "HomeButton", "กลับหน้าหลัก", thaiSemi, Blue, 480, 980, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(homeBtn.onClick, director.ExitToHome);

            // =================== FRAMING OVERLAY (dim, pauses the card) ===================
            var framing = NewPanel(canvas.transform, "FramingPanel");
            var framingImg = framing.GetComponent<Image>();
            framingImg.color = new Color(0.06f, 0.11f, 0.20f, 0.82f);
            framingImg.raycastTarget = true;
            AddCard(framing.transform, 113, 560, 700, 320);
            var framingIcon = AddImage(framing.transform, "FramingIcon", knob, Blue);
            Place(framingIcon.rectTransform, 423, 600, 80, 80);
            var framingPromptText = AddText(framing.transform, "ยืนให้กล้องเห็นเต็มตัว", thaiBlack, 48, Ink, TextAlignmentOptions.Center);
            Place(framingPromptText.rectTransform, 143, 700, 640, 160);

            // =================== EXIT (all states, topmost) ===================
            var exitBtn = AddPillButton(canvas.transform, "ExitButton", "ออก", thaiSemi, Coral, 787, 30, 100, 76);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitBtn.onClick, director.ExitToHome);
            exitBtn.transform.SetAsLastSibling();

            // ---- Wire the director. ----
            var so = new SerializedObject(director);

            AssignGo(so, "introPanel", intro);
            Assign(so, "introTitleText", introTitle);
            Assign(so, "introSubtitleText", introSubtitle);

            AssignGo(so, "calibPanel", calib);
            Assign(so, "calibText", calibText);
            Assign(so, "calibProgressFill", calibFill);

            AssignGo(so, "chairSafetyPanel", chairSafety);
            Assign(so, "chairSafetyText", chairSafetyText);

            AssignGo(so, "hudPanel", hud);
            Assign(so, "poseNameText", poseNameText);
            Assign(so, "cardCountText", cardCountText);
            Assign(so, "sectionNameText", sectionNameText);
            Assign(so, "songProgressFill", songFill);
            Assign(so, "countdownRingFill", countdownRingFill);
            Assign(so, "ratingPopupText", ratingPopupText);
            Assign(so, "referenceImage", referenceImage);
            Assign(so, "feedbackText", feedbackText);
            Assign(so, "matchMeterFill", matchMeterFill);

            AssignGo(so, "framingPanel", framing);
            Assign(so, "framingPromptText", framingPromptText);

            AssignGo(so, "resultsPanel", results);
            Assign(so, "resultsStatsText", statsText);
            var starsProp = so.FindProperty("resultsStarImages");
            starsProp.arraySize = 3;
            for (int i = 0; i < 3; i++) starsProp.GetArrayElementAtIndex(i).objectReferenceValue = starImages[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            // Panels start correct: intro on, HUD/others off, feed always on (matches director.Start's
            // ShowOnly(introPanel)). Feed + background stay active behind everything.
            bg.SetActive(true);
            feed.SetActive(true);
            intro.SetActive(true);
            calib.SetActive(false);
            chairSafety.SetActive(false);
            hud.SetActive(false);
            results.SetActive(false);
            framing.SetActive(false);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Dance Star UI (2D clinical redesign) rebuilt and wired to DanceStarDirector.";
        }

        // ---- helpers ----

        static Color StarOff() => new Color(1f, 1f, 1f, 0.35f);

        static TMP_FontAsset LoadFont(string path)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) Debug.LogWarning($"[DanceStarUIBuilder] Font not found: {path} (using TMP default)");
            return f;
        }

        static GameObject NewPanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = false;
            return go;
        }

        static Image AddImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static GameObject AddCard(Transform parent, float x, float y, float w, float h) => AddCard(parent, x, y, w, h, CardWhite);

        static GameObject AddCard(Transform parent, float x, float y, float w, float h, Color color)
        {
            var img = AddImage(parent, "Card", null, color);
            Round(img);
            var shadow = img.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.11f, 0.19f, 0.33f, 0.22f);
            shadow.effectDistance = new Vector2(0, 5);
            Place(img.rectTransform, x, y, w, h);
            return img.gameObject;
        }

        static (Image track, Image fill) AddRadialRing(Transform parent, float x, float y, float size, Color color)
        {
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var track = AddImage(parent, "RingTrack", knob, TrackDim);
            Place(track.rectTransform, x, y, size, size);
            var fill = AddImage(parent, "RingFill", knob, color);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            Place(fill.rectTransform, x, y, size, size);
            return (track, fill);
        }

        static (Image track, Image fill) AddProgressBar(Transform parent, float x, float y, float w, float h, Color color)
        {
            var track = AddImage(parent, "BarTrack", null, new Color(1f, 1f, 1f, 0.28f));
            Round(track);
            Place(track.rectTransform, x, y, w, h);
            var fill = AddImage(parent, "BarFill", null, color);
            Round(fill);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            Place(fill.rectTransform, x, y, w, h);
            return (track, fill);
        }

        static Button AddPillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                    Color bg, float x, float y, float w, float h)
        {
            var img = AddImage(parent, name, null, bg);
            Round(img);
            img.raycastTarget = true;
            Place(img.rectTransform, x, y, w, h);
            var btn = img.gameObject.AddComponent<Button>();
            var text = AddText(img.transform, label, font, 40, Color.white, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);
            return btn;
        }

        static void Round(Image img)
        {
            var ui = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (ui != null) { img.sprite = ui; img.type = Image.Type.Sliced; }
        }

        static TMP_Text AddText(Transform parent, string text, TMP_FontAsset font, float size,
                                Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        static void Outline(TMP_Text t, Color color, float width)
        {
            t.outlineColor = color;
            t.outlineWidth = width;
            t.UpdateMeshPadding();
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(x / FW, 1f - (y + h) / FH);
            rt.anchorMax = new Vector2((x + w) / FW, 1f - y / FH);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void Assign(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning($"[DanceStarUIBuilder] Director field not found: {field}");
        }

        static void AssignGo(SerializedObject so, string field, GameObject value) => Assign(so, field, value);
    }
}
#endif
