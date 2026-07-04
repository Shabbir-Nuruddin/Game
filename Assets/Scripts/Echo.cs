using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace TrustIssues
{
    /// <summary>
    /// Death echoes — the asynchronous haunting layer. Every death is reported to
    /// the analytics backend with its position; when a floor is built, up to three
    /// tombstones of REAL other players who died there appear ("Heir-412 died
    /// here"), whispering their cause of death when you get close. Dark Souls
    /// bloodstain energy: the castle is haunted by actual strangers.
    ///
    /// Same fail-soft contract as Leaderboard: no server, no error, no markers —
    /// the game never waits on it. Results are cached per floor per session so a
    /// death-retry doesn't refetch (and doesn't suddenly show your own fresh grave).
    /// </summary>
    public static class Echo
    {
        [Serializable] public class Entry { public string nick; public float x; public float y; public string cause; }
        [Serializable] class Page { public Entry[] entries; }
        [Serializable] class Body
        {
            public string mode; public int level; public int day;
            public string nick; public string device_id;
            public float x; public float y; public string cause;
        }

        static readonly Dictionary<string, List<Entry>> _cache = new Dictionary<string, List<Entry>>();

        static EchoRunner _runner;
        static EchoRunner Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("Echo");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _runner = go.AddComponent<EchoRunner>();
                }
                return _runner;
            }
        }

        /// <summary>Fire-and-forget: this death becomes a tombstone in other players' games.</summary>
        public static void Report(string mode, int level, int day, Vector2 pos, string cause)
        {
            var b = new Body
            {
                mode = mode, level = level, day = day,
                nick = Meta.Nick, device_id = Analytics.DeviceId,
                x = pos.x, y = pos.y, cause = cause,
            };
            Runner.StartCoroutine(Post(b));
        }

        /// <summary>Fetch other players' deaths for a floor (session-cached, fail-soft).</summary>
        public static void Fetch(string mode, int level, int day, Action<List<Entry>> onResult)
        {
            string key = mode + "|" + level + "|" + day;
            if (_cache.TryGetValue(key, out var hit)) { onResult?.Invoke(hit); return; }
            Runner.StartCoroutine(Get(key, mode, level, day, onResult));
        }

        static IEnumerator Post(Body body)
        {
            using var req = new UnityWebRequest(Leaderboard.Host + "/echo", "POST");
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            // ignore result — echoes are best-effort
        }

        static IEnumerator Get(string key, string mode, int level, int day, Action<List<Entry>> onResult)
        {
            string url = $"{Leaderboard.Host}/echoes?mode={UnityWebRequest.EscapeURL(mode)}" +
                         $"&level={level}&day={day}&device={UnityWebRequest.EscapeURL(Analytics.DeviceId)}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 8;
            yield return req.SendWebRequest();
            var list = new List<Entry>();
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { var p = JsonUtility.FromJson<Page>(req.downloadHandler.text); if (p?.entries != null) list.AddRange(p.entries); }
                catch { /* malformed / route not live yet */ }
            }
            _cache[key] = list;
            onResult?.Invoke(list);
        }

        /// <summary>
        /// Place up to three tombstones from a fetched list. Cluster-deduped (no two
        /// within 1.5u) and kept clear of the spawn and the exit so a grave never
        /// blocks the read of the actual level.
        /// </summary>
        public static void SpawnMarkers(Transform parent, List<Entry> list, float spawnX, float endX,
                                        float minX, float maxX)
        {
            if (parent == null || list == null || list.Count == 0) return;
            var placed = new List<float>();
            foreach (var e in list)
            {
                if (placed.Count >= 3) break;
                // The server's X is from a floor we didn't necessarily generate (or
                // hostile data) — clamp into THIS floor's span. Clamp a local, not
                // e.x: entries live in the session cache and shouldn't be rewritten.
                // A far-out-of-bounds grave pins to the edge and then usually gets
                // dropped by the spawn/exit exclusion below — that's the intent.
                float x = Mathf.Clamp(e.x, minX + 0.6f, maxX - 0.6f);
                if (Mathf.Abs(x - spawnX) < 3f || Mathf.Abs(x - endX) < 3f) continue;
                bool crowded = false;
                foreach (float px in placed)
                    if (Mathf.Abs(x - px) < 1.5f) { crowded = true; break; }
                if (crowded) continue;
                placed.Add(x);
                BuildMarker(parent, e, x);
            }
        }

        static void BuildMarker(Transform parent, Entry e, float x)
        {
            // Deaths can happen mid-air (falls, bolts) — the grave still stands on
            // the ground near where they fell.
            float y = Mathf.Clamp(e.y, -2.15f, 4f);

            // Slab + a lighter cross on its face: reads "grave" at a glance, built
            // from the same box factory as everything else. The cross boxes are
            // SIBLINGS, not children — Theme.Box sizes via localScale, so parenting
            // a box to a box distorts it by the parent's scale.
            var slab = Theme.Box("EchoGrave", parent, new Vector2(x, y), new Vector2(0.52f, 0.66f),
                new Color(0.24f, 0.2f, 0.3f, 0.55f), 0);
            Theme.Box("EchoCrossV", parent, new Vector2(x, y + 0.1f), new Vector2(0.08f, 0.3f),
                new Color(0.5f, 0.45f, 0.6f, 0.5f), 0);
            Theme.Box("EchoCrossH", parent, new Vector2(x, y + 0.16f), new Vector2(0.2f, 0.07f),
                new Color(0.5f, 0.45f, 0.6f, 0.5f), 0);
            var fp = slab.AddComponent<FaintPulse>(); fp.min = 0.35f; fp.max = 0.6f; fp.speed = 3f;
            slab.AddComponent<EchoMarker>().Init(e.nick, e.cause);
        }

        class EchoRunner : MonoBehaviour
        {
            // If the runner object ever dies (editor teardown, stray cleanup),
            // clear the static so the lazy factory rebuilds instead of holding a
            // dead reference whose coroutines silently never run.
            void OnDestroy() { if (_runner == this) _runner = null; }
        }
    }

    /// <summary>
    /// The behaviour on a tombstone: when the player comes close, the fallen
    /// player's name (and what killed them) fades in above it with a soft whisper —
    /// once. Pure ambience, no collision.
    /// </summary>
    public class EchoMarker : MonoBehaviour
    {
        TextMesh _tm;
        bool _whispered;
        float _alpha;

        public void Init(string nick, string cause)
        {
            // Sibling of the slab (its localScale is the slab SIZE — children of a
            // Theme.Box inherit that scale and would render squashed).
            var go = new GameObject("EchoText");
            go.transform.SetParent(transform.parent, false);
            go.transform.position = transform.position + Vector3.up * 0.55f;
            _tm = go.AddComponent<TextMesh>();
            // Curse.San is the shared wire-text sanitizer — echo text comes from
            // the server, so the markup stripping is a free defensive win here.
            string line = $"{Curse.San(nick)} died here";
            if (!string.IsNullOrEmpty(cause)) line += "\n" + Curse.San(cause);
            _tm.text = line;
            _tm.fontSize = 48; _tm.characterSize = 0.045f;
            _tm.anchor = TextAnchor.LowerCenter; _tm.alignment = TextAlignment.Center;
            _tm.color = new Color(0.75f, 0.8f, 1f, 0f);
            go.GetComponent<MeshRenderer>().sortingOrder = 6;
        }

        void Update()
        {
            var pl = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            bool near = pl != null && Vector2.Distance(pl.position, transform.position) < 1.7f;
            _alpha = Mathf.MoveTowards(_alpha, near ? 0.9f : 0f, Time.deltaTime * 3f);
            if (_tm != null) { var c = _tm.color; c.a = _alpha; _tm.color = c; }
            if (near && !_whispered)
            {
                _whispered = true;
                Audio.PlayOr("whisper", "click", 0.25f);
                Fx.Ring(transform.position, new Color(0.6f, 0.7f, 1f, 0.4f), 1.2f, 0.4f);
            }
        }
    }
}
