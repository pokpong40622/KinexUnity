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
        // Per-limb names — all 8 segments named distinctly so coaching covers the full body.
        // Order matches PoseScorer: L/R upper arm, L/R forearm, L/R thigh, L/R shin.
        static readonly string[] LimbName = {
            "left upper arm",  "right upper arm",
            "left forearm",    "right forearm",
            "left thigh",      "right thigh",
            "left shin",       "right shin"
        };
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
            string limbName = LimbName[worst];
            string side = Side[worst];
            if (mirrorLR)
            {
                side = side == "left" ? "right" : "left";
                limbName = side + limbName.Substring(limbName.IndexOf(' ')); // flip the side prefix
                if (dir == "left") dir = "right";
                else if (dir == "right") dir = "left";
            }
            return Sentence(limbName, IsLeg[worst], dir, worstErr);
        }

        // Build a natural instruction from a full limb name (e.g. "left upper arm"), whether it is
        // a leg segment, the direction the far end must travel, and the error magnitude for urgency.
        static string Sentence(string limbName, bool isLeg, string dir, float errRad)
        {
            float errDeg = errRad * Mathf.Rad2Deg;
            // amount adverb for up/down ("higher"/"much higher"/"a little higher")
            string amt = errDeg > 70f ? "much " : errDeg < 30f ? "a little " : "";
            switch (dir)
            {
                case "up":
                    return isLeg ? $"Lift your {limbName} {amt}higher"
                                 : $"Raise your {limbName} {amt}higher";
                case "down":
                    return errDeg > 70f ? $"Drop your {limbName} lower"
                                        : $"Lower your {limbName}";
                default: // "left" / "right"
                    return errDeg > 70f ? $"Swing your {limbName} to the {dir}"
                                        : $"Move your {limbName} to the {dir}";
            }
        }

        // Which way the limb's far end must move to rotate the player's angle toward the target.
        //
        // Angles come from PoseSignatureBaker/PoseScorer.ComputeAngles (rig path).
        // SegmentAngle = Atan2(dy, dx) where:
        //   dx = -(to.x - from.x)   (negate world-X)
        //   dy = -(to.y - from.y) but baker projects v = -world.y, so dy = -(-world_y_B + world_y_A)
        //                                                                    = world_y_B - world_y_A
        // Therefore sin(angle) > 0  ↔  far end is ABOVE near end in world space  ("up").
        //           cos(angle) > 0  ↔  dx > 0  ↔  to.x < from.x  ↔  far end to world-LEFT.
        //                              (left/right will be flipped by mirrorLR in Compute()).
        // Use the angle vector directly (no negation) so up/down map to world up/down correctly.
        static string Direction(float playerAngle, float targetAngle)
        {
            // Unit-circle tip position in rig-angle space: y = sin → world-up, x = cos → rig-left.
            Vector2 cur  = new Vector2(Mathf.Cos(playerAngle), Mathf.Sin(playerAngle));
            Vector2 want = new Vector2(Mathf.Cos(targetAngle), Mathf.Sin(targetAngle));
            Vector2 move = want - cur; // direction the limb tip must travel

            if (Mathf.Abs(move.x) >= Mathf.Abs(move.y))
                return move.x >= 0f ? "left" : "right"; // x+ = rig-left; mirrorLR will flip if needed
            // y+ = world-up, so positive move.y means the limb needs to go higher.
            return move.y >= 0f ? "up" : "down";
        }
    }
}
