using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// The castle remembers YOU. Local haunting state that makes returning (or
    /// leaving) feel *noticed*:
    ///  - absence tracking: the menu greets you differently after hours/days away,
    ///    name-dropping whatever killed you last
    ///  - rage-quit detection: close the tab mid-run and the castle calls it out
    ///    next session
    ///  - nemesis: the trap that's killed you most (5+ lifetime) wears a little
    ///    gold crown in levels and taunts you with its kill streak on death
    /// All PlayerPrefs, zero server. Session facts (absence, rage-quit) are
    /// snapshotted once in SessionStart before anything overwrites them.
    /// </summary>
    public static class Memory
    {
        const string LastSeenKey  = "ti_lastseen";        // unix seconds, heartbeat-updated
        const string LastKillerKey = "ti_lastkiller_tag"; // (int)TrapType of the last tagged kill
        const string InRunKey     = "ti_inrun";           // 1 while mid-run; still 1 at boot = rage-quit
        const string NemLastKey   = "ti_nemlast";         // last tagged killer (streak tracking)
        const string NemStreakKey = "ti_nemstreak";       // consecutive kills by that trap

        static bool _sessionStarted, _wasRageQuit;
        static double _absenceHours = -1.0;
        static int _nemesisCache = -2;                    // -2 = not computed yet
        static int _lastKillFrame = -1;                   // roast decoration only right after a tagged kill

        static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Snapshot absence + rage-quit BEFORE this session overwrites them. Call once at boot.</summary>
        public static void SessionStart()
        {
            if (_sessionStarted) return;
            _sessionStarted = true;
            long last = 0;
            long.TryParse(PlayerPrefs.GetString(LastSeenKey, "0"), out last);
            _absenceHours = last > 0 ? (Now - last) / 3600.0 : -1.0;
            _wasRageQuit = PlayerPrefs.GetInt(InRunKey, 0) == 1;
            PlayerPrefs.SetInt(InRunKey, 0);
            Touch();   // Touch() flushes, which also persists the rage-quit reset above —
                       // without it a single rage-quit greets the player forever.
        }

        /// <summary>
        /// "Still here" — called from the gameplay heartbeat. Saves every call: on
        /// WebGL prefs only reach IndexedDB on Save(), and tab close (this game's
        /// primary exit) is exactly the moment a deferred save can never capture.
        /// The absence greeting tiers depend on last-seen being fresh at that moment.
        /// </summary>
        public static void Touch()
        {
            PlayerPrefs.SetString(LastSeenKey, Now.ToString());
            PlayerPrefs.Save();
        }

        public static void RunStarted()      { PlayerPrefs.SetInt(InRunKey, 1); PlayerPrefs.Save(); }
        public static void RunEndedCleanly() { PlayerPrefs.SetInt(InRunKey, 0); PlayerPrefs.Save(); }

        /// <summary>
        /// Called by KillZone on every kill it lands (tag &lt; 0 = untagged: breaks any
        /// streak but tallies nothing). Feeds the lifetime tally + the streak.
        /// </summary>
        public static void RecordKill(int tag)
        {
            _lastKillFrame = Time.frameCount;
            if (tag < 0)
            {
                // Untagged kill: breaks the streak but tallies nothing.
                PlayerPrefs.SetInt(NemLastKey, -1);
                PlayerPrefs.SetInt(NemStreakKey, 0);
            }
            else
            {
                PlayerPrefs.SetInt(LastKillerKey, tag);
                string k = "ti_kill_" + tag;
                PlayerPrefs.SetInt(k, PlayerPrefs.GetInt(k, 0) + 1);
                int streak = PlayerPrefs.GetInt(NemLastKey, -1) == tag ? PlayerPrefs.GetInt(NemStreakKey, 0) + 1 : 1;
                PlayerPrefs.SetInt(NemLastKey, tag);
                PlayerPrefs.SetInt(NemStreakKey, streak);
                _nemesisCache = -2;   // tallies changed
            }
            PlayerPrefs.Save();   // nemesis data must survive a tab close (WebGL)
        }

        /// <summary>Display name of whatever tagged trap killed you last ("" if none yet).</summary>
        public static string LastKillerName
        {
            get
            {
                int k = PlayerPrefs.GetInt(LastKillerKey, -1);
                return k >= 0 ? Codex.Title((TrapType)k) : "";
            }
        }

        /// <summary>The trap that's killed you most (needs 5+ lifetime kills), as (int)TrapType; -1 = none.</summary>
        public static int Nemesis
        {
            get
            {
                if (_nemesisCache != -2) return _nemesisCache;
                int best = -1, bestN = 4;   // must beat 4 → at least 5 kills
                foreach (TrapType t in System.Enum.GetValues(typeof(TrapType)))
                {
                    int n = PlayerPrefs.GetInt("ti_kill_" + (int)t, 0);
                    if (n > bestN) { bestN = n; best = (int)t; }
                }
                return _nemesisCache = best;
            }
        }

        /// <summary>Append the nemesis scoreline to a death roast (only right after ITS kill).</summary>
        public static string DecorateRoast(string msg)
        {
            if (Time.frameCount != _lastKillFrame) return msg;
            int tag = PlayerPrefs.GetInt(NemLastKey, -1);
            int streak = PlayerPrefs.GetInt(NemStreakKey, 0);
            if (tag >= 0 && tag == Nemesis && streak >= 2)
                return $"{msg}  {Codex.Title((TrapType)tag)}: {streak} in a row. You: 0.";
            return msg;
        }

        /// <summary>One line for the menu: absence / rage-quit / nemesis flavour. Null = say nothing.</summary>
        public static string MenuGreeting()
        {
            if (_wasRageQuit)
                return "You slammed the door mid-run last time. The Castle noticed.";

            int killer = PlayerPrefs.GetInt(LastKillerKey, -1);
            string killerName = killer >= 0 ? Codex.Title((TrapType)killer) : null;

            if (_absenceHours >= 72)
            {
                int days = (int)(_absenceHours / 24.0);
                return killerName != null
                    ? $"Gone {days} days. {killerName} kept your spot warm."
                    : $"Gone {days} days. The Castle counted every one.";
            }
            if (_absenceHours >= 20)
                return killerName != null
                    ? $"Back so soon? {killerName} missed you."
                    : "The candles stayed lit for you. Sit. Die a while.";
            if (_absenceHours >= 6)
                return "A few hours away and the bats already forgot your face. Remind them.";
            return null;   // barely left — the castle plays it cool
        }
    }
}
