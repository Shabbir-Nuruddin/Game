using System;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrustIssues
{
    /// <summary>
    /// Haunt a Friend — the marketing loop with teeth. After a run you can "curse"
    /// a link; anyone who opens the game through it finds YOUR red ghost waiting on
    /// the floor you died on, taunting them with your death. Clear that floor with
    /// fewer deaths and the curse is RETURNED (a counter-brag with a fresh link).
    /// Every share is a personal challenge that carries the game's URL with it.
    ///
    /// Wire format (?haunt=Base64Url of "1|nick|floor|deaths|cause|mode") — no
    /// server involved; decode is fully defensive so garbage params are ignored.
    /// </summary>
    public static class Curse
    {
        [Serializable]
        public class Data
        {
            public string nick; public int floor; public int deaths;
            public string cause; public string mode;
        }

        const string PendingKey = "ti_curse_pending";
        // Base for links when Application.absoluteURL is empty (editor / standalone).
        const string FallbackBase = "https://trust-issues.game/";

        /// <summary>The curse waiting to be broken (survives reloads), or null.</summary>
        public static Data Pending { get; private set; }
        /// <summary>Set when a curse is broken this run, for the result-screen counter-brag.</summary>
        public static Data LastBroken { get; private set; }
        /// <summary>New run, fresh slate for the counter-brag.</summary>
        public static void ClearBroken() => LastBroken = null;

        /// <summary>Load the persisted curse, then let a ?haunt= URL param override it.</summary>
        public static void Boot(string absoluteUrl)
        {
            Pending = Decode(PlayerPrefs.GetString(PendingKey, ""));
            string code = ParamFromUrl(absoluteUrl, "haunt");
            var fromUrl = Decode(code);
            if (fromUrl != null)
            {
                Pending = fromUrl;
                PlayerPrefs.SetString(PendingKey, code);
                PlayerPrefs.Save();
                Analytics.Track("curse_received", new System.Collections.Generic.Dictionary<string, object>
                {
                    { "from", fromUrl.nick }, { "floor", fromUrl.floor }, { "mode", fromUrl.mode },
                });
            }
        }

        /// <summary>The recipient beat it — remember it for the counter-brag and clear it.</summary>
        public static void MarkBroken()
        {
            LastBroken = Pending;
            Pending = null;
            PlayerPrefs.DeleteKey(PendingKey);
            PlayerPrefs.Save();
            Analytics.Track("curse_broken", new System.Collections.Generic.Dictionary<string, object>());
        }

        // ---- wire format ----

        public static string Encode(Data d)
        {
            string raw = "1|" + San(d.nick) + "|" + d.floor + "|" + d.deaths + "|" + San(d.cause) + "|" + San(d.mode);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public static Data Decode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            try
            {
                string b64 = code.Replace('-', '+').Replace('_', '/');
                while (b64.Length % 4 != 0) b64 += "=";
                var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|');
                if (parts.Length < 6 || parts[0] != "1") return null;
                var d = new Data
                {
                    nick = San(parts[1]),
                    floor = Mathf.Clamp(int.Parse(parts[2]), 0, 9999),
                    deaths = Mathf.Clamp(int.Parse(parts[3]), 0, 99999),
                    cause = San(parts[4]),
                    mode = San(parts[5]),
                };
                return string.IsNullOrEmpty(d.nick) ? null : d;
            }
            catch { return null; }   // malformed / hand-mangled → silently no curse
        }

        static string San(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("|", "").Replace("<", "").Replace(">", "").Trim();
            return s.Length > 24 ? s.Substring(0, 24) : s;
        }

        static string ParamFromUrl(string url, string name)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf('?');
            if (q < 0) return null;
            foreach (var pair in url.Substring(q + 1).Split('&', '#'))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && kv[0] == name) return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        /// <summary>The full curse link for the current page (editor gets the fallback host).</summary>
        public static string BuildLink(Data d)
        {
            string baseUrl = Application.absoluteURL;
            baseUrl = string.IsNullOrEmpty(baseUrl) ? FallbackBase : baseUrl.Split('?')[0].Split('#')[0];
            return baseUrl + "?haunt=" + Encode(d);
        }

        // ---- share bridge ----

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TI_ShareLink(string url, string text);
#endif
        /// <summary>Web Share the link (clipboard fallback in the browser AND in the editor).</summary>
        public static void ShareLink(string url, string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TI_ShareLink(url, text);
#else
            GUIUtility.systemCopyBuffer = text + " " + url;
#endif
        }
    }

    /// <summary>
    /// The cursing player's ghost: a red-tinted vampire that bobs near the spawn of
    /// the cursed floor, lazily drifts toward the player (never lethal — the menace
    /// is social, not mechanical) and cycles taunts about its own death.
    /// </summary>
    public class CurseGhost : MonoBehaviour
    {
        Curse.Data _d;
        TextMesh _tm;
        float _cycle;
        int _line;

        public void Init(Curse.Data d)
        {
            _d = d;
            var go = new GameObject("CurseTaunt");
            go.transform.SetParent(transform.parent, false);   // sibling: parent scale is the sprite size
            _tm = go.AddComponent<TextMesh>();
            _tm.fontSize = 46; _tm.characterSize = 0.05f;
            _tm.anchor = TextAnchor.LowerCenter; _tm.alignment = TextAlignment.Center;
            _tm.color = new Color(1f, 0.45f, 0.45f, 0.85f);
            go.GetComponent<MeshRenderer>().sortingOrder = 8;
            _tm.text = $"{_d.nick} haunts this floor";
        }

        string[] Taunts => new[]
        {
            string.IsNullOrEmpty(_d.cause) ? $"{_d.nick} died here. You'll do worse."
                                           : $"\"{_d.cause}\" — {_d.nick}'s last words.",
            $"{_d.deaths} deaths on this floor. Beat that or join me.",
            "The castle whispered your name to me.",
        };

        void Update()
        {
            var pl = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            if (pl != null)
            {
                // Lazy drift toward the player — presence, not threat.
                Vector3 to = pl.position - transform.position;
                if (to.magnitude > 2.2f)
                    transform.position += to.normalized * (1.1f * Time.deltaTime);
            }
            if (_tm != null)
            {
                _tm.transform.position = transform.position + Vector3.up * 0.85f;
                _cycle += Time.deltaTime;
                if (_cycle >= 4.5f)
                {
                    _cycle = 0f;
                    _tm.text = Taunts[_line++ % 3];
                }
            }
        }
    }
}
