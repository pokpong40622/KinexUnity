using System.Collections;
using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// Small coroutine-based tween helpers for one-off UI/world "juice" — no tween package
    /// dependency, matching CorrectEffect's own hand-rolled Pop() coroutine style. The caller
    /// supplies the host MonoBehaviour that runs the coroutine (usually the object being tweened).
    /// </summary>
    public static class SimpleTween
    {
        /// <summary>Scale up with an out-back overshoot then settle back to the original scale.</summary>
        public static Coroutine ScalePop(Transform t, MonoBehaviour host, float scale = 1.15f, float duration = 0.25f)
        {
            if (t == null || host == null) return null;
            if (!host.isActiveAndEnabled) return null; // end state == original scale, already correct
            return host.StartCoroutine(ScalePopRoutine(t, scale, duration));
        }

        static IEnumerator ScalePopRoutine(Transform t, float scale, float duration)
        {
            Vector3 baseScale = t.localScale;
            Vector3 peak = baseScale * scale;
            float half = Mathf.Max(0.01f, duration * 0.5f);

            float time = 0f;
            while (time < half)
            {
                time += Time.deltaTime;
                float k = OutBack(Mathf.Clamp01(time / half));
                t.localScale = Vector3.LerpUnclamped(baseScale, peak, k);
                yield return null;
            }
            time = 0f;
            while (time < half)
            {
                time += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / half));
                t.localScale = Vector3.Lerp(peak, baseScale, k);
                yield return null;
            }
            t.localScale = baseScale;
        }

        static float OutBack(float k)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = k - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        /// <summary>Smoothstep-eased move of localPosition to a target.</summary>
        public static Coroutine MoveTo(Transform t, Vector3 localTarget, float duration, MonoBehaviour host)
        {
            if (t == null || host == null) return null;
            if (!host.isActiveAndEnabled) { t.localPosition = localTarget; return null; }
            return host.StartCoroutine(MoveToRoutine(t, localTarget, duration));
        }

        static IEnumerator MoveToRoutine(Transform t, Vector3 target, float duration)
        {
            Vector3 start = t.localPosition;
            duration = Mathf.Max(0.01f, duration);
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / duration));
                t.localPosition = Vector3.Lerp(start, target, k);
                yield return null;
            }
            t.localPosition = target;
        }

        /// <summary>Smoothstep-eased scalar tween — caller supplies onValue to drive whatever it's animating.</summary>
        public static Coroutine Float(float from, float to, float duration, System.Action<float> onValue, MonoBehaviour host)
        {
            if (onValue == null || host == null) return null;
            if (!host.isActiveAndEnabled) { onValue(to); return null; }
            return host.StartCoroutine(FloatRoutine(from, to, duration, onValue));
        }

        static IEnumerator FloatRoutine(float from, float to, float duration, System.Action<float> onValue)
        {
            duration = Mathf.Max(0.01f, duration);
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / duration));
                onValue(Mathf.Lerp(from, to, k));
                yield return null;
            }
            onValue(to);
        }
    }
}
