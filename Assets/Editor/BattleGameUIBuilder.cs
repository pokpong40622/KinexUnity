#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.BattleGame;
using Kinex.Trainer;

namespace Kinex.BattleGame.EditorTools
{
    /// <summary>
    /// One-click builder for the Guardian of Balance battle UI — same procedural-canvas,
    /// SerializedObject-wiring pattern as FruitGameUIBuilder/BalanceQuestUIBuilder. Re-runnable:
    /// destroys its own panels by name and rebuilds, then wires every BattleDirector field.
    /// </summary>
    public static class BattleGameUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        static readonly Color Navy = new Color32(0x1F, 0x2F, 0x66, 0xFF);
        static readonly Color CardWhite = new Color32(0xFF, 0xFF, 0xFF, 0xF0);
        static readonly Color WarmRed = new Color32(0xE0, 0x5A, 0x4E, 0xFF);
        static readonly Color Gold = new Color32(0xFF, 0xD6, 0x33, 0xFF);
        static readonly Color HpGreen = new Color32(0x4C, 0xAF, 0x50, 0xFF);
        static readonly Color HeartRed = new Color32(0xED, 0x42, 0x56, 0xFF);
        static readonly Color ChargeBlue = new Color32(0x4F, 0xC3, 0xF7, 0xFF);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";
        const string NumBlackPath = "Assets/Fonts/Montserrat-Black SDF.asset";
        const string RehabPoseDataPath = "Assets/Animations/RehabPoseData.asset";
        const string TrainerRigPath = "Assets/Characters/NewTrainerAnimated.fbx";

        static readonly string[] PanelNames =
        {
            "IntroPanel", "CalibPanel", "CountdownPanel", "HudPanel", "ResultsPanel",
            "PauseOverlay", "ExitButton", "ScreenGlowFx",
        };

