#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.TempleHunt;

namespace Kinex.TempleHunt.EditorTools
{
    /// <summary>
    /// One-click builder for the Temple Hunt UI — same procedural-canvas + SerializedObject-wiring
    /// pattern as MirrorGameUIBuilder. Re-runnable: destroys its own panels by name and rebuilds,
    /// then wires every TempleHuntDirector field. Big Thai text (≥40px), a chamber banner card
    /// clear of the camera feed, gate pips, a balance meter, the parrot guide "โปโล่" with its
    /// speech bubble (canvas-level, visible in every state), an amber safety overlay, and a
    /// victory panel with the explorer badge.
    /// </summary>
    public static class TempleHuntUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        static readonly Color Ink = new Color32(0x33, 0x26, 0x1A, 0xFF);
        static readonly Color CardWhite = new Color32(0xFF, 0xFA, 0xEE, 0xF0);
        static readonly Color Gold = new Color32(0xFF, 0xD1, 0x55, 0xFF);
        static readonly Color Amber = new Color(1f, 0.72f, 0.25f, 0.55f);
        static readonly Color Jungle = new Color32(0x2E, 0x7D, 0x32, 0xFF);
        static readonly Color GreenGo = new Color32(0x4C, 0xAF, 0x50, 0xFF);
        static readonly Color TerraCotta = new Color32(0xB5, 0x6A, 0x3C, 0xFF);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";

        static readonly string[] PanelNames =
        {
            "IntroPanel", "CalibPanel", "HudPanel", "GuidePanel", "PauseOverlay", "VictoryPanel", "ExitButton",
        };

        [MenuItem("Kinex/Build Temple Hunt UI")]
        public static void Build() => Debug.Log("[TempleHuntUIBuilder] " + BuildUI());

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<TempleHuntDirector>();
            if (director == null) return "No TempleHuntDirector in the open scene. Run the scene builder first.";
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return "No Canvas in scene.";

