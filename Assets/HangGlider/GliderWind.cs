using UnityEngine;

namespace Kinex.HangGlider
{
    /// <summary>
    /// Looping wind ambience for the Hang Glider, with volume and pitch tied to how fast the glider
    /// is actually moving — so the flight has an audible sense of speed instead of a flat drone.
    ///
    /// Deliberately self-contained (loads its own clip from Resources, makes its own AudioSource)
    /// in the same spirit as <see cref="Kinex.Sfx"/> and <see cref="Kinex.Music"/>, so there is no
    /// scene wiring to forget. A missing clip is a silent no-op, never an error.
    ///
    /// Uses UNSCALED time deliberately: the wind should keep blowing on the paused start screen
    /// (Time.timeScale == 0), which is also where the player reads the instructions.
    /// </summary>
    public class GliderWind : MonoBehaviour
    {
        [Tooltip("Clip name under Assets/Resources/Sfx/.")]
        public string clipName = "wind_loop";

        [Tooltip("Volume when the glider is stationary (the start screen).")]
        [Range(0f, 1f)] public float idleVolume = 0.16f;
        [Tooltip("Volume at fullSpeed and above.")]
        [Range(0f, 1f)] public float fullVolume = 0.42f;
        [Tooltip("Speed (units/sec) treated as 'full pelt'.")]
        public float fullSpeed = 12f;

        [Tooltip("Pitch rises slightly with speed — a small amount goes a long way; too much and " +
                 "it stops sounding like air and starts sounding like an engine.")]
        public float pitchAtIdle = 0.92f;
        public float pitchAtFull = 1.10f;

        [Tooltip("Higher = the wind reacts faster to speed changes.")]
        public float responsiveness = 2f;

        [Tooltip("What to measure the speed of. Defaults to this object.")]
        public Transform target;

        AudioSource _src;
        Vector3 _lastPos;
        float _speed;

        void Start()
        {
            if (target == null) target = transform;
            _lastPos = target.position;

            var clip = Resources.Load<AudioClip>($"Sfx/{clipName}");
            if (clip == null) return; // no clip imported yet — stay silent rather than error

            _src = gameObject.AddComponent<AudioSource>();
            _src.clip = clip;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;  // 2D: it's the air rushing past the player, not a world source
            _src.volume = idleVolume;
            _src.Play();
        }

        void Update()
        {
            if (_src == null) return;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float instant = (target.position - _lastPos).magnitude / dt;
            _lastPos = target.position;
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-responsiveness * dt));

            float k = Mathf.Clamp01(_speed / Mathf.Max(fullSpeed, 0.01f));
            _src.volume = Mathf.Lerp(idleVolume, fullVolume, k);
            _src.pitch = Mathf.Lerp(pitchAtIdle, pitchAtFull, k);
        }
    }
}
