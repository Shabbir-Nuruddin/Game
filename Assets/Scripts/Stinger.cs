using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// The PUNCHLINE SOUND. A death used to end on a long human groan, which is
    /// the wrong instrument entirely — a groan is sad, and this game wants the
    /// player smug-angry, not sympathetic. What lands under a one-word roast is
    /// the internet's own punctuation: a deep, short, comedic BOOM (the sound
    /// every "bro thought" clip cuts to), sometimes a dry bonk, occasionally a
    /// sad little trombone-ish fall when the castle is pretending to pity you.
    ///
    /// These are SYNTHESISED at runtime rather than shipped as files, for three
    /// reasons: no licensing on a meme sample, no download weight on the WebGL
    /// build, and — the important one — the pitch can be jittered per death, so
    /// the hundredth death never sounds identical to the first. Everything is
    /// generated once and cached; the cost is a few milliseconds on first death.
    ///
    /// Volume rides the VOICE slider (Voice.Volume), because this IS the castle
    /// talking — a player who mutes the voice must not keep getting boomed at.
    /// </summary>
    public static class Stinger
    {
        const int Rate = 44100;

        static AudioSource _src;          // its own source so pitch can be set per hit
        static AudioClip _boom, _bonk, _sad;

        static void Ensure()
        {
            if (_src != null) return;
            var go = new GameObject("Stinger");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;       // 2D — always audible, never positional
            _src.volume = 1f;
        }

        // ------------------------------------------------------------------
        //  Synthesis
        // ------------------------------------------------------------------

        /// <summary>Soft clipper — squashes peaks instead of cracking, so the hit
        /// reads as LOUD and thick on a phone speaker without actually clipping.</summary>
        static float Saturate(float x)
        {
            const float k = 1.7f;
            return (float)System.Math.Tanh(x * k) / (float)System.Math.Tanh(k);
        }

        /// <summary>
        /// The boom: a sine that starts around 190Hz and dives to a chest-thump
        /// 55Hz within a fifth of a second, with a tiny noise transient on the
        /// front so it has a "hit" and not just a "hum". Frequency is swept by
        /// integrating phase — stepping the frequency directly would tear.
        /// </summary>
        static AudioClip Boom()
        {
            if (_boom != null) return _boom;
            int n = (int)(Rate * 0.50f);
            var d = new float[n];
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 55f + 135f * Mathf.Exp(-t * 11f);       // the dive
                phase += 2.0 * System.Math.PI * f / Rate;
                float env = Mathf.Exp(-t * 6.5f);                 // fast, comedic decay
                float body = (float)System.Math.Sin(phase) * env;
                float click = (Random.value * 2f - 1f) * Mathf.Exp(-t * 150f) * 0.30f;
                d[i] = Saturate(body * 0.95f + click) * 0.85f;
            }
            FadeTail(d);
            _boom = AudioClip.Create("ti_boom", n, 1, Rate, false);
            _boom.SetData(d, 0);
            return _boom;
        }

        /// <summary>A dry wooden bonk — brighter and shorter, for the cheeky
        /// one-worders where a full sub-bass boom would be overkill.</summary>
        static AudioClip Bonk()
        {
            if (_bonk != null) return _bonk;
            int n = (int)(Rate * 0.22f);
            var d = new float[n];
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 170f + 700f * Mathf.Exp(-t * 26f);
                phase += 2.0 * System.Math.PI * f / Rate;
                float env = Mathf.Exp(-t * 15f);
                // A touch of the third harmonic gives it a hollow, wooden knock.
                float body = ((float)System.Math.Sin(phase) +
                              0.35f * (float)System.Math.Sin(phase * 3.0)) * env;
                d[i] = Saturate(body * 0.7f) * 0.8f;
            }
            FadeTail(d);
            _bonk = AudioClip.Create("ti_bonk", n, 1, Rate, false);
            _bonk.SetData(d, 0);
            return _bonk;
        }

        /// <summary>The sad slide — three descending tones, the "womp womp womp"
        /// shape. Reserved for the pity tier, where the castle is being kind on
        /// purpose because that's more annoying than being mean.</summary>
        static AudioClip Sad()
        {
            if (_sad != null) return _sad;
            int n = (int)(Rate * 0.75f);
            var d = new float[n];
            double phase = 0.0;
            // Three falling steps, each one lower — a trombone shrugging.
            float[] steps = { 233f, 196f, 155f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int step = Mathf.Clamp((int)(t / 0.25f), 0, 2);
                float local = t - step * 0.25f;
                // Slide down a little WITHIN each step, which is what makes it read
                // as a slide instrument rather than three separate beeps.
                float f = steps[step] * (1f - 0.10f * (local / 0.25f));
                phase += 2.0 * System.Math.PI * f / Rate;
                float env = Mathf.Exp(-local * 5.5f) * Mathf.Exp(-t * 0.7f);
                // Saw-ish stack = brassy. A pure sine here sounds like a test tone.
                float body = (float)(System.Math.Sin(phase)
                                   + 0.5 * System.Math.Sin(phase * 2.0)
                                   + 0.25 * System.Math.Sin(phase * 3.0)) * env;
                d[i] = Saturate(body * 0.45f) * 0.6f;
            }
            FadeTail(d);
            _sad = AudioClip.Create("ti_sad", n, 1, Rate, false);
            _sad.SetData(d, 0);
            return _sad;
        }

        /// <summary>Ramp the last few ms to zero. Without this the clip ends on a
        /// non-zero sample and every playback ticks audibly.</summary>
        static void FadeTail(float[] d)
        {
            int fade = Mathf.Min(600, d.Length);
            for (int i = 0; i < fade; i++)
            {
                int j = d.Length - 1 - i;
                d[j] *= i / (float)fade;
            }
        }

        // ------------------------------------------------------------------
        //  Playback
        // ------------------------------------------------------------------

        static void Hit(AudioClip c, float vol, float pitchLo, float pitchHi)
        {
            if (c == null) return;
            Ensure();
            // The VOICE slider owns this sound, and the master mute silences it —
            // otherwise muting the game still leaves you being boomed at.
            if (Audio.Muted || Voice.Muted) return;
            float v = vol * Voice.Volume;
            if (v <= 0.001f) return;
            _src.pitch = Random.Range(pitchLo, pitchHi);   // never twice the same
            _src.PlayOneShot(c, Mathf.Clamp01(v));
        }

        /// <summary>
        /// The death hit. Picks its flavour from how badly the run is going: a
        /// straight boom most of the time, a dry bonk for variety, and the sad
        /// slide once the death count has tipped into pity territory.
        /// </summary>
        public static void Death(int deaths)
        {
            // Past 25 deaths the castle switches to fake sympathy, and the sound
            // has to agree with the words — a boom under "You good?" is a mixed
            // message, the trombone is the joke.
            if (deaths >= 25 && Random.value < 0.45f) { Hit(Sad(), 0.55f, 0.94f, 1.06f); return; }
            if (Random.value < 0.25f) { Hit(Bonk(), 0.70f, 0.90f, 1.12f); return; }
            Hit(Boom(), 0.85f, 0.88f, 1.14f);
        }

        /// <summary>A bare boom, for callers that just want the punctuation.</summary>
        public static void Punch(float volume = 0.85f) => Hit(Boom(), volume, 0.88f, 1.14f);
    }
}
