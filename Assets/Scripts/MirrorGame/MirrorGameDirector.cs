using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using Kinex.MegaDance;  // VoiceCoach
using Kinex.Trainer;    // TrainerPoseData

namespace Kinex.MirrorGame
{
    /// <summary>
    /// "กระจกวิเศษ / Magic Mirror" director. The player fits their body into a glowing ghost
    /// outline; each tracked joint has a world-space zone (see ZoneMatcher) and holding ALL zones
    /// filled for 2s clears the pose. Cycles the WHOLE RehabPoseData ladder in a fixed easy→hard
    /// order, then reports stars + coins to Flutter. Detection is ZoneMatcher only (joint-in-zone) —
    /// deliberately independent of PoseScorer/MegaDance, per the project decision.
    ///
    /// No timer, no fail: slower just scores fewer points, and after ~30s stuck the zones grow
    /// (assist) and the coach gives a hint — assist never reduces the score.
    /// </summary>
    public class MirrorGameDirector : MonoBehaviour
    {
        public enum State { Idle, Intro, PoseAnnounce, Matching, PosePassed, Results }

        [Header("Scene refs")]
        public MediaPipePoseDetector poseDetector;
        public Camera worldCamera;
        public VoiceCoach voice;

        [Header("Ghost outline (wired by the UI builder)")]
        [Tooltip("RehabPoseData.asset — the same baked poses the trainer rig uses in MegaDance/World.")]
        public TrainerPoseData rehabPoseData;
        [Tooltip("The TRAINER rig (NewTrainerAnimated.fbx) — the rig RehabPoseData was baked on. " +
                 "MirrorOutline height-matches its clone to the player avatar at runtime.")]
        public GameObject ghostRigPrefab;

        [Header("Timing")]
        public float introSeconds = 3.5f;
        public float announceSeconds = 3.5f; // long enough to READ the instruction (senior users)
        public float passCelebrateSeconds = 1.3f;

        [Header("UI - Intro")]
        public GameObject introPanel;
        public TMP_Text introTitleText;
        public TMP_Text introSubtitleText;

        [Header("UI - HUD")]
        public GameObject hudPanel;
        public TMP_Text poseCountText;        // "ท่าที่ 3/25"
        public TMP_Text poseNameText;         // the pose's Thai name
        public TMP_Text poseInstructionText;  // how to actually DO the pose (senior-readable)
        public TMP_Text firstPoseHintText;    // mechanic hint, shown during the first pose only
        public Image holdRingFill;            // radial fill 0..1, tracks the 2s hold
        public TMP_Text modeToggleLabel;      // "โหมดกำแพง" / "โหมดเงา"

        [Header("UI - Results")]
        public GameObject resultsPanel;
        public Image[] resultsStarImages;
        public TMP_Text resultsStatsText;

        // Fixed easy→hard ladder over RehabPoseData's 25 poses. The game OPENS with the arm
        // reaches, not the head turns: reaches move the wrist/elbow zone circles visibly, so the
        // very first pose teaches the core mechanic (move → circles turn green → hold). Head turns
        // barely move any tracked joint (rings track shoulders/elbows/wrists/knees/ankles) — as an
        // opener they gave zero feedback and players didn't know what to do (user test 2026-07-14);
        // as block two they work as a gentle guided neck stretch.
        static readonly int[] Ladder =
        {
            10, 11, 12,          // reach clockwise (arms — visible + detectable, teaches the loop)
            0, 1, 2, 3,          // head turns (neck only — gentle stretch block)
            8, 9,                // lean to the side
            17, 18, 19, 20,      // trunk rotation (standing)
            6, 7,                // march in place (knee up)
            13, 14,              // turn / look behind
            21, 22,              // cross-step
            4, 5,                // toe touch (deep forward bend)
            15, 16,              // single-leg balance
            23, 24,              // sit-stand (squat) — hardest
        };

