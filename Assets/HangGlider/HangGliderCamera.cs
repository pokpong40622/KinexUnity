using UnityEngine;

namespace Kinex.HangGlider
{
    /// <summary>
    /// Plain follow-camera for the Hang Glider game (replaces the original
    /// Cinemachine rig, which the main Kinex project doesn't include). Keeps a
    /// fixed world-space offset behind/above the glider and a fixed authored
    /// pitch, so the glider stays centred while banking left/right without the
    /// camera rolling with it.
    /// </summary>
    public class HangGliderCamera : MonoBehaviour
    {
        [Tooltip("The glider to follow (MainGlider).")]
        public Transform target;

        [Tooltip("World-space offset from the target: X centred, Y above, Z behind.")]
        public Vector3 offset = new Vector3(0f, 8f, -26f);

        [Tooltip("Higher = snappier follow. Frame-rate independent.")]
        public float followSmooth = 8f;

        [Header("Speed feel")]
        [Tooltip("Base field of view. The camera widens toward baseFov + fovPush as the glider " +
                 "reaches full speed, which is what actually sells velocity — the scenery is too " +
                 "far away for translation alone to read as fast.")]
        public float baseFov = 60f;
        public float fovPush = 8f;
        [Tooltip("Speed (units/sec) at which the FOV push is fully applied.")]
        public float fovFullSpeed = 12f;
        public float fovSmooth = 2.5f;

        [Header("Bank lean")]
        [Tooltip("How much of the glider's own roll the camera echoes. Small — the camera leaning " +
                 "as hard as the glider makes the horizon swing and reads as nausea, not speed.")]
        [Range(0f, 0.5f)] public float bankFollow = 0.18f;
        public float bankSmooth = 4f;

        [Header("Shake")]
        [Tooltip("How fast a shake decays back to zero (higher = snappier).")]
        public float shakeDecay = 3.5f;
        [Tooltip("Positional shake per unit of trauma.")]
        public float shakeAmount = 0.6f;

        Quaternion _fixedRotation;
        Camera _cam;
        Vector3 _lastTargetPos;
        float _speed;      // smoothed forward speed
        float _bank;       // smoothed camera roll
        float _trauma;     // 0..1, decays every frame
        float _shakeSeed;

        void Start()
        {
            // Preserve the pitch the camera was authored with (looks slightly down).
            _fixedRotation = transform.rotation;
            _cam = GetComponent<Camera>();
            if (_cam != null) baseFov = _cam.fieldOfView;
            if (target != null)
            {
                transform.position = target.position + offset;
                _lastTargetPos = target.position;
            }
            _shakeSeed = Random.value * 100f;
        }

        /// <summary>Kick the camera. 0.3 = a gate whooshing past, 1 = hitting a mountain.
        /// Additive and clamped, so several hits in a row don't compound into a seizure.</summary>
        public void Shake(float trauma)
        {
            _trauma = Mathf.Clamp01(_trauma + trauma);
        }

        void LateUpdate()
        {
            if (target == null) return;

            // --- follow position (unchanged behaviour) ---
            Vector3 desired = target.position + offset;
            float t = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            // --- speed -> FOV ---
            float instantSpeed = (target.position - _lastTargetPos).magnitude / dt;
            _lastTargetPos = target.position;
            _speed = Mathf.Lerp(_speed, instantSpeed, 1f - Mathf.Exp(-fovSmooth * dt));
            if (_cam != null)
            {
                float k = Mathf.Clamp01(_speed / Mathf.Max(fovFullSpeed, 0.01f));
                _cam.fieldOfView = baseFov + fovPush * k;
            }

            // --- bank lean: echo a fraction of the glider's roll ---
            float targetRoll = -target.eulerAngles.z;
            if (targetRoll > 180f) targetRoll -= 360f;
            if (targetRoll < -180f) targetRoll += 360f;
            _bank = Mathf.Lerp(_bank, targetRoll * bankFollow, 1f - Mathf.Exp(-bankSmooth * dt));

            // --- shake ---
            _trauma = Mathf.Max(0f, _trauma - shakeDecay * dt);
            Vector3 shakeOffset = Vector3.zero;
            float shakeRoll = 0f;
            if (_trauma > 0f)
            {
                // Squared trauma so small kicks stay subtle and only a real crash is violent.
                float s = _trauma * _trauma;
                float ts = Time.unscaledTime * 28f + _shakeSeed;
                shakeOffset = new Vector3(
                    (Mathf.PerlinNoise(ts, 0f) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(0f, ts) - 0.5f) * 2f,
                    0f) * (s * shakeAmount);
                shakeRoll = (Mathf.PerlinNoise(ts, ts) - 0.5f) * 2f * s * 3f;
            }

            transform.position += shakeOffset;
            transform.rotation = _fixedRotation * Quaternion.Euler(0f, 0f, _bank + shakeRoll);
        }
    }
}
