using System;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// BLOOD SHARDS — the death economy. The castle bottles your failures and
    /// sells them back to you: every death pays a shard (capped per floor-visit
    /// so clearing always beats farming), floor clears pay more, and returning
    /// each day pays a "nightly tithe" scaled by the Blood Moon streak. Shards
    /// buy COSMETICS ONLY in the Crypt Shop (see Shop.cs) — never power. This is
    /// the roguelite lesson: a failed run must still move something forward.
    /// All local in PlayerPrefs, saved eagerly (WebGL flushes async on quit).
    /// </summary>
    public static class Currency
    {
        // Same UTC day-key math as Meta, so the tithe and the streak can never
        // disagree about what "today" is across a midnight rollover.
        static int Today => DateTime.UtcNow.Year * 10000 + DateTime.UtcNow.Month * 100 + DateTime.UtcNow.Day;

        public static int Balance => PlayerPrefs.GetInt("shards", 0);
        public static int LifetimeEarned => PlayerPrefs.GetInt("shards_earned", 0);

        /// <summary>Fired after every successful Earn (amount, source) — the HUD
        /// listens for its counter tick + pop.</summary>
        public static event Action<int, string> OnEarned;

        // ---- anti-farm: deaths only pay for the first few per floor-VISIT ----
        // Session-only counters (deliberately NOT persisted): a fresh visit to a
        // floor re-opens the payout window, but idle death-farming one floor dries
        // up after 5 shards — clearing (+10) is always strictly better.
        const int DeathPayCap = 5;
        static int _deathsPaidThisFloor;
        static bool _nearMissPaidThisFloor;

        /// <summary>Call when a NEW floor starts (not on death-respawn).</summary>
        public static void ResetFloorPayouts()
        {
            _deathsPaidThisFloor = 0;
            _nearMissPaidThisFloor = false;
            Charms.ResetFloor();   // the Grave Ward recharges once per floor
        }

        /// <summary>Shards owed for the death that just happened (0 once the
        /// per-floor window is spent). Near misses sting a coin sweeter — once.</summary>
        public static int DeathPayout(bool nearMiss)
        {
            int pay = 0;
            if (_deathsPaidThisFloor < DeathPayCap) { pay += 1; _deathsPaidThisFloor++; }
            if (nearMiss && !_nearMissPaidThisFloor) { pay += 2; _nearMissPaidThisFloor = true; }
            // The Gravedigger's Cut charm doubles what a death is worth. The
            // per-floor cap is applied BEFORE the multiplier, so the charm makes
            // dying more valuable without re-opening death-farming.
            return pay * Charms.DeathShardMultiplier;
        }

        public static void Earn(int amount, string source)
        {
            if (amount <= 0) return;
            PlayerPrefs.SetInt("shards", Balance + amount);
            PlayerPrefs.SetInt("shards_earned", LifetimeEarned + amount);
            PlayerPrefs.Save();
            Analytics.Track("currency_earned", new System.Collections.Generic.Dictionary<string, object>
            {
                { "source", source },
                { "amount", amount },
                { "balance", Balance },
            });
            OnEarned?.Invoke(amount, source);
        }

        /// <summary>False (and no change) if the balance can't cover it.</summary>
        public static bool Spend(int amount)
        {
            if (amount <= 0 || Balance < amount) return false;
            PlayerPrefs.SetInt("shards", Balance - amount);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>The NIGHTLY TITHE: first menu visit of each day pays 20 plus a
        /// streak bonus (up to +35). Returns the amount granted, 0 if already paid.</summary>
        public static int GrantDailyIfDue()
        {
            if (PlayerPrefs.GetInt("shards_lastday", 0) == Today) return 0;
            PlayerPrefs.SetInt("shards_lastday", Today);
            int amount = 20 + 5 * Mathf.Min(Meta.Streak, 7);
            Earn(amount, "daily_tithe");   // Earn saves prefs, including shards_lastday above
            Analytics.Track("currency_daily", new System.Collections.Generic.Dictionary<string, object>
            {
                { "amount", amount },
                { "streak", Meta.Streak },
            });
            return amount;
        }
    }
}
