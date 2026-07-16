#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Kinex.AstroStance.EditorTools
{
    /// <summary>
    /// One-shot regression runner for the AstroStance work: runs the new AstroStance logic/scene
    /// self-test and real-feed replay, plus the motion + every existing game self-test, in a
    /// single batch invocation. AstroStance touched shared code (MediaPipePoseDetector's
    /// sameSideRetarget flag, SitStandDetector's new estimate overload), so this guards against
    /// regressions in the other games. Each self-test logs "ALL PASS (n)" or a line containing
    /// "SELFTEST FAIL" / "FAIL" on error; we capture the log stream and exit non-zero if any
    /// error surfaced.
    /// Batch: -executeMethod Kinex.AstroStance.EditorTools.AstroRegression.RunBatch
    /// </summary>
    public static class AstroRegression
    {
        static int _errors;

        public static void RunBatch()
        {
            Application.logMessageReceived += OnLog;
            try
            {
                Run("AstroStance", AstroStanceSelfTest.Run);
                Run("AstroFeed", AstroFeedReplayTest.Run);
                Run("Motion", Kinex.Motion.EditorTools.MotionSelfTest.Run);
                Run("MotionLab", Kinex.MotionLabEditor.MotionLabSelfTest.Run);
                Run("TempleHunt", Kinex.TempleHunt.EditorTools.TempleHuntSelfTest.Run);
                Run("DanceStar", Kinex.DanceStar.DanceStarSelfTest.Run);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            if (_errors == 0) Debug.Log("ASTRO REGRESSION: ALL SUITES PASS");
            else Debug.LogError($"ASTRO REGRESSION FAIL — {_errors} error log(s) across suites");
            EditorApplication.Exit(_errors == 0 ? 0 : 1);
        }

        static void Run(string label, System.Action test)
        {
            Debug.Log($"===== regression: {label} =====");
            try { test(); }
            catch (System.Exception e) { _errors++; Debug.LogError($"[{label}] threw: {e}"); }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            // Any error/exception log (self-tests use Debug.LogError with "SELFTEST FAIL") counts.
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _errors++;
        }
    }
}
#endif
