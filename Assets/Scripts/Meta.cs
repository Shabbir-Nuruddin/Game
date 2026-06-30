using System;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Light meta-progression: the Blood Moon daily streak (the "come back tomorrow"
    /// hook) and the player's display name (used for the leaderboard + Versus). All
    /// local in PlayerPrefs.
    /// </summary>
    public static class Meta
    {
        static int Today => DateTime.UtcNow.Year * 10000 + DateTime.UtcNow.Month * 100 + DateTime.UtcNow.Day;
        static int DayKey(DateTime d) => d.Year * 10000 + d.Month * 100 + d.Day;

        public static int Streak => PlayerPrefs.GetInt("bm_streak", 0);

        /// <summary>Call when the player engages Blood Moon today. Advances the streak
        /// if yesterday was played, keeps it if already played today, else resets to 1.</summary>
        public static void RecordDailyPlay()
        {
            int last = PlayerPrefs.GetInt("bm_lastday", 0);
            if (last == Today) return;                       // already counted today
            int streak = PlayerPrefs.GetInt("bm_streak", 0);
            int yesterday = DayKey(DateTime.UtcNow.AddDays(-1).Date);
            streak = (last == yesterday) ? streak + 1 : 1;   // continued vs broken
            PlayerPrefs.SetInt("bm_streak", streak);
            PlayerPrefs.SetInt("bm_lastday", Today);
            PlayerPrefs.Save();
        }

        /// <summary>True if the streak is still alive going into today (played today or yesterday).</summary>
        public static bool StreakAlive
        {
            get
            {
                int last = PlayerPrefs.GetInt("bm_lastday", 0);
                return last == Today || last == DayKey(DateTime.UtcNow.AddDays(-1).Date);
            }
        }

        public static string Nick
        {
            get
            {
                string n = PlayerPrefs.GetString("ti_nick", "");
                if (string.IsNullOrEmpty(n)) { n = "Heir-" + UnityEngine.Random.Range(100, 999); Nick = n; }
                return n;
            }
            set { PlayerPrefs.SetString("ti_nick", value); PlayerPrefs.Save(); }
        }
    }
}
