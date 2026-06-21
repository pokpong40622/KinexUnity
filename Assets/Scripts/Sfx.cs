using UnityEngine;

namespace Kinex
{
    /// <summary>
    /// One-line game SFX. Plays a clip from Resources/Sfx by name through a single
    /// persistent AudioSource created on first use — same runtime/Resources spirit as
    /// ScoreHud and CorrectEffect, so no scene/prefab wiring is needed.
    ///
    /// Drop CC0 clips into Assets/Resources/Sfx/ named to match (correct, pose_appear,
    /// results, ...). Missing clips are silently ignored, so calls are always safe.
    /// </summary>
    public static class Sfx
    {
        static AudioSource _src;

        // Lazily build a hidden, scene-independent AudioSource the first time we play.
        static AudioSource Source()
        {
            if (_src != null) return _src;
            var go = new GameObject("SfxPlayer");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            return _src;
        }

        /// <summary>Play Resources/Sfx/&lt;name&gt; once. No-op if the clip is missing.
        /// pitch lets callers escalate a sound (e.g. a rising combo chime).</summary>
        public static void Play(string name, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(name)) return;
            var clip = Resources.Load<AudioClip>($"Sfx/{name}");
            if (clip == null) return; // clip not imported yet — stay silent rather than error
            var src = Source();
            src.pitch = pitch; // affects the PlayOneShot that follows
            src.PlayOneShot(clip, volume);
        }
    }
}
