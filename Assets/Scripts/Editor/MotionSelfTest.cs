using UnityEngine;
using UnityEditor;
using Kinex.Motion;

namespace Kinex.Motion.EditorTools
{
    /// <summary>
    /// OFFLINE self-test for the whole Motion detector layer. No camera, no scene, no Play mode
    /// needed — synthesizes plausible 17-keypoint COCO frames (y-down, normalized, torso ~0.25)
    /// and feeds them through each detector's Tick(), asserting the documented behaviour.
    ///
    /// Run: menu  Kinex → Run Motion Self Test  (results in the Console).
    /// Mirrors the structure of PoseScorerSelfTest.cs.
    /// </summary>
    public static class MotionSelfTest
    {
        const float Torso = 0.25f;
        const float MinConf = 0.3f;

        static int s_PassCount;

        [MenuItem("Kinex/Run Motion Self Test")]
        public static void Run()
        {
            s_PassCount = 0;

            TestSitStand();
            TestSitStandGoodCalibFiveStands();
            TestSitStandEstimatedCalibRecovers();
            TestSitStandJitterNoFalseCounts();
            TestSitStandCameraDistanceChange();
            TestLane();
            TestKneeRaise();
            TestTiptoe();
            TestLegAbduction();
            TestSingleLegStance();
            TestConfidenceGating();

            Debug.Log($"MOTION SELF TEST: ALL PASS ({s_PassCount})");
        }

        /// <summary>Batch-mode entry point: runs the suite then exits with 0 (pass) or 1 (fail).</summary>
        public static void RunBatch()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        // ---- Fixture builders ---------------------------------------------------------------

        // A plausible standing skeleton (y-down) with hip-mid at hipY and torso length `Torso`.
        static Vector2[] SkeletonAtHipY(float hipY, float centerX = 0.5f)
        {
            float shY = hipY - Torso;
            float kneeY = hipY + Torso;
            float ankleY = hipY + 2f * Torso;
            var kp = new Vector2[17];
            kp[MotionMath.Nose] = new Vector2(centerX, shY - 0.15f);
            kp[MotionMath.LShoulder] = new Vector2(centerX - 0.1f, shY);
            kp[MotionMath.RShoulder] = new Vector2(centerX + 0.1f, shY);
            kp[MotionMath.LElbow] = new Vector2(centerX - 0.15f, shY + 0.1f);
            kp[MotionMath.RElbow] = new Vector2(centerX + 0.15f, shY + 0.1f);
            kp[MotionMath.LWrist] = new Vector2(centerX - 0.2f, shY + 0.2f);
            kp[MotionMath.RWrist] = new Vector2(centerX + 0.2f, shY + 0.2f);
            kp[MotionMath.LHip] = new Vector2(centerX - 0.05f, hipY);
            kp[MotionMath.RHip] = new Vector2(centerX + 0.05f, hipY);
            kp[MotionMath.LKnee] = new Vector2(centerX - 0.05f, kneeY);
            kp[MotionMath.RKnee] = new Vector2(centerX + 0.05f, kneeY);
            kp[MotionMath.LAnkle] = new Vector2(centerX - 0.05f, ankleY);
            kp[MotionMath.RAnkle] = new Vector2(centerX + 0.05f, ankleY);
            return kp;
        }

        static Vector2[] StandingSkeleton() => SkeletonAtHipY(0.6f);

        // Same layout as SkeletonAtHipY, but every joint's offset from the (centerX, hipY) pivot
        // is scaled — simulates the whole skeleton appearing bigger/smaller because the player
        // moved closer to/further from the camera, independent of the sit<->stand motion itself.
        static Vector2[] SkeletonAtHipYScaled(float hipY, float scale, float centerX = 0.5f)
        {
            var kp = SkeletonAtHipY(hipY, centerX);
            for (int i = 0; i < kp.Length; i++)
                kp[i] = new Vector2(centerX + (kp[i].x - centerX) * scale, hipY + (kp[i].y - hipY) * scale);
            return kp;
        }

