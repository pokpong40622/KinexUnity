using UnityEngine;
using UnityEngine.UI;

public class CameraBackground : MonoBehaviour
{
    public WebCamTexture Webcam { get; private set; }

    void Start()
    {
        Webcam = new WebCamTexture();
        GetComponent<RawImage>().texture = Webcam;
        Webcam.Play();
    }

    void Update()
    {
        if (Webcam.width <= 16) return;
        float webcamAspect = (float)Webcam.width / Webcam.height;
        float screenAspect = (float)Screen.width / Screen.height;
        float scaleX = screenAspect / webcamAspect;
        GetComponent<RawImage>().uvRect = new Rect((1 - scaleX) / 2f, 0, scaleX, 1);
    }
}
