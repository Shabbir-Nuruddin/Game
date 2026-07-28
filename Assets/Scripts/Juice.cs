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
    ///
    /// ROAST STYLE RULE (2026 rewrite): every line is ONE TO THREE WORDS.
    /// The retry is instant and the toast only lives ~1.2s, so a full sentence
    /// never gets read — the player is already moving. Short lines land, get
    /// quoted back, and read cleanly out loud through the TTS voice. The register
    /// is current internet trash-talk (skill issue / womp womp / cooked / aura /
    /// caught in 4K / bro thought), because the reaction being farmed is
    /// "nah I'M him, watch this" — dismissive, never explanatory. Nothing here
    /// ever blames the trap or apologises for it; the castle is smug, not sorry.
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

        // ------------------------------------------------------------------
        //  WHICH TRAP just killed you
        // ------------------------------------------------------------------
        // A death only carries a free-text cause string, which is too coarse to
        // tell a Crusher from a Chandelier. KillZone stamps the exact TrapType
        // here on its way out, so the roast can be bespoke to the thing that got
        // you — that specificity ("It was spinning.") is what makes the castle
        // feel like it was WATCHING rather than reading from a list. Consumed
        // once, so a later non-trap death (the sun, the void) can never inherit
        // a stale trap line.
        static int _lastTrap = -1;
        public static void ReportTrap(int trapType) { _lastTrap = trapType; }
        static int TakeTrap() { int t = _lastTrap; _lastTrap = -1; return t; }

        // ------------------------------------------------------------------
        //  PER-TRAP lines — the trap that killed you gets its own voice
        // ------------------------------------------------------------------
        // Indexed by (int)TrapType. Every lethal trap has its own shelf so the
        // same death never sounds the same twice, and so the line can reference
        // the trap's actual TELL ("It flared first.") — a roast that names the
        // tell teaches the counter while still being an insult.
        static readonly System.Collections.Generic.Dictionary<TrapType, string[]> TrapLines = new()
        {
            [TrapType.FakeFloor] = new[]
            {
                "Floor said nah.", "You trusted it.", "Trust issues.", "The floor lied.",
                "Womp womp.", "Not solid, bestie.", "Gravity claims you.", "Bro thought.",
            },
            [TrapType.LateSpike] = new[]
            {
                "Spikes. Obviously.", "You saw nothing.", "Late. Like you.", "Called it.",
                "Zero aura.", "Skill issue.", "Every time.", "Predictable.",
            },
            [TrapType.Crusher] = new[]
            {
                "Greed. Classic.", "Stay low.", "The bait won.", "Flattened.",
                "You reached. Cute.", "Shiny thing bad.", "Pancaked.", "Mid.",
            },
            [TrapType.FakeExit] = new[]
            {
                "Not the door.", "Wrong door, king.", "Delulu.", "That was bait.",
                "The BRIGHT one? Really.", "Too obvious.", "Cooked.", "Womp womp.",
            },
            [TrapType.Surprise] = new[]
            {
                "Skill issue.", "Unlucky.", "Diabolical.", "Nothing was there. Ok.",
                "My bad. Not really.", "Sue me.", "Nah he tweakin.", "Sorry not sorry.",
            },
            [TrapType.Dart] = new[]
            {
                "Dodge? Never heard.", "Caught in 4K.", "Shot. Ratio.", "NPC behaviour.",
                "Stood still. Bold.", "It fired. You didn't.", "Sniped.", "L.",
            },
            [TrapType.Faller] = new[]
            {
                "Look UP.", "Bonk.", "From above, genius.", "Ceiling won.",
                "Never looked up.", "Squish.", "Skill issue.", "Two dimensions. TWO.",
            },
            [TrapType.Spring] = new[]
            {
                "Boing. Bye.", "Free flight, free L.", "You LIKED that spring.", "Up. Then over.",
                "Yeeted.", "That was a launch pad.", "Airborne. Briefly.", "Womp.",
            },
            [TrapType.Saw] = new[]
            {
                "Saw won.", "It was spinning.", "Blender.", "Shredded.",
                "You walked in.", "Loud AND visible.", "Confetti.", "Mid.",
            },
            [TrapType.WarpBack] = new[]
            {
                "Back to start.", "Spite. Pure spite.", "Run it back.", "Say sike.",
                "Shortcut? Sike.", "Do it again.", "From the top.", "Diabolical.",
            },
            [TrapType.Reverse] = new[]
            {
                "Left is right. Cope.", "Controls: delulu.", "Brain AFK.", "You flipped.",
                "Skill issue, inverted.", "Adapt. Or don't.", "Confused?", "Same buttons.",
            },
            [TrapType.SpikeStatic] = new[]
            {
                "It never moved.", "One job.", "Jump. That's it.", "Stationary.",
                "Didn't even hide.", "Zero aura.", "Skill issue.", "It's been there.",
            },
            [TrapType.ArrowRain] = new[]
            {
                "Rain check.", "Timing? Zero.", "From the ceiling. Again.", "Sky issue.",
                "It's on a timer.", "Count. Please.", "Riddled.", "Womp womp.",
            },
            [TrapType.GrowSpike] = new[]
            {
                "It GROWS.", "Pattern? What pattern.", "Grew. You didn't.", "Timed that awfully.",
                "Watch it breathe.", "Impatient.", "Wait two seconds.", "Cooked.",
            },
            [TrapType.Pendulum] = new[]
            {
                "Swing and a miss.", "It has rhythm.", "Tick. Tock. Dead.", "Read the room.",
                "No beat.", "Off tempo.", "Sliced.", "Mid.",
            },
            [TrapType.FlameJet] = new[]
            {
                "Toasted.", "Well done.", "Fire has a schedule.", "Crispy.",
                "It was OFF. Then on.", "Extra crispy.", "Cooked. Literally.", "Sizzle.",
            },
            [TrapType.Chandelier] = new[]
            {
                "Big ceiling. Big L.", "You WATCHED it fall.", "Telegraphed. Ignored.", "Chandelier: 1.",
                "It creaked first.", "Decor got you.", "Squish.", "Bonk.",
            },
            [TrapType.HolyWater] = new[]
            {
                "Holy. Water. Vampire.", "It was glowing.", "Sizzle.", "Read the puddle.",
                "You're a VAMPIRE.", "Blessed. Unfortunately.", "Steamed.", "Skill issue.",
            },
            [TrapType.BatSwoop] = new[]
            {
                "You ARE a bat.", "Out-batted.", "It flared first.", "Mogged by a bat.",
                "Your own kind.", "Embarrassing.", "The red glow. Hello.", "Womp womp.",
            },
            [TrapType.BreakBlock] = new[]
            {
                "Shoot it.", "You have a GUN.", "Walls don't move.", "Try shooting.",
                "Brain AFK.", "Skill issue.",
            },
        };

        // ---- cause-flavoured lines (used when the exact trap isn't known) ----
        static readonly System.Collections.Generic.Dictionary<string, string[]> Flavour = new()
        {
            [Spike] = new[]
            {
                "Impaled. Ok.", "Pointy sticks. Wow.", "You walked in.", "Skill issue.",
                "Zero aura.", "Cooked.", "Sharp. Obviously.", "Womp womp.",
            },
            [Crush] = new[]
            {
                "Flattened.", "Stay low.", "Squish.", "Pancaked.",
                "Bonk.", "Two dimensions now.", "Mid.", "L.",
            },
            [Burn] = new[]
            {
                "Sunburn. Rookie.", "Crispy.", "Ash.", "Toasted.",
                "You had TIME.", "Well done.", "Cooked. Literally.", "Sizzle.",
            },
            [Bat] = new[]
            {
                "Out-batted.", "You ARE a bat.", "Mogged.", "Embarrassing.",
                "By a BAT.", "Womp womp.", "Screeched.", "Cooked.",
            },
            [Saw] = new[]
            {
                "Shredded.", "Blender.", "It was spinning.", "Confetti.",
                "Saw won.", "Sliced.", "Mid.", "L.",
            },
            [Fall] = new[]
            {
                "Down bad.", "You found the hole.", "Bye.", "Gravity: 1.",
                "Not a shortcut.", "Yeeted.", "Womp womp.", "Skill issue.",
            },
            [Generic] = new[]
            {
                "Skill issue.", "Womp womp.", "Cooked.", "Bonk.",
                "Mid.", "L.", "Zero aura.", "Trust issues.",
                "Delulu.", "Nah he tweakin.", "Bro thought.", "Caught in 4K.",
            },
        };

        // ---- escalation tiers: it gets PERSONAL the more you die ----
        // Written to be said OUT LOUD (the TTS voice reads them) and quotable.
        // Every line blames the PLAYER, never the trap — the reaction being farmed
        // is "wait and watch, I'll show you", and pity does that better than anger.
        static readonly string[] TierMocking =   // ~4–9 deaths
        {
            "Again?", "Same trap.", "Bold strategy.", "Predictable.",
            "You're not him.", "Womp womp.", "Skill issue. Again.", "Mid run.",
            "Chat, he's cooked.", "Bro thought.", "Zero progress.", "Try harder.",
        };
        static readonly string[] TierBrutal =    // ~10–24 deaths
        {
            "Down horrendous.", "It's over.", "Pack watch.", "Aura: gone.",
            "Crash out incoming.", "You're NOT him.", "Cooked. Thoroughly.", "L + ratio.",
            "Touch grass.", "Genuinely mid.", "Brain AFK.", "Not cooking.",
            "Diabolical.", "Get ripped, bozo.",
        };
        static readonly string[] TierPity =      // 25+ deaths
        {
            "It's okay. (It's not.)", "We can stop.", "Please stop.", "For me?",
            "This is sad.", "I'm not angry.", "Just disappointed.", "Take a break.",
            "Hydrate, king.", "Try easy mode.", "You good?", "Blink twice.",
            "Sending me.", "Unemployed behaviour.",
        };

        // Twisting the knife when you die RIGHT before the exit (the viral moment).
        static readonly string[] NearMiss =
        {
            "SO close.", "Almost. ALMOST.", "One step.", "Right at the end.",
            "Choked.", "Clutch? No.", "Hope: deleted.", "It was RIGHT there.",
            "Sending me.", "That's crazy.", "Nooo way.", "Womp womp womp.",
        };

        // ------------------------------------------------------------------
        //  NO-REPEAT: the whole point is unpredictability
        // ------------------------------------------------------------------
        // With one-word lines a repeat inside a 10-death streak is instantly
        // obvious and kills the illusion that the castle is reacting to YOU.
        // Remember the last few spoken lines and re-roll past them.
        static readonly System.Collections.Generic.Queue<string> _recent = new();
        const int RecentMemory = 8;

        static bool WasRecent(string s)
        {
            foreach (var r in _recent) if (r == s) return true;
            return false;
        }

        static string Remember(string s)
        {
            _recent.Enqueue(s);
            while (_recent.Count > RecentMemory) _recent.Dequeue();
            return s;
        }

        /// <summary>Pick from a pool, avoiding anything said in the last few deaths.</summary>
        static string Pick(string[] a)
        {
            if (a == null || a.Length == 0) return "Womp womp.";
            // Try a handful of times, then take whatever comes — a small pool
            // shouldn't be able to starve the picker into a stall.
            for (int i = 0; i < 6; i++)
            {
                string s = a[Random.Range(0, a.Length)];
                if (!WasRecent(s)) return Remember(s);
            }
            return Remember(a[Random.Range(0, a.Length)]);
        }

        /// <summary>
        /// The roast shown on death. If the exact trap is known it usually speaks
        /// with that trap's own voice; otherwise it falls back to cause flavour,
        /// shifting to harsher tiers as the toll climbs — with milestone barbs and
        /// a special twist when you die right at the exit (nearMiss).
        /// </summary>
        public static string Roast(string category, int deaths, int floor, bool nearMiss = false)
        {
            int trap = TakeTrap();   // consumed either way, so it can never go stale

            // Milestone humiliations. Still short — a milestone that takes four
            // seconds to read is a milestone nobody reads.
            switch (deaths)
            {
                case 10:  return Remember("Ten. Nice.");
                case 25:  return Remember("Twenty-five. On floor " + floor + ".");
                case 50:  return Remember("FIFTY. Genuinely.");
                case 75:  return Remember("Seventy-five. Wow.");
                case 100: return Remember("One hundred. Respect. Kind of.");
                case 150: return Remember("One fifty. This floor is yours now.");
                case 200: return Remember("Two hundred. You ARE the trap.");
            }

            // Dying at the doorstep is the funniest death — call it out.
            if (nearMiss && deaths > 1 && Random.value < 0.75f) return Pick(NearMiss);

            // The bespoke trap line is the good stuff, so it gets first refusal —
            // but not every time, or the trap starts to feel like a vending machine.
            if (trap >= 0 && TrapLines.TryGetValue((TrapType)trap, out var tl) && Random.value < 0.6f)
                return Pick(tl);

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
