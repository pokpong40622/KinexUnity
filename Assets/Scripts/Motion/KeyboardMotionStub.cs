using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinex.Motion
{
    /// <summary>
    /// Editor/dev testing stub — exposes the SAME property names as every real detector above so
    /// a game can be wired up and played with a keyboard before the camera pipeline is plugged in
    /// (mirrors how MegaDanceManager's useKeyboardStub flag works today).
    ///
    /// S = toggle stand/sit, Left/Right arrows = lane while held, K/J = left/right knee raise
    /// pulse, T held = tiptoe, N/M = left/right kick pulse, B held = single-leg hold.
    /// </summary>
    public class KeyboardMotionStub
    {
        const float StandDuration = 0.4f;

        public bool Visible => true;
        public bool ShouldPauseGame => false;

        public float Progress01 { get; private set; }
        public bool JustStood { get; private set; }
        public bool JustSat { get; private set; }
        public int StandCount { get; private set; }
        bool _standing;
        float _progressTarget;
        bool _sPrevDown;

        public int Lane { get; private set; }
        public bool JustChangedLane { get; private set; }

        public bool LeftUp { get; private set; }
        public bool RightUp { get; private set; }
        public bool JustCountedAlternating { get; private set; }
        public int AlternatingCount { get; private set; }
        int _lastCountedLeg;
        bool _kPrevDown, _jPrevDown;

        public bool IsRaised { get; private set; }
        public bool JustRaised { get; private set; }

        public bool JustKickedLeft { get; private set; }
        public bool JustKickedRight { get; private set; }
        bool _nPrevDown, _mPrevDown;

        public bool IsHolding { get; private set; }
        public float HoldSeconds { get; private set; }
        public float Wobble01 { get; private set; }

        // --- Balance Quest additions (H/G = back-kick pulses, V = tandem-stand hold) ---
        public bool JustKickedBackLeft { get; private set; }
        public bool JustKickedBackRight { get; private set; }
        bool _hPrevDown, _gPrevDown;

        public bool IsTandemHolding { get; private set; }
        public float TandemHoldSeconds { get; private set; }

        public void Tick(float dt)
        {
            JustStood = false;
            JustSat = false;
            JustChangedLane = false;
            JustCountedAlternating = false;
            JustRaised = false;
            JustKickedLeft = false;
            JustKickedRight = false;
            JustKickedBackLeft = false;
            JustKickedBackRight = false;

            var kb = Keyboard.current;
            if (kb == null) return;

            // Stand/sit toggle.
            bool sDown = kb.sKey.isPressed;
            if (sDown && !_sPrevDown)
            {
                _standing = !_standing;
                _progressTarget = _standing ? 1f : 0f;
                if (_standing) { JustStood = true; StandCount++; }
                else JustSat = true;
            }
            _sPrevDown = sDown;
            Progress01 = Mathf.MoveTowards(Progress01, _progressTarget, dt / StandDuration);

            // Lane, while held.
            bool left = kb.leftArrowKey.isPressed;
            bool right = kb.rightArrowKey.isPressed;
            int newLane = left ? -1 : (right ? 1 : 0);
            if (newLane != Lane) { Lane = newLane; JustChangedLane = true; }

            // Knee raise pulses — same alternation-gating as KneeRaiseDetector.
            bool kDown = kb.kKey.isPressed;
            if (kDown && !_kPrevDown) CountKnee(isLeft: true);
            _kPrevDown = kDown;

            bool jDown = kb.jKey.isPressed;
            if (jDown && !_jPrevDown) CountKnee(isLeft: false);
            _jPrevDown = jDown;

            LeftUp = kDown;
            RightUp = jDown;

            // Tiptoe, held.
            bool tDown = kb.tKey.isPressed;
            if (tDown && !IsRaised) JustRaised = true;
            IsRaised = tDown;

            // Kick pulses.
            bool nDown = kb.nKey.isPressed;
            if (nDown && !_nPrevDown) JustKickedLeft = true;
            _nPrevDown = nDown;

            bool mDown = kb.mKey.isPressed;
            if (mDown && !_mPrevDown) JustKickedRight = true;
            _mPrevDown = mDown;

            // Single-leg hold, held.
            bool bDown = kb.bKey.isPressed;
            IsHolding = bDown;
            if (bDown) { HoldSeconds += dt; Wobble01 = 0.2f; }
            else { HoldSeconds = 0f; Wobble01 = 0f; }

            // Back-kick pulses (Balance Quest BackKick beat).
            bool hDown = kb.hKey.isPressed;
            if (hDown && !_hPrevDown) JustKickedBackLeft = true;
            _hPrevDown = hDown;

            bool gDown = kb.gKey.isPressed;
            if (gDown && !_gPrevDown) JustKickedBackRight = true;
            _gPrevDown = gDown;

            // Tandem-stand hold (Balance Quest TandemStand beat).
            bool vDown = kb.vKey.isPressed;
            IsTandemHolding = vDown;
            TandemHoldSeconds = vDown ? TandemHoldSeconds + dt : 0f;
        }

        void CountKnee(bool isLeft)
        {
            int leg = isLeft ? 1 : 2;
            if (leg != _lastCountedLeg)
            {
                _lastCountedLeg = leg;
                AlternatingCount++;
                JustCountedAlternating = true;
            }
        }
    }
}
