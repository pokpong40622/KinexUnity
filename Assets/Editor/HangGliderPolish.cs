#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kinex.HangGlider.EditorTools
{
    /// <summary>
    /// Surgical look pass for Assets/HangGlider/HangGliderScene.unity.
    ///
    /// Unlike the other Kinex scene tools (TheDasherSceneBuilder etc.) this does NOT rebuild the
    /// scene — the valley, terrain and gate layout are hand-authored and already good. It only
    /// retunes the things that were left at defaults:
    ///
    ///   • HangGliderPostFX.asset was wired to the camera (m_RenderPostProcessing: 1) but held five
    ///     NULL components — the grading pipeline was switched on and rendering nothing.
    ///   • The sun was a flat untinted directional with no warmth and shadows barely reading.
    ///   • Linear fog ended at 1100 while the camera's far plane clips at 1000, so terrain was cut
    ///     off BEFORE fog could hide it — visible pop-in at the horizon.
    ///   • Camera was on SMAA/high, which is a lot of frame time for a Flutter-embedded URP texture.
    ///
    /// Idempotent: re-run it freely while tuning. It mutates the existing profile asset in place
    /// rather than recreating it, so the scene's Volume keeps its reference (recreating the asset
    /// would change the GUID and silently unhook the volume).
    /// </summary>
    public static class HangGliderPolish
    {
        const string ScenePath = "Assets/HangGlider/HangGliderScene.unity";
        const string ProfilePath = "Assets/HangGlider/HangGliderPostFX.asset";

        // Late-afternoon alpine light: warm low sun, cool shadow, hazy blue distance.
        static readonly Color SunWarm = new Color(1.00f, 0.94f, 0.80f);
        static readonly Color FogHaze = new Color(0.72f, 0.82f, 0.93f);
        static readonly Color AmbSky = new Color(0.48f, 0.56f, 0.66f);
        static readonly Color AmbEquator = new Color(0.40f, 0.44f, 0.44f);
        static readonly Color AmbGround = new Color(0.22f, 0.22f, 0.18f);

        [MenuItem("Kinex/Hang Glider/Polish Scene")]
        public static void Polish()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            TunePostProfile();
            TuneSun();
            TuneAtmosphere();
            TuneCamera();
            EnsureWind();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[HangGliderPolish] done");
        }

        /// <summary>Batchmode entry point: unity -batchmode -quit -executeMethod
        /// Kinex.HangGlider.EditorTools.HangGliderPolish.PolishBatch</summary>
        public static void PolishBatch() => Polish();

        // ── Post-processing ──────────────────────────────────────────────────────────────────────

        static void TunePostProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogError($"[HangGliderPolish] No volume profile at {ProfilePath}");
                return;
            }

            // The asset shipped with five null component slots; Add/Has choke on those.
            profile.components.RemoveAll(c => c == null);

            // Neutral tonemapping, not ACES: ACES crushes the sky's highlights toward a moody dark
            // look, which fights a bright daytime flight.
            var tonemap = Ensure<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.Neutral);

            // The valley reads muddy and washed out untouched — the greens and rock browns sit at
            // almost the same value. Saturation and contrast do most of the visible work here.
            var colors = Ensure<ColorAdjustments>(profile);
            colors.saturation.Override(18f);
            colors.contrast.Override(12f);
            colors.postExposure.Override(0.20f);
            colors.colorFilter.Override(new Color(1.02f, 1.00f, 0.96f)); // a touch of afternoon warmth

            // Bloom only on the genuinely bright stuff (sky, sun-lit rock edges, the answer gates),
            // not a general glow — a low threshold here makes the whole valley look foggy.
            var bloom = Ensure<Bloom>(profile);
            bloom.intensity.Override(0.50f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.62f);

            // Just enough corner falloff to focus the centre of a landscape screen. Deliberately
            // weak — a strong vignette on a flying game reads as tunnel vision.
            var vignette = Ensure<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.75f);

            // Lifts the deep shadows in the rock faces so they don't read as flat black shapes.
            var curves = Ensure<ShadowsMidtonesHighlights>(profile);
            curves.shadows.Override(new Vector4(1.06f, 1.08f, 1.14f, 0f)); // cool, slightly lifted
            curves.highlights.Override(new Vector4(1.02f, 1.00f, 0.97f, 0f));

            EditorUtility.SetDirty(profile);
        }

        /// <summary>Existing override if the profile already has one (so re-runs retune rather than
        /// stack duplicates), otherwise a fresh one added as a sub-asset.</summary>
        static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;
            var comp = profile.Add<T>(true);
            comp.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(comp, profile);
            return comp;
        }

        // ── Sun ──────────────────────────────────────────────────────────────────────────────────

        static void TuneSun()
        {
            var go = GameObject.Find("Directional Light");
            if (go == null) { Debug.LogWarning("[HangGliderPolish] no 'Directional Light'"); return; }
            var light = go.GetComponent<Light>();
            if (light == null) return;

            // Low and off to one side: a high overhead sun lights every rock face equally and is
            // exactly why the mountains read flat. A raking angle gives them a lit and a shadow side.
            go.transform.rotation = Quaternion.Euler(28f, 35f, 0f);
            light.color = SunWarm;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.62f; // softened — full-strength shadows go inky against the haze
            EditorUtility.SetDirty(go);
        }

        // ── Fog / ambient ────────────────────────────────────────────────────────────────────────

        static void TuneAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogHaze;
            // Fog must finish INSIDE the camera's 1000 far plane, or geometry is clipped before the
            // haze has hidden it and you see the horizon pop. Was 280 -> 1100.
            RenderSettings.fogStartDistance = 200f;
            RenderSettings.fogEndDistance = 950f;

            // Trilight ambient: the old values were near-black (0.21/0.11/0.05), which is why the
            // shadowed valley sides went muddy.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbSky;
            RenderSettings.ambientEquatorColor = AmbEquator;
            RenderSettings.ambientGroundColor = AmbGround;
            RenderSettings.ambientIntensity = 1f;
        }

        // ── Camera ───────────────────────────────────────────────────────────────────────────────

        static void TuneCamera()
        {
            var go = GameObject.Find("Main Camera");
            if (go == null) { Debug.LogWarning("[HangGliderPolish] no 'Main Camera'"); return; }

            var data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
            {
                data.renderPostProcessing = true;
                // SMAA/high is a real cost on a tablet rendering into a Flutter texture. FXAA is
                // roughly free and the difference is not visible at arm's length on a 10" screen.
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            }
            EditorUtility.SetDirty(go);
        }

        // ── Wind ─────────────────────────────────────────────────────────────────────────────────

        static void EnsureWind()
        {
            var glider = GameObject.Find("MainGlider");
            if (glider == null) { Debug.LogWarning("[HangGliderPolish] no 'MainGlider'"); return; }

            // Idempotent: adding a second GliderWind would double the ambience.
            if (glider.GetComponent<Kinex.HangGlider.GliderWind>() == null)
                glider.AddComponent<Kinex.HangGlider.GliderWind>();
            EditorUtility.SetDirty(glider);
        }
    }
}
#endif