        static Vector2[] ShiftX(Vector2[] kp, float dx)
        {
            var outKp = (Vector2[])kp.Clone();
            for (int i = 0; i < outKp.Length; i++) outKp[i].x += dx;
            return outKp;
        }

        static Vector2[] ShiftY(Vector2[] kp, float dy)
        {
            var outKp = (Vector2[])kp.Clone();
            for (int i = 0; i < outKp.Length; i++) outKp[i].y += dy;
            return outKp;
        }

        static float[] FullConf()
        {
            var conf = new float[17];
            for (int i = 0; i < 17; i++) conf[i] = 1f;
            return conf;
        }

        // ---- Assertion helper -----------------------------------------------------------------

        static void Assert(bool condition, string message)
        {
            if (condition)
            {
                s_PassCount++;
                Debug.Log($"[MotionSelfTest] PASS: {message}");
            }
            else
            {
                Debug.LogError($"[MotionSelfTest] FAIL: {message}");
                throw new System.Exception($"[MotionSelfTest] FAIL: {message}");
            }
        }

        // ---- Individual detector tests ---------------------------------------------------------

        static void TestSitStand()
        {
            var det = new SitStandDetector();
            const float seatedHipY = 0.75f;
            const float standingHipY = 0.50f;
            var conf = FullConf();
            const float dt = 0.05f;

            for (int i = 0; i < 40; i++) det.CalibrateSeated(SkeletonAtHipY(seatedHipY));
            for (int i = 0; i < 40; i++) det.CalibrateStanding(SkeletonAtHipY(standingHipY));
            Assert(det.IsCalibrated, "SitStand: calibrates from seated+standing averages");

            // Ramp seated -> standing, then hold. The 0.25s hold hysteresis can clear MID-ramp
            // (once Progress01 crosses 0.70 partway through), so JustStood must be watched across
            // the whole ramp+hold sequence, not just after the ramp completes.
            int stoodFireCount = 0;
            for (int i = 0; i <= 30; i++)
            {
                float hipY = Mathf.Lerp(seatedHipY, standingHipY, i / 30f);
                det.Tick(SkeletonAtHipY(hipY), conf, dt);
                if (det.JustStood) stoodFireCount++;
            }
            for (int i = 0; i < 10; i++)
            {
                det.Tick(SkeletonAtHipY(standingHipY), conf, dt);
                if (det.JustStood) stoodFireCount++;
            }
            Assert(stoodFireCount == 1 && det.StandCount == 1,
                "SitStand: sit->stand fires JustStood exactly once (not on every held frame), StandCount==1");

            // Ramp standing -> seated, then hold. Same reasoning: watch across the whole sequence.
            int satFireCount = 0;
            for (int i = 0; i <= 30; i++)
            {
                float hipY = Mathf.Lerp(standingHipY, seatedHipY, i / 30f);
                det.Tick(SkeletonAtHipY(hipY), conf, dt);
                if (det.JustSat) satFireCount++;
            }
            for (int i = 0; i < 10; i++)
            {
                det.Tick(SkeletonAtHipY(seatedHipY), conf, dt);
                if (det.JustSat) satFireCount++;
            }
            Assert(satFireCount == 1, "SitStand: stand->sit fires JustSat exactly once");

            // EstimateStandingFromSeated smoke test (alternative calibration path).
            var det2 = new SitStandDetector();
            for (int i = 0; i < 10; i++) det2.CalibrateSeated(SkeletonAtHipY(seatedHipY));
            det2.EstimateStandingFromSeated();
            Assert(det2.IsCalibrated, "SitStand: EstimateStandingFromSeated completes calibration");
        }

        // ---- Fruit Header field-bug coverage: good calib, bad/estimated calib recovery,
        // jitter rejection, and camera-distance robustness. ----

