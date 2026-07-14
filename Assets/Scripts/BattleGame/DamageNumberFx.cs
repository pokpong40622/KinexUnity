using System.Collections;
using UnityEngine;
using TMPro;

namespace Kinex.BattleGame
{
    /// <summary>
    /// One-shot floating damage number — a world-space TMP text that rises and fades over its
    /// world position, billboarded to the given camera. Self-contained/self-destroying, same
    /// "no scene/prefab authoring" spirit as Kinex.FX.KinexFx's particle helpers.
    /// </summary>
    public static class DamageNumberFx
    {
        public static void Spawn(Vector3 worldPos, int amount, Quality tier, Camera cam)
        {
            var go = new GameObject("DamageNumber");
            go.transform.position = worldPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = amount.ToString();
            tmp.fontSize = tier == Quality.Perfect ? 11f : (tier == Quality.Good ? 9f : 7.5f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = tier == Quality.Perfect
                ? new Color(1f, 0.85f, 0.15f)
                : (tier == Quality.Good ? new Color(1f, 1f, 1f) : new Color(0.85f, 0.85f, 0.85f));
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = new Color(0.1f, 0.05f, 0.02f);
            tmp.fontStyle = FontStyles.Bold;

            var runner = go.AddComponent<DamageNumberRunner>();
            runner.Run(cam);
        }
    }

    /// <summary>Tiny MonoBehaviour host for the rise+fade coroutine — TMP components need a
    /// GameObject but DamageNumberFx.Spawn itself stays a static call site for callers.</summary>
    class DamageNumberRunner : MonoBehaviour
    {
        public void Run(Camera cam) => StartCoroutine(RiseAndFade(cam));

        IEnumerator RiseAndFade(Camera cam)
        {
            var tmp = GetComponent<TextMeshPro>();
            Vector3 start = transform.position;
            Vector3 end = start + Vector3.up * 0.7f + new Vector3(Random.Range(-0.15f, 0.15f), 0f, 0f);
            const float dur = 0.9f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                transform.position = Vector3.Lerp(start, end, k);
                if (cam != null) transform.rotation = cam.transform.rotation;
                if (tmp != null)
                {
                    var c = tmp.color;
                    c.a = 1f - Mathf.Clamp01((k - 0.5f) / 0.5f);
                    tmp.color = c;
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
