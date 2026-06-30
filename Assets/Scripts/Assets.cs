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

        static readonly Dictionary<string, Sprite[]> _sheets = new();

        /// <summary>
        /// Loads a horizontal sprite sheet (frames left-to-right) as a Texture2D
        /// and slices it into frames at runtime — no manual Sprite Editor needed.
        /// </summary>
        public static Sprite[] Sheet(string name, int frameW)
        {
            if (_sheets.TryGetValue(name, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>("art/" + name);
            Sprite[] frames = null;
            if (tex != null && frameW > 0)
            {
                tex.filterMode = FilterMode.Point;
                int count = Mathf.Max(1, tex.width / frameW);
                frames = new Sprite[count];
                for (int i = 0; i < count; i++)
                    frames[i] = UnityEngine.Sprite.Create(tex,
                        new Rect(i * frameW, 0, frameW, tex.height),
                        new Vector2(0.5f, 0.5f), frameW);
            }
            _sheets[name] = frames;
            return frames;
        }

        static readonly Dictionary<string, Sprite[]> _grids = new();

        /// <summary>
        /// Slices ONE ROW of a square-framed GRID sheet (rows = directions, top
        /// to bottom; columns = animation frames). Used for the 4-direction
        /// vampire pack. Like Sheet, it slices via Sprite.Create so the texture
        /// never needs to be CPU-readable.
        /// </summary>
        public static Sprite[] Grid(string name, int frame, int rowFromTop)
        {
            string key = name + "#" + rowFromTop;
            if (_grids.TryGetValue(key, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>("art/" + name);
            Sprite[] frames = null;
            if (tex != null && frame > 0)
            {
                tex.filterMode = FilterMode.Point;
                int cols = Mathf.Max(1, tex.width / frame);
                int rows = Mathf.Max(1, tex.height / frame);
                int r = Mathf.Clamp(rowFromTop, 0, rows - 1);
                int y = tex.height - (r + 1) * frame; // texture origin is bottom-left
                frames = new Sprite[cols];
                for (int i = 0; i < cols; i++)
                    frames[i] = UnityEngine.Sprite.Create(tex,
                        new Rect(i * frame, y, frame, frame),
                        new Vector2(0.5f, 0.5f), frame);
            }
            _grids[key] = frames;
            return frames;
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
        static bool _muted;        // master mute (HUD button) — silences everything
        static bool _musicMuted;   // per-channel toggles from the Settings screen
        static bool _sfxMuted;

        // The curated SFX/music are mastered louder than this game needs, so a
        // global trim keeps everything in the "present but not painful" range.
        const float MasterSfx = 0.55f;
        const float MasterMusic = 0.6f;

        // Player-set 0..1 volumes (the Settings sliders). Cached after first read.
        static float _musicVol = -1f, _sfxVol = -1f;
        static float _musicBase = 0.35f;   // last per-track volume passed to Music(), so the slider can re-scale live

        public static float MusicVol
        {
            get { if (_musicVol < 0f) _musicVol = Mathf.Clamp01(PlayerPrefs.GetFloat("music_vol", 1f)); return _musicVol; }
            set
            {
                _musicVol = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat("music_vol", _musicVol); PlayerPrefs.Save();
                if (_music != null) _music.volume = _musicBase * MasterMusic * _musicVol;   // apply immediately
            }
        }

        public static float SfxVol
        {
            get { if (_sfxVol < 0f) _sfxVol = Mathf.Clamp01(PlayerPrefs.GetFloat("sfx_vol", 1f)); return _sfxVol; }
            set { _sfxVol = Mathf.Clamp01(value); PlayerPrefs.SetFloat("sfx_vol", _sfxVol); PlayerPrefs.Save(); }
        }

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
            _muted      = PlayerPrefs.GetInt("muted", 0) == 1;
            _musicMuted = PlayerPrefs.GetInt("music_muted", 0) == 1;
            _sfxMuted   = PlayerPrefs.GetInt("sfx_muted", 0) == 1;
            ApplyMutes();
        }

        // A channel is silent if the master mute is on OR its own toggle is off.
        static void ApplyMutes()
        {
            if (_sfx != null)   _sfx.mute   = _muted || _sfxMuted;
            if (_music != null) _music.mute = _muted || _musicMuted;
        }

        // Master mute (the in-game HUD ♪/✕ button) — silences both channels.
        public static bool Muted
        {
            get { return _muted; }
            set
            {
                _muted = value;
                PlayerPrefs.SetInt("muted", value ? 1 : 0); PlayerPrefs.Save();
                ApplyMutes();
            }
        }

        // Per-channel toggles surfaced on the Settings screen.
        public static bool MusicMuted
        {
            get { return _musicMuted; }
            set
            {
                _musicMuted = value;
                PlayerPrefs.SetInt("music_muted", value ? 1 : 0); PlayerPrefs.Save();
                ApplyMutes();
            }
        }

        public static bool SfxMuted
        {
            get { return _sfxMuted; }
            set
            {
                _sfxMuted = value;
                PlayerPrefs.SetInt("sfx_muted", value ? 1 : 0); PlayerPrefs.Save();
                ApplyMutes();
            }
        }

        public static void Play(string name, float volume = 1f)
        {
            Ensure();
            var c = Assets.Clip(name);
            if (c != null) _sfx.PlayOneShot(c, volume * MasterSfx * SfxVol);
        }

        // Like Play, but if the preferred clip is missing it falls back to another
        // (e.g. cause-specific death SFX -> the generic "death"), so there's always
        // SOME feedback even before the bespoke clips are dropped in.
        public static void PlayOr(string name, string fallback, float volume = 1f)
        {
            Ensure();
            var c = Assets.Clip(name) ?? Assets.Clip(fallback);
            if (c != null) _sfx.PlayOneShot(c, volume * MasterSfx * SfxVol);
        }

        // A spoken/groan clip (e.g. the vampire's dying voice) — scaled by the VOICE
        // slider, not SFX, so the two can be balanced independently.
        public static void PlayVoice(string name, float volume = 1f)
        {
            Ensure();
            var c = Assets.Clip(name);
            if (c != null) _sfx.PlayOneShot(c, volume * MasterSfx * Voice.Volume);
        }

        public static void Music(string name, float volume = 0.35f)
        {
            Ensure();
            var c = Assets.Clip(name);
            if (c == null) { _music.Stop(); return; }
            _musicBase = volume;
            if (_music.clip == c && _music.isPlaying) { _music.volume = volume * MasterMusic * MusicVol; return; }
            _music.clip = c; _music.volume = volume * MasterMusic * MusicVol; _music.Play();
        }

        public static void StopMusic() { if (_music != null) _music.Stop(); }
    }
}
