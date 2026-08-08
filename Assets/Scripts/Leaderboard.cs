using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace TrustIssues
{
    /// <summary>
    /// Online leaderboard client. Reuses the existing analytics host. It fails
    /// SILENTLY until you add the two routes to that backend, so the game is never
    /// blocked on it.
    ///
    /// Backend contract (add to the analytics Express/Postgres app):
    ///   POST {Host}/score
    ///        body: { "mode":"daily|endless|castle", "nick":"Heir-123",
    ///                "value": 12, "day": 20260621 }
    ///        - daily/castle: value = deaths (LOWER is better)
    ///        - endless:      value = distance in metres (HIGHER is better)
    ///        store the BEST value per (nick, mode[, day]).
    ///   GET  {Host}/leaderboard?mode=daily&scope=today|all  -> { "entries":[ {"nick","value"} ... ] }
    ///        sorted best-first, top ~20.
    /// </summary>
    public static class Leaderboard
    {
        // Same host as Analytics.Endpoint (just different paths).
        public const string Host = "https://trust-issues-analytics.onrender.com";

        [Serializable] public class Entry
        {
            public string nick; public int value;
            public bool ghost;   // one of the castle's own dead, not a real player
            public bool you;     // this row is the person holding the phone
        }
        [Serializable] class Page { public Entry[] entries; }
        [Serializable] class ScoreBody { public string mode; public string nick; public int value; public int day; }

        // ── THE HOUSE DEAD ───────────────────────────────────────────────────
        // An empty leaderboard is worse than no leaderboard: it tells a new
        // player that nobody is here and nothing they do will be measured. So
        // every board ships with ten standing scores to hunt.
        //
        // These are NOT fake players and must never be dressed up as any. They
        // are named characters of the castle, they carry a ghost flag, and the
        // board draws them dimmed under a dagger with the heading saying whose
        // scores they are. Passing invented humans off as a real ranking would
        // be a lie to the player and a false claim on the store listing; a
        // house rival to beat is an honest, ancient piece of game design (the
        // arcade default-initials table did exactly this).
        //
        // The spread is deliberate: the weakest is beatable in a first sitting
        // so the board immediately does something, and the strongest sits past
        // where a good player lands so it stays a target for weeks.
        static readonly string[] GhostNames =
        {
            "Lord Vasile", "The Pale Countess", "Brother Mordant", "Ilinca the Thrice-Fallen",
            "Grigore", "The Warden of Ash", "Sister Vespera", "Old Dracul",
            "The Gravekeeper", "Nameless Heir",
        };
        // endless = metres survived (higher is better); castle/daily = deaths (lower is better).
        static readonly int[] GhostEndless = { 1480, 1120, 905, 760, 640, 525, 430, 340, 245, 130 };
        static readonly int[] GhostCastle  = {  143,  178,  216,  259,  305,  368,  431,  504,  568,  612 };
        static readonly int[] GhostDaily   = {   11,   16,   21,   27,   33,   40,   48,   55,   61,   68 };

        /// <summary>True when a BIGGER number wins (Endless distance); false when the
        /// board ranks on fewest deaths.</summary>
        public static bool HigherIsBetter(string mode) => mode == "endless";

        static int Today => DateTime.UtcNow.Year * 10000 + DateTime.UtcNow.Month * 100 + DateTime.UtcNow.Day;

        // Blood Moon is TONIGHT's board, so its personal best is keyed to the day
        // and a new night starts everyone level again. The other two are all-time.
        static string BestKey(string mode) => mode == "daily" ? $"lb_best_daily_{Today}" : "lb_best_" + mode;

        /// <summary>The player's own best for this board, or -1 if they've never scored.</summary>
        public static int LocalBest(string mode) => PlayerPrefs.GetInt(BestKey(mode), -1);

        static void RecordLocal(string mode, int value)
        {
            int prev = LocalBest(mode);
            bool better = prev < 0 || (HigherIsBetter(mode) ? value > prev : value < prev);
            if (!better) return;
            PlayerPrefs.SetInt(BestKey(mode), value);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The board as the player should see it: the house dead, any real online
        /// entries, and the player's own best — sorted, and with their row flagged
        /// so the UI can point at it. Works with no server and no connection,
        /// which is the state the game actually ships in.
        /// </summary>
        public static List<Entry> Board(string mode, List<Entry> online = null)
        {
            var list = new List<Entry>();
            if (online != null)
                foreach (var e in online)
                    if (e != null && !string.IsNullOrEmpty(e.nick)) list.Add(e);

            int[] values = mode == "endless" ? GhostEndless : mode == "castle" ? GhostCastle : GhostDaily;
            for (int i = 0; i < GhostNames.Length && i < values.Length; i++)
                list.Add(new Entry { nick = GhostNames[i], value = values[i], ghost = true });

            int mine = LocalBest(mode);
            if (mine >= 0) list.Add(new Entry { nick = Meta.Nick, value = mine, you = true });

            list.Sort((a, b) => HigherIsBetter(mode)
                ? b.value.CompareTo(a.value)     // furthest first
                : a.value.CompareTo(b.value));   // fewest deaths first
            return list;
        }

        /// <summary>Where the player sits on that board (1-based), or 0 if unranked.</summary>
        public static int MyRank(List<Entry> board)
        {
            for (int i = 0; i < board.Count; i++) if (board[i].you) return i + 1;
            return 0;
        }

        static LbRunner _runner;
        static LbRunner Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("Leaderboard");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _runner = go.AddComponent<LbRunner>();
                }
                return _runner;
            }
        }

        public static void Submit(string mode, int value)
        {
            // ALWAYS record locally first. The board has to work on a plane, in a
            // dead zone, and — right now — with no server behind it at all.
            RecordLocal(mode, value);
            var body = new ScoreBody { mode = mode, nick = Meta.Nick, value = value, day = Today };
            // No server, no request. See Analytics.ServerLive.
            if (!Analytics.ServerLive) return;
            Runner.StartCoroutine(PostScore(body));
        }

        public static void Fetch(string mode, string scope, Action<List<Entry>> onResult)
        {
            // Offline or no server: hand back the local board immediately rather
            // than an empty list. The caller gets the same shape either way.
            if (!Analytics.ServerLive) { onResult?.Invoke(Board(mode)); return; }
            Runner.StartCoroutine(GetPage(mode, scope, onResult));
        }

        static IEnumerator PostScore(ScoreBody body)
        {
            using var req = new UnityWebRequest(Host + "/score", "POST");
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            // ignore result — leaderboard is best-effort
        }

        static IEnumerator GetPage(string mode, string scope, Action<List<Entry>> onResult)
        {
            string url = $"{Host}/leaderboard?mode={UnityWebRequest.EscapeURL(mode)}&scope={UnityWebRequest.EscapeURL(scope)}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 8;
            yield return req.SendWebRequest();
            var list = new List<Entry>();
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { var p = JsonUtility.FromJson<Page>(req.downloadHandler.text); if (p?.entries != null) list.AddRange(p.entries); }
                catch { /* malformed / route not live yet */ }
            }
            // Merge whatever the server gave us with the house dead and the
            // player's own best, so a slow or half-empty server still shows a
            // board worth looking at.
            onResult?.Invoke(Board(mode, list));
        }

        class LbRunner : MonoBehaviour
        {
            // Same contract as EchoRunner: a destroyed runner must clear the
            // static so the lazy factory can rebuild it.
            void OnDestroy() { if (_runner == this) _runner = null; }
        }
    }
}
