using UnityEngine;

namespace Kinex.Motion
{
    /// <summary>
    /// Strict "whole body in frame" gate for the framing UI — unlike <see cref="PoseGate"/>
    /// (torso-only safety pause), this checks every body group the games actually track and says
    /// WHY framing fails so the UI can give one specific instruction ("step back, we can't see
    /// your feet"). Reads the 33-point normalized landmarks: MediaPipe extrapolates off-screen
    /// points with high visibility, so each landmark must BOTH be confident AND lie inside the
    /// frame margins to count.
    /// </summary>
    public class FullBodyGate
    {
        // BlazePose-33 indices.
        const int Nose = 0;
        const int LShoulder = 11, RShoulder = 12;
        const int LHip = 23, RHip = 24;
        const int LKnee = 25, RKnee = 26;
        const int LAnkle = 27, RAnkle = 28;

        const float MinVisibility = 0.5f;
        const float Margin = 0.05f;             // landmarks must sit inside [Margin, 1-Margin]
        const float MinBodySpan = 0.55f;        // nose→ankle span as fraction of frame height
        const float MaxBodySpan = 0.95f;
        const float CenterTolerance = 0.22f;    // hip-mid x offset from 0.5 before "move to center"

        public enum Group { Head = 0, Shoulders = 1, Hips = 2, Knees = 3, Feet = 4 }

        public enum Reason
        {
            Ok,
            NotFound,   // no pose at all
            HeadCut,    // head missing / at top edge
            FeetCut,    // ankles missing / at bottom edge
            TooClose,   // body span fills too much of the frame
            TooFar,     // body span too small
            OffCenter,  // horizontally off to one side
            PartlyHidden, // mid-body group missing (occluded)
        }

        public bool IsFullBodyVisible { get; private set; }
        public Reason Why { get; private set; } = Reason.NotFound;
        /// <summary>Per-group pass flags, indexed by <see cref="Group"/> — drives the ✓/✗ checklist chips.</summary>
        public bool[] GroupOk { get; } = new bool[5];
        /// <summary>Seconds the gate has been continuously valid — the UI's "hold still" ring rides this.</summary>
        public float ValidSeconds { get; private set; }
        public float InvalidSeconds { get; private set; }

        public void Tick(bool hasPose, MediaPipePoseDetector.NormLandmark[] lm, float dt)
        {
            Evaluate(hasPose, lm);
            if (IsFullBodyVisible) { ValidSeconds += dt; InvalidSeconds = 0f; }
            else { ValidSeconds = 0f; InvalidSeconds += dt; }
        }

        void Evaluate(bool hasPose, MediaPipePoseDetector.NormLandmark[] lm)
        {
            IsFullBodyVisible = false;
            if (!hasPose || lm == null || lm.Length <= RAnkle)
            {
                for (int i = 0; i < GroupOk.Length; i++) GroupOk[i] = false;
                Why = Reason.NotFound;
                return;
            }

            GroupOk[(int)Group.Head] = Seen(lm, Nose);
            GroupOk[(int)Group.Shoulders] = Seen(lm, LShoulder) && Seen(lm, RShoulder);
            GroupOk[(int)Group.Hips] = Seen(lm, LHip) && Seen(lm, RHip);
            GroupOk[(int)Group.Knees] = Seen(lm, LKnee) && Seen(lm, RKnee);
            GroupOk[(int)Group.Feet] = Seen(lm, LAnkle) && Seen(lm, RAnkle);

            // Most-actionable failure first: feet are what portrait framing cuts off in practice.
            if (!GroupOk[(int)Group.Feet]) { Why = Reason.FeetCut; return; }
            if (!GroupOk[(int)Group.Head]) { Why = Reason.HeadCut; return; }
            if (!GroupOk[(int)Group.Shoulders] || !GroupOk[(int)Group.Hips] || !GroupOk[(int)Group.Knees])
            {
                Why = Reason.PartlyHidden;
                return;
            }

            float ankleY = Mathf.Max(lm[LAnkle].y, lm[RAnkle].y);
            float span = Mathf.Abs(ankleY - lm[Nose].y);
            if (span > MaxBodySpan) { Why = Reason.TooClose; return; }
            if (span < MinBodySpan) { Why = Reason.TooFar; return; }

            float hipMidX = (lm[LHip].x + lm[RHip].x) * 0.5f;
            if (Mathf.Abs(hipMidX - 0.5f) > CenterTolerance) { Why = Reason.OffCenter; return; }

            Why = Reason.Ok;
            IsFullBodyVisible = true;
        }

        static bool Seen(MediaPipePoseDetector.NormLandmark[] lm, int i)
        {
            return lm[i].visibility >= MinVisibility &&
                   lm[i].x > Margin && lm[i].x < 1f - Margin &&
                   lm[i].y > Margin && lm[i].y < 1f - Margin;
        }
    }
}
