using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Head facing left/right from the 33-point landmarks — the classic monocular heuristic:
    /// where the nose sits between the two ears. Facing the camera the nose is mid-way; turning
    /// pushes it toward one ear and the far ear's visibility drops. Runs on 2D normalized x only
    /// (depth-free, works at any distance), with hysteresis + a sustain time so glances don't
    /// flicker the state.
    ///
    /// Facing sign convention matches the PLAYER's own left/right (selfie view): the front camera
    /// mirrors the image, so the player's left ear (index 7) appears on the image's right side.
    /// </summary>
    public class HeadYawDetector
    {
        const int Nose = 0;
        const int LEar = 7, REar = 8;

        const float MinVisibility = 0.4f;
        const float TurnEnter = 0.18f;      // |offset from center| as fraction of ear span
        const float TurnExit = 0.10f;       // hysteresis re-center threshold
        const float FarEarLost = 0.35f;     // far-ear visibility below this = strong turn evidence
        const float SustainSeconds = 0.2f;  // new direction must hold this long before it counts
        const float Smoothing = 0.35f;      // EMA on the raw ratio

        float _offsetSmoothed;
        bool _hasSample;
        int _pendingFacing;
        float _pendingSeconds;

        /// <summary>-1 = turned to player's left, 0 = facing camera, +1 = turned to player's right.</summary>
        public int Facing { get; private set; }
        /// <summary>Continuous signed turn amount, roughly -1..+1 (player-left negative). For meters/debug.</summary>
        public float Yaw01 { get; private set; }
        public bool HasSignal { get; private set; }

        public void Tick(MediaPipePoseDetector.NormLandmark[] lm, float dt)
        {
            HasSignal = false;
            if (lm == null || lm.Length <= REar) return;

            var nose = lm[Nose];
            var lEar = lm[LEar];
            var rEar = lm[REar];
            if (nose.visibility < MinVisibility) return;
            // Need at least one ear; with one ear lost we already know which way the head turned.
            bool lSeen = lEar.visibility >= MinVisibility;
            bool rSeen = rEar.visibility >= MinVisibility;
            if (!lSeen && !rSeen) return;

            // Convention: NEGATIVE = player turned left. Assumes the player's LEFT ear sits at the
            // larger image x (matches the avatar/selfie mapping used elsewhere); if a device build
            // comes out mirrored, the director's invertHeadYaw toggle flips it.
            //
            // Geometry (head as a circle, nose at the front): turning LEFT swings the nose toward
            // the player-left ear (r > 0.5) while that same left ear rotates away from the camera —
            // so ratio and ear-visibility evidence must carry the SAME sign.
            float offset;
            if (lSeen && rSeen)
            {
                float span = lEar.x - rEar.x; // player-left ear at larger image x
                if (Mathf.Abs(span) < 0.01f) return; // profile view / degenerate — hold state
                float r = (nose.x - rEar.x) / span;   // 0.5 = centered, >0.5 = nose toward left ear
                offset = (0.5f - r) * 2f;             // turned left → negative

                // Strong evidence override: a fading ear means a big turn regardless of ratio noise.
                if (lEar.visibility < FarEarLost) offset = Mathf.Min(offset, -1f);      // left ear hides when turning left
                else if (rEar.visibility < FarEarLost) offset = Mathf.Max(offset, 1f);  // right ear hides when turning right
            }
            else
            {
                // Only one ear visible: the HIDDEN ear is the side the head turned toward.
                offset = lSeen ? 1f : -1f;
            }

            _offsetSmoothed = _hasSample ? Mathf.Lerp(_offsetSmoothed, offset, Smoothing) : offset;
            _hasSample = true;
            HasSignal = true;
            Yaw01 = Mathf.Clamp(_offsetSmoothed, -1f, 1f);

            // Hysteresis: enter a turn past TurnEnter, only return to center inside TurnExit.
            int target = Facing;
            float mag = Mathf.Abs(_offsetSmoothed);
            if (Facing == 0)
            {
                if (mag > TurnEnter) target = _offsetSmoothed < 0f ? -1 : 1;
            }
            else
            {
                if (mag < TurnExit) target = 0;
                else if (Mathf.Sign(_offsetSmoothed) != Facing && mag > TurnEnter)
                    target = _offsetSmoothed < 0f ? -1 : 1;
            }

            // Sustain gate so a one-frame spike doesn't flip the state.
            if (target != Facing)
            {
                if (target == _pendingFacing) _pendingSeconds += dt;
                else { _pendingFacing = target; _pendingSeconds = dt; }
                if (_pendingSeconds >= SustainSeconds) Facing = target;
            }
            else _pendingSeconds = 0f;
        }
    }
}
