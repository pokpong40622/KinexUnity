using UnityEngine;
using Kinex.FX;

namespace Kinex.TempleHunt
{
    /// <summary>
    /// Runtime-built ancient temple for "ล่าสมบัติวิหารโบราณ": a torch-lit sandstone chamber shell
    /// (floor, carved back wall, side walls, pillars, jungle overgrowth at the entrance, drifting
    /// warm dust) plus four swappable chamber prop sets — pump station with rising water, the
    /// giant stone gates, the chasm beam, and the totem vault with the treasure chest. Same
    /// no-scene-authoring philosophy as MirrorStage/BattleStage: everything is primitives +
    /// Kenney nature props + code-built materials. The player avatar and camera NEVER move —
    /// only the props around them change — so the MediaPipe framing stays constant all game.
    ///
    /// The director drives it through small setter methods (SetWaterLevel01, SetGateLift01, ...);
    /// Update() eases everything toward its target and flickers the torches.
    /// </summary>
    public class TempleStage : MonoBehaviour
    {
        // ---- palette ----
        static readonly Color Sandstone = new Color(0.60f, 0.50f, 0.35f);
        static readonly Color DarkStone = new Color(0.40f, 0.33f, 0.24f);
        static readonly Color Moss = new Color(0.32f, 0.42f, 0.25f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.42f);
        static readonly Color WaterColor = new Color(0.16f, 0.48f, 0.52f, 0.55f);
        static readonly Color RuneCyan = new Color(0.35f, 0.85f, 0.9f);

        const float WaterTopFull = 0.7f;    // knee-ish height — the ghost's knee-up demo stays readable
        const float WaterTopEmpty = 0.02f;
        const float GateTravel = 3.0f;
        const float LeverTiltDegrees = 26f;

        /// <summary>Everything the animated setters touch, so the static builder can hand a full
        /// rig to the runtime instance AND to the scene builder's screenshot preview.</summary>
        public class Rig
        {
            public GameObject[] chamberRoots = new GameObject[5]; // 1..4 used
            public Transform water;
            public Transform leverArm;
            public Transform gateSlab;
            public Renderer gateSlabRenderer;
            public Material beamGlowMat;
            public Material[] runeMats;
            public Transform chestLidPivot;
            public GameObject chestGlow;
            public Light[] torches;
        }

        Rig _rig;
        float _waterTarget = 1f, _waterCur = 1f;
        float _gateTarget, _gateCur;
        float _leverTarget, _leverCur;
        float _beamGlowTarget, _beamGlowCur;
        float _totemLitTarget, _totemLitCur;
        bool _chestOpen;
        float _chestOpenAngle;
        float[] _torchSeeds;

        // Guarded so a device-only failure degrades to a partial stage instead of aborting the
        // whole build mid-way (the MirrorStage lesson, 2026-07-14).
        void Awake()
        {
            try
            {
                _rig = Build(transform, showChamber: 1);
                SeedTorches();
            }
            catch (System.Exception e) { Debug.LogError($"[TempleStage] stage build failed: {e}"); }
        }

        void SeedTorches()
        {
            int n = _rig?.torches?.Length ?? 0;
            _torchSeeds = new float[n];
            for (int i = 0; i < n; i++) _torchSeeds[i] = i * 17.31f;
        }

        // ---- director API (all null-safe: a partial build never throws mid-game) ----

        public void ShowChamber(int index)
        {
            if (_rig == null) return;
            for (int i = 1; i < _rig.chamberRoots.Length; i++)
                if (_rig.chamberRoots[i] != null)
                    _rig.chamberRoots[i].SetActive(i == index);
        }

        public void SetWaterLevel01(float t) => _waterTarget = Mathf.Clamp01(t);

        public void SetLever(bool leftUp, bool rightUp)
            => _leverTarget = leftUp ? -LeverTiltDegrees : (rightUp ? LeverTiltDegrees : 0f);