        static void TestSitStandGoodCalibFiveStands()
        {
            var det = new SitStandDetector();
            const float seatedHipY = 0.75f;
            const float standingHipY = 0.50f;
            var conf = FullConf();
            const float dt = 0.05f;

            for (int i = 0; i < 40; i++) det.CalibrateSeated(SkeletonAtHipY(seatedHipY));
            for (int i = 0; i < 40; i++) det.CalibrateStanding(SkeletonAtHipY(standingHipY));

            for (int cycle = 0; cycle < 5; cycle++)
            {
                for (int i = 0; i <= 20; i++)
                    det.Tick(SkeletonAtHipY(Mathf.Lerp(seatedHipY, standingHipY, i / 20f)), conf, dt);
                for (int i = 0; i < 10; i++)
                    det.Tick(SkeletonAtHipY(standingHipY), conf, dt);
                for (int i = 0; i <= 20; i++)
                    det.Tick(SkeletonAtHipY(Mathf.Lerp(standingHipY, seatedHipY, i / 20f)), conf, dt);
                for (int i = 0; i < 10; i++)
                    det.Tick(SkeletonAtHipY(seatedHipY), conf, dt);
            }
            Assert(det.StandCount == 5,
                $"SitStand adaptive: 5 full sit-stand cycles with a good calibration count exactly 5 stands (got {det.StandCount})");
        }

        static void TestSitStandEstimatedCalibRecovers()
        {
            var det = new SitStandDetector();
            const float seatedHipY = 0.75f;
            var conf = FullConf();
            const float dt = 0.05f;

            // Exactly the field-bug path: seated calibrates fine, but standing is a silent estimate
            // (EstimateStandingFromSeated = seatedY - 0.55*torso).
            for (int i = 0; i < 40; i++) det.CalibrateSeated(SkeletonAtHipY(seatedHipY));
            det.EstimateStandingFromSeated();

            // The player's REAL achievable stand only rises 0.30*torso — a realistic partial stand,
            // well short of the crude 0.55*torso the estimate assumed. Against a static baseline
            // this alone (Progress ~= 0.30/0.55 = 0.545) never crosses even the lowered 0.62
            // threshold. Repeated real attempts should let the adaptive range recover the gap.
            float realStandHipY = seatedHipY - 0.30f * Torso;

            bool everStood = false;
            for (int i = 0; i < 4000 && !everStood; i++) // up to 200s of simulated real attempts
            {
                det.Tick(SkeletonAtHipY(realStandHipY), conf, dt);
                if (det.JustStood) everStood = true;
            }
            Assert(everStood,
                "SitStand adaptive: a crude estimated standing calibration recovers via the adaptive range " +
                "so a real, more modest stand eventually counts");
        }

        static void TestSitStandJitterNoFalseCounts()
        {
            var det = new SitStandDetector();
            const float seatedHipY = 0.75f;
            const float standingHipY = 0.50f;
            var conf = FullConf();
            const float dt = 0.05f;

            for (int i = 0; i < 40; i++) det.CalibrateSeated(SkeletonAtHipY(seatedHipY));
            for (int i = 0; i < 40; i++) det.CalibrateStanding(SkeletonAtHipY(standingHipY));

            // Small noise around the seated pose only — never near the rise threshold.
            float[] jitter = { 0f, 0.01f, -0.008f, 0.012f, -0.015f, 0.006f, -0.011f, 0.009f };
            for (int i = 0; i < 400; i++)
            {
                float noisy = seatedHipY + jitter[i % jitter.Length] * Torso;
                det.Tick(SkeletonAtHipY(noisy), conf, dt);
            }
            Assert(det.StandCount == 0 && det.Current == SitStandDetector.SitStandPhase.Seated,
                "SitStand adaptive: small seated jitter never triggers a stand");
        }

