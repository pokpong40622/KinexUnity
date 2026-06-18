using UnityEngine;

namespace Kinex.App
{
    /// <summary>
    /// Open/close controller for the in-game "Camera Setup" overlay (the gear button shows it).
    /// The overlay's buttons call MediaPipePoseDetector.ToggleMirror/ToggleUpDown/ToggleArmsOnly/
    /// SensUp/SensDown and the director's Recalibrate directly — this just shows and hides the
    /// panel. Lives on the same GameObject as the game director so the UI builder can find it.
    /// </summary>
    public class CameraSetupPanel : MonoBehaviour
    {
        public GameObject panelRoot;

        public void Open()  { if (panelRoot) panelRoot.SetActive(true); }
        public void Close() { if (panelRoot) panelRoot.SetActive(false); }
    }
}
