using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Kinex.Motion;

namespace Collapse
{
    /// <summary>
    /// "Stand fully in frame" popup for Quake Escape — the SAME system The Dasher uses, recoloured to
    /// this game's theme. It runs the shared <see cref="FullBodyGate"/> over the camera/AI landmarks
    /// and shows a card with FIVE body-part lights (หัว ไหล่ สะโพก เข่า เท้า) that turn GREEN when that
    /// group is in frame and RED when it isn't, a specific Thai instruction ("step back — we can't see
    /// your feet"), and a hold-ring. The card is authored in the scene from picture assets and wired to
    /// this component; it appears when the body is lost and hides once the whole body is visible again.
    /// Suppressed on victory / game-over.
    /// </summary>
    public class OutOfFrameOverlay : MonoBehaviour
    {
        [Header("Wiring (assigned by the scene builder)")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Image[] chips = new Image[5];   // Head, Shoulders, Hips, Knees, Feet
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private Image holdRingFill;
        [SerializeField] private MediaPipePoseDetector detector; // auto-found if empty

        [Header("Timing")]
        [SerializeField] private float showDelay = 0.5f;
        [SerializeField] private float hideDelay = 0.4f;
        [SerializeField] private float holdSeconds = 1.0f;

        // Red / green body-part lights (the point of the popup).
        static readonly Color Green = new Color(0.30f, 0.80f, 0.35f, 1f);
        static readonly Color Red   = new Color(0.93f, 0.26f, 0.22f, 1f);

        readonly FullBodyGate _gate = new FullBodyGate();
        bool _shown;

        /// <summary>Whole body currently in frame. GameManager gates the countdown on this.</summary>
        public bool FullBodyOk => _gate.IsFullBodyVisible;

        void Start()
        {
            if (detector == null) detector = Object.FindFirstObjectByType<MediaPipePoseDetector>();
            SetShown(false);
        }

        void Update()
        {
            if (panel == null) return;
            if (detector == null) detector = Object.FindFirstObjectByType<MediaPipePoseDetector>();

            var gm = GameManager.Instance;
            // Not while the menu is up (Ready) or after the run ends. During Framing the card is
            // the whole point — it is what tells the player how to stand before the timer starts —
            // so it shows there immediately, without waiting out showDelay.
            bool framing = gm != null && gm.Phase == GamePhase.Framing;
            bool suppress = gm != null && (gm.Phase == GamePhase.Ready ||
                            gm.Phase == GamePhase.Victory || gm.Phase == GamePhase.GameOver);

            var lm = detector != null ? detector.Landmarks33 : null;
            bool hasPose = detector != null && detector.HasPose;
            _gate.Tick(hasPose, lm, Time.unscaledDeltaTime);

            for (int i = 0; i < chips.Length && i < _gate.GroupOk.Length; i++)
                if (chips[i] != null) chips[i].color = _gate.GroupOk[i] ? Green : Red;

            if (promptText != null) promptText.text = PromptFor(_gate.Why);
            if (holdRingFill != null)
                holdRingFill.fillAmount = Mathf.Clamp01(_gate.ValidSeconds / Mathf.Max(0.01f, holdSeconds));

            if (suppress) { SetShown(false); return; }

            if (framing)
            {
                // Stay up for the whole gate: the player needs the body-part lights to see WHICH
                // part is still out of frame, and it hides only once GameManager has started.
                SetShown(true);
                return;
            }

            if (_gate.IsFullBodyVisible)
            {
                if (_shown && _gate.ValidSeconds >= hideDelay) SetShown(false);
            }
            else
            {
                if (!_shown && _gate.InvalidSeconds >= showDelay) SetShown(true);
            }
        }

        void SetShown(bool visible)
        {
            _shown = visible;
            if (panel != null && panel.activeSelf != visible) panel.SetActive(visible);
        }

        static string PromptFor(FullBodyGate.Reason why)
        {
            switch (why)
            {
                case FullBodyGate.Reason.Ok:          return "ดีมาก! พร้อมแล้ว";
                case FullBodyGate.Reason.NotFound:    return "ยังไม่เห็นตัวคุณ — มายืนหน้ากล้อง";
                case FullBodyGate.Reason.FeetCut:     return "ถอยหลังอีกนิด — มองไม่เห็นเท้า";
                case FullBodyGate.Reason.HeadCut:     return "ถอยหลังอีกนิด — มองไม่เห็นศีรษะ";
                case FullBodyGate.Reason.TooClose:    return "ถอยหลังอีกนิด ให้เห็นทั้งตัว";
                case FullBodyGate.Reason.TooFar:      return "เดินเข้ามาใกล้อีกนิด";
                case FullBodyGate.Reason.OffCenter:   return "ขยับมายืนตรงกลางภาพ";
                default:                               return "มีบางส่วนถูกบัง — ยืนให้เห็นทั้งตัว";
            }
        }
    }
}