        static void TestSitStandCameraDistanceChange()
        {
            var det = new SitStandDetector();
            const float seatedHipY = 0.75f;
            const float standingHipY = 0.50f;
            var conf = FullConf();
            const float dt = 0.05f;

            // Calibrate at the original camera distance (torso ~0.25).
            for (int i = 0; i < 40; i++) det.CalibrateSeated(SkeletonAtHipY(seatedHipY));
            for (int i = 0; i < 40; i++) det.CalibrateStanding(SkeletonAtHipY(standingHipY));

            // Player moves closer mid-session: torso grows 1.4x. The SAME physical stand now
            // produces a proportionally larger apparent rise — torso normalization should rescale
            // it back into calibration space so it still counts.
            const float scale = 1.4f;
            float scaledStandingHipY = seatedHipY - (seatedHipY - standingHipY) * scale;

            int stoodFireCount = 0;
            for (int i = 0; i <= 20; i++)
            {
                float hipY = Mathf.Lerp(seatedHipY, scaledStandingHipY, i / 20f);
                det.Tick(SkeletonAtHipYScaled(hipY, scale), conf, dt);
                if (det.JustStood) stoodFireCount++;
            }
            for (int i = 0; i < 10; i++)
            {
                det.Tick(SkeletonAtHipYScaled(scaledStandingHipY, scale), conf, dt);
                if (det.JustStood) stoodFireCount++;
            }
            Assert(stoodFireCount == 1 && det.StandCount == 1,
                "SitStand adaptive: torso-length normalization keeps detecting stands after a camera-distance change");
        }

        static void TestLane()
        {
            var det = new LaneDetector();
            var conf = FullConf();
            for (int i = 0; i < 10; i++) det.CalibrateCenter(StandingSkeleton());
            Assert(det.IsCalibrated && det.Lane == 0, "Lane: calibrates centered");

            var shiftedRight = ShiftX(StandingSkeleton(), 0.15f);
            bool changedRight = false;
            for (int i = 0; i < 6; i++) // 6 * 0.05s = 0.3s > 0.15s sustain
            {
                det.Tick(shiftedRight, conf, 0.05f);
                if (det.JustChangedLane) changedRight = true;
            }
            Assert(changedRight && det.Lane == 1, "Lane: sustained side-shift flips Lane to +1 and fires JustChangedLane");

            var shiftedLeft = ShiftX(StandingSkeleton(), -0.15f);
            bool changedLeft = false;
            for (int i = 0; i < 4; i++) det.Tick(StandingSkeleton(), conf, 0.05f); // pass back through center first
            for (int i = 0; i < 6; i++)
            {
                det.Tick(shiftedLeft, conf, 0.05f);
                if (det.JustChangedLane) changedLeft = true;
            }
            Assert(changedLeft && det.Lane == -1, "Lane: sustained opposite side-shift flips Lane to -1");
        }

        static void TestKneeRaise()
        {
            var det = new KneeRaiseDetector();
            det.SetBaseline(StandingSkeleton());
            var conf = FullConf();

            var down = StandingSkeleton();
            var raisedLeft = (Vector2[])StandingSkeleton().Clone();
            raisedLeft[MotionMath.LKnee] = new Vector2(raisedLeft[MotionMath.LKnee].x, 0.45f); // well above rearm/raise threshold
            var raisedRight = (Vector2[])StandingSkeleton().Clone();
            raisedRight[MotionMath.RKnee] = new Vector2(raisedRight[MotionMath.RKnee].x, 0.45f);

            det.Tick(raisedLeft, conf, 0.1f);
            Assert(det.LeftUp && det.JustCountedAlternating && det.AlternatingCount == 1,
                "KneeRaise: first left raise counts (alternation starts from none)");

            det.Tick(down, conf, 0.1f);
            Assert(!det.LeftUp, "KneeRaise: left leg re-arms after lowering below rearm threshold");

            det.Tick(raisedLeft, conf, 0.6f); // clears the 0.4s debounce
            Assert(!det.JustCountedAlternating && det.AlternatingCount == 1,
                "KneeRaise: same-leg-twice in a row does NOT increment AlternatingCount");

            det.Tick(down, conf, 0.1f);
            det.Tick(raisedRight, conf, 0.6f);
            Assert(det.RightUp && det.JustCountedAlternating && det.AlternatingCount == 2,
                "KneeRaise: alternating to the other leg counts");
        }

        static void TestTiptoe()
        {
            var det = new TiptoeDetector();
            for (int i = 0; i < 10; i++) det.CalibrateStanding(StandingSkeleton());
            Assert(det.IsCalibrated, "Tiptoe: calibrates standing baseline");

            float riseAmt = 0.10f * Torso; // > 0.06 * torso on threshold
            var raised = ShiftY(StandingSkeleton(), -riseAmt);
            bool justRaised = false;
            var conf = FullConf();
            for (int i = 0; i < 6; i++) // 6 * 0.05s = 0.3s > 0.2s sustain
            {
                det.Tick(raised, conf, 0.05f);
                if (det.JustRaised) justRaised = true;
            }
            Assert(justRaised && det.IsRaised, "Tiptoe: sustained rise fires JustRaised and sets IsRaised");
        }

