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

        Quaternion _fixedRotation;

        void Start()
        {
            // Preserve the pitch the camera was authored with (looks slightly down).
            _fixedRotation = transform.rotation;
            if (target != null) transform.position = target.position + offset;
        }

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 desired = target.position + offset;
            float t = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.rotation = _fixedRotation;
        }
    }
}
