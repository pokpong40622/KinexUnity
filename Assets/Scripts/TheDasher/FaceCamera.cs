using UnityEngine;

namespace Kinex.TheDasher
{
    /// <summary>
    /// Keeps a world-space label readable from the main camera. Orienting +Z away from the camera
    /// (LookRotation of camera→object) puts the text's readable face toward the viewer without the
    /// left-right mirroring a fixed 180° yaw produces on a behind-the-object camera.
    /// </summary>
    public class FaceCamera : MonoBehaviour
    {
        Camera _cam;

        void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.transform.position, Vector3.up);
        }
    }
}
