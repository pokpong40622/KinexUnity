using UnityEngine;
using Kinex.Motion;

namespace Kinex.DanceStar
{
    /// <summary>
    /// Tandem-stand (heel-to-toe) check for SUPERSTAR STAGE — ports BalanceQuest's
    /// TandemStandBeatRunner stillness idea (hold-with-decay while hip height stays put) and adds
    /// an ankle-proximity gate on top: the camera can't verify true heel-to-toe foot placement,
    /// but it CAN see whether the ankles are drawn close together in X, which is what actually
    /// distinguishes a tandem stance from a normal shoulder-width stance. Kept DanceStar-local
    /// (not in Assets/Scripts/Motion) the same way TempleLogic keeps its Temple-specific arm-pose
    /// checks local — this is a gate layered on existing detectors, not a general detector.
    /// Pure static so DanceStarSelfTest can hit it directly with synthetic keypoints.
    /// </summary>
    public static class DanceTandemLogic
    {
        const float MinConf = 0.3f;
        const float AnkleCloseFactor = 0.35f;   // * torso — ankles must be at least this close
        const float HipHeightTolerance = 0.15f; // * torso, same convention as SingleLegStanceDetector

        /// <summary>
        /// True when both ankles are drawn close together (tandem-ish stance) AND hips are near
        /// the given standing baseline height (not crouching/stepping). Null when required joints
        /// aren't confidently tracked — caller should hold its last known answer, same convention
        /// as TempleLogic.ArmsOutOk / HandsOnChestOk.
        /// </summary>
        public static bool? IsTandemPose(Vector2[] kp, float[] conf, float hipY0)
        {
            if (!MotionMath.Valid(conf, MinConf,
                    MotionMath.LAnkle, MotionMath.RAnkle,
                    MotionMath.LHip, MotionMath.RHip,
                    MotionMath.LShoulder, MotionMath.RShoulder))
                return null;

            float torso = MotionMath.TorsoLen(kp);
            if (torso < 0.001f) return null;

            float ankleDx = Mathf.Abs(kp[MotionMath.LAnkle].x - kp[MotionMath.RAnkle].x);
            bool anklesClose = ankleDx < AnkleCloseFactor * torso;

            float hipMidY = MotionMath.Mid(kp[MotionMath.LHip], kp[MotionMath.RHip]).y;
            bool heightOk = Mathf.Abs(hipMidY - hipY0) < HipHeightTolerance * torso;

            return anklesClose && heightOk;
        }

        /// <summary>Hold-with-decay: builds at 1x while held, drains at 2x while lost, clamped to
        /// [0, target]. Identical shape to TempleLogic.TickHold / TandemStandBeatRunner's held-seconds.</summary>
        public static float TickHold(float held, bool ok, float dt, float target) =>
            ok ? Mathf.Min(target, held + dt) : Mathf.Max(0f, held - 2f * dt);
    }
}
