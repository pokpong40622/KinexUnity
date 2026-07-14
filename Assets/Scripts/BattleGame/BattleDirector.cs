using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.FX;
using Kinex.Motion;
using Kinex.MegaDance; // VoiceCoach
using Kinex.Trainer;   // TrainerPoseData / TrainerPoseController (pose ghost)

namespace Kinex.BattleGame
{
    /// <summary>
    /// "ผู้พิทักษ์สวนสมดุล" (Guardian of Balance) turn-based pose-battle. PLAYER TURN draws a
    /// weighted pose card and grades the attempt continuously (never a hard miss); ENEMY TURN
    /// telegraphs then asks for Shield (single-leg hold) or Dodge (side-step) — failing costs a
    /// heart, and 0 hearts is a gentle retry of the CURRENT monster only (never a game-over).
    /// Each level is 2 monsters + a boss. Motion source is swappable exactly like
    /// FruitGameManager/BalanceQuestDirector: useKeyboardStub (default true) plays the whole
    /// game from a keyboard; flip it off for the live MediaPipe-driven Kinex.Motion detectors.
    /// </summary>
    public class BattleDirector : MonoBehaviour
    {
        public enum State { Idle, Intro, Calibrating, Countdown, PlayerTurn, EnemyTurn, MonsterDefeat, Results }

        [Header("Level (1..3 — Meadow / Crystal Cave / Sky Castle)")]
        [Range(1, 3)] public int level = 1;

        [Header("Motion source")]
        [Tooltip("Play with the keyboard (see Kinex.Motion.KeyboardMotionStub for the key map). " +
                 "Bypasses the camera entirely so the whole game runs in the editor.")]
        public bool useKeyboardStub = true;
        public MediaPipePoseDetector poseDetector;

        [Header("Scene refs")]
        public Transform monsterSlot;
        public Camera worldCamera;
        public VoiceCoach voice;

        [Header("Timing")]
        public float introSeconds = 3f;
        public float calibSeconds = 3f;
        public float calibTimeoutSeconds = 15f;
        [Range(1, 5)] public int countdownSeconds = 3;
        public float enemyDefendWindowSeconds = 3.5f;
        public float gentleRetryPauseSeconds = 2f;

        [Header("UI - Intro")]
        public GameObject introPanel;
        public TMP_Text introTitleText;
        public TMP_Text introSubtitleText;

        [Header("UI - Calibration")]
        public GameObject calibPanel;
        public TMP_Text calibText;

        [Header("UI - Countdown")]
        public GameObject countdownPanel;
        public TMP_Text countdownText;

        [Header("UI - HUD")]
        public GameObject hudPanel;
        public TMP_Text monsterNameText;
        public Image monsterHpFill;
        public Image[] heartImages;
        public TMP_Text comboText;
        public GameObject chargeMeterRoot;
        public Image chargeMeterFill;

        [Header("UI - Pose Card")]
        public GameObject cardPanel;
        public TMP_Text cardNameText;
        public Image cardBadge;
        public Image cardTimerFill;

        [Header("UI - Enemy Telegraph / Defend")]
        public GameObject telegraphPanel;
        public TMP_Text telegraphText;
        public GameObject defendPanel;
        public TMP_Text defendText;
        public Image defendTimerFill;

        [Header("UI - Results")]
        public GameObject resultsPanel;
        public Image[] resultsStarImages;
        public TMP_Text resultsStatsText;

        [Header("UI - Pause")]
        public GameObject pauseOverlay;

        [Header("UI - Pose Ghost (spirit coach)")]
        [Tooltip("RehabPoseData.asset — same pose data the trainer rig uses in MegaDance/Kinex World. " +
                 "Wired by BattleGameUIBuilder (AssetDatabase.LoadAssetAtPath), not authored in-scene.")]
        public TrainerPoseData rehabPoseData;
        [Tooltip("NewTrainerAnimated.fbx — instantiated at runtime beside the player avatar to strike " +
                 "the target pose. Wired by BattleGameUIBuilder. Left unassigned = ghost quietly disabled " +
                 "(pose card text/TTS still work on their own).")]
        public GameObject trainerRigPrefab;
        [Tooltip("World-space offset from this director's transform where the ghost stands — beside " +
                 "the player avatar (origin), clear of the monster slot which sits far across the arena.")]
        public Vector3 ghostLocalPosition = new Vector3(0.75f, 0f, -0.15f);

