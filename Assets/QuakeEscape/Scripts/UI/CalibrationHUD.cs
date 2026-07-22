using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace Collapse
{
    /// <summary>
    /// Calibration screen: mirrors the webcam preview, shows Thai status prompts, and lets the
    /// player capture a standing baseline with the C key (or CaptureNow() from UI).
    /// </summary>
    public class CalibrationHUD : MonoBehaviour
    {
        [SerializeField] private MediaPipePoseSource poseSource;
        [SerializeField] private RawImage preview;
        [SerializeField] private TMP_Text statusText;

        private const float ReadyMessageDurationSeconds = 2f;

        private bool previousHasBaseline;
        private float readyMessageTimeRemaining;

        private void Start()
        {
            if (statusText != null)
            {
                // bold with a dark stroke so the prompt stays readable over the webcam feed
                GameHUD.ApplyStroke(statusText);
            }

            if (preview != null)
            {
                // Mirror horizontally so the preview matches how the player sees themselves.
                preview.uvRect = new Rect(1, 0, -1, 1);
            }
        }

        private void Update()
        {
            if (poseSource != null && preview != null)
            {
                preview.texture = poseSource.PreviewTexture;
            }

            UpdateStatusText();

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
            {
                CaptureNow();
            }
        }

        private void UpdateStatusText()
        {
            if (poseSource == null || statusText == null)
            {
                return;
            }

            bool bodyVisible = poseSource.BodyVisible;
            bool hasBaseline = poseSource.HasBaseline;
            bool isTracking = poseSource.IsTracking;

            if (hasBaseline && !previousHasBaseline)
            {
                readyMessageTimeRemaining = ReadyMessageDurationSeconds;
            }
            previousHasBaseline = hasBaseline;

            if (!isTracking || !bodyVisible)
            {
                statusText.text = "ยืนให้เห็นเต็มตัวในกล้อง";
            }
            else if (!hasBaseline)
            {
                statusText.text = "ยืนตรงนิ่ง ๆ แล้วกด C";
            }
            else
            {
                if (readyMessageTimeRemaining > 0f)
                {
                    readyMessageTimeRemaining -= Time.deltaTime;
                    statusText.text = "พร้อมแล้ว!";
                }
                else
                {
                    statusText.text = "";
                }
            }
        }

        public void CaptureNow()
        {
            if (poseSource != null)
            {
                poseSource.CaptureBaseline();
            }
        }
    }
}
