#if UNITY_EDITOR
using System.Collections.Generic;
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

        // ---- sprite-asset lookup (docs/astro_ui_assets.md is the source of truth) ----
        // Null-safe: the PNGs are produced by a separate stream and may not exist on disk yet.
        // Every caller must handle a null return by keeping its current procedural look, so the
        // scene still builds today. Warns once per missing name per build run (not once per call
        // site) so a chip used 5x doesn't spam 5 identical warnings.
        static readonly HashSet<string> _warnedMissingSprites = new HashSet<string>();

        static Sprite Ui(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AstroStance/UI/" + name + ".png");
            if (sprite == null && _warnedMissingSprites.Add(name))
                Debug.LogWarning($"[AstroStanceUIBuilder] Sprite not found, using procedural fallback: {name}.png");
            return sprite;
        }

        [MenuItem("Kinex/Build AstroStance UI")]
        public static void Build() => Debug.Log("[AstroStanceUIBuilder] " + BuildUI());

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<AstroStanceDirector>();
            if (director == null) return "No AstroStanceDirector in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";
            _warnedMissingSprites.Clear();

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

            // Full-bleed park background (bright sunny scene, aspect ~0.65 already close to the
            // 927x1427 canvas' 0.65 — a straight stretch reads fine). Added first so every other
            // intro element draws on top of it. Falls back to the plain SpaceNavy fill above if the
            // art isn't built yet.
            var introBg = Ui("astrostancestartpagebackground");
            if (introBg != null)
            {
                var bgImg = AddImage(intro.transform, "Background", introBg, Color.white);
                Stretch(bgImg.rectTransform);
            }

            // Logo replaces the old procedural ring/star + "ASTRO STANCE" text.
            var logo = Ui("astrostancelogo");
            if (logo != null)
            {
                var logoImg = AddImage(intro.transform, "Logo", logo, Color.white);
                Place(logoImg.rectTransform, 250, 40, 428, 210);
            }
            else
            {
                var ringAccent = AddImage(intro.transform, "TitleRingAccent", knob, new Color(Indigo.r, Indigo.g, Indigo.b, 0.55f));
                Place(ringAccent.rectTransform, 700, 8, 150, 150);
                var titleStar = Ui("star_lit");
                var starAccent = AddImage(intro.transform, "TitleStarAccent", titleStar != null ? titleStar : knob,
                                          titleStar != null ? Color.white : Gold);
                Place(starAccent.rectTransform, 744, 30, 70, 70);

                var titleTop = AddText(intro.transform, "ASTRO", montserratBlack, 100, OffWhite, TextAlignmentOptions.Center);
                Place(titleTop.rectTransform, 0, 58, FW, 118);
                Outline(titleTop, Cyan, 0.2f);
                var titleBottom = AddText(intro.transform, "STANCE", montserratBlack, 100, OffWhite, TextAlignmentOptions.Center);
                Place(titleBottom.rectTransform, 0, 172, FW, 118);
                Outline(titleBottom, Cyan, 0.2f);
            }

            // Outline added now that this sits over a photographic background instead of a flat fill.
            var subtitle = AddText(intro.transform, "ภารกิจเก็บสมบัติในสวน", thaiSemi, 44, DarkText, TextAlignmentOptions.Center);
            Place(subtitle.rectTransform, 64, 270, 799, 60);
            Outline(subtitle, Color.white, 0.3f);

            // Star badge (top-right) — matches the reference start page: a gold star on a leaf-green
            // disc. Decorative for now (no handler); raycast off so it never blocks a tap.
            var badgeBg = AddImage(intro.transform, "StarBadge", knob, new Color(0.55f, 0.76f, 0.44f));
            badgeBg.raycastTarget = false;
            Place(badgeBg.rectTransform, 786, 26, 108, 108);
            var badgeStarSprite = Ui("star_lit");
            var badgeStar = AddImage(intro.transform, "StarBadgeStar",
                badgeStarSprite != null ? badgeStarSprite : knob, badgeStarSprite != null ? Color.white : Gold);
            badgeStar.raycastTarget = false;
            Place(badgeStar.rectTransform, 810, 50, 60, 60);

            BuildHowToCards(intro.transform, thaiSemi, knob);

            // ---- Difficulty (ง่าย / ปกติ / ยาก) ----
            // AstroDifficultySelector (Assets/Scripts/AstroStance/AstroDifficultySelector.cs) is a
            // tiny new helper component: UnityEventTools has no persistent-listener overload that can
            // pass an ENUM argument (AddIntPersistentListener needs a UnityAction<int>, and
            // SetDifficulty takes AstroDifficulty — that method-group-to-delegate conversion doesn't
            // compile), so each button below wires to one of the selector's plain void SelectX()
            // methods instead, via the same AddPersistentListener pattern as every other button here.
            var diffLabel = AddText(intro.transform, "เลือกระดับความยาก", thaiSemi, 32, DarkText, TextAlignmentOptions.Center);
            Place(diffLabel.rectTransform, 64, 852, 799, 40);
            Outline(diffLabel, Color.white, 0.3f);

            const float diffW = 220f, diffH = 88f, diffGap = 24f, diffY = 896f;
            float diffMarginX = (FW - (3 * diffW + 2 * diffGap)) * 0.5f;
            var easyBtn = AddPillButton(intro.transform, "DifficultyEasyButton", "ง่าย", thaiSemi, Cyan, Color.white,
                diffMarginX, diffY, diffW, diffH, 38);
            var normalBtn = AddPillButton(intro.transform, "DifficultyNormalButton", "ปกติ", thaiSemi, Cyan, Color.white,
                diffMarginX + (diffW + diffGap), diffY, diffW, diffH, 38);
            var hardBtn = AddPillButton(intro.transform, "DifficultyHardButton", "ยาก", thaiSemi, Cyan, Color.white,
                diffMarginX + 2 * (diffW + diffGap), diffY, diffW, diffH, 38);

            var diffSelectorGo = new GameObject("DifficultySelector", typeof(RectTransform));
            diffSelectorGo.transform.SetParent(intro.transform, false);
            var diffSelector = diffSelectorGo.AddComponent<AstroDifficultySelector>();
            diffSelector.director = director;
            diffSelector.easyBg = easyBtn.GetComponent<Image>();
            diffSelector.normalBg = normalBtn.GetComponent<Image>();
            diffSelector.hardBg = hardBtn.GetComponent<Image>();
            diffSelector.ApplyInitialHighlight(); // preview the default (Normal) tint before Play mode

            UnityEditor.Events.UnityEventTools.AddPersistentListener(easyBtn.onClick, diffSelector.SelectEasy);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(normalBtn.onClick, diffSelector.SelectNormal);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(hardBtn.onClick, diffSelector.SelectHard);

            // Start button: baked "เริ่มภารกิจ" PNG (solid glossy green pill, label + shadow baked in,
            // correct Thai). Sized to its own aspect + centred. The old startbutton.png had huge
            // transparent margins so it rendered small/washed — btn_start is trimmed + opaque.
            const float startH = 150f;
            float startW = BakedWidth("btn_start", startH);
            var startBtn = AddBakedButton(intro.transform, "StartButton", "btn_start",
                (FW - startW) * 0.5f, 1205f, startW, startH, "เริ่มภารกิจ", thaiSemi, Green);
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
            // dot_on.png (same rounded shape already used for the lane dots) instead of the raw
            // engine knob — a plain color rect where a real sprite exists (Task 4 consistency pass).
            var chipDotSprite = Ui("dot_on");
            for (int i = 0; i < chipLabels.Length; i++)
            {
                float cx = chipStartX + i * (chipSize + chipGap);
                var chip = AddImage(framing.transform, $"FramingChip{i}",
                    chipDotSprite != null ? chipDotSprite : knob, new Color(1f, 1f, 1f, 0.25f));
                Place(chip.rectTransform, cx, 540, chipSize, chipSize);
                var chipLabel = AddText(framing.transform, chipLabels[i], thaiSemi, 30, DarkText, TextAlignmentOptions.Center);
                Place(chipLabel.rectTransform, cx - 13, 608, chipSize + 26, 40);
                framingChips[i] = chip;
            }

            var ringTrackSprite = Ui("ring_track");
            var ringFillSprite = Ui("ring_fill");
            var framingHoldTrack = AddImage(framing.transform, "FramingHoldRingTrack",
                ringTrackSprite != null ? ringTrackSprite : knob, ringTrackSprite != null ? Color.white : RingTrack);
            Place(framingHoldTrack.rectTransform, 404, 680, 120, 120);
            var framingHoldRing = AddImage(framing.transform, "FramingHoldRingFill",
                ringFillSprite != null ? ringFillSprite : knob, ringFillSprite != null ? Color.white : Cyan);
            framingHoldRing.type = Image.Type.Filled;
            framingHoldRing.fillMethod = Image.FillMethod.Radial360;
            framingHoldRing.fillOrigin = (int)Image.Origin360.Top;
            framingHoldRing.fillClockwise = true;
            framingHoldRing.fillAmount = 0f;
            Place(framingHoldRing.rectTransform, 404, 680, 120, 120);

            // Body-loss "ignore" button (Contract 2). Lives on the SAME FramingPanel used both for
            // the initial pre-Start guide-in and the mid-game body-loss lock — the director owns its
            // visibility and must only ever show it for the latter, so it starts hidden here and stays
            // that way until the director explicitly activates it (see initial active-states below).
            var bodyLostIgnoreBtn = AddPillButton(framing.transform, "BodyLostIgnoreButton", "ข้ามการเตือน",
                thaiSemi, Gray, Color.white, 314, 820, 300, 80, 34, secondary: true);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(bodyLostIgnoreBtn.onClick, director.OnBodyLostIgnorePressed);

            // =================== CALIBRATION (canvas-level sibling — see class doc) ===================
            var calibGroup = NewPanel(canvas.transform, "CalibGroup");
            calibGroup.GetComponent<Image>().color = ScrimDim;
            // Cream card behind the ring+prompt (Task 4 consistency pass) — every other panel's
            // guidance text sits on a card_cream now; CalibGroup was the one holdout floating
            // straight on the dark scrim. calibText below switches to dark ink to match.
            AddCard(calibGroup.transform, 140, 460, 647, 390);
            var calibTrack = AddImage(calibGroup.transform, "CalibRingTrack",
                ringTrackSprite != null ? ringTrackSprite : knob, ringTrackSprite != null ? Color.white : RingTrack);
            Place(calibTrack.rectTransform, 383, 560, 160, 160);
            var calibRing = AddImage(calibGroup.transform, "CalibRingFill",
                ringFillSprite != null ? ringFillSprite : knob, ringFillSprite != null ? Color.white : Cyan);
            calibRing.type = Image.Type.Filled;
            calibRing.fillMethod = Image.FillMethod.Radial360;
            calibRing.fillOrigin = (int)Image.Origin360.Top;
            calibRing.fillClockwise = true;
            calibRing.fillAmount = 0f;
            Place(calibRing.rectTransform, 383, 560, 160, 160);
            // Now sits on the cream card above, so dark ink (like every other card's guidance
            // text) instead of the old white-on-scrim treatment.
            var calibText = AddText(calibGroup.transform, "ยืนตรง นิ่ง ๆ 2 วินาที", thaiSemi, 48, DarkText, TextAlignmentOptions.Center);
            Place(calibText.rectTransform, 114, 740, 700, 70);

            // =================== HUD ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Score, top-left. scorebadge.png bakes its own gold star into the LEFT side (source
            // art is 623x400, ~1.56:1) — the numeric score just sits in the empty space to the
            // RIGHT of that baked star, inside the same footprint the old chip+star combo used.
            // Sized to the ~240x110 HUD slot per spec (a mild stretch off the source aspect — an
            // acceptable trade to keep the score's on-screen position/footprint unchanged). Falls
            // back to the original procedural chip + tinted knob star if the art isn't built yet.
            // HUD in-play elements run ~15% larger than the reference slots (user request) so they
            // read clearly at arm's length during play.
            var scoreBadgeSprite = Ui("scorebadge");
            TMP_Text scoreText;
            if (scoreBadgeSprite != null)
            {
                var scoreBadge = AddImage(hud.transform, "ScoreBadge", scoreBadgeSprite, Color.white);
                Place(scoreBadge.rectTransform, 36, 36, 276, 127);
                scoreText = AddText(hud.transform, "0", montserratBlack, 66, OffWhite, TextAlignmentOptions.Left);
                Place(scoreText.rectTransform, 146, 52, 150, 92);
            }
            else
            {
                AddChip(hud.transform, 36, 36, 240, 110);
                var scoreStar = AddImage(hud.transform, "ScoreStar", knob, Gold);
                Place(scoreStar.rectTransform, 56, 58, 56, 56);
                scoreText = AddText(hud.transform, "0", montserratBlack, 64, OffWhite, TextAlignmentOptions.Left);
                Place(scoreText.rectTransform, 122, 52, 140, 82);
            }

            // -- Timer, top-center. Clean circular ring (no square chip behind it) with mm:ss
            // centered inside — matches the timeuireference.png reference. Same ring_track/
            // ring_fill sprites as CalibRing/FramingHoldRing, just sized+placed to read as a
            // tidy stopwatch instead of the old chip-backed square.
            const float timerRingSize = 212f; // bigger, more prominent stopwatch (+15%)
            const float timerColCenterX = 463f;
            const float timerColCenterY = 128f;
            float timerRingX = timerColCenterX - timerRingSize * 0.5f;
            float timerRingY = timerColCenterY - timerRingSize * 0.5f;
            // Cream backing disc behind the ring so the mm:ss reads clearly over the bright park.
            var timerDiscSprite = Ui("dot_on");
            float timerDiscSize = timerRingSize * 0.80f;
            var timerDisc = AddImage(hud.transform, "TimerDisc",
                timerDiscSprite != null ? timerDiscSprite : knob, new Color(0.96f, 0.98f, 0.90f, 0.96f));
            Place(timerDisc.rectTransform, timerColCenterX - timerDiscSize * 0.5f, timerColCenterY - timerDiscSize * 0.5f, timerDiscSize, timerDiscSize);
            var timerRingTrack = AddImage(hud.transform, "TimerRingTrack",
                ringTrackSprite != null ? ringTrackSprite : knob, ringTrackSprite != null ? Color.white : RingTrack);
            Place(timerRingTrack.rectTransform, timerRingX, timerRingY, timerRingSize, timerRingSize);
            var timerRing = AddImage(hud.transform, "TimerRingFill",
                ringFillSprite != null ? ringFillSprite : knob, ringFillSprite != null ? Color.white : Cyan);
            timerRing.type = Image.Type.Filled;
            timerRing.fillMethod = Image.FillMethod.Radial360;
            timerRing.fillOrigin = (int)Image.Origin360.Top;
            timerRing.fillClockwise = false; // counts DOWN
            timerRing.fillAmount = 1f;
            Place(timerRing.rectTransform, timerRingX, timerRingY, timerRingSize, timerRingSize);
            var timerText = AddText(hud.transform, "3:00", montserratBlack, 62, DarkText, TextAlignmentOptions.Center);
            Place(timerText.rectTransform, timerRingX, timerColCenterY - 40f, timerRingSize, 80f);

            // -- Score-change popup: floats just under the score badge. Hidden by default — the
            // director activates + animates it (ShowScorePop) whenever the score changes. Plain
            // (non-sliced) scorechange.png bakes its own gold star top-center (source 488x511,
            // ~0.96:1); the +1/-1 number goes in the card body BELOW that baked star.
            var scoreChangeSprite = Ui("scorechange");
            const float scPopW = 207f, scPopH = 219f;
            var scoreChangePopup = AddImage(hud.transform, "ScoreChangePopup",
                scoreChangeSprite != null ? scoreChangeSprite : knob, scoreChangeSprite != null ? Color.white : GlassBg);
            if (scoreChangeSprite == null) Round(scoreChangePopup);
            Place(scoreChangePopup.rectTransform, 36, 176, scPopW, scPopH);
            var scoreChangeText = AddText(scoreChangePopup.transform, "+1", montserratBlack, 66, OffWhite, TextAlignmentOptions.Center);
            scoreChangeText.fontStyle = FontStyles.Bold;
            // Parent-relative stretch (NOT Place — Place's math assumes a canvas-sized parent):
            // fills the popup's lower body, below the baked star which sits in the top ~40%.
            var scText = scoreChangeText.rectTransform;
            scText.anchorMin = Vector2.zero;
            scText.anchorMax = Vector2.one;
            scText.offsetMin = new Vector2(12f, 16f);
            scText.offsetMax = new Vector2(-12f, -78f);
            scoreChangePopup.gameObject.SetActive(false);

            // -- Pause round button, top-right. --
            var btnRoundSprite = Ui("btn_round");
            var pauseBtnImg = AddImage(hud.transform, "PauseButton",
                btnRoundSprite != null ? btnRoundSprite : knob, btnRoundSprite != null ? Color.white : GlassBg);
            pauseBtnImg.raycastTarget = true;
            Place(pauseBtnImg.rectTransform, 781, 36, 110, 110);
            var pauseBtn = pauseBtnImg.gameObject.AddComponent<Button>();
            var pauseGlyph = AddText(pauseBtnImg.transform, "II", thaiSemi, 46, OffWhite, TextAlignmentOptions.Center);
            Stretch(pauseGlyph.rectTransform);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(pauseBtn.onClick, director.OnPausePressed);

            // -- Feedback popups (pictures), centred. Shown briefly on a safe dodge (ปลอดภัย! ✓) or
            //    a hit (ระวัง! ⚠). Baked PNGs (correct Thai); the director pops one for ~1.2s.
            var safeSprite = Ui("pop_safe");
            var dangerSprite = Ui("pop_danger");
            const float fbH = 184f, fbY = 452f; // bigger picture popup (+15% and then some — it's the key feedback)
            Image feedbackSafe = null, feedbackDanger = null;
            if (safeSprite != null)
            {
                float w = fbH * safeSprite.rect.width / safeSprite.rect.height;
                feedbackSafe = AddImage(hud.transform, "FeedbackSafe", safeSprite, Color.white);
                feedbackSafe.raycastTarget = false;
                Place(feedbackSafe.rectTransform, (FW - w) * 0.5f, fbY, w, fbH);
                feedbackSafe.gameObject.SetActive(false);
            }
            if (dangerSprite != null)
            {
                float w = fbH * dangerSprite.rect.width / dangerSprite.rect.height;
                feedbackDanger = AddImage(hud.transform, "FeedbackDanger", dangerSprite, Color.white);
                feedbackDanger.raycastTarget = false;
                Place(feedbackDanger.rectTransform, (FW - w) * 0.5f, fbY, w, fbH);
                feedbackDanger.gameObject.SetActive(false);
            }

            // -- Lane dots, centered bottom. --
            var laneDots = new Image[3];
            const float dotSize = 42f, dotSpacing = 80f;
            float dotsCenterX = FW * 0.5f;
            // Director only ever retints .color at runtime (LaneDotOn/Off) — it never swaps
            // sprites — so all 3 dots share one shape sprite (dot_on's baked rim/fill) and rely
            // on that tint for the on/off look, same mechanism as before with `knob`.
            var dotSprite = Ui("dot_on");
            for (int i = 0; i < 3; i++)
            {
                float cx = dotsCenterX + (i - 1) * dotSpacing - dotSize * 0.5f;
                var dot = AddImage(hud.transform, $"LaneDot{i}", dotSprite != null ? dotSprite : knob, new Color(1f, 1f, 1f, 0.25f));
                Place(dot.rectTransform, cx, 1290, dotSize, dotSize);
                laneDots[i] = dot;
            }

            // -- Toast, centered. --
            // The pill sits BEHIND the toast text (added first = earlier sibling = drawn under) and
            // starts fully transparent: the director owns its alpha via toastBg and fades it in step
            // with the message text, so nothing is visible between toasts. Optional — with no
            // toast.png the toast stays text-only, which reads fine against the bright park.
            var toastSprite = Ui("toast");
            Image toastBg = null;
            if (toastSprite != null)
            {
                toastBg = AddImage(hud.transform, "ToastBg", toastSprite, new Color(1f, 1f, 1f, 0f));
                toastBg.type = Image.Type.Sliced;
                toastBg.raycastTarget = false;
                Place(toastBg.rectTransform, 64, 495, 799, 100);
            }
            var toastText = AddText(hud.transform, "", thaiBlack, 74, Gold, TextAlignmentOptions.Center);
            toastText.fontStyle = FontStyles.Bold;
            toastText.raycastTarget = false;
            Place(toastText.rectTransform, 44, 500, 839, 100);
            Outline(toastText, new Color(0f, 0f, 0f, 0.6f), 0.25f);

            // -- Body-loss warning popup (Contract 2), centered, hidden by default. --
            // LOGIC shows this ~3s if the body is lost AFTER the player has already pressed
            // "ignore" (bodyLostIgnoreBtn above) during a mid-game lock, without bouncing back to
            // Framing. Styled like the main toast above (same toast.png + outlined text pattern),
            // just a separate GameObject since the director drives its active state directly
            // (SetActive) rather than the main toast's fade-timer.
            var bodyLostToastGo = new GameObject("BodyLostToast", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bodyLostToastRt = (RectTransform)bodyLostToastGo.transform;
            bodyLostToastRt.SetParent(hud.transform, false);
            Place(bodyLostToastRt, 114, 950, 700, 110);
            var bodyLostToastBg = bodyLostToastGo.GetComponent<Image>();
            bodyLostToastBg.raycastTarget = false;
            if (toastSprite != null)
            {
                bodyLostToastBg.sprite = toastSprite;
                bodyLostToastBg.type = Image.Type.Sliced;
                bodyLostToastBg.color = Color.white;
            }
            else
            {
                bodyLostToastBg.color = GlassBg; // same fallback look as AddChip's missing-art case
                Round(bodyLostToastBg);
            }
            var bodyLostToastText = AddText(bodyLostToastRt, "ขยับให้เห็นทั้งตัว", thaiBlack, 48, MeteorOrange, TextAlignmentOptions.Center);
            Stretch(bodyLostToastText.rectTransform);
            Outline(bodyLostToastText, new Color(0f, 0f, 0f, 0.6f), 0.25f);

            // -- Countdown, huge center, initially inactive. The number sits on a dark badge disc so
            //    3-2-1-ไป reads clearly over the bright park (was hard to see as bare dark ink). The
            //    disc is a separate object the director toggles alongside the number (countdownBadge).
            var cdDiscSprite = Ui("dot_on");
            var countdownBadge = AddImage(hud.transform, "CountdownBadge",
                cdDiscSprite != null ? cdDiscSprite : knob, new Color(0.12f, 0.16f, 0.10f, 0.74f));
            Place(countdownBadge.rectTransform, 264f, 510f, 400f, 400f);
            countdownBadge.gameObject.SetActive(false);
            var countdownText = AddText(hud.transform, "3", montserratBlack, 200, Color.white, TextAlignmentOptions.Center);
            Place(countdownText.rectTransform, 0, 560, FW, 300);
            Outline(countdownText, new Color(0.10f, 0.35f, 0.62f, 1f), 0.3f);
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
            // Baked-art buttons (เล่นต่อ / ออกจากเกม) sized to their own aspect + centered as a row.
            const float pauseBtnH = 104f, pauseGap = 30f, pauseRowY = 636f;
            float wResume = BakedWidth("btn_resume", pauseBtnH);
            float wExit = BakedWidth("btn_exit", pauseBtnH);
            float pauseRowX = 94f + (740f - (wResume + pauseGap + wExit)) * 0.5f;
            var resumeBtn = AddBakedButton(pause.transform, "ResumeButton", "btn_resume", pauseRowX, pauseRowY, wResume, pauseBtnH, "เล่นต่อ", thaiSemi, Green);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(resumeBtn.onClick, director.OnResumePressed);
            var exitFromPauseBtn = AddBakedButton(pause.transform, "ExitFromPauseButton", "btn_exit", pauseRowX + wResume + pauseGap, pauseRowY, wExit, pauseBtnH, "ออกจากเกม", thaiSemi, Gray, fbSecondary: true);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitFromPauseBtn.onClick, director.OnExitPressed);
            // Small "จบเกม" button below → ends the run straight to the results screen.
            const float endGameH = 80f;
            float wEndGame = BakedWidth("btn_endgame", endGameH);
            var endGameBtn = AddBakedButton(pause.transform, "EndGameButton", "btn_endgame", 94f + (740f - wEndGame) * 0.5f, 778f, wEndGame, endGameH, "จบเกม", thaiSemi, Gold);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(endGameBtn.onClick, director.OnEndGamePressed);

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
            // Same story as the lane dots: the director only retints .color (StarLit/StarDim), it
            // never swaps sprites, so all 3 stars share star_lit's shape and the runtime tint does
            // the lit/dim differentiation (star_dim.png is unused while that's true — flagged for review).
            var starSprite = Ui("star_lit");
            for (int i = 0; i < 3; i++)
            {
                var star = AddImage(results.transform, $"ResultStar{i}", starSprite != null ? starSprite : knob, new Color(0.14f, 0.20f, 0.12f, 0.18f));
                Place(star.rectTransform, starStartX + i * (starSize + starGap), 540, starSize, starSize);
                resultStars[i] = star;
            }

            // Dynamic praise headline (ทำได้ดีมาก! etc.) — director sets the text from the star count.
            var resultPraiseText = AddText(results.transform, "ทำได้ดีมาก!", thaiBlack, 52, Green, TextAlignmentOptions.Center);
            Place(resultPraiseText.rectTransform, 94, 636, 740, 62);
            Outline(resultPraiseText, Color.white, 0.25f);

            // Rows double as the icon fill AND the value-text color, so each tone is darkened
            // enough to stay readable as text on the cream results card (plain Gold/Cyan/Green
            // read fine as icon fills but wash out as text on cream).
            // Two groups: the first 3 rows are the in-game ACTION names (treasure / kick / dodge) and
            // get a white rounded-rect background so they read as a set, separate from the lower rows
            // which are the rehab POSE names (sit-stand / step / side-kick). The director fills all 6
            // values in this exact order (see EndRun's reps[]).
            (string label, Color color)[] repRows =
            {
                ("สมบัติที่เก็บ", new Color(0.68f, 0.46f, 0.04f)),   // deep amber (was Gold)
                ("เตะโดน", new Color(0.08f, 0.32f, 0.62f)),          // deep sky-blue (was Cyan)
                ("หลบก้อนหิน", new Color(0.10f, 0.40f, 0.16f)),      // deep green (was Green)
                ("ลุก-นั่ง", OffWhite),
                ("ก้าวข้าง", IndigoLight),
                ("เตะขาออกข้าง", new Color(0.10f, 0.40f, 0.45f)),    // deep teal — the side-kick pose dose
            };
            const int actionRowCount = 3; // first N rows are game actions → white pill background
            // Icon per row, in repRows order — these Images are build-time only (never touched by the
            // director afterward), so unlike stars/dots we can safely give each its own icon sprite.
            string[] repIconSprites = { "icon_treasure", "icon_kick", "icon_dodge", "icon_sit", "icon_step", "icon_kick" };
            var resultRepValues = new TMP_Text[repRows.Length];
            const float rowPitch = 72f, rowTop = 724f;
            for (int i = 0; i < repRows.Length; i++)
            {
                float ry = rowTop + i * rowPitch;
                if (i < actionRowCount)
                {
                    var pill = AddImage(results.transform, $"RepPill{i}", null, new Color(1f, 1f, 1f, 0.92f));
                    Round(pill);
                    Place(pill.rectTransform, 96, ry - 10, 716, 64);
                }
                var iconSprite = Ui(repIconSprites[i]);
                var icon = AddImage(results.transform, $"RepIcon{i}", iconSprite != null ? iconSprite : knob, iconSprite != null ? Color.white : repRows[i].color);
                Place(icon.rectTransform, 120, ry, 44, 44);
                var label = AddText(results.transform, repRows[i].label, thaiSemi, 38, OffWhite, TextAlignmentOptions.Left);
                Place(label.rectTransform, 180, ry - 4, 400, 52);
                var value = AddText(results.transform, "0", thaiSemi, 44, repRows[i].color, TextAlignmentOptions.Right);
                Place(value.rectTransform, 600, ry - 6, 190, 56);
                resultRepValues[i] = value;
            }

            // Baked-art buttons (เล่นอีกครั้ง / กลับหน้าหลัก) sized to aspect + centered as a row.
            const float resBtnH = 104f, resGap = 30f, resRowY = 1200f;
            float wPlay = BakedWidth("btn_playagain", resBtnH);
            float wHome = BakedWidth("btn_home", resBtnH);
            float resRowX = 54f + (819f - (wPlay + resGap + wHome)) * 0.5f;
            var playAgainBtn = AddBakedButton(results.transform, "PlayAgainButton", "btn_playagain", resRowX, resRowY, wPlay, resBtnH, "เล่นอีกครั้ง", thaiSemi, Cyan);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(playAgainBtn.onClick, director.OnPlayAgainPressed);
            var exitFromResultsBtn = AddBakedButton(results.transform, "ExitFromResultsButton", "btn_home", resRowX + wPlay + resGap, resRowY, wHome, resBtnH, "กลับหน้าหลัก", thaiSemi, Gray, fbSecondary: true);
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
            Assign(so, "toastBg", toastBg);
            Assign(so, "scoreChangePopup", scoreChangePopup);
            Assign(so, "scoreChangeText", scoreChangeText);
            Assign(so, "feedbackSafe", feedbackSafe);
            Assign(so, "feedbackDanger", feedbackDanger);
            Assign(so, "countdownText", countdownText);
            AssignGo(so, "countdownBadge", countdownBadge.gameObject);
            Assign(so, "resultScoreText", resultScoreText);
            Assign(so, "resultPraiseText", resultPraiseText);
            Assign(so, "bodyLostIgnoreButton", bodyLostIgnoreBtn);
            AssignGo(so, "bodyLostToast", bodyLostToastGo);

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
            // Both hidden regardless of their parent panel's own active state (Contract 2) — the
            // director is the only thing that ever turns these on, for a MID-GAME body-loss lock.
            bodyLostIgnoreBtn.gameObject.SetActive(false);
            bodyLostToastGo.SetActive(false);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "AstroStance UI rebuilt and wired to AstroStanceDirector.";
        }

        // ---- 3 how-to cards: side-step / sit-stand / kick. ----
        // sidewalkcard.png / sitcard.png / kickcard.png already bake in their own rounded card
        // frame + Thai caption (art direction moved the whole card to a single designed image), so
        // when present each card is just that one Image — no separate AddCard/icon/caption. Falls
        // back to the original procedural cream card + pictogram + caption per card when its art is
        // missing, so the intro still builds without it.
        static void BuildHowToCards(Transform parent, TMP_FontAsset thaiSemi, Sprite knob)
        {
            const float cardW = 264f, cardH = 386f; // a touch wider than the original 250
            // Tighter, centred fan (small gaps, cards nearly touching) sitting just under the
            // subtitle — matches the reference. Middle card dropped lower for the staggered look.
            float[] xs = { 46f, 332f, 618f };
            float[] ys = { 372f, 430f, 372f };
            string[] cardSprites = { "sidewalkcard", "sitcard", "kickcard" };
            string[] icons = { "icon_step", "icon_sit", "icon_kick" }; // fallback-only
            string[] captions = // fallback-only
            {
                "ก้าวข้าง หลบก้อนหิน",
                "นั่งแล้วลุก เก็บสมบัติ",
                "เตะวงแหวนจากเลนข้าง ๆ",
            };

            for (int i = 0; i < 3; i++)
            {
                float x = xs[i];
                float cardY = ys[i];
                var cardArt = Ui(cardSprites[i]);
                if (cardArt != null)
                {
                    var img = AddImage(parent, "Card", cardArt, Color.white);
                    Place(img.rectTransform, x, cardY, cardW, cardH);
                    continue;
                }

                // Pictograms + captions are siblings of the card (added after it, so they draw on
                // top): Place() uses CANVAS-space coords, so children of the card would land wrong —
                // anchors are fractions of the PARENT rect.
                AddCard(parent, x, cardY, cardW, cardH);

                var icon = Ui(icons[i]);
                if (icon != null)
                {
                    const float iconSize = 120f;
                    var iconImg = AddImage(parent, "Picto", icon, Color.white);
                    Place(iconImg.rectTransform, x + (cardW - iconSize) / 2f, cardY + 36, iconSize, iconSize);
                }
                else switch (i)
                {
                    case 0: // side-step: person dot + left/right arrow bars
                        AddPictoDot(parent, knob, OffWhite, x + 95, cardY + 56, 56);
                        AddPictoBar(parent, Cyan, x + 26, cardY + 82, 56, 13, -20f);
                        AddPictoBar(parent, Cyan, x + 168, cardY + 82, 56, 13, 20f);
                        break;
                    case 1: // sit-stand: chair-ish seat + backrest
                        AddPictoBar(parent, Gold, x + 78, cardY + 120, 94, 18, 0f);
                        AddPictoBar(parent, Gold, x + 78, cardY + 56, 18, 84, 0f);
                        break;
                    default: // kick: leg-line + ring
                        AddPictoBar(parent, OffWhite, x + 153, cardY + 84, 13, 102, 0f);
                        var ringOuter = AddImage(parent, "RingOuter", knob, new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f));
                        Place(ringOuter.rectTransform, x + 37, cardY + 56, 65, 65);
                        var ringInner = AddImage(parent, "RingInner", knob, GlassBg);
                        Place(ringInner.rectTransform, x + 51, cardY + 70, 37, 37);
                        break;
                }

                var caption = AddText(parent, captions[i], thaiSemi, 34, OffWhite, TextAlignmentOptions.Center);
                Place(caption.rectTransform, x + 10, cardY + 216, cardW - 20, 150);
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

            // feed_frame.png draws OVER the RawImage (transparent centre + leaf rim + shadow), added
            // last so it's the top sibling. Null-safe: if it's not built yet the plain GlassBg
            // backdrop (panelImg above) is the only frame, same as before.
            var frameSprite = Ui("feed_frame");
            if (frameSprite != null)
            {
                var frame = AddImage(panelRt, "FeedFrame", frameSprite, Color.white);
                frame.type = Image.Type.Sliced;
                Stretch(frame.rectTransform);
            }

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
            var sprite = Ui("card_cream");
            if (sprite != null)
            {
                // card_cream.png already bakes in the rim + soft ambient shadow — no procedural
                // rim/Shadow needed on top of it.
                var img = AddImage(parent, "Card", sprite, Color.white);
                img.type = Image.Type.Sliced;
                Place(img.rectTransform, x, y, w, h);
                return img.gameObject;
            }
            // Fallback while card_cream.png isn't built yet: original procedural rim + fill + shadow.
            // Faint leaf-green rim first (a slightly larger card behind), then the cream fill on top —
            // gives every card a visible edge against the bright park stage without a real 9-slice border.
            var rim = AddImage(parent, "CardRim", null, CardRim);
            Round(rim);
            Place(rim.rectTransform, x - 2f, y - 2f, w + 4f, h + 4f);

            var card = AddImage(parent, "Card", null, GlassBg);
            Round(card);
            var shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(0, 6);
            Place(card.rectTransform, x, y, w, h);
            return card.gameObject;
        }

        // HUD score/timer background — smaller than a card, uses chip.png (contract: "HUD chips").
        static Image AddChip(Transform parent, float x, float y, float w, float h)
        {
            var sprite = Ui("chip");
            Image img;
            if (sprite != null)
            {
                img = AddImage(parent, "Chip", sprite, Color.white);
                img.type = Image.Type.Sliced;
            }
            else
            {
                img = AddImage(parent, "Chip", null, GlassBg); // fallback: same look as AddCard's fallback
                Round(img);
            }
            Place(img.rectTransform, x, y, w, h);
            return img;
        }

        // Baked-art button: a pre-rendered PNG that already contains its label (correct Thai
        // shaping, gloss + drop shadow baked in) — no TMP text child on top. Sized to (w,h) which
        // the caller derives from BakedWidth so the sprite's own aspect is preserved (text never
        // stretches). Falls back to a procedural AddPillButton (tint + label) if the art is missing.
        static Button AddBakedButton(Transform parent, string name, string spriteName, float x, float y,
                                     float w, float h, string fbLabel, TMP_FontAsset fbFont, Color fbBg,
                                     bool fbSecondary = false)
        {
            var sprite = Ui(spriteName);
            if (sprite != null)
            {
                var img = AddImage(parent, name, sprite, Color.white);
                img.raycastTarget = true;
                Place(img.rectTransform, x, y, w, h);
                return img.gameObject.AddComponent<Button>();
            }
            return AddPillButton(parent, name, fbLabel, fbFont, fbBg, Color.white, x, y, w, h * 0.9f, 40, secondary: fbSecondary);
        }

        // Width for a baked button at height h, from the sprite's own aspect (keeps text un-stretched).
        static float BakedWidth(string spriteName, float h)
        {
            var s = Ui(spriteName);
            return s != null ? h * s.rect.width / s.rect.height : 260f;
        }

        static Button AddPillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                    Color bg, Color textColor, float x, float y, float w, float h, float labelSize,
                                    bool secondary = false)
        {
            var sprite = Ui(secondary ? "btn_secondary" : "btn_primary");
            var img = AddImage(parent, name, sprite, sprite != null ? Color.white : bg);
            if (sprite != null) img.type = Image.Type.Sliced;
            else Round(img); // fallback: procedural pill, keep the caller's tint
            img.raycastTarget = true;
            Place(img.rectTransform, x, y, w, h);
            var btn = img.gameObject.AddComponent<Button>();
            if (!secondary && sprite != null) // only wire a pressed sprite once the normal one is real art
            {
                var pressedSprite = Ui("btn_primary_down");
                if (pressedSprite != null)
                {
                    btn.transition = Selectable.Transition.SpriteSwap;
                    var state = btn.spriteState;
                    state.pressedSprite = pressedSprite;
                    btn.spriteState = state;
                }
            }
            // Secondary buttons use the pale btn_secondary sprite — white text on it is nearly
            // invisible (see pause/results screenshots), so force a dark readable label regardless
            // of what the caller passed.
            var text = AddText(img.transform, label, font, labelSize, secondary ? DarkText : textColor, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);
            return btn;
        }

        static Button AddRoundButton(Transform parent, string name, Color bg, float x, float y, float size)
        {
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var sprite = Ui("btn_round");
            var img = AddImage(parent, name, sprite != null ? sprite : knob, sprite != null ? Color.white : bg);
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
