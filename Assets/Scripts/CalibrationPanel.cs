using UnityEngine;
using TMPro;

namespace Kinex.MegaDance
{
    /// <summary>
    /// In-game calibration popup. The gear opens it; the Calibrate button runs the detector's
    /// T-pose calibration. Shows a live countdown plus "Calibrating…" / "Calibrated!" states
    /// driven by the detector's real state. The always-on corner camera preview (separate object)
    /// is what the user watches for framing.
    /// </summary>
    public class CalibrationPanel : MonoBehaviour
    {
        [SerializeField] MediaPipePoseDetector detector;
        [SerializeField] GameObject panelRoot;   // the popup; hidden until the gear is tapped
        [SerializeField] TMP_Text statusText;
        [SerializeField] TMP_Text countdownText;

        static readonly Color Dark  = new Color32(0x33, 0x33, 0x33, 0xFF);
        static readonly Color Green = new Color32(0x2E, 0x9E, 0x2A, 0xFF);

        public void Open()  { if (panelRoot != null) panelRoot.SetActive(true); }
        public void Close() { if (panelRoot != null) panelRoot.SetActive(false); }
        public void Calibrate() { if (detector != null) detector.StartCalibration(); }

        void Update()
        {
            if (detector == null || statusText == null) return;

            var phase = detector.CalibrationPhase;
            bool busy = phase == MediaPipePoseDetector.CalibState.Prompting
                     || phase == MediaPipePoseDetector.CalibState.Counting;

            if (busy)
            {
                statusText.color = Dark;
                statusText.text = "Calibrating… hold your T-pose";
                if (countdownText != null)
                    countdownText.text = phase == MediaPipePoseDetector.CalibState.Counting
                        ? Mathf.CeilToInt(detector.CalibrationCountdown).ToString()
                        : "";
            }
            else if (detector.IsCalibrated)
            {
                statusText.color = Green;
                statusText.text = "Calibrated! ✓  Press Calibrate to redo.";
                if (countdownText != null) countdownText.text = "";
            }
            else
            {
                statusText.color = Dark;
                statusText.text = "Stand in a T-pose (full body in view), then press Calibrate.";
                if (countdownText != null) countdownText.text = "";
            }
        }
    }
}
