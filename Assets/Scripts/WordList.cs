using System;
using System.Collections.Generic;

namespace MorningWords
{
    /// <summary>
    /// A small starter dictionary of common, friendly 5-letter words.
    /// Deliberately avoids obscure or upsetting words — this is a calm game.
    /// Later we can load a much bigger list from a text file instead.
    /// </summary>
    public static class WordList
    {
        public static readonly string[] Words =
        {
            "APPLE", "BEACH", "BREAD", "CHAIR", "CLOUD", "DANCE", "DREAM", "EARTH",
            "FLOUR", "GRACE", "GREEN", "HEART", "HONEY", "HOUSE", "LEMON", "LIGHT",
            "MUSIC", "OCEAN", "PEACE", "PLANT", "QUIET", "RIVER", "SMILE", "SUGAR",
            "SUNNY", "SWEET", "TABLE", "TIGER", "TOAST", "WATER", "WHEAT", "BERRY",
            "CANDY", "DAISY", "EAGLE", "FROST", "GLOVE", "HAPPY", "IVORY", "JELLY",
            "KOALA", "LILAC", "MAPLE", "NIGHT", "OLIVE", "PEARL", "ROBIN", "SHINE",
            "SPICE", "STORY", "THYME", "TULIP", "VOICE", "WALTZ", "YEAST"
        };

        /// <summary>
        /// Pick the word of the day deterministically from the date, so every
        /// player gets the SAME puzzle on the same day (this is what makes a
        /// daily game shareable, like Wordle).
        /// </summary>
        public static string WordForDate(DateTime date)
        {
            // Days since a fixed launch date -> stable index into the list.
            var epoch = new DateTime(2026, 1, 1);
            int dayNumber = (int)(date.Date - epoch).TotalDays;
            int index = ((dayNumber % Words.Length) + Words.Length) % Words.Length;
            return Words[index];
        }

        public static IEnumerable<string> All => Words;
    }
}
