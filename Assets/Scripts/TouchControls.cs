using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>Shared touch/mouse input so the game is playable on phones.</summary>
    public static class TouchInput
    {
        public static float X;       // -1 left, +1 right, 0 none
        public static bool FlyHeld;  // held while the FLY/BAT button is pressed
        public static bool JumpHeld; // held while the JUMP button is pressed (variable jump height)
        static bool _jump, _fire, _dash;
        public static void QueueJump() => _jump = true;
        public static bool ConsumeJump() { if (_jump) { _jump = false; return true; } return false; }
        public static void QueueFire() => _fire = true;
        public static bool ConsumeFire() { if (_fire) { _fire = false; return true; } return false; }
        public static void QueueDash() => _dash = true;
        public static bool ConsumeDash() { if (_dash) { _dash = false; return true; } return false; }
        public static void Clear() { X = 0f; _jump = false; _fire = false; _dash = false; FlyHeld = false; JumpHeld = false; }
    }

    /// <summary>
    /// An on-screen button: hold to move/fly, tap to jump/fire/dash.
    ///
    /// This POLLS Input.touches directly instead of using EventSystem pointer
    /// handlers. Mobile WebGL emulates ONE mouse pointer for the whole screen,
    /// so pointer events fall apart the moment a second finger lands — you
    /// couldn't move and jump at the same time, and a swallowed pointer-up left
    /// the BAT glide stuck on (the "vampire flies forever" bug). Recomputing
    /// every state from the live finger list each frame is multi-touch safe and
    /// can never get stuck.
    /// </summary>
    public class TouchButton : MonoBehaviour
    {
        public int dir; // -1=left, +1=right, 0=jump, 2=fire, 3=fly(hold), 4=dash(tap)

        // Fingers are fatter than the (deliberately small) visual circle, so the
        // hit zone extends past it. Kept modest so neighbouring zones never
        // overlap — an overlap would fire two actions with one touch.
        const float HitPad = 1.25f;

        RectTransform _rt;
        Image _img;
        float _idleAlpha;
        bool _held;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _img = GetComponent<Image>();
            if (_img != null) _idleAlpha = _img.color.a;
        }

        void Update()
        {
            bool held = false, tapped = false;

            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                    if (!Contains(t.position)) continue;
                    held = true;
                    if (t.phase == TouchPhase.Began) tapped = true;
                }
            }
            else if (Input.GetMouseButton(0) && Contains(Input.mousePosition))
            {
                // Mouse fallback so the layout is testable on desktop (opt_touch).
                held = true;
                tapped = Input.GetMouseButtonDown(0);
            }

            if (held != _held)
            {
                _held = held;
                // A soft tap sound so every on-screen press has audio feedback,
                // not just the desktop click. Falls back to the menu "click" clip
                // until a dedicated "tap" clip is dropped into Resources/audio.
                if (held) Audio.PlayOr("tap", "click", 0.35f);
                // Press feedback: the disc brightens while a finger is on it.
                if (_img != null)
                {
                    var c = _img.color;
                    c.a = held ? Mathf.Min(0.6f, _idleAlpha * 2.5f) : _idleAlpha;
                    _img.color = c;
                }
            }

            switch (dir)
            {
                case -1:
                case 1:
                    if (held) TouchInput.X = dir;
                    else if (Mathf.Approximately(TouchInput.X, dir)) TouchInput.X = 0f;
                    break;
                case 0:
                    if (tapped) TouchInput.QueueJump();
                    TouchInput.JumpHeld = held;   // release early = shorter hop
                    break;
                case 2: if (tapped) TouchInput.QueueFire(); break;
                case 3: TouchInput.FlyHeld = held; break;
                case 4: if (tapped) TouchInput.QueueDash(); break;
            }
        }

        // If the button is hidden mid-press (menu opened, gun emptied, panel
        // toggled) its held state must not linger in TouchInput.
        void OnDisable()
        {
            if (dir == 3) TouchInput.FlyHeld = false;
            else if (dir == 0) TouchInput.JumpHeld = false;
            else if ((dir == -1 || dir == 1) && Mathf.Approximately(TouchInput.X, dir)) TouchInput.X = 0f;
            _held = false;
            if (_img != null) { var c = _img.color; c.a = _idleAlpha; _img.color = c; }
        }

        bool Contains(Vector2 screen)
        {
            // Overlay canvas → no camera needed for the conversion.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screen, null, out var p))
                return false;
            var r = _rt.rect;
            return Mathf.Abs(p.x - r.center.x) <= r.width * 0.5f * HitPad &&
                   Mathf.Abs(p.y - r.center.y) <= r.height * 0.5f * HitPad;
        }
    }
}