        /// <summary>Retint the slab per gate so each of the three gates reads as a new door.</summary>
        public void SetGateStage(int gate)
        {
            _gateTarget = 0f;
            _gateCur = 0f;
            if (_rig?.gateSlab != null) ApplyGateLift(0f);
            if (_rig?.gateSlabRenderer == null) return;
            Color tint = gate switch
            {
                1 => DarkStone,
                2 => new Color(0.36f, 0.30f, 0.30f), // reddish granite
                _ => new Color(0.28f, 0.30f, 0.34f), // dark basalt — the final, most ancient door
            };
            _rig.gateSlabRenderer.sharedMaterial = PropMeshes.Mat(tint, smooth: 0.1f);
        }

        public void SetGateLift01(float t) => _gateTarget = Mathf.Clamp01(t);

        public void SetBeamGlow01(float t) => _beamGlowTarget = Mathf.Clamp01(t);

        public void SetTotemLit01(float t) => _totemLitTarget = Mathf.Clamp01(t);

        public void OpenChest() => _chestOpen = true;

        /// <summary>World position of the treasure chest lid — confetti anchor for the finale.</summary>
        public Vector3 ChestPosition => _rig?.chestLidPivot != null
            ? _rig.chestLidPivot.position
            : new Vector3(1.1f, 0.6f, 1.6f);

        void Update()
        {
            if (_rig == null) return;
            float dt = Time.deltaTime;

            // Water eases toward its level; slow enough to read as "draining", not snapping.
            _waterCur = Mathf.MoveTowards(_waterCur, _waterTarget, dt * 0.25f);
            if (_rig.water != null)
            {
                float top = Mathf.Lerp(WaterTopEmpty, WaterTopFull, _waterCur);
                var p = _rig.water.localPosition;
                _rig.water.localPosition = new Vector3(p.x, top - 0.2f, p.z);
                _rig.water.gameObject.SetActive(_waterCur > 0.015f);
            }

            _leverCur = Mathf.MoveTowards(_leverCur, _leverTarget, dt * 140f);
            if (_rig.leverArm != null)
                _rig.leverArm.localRotation = Quaternion.Euler(0f, 0f, _leverCur);

            _gateCur = Mathf.MoveTowards(_gateCur, _gateTarget, dt * 0.6f);
            ApplyGateLift(_gateCur);

            _beamGlowCur = Mathf.MoveTowards(_beamGlowCur, _beamGlowTarget, dt * 1.5f);
            if (_rig.beamGlowMat != null)
            {
                Color c = Color.Lerp(RuneCyan * 0.25f, Gold * 1.3f, _beamGlowCur);
                c.a = Mathf.Lerp(0.25f, 0.85f, _beamGlowCur);
                SetBaseColor(_rig.beamGlowMat, c);
            }

            _totemLitCur = Mathf.MoveTowards(_totemLitCur, _totemLitTarget, dt * 1.5f);
            if (_rig.runeMats != null)
            {
                float lit = _totemLitCur * _rig.runeMats.Length;
                for (int i = 0; i < _rig.runeMats.Length; i++)
                {
                    if (_rig.runeMats[i] == null) continue;
                    float k = Mathf.Clamp01(lit - i); // rune i lights over its own 1/N slice
                    Color c = Color.Lerp(RuneCyan * 0.2f, Gold * 1.5f, k);
                    c.a = Mathf.Lerp(0.3f, 0.95f, k);
                    SetBaseColor(_rig.runeMats[i], c);
                }
            }

            if (_chestOpen && _rig.chestLidPivot != null && _chestOpenAngle < 105f)
            {
                _chestOpenAngle = Mathf.MoveTowards(_chestOpenAngle, 105f, dt * 140f);
                _rig.chestLidPivot.localRotation = Quaternion.Euler(-_chestOpenAngle, 0f, 0f);
                if (_rig.chestGlow != null) _rig.chestGlow.SetActive(true);
            }

            // Torch flicker — Perlin noise per torch so they never pulse in sync.
            if (_rig.torches != null && _torchSeeds != null)
                for (int i = 0; i < _rig.torches.Length; i++)
                    if (_rig.torches[i] != null)
                        _rig.torches[i].intensity =
                            Mathf.Lerp(1.5f, 2.3f, Mathf.PerlinNoise(_torchSeeds[i], Time.time * 2.1f));
        }