        [MenuItem("Kinex/Build Battle Game UI")]
        public static void Build()
        {
            Debug.Log("[BattleGameUIBuilder] " + BuildUI());
        }

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<BattleDirector>();
            if (director == null) return "No BattleDirector in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var thaiBlack = LoadFont(ThaiBlackPath);
            var thaiSemi = LoadFont(ThaiSemiPath);
            var numBlack = LoadFont(NumBlackPath);

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i);
                foreach (var n in PanelNames)
                    if (child.name == n) { Object.DestroyImmediate(child.gameObject); break; }
            }

            // =================== INTRO ===================
            var intro = NewPanel(canvas.transform, "IntroPanel");
            AddCard(intro.transform, 74, 300, 780, 520);
            var introTitle = AddText(intro.transform, "ผู้พิทักษ์สวนสมดุล", thaiBlack, 84, FontStyles.Normal,
                                     Navy, TextAlignmentOptions.Center);
            Place(introTitle.rectTransform, 94, 340, 740, 190);
            var introSubtitle = AddText(intro.transform, "", thaiSemi, 46, FontStyles.Normal,
                                        new Color(0.25f, 0.2f, 0.1f), TextAlignmentOptions.Center);
            Place(introSubtitle.rectTransform, 114, 560, 700, 220);

            // =================== CALIB ===================
            var calib = NewPanel(canvas.transform, "CalibPanel");
            AddCard(calib.transform, 94, 500, 740, 300);
            var calibText = AddText(calib.transform, "ยืนตรงกลางให้เห็นเต็มตัว", thaiSemi, 52,
                                    FontStyles.Normal, Navy, TextAlignmentOptions.Center);
            Place(calibText.rectTransform, 134, 560, 660, 190);

            // =================== COUNTDOWN ===================
            var countdown = NewPanel(canvas.transform, "CountdownPanel");
            var countText = AddText(countdown.transform, "3", thaiBlack, 320, FontStyles.Normal,
                                    Gold, TextAlignmentOptions.Center);
            countText.enableWordWrapping = false;
            countText.overflowMode = TextOverflowModes.Overflow;
            Place(countText.rectTransform, 214, 500, 500, 420);
            Outline(countText, new Color(0.3f, 0.15f, 0f), 0.25f);

            // =================== HUD (persistent frame during battle) ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Monster nameplate + HP bar, top area (clear of the 234x300 camera feed top-right).
            AddCard(hud.transform, 40, 40, 560, 150);
            var monsterName = AddText(hud.transform, "มอนสเตอร์", thaiBlack, 42, FontStyles.Normal,
                                      Navy, TextAlignmentOptions.Left);
            Place(monsterName.rectTransform, 66, 56, 500, 50);
            var hpTrack = AddImage(hud.transform, "HpTrack", null, new Color(0f, 0f, 0f, 0.18f));
            Place(hpTrack.rectTransform, 66, 116, 480, 30);
            var hpFill = AddImage(hud.transform, "HpFill", null, WarmRed);
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillAmount = 1f;
            Place(hpFill.rectTransform, 66, 116, 480, 30);

            // -- Hearts row, top-left under the nameplate.
            var heartImgs = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                heartImgs[i] = AddImage(hud.transform, $"Heart{i + 1}", null, HeartRed);
                Round(heartImgs[i]);
                Place(heartImgs[i].rectTransform, 40 + i * 64, 210, 52, 52);
            }

            // -- Combo label, upper-middle.
            var comboLabel = AddText(hud.transform, "", thaiBlack, 56, FontStyles.Normal,
                                     Gold, TextAlignmentOptions.Center);
            Place(comboLabel.rectTransform, 84, 300, 760, 90);
            Outline(comboLabel, new Color(0.2f, 0.1f, 0f), 0.22f);

            // -- Charge meter (Lightning Charge / Focus Heal), right side mid-height.
            var chargeRoot = NewPanel(hud.transform, "ChargeMeterRoot");
            var chargeTrack = AddImage(chargeRoot.transform, "ChargeTrack", null, new Color(0f, 0f, 0f, 0.2f));
            Place(chargeTrack.rectTransform, 860, 500, 36, 260);
            var chargeFill = AddImage(chargeRoot.transform, "ChargeFill", null, ChargeBlue);
            chargeFill.type = Image.Type.Filled;
            chargeFill.fillMethod = Image.FillMethod.Vertical;
            chargeFill.fillOrigin = (int)Image.OriginVertical.Bottom;
            chargeFill.fillAmount = 0f;
            Place(chargeFill.rectTransform, 860, 500, 36, 260);
            chargeRoot.SetActive(false);

            // -- Pose card panel, lower-middle (big, senior-readable).
            var card = NewPanel(hud.transform, "CardPanel");
            var cardBadge = AddImage(card.transform, "CardBadge", null, ChargeBlue);
            Round(cardBadge);
            Place(cardBadge.rectTransform, 313, 900, 300, 300);
            var cardName = AddText(card.transform, "ท่าโจมตี", thaiBlack, 58, FontStyles.Normal,
                                   Color.white, TextAlignmentOptions.Center);
            Place(cardName.rectTransform, 163, 1000, 600, 100);
            Outline(cardName, new Color(0f, 0f, 0f, 0.55f), 0.22f);
            var cardTimerTrack = AddImage(card.transform, "CardTimerTrack", null, new Color(0f, 0f, 0f, 0.2f));
            Place(cardTimerTrack.rectTransform, 163, 1230, 600, 24);
            var cardTimerFill = AddImage(card.transform, "CardTimerFill", null, HpGreen);
            cardTimerFill.type = Image.Type.Filled;
            cardTimerFill.fillMethod = Image.FillMethod.Horizontal;
            cardTimerFill.fillAmount = 1f;
            Place(cardTimerFill.rectTransform, 163, 1230, 600, 24);
            // Glow ring behind the badge/text — alpha/color track detector progress ("getting warmer").
            var cardGlow = ScreenGlow.Create(card.GetComponent<RectTransform>(), "CardGlow");
            cardGlow.transform.SetAsFirstSibling();
            card.SetActive(false);

            // -- Enemy telegraph banner, upper-middle warning.
            var telegraph = NewPanel(hud.transform, "TelegraphPanel");
            var telegraphBg = AddImage(telegraph.transform, "Banner", null, new Color(0.7f, 0.1f, 0.1f, 0.85f));
            Round(telegraphBg);
            Place(telegraphBg.rectTransform, 133, 380, 660, 130);
            var telegraphText = AddText(telegraph.transform, "ระวัง!", thaiBlack, 70, FontStyles.Normal,
                                        Color.white, TextAlignmentOptions.Center);
            Stretch(telegraphText.rectTransform);
            telegraph.SetActive(false);

            // -- Enemy defend prompt, lower-middle (same slot as the pose card — never both shown).
            var defend = NewPanel(hud.transform, "DefendPanel");
            var defendBg = AddImage(defend.transform, "Banner", null, new Color(0.15f, 0.35f, 0.75f, 0.9f));
            Round(defendBg);
            Place(defendBg.rectTransform, 133, 950, 660, 220);
            var defendText = AddText(defend.transform, "ยืนขาเดียว!", thaiBlack, 56, FontStyles.Normal,
                                     Color.white, TextAlignmentOptions.Center);
            Place(defendText.rectTransform, 163, 990, 600, 120);
            var defendTimerTrack = AddImage(defend.transform, "DefendTimerTrack", null, new Color(0f, 0f, 0f, 0.25f));
            Place(defendTimerTrack.rectTransform, 163, 1120, 600, 20);
            var defendTimerFill = AddImage(defend.transform, "DefendTimerFill", null, Gold);
            defendTimerFill.type = Image.Type.Filled;
            defendTimerFill.fillMethod = Image.FillMethod.Horizontal;
            defendTimerFill.fillAmount = 1f;
            Place(defendTimerFill.rectTransform, 163, 1120, 600, 20);
            defend.SetActive(false);

            // -- One-time tutorial card (name + ghost pose + one-line instruction), auto-continues.
            var tutorial = NewPanel(hud.transform, "TutorialPanel");
            AddCard(tutorial.transform, 64, 460, 800, 380);
            var tutorialName = AddText(tutorial.transform, "ท่าใหม่!", thaiBlack, 62, FontStyles.Normal,
                                       Navy, TextAlignmentOptions.Center);
            Place(tutorialName.rectTransform, 104, 500, 720, 110);
            var tutorialInstruction = AddText(tutorial.transform, "", thaiSemi, 44, FontStyles.Normal,
                                              new Color(0.25f, 0.2f, 0.1f), TextAlignmentOptions.Center);
            Place(tutorialInstruction.rectTransform, 124, 630, 680, 180);
            tutorial.SetActive(false);

            // -- Big procedural checkmark (no font-glyph dependency) flashed on a Good/Perfect hit.
            var checkmark = BuildCheckmark(hud.transform, HpGreen, 260f);
            Place(checkmark.GetComponent<RectTransform>(), 333, 520, 260, 260);
            checkmark.SetActive(false);

            // =================== RESULTS ===================
            var results = NewPanel(canvas.transform, "ResultsPanel");
            results.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var resTitle = AddText(results.transform, "ชนะแล้ว!", thaiBlack, 120, FontStyles.Normal,
                                   Color.white, TextAlignmentOptions.Center);
            Place(resTitle.rectTransform, 114, 190, 700, 170);
            Outline(resTitle, Gold, 0.25f);
            var starImages = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                starImages[i] = AddImage(results.transform, $"Star{i + 1}", null, new Color(1f, 1f, 1f, 0.22f));
                Round(starImages[i]);
                Place(starImages[i].rectTransform, 288 + i * 140, 390, 110, 110);
            }
            var statsText = AddText(results.transform, "", thaiSemi, 46, FontStyles.Normal,
                                    Color.white, TextAlignmentOptions.Center);
            Place(statsText.rectTransform, 114, 560, 700, 400);
            var againBtn = AddPillButton(results.transform, "PlayAgainButton", "เล่นอีกครั้ง", thaiSemi, HpGreen,
                                         160, 1010, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(againBtn.onClick, director.PlayAgain);
            var homeBtn = AddPillButton(results.transform, "HomeButton", "กลับหน้าหลัก", thaiSemi, Navy,
                                        480, 1010, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(homeBtn.onClick, director.ExitToHome);

            // =================== PAUSE OVERLAY ===================
            var pause = NewPanel(canvas.transform, "PauseOverlay");
            pause.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            pause.GetComponent<Image>().raycastTarget = true;
            var pauseText = AddText(pause.transform, "ขยับให้เห็นเต็มตัวในกล้อง", thaiBlack, 62,
                                    FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
            Place(pauseText.rectTransform, 64, 600, 800, 200);

            // =================== EXIT (all states) ===================
            var exitBtn = AddPillButton(canvas.transform, "ExitButton", "ออก", thaiSemi, WarmRed,
                                        40, 44, 150, 92);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitBtn.onClick, director.ExitToHome);
            exitBtn.transform.SetAsLastSibling();
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed != null) feed.SetAsLastSibling();

            // -- Full-screen soft glow: green/amber on a hit, gentle red on a failed defend. Sits on
            // top of everything (raycastTarget is off, so it never blocks the exit/pause buttons).
            var screenGlow = ScreenGlow.Create(canvas.GetComponent<RectTransform>(), "ScreenGlowFx");
            screenGlow.transform.SetAsLastSibling();

            // ---- Rehab pose ghost assets — reused as-is from MegaDance/Kinex World, not authored
            // here. Missing either just disables the ghost; the pose card's text/TTS still works.
            var rehabPoseData = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(RehabPoseDataPath);
            var trainerRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrainerRigPath);
            if (rehabPoseData == null) Debug.LogWarning($"[BattleGameUIBuilder] {RehabPoseDataPath} not found — pose ghost disabled.");
            if (trainerRigPrefab == null) Debug.LogWarning($"[BattleGameUIBuilder] {TrainerRigPath} not found — pose ghost disabled.");

            // ---- Wire every director field. ----
            var so = new SerializedObject(director);
            AssignGo(so, "introPanel", intro);
            Assign(so, "introTitleText", introTitle);
            Assign(so, "introSubtitleText", introSubtitle);
            AssignGo(so, "calibPanel", calib);
            Assign(so, "calibText", calibText);
            AssignGo(so, "countdownPanel", countdown);
            Assign(so, "countdownText", countText);
            AssignGo(so, "hudPanel", hud);
            Assign(so, "monsterNameText", monsterName);
            Assign(so, "monsterHpFill", hpFill);
            Assign(so, "comboText", comboLabel);
            AssignGo(so, "chargeMeterRoot", chargeRoot);
            Assign(so, "chargeMeterFill", chargeFill);
            AssignGo(so, "cardPanel", card);
            Assign(so, "cardNameText", cardName);
            Assign(so, "cardBadge", cardBadge);
            Assign(so, "cardTimerFill", cardTimerFill);
            AssignGo(so, "telegraphPanel", telegraph);
            Assign(so, "telegraphText", telegraphText);
            AssignGo(so, "defendPanel", defend);
            Assign(so, "defendText", defendText);
            Assign(so, "defendTimerFill", defendTimerFill);
            AssignGo(so, "resultsPanel", results);
            Assign(so, "resultsStatsText", statsText);
            AssignGo(so, "pauseOverlay", pause);
            Assign(so, "cardGlow", cardGlow);
            Assign(so, "screenGlow", screenGlow);
            AssignGo(so, "successCheckmark", checkmark);
            AssignGo(so, "tutorialPanel", tutorial);
            Assign(so, "tutorialNameText", tutorialName);
            Assign(so, "tutorialInstructionText", tutorialInstruction);
            Assign(so, "rehabPoseData", rehabPoseData);
            AssignGo(so, "trainerRigPrefab", trainerRigPrefab);

            var heartsProp = so.FindProperty("heartImages");
            heartsProp.arraySize = 3;
            for (int i = 0; i < 3; i++) heartsProp.GetArrayElementAtIndex(i).objectReferenceValue = heartImgs[i];

            var starsProp = so.FindProperty("resultsStarImages");
            starsProp.arraySize = 3;
            for (int i = 0; i < 3; i++) starsProp.GetArrayElementAtIndex(i).objectReferenceValue = starImages[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            intro.SetActive(true);
            calib.SetActive(false); countdown.SetActive(false); hud.SetActive(false); results.SetActive(false);
            pause.SetActive(false);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Battle Game UI rebuilt and wired to BattleDirector.";
        }

        // ---- helpers (FruitGameUIBuilder pattern) ----------------------------------

        static TMP_FontAsset LoadFont(string path)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) Debug.LogWarning($"[BattleGameUIBuilder] Font not found: {path} (using TMP default)");
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
            var text = AddText(img.transform, label, font, 42, FontStyles.Bold, Color.white,
                               TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return btn;
        }

        // Procedural checkmark (two rotated rounded bars) — avoids depending on the check-mark
        // glyph (U+2713) existing in the Thai/Latin SDF font atlases, which isn't guaranteed.
        static GameObject BuildCheckmark(Transform parent, Color color, float size)
        {
            var root = new GameObject("Checkmark", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(size, size);

            var shortLeg = AddImage(root.transform, "Short", null, color);
            Round(shortLeg);
            CenterAnchor(shortLeg.rectTransform);
            shortLeg.rectTransform.sizeDelta = new Vector2(size * 0.16f, size * 0.42f);
            shortLeg.rectTransform.anchoredPosition = new Vector2(-size * 0.16f, -size * 0.02f);
            shortLeg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var longLeg = AddImage(root.transform, "Long", null, color);
            Round(longLeg);
            CenterAnchor(longLeg.rectTransform);
            longLeg.rectTransform.sizeDelta = new Vector2(size * 0.16f, size * 0.78f);
            longLeg.rectTransform.anchoredPosition = new Vector2(size * 0.12f, size * 0.12f);
            longLeg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);

            return root;
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

        // Center-point anchor so anchoredPosition offsets are measured from the parent's middle
        // (used for the checkmark's two bars, positioned as offsets from the icon's center).
        static void CenterAnchor(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        static void Assign(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning($"[BattleGameUIBuilder] Director field not found: {field}");
        }

        static void AssignGo(SerializedObject so, string field, GameObject value) => Assign(so, field, value);
    }
}
#endif
