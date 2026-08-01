using UnityEngine;
using Kinex.Trainer;

namespace Kinex
{
    /// <summary>
    /// Translucent "spirit coach" that strikes the target pose for the current skill card, reusing
    /// the existing TrainerPoseController/RehabPoseData pipeline (Kinex.Trainer) instead of
    /// re-authoring poses. Instantiated entirely at RUNTIME by BattleDirector (never added to the
    /// scene file) so it never collides with the concurrently-edited BattleGameSceneBuilder/
    /// BattleStage. Strips every Light/Camera the FBX ships with (this rig family carries a stray
    /// intensity-1000 point light — see BattleGameSceneBuilder's own player-avatar setup for the
    /// same fix) and re-materials every renderer to an unlit, translucent "ghost" look with light
    /// probes off so it never picks up the level's baked/live lighting.
    /// </summary>
    public class PoseGhost : MonoBehaviour
    {
        const float BobAmplitude = 0.035f;
        const float BobSpeed = 1.6f;
        const float FadeSpeedPerSecond = 1.6f;
        const float MaxAlpha = 0.72f; // never fully opaque — stays a translucent "spirit", not a solid clone

        static readonly Color CalmCyan = new Color(0.4f, 0.86f, 0.95f);
        static readonly Color HotGlow = new Color(1f, 0.86f, 0.4f);

        static Material s_GhostMaterialTemplate;

        TrainerPoseController _controller;
        Material[] _materials;
        Vector3 _basePos;
        float _bobClock;
        float _alpha;
        float _targetAlpha;

        public bool HasController => _controller != null;

        /// <summary>Builds the ghost rig as a child of <paramref name="parent"/>, snapped to
        /// <paramref name="poseIndex"/> and starting fully invisible (caller fades it in with the
        /// pose card via <see cref="FadeTo"/>).</summary>
        public static PoseGhost Create(Transform parent, GameObject rigPrefab, TrainerPoseData poseData,
                                        int poseIndex, Vector3 localPosition, float scale = 1f)
        {
            if (parent == null || rigPrefab == null || poseData == null) return null;

            var root = Instantiate(rigPrefab, parent);
            root.name = "PoseGhost";
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the camera, like the player avatar
            root.transform.localScale = Vector3.one * scale;

            StripLightsAndCameras(root);

            var ghost = root.AddComponent<PoseGhost>();
            ghost._materials = GhostifyRenderers(root);
            ghost._basePos = localPosition;

            var controller = root.AddComponent<TrainerPoseController>();
            controller.autoAdvance = false;
            controller.blendTime = 0.5f;
            int startIndex = poseIndex >= 0 ? poseIndex : 0;
            controller.InitRuntime(poseData, root.transform, startIndex);
            ghost._controller = controller;

            ghost.SetAlpha(0f);
            return ghost;
        }

        /// <summary>Blend to a different baked pose (e.g. a new skill's ghost) without recreating the rig.</summary>
        public void ShowPoseIndex(int index)
        {
            if (_controller != null && index >= 0) _controller.ShowPose(index);
        }

        /// <summary>Fades toward the given alpha (0..1); Update() eases toward it every frame.</summary>
        public void FadeTo(float target01) => _targetAlpha = Mathf.Clamp01(target01);

        /// <summary>"Getting warmer": 0 = calm dim cyan, 1 = bright gold-white as the player nears success.</summary>
        public void SetProgressGlow(float progress01)
        {
            if (_materials == null) return;
            progress01 = Mathf.Clamp01(progress01);
            Color tint = Color.Lerp(CalmCyan, HotGlow, progress01) * Mathf.Lerp(1f, 1.8f, progress01);
            foreach (var m in _materials)
            {
                if (m == null) continue;
                float a = m.color.a; // alpha stays driven by the fade, not the progress glow
                m.color = new Color(tint.r, tint.g, tint.b, a);
            }
        }

        void Update()
        {
            _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, Time.deltaTime * FadeSpeedPerSecond);
            SetAlpha(_alpha);

            _bobClock += Time.deltaTime * BobSpeed;
            Vector3 pos = _basePos;
            pos.y += Mathf.Sin(_bobClock) * BobAmplitude;
            transform.localPosition = pos;
        }

        void SetAlpha(float a)
        {
            if (_materials == null) return;
            float outAlpha = a * MaxAlpha;
            foreach (var m in _materials)
            {
                if (m == null) continue;
                var c = m.color;
                c.a = outAlpha;
                m.color = c;
            }
        }

        static void StripLightsAndCameras(GameObject root)
        {
            foreach (var l in root.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
        }

        static Material[] GhostifyRenderers(GameObject root)
        {
            var template = GhostMaterialTemplate();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var mats = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                var mat = new Material(template) { name = "GhostMat" };
                var shared = r.sharedMaterials;
                for (int j = 0; j < shared.Length; j++) shared[j] = mat;
                r.sharedMaterials = shared;
                mats[i] = mat;
            }
            return mats;
        }

        static Material GhostMaterialTemplate()
        {
            if (s_GhostMaterialTemplate != null) return s_GhostMaterialTemplate;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color"); // fallback if the URP shader ever isn't resolvable
            var mat = new Material(shader) { name = "GhostMaterialTemplate", color = CalmCyan };
            ConfigureTransparent(mat);
            s_GhostMaterialTemplate = mat;
            return mat;
        }

        // URP Unlit "Surface Type = Transparent" wiring — the same values the Inspector toggle sets,
        // applied via script so this works identically in a build with no manual material asset.
        static void ConfigureTransparent(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
