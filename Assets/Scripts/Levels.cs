using System.Collections.Generic;

namespace WordBloom
{
    /// <summary>One puzzle: the wheel letters, and the words to find.</summary>
    public class Level
    {
        public string Letters;            // the letters on the wheel, e.g. "PLANTS"
        public List<string> Targets;      // words that fill the board
        public HashSet<string> Bonus;     // valid extra words -> bonus coins

        public Level(string letters, string[] targets, string[] bonus = null)
        {
            Letters = letters.ToUpperInvariant();
            Targets = new List<string>();
            foreach (var w in targets) Targets.Add(w.ToUpperInvariant());
            Targets.Sort((a, b) => a.Length != b.Length ? a.Length - b.Length
                                                        : string.CompareOrdinal(a, b));
            Bonus = new HashSet<string>();
            if (bonus != null) foreach (var w in bonus) Bonus.Add(w.ToUpperInvariant());
        }
    }

    /// <summary>
    /// Hand-authored starter levels. The difficulty curve is gentle on purpose:
    /// few short words first, growing slowly. (Later we can auto-generate these
    /// from a big dictionary; hand-made levels just feel better to start.)
    /// </summary>
    public static class Levels
    {
        public static readonly List<Level> All = new List<Level>
        {
            new Level("CAT",  new[]{"AT","CAT","ACT"}),
            new Level("STAR", new[]{"AS","ART","RAT","TAR","STAR","ARTS","RATS","TARS"}),
            new Level("PLANT", new[]{"AT","TAN","NAP","PAN","PLAN","PLANT","PANT"},
                               new[]{"LAP","ANT","NAB"}),
            new Level("GARDEN", new[]{"AGE","RAGE","GEAR","DEAR","READ","DARE","GRADE","GARDEN"},
                                new[]{"RANGE","ANGER","DANGER","GENDER"}),
            new Level("FLOWER", new[]{"OWE","LOW","ROW","FLOW","WOLF","FOWL","LOWER","FLOWER"},
                                new[]{"WORE","ROLE","FLEW","FORE"}),
            new Level("BLOOM", new[]{"MOB","LOB","BOO","LOOM","BOOM","BLOOM"},
                               new[]{"MOO","OBOL"}),
            new Level("SUNNY", new[]{"SUN","NUN","UNS","SUNNY"}),
            new Level("BREEZE", new[]{"BEE","ERE","REE","BREE","BEER","BREEZE"},
                                new[]{"ZEBRA"}),
            new Level("ORCHARD", new[]{"CAR","CHAR","CORD","CARD","ROAD","ORCHARD","HOARD"},
                                 new[]{"CHORD","ARCH","HARD","ROACH"}),
            new Level("HARVEST", new[]{"EAT","SEA","HAVE","SAVE","VASE","STAR","HEART","HARVEST"},
                                 new[]{"HEARS","RATES","STARE","SHARE","HASTE"}),
        };

        public static int Count => All.Count;

        public static Level Get(int index)
        {
            if (index < 0) index = 0;
            // Loop back to the start once the hand-made levels run out, so the
            // game never dead-ends during early testing.
            return All[index % All.Count];
        }
    }
}
