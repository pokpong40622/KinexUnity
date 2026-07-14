#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.BalanceQuest;

namespace Kinex.BalanceQuest.EditorTools
{
    /// <summary>
    /// One-click builder for the Balance Quest in-session UI. Re-runnable: clears the Canvas
    /// (keeping the live camera-feed preview subtree), builds every director panel, and wires
    /// every BalanceQuestDirector field. Layout uses fractional anchors from a 927x1427 portrait
    /// frame — same helper set/convention as Kinex.World.EditorTools.KinexWorldUIBuilder.
    ///
    /// Design language: glow-stage dusk palette — deep indigo/hot-pink scrims, cyan/magenta
    /// accents, large senior-readable Thai (FCIconic) + Montserrat numerals.
    /// </summary>
    public static class BalanceQuestUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        static readonly Color White    = new Color32(0xFF, 0xFF, 0xFF, 0xF5);
        static readonly Color Ink      = new Color32(0x1E, 0x14, 0x33, 0xFF);
        static readonly Color Cyan     = new Color32(0x33, 0xE0, 0xF0, 0xFF);
        static readonly Color Magenta  = new Color32(0xF2, 0x40, 0x8C, 0xFF);
        static readonly Color Gold     = new Color32(0xFF, 0xC8, 0x3D, 0xFF);
        static readonly Color GrayText = new Color32(0x5B, 0x54, 0x66, 0xFF);
        static readonly Color Track    = new Color32(0xE3, 0xE0, 0xEE, 0xFF);

        static readonly Color DuskScrim = new Color(0.10f, 0.06f, 0.20f, 0.85f);

        const string BlackPath  = "Assets/Fonts/Montserrat-Black SDF.asset";
        const string ItalicPath = "Assets/Fonts/Montserrat-BlackItalic SDF.asset";
        const string SemiPath   = "Assets/Fonts/Montserrat-SemiBold SDF.asset";
        const string ThaiPath   = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";
        const string BackSpritePath = "Assets/Art/MegaDanceUI/back_button.png";