        // How to DO each pose, indexed by POSE index (not ladder position) — shown big on the HUD
        // card and spoken by the coach. RehabPoseData names are labels ("หมุนศีรษะ • 1/4 (หันซ้าย)"),
        // not instructions; user test showed players didn't know what the pose wanted.
        static readonly string[] Instructions =
        {
            "ยืนตรง ค่อยๆ หันหน้าไปทางซ้าย",          // 0 head left
            "ยืนตรง ค่อยๆ หันหน้าไปทางขวา",           // 1 head right
            "ยืนตรง ค่อยๆ เงยหน้าขึ้นด้านบน",          // 2 head up
            "ยืนตรง ค่อยๆ ก้มหน้ามองพื้น",             // 3 head down
            "ก้มตัวลง เอื้อมมือแตะปลายเท้าซ้าย",       // 4 toe touch L
            "ก้มตัวลง เอื้อมมือแตะปลายเท้าขวา",        // 5 toe touch R
            "ยกเข่าซ้ายขึ้น เหมือนเดินอยู่กับที่",       // 6 march L
            "ยกเข่าขวาขึ้น เหมือนเดินอยู่กับที่",        // 7 march R
            "กางขาซ้ายออกด้านข้าง โยกตัวตาม",          // 8 lean L
            "กางขาขวาออกด้านข้าง โยกตัวตาม",           // 9 lean R
            "ยกแขนทั้งสองข้างขึ้นเหนือศีรษะ",           // 10 reach 12 o'clock
            "กางแขนออกด้านข้างระดับไหล่",              // 11 reach side
            "เหยียดแขนไปด้านหลังเท่าที่ไหว",           // 12 reach back
            "หมุนตัวมองข้ามไหล่ขวาไปด้านหลัง",         // 13 look behind R
            "หมุนตัวมองข้ามไหล่ซ้ายไปด้านหลัง",        // 14 look behind L
            "ยืนขาขวา ยกขาซ้าย กางแขนทรงตัว",          // 15 balance L up
            "ยืนขาซ้าย ยกขาขวา กางแขนทรงตัว",          // 16 balance R up
            "เอนตัวไปด้านหน้าช้าๆ หลังตรง",            // 17 trunk fwd
            "เอนตัวไปทางซ้ายช้าๆ",                    // 18 trunk L
            "เอนตัวไปด้านหลังเล็กน้อย",                // 19 trunk back
            "เอนตัวไปทางขวาช้าๆ",                     // 20 trunk R
            "ก้าวเท้าซ้ายไขว้ไปด้านหน้า",              // 21 cross-step L
            "ก้าวเท้าขวาไขว้ไปด้านหลัง",               // 22 cross-step R
            "ย่อตัวลงช้าๆ เหมือนนั่งเก้าอี้",           // 23 squat down
            "ลุกขึ้นยืนตรงช้าๆ",                      // 24 stand up
        };

        const string FirstPoseHint = "ขยับตามเงาสีทอง ให้วงกลมทุกวงเป็นสีเขียว แล้วค้างไว้";

        static readonly Color ZoneGreen = new Color(0.32f, 0.9f, 0.42f, 0.95f);
        static readonly Color ZoneAmber = new Color(1f, 0.75f, 0.25f, 0.95f);
        static readonly Color ZoneRed = new Color(0.92f, 0.32f, 0.32f, 0.9f);
        static readonly Color StarOn = new Color(1f, 0.84f, 0.2f);
        static readonly Color StarOff = new Color(1f, 1f, 1f, 0.22f);

        public event Action<MirrorResult> OnSessionComplete;
        public event Action OnExitRequested;

        State _state = State.Idle;
        MirrorOutline _outline;
        MirrorWall _wall;
        bool _wallMode;

        readonly Vector3[] _targets = new Vector3[ZoneMatcher.JointCount];
        readonly Vector3[] _players = new Vector3[ZoneMatcher.JointCount];
        readonly bool[] _inZone = new bool[ZoneMatcher.JointCount];
        readonly Color[] _zoneColors = new Color[ZoneMatcher.JointCount];
        bool _wasAllIn;

        int _totalScore;
        int _posesCompleted;
        float _startTime;

        public State CurrentState => _state;
        public bool HasOutline => _outline != null;
        public MirrorWall Wall => _wall;

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            ShowOnly(introPanel);
            StartCoroutine(AutoStart());
        }

        IEnumerator AutoStart()
        {
            yield return null; // let every other Awake/Start finish first
            StartCoroutine(GameFlow());
        }

        // ---- buttons (wired by MirrorGameUIBuilder) ----
        public void PlayAgain()
        {
            StopAllCoroutines();
            StartCoroutine(GameFlow());
        }

        public void ExitToHome() => OnExitRequested?.Invoke();

        public void ToggleMode()
        {
            _wallMode = !_wallMode;
            if (_outline != null) _outline.SetGhostVisible(!_wallMode);
            if (_wall != null)
            {
                _wall.SetVisible(_wallMode);
                if (_wallMode) _wall.Rebuild();
            }
            if (modeToggleLabel != null) modeToggleLabel.text = _wallMode ? "โหมดเงา" : "โหมดกำแพง";
        }

        // =====================================================================
        IEnumerator GameFlow()
        {
            _startTime = Time.time;
            _totalScore = 0;
            _posesCompleted = 0;
            _wallMode = false;

            yield return IntroRoutine();
            EnsureOutlineAndWall();

            ShowOnly(hudPanel);
            if (modeToggleLabel != null) modeToggleLabel.text = "โหมดกำแพง";

            for (int i = 0; i < Ladder.Length; i++)
                yield return RunPose(i);

            yield return ResultsRoutine();
        }

