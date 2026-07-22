using UnityEngine;
using UnityEngine.UI;

/// Gentle sine pulse on a UI Graphic's alpha (and optionally scale) — for glows,
/// seismic waveform decorations and glowing borders.
public class MenuPulse : MonoBehaviour
{
    public float speed = 2.2f;
    public float alphaMin = 0.5f;
    public float alphaMax = 1f;
    public float scaleAmp = 0f;
    public float phase = 0f;

    Graphic g;
    Vector3 baseScale;

    void Awake()
    {
        g = GetComponent<Graphic>();
        baseScale = transform.localScale;
    }

    void Update()
    {
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed + phase);
        if (g)
        {
            var c = g.color;
            c.a = Mathf.Lerp(alphaMin, alphaMax, t);
            g.color = c;
        }
        if (scaleAmp > 0f)
            transform.localScale = baseScale * (1f + scaleAmp * t);
    }
}
