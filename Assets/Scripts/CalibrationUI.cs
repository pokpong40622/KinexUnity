// CalibrationUI — full-screen overlay that guides the user through the T-pose calibration.
// Attach to any GameObject in the scene; it finds the MediaPipePoseDetector automatically
// and builds its own Canvas so it works without any manual prefab wiring.

using UnityEngine;
using UnityEngine.UI;

public class CalibrationUI : MonoBehaviour
{
    [Tooltip("Leave empty — auto-found at Awake.")]
    [SerializeField] MediaPipePoseDetector detector;

    GameObject _overlay;
    Text _topText;
    Text _countText;
    Text _subText;

    void Awake()
    {
        if (detector == null)
            detector = FindAnyObjectByType<MediaPipePoseDetector>();

        BuildOverlay();
        ShowOverlay(false);
    }

    void Update()
    {
        if (detector == null) return;

        switch (detector.CalibrationPhase)
        {
            case MediaPipePoseDetector.CalibState.Prompting:
                ShowOverlay(true);
                _topText.text  = "CALIBRATION";
                _countText.text = "";
                _subText.text  = "Stand in T-Pose\nArms straight out → feet together\nHold still...";
                break;

            case MediaPipePoseDetector.CalibState.Counting:
                ShowOverlay(true);
                _topText.text   = "Hold T-Pose";
                _countText.text = Mathf.CeilToInt(detector.CalibrationCountdown).ToString();
                _subText.text   = "Averaging your body shape...";
                break;

            case MediaPipePoseDetector.CalibState.Done:
                ShowOverlay(true);
                _topText.text   = detector.IsCalibrated ? "Ready!" : "Try Again";
                _countText.text = detector.IsCalibrated ? "✓" : "!";
                _subText.text   = detector.IsCalibrated
                    ? "Calibration complete"
                    : "Not enough data — stay in frame next time";
                break;

            default:
                ShowOverlay(false);
                break;
        }
    }

    void ShowOverlay(bool visible) => _overlay.SetActive(visible);

    void BuildOverlay()
    {
        // Root canvas — highest sorting order so it draws on top of everything.
        _overlay = new GameObject("CalibrationOverlay");
        _overlay.transform.SetParent(transform, false);

        var canvas = _overlay.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = _overlay.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        _overlay.AddComponent<GraphicRaycaster>();

        // Semi-transparent dark backdrop.
        var bg = MakeRect("Background", _overlay.transform);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.78f);
        Stretch(bg);

        // Center card.
        var card = MakeRect("Card", _overlay.transform);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        card.anchorMin = new Vector2(0.1f, 0.3f);
        card.anchorMax = new Vector2(0.9f, 0.7f);
        card.offsetMin = card.offsetMax = Vector2.zero;

        Font builtIn = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Top label (CALIBRATION / Hold T-Pose / Ready!).
        var topRect = MakeRect("TopText", card);
        topRect.anchorMin = new Vector2(0f, 0.65f);
        topRect.anchorMax = Vector2.one;
        topRect.offsetMin = topRect.offsetMax = Vector2.zero;
        _topText = topRect.gameObject.AddComponent<Text>();
        _topText.font      = builtIn;
        _topText.fontSize  = 52;
        _topText.fontStyle = FontStyle.Bold;
        _topText.alignment = TextAnchor.MiddleCenter;
        _topText.color     = new Color(0.3f, 0.9f, 1f);

        // Big countdown number (or checkmark).
        var countRect = MakeRect("CountText", card);
        countRect.anchorMin = new Vector2(0f, 0.3f);
        countRect.anchorMax = new Vector2(1f, 0.68f);
        countRect.offsetMin = countRect.offsetMax = Vector2.zero;
        _countText = countRect.gameObject.AddComponent<Text>();
        _countText.font      = builtIn;
        _countText.fontSize  = 110;
        _countText.fontStyle = FontStyle.Bold;
        _countText.alignment = TextAnchor.MiddleCenter;
        _countText.color     = new Color(0.2f, 1f, 0.5f);

        // Instruction subtitle.
        var subRect = MakeRect("SubText", card);
        subRect.anchorMin = Vector2.zero;
        subRect.anchorMax = new Vector2(1f, 0.32f);
        subRect.offsetMin = subRect.offsetMax = Vector2.zero;
        _subText = subRect.gameObject.AddComponent<Text>();
        _subText.font      = builtIn;
        _subText.fontSize  = 28;
        _subText.alignment = TextAnchor.MiddleCenter;
        _subText.color     = new Color(0.85f, 0.85f, 0.85f);
    }

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
}