        [Header("UI - Feedback (border glow / screen pulse / checkmark)")]
        [Tooltip("Soft glow ring around the pose card — alpha/color track detector progress ('getting warmer').")]
        public ScreenGlow cardGlow;
        [Tooltip("Full-screen soft glow — pulses green/amber on a hit, gentle red on a failed defend.")]
        public ScreenGlow screenGlow;
        [Tooltip("Big checkmark shown briefly on a Good/Perfect hit.")]
        public GameObject successCheckmark;

        [Header("UI - Tutorial (one-time per new skill)")]
        public GameObject tutorialPanel;
        public TMP_Text tutorialNameText;
        public TMP_Text tutorialInstructionText;
        public float tutorialSeconds = 6f;

        static readonly Color GlowCool = new Color(0.35f, 0.7f, 0.95f);
        static readonly Color GlowHot = new Color(1f, 0.82f, 0.25f);
        static readonly Color GlowSuccessGreen = new Color(0.35f, 0.85f, 0.45f);
        static readonly Color GlowAmber = new Color(1f, 0.75f, 0.25f);
        static readonly Color GlowGentleRed = new Color(0.9f, 0.3f, 0.3f);

        public event Action<BattleResult> OnSessionComplete;
        public event Action OnExitRequested;

        State _state = State.Idle;

        readonly SitStandDetector _sitStand = new SitStandDetector();
        readonly KneeRaiseDetector _knee = new KneeRaiseDetector();
        readonly LegAbductionDetector _abduction = new LegAbductionDetector();
        readonly HipExtensionDetector _hipExt = new HipExtensionDetector();
        readonly TiptoeDetector _tiptoe = new TiptoeDetector();
        readonly SingleLegStanceDetector _singleLeg = new SingleLegStanceDetector();
        readonly LaneDetector _lane = new LaneDetector();
        readonly PoseGate _gate = new PoseGate();
        readonly KeyboardMotionStub _stub = new KeyboardMotionStub();
        readonly TandemHoldTracker _tandemTracker = new TandemHoldTracker();

        readonly System.Random _rng = new System.Random(); // explicit: UnityEngine.Random is also in scope here

        int _hearts;
        int _comboStreak;
        int _bestCombo;
        int _coins;
        int _totalDamage;
        int _monstersDefeated;
        float _qualitySum;
        int _qualitySamples;
        float _startTime;
        int _previousPoolIndex = -1;

        MonsterBlob _currentMonsterBlob;
        int _currentMonsterHp;
        int _currentMonsterMaxHp;

        PoseGhost _poseGhost;
        readonly HashSet<PoseSkillId> _seenSkills = new HashSet<PoseSkillId>();

        // Cumulative counters — turn windows read a DELTA against a baseline captured at the
        // start of that turn (same idea as FruitGameManager's bonus-round KneeCount baseline).
        float _tiptoeHoldTotal;
        int _fireKickTotal, _tailWhipTotal;
        float _turnKneeBaseline, _turnTiptoeBaseline, _turnTandemBaseline;
        int _turnFireKickBaseline, _turnTailWhipBaseline;
        float _lastTurnQuality;

        void Start()
        {
            ShowOnly(introPanel);
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            EnsureGhost();
            StartCoroutine(AutoStart());
        }

        // ---- Pose ghost: built once, entirely at runtime (never scene-authored — see the class
        // doc on PoseGhost). Missing prefab/pose data just means no ghost; the pose card's Thai
        // text/TTS instruction still carries the turn on its own. ----
        void EnsureGhost()
        {
            if (_poseGhost != null || trainerRigPrefab == null || rehabPoseData == null) return;
            var anchor = new GameObject("PoseGhostAnchor").transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = ghostLocalPosition;
            _poseGhost = PoseGhost.Create(anchor, trainerRigPrefab, rehabPoseData, 0, Vector3.zero);
        }

        IEnumerator AutoStart()
        {
            yield return null; // one frame so every other Awake/Start completes first
            StartCoroutine(GameFlow());
        }

        // ---- Buttons (wired by BattleGameUIBuilder) ----
        public void PlayAgain()
        {
            StopAllCoroutines();
            StartCoroutine(GameFlow());
        }

        public void ExitToHome() => OnExitRequested?.Invoke();

        // =====================================================================
        IEnumerator GameFlow()
        {
            _startTime = Time.time;
            _hearts = BattleLogic.MaxHearts;
            _comboStreak = 0; _bestCombo = 0; _coins = 0; _totalDamage = 0; _monstersDefeated = 0;
            _qualitySum = 0f; _qualitySamples = 0;
            _previousPoolIndex = -1;
            _seenSkills.Clear();
            UpdateHeartsUi();

            yield return IntroRoutine();
            yield return Calibrate();
            yield return CountdownRoutine();

            var monsters = LevelLibrary.Monsters(level);
            for (int i = 0; i < monsters.Length; i++)
            {
                yield return RunMonsterFight(monsters[i]);
                _monstersDefeated++;
            }

            yield return ShowResultsRoutine();
        }

