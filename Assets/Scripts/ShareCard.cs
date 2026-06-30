using System.Collections;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrustIssues
{
    /// <summary>
    /// Turns the current result screen into a real shareable PNG (instead of a
    /// "screenshot this" label): captures the frame, then hands it to the browser's
    /// Web Share API — or downloads it + copies the brag text as a fallback.
    /// The result overlay is designed to read as a gothic "result card", so the
    /// captured frame IS the card. Native TTS/share live in Plugins/Share.jslib.
    /// </summary>
    public static class ShareCard
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TI_Share(string name, string b64, string text);
#endif

        public static IEnumerator CaptureAndShare(string filename, string brag)
        {
            yield return new WaitForEndOfFrame();   // capture a fully-rendered frame
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] png = tex.EncodeToPNG();
            Object.Destroy(tex);
#if UNITY_WEBGL && !UNITY_EDITOR
            TI_Share(filename, System.Convert.ToBase64String(png), brag);
#else
            Debug.Log($"[ShareCard] would share {filename} ({png.Length} bytes) — {brag}");
#endif
        }
    }
}