        [MenuItem("Kinex/Build Balance Quest UI")]
        public static void Build()
        {
            Debug.Log("[BalanceQuestUIBuilder] " + BuildUI());
        }

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<BalanceQuestDirector>();
            if (director == null) return "No BalanceQuestDirector in the open scene. Open BalanceQuestScene first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var black  = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BlackPath);
            var italic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ItalicPath);
            var semi   = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiPath);
            var thai   = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiPath) != null
                         ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiPath) : black;
            var thaiSemi = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiSemiPath) != null
                         ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiSemiPath) : semi;

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i).gameObject;
                if (child.GetComponentInChildren<RawImage>(true) != null) continue; // keep camera feed
                Object.DestroyImmediate(child);
            }

            var so = new SerializedObject(director);

            var intro       = MakePanel("IntroPanel", canvas.transform, new Color(0, 0, 0, 0));
            var calib       = MakePanel("CalibPanel", canvas.transform, new Color(0, 0, 0, 0));
            var countdown   = MakePanel("CountdownPanel", canvas.transform, DuskScrim);
            var hud         = MakePanel("HudPanel", canvas.transform, new Color(0, 0, 0, 0));
            var results     = MakePanel("ResultsPanel", canvas.transform, DuskScrim);

            // =================== INTRO ===================
            AddExit(intro.transform, director);
            AddCard(intro.transform, 70, 300, 787, 560);
            var accentBar = AddImage(intro.transform, "AccentBar", null, Magenta);
            Round(accentBar);
            Place(accentBar.rectTransform, 70, 300, 787, 16);

            var title = AddText(intro.transform, "เส้นทางนักสมดุล", thai, 78, FontStyles.Normal, Ink, TextAlignmentOptions.Center);
            Place(title.rectTransform, 110, 345, 707, 160);
            Outline(title, Cyan, 0.06f);

            var subtitle = AddText(intro.transform, "เตรียมเก้าอี้มั่นคงไว้ด้านหลังก่อนเริ่มเล่นนะครับ",
                                   thaiSemi, 36, FontStyles.Normal, GrayText, TextAlignmentOptions.Center);
            Place(subtitle.rectTransform, 140, 540, 647, 160);

            AddPillButton(intro.transform, "StartButton", 140, 730, 647, 120,
                          Magenta, "เริ่มเลย", thai, 54, Color.white, director.StartSession);

            // =================== CALIBRATION ===================
            AddExit(calib.transform, director);
            AddCard(calib.transform, 70, 350, 787, 520);
            var calibAccent = AddImage(calib.transform, "AccentBar", null, Cyan);
            Round(calibAccent);
            Place(calibAccent.rectTransform, 70, 350, 787, 14);

            var calibPrompt = AddText(calib.transform, "ยืนตรงกลางให้เห็นเต็มตัว 3 วินาที",
                                      thaiSemi, 42, FontStyles.Normal, Ink, TextAlignmentOptions.Center);
            Place(calibPrompt.rectTransform, 120, 420, 687, 320);

            // =================== COUNTDOWN ===================
            var countNum = AddText(countdown.transform, "3", italic, 300, FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
            Place(countNum.rectTransform, 100, 560, 727, 340);
            Outline(countNum, Magenta, 0.16f);

            // =================== HUD ===================
            AddExit(hud.transform, director);

            // Top-left: coin counter card.
            var coinCard = AddCard(hud.transform, 45, 45, 220, 100);
            var coinText = AddText(coinCard.transform, "0", italic, 46, FontStyles.Normal, Gold, TextAlignmentOptions.Center);
            Stretch(coinText.rectTransform);

            // Top-right-of-center: stage progress ("ด่าน 1/3").
            var progressCard = AddCard(hud.transform, 300, 55, 320, 80);
            var progressLbl = AddText(progressCard.transform, "ด่าน 1/3", thaiSemi, 36, FontStyles.Normal, Ink, TextAlignmentOptions.Center);
            Stretch(progressLbl.rectTransform);

            // Center-top beat-cue card: pictogram + Thai instruction.
            var cue = AddCard(hud.transform, 90, 400, 747, 220);
            var cueArrow = AddText(cue.transform, "", italic, 90, FontStyles.Normal, Magenta, TextAlignmentOptions.Center);
            Place(cueArrow.rectTransform, 20, 20, 707, 100);
            var cueInstruction = AddText(cue.transform, "", thai, 44, FontStyles.Normal, Ink, TextAlignmentOptions.Center);
            Place(cueInstruction.rectTransform, 20, 120, 707, 90);
            cue.SetActive(false);

            // =================== CHECKPOINT BANNER (overlays HUD) ===================
            var checkpoint = MakePanel("CheckpointPanel", canvas.transform, DuskScrim);
            var checkpointText = AddText(checkpoint.transform, "ผ่านด่านที่ 1!", italic, 70, FontStyles.Normal, Gold, TextAlignmentOptions.Center);
            Place(checkpointText.rectTransform, 80, 640, 767, 160);
            Outline(checkpointText, Magenta, 0.12f);
            checkpoint.SetActive(false);

            // =================== PAUSE OVERLAY (camera-lost, overlays HUD) ===================
            var pause = MakePanel("PauseOverlay", canvas.transform, new Color(0f, 0f, 0f, 0.6f));
            var pauseText = AddText(pause.transform, "ขยับให้เห็นเต็มตัวในกล้อง", thaiSemi, 46, FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
            Place(pauseText.rectTransform, 100, 640, 727, 160);
            pause.SetActive(false);

            // =================== RESULTS ===================
            var doneTitle = AddText(results.transform, "จบเส้นทาง!", italic, 90, FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
            Place(doneTitle.rectTransform, 80, 220, 767, 160);
            Outline(doneTitle, Magenta, 0.12f);

            var starsText = AddText(results.transform, "***", italic, 90, FontStyles.Normal, Gold, TextAlignmentOptions.Center);
            Place(starsText.rectTransform, 80, 390, 767, 120);

            var avgLbl = AddText(results.transform, "คะแนนเฉลี่ย", thaiSemi, 32, FontStyles.Normal, new Color(1, 1, 1, 0.7f), TextAlignmentOptions.Center);
            Place(avgLbl.rectTransform, 80, 540, 767, 50);

            var avgVal = AddText(results.transform, "0%", italic, 160, FontStyles.Normal, Cyan, TextAlignmentOptions.Center);
            Place(avgVal.rectTransform, 80, 590, 767, 200);

            var coinsVal = AddText(results.transform, "0 เหรียญ", thai, 44, FontStyles.Normal, Gold, TextAlignmentOptions.Center);
            Place(coinsVal.rectTransform, 80, 800, 767, 70);

            AddPillButton(results.transform, "HomeButton", 220, 950, 487, 120,
                          Cyan, "กลับหน้าแรก", thai, 46, Ink, director.ExitSession);

            BuildCornerPreview(canvas);

            // ---- wire every BalanceQuestDirector field ----
            Assign(so, "introPanel", intro);
            Assign(so, "introTitleText", title);
            Assign(so, "introSubtitleText", subtitle);
            Assign(so, "calibPanel", calib);
            Assign(so, "calibPromptText", calibPrompt);
            Assign(so, "countdownPanel", countdown);
            Assign(so, "countdownText", countNum);
            Assign(so, "hudPanel", hud);
            Assign(so, "coinCounterText", coinText);
            Assign(so, "progressText", progressLbl);
            Assign(so, "cueCard", cue);
            Assign(so, "cueArrowText", cueArrow);
            Assign(so, "cueInstructionText", cueInstruction);
            Assign(so, "checkpointPanel", checkpoint);
            Assign(so, "checkpointText", checkpointText);
            Assign(so, "pauseOverlay", pause);
            Assign(so, "resultsPanel", results);
            Assign(so, "resultsStarsText", starsText);
            Assign(so, "resultsPercentText", avgVal);
            Assign(so, "resultsCoinsText", coinsVal);
            so.ApplyModifiedPropertiesWithoutUndo();

            intro.SetActive(true);
            calib.SetActive(false);
            countdown.SetActive(false);
            hud.SetActive(false);
            results.SetActive(false);

            EditorUtility.SetDirty(director);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Balance Quest UI rebuilt (glow-stage theme) and wired to BalanceQuestDirector.";
        }

        static void BuildCornerPreview(Canvas canvas)
        {
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed == null) return;
            feed.SetParent(canvas.transform, false);
            feed.gameObject.SetActive(true);
            feed.SetAsLastSibling();
            Place((RectTransform)feed, 648, 55, 234, 300);
            var img = feed.GetComponent<Image>();
            if (img != null) img.color = new Color(0.08f, 0.05f, 0.18f, 1f);
            var raw = feed.GetComponentInChildren<RawImage>(true);
            if (raw != null)
            {
                raw.gameObject.SetActive(true);
                var rt = raw.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(5, 5); rt.offsetMax = new Vector2(-5, -5);
            }
        }

        // ---- helpers (same shape as KinexWorldUIBuilder) ----

        static GameObject MakePanel(string name, Transform parent, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = bg.a > 0f;
            return go;
        }

        static GameObject AddCard(Transform parent, float x, float y, float w, float h)
        {
            var img = AddImage(parent, "Card", null, White);
            Round(img);
            var sh = img.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.28f);
            sh.effectDistance = new Vector2(0, 8);
            Place(img.rectTransform, x, y, w, h);
            return img.gameObject;
        }

        static void AddExit(Transform parent, BalanceQuestDirector director)
        {
            var backSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackSpritePath);
            var btn = AddImage(parent, "ExitButton", backSprite, Color.white);
            btn.raycastTarget = true;
            var b = btn.gameObject.AddComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(b.onClick, director.ExitSession);
            Place(btn.rectTransform, 45, 50, 130, 130);
        }

        static void AddPillButton(Transform parent, string name, float x, float y, float w, float h,
                                  Color bg, string label, TMP_FontAsset font, float size, Color labelColor,
                                  UnityEngine.Events.UnityAction action)
        {
            var img = AddImage(parent, name, null, bg);
            Round(img);
            img.raycastTarget = true;
            var sh = img.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.22f);
            sh.effectDistance = new Vector2(0, 5);
            var btn = img.gameObject.AddComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, action);
            Place(img.rectTransform, x, y, w, h);
            var lbl = AddText(img.transform, label, font, size, FontStyles.Normal, labelColor, TextAlignmentOptions.Center);
            Stretch(lbl.rectTransform);
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

        static void Round(Image img)
        {
            var ui = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (ui != null) { img.sprite = ui; img.type = Image.Type.Sliced; }
        }

        static TMP_Text AddText(Transform parent, string text, TMP_FontAsset font, float size,
                                FontStyles style, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
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
        }
    }
}
#endif