        void ApplyGateLift(float t)
        {
            if (_rig?.gateSlab == null) return;
            var p = _rig.gateSlab.localPosition;
            _rig.gateSlab.localPosition = new Vector3(p.x, 1.75f + t * GateTravel, p.z);
        }

        // =====================================================================
        // Static builder — used by Awake at runtime AND by TempleHuntSceneBuilder for the
        // edit-mode screenshot preview (where Awake never runs).
        // =====================================================================
        public static Rig Build(Transform parent, int showChamber)
        {
            var rig = new Rig();

            // Torch-lit night temple: dim teal from above, warm torch glow at eye level.
            ProceduralSkybox.SetAmbient(
                sky: new Color(0.20f, 0.24f, 0.27f),
                equator: new Color(0.46f, 0.35f, 0.22f),
                ground: new Color(0.15f, 0.11f, 0.08f));

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.15f, 0.12f, 0.09f);
            RenderSettings.fogStartDistance = 5f;
            RenderSettings.fogEndDistance = 18f;

            BuildShell(parent, rig);
            rig.chamberRoots[1] = BuildChamberPump(parent, rig);
            rig.chamberRoots[2] = BuildChamberGate(parent, rig);
            rig.chamberRoots[3] = BuildChamberChasm(parent, rig);
            rig.chamberRoots[4] = BuildChamberVault(parent, rig);
            for (int i = 1; i <= 4; i++)
                if (rig.chamberRoots[i] != null)
                    rig.chamberRoots[i].SetActive(i == showChamber);

            var cam = Camera.main;
            if (cam != null && cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = new Color(0.09f, 0.08f, 0.06f);

            return rig;
        }

