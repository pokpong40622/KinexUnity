#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.DanceStar;

namespace Kinex.EditorTools
{
    /// <summary>
    /// One-click builder for the SUPERSTAR STAGE UI — same procedural-canvas + SerializedObject
    /// wiring pattern as MirrorGameUIBuilder/TempleHuntUIBuilder. Re-runnable: destroys its own
    /// panels by name and rebuilds, then wires every DanceStarDirector UI field. Big Thai text
    /// (>=40px), FC Iconic fonts, gold/magenta/cyan neon accents matching DanceStage. Bottom
    /// corners are left deliberately empty for the shared Kinex.UI.GameHud heart-rate/match rings
    /// (see class doc note below — DanceStarDirector does not currently call GameHud.Ensure, so
    /// nothing renders there yet; the space is reserved for parity with the other games).
    /// </summary>
    public static class DanceStarUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        static readonly Color Ink = new Color32(0x1E, 0x16, 0x2E, 0xFF);
        static readonly Color CardWhite = new Color32(0xFF, 0xFF, 0xFF, 0xF0);
        static readonly Color CardWarm = new Color32(0xFF, 0xF3, 0xDD, 0xF2);
        static readonly Color Gold = new Color32(0xFF, 0xD1, 0x55, 0xFF);
        static readonly Color Magenta = new Color32(0xE8, 0x3A, 0xA8, 0xFF);
        static readonly Color Cyan = new Color32(0x2A, 0xC7, 0xE0, 0xFF);
        static readonly Color RingDim = new Color32(0x00, 0x00, 0x00, 0x55);
        static readonly Color Violet = new Color32(0x5B, 0x3A, 0x8E, 0xFF);
        static readonly Color GreenGo = new Color32(0x4C, 0xAF, 0x50, 0xFF);
        static readonly Color FlameOrange = new Color32(0xFF, 0x8A, 0x2E, 0xFF);
        static readonly Color HeartOff = new Color32(0xFF, 0xFF, 0xFF, 0x38);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";

        static readonly string[] PanelNames =
            { "IntroPanel", "CalibPanel", "ChairSafetyPanel", "HudPanel", "ResultsPanel", "ExitButton" };

