using System;
using UnityEngine;

namespace Kinex.TheDasher
{
    public enum DasherKind { Meteor, Treasure, KickRing, Rest }

    /// <summary>
    /// Result payload for one 3-minute TheDasher run. Serializable so the bridge can
    /// JsonUtility it straight onto the wire.
    /// </summary>
    [Serializable]
    public class DasherResult
    {
        public int score;          // raw tally — may be negative; UI clamps via DisplayScore
        public int treasures;      // treasures collected (sit→stand in lane)
        public int kicks;          // kick rings hit from an adjacent lane
        public int dodges;         // meteors that landed in an empty lane
        public int meteorHits;     // meteors that caught the player
        public int kickFouls;      // stood in a ring's lane at arrival
        public int sitStands;      // total stand reps during play (rehab dose)
        public int laneSteps;      // side-step lane changes (rehab dose)
        public int kickReps;       // total side-kick motions during play (rehab dose)
        public int stars;          // 0-3
        public float durationSeconds;
    }

    /// <summary>
    /// Pure rules for TheDasher: the beat deck, the kick-adjacency rule, scoring and
    /// stars. No UnityEngine scene types — fully unit-testable in the editor self-test.
    ///
    /// Lane convention everywhere in this game: -1 = left, 0 = middle, +1 = right,
    /// as seen ON SCREEN (behind-the-avatar view). The director converts the raw
    /// image-space LaneDetector sign into this convention.
    /// </summary>
    public static class DasherLogic
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
            public DasherKind kind;
            public int lane; // -1 / 0 / +1 (screen space)
        }

        /// <summary>
        /// Deterministic session deck (seeded — the self-test replays exact decks).
        /// Rules: opens with a treasure (teach the friendliest mechanic first), NO two
        /// adjacent beats of the same kind (a true shuffle of 8/8/7/5 routinely produced
        /// three of a kind in a row, which players read as "the same pose over and over"),
        /// lanes vary (no 3 identical lanes in a row), kick rings use all three lanes.
        /// </summary>
        public static Beat[] BuildDeck(int seed)
        {
            var rng = new System.Random(seed);

            // Deal slot by slot instead of shuffling-and-rejecting: at each beat, pick among the
            // kinds that still have dose left AND differ from the previous beat, weighted by how
            // much dose each has left. Weighting keeps the heavier kinds spread across the whole
            // session (a plain uniform pick would exhaust the light kinds early and leave a tail of
            // the heavy one), and the "differ from previous" filter makes the even spread structural
            // rather than something we rejection-sample for.
            var remain = new int[4]; // indexed by (int)DasherKind
            remain[(int)DasherKind.Meteor] = MeteorCount;
            remain[(int)DasherKind.Treasure] = TreasureCount;
            remain[(int)DasherKind.KickRing] = KickCount;
            remain[(int)DasherKind.Rest] = RestCount;

            var kinds = new DasherKind[BeatCount];
            kinds[0] = DasherKind.Treasure; // beat 0 is always a treasure
            remain[(int)DasherKind.Treasure]--;
            int prev = (int)DasherKind.Treasure;

            for (int i = 1; i < BeatCount; i++)
            {
                int slotsAfter = BeatCount - i - 1;

                // Only consider picks that still leave the REST of the deck arrangeable — otherwise
                // naive no-repeat dealing paints itself into a corner (e.g. 3 treasures and nothing
                // else left, forcing T,T,T at the end).
                int totalWeight = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (k == prev || remain[k] == 0) continue;
                    remain[k]--;
                    bool ok = Arrangeable(remain, slotsAfter, k);
                    remain[k]++;
                    if (ok) totalWeight += remain[k];
                }

                int pick = -1;
                if (totalWeight > 0)
                {
                    int roll = rng.Next(totalWeight);
                    for (int k = 0; k < 4; k++)
                    {
                        if (k == prev || remain[k] == 0) continue;
                        remain[k]--;
                        bool ok = Arrangeable(remain, slotsAfter, k);
                        remain[k]++;
                        if (!ok) continue;
                        roll -= remain[k];
                        if (roll < 0) { pick = k; break; }
                    }
                }

                // Endgame fallbacks. Unreachable with the shipping doses (Arrangeable guarantees a
                // legal pick exists), but if the counts are ever retuned into an impossible shape we
                // keep the EXACT dose counts and give up the no-repeat rule rather than loop forever.
                if (pick < 0)
                    for (int k = 0; k < 4 && pick < 0; k++) if (k != prev && remain[k] > 0) pick = k;
                if (pick < 0)
                    for (int k = 0; k < 4 && pick < 0; k++) if (remain[k] > 0) pick = k;

                kinds[i] = (DasherKind)pick;
                remain[pick]--;
                prev = pick;
            }

            // Assign lanes: avoid 3 repeats in a row so the player keeps stepping.
            var deck = new Beat[BeatCount];
            int prevLane = int.MinValue, prevPrevLane = int.MinValue;
            for (int i = 0; i < BeatCount; i++)
            {
                int lane = 0;
                if (kinds[i] == DasherKind.Treasure)
                {
                    // Treasures only land center: the player sits in a chair fixed at the middle lane.
                    lane = 0;
                }
                else if (kinds[i] != DasherKind.Rest)
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

        /// <summary>
        /// Can the remaining dose in <paramref name="remain"/> still fill <paramref name="slots"/>
        /// beats with no two adjacent alike, given the beat just placed was <paramref name="prev"/>?
        /// A kind can only occupy every other slot, so it fits iff its count is within
        /// ceil(slots/2) — or floor(slots/2) for `prev`, which cannot take the very next slot.
        /// </summary>
        static bool Arrangeable(int[] remain, int slots, int prev)
        {
            for (int k = 0; k < remain.Length; k++)
            {
                int cap = k == prev ? slots / 2 : (slots + 1) / 2;
                if (remain[k] > cap) return false;
            }
            return true;
        }
    }
}
