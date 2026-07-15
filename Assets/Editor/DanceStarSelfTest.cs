#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Kinex.Motion;

namespace Kinex.DanceStar
{
    /// <summary>
    /// Editor/batchmode self-test for SUPERSTAR STAGE. Two halves:
    ///  1. DanceScoring PURE tests — card ratings (speed + stability), chair-rep ratings, streak
    ///     multiplier tiers, per-card score, hearts rules (MISS −1 / PERFECT +1 / floor 0 / cap 5),
    ///     stars incl. the zero-hearts 2-star cap, coins, accuracy, and DanceTandemLogic.
    ///  2. Setlist integrity — card count, per-section composition, every pose name on the frozen
    ///     contract list, chair-verse rep targets, Thai display names present.
    /// Prints one PASS/FAIL line per check; any failure also logs the "SELFTEST FAIL" signal line
    /// and (in batch mode) exits 1.
    /// Run: -executeMethod Kinex.DanceStar.DanceStarSelfTest.Run
    /// </summary>
    public static class DanceStarSelfTest
    {
        static int s_Passed, s_Failed;

        [MenuItem("Kinex/Run Dance Star Self-Test")]
        public static void Run()
        {
            s_Passed = 0; s_Failed = 0;

            TestRateCard();
            TestRateChairRep();
            TestStreakMultiplier();
            TestCardScore();
            TestHearts();
            TestStars();
            TestCoinsAndAccuracy();
            TestThaiLabels();
            TestTandemLogic();
            TestSetlistComposition();
            TestSetlistPoseNamesFrozen();
            TestChairVerse();
            TestResultMessage();

            if (s_Failed == 0)
            {
                Debug.Log($"[DanceStarSelfTest] ALL PASS ({s_Passed} checks)");
            }
            else
            {
                Debug.LogError($"SELFTEST FAIL — [DanceStarSelfTest] {s_Failed} FAILED / {s_Passed} passed");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void Check(bool ok, string name, string detail = "")
        {
            if (ok) { s_Passed++; Debug.Log($"[DanceStarSelfTest] PASS: {name}"); }
            else { s_Failed++; Debug.LogError($"[DanceStarSelfTest] FAIL: {name} {detail}"); }
        }

        static bool Near(float a, float b, float eps = 0.001f) => Mathf.Abs(a - b) < eps;

        // ---- 1. DanceScoring pure tests --------------------------------------

        static void TestRateCard()
        {
            // Fast AND steady = PERFECT (combined = 0.5*speed + 0.5*stability >= 0.75).
            Check(DanceScoring.RateCard(true, 0f, 8f, 1f) == DanceScoring.Rating.Perfect,
                  "RateCard: instant steady match = PERFECT");
            // Slow but steady = GOOD (speed 0, stability 1 -> 0.5).
            Check(DanceScoring.RateCard(true, 8f, 8f, 1f) == DanceScoring.Rating.Good,
                  "RateCard: slow steady match = GOOD");
            // Slow AND shaky = OK (never harsh — matched at all is at least OK).
            Check(DanceScoring.RateCard(true, 8f, 8f, 0.4f) == DanceScoring.Rating.Ok,
                  "RateCard: slow shaky match = OK");
            Check(DanceScoring.RateCard(false, 8f, 8f, 0f) == DanceScoring.Rating.Miss,
                  "RateCard: no match = MISS");
            // Degenerate window doesn't divide by zero.
            var r = DanceScoring.RateCard(true, 0f, 0f, 1f);
            Check(r != DanceScoring.Rating.Miss, "RateCard: zero window degrades safely", r.ToString());
        }

        static void TestRateChairRep()
        {
            Check(DanceScoring.RateChairRep(5, 5, 15f, 45f) == DanceScoring.Rating.Perfect,
                  "ChairRep: full reps at good pace = PERFECT");
            Check(DanceScoring.RateChairRep(5, 5, 44f, 45f) == DanceScoring.Rating.Good,
                  "ChairRep: full reps at slow pace = GOOD");
            Check(DanceScoring.RateChairRep(3, 5, 45f, 45f) == DanceScoring.Rating.Ok,
                  "ChairRep: partial reps = OK");
            Check(DanceScoring.RateChairRep(1, 5, 45f, 45f) == DanceScoring.Rating.Miss,
                  "ChairRep: under a third of target = MISS");
            Check(DanceScoring.RateChairRep(2, 6, 45f, 45f) == DanceScoring.Rating.Ok,
                  "ChairRep: exactly a third counts as OK (boundary)");
            Check(DanceScoring.RateChairRep(5, 0, 10f, 45f) == DanceScoring.Rating.Miss,
                  "ChairRep: zero target degrades safely");
        }

        static void TestStreakMultiplier()
        {
            Check(Near(DanceScoring.StreakMultiplier(0), 1f) && Near(DanceScoring.StreakMultiplier(2), 1f),
                  "Streak: x1 below 3");
            Check(Near(DanceScoring.StreakMultiplier(3), 1.2f) && Near(DanceScoring.StreakMultiplier(4), 1.2f),
                  "Streak: x1.2 at 3-4");
            Check(Near(DanceScoring.StreakMultiplier(5), 1.5f) && Near(DanceScoring.StreakMultiplier(9), 1.5f),
                  "Streak: x1.5 at 5-9");
            Check(Near(DanceScoring.StreakMultiplier(10), 2f) && Near(DanceScoring.StreakMultiplier(25), 2f),
                  "Streak: x2 at 10+");
        }

        static void TestCardScore()
        {
            Check(DanceScoring.CardScore(DanceScoring.Rating.Perfect, 0) == 100,
                  "CardScore: PERFECT with no streak = 100");
            Check(DanceScoring.CardScore(DanceScoring.Rating.Perfect, 10) == 200,
                  "CardScore: PERFECT at x2 streak = 200");
            Check(DanceScoring.CardScore(DanceScoring.Rating.Good, 3) == 84,
                  "CardScore: GOOD at x1.2 = 84");
            Check(DanceScoring.CardScore(DanceScoring.Rating.Ok, 5) == 60,
                  "CardScore: OK at x1.5 = 60");
            Check(DanceScoring.CardScore(DanceScoring.Rating.Miss, 10) == 0,
                  "CardScore: MISS is always 0");
        }

        static void TestHearts()
        {
            Check(DanceScoring.ApplyRatingToHearts(5, DanceScoring.Rating.Miss) == 4,
                  "Hearts: MISS costs 1");
            Check(DanceScoring.ApplyRatingToHearts(0, DanceScoring.Rating.Miss) == 0,
                  "Hearts: floor at 0 (never negative, never a fail state)");
            Check(DanceScoring.ApplyRatingToHearts(3, DanceScoring.Rating.Perfect) == 4,
                  "Hearts: PERFECT regenerates 1");
            Check(DanceScoring.ApplyRatingToHearts(5, DanceScoring.Rating.Perfect) == 5,
                  "Hearts: cap at 5");
            Check(DanceScoring.ApplyRatingToHearts(3, DanceScoring.Rating.Good) == 3 &&
                  DanceScoring.ApplyRatingToHearts(3, DanceScoring.Rating.Ok) == 3,
                  "Hearts: GOOD/OK leave hearts unchanged");
        }

        static void TestStars()
        {
            const int n = 28;
            Check(DanceScoring.Stars(n * 100, n, false) == 3, "Stars: perfect session = 3");
            Check(DanceScoring.Stars((int)(n * 100 * 0.7f), n, false) == 2, "Stars: 70% = 2");
            Check(DanceScoring.Stars((int)(n * 100 * 0.3f), n, false) == 1, "Stars: 30% = 1");
            Check(DanceScoring.Stars(n * 100, n, true) == 2,
                  "Stars: zero-hearts moment caps a perfect run at 2");
            Check(DanceScoring.Stars((int)(n * 100 * 0.3f), n, true) == 1,
                  "Stars: cap never lifts a low score");
            Check(DanceScoring.Stars(0, 0, false) == 1, "Stars: degenerate card count still 1 (never 0)");
        }

        static void TestCoinsAndAccuracy()
        {
            Check(DanceScoring.Coins(2000, 3) == 130, "Coins: 2000pts + 3 stars = 130",
                  $"got {DanceScoring.Coins(2000, 3)}");
            Check(DanceScoring.AccuracyPct(21, 28) == 75, "Accuracy: 21/28 = 75%");
            Check(DanceScoring.AccuracyPct(0, 0) == 0, "Accuracy: degenerate = 0");
        }

        static void TestThaiLabels()
        {
            Check(DanceScoring.ThaiLabel(DanceScoring.Rating.Perfect) == "เป๊ะเวอร์!" &&
                  DanceScoring.ThaiLabel(DanceScoring.Rating.Good) == "ดีมาก!" &&
                  DanceScoring.ThaiLabel(DanceScoring.Rating.Ok) == "พอใช้" &&
                  DanceScoring.ThaiLabel(DanceScoring.Rating.Miss) == "พลาดจ้า",
                  "Labels: all four Thai rating strings (never harsh)");
        }

        // ---- DanceTandemLogic on synthetic keypoints (same fixture style as TempleHuntSelfTest) ----

        // Standing body in normalized image space (y grows DOWN): shoulders y=0.35, hips y=0.55,
        // torso = 0.2.
        static Vector2[] Body()
        {
            var kp = new Vector2[17];
            kp[MotionMath.LShoulder] = new Vector2(0.42f, 0.35f);
            kp[MotionMath.RShoulder] = new Vector2(0.58f, 0.35f);
            kp[MotionMath.LHip] = new Vector2(0.44f, 0.55f);
            kp[MotionMath.RHip] = new Vector2(0.56f, 0.55f);
            kp[MotionMath.LKnee] = new Vector2(0.44f, 0.75f);
            kp[MotionMath.RKnee] = new Vector2(0.56f, 0.75f);
            kp[MotionMath.LAnkle] = new Vector2(0.44f, 0.93f);
            kp[MotionMath.RAnkle] = new Vector2(0.56f, 0.93f);
            return kp;
        }

        static float[] Conf(float v = 0.9f)
        {
            var conf = new float[17];
            for (int i = 0; i < conf.Length; i++) conf[i] = v;
            return conf;
        }

        static void TestTandemLogic()
        {
            const float hipY0 = 0.55f;
            var conf = Conf();

            var tandem = Body(); // ankles drawn together: dx 0.02 < 0.35 * torso(0.2) = 0.07
            tandem[MotionMath.LAnkle] = new Vector2(0.49f, 0.93f);
            tandem[MotionMath.RAnkle] = new Vector2(0.51f, 0.93f);
            Check(DanceTandemLogic.IsTandemPose(tandem, conf, hipY0) == true,
                  "Tandem: ankles together at standing height passes");

            Check(DanceTandemLogic.IsTandemPose(Body(), conf, hipY0) == false,
                  "Tandem: normal shoulder-width stance fails");

            var crouched = (Vector2[])tandem.Clone(); // hips dropped well past the height tolerance
            crouched[MotionMath.LHip].y += 0.08f;
            crouched[MotionMath.RHip].y += 0.08f;
            Check(DanceTandemLogic.IsTandemPose(crouched, conf, hipY0) == false,
                  "Tandem: crouching/stepping (hip height off baseline) fails");

            var lowConf = Conf();
            lowConf[MotionMath.LAnkle] = 0.1f;
            Check(DanceTandemLogic.IsTandemPose(tandem, lowConf, hipY0) == null,
                  "Tandem: low ankle confidence returns null (hold last state)");

            // Hold-with-decay: builds 1x, drains 2x, clamps to [0, target].
            float held = 0f;
            for (int i = 0; i < 300; i++) held = DanceTandemLogic.TickHold(held, true, 1f / 60f, 8f);
            Check(Near(held, 5f, 0.01f), "Tandem hold: 5s of ok-ticks = 5s", $"got {held}");
            Check(Near(DanceTandemLogic.TickHold(8f, true, 1f, 8f), 8f), "Tandem hold: capped at target");
            Check(Near(DanceTandemLogic.TickHold(5f, false, 0.5f, 8f), 4f), "Tandem hold: decays at 2x while lost");
            Check(Near(DanceTandemLogic.TickHold(0.1f, false, 1f, 8f), 0f), "Tandem hold: floors at zero");
        }

        // ---- 2. Setlist integrity --------------------------------------------

        static void TestSetlistComposition()
        {
            var cards = DanceSetlist.BuildSetlist();
            Check(cards.Count == 28, "Setlist: 28 cards total", $"got {cards.Count}");

            int Count(DanceSection s) { int c = 0; foreach (var card in cards) if (card.section == s) c++; return c; }
            Check(Count(DanceSection.Warmup) == 2, "Setlist: warm-up = 2 cards", $"got {Count(DanceSection.Warmup)}");
            Check(Count(DanceSection.Verse1) == 6, "Setlist: verse 1 = 6 cards", $"got {Count(DanceSection.Verse1)}");
            Check(Count(DanceSection.Chorus1) == 6, "Setlist: chorus 1 = 6 cards", $"got {Count(DanceSection.Chorus1)}");
            Check(Count(DanceSection.ChairVerse) == 2, "Setlist: chair verse = 2 rep cards", $"got {Count(DanceSection.ChairVerse)}");
            Check(Count(DanceSection.Verse2) == 8, "Setlist: verse 2 = 8 cards", $"got {Count(DanceSection.Verse2)}");
            Check(Count(DanceSection.Finale) == 4, "Setlist: finale = 4 cards", $"got {Count(DanceSection.Finale)}");

            // Sections arrive in setlist order (the director fires OnSectionChanged on transitions).
            bool ordered = true;
            for (int i = 1; i < cards.Count; i++)
                if ((int)cards[i].section < (int)cards[i - 1].section) ordered = false;
            Check(ordered, "Setlist: sections are in song order");

            bool allHaveThai = true, allHaveWindow = true;
            foreach (var c in cards)
            {
                if (string.IsNullOrEmpty(c.displayNameThai)) allHaveThai = false;
                if (c.windowSeconds <= 0f) allHaveWindow = false;
            }
            Check(allHaveThai, "Setlist: every card has a Thai display name");
            Check(allHaveWindow, "Setlist: every card has a positive window");

            // Every side-step card is beat-timed; every single-leg card carries an arm variant.
            bool stepsAreBeat = true, slHaveArms = true;
            foreach (var c in cards)
            {
                if (c.detector == DanceDetectorKind.SideStep && c.cardType != DanceCardType.Beat) stepsAreBeat = false;
                if (c.detector == DanceDetectorKind.SingleLeg && c.armVariant == DanceArmVariant.None) slHaveArms = false;
            }
            Check(stepsAreBeat, "Setlist: side-step cards are Beat type");
            Check(slHaveArms, "Setlist: single-leg cards all specify an arm variant");
        }

        static void TestSetlistPoseNamesFrozen()
        {
            var frozen = new HashSet<string>(DanceSetlist.FrozenPoseNames);
            var cards = DanceSetlist.BuildSetlist();
            bool allFrozen = true;
            foreach (var c in cards)
                if (!frozen.Contains(c.poseAssetName))
                {
                    allFrozen = false;
                    Debug.LogError($"[DanceStarSelfTest] card pose '{c.poseAssetName}' not on the frozen list");
                }
            Check(allFrozen, "Setlist: every card pose name is on the frozen contract list");
            Check(DanceSetlist.FrozenPoseNames.Length == 22, "Setlist: frozen contract has 22 names",
                  $"got {DanceSetlist.FrozenPoseNames.Length}");
        }

        static void TestChairVerse()
        {
            var cards = DanceSetlist.BuildSetlist();
            DanceCard stands = null, raises = null;
            foreach (var c in cards)
            {
                if (c.section != DanceSection.ChairVerse) continue;
                if (c.detector == DanceDetectorKind.ChairStand) stands = c;
                if (c.detector == DanceDetectorKind.SeatedKneeRaise) raises = c;
            }
            Check(stands != null && stands.cardType == DanceCardType.ChairRep && stands.targetReps == 5,
                  "ChairVerse: chair-stand rep card targets 5 stands");
            Check(raises != null && raises.cardType == DanceCardType.ChairRep && raises.targetReps == 6,
                  "ChairVerse: seated knee-raise rep card targets 6 alternating raises");
            Check(stands != null && cards.IndexOf(stands) < cards.IndexOf(raises),
                  "ChairVerse: stands come before seated raises (sit down once, stay seated)");
        }

        static void TestResultMessage()
        {
            var msg = DanceStarResultBridge.BuildMessage(new DanceStarResult
            {
                cardsCompleted = 26, cardCount = 28, totalScore = 2450, maxStreak = 12,
                accuracyPct = 93, stars = 3, coins = 152, durationSeconds = 270f,
            });
            Check(msg.StartsWith("{\"type\":\"dancestar_result\","), "Bridge: starts with type tag", msg);
            Check(msg.Contains("\"cardsCompleted\":26") && msg.Contains("\"cardCount\":28") &&
                  msg.Contains("\"totalScore\":2450") && msg.Contains("\"maxStreak\":12") &&
                  msg.Contains("\"accuracyPct\":93") && msg.Contains("\"stars\":3") &&
                  msg.Contains("\"coins\":152"),
                  "Bridge: carries every frozen field", msg);
            Check(msg.EndsWith("}"), "Bridge: well-formed JSON tail", msg);
        }
    }
}
#endif
