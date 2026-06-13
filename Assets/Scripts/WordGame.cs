using System;
using System.Collections.Generic;

namespace MorningWords
{
    /// <summary>
    /// The result of comparing one guessed letter to the secret word.
    /// </summary>
    public enum LetterState
    {
        Empty,    // not guessed yet
        Correct,  // right letter, right position (green)
        Present,  // right letter, wrong position (yellow)
        Absent    // letter is not in the word (grey)
    }

    /// <summary>
    /// Pure game logic for a Wordle-style word puzzle. Deliberately has NO
    /// Unity code in it, so the rules can be reasoned about (and unit-tested)
    /// on their own. The UI layer (GameBootstrap) talks to this class.
    /// </summary>
    public class WordGame
    {
        public const int WordLength = 5;
        public const int MaxGuesses = 6;

        public string Secret { get; private set; }
        public int CurrentRow { get; private set; }
        public bool IsWon { get; private set; }
        public bool IsOver => IsWon || CurrentRow >= MaxGuesses;

        private readonly HashSet<string> _validWords;

        public WordGame(string secret, IEnumerable<string> validWords)
        {
            Secret = (secret ?? "").ToUpperInvariant();
            _validWords = new HashSet<string>();
            foreach (var w in validWords)
                _validWords.Add(w.ToUpperInvariant());
            // The secret is always a valid guess.
            _validWords.Add(Secret);
        }

        /// <summary>True if the word is in our dictionary (used to reject typos).</summary>
        public bool IsValidWord(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length != WordLength) return false;
            return _validWords.Contains(word.ToUpperInvariant());
        }

        /// <summary>
        /// Score a guess against the secret using standard Wordle rules,
        /// correctly handling duplicate letters.
        /// </summary>
        public LetterState[] Evaluate(string guess)
        {
            guess = guess.ToUpperInvariant();
            var result = new LetterState[WordLength];
            var secretLeft = new Dictionary<char, int>();

            // Count letters in the secret that aren't an exact match.
            for (int i = 0; i < WordLength; i++)
            {
                if (guess[i] == Secret[i])
                {
                    result[i] = LetterState.Correct;
                }
                else
                {
                    char c = Secret[i];
                    secretLeft[c] = secretLeft.TryGetValue(c, out var n) ? n + 1 : 1;
                }
            }

            // Anything not already Correct is Present (if letters remain) or Absent.
            for (int i = 0; i < WordLength; i++)
            {
                if (result[i] == LetterState.Correct) continue;
                char c = guess[i];
                if (secretLeft.TryGetValue(c, out var n) && n > 0)
                {
                    result[i] = LetterState.Present;
                    secretLeft[c] = n - 1;
                }
                else
                {
                    result[i] = LetterState.Absent;
                }
            }

            return result;
        }

        /// <summary>
        /// Submit a full-length, valid guess. Advances the row and sets IsWon.
        /// Returns the per-letter result for the UI to colour the tiles.
        /// </summary>
        public LetterState[] SubmitGuess(string guess)
        {
            if (IsOver) throw new InvalidOperationException("The game is already over.");
            if (!IsValidWord(guess)) throw new ArgumentException("Not a valid word.");

            var result = Evaluate(guess);
            CurrentRow++;
            if (guess.ToUpperInvariant() == Secret) IsWon = true;
            return result;
        }
    }
}
