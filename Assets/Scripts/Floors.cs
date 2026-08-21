using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// What a floor is CALLED, and what it does to you.
    ///
    /// The castle map's selection plate asks this file two questions: the floor's
    /// name, and its one-line character. Both are authored, one entry per floor.
    ///
    /// WHY AUTHORED. The line used to be derived: the map read the level back and
    /// reported its dominant room rule. That sounds clever and reads terribly,
    /// because only a handful of floors carry a rule at all — every other floor in
    /// the castle fell through to the same fallback sentence, so a player scrubbing
    /// the road saw "No rule. Just the traps, and whatever you assume about them."
    /// perhaps twenty-five times in a row. Twenty-five floors that all say the same
    /// thing are twenty-five floors the map is telling you not to bother reading.
    ///
    /// THE RULE THESE LINES KEEP. They set an EXPECTATION and never name the trap.
    /// This is a game about not trusting what you are told; a map that lists each
    /// floor's hazards would hand the player the answer sheet and delete the point.
    /// So the lines describe posture — what this floor wants from you — and stay
    /// honest about that. Where a floor carries a room rule, that promise is real
    /// and gets said out loud (see <see cref="RuleLines"/>), because a rule changes
    /// how the whole space behaves and finding it out by dying is not a joke, it is
    /// just a wasted life.
    /// </summary>
    public static class Floors
    {
        // 40 names, one per floor, walking down through castle → crypt → swamp → throne.
        static readonly string[] Names =
        {
            "The Threshold",   "Servants' Hall",   "The Long Gallery", "Cellar Stair",     "The Dry Well",
            "Chapel of Salt",  "The Red Corridor", "Bone Kitchen",     "The Drowned Ward", "The Reckoning",
            "Chapel Inverts",  "The Ossuary",      "Coffin Row",       "The Cold Larder",  "Beneath the Slabs",
            "The Weeping Wall","Rat Court",        "The Sealed Vault", "Grave Tide",       "The Countess",
            "The Green Mire",  "Sunken Chapel",    "The Leech Pools",  "Rot Bridge",       "Fen of Teeth",
            "The Bog Choir",   "Drowned Lanterns", "The Black Reeds",  "Mire Gate",        "The Warlock",
            "Ash Stair",       "The Gilded Hall",  "Throne Antechamber","The Long Mirror", "Crown of Iron",
            "The Last Gallery","Hall of Names",    "The Vigil",        "The Final Stair",  "The Lord",
        };

        public static string Name(int index) =>
            index >= 0 && index < Names.Length ? Names[index] : "Unnamed Floor";

        /// <summary>
        /// One authored line per floor. Sets the posture, never names the trap.
        /// Kept to roughly 60-110 characters: the plate gives these two lines at
        /// font 23, and anything longer clips rather than wraps.
        /// </summary>
        static readonly string[] Lines =
        {
            // ---- WORLD 1 · THE CASTLE (1-10) — learning that nothing holds ----
            /*  1 */ "The castle's handshake. It only lies to you once here, and it lies early.",
            /*  2 */ "Nothing new lives on this floor. Get comfortable. That is the trap.",
            /*  3 */ "The ground develops opinions. Two of them, and it holds nothing back.",
            /*  4 */ "The same lean, now with somewhere to fall and something waiting down there.",
            /*  5 */ "One slab in an empty room. Watch what it does before you need to know.",
            /*  6 */ "Two floors that break their promise differently. Telling them apart is the floor.",
            /*  7 */ "Everything so far came from below. Look up. Keep looking up.",
            /*  8 */ "Teeth above, steel below. Running through is no longer free.",
            /*  9 */ "The floor stops pretending it will catch you and goes down with you instead.",
            /* 10 */ "The exam. Everything this wing taught you, asked again without warning.",

            // ---- WORLD 2 · THE CRYPT (11-19) — the room joins in ----
            /* 11 */ "Down stops meaning down. Your instincts are now working against you.",
            /* 12 */ "You cannot see the floor leave. You can only hear it decide to.",
            /* 13 */ "A long row of things that look identical. One of them is not.",
            /* 14 */ "Cold, narrow and patient. This floor wants you to hurry. Do not hurry.",
            /* 15 */ "Whatever is under the stone has been listening to you walk on it.",
            /* 16 */ "The walls are wet and the ground is worse. Commit or stand still forever.",
            /* 17 */ "Twice the floor simply leaves. Both times you will have already stepped.",
            /* 18 */ "The ground rises to meet you, which sounds kind. It is not being kind.",
            /* 19 */ "Every way the ground can betray you, in one room, in order. Good luck.",
            /* 20 */ "She does not fight you. She convinces you to fight the wrong one of her.",

            // ---- WORLD 3 · THE SWAMP (21-29) — two lies at once ----
            /* 21 */ "The floor leaves twice, and the candles are out for both of them.",
            /* 22 */ "A gap you cannot jump. Cross it anyway, then survive the far side.",
            /* 23 */ "Pressed from underneath and from the side. Only one of them is patient.",
            /* 24 */ "The bridge holds. The bridge holds. The bridge holds. The bridge holds.",
            /* 25 */ "Sunken ground with something wrong about its spacing. Count your steps.",
            /* 26 */ "It drops you somewhere. Whether that somewhere is survivable is the joke.",
            /* 27 */ "Lanterns you want to walk toward. That want is the whole mechanism.",
            /* 28 */ "Two betrayals sharing a room and answering each other. Pick wrong once.",
            /* 29 */ "The mire's final gate. It has been saving all four of its tricks.",
            /* 30 */ "He will not let you near him until you have solved what he is standing behind.",

            // ---- WORLD 4 · THE THRONE (31-39) — the castle at full strength ----
            /* 31 */ "Ash underfoot and a long way down. This wing has stopped being funny.",
            /* 32 */ "Gold on everything, including the parts that are about to kill you.",
            /* 33 */ "The room before the room. It is not a formality and it knows it.",
            /* 34 */ "Everything here is reflected. Not everything here is real.",
            /* 35 */ "Iron, height, and a floor that has been waiting for a heavier guest.",
            /* 36 */ "The long walk. The castle has run out of new ideas and is using all the old ones.",
            /* 37 */ "Every heir who failed here is named on the wall. There is room for one more.",
            /* 38 */ "Nothing happens for a while. That is the most expensive part of this floor.",
            /* 39 */ "The last stair down. Everything you learned, at once, with no exam kindness.",
            /* 40 */ "He wears the other three before he wears himself. Do not celebrate early.",
        };

        // A rule is a real, space-wide promise, so the map says it out loud — it
        // changes how you move rather than telling you where a spike is.
        static readonly Dictionary<RoomRule, string> RuleLines = new()
        {
            { RoomRule.Dark,    "The candles go out and you keep walking anyway." },
            { RoomRule.Flee,    "It runs from you. Corner it or die tired." },
            { RoomRule.Press,   "The ceiling comes down. Standing still is a decision." },
            { RoomRule.Reverse, "The curse takes your hands. Left is right in here." },
            { RoomRule.Loop,    "The doorway puts you back where you started." },
        };

        // Sealed floors get a taunt rather than a spoiler — rotated by index so a
        // player scrolling the unreached half of the road doesn't read one sentence
        // twenty times. That repetition was the original complaint.
        static readonly string[] SealedTaunts =
        {
            "Sealed. Whatever it does, it hasn't done it to you yet.",
            "Sealed. The castle is still deciding how to introduce this one.",
            "Sealed. It has heard about you. It is not worried.",
            "Sealed. Come back when the floor above stops winning.",
            "Sealed. There is a reason this door is quiet.",
        };

        // Which rule (if any) a floor leans on. Cached: Levels.Get() runs the whole
        // builder and the map can be scrubbed fast.
        static readonly Dictionary<int, string> _ruleCache = new();

        /// <summary>
        /// The floor's one-line character. <paramref name="reached"/> is false for
        /// floors still sealed — those get a taunt instead of a description.
        /// </summary>
        public static string Rule(int index, bool reached)
        {
            if (!reached)
                return SealedTaunts[((index % SealedTaunts.Length) + SealedTaunts.Length) % SealedTaunts.Length];

            string authored = index >= 0 && index < Lines.Length
                ? Lines[index]
                : "A floor. It has not introduced itself.";

            // A room rule is worth saying on top of the authored line, but only when
            // the floor actually has one and only if both still fit the plate.
            string rule = RuleFor(index);
            if (rule == null) return authored;
            return authored.Length + rule.Length <= 150 ? authored + "\n" + rule : rule;
        }

        /// <summary>The dominant room rule's line for a floor, or null if it has none.</summary>
        static string RuleFor(int index)
        {
            if (_ruleCache.TryGetValue(index, out var cached)) return cached;

            string line = null;
            try
            {
                var level = Levels.Get(index);
                // The floor's signature is the rule it leans on most. A floor with
                // three dark rooms IS the dark floor.
                var tally = new Dictionary<RoomRule, int>();
                foreach (var room in level.Rooms)
                {
                    if (room.Rule == RoomRule.None) continue;
                    tally.TryGetValue(room.Rule, out int n);
                    tally[room.Rule] = n + 1;
                }
                var best = RoomRule.None; int bestCount = 0;
                foreach (var kv in tally)
                    if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
                if (best != RoomRule.None) RuleLines.TryGetValue(best, out line);
            }
            catch
            {
                // A generator that throws on a floor must not take the map down with it.
                line = null;
            }

            _ruleCache[index] = line;
            return line;
        }
    }
}
