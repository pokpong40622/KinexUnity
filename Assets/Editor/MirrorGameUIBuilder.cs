#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.MirrorGame;
using Kinex.Trainer;

namespace Kinex.MirrorGame.EditorTools
{
    /// <summary>
    /// One-click builder for the Magic Mirror UI — same procedural-canvas + SerializedObject-wiring
    /// pattern as BattleGameUIBuilder/FruitGameUIBuilder. Re-runnable: destroys its own panels by
    /// name and rebuilds, then wires every MirrorGameDirector field. Big Thai text (≥40px), a pose
    /// name card on top, a central hold ring, a mode-toggle button, and a star/coin results panel.
    /// </summary>
    public static class MirrorGameUIBuilder
    {
        const float FW = 927f, FH = 1427f;

        static readonly Color Ink = new Color32(0x2A, 0x22, 0x3A, 0xFF);
        static readonly Color CardWhite = new Color32(0xFF, 0xFF, 0xFF, 0xF0);
        static readonly Color Gold = new Color32(0xFF, 0xD1, 0x55, 0xFF);
        static readonly Color RingDim = new Color32(0x00, 0x00, 0x00, 0x55);
        static readonly Color Violet = new Color32(0x6B, 0x4A, 0x9E, 0xFF);
        static readonly Color GreenGo = new Color32(0x4C, 0xAF, 0x50, 0xFF);

        const string ThaiBlackPath = "Assets/Fonts/FCIconic-Black SDF.asset";
        const string ThaiSemiPath = "Assets/Fonts/FCIconic-SemiBold SDF.asset";
        const string RehabPoseDataPath = "Assets/Animations/RehabPoseData.asset";
        // The ghost must be the rig RehabPoseData was baked on — see MirrorOutline's class doc.
        const string GhostRigPath = "Assets/Characters/NewTrainerAnimated.fbx";

        static readonly string[] PanelNames = { "IntroPanel", "HudPanel", "ResultsPanel", "ExitButton" };

        [MenuItem("Kinex/Build Mirror Game UI")]
        public static void Build() => Debug.Log("[MirrorGameUIBuilder] " + BuildUI());

