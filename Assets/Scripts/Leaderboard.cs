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
    ///        - endless:      value = floor reached (HIGHER is better)
    ///        store the BEST value per (nick, mode[, day]).
    ///   GET  {Host}/leaderboard?mode=daily&scope=today|all  -> { "entries":[ {"nick","value"} ... ] }
    ///        sorted best-first, top ~20.
    /// </summary>
    public static class Leaderboard
    {
        // Same host as Analytics.Endpoint (just different paths).
        public const string Host = "https://trust-issues-analytics.onrender.com";

        [Serializable] public class Entry { public string nick; public int value; }
        [Serializable] class Page { public Entry[] entries; }
        [Serializable] class ScoreBody { public string mode; public string nick; public int value; public int day; }

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
            var body = new ScoreBody { mode = mode, nick = Meta.Nick, value = value,
                day = DateTime.UtcNow.Year * 10000 + DateTime.UtcNow.Month * 100 + DateTime.UtcNow.Day };
            Runner.StartCoroutine(PostScore(body));
        }

        public static void Fetch(string mode, string scope, Action<List<Entry>> onResult)
        {
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
            onResult?.Invoke(list);
        }

        class LbRunner : MonoBehaviour
        {
            // Same contract as EchoRunner: a destroyed runner must clear the
            // static so the lazy factory can rebuild it.
            void OnDestroy() { if (_runner == this) _runner = null; }
        }
    }
}
