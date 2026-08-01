#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Kinex.MotionLab
{
    /// <summary>
    /// One-click builder for the Motion Lab UI — same procedural-canvas + direct-field-wiring
    /// pattern as MirrorGameUIBuilder (MotionLabDirector's fields are public, so no SerializedObject
    /// layer is needed here). Re-runnable: destroys its own panels by name and rebuilds. A camera
    /// feed panel top-right, a status board (sit/stand chip + facing) top-left, a big toast line, a
    /// calibration ring group, a debug toggle + panel, a full-body framing gate overlay, and the
    /// exit button.
    /// </summary>
    public static class MotionLabUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        static readonly Color GlassBg = new Color(0.07f, 0.045f, 0.15f, 0.82f);
        static readonly Color Lavender = new Color(0.80f, 0.72f, 1f, 1f);
        static readonly Color Amber = new Color(1f, 0.76f, 0.29f, 1f);
        static readonly Color Cyan = new Color(0.45f, 0.92f, 1f, 1f);
        static readonly Color ChipNeutral = new Color(0.35f, 0.32f, 0.48f, 0.85f);
        static readonly Color Violet = new Color(0.42f, 0.29f, 0.62f, 1f);
        static readonly Color ExitRed = new Color(0.88f, 0.35f, 0.31f, 1f);
        static readonly Color RingTrack = new Color(1f, 1f, 1f, 0.18f);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";

        static readonly string[] PanelNames = { "CameraFeedPanel", "HudPanel", "FramingPanel", "ExitButton" };

        [MenuItem("Kinex/Build Motion Lab UI")]
        public static void Build() => Debug.Log("[MotionLabUIBuilder] " + BuildUI());

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<MotionLabDirector>();
            if (director == null) return "No MotionLabDirector in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var thaiBlack = LoadFont(ThaiBlackPath);
            var thaiSemi = LoadFont(ThaiSemiPath);

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i);
                foreach (var n in PanelNames)
                    if (child.name == n) { Object.DestroyImmediate(child.gameObject); break; }
            }

            // =================== CAMERA FEED (top-right) ===================
            var feedPanel = BuildCameraFeed(canvas.transform, director.poseDetector);

            // =================== HUD ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Status board card, top-left. Starts at y=150 so the ExitButton (44..136) sits
            // clear above it instead of covering the title (seen in the round-2 screenshot). --
            AddCard(hud.transform, 24, 150, 330, 230);
            var title = AddText(hud.transform, "ห้องทดลองท่าทาง", thaiSemi, 34, Lavender, TextAlignmentOptions.Left);
            Place(title.rectTransform, 44, 165, 290, 48);

            var chipImg = AddImage(hud.transform, "SitStandChip", null, Violet);
            Round(chipImg);
            Place(chipImg.rectTransform, 44, 223, 150, 64);
            var chipText = AddText(chipImg.transform, "นั่ง", thaiSemi, 32, Color.white, TextAlignmentOptions.Center);
            chipText.fontStyle = FontStyles.Bold;
            Stretch(chipText.rectTransform);

            var facingText = AddText(hud.transform, "หน้าตรง   ตัว 0°", thaiSemi, 36, Color.white, TextAlignmentOptions.Left);
            Place(facingText.rectTransform, 44, 299, 290, 48);

            // -- Big toast, centered. --
            var toastText = AddText(hud.transform, "", thaiBlack, 64, Color.white, TextAlignmentOptions.Center);
            toastText.fontStyle = FontStyles.Bold;
            Place(toastText.rectTransform, 64, 940, 799, 90);
            Outline(toastText, new Color(0f, 0f, 0f, 0.6f), 0.25f);

            // -- Calibration group, centered, initially inactive. --
            var calibGroup = NewPanel(hud.transform, "CalibGroup");
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var calibTrack = AddImage(calibGroup.transform, "CalibRingTrack", knob, RingTrack);
            Place(calibTrack.rectTransform, 353, 640, 220, 220);
            var calibRing = AddImage(calibGroup.transform, "CalibRingFill", knob, Cyan);
            calibRing.type = Image.Type.Filled;
            calibRing.fillMethod = Image.FillMethod.Radial360;
            calibRing.fillOrigin = (int)Image.Origin360.Top;
            calibRing.fillClockwise = true;
            calibRing.fillAmount = 0f;
            Place(calibRing.rectTransform, 353, 640, 220, 220);
            var calibText = AddText(calibGroup.transform, "ยืนตรง นิ่ง ๆ 2 วินาที", thaiSemi, 36, Color.white, TextAlignmentOptions.Center);
            Place(calibText.rectTransform, 114, 870, 700, 60);
            calibGroup.SetActive(false);

            // -- Details toggle button, bottom-left. Wider + smaller label so "รายละเอียด"
            // stays on one line (it wrapped at 160px/36px in the round-2 screenshot). --
            var debugBtn = AddPillButton(hud.transform, "DebugToggleButton", "รายละเอียด", thaiSemi, GlassBg, 24, 1300, 200, 64, labelSize: 28);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(debugBtn.onClick, director.OnDebugTogglePressed);

            // -- Debug panel, above the button, initially inactive. --
            var debugPanel = AddCard(hud.transform, 24, 1080, 500, 220);
            debugPanel.name = "DebugPanel";
            var debugText = AddText(debugPanel.transform, "", thaiSemi, 26, new Color(0.78f, 0.95f, 0.85f), TextAlignmentOptions.TopLeft);
            // Child of the card, so anchors are card-relative: full stretch with a 16px inset
            // (Place() maths is canvas-relative and would misplace it here).
            Stretch(debugText.rectTransform);
            debugText.rectTransform.offsetMin = new Vector2(16f, 16f);
            debugText.rectTransform.offsetMax = new Vector2(-16f, -16f);
            debugPanel.SetActive(false);

            // =================== FRAMING GATE ===================
            var framing = NewPanel(canvas.transform, "FramingPanel");
            framing.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            // Title kept left of the camera feed panel (x>=627): centered in the 0..600 band so
            // its tail doesn't disappear under the feed (round-2 screenshot cut it off).
            var framingTitle = AddText(framing.transform, "จัดตัวให้อยู่ในกรอบ", thaiSemi, 42, Color.white, TextAlignmentOptions.Center);
            Place(framingTitle.rectTransform, 10, 155, 590, 90);

            BuildOutlineFrame(framing.transform, 249, 230, 430, 860, 4f, Cyan);

            // Hold ring near the frame's top-right corner but clear of the camera feed panel
            // (627..907 x 55..415 — the old 660,200 spot was fully hidden behind it).
            var framingHoldRingTrack = AddImage(framing.transform, "FramingHoldRingTrack", knob, RingTrack);
            Place(framingHoldRingTrack.rectTransform, 490, 250, 120, 120);
            var framingHoldRing = AddImage(framing.transform, "FramingHoldRingFill", knob, Cyan);
            framingHoldRing.type = Image.Type.Filled;
            framingHoldRing.fillMethod = Image.FillMethod.Radial360;
            framingHoldRing.fillOrigin = (int)Image.Origin360.Top;
            framingHoldRing.fillClockwise = true;
            framingHoldRing.fillAmount = 0f;
            Place(framingHoldRing.rectTransform, 490, 250, 120, 120);

            var framingPromptText = AddText(framing.transform, "ยังไม่เห็นตัวคุณ — มายืนหน้ากล้องได้เลย",
                                            thaiSemi, 40, Amber, TextAlignmentOptions.Center);
            Place(framingPromptText.rectTransform, 64, 1130, 799, 70);

            string[] chipLabels = { "หัว", "ไหล่", "สะโพก", "เข่า", "เท้า" };
            var framingChips = new Image[5];
            const float chipW = 150, chipH = 58, chipGap = 14;
            float chipsTotalW = chipLabels.Length * chipW + (chipLabels.Length - 1) * chipGap;
            float chipStartX = (FW - chipsTotalW) * 0.5f;
            for (int i = 0; i < chipLabels.Length; i++)
            {
                var chip = AddImage(framing.transform, $"FramingChip{i}", null, ChipNeutral);
                Round(chip);
                float x = chipStartX + i * (chipW + chipGap);
                Place(chip.rectTransform, x, 1230, chipW, chipH);
                var chipLabel = AddText(chip.transform, chipLabels[i], thaiSemi, 28, Color.white, TextAlignmentOptions.Center);
                Stretch(chipLabel.rectTransform);
                framingChips[i] = chip;
            }

            // =================== EXIT (all states) ===================
            var exitBtn = AddPillButton(canvas.transform, "ExitButton", "ออก", thaiSemi, ExitRed, 40, 44, 150, 92);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitBtn.onClick, director.OnExitPressed);
            exitBtn.transform.SetAsLastSibling();
            feedPanel.transform.SetAsLastSibling();

            // ---- Wire the director (public fields — direct assignment). ----
            director.framingPanel = framing;
            director.hudPanel = hud;
            director.framingPromptText = framingPromptText;
            director.framingChips = framingChips;
            director.framingHoldRing = framingHoldRing;
            director.calibGroup = calibGroup;
            director.calibText = calibText;
            director.calibRing = calibRing;
            director.sitStandText = chipText;
            director.sitStandChip = chipImg;
            director.facingText = facingText;
            director.toastText = toastText;
            director.debugPanel = debugPanel;
            director.debugText = debugText;

            toastText.text = "";

            hud.SetActive(true);
            framing.SetActive(true);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Motion Lab UI rebuilt and wired to MotionLabDirector.";
        }

        // ---- camera feed (top-right) ----
        static GameObject BuildCameraFeed(Transform canvas, MediaPipePoseDetector detector)
        {
            var panelGo = new GameObject("CameraFeedPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var panelRt = (RectTransform)panelGo.transform;
            panelRt.SetParent(canvas, false);
            Place(panelRt, 627, 55, 280, 360);
            panelGo.GetComponent<Image>().color = Color.black;

            var rawGo = new GameObject("CameraFeedImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rawRt = (RectTransform)rawGo.transform;
            rawRt.SetParent(panelRt, false);
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(6f, 6f);
            rawRt.offsetMax = new Vector2(-6f, -6f);

            var overlayGo = new GameObject("PoseSkeletonOverlay", typeof(RectTransform), typeof(CanvasRenderer));
            var overlayRt = (RectTransform)overlayGo.transform;
            overlayRt.SetParent(rawRt, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            var overlay = overlayGo.AddComponent<PoseSkeletonOverlay>();
            overlay.source = detector;
            overlay.confidenceColors = true;

            return panelGo;
        }

        // 4 thin bars forming a rounded-look rectangle outline — no torus/sliced-transparent sprite
        // needed for a simple framing guide.
        static void BuildOutlineFrame(Transform parent, float x, float y, float w, float h, float thickness, Color color)
        {
            var top = AddImage(parent, "FrameTop", null, color);
            Place(top.rectTransform, x, y, w, thickness);
            var bottom = AddImage(parent, "FrameBottom", null, color);
            Place(bottom.rectTransform, x, y + h - thickness, w, thickness);
            var left = AddImage(parent, "FrameLeft", null, color);
            Place(left.rectTransform, x, y, thickness, h);
            var right = AddImage(parent, "FrameRight", null, color);
            Place(right.rectTransform, x + w - thickness, y, thickness, h);
        }

        // ---- helpers (MirrorGameUIBuilder pattern) ----

        static TMP_FontAsset LoadFont(string path)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) Debug.LogWarning($"[MotionLabUIBuilder] Font not found: {path} (using TMP default)");
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
            var img = AddImage(parent, "Card", null, GlassBg);
            Round(img);
            Place(img.rectTransform, x, y, w, h);
            return img.gameObject;
        }

        static Button AddPillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                    Color bg, float x, float y, float w, float h, float labelSize = 36)
        {
            var img = AddImage(parent, name, null, bg);
            Round(img);
            img.raycastTarget = true;
            Place(img.rectTransform, x, y, w, h);
            var btn = img.gameObject.AddComponent<Button>();
            var text = AddText(img.transform, label, font, labelSize, Color.white, TextAlignmentOptions.Center);
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
    }
}
#endif
