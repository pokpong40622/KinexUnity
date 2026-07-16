using System.Collections.Generic;
using UnityEngine;

namespace Kinex.TheDasher
{
    /// <summary>
    /// Gently drifts the backdrop clouds sideways and wraps them around, so the
    /// sky feels alive instead of static. Picks up every child of DasherStage
    /// whose name starts with "Cloud" at Start (including runtime-added ones).
    /// </summary>
    public class DasherSkyDrift : MonoBehaviour
    {
        [Tooltip("Sideways drift speed (metres/sec). Small = calm.")]
        public float speed = 0.35f;
        [Tooltip("When a cloud drifts past maxX it wraps back to minX.")]
        public float wrapMinX = -45f;
        public float wrapMaxX = 45f;

        readonly List<Transform> _clouds = new List<Transform>();

        void Start()
        {
            var stage = GameObject.Find("DasherStage");
            if (stage == null) return;
            foreach (Transform child in stage.transform)
                if (child.name.StartsWith("Cloud")) _clouds.Add(child);
        }

        void Update()
        {
            float dx = speed * Time.deltaTime;
            for (int i = 0; i < _clouds.Count; i++)
            {
                var c = _clouds[i];
                if (c == null) continue;
                var p = c.position;
                p.x += dx;
                if (p.x > wrapMaxX) p.x = wrapMinX;
                c.position = p;
            }
        }
    }
}