        static void TestLegAbduction()
        {
            var det = new LegAbductionDetector();
            det.SetBaseline(StandingSkeleton());
            var conf = FullConf();

            var kickLeft = (Vector2[])StandingSkeleton().Clone();
            float hipXL = kickLeft[MotionMath.LHip].x;
            kickLeft[MotionMath.LAnkle] = new Vector2(hipXL - 0.45f * Torso - 0.05f, kickLeft[MotionMath.LAnkle].y);
            det.Tick(kickLeft, conf, 0.1f);
            Assert(det.LeftOut && det.JustKickedLeft, "LegAbduction: left ankle kicked out fires JustKickedLeft");

            var kickRight = (Vector2[])StandingSkeleton().Clone();
            float hipXR = kickRight[MotionMath.RHip].x;
            kickRight[MotionMath.RAnkle] = new Vector2(hipXR + 0.45f * Torso + 0.05f, kickRight[MotionMath.RAnkle].y);
            det.Tick(kickRight, conf, 0.1f);
            Assert(det.RightOut && det.JustKickedRight, "LegAbduction: right ankle kicked out fires JustKickedRight");
        }

        static void TestSingleLegStance()
        {
            var det = new SingleLegStanceDetector();
            for (int i = 0; i < 10; i++) det.CalibrateStanding(StandingSkeleton());
            Assert(det.IsCalibrated, "SingleLegStance: calibrates standing baseline");

            var conf = FullConf();
            var holding = (Vector2[])StandingSkeleton().Clone();
            holding[MotionMath.RKnee] = new Vector2(holding[MotionMath.RKnee].x, holding[MotionMath.LHip].y - 0.3f * Torso);
            var neutral = StandingSkeleton(); // both feet down -> not holding

            for (int i = 0; i < 5; i++) det.Tick(holding, conf, 0.1f); // 0.5s of holding
            Assert(det.IsHolding && det.StanceLegIsLeft && det.HoldSeconds >= 0.45f,
                "SingleLegStance: holding accumulates HoldSeconds, right-knee-raised -> stance leg is left");

            for (int i = 0; i < 5; i++) det.Tick(neutral, conf, 0.1f); // 0.5s dip, < 0.75s grace
            Assert(det.HoldSeconds >= 0.45f, "SingleLegStance: a 0.5s dip does NOT reset HoldSeconds (grace window)");

            for (int i = 0; i < 2; i++) det.Tick(holding, conf, 0.1f); // resume holding
            Assert(det.HoldSeconds > 0.45f, "SingleLegStance: HoldSeconds resumes accumulating after a short dip");

            for (int i = 0; i < 10; i++) det.Tick(neutral, conf, 0.1f); // 1.0s dip, > 0.75s grace
            Assert(det.HoldSeconds == 0f, "SingleLegStance: a 1.0s dip DOES reset HoldSeconds");
        }

        static void TestConfidenceGating()
        {
            var det = new KneeRaiseDetector();
            det.SetBaseline(StandingSkeleton());

            var lowConf = FullConf();
            lowConf[MotionMath.LHip] = 0.1f;
            lowConf[MotionMath.RHip] = 0.1f;

            var wouldRaise = (Vector2[])StandingSkeleton().Clone();
            wouldRaise[MotionMath.LKnee] = new Vector2(wouldRaise[MotionMath.LKnee].x, 0.45f);

            int beforeCount = det.AlternatingCount;
            det.Tick(wouldRaise, lowConf, 0.1f);
            Assert(!det.JustCountedAlternating && det.AlternatingCount == beforeCount && !det.LeftUp,
                "Confidence gating: a frame with a required joint below 0.3 holds last state (no flag fires)");
        }
    }
}
