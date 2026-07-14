using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Builds and cleans up every in-lane prop (gates, coins, bridge/ledge/beam planks, hanging
    /// fruit, kick targets, back-kick pads) from PropMeshes primitives, parented under this
    /// factory's own transform. <see cref="DespawnAll"/> is called by the director after every
    /// beat, so individual runners never need their own cleanup bookkeeping.
    /// </summary>
    public class QuestPropFactory : MonoBehaviour
    {
        public Transform SpawnGate(int screenLane, float z)
        {
            var go = PropMeshes.GateArch(TrailScroller.LaneWidth, screenLane);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            return go.transform;
        }

        /// <summary>Dims a prop's materials to read as "faded/missed" without needing a
        /// transparent render-state switch (keeps URP/Lit materials opaque, just darker).</summary>
        public void Ghost(Transform prop)
        {
            if (prop == null) return;
            foreach (var r in prop.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterial;
                if (src == null) continue;
                var m = new Material(src);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * 0.35f);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                r.sharedMaterial = m;
            }
        }

        public Transform SpawnCoin(int screenLane, float z)
        {
            var go = PropMeshes.Coin();
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(screenLane * TrailScroller.LaneWidth, 1f, z);
            return go.transform;
        }

        /// <summary>Apple/orange/pear hanging on a short string, "arriving" toward overhead.
        /// Uses the Kenney food models (falls back to the primitive apple/orange if the model
        /// asset is missing) — real produce shape reads much better than a plain sphere.</summary>
        public Transform SpawnHangingFruit(bool apple, float z)
        {
            string name = apple ? "apple" : (Random.value < 0.5f ? "orange" : "pear");
            var go = KenneyProps.Food(name) ?? (apple ? PropMeshes.Apple() : PropMeshes.Orange());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.0f, z);
            ScaleToWorldSize(go, 0.5f);
            TintWarm(go);
            go.AddComponent<SlowSpin>();

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, new Vector3(0f, 1.2f, 0f));
            line.startWidth = line.endWidth = 0.01f;
            line.sharedMaterial = PropMeshes.Mat(new Color(0.85f, 0.85f, 0.85f));
            return go.transform;
        }

        static void ScaleToWorldSize(GameObject go, float targetSize)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (maxDim < 0.0001f) return;
            go.transform.localScale *= targetSize / maxDim;
        }

        // Emissive-free warm lift — no _EmissionColor set, just a slightly >1 base-colour tint
        // so the fruit reads warm against the cool sky without adding another bloom source.
        static void TintWarm(GameObject go)
        {
            var mpb = new MaterialPropertyBlock();
            var warm = new Color(1.08f, 0.98f, 0.85f);
            mpb.SetColor("_BaseColor", warm);
            mpb.SetColor("_Color", warm);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(mpb);
        }

        /// <summary>Slow idle spin for hanging fruit — purely cosmetic, self-contained.</summary>
        class SlowSpin : MonoBehaviour
        {
            void Update() => transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
        }

        public Transform SpawnKickTarget(int side, float z)
        {
            var go = PropMeshes.KickTarget();
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(side * TrailScroller.LaneWidth, 0f, z);
            return go.transform;
        }

        /// <summary>Low glowing disc for the BackKick beat (hip-extension "kick behind you").</summary>
        public Transform SpawnBackKickPad(int side, float z)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "BackKickPad";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(side * TrailScroller.LaneWidth, 0.05f, z);
            go.transform.localScale = new Vector3(0.35f, 0.02f, 0.35f);
            go.GetComponent<Renderer>().sharedMaterial =
                PropMeshes.Mat(new Color(0.75f, 0.35f, 0.95f), smooth: 0.7f, emission: new Color(0.5f, 0.2f, 0.7f));
            return go.transform;
        }

        /// <summary>Used for Bridge (chasm plank), TandemStand (narrow ledge — pass a short
        /// length) and BeamWalk (long beam — pass a long length). One mesh, three meanings.</summary>
        public Transform SpawnBridge(float length)
        {
            var go = PropMeshes.BridgePlank(length);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            return go.transform;
        }

        public void Despawn(Transform t)
        {
            if (t != null) Destroy(t.gameObject);
        }

        /// <summary>Destroys every prop spawned so far. Called by the director after each beat.</summary>
        public void DespawnAll()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}