        IEnumerator IntroRoutine()
        {
            _state = State.Intro;
            ShowOnly(introPanel);
            Music.Play("battle_theme");
            bool hasChair = level <= 1;
            string theme = LevelLibrary.ThemeNameThai(level);
            if (introTitleText != null) introTitleText.text = "ผู้พิทักษ์สวนสมดุล";
            if (introSubtitleText != null)
                introSubtitleText.text = hasChair
                    ? $"ด่าน {theme}\nเตรียมเก้าอี้มั่นคงไว้ด้านหลังก่อนเริ่มนะครับ"
                    : $"ด่าน {theme}\nยืนให้กล้องเห็นเต็มตัวนะครับ";
            Speak(hasChair
                ? "ยินดีต้อนรับสู่ผู้พิทักษ์สวนสมดุล เตรียมเก้าอี้มั่นคงไว้ด้านหลังนะครับ"
                : "ยินดีต้อนรับสู่ผู้พิทักษ์สวนสมดุล ยืนให้กล้องเห็นเต็มตัวนะครับ");
            yield return new WaitForSeconds(introSeconds);
        }

        // ---- Calibration: standing baseline for every detector; level 1 (chair) also derives a
        // seated baseline from it (EstimateSeatedFromStanding) so the player is never asked to
        // sit still during setup — only Earth Slam later actually asks them to sit-then-stand. ----
        IEnumerator Calibrate()
        {
            _state = State.Calibrating;
            ShowOnly(calibPanel);
            bool hasChair = level <= 1;
            if (calibText != null)
                calibText.text = hasChair ? "ยืนตรงกลาง เตรียมเก้าอี้ไว้ด้านหลัง" : "ยืนตรงกลางให้เห็นเต็มตัว";

            if (useKeyboardStub) { yield return new WaitForSeconds(1f); yield break; }

            float t = 0f;
            while (t < calibTimeoutSeconds)
            {
                float dt = Time.deltaTime;
                t += dt;

                var kp = poseDetector != null ? poseDetector.LatestKeypoints : null;
                var conf = poseDetector != null ? poseDetector.LatestConfidence : null;
                bool hasPose = poseDetector != null && poseDetector.HasPose;
                _gate.Tick(hasPose, conf, dt);

                if (_gate.Visible && kp != null)
                {
                    _lane.CalibrateCenter(kp);
                    _singleLeg.CalibrateStanding(kp);
                    _tiptoe.CalibrateStanding(kp);
                    _abduction.SetBaseline(kp);
                    _knee.SetBaseline(kp);
                    if (hasChair)
                    {
                        _sitStand.CalibrateStanding(kp);
                        _sitStand.EstimateSeatedFromStanding();
                    }
                    var lm = poseDetector != null ? poseDetector.Landmarks33 : null;
                    if (lm != null) _hipExt.SetBaseline(lm);

                    if (t >= calibSeconds && _lane.IsCalibrated) break;
                }
                yield return null;
            }
        }

        IEnumerator CountdownRoutine()
        {
            _state = State.Countdown;
            ShowOnly(countdownPanel);
            for (int n = countdownSeconds; n > 0; n--)
            {
                if (countdownText != null)
                {
                    countdownText.text = n.ToString();
                    countdownText.transform.localScale = Vector3.one;
                    SimpleTween.ScalePop(countdownText.transform, this, 1.25f, 0.3f);
                }
                Sfx.Play("beep");
                yield return new WaitForSeconds(1f);
            }
            if (countdownText != null)
            {
                countdownText.text = "ไป!";
                SimpleTween.ScalePop(countdownText.transform, this, 1.25f, 0.3f);
            }
            Sfx.Play("go");
            yield return new WaitForSeconds(0.6f);
        }

        // ---- One monster: player/enemy turns alternate until its HP hits 0. 0 hearts mid-fight
        // is a GENTLE RETRY of this same monster (fresh hearts + fresh monster HP) — never a
        // harsh game-over. ----
        IEnumerator RunMonsterFight(MonsterDef def)
        {
            SpawnMonster(def);
            _currentMonsterMaxHp = def.maxHp;
            _currentMonsterHp = def.maxHp;
            UpdateMonsterHpUi();
            Speak(def.isBoss ? "ราชันหนามทองปรากฏตัว!" : $"{def.nameThai} ปรากฏตัว!");

            while (true)
            {
                yield return RunPlayerTurnCycle();
                if (_currentMonsterHp <= 0) break;

                yield return RunEnemyTurn(def);
                if (BattleLogic.ShouldRetryMonster(_hearts))
                    yield return GentleRetryRoutine();
            }

            yield return MonsterDefeatRoutine(def);
        }

