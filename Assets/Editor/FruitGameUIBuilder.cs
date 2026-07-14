#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.FruitGame;

namespace Kinex.FruitGame.EditorTools
{
    /// <summary>
    /// One-click builder for the Fruit Header game UI (senior-friendly: big readable type,
    /// FC Iconic for Thai, Montserrat for numbers). Re-runnable: destroys its own panels by
    /// name and rebuilds, then wires every FruitGameManager field via SerializedObject.
    /// Layout uses fractional anchors derived from the 927×1427 portrait frame (same helper
    /// set as MegaDanceUIBuilder).
    /// </summary>
    public static class FruitGameUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        static readonly Color Green = new Color32(0x4C, 0xAF, 0x50, 0xFF);
        static readonly Color GreenDark = new Color32(0x1E, 0x5E, 0x20, 0xFF);
        static readonly Color Navy = new Color32(0x1F, 0x2F, 0x66, 0xFF);
        static readonly Color CardWhite = new Color32(0xFF, 0xFF, 0xFF, 0xF0);
        static readonly Color WarmRed = new Color32(0xE0, 0x5A, 0x4E, 0xFF);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";
        const string NumBlackPath = "Assets/Fonts/Montserrat-Black SDF.asset";

        static readonly string[] PanelNames =
        {
            "IntroPanel", "CalibPanel", "CountdownPanel", "HudPanel", "RestPanel",
            "BonusPanel", "ResultsPanel", "PauseOverlay", "ExitButton",
            "CalibWarningBanner", "DebugMeter",
        };

        [MenuItem("Kinex/Build Fruit Game UI")]
        public static void Build()
        {
            Debug.Log("[FruitGameUIBuilder] " + BuildUI());
        }

