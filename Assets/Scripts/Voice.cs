using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrustIssues
{
    /// <summary>
    /// Optional spoken delivery for rare story interludes. WebGL uses the browser's
    /// speech synthesis and Android uses the phone's TextToSpeech service. Honours
    /// the player's VOICE toggle and volume setting.
    /// </summary>
    public static class Voice
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TI_Speak(string text, float volume);
        [DllImport("__Internal")] static extern void TI_Narrate(string text, float volume);
        [DllImport("__Internal")] static extern void TI_StopSpeak();
#endif

        public static bool Muted
        {
            get => PlayerPrefs.GetInt("voice_muted", 0) == 1;
            set { PlayerPrefs.SetInt("voice_muted", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // 0..1 voice level (the Settings VOICE slider). Drives narrated interludes
        // and any authored voice clips played elsewhere by the audio system.
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
            AndroidSpeak(text, false);
#endif
        }

        /// <summary>Measured story delivery, reserved for milestone interludes.</summary>
        public static void Narrate(string text)
        {
            if (Muted || Volume <= 0.001f || string.IsNullOrEmpty(text)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { TI_Narrate(text, Volume); } catch { }
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidSpeak(text, true);
#endif
        }

        /// <summary>Stops narration immediately when the player chooses Skip.</summary>
        public static void Stop()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { TI_StopSpeak(); } catch { }
#elif UNITY_ANDROID && !UNITY_EDITOR
            try { _pending = null; if (_tts != null) _tts.Call<int>("stop"); } catch { }
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
        static bool _pendingNarration;

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
                    var say = _pending; var story = _pendingNarration; _pending = null;
                    Utter(say, story);
                }
            }
        }

        static void AndroidSpeak(string text, bool narration)
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
                if (!_ttsReady) { _pending = text; _pendingNarration = narration; return; }
                Utter(text, narration);
            }
            catch { _ttsFailed = true; }
        }

        static void Utter(string text, bool narration)
        {
            try
            {
                if (_tts == null) return;
                // Fast and bright, not slow and demonic. The roasts are now one to
                // three words; a deep, drawn-out delivery on "Cooked." sounds like a
                // bored robot, while a quick smug read sounds like someone in the
                // room laughing at you — which is the reaction the writing wants.
                // Both values are jittered a little so repeat deaths never land on
                // the exact same reading.
                _tts.Call<int>("setSpeechRate", narration ? 0.92f : Random.Range(1.25f, 1.45f));
                _tts.Call<int>("setPitch", narration ? 0.86f : Random.Range(1.05f, 1.35f));
                // speak(CharSequence, int queueMode, Bundle params, String utteranceId)
                // queueMode 0 = QUEUE_FLUSH, so a new roast cuts off the last one.
                // The Bundle must be a TYPED null or the JNI bridge can't pick the
                // overload and throws at runtime.
                _tts.Call<int>("speak", text, 0, (AndroidJavaObject)null,
                    narration ? "ti_story" : "ti_line");
            }
            catch { _ttsFailed = true; }
        }
#endif
    }
}
