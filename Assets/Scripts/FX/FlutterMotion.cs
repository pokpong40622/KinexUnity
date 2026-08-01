using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// Sine-path drift + wing flap for a <see cref="Butterfly"/>, both driven off Time.time. Like
    /// CloudDrift/GentleSway, it only animates once the Update loop ticks — an edit-mode screenshot
    /// won't show motion, only runtime play will.
    ///
    /// This is a TOP-LEVEL MonoBehaviour in its own file (not nested inside Butterfly) on purpose:
    /// Unity can only serialize a MonoBehaviour into a saved scene/prefab if it has a MonoScript
    /// asset, which requires a top-level class in a matching-named file. A nested MonoBehaviour has
    /// no script asset, so a baked-into-scene butterfly would reload as a "missing script."
    /// </summary>
    public class FlutterMotion : MonoBehaviour
    {
        public Transform wingL, wingR;
        public Vector3 rangeBox = new Vector3(2f, 0.6f, 2f);
        public Vector3 startPos;

        float _phase;
        const float FlapSpeed = 9f;

        void Awake() => _phase = Random.Range(0f, Mathf.PI * 2f); // desync multiple butterflies

        void Update()
        {
            float t = Time.time + _phase;
            var offset = new Vector3(
                Mathf.Sin(t * 0.6f) * rangeBox.x,
                Mathf.Sin(t * 1.3f) * rangeBox.y * 0.5f + rangeBox.y * 0.5f,
                Mathf.Cos(t * 0.4f) * rangeBox.z);
            transform.position = startPos + offset;
            transform.rotation = Quaternion.LookRotation(
                new Vector3(Mathf.Cos(t * 0.6f) * 0.6f, 0f, -Mathf.Sin(t * 0.4f) * 0.4f + 0.1f));

            float flap = Mathf.Abs(Mathf.Sin(t * FlapSpeed)) * 60f + 10f; // 10-70 degrees open
            if (wingL != null) wingL.localRotation = Quaternion.Euler(0f, flap, 0f);
            if (wingR != null) wingR.localRotation = Quaternion.Euler(0f, -flap, 0f);
        }
    }
}
