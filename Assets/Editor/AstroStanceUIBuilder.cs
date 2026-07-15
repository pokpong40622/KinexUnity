#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.AstroStance;

namespace Kinex.AstroStance.EditorTools
{
    /// <summary>
    /// One-click builder for the AstroStance UI — same procedural-canvas + SerializedObject-wiring
    /// pattern as TempleHuntUIBuilder. Re-runnable: destroys its own panels by name and rebuilds,
    /// then wires every AstroStanceDirector field. Bright cream glass cards with DARK ink text
    /// (the 3D scene behind is now a bright sunny park — dark cards would vanish into the shade,
    /// so the cards stay light and the text goes dark instead).
    /// CalibGroup is deliberately a CANVAS-level sibling of HudPanel, not nested inside it: the
    /// director activates calibGroup during the Calibrating state, which is BEFORE hudPanel turns
    /// on (that only happens at EnterCountdown) — nesting it under HudPanel would hide it.
    /// </summary>
    public static class AstroStanceUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        // Palette (matches AstroStanceSceneBuilder / AstroProps where it overlaps).
        // SpaceNavy was the near-black space bg; it's now the OPAQUE bright panel background
        // (mint-cream) used for the full-screen Intro / Results / Pause panels so they fully
        // cover the sunny park scene behind them.
        static readonly Color SpaceNavy = new Color(0.94f, 0.97f, 0.90f);         // #F0F7E6
        // Indigo was a dark accent circle behind the intro star; now a soft leaf-green accent.
        static readonly Color Indigo = new Color(0.55f, 0.72f, 0.40f);            // #8CB866
        static readonly Color Cyan = new Color(0.28f, 0.62f, 0.92f);              // sky-blue secondary accent
        static readonly Color Gold = new Color(1.00f, 0.76f, 0.20f);              // sunny gold
        static readonly Color MeteorOrange = new Color(0.95f, 0.45f, 0.34f);      // warm coral hit/dodge warning
        static readonly Color Green = new Color(0.32f, 0.70f, 0.36f);             // friendly green
        static readonly Color OffWhite = new Color(0.14f, 0.20f, 0.12f);          // dark ink (name kept, now the primary TEXT color)
        static readonly Color IndigoLight = new Color(0.32f, 0.20f, 0.48f);       // deep plum — readable as text on cream, still a distinct accent
        static readonly Color Gray = new Color(0.45f, 0.48f, 0.55f);
        // Cards must read against a bright sunny-park stage — cream fill with a soft leaf-green
        // rim (AddCard) so every card still has a visible edge, just like the old dark-glass cards did.
        static readonly Color GlassBg = new Color(0.99f, 0.99f, 0.96f, 0.95f);
        static readonly Color CardRim = new Color(0.42f, 0.62f, 0.32f, 0.55f);
        // Gentle warm-dark translucent overlays used to dim the (bright) scene behind the
        // Framing / Calibration guidance panels for focus — not opaque, so the camera feed /
        // park scene still shows through, just dimmed.
        static readonly Color ScrimDim = new Color(0.15f, 0.20f, 0.12f, 0.45f);
        static readonly Color ScrimHeavy = new Color(0.15f, 0.20f, 0.12f, 0.65f);
        static readonly Color RingTrack = new Color(0.5f, 0.5f, 0.5f, 0.30f);
        static readonly Color DarkText = new Color(0.14f, 0.20f, 0.12f);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";
        const string MontserratBlackPath = "Assets/Fonts/Montserrat-Black SDF.asset";

        static readonly string[] PanelNames =
        {
            "IntroPanel", "FramingPanel", "HudPanel", "CalibGroup", "PausePanel",
            "ResultsPanel", "CameraFeedPanel", "PreviewToggleButton",
        };

