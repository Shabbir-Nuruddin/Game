using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Tonight's Rumor — every Blood Moon night hides ONE secret rule, chosen
    /// deterministically from the daily seed (so the whole world shares it). The
    /// game only whispers a cryptic line at run start; PROVING the rumor is the
    /// share-worthy moment, and the result-screen brag gets the receipt.
    ///
    /// The four rumors:
    ///   0  "the saws lie tonight"        — every saw is harmless scenery
    ///   1  "one floor that looks false,
    ///       holds true"                  — night 3's first fake floor is real
    ///   2  "a door hides where the
    ///       second night begins"         — a ghost door near night 2's start
    ///                                      skips you straight onward
    ///   3  "the moon protects the marked"— your learned late-spikes never arm
    /// GameRoot applies the effects (all gated on Mode.Daily); RumorZone triggers
    /// report the discovery.
    /// </summary>
    public static class Rumor
    {
        static int _seed = -1;

        public static void Arm(int dailySeed) => _seed = dailySeed;
        public static void Disarm() => _seed = -1;
        public static bool Active => _seed > 0;

        public static int Id
        {
            get
            {
                if (!Active) return -1;
                // Knuth multiplicative hash so consecutive days don't cycle 0,1,2,3.
                unchecked { return (int)(((uint)_seed * 2654435761u) % 4u); }
            }
        }

        public static bool SawsLie      => Id == 0;
        public static bool FloorHolds   => Id == 1;
        public static bool HiddenDoor   => Id == 2;
        public static bool MoonProtects => Id == 3;

        public static string CrypticLine
        {
            get
            {
                switch (Id)
                {
                    case 0: return "the saws lie tonight";
                    case 1: return "one floor that looks false, holds true";
                    case 2: return "a door hides where the second night begins";
                    case 3: return "the moon protects the marked";
                    default: return "";
                }
            }
        }

        public static bool Discovered => Active && PlayerPrefs.GetInt("rumor_found_" + _seed, 0) == 1;

        /// <summary>Prove tonight's rumor (once per night): toast + analytics + brag receipt.</summary>
        public static void Discover()
        {
            if (!Active || Discovered) return;
            PlayerPrefs.SetInt("rumor_found_" + _seed, 1);
            PlayerPrefs.Save();
            Audio.PlayOr("levelup", "win", 0.6f);
            GameRoot.I?.BossToast("…THE RUMOR WAS TRUE");
            Analytics.Track("rumor_discovered", new System.Collections.Generic.Dictionary<string, object>
            {
                { "rumor", Id },
            });
        }
    }

    /// <summary>A touch-trigger that proves tonight's rumor when the player finds it.</summary>
    public class RumorZone : MonoBehaviour
    {
        void OnTriggerEnter2D(Collider2D o)
        {
            if (o.GetComponent<PlayerController>() != null) Rumor.Discover();
        }
    }
}
