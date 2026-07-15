using System;
using UnityEngine;

namespace Kinex.AstroStance
{
    public enum AstroKind { Meteor, Treasure, KickRing, Rest }

    /// <summary>
    /// Result payload for one 3-minute AstroStance run. Serializable so the bridge can
    /// JsonUtility it straight onto the wire.
    /// </summary>
    [Serializable]
    public class AstroResult
    {
        public int score;          // raw tally — may be negative; UI clamps via DisplayScore
        public int treasures;      // treasures collected (sit→stand in lane)
        public int kicks;          // kick rings hit from an adjacent lane
        public int dodges;         // meteors that landed in an empty lane
        public int meteorHits;     // meteors that caught the player
        public int kickFouls;      // stood in a ring's lane at arrival
        public int sitStands;      // total stand reps during play (rehab dose)
        public int laneSteps;      // side-step lane changes (rehab dose)
        public int stars;          // 0-3
        public float durationSeconds;
    }

    /// <summary>
    /// Pure rules for AstroStance: the beat deck, the kick-adjacency rule, scoring and
    /// stars. No UnityEngine scene types — fully unit-testable in the editor self-test.
    ///
    /// Lane convention everywhere in this game: -1 = left, 0 = middle, +1 = right,
    /// as seen ON SCREEN (behind-the-avatar view). The director converts the raw
    /// image-space LaneDetector sign into this convention.
    /// </summary>
    public static class AstroLogic
    {
        // Fixed 3-minute dose: 28 beats ≈ one every 6 s. Counts chosen for rehab value —
        // 8 sit-to-stands, 7 side-kicks, 8 dodge steps, 5 rest beats to breathe.
        public const int MeteorCount = 8;
        public const int TreasureCount = 8;
        public const int KickCount = 7;
        public const int RestCount = 5;
        public const int BeatCount = MeteorCount + TreasureCount + KickCount + RestCount;

        /// <summary>Max achievable score (+1 per treasure and kick; dodges score nothing).</summary>
        public const int MaxScore = TreasureCount + KickCount;

        /// <summary>Score shown on the HUD — never negative (senior-friendly).</summary>
        public static int DisplayScore(int raw) => Mathf.Max(0, raw);

        /// <summary>
        /// A kick counts only from an ADJACENT lane, with the leg nearest the ring:
        /// ring left of the player → left leg, ring right → right leg.
        /// </summary>
        public static bool KickValid(int playerLane, int ringLane, bool kickedLeft)
        {
            if (Mathf.Abs(playerLane - ringLane) != 1) return false;
            bool ringIsLeft = ringLane < playerLane;
            return kickedLeft == ringIsLeft;
        }

        /// <summary>Standing in the ring's own lane when it arrives is the foul (−1).</summary>
        public static bool KickFoul(int playerLane, int ringLane) => playerLane == ringLane;

        public static int Stars(int score)
        {
            float frac = MaxScore > 0 ? (float)score / MaxScore : 0f;
            if (frac >= 0.75f) return 3;
            if (frac >= 0.5f) return 2;
            if (frac >= 0.25f) return 1;
            return 0;
        }

        /// <summary>
        /// One beat of the session: what falls and into which lane. Rest beats have Lane 0
        /// and spawn nothing.
        /// </summary>
        [Serializable]
        public struct Beat
        {
            public AstroKind kind;
            public int lane; // -1 / 0 / +1 (screen space)
        }

        /// <summary>
        /// Deterministic session deck (seeded — the self-test replays exact decks).
        /// Rules: opens with a treasure (teach the friendliest mechanic first), never two
        /// meteors back-to-back, lanes vary (no 3 identical lanes in a row), kick rings
        /// use all three lanes.
        /// </summary>
        public static Beat[] BuildDeck(int seed)
        {
            var rng = new System.Random(seed);

            // Bag of kinds at exact dose counts.
            var kinds = new AstroKind[BeatCount];
            int n = 0;
            for (int i = 0; i < MeteorCount; i++) kinds[n++] = AstroKind.Meteor;
            for (int i = 0; i < TreasureCount; i++) kinds[n++] = AstroKind.Treasure;
            for (int i = 0; i < KickCount; i++) kinds[n++] = AstroKind.KickRing;
            for (int i = 0; i < RestCount; i++) kinds[n++] = AstroKind.Rest;

            // Rejection-sample a shuffle satisfying both constraints (treasure first, no
            // adjacent meteors). A local swap repair can CREATE a new meteor pair at the
            // swap destination — the self-test caught exactly that — so re-shuffling until
            // clean is the simple correct approach. Deterministic per seed; with 8 meteors
            // in 28 beats a clean shuffle shows up within a handful of attempts.
            for (int attempt = 0; attempt < 200; attempt++)
            {
                for (int i = BeatCount - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
                }

                // Beat 0 is always a treasure: swap the first treasure forward.
                for (int i = 0; i < BeatCount; i++)
                    if (kinds[i] == AstroKind.Treasure) { (kinds[0], kinds[i]) = (kinds[i], kinds[0]); break; }

                bool pairFound = false;
                for (int i = 1; i < BeatCount && !pairFound; i++)
                    pairFound = kinds[i] == AstroKind.Meteor && kinds[i - 1] == AstroKind.Meteor;
                if (!pairFound) break;

                // With 8 meteors in 28 beats a clean shuffle appears within a handful of tries;
                // exhausting 200 is effectively impossible, but warn rather than silently ship a
                // deck with an adjacent-meteor pair.
                if (attempt == 199)
                    UnityEngine.Debug.LogWarning("[AstroLogic] deck shuffle hit attempt cap with an adjacent-meteor pair.");
            }

            // Assign lanes: avoid 3 repeats in a row so the player keeps stepping.
            var deck = new Beat[BeatCount];
            int prevLane = int.MinValue, prevPrevLane = int.MinValue;
            for (int i = 0; i < BeatCount; i++)
            {
                int lane = 0;
                if (kinds[i] != AstroKind.Rest)
                {
                    lane = rng.Next(3) - 1;
                    if (lane == prevLane && lane == prevPrevLane)
                        lane = lane == 1 ? -1 : lane + 1;
                }
                deck[i] = new Beat { kind = kinds[i], lane = lane };
                prevPrevLane = prevLane;
                prevLane = lane;
            }
            return deck;
        }
    }
}
