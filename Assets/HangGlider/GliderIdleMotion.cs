using UnityEngine;

namespace Kinex.HangGlider
{
    /// <summary>
    /// Purely cosmetic idle bob/sway for the hang glider VISUAL child. It layers a
    /// gentle vertical bob and a subtle pitch/roll wobble on top of whatever the
    /// parent (MainGlider, driven by PlayerMovement) is doing. It only touches this
    /// object's LOCAL transform and never reads input, so steering/banking are
    /// completely untouched. Uses unscaled time so the glider still breathes on the
    /// paused start screen (Time.timeScale == 0).
    /// </summary>
    public class GliderIdleMotion : MonoBehaviour
    {
        [Header("Vertical bob (local units)")]
        public float bobAmplitude = 0.18f;
        public float bobSpeed = 1.1f;

        [Header("Rotational sway (degrees)")]
        public float pitchAmplitude = 1.4f;
        public float pitchSpeed = 0.8f;
        public float rollAmplitude = 1.8f;
        public float rollSpeed = 0.55f;

        Vector3 _baseLocalPos;
        Quaternion _baseLocalRot;
        float _seed;

        void Awake()
        {
            _baseLocalPos = transform.localPosition;
            _baseLocalRot = transform.localRotation;
            _seed = Random.value * 10f; // desync if ever more than one glider
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime + _seed;

            float bob = Mathf.Sin(t * bobSpeed) * bobAmplitude;
            transform.localPosition = _baseLocalPos + new Vector3(0f, bob, 0f);

            float pitch = Mathf.Sin(t * pitchSpeed) * pitchAmplitude;
            float roll = Mathf.Sin(t * rollSpeed + 1.3f) * rollAmplitude;
            transform.localRotation = _baseLocalRot * Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
