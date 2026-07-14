using UnityEngine;
using Kinex.Trainer;

namespace Kinex.MirrorGame
{
    /// <summary>
    /// Mode-1 border for the Magic Mirror game: a soft white-gold GLOWING GHOST that strikes the
    /// target pose, with a ring "zone circle" hovering at each tracked joint. The ghost MUST be a
    /// clone of the rig RehabPoseData was baked on (NewTrainerAnimated.fbx) — TrainerPoseController
    /// copies raw local rotations/positions by bone name, which only reproduces the pose on that
    /// exact rig (a player-model clone ends up lying flat: same bone names, different bind frames).
    /// Proportion match with the player is approximated by height-normalizing the ghost to the
    /// player avatar's rendered height. Recipe otherwise copied from Kinex.BattleGame.PoseGhost
    /// (runtime instantiate, strip stray FBX lights/cameras, re-material every renderer to URP
    /// Unlit transparent with light probes off, TrainerPoseController added at runtime + InitRuntime).
    ///
    /// The 10 zone circles map 1:1 to <see cref="ZoneMatcher.Joint"/>. Their world positions come
    /// from the ghost's own Humanoid bones every frame, so a live TrainerPoseController blend still
    /// lands the circles on the moving target. Colours are pushed in by the director each frame
    /// (red → amber → green from the matcher); a circle pops on the frame it turns green.
    /// </summary>
    public class MirrorOutline : MonoBehaviour
    {
        // ZoneMatcher.Joint index order → HumanBodyBones (per the director's joint map).
        public static readonly HumanBodyBones[] JointBones =
        {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,   // shoulders
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,   // elbows
            HumanBodyBones.LeftHand,     HumanBodyBones.RightHand,       // wrists
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,   // knees
            HumanBodyBones.LeftFoot,     HumanBodyBones.RightFoot,       // ankles
        };

        const float MaxAlpha = 0.55f;
        const float FadeSpeedPerSecond = 1.6f;
        // HDR-ish soft white-gold so URP bloom catches the ghost edges as a glow. Bumped from the
        // original 1.5x — measured in-editor (pixel-sampled the rendered screenshot) that 1.5x/2.0x
        // at 0.45-0.55 alpha blended out to ~1.1 max channel value, right at the post volume's
        // bloom threshold with no visible halo; 2.6x clears it with margin so the silhouette
        // actually blooms instead of reading as flat grey-white.
        static readonly Color GlowColor = new Color(1.0f, 0.92f, 0.72f) * 2.6f;

        static Material s_GhostTemplate;
        static Texture2D s_RingTex;

        TrainerPoseController _controller;
        Animator _ghostAnimator;
        Renderer[] _ghostRenderers;
        Material[] _ghostMaterials;

        Transform _zonesHolder;
        Transform[] _zoneQuads;
        Material[] _zoneMats;
        float[] _zonePulse;                 // decays 1→0 after a lock, drives a scale pop
        readonly bool[] _wasGreen = new bool[ZoneMatcher.JointCount];

        readonly Color[] _zoneColors = new Color[ZoneMatcher.JointCount];
        float _radiusMult = 1f;

        float _alpha;
        float _targetAlpha;
        bool _ghostShown = true;

        public Animator GhostAnimator => _ghostAnimator;
        /// <summary>The separate holder for the zone circle quads (parented outside the scaled
        /// ghost). Exposed so the edit-mode screenshot preview can tear it down cleanly.</summary>
        public Transform ZonesHolder => _zonesHolder;

        public static MirrorOutline Create(Transform playerModel, GameObject rigPrefab,
                                           TrainerPoseData poseData, int startPoseIndex)
        {
            if (playerModel == null || rigPrefab == null || poseData == null) return null;

            var root = Instantiate(rigPrefab);
            root.name = "MirrorOutlineGhost";
            // Co-locate with the player avatar so the ghost bones sit exactly where the player
            // must place theirs. The trainer FBX's units differ from the player model's, so match
            // rendered HEIGHT rather than copying the player's localScale.
            root.transform.SetParent(playerModel.parent, false);
            root.transform.localPosition = playerModel.localPosition;
            root.transform.localRotation = playerModel.localRotation; // already Euler(0,180,0)
            MatchHeight(root, playerModel);

            StripLightsAndCameras(root);

            var outline = root.AddComponent<MirrorOutline>();
            outline._ghostAnimator = root.GetComponentInChildren<Animator>();
            outline.Ghostify(root);

            var controller = root.AddComponent<TrainerPoseController>();
            controller.autoAdvance = false;
            controller.blendTime = 0.5f;
            controller.InitRuntime(poseData, root.transform, Mathf.Max(0, startPoseIndex));
            outline._controller = controller;

            outline.BuildZones();
            outline.SetAlphaImmediate(0f);
            return outline;
        }

