using UnityEngine;
using UnityEngine.UI;

namespace Kinex.World
{
    /// <summary>
    /// Wires the puppet's dedicated head-on camera to a UI RawImage via a RenderTexture, so the
    /// stick-figure is shown exactly like the reference web app (its own straight-on camera + clean
    /// backdrop) instead of through the room's angled game camera. Created at runtime so there's no
    /// RenderTexture asset to manage.
    /// </summary>
    public class PuppetView : MonoBehaviour
    {
        public Camera puppetCamera;   // the dedicated head-on camera (renders only the puppet)
        public RawImage targetImage;  // the on-screen panel that shows the camera
        public int width = 600;
        public int height = 800;

        RenderTexture _rt;

        void Start()
        {
            if (puppetCamera == null || targetImage == null) return;
            _rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = "PuppetViewRT" };
            _rt.Create();
            puppetCamera.targetTexture = _rt;
            targetImage.texture = _rt;
            targetImage.color = Color.white; // ensure the texture isn't tinted away
        }

        void OnDestroy()
        {
            if (puppetCamera != null) puppetCamera.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