        [MenuItem("Kinex/Build Dance Star UI")]
        public static void Build() => Debug.Log("[DanceStarUIBuilder] " + BuildUI());

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<DanceStarDirector>();
            if (director == null) return "No DanceStarDirector in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var thaiBlack = LoadFont(ThaiBlackPath);
            var thaiSemi = LoadFont(ThaiSemiPath);
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i);
                foreach (var n in PanelNames)
                    if (child.name == n) { Object.DestroyImmediate(child.gameObject); break; }
            }

            // =================== INTRO ===================
            var intro = NewPanel(canvas.transform, "IntroPanel");
            AddCard(intro.transform, 64, 320, 800, 560);
            AddStarMotif(intro.transform, 150, 372, knob);
            AddStarMotif(intro.transform, 700, 372, knob);
            var introTitle = AddText(intro.transform, "เวทีซุปตาร์", thaiBlack, 94, Ink, TextAlignmentOptions.Center);
            Place(introTitle.rectTransform, 94, 400, 740, 140);
            var superstarLabel = AddText(intro.transform, "SUPERSTAR STAGE", thaiSemi, 34, Magenta, TextAlignmentOptions.Center);
            Place(superstarLabel.rectTransform, 94, 542, 740, 54);
            var introSubtitle = AddText(intro.transform, "", thaiSemi, 42, new Color(0.28f, 0.2f, 0.16f), TextAlignmentOptions.Center);
            Place(introSubtitle.rectTransform, 124, 630, 680, 220);

            // =================== CALIBRATION ===================
            var calib = NewPanel(canvas.transform, "CalibPanel");
            AddCard(calib.transform, 64, 560, 800, 300);
            var calibText = AddText(calib.transform, "", thaiSemi, 48, Ink, TextAlignmentOptions.Center);
            Place(calibText.rectTransform, 104, 600, 720, 140);
            var (calibTrack, calibFill) = AddProgressBar(calib.transform, 124, 770, 680, 26, Cyan);

            // =================== CHAIR SAFETY (mid-song bridge) ===================
            var chairSafety = NewPanel(canvas.transform, "ChairSafetyPanel");
            var chairCard = AddCard(chairSafety.transform, 64, 500, 800, 400, CardWarm);
            var chairIcon = AddImage(chairSafety.transform, "ChairIcon", knob, FlameOrange);
            Place(chairIcon.rectTransform, 413, 540, 100, 100);
            var chairSafetyText = AddText(chairSafety.transform, "", thaiSemi, 48,
                                          new Color(0.35f, 0.2f, 0.05f), TextAlignmentOptions.Center);
            Place(chairSafetyText.rectTransform, 114, 670, 700, 200);

            // =================== HUD ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Score, top-centre. --
            var scoreText = AddText(hud.transform, "0", thaiBlack, 76, Gold, TextAlignmentOptions.Center);
            Place(scoreText.rectTransform, 313, 30, 300, 96);
            Outline(scoreText, new Color(0f, 0f, 0f, 0.6f), 0.25f);

            // -- Streak chip, centred just under the score. --
            var streakChip = AddImage(hud.transform, "StreakChip", null, new Color(0.1f, 0.05f, 0.15f, 0.55f));
            Round(streakChip);
            Place(streakChip.rectTransform, 335, 138, 260, 58);
            var streakFlameIcon = AddImage(hud.transform, "StreakFlameIcon", knob, FlameOrange);
            Place(streakFlameIcon.rectTransform, 350, 146, 42, 42);
            var streakText = AddText(hud.transform, "", thaiBlack, 40, Gold, TextAlignmentOptions.Left);
            Place(streakText.rectTransform, 404, 138, 176, 58);

            // -- Hearts row, top-left (clear of the camera feed, which starts at x=648). --
            var heartImages = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                heartImages[i] = AddImage(hud.transform, $"Heart{i + 1}", knob, HeartOff);
                Place(heartImages[i].rectTransform, 40 + i * 46, 140, 40, 40);
            }

            // -- Song progress bar + section name, spanning full width below the camera feed
            // (which occupies y=55..355) so nothing renders underneath the opaque feed panel. --
            var (songTrack, songFill) = AddProgressBar(hud.transform, 40, 365, 847, 18, Magenta);
            var sectionNameText = AddText(hud.transform, "", thaiSemi, 34, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center);
            Place(sectionNameText.rectTransform, 40, 392, 847, 40);
            Outline(sectionNameText, new Color(0f, 0f, 0f, 0.6f), 0.2f);

            // -- Pose card + balance gauge, BOTH stacked in the right column (x560..887) — the
            // trainer stands off to screen-left on its podium, so keeping every player-status
            // gauge on the right keeps them clear of the trainer entirely (round-2 screenshot
            // showed a left-side gauge sitting right on top of the trainer). --
            // Dark glass cards — white cards glowed like lightboxes against the night stage
            // (bloom amplified them) and made their dark Ink/Violet text unreadable.
            var hudCardColor = new Color(0.07f, 0.045f, 0.15f, 0.82f);
            AddCard(hud.transform, 560, 430, 327, 270, hudCardColor);
            var poseNameText = AddText(hud.transform, "", thaiBlack, 42, Color.white, TextAlignmentOptions.Center);
            Place(poseNameText.rectTransform, 582, 450, 284, 70);
            var (ringTrack, countdownRingFill) = AddRadialRing(hud.transform, 638, 528, 150, Gold);
            ringTrack.color = new Color(1f, 1f, 1f, 0.12f);
            var cardCountText = AddText(hud.transform, "", thaiSemi, 36, new Color(0.82f, 0.74f, 1f, 1f), TextAlignmentOptions.Center);
            Place(cardCountText.rectTransform, 582, 656, 284, 44);

            AddCard(hud.transform, 560, 716, 327, 150, hudCardColor);
            var wobbleLabel = AddText(hud.transform, "ทรงตัว", thaiSemi, 32, new Color(0.82f, 0.74f, 1f, 1f), TextAlignmentOptions.Center);
            Place(wobbleLabel.rectTransform, 582, 730, 180, 40);
            var (wobbleTrack, tandemWobbleFill) = AddRadialRing(hud.transform, 764, 726, 100, Cyan);
            wobbleTrack.color = new Color(1f, 1f, 1f, 0.12f);

            // -- Rating popup, big + centred. --
            var ratingPopupText = AddText(hud.transform, "", thaiBlack, 104, Gold, TextAlignmentOptions.Center);
            Place(ratingPopupText.rectTransform, 64, 850, 800, 150);
            Outline(ratingPopupText, new Color(0f, 0f, 0f, 0.75f), 0.3f);
            ratingPopupText.gameObject.SetActive(false);

            // =================== RESULTS ===================
            var results = NewPanel(canvas.transform, "ResultsPanel");
            results.GetComponent<Image>().color = new Color(0.05f, 0.03f, 0.09f, 0.72f);
            var resTitle = AddText(results.transform, "คุณคือซุปตาร์!", thaiBlack, 88, Color.white, TextAlignmentOptions.Center);
            Place(resTitle.rectTransform, 94, 190, 740, 160);
            Outline(resTitle, Gold, 0.25f);
            var starImages = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                starImages[i] = AddImage(results.transform, $"Star{i + 1}", knob, StarOff());
                Place(starImages[i].rectTransform, 288 + i * 140, 390, 110, 110);
            }
            var statsText = AddText(results.transform, "", thaiSemi, 46, Color.white, TextAlignmentOptions.Center);
            Place(statsText.rectTransform, 114, 570, 700, 320);
            var againBtn = AddPillButton(results.transform, "PlayAgainButton", "เล่นอีกครั้ง", thaiSemi, GreenGo, 160, 980, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(againBtn.onClick, director.PlayAgain);
            var homeBtn = AddPillButton(results.transform, "HomeButton", "กลับหน้าหลัก", thaiSemi, Violet, 480, 980, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(homeBtn.onClick, director.ExitToHome);

            // =================== EXIT (all states) ===================
            var exitBtn = AddPillButton(canvas.transform, "ExitButton", "ออก", thaiSemi, new Color32(0xE0, 0x5A, 0x4E, 0xFF), 40, 32, 150, 92);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitBtn.onClick, director.ExitToHome);
            exitBtn.transform.SetAsLastSibling();
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed != null) feed.SetAsLastSibling();

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
            Assign(so, "scoreText", scoreText);
            Assign(so, "streakText", streakText);
            Assign(so, "streakFlameIcon", streakFlameIcon);
            Assign(so, "cardCountText", cardCountText);
            Assign(so, "poseNameText", poseNameText);
            Assign(so, "sectionNameText", sectionNameText);
            Assign(so, "songProgressFill", songFill);
            Assign(so, "countdownRingFill", countdownRingFill);
            Assign(so, "ratingPopupText", ratingPopupText);
            Assign(so, "tandemWobbleFill", tandemWobbleFill);

            var heartsProp = so.FindProperty("heartImages");
            heartsProp.arraySize = heartImages.Length;
            for (int i = 0; i < heartImages.Length; i++)
                heartsProp.GetArrayElementAtIndex(i).objectReferenceValue = heartImages[i];

            AssignGo(so, "resultsPanel", results);
            Assign(so, "resultsStatsText", statsText);
            var starsProp = so.FindProperty("resultsStarImages");
            starsProp.arraySize = 3;
            for (int i = 0; i < 3; i++) starsProp.GetArrayElementAtIndex(i).objectReferenceValue = starImages[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            // Panels start correct: intro on, everything else off (matches DanceStarDirector.Start's
            // own ShowOnly(introPanel) call, so the saved scene already looks right before Play).
            intro.SetActive(true);
            calib.SetActive(false);
            chairSafety.SetActive(false);
            hud.SetActive(false);
            results.SetActive(false);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Dance Star UI rebuilt and wired to DanceStarDirector.";
        }

        // ---- helpers (MirrorGameUIBuilder pattern, plus a couple of small DanceStar-only ones) ----

        static Color StarOff() => new Color(1f, 1f, 1f, 0.22f);

        static void AddStarMotif(Transform parent, float centerX, float centerY, Sprite knob)
        {
            var star = AddImage(parent, "StarMotif", knob, Gold);
            Place(star.rectTransform, centerX - 22, centerY - 22, 44, 44);
        }

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
            shadow.effectColor = new Color(0, 0, 0, 0.3f);
            shadow.effectDistance = new Vector2(0, 6);
            Place(img.rectTransform, x, y, w, h);
            return img.gameObject;
        }

        // Track (dim) + fill (coloured) radial ring pair, reused for the countdown ring and the
        // tandem wobble gauge. Caller wires the returned fill Image to the director field it needs;
        // the track is purely decorative.
        static (Image track, Image fill) AddRadialRing(Transform parent, float x, float y, float size, Color color)
        {
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var track = AddImage(parent, "RingTrack", knob, RingDim);
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

        // Track (dim) + fill (coloured) horizontal bar pair, reused for calibration progress and
        // the song progress bar.
        static (Image track, Image fill) AddProgressBar(Transform parent, float x, float y, float w, float h, Color color)
        {
            // Brighter than RingDim's 0x55 alpha — a near-black bar on the stage's near-black
            // background at rest (0% fill) was reading as invisible in the HUD screenshot.
            var track = AddImage(parent, "BarTrack", null, new Color(1f, 1f, 1f, 0.16f));
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
            var text = AddText(img.transform, label, font, 42, Color.white, TextAlignmentOptions.Center);
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
