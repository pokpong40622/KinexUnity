using UnityEngine;

namespace Kinex.MirrorGame
{
    /// <summary>
    /// NEW detection core for the Magic Mirror game (deliberately independent from
    /// PoseScorer/MegaDance scoring — per project decision this game judges poses by
    /// joint-in-zone, not limb angles).
    ///
    /// A target pose is a set of world-space sphere zones, one per tracked joint,
    /// sampled from the outline ghost's bones. The player's avatar bone must sit inside
    /// each zone; when ALL zones are filled the player holds for <see cref="HoldSeconds"/>
    /// to pass. Pure static + plain structs: no MonoBehaviour, no scene deps, fully
    /// drivable from editor self-tests with synthetic positions.
    /// </summary>
    public static class ZoneMatcher
    {
        /// <summary>Tracked joints, index order shared by every array in this class.
        /// Mirrors HumanBodyBones names so the director can map 1:1.</summary>
        public enum Joint
        {
            LeftShoulder, RightShoulder,
            LeftElbow, RightElbow,
            LeftWrist, RightWrist,
            LeftKnee, RightKnee,
            LeftAnkle, RightAnkle,
        }

        public const int JointCount = 10;

        /// <summary>Base zone radius (metres, avatar space). Generous on purpose — senior
        /// players. Wrists/ankles travel furthest so they get a wider allowance.</summary>
        public static readonly float[] BaseRadius =
        {
            0.16f, 0.16f, // shoulders barely move — tight keeps torso honest
            0.20f, 0.20f, // elbows
            0.26f, 0.26f, // wrists
            0.20f, 0.20f, // knees
            0.24f, 0.24f, // ankles
        };

        public const float HoldSeconds = 2f;

        /// <summary>Assist: after this long on one pose without passing, zones grow.</summary>
        public const float AssistStuckSeconds = 30f;
        public const float AssistGrowFactor = 1.2f;
        public const float AssistMaxMultiplier = 2.0f; // ~4 growth steps, then hints only

        public static bool InZone(Vector3 target, Vector3 player, float radius)
            => (player - target).sqrMagnitude <= radius * radius;

        /// <summary>
        /// Checks every joint. <paramref name="inZone"/> (length <see cref="JointCount"/>)
        /// receives per-joint results; returns how many are in. A joint whose player
        /// position is invalid (caller passes <see cref="Invalid"/>) counts as out.
        /// </summary>
        public static int Evaluate(Vector3[] targets, Vector3[] players, float radiusMultiplier, bool[] inZone)
        {
            int count = 0;
            for (int i = 0; i < JointCount; i++)
            {
                bool ok = players[i].x != Invalid.x
                          && InZone(targets[i], players[i], BaseRadius[i] * radiusMultiplier);
                inZone[i] = ok;
                if (ok) count++;
            }
            return count;
        }

        /// <summary>Sentinel for "no tracking this frame" player joints.</summary>
        public static readonly Vector3 Invalid = new Vector3(float.NegativeInfinity, 0f, 0f);

        /// <summary>
        /// Per-pose progress: lock-in stopwatch, 2s hold fill, dropout counting and the
        /// assist growth timer. Value type so tests can copy states freely.
        /// </summary>
        public struct PoseProgress
        {
            public float ElapsedSeconds;    // total time on this pose
            public float HoldFill01;        // 0..1 ring fill while all-in
            public int Dropouts;            // times the hold broke after starting to fill
            public float RadiusMultiplier;  // assist growth, starts at 1
            public float StuckTimer;        // time since pose start or last assist step
            public bool AssistTriggered;    // true for one Tick after a growth step (director speaks a hint)
            public bool Passed;
            public float LockInSeconds;     // elapsed when the passing hold STARTED (score input)
            bool _holding;

            public static PoseProgress Start() => new PoseProgress { RadiusMultiplier = 1f };

            /// <summary>Advance one frame. <paramref name="allIn"/> = every zone filled this frame.</summary>
            public void Tick(bool allIn, float dt)
            {
                if (Passed) return;
                ElapsedSeconds += dt;
                AssistTriggered = false;

                if (allIn)
                {
                    if (!_holding)
                    {
                        _holding = true;
                        LockInSeconds = ElapsedSeconds;
                    }
                    HoldFill01 = Mathf.Min(1f, HoldFill01 + dt / HoldSeconds);
                    if (HoldFill01 >= 1f) { Passed = true; return; }
                }
                else
                {
                    if (_holding) { Dropouts++; _holding = false; }
                    // drain, don't snap — a one-frame tracking blip shouldn't erase the ring
                    HoldFill01 = Mathf.Max(0f, HoldFill01 - dt * 2f / HoldSeconds);

                    StuckTimer += dt;
                    if (StuckTimer >= AssistStuckSeconds && RadiusMultiplier < AssistMaxMultiplier)
                    {
                        RadiusMultiplier = Mathf.Min(AssistMaxMultiplier, RadiusMultiplier * AssistGrowFactor);
                        StuckTimer = 0f;
                        AssistTriggered = true;
                    }
                }
            }
        }

        // --- Scoring: speed + steadiness. Assist never reduces score (senior-first). ---

        public const int MaxPoseScore = 1000;

        /// <summary>Locking in within 8s = full speed bonus, fading to none at 45s. Each
        /// dropout costs 60 of the steadiness pool. Passing at all guarantees 400.</summary>
        public static int PoseScore(float lockInSeconds, int dropouts)
        {
            const int basePart = 400, speedPart = 350, steadyPart = 250;
            float speed01 = 1f - Mathf.Clamp01((lockInSeconds - 8f) / (45f - 8f));
            int steady = Mathf.Max(0, steadyPart - dropouts * 60);
            return basePart + Mathf.RoundToInt(speedPart * speed01) + steady;
        }

        /// <summary>Session stars from average pose score: 3 at ≥80%, 2 at ≥55%, else 1.</summary>
        public static int Stars(int totalScore, int poseCount)
        {
            if (poseCount <= 0) return 1;
            float avg01 = totalScore / (float)(poseCount * MaxPoseScore);
            if (avg01 >= 0.80f) return 3;
            if (avg01 >= 0.55f) return 2;
            return 1;
        }

        /// <summary>Coins for the shop economy — same order of magnitude as the battle game.</summary>
        public static int Coins(int totalScore, int stars) => totalScore / 100 + stars * 10;
    }
}
