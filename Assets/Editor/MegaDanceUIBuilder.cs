#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.MegaDance;

namespace Kinex.MegaDance.EditorTools
{
    /// <summary>
    /// One-click builder for the MEGA DANCE game UI, styled to match the 4 Figma frames
    /// (Start / FirstPose / Playing / Correct). Re-runnable: it reuses the panel GameObjects
    /// already referenced by MegaDanceManager (creating any that are missing), clears their
    /// contents, rebuilds the Figma layout, and wires every manager field.
    ///
    /// Layout is resolution-independent: every element is placed with fractional anchors
    /// derived from the 927×1427 Figma frame, so it adapts to the Canvas reference resolution.
    /// The live camera feed (existing RawImage) is left untouched and shows through behind the
    /// Playing HUD. Run AFTER "Kinex/Capture Pose Previews".
    /// </summary>
    public static class MegaDanceUIBuilder
    {
        // Figma frame size — all coordinates below are in this space (origin top-left, y down).
        const float FW = 927f, FH = 1427f;

        // Palette pulled from the Figma frames.
        static readonly Color Green     = new Color32(0x6F, 0xCF, 0x3A, 0xFF); // Start button / Correct
        static readonly Color OutlineGray = new Color32(0x6B, 0x6B, 0x6B, 0xFF);
        static readonly Color PanelDim   = new Color(0, 0, 0, 0.18f);          // slight scrim on Correct

        const string BlackPath = "Assets/Fonts/Montserrat-Black SDF.asset";        // Figma title weight (900)
        const string ItalicPath = "Assets/Fonts/Montserrat-BlackItalic SDF.asset";  // MEGA DANCE / Correct!
        const string SemiPath = "Assets/Fonts/Montserrat-SemiBold SDF.asset";        // body
        const string BackSpritePath = "Assets/Art/MegaDanceUI/back_button.png";
        const string StartSpritePath = "Assets/Art/MegaDanceUI/start_button.png";
        const string CardSpritePath = "Assets/Art/MegaDanceUI/card_frame.png";
        const string GearSpritePath = "Assets/Art/MegaDanceUI/gear.png";

        [MenuItem("Kinex/Build MegaDance UI")]
        public static void Build()
        {
            EditorUtility.DisplayDialog("Build MegaDance UI", BuildUI(), "OK");
        }

        // The actual work, with no modal dialog so it can be driven from automation. Returns a status string.
        public static string BuildUI()
        {
            var manager = Object.FindAnyObjectByType<MegaDanceManager>();
            if (manager == null) return "No MegaDanceManager in the open scene. Open MegaDanceScene first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var black = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BlackPath);
            var italic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ItalicPath);
            var semi = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiPath);
            var backSprite = LoadSprite(BackSpritePath);
            var startSprite = LoadSprite(StartSpritePath);
            var cardSprite = LoadSprite(CardSpritePath);

            var so = new SerializedObject(manager);

            // ---- Resolve / create the five panels (reuse existing refs to avoid duplicates). ----
            var startPanel = EnsurePanel(so, "startPanel", "StartPanel", canvas.transform);
            var firstPose  = EnsurePanel(so, "poseInstructionPanel", "FirstPosePanel", canvas.transform);
            var hud        = EnsurePanel(so, "hudPanel", "HUDPanel", canvas.transform);
            var correct    = EnsurePanel(so, "correctOverlay", "CorrectOverlay", canvas.transform);
            var results    = EnsurePanel(so, "resultsPanel", "ResultsPanel", canvas.transform);

            ClearChildren(startPanel); ClearChildren(firstPose); ClearChildren(hud);
            ClearChildren(correct); ClearChildren(results);

            // Reused panels keep their old Image color — force the see-through screens transparent
            // so the live 3D room shows behind them (Correct/Results scrims are set later).
            var clear = new Color(0, 0, 0, 0);
            startPanel.GetComponent<Image>().color = clear;
            firstPose.GetComponent<Image>().color = clear;
            hud.GetComponent<Image>().color = clear;

