using UnityEditor;
using UnityEngine;

namespace Kinex.EditorTools
{
    /// <summary>
    /// The runtime-built stages/FX create materials via Shader.Find(), which only works in a
    /// device build if the shader was actually shipped. "Universal Render Pipeline/Unlit" and
    /// "Universal Render Pipeline/Particles/Unlit" are referenced by NO asset in any scene
    /// (everything is code-built), so the Android build stripped them — Shader.Find returned
    /// null on device and MirrorOutline/MirrorStage threw ArgumentNullException (the stuck
    /// intro-popup bug). Adding them to GraphicsSettings' Always Included Shaders ships them
    /// in every build. Idempotent; run in batch before exporting.
    /// </summary>
    public static class AlwaysIncludedShaders
    {
        static readonly string[] Needed =
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Unlit",
        };

        [MenuItem("Kinex/Setup Always-Included Shaders")]
        public static void Apply()
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs == null || gs.Length == 0)
            {
                Debug.LogError("[AlwaysIncludedShaders] GraphicsSettings.asset not found");
                return;
            }

            var so = new SerializedObject(gs[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            int added = 0;

            foreach (var name in Needed)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError($"[AlwaysIncludedShaders] shader not found in editor: {name}");
                    continue;
                }

                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                if (present) continue;

                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
                added++;
                Debug.Log($"[AlwaysIncludedShaders] added: {name}");
            }

            if (added > 0)
            {
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[AlwaysIncludedShaders] DONE — {added} added, {arr.arraySize} total");
        }
    }
}
