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
    /// ROAST STYLE RULE. These were one-to-three words for exactly one build and
    /// it failed playtest: "Mid." and "Cooked." are *annoying* rather than
    /// enraging, because a bare insult with no content isn't ABOUT you — it's
    /// noise you learn to tune out, and the player's reaction was to go and mute
    /// it. What actually lands is SPECIFICITY: the castle naming the exact
    /// stupid thing you just did, in one short sentence, with the insult riding
    /// on top. "Did you not see the spike? A vampire. With cataracts." stings
    /// because it proves something was watching.
    ///
    /// So the rules are:
    ///   • ONE sentence, ~5-10 words. Long enough to carry an observation,
    ///     short enough to read before the respawn.
    ///   • It must name WHAT HAPPENED, not just deliver a verdict.
    ///   • It blames the PLAYER, never the trap. The reaction being farmed is
    ///     "wait, watch, I'll show you" — so the castle is smug, never sorry.
    ///   • Current internet register is seasoning, not the meal. One "skill
    ///     issue" among specifics hits; ten in a row is wallpaper.
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
                "You trusted a floor. In THIS game.",
                "The game is called Trust Issues. It's on the box.",
                "Solid ground is a rumour around here.",
                "You stood on it like it owed you nothing.",
                "Every floor lies. That one just went first.",
                "Walked onto thin air with total confidence.",
            },
            [TrapType.LateSpike] = new[]
            {
                "It waited for you to land. You obliged.",
                "The ground bit back. You look shocked.",
                "It came up AFTER you landed. That's the joke.",
                "You keep landing on the same tile, hero.",
                "Out-waited by a spike. A spike.",
                "It had all night. You had one job.",
            },
            [TrapType.Crusher] = new[]
            {
                "You went for the shiny thing. Obviously.",
                "Greed, then a ceiling. Very poetic.",
                "Stay low. I'll say it slower next time.",
                "The bait worked. The bait always works.",
                "Reached for treasure, received a ceiling.",
                "Every single time with the high ground.",
            },
            [TrapType.FakeExit] = new[]
            {
                "The bright obvious door. Really. Truly.",
                "Doors are exits in other games. Not mine.",
                "You sprinted at the one thing glowing.",
                "Only coffins let you out. Take notes.",
                "That door has killed better than you.",
            },
            [TrapType.Surprise] = new[]
            {
                "There was nothing there. Now there's you.",
                "I put that in this morning. Just for you.",
                "Unfair? Obviously. Read the title.",
                "You died to empty air. Frame that one.",
                "No warning, no tell, no mercy. Lovely.",
            },
            [TrapType.Dart] = new[]
            {
                "It fired. You stood there thinking about it.",
                "A dart. Travelling in a straight line. Unbeatable.",
                "You had a full second and used none of it.",
                "Caught in 4K and also in the chest.",
                "It moved. That was your cue. You sat.",
            },
            [TrapType.Faller] = new[]
            {
                "Look UP. That's the tip. That's the whole tip.",
                "Something fell on you. From above. Again.",
                "Two dimensions and you only watch one.",
                "The ceiling would like to be acknowledged.",
                "It dropped exactly where you were standing.",
            },
            [TrapType.Spring] = new[]
            {
                "You liked the bouncy thing. It didn't like you.",
                "You launched yourself. Nobody made you.",
                "Great height. Genuinely awful landing.",
                "Free flight, and you still found a way.",
            },
            [TrapType.Saw] = new[]
            {
                "It was spinning. Loudly. In plain sight.",
                "You walked into a blender on purpose.",
                "The saw has exactly one move. It worked.",
                "Big, loud and obvious. Still got you.",
                "That blade has never once hidden from anyone.",
            },
            [TrapType.WarpBack] = new[]
            {
                "Back to the start. That's the trap. That's it.",
                "The shortcut was the bait. It's always the bait.",
                "You saved zero seconds. Congratulations.",
                "Enjoy the walk. Take your time.",
            },
            [TrapType.Reverse] = new[]
            {
                "Left is right now. Deal with it.",
                "Your own hands turned on you.",
                "The controls flipped. You did not.",
                "Same buttons. Different meaning. Keep up.",
            },
            [TrapType.SpikeStatic] = new[]
            {
                "It never moved. Not once. It just stood there.",
                "That spike has been there since before you.",
                "You had one job. The spike had none.",
                "Killed by scenery. Actual scenery.",
                "It didn't even hide. It just waited.",
            },
            [TrapType.ArrowRain] = new[]
            {
                "It's on a timer. You could count. You didn't.",
                "The ceiling rains. Every few seconds. Forever.",
                "You walked in mid-volley. Confident.",
                "Rhythm exists. You've heard of it.",
            },
            [TrapType.GrowSpike] = new[]
            {
                "It grows and shrinks. Wait two seconds.",
                "Impatience. That's the actual cause of death.",
                "Did you time that with your eyes shut?",
                "It was small a moment ago. That was the moment.",
            },
            [TrapType.Pendulum] = new[]
            {
                "It swings on a beat. You have no rhythm.",
                "Tick. Tock. You.",
                "The most predictable object ever built.",
                "It's been doing that loop all night.",
            },
            [TrapType.FlameJet] = new[]
            {
                "The fire has a schedule. You don't read schedules.",
                "It was off. You waited. Then it wasn't.",
                "Cooked. Literally, this time.",
                "You stood on a hole that breathes fire.",
            },
            [TrapType.Chandelier] = new[]
            {
                "It creaked first. You ignored the creak.",
                "You watched it fall and stayed anyway.",
                "Killed by interior decorating.",
                "A whole chandelier. Telegraphed. Ignored.",
            },
            [TrapType.HolyWater] = new[]
            {
                "You're a VAMPIRE. That's HOLY WATER.",
                "It was glowing. Glowing means don't.",
                "Read the puddle. It's one puddle.",
                "Blessed to death. Embarrassing for a vampire.",
            },
            [TrapType.BatSwoop] = new[]
            {
                "Out-flown by a bat. You ARE a bat.",
                "It flared red first. That was your cue.",
                "Mogged by your own species.",
                "A bat beat you at being a bat.",
            },
            [TrapType.BreakBlock] = new[]
            {
                "You have a gun. Try it on the wall.",
                "Walls don't move. Bullets do.",
                "There's a shoot button. Genuinely.",
            },
        };

        // ---- cause-flavoured lines (used when the exact trap isn't known) ----
        static readonly System.Collections.Generic.Dictionary<string, string[]> Flavour = new()
        {
            [Spike] = new[]
            {
                "Did you not SEE that? A vampire with cataracts.",
                "Impaled by something that never moved.",
                "You walked directly into the pointy bit.",
                "Centuries of night vision, wasted.",
                "Sharp things are sharp. Noted for next time.",
            },
            [Crush] = new[]
            {
                "Flattened. Try existing lower down.",
                "You jumped straight into it. On purpose?",
                "Two dimensions was already plenty.",
                "The ceiling had the better idea.",
            },
            [Burn] = new[]
            {
                "Sunburn. On a vampire. Genuinely a first.",
                "You had time. You always have time.",
                "Ash. That's it. That's all that's left.",
                "Burned alive by scheduled lighting.",
            },
            [Bat] = new[]
            {
                "Killed by a bat. You turn INTO a bat.",
                "Out-flapped by something the size of a fist.",
                "It saw you coming. You saw nothing.",
                "Your own species is embarrassed.",
            },
            [Saw] = new[]
            {
                "Shredded by the loudest object in the room.",
                "It was spinning the entire time.",
                "You and the saw disagreed. Saw won.",
                "Cut to ribbons. Very tidy about it.",
            },
            [Fall] = new[]
            {
                "You found the one hole. Of course you did.",
                "Gravity remains undefeated.",
                "That was not a shortcut.",
                "Straight down. No hesitation. Impressive.",
            },
            [Generic] = new[]
            {
                "That was entirely your own doing.",
                "Skill issue, and I mean that clinically.",
                "The castle didn't even try that time.",
                "You did that to yourself. I just watched.",
                "Trust issues confirmed. Yours, not mine.",
                "Maybe try jumping. Wild idea, I know.",
            },
        };

        // ---- escalation tiers: it gets PERSONAL the more you die ----
        // Written to be said OUT LOUD (the TTS voice reads them) and quotable.
        // Every line blames the PLAYER, never the trap — the reaction being farmed
        // is "wait and watch, I'll show you", and pity does that better than anger.
        static readonly string[] TierMocking =   // ~4–9 deaths
        {
            "Same trap. Same spot. Same face.",
            "Bold of you to try that identically.",
            "Centuries undead, beaten by a hallway.",
            "The spikes recognise you now.",
            "That's twice. I'm keeping count.",
            "You walked into that like it owed you money.",
            "The castle didn't even move that one.",
        };
        static readonly string[] TierBrutal =    // ~10–24 deaths
        {
            "This floor has a body count and it's all you.",
            "The bats have started taking bets.",
            "I'd offer a tutorial but you'd die in it.",
            "Your ghost has requested a transfer.",
            "Even the trap feels strange about this now.",
            "The floor has filed a formal complaint.",
            "At some point this stops being my fault.",
        };
        static readonly string[] TierPity =      // 25+ deaths
        {
            "Hey. It's fine. It isn't, but hey.",
            "We can stop whenever you like. Please.",
            "You've earned a participation coffin.",
            "I'm not angry. I'm genuinely puzzled.",
            "Would you like me to remove one? Honestly.",
            "Your deaths have their own leaderboard now.",
            "Take a break. The castle will wait. Forever.",
        };

        // Twisting the knife when you die RIGHT before the exit (the viral moment).
        static readonly string[] NearMiss =
        {
            "That was hope leaving your body.",
            "One step. That's all it was. One.",
            "The exit waved at you. Then you died.",
            "You could taste it. Now taste the floor.",
            "It watched you die from a metre away.",
            "So close the coffin actually flinched.",
            "Right at the end. Every single time.",
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
            if (a == null || a.Length == 0) return "That was entirely your own doing.";
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
                case 10:  return Remember("Ten deaths. A perfect, round, embarrassing number.");
                case 25:  return Remember("Twenty-five on floor " + floor + ". That's framed now.");
                case 50:  return Remember("FIFTY. The castle has adopted you as a ghost.");
                case 75:  return Remember("Seventy-five. The other ghosts held a meeting.");
                case 100: return Remember("One hundred deaths. Genuinely impressive. Genuinely.");
                case 150: return Remember("One fifty. This floor is named after you now.");
                case 200: return Remember("Two hundred. At this point you ARE the trap.");
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
