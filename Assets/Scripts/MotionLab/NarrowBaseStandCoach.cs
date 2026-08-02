using UnityEngine;
using Kinex.Motion;

namespace Kinex.MotionLab
{
    /// <summary>
    /// Live coach for narrow-base standing ("ยืนฐานแคบ — ถ่ายน้ำหนักลงส้นเท้า แล้วเขย่งปลายเท้า") —
    /// two 10-second phases: phase 0 rocked back on the HEELS, phase 1 up on the TOES.
    ///
    /// Two different signals, each chosen because it is the one that actually survives a tablet
    /// camera:
    ///   * phase 1 (toes) uses <see cref="TiptoeDetector"/> — it deliberately ignores the feet and
    ///     reads how far the hips + shoulders rose together, which is robust even when the feet are
    ///     cropped or blurry.
    ///   * phase 0 (heels) cannot use that: rocking back barely moves the body. It reads the FOOT
    ///     TILT instead, from the BlazePose heel (29/30) and foot-index/toe (31/32) landmarks —
    ///     lifting the toes raises toe.y relative to heel.y. If the feet are not visible this coach
    ///     says 'not_in_frame' rather than guessing.
    ///
    /// <see cref="SingleLegStanceDetector"/> is reused purely for its Wobble01 (a 1-second rolling
    /// stddev of hip X); it is not asked whether anything is "holding".
    ///
    /// Cue ids: get_ready, on_heels, on_toes, hold, steady, stand_tall, phase_done, not_in_frame, done.
    ///
    /// NOT judged: stance WIDTH (the "narrow base" part). The ADR gives this pose no cue id for it,
    /// so there is no way to tell the user about it — Flutter's wizard sets the stance up front.
    /// </summary>
    public class NarrowBaseStandCoach : PoseCoachBase
    {
        [Header("Session")]
        [Tooltip("Seconds each phase (heels, then toes) must be held.")]
        public float holdTargetSeconds = 10f;

        [Header("Heels phase (foot tilt, in baseline foot lengths)")]
        [Tooltip("How far the toes must rise relative to the heels, measured against the flat-footed " +
                 "warm-up baseline, to count as 'on the heels'. Feet are small and noisy in a portrait " +
                 "tablet frame — this is the single least certain threshold in the folder.")]
        public float heelTiltRatio = 0.25f;
        [Tooltip("Minimum visibility on the heel/toe landmarks before the heels phase can be judged.")]
        [Range(0f, 1f)] public float footVisibility = 0.4f;

        [Header("Form checks")]
        [Tooltip("Wobble (0..1) above which the user is told to steady up. Advice only — the hold clock " +
                 "keeps running.")]
        [Range(0f, 1f)] public float steadyWobbleLimit = 0.55f;
        [Tooltip("Sideways offset between shoulder-mid and hip-mid as a fraction of torso length.")]
        public float trunkLeanRatio = 0.22f;

        const string CueOnHeels = "on_heels";
        const string CueOnToes = "on_toes";
        const string CuePhaseDone = "phase_done";

        // BlazePose-33 foot landmarks. COCO-17 has no heel or toe, so these must come from Landmarks33.
        const int LmLHeel = 29, LmRHeel = 30, LmLToe = 31, LmRToe = 32;

        protected override string PoseId => "narrow_base_stand";
        protected override string Mode => "hold";
        protected override string SidesMode => "phases";
        protected override int Target => Mathf.RoundToInt(holdTargetSeconds);

        readonly TiptoeDetector _tiptoe = new TiptoeDetector();
        readonly SingleLegStanceDetector _sway = new SingleLegStanceDetector(); // Wobble01 only

        int _phase; // 0 = heels, 1 = toes
        float _tilt0, _footLen0;
        bool _hasFootBaseline;

        void Awake() => PhaseLabel = "phase0";

        protected override void OnWarmupComplete(Vector2[] kp)
        {
            _tiptoe.CalibrateStanding(kp);
            _sway.CalibrateStanding(kp);

            var lm = Landmarks;
            if (lm == null || lm.Length <= LmRToe) return;
            _tilt0 = FootTilt(lm);
            _footLen0 = Mathf.Max(FootLength(lm), 0.01f);
            _hasFootBaseline = true;
        }

        protected override string Evaluate(float dt, out int angleDeg, out bool immediate)
        {
            immediate = false;
            angleDeg = 0;

            if (!ReadPose(dt, out Vector2[] kp)) { TickHold(false, dt, holdTargetSeconds); return LostCue(); }
            if (Warming(kp, dt)) return CueGetReady;

            _tiptoe.Tick(kp, Confidence, dt);
            _sway.Tick(kp, Confidence, dt);

            bool inPose;
            if (_phase == 0)
            {
                var lm = Landmarks;
                if (!_hasFootBaseline || !FeetVisible(lm))
                {
                    // No usable feet means the heels phase genuinely cannot be judged. Say so
                    // instead of silently passing or silently stalling.
                    TickHold(false, dt, holdTargetSeconds);
                    return CueNotInFrame;
                }
                float tilt = (FootTilt(lm) - _tilt0) / _footLen0;
                angleDeg = Mathf.RoundToInt(tilt * 100f); // informational: toe lift in % of foot length
                inPose = tilt > heelTiltRatio;
            }
            else
            {
                inPose = _tiptoe.IsRaised;
                angleDeg = Mathf.RoundToInt(_tiptoe.Progress01 * 100f); // informational: body rise in %
            }

            TickHold(inPose, dt, holdTargetSeconds);

            if (HoldSeconds >= holdTargetSeconds)
            {
                Reps++;               // phases completed (0 -> 1 -> 2)
                ResetHold();
                immediate = true;
                if (_phase == 1) return FinishSession(Reps, 2);
                _phase = 1;
                PhaseLabel = "phase1";
                Kinex.Sfx.Play("checkpoint", 0.7f);
                return CuePhaseDone;
            }

            if (!inPose) return _phase == 0 ? CueOnHeels : CueOnToes;
            if (_sway.Wobble01 > steadyWobbleLimit) return CueSteady;
            if (TrunkLean(kp) > trunkLeanRatio) return CueStandTall;
            return CueHold;
        }

        /// <summary>
        /// Mean (heel.y - toe.y) across both feet. Image y grows downward, so lifting the TOES makes
        /// this larger and lifting the HEELS makes it smaller.
        /// </summary>
        static float FootTilt(MediaPipePoseDetector.NormLandmark[] lm) =>
            ((lm[LmLHeel].y - lm[LmLToe].y) + (lm[LmRHeel].y - lm[LmRToe].y)) * 0.5f;

        float FootLength(MediaPipePoseDetector.NormLandmark[] lm)
        {
            float l = Delta(new Vector2(lm[LmLHeel].x, lm[LmLHeel].y),
                            new Vector2(lm[LmLToe].x, lm[LmLToe].y)).magnitude;
            float r = Delta(new Vector2(lm[LmRHeel].x, lm[LmRHeel].y),
                            new Vector2(lm[LmRToe].x, lm[LmRToe].y)).magnitude;
            return (l + r) * 0.5f;
        }

        bool FeetVisible(MediaPipePoseDetector.NormLandmark[] lm) =>
            lm != null && lm.Length > LmRToe &&
            lm[LmLHeel].visibility >= footVisibility && lm[LmRHeel].visibility >= footVisibility &&
            lm[LmLToe].visibility >= footVisibility && lm[LmRToe].visibility >= footVisibility;
    }
}