        public static string BuildUI()
        {
            var director = Object.FindAnyObjectByType<MirrorGameDirector>();
            if (director == null) return "No MirrorGameDirector in the open scene. Run the scene builder first.";
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

            // =================== INTRO ===================
            var intro = NewPanel(canvas.transform, "IntroPanel");
            AddCard(intro.transform, 74, 320, 780, 520);
            var introTitle = AddText(intro.transform, "กระจกวิเศษ", thaiBlack, 96, Ink, TextAlignmentOptions.Center);
            Place(introTitle.rectTransform, 94, 370, 740, 180);
            var introSubtitle = AddText(intro.transform, "", thaiSemi, 46, new Color(0.3f, 0.24f, 0.16f), TextAlignmentOptions.Center);
            Place(introSubtitle.rectTransform, 114, 560, 700, 240);

            // =================== HUD ===================
            var hud = NewPanel(canvas.transform, "HudPanel");

            // -- Pose card, top-left (clear of the 234-wide camera feed on the top-right):
            //    count + name + HOW-TO instruction. The instruction line is the senior-UX fix —
            //    pose names alone ("หมุนศีรษะ • 1/4") told players nothing about what to do. --
            // Card must end left of the camera feed (feed starts at x=648 — see BuildCameraFeed).
            AddCard(hud.transform, 40, 40, 590, 300);
            var poseCount = AddText(hud.transform, "ท่าที่ 1/25", thaiSemi, 44, Violet, TextAlignmentOptions.Left);
            Place(poseCount.rectTransform, 66, 56, 520, 56);
            var poseName = AddText(hud.transform, "", thaiBlack, 52, Ink, TextAlignmentOptions.Left);
            Place(poseName.rectTransform, 66, 112, 540, 88);
            var poseInstruction = AddText(hud.transform, "", thaiSemi, 40, new Color(0.28f, 0.22f, 0.14f), TextAlignmentOptions.TopLeft);
            Place(poseInstruction.rectTransform, 66, 204, 540, 122);

            // -- First-pose mechanic hint, centre (above the hold ring), gold on dark outline. --
            var hint = AddText(hud.transform, "ขยับตามเงาสีทอง ให้วงกลมทุกวงเป็นสีเขียว แล้วค้างไว้",
                               thaiSemi, 42, Gold, TextAlignmentOptions.Center);
            Place(hint.rectTransform, 74, 840, 780, 110);
            Outline(hint, new Color(0f, 0f, 0f, 0.7f), 0.25f);

            // -- Hold ring, centre-bottom. --
            var holdLabel = AddText(hud.transform, "ค้างท่าไว้", thaiSemi, 42, Color.white, TextAlignmentOptions.Center);
            Place(holdLabel.rectTransform, 313, 940, 300, 60);
            Outline(holdLabel, new Color(0f, 0f, 0f, 0.5f), 0.2f);
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var ringTrack = AddImage(hud.transform, "HoldRingTrack", knob, RingDim);
            Place(ringTrack.rectTransform, 353, 1010, 220, 220);
            var ringFill = AddImage(hud.transform, "HoldRingFill", knob, Gold);
            ringFill.type = Image.Type.Filled;
            ringFill.fillMethod = Image.FillMethod.Radial360;
            ringFill.fillOrigin = (int)Image.Origin360.Top;
            ringFill.fillClockwise = true;
            ringFill.fillAmount = 0f;
            Place(ringFill.rectTransform, 353, 1010, 220, 220);

            // -- Mode toggle button, bottom-left. --
            var modeBtn = AddImage(hud.transform, "ModeToggleButton", null, Violet);
            Round(modeBtn);
            modeBtn.raycastTarget = true;
            Place(modeBtn.rectTransform, 40, 1300, 300, 96);
            var modeButton = modeBtn.gameObject.AddComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(modeButton.onClick, director.ToggleMode);
            var modeLabel = AddText(modeBtn.transform, "โหมดกำแพง", thaiSemi, 40, Color.white, TextAlignmentOptions.Center);
            Stretch(modeLabel.rectTransform);

            // =================== RESULTS ===================
            var results = NewPanel(canvas.transform, "ResultsPanel");
            results.GetComponent<Image>().color = new Color(0.06f, 0.04f, 0.10f, 0.7f);
            var resTitle = AddText(results.transform, "เยี่ยมมาก!", thaiBlack, 120, Color.white, TextAlignmentOptions.Center);
            Place(resTitle.rectTransform, 114, 200, 700, 170);
            Outline(resTitle, Gold, 0.25f);
            var starImages = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                starImages[i] = AddImage(results.transform, $"Star{i + 1}", knob, StarOff());
                Place(starImages[i].rectTransform, 288 + i * 140, 400, 110, 110);
            }
            var statsText = AddText(results.transform, "", thaiSemi, 50, Color.white, TextAlignmentOptions.Center);
            Place(statsText.rectTransform, 114, 580, 700, 360);
            var againBtn = AddPillButton(results.transform, "PlayAgainButton", "เล่นอีกครั้ง", thaiSemi, GreenGo, 160, 1010, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(againBtn.onClick, director.PlayAgain);
            var homeBtn = AddPillButton(results.transform, "HomeButton", "กลับหน้าหลัก", thaiSemi, Violet, 480, 1010, 300, 110);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(homeBtn.onClick, director.ExitToHome);

            // =================== EXIT (all states) ===================
            var exitBtn = AddPillButton(canvas.transform, "ExitButton", "ออก", thaiSemi, new Color32(0xE0, 0x5A, 0x4E, 0xFF), 40, 44, 150, 92);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(exitBtn.onClick, director.ExitToHome);
            exitBtn.transform.SetAsLastSibling();
            var feed = canvas.transform.Find("CameraFeedPanel");
            if (feed != null) feed.SetAsLastSibling();

            // ---- Ghost assets (reused as-is). Ghost rig = the PLAYER prefab, not the trainer FBX. ----
            var rehabPoseData = AssetDatabase.LoadAssetAtPath<TrainerPoseData>(RehabPoseDataPath);
            var ghostRig = AssetDatabase.LoadAssetAtPath<GameObject>(GhostRigPath);
            if (rehabPoseData == null) Debug.LogWarning($"[MirrorGameUIBuilder] {RehabPoseDataPath} not found — ghost disabled.");
            if (ghostRig == null) Debug.LogWarning($"[MirrorGameUIBuilder] {GhostRigPath} not found — ghost disabled.");

            // ---- Wire the director. ----
            var so = new SerializedObject(director);
            AssignGo(so, "introPanel", intro);
            Assign(so, "introTitleText", introTitle);
            Assign(so, "introSubtitleText", introSubtitle);
            AssignGo(so, "hudPanel", hud);
            Assign(so, "poseCountText", poseCount);
            Assign(so, "poseNameText", poseName);
            Assign(so, "poseInstructionText", poseInstruction);
            Assign(so, "firstPoseHintText", hint);
            Assign(so, "holdRingFill", ringFill);
            Assign(so, "modeToggleLabel", modeLabel);
            AssignGo(so, "resultsPanel", results);
            Assign(so, "resultsStatsText", statsText);
            Assign(so, "rehabPoseData", rehabPoseData);
            AssignGo(so, "ghostRigPrefab", ghostRig);

            // Scene was baked with the old 2.2s announce — push the new senior-readable pace in.
            var announceProp = so.FindProperty("announceSeconds");
            if (announceProp != null) announceProp.floatValue = 3.5f;

            var starsProp = so.FindProperty("resultsStarImages");
            starsProp.arraySize = 3;
            for (int i = 0; i < 3; i++) starsProp.GetArrayElementAtIndex(i).objectReferenceValue = starImages[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            intro.SetActive(true);
            hud.SetActive(false);
            results.SetActive(false);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(canvas.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            return "Mirror Game UI rebuilt and wired to MirrorGameDirector.";
        }

        // ---- helpers (BattleGameUIBuilder pattern) ----
        static Color StarOff() => new Color(1f, 1f, 1f, 0.22f);

        static TMP_FontAsset LoadFont(string path)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) Debug.LogWarning($"[MirrorGameUIBuilder] Font not found: {path} (using TMP default)");
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
            else Debug.LogWarning($"[MirrorGameUIBuilder] Director field not found: {field}");
        }

        static void AssignGo(SerializedObject so, string field, GameObject value) => Assign(so, field, value);
    }
}
#endif