            // Retire the stray root back button (the 3D scene + avatar is the background). Remove any
            // previous canvas-level gear (it now lives on the Start screen). The CameraFeedPanel is
            // kept and repurposed as the always-on corner preview (see BuildCornerPreview).
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var c = canvas.transform.GetChild(i);
                if (c.name == "BackButton") c.gameObject.SetActive(false);
                if (c.name == "SettingsGear") Object.DestroyImmediate(c.gameObject);
                if (c.name == "DebugNextButton") Object.DestroyImmediate(c.gameObject);
            }

            // Calibration popup (created/refreshed here) + the gear that opens it. Built before the
            // Start screen so the gear button can be wired to the panel's Open().
            var calPanel = BuildCalibration(canvas, manager.gameObject, black, semi);

            // =================== START ===================
            // Live room background shows through (panel is transparent). Back + MEGA DANCE + Start.
            AddBackButton(startPanel.transform, backSprite);
            var megaTitle = AddText(startPanel.transform, "MEGA\nDANCE", italic, 150, FontStyles.Normal,
                                    Color.white, TextAlignmentOptions.Center);
            Place(megaTitle.rectTransform, 80, 300, 767, 380);
            Outline(megaTitle, OutlineGray, 0.25f);
            var startBtn = AddSpriteButton(startPanel.transform, "StartButton", startSprite, manager);
            Place(startBtn, 220, 1180, 487, 195);
            // Gear opens the calibration popup. Tucked under the back button (top-left) so it
            // stays clear of the MEGA DANCE title.
            var gear = AddImage(startPanel.transform, "SettingsGear", LoadSprite(GearSpritePath), Color.white);
            gear.raycastTarget = true;
            Place(gear.rectTransform, 58, 240, 108, 108);
            var gearBtn = gear.gameObject.AddComponent<Button>();
            if (calPanel != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(gearBtn.onClick, calPanel.Open);

            // =================== FIRST POSE ===================
            // White card (border sprite) holds: title, "Do this pose", the target-pose image,
            // and the big get-ready countdown overlay.
            AddBackButton(firstPose.transform, backSprite);
            // Smaller, centred, see-through card so the live 3D room shows both around AND through it
            // — it reads as a popup floating over the same room as the Start/Playing screens (Figma).
            var card = AddImage(firstPose.transform, "Card", cardSprite, new Color(1f, 1f, 1f, 0.82f));
            Place(card.rectTransform, 110, 250, 707, 970);

            // Dark fill so it reads on the card; light outline for a slight pop.
            var fpTitle = AddText(firstPose.transform, "Pose 1", black, 78, FontStyles.Normal,
                                  new Color32(0x2A, 0x2A, 0x2A, 0xFF), TextAlignmentOptions.Center);
            Place(fpTitle.rectTransform, 140, 285, 647, 95);
            Outline(fpTitle, Color.white, 0.12f);

            var doThis = AddText(firstPose.transform, "Do this pose", semi, 44, FontStyles.Normal,
                                 new Color32(0x22, 0x22, 0x22, 0xFF), TextAlignmentOptions.Center);
            Place(doThis.rectTransform, 140, 390, 647, 60);

            var poseImg = AddImage(firstPose.transform, "PoseImage", null, Color.white);
            poseImg.preserveAspect = true;
            Place(poseImg.rectTransform, 175, 460, 577, 730);

            // Green countdown (not white) so it never blends into the white card; dark-green outline
            // keeps it readable over the dark figure too.
            var countdown = AddText(firstPose.transform, "3", black, 300, FontStyles.Normal,
                                    Green, TextAlignmentOptions.Center);
            Place(countdown.rectTransform, 313, 600, 300, 270);
            Outline(countdown, new Color32(0x10, 0x3A, 0x05, 0xFF), 0.3f);

            // =================== PLAYING (HUD) ===================
            // Camera feed shows behind. Top title, bottom percentage strip, thin match bar.
            AddBackButton(hud.transform, backSprite);
            var hudTitle = AddText(hud.transform, "Pose 1", black, 84, FontStyles.Normal,
                                   Color.white, TextAlignmentOptions.Center);
            Place(hudTitle.rectTransform, 213, 95, 500, 120);
            Outline(hudTitle, OutlineGray, 0.22f);

            // Match bar (BG + fill) just above the percentage strip.
            var barBg = AddImage(hud.transform, "MatchBarBG", null, new Color(0, 0, 0, 0.45f));
            Place(barBg.rectTransform, 56, 1210, 814, 36);
            var barFill = AddImage(hud.transform, "MatchBarFill", null, Green);
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFill.fillAmount = 0f;
            var bf = barFill.rectTransform; bf.SetParent(barBg.transform, false);
            bf.anchorMin = Vector2.zero; bf.anchorMax = Vector2.one; bf.offsetMin = Vector2.zero; bf.offsetMax = Vector2.zero;

            var percent = AddText(hud.transform, "0%", black, 96, FontStyles.Normal,
                                  Color.white, TextAlignmentOptions.Center);
            Place(percent.rectTransform, 56, 1270, 814, 120);
            Outline(percent, OutlineGray, 0.22f);

            // =================== CORRECT ===================
            correct.GetComponent<Image>().color = PanelDim; // light scrim over the frozen scene
            AddBackButton(correct.transform, backSprite);
            var corSmall = AddText(correct.transform, "Pose 1", black, 70, FontStyles.Normal,
                                   Color.white, TextAlignmentOptions.Center);
            Place(corSmall.rectTransform, 213, 470, 500, 90);
            Outline(corSmall, OutlineGray, 0.2f);
            var corBig = AddText(correct.transform, "Correct!", italic, 170, FontStyles.Normal,
                                 Green, TextAlignmentOptions.Center);
            Place(corBig.rectTransform, 60, 580, 807, 240);
            Outline(corBig, Color.white, 0.35f);

            // =================== RESULTS ===================
            results.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
            var resTitle = AddText(results.transform, "Complete!", italic, 150, FontStyles.Normal,
                                   Color.white, TextAlignmentOptions.Center);
            Place(resTitle.rectTransform, 60, 520, 807, 220);
            Outline(resTitle, OutlineGray, 0.25f);
            var retryBtn = AddSpriteButton(results.transform, "RetryButton", startSprite, manager);
            Place(retryBtn, 220, 900, 487, 195);

            // ---- Wire every manager field. ----
            Assign(so, "poseImage", poseImg);
            Assign(so, "countdownText", countdown);
            Assign(so, "poseNameText", fpTitle);
            Assign(so, "poseCounterText", hudTitle);
            Assign(so, "percentText", percent);
            Assign(so, "matchBarFill", barFill);
            Assign(so, "correctPoseNameText", corSmall);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Always-on small camera+skeleton preview in the top-right corner (on top of everything).
            BuildCornerPreview(canvas);

            // TEMP debug button (bottom-left): skip straight to the next pose for testing. Remove later.
            // Hidden by default; MegaDanceManager only shows it while a game is in progress.
            var dbg = AddImage(canvas.transform, "DebugNextButton", null, new Color(0.12f, 0.12f, 0.12f, 0.75f));
            dbg.raycastTarget = true;
            Place(dbg.rectTransform, 45, 1295, 250, 95);
            var dbgBtn = dbg.gameObject.AddComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(dbgBtn.onClick, manager.SkipPose);
            var dbgLbl = AddText(dbg.transform, "NEXT ▶", semi, 36, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            Stretch(dbgLbl.rectTransform);
            var dso = new SerializedObject(manager);
            dso.FindProperty("debugNextButton").objectReferenceValue = dbg.gameObject;
            dso.ApplyModifiedPropertiesWithoutUndo();
            dbg.gameObject.SetActive(false);

            // Default visible panel = Start; others off.
            startPanel.SetActive(true); firstPose.SetActive(false);
            hud.SetActive(false); correct.SetActive(false); results.SetActive(false);

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Debug.Log("[MegaDanceUIBuilder] UI rebuilt and wired. Save the scene (Ctrl+S).");
            return "UI rebuilt and wired to MegaDanceManager. Press Play to test, then save the scene.";
        }

        // Builds (or refreshes) the calibration popup overlay and the CalibrationPanel controller,
        // returning the controller so the Start-screen gear can be wired to its Open().
        static CalibrationPanel BuildCalibration(Canvas canvas, GameObject managerGo,
                                                 TMP_FontAsset black, TMP_FontAsset semi)
        {
            var old = canvas.transform.Find("CalibrationOverlay");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var detector = Object.FindAnyObjectByType<MediaPipePoseDetector>();

            // Full-screen dim overlay (hidden until the gear is tapped). All children use frame coords.
            var ov = AddImage(canvas.transform, "CalibrationOverlay", null, new Color(0, 0, 0, 0.65f));
            ov.raycastTarget = true;
            Stretch(ov.rectTransform);

            var card = AddImage(ov.transform, "Card", null, Color.white);
            Place(card.rectTransform, 93, 400, 741, 556);

            var title = AddText(ov.transform, "CALIBRATION", black, 58, FontStyles.Normal,
                                new Color32(0x2A, 0x2A, 0x2A, 0xFF), TextAlignmentOptions.Center);
            Place(title.rectTransform, 110, 430, 707, 80);

            var status = AddText(ov.transform, "Stand in a T-pose, then press Calibrate.", semi, 34,
                                 FontStyles.Normal, new Color32(0x33, 0x33, 0x33, 0xFF), TextAlignmentOptions.Center);
            Place(status.rectTransform, 140, 540, 661, 120);

            var count = AddText(ov.transform, "", black, 150, FontStyles.Normal, Green, TextAlignmentOptions.Center);
            Place(count.rectTransform, 313, 610, 300, 200);

            var calBtn = AddImage(ov.transform, "CalibrateBtn", null, Green);
            calBtn.raycastTarget = true; Place(calBtn.rectTransform, 150, 850, 290, 88);
            var calBtnC = calBtn.gameObject.AddComponent<Button>();
            var calLbl = AddText(calBtn.transform, "Calibrate", semi, 34, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            Stretch(calLbl.rectTransform);

            var closeBtn = AddImage(ov.transform, "CloseBtn", null, new Color32(0xCC, 0x33, 0x33, 0xFF));
            closeBtn.raycastTarget = true; Place(closeBtn.rectTransform, 487, 850, 290, 88);
            var closeBtnC = closeBtn.gameObject.AddComponent<Button>();
            var closeLbl = AddText(closeBtn.transform, "Close", semi, 34, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            Stretch(closeLbl.rectTransform);

            var cp = managerGo.GetComponent<CalibrationPanel>();
            if (cp == null) cp = managerGo.AddComponent<CalibrationPanel>();
            var cso = new SerializedObject(cp);
            cso.FindProperty("detector").objectReferenceValue = detector;
            cso.FindProperty("panelRoot").objectReferenceValue = ov.gameObject;
            cso.FindProperty("statusText").objectReferenceValue = status;
            cso.FindProperty("countdownText").objectReferenceValue = count;
            cso.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(calBtnC.onClick, cp.Calibrate);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(closeBtnC.onClick, cp.Close);

            ov.gameObject.SetActive(false);
            return cp;
        }

        // Repurposes the scene's webcam feed (CameraFeedPanel > CameraFeedImage + PoseSkeletonOverlay)
        // as a small always-on preview pinned to the top-right corner, rendered on top of everything
        // (incl. the calibration dim) so the user can always check their framing/detection.
        static void BuildCornerPreview(Canvas canvas)
        {
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed == null) return; // no webcam feed object in this scene

            feed.SetParent(canvas.transform, false);
            feed.gameObject.SetActive(true);
            feed.SetAsLastSibling();                 // draw above the panels + calibration overlay
            Place((RectTransform)feed, 648, 55, 234, 300); // small, top-right corner

            var img = feed.GetComponent<Image>();
            if (img != null) img.color = Color.black;     // dark frame behind the feed

            var raw = feed.GetComponentInChildren<RawImage>(true);
            if (raw != null)
            {
                raw.gameObject.SetActive(true);
                var rt = raw.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(6, 6); rt.offsetMax = new Vector2(-6, -6); // small inset border
            }
        }

        // ---- helpers ----------------------------------------------------------------

        static Sprite LoadSprite(string path)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) Debug.LogWarning($"[MegaDanceUIBuilder] Sprite not found (import it as Sprite): {path}");
            return s;
        }

        static GameObject EnsurePanel(SerializedObject so, string field, string name, Transform parent)
        {
            var prop = so.FindProperty(field);
            var existing = prop.objectReferenceValue as GameObject;

            // Reuse only if it's a real container: has a RectTransform and either no Graphic or an
            // Image. A GameObject that already carries a Text Graphic can't also take an Image
            // (Unity allows one Graphic per object), so we build a fresh panel and retire the old one.
            var existingGraphic = existing != null ? existing.GetComponent<Graphic>() : null;
            bool reuse = existing != null && existing.GetComponent<RectTransform>() != null
                         && (existingGraphic == null || existingGraphic is Image);

            GameObject go;
            if (reuse) { go = existing; }
            else
            {
                go = new GameObject(name);
                go.AddComponent<RectTransform>();
                go.transform.SetParent(parent, false);
                Stretch(go.GetComponent<RectTransform>());
                if (existing != null) existing.SetActive(false); // hide the object we replaced
                prop.objectReferenceValue = go;
            }
            if (go.GetComponent<Image>() == null)
                go.AddComponent<Image>().color = new Color(0, 0, 0, 0); // transparent full-screen
            // Always full-screen so child fractional anchors map to screen fractions (reused
            // panels may have been a small rect in the original scene).
            Stretch(go.GetComponent<RectTransform>());
            return go;
        }

        static void ClearChildren(GameObject go)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i).gameObject;
                // Never destroy the live camera feed / skeleton overlay subtree.
                if (child.GetComponentInChildren<RawImage>(true) != null) continue;
                Object.DestroyImmediate(child);
            }
        }

        static void AddBackButton(Transform parent, Sprite sprite)
        {
            var img = AddImage(parent, "BackButton", sprite, Color.white);
            img.gameObject.AddComponent<Button>();
            Place(img.rectTransform, 45, 62, 162, 162); // matches Figma Group 37
            // (No onClick wired — navigation back to Flutter/menu is handled elsewhere.)
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

        static RectTransform AddSpriteButton(Transform parent, string name, Sprite sprite, MegaDanceManager manager)
        {
            var img = AddImage(parent, name, sprite, Color.white);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            // Wire onClick → StartGame (persistent listener so it survives play/scene save).
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, manager.StartGame);
            return img.rectTransform;
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
            // TMP outline lives on a material instance — set per-text so others aren't affected.
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
        }
    }
}
#endif
