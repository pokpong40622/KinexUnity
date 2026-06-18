#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.World;

namespace Kinex.World.EditorTools
{
    /// <summary>
    /// One-click builder for the KINEX WORLD in-session HUD. Re-runnable: clears the Canvas
    /// (keeping the live camera-feed preview subtree), builds the five director panels
    /// (Intro / Countdown / HUD / Transition / Results), and wires every KinexWorldDirector
    /// field.
    ///
    /// Design language: game-like energy via large Montserrat Black (Italic for titles), a
    /// vivid Blue/Orange/Navy palette, thick accent bars, and punchy score typography. White
    /// rounded cards ground interactive elements on the 3-D background.
    ///
    /// Layout uses fractional anchors from the 927×1427 portrait frame so it scales to any
    /// canvas resolution. Run with KinexWorldScene open.
    /// </summary>
    public static class KinexWorldUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        // Kinex World palette.
        static readonly Color White    = new Color32(0xFF, 0xFF, 0xFF, 0xF5); // card background
        static readonly Color Navy     = new Color32(0x1F, 0x2F, 0x66, 0xFF); // primary text / scrim tint
        static readonly Color Blue     = new Color32(0x27, 0x66, 0xEF, 0xFF); // accent / hero numbers
        static readonly Color Orange   = new Color32(0xFF, 0x8A, 0x00, 0xFF); // CTA / results score
        static readonly Color Green    = new Color32(0x5E, 0xC8, 0x32, 0xFF); // live-score fill / encourage
        static readonly Color GrayText = new Color32(0x5B, 0x64, 0x72, 0xFF); // secondary text
        static readonly Color Track    = new Color32(0xE3, 0xE6, 0xEE, 0xFF); // progress track

        // Scrims
        static readonly Color NavyScrim  = new Color(0.08f, 0.12f, 0.28f, 0.82f); // transition / results
        static readonly Color BlueScrim  = new Color(0.05f, 0.10f, 0.35f, 0.90f); // countdown dramatic bg

        const string BlackPath      = "Assets/Fonts/Montserrat-Black SDF.asset";
        const string ItalicPath     = "Assets/Fonts/Montserrat-BlackItalic SDF.asset";
        const string SemiPath       = "Assets/Fonts/Montserrat-SemiBold SDF.asset";
        const string BackSpritePath = "Assets/Art/MegaDanceUI/back_button.png";

        [MenuItem("Kinex/Build Kinex World UI")]
        public static void Build()
        {
            Debug.Log("[KinexWorldUIBuilder] " + BuildUI());
        }

