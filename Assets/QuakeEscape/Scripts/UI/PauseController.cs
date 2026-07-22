using UnityEngine;
using UnityEngine.SceneManagement;

namespace Collapse
{
    // Click-driven pause: freezes gameplay via Time.timeScale so GameManager's
    // deltaTime-based coroutines stop automatically, while UI juice (which runs
    // on unscaledDeltaTime, e.g. MenuButtonFX) keeps animating behind the popup.
    public class PauseController : MonoBehaviour
    {
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private string mainMenuScene = "MainMenu";

        private bool isPaused;

        public void OnPauseClicked()
        {
            if (isPaused) return;
            isPaused = true;
            Time.timeScale = 0f;
            if (popupRoot != null) popupRoot.SetActive(true);
        }

        public void OnResumeClicked()
        {
            if (!isPaused) return;
            isPaused = false;
            Time.timeScale = 1f;
            if (popupRoot != null) popupRoot.SetActive(false);
        }

        public void OnExitClicked()
        {
            // No MainMenu scene in the Kinex build — hand the exit to the Flutter
            // host, which owns navigation and will pop back off the game screen.
            Time.timeScale = 1f;
            SendToFlutter.Send("{\"type\":\"exit\"}");
        }
    }
}
