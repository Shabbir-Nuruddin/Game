using System.Collections;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrustIssues
{
    /// <summary>
    /// Opens the REAL OS share sheet — the one with WhatsApp / Instagram / TikTok /
    /// Facebook / Bluetooth — instead of quietly copying text to a clipboard.
    ///
    /// Android is implemented in pure C# over AndroidJavaObject (ACTION_SEND), so
    /// it needs NO custom AndroidManifest and no FileProvider setup: nothing here
    /// can break the APK build. The image is published through MediaStore, which
    /// hands back a content:// URI the share sheet is allowed to read; if any part
    /// of that fails at runtime we fall back to sharing text + link, which always
    /// works. WebGL keeps using the existing Share.jslib bridge.
    /// </summary>
    public static class NativeShare
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TI_Share(string name, string b64, string text);
        [DllImport("__Internal")] static extern int TI_ShareLink(string url, string text);
#endif

        /// <summary>
        /// Share sheets and chat apps mangle typographic dashes, and the player
        /// asked for none in the shared message — so every brag string is flattened
        /// to plain ASCII punctuation before it leaves the game.
        /// </summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('—', '-')   // em dash
                    .Replace('–', '-')   // en dash
                    .Replace('‒', '-')   // figure dash
                    .Replace('−', '-')   // minus sign
                    .Replace('‘', '\'').Replace('’', '\'')
                    .Replace('“', '"').Replace('”', '"')
                    .Replace('…', '.')   // ellipsis
                    .Replace("  ", " ")
                    .Trim();
        }

        /// <summary>Share plain text + a link through the OS share sheet.</summary>
        public static void ShareText(string message, string url)
        {
            message = Sanitize(message);
            string payload = string.IsNullOrEmpty(url) ? message : message + "\n" + url;
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidSend(payload, null);
#elif UNITY_WEBGL && !UNITY_EDITOR
            try { TI_ShareLink(url ?? "", message); } catch { }
#else
            GUIUtility.systemCopyBuffer = payload;
            Debug.Log("[NativeShare] copied: " + payload);
#endif
        }

        /// <summary>
        /// Capture the current screen and share it as a real image with the brag
        /// text attached. This is what turns a death/win screen into something a
        /// player can actually post.
        /// </summary>
        public static IEnumerator ShareScreenshot(string filename, string message, string url)
        {
            yield return new WaitForEndOfFrame();      // a fully-rendered frame
            message = Sanitize(message);
            string payload = string.IsNullOrEmpty(url) ? message : message + "\n" + url;

            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] png = null;
            try { png = tex.EncodeToPNG(); } catch { }
            if (tex != null) Object.Destroy(tex);
            if (png == null) { ShareText(message, url); yield break; }

#if UNITY_ANDROID && !UNITY_EDITOR
            string path = System.IO.Path.Combine(Application.temporaryCachePath, filename + ".png");
            bool wrote = false;
            try { System.IO.File.WriteAllBytes(path, png); wrote = true; } catch { }
            AndroidSend(payload, wrote ? path : null);
#elif UNITY_WEBGL && !UNITY_EDITOR
            try { TI_Share(filename + ".png", System.Convert.ToBase64String(png), payload); } catch { }
#else
            Debug.Log($"[NativeShare] would share {filename}.png ({png.Length} bytes) - {payload}");
            GUIUtility.systemCopyBuffer = payload;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Build and fire an ACTION_SEND chooser. imagePath may be null for a
        /// text-only share; if publishing the image fails for any reason we still
        /// send the text rather than showing the player nothing.
        /// </summary>
        static void AndroidSend(string text, string imagePath)
        {
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");

                bool sentImage = false;
                if (!string.IsNullOrEmpty(imagePath))
                {
                    try
                    {
                        // MediaStore hands back a content:// URI that other apps are
                        // permitted to read — this is what avoids needing a
                        // FileProvider entry in a hand-written manifest.
                        using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                        using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                        string uriStr = media.CallStatic<string>("insertImage", resolver, imagePath,
                                                                 "Trust Issues", "Trust Issues");
                        if (!string.IsNullOrEmpty(uriStr))
                        {
                            using var uriClass = new AndroidJavaClass("android.net.Uri");
                            using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", uriStr);
                            intent.Call<AndroidJavaObject>("putExtra",
                                intentClass.GetStatic<string>("EXTRA_STREAM"), uri);
                            intent.Call<AndroidJavaObject>("setType", "image/png");
                            // Grant the receiving app one-shot read access.
                            intent.Call<AndroidJavaObject>("addFlags",
                                intentClass.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION"));
                            sentImage = true;
                        }
                    }
                    catch { /* fall through to text-only */ }
                }
                if (!sentImage) intent.Call<AndroidJavaObject>("setType", "text/plain");

                using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Share your death");
                activity.Call("startActivity", chooser);
            }
            catch (System.Exception e) { Debug.LogWarning("[NativeShare] " + e.Message); }
        }
#endif
    }
}