        // No-dialog worker so it can be driven from automation. Returns a status string.
        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<KinexWorldDirector>();
            if (director == null) return "No KinexWorldDirector in the open scene. Open KinexWorldScene first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var black  = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BlackPath);
            var italic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ItalicPath);
            var semi   = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiPath);

            // Clear the canvas of everything EXCEPT the live camera-feed subtree (kept as preview).
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i).gameObject;
                if (child.GetComponentInChildren<RawImage>(true) != null) continue; // keep camera feed
                Object.DestroyImmediate(child);
            }

            var so = new SerializedObject(director);

            var intro      = MakePanel("IntroPanel",      canvas.transform, new Color(0, 0, 0, 0));
            var countdown  = MakePanel("CountdownPanel",  canvas.transform, BlueScrim);
            var hud        = MakePanel("HUDPanel",        canvas.transform, new Color(0, 0, 0, 0));
            var transition = MakePanel("TransitionPanel", canvas.transform, NavyScrim);
            var results    = MakePanel("ResultsPanel",    canvas.transform, NavyScrim);

            // =================== INTRO ===================
            // Cinematic intro: dark gradient card, italic wordmark, colour-bar accent, big CTA.
            AddExit(intro.transform, director);

            // Main card — slightly tinted White for premium feel.
            AddCard(intro.transform, 70, 260, 787, 640);

            // Thin Blue accent bar at top of the card — a game-like stripe.
            var accentBar = AddImage(intro.transform, "AccentBar", null, Blue);
            Round(accentBar);
            Place(accentBar.rectTransform, 70, 260, 787, 16);

            // "KINEX" in Montserrat Black Italic — big, energetic.
            var kinexWord = AddText(intro.transform, "KINEX", italic, 130, FontStyles.Normal,
                                    Navy, TextAlignmentOptions.Center);
            Place(kinexWord.rectTransform, 100, 295, 727, 155);
            Outline(kinexWord, Blue, 0.08f);

            // "WORLD" smaller, Blue colour — two-line title hierarchy.
            var worldWord = AddText(intro.transform, "WORLD", black, 72, FontStyles.Normal,
                                    Blue, TextAlignmentOptions.Center);
            Place(worldWord.rectTransform, 100, 440, 727, 95);

            // Thin divider between title and routine info.
            var divider = AddImage(intro.transform, "Divider", null, new Color32(0xE3, 0xE6, 0xEE, 0xFF));
            Place(divider.rectTransform, 170, 555, 587, 4);

            // Routine name — SemiBold, Blue, medium size.
            var routineName = AddText(intro.transform, "Daily Class", semi, 52, FontStyles.Normal,
                                      Blue, TextAlignmentOptions.Center);
            Place(routineName.rectTransform, 100, 575, 727, 75);

            // Subtitle hint.
            var subtitle = AddText(intro.transform, "Follow the trainer — the class keeps going, so just do your best!",
                                   semi, 32, FontStyles.Normal, GrayText, TextAlignmentOptions.Center);
            Place(subtitle.rectTransform, 140, 665, 647, 120);

            // Big Orange START button — pill, full width of card content.
            AddPillButton(intro.transform, "StartButton", 140, 810, 647, 120,
                          Orange, "START", italic, 62, Color.white, director.StartSession);

            // =================== COUNTDOWN ===================
            // Full-panel dark-blue scrim — dramatic; content centered vertically.

            // "GET READY" label in White semi-bold.
            var getReady = AddText(countdown.transform, "GET READY", semi, 52, FontStyles.Normal,
                                   new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Center);
            Place(getReady.rectTransform, 100, 420, 727, 75);

            // Massive countdown number — White with Blue outline pop.
            var countNum = AddText(countdown.transform, "3", italic, 340, FontStyles.Normal,
                                   Color.white, TextAlignmentOptions.Center);
            Place(countNum.rectTransform, 100, 490, 727, 420);
            Outline(countNum, Blue, 0.18f);

            // Thin Blue horizontal line below the number for visual grounding.
            var countLine = AddImage(countdown.transform, "CountLine", null, Blue);
            Place(countLine.rectTransform, 263, 915, 400, 6);

            // Exercise name preview (shown as "Exercise: X" during countdown).
            var countExLabel = AddText(countdown.transform, "Exercise", semi, 38, FontStyles.Normal,
                                       new Color(1f, 1f, 1f, 0.60f), TextAlignmentOptions.Center);
            Place(countExLabel.rectTransform, 100, 940, 727, 60);

            // =================== CALIBRATION ===================
            // Guided T-pose capture — white card, clear prompt, large countdown.
            var calib = MakePanel("CalibrationPanel", canvas.transform, new Color(0, 0, 0, 0));
            AddExit(calib.transform, director);

            AddCard(calib.transform, 70, 350, 787, 600);

            // Blue accent bar at top of calib card.
            var calibAccent = AddImage(calib.transform, "AccentBar", null, Blue);
            Round(calibAccent);
            Place(calibAccent.rectTransform, 70, 350, 787, 14);

            // "CALIBRATION" title.
            var calibTitle = AddText(calib.transform, "CALIBRATION", black, 56, FontStyles.Normal,
                                     Navy, TextAlignmentOptions.Center);
            Place(calibTitle.rectTransform, 100, 380, 727, 80);

            // Prompt text.
            var calibPrompt = AddText(calib.transform, "Stand in a T-pose\narms straight out to the sides",
                                      semi, 40, FontStyles.Normal, GrayText, TextAlignmentOptions.Center);
            Place(calibPrompt.rectTransform, 120, 475, 687, 200);

            // Large countdown — Blue.
            var calibCount = AddText(calib.transform, "", black, 200, FontStyles.Normal,
                                     Blue, TextAlignmentOptions.Center);
            Place(calibCount.rectTransform, 120, 680, 687, 240);

            // =================== HUD ===================
            // Thick timer bar at the very top (8px taller than before, more visible).
            var timerBg = AddImage(hud.transform, "TimerBG", null, new Color(1f, 1f, 1f, 0.22f));
            Place(timerBg.rectTransform, 0, 0, 927, 20);
            var timerFill = AddImage(hud.transform, "TimerFill", null, Blue);
            MakeFilledLeft(timerFill);
            var tf = timerFill.rectTransform;
            tf.SetParent(timerBg.transform, false);
            Stretch(tf);

            // Exit button.
            AddExit(hud.transform, director);

            // Top pill card: exercise name (left) + counter (right). Card is slightly taller/wider.
            AddCard(hud.transform, 165, 40, 470, 118);

            // Exercise name — Black font, navy, larger.
            var exName = AddText(hud.transform, "Exercise", black, 44, FontStyles.Normal,
                                 Navy, TextAlignmentOptions.Left);
            Place(exName.rectTransform, 195, 57, 295, 84);

            // Counter — italic, Blue, punchy.
            var counter = AddText(hud.transform, "1/5", italic, 50, FontStyles.Normal,
                                  Blue, TextAlignmentOptions.Right);
            Place(counter.rectTransform, 495, 57, 126, 84);

            // Camera hint banner.
            var hint = AddCard(hud.transform, 90, 185, 747, 80);
            var hintText = AddText(hint.transform, "Step back ~2m — full body in view", semi, 32,
                                   FontStyles.Normal, Navy, TextAlignmentOptions.Center);
            Stretch(hintText.rectTransform);
            hint.SetActive(false);

            // Bottom score card — taller for more breathing room.
            AddCard(hud.transform, 46, 1050, 835, 345);

            // Blue accent stripe at top of score card.
            var scoreAccent = AddImage(hud.transform, "ScoreAccent", null, Blue);
            Round(scoreAccent);
            Place(scoreAccent.rectTransform, 46, 1050, 835, 12);

            // "LIVE SCORE" label — semi, small caps style, Gray.
            var scoreLabel = AddText(hud.transform, "LIVE SCORE", semi, 28, FontStyles.Normal,
                                     GrayText, TextAlignmentOptions.Center);
            Place(scoreLabel.rectTransform, 86, 1075, 755, 42);

            // Big percent — Italic, Blue, very large.
            var percent = AddText(hud.transform, "0%", italic, 130, FontStyles.Normal,
                                  Blue, TextAlignmentOptions.Center);
            Place(percent.rectTransform, 86, 1110, 755, 160);

            // Live bar track.
            var barBg = AddImage(hud.transform, "LiveBarBG", null, Track);
            Round(barBg);
            Place(barBg.rectTransform, 110, 1280, 707, 38);

            // Live bar fill.
            var barFill = AddImage(barBg.transform, "LiveBarFill", null, Green);
            Round(barFill);
            MakeFilledLeft(barFill);
            Stretch(barFill.rectTransform);

            // Encouragement — italic, Green, medium.
            var encourage = AddText(hud.transform, "", italic, 44, FontStyles.Normal,
                                    Green, TextAlignmentOptions.Center);
            Place(encourage.rectTransform, 86, 1330, 755, 60);

            // =================== TRANSITION ===================
            // Navy scrim with "UP NEXT" kicker and the next exercise name large.

            // Thin horizontal accent bar at vertical center.
            var transAccent = AddImage(transition.transform, "TransAccent", null, Orange);
            Place(transAccent.rectTransform, 0, 700, 927, 6);

            // "UP NEXT" kicker label.
            var upNext = AddText(transition.transform, "UP NEXT", semi, 38, FontStyles.Normal,
                                 new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Center);
            Place(upNext.rectTransform, 100, 580, 727, 60);

            // Exercise name — large, italic, White.
            var transText = AddText(transition.transform, "Next: …", italic, 78, FontStyles.Normal,
                                    Color.white, TextAlignmentOptions.Center);
            Place(transText.rectTransform, 80, 640, 767, 180);
            Outline(transText, Blue, 0.10f);

            // =================== RESULTS ===================
            // Navy scrim, italic "CLASS COMPLETE!" White, massive Orange average %, two buttons.

            // "CLASS COMPLETE!" — italic White, with outline.
            var doneTitle = AddText(results.transform, "CLASS\nCOMPLETE!", italic, 100, FontStyles.Normal,
                                    Color.white, TextAlignmentOptions.Center);
            Place(doneTitle.rectTransform, 80, 260, 767, 280);
            Outline(doneTitle, Blue, 0.12f);

            // Thin Orange separator.
            var resSep = AddImage(results.transform, "Separator", null, Orange);
            Place(resSep.rectTransform, 200, 555, 527, 6);

            // "AVERAGE PERFORMANCE" label.
            var avgLbl = AddText(results.transform, "AVERAGE PERFORMANCE", semi, 34, FontStyles.Normal,
                                 new Color(1f, 1f, 1f, 0.70f), TextAlignmentOptions.Center);
            Place(avgLbl.rectTransform, 80, 575, 767, 55);

            // Big average % — italic, Orange — stands out against Navy scrim.
            var avgVal = AddText(results.transform, "0%", italic, 200, FontStyles.Normal,
                                 Orange, TextAlignmentOptions.Center);
            Place(avgVal.rectTransform, 80, 630, 767, 260);
            Outline(avgVal, new Color(1f, 1f, 1f, 0.25f), 0.08f);

            // "AGAIN" — White semi-transparent card, Navy text.
            AddPillButton(results.transform, "AgainButton", 100, 935, 320, 118,
                          new Color32(0xFF, 0xFF, 0xFF, 0xCC), "AGAIN", black, 46, Navy, director.StartSession);

            // "DONE" — Orange pill, White text.
            AddPillButton(results.transform, "DoneButton", 507, 935, 320, 118,
                          Orange, "DONE", black, 46, Color.white, director.ExitSession);

            // ---- corner camera + skeleton preview (reuse the scene's feed) ----
            BuildCornerPreview(canvas);

            // ---- wire every KinexWorldDirector field ----
            Assign(so, "introPanel",           intro);
            Assign(so, "routineNameText",      routineName);
            Assign(so, "countdownPanel",       countdown);
            Assign(so, "countdownText",        countNum);
            Assign(so, "calibrationPanel",     calib);
            Assign(so, "calibPromptText",      calibPrompt);
            Assign(so, "calibCountdownText",   calibCount);
            Assign(so, "hudPanel",             hud);
            Assign(so, "exerciseNameText",     exName);
            Assign(so, "exerciseCounterText",  counter);
            Assign(so, "percentText",          percent);
            Assign(so, "liveBarFill",          barFill);
            Assign(so, "encourageText",        encourage);
            Assign(so, "segmentTimerFill",     timerFill);
            Assign(so, "cameraHint",           hint);
            Assign(so, "cameraHintText",       hintText);
            Assign(so, "transitionPanel",      transition);
            Assign(so, "transitionText",       transText);
            Assign(so, "resultsPanel",         results);
            Assign(so, "resultsAverageText",   avgVal);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Default: Intro shown, the rest hidden.
            intro.SetActive(true);
            calib.SetActive(false);
            countdown.SetActive(false);
            hud.SetActive(false);
            transition.SetActive(false);
            results.SetActive(false);

            EditorUtility.SetDirty(director);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Kinex World HUD rebuilt (game-like theme) and wired to KinexWorldDirector. Press Play to test, then save the scene.";
        }

        // Reuse the scene's webcam feed (CameraFeedPanel > RawImage + skeleton) as a small
        // always-on top-right preview, on top of everything.
        static void BuildCornerPreview(Canvas canvas)
        {
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed == null) return;
            feed.SetParent(canvas.transform, false);
            feed.gameObject.SetActive(true);
            feed.SetAsLastSibling();
            // Corner preview: top-right, same position as before.
            Place((RectTransform)feed, 648, 55, 234, 300);
            var img = feed.GetComponent<Image>();
            // Dark border behind the feed for a clean inset frame.
            if (img != null) img.color = new Color(0.06f, 0.12f, 0.25f, 1f);
            var raw = feed.GetComponentInChildren<RawImage>(true);
            if (raw != null)
            {
                raw.gameObject.SetActive(true);
                var rt = raw.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(5, 5); rt.offsetMax = new Vector2(-5, -5);
            }
        }

        // ---- helpers ----------------------------------------------------------------

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

        // A white rounded card with a soft drop shadow.
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

        static void AddExit(Transform parent, KinexWorldDirector director)
        {
            var backSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackSpritePath);
            var btn = AddImage(parent, "ExitButton", backSprite, Color.white);
            btn.raycastTarget = true;
            var b = btn.gameObject.AddComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(b.onClick, director.ExitSession);
            Place(btn.rectTransform, 45, 50, 162, 162);
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

        static void MakeFilledLeft(Image img)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 0f;
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
