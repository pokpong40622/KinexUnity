using UnityEngine;

namespace Collapse
{
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake Instance { get; private set; }

        [SerializeField] private float warningAmplitude = 0.015f;
        [SerializeField] private float collapseAmplitude = 0.08f;

        private Vector3 basePosition;
        private float amplitude;
        private float duration = 1f;
        private float timeLeft;
        private GamePhase lastPhase = GamePhase.Ready;

        private void Awake()
        {
            Instance = this;
            basePosition = transform.localPosition;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Shake(float shakeAmplitude, float shakeDuration)
        {
            amplitude = Mathf.Max(amplitude, shakeAmplitude);
            duration = Mathf.Max(shakeDuration, 0.01f);
            timeLeft = Mathf.Max(timeLeft, shakeDuration);
        }

        private void LateUpdate()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.Phase != lastPhase)
            {
                switch (gm.Phase)
                {
                    case GamePhase.Warning:
                        Shake(warningAmplitude, 0.6f);
                        break;
                    case GamePhase.Gap:
                        Shake(collapseAmplitude, 1.1f);
                        break;
                }
                lastPhase = gm.Phase;
            }

            if (timeLeft <= 0f)
            {
                transform.localPosition = basePosition;
                amplitude = 0f;
                return;
            }

            timeLeft -= Time.deltaTime;
            float falloff = Mathf.Clamp01(timeLeft / duration);
            float a = amplitude * falloff * falloff;
            float t = Time.time * 28f;
            Vector3 offset = new Vector3(
                Mathf.PerlinNoise(t, 0.3f) - 0.5f,
                Mathf.PerlinNoise(0.7f, t) - 0.5f,
                0f) * (2f * a);
            transform.localPosition = basePosition + offset;
        }
    }
}
