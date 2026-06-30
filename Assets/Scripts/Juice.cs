using System.Collections;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Feel/comedy helpers: screen shake, the death squish, and — the heart of the
    /// game's personality — the ROAST system. Every death is categorised by what
    /// killed you, then the game picks a taunt that gets meaner the more you die.
    /// It also maps each death cause to a punchy SFX so dying to spikes SOUNDS
    /// different from being crushed or burned by daylight.
    /// </summary>
    public static class Juice
    {
        // ---- death CAUSE categories (inferred from the cause text passed to Die) ----
        public const string Spike = "spike";
        public const string Crush = "crush";
        public const string Burn  = "burn";   // sun / flame / holy water
        public const string Bat   = "bat";
        public const string Saw   = "saw";
        public const string Fall  = "fall";   // the void / gravity
        public const string Generic = "generic";

        /// <summary>Map the free-text cause Die() received to a stable category.</summary>
        public static string Categorize(string cause)
        {
            if (string.IsNullOrEmpty(cause)) return Generic;
            string s = cause.ToLowerInvariant();
            if (s.Contains("impale") || s.Contains("spike") || s.Contains("stake") ||
                s.Contains("skewer") || s.Contains("pendulum")) return Spike;
            if (s.Contains("crush") || s.Contains("slam") || s.Contains("stone") ||
                s.Contains("flat") || s.Contains("chandelier") || s.Contains("low")) return Crush;
            if (s.Contains("sun") || s.Contains("burn") || s.Contains("daylight") ||
                s.Contains("flame") || s.Contains("fire") || s.Contains("holy")) return Burn;
            if (s.Contains("bat") || s.Contains("screech") || s.Contains("wing")) return Bat;
            if (s.Contains("shred") || s.Contains("saw") || s.Contains("blade")) return Saw;
            if (s.Contains("gravity") || s.Contains("fell") || s.Contains("fall") ||
                s.Contains("void") || s.Contains("pit")) return Fall;
            return Generic;
        }

        /// <summary>The SFX clip name for a death category (drop-in; missing → "death").</summary>
        public static string DeathSfx(string category)
        {
            switch (category)
            {
                case Spike: return "die_impale";
                case Crush: return "die_slam";
                case Burn:  return "die_burn";
                case Bat:   return "die_screech";
                case Saw:   return "die_shred";
                case Fall:  return "die_fall";
                default:    return "death";
            }
        }

        // ---- cause-flavoured taunts (cheeky, used mostly early) ----
        static readonly System.Collections.Generic.Dictionary<string, string[]> Flavour = new()
        {
            [Spike] = new[]
            {
                "Impaled. The spikes were RIGHT THERE.",
                "A vampire, undone by pointy sticks.",
                "You walked into that. On purpose, apparently.",
                "The spikes said hi. You said goodbye.",
            },
            [Crush] = new[]
            {
                "Flattened. Stay LOW next time, genius.",
                "Crushed. You jumped right into it.",
                "Thwomp'd. That's a technical term.",
                "Pancaked. Add syrup.",
            },
            [Burn] = new[]
            {
                "Sunburn. Classic rookie vampire.",
                "Daylight: 1. You: 0.",
                "You burned. You ABSOLUTELY had time.",
                "Ash. Just ash now.",
            },
            [Bat] = new[]
            {
                "Killed by a bat. You ARE a bat.",
                "Out-flapped by your own kind.",
                "The bat saw you coming. You didn't.",
                "Screeched into the afterlife.",
            },
            [Saw] = new[]
            {
                "Shredded. Beautifully, even.",
                "The blade was spinning the WHOLE time.",
                "Cut to ribbons. Tidy.",
                "You and the saw. Saw won.",
            },
            [Fall] = new[]
            {
                "Gravity wins again.",
                "You found the one hole. Of course you did.",
                "Down you go. Bye.",
                "That wasn't a shortcut.",
            },
            [Generic] = new[]
            {
                "Trust issues confirmed.",
                "Skill issue (it was a trap).",
                "Bonk.",
                "Maybe... jump next time?",
            },
        };

        // ---- escalation tiers: it gets PERSONAL the more you die ----
        static readonly string[] TierMocking =   // ~4–9 deaths
        {
            "Again? Bold strategy.",
            "You're getting worse at this.",
            "The trap isn't even trying anymore.",
            "Centuries undead, foiled by a hallway.",
        };
        static readonly string[] TierBrutal =    // ~10–24 deaths
        {
            "This floor has a body count and it's all you.",
            "Have you considered a different game?",
            "The bats are taking bets on you now.",
            "I'd offer a tutorial but you'd die in it.",
        };
        static readonly string[] TierPity =      // 25+ deaths
        {
            "Hey. It's okay. (It's not.)",
            "We can stop whenever you want. Please.",
            "Breathe. Then die again, probably.",
            "You've earned a participation coffin.",
        };

        // Twisting the knife when you die RIGHT before the exit (the viral moment).
        static readonly string[] NearMiss =
        {
            "SO close. That was hope leaving your body.",
            "Right at the end. Almost. ALMOST.",
            "The exit waved at you. Then you died.",
            "One more step. That's all it was. One.",
            "You could taste the win. Now taste the floor.",
        };

        static string Pick(string[] a) => a[Random.Range(0, a.Length)];

        /// <summary>
        /// The roast shown on death. Early deaths get cause-flavoured cheek; as the
        /// toll climbs it shifts to harsher tiers, with milestone barbs — and a
        /// special twist when you die right before the exit (nearMiss).
        /// </summary>
        public static string Roast(string category, int deaths, int floor, bool nearMiss = false)
        {
            // Milestone humiliations.
            switch (deaths)
            {
                case 10:  return "TEN deaths. A perfect, round failure.";
                case 25:  return "25 deaths on floor " + floor + ". Framed and hung in the castle.";
                case 50:  return "FIFTY. The castle has adopted you as a permanent ghost.";
                case 100: return "100 deaths. Genuinely impressive. Genuinely.";
            }

            // Dying at the doorstep is the funniest death — call it out.
            if (nearMiss && deaths > 1 && Random.value < 0.75f) return Pick(NearMiss);

            var pool = Flavour.TryGetValue(category, out var f) ? f : Flavour[Generic];

            // Blend cause-flavour with an escalating tier — more tier as deaths rise.
            if (deaths >= 25) return Random.value < 0.7f ? Pick(TierPity)   : Pick(pool);
            if (deaths >= 10) return Random.value < 0.6f ? Pick(TierBrutal) : Pick(pool);
            if (deaths >= 4)  return Random.value < 0.5f ? Pick(TierMocking): Pick(pool);
            return Pick(pool);
        }

        // Kept for any legacy callers — a plain random line.
        public static string DeathLine() => Pick(Flavour[Generic]);

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
