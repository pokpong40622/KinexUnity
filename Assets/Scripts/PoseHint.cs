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
        /// </summary>
        public static string Compute(float[] playerAngles, float[] targetAngles, bool[] valid,
                                     float deadzoneRad, bool mirrorLR = false)
        {
            if (playerAngles == null || targetAngles == null) return "";
            int worst = -1;
            float worstErr = deadzoneRad;
            for (int i = 0; i < PoseScorer.NumLimbs; i++)
            {
                if (valid != null && !valid[i]) continue;
                float err = PoseScorer.AngleError(playerAngles[i], targetAngles[i]);
                if (err > worstErr) { worstErr = err; worst = i; }
            }
            if (worst < 0) return "Nice — hold that pose!"; // everything within the deadzone

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
            // amount adverb for up/down ("higher"/"much higher"/"a little higher")
            string amt = errDeg > 70f ? "much " : errDeg < 30f ? "a little " : "";
            switch (dir)
            {
                case "up":
                    return isLeg ? $"Lift your {side} {limb} {amt}higher"
                                 : $"Raise your {side} {limb} {amt}higher";
                case "down":
                    return errDeg > 70f ? $"Drop your {side} {limb} down lower"
                                        : $"Lower your {side} {limb}";
                default: // "left" / "right"
                    return errDeg > 70f ? $"Swing your {side} {limb} to the {dir}"
                                        : $"Move your {side} {limb} to the {dir}";
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