        IEnumerator IntroRoutine()
        {
            _state = State.Intro;
            ShowOnly(introPanel);
            Music.Play("mirror_theme", 0.35f);
            if (introTitleText != null) introTitleText.text = "กระจกวิเศษ";
            if (introSubtitleText != null)
                introSubtitleText.text = "ขยับตัวให้พอดีกับเงาสีทองในกระจก\nทำให้วงกลมทุกวงเป็นสีเขียว แล้วค้างไว้\nค่อยๆ ทำ ไม่มีจับเวลา";
            Speak("ยินดีต้อนรับสู่กระจกวิเศษ ขยับตัวให้พอดีกับเงาในกระจก ทำให้วงกลมทุกวงเป็นสีเขียวนะครับ");
            yield return new WaitForSeconds(introSeconds);
        }

        IEnumerator RunPose(int ladderIndex)
        {
            int poseIndex = Ladder[ladderIndex];

            // ---- Announce: blend the ghost to the new pose, re-punch the wall hole, speak. ----
            _state = State.PoseAnnounce;
            _outline?.ShowPose(poseIndex);
            _outline?.FadeTo(1f);
            if (_wall != null && _wallMode) _wall.Rebuild();

            string instruction = PoseInstruction(poseIndex);
            if (poseCountText != null) poseCountText.text = $"ท่าที่ {ladderIndex + 1}/{Ladder.Length}";
            if (poseNameText != null) poseNameText.text = PoseName(poseIndex);
            if (poseInstructionText != null) poseInstructionText.text = instruction;
            if (firstPoseHintText != null) firstPoseHintText.gameObject.SetActive(ladderIndex == 0);
            if (holdRingFill != null) holdRingFill.fillAmount = 0f;
            Sfx.Play("whoosh", 0.6f);
            // Speak the HOW, not the label — and teach the ring mechanic on the very first pose.
            Speak(ladderIndex == 0 ? instruction + " " + FirstPoseHint : instruction);
            yield return new WaitForSeconds(announceSeconds);
            // The wall hole is carved from the BLENDED bones — rebuild once the blend has landed.
            if (_wall != null && _wallMode) _wall.Rebuild();

            // ---- Matching + Holding: ZoneMatcher drives the 2s hold ring. ----
            _state = State.Matching;
            var progress = ZoneMatcher.PoseProgress.Start();
            _wasAllIn = false;

            while (!progress.Passed)
            {
                float dt = Time.deltaTime;

                if (_outline != null) _outline.TargetPositions(_targets);
                ReadPlayerPositions();

                bool snap = DebugSnap();
                int inCount;
                if (snap)
                {
                    for (int i = 0; i < ZoneMatcher.JointCount; i++) _inZone[i] = true;
                    inCount = ZoneMatcher.JointCount;
                }
                else
                {
                    inCount = ZoneMatcher.Evaluate(_targets, _players, progress.RadiusMultiplier, _inZone);
                }

                bool allIn = inCount == ZoneMatcher.JointCount;
                progress.Tick(allIn, dt);

                BuildZoneColors(progress.RadiusMultiplier);
                _outline?.ApplyZones(_zoneColors, progress.RadiusMultiplier);
                if (holdRingFill != null) holdRingFill.fillAmount = progress.HoldFill01;

                if (allIn && !_wasAllIn) Sfx.Play("pop", 0.5f);
                _wasAllIn = allIn;

                if (progress.AssistTriggered)
                    Speak("ค่อยๆ ขยับให้เข้าใกล้วงนะครับ เดี๋ยววงจะกว้างขึ้นให้");

                yield return null;
            }

            // ---- Passed: score, coins chime, brief celebration. ----
            _state = State.PosePassed;
            _totalScore += ZoneMatcher.PoseScore(progress.LockInSeconds, progress.Dropouts);
            _posesCompleted++;
            if (holdRingFill != null) holdRingFill.fillAmount = 1f;
            Sfx.Play("star");
            Sfx.Play("coin", 0.7f);
            Speak("เก่งมากครับ");
            if (_outline != null)
                Kinex.FX.KinexFx.ConfettiBurst(
                    _outline.BonePos(HumanBodyBones.Chest) + Vector3.up * 0.2f, 50);
            if (firstPoseHintText != null) firstPoseHintText.gameObject.SetActive(false);
            _outline?.FadeTo(0.25f);
            yield return new WaitForSeconds(passCelebrateSeconds);
        }

