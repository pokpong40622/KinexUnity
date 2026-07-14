using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// A tiny butterfly: two flat colored wing quads either side of a small capsule body,
    /// flapping open/closed while it drifts along a lazy sine path inside a box volume. No
    /// textures or animation clips — same runtime-primitive philosophy as PropMeshes/KinexFx.
    /// </summary>
    public static class Butterfly
    {
        public static GameObject Spawn(Vector3 center, Vector3 rangeBox, Color wingColor)
        {
            var root = new GameObject("Butterfly");
            root.transform.position = center;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            DestroySafe(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.015f, 0.03f, 0.015f);
            body.GetComponent<Renderer>().sharedMaterial = PropMeshes.Mat(new Color(0.2f, 0.15f, 0.1f));

            var wingMat = PropMeshes.MatUnlit(wingColor);
            var wingL = BuildWingQuad(root.transform, "WingL", wingMat, mirror: false);
            var wingR = BuildWingQuad(root.transform, "WingR", wingMat, mirror: true);

            var flutter = root.AddComponent<FlutterMotion>();
            flutter.wingL = wingL;
            flutter.wingR = wingR;
            flutter.rangeBox = rangeBox;
            flutter.startPos = center;
            return root;
        }

        // A single 2-triangle diamond per wing (4 verts, hinged at the body) — cheapest possible
        // "wing" silhouette, no mesh asset needed.
        static Transform BuildWingQuad(Transform parent, string name, Material mat, bool mirror)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            float sign = mirror ? -1f : 1f;
            var mesh = new Mesh { name = "Wing" };
            mesh.vertices = new[]
            {
                Vector3.zero,
                new Vector3(sign * 0.05f, 0.035f, 0.01f),
                new Vector3(sign * 0.06f, -0.01f, -0.01f),
                new Vector3(sign * 0.025f, -0.03f, 0.01f),
            };
            mesh.triangles = mirror
                ? new[] { 0, 2, 1, 0, 3, 2 }
                : new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
            return go.transform;
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        /// <summary>
        /// Sine-path drift + wing flap, both driven off Time.time. Like CloudDrift/AmbientMotes,
        /// this only animates once the ParticleSystem-free Update loop actually ticks — the
        /// builder's single-frame edit-mode screenshot won't show motion, only runtime play will.
        /// </summary>
        class FlutterMotion : MonoBehaviour
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
}