        IEnumerator RunPlayerTurnCycle()
        {
            var pool = LevelLibrary.Pool(level);
            var weights = BattleLogic.ApplyHealGate(LevelLibrary.BaseWeights(level), LevelLibrary.HealIndex, _hearts);
            int idx = BattleLogic.DrawWeightedIndex(weights, _previousPoolIndex, _rng);
            if (idx < 0) idx = 1; // degenerate pool fallback — StompQuake is always available
            _previousPoolIndex = idx;
            var skillId = pool[idx];
            var skill = PoseSkillLibrary.Get(skillId);

            _state = State.PlayerTurn;
            if (BattleLogic.FirstTimeThisSession(_seenSkills, skillId))
                yield return TutorialRoutine(skill);

            yield return RunPlayerTurnWindow(skill);
            _qualitySum += _lastTurnQuality;
            _qualitySamples++;
            var tier = BattleLogic.Tier(_lastTurnQuality);
            PlayResultGlow(tier);

            if (skillId == PoseSkillId.FocusHeal)
            {
                _hearts = BattleLogic.Heal(_hearts);
                UpdateHeartsUi();
                Sfx.Play("heal");
                Speak("หัวใจเพิ่มขึ้นแล้ว");
                yield break;
            }

            int dmg = BattleLogic.Damage(skill.baseDamage, _lastTurnQuality, _comboStreak);
            _comboStreak = BattleLogic.ComboStreakAfter(_comboStreak, tier);
            _bestCombo = Math.Max(_bestCombo, _comboStreak);
            _coins += BattleLogic.CoinsForHit(tier);
            _totalDamage += dmg;
            _currentMonsterHp = Math.Max(0, _currentMonsterHp - dmg);

            PlayHitFx(tier, dmg);
            UpdateMonsterHpUi();
            UpdateComboUi(tier);
        }

        // ---- One-time tutorial card per NEW skill: name + ghost pose + one-line Thai instruction,
        // auto-continues after tutorialSeconds (no input required — never blocks the player). ----
        IEnumerator TutorialRoutine(PoseSkillDef skill)
        {
            if (tutorialPanel == null) yield break;
            ShowOnly(hudPanel);
            tutorialPanel.SetActive(true);
            if (tutorialNameText != null) tutorialNameText.text = skill.nameThai;
            if (tutorialInstructionText != null) tutorialInstructionText.text = skill.announceLine;

            int ghostIdx = BattleGhostPoses.FindPoseIndex(rehabPoseData, skill.id);
            if (_poseGhost != null && ghostIdx >= 0)
            {
                _poseGhost.ShowPoseIndex(ghostIdx);
                _poseGhost.FadeTo(1f);
            }
            Sfx.Play("pose_appear");
            Speak(skill.announceLine);

            yield return new WaitForSeconds(tutorialSeconds);
            tutorialPanel.SetActive(false);
            _poseGhost?.FadeTo(0f);
        }

        // ---- Success/fail feedback: soft full-screen glow pulse, never a harsh flash/shake. ----
        void PlayResultGlow(Quality tier)
        {
            if (screenGlow == null) return;
            if (tier == Quality.Ok)
            {
                screenGlow.Pulse(GlowAmber, 0.28f);
                return;
            }
            screenGlow.Pulse(GlowSuccessGreen, 0.35f);
            StartCoroutine(FlashCheckmark());
        }

        IEnumerator FlashCheckmark()
        {
            if (successCheckmark == null) yield break;
            successCheckmark.SetActive(true);
            successCheckmark.transform.localScale = Vector3.one * 0.6f;
            SimpleTween.ScalePop(successCheckmark.transform, this, 1.15f, 0.25f);
            yield return new WaitForSeconds(0.9f);
            successCheckmark.SetActive(false);
        }