        [MenuItem("Kinex/Build AstroStance UI")]
        public static void Build() => Debug.Log("[AstroStanceUIBuilder] " + BuildUI());

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<AstroStanceDirector>();
            if (director == null) return "No AstroStanceDirector in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var thaiBlack = LoadFont(ThaiBlackPath);
            var thaiSemi = LoadFont(ThaiSemiPath);
            var montserratBlack = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MontserratBlackPath);
            if (montserratBlack == null) montserratBlack = thaiBlack;
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i);
                foreach (var n in PanelNames)
                    if (child.name == n) { Object.DestroyImmediate(child.gameObject); break; }
            }

            // =================== INTRO ===================
            var intro = NewPanel(canvas.transform, "IntroPanel");
            intro.GetComponent<Image>().color = SpaceNavy;

            var ringAccent = AddImage(intro.transform, "TitleRingAccent", knob, new Color(Indigo.r, Indigo.g, Indigo.b, 0.55f));
            Place(ringAccent.rectTransform, 700, 8, 150, 150);
            var starAccent = AddImage(intro.transform, "TitleStarAccent", knob, Gold);
            Place(starAccent.rectTransform, 748, 34, 62, 62);

            var titleTop = AddText(intro.transform, "ASTRO", montserratBlack, 100, OffWhite, TextAlignmentOptions.Center);
            Place(titleTop.rectTransform, 0, 58, FW, 118);
            Outline(titleTop, Cyan, 0.2f);
            var titleBottom = AddText(intro.transform, "STANCE", montserratBlack, 100, OffWhite, TextAlignmentOptions.Center);
            Place(titleBottom.rectTransform, 0, 172, FW, 118);
            Outline(titleBottom, Cyan, 0.2f);

            var subtitle = AddText(intro.transform, "ภารกิจเก็บสมบัติในสวน", thaiSemi, 44, DarkText, TextAlignmentOptions.Center);
            Place(subtitle.rectTransform, 64, 305, 799, 70);

            BuildHowToCards(intro.transform, thaiSemi, knob);

            var startBtn = AddPillButton(intro.transform, "StartButton", "เริ่มภารกิจ", thaiSemi, Cyan, Color.white, 213, 1250, 500, 120, 56);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(startBtn.onClick, director.OnStartPressed);

            // =================== FRAMING ===================
            var framing = NewPanel(canvas.transform, "FramingPanel");
            framing.GetComponent<Image>().color = ScrimHeavy;

            AddCard(framing.transform, 94, 280, 740, 640);
            var framingTitle = AddText(framing.transform, "ให้เห็นตัวคุณทั้งตัว", thaiSemi, 52, OffWhite, TextAlignmentOptions.Center);
            Place(framingTitle.rectTransform, 114, 320, 700, 90);
            // Prompt sits on the cream card (not the dark FramingPanel margin scrim), so it needs
            // dark ink like the title, not Gold — Gold-on-cream is unreadable.
            var framingPromptText = AddText(framing.transform, "ยังไม่เห็นตัวคุณ — มายืนหน้ากล้องได้เลย",
                                            thaiSemi, 44, DarkText, TextAlignmentOptions.Center);
            Place(framingPromptText.rectTransform, 114, 430, 700, 90);

            string[] chipLabels = { "หัว", "ไหล่", "สะโพก", "เข่า", "เท้า" };
            var framingChips = new Image[5];
            const float chipSize = 64f, chipGap = 24f;
            float chipsTotalW = chipLabels.Length * chipSize + (chipLabels.Length - 1) * chipGap;
            float chipStartX = (FW - chipsTotalW) * 0.5f;
            for (int i = 0; i < chipLabels.Length; i++)
            {
                float cx = chipStartX + i * (chipSize + chipGap);
                var chip = AddImage(framing.transform, $"FramingChip{i}", knob, new Color(1f, 1f, 1f, 0.25f));
                Place(chip.rectTransform, cx, 540, chipSize, chipSize);
                var chipLabel = AddText(framing.transform, chipLabels[i], thaiSemi, 30, DarkText, TextAlignmentOptions.Center);
                Place(chipLabel.rectTransform, cx - 13, 608, chipSize + 26, 40);
                framingChips[i] = chip;
            }

            var framingHoldTrack = AddImage(framing.transform, "FramingHoldRingTrack", knob, RingTrack);
            Place(framingHoldTrack.rectTransform, 404, 680, 120, 120);
            var framingHoldRing = AddImage(framing.transform, "FramingHoldRingFill", knob, Cyan);
            framingHoldRing.type = Image.Type.Filled;
            framingHoldRing.fillMethod = Image.FillMethod.Radial360;
            framingHoldRing.fillOrigin = (int)Image.Origin360.Top;
            framingHoldRing.fillClockwise = true;
            framingHoldRing.fillAmount = 0f;
            Place(framingHoldRing.rectTransform, 404, 680, 120, 120);

            // =================== CALIBRATION (canvas-level sibling — see class doc) ===================
            var calibGroup = NewPanel(canvas.transform, "CalibGroup");
            calibGroup.GetComponent<Image>().color = ScrimDim;
            var calibTrack = AddImage(calibGroup.transform, "CalibRingTrack", knob, RingTrack);
            Place(calibTrack.rectTransform, 383, 560, 160, 160);
            var calibRing = AddImage(calibGroup.transform, "CalibRingFill", knob, Cyan);
            calibRing.type = Image.Type.Filled;
            calibRing.fillMethod = Image.FillMethod.Radial360;
            calibRing.fillOrigin = (int)Image.Origin360.Top;
            calibRing.fillClockwise = true;
            calibRing.fillAmount = 0f;
            Place(calibRing.rectTransform, 383, 560, 160, 160);
            // CalibGroup has no card behind it — it sits directly on the dark ScrimDim overlay,
            // so this stays white (not dark ink) for contrast.
            var calibText = AddText(calibGroup.transform, "ยืนตรง นิ่ง ๆ 2 วินาที", thaiSemi, 48, Color.white, TextAlignmentOptions.Center);
            Place(calibText.rectTransform, 114, 740, 700, 70);

            // =================== HUD ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Score, top-left. --
            AddCard(hud.transform, 36, 36, 240, 110);
            var scoreStar = AddImage(hud.transform, "ScoreStar", knob, Gold);
            Place(scoreStar.rectTransform, 56, 58, 56, 56);
            var scoreText = AddText(hud.transform, "0", montserratBlack, 64, OffWhite, TextAlignmentOptions.Left);
            Place(scoreText.rectTransform, 122, 52, 140, 82);

            // -- Timer, top-center. --
            AddCard(hud.transform, 343, 36, 240, 110);
            var timerRingTrack = AddImage(hud.transform, "TimerRingTrack", knob, RingTrack);
            Place(timerRingTrack.rectTransform, 403, 46, 90, 90);
            var timerRing = AddImage(hud.transform, "TimerRingFill", knob, Cyan);
            timerRing.type = Image.Type.Filled;
            timerRing.fillMethod = Image.FillMethod.Radial360;
            timerRing.fillOrigin = (int)Image.Origin360.Top;
            timerRing.fillClockwise = false; // counts DOWN
            timerRing.fillAmount = 1f;
            Place(timerRing.rectTransform, 403, 46, 90, 90);
            var timerText = AddText(hud.transform, "3:00", thaiSemi, 56, OffWhite, TextAlignmentOptions.Center);
            Place(timerText.rectTransform, 343, 52, 240, 82);
            Outline(timerText, new Color(0f, 0f, 0f, 0.6f), 0.2f);

            // -- Pause round button, top-right. --
            var pauseBtnImg = AddImage(hud.transform, "PauseButton", knob, GlassBg);
            pauseBtnImg.raycastTarget = true;
            Place(pauseBtnImg.rectTransform, 795, 36, 96, 96);
            var pauseBtn = pauseBtnImg.gameObject.AddComponent<Button>();
            var pauseGlyph = AddText(pauseBtnImg.transform, "II", thaiSemi, 40, OffWhite, TextAlignmentOptions.Center);
            Stretch(pauseGlyph.rectTransform);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(pauseBtn.onClick, director.OnPausePressed);

            // -- Lane dots, centered bottom. --
            var laneDots = new Image[3];
            const float dotSize = 36f, dotSpacing = 70f;
            float dotsCenterX = FW * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                float cx = dotsCenterX + (i - 1) * dotSpacing - dotSize * 0.5f;
                var dot = AddImage(hud.transform, $"LaneDot{i}", knob, new Color(1f, 1f, 1f, 0.25f));
                Place(dot.rectTransform, cx, 1290, dotSize, dotSize);
                laneDots[i] = dot;
            }

            // -- Toast, centered. --
            var toastText = AddText(hud.transform, "", thaiBlack, 64, Gold, TextAlignmentOptions.Center);
            toastText.fontStyle = FontStyles.Bold;
            toastText.raycastTarget = false;
            Place(toastText.rectTransform, 64, 500, 799, 90);
            Outline(toastText, new Color(0f, 0f, 0f, 0.6f), 0.25f);

            // -- Countdown, huge center, initially inactive. --
            var countdownText = AddText(hud.transform, "3", montserratBlack, 200, OffWhite, TextAlignmentOptions.Center);
            Place(countdownText.rectTransform, 0, 560, FW, 300);
            Outline(countdownText, Cyan, 0.25f);
            countdownText.gameObject.SetActive(false);

            // =================== CAMERA FEED + PREVIEW TOGGLE (canvas-level, siblings) ===================
            var feedPanel = BuildCameraFeed(canvas.transform, director.poseDetector);
            var previewToggleBtn = AddRoundButton(canvas.transform, "PreviewToggleButton", GlassBg, 24, 1000, 60);
            var chevron = AddText(previewToggleBtn.transform, "^", thaiSemi, 32, OffWhite, TextAlignmentOptions.Center);
            Stretch(chevron.rectTransform);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(previewToggleBtn.onClick, director.OnTogglePreviewPressed);

            // =================== PAUSE ===================
            var pause = NewPanel(canvas.transform, "PausePanel");
            pause.GetComponent<Image>().color = SpaceNavy;
            AddCard(pause.transform, 94, 480, 740, 400);
            var pauseTitle = AddText(pause.transform, "พักเกม", thaiBlack, 64, OffWhite, TextAlignmentOptions.Center);
            Place(pauseTitle.rectTransform, 114, 520, 700, 110);
            var resumeBtn = AddPillButton(pause.transform, "ResumeButton", "เล่นต่อ", thaiSemi, Green, Color.white, 197, 660, 260, 110, 42);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(resumeBtn.onClick, director.OnResumePressed);
            var exitFromPauseBtn = AddPillButton(pause.transform, "ExitFromPauseButton", "ออกจากเกม", thaiSemi, Gray, Color.white, 470, 660, 260, 110, 42);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitFromPauseBtn.onClick, director.OnExitPressed);

            // =================== RESULTS ===================
            var results = NewPanel(canvas.transform, "ResultsPanel");
            results.GetComponent<Image>().color = SpaceNavy;
            AddCard(results.transform, 54, 110, 819, 1210);
            var resultsTitle = AddText(results.transform, "ภารกิจสำเร็จ!", thaiBlack, 72, DarkText, TextAlignmentOptions.Center);
            Place(resultsTitle.rectTransform, 94, 150, 740, 110);
            Outline(resultsTitle, new Color(0f, 0f, 0f, 0.6f), 0.2f);

            var resultScoreText = AddText(results.transform, "0", montserratBlack, 140, DarkText, TextAlignmentOptions.Center);
            Place(resultScoreText.rectTransform, 94, 280, 740, 170);
            var scoreLabel = AddText(results.transform, "คะแนน", thaiSemi, 40, OffWhite, TextAlignmentOptions.Center);
            Place(scoreLabel.rectTransform, 94, 460, 740, 60);

            var resultStars = new Image[3];
            const float starSize = 96f, starGap = 24f;
            float starsTotalW = 3 * starSize + 2 * starGap;
            float starStartX = (FW - starsTotalW) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                var star = AddImage(results.transform, $"ResultStar{i}", knob, new Color(0.14f, 0.20f, 0.12f, 0.18f));
                Place(star.rectTransform, starStartX + i * (starSize + starGap), 540, starSize, starSize);
                resultStars[i] = star;
            }

            // Rows double as the icon fill AND the value-text color, so each tone is darkened
            // enough to stay readable as text on the cream results card (plain Gold/Cyan/Green
            // read fine as icon fills but wash out as text on cream).
            (string label, Color color)[] repRows =
            {
                ("สมบัติที่เก็บ", new Color(0.68f, 0.46f, 0.04f)),   // deep amber (was Gold)
                ("เตะโดน", new Color(0.08f, 0.32f, 0.62f)),          // deep sky-blue (was Cyan)
                ("หลบก้อนหิน", new Color(0.10f, 0.40f, 0.16f)),      // deep green (was Green)
                ("ลุก-นั่ง", OffWhite),
                ("ก้าวข้าง", IndigoLight),
            };
            var resultRepValues = new TMP_Text[5];
            for (int i = 0; i < repRows.Length; i++)
            {
                float ry = 700 + i * 84;
                var icon = AddImage(results.transform, $"RepIcon{i}", knob, repRows[i].color);
                Place(icon.rectTransform, 110, ry, 44, 44);
                var label = AddText(results.transform, repRows[i].label, thaiSemi, 38, OffWhite, TextAlignmentOptions.Left);
                Place(label.rectTransform, 170, ry - 4, 400, 52);
                var value = AddText(results.transform, "0", thaiSemi, 44, repRows[i].color, TextAlignmentOptions.Right);
                Place(value.rectTransform, 610, ry - 6, 190, 56);
                resultRepValues[i] = value;
            }

            var playAgainBtn = AddPillButton(results.transform, "PlayAgainButton", "เล่นอีกครั้ง", thaiSemi, Cyan, Color.white, 150, 1210, 300, 110, 42);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(playAgainBtn.onClick, director.OnPlayAgainPressed);
            var exitFromResultsBtn = AddPillButton(results.transform, "ExitFromResultsButton", "กลับหน้าหลัก", thaiSemi, Gray, Color.white, 480, 1210, 300, 110, 42);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitFromResultsBtn.onClick, director.OnExitPressed);

            feedPanel.transform.SetAsLastSibling();
            previewToggleBtn.transform.SetAsLastSibling();

            // ---- Wire the director. ----
            var so = new SerializedObject(director);
            AssignGo(so, "introPanel", intro);
            AssignGo(so, "framingPanel", framing);
            AssignGo(so, "hudPanel", hud);
            AssignGo(so, "resultsPanel", results);
            AssignGo(so, "pausePanel", pause);
            AssignGo(so, "cameraPreviewPanel", feedPanel);
            AssignGo(so, "previewToggleButton", previewToggleBtn.gameObject);
            Assign(so, "framingPromptText", framingPromptText);
            Assign(so, "framingHoldRing", framingHoldRing);
            AssignGo(so, "calibGroup", calibGroup);
            Assign(so, "calibText", calibText);
            Assign(so, "calibRing", calibRing);
            Assign(so, "scoreText", scoreText);
            Assign(so, "timerText", timerText);
            Assign(so, "timerRing", timerRing);
            Assign(so, "toastText", toastText);
            Assign(so, "countdownText", countdownText);
            Assign(so, "resultScoreText", resultScoreText);

            AssignArray(so, "framingChips", framingChips);
            AssignArray(so, "laneDots", laneDots);
            AssignArray(so, "resultStars", resultStars);
            AssignArray(so, "resultRepValues", resultRepValues);

            so.ApplyModifiedPropertiesWithoutUndo();

            // ---- Initial active states. ----
            intro.SetActive(true);
            framing.SetActive(false);
            calibGroup.SetActive(false);
            hud.SetActive(false);
            pause.SetActive(false);
            results.SetActive(false);
            feedPanel.SetActive(true); // the detector finds this RawImage at Start
            previewToggleBtn.gameObject.SetActive(true);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "AstroStance UI rebuilt and wired to AstroStanceDirector.";
        }

        // ---- 3 how-to cards: side-step / sit-stand / kick, each a bright cream card with a
        // small pictogram built from primitive shapes + a Thai caption. ----
        static void BuildHowToCards(Transform parent, TMP_FontAsset thaiSemi, Sprite knob)
        {
            const float cardW = 270f, cardH = 330f, cardY = 420f;
            float[] xs = { 29f, 328f, 627f };
            string[] captions =
            {
                "ก้าวข้าง หลบก้อนหิน",
                "นั่งแล้วลุก เก็บสมบัติ",
                "เตะวงแหวนจากเลนข้าง ๆ",
            };

            for (int i = 0; i < 3; i++)
            {
                float x = xs[i];
                // Pictograms + captions are siblings of the card (added after it, so they draw on
                // top): Place() uses CANVAS-space coords, so children of the 270px card would land
                // wrong — anchors are fractions of the PARENT rect.
                AddCard(parent, x, cardY, cardW, cardH);

                switch (i)
                {
                    case 0: // side-step: person dot + left/right arrow bars
                        AddPictoDot(parent, knob, OffWhite, x + 105, cardY + 60, 60);
                        AddPictoBar(parent, Cyan, x + 30, cardY + 88, 60, 14, -20f);
                        AddPictoBar(parent, Cyan, x + 180, cardY + 88, 60, 14, 20f);
                        break;
                    case 1: // sit-stand: chair-ish seat + backrest
                        AddPictoBar(parent, Gold, x + 85, cardY + 130, 100, 20, 0f);
                        AddPictoBar(parent, Gold, x + 85, cardY + 60, 20, 90, 0f);
                        break;
                    default: // kick: leg-line + ring
                        AddPictoBar(parent, OffWhite, x + 165, cardY + 90, 14, 110, 0f);
                        var ringOuter = AddImage(parent, "RingOuter", knob, new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f));
                        Place(ringOuter.rectTransform, x + 40, cardY + 60, 70, 70);
                        var ringInner = AddImage(parent, "RingInner", knob, GlassBg);
                        Place(ringInner.rectTransform, x + 55, cardY + 75, 40, 40);
                        break;
                }

                var caption = AddText(parent, captions[i], thaiSemi, 36, OffWhite, TextAlignmentOptions.Center);
                Place(caption.rectTransform, x + 10, cardY + 190, cardW - 20, 130);
            }
        }

        static void AddPictoDot(Transform parent, Sprite knob, Color color, float x, float y, float size)
        {
            var img = AddImage(parent, "PictoDot", knob, color);
            Place(img.rectTransform, x, y, size, size);
        }

        static void AddPictoBar(Transform parent, Color color, float x, float y, float w, float h, float rotZ)
        {
            var img = AddImage(parent, "PictoBar", null, color);
            Place(img.rectTransform, x, y, w, h);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        }

        // ---- camera feed (bottom-left) ----
        static GameObject BuildCameraFeed(Transform canvas, MediaPipePoseDetector detector)
        {
            var panelGo = new GameObject("CameraFeedPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var panelRt = (RectTransform)panelGo.transform;
            panelRt.SetParent(canvas, false);
            Place(panelRt, 24, 1060, 250, 333);
            var panelImg = panelGo.GetComponent<Image>();
            panelImg.color = GlassBg;
            Round(panelImg);

            var rawGo = new GameObject("CameraFeedImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rawRt = (RectTransform)rawGo.transform;
            rawRt.SetParent(panelRt, false);
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(6f, 6f);
            rawRt.offsetMax = new Vector2(-6f, -6f);
            // Dark placeholder so the feed reads as "camera goes here" before the webcam warms up
            // (RawImage with no texture renders solid white). The detector swaps in the live
            // texture at Start; color stays white so it doesn't tint the feed.
            var raw = rawGo.GetComponent<RawImage>();
            raw.texture = SolidTex(new Color(0.08f, 0.10f, 0.18f));

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

        // ---- helpers (TempleHuntUIBuilder / MotionLabUIBuilder pattern) ----

        static Texture2D SolidTex(Color c)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "AstroFeedPlaceholder" };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static TMP_FontAsset LoadFont(string path)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) Debug.LogWarning($"[AstroStanceUIBuilder] Font not found: {path} (using TMP default)");
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
            // Faint leaf-green rim first (a slightly larger card behind), then the cream fill on top —
            // gives every card a visible edge against the bright park stage without a real 9-slice border.
            var rim = AddImage(parent, "CardRim", null, CardRim);
            Round(rim);
            Place(rim.rectTransform, x - 2f, y - 2f, w + 4f, h + 4f);

            var img = AddImage(parent, "Card", null, GlassBg);
            Round(img);
            var shadow = img.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(0, 6);
            Place(img.rectTransform, x, y, w, h);
            return img.gameObject;
        }

        static Button AddPillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                    Color bg, Color textColor, float x, float y, float w, float h, float labelSize)
        {
            var img = AddImage(parent, name, null, bg);
            Round(img);
            img.raycastTarget = true;
            Place(img.rectTransform, x, y, w, h);
            var btn = img.gameObject.AddComponent<Button>();
            var text = AddText(img.transform, label, font, labelSize, textColor, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);
            return btn;
        }

        static Button AddRoundButton(Transform parent, string name, Color bg, float x, float y, float size)
        {
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var img = AddImage(parent, name, knob, bg);
            img.raycastTarget = true;
            Place(img.rectTransform, x, y, size, size);
            return img.gameObject.AddComponent<Button>();
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
            else Debug.LogWarning($"[AstroStanceUIBuilder] Director field not found: {field}");
        }

        static void AssignGo(SerializedObject so, string field, GameObject value) => Assign(so, field, value);

        static void AssignArray(SerializedObject so, string field, Object[] values)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[AstroStanceUIBuilder] Director field not found: {field}"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
#endif
