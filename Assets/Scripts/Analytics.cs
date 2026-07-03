using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrustIssues
{
    /// <summary>
    /// Lightweight, fire-and-forget telemetry client. Events are queued in memory,
    /// persisted to PlayerPrefs (so a failed send survives a reload), and flushed in
    /// batches to our own analytics server over HTTP. No PII — just an anonymous
    /// per-device id and gameplay events. See /analytics for the server + dashboard.
    /// </summary>
    public static class Analytics
    {
        // ====== EDIT THIS ONE LINE when you deploy ======
        // Local testing (Unity Editor):  "http://localhost:3000/collect"
        // Live (after deploying to Render): "https://<your-app>.onrender.com/collect"
        public const string Endpoint = "https://trust-issues-analytics.onrender.com/collect";

        const float FlushInterval = 5f;   // seconds between sends
        const int MaxQueue = 400;          // hard cap so a long offline run can't grow forever
        const int PersistTail = 200;       // how many unsent events we keep across reloads
        const string DeviceKey = "analytics_device_id";
        const string QueueKey = "analytics_queue";

        static readonly List<string> _queue = new List<string>(); // each entry = one event JSON object
        static string _deviceId, _sessionId, _version;
        static bool _started;

        // The anonymous per-device id, shared with the echo system so players are
        // never shown their OWN tombstones.
        public static string DeviceId => _deviceId ?? "";

        public static void Init()
        {
            if (_started) return;
            _started = true;

            _deviceId = PlayerPrefs.GetString(DeviceKey, "");
            if (string.IsNullOrEmpty(_deviceId))
            {
                _deviceId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceKey, _deviceId);
                PlayerPrefs.Save();
            }
            _sessionId = Guid.NewGuid().ToString("N");
            _version = Application.version;

            // Reload any events that didn't make it out last time.
            var saved = PlayerPrefs.GetString(QueueKey, "");
            if (!string.IsNullOrEmpty(saved))
                foreach (var line in saved.Split('\n'))
                    if (!string.IsNullOrEmpty(line)) _queue.Add(line);

            // A hidden, persistent object drives the flush loop + final beacon.
            var go = new GameObject("Analytics");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AnalyticsRunner>();
        }

        /// <summary>Queue an event. Props values may be string/bool/int/float.</summary>
        public static void Track(string name, Dictionary<string, object> props = null)
        {
            if (!_started) return;

            var sb = new StringBuilder(160);
            sb.Append('{');
            Field(sb, "name", name); sb.Append(',');
            Field(sb, "session_id", _sessionId); sb.Append(',');
            Field(sb, "device_id", _deviceId); sb.Append(',');
            Field(sb, "app_version", _version); sb.Append(',');
            Field(sb, "ts", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            if (props != null && props.Count > 0)
            {
                sb.Append(",\"props\":{");
                bool first = true;
                foreach (var kv in props)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Value(sb, kv.Key, kv.Value);
                }
                sb.Append('}');
            }
            sb.Append('}');

            _queue.Add(sb.ToString());
            if (_queue.Count > MaxQueue) _queue.RemoveRange(0, _queue.Count - MaxQueue);
        }

        // Convenience overload for the common single-prop case.
        public static void Track(string name, string key, object value)
        {
            Track(name, new Dictionary<string, object> { { key, value } });
        }

        internal static IEnumerator Flush()
        {
            if (_queue.Count == 0) yield break;

            int sending = _queue.Count;
            string body = "[" + string.Join(",", _queue.GetRange(0, sending)) + "]";

            using (var req = new UnityWebRequest(Endpoint, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                // text/plain is a CORS "simple" request -> no preflight, works from any host.
                req.SetRequestHeader("Content-Type", "text/plain");
                req.timeout = 15;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                    _queue.RemoveRange(0, Mathf.Min(sending, _queue.Count)); // drop what we just sent
                // On failure we keep everything and retry next interval.
            }
            Persist();
        }

        // Save the unsent tail so a page reload / crash doesn't lose recent events.
        static void Persist()
        {
            int start = Mathf.Max(0, _queue.Count - PersistTail);
            var sb = new StringBuilder();
            for (int i = start; i < _queue.Count; i++)
            {
                if (i > start) sb.Append('\n');
                sb.Append(_queue[i]);
            }
            PlayerPrefs.SetString(QueueKey, sb.ToString());
            PlayerPrefs.Save();
        }

        // Best-effort final send when the tab closes (WebGL quit hooks are unreliable,
        // so we hand the batch to navigator.sendBeacon which the browser guarantees).
        internal static void FlushBeacon()
        {
            if (_queue.Count == 0) return;
            string body = "[" + string.Join(",", _queue) + "]";
#if UNITY_WEBGL && !UNITY_EDITOR
            try { AnalyticsBeacon(Endpoint, body); } catch { }
#endif
        }

        // ---- minimal JSON writers (escape strings; pass numbers/bools raw) ----
        static void Field(StringBuilder sb, string key, string val)
        {
            sb.Append('"').Append(key).Append("\":");
            WriteString(sb, val);
        }

        static void Value(StringBuilder sb, string key, object val)
        {
            sb.Append('"').Append(key).Append("\":");
            if (val is bool b) sb.Append(b ? "true" : "false");
            else if (val is int || val is long) sb.Append(Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture));
            else if (val is float f) sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (val is double d) sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else WriteString(sb, val == null ? "" : val.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void AnalyticsBeacon(string url, string body);
#endif
    }

    /// <summary>Drives the periodic flush and the on-close beacon.</summary>
    public class AnalyticsRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            var wait = new WaitForSecondsRealtime(5f);
            while (true)
            {
                yield return wait;
                yield return Analytics.Flush();
            }
        }

        void OnApplicationQuit() { Analytics.FlushBeacon(); }
        void OnApplicationFocus(bool focused) { if (!focused) Analytics.FlushBeacon(); }
        void OnApplicationPause(bool paused) { if (paused) Analytics.FlushBeacon(); }
    }
}
