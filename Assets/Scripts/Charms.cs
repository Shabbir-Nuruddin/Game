using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// CHARMS — the answer to "what are the coins actually FOR".
    ///
    /// The shop was cosmetics-only on purpose ("shards buy style, never power"),
    /// which is a defensible rule for a rage game and also the reason the currency
    /// felt pointless: a skin is a way to show off, and showing off needs an
    /// audience the game doesn't have yet.
    ///
    /// Charms give shards real utility WITHOUT cheapening the challenge, by
    /// following the same rule that fixed Blood Moon: punish the mistake, never
    /// punish the replay. Every charm here removes TEDIUM (re-walking cleared
    /// ground, fighting the camera in the dark, waiting out a curse) or pays you
    /// back faster. None of them make a trap stop killing you.
    ///
    /// Exactly ONE charm can be worn at a time, so buying more is about choosing a
    /// playstyle rather than stacking advantages — the collection stays meaningful
    /// instead of becoming a power ladder.
    /// </summary>
    public static class Charms
    {
        public class Def
        {
            public string id;
            public string name;
            public string desc;      // what it does, in player language
            public int price;        // in blood shards
            public int reqFloors;    // castle floors you must have cleared first
            public Color tint;
        }

        /// <summary>
        /// Ordered cheapest-first so the shop reads as a ladder. Floor requirements
        /// are the "how many levels do I finish to unlock things" answer: shards
        /// alone can't buy the late charms, so grinding deaths can never skip the
        /// game. You need both the money AND the progress.
        /// </summary>
        public static readonly List<Def> All = new()
        {
            new Def { id = "charm_gravedigger", name = "Gravedigger's Cut", reqFloors = 3, price = 150,
                      desc = "Your deaths pay DOUBLE shards.", tint = new Color(0.85f, 0.70f, 0.25f) },
            new Def { id = "charm_wings", name = "Tattered Wing", reqFloors = 8, price = 260,
                      desc = "Your bat glide lasts much longer.", tint = new Color(0.55f, 0.40f, 0.80f) },
            new Def { id = "charm_candle", name = "Guttering Candle", reqFloors = 12, price = 300,
                      desc = "You see further when the lights go out.", tint = new Color(0.95f, 0.80f, 0.40f) },
            new Def { id = "charm_steady", name = "Steady Hands", reqFloors = 16, price = 340,
                      desc = "Reversed controls wear off twice as fast.", tint = new Color(0.40f, 0.75f, 0.85f) },
            new Def { id = "charm_ward", name = "Grave Ward", reqFloors = 20, price = 600,
                      desc = "Survive the FIRST death on every floor. Once per floor.",
                      tint = new Color(0.80f, 0.25f, 0.30f) },
        };

        public static Def Get(string id) => All.Find(c => c.id == id);

        // ---- ownership + progression gate -------------------------------------

        /// <summary>Castle floors the player has actually cleared.</summary>
        public static int FloorsCleared => Mathf.Clamp(PlayerPrefs.GetInt("castle_unlocked", 0), 0, Levels.Count);

        public static bool Owns(string id) => PlayerPrefs.GetInt("own_" + id, 0) == 1;

        /// <summary>Progress gate — separate from affordability, so the shop can
        /// say "locked until floor 8" instead of just greying a button out.</summary>
        public static bool Unlocked(Def d) => d != null && FloorsCleared >= d.reqFloors;

        public static bool Buy(Def d)
        {
            if (d == null || Owns(d.id) || !Unlocked(d)) return false;
            if (!Currency.Spend(d.price)) return false;
            PlayerPrefs.SetInt("own_" + d.id, 1);
            PlayerPrefs.Save();
            Analytics.Track("charm_bought", new Dictionary<string, object>
            { { "id", d.id }, { "price", d.price }, { "floors", FloorsCleared } });
            return true;
        }

        // ---- equipping (exactly one) ------------------------------------------

        public static string EquippedId
        {
            get => PlayerPrefs.GetString("charm_equipped", "");
            set { PlayerPrefs.SetString("charm_equipped", value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Wear a charm, or pass the worn id / "" to take it off.</summary>
        public static void Equip(string id)
        {
            EquippedId = (id == EquippedId) ? "" : (id ?? "");
            Analytics.Track("charm_equipped", new Dictionary<string, object> { { "id", EquippedId } });
        }

        public static bool IsWorn(string id) => !string.IsNullOrEmpty(id) && EquippedId == id;

        // ---- the actual effects ------------------------------------------------
        // Queried from gameplay. Each is a single cheap PlayerPrefs string compare,
        // so these are safe to call from hot paths.

        /// <summary>Multiplier applied to shards earned from dying.</summary>
        public static int DeathShardMultiplier => IsWorn("charm_gravedigger") ? 2 : 1;

        /// <summary>Extra bat-glide time, as a multiplier on the flight meter.</summary>
        public static float GlideMultiplier => IsWorn("charm_wings") ? 1.6f : 1f;

        /// <summary>How much of the screen stays lit when a room goes dark.</summary>
        public static float DarkVisionMultiplier => IsWorn("charm_candle") ? 1.55f : 1f;

        /// <summary>Scales how long a reversed-controls curse lasts.</summary>
        public static float ReverseMultiplier => IsWorn("charm_steady") ? 0.5f : 1f;

        /// <summary>True if the Grave Ward can still eat a death on this floor.</summary>
        public static bool WardReady => IsWorn("charm_ward") && !_wardSpentThisFloor;
        static bool _wardSpentThisFloor;

        /// <summary>Call when a NEW floor begins (not on a death-respawn).</summary>
        public static void ResetFloor() => _wardSpentThisFloor = false;

        /// <summary>
        /// Consume the ward. Returns true if it absorbed the death — the caller
        /// must then NOT kill the player.
        /// </summary>
        public static bool ConsumeWard()
        {
            if (!WardReady) return false;
            _wardSpentThisFloor = true;
            Analytics.Track("ward_used", new Dictionary<string, object>());
            return true;
        }

        /// <summary>The cheapest thing the player can't afford yet — drives the
        /// "next unlock" teaser so there's always a named goal.</summary>
        public static Def NextGoal()
        {
            Def best = null;
            foreach (var d in All)
            {
                if (Owns(d.id)) continue;
                if (best == null || d.price < best.price) best = d;
            }
            return best;
        }
    }
}
