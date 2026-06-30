using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrustIssues
{
    /// <summary>
    /// Speaks the death-roast out loud via the browser's speech synthesis (WebGL).
    /// No-op in the editor / non-WebGL builds. Honours a player toggle stored in
    /// PlayerPrefs ("voice_muted"). The actual TTS lives in Plugins/Speak.jslib.
    /// </summary>
    public static class Voice
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TI_Speak(string text, float volume);
#endif

        public static bool Muted
        {
            get => PlayerPrefs.GetInt("voice_muted", 0) == 1;
            set { PlayerPrefs.SetInt("voice_muted", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // 0..1 voice level (the Settings VOICE slider). Drives both the spoken death
        // roast (browser TTS) and the vampire's dying-groan clip (Audio.PlayVoice).
        static float _volume = -1f;
        public static float Volume
        {
            get { if (_volume < 0f) _volume = Mathf.Clamp01(PlayerPrefs.GetFloat("voice_vol", 1f)); return _volume; }
            set { _volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("voice_vol", _volume); PlayerPrefs.Save(); }
        }

        public static void Speak(string text)
        {
            if (Muted || Volume <= 0.001f || string.IsNullOrEmpty(text)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { TI_Speak(text, Volume); } catch { /* TTS is best-effort */ }
#endif
        }
    }
}
