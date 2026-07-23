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
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidSpeak(text);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // The roast used to be WebGL-only, so the castle went completely SILENT in
        // the installed app — it talked on the web build and then said nothing on a
        // phone. Android gets the same voice through the platform's own TextToSpeech
        // engine. Created once and reused; every call is best-effort so a device
        // with no TTS engine simply stays quiet instead of throwing.
        static AndroidJavaObject _tts;
        static bool _ttsReady, _ttsFailed;
        // The engine takes a moment to start, and the FIRST death is exactly when
        // the castle most needs to speak. Hold that line and say it the instant the
        // engine reports ready, instead of silently dropping it.
        static string _pending;

        class InitListener : AndroidJavaProxy
        {
            public InitListener() : base("android.speech.tts.TextToSpeech$OnInitListener") { }
            // MUST be public: AndroidJavaProxy resolves the callback by reflection.
            public void onInit(int status)
            {
                _ttsReady = status == 0;
                _ttsFailed = status != 0;
                if (_ttsReady && !string.IsNullOrEmpty(_pending))
                {
                    var say = _pending; _pending = null;
                    Utter(say);
                }
            }
        }

        static void AndroidSpeak(string text)
        {
            try
            {
                if (_ttsFailed) return;
                if (_tts == null)
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener());
                }
                if (!_ttsReady) { _pending = text; return; }   // spoken on init
                Utter(text);
            }
            catch { _ttsFailed = true; }
        }

        static void Utter(string text)
        {
            try
            {
                if (_tts == null) return;
                _tts.Call<int>("setSpeechRate", 0.95f); // a touch slow = more menacing
                _tts.Call<int>("setPitch", 0.75f);      // drop it: the castle, not a phone
                // speak(CharSequence, int queueMode, Bundle params, String utteranceId)
                // queueMode 0 = QUEUE_FLUSH, so a new roast cuts off the last one.
                // The Bundle must be a TYPED null or the JNI bridge can't pick the
                // overload and throws at runtime.
                _tts.Call<int>("speak", text, 0, (AndroidJavaObject)null, "ti_roast");
            }
            catch { _ttsFailed = true; }
        }
#endif
    }
}
