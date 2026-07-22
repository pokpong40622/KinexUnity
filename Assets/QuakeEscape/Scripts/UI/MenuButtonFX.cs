using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Hover / press / release juice for menu buttons: scale spring + glow intensify.
public class MenuButtonFX : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.05f;
    public float pressScale = 0.94f;
    public float speed = 14f;
    public Image glow;          // soft glow image behind the button
    public float glowIdle = 0.35f;
    public float glowHover = 0.85f;

    RectTransform rt;
    Vector3 targetScale = Vector3.one;
    float glowTarget;
    bool inside;

    void Awake()
    {
        rt = (RectTransform)transform;
        glowTarget = glowIdle;
        if (glow)
        {
            var c = glow.color;
            c.a = glowIdle;
            glow.color = c;
        }
    }

    void OnEnable()
    {
        targetScale = Vector3.one;
        rt.localScale = Vector3.one;
    }

    void Update()
    {
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        rt.localScale = Vector3.Lerp(rt.localScale, targetScale, k);
        if (glow)
        {
            var c = glow.color;
            c.a = Mathf.Lerp(c.a, glowTarget, k);
            glow.color = c;
        }
    }

    public void OnPointerEnter(PointerEventData e)
    {
        inside = true;
        targetScale = Vector3.one * hoverScale;
        glowTarget = glowHover;
    }

    public void OnPointerExit(PointerEventData e)
    {
        inside = false;
        targetScale = Vector3.one;
        glowTarget = glowIdle;
    }

    public void OnPointerDown(PointerEventData e)
    {
        targetScale = Vector3.one * pressScale;
        glowTarget = glowHover;
    }

    public void OnPointerUp(PointerEventData e)
    {
        targetScale = inside ? Vector3.one * hoverScale : Vector3.one;
        glowTarget = inside ? glowHover : glowIdle;
    }
}
