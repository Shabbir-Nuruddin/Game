using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// What a floor is CALLED, and what it does to you.
    ///
    /// The castle map redesign puts a selection plate under the road: tap a floor and it
    /// tells you its name, the lie it tells, and what it has cost you so far. The floors
    /// never had names — they were numbers — so this supplies them.
    ///
    /// The names are authored. The RULE LINE is not: it's read back off the level the
    /// game actually builds, so it can never promise a darkness that isn't there. Floors
    /// that keep their lie hidden (and every floor you haven't reached) say so instead —
    /// this is a game about not trusting what you're told, and a map that spoils each
    /// trap would be the wrong kind of honest.
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

        // One line per rule, in the castle's voice rather than the code's.
        static readonly Dictionary<RoomRule, string> RuleLines = new()
        {
            { RoomRule.Dark,    "The candles go out and you keep walking anyway." },
            { RoomRule.Flee,    "It runs from you. Corner it or die tired." },
            { RoomRule.Press,   "The ceiling comes down. Standing still is a decision." },
            { RoomRule.Reverse, "The curse takes your hands. Left is right in here." },
            { RoomRule.Loop,    "The doorway puts you back where you started." },
        };

        // Cached per floor so tapping around the map doesn't rebuild levels repeatedly —
        // Levels.Get() runs the whole generator, and the map can be scrubbed fast.
        static readonly Dictionary<int, string> _cache = new();

        /// <summary>
        /// The floor's one-line character. `reached` is false for floors still sealed —
        /// those get a taunt instead of a spoiler.
        /// </summary>
        public static string Rule(int index, bool reached)
        {
            if (!reached) return "Sealed. Whatever it does, it hasn't done it to you yet.";
            if (_cache.TryGetValue(index, out var cached)) return cached;

            string line = null;
            try
            {
                var level = Levels.Get(index);
                // The floor's signature is the rule it leans on most. A floor with three
                // dark rooms IS the dark floor; one with none is an honest corridor, and
                // saying so is itself information.
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

            line ??= "No rule. Just the traps, and whatever you assume about them.";
            _cache[index] = line;
            return line;
        }
    }
}