        /// <summary>Automation-friendly entry (no dialogs). Returns a status string.</summary>
        public static string BuildUI()
        {
            var manager = Object.FindAnyObjectByType<FruitGameManager>();
            if (manager == null) return "No FruitGameManager in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var thaiBlack = LoadFont(ThaiBlackPath);
            var thaiSemi = LoadFont(ThaiSemiPath);
            var numBlack = LoadFont(NumBlackPath);

            // Idempotent: retire any previous build of our panels.
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i);
                foreach (var n in PanelNames)
                    if (child.name == n) { Object.DestroyImmediate(child.gameObject); break; }
            }

            // =================== INTRO ===================
            var intro = NewPanel(canvas.transform, "IntroPanel");
            // Full-screen tap-to-skip button (transparent but raycastable).
            var tap = AddImage(intro.transform, "TapToStart", null, new Color(0, 0, 0, 0));
            tap.raycastTarget = true;
            Stretch(tap.rectTransform);
            var tapBtn = tap.gameObject.AddComponent<Button>();
            tapBtn.transition = Selectable.Transition.None;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(tapBtn.onClick, manager.SkipIntro);
            AddCard(intro.transform, 84, 330, 760, 480);
            var title = AddText(intro.transform, "สวนผลไม้", thaiBlack, 130, FontStyles.Normal,
                                GreenDark, TextAlignmentOptions.Center);
            Place(title.rectTransform, 84, 370, 760, 190);
            var subtitle = AddText(intro.transform, "ลุกยืนโหม่งของดีต่อสุขภาพ\nนั่งเฉยๆ ปล่อยของหวานลอยผ่านไป",
                                   thaiSemi, 48, FontStyles.Normal, Navy, TextAlignmentOptions.Center);
            Place(subtitle.rectTransform, 104, 580, 720, 180);
            var hint = AddText(intro.transform, "แตะที่หน้าจอเพื่อเริ่มเลย", thaiSemi, 40, FontStyles.Normal,
                               new Color(1f, 1f, 1f, 0.95f), TextAlignmentOptions.Center);
            Place(hint.rectTransform, 264, 1180, 400, 70);
            Outline(hint, new Color(0f, 0f, 0f, 0.6f), 0.2f);

            // =================== CALIB ===================
            var calib = NewPanel(canvas.transform, "CalibPanel");
            AddCard(calib.transform, 94, 430, 740, 460);
            var calibTitle = AddText(calib.transform, "เตรียมตัว", thaiBlack, 66, FontStyles.Normal,
                                     Navy, TextAlignmentOptions.Center);
            Place(calibTitle.rectTransform, 134, 470, 660, 100);
            var calibText = AddText(calib.transform, "นั่งบนเก้าอี้ให้เห็นเต็มตัว", thaiSemi, 52,
                                    FontStyles.Normal, GreenDark, TextAlignmentOptions.Center);
            Place(calibText.rectTransform, 134, 590, 660, 250);
            // Capture-progress bar: fills 0..1 while a seated/standing hold is being timed.
            var calibBarTrack = AddImage(calib.transform, "CalibBarTrack", null, new Color(0f, 0f, 0f, 0.15f));
            Place(calibBarTrack.rectTransform, 164, 810, 600, 28);
            var calibBar = AddImage(calib.transform, "CalibBarFill", null, Green);
            calibBar.type = Image.Type.Filled;
            calibBar.fillMethod = Image.FillMethod.Horizontal;
            calibBar.fillAmount = 0f;
            Place(calibBar.rectTransform, 164, 810, 600, 28);

            // =================== COUNTDOWN ===================
            var countdown = NewPanel(canvas.transform, "CountdownPanel");
            var countText = AddText(countdown.transform, "3", thaiBlack, 320, FontStyles.Normal,
                                    Green, TextAlignmentOptions.Center);
            countText.enableWordWrapping = false;
            countText.overflowMode = TextOverflowModes.Overflow;
            Place(countText.rectTransform, 214, 500, 500, 420);
            Outline(countText, GreenDark, 0.25f);

            // =================== HUD ===================
            // Top-left card (clear of the 234×300 camera feed pinned top-right); bottom corners
            // stay empty for GameHud's runtime ring gauges.
            var hud = NewPanel(canvas.transform, "HudPanel");
            AddCard(hud.transform, 56, 44, 520, 170);
            var setLabel = AddText(hud.transform, "เซตที่ 1/3", thaiBlack, 46, FontStyles.Normal,
                                   Navy, TextAlignmentOptions.Center);
            Place(setLabel.rectTransform, 76, 62, 480, 56);
            var repLabel = AddText(hud.transform, "0/15", numBlack, 78, FontStyles.Normal,
                                   GreenDark, TextAlignmentOptions.Center);
            Place(repLabel.rectTransform, 76, 118, 480, 84);
            var comboLabel = AddText(hud.transform, "", thaiBlack, 64, FontStyles.Normal,
                                     Green, TextAlignmentOptions.Center);
            Place(comboLabel.rectTransform, 84, 470, 760, 100);
            Outline(comboLabel, Color.white, 0.22f);

            // =================== REST ===================
            var rest = NewPanel(canvas.transform, "RestPanel");
            rest.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            var restTitle = AddText(rest.transform, "พักสักครู่", thaiBlack, 84, FontStyles.Normal,
                                    Color.white, TextAlignmentOptions.Center);
            Place(restTitle.rectTransform, 164, 220, 600, 120);
            var restCount = AddText(rest.transform, "พัก 60 วินาที", thaiSemi, 56, FontStyles.Normal,
                                    new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center);
            Place(restCount.rectTransform, 164, 360, 600, 90);
            // BreathingCue is created at runtime by the manager, centered in this panel.
            var skipBtn = AddPillButton(rest.transform, "SkipRestButton", "ข้ามการพัก", thaiSemi, Green,
                                        264, 1140, 400, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(skipBtn.onClick, manager.SkipRest);
            skipBtn.gameObject.SetActive(false);

            // =================== BONUS ===================
            var bonus = NewPanel(canvas.transform, "BonusPanel");
            var bonusTitle = AddText(bonus.transform, "รอบโบนัส!\nยกเข่าสลับซ้าย-ขวา เป่าลูกโป่ง", thaiBlack,
                                     56, FontStyles.Normal, Navy, TextAlignmentOptions.Center);
            Place(bonusTitle.rectTransform, 64, 90, 800, 190);
            Outline(bonusTitle, Color.white, 0.22f);
            var bonusCount = AddText(bonus.transform, "0/20", numBlack, 120, FontStyles.Normal,
                                     Green, TextAlignmentOptions.Center);
            bonusCount.enableWordWrapping = false;
            bonusCount.overflowMode = TextOverflowModes.Overflow;
            Place(bonusCount.rectTransform, 264, 300, 400, 160);
            Outline(bonusCount, GreenDark, 0.22f);

            // =================== RESULTS ===================
            var results = NewPanel(canvas.transform, "ResultsPanel");
            results.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var resTitle = AddText(results.transform, "จบเกม!", thaiBlack, 130, FontStyles.Normal,
                                   Color.white, TextAlignmentOptions.Center);
            Place(resTitle.rectTransform, 114, 250, 700, 190);
            Outline(resTitle, GreenDark, 0.25f);
            var starImages = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                starImages[i] = AddImage(results.transform, $"Star{i + 1}", null, new Color(1f, 1f, 1f, 0.22f));
                Round(starImages[i]);
                Place(starImages[i].rectTransform, 288 + i * 140, 470, 110, 110);
            }
            var statsText = AddText(results.transform, "", thaiSemi, 50, FontStyles.Normal,
                                    Color.white, TextAlignmentOptions.Center);
            Place(statsText.rectTransform, 114, 640, 700, 330);
            var homeBtn = AddPillButton(results.transform, "HomeButton", "กลับหน้าหลัก", thaiSemi, Green,
                                        239, 1060, 450, 120);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(homeBtn.onClick, manager.ExitToHome);

            // =================== PAUSE OVERLAY ===================
            var pause = NewPanel(canvas.transform, "PauseOverlay");
            pause.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            pause.GetComponent<Image>().raycastTarget = true;
            var pauseText = AddText(pause.transform, "ขยับให้เห็นเต็มตัวในกล้อง", thaiBlack, 62,
                                    FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
            Place(pauseText.rectTransform, 64, 600, 800, 200);

            // =================== CALIB WARNING BANNER (persistent, hidden by default) ===================
            // Shown once calibration exhausts every retry and falls back to an estimate — stays up
            // for the rest of the session so the fallback is never silent.
            var warnBanner = AddImage(canvas.transform, "CalibWarningBanner",
                                      null, new Color(0.85f, 0.55f, 0.1f, 0.94f));
            Round(warnBanner);
            warnBanner.raycastTarget = false;
            Place(warnBanner.rectTransform, 40, 1300, 847, 70);
            var warnText = AddText(warnBanner.transform, "การตรวจจับอาจไม่แม่นยำ", thaiSemi, 34,
                                   FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
            Stretch(warnText.rectTransform);
            warnBanner.gameObject.SetActive(false);

            // =================== DEBUG METER (persistent, all states) ===================
            // Small vertical Progress01 bar + phase label for tuning sit-stand detection on device.
            // Gated behind FruitGameManager.debugMeter (default ON).
            var debugMeter = NewPanel(canvas.transform, "DebugMeter");
            var meterTrack = AddImage(debugMeter.transform, "MeterTrack", null, new Color(1f, 1f, 1f, 0.18f));
            Place(meterTrack.rectTransform, 20, 520, 36, 260);
            var meterFill = AddImage(debugMeter.transform, "MeterFill", null, Green);
            meterFill.type = Image.Type.Filled;
            meterFill.fillMethod = Image.FillMethod.Vertical;
            meterFill.fillOrigin = (int)Image.OriginVertical.Bottom;
            meterFill.fillAmount = 0f;
            Place(meterFill.rectTransform, 20, 520, 36, 260);
            var meterLabel = AddText(debugMeter.transform, "seated 0.00", thaiSemi, 26, FontStyles.Normal,
                                     Color.white, TextAlignmentOptions.Center);
            Outline(meterLabel, new Color(0f, 0f, 0f, 0.6f), 0.2f);
            Place(meterLabel.rectTransform, 0, 786, 160, 44);

            // =================== EXIT (all states) ===================
            var exitBtn = AddPillButton(canvas.transform, "ExitButton", "ออก", thaiSemi, WarmRed,
                                        40, 44, 150, 92);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitBtn.onClick, manager.ExitToHome);

            // Keep the camera feed drawn above everything (incl. the pause dim) — exit above panels.
            exitBtn.transform.SetAsLastSibling();
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed != null) feed.SetAsLastSibling();

            // ---- Wire every manager field. ----
            var so = new SerializedObject(manager);
            AssignGo(so, "introPanel", intro);
            AssignGo(so, "calibPanel", calib);
            AssignGo(so, "countdownPanel", countdown);
            AssignGo(so, "hudPanel", hud);
            AssignGo(so, "restPanel", rest);
            AssignGo(so, "bonusPanel", bonus);
            AssignGo(so, "resultsPanel", results);
            AssignGo(so, "pauseOverlay", pause);
            Assign(so, "calibText", calibText);
            Assign(so, "countdownText", countText);
            Assign(so, "setText", setLabel);
            Assign(so, "repText", repLabel);
            Assign(so, "comboText", comboLabel);
            Assign(so, "restCountdownText", restCount);
            Assign(so, "bonusCountText", bonusCount);
            Assign(so, "resultsStatsText", statsText);
            AssignGo(so, "skipRestButton", skipBtn.gameObject);
            Assign(so, "calibProgressBar", calibBar);
            AssignGo(so, "calibWarningBanner", warnBanner.gameObject);
            AssignGo(so, "debugMeterPanel", debugMeter);
            Assign(so, "debugMeterFill", meterFill);
            Assign(so, "debugMeterLabel", meterLabel);
            var starsProp = so.FindProperty("resultsStarImages");
            starsProp.arraySize = 3;
            for (int i = 0; i < 3; i++)
                starsProp.GetArrayElementAtIndex(i).objectReferenceValue = starImages[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            // Default visibility: intro only; the manager drives everything else.
            intro.SetActive(true);
            calib.SetActive(false); countdown.SetActive(false); hud.SetActive(false);
            rest.SetActive(false); bonus.SetActive(false); results.SetActive(false);
            pause.SetActive(false);
            debugMeter.SetActive(true); // manager syncs this to its debugMeter bool every frame

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Fruit Game UI rebuilt and wired to FruitGameManager.";
        }

        // ---- helpers (MegaDanceUIBuilder pattern) ----------------------------------

        static TMP_FontAsset LoadFont(string path)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) Debug.LogWarning($"[FruitGameUIBuilder] Font not found: {path} (using TMP default)");
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

        static GameObject AddCard(Transform parent, float x, float y, float w, float h)
        {
            var img = AddImage(parent, "Card", null, CardWhite);
            Round(img);
            var shadow = img.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.25f);
            shadow.effectDistance = new Vector2(0, 6);
            Place(img.rectTransform, x, y, w, h);
            return img.gameObject;
        }

        static Button AddPillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                    Color bg, float x, float y, float w, float h)
        {
            var img = AddImage(parent, name, null, bg);
            Round(img);
            img.raycastTarget = true;
            Place(img.rectTransform, x, y, w, h);
            var btn = img.gameObject.AddComponent<Button>();
            var text = AddText(img.transform, label, font, 46, FontStyles.Bold, Color.white,
                               TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return btn;
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

        // Place a RectTransform using Figma top-left coords in the 927×1427 frame, via
        // fractional anchors so it scales with any canvas resolution.
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
            else Debug.LogWarning($"[FruitGameUIBuilder] Manager field not found: {field}");
        }

        static void AssignGo(SerializedObject so, string field, GameObject value) => Assign(so, field, value);
    }
}
#endif