        IEnumerator ResultsRoutine()
        {
            _state = State.Results;
            ShowOnly(resultsPanel);
            Music.Stop();
            Sfx.Play("fanfare");

            int stars = ZoneMatcher.Stars(_totalScore, Ladder.Length);
            int coins = ZoneMatcher.Coins(_totalScore, stars);

            var result = new MirrorResult
            {
                posesCompleted = _posesCompleted,
                poseCount = Ladder.Length,
                totalScore = _totalScore,
                coins = coins,
                stars = stars,
                durationSeconds = Time.time - _startTime,
            };

            if (resultsStarImages != null)
                for (int i = 0; i < resultsStarImages.Length; i++)
                    if (resultsStarImages[i] != null)
                        resultsStarImages[i].color = i < stars ? StarOn : StarOff;

            if (resultsStatsText != null)
                resultsStatsText.text =
                    $"ทำครบ {result.posesCompleted}/{result.poseCount} ท่า\n" +
                    $"คะแนนรวม {result.totalScore}\n" +
                    $"เหรียญ {result.coins}";

            Speak("เยี่ยมมากครับ ทำครบทุกท่าแล้ว");
            yield return null; // keep this an iterator block
            OnSessionComplete?.Invoke(result);
        }

        // ---- helpers --------------------------------------------------------

        void EnsureOutlineAndWall()
        {
            if (_outline != null) return;
            if (poseDetector == null || rehabPoseData == null || ghostRigPrefab == null)
            {
                Debug.LogWarning("[MirrorGameDirector] Missing detector / poseData / ghost prefab — " +
                                 "outline disabled (pose card + TTS still run).");
                return;
            }
            // Never let outline construction kill the GameFlow coroutine — an exception here left
            // the intro popup on screen forever (device-only Shader.Find failure, 2026-07-14).
            // Degrade to the card+TTS-only flow instead.
            try
            {
                _outline = MirrorOutline.Create(poseDetector.transform, ghostRigPrefab, rehabPoseData, Ladder[0]);
                if (_outline != null)
                {
                    Vector3 center = poseDetector.transform.position + Vector3.up * 1.1f;
                    _wall = MirrorWall.Create(_outline, center);
                    _outline.SetGhostVisible(true);
                    _wall.SetVisible(false);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MirrorGameDirector] outline/wall build failed — continuing without: {e}");
            }
        }

        void ReadPlayerPositions()
        {
            var anim = poseDetector != null ? poseDetector.AvatarAnimator : null;
            bool hasPose = poseDetector != null && poseDetector.HasPose;
            for (int i = 0; i < ZoneMatcher.JointCount; i++)
            {
                var t = (anim != null && hasPose) ? anim.GetBoneTransform(MirrorOutline.JointBones[i]) : null;
                _players[i] = t != null ? t.position : ZoneMatcher.Invalid;
            }
        }

        // Per-joint colour: green in-zone, amber when close, red when far / untracked.
        void BuildZoneColors(float radiusMult)
        {
            for (int i = 0; i < ZoneMatcher.JointCount; i++)
            {
                if (_inZone[i]) { _zoneColors[i] = ZoneGreen; continue; }
                bool tracked = _players[i].x != ZoneMatcher.Invalid.x;
                if (tracked)
                {
                    float dist = Vector3.Distance(_targets[i], _players[i]);
                    float rad = ZoneMatcher.BaseRadius[i] * radiusMult;
                    _zoneColors[i] = dist < rad * 1.7f ? ZoneAmber : ZoneRed;
                }
                else _zoneColors[i] = ZoneRed;
            }
        }

        static string PoseInstruction(int poseIndex)
        {
            if (poseIndex >= 0 && poseIndex < Instructions.Length) return Instructions[poseIndex];
            return "ทำท่าตามเงาในกระจก";
        }

        string PoseName(int poseIndex)
        {
            if (rehabPoseData != null && rehabPoseData.poses != null &&
                poseIndex >= 0 && poseIndex < rehabPoseData.poses.Length)
                return rehabPoseData.poses[poseIndex].name;
            return "";
        }

        bool DebugSnap()
        {
#if UNITY_EDITOR
            var kb = Keyboard.current;
            return kb != null && kb.gKey.isPressed; // hold G = snap player to ghost (editor testing)
#else
            return false;
#endif
        }

        void Speak(string line)
        {
            if (voice == null) return;
            Music.Duck(2.5f);
            voice.Speak(line);
        }

        void ShowOnly(GameObject panel)
        {
            GameObject[] all = { introPanel, hudPanel, resultsPanel };
            foreach (var p in all)
                if (p != null) p.SetActive(p == panel);
        }
    }
}