        IEnumerator RunPlayerTurnWindow(PoseSkillDef skill)
        {
            ShowOnly(hudPanel);
            if (cardPanel != null) cardPanel.SetActive(true);
            if (cardNameText != null) cardNameText.text = skill.nameThai;
            if (cardBadge != null) cardBadge.color = SkillColor(skill.id);
            if (chargeMeterRoot != null) chargeMeterRoot.SetActive(skill.metric == SkillMetric.HoldRatio);
            if (chargeMeterFill != null) chargeMeterFill.fillAmount = 0f;
            if (cardGlow != null) cardGlow.SetGlow(GlowCool, 0.15f);
            Sfx.Play("pose_appear");
            Speak(skill.announceLine);

            int ghostIdx = BattleGhostPoses.FindPoseIndex(rehabPoseData, skill.id);
            if (_poseGhost != null && ghostIdx >= 0)
            {
                _poseGhost.ShowPoseIndex(ghostIdx);
                _poseGhost.FadeTo(1f);
            }
            else
            {
                _poseGhost?.FadeTo(0f); // no rehab pose maps to this skill — text/TTS carries the turn
            }

            ResetTurnBaselines();
            float window = PoseSkillLibrary.PlayerTurnWindowSeconds;
            float t = 0f;
            float achievedRatio = 0f;
            bool eventFired = false;
            float eventFiredAt = -1f;

            while (t < window)
            {
                float dt = Time.deltaTime;
                t += dt;
                TickDetectors(dt);
                if (cardTimerFill != null) cardTimerFill.fillAmount = 1f - t / window;

                bool paused = !useKeyboardStub && _gate.ShouldPauseGame;
                if (pauseOverlay != null) pauseOverlay.SetActive(paused);

                if (!paused)
                {
                    switch (skill.metric)
                    {
                        case SkillMetric.EventSpeed:
                            if (!eventFired && JustStoodNow()) { eventFired = true; eventFiredAt = t; }
                            break;
                        case SkillMetric.CountRatio:
                            achievedRatio = BattleLogic.Ratio(CountFor(skill.id), skill.targetValue);
                            break;
                        case SkillMetric.HoldRatio:
                            achievedRatio = BattleLogic.Ratio(HoldSecondsFor(skill.id), skill.targetValue);
                            if (chargeMeterFill != null) chargeMeterFill.fillAmount = achievedRatio;
                            break;
                    }
                }

                // "Getting warmer": card border + ghost glow intensify with detector progress. An
                // EventSpeed skill (e.g. stand-up-fast) has no partial signal to show — it stays
                // calm right up until the moment it fires.
                float progress01 = skill.metric == SkillMetric.EventSpeed ? (eventFired ? 1f : 0f) : achievedRatio;
                if (cardGlow != null) cardGlow.SetGlow(Color.Lerp(GlowCool, GlowHot, progress01), 0.18f + 0.7f * progress01);
                _poseGhost?.SetProgressGlow(progress01);

                bool doneEarly = (skill.metric == SkillMetric.EventSpeed && eventFired) ||
                                  (skill.metric != SkillMetric.EventSpeed && achievedRatio >= 1f);
                if (doneEarly) { yield return new WaitForSeconds(0.15f); break; }
                yield return null;
            }

            float rawQuality = skill.metric == SkillMetric.EventSpeed
                ? (eventFired ? BattleLogic.Clamp01(1f - eventFiredAt / window) : 0f)
                : achievedRatio;
            _lastTurnQuality = BattleLogic.QualityWithFloor(rawQuality);

            if (cardPanel != null) cardPanel.SetActive(false);
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            if (cardGlow != null) cardGlow.SetGlow(GlowCool, 0f);
            _poseGhost?.FadeTo(0f);
        }

        IEnumerator RunEnemyTurn(MonsterDef def)
        {
            _state = State.EnemyTurn;

            // Decided BEFORE the telegraph (not after) so the DEFEND ghost can demonstrate the
            // upcoming move — Shield (single-leg hold) maps to an existing rehab balance pose;
            // Dodge (side-step) has no matching pose, so only the big arrow text carries it.
            bool useShield = _rng.NextDouble() < 0.5;
            int requiredLane = useShield ? 0 : (_rng.NextDouble() < 0.5 ? -1 : 1);

            if (telegraphPanel != null) telegraphPanel.SetActive(true);
            if (telegraphText != null) telegraphText.text = "เตรียมตัว!";
            _currentMonsterBlob?.SetTelegraph(true);
            Speak("เตรียมตัว!");

            if (useShield && _poseGhost != null)
            {
                int shieldGhostIdx = BattleGhostPoses.FindPoseIndex(rehabPoseData,
                    BattleGhostPoses.ShieldExercise, BattleGhostPoses.ShieldCheckpoint);
                if (shieldGhostIdx >= 0)
                {
                    _poseGhost.ShowPoseIndex(shieldGhostIdx);
                    _poseGhost.FadeTo(1f);
                }
            }

            yield return new WaitForSeconds(def.telegraphSeconds);
            _currentMonsterBlob?.SetTelegraph(false);
            if (telegraphPanel != null) telegraphPanel.SetActive(false);
            _poseGhost?.FadeTo(0f);

            if (defendPanel != null) defendPanel.SetActive(true);
            if (defendText != null)
                defendText.text = useShield ? "ยืนขาเดียว!" : (requiredLane < 0 ? "ก้าวไปทางซ้าย!" : "ก้าวไปทางขวา!");
            Speak(useShield ? "ยืนขาเดียวป้องกันเลย!" : (requiredLane < 0 ? "ก้าวไปทางซ้าย!" : "ก้าวไปทางขวา!"));

            bool success = false;
            float t = 0f;
            while (t < enemyDefendWindowSeconds)
            {
                float dt = Time.deltaTime;
                t += dt;
                TickDetectors(dt);
                if (defendTimerFill != null) defendTimerFill.fillAmount = 1f - t / enemyDefendWindowSeconds;

                bool holding = useKeyboardStub ? _stub.IsHolding : _singleLeg.IsHolding;
                int lane = useKeyboardStub ? _stub.Lane : _lane.Lane;
                bool ok = useShield ? BattleLogic.ShieldSuccess(holding) : BattleLogic.DodgeSuccess(lane, requiredLane);
                if (ok) { success = true; break; }
                yield return null;
            }

            if (defendPanel != null) defendPanel.SetActive(false);
            _hearts = BattleLogic.ApplyDefend(_hearts, success);
            UpdateHeartsUi();

            if (success)
            {
                Sfx.Play(useShield ? "shield" : "dodge");
                Speak(useShield ? "ป้องกันสำเร็จ!" : "หลบสำเร็จ!");
            }
            else
            {
                Sfx.Play("whoosh");
                Speak("โดนโจมตี!");
                screenGlow?.Pulse(GlowGentleRed, 0.3f); // gentle, never scary — no shake/flash
            }
        }