        // ---- shared temple shell ----
        static void BuildShell(Transform parent, Rig rig)
        {
            // Sandstone floor slab.
            var floor = Box(parent, "TempleFloor", new Vector3(0f, -0.1f, 2f), new Vector3(12f, 0.2f, 14f),
                            PropMeshes.Mat(Sandstone * 0.85f, smooth: 0f));

            // Big floor tiles read as ancient paving: thin dark grout strips both ways.
            var groutMat = PropMeshes.Mat(DarkStone * 0.55f, smooth: 0f);
            for (float z = -4.4f; z < 9f; z += 1.6f)
                Box(parent, "FloorGroutZ", new Vector3(0f, 0.003f, z), new Vector3(12f, 0.002f, 0.045f), groutMat);
            for (float x = -5.6f; x < 6f; x += 1.6f)
                Box(parent, "FloorGroutX", new Vector3(x, 0.003f, 2f), new Vector3(0.045f, 0.002f, 14f), groutMat);

            // A warm circular "explorer's mark" where the player stands.
            var mark = Cylinder(parent, "StageMark", new Vector3(0f, 0.006f, 0.2f), new Vector3(2.4f, 0.012f, 2.4f),
                                PropMeshes.Mat(new Color(0.5f, 0.36f, 0.2f), smooth: 0.1f,
                                               emission: new Color(0.3f, 0.18f, 0.06f)));

            // Back wall (tall enough to clear the camera frustum — MirrorStage lesson).
            var wallMat = PropMeshes.Mat(Sandstone, smooth: 0.05f);
            Box(parent, "BackWall", new Vector3(0f, 2.9f, 5.4f), new Vector3(13f, 7.4f, 0.4f), wallMat);

            // Carved relief strips across the back wall.
            var carveMat = PropMeshes.Mat(DarkStone, smooth: 0f);
            Box(parent, "WallCarveTop", new Vector3(0f, 4.3f, 5.16f), new Vector3(11f, 0.22f, 0.06f), carveMat);
            Box(parent, "WallCarveMid", new Vector3(0f, 3.7f, 5.16f), new Vector3(11f, 0.10f, 0.06f), carveMat);

            // Glowing gold glyph band — the "ancient magic" accent line.
            Box(parent, "GlyphBand", new Vector3(0f, 4.0f, 5.14f), new Vector3(10.5f, 0.14f, 0.05f),
                PropMeshes.Mat(Gold * 0.4f, smooth: 0.2f, emission: Gold * 0.9f));

            // Side walls with mossy bases.
            foreach (float x in new[] { -6.3f, 6.3f })
            {
                Box(parent, "SideWall", new Vector3(x, 2.2f, 3f), new Vector3(0.4f, 6f, 10.5f), wallMat);
                Box(parent, "MossBase", new Vector3(x - Mathf.Sign(x) * 0.03f, 0.25f, 3f),
                    new Vector3(0.08f, 0.5f, 10f), PropMeshes.Mat(Moss, smooth: 0f));
            }

            // Two square pillars framing the back of the play space. The portrait camera cone is
            // narrow (~±2.4 m at the back wall), so everything decorative must hug the centre.
            var pillarMat = PropMeshes.Mat(Sandstone * 0.92f, smooth: 0f);
            foreach (float x in new[] { -2.05f, 2.05f })
            {
                Box(parent, "Pillar", new Vector3(x, 2.1f, 4.15f), new Vector3(0.55f, 4.2f, 0.55f), pillarMat);
                Box(parent, "PillarCap", new Vector3(x, 4.25f, 4.15f), new Vector3(0.8f, 0.14f, 0.8f), carveMat);
            }

            // Four torches along the back: on the pillars' inner faces + in the wall corners.
            // All inside the visible cone so their glow actually reads on screen.
            rig.torches = new Light[4];
            int ti = 0;
            foreach (Vector3 pos in new[]
                     {
                         new Vector3(-1.62f, 2.6f, 4.05f), new Vector3(1.62f, 2.6f, 4.05f),
                         new Vector3(-2.35f, 2.2f, 4.9f), new Vector3(2.35f, 2.2f, 4.9f),
                     })
                {
                    Cylinder(parent, "TorchBowl", pos, new Vector3(0.24f, 0.08f, 0.24f),
                             PropMeshes.Mat(new Color(0.35f, 0.22f, 0.1f), metallic: 0.4f, smooth: 0.5f,
                                            emission: new Color(1f, 0.5f, 0.15f) * 0.8f));

                    // Bright emissive "flame" blob above each bowl — the visible fire (the point
                    // light alone reads as nothing on screen; HDR emission catches the bloom).
                    Sphere(parent, "TorchFlame", pos + Vector3.up * 0.16f, new Vector3(0.16f, 0.24f, 0.16f),
                           PropMeshes.Mat(new Color(1f, 0.6f, 0.2f), smooth: 0.1f,
                                          emission: new Color(1f, 0.55f, 0.12f) * 2.4f));

                    var lightGo = new GameObject("TorchLight");
                    lightGo.transform.SetParent(parent, false);
                    lightGo.transform.localPosition = pos + Vector3.up * 0.25f;
                    var l = lightGo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.62f, 0.28f);
                    l.range = 4.5f;
                    l.intensity = 1.9f;
                    l.shadows = LightShadows.None;
                    rig.torches[ti++] = l;

                    var flame = SoftMotes(parent, pos + Vector3.up * 0.32f,
                                          new Vector3(0.18f, 0.4f, 0.18f),
                                          new Color(1f, 0.62f, 0.2f, 0.9f), rate: 10, rise: 0.45f);
                    flame.name = "TorchEmbers";
                    var main = flame.main;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                }

            // Jungle reclaiming the temple — all inside the visible cone, hugging the back wall
            // so the greenery frames the avatar without occluding it.
            PlaceProp(parent, "tree_palm", new Vector3(-2.5f, 0f, 4.85f), 1.6f);
            PlaceProp(parent, "tree_palmBend", new Vector3(2.55f, 0f, 4.75f), 1.5f);
            PlaceProp(parent, "plant_bushLarge", new Vector3(-1.5f, 0f, 4.7f), 1.3f);
            PlaceProp(parent, "plant_bush", new Vector3(1.55f, 0f, 4.75f), 1.2f);
            PlaceProp(parent, "grass_large", new Vector3(-2.1f, 0f, 4.35f), 1.4f);
            PlaceProp(parent, "grass_large", new Vector3(2.15f, 0f, 4.4f), 1.3f);
            PlaceProp(parent, "stone_largeA", new Vector3(-2.3f, 0f, 4.45f), 0.9f);

