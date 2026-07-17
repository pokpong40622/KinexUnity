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

    [Header("Landscape steering (rotate the tablet like a steering wheel)")]
    [Tooltip("Degrees of tablet ROLL that map to full left/right steer. Smaller = twitchier, " +
             "bigger = you must tilt more. ~25 is a comfortable wrist roll.")]
    public float fullSteerRollDegrees = 25f;
    [Tooltip("Ignore rolls smaller than this (deg) so holding level flies dead straight.")]
    public float steerDeadzoneDegrees = 3f;
    [Tooltip("Flip if tilting one way steers the other way.")]
    public float steerSign = 1f;

    // Calibrated neutral roll: the gravity-in-screen-plane angle captured the moment the run
    // starts, so the natural way the player is holding the tablet becomes "straight ahead". Steering
    // is the DELTA from this neutral — which removes the constant gravity bias that made the glider
    // drift to one side when we read a raw accelerometer axis directly.
    float _neutralRoll;
    bool _rollCalibrated;
    float _lastSteer; // held when the tablet is briefly too flat to read a stable roll

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
    private float _startUnscaledTime; // wall-clock start, for the result payload duration

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
        _startUnscaledTime = Time.unscaledTime;
        // Capture "straight ahead" from however the player is holding the tablet right now, so the
        // glider starts level instead of drifting toward one side.
        _rollCalibrated = false;
    }

    // Current tablet ROLL in degrees: the direction of gravity within the screen plane (device
    // X/Y). Rotating the tablet like a steering wheel sweeps this angle; pitching it toward/away
    // barely changes it. Returns false if the tablet is too flat to read a stable roll.
    bool TryReadRoll(out float rollDeg)
    {
        rollDeg = 0f;
        var acc = Accelerometer.current;
        if (acc == null) return false;
        Vector3 g = acc.acceleration.ReadValue();
        // In-plane gravity magnitude: near zero when the tablet lies flat (screen up), where roll is
        // undefined/noisy — hold the last steer in that case instead of jittering.
        if (g.x * g.x + g.y * g.y < 0.04f) return false;
        rollDeg = Mathf.Atan2(g.x, g.y) * Mathf.Rad2Deg;
        return true;
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

        // --- 2. TABLET ROLL (device) ---
        // Steer by how far the tablet is ROLLED from the neutral captured at StartGame — a delta,
        // not a raw axis, so there's no constant gravity bias pulling the glider to one side.
        float sensorX = _lastSteer;
        if (TryReadRoll(out float roll))
        {
            if (!_rollCalibrated) { _neutralRoll = roll; _rollCalibrated = true; }
            float delta = Mathf.DeltaAngle(_neutralRoll, roll);
            if (Mathf.Abs(delta) < steerDeadzoneDegrees) delta = 0f;
            sensorX = steerSign * Mathf.Clamp(delta / Mathf.Max(fullSteerRollDegrees, 1f), -1f, 1f);
            _lastSteer = sensorX;
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

        // Report the outcome to the Flutter host so the game gets logged to history
        // (same JSON bridge pattern as the other games). Editor SendToFlutter just logs.
        float duration = Mathf.Max(0f, Time.unscaledTime - _startUnscaledTime);
        string msg = "{\"type\":\"hangglider_result\",\"score\":" + currentScore +
                     ",\"total\":" + questionList.Length +
                     ",\"durationSeconds\":" +
                     duration.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}";
        SendToFlutter.Send(msg);
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