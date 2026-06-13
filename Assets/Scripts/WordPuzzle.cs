using System.Collections.Generic;

namespace WordBloom
{
    public enum SubmitResult
    {
        Found,        // a target word, newly found
        AlreadyFound, // target word found before
        Bonus,        // valid bonus word -> coins
        Invalid       // not a word in this level
    }

    /// <summary>
    /// Pure logic for one Word-Cookies-style level: which target words have
    /// been found, scoring, and hints. No Unity code, so it can be reasoned
    /// about and unit-tested on its own.
    /// </summary>
    public class WordPuzzle
    {
        public Level Level { get; }
        public HashSet<string> Found { get; } = new HashSet<string>();
        public HashSet<string> FoundBonus { get; } = new HashSet<string>();

        public WordPuzzle(Level level) { Level = level; }

        public int TargetCount => Level.Targets.Count;
        public int FoundCount => Found.Count;
        public bool IsComplete => Found.Count >= Level.Targets.Count;

        /// <summary>Can this word be spelled from the wheel letters at all?</summary>
        public bool CanSpell(string word)
        {
            var pool = new Dictionary<char, int>();
            foreach (char c in Level.Letters)
                pool[c] = pool.TryGetValue(c, out var n) ? n + 1 : 1;
            foreach (char c in word.ToUpperInvariant())
            {
                if (!pool.TryGetValue(c, out var n) || n == 0) return false;
                pool[c] = n - 1;
            }
            return true;
        }

        public SubmitResult Submit(string word)
        {
            word = word.ToUpperInvariant();
            if (Level.Targets.Contains(word))
                return Found.Add(word) ? SubmitResult.Found : SubmitResult.AlreadyFound;

            if (Level.Bonus.Contains(word) && CanSpell(word))
                return FoundBonus.Add(word) ? SubmitResult.Bonus : SubmitResult.AlreadyFound;

            return SubmitResult.Invalid;
        }

        /// <summary>
        /// For a hint: find a not-yet-found target and the index of its first
        /// still-hidden letter. Returns (word, letterIndex) or (null, -1).
        /// </summary>
        public (string word, int index) NextHint()
        {
            foreach (var w in Level.Targets)
                if (!Found.Contains(w))
                    return (w, 0);
            return (null, -1);
        }
    }
}
