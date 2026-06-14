using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Loads sprites/sounds the user dropped into Assets/Resources. Everything is
    /// optional: if a file is missing the loader returns null and the game falls
    /// back to its coloured-box / silent version, so it can never break.
    /// </summary>
    public static class Assets
    {
        static readonly Dictionary<string, Sprite> _sprites = new();
        static readonly Dictionary<string, AudioClip> _clips = new();

        public static Sprite Sprite(string name)
        {
            if (_sprites.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("art/" + name);
            _sprites[name] = s;
            return s;
        }

        public static AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("audio/" + name);
            _clips[name] = c;
            return c;
        }
    }

    /// <summary>A tiny global audio player (one shared object, SFX + music).</summary>
    public static class Audio
    {
        static AudioSource _sfx, _music;

        static void Ensure()
        {
            if (_sfx != null) return;
            var go = new GameObject("Audio");
            Object.DontDestroyOnLoad(go);
            _sfx = go.AddComponent<AudioSource>();
            _music = go.AddComponent<AudioSource>();
            _music.loop = true;
            _sfx.playOnAwake = _music.playOnAwake = false;
            // Force 2D so clips are always audible regardless of position/listener.
            _sfx.spatialBlend = _music.spatialBlend = 0f;
            _sfx.volume = _music.volume = 1f;
        }

        public static void Play(string name, float volume = 1f)
        {
            Ensure();
            var c = Assets.Clip(name);
            if (c != null) _sfx.PlayOneShot(c, volume);
        }

        public static void Music(string name, float volume = 0.35f)
        {
            Ensure();
            var c = Assets.Clip(name);
            if (c == null) { _music.Stop(); return; }
            if (_music.clip == c && _music.isPlaying) return;
            _music.clip = c; _music.volume = volume; _music.Play();
        }

        public static void StopMusic() { if (_music != null) _music.Stop(); }
    }
}
