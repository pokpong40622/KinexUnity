using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Quake Escape main menu — EMBEDDED rewrite.
///
/// The original menu additively loaded the gameplay scene as a live background and
/// URP-stacked its UI camera over the gameplay camera, then panned that camera on PLAY.
/// None of that survives being embedded under Flutter (single Unity player, offscreen
/// render target): the camera stacking wiped the frame and OnExit called Application.Quit
/// which would kill the whole Flutter app. This version keeps ONLY the parts that work
/// standalone — the forced 2-slide "how to play" gate, then the logo/PLAY/EXIT menu over
/// the scene's own MenuBackground art — and:
///   • PLAY  → load the gameplay scene directly (single load, no additive/camera tricks).
///   • EXIT  → tell Flutter to close the game (SendToFlutter), never Application.Quit.
///
/// The serialized field NAMES are unchanged so the menu scene's existing wiring still binds.
public class MainMenuController : MonoBehaviour
{
    public CanvasGroup fadeGroup;       // full-screen black overlay, used for scene fade
    public CanvasGroup menuGroup;       // logo/PLAY/EXIT content (hidden while the intro gate is open)
    public CanvasGroup tutorialGroup;   // full-screen intro gate overlay root
    public GameObject introSlide1;      // "raise left/right leg" slide
    public GameObject introSlide2;      // "tiptoe" slide
    public float fadeTime = 0.4f;
    public float gateFadeTime = 0.32f;

    // The gameplay scene this menu launches. Loaded by name (must be in Build Settings).
    const string GameplayScene = "QuakeEscapeScene";

    bool busy;
    bool gateOpen;
    Coroutine gateTween;

    void Start()
    {
        if (tutorialGroup)
        {
            tutorialGroup.gameObject.SetActive(true);
            tutorialGroup.alpha = 1f;
            tutorialGroup.blocksRaycasts = true;
            tutorialGroup.interactable = true;
        }
        if (introSlide1) introSlide1.SetActive(true);
        if (introSlide2) introSlide2.SetActive(false);
        gateOpen = true;

        if (menuGroup)
        {
            menuGroup.alpha = 0f;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
        }
        if (fadeGroup)
        {
            fadeGroup.gameObject.SetActive(true);
            fadeGroup.alpha = 1f;
            fadeGroup.blocksRaycasts = true;
            StartCoroutine(Fade(fadeGroup, 1f, 0f, fadeTime, false));
        }
    }

    public void OnPlay()
    {
        if (busy || gateOpen) return;
        busy = true;
        StartCoroutine(PlayRoutine());
    }

    IEnumerator PlayRoutine()
    {
        // Kick the camera and the run off FIRST, then fade the menu over the top of them.
        //
        // These two calls used to sit after the fade — and never ran. Fade() ends with
        // SetActive(false) on the group it faded, this controller lives under menuGroup, and
        // deactivating a GameObject stops every coroutine on it *immediately*: the routine died on
        // the last line of the fade and the camera was never told to move. Ordering the side
        // effects before the teardown makes that class of bug impossible rather than merely fixed.
        var cam = Camera.main;
        if (cam != null)
        {
            var pan = cam.GetComponent<Collapse.IntroCameraPan>();
            if (pan != null) pan.Play();
        }
        // Begin the run (GameManager was held in Ready with autoStart = false). It opens on the
        // full-body framing gate, so the countdown waits for the player either way.
        if (Collapse.GameManager.Instance != null)
        {
            Collapse.GameManager.Instance.StartFromReady();
        }

        // Now fade the menu UI OUT (not to black) so the player watches the camera rotate off the
        // character's face and into the gameplay view. The menu and game share one scene now.
        if (menuGroup)
        {
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
            yield return Fade(menuGroup, menuGroup.alpha, 0f, fadeTime, false);
        }
    }

    public void OnIntroNext1()
    {
        if (!gateOpen) return;
        if (introSlide1) introSlide1.SetActive(false);
        if (introSlide2) introSlide2.SetActive(true);
    }

    public void OnIntroNext2()
    {
        if (!gateOpen) return;
        gateOpen = false;
        if (gateTween != null) StopCoroutine(gateTween);
        gateTween = StartCoroutine(CloseGate());
    }

    public void OnSkipIntro() => OnIntroNext2();

    IEnumerator CloseGate()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / gateFadeTime;
            float e = Mathf.Clamp01(t);
            if (tutorialGroup) tutorialGroup.alpha = 1f - e;
            if (menuGroup) menuGroup.alpha = e;
            yield return null;
        }
        if (tutorialGroup)
        {
            tutorialGroup.alpha = 0f;
            tutorialGroup.blocksRaycasts = false;
            tutorialGroup.interactable = false;
            tutorialGroup.gameObject.SetActive(false);
        }
        if (menuGroup)
        {
            menuGroup.alpha = 1f;
            menuGroup.interactable = true;
            menuGroup.blocksRaycasts = true;
        }
    }

    // Leave the game and return to Flutter. SendToFlutter is global-namespace and the same
    // "exit" contract the gameplay PauseController already uses.
    public void OnExit()
    {
        if (busy) return;
        SendToFlutter.Send("{\"type\":\"exit\"}");
    }

    IEnumerator Fade(CanvasGroup g, float from, float to, float time, bool block)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / time;
            g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
            yield return null;
        }
        g.alpha = to;
        g.blocksRaycasts = block;
        if (!block) g.gameObject.SetActive(false);
    }
}