            // Warm drifting dust — torchlit air (soft round motes, not the default squares).
            var dust = SoftMotes(parent, new Vector3(0f, 2f, 2.5f), new Vector3(4.5f, 3f, 4f),
                                 new Color(1f, 0.8f, 0.5f, 0.6f), rate: 10, rise: 0.05f);
            dust.name = "TempleDust";

            // Batch-render fix (same as the other stages): pre-simulate for edit-mode screenshots.
            if (!Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                dust.Simulate(4f, true, true);
                foreach (var ps in parent.GetComponentsInChildren<ParticleSystem>())
                    if (ps != dust) ps.Simulate(1.5f, true, true);
            }
        }

        // ---- Chamber 1: the Pumping Station ----
        static GameObject BuildChamberPump(Transform parent, Rig rig)
        {
            var root = Root(parent, "Chamber1_Pump");

            // Rising water: one flat translucent slab; Update() moves its Y with the level.
            var water = Box(root.transform, "Water", new Vector3(0f, WaterTopFull - 0.2f, 1.5f),
                            new Vector3(11.5f, 0.4f, 12.5f), WaterMaterial());
            rig.water = water.transform;

            // The giant floor lever the avatar "pumps": bronze base + tilting arm with knobs.
            // Beside/behind the avatar, inside the narrow camera cone.
            var baseMat = PropMeshes.Mat(new Color(0.42f, 0.3f, 0.16f), metallic: 0.6f, smooth: 0.5f);
            Cylinder(root.transform, "LeverBase", new Vector3(1.15f, 0.25f, 1.5f), new Vector3(0.4f, 0.25f, 0.4f), baseMat);

            var pivot = new GameObject("LeverPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(1.15f, 0.55f, 1.5f);
            rig.leverArm = pivot.transform;
            Box(pivot.transform, "LeverArm", Vector3.zero, new Vector3(1.3f, 0.09f, 0.09f), baseMat);
            var knobMat = PropMeshes.Mat(Gold * 0.6f, metallic: 0.7f, smooth: 0.7f, emission: Gold * 0.5f);
            Sphere(pivot.transform, "LeverKnobL", new Vector3(-0.65f, 0f, 0f), Vector3.one * 0.18f, knobMat);
            Sphere(pivot.transform, "LeverKnobR", new Vector3(0.65f, 0f, 0f), Vector3.one * 0.18f, knobMat);

            // Pump machine against the back wall: bronze tank + pipes + drain grate.
            var tankMat = PropMeshes.Mat(new Color(0.36f, 0.26f, 0.15f), metallic: 0.7f, smooth: 0.55f);
            Cylinder(root.transform, "PumpTank", new Vector3(-1.6f, 1.1f, 4.5f), new Vector3(1.1f, 1.1f, 1.1f), tankMat);
            Box(root.transform, "PumpPipeUp", new Vector3(-1.6f, 2.8f, 4.6f), new Vector3(0.18f, 1.6f, 0.18f), tankMat);
            Box(root.transform, "PumpPipeSide", new Vector3(-0.6f, 0.5f, 4.5f), new Vector3(1.8f, 0.16f, 0.16f), tankMat);
            Box(root.transform, "DrainGrate", new Vector3(-1.6f, 0.01f, 3.3f), new Vector3(1.1f, 0.02f, 1.1f),
                PropMeshes.Mat(new Color(0.2f, 0.16f, 0.1f), metallic: 0.5f, smooth: 0.3f));

            return root;
        }

        // ---- Chamber 2: the Heavy Stone Gate ----
        static GameObject BuildChamberGate(Transform parent, Rig rig)
        {
            var root = Root(parent, "Chamber2_Gate");

            // Door frame set into the back wall.
            var frameMat = PropMeshes.Mat(DarkStone, smooth: 0f);
            Box(root.transform, "GatePillarL", new Vector3(-1.75f, 1.9f, 5.0f), new Vector3(0.5f, 3.8f, 0.5f), frameMat);
            Box(root.transform, "GatePillarR", new Vector3(1.75f, 1.9f, 5.0f), new Vector3(0.5f, 3.8f, 0.5f), frameMat);
            Box(root.transform, "GateLintel", new Vector3(0f, 3.95f, 5.0f), new Vector3(4.2f, 0.5f, 0.6f), frameMat);

            // Bright doorway beyond — revealed as the slab rises.
            Box(root.transform, "DoorwayGlow", new Vector3(0f, 1.7f, 5.25f), new Vector3(2.9f, 3.4f, 0.05f),
                PropMeshes.Mat(Gold * 0.5f, smooth: 0.2f, emission: Gold * 1.6f));

            // The slab itself (Update slides it up on GateLift01; SetGateStage retints it).
            var slab = Box(root.transform, "GateSlab", new Vector3(0f, 1.75f, 5.05f), new Vector3(3.0f, 3.5f, 0.3f),
                           PropMeshes.Mat(DarkStone, smooth: 0.1f));
            rig.gateSlab = slab.transform;
            rig.gateSlabRenderer = slab.GetComponent<Renderer>();

            // Carved rune squares on the slab face so its motion is readable.
            var runeMat = PropMeshes.Mat(new Color(0.22f, 0.18f, 0.13f), smooth: 0f);
            foreach (float y in new[] { -0.9f, 0f, 0.9f })
                Box(slab.transform, "SlabRune", new Vector3(0f, y / 3.5f, -0.52f), new Vector3(0.35f, 0.16f, 0.1f), runeMat);

            return root;
        }

        // ---- Chamber 3: the Chasm Bridge ----
        static GameObject BuildChamberChasm(Transform parent, Rig rig)
        {
            var root = Root(parent, "Chamber3_Chasm");

            // Dark pit reading as a deep drop around the beam.
            Box(root.transform, "ChasmPit", new Vector3(0f, 0.012f, 1.8f), new Vector3(7.5f, 0.01f, 8.5f),
                PropMeshes.MatUnlit(new Color(0.03f, 0.03f, 0.045f)));

            // The narrow stone beam under the player, spanning the pit toward the far door.
            // Via PlaceProp so the FBX's Light/Camera children get disabled like every other prop.
            var beam = PlaceProp(root.transform, "bridge_stone", new Vector3(0f, 0.02f, 0.8f), 1.6f);
            if (beam == null)
            {
                Box(root.transform, "BeamFallback", new Vector3(0f, 0.1f, 0.8f), new Vector3(0.9f, 0.2f, 5.5f),
                    PropMeshes.Mat(DarkStone, smooth: 0f));
            }

            // Rune strip along the beam top — brightens with the player's balance hold.
            var strip = Quad(root.transform, "BeamGlowStrip", new Vector3(0f, 0.28f, 0.8f),
                             Quaternion.Euler(90f, 0f, 0f), new Vector3(0.5f, 4.5f, 1f));
            rig.beamGlowMat = TransparentUnlit("BeamGlowMat", RuneCyan * 0.25f);
            strip.GetComponent<Renderer>().sharedMaterial = rig.beamGlowMat;

            // Cliff rocks at both rims of the chasm (inside the visible cone).
            PlaceProp(root.transform, "cliff_top_rock", new Vector3(-1.7f, 0f, 3.2f), 1.2f);
            PlaceProp(root.transform, "cliff_top_rock", new Vector3(1.75f, 0f, 3.4f), 1.2f);
            PlaceProp(root.transform, "rock_largeA", new Vector3(-1.2f, 0f, 4.2f), 1.0f);

            // Cool mist drifting up from the pit.
            var mist = SoftMotes(root.transform, new Vector3(0f, 0.6f, 2.2f), new Vector3(4f, 1.2f, 5f),
                                 new Color(0.5f, 0.65f, 0.75f, 0.35f), rate: 8, rise: 0.08f);
            mist.name = "ChasmMist";

            return root;
        }

        // ---- Chamber 4: the Sacred Totem Vault ----
        static GameObject BuildChamberVault(Transform parent, Rig rig)
        {
            var root = Root(parent, "Chamber4_Vault");

            // The totem: three stacked stone drums in front of the back wall.
            var drumMat = PropMeshes.Mat(DarkStone, smooth: 0.05f);
            Cylinder(root.transform, "TotemBase", new Vector3(0f, 0.5f, 4.4f), new Vector3(1.5f, 0.5f, 1.5f), drumMat);
            Cylinder(root.transform, "TotemMid", new Vector3(0f, 1.5f, 4.4f), new Vector3(1.15f, 0.5f, 1.15f), drumMat);
            Cylinder(root.transform, "TotemTop", new Vector3(0f, 2.5f, 4.4f), new Vector3(0.85f, 0.5f, 0.85f), drumMat);

            // Five runes climbing the totem face — SetTotemLit01 lights them one by one.
            rig.runeMats = new Material[5];
            for (int i = 0; i < 5; i++)
            {
                float y = 0.4f + i * 0.62f;
                float x = (i % 2 == 0) ? -0.18f : 0.18f;
                var quad = Quad(root.transform, $"TotemRune{i}", new Vector3(x, y, 3.6f - i * 0.11f),
                                Quaternion.identity, new Vector3(0.34f, 0.34f, 1f));
                rig.runeMats[i] = TransparentUnlit($"TotemRuneMat{i}", RuneCyan * 0.2f);
                quad.GetComponent<Renderer>().sharedMaterial = rig.runeMats[i];
            }

            // Guardian bird statues flanking the totem.
            var statueMat = PropMeshes.Mat(Sandstone * 0.8f, smooth: 0f);
            foreach (float x in new[] { -1.75f, 1.75f })
            {
                Box(root.transform, "StatuePlinth", new Vector3(x, 0.35f, 3.9f), new Vector3(0.7f, 0.7f, 0.7f), statueMat);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Prep(body, root.transform, "StatueBird", new Vector3(x, 1.25f, 3.9f), new Vector3(0.45f, 0.55f, 0.45f), statueMat);
                Sphere(root.transform, "StatueHead", new Vector3(x, 1.95f, 3.85f), Vector3.one * 0.32f, statueMat);
                Box(root.transform, "StatueBeak", new Vector3(x, 1.9f, 3.62f), new Vector3(0.1f, 0.08f, 0.24f),
                    PropMeshes.Mat(Gold * 0.7f, metallic: 0.5f, smooth: 0.6f, emission: Gold * 0.4f));
            }

            // The treasure chest, angled toward the camera. Lid hinges at its back edge.
            var chestRoot = new GameObject("TreasureChest");
            chestRoot.transform.SetParent(root.transform, false);
            chestRoot.transform.localPosition = new Vector3(1.1f, 0f, 2.1f);
            chestRoot.transform.localRotation = Quaternion.Euler(0f, -20f, 0f);

            var woodMat = PropMeshes.Mat(new Color(0.4f, 0.26f, 0.13f), smooth: 0.15f);
            var trimMat = PropMeshes.Mat(Gold * 0.6f, metallic: 0.7f, smooth: 0.7f, emission: Gold * 0.35f);
            Box(chestRoot.transform, "ChestBody", new Vector3(0f, 0.28f, 0f), new Vector3(0.85f, 0.5f, 0.55f), woodMat);
            Box(chestRoot.transform, "ChestTrim", new Vector3(0f, 0.28f, 0f), new Vector3(0.88f, 0.12f, 0.58f), trimMat);

            var lidPivot = new GameObject("ChestLidPivot");
            lidPivot.transform.SetParent(chestRoot.transform, false);
            lidPivot.transform.localPosition = new Vector3(0f, 0.53f, 0.275f); // back top edge hinge
            rig.chestLidPivot = lidPivot.transform;
            Box(lidPivot.transform, "ChestLid", new Vector3(0f, 0.07f, -0.275f), new Vector3(0.85f, 0.14f, 0.55f), woodMat);
            Box(lidPivot.transform, "ChestLidTrim", new Vector3(0f, 0.07f, -0.275f), new Vector3(0.88f, 0.05f, 0.58f), trimMat);

            // Gold light spilling out once opened (off until OpenChest).
            var glow = Box(chestRoot.transform, "ChestGlow", new Vector3(0f, 0.56f, 0f), new Vector3(0.8f, 0.06f, 0.5f),
                           PropMeshes.Mat(Gold, smooth: 0.2f, emission: Gold * 2.2f));
            glow.SetActive(false);
            rig.chestGlow = glow;

            return root;
        }

        // ---- tiny local helpers ----

        static GameObject Root(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
            => Primitive(PrimitiveType.Cube, parent, name, pos, scale, mat);

        static GameObject Cylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
            => Primitive(PrimitiveType.Cylinder, parent, name, pos, scale, mat);

        static GameObject Sphere(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
            => Primitive(PrimitiveType.Sphere, parent, name, pos, scale, mat);

        static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Prep(go, parent, name, pos, scale, mat);
            return go;
        }

        static void Prep(GameObject go, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static GameObject Quad(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            return go;
        }

        static GameObject PlaceProp(Transform parent, string kenneyName, Vector3 pos, float scale)
        {
            var go = KenneyProps.Nature(kenneyName, parent, scale);
            if (go == null) return null;
            go.transform.localPosition = pos;
            // Defensive: never let an imported FBX ship a live light/camera into the scene
            // (the KinexUserModel intensity-1000 light lesson).
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            return go;
        }

        /// <summary>
        /// KinexFx.AmbientMotes upgraded with a soft round falloff sprite (the default particle
        /// material renders hard-edged squares — the MirrorStage lesson) and a gentle upward
        /// drift with ALL velocity axes in the same curve mode (mixed modes log a warning).
        /// </summary>
        static ParticleSystem SoftMotes(Transform parent, Vector3 center, Vector3 box, Color color,
                                        int rate, float rise)
        {
            var ps = KinexFx.AmbientMotes(center, box, color, rate);
            ps.transform.SetParent(parent, true);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.sharedMaterial = SoftDotMaterial();
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(rise * 0.6f, rise);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            return ps;
        }

        static Texture2D s_SoftDotTex;
        static Material s_SoftDotMat;

        static Material SoftDotMaterial()
        {
            if (s_SoftDotMat != null) return s_SoftDotMat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            s_SoftDotMat = new Material(shader) { name = "TempleDotMat" };
            if (s_SoftDotMat.HasProperty("_BaseMap")) s_SoftDotMat.SetTexture("_BaseMap", SoftDotTexture());
            else if (s_SoftDotMat.HasProperty("_MainTex")) s_SoftDotMat.SetTexture("_MainTex", SoftDotTexture());
            // The default particle material is Opaque and ignores the alpha falloff entirely —
            // must explicitly switch it to alpha-blended transparent (MirrorStage's fix).
            if (s_SoftDotMat.HasProperty("_Surface")) s_SoftDotMat.SetFloat("_Surface", 1f);
            if (s_SoftDotMat.HasProperty("_Cull")) s_SoftDotMat.SetFloat("_Cull", 0f);
            s_SoftDotMat.SetOverrideTag("RenderType", "Transparent");
            s_SoftDotMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_SoftDotMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive glow
            s_SoftDotMat.SetInt("_ZWrite", 0);
            s_SoftDotMat.EnableKeyword("_ALPHABLEND_ON");
            s_SoftDotMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_SoftDotMat;
        }

        // Soft round dot: quadratic falloff from opaque centre to transparent edge (deterministic).
        static Texture2D SoftDotTexture()
        {
            if (s_SoftDotTex != null) return s_SoftDotTex;
            const int size = 48;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = 1f - r * r;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            s_SoftDotTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_SoftDotTex.SetPixels32(px);
            s_SoftDotTex.Apply();
            return s_SoftDotTex;
        }

        static Material WaterMaterial()
        {
            var mat = TransparentUnlit("TempleWaterMat", WaterColor);
            return mat;
        }

        static Material TransparentUnlit(string name, Color color)
        {
            var mat = new Material(UnlitShader()) { name = name };
            SetBaseColor(mat, color);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        static Shader s_UnlitShader;
        static Shader UnlitShader()
        {
            if (s_UnlitShader != null) return s_UnlitShader;
            // Fallback chain for stripped builds — see Editor/AlwaysIncludedShaders.cs.
            s_UnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (s_UnlitShader == null) s_UnlitShader = Shader.Find("Unlit/Transparent");
            if (s_UnlitShader == null) s_UnlitShader = Shader.Find("Sprites/Default");
            if (s_UnlitShader == null) s_UnlitShader = Shader.Find("Unlit/Color");
            return s_UnlitShader;
        }

        static void SetBaseColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
        }
    }
}
