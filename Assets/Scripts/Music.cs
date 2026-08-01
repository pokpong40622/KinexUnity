using System.Collections;
using UnityEngine;

namespace Kinex
{
    /// <summary>
    /// One-line background music. Plays a looping clip from Resources/Music by name through
    /// a single persistent AudioSource created on first use — same runtime/Resources spirit
    /// as Sfx, so no scene/prefab wiring is needed.
    ///
    /// Drop CC0 tracks into Assets/Resources/Music/ named to match (fruit_theme, quest_theme,
    /// battle_theme, shop_theme, ...). Missing clips just warn and no-op.
    /// </summary>
    public static class Music
    {
        static AudioSource _src;
        static MusicRunner _runner;
        static string _currentName;
        static float _baseVolume = 0.4f;
        static bool _stopping; // a Stop() fade is in flight — Duck must not cancel it

        // Lazily build a hidden, scene-independent AudioSource + coroutine host the first time we play.
        static AudioSource Source()
        {
            if (_src != null) return _src;
            var go = new GameObject("MusicPlayer");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _runner = go.AddComponent<MusicRunner>();
            return _src;
        }

        /// <summary>Play Resources/Music/&lt;name&gt; looping. No-op if the clip is missing or
        /// already playing (so callers can call this every frame/scene-enter safely).</summary>
        public static void Play(string name, float volume = 0.4f, bool loop = true)
        {
            if (string.IsNullOrEmpty(name)) return;
            var src = Source();
            if (src.isPlaying && _currentName == name) return; // already playing this track

            var clip = Resources.Load<AudioClip>($"Music/{name}");
            if (clip == null)
            {
                Debug.LogWarning($"Music: clip '{name}' not found in Resources/Music/");
                return;
            }

            _runner.StopAllCoroutines();
            _stopping = false;
            src.clip = clip;
            src.loop = loop;
            src.volume = volume;
            src.Play();
            _currentName = name;
            _baseVolume = volume;
        }

        /// <summary>Fade the current track out over fadeSeconds, then stop it.</summary>
        public static void Stop(float fadeSeconds = 0.5f)
        {
            if (_src == null || !_src.isPlaying) return;
            _runner.StopAllCoroutines();
            _stopping = true;
            _runner.StartCoroutine(FadeOutAndStop(fadeSeconds));
        }

        /// <summary>Drop volume to 25% (e.g. while TTS speaks), then restore it over seconds.</summary>
        public static void Duck(float seconds)
        {
            if (_src == null || !_src.isPlaying || _stopping) return;
            _runner.StopAllCoroutines();
            _runner.StartCoroutine(DuckAndRestore(seconds));
        }

        static IEnumerator FadeOutAndStop(float fadeSeconds)
        {
            var src = _src;
            var startVolume = src.volume;
            var t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.deltaTime;
                src.volume = Mathf.Lerp(startVolume, 0f, t / fadeSeconds);
                yield return null;
            }
            src.Stop();
            src.volume = _baseVolume;
            _currentName = null;
            _stopping = false;
        }

        static IEnumerator DuckAndRestore(float seconds)
        {
            var src = _src;
            src.volume = _baseVolume * 0.25f;
            var t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                src.volume = Mathf.Lerp(_baseVolume * 0.25f, _baseVolume, t / seconds);
                yield return null;
            }
            src.volume = _baseVolume;
        }
    }
}
