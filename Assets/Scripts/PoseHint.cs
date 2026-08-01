using UnityEngine;

namespace Kinex.MegaDance
{
    /// <summary>
    /// Turns the scorer's per-limb angle error into a single short coaching line, e.g.
    /// "Move left arm up". Pure + static so it can be unit-tested offline like PoseScorer.
    ///
    /// It reflects EXACTLY what the scorer wants: it picks the limb with the largest angle
    /// error (above a deadzone) and tells the user which way to swing it so that limb's
    /// direction rotates toward the target. Doubles as a live debugging aid.
    /// </summary>
    public static class PoseHint
    {
        // Per-limb "your left/right arm/leg" — forearm/shin fold into arm/leg so the spoken
        // line stays plain-English ("your right arm") instead of clinical ("right forearm").
        static readonly string[] Side = { "left", "right", "left", "right",
                                          "left", "right", "left", "right" };
        static readonly bool[] IsLeg = { false, false, false, false, true, true, true, true };

        /// <summary>
        /// Returns a full coaching sentence (or "" if every limb is within the deadzone /
        /// nothing valid). playerAngles/targetAngles are SegmentAngle-space radians from
        /// PoseScorer.ComputeAngles. deadzoneRad: limbs closer than this produce no hint.
        /// turnErrorDeg/turnDeadzoneDeg add a TURN cue that takes priority over the limb cue — a
        /// signed avatar-vs-trainer hip-yaw gap; positive ⇒ "turn right". Pass the default huge
        /// deadzone to disable it (e.g. 2D mode / unit tests).
        /// </summary>
        public static string Compute(float[] playerAngles, float[] targetAngles, bool[] valid,
                                     float deadzoneRad, bool mirrorLR = false,
                                     float turnErrorDeg = 0f, float turnDeadzoneDeg = 9999f)
        {
            if (playerAngles == null || targetAngles == null) return "";

            // 1) TURN first — orientation can't be fixed by moving a single limb, so coach it before
            //    anything else when the player is facing the wrong way for a side-on pose.
            if (Mathf.Abs(turnErrorDeg) > turnDeadzoneDeg)
                return turnErrorDeg > 0f ? "Turn to your right." : "Turn to your left.";

            int worst = -1;
            float worstErr = deadzoneRad;
            for (int i = 0; i < PoseScorer.NumLimbs; i++)
            {
                if (valid != null && !valid[i]) continue;
                float err = PoseScorer.AngleError(playerAngles[i], targetAngles[i]);
                if (err > worstErr) { worstErr = err; worst = i; }
            }
            if (worst < 0) return "Great! Hold the pose!"; // everything within the deadzone

            string dir = Direction(playerAngles[worst], targetAngles[worst]);
            // The avatar mirrors the user, so the rig's left/right is the OPPOSITE of the limb the
            // user must move. mirrorLR flips both the named side and the left/right direction so the
            // coaching matches what the user sees (raise the arm on the same side as your mirror).
            string side = Side[worst];
            if (mirrorLR)
            {
                side = side == "left" ? "right" : "left";
                if (dir == "left") dir = "right";
                else if (dir == "right") dir = "left";
            }
            return Sentence(side, IsLeg[worst], dir, worstErr);
        }

        // Build a natural instruction from side ("left"/"right"), limb kind, the screen direction
        // the limb's far end must travel ("up"/"down"/"left"/"right"), and how far off it is so the
        // line conveys urgency ("a little" vs "much"/"way").
        static string Sentence(string side, bool isLeg, string dir, float errRad)
        {
            string limb = isLeg ? "leg" : "arm";
            float errDeg = errRad * Mathf.Rad2Deg;
            // amount adverb (" a lot" / " a little" / "")
            string amt = errDeg > 70f ? " a lot" : errDeg < 30f ? " a little" : "";
            switch (dir)
            {
                case "up":
                    return $"Raise your {side} {limb}{amt}.";
                case "down":
                    return $"Lower your {side} {limb}{amt}.";
                case "left":
                    return $"Move your {side} {limb} left{amt}.";
                default: // "right"
                    return $"Move your {side} {limb} right{amt}.";
            }
        }

        // Which way the limb's far end must move to rotate the player's angle toward the target.
        // SegmentAngle uses dx=-(to.x-from.x), dy=-(to.y-from.y), so a SegmentAngle vector
        // (cosA, sinA) corresponds to a SCREEN direction of (-cosA, -sinA) with screen-y DOWN.
        // We compare where the limb's end IS (player) vs where it SHOULD be (target) in screen
        // space and name the dominant axis of that move.
        static string Direction(float playerAngle, float targetAngle)
        {
            // End positions on a unit circle in SCREEN space (y DOWN): screenDir = (-cos, -sin).
            Vector2 cur = new Vector2(-Mathf.Cos(playerAngle), -Mathf.Sin(playerAngle));
            Vector2 want = new Vector2(-Mathf.Cos(targetAngle), -Mathf.Sin(targetAngle));
            Vector2 move = want - cur; // direction the limb end should travel, screen space (y DOWN)

            if (Mathf.Abs(move.x) >= Mathf.Abs(move.y))
                return move.x >= 0f ? "right" : "left";
            // y is screen-DOWN, so positive y = downward on screen.
            return move.y >= 0f ? "down" : "up";
        }
    }
}