            var thaiBlack = LoadFont(ThaiBlackPath);
            var thaiSemi = LoadFont(ThaiSemiPath);
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var check = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");

            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvas.transform.GetChild(i);
                foreach (var n in PanelNames)
                    if (child.name == n) { Object.DestroyImmediate(child.gameObject); break; }
            }

            // =================== INTRO ===================
            var intro = NewPanel(canvas.transform, "IntroPanel");
            AddCard(intro.transform, 74, 260, 780, 620);
            var introTitle = AddText(intro.transform, "ล่าสมบัติวิหารโบราณ", thaiBlack, 82, Ink, TextAlignmentOptions.Center);
            Place(introTitle.rectTransform, 94, 310, 740, 150);
            var introSubtitle = AddText(intro.transform,
                "ผจญภัยผ่านห้องลับ 4 ห้อง\nออกกำลังตามโปโล่ นกแก้วนำทาง\nค่อยๆ ทำ ไม่มีจับเวลา",
                thaiSemi, 46, new Color(0.32f, 0.25f, 0.16f), TextAlignmentOptions.Center);
            Place(introSubtitle.rectTransform, 114, 480, 700, 240);
            var startBtn = AddPillButton(intro.transform, "StartButton", "เริ่มผจญภัย", thaiSemi, GreenGo, 288, 740, 350, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(startBtn.onClick, director.SkipIntro);

            // =================== CALIBRATION ===================
            var calib = NewPanel(canvas.transform, "CalibPanel");
            calib.GetComponent<Image>().color = new Color(0.07f, 0.06f, 0.04f, 0.85f);
            AddCard(calib.transform, 74, 280, 780, 640);
            var calibTitle = AddText(calib.transform, "เตรียมตัวก่อนเริ่ม", thaiBlack, 66, Ink, TextAlignmentOptions.Center);
            Place(calibTitle.rectTransform, 94, 320, 740, 110);
            var calibBody = AddText(calib.transform, "", thaiSemi, 46, new Color(0.32f, 0.25f, 0.16f), TextAlignmentOptions.Center);
            Place(calibBody.rectTransform, 114, 440, 700, 200);
            var calibSafety = AddText(calib.transform, "หากเหนื่อยหรือเวียนศีรษะ หยุดพักได้ทุกเมื่อ",
                                      thaiSemi, 40, TerraCotta, TextAlignmentOptions.Center);
            Place(calibSafety.rectTransform, 114, 650, 700, 70);
            var calibTrack = AddImage(calib.transform, "CalibRingTrack", knob, new Color(0f, 0f, 0f, 0.3f));
            Place(calibTrack.rectTransform, 383, 740, 160, 160);
            var calibFill = AddImage(calib.transform, "CalibRingFill", knob, GreenGo);
            calibFill.type = Image.Type.Filled;
            calibFill.fillMethod = Image.FillMethod.Radial360;
            calibFill.fillOrigin = (int)Image.Origin360.Top;
            calibFill.fillClockwise = true;
            calibFill.fillAmount = 0f;
            Place(calibFill.rectTransform, 383, 740, 160, 160);

            // =================== HUD ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Chamber banner card, top-left (must end left of the camera feed at x=648).
            //    The chamber count sits right of the exit pill, which overlaps the card corner. --
            AddCard(hud.transform, 40, 40, 590, 350);
            var chamberCount = AddText(hud.transform, "ห้องที่ 1/4", thaiSemi, 42, Jungle, TextAlignmentOptions.Left);
            Place(chamberCount.rectTransform, 210, 60, 396, 52);
            var chamberTitle = AddText(hud.transform, "", thaiBlack, 56, Ink, TextAlignmentOptions.Left);
            Place(chamberTitle.rectTransform, 66, 124, 540, 92);
            var instruction = AddText(hud.transform, "", thaiSemi, 40, new Color(0.3f, 0.23f, 0.14f), TextAlignmentOptions.TopLeft);
            Place(instruction.rectTransform, 66, 222, 540, 150);

            // -- Gate pips (Chamber 2 only), under the banner card. --
            var gatePipsPanel = NewPanel(hud.transform, "GatePipsPanel");
            var gatePips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                gatePips[i] = AddImage(gatePipsPanel.transform, $"GatePip{i + 1}", knob, new Color(1f, 1f, 1f, 0.25f));
                Place(gatePips[i].rectTransform, 56 + i * 84, 406, 64, 64);
            }
            gatePipsPanel.SetActive(false);

            // -- Balance meter (Chambers 3-4 only), below the camera feed (feed ends at y=355). --
            var meterPanel = NewPanel(hud.transform, "BalanceMeterPanel");
            var meterLabel = AddText(meterPanel.transform, "ความนิ่ง", thaiSemi, 38, Color.white, TextAlignmentOptions.Left);
            Place(meterLabel.rectTransform, 124, 410, 220, 54);
            Outline(meterLabel, new Color(0f, 0f, 0f, 0.6f), 0.22f);
            var meterBg = AddImage(meterPanel.transform, "BalanceMeterBg", null, new Color(0f, 0f, 0f, 0.45f));
            Round(meterBg);
            Place(meterBg.rectTransform, 114, 466, 700, 46);
            var meterFill = AddImage(meterPanel.transform, "BalanceMeterFill", null, GreenGo);
            Round(meterFill);
            meterFill.type = Image.Type.Filled;
            meterFill.fillMethod = Image.FillMethod.Horizontal;
            meterFill.fillAmount = 1f;
            Place(meterFill.rectTransform, 120, 472, 688, 34);
            meterPanel.SetActive(false);

            // -- Rest countdown (between gates) + skip button, centre. --
            var restText = AddText(hud.transform, "", thaiSemi, 50, Gold, TextAlignmentOptions.Center);
            Place(restText.rectTransform, 114, 540, 700, 90);
            Outline(restText, new Color(0f, 0f, 0f, 0.7f), 0.25f);
            restText.gameObject.SetActive(false);
            var skipRestBtn = AddPillButton(hud.transform, "SkipRestButton", "พร้อมแล้ว ไปต่อ", thaiSemi, Jungle, 313, 650, 300, 100);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(skipRestBtn.onClick, director.SkipRest);
            skipRestBtn.gameObject.SetActive(false);

            // =================== PARROT GUIDE (canvas-level: talks in every state) ===================
            var guide = NewPanel(canvas.transform, "GuidePanel");
            var parrot = BuildParrot(guide, knob, thaiSemi);

            // =================== SAFETY PAUSE OVERLAY ===================
            var pause = NewPanel(canvas.transform, "PauseOverlay");
            var pauseImg = pause.GetComponent<Image>();
            pauseImg.color = Amber;
            pauseImg.raycastTarget = true; // soak taps while paused
            var pauseCard = AddCard(pause.transform, 94, 480, 740, 360);
            var pauseTitle = AddText(pause.transform, "พักก่อนนะ", thaiBlack, 72, Ink, TextAlignmentOptions.Center);
            Place(pauseTitle.rectTransform, 114, 520, 700, 120);
            var pauseText = AddText(pause.transform, "", thaiSemi, 48, new Color(0.32f, 0.25f, 0.16f), TextAlignmentOptions.Center);
            Place(pauseText.rectTransform, 114, 650, 700, 170);
            pause.SetActive(false);

            // =================== VICTORY ===================
            var victory = NewPanel(canvas.transform, "VictoryPanel");
            victory.GetComponent<Image>().color = new Color(0.07f, 0.05f, 0.03f, 0.78f);
            var vTitle = AddText(victory.transform, "พบสมบัติแล้ว!", thaiBlack, 110, Color.white, TextAlignmentOptions.Center);
            Place(vTitle.rectTransform, 74, 170, 780, 170);
            Outline(vTitle, Gold, 0.25f);

            // Explorer badge: gold ring + parchment disc + big checkmark.
            var badgeRing = AddImage(victory.transform, "BadgeRing", knob, Gold);
            Place(badgeRing.rectTransform, 353, 370, 220, 220);
            var badgeDisc = AddImage(victory.transform, "BadgeDisc", knob, new Color32(0x6B, 0x4A, 0x28, 0xFF));
            Place(badgeDisc.rectTransform, 371, 388, 184, 184);
            var badgeMark = AddImage(victory.transform, "BadgeMark", check, Gold);
            Place(badgeMark.rectTransform, 398, 415, 130, 130);
            var badgeLabel = AddText(victory.transform, "นักสำรวจวิหารทอง", thaiSemi, 46, Gold, TextAlignmentOptions.Center);
            Place(badgeLabel.rectTransform, 214, 606, 500, 74);
            Outline(badgeLabel, new Color(0f, 0f, 0f, 0.6f), 0.2f);

            for (int i = 0; i < 3; i++)
            {
                var star = AddImage(victory.transform, $"Star{i + 1}", knob, Gold);
                Place(star.rectTransform, 308 + i * 120, 700, 90, 90);
            }

            var vStats = AddText(victory.transform, "", thaiSemi, 46, Color.white, TextAlignmentOptions.Center);
            Place(vStats.rectTransform, 74, 820, 780, 230);
            var againBtn = AddPillButton(victory.transform, "PlayAgainButton", "เล่นอีกครั้ง", thaiSemi, GreenGo, 150, 1090, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(againBtn.onClick, director.PlayAgain);
            var homeBtn = AddPillButton(victory.transform, "HomeButton", "กลับหน้าหลัก", thaiSemi, TerraCotta, 480, 1090, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(homeBtn.onClick, director.ExitToHome);
            victory.SetActive(false);

            // =================== EXIT (all states) ===================
            var exitBtn = AddPillButton(canvas.transform, "ExitButton", "ออก", thaiSemi, new Color32(0xE0, 0x5A, 0x4E, 0xFF), 40, 44, 150, 92);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitBtn.onClick, director.ExitToHome);
            exitBtn.transform.SetAsLastSibling();
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed != null) feed.SetAsLastSibling();

            // ---- Wire the director. ----
            var so = new SerializedObject(director);
            AssignGo(so, "introPanel", intro);
            AssignGo(so, "calibPanel", calib);
            AssignGo(so, "hudPanel", hud);
            AssignGo(so, "pauseOverlay", pause);
            AssignGo(so, "victoryPanel", victory);
            Assign(so, "calibText", calibBody);
            Assign(so, "calibProgressFill", calibFill);
            Assign(so, "chamberTitleText", chamberTitle);
            Assign(so, "chamberCountText", chamberCount);
            Assign(so, "instructionText", instruction);
            Assign(so, "restText", restText);
            AssignGo(so, "skipRestButton", skipRestBtn.gameObject);
            AssignGo(so, "balanceMeterPanel", meterPanel);
            Assign(so, "balanceMeterFill", meterFill);
            AssignGo(so, "gatePipsPanel", gatePipsPanel);
            Assign(so, "pauseText", pauseText);
            Assign(so, "victoryStatsText", vStats);
            Assign(so, "parrot", parrot);

            var pipsProp = so.FindProperty("gatePips");
            pipsProp.arraySize = 3;
            for (int i = 0; i < 3; i++) pipsProp.GetArrayElementAtIndex(i).objectReferenceValue = gatePips[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            intro.SetActive(true);
            calib.SetActive(false);
            hud.SetActive(false);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Temple Hunt UI rebuilt and wired to TempleHuntDirector.";
        }

        // ---- The parrot mascot "โปโล่": a few tinted circles + a speech bubble, bottom-left. ----
        static ParrotGuide BuildParrot(GameObject guidePanel, Sprite knob, TMP_FontAsset thaiSemi)
        {
            var bodyGreen = new Color32(0x3F, 0xA3, 0x4D, 0xFF);
            var bellyGreen = new Color32(0xA8, 0xD9, 0x7F, 0xFF);
            var wingGreen = new Color32(0x2E, 0x7D, 0x32, 0xFF);
            var beakOrange = new Color32(0xF4, 0x97, 0x2C, 0xFF);
            var crestRed = new Color32(0xE0, 0x5A, 0x4E, 0xFF);

            // Body root — the ParrotGuide bobs this whole cluster.
            var bodyGo = new GameObject("ParrotBody", typeof(RectTransform));
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.SetParent(guidePanel.transform, false);
            // Left edge, above the runtime GameHud heart-rate ring (bottom-left corner).
            Place(bodyRt, 28, 920, 160, 200);

            AddLocal(bodyRt, "Crest", knob, crestRed, new Vector2(-14f, 92f), new Vector2(30f, 44f), -14f);
            AddLocal(bodyRt, "Body", knob, bodyGreen, new Vector2(0f, -6f), new Vector2(126f, 148f), 0f);
            AddLocal(bodyRt, "Wing", knob, wingGreen, new Vector2(-40f, -20f), new Vector2(56f, 88f), 18f);
            AddLocal(bodyRt, "Belly", knob, bellyGreen, new Vector2(10f, -32f), new Vector2(72f, 86f), 0f);
            AddLocal(bodyRt, "EyeWhite", knob, Color.white, new Vector2(28f, 42f), new Vector2(38f, 38f), 0f);
            AddLocal(bodyRt, "EyePupil", knob, new Color32(0x22, 0x18, 0x10, 0xFF), new Vector2(32f, 42f), new Vector2(17f, 17f), 0f);
            AddLocal(bodyRt, "Beak", knob, beakOrange, new Vector2(58f, 30f), new Vector2(40f, 26f), -12f);

            // Speech bubble to the right of the parrot.
            var bubbleImg = AddImage(guidePanel.transform, "ParrotBubble", null, CardWhite);
            Round(bubbleImg);
            Place(bubbleImg.rectTransform, 196, 890, 330, 210);
            var bubbleText = AddText(bubbleImg.transform, "", thaiSemi, 36, Ink, TextAlignmentOptions.Center);
            Stretch(bubbleText.rectTransform);
            bubbleText.rectTransform.offsetMin = new Vector2(18f, 14f);
            bubbleText.rectTransform.offsetMax = new Vector2(-18f, -14f);

            var parrot = guidePanel.AddComponent<ParrotGuide>();
            parrot.body = bodyRt;
            parrot.bubble = bubbleImg.gameObject;
            parrot.bubbleText = bubbleText;
            return parrot;
        }

        static void AddLocal(RectTransform parent, string name, Sprite sprite, Color color,
                             Vector2 pos, Vector2 size, float rotZ)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
        }

        // ---- helpers (MirrorGameUIBuilder pattern) ----

        static TMP_FontAsset LoadFont(string path)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) Debug.LogWarning($"[TempleHuntUIBuilder] Font not found: {path} (using TMP default)");
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
            else Debug.LogWarning($"[TempleHuntUIBuilder] Director field not found: {field}");
        }

        static void AssignGo(SerializedObject so, string field, GameObject value) => Assign(so, field, value);
    }
}
#endif