        IEnumerator GentleRetryRoutine()
        {
            Sfx.Play("defeat");
            if (telegraphPanel != null)
            {
                telegraphPanel.SetActive(true);
                if (telegraphText != null) telegraphText.text = "ไม่เป็นไร ลองใหม่นะ";
            }
            Speak("ไม่เป็นไร ลองใหม่นะ");
            yield return new WaitForSeconds(gentleRetryPauseSeconds);
            if (telegraphPanel != null) telegraphPanel.SetActive(false);

            _hearts = BattleLogic.MaxHearts;
            _currentMonsterHp = _currentMonsterMaxHp;
            UpdateHeartsUi();
            UpdateMonsterHpUi();
        }

        IEnumerator MonsterDefeatRoutine(MonsterDef def)
        {
            _state = State.MonsterDefeat;
            int bonus = def.isBoss ? BattleLogic.BossDefeatCoins : BattleLogic.MonsterDefeatCoins;
            _coins += bonus;
            Sfx.Play("coin");
            Speak(def.isBoss ? "ชนะราชันหนามทองแล้ว!" : "ชนะแล้ว!");

            bool done = false;
            if (_currentMonsterBlob != null) _currentMonsterBlob.PlayDefeat(() => done = true);
            else done = true;
            while (!done) yield return null;
            yield return new WaitForSeconds(0.6f);
        }

        IEnumerator ShowResultsRoutine()
        {
            _state = State.Results;
            ShowOnly(resultsPanel);
            Music.Stop();
            Sfx.Play("fanfare");

            float avgQuality = _qualitySamples > 0 ? (_qualitySum / _qualitySamples) * 100f : 0f;
            int stars = BattleLogic.Stars(avgQuality);

            var result = new BattleResult
            {
                level = level,
                monstersDefeated = _monstersDefeated,
                coins = _coins,
                totalDamageDealt = _totalDamage,
                bestCombo = _bestCombo,
                avgQualityPercent = avgQuality,
                stars = stars,
                durationSeconds = Time.time - _startTime,
            };

            if (resultsStatsText != null)
                resultsStatsText.text =
                    $"ปราบมอนสเตอร์ {result.monstersDefeated} ตัว\n" +
                    $"คอมโบสูงสุด x{result.bestCombo}\n" +
                    $"ดาเมจรวม {result.totalDamageDealt}\n" +
                    $"เหรียญ {result.coins}\n" +
                    $"ความแม่นยำเฉลี่ย {Mathf.RoundToInt(result.avgQualityPercent)}%";

            if (resultsStarImages != null)
                for (int i = 0; i < resultsStarImages.Length; i++)
                    if (resultsStarImages[i] != null)
                        resultsStarImages[i].color = i < stars ? new Color(1f, 0.84f, 0.2f) : new Color(1f, 1f, 1f, 0.22f);

            Speak("เก่งมาก ชนะแล้ว");
            yield return null; // keep this an iterator block even though nothing else here suspends
            OnSessionComplete?.Invoke(result);
        }

