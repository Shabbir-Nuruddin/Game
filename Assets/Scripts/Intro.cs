using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TrustIssues
{
    /// <summary>
    /// The full-screen video that plays once at launch, before the main menu.
    ///
    /// Deliberately built to be impossible to get stuck on: if the file is
    /// missing, the codec is unsupported (a real risk on WebGL/older Android),
    /// or preparation simply stalls, we bail to the menu instead of leaving the
    /// player on a black screen. Any tap or key skips it, and the skip hint only
    /// appears after a moment so the opening shot isn't cluttered.
    /// </summary>
    public class Intro : MonoBehaviour
    {
        const string FileName = "intro.mp4";
        // If the video hasn't started within this long, assume the platform
        // can't play it and go straight to the menu.
        const float PrepareTimeout = 4f;
        // Skipping is disabled for a beat so the tap that launched the app (or a
        // stray finger still on the screen) can't instantly dismiss the intro.
        const float SkipArmDelay = 0.5f;

        System.Action _onDone;
        VideoPlayer _vp;
        CanvasGroup _group;
        bool _finished;

        /// <summary>Plays the intro, then invokes onDone. Never fails to call onDone.</summary>
        public static void Play(System.Action onDone)
        {
            var go = new GameObject("Intro");
            var intro = go.AddComponent<Intro>();
            intro._onDone = onDone;
            intro.Begin();
        }

        void Begin()
        {
            // Its own canvas at a very high sorting order so it covers the HUD,
            // the menu, and anything else already built at boot.
            var canvasGo = new GameObject("IntroCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;   // never eat input meant for the menu underneath

            // Black backing so letterbox bars (and the pre-roll frame) are black,
            // not whatever was rendered behind.
            var bg = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(canvasGo.transform, false);
            bg.color = Color.black;
            bg.raycastTarget = false;
            Stretch(bg.rectTransform);

            var video = new GameObject("Video", typeof(RectTransform)).AddComponent<RawImage>();
            video.transform.SetParent(canvasGo.transform, false);
            video.color = Color.clear;   // stays invisible until the first frame is ready
            video.raycastTarget = false;
            Stretch(video.rectTransform);
            // Letterbox rather than crop, so nothing in the shot is cut off on
            // whatever aspect ratio the device happens to have.
            var fitter = video.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;

            var hint = Theme.Label(canvasGo.transform, "TAP TO SKIP", 30,
                new Color(1f, 1f, 1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 70), new Vector2(600, 60));
            hint.raycastTarget = false;

            _vp = gameObject.AddComponent<VideoPlayer>();
            _vp.playOnAwake = false;
            _vp.isLooping = false;
            _vp.renderMode = VideoRenderMode.APIOnly;   // we hand the texture to the RawImage ourselves
            _vp.source = VideoSource.Url;
            _vp.url = System.IO.Path.Combine(Application.streamingAssetsPath, FileName);
            // Direct is the only audio path WebGL supports; browsers may still mute
            // it until the player interacts, which is fine — the visuals carry it.
            _vp.audioOutputMode = VideoAudioOutputMode.Direct;
            _vp.SetDirectAudioVolume(0, 1f);
            _vp.errorReceived += (_, __) => Finish();
            _vp.loopPointReached += _ => Finish();

            StartCoroutine(Run(video, fitter, hint));
        }

        IEnumerator Run(RawImage video, AspectRatioFitter fitter, Text hint)
        {
            _vp.Prepare();

            float waited = 0f;
            while (!_vp.isPrepared && !_finished && waited < PrepareTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (_finished) yield break;
            if (!_vp.isPrepared) { Finish(); yield break; }

            video.texture = _vp.texture;
            video.color = Color.white;
            if (_vp.height > 0) fitter.aspectRatio = (float)_vp.width / _vp.height;
            _vp.Play();

            float t = 0f;
            while (!_finished)
            {
                t += Time.unscaledDeltaTime;
                // Fade the skip hint in once the player has had a moment to watch.
                if (t > 1.2f)
                    hint.color = new Color(1f, 1f, 1f, Mathf.Min(0.55f, (t - 1.2f) * 0.8f));
                if (t > SkipArmDelay && SkipPressed()) break;
                // isPlaying goes false when the clip ends on platforms that don't
                // raise loopPointReached reliably (WebGL among them).
                if (t > 0.5f && !_vp.isPlaying) break;
                yield return null;
            }
            Finish();
        }

        static bool SkipPressed()
        {
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            }
            return Input.GetMouseButtonDown(0) || Input.anyKeyDown;
        }

        void Finish()
        {
            if (_finished) return;
            _finished = true;
            StartCoroutine(FadeOutAndHandOff());
        }

        IEnumerator FadeOutAndHandOff()
        {
            // Hand control to the menu FIRST, then fade the black away over it, so
            // the menu appears to emerge from the intro instead of hard-cutting.
            var done = _onDone;
            _onDone = null;
            done?.Invoke();

            if (_vp != null) _vp.Stop();
            for (float a = 1f; a > 0f; a -= Time.unscaledDeltaTime * 2.5f)
            {
                if (_group != null) _group.alpha = a;
                yield return null;
            }
            Destroy(gameObject);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
