using UnityEngine;

namespace Collapse
{
    public class GameAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip warningClip;
        [SerializeField] private AudioClip collapseClip;
        [SerializeField] private AudioClip heartLossClip;
        [SerializeField] private AudioClip surviveClip;
        [SerializeField] private AudioClip winClip;

        [Header("Thai warning voice lines")]
        [SerializeField] private AudioClip warningVoiceLeftClip;
        [SerializeField] private AudioClip warningVoiceRightClip;
        [SerializeField] private AudioClip warningVoiceBothClip;
        [SerializeField] private AudioClip surviveVoiceClip;

        private AudioSource source;
        private GamePhase lastPhase = GamePhase.Ready;
        private int lastHearts = -1;
        private int heartsAtGapStart = -1;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            if (lastHearts < 0)
            {
                lastHearts = gm.Hearts;
            }

            if (gm.Hearts < lastHearts)
            {
                Play(heartLossClip);
            }

            if (gm.Phase != lastPhase)
            {
                switch (gm.Phase)
                {
                    case GamePhase.Warning:
                        Play(warningClip, 0.6f);
                        Play(VoiceClipFor(gm.CurrentWarning), 1.3f);
                        break;
                    case GamePhase.Gap:
                        Play(collapseClip);
                        heartsAtGapStart = gm.Hearts;
                        break;
                    case GamePhase.Rest:
                        if (lastPhase == GamePhase.Gap && gm.Hearts == heartsAtGapStart)
                        {
                            Play(surviveClip);
                            Play(surviveVoiceClip, 1.3f);
                        }
                        break;
                    case GamePhase.Victory:
                        Play(winClip);
                        break;
                }
            }

            lastPhase = gm.Phase;
            lastHearts = gm.Hearts;
        }

        private void Play(AudioClip clip, float volumeScale = 1f)
        {
            if (clip != null)
            {
                source.PlayOneShot(clip, volumeScale);
            }
        }

        private AudioClip VoiceClipFor(WarningSide side)
        {
            switch (side)
            {
                case WarningSide.Left:
                    return warningVoiceLeftClip;
                case WarningSide.Right:
                    return warningVoiceRightClip;
                case WarningSide.Both:
                    return warningVoiceBothClip;
                default:
                    return null;
            }
        }
    }
}