        public void ShowPose(int index)
        {
            if (_controller != null && index >= 0) _controller.ShowPose(index);
        }

        public void FadeTo(float target01) => _targetAlpha = Mathf.Clamp01(target01);

        public void SetAlphaImmediate(float a)
        {
            _alpha = _targetAlpha = Mathf.Clamp01(a);
            ApplyGhostAlpha(_alpha);
        }

        /// <summary>Hide/show just the ghost body — the zone circles stay visible (used by the
        /// wall-mode toggle, where the wall replaces the ghost but the zones remain the guide).</summary>
        public void SetGhostVisible(bool visible)
        {
            _ghostShown = visible;
            if (_ghostRenderers == null) return;
            foreach (var r in _ghostRenderers) if (r != null) r.enabled = visible;
        }

        /// <summary>World position of a ghost bone (used by MirrorWall to carve its hole).</summary>
        public Vector3 BonePos(HumanBodyBones bone)
        {
            var t = _ghostAnimator != null ? _ghostAnimator.GetBoneTransform(bone) : null;
            return t != null ? t.position : transform.position;
        }

        /// <summary>Fill <paramref name="outPos"/> (length 10) with the target joint world
        /// positions from the ghost's current pose.</summary>
        public void TargetPositions(Vector3[] outPos)
        {
            for (int i = 0; i < ZoneMatcher.JointCount; i++)
                outPos[i] = BonePos(JointBones[i]);
        }

        /// <summary>Director pushes per-joint colours + the current assist radius multiplier each
        /// frame; the actual layout happens in LateUpdate (after the pose controller has written
        /// the ghost bones).</summary>
        public void ApplyZones(Color[] colors, float radiusMultiplier)
        {
            if (colors != null)
                for (int i = 0; i < ZoneMatcher.JointCount && i < colors.Length; i++)
                    _zoneColors[i] = colors[i];
            _radiusMult = radiusMultiplier;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;

            _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, Time.deltaTime * FadeSpeedPerSecond);
            ApplyGhostAlpha(_ghostShown ? _alpha : 0f);
            LayoutZones(Camera.main, Time.deltaTime);
        }

        /// <summary>One synchronous layout pass for the edit-mode screenshot (LateUpdate never runs
        /// outside play mode). Poses are already applied by InitRuntime/ShowPose (SnapToPose writes
        /// bones immediately), so the bone positions are valid here.</summary>
        public void EditorPreviewLayout(Camera cam)
        {
            LayoutZones(cam, 0f);
        }

        void LayoutZones(Camera cam, float dt)
        {
            if (_zoneQuads == null) return;
            for (int i = 0; i < _zoneQuads.Length; i++)
            {
                var q = _zoneQuads[i];
                if (q == null) continue;

                q.position = BonePos(JointBones[i]);
                if (cam != null) q.rotation = cam.transform.rotation; // screen-aligned billboard

                float diameter = 2f * ZoneMatcher.BaseRadius[i] * _radiusMult;
                float pop = 1f + 0.28f * _zonePulse[i];
                q.localScale = new Vector3(diameter * pop, diameter * pop, 1f);

                if (_zoneMats[i] != null) SetMatColor(_zoneMats[i], _zoneColors[i]);
                if (dt > 0f) _zonePulse[i] = Mathf.Max(0f, _zonePulse[i] - dt * 3f);

                // Pop the circle + a small gold sparkle on the frame it first turns "green"
                // (matched). dt > 0 gates the sparkle to play mode (never the editor preview).
                bool green = _zoneColors[i].g > 0.6f && _zoneColors[i].r < 0.5f;
                if (green && !_wasGreen[i])
                {
                    _zonePulse[i] = 1f;
                    if (dt > 0f)
                        Kinex.FX.KinexFx.PopBurst(q.position, new Color(1f, 0.9f, 0.5f, 0.9f), 12);
                }
                _wasGreen[i] = green;
            }
        }

        // ---- build ----------------------------------------------------------

        /// <summary>Scale the ghost so its rendered (bind-pose) height equals the player avatar's —
        /// the two FBX families use different units, and near-equal height is what makes the zone
        /// circles land where the player can actually reach them.</summary>
        static void MatchHeight(GameObject ghost, Transform playerModel)
        {
            float playerH = BoundsHeight(playerModel.gameObject);
            float ghostH = BoundsHeight(ghost);
            if (playerH > 0.01f && ghostH > 0.01f)
                ghost.transform.localScale *= playerH / ghostH;
        }

