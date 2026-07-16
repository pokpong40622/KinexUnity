using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float forwardSpeed = 10f;
    public float steeringSpeed = 10f;
    public float maxTiltAngle = 15;
    public float tiltSpeed = 3f;

    [Header("Sensor Settings")]
    [Range(1f, 5f)]
    public float sensorSensitivity = 2.0f; // Higher = more sensitive steering

    [Header("Landscape steering (tune on device)")]
    [Tooltip("Which raw accelerometer axis maps to LEFT/RIGHT tilt. The game plays " +
             "in LANDSCAPE, so left/right roll is usually the Y axis (not X as in portrait).")]
    public SteerAxis steerAxis = SteerAxis.Y;
    [Tooltip("Flip if tilting one way steers the other way.")]
    public float steerSign = 1f;

    public enum SteerAxis { X, Y, Z }

    [Header("HUD / UI Settings")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI questionText;

    [Header("UI Canvases")]
    public GameObject startCanvas;
    public GameObject questionCanvas;
    public GameObject gameoverCanvas;

    [Header("Game Over Specifics")]
    public TextMeshProUGUI finalScoreText;

    [Header("Quiz Content")]
    public string[] questionList; 
    public GameObject[] questionGateFolders;

    private int currentScore = 0;
    private int questionIndex = 0;

    void Start()
    {
        // The new Input System keeps sensors disabled until asked. Enable the
        // accelerometer so tilt steering works on device.
        if (Accelerometer.current != null)
            InputSystem.EnableDevice(Accelerometer.current);

        if (startCanvas != null) startCanvas.SetActive(true);
        if (questionCanvas != null) questionCanvas.SetActive(false);
        if (gameoverCanvas != null) gameoverCanvas.SetActive(false);

        Time.timeScale = 0;

        if (questionList.Length > 0) questionText.text = questionList[0];

        for (int i = 0; i < questionGateFolders.Length; i++)
        {
            if (questionGateFolders[i] != null)
                questionGateFolders[i].SetActive(i == 0);
        }
    }

    public void StartGame()
    {
        startCanvas.SetActive(false);
        questionCanvas.SetActive(true);
        Time.timeScale = 1;
    }

    public void RestartGame() { SceneManager.LoadScene(SceneManager.GetActiveScene().name); }

    // Embedded in the Kinex/Flutter app: "exit" returns to the Flutter home screen
    // (the same message the other games send) instead of quitting the whole app.
    public void ExitGame() { SendToFlutter.Send("{\"type\":\"exit\"}"); }

    void Update()
    {
        // --- 1. KEYBOARD (editor testing only) ---
        float keyboardX = 0f;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) keyboardX -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) keyboardX += 1f;
        }

        // --- 2. ACCELEROMETER TILT (device) ---
        // New Input System gives RAW device axes (not rotated for screen orientation),
        // so the left/right axis is chosen explicitly via steerAxis for landscape.
        float sensorX = 0f;
        var acc = Accelerometer.current;
        if (acc != null)
        {
            Vector3 a = acc.acceleration.ReadValue();
            float raw = steerAxis == SteerAxis.X ? a.x : (steerAxis == SteerAxis.Y ? a.y : a.z);
            sensorX = steerSign * raw * sensorSensitivity;
        }

        // --- 3. COMBINE INPUTS ---
        float moveX = Mathf.Clamp(keyboardX + sensorX, -1f, 1f);
        
        // Locked Y: We ignore all vertical input so the glider stays level
        float moveY = 0f; 

        // --- 4. APPLY TILT (VISUALS) ---
        float targetTilt = -moveX * maxTiltAngle;
        Quaternion targetRotation = Quaternion.Euler(0, 0, targetTilt);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * tiltSpeed);

        // --- 5. APPLY MOVEMENT (PHYSICS) ---
        transform.Translate(Vector3.forward * forwardSpeed * Time.deltaTime, Space.World);
        
        // Now it only steers on the X axis
        Vector3 steering = new Vector3(moveX, moveY, 0);
        transform.Translate(steering * steeringSpeed * Time.deltaTime, Space.World);
    }

    void ShowFinalResults()
    {
        Time.timeScale = 0;
        if (gameoverCanvas != null) gameoverCanvas.SetActive(true);
        if (questionCanvas != null) questionCanvas.SetActive(false);

        if (finalScoreText != null)
        {
            finalScoreText.text = currentScore.ToString() + " / " + questionList.Length.ToString();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Correct") || other.CompareTag("Wrong"))
        {
            if (other.CompareTag("Correct"))
            {
                currentScore += 1;
                if (scoreText != null) scoreText.text = currentScore.ToString();
            }

            if (questionIndex < questionGateFolders.Length && questionGateFolders[questionIndex] != null)
                questionGateFolders[questionIndex].SetActive(false);

            questionIndex++;

            if (questionIndex < questionList.Length)
            {
                questionText.text = questionList[questionIndex];
                if (questionIndex < questionGateFolders.Length && questionGateFolders[questionIndex] != null)
                    questionGateFolders[questionIndex].SetActive(true);
            }
            else
            {
                ShowFinalResults();
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Mountain") || collision.gameObject.name.Contains("mountain"))
        {
            RestartGame();
        }
    }
}