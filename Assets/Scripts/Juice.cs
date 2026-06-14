using System.Collections;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>Small feel/comedy helpers: screen shake and cheeky death lines.</summary>
    public static class Juice
    {
        static readonly string[] Deaths =
        {
            "Trust issues confirmed.",
            "The floor lied to you.",
            "You ABSOLUTELY saw that coming.",
            "Skill issue (it was a trap).",
            "Beanie regrets everything.",
            "That door? Pure evil.",
            "Gravity wins again.",
            "Bonk.",
            "Maybe... jump next time?",
            "The spikes said hi.",
        };

        public static string DeathLine() => Deaths[Random.Range(0, Deaths.Length)];

        public static IEnumerator Shake(Transform cam, float amount = 0.35f, float dur = 0.28f)
        {
            Vector3 home = cam.localPosition;
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = 1f - (e / dur);
                cam.localPosition = home + (Vector3)(Random.insideUnitCircle * amount * k);
                yield return null;
            }
            cam.localPosition = home;
        }

        /// <summary>Comedic squish: flatten the visual then pop, used on death.</summary>
        public static IEnumerator Squish(Transform visual, float dur = 0.25f)
        {
            float e = 0f;
            Vector3 start = visual.localScale;
            Vector3 flat = new Vector3(Mathf.Abs(start.x) * 1.6f, start.y * 0.25f, 1f);
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                visual.localScale = Vector3.Lerp(start, flat, e / dur);
                yield return null;
            }
        }
    }
}