        // ---- Detector plumbing (BalanceQuestDirector pattern: ticked explicitly inside the
        // active window's own coroutine loop, not globally in Update). ----
        void TickDetectors(float dt)
        {
            if (useKeyboardStub)
            {
                _stub.Tick(dt);
                if (_stub.JustKickedLeft || _stub.JustKickedRight) _fireKickTotal++;
                if (_stub.JustKickedBackLeft || _stub.JustKickedBackRight) _tailWhipTotal++;
                if (_stub.IsRaised) _tiptoeHoldTotal += dt;
                return;
            }

            var kp = poseDetector != null ? poseDetector.LatestKeypoints : null;
            var conf = poseDetector != null ? poseDetector.LatestConfidence : null;
            bool hasPose = poseDetector != null && poseDetector.HasPose;
            _gate.Tick(hasPose, conf, dt);

            if (kp != null && conf != null)
            {
                _lane.Tick(kp, conf, dt);
                _singleLeg.Tick(kp, conf, dt);
                _tiptoe.Tick(kp, conf, dt);
                _abduction.Tick(kp, conf, dt);
                _knee.Tick(kp, conf, dt);
                _sitStand.Tick(kp, conf, dt);
                _tandemTracker.Tick(kp, conf, dt);

                if (_abduction.JustKickedLeft || _abduction.JustKickedRight) _fireKickTotal++;
                if (_tiptoe.IsRaised) _tiptoeHoldTotal += dt;
            }

            var lm = poseDetector != null ? poseDetector.Landmarks33 : null;
            if (lm != null)
            {
                _hipExt.Tick(lm, dt);
                if (_hipExt.JustKickedBackLeft || _hipExt.JustKickedBackRight) _tailWhipTotal++;
            }
        }

        void ResetTurnBaselines()
        {
            _turnKneeBaseline = KneeCountNow();
            _turnFireKickBaseline = _fireKickTotal;
            _turnTailWhipBaseline = _tailWhipTotal;
            _turnTiptoeBaseline = _tiptoeHoldTotal;
            _turnTandemBaseline = TandemHeldNow();
        }

        float KneeCountNow() => useKeyboardStub ? _stub.AlternatingCount : _knee.AlternatingCount;
        float TandemHeldNow() => useKeyboardStub ? _stub.TandemHoldSeconds : _tandemTracker.HeldSeconds;
        bool JustStoodNow() => useKeyboardStub ? _stub.JustStood : _sitStand.JustStood;

        float CountFor(PoseSkillId id) => id switch
        {
            PoseSkillId.StompQuake => KneeCountNow() - _turnKneeBaseline,
            PoseSkillId.FireKick => _fireKickTotal - _turnFireKickBaseline,
            PoseSkillId.TailWhip => _tailWhipTotal - _turnTailWhipBaseline,
            _ => 0f,
        };

        float HoldSecondsFor(PoseSkillId id) => id switch
        {
            PoseSkillId.LightningCharge => _tiptoeHoldTotal - _turnTiptoeBaseline,
            PoseSkillId.FocusHeal => TandemHeldNow() - _turnTandemBaseline,
            _ => 0f,
        };

        // ---- Monster + FX plumbing ----
        void SpawnMonster(MonsterDef def)
        {
            if (_currentMonsterBlob != null) Destroy(_currentMonsterBlob.gameObject);
            var tint = def.shape switch
            {
                MonsterShape.RoundSlime => new Color(0.35f, 0.75f, 0.4f),
                MonsterShape.TallGhost => new Color(0.55f, 0.4f, 0.85f),
                MonsterShape.SpikyBoss => new Color(0.85f, 0.25f, 0.25f),
                _ => Color.white,
            };
            var parent = monsterSlot != null ? monsterSlot : transform;
            _currentMonsterBlob = MonsterBlob.Spawn(parent, def.shape, tint);
            if (monsterNameText != null) monsterNameText.text = def.nameThai;
        }

        void PlayHitFx(Quality tier, int dmg)
        {
            Sfx.Play(tier switch { Quality.Perfect => "hit_3", Quality.Good => "hit_2", _ => "hit_1" });
            _currentMonsterBlob?.HitFlash(tier);
            if (_currentMonsterBlob != null)
                DamageNumberFx.Spawn(_currentMonsterBlob.transform.position + Vector3.up * 0.8f, dmg, tier, worldCamera);
        }

        static Color SkillColor(PoseSkillId id) => id switch
        {
            PoseSkillId.EarthSlam => new Color(0.55f, 0.4f, 0.2f),
            PoseSkillId.StompQuake => new Color(0.6f, 0.45f, 0.2f),
            PoseSkillId.FireKick => new Color(0.85f, 0.35f, 0.15f),
            PoseSkillId.TailWhip => new Color(0.7f, 0.2f, 0.35f),
            PoseSkillId.LightningCharge => new Color(0.9f, 0.8f, 0.2f),
            PoseSkillId.FocusHeal => new Color(0.3f, 0.8f, 0.55f),
            _ => Color.white,
        };