        static float BoundsHeight(GameObject go)
        {
            var rens = go.GetComponentsInChildren<Renderer>();
            if (rens.Length == 0) return 0f;
            var b = rens[0].bounds;
            foreach (var r in rens) b.Encapsulate(r.bounds);
            return b.size.y;
        }

        void Ghostify(GameObject root)
        {
            var template = GhostTemplate();
            _ghostRenderers = root.GetComponentsInChildren<Renderer>(true);
            _ghostMaterials = new Material[_ghostRenderers.Length];
            for (int i = 0; i < _ghostRenderers.Length; i++)
            {
                var r = _ghostRenderers[i];
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                var mat = new Material(template) { name = "MirrorGhostMat" };
                var shared = r.sharedMaterials;
                for (int j = 0; j < shared.Length; j++) shared[j] = mat;
                r.sharedMaterials = shared;
                _ghostMaterials[i] = mat;
            }
        }

        void BuildZones()
        {
            var holderGo = new GameObject("MirrorZones");
            holderGo.transform.SetParent(transform.parent, false); // NOT under the scaled ghost
            holderGo.transform.localPosition = Vector3.zero;
            holderGo.transform.localRotation = Quaternion.identity;
            holderGo.transform.localScale = Vector3.one;
            _zonesHolder = holderGo.transform;

            int n = ZoneMatcher.JointCount;
            _zoneQuads = new Transform[n];
            _zoneMats = new Material[n];
            _zonePulse = new float[n];

            var ringTemplate = RingTemplate();
            for (int i = 0; i < n; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"Zone_{(ZoneMatcher.Joint)i}";
                DestroyColliderSafe(quad);
                quad.transform.SetParent(_zonesHolder, false);

                var mat = new Material(ringTemplate) { name = $"ZoneMat_{i}" };
                quad.GetComponent<Renderer>().sharedMaterial = mat;
                _zoneMats[i] = mat;
                _zoneColors[i] = new Color(0.9f, 0.3f, 0.3f, 0.9f); // start "not matched" red
                _zoneQuads[i] = quad.transform;
            }
        }

        void ApplyGhostAlpha(float a)
        {
            if (_ghostMaterials == null) return;
            float outA = a * MaxAlpha;
            foreach (var m in _ghostMaterials)
            {
                if (m == null) continue;
                SetMatColor(m, new Color(GlowColor.r, GlowColor.g, GlowColor.b, outA));
            }
        }

        static void SetMatColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
        }

        static void StripLightsAndCameras(GameObject root)
        {
            foreach (var l in root.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
        }

        // Works at runtime (Destroy) and in the edit-mode screenshot preview (DestroyImmediate).
        static void DestroyColliderSafe(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        // Shader.Find only works in a build for shaders actually shipped (see
        // Editor/AlwaysIncludedShaders.cs, which force-includes URP/Unlit). The chain here is a
        // belt-and-braces fallback so a stripped shader degrades the look instead of throwing —
        // an ArgumentNullException here killed the game flow on device (stuck intro popup).
        static Shader FindShaderSafe(params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            Debug.LogError($"[MirrorOutline] none of these shaders are in the build: {string.Join(", ", names)}");
            return null;
        }

        static Material GhostTemplate()
        {
            if (s_GhostTemplate != null) return s_GhostTemplate;
            var shader = FindShaderSafe("Universal Render Pipeline/Unlit",
                                        "Unlit/Transparent", "Sprites/Default", "Unlit/Color");
            var mat = new Material(shader) { name = "MirrorGhostTemplate" };
            SetMatColor(mat, GlowColor);
            ConfigureTransparent(mat, cullOff: false);
            s_GhostTemplate = mat;
            return mat;
        }

        static Material RingTemplate()
        {
            var shader = FindShaderSafe("Universal Render Pipeline/Unlit",
                                        "Unlit/Transparent", "Sprites/Default", "Unlit/Color");
            var mat = new Material(shader) { name = "MirrorZoneTemplate" };
            var tex = RingTexture();
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            ConfigureTransparent(mat, cullOff: true); // billboard quads must show from either side
            return mat;
        }

        // Procedural ring (annulus): transparent inside + outside, opaque band near the rim, so the
        // player sees an open target circle they slot the joint into. Built once, shared.
        static Texture2D RingTexture()
        {
            if (s_RingTex != null) return s_RingTex;
            const int size = 128;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);   // 0 center, 1 at edge
                    // Band centred at r=0.82, ~0.16 wide, soft falloff.
                    float a = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.82f) / 0.16f);
                    a *= a;
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            s_RingTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_RingTex.SetPixels32(px);
            s_RingTex.Apply();
            return s_RingTex;
        }

        static void ConfigureTransparent(Material mat, bool cullOff)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (cullOff && mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
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
