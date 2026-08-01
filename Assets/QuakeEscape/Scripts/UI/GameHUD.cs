using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Collapse
{
    // HUD with cartoon juice, all procedural (no Animator/DOTween assets):
    // hearts idle with a heartbeat thump and shake+flash when one is lost, the
    // prompt bounces in and blinks while a move is demanded, countdown numbers
    // punch in traffic-light colours, the victory line cycles rainbow, and the
    // timer ticks with a little pop (turning hot for the final stretch).
    public class GameHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text heartsText;
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private TMP_Text bigText;

        private static readonly Color HeartColor = new Color(1f, 0.35f, 0.45f);
        private static readonly Color TimerColor = new Color(1f, 0.93f, 0.8f);
        private static readonly Color TimerHotColor = new Color(1f, 0.32f, 0.2f);
        private static readonly Color WarnColorA = new Color(1f, 0.85f, 0.2f);
        private static readonly Color GapColorA = new Color(1f, 0.15f, 0.1f);
        private static readonly Color GapColorB = new Color(1f, 0.55f, 0.1f);

        private Vector3 timerScale, heartsScale, promptScale, bigScale;
        private int lastHearts = -1;
        private float heartHitT = 10f;    // seconds since a heart was lost
        private string lastPrompt;
        private float promptPopT = 10f;
        private string lastBig;
        private float bigPopT = 10f;
        private int lastSecond = -1;
        private float timerPopT = 10f;

        private void Awake()
        {
            timerScale = timerText.rectTransform.localScale;
            heartsScale = heartsText.rectTransform.localScale;
            promptScale = promptText.rectTransform.localScale;
            bigScale = bigText.rectTransform.localScale;

            // chunky readable text with a dark stroke so it pops off the dusk sky
            // (timer is bold but rimless by design)
            timerText.fontStyle |= FontStyles.Bold;
            ApplyStroke(heartsText);
            ApplyStroke(promptText);
            ApplyStroke(bigText);
            heartsText.color = HeartColor;

            // DEBUG HOOK: tapping the on-screen timer opens the SOS card immediately, so QA can
            // preview the whole fall-alert flow without staging a real fall. Remove if this ever
            // needs to ship without a debug trigger.
            var sosTestBtn = timerText.gameObject.GetComponent<Button>();
            if (sosTestBtn == null) sosTestBtn = timerText.gameObject.AddComponent<Button>();
            sosTestBtn.transition = Selectable.Transition.None;
            sosTestBtn.onClick.AddListener(() => GameManager.Instance?.DebugShowSos());
        }

        // The SDF outline alone can't get thicker than ~a hairline (atlas padding
        // is only 9px at 90pt sampling), so the stroke is outline + a dilated
        // zero-offset underlay behind the glyphs. UpdateShaderRatios is mandatory
        // after enabling the keyword or the glyphs render as solid boxes.
        public static void ApplyStroke(TMP_Text text)
        {
            text.fontStyle |= FontStyles.Bold;
            text.outlineColor = new Color32(10, 5, 3, 255);
            text.outlineWidth = 0.45f;
            Material mat = text.fontMaterial;
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(0.04f, 0.02f, 0.01f, 1f));
            mat.SetFloat("_UnderlayOffsetX", 0f);
            mat.SetFloat("_UnderlayOffsetY", 0f);
            mat.SetFloat("_UnderlayDilate", 0.85f);
            mat.SetFloat("_UnderlaySoftness", 0.12f);
            ShaderUtilities.UpdateShaderRatios(mat);
            text.SetMaterialDirty();
        }

        private void Update()
        {
            GameManager gameManager = GameManager.Instance;
            if (gameManager == null)
                return;

            float dt = Time.deltaTime;
            UpdateTimer(gameManager, dt);
            UpdateHearts(gameManager, dt);
            UpdatePrompt(gameManager, dt);
            UpdateBig(gameManager, dt);
        }

        private void UpdateTimer(GameManager gm, float dt)
        {
            float timeToDisplay = Mathf.Max(gm.RemainingTime, 0f);
            int minutes = (int)(timeToDisplay / 60f);
            int seconds = (int)(timeToDisplay % 60f);
            timerText.text = $"{minutes}:{seconds:D2}";

            if (seconds != lastSecond)
            {
                lastSecond = seconds;
                timerPopT = 0f;
            }
            timerPopT += dt;
            float pop = 1f + 0.18f * Cool(timerPopT / 0.22f);
            timerText.rectTransform.localScale = timerScale * pop;

            // final stretch: the countdown to survival glows hot and pulses
            bool playing = gm.Phase == GamePhase.Warning
                || gm.Phase == GamePhase.Gap || gm.Phase == GamePhase.Rest;
            if (playing && timeToDisplay <= 15f)
            {
                float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
                timerText.color = Color.Lerp(TimerHotColor, Color.white, blink * 0.4f);
            }
            else
            {
                timerText.color = TimerColor;
            }
        }

        private void UpdateHearts(GameManager gm, float dt)
        {
            int hearts = gm.Hearts;
            heartsText.text = hearts <= 0 ? "" : new string('♥', hearts);
            if (lastHearts >= 0 && hearts < lastHearts)
            {
                heartHitT = 0f;
            }
            lastHearts = hearts;
            heartHitT += dt;

            // idle double-thump heartbeat (sharpened sine so it beats, not sways)
            float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * 4f)), 8f);
            float scale = 1f + 0.1f * beat;
            float angle = 0f;
            Color color = HeartColor;
            if (heartHitT < 0.6f)
            {
                // ouch: swell + rattle + white flash, all decaying together
                float k = 1f - heartHitT / 0.6f;
                scale += 0.55f * k * k;
                angle = Mathf.Sin(heartHitT * 55f) * 14f * k;
                color = Color.Lerp(HeartColor, Color.white, k * k);
            }
            heartsText.rectTransform.localScale = heartsScale * scale;
            heartsText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            heartsText.color = color;
        }

        private void UpdatePrompt(GameManager gm, float dt)
        {
            promptText.text = gm.Prompt;
            if (gm.Prompt != lastPrompt)
            {
                lastPrompt = gm.Prompt;
                if (!string.IsNullOrEmpty(gm.Prompt))
                {
                    promptPopT = 0f;
                }
            }
            promptPopT += dt;
            // bouncy pop-in with overshoot, then phase-driven attitude
            float scale = OutBack(Mathf.Clamp01(promptPopT / 0.35f));
            float angle = 0f;
            if (gm.Phase == GamePhase.Warning)
            {
                // "get ready!" — bob gently and blink yellow/white
                scale *= 1f + 0.05f * Mathf.Sin(Time.time * 7f);
                float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
                promptText.color = Color.Lerp(WarnColorA, Color.white, blink);
            }
            else if (gm.Phase == GamePhase.Gap)
            {
                // "NOW!" — urgent red strobe and a frantic little wiggle
                scale *= 1f + 0.08f * Mathf.Sin(Time.time * 14f);
                angle = Mathf.Sin(Time.time * 18f) * 3f;
                float strobe = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
                promptText.color = Color.Lerp(GapColorA, GapColorB, strobe);
            }
            else
            {
                promptText.color = Color.white;
            }
            promptText.rectTransform.localScale = promptScale * scale;
            promptText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void UpdateBig(GameManager gm, float dt)
        {
            string big;
            if (gm.Phase == GamePhase.Countdown)
            {
                big = Mathf.Ceil(gm.PhaseTimeLeft).ToString();
            }
            else if (gm.Phase == GamePhase.Victory)
            {
                big = "คุณรอดแล้ว!";
            }
            else if (gm.Phase == GamePhase.GameOver)
            {
                big = "จบเกม";
            }
            else
            {
                big = "";
            }
            bigText.text = big;
            if (big != lastBig)
            {
                lastBig = big;
                if (!string.IsNullOrEmpty(big))
                {
                    bigPopT = 0f;
                }
            }
            bigPopT += dt;

            float scale;
            float angle = 0f;
            if (gm.Phase == GamePhase.Countdown)
            {
                // each number slams in huge then settles; traffic-light colours
                scale = Mathf.Lerp(2.2f, 1f, OutCubic(Mathf.Clamp01(bigPopT / 0.4f)));
                int n = (int)Mathf.Ceil(gm.PhaseTimeLeft);
                bigText.color = n >= 3 ? WarnColorA
                    : n == 2 ? GapColorB
                    : GapColorA;
            }
            else if (gm.Phase == GamePhase.Victory)
            {
                // party mode: rainbow cycle + happy bounce
                scale = OutBack(Mathf.Clamp01(bigPopT / 0.45f))
                    * (1f + 0.07f * Mathf.Abs(Mathf.Sin(Time.time * 5f)));
                angle = Mathf.Sin(Time.time * 3f) * 4f;
                bigText.color = Color.HSVToRGB(
                    Mathf.Repeat(Time.time * 0.6f, 1f), 0.55f, 1f);
            }
            else if (gm.Phase == GamePhase.GameOver)
            {
                // sad trombone: drops in, hangs at a defeated tilt
                scale = OutBack(Mathf.Clamp01(bigPopT / 0.5f));
                angle = -6f + Mathf.Sin(Time.time * 1.5f) * 1.5f;
                bigText.color = new Color(0.85f, 0.25f, 0.2f);
            }
            else
            {
                scale = 1f;
                bigText.color = Color.white;
            }
            bigText.rectTransform.localScale = bigScale * scale;
            bigText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // eases: 0→1 with overshoot (back) / plain decelerate / one-shot bump
        private static float OutBack(float t)
        {
            const float c1 = 2.3f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        private static float OutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
        }

        // 1→0 bump used for the timer tick pop
        private static float Cool(float t)
        {
            return Mathf.Pow(1f - Mathf.Clamp01(t), 2f);
        }
    }
}