        void UpdateHeartsUi()
        {
            if (heartImages == null) return;
            for (int i = 0; i < heartImages.Length; i++)
                if (heartImages[i] != null)
                    heartImages[i].color = i < _hearts ? new Color(0.93f, 0.26f, 0.34f) : new Color(1f, 1f, 1f, 0.2f);
        }

        void UpdateMonsterHpUi()
        {
            if (monsterHpFill != null)
                monsterHpFill.fillAmount = _currentMonsterMaxHp > 0 ? (float)_currentMonsterHp / _currentMonsterMaxHp : 0f;
        }

        void UpdateComboUi(Quality tier)
        {
            if (comboText == null) return;
            string label = _comboStreak >= BattleLogic.ComboStreakForBonus
                ? $"คอมโบ x{_comboStreak} (x1.5!)"
                : tier switch { Quality.Perfect => "เพอร์เฟกต์!", Quality.Good => "ดีมาก!", _ => "โอเค" };
            comboText.text = label;
            SimpleTween.ScalePop(comboText.transform, this, 1.2f, 0.25f);
        }

        void Speak(string line)
        {
            if (voice == null) return;
            Music.Duck(2.5f);
            voice.Speak(line);
        }

        void ShowOnly(GameObject panel)
        {
            GameObject[] all = { introPanel, calibPanel, countdownPanel, hudPanel, resultsPanel };
            foreach (var p in all)
                if (p != null) p.SetActive(p == panel);
            // hudPanel hosts the card/telegraph/defend sub-panels — those toggle independently.
        }

        /// <summary>
        /// Local "feet together, hold still" tracker for Focus Heal — deliberately NOT a shared
        /// Motion detector, mirroring Kinex.BalanceQuest.TandemStandBeatRunner's own comment on
        /// why this stays local rather than adding another shared detector. HeldSeconds decays
        /// (doesn't hard-reset) when the pose is lost, so a brief camera glitch doesn't wipe it.
        /// </summary>
        class TandemHoldTracker
        {
            const float WobbleWindowSeconds = 1f;
            const float WobbleNormalizer = 0.08f;
            const float HipHeightTolerance = 0.15f;
            const int BufferCapacity = 128;

            struct Sample { public float t; public float x; }
            readonly Sample[] _buffer = new Sample[BufferCapacity];
            int _head, _count;
            float _clock;
            float _hipY0;
            bool _hasBaseline;

            public float HeldSeconds { get; private set; }

            public void Tick(Vector2[] kp, float[] conf, float dt)
            {
                _clock += dt;
                bool visible = kp != null && conf != null && MotionMath.Valid(conf, 0.3f, MotionMath.LHip, MotionMath.RHip);
                bool heightOk = true;
                float wobble01 = 0f;

                if (visible)
                {
                    float torso = Mathf.Max(MotionMath.TorsoLen(kp), 0.02f);
                    float hipY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
                    if (!_hasBaseline) { _hipY0 = hipY; _hasBaseline = true; }
                    heightOk = Mathf.Abs(hipY - _hipY0) < HipHeightTolerance * torso;

                    float hipX = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).x;
                    _buffer[_head] = new Sample { t = _clock, x = hipX };
                    _head = (_head + 1) % BufferCapacity;
                    if (_count < BufferCapacity) _count++;
                    wobble01 = ComputeWobble(torso);
                }

                bool held = visible && heightOk && wobble01 < 0.5f;
                HeldSeconds = held ? HeldSeconds + dt : Mathf.Max(0f, HeldSeconds - dt * 2f);
            }

            float ComputeWobble(float torso0)
            {
                float cutoff = _clock - WobbleWindowSeconds;
                float sum = 0f, sumSq = 0f; int n = 0;
                for (int i = 0; i < _count; i++)
                {
                    int idx = (_head - 1 - i + BufferCapacity) % BufferCapacity;
                    if (_buffer[idx].t < cutoff) break;
                    sum += _buffer[idx].x; sumSq += _buffer[idx].x * _buffer[idx].x; n++;
                }
                if (n < 2) return 0f;
                float mean = sum / n;
                float variance = Mathf.Max(0f, sumSq / n - mean * mean);
                float stddev = Mathf.Sqrt(variance);
                return Mathf.Clamp01(stddev / (WobbleNormalizer * torso0));
            }
        }
    }
}
