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

    [Header("Hand-board steering (preferred — the Kinex-Hand MPU6050)")]
    [Tooltip("Degrees of hand PITCH that map to full left/right steer. The board sends " +
             "TILT:x,y,z and Flutter forwards y here; positive = steer right.")]
    public float handFullSteerDegrees = 30f;
    [Tooltip("Ignore hand tilts smaller than this (deg) so a level hand flies dead straight.")]
    public float handDeadzoneDegrees = 2f;
    [Tooltip("Flip to -1 if tilting the hand one way steers the glider the other way.")]
    // -1 because the MPU6050 is mounted upside-down on the hand board, so its
    // pitch axis reads inverted. Fixing it here rather than in firmware keeps the
    // board's TILT: output raw and consistent for every other consumer.
    public float handSign = -1f;
    [Tooltip("If no hand sample arrives for this long, fall back to tablet tilt. Without this the " +
             "last value would stick and keep steering after the board disconnects.")]
    public float handStaleSeconds = 1f;
    [Tooltip("Higher = snappier. The board streams at ~30 Hz into a 60 fps game, so the raw value " +
             "steps visibly; this smooths between samples.")]
    public float handSmoothing = 15f;

    float _handSteer; // smoothed hand steer, carried between frames

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
    private bool _crashed;            // guard: a crash restart is already in flight
    Kinex.HangGlider.HangGliderCamera _camera; // for shake kicks; null-checked everywhere

    void Start()
    {
        _camera = Camera.main != null
            ? Camera.main.GetComponent<Kinex.HangGlider.HangGliderCamera>()
            : null;

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
        _crashed = false;
        Kinex.Sfx.Play("go");
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

        // --- 2. STEERING SENSOR ---
        // Preferred: the Kinex-Hand board. Flutter forwards its pitch here (see
        // SceneRouter.SetHandTilt). Absolute, not a delta — the board reads pitch straight off
        // gravity, so "level hand" really is straight ahead with no calibration step.
        float sensorX;
        bool handLive = Kinex.App.SceneRouter.HandTiltStamp > 0f &&
                        Time.unscaledTime - Kinex.App.SceneRouter.HandTiltStamp < handStaleSeconds;

        if (handLive)
        {
            float deg = Kinex.App.SceneRouter.HandTiltY;
            if (Mathf.Abs(deg) < handDeadzoneDegrees) deg = 0f;
            float target = handSign *
                Mathf.Clamp(deg / Mathf.Max(handFullSteerDegrees, 1f), -1f, 1f);
            _handSteer = Mathf.Lerp(_handSteer, target,
                1f - Mathf.Exp(-handSmoothing * Time.deltaTime));
            sensorX = _handSteer;
            // Re-arm the tablet path, so if the board drops out mid-run the fallback recaptures
            // neutral from however the tablet is being held rather than snapping to a stale one.
            _rollCalibrated = false;
        }
        else
        {
            // Fallback: TABLET ROLL. Steer by how far the tablet is ROLLED from the neutral
            // captured at StartGame — a delta, not a raw axis, so there's no constant gravity bias
            // pulling the glider to one side.
            sensorX = _lastSteer;
            if (TryReadRoll(out float roll))
            {
                if (!_rollCalibrated) { _neutralRoll = roll; _rollCalibrated = true; }
                float delta = Mathf.DeltaAngle(_neutralRoll, roll);
                if (Mathf.Abs(delta) < steerDeadzoneDegrees) delta = 0f;
                sensorX = steerSign * Mathf.Clamp(delta / Mathf.Max(fullSteerRollDegrees, 1f), -1f, 1f);
                _lastSteer = sensorX;
            }
            _handSteer = sensorX; // so a returning board eases in from where the tablet left off
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
        Kinex.Sfx.Play("fanfare");
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
                Kinex.Sfx.Play("correct");
                if (_camera != null) _camera.Shake(0.25f);
            }
            else
            {
                Kinex.Sfx.Play("beep", 0.8f, 0.7f); // low, flat buzz — clearly not the correct chime
                if (_camera != null) _camera.Shake(0.35f);
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
        if (_crashed) return; // one crash per run — the restart is already on its way
        if (collision.gameObject.CompareTag("Mountain") || collision.gameObject.name.Contains("mountain"))
        {
            StartCoroutine(CrashThenRestart());
        }
    }

    // The original code called RestartGame() the instant you clipped a mountain — no sound, no
    // reaction, the run just blinked away, which reads as a glitch rather than a crash. Give it a
    // short beat so the player understands what happened before the scene reloads.
    System.Collections.IEnumerator CrashThenRestart()
    {
        _crashed = true;
        Kinex.Sfx.Play("hit_1");
        if (_camera != null) _camera.Shake(1f);
        // Unscaled: the crash beat must play at the same length no matter what timeScale is doing.
        yield return new WaitForSecondsRealtime(0.9f);
        RestartGame();
    }
}