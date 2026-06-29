#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

namespace Kinex.EditorTools
{
    /// <summary>
    /// Thai localization for the Unity in-game UI. Two steps (run in order):
    ///   1. "Kinex/Localize/1 Generate FC Iconic Fonts" — builds TMP font assets from the FC Iconic
    ///      TTFs with the Thai + Latin glyphs baked into the atlas (so they render Thai in a build),
    ///      then sets them as the TMP default + fallback.
    ///   2. "Kinex/Localize/2 Localize Scenes" — opens the MegaDance + Kinex World scenes, points every
    ///      TMP_Text at the matching FC Iconic weight, and replaces English text with Thai (dictionary).
    /// Surgical: only touches fonts + text, not layout/wiring. THROWAWAY — delete before final commit.
    /// </summary>
    public static class ThaiLocalize
    {
        const string FontDir = "Assets/Fonts/";
        const string BlackSrc = FontDir + "FCIconic-Black.ttf";
        const string SemiSrc = FontDir + "FCIconic-SemiBold.ttf";
        const string ItalicSrc = FontDir + "FCIconic-BlackItalic.ttf";
        const string RegularSrc = FontDir + "FCIconic-Regular.ttf";
        const string BlackAsset = FontDir + "FCIconic-Black SDF.asset";
        const string SemiAsset = FontDir + "FCIconic-SemiBold SDF.asset";
        const string ItalicAsset = FontDir + "FCIconic-BlackItalic SDF.asset";
        const string RegularAsset = FontDir + "FCIconic-Regular SDF.asset";

        static readonly string[] ScenePaths =
        {
            "Assets/Scenes/MegaDanceScene.unity",
            "Assets/Scenes/KinexWorldScene.unity",
        };

        // Every glyph the UI can show: printable ASCII + the Thai unicode block + ellipsis/dashes.
        static string CharSet()
        {
            var sb = new StringBuilder();
            for (int c = 0x20; c <= 0x7E; c++) sb.Append((char)c);          // ASCII printable
            for (int c = 0x0E01; c <= 0x0E3A; c++) sb.Append((char)c);      // Thai letters + vowels
            for (int c = 0x0E3F; c <= 0x0E5B; c++) sb.Append((char)c);      // Thai symbols + tone marks
            sb.Append('…').Append('—').Append('•').Append('✓');
            return sb.ToString();
        }

        [MenuItem("Kinex/Localize/1 Generate FC Iconic Fonts")]
        public static void GenerateFonts()
        {
            string chars = CharSet();
            var black = BuildFont(BlackSrc, BlackAsset, chars);
            var semi = BuildFont(SemiSrc, SemiAsset, chars);
            BuildFont(ItalicSrc, ItalicAsset, chars);
            BuildFont(RegularSrc, RegularAsset, chars);

            // Default + fallback so runtime-created TMP texts (hints, "Correct!") render Thai too.
            if (semi != null)
            {
                var settings = TMP_Settings.instance;
                if (settings != null)
                {
                    var so = new SerializedObject(settings);
                    var def = so.FindProperty("m_defaultFontAsset");
                    if (def != null) { def.objectReferenceValue = semi; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); }
                }
                var fb = new List<TMP_FontAsset>();
                if (black != null) fb.Add(black);
                fb.Add(semi);
                if (semi.fallbackFontAssetTable == null) semi.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (black != null && !semi.fallbackFontAssetTable.Contains(black)) semi.fallbackFontAssetTable.Add(black);
                EditorUtility.SetDirty(semi);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ThaiLocalize] Fonts generated (FC Iconic Black/SemiBold/BlackItalic/Regular) + TMP default set.");
        }

        static TMP_FontAsset BuildFont(string ttfPath, string assetPath, string chars)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null) { Debug.LogError("[ThaiLocalize] Missing TTF: " + ttfPath); return null; }

            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                   AtlasPopulationMode.Dynamic, true);
            if (fa == null) { Debug.LogError("[ThaiLocalize] CreateFontAsset failed for " + ttfPath); return null; }
            fa.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            // Persist, then bake the glyphs into the atlas and add atlas/material as sub-assets.
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.TryAddCharacters(chars, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[ThaiLocalize] {fa.name}: {missing.Length} chars not in this TTF (ok if just a few).");

            fa.material.name = fa.name + " Material";
            if (!AssetDatabase.Contains(fa.material)) AssetDatabase.AddObjectToAsset(fa.material, fa);
            foreach (var tex in fa.atlasTextures)
                if (tex != null && !AssetDatabase.Contains(tex)) AssetDatabase.AddObjectToAsset(tex, fa);

            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }

        // English → Thai for the in-scene TMP texts. Exact-match (incl. newlines). Anything not listed
        // keeps its current text (caught later via screenshots). Numbers/brand stay as-is.
        static readonly Dictionary<string, string> Map = new()
        {
            { "Start", "เริ่ม" },
            { "START", "เริ่ม" },
            { "Calibrate", "ปรับเทียบ" },
            { "CALIBRATION", "ปรับเทียบ" },
            { "Do this pose", "ทำท่านี้" },
            { "Correct!", "ถูกต้อง!" },
            { "Complete!", "สำเร็จ!" },
            { "Close", "ปิด" },
            { "Daily Class", "คลาสประจำวัน" },
            { "LIVE SCORE", "คะแนนสด" },
            { "DONE", "เสร็จสิ้น" },
            { "Exercise", "ท่าออกกำลังกาย" },
            { "AVERAGE PERFORMANCE", "คะแนนเฉลี่ย" },
            { "GET READY", "เตรียมตัว" },
            { "UP NEXT", "ถัดไป" },
            { "AGAIN", "เล่นอีกครั้ง" },
            { "Pose 1", "ท่าที่ 1" },
            { "CLASS\nCOMPLETE!", "จบ\nคลาส!" },
            { "CLASS COMPLETE!", "จบคลาส!" },
            { "Stand in a T-pose, then press Calibrate.", "ยืนท่าตัว T แล้วกดปรับเทียบ" },
            { "Stand in a T-pose\narms straight out to the sides", "ยืนท่าตัว T\nกางแขนตรงออกด้านข้าง" },
            { "Follow the trainer — the class keeps going, so just do your best!", "ทำตามเทรนเนอร์ คลาสจะดำเนินต่อไป ทำให้เต็มที่นะครับ" },
            // KINEX / WORLD / numbers (0%, 3, 1/5) intentionally left unchanged (brand / numeric).
        };

        [MenuItem("Kinex/Localize/2 Localize Scenes")]
        public static void LocalizeScenes()
        {
            var black = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BlackAsset);
            var semi = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiAsset);
            var italic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ItalicAsset);
            if (semi == null) { Debug.LogError("[ThaiLocalize] Run step 1 first (fonts missing)."); return; }

            foreach (var path in ScenePaths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int fontChanged = 0, textChanged = 0;
                foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    // Pick a weight that resembles the current one.
                    string cur = t.font != null ? t.font.name.ToLowerInvariant() : "";
                    TMP_FontAsset target = semi;
                    if (cur.Contains("italic") && italic != null) target = italic;
                    else if (cur.Contains("black") && black != null) target = black;
                    if (t.font != target) { t.font = target; fontChanged++; }

                    if (Map.TryGetValue(t.text, out string thai)) { t.text = thai; textChanged++; }
                    EditorUtility.SetDirty(t);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[ThaiLocalize] {path}: {fontChanged} fonts + {textChanged} texts localized.");
            }
        }
    }
}
#endif
