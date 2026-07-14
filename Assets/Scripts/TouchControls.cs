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
        // Some buttons are a single Image (the old circular pads); others (the
        // bare arrow glyph, the gun icon built from stacked rects) have no
        // single root graphic, so press-feedback tints every Graphic under the
        // button at once instead of assuming one Image exists.
        Graphic[] _graphics = System.Array.Empty<Graphic>();
        float[] _idleAlpha = System.Array.Empty<float>();
        bool _held;

        void Awake()
        {
            _rt = (RectTransform)transform;
            var img = GetComponent<Image>();
            if (img != null) SetFeedback(new Graphic[] { img });
        }

        // Call after any extra visual children (icon parts, glyph label) exist,
        // so their current alpha becomes the idle baseline for press feedback.
        public void SetFeedback(Graphic[] graphics)
        {
            _graphics = graphics ?? System.Array.Empty<Graphic>();
            _idleAlpha = new float[_graphics.Length];
            for (int i = 0; i < _graphics.Length; i++)
                _idleAlpha[i] = _graphics[i] != null ? _graphics[i].color.a : 1f;
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
                // Press feedback: every visual part brightens while a finger is on it.
                for (int i = 0; i < _graphics.Length; i++)
                {
                    if (_graphics[i] == null) continue;
                    var c = _graphics[i].color;
                    c.a = held ? Mathf.Min(0.9f, _idleAlpha[i] * 2.5f) : _idleAlpha[i];
                    _graphics[i].color = c;
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
            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] == null) continue;
                var c = _graphics[i].color;
                c.a = _idleAlpha[i];
                _graphics[i].color = c;
            }
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

    /// <summary>
    /// A draggable virtual joystick alternative to the left/right arrow pads.
    /// Same raw Input.touches polling as TouchButton (no EventSystem) so it stays
    /// multitouch-safe. Writes a continuous TouchInput.X in [-1, 1] — the same
    /// field the binary arrows use — so PlayerController needs no changes to
    /// support it. The knob tracks the finger 1:1 the instant it moves, which
    /// gives immediate visual feedback (unlike a press/release button) and is
    /// why a joystick reads as more responsive even at identical input latency.
    /// </summary>
    public class TouchJoystick : MonoBehaviour
    {
        // Generous catch radius for the initial finger-down so a slightly
        // off-center tap still grabs the stick.
        const float GrabPad = 1.4f;

        RectTransform _rt, _knob;
        Graphic[] _graphics = System.Array.Empty<Graphic>();
        float[] _idleAlpha = System.Array.Empty<float>();
        float _radius;
        int _fingerId = -1; // -1 = not tracking a finger (or the mouse fallback)
        bool _mouseDrag;
        bool _held;

        public void Setup(RectTransform knob, Graphic[] feedbackGraphics)
        {
            _rt = (RectTransform)transform;
            _knob = knob;
            _radius = _rt.rect.width * 0.5f;
            _graphics = feedbackGraphics ?? System.Array.Empty<Graphic>();
            _idleAlpha = new float[_graphics.Length];
            for (int i = 0; i < _graphics.Length; i++)
                _idleAlpha[i] = _graphics[i] != null ? _graphics[i].color.a : 1f;
        }

        void Update()
        {
            bool held = false;
            Vector2 knobOffset = Vector2.zero;

            if (_fingerId != -1)
            {
                // Already tracking a finger — follow it anywhere on screen until
                // it lifts, even past the base's own bounds (that's what makes a
                // drag stick feel natural instead of clamping to the base rect).
                bool stillDown = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.fingerId != _fingerId) continue;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) break;
                    stillDown = true;
                    held = true;
                    knobOffset = LocalOffset(t.position);
                    break;
                }
                if (!stillDown) _fingerId = -1;
            }
            else if (_mouseDrag)
            {
                if (Input.GetMouseButton(0))
                {
                    held = true;
                    knobOffset = LocalOffset(Input.mousePosition);
                }
                else _mouseDrag = false;
            }
            else
            {
                // Not tracking anything: look for a fresh press that lands on the base.
                if (Input.touchCount > 0)
                {
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        var t = Input.GetTouch(i);
                        if (t.phase != TouchPhase.Began) continue;
                        if (!Contains(t.position)) continue;
                        _fingerId = t.fingerId;
                        held = true;
                        knobOffset = LocalOffset(t.position);
                        break;
                    }
                }
                else if (Input.GetMouseButtonDown(0) && Contains(Input.mousePosition))
                {
                    _mouseDrag = true;
                    held = true;
                    knobOffset = LocalOffset(Input.mousePosition);
                }
            }

            if (held)
            {
                knobOffset = Vector2.ClampMagnitude(knobOffset, _radius);
                if (_knob != null) _knob.anchoredPosition = knobOffset;
                TouchInput.X = _radius > 0.01f ? Mathf.Clamp(knobOffset.x / _radius, -1f, 1f) : 0f;
            }
            else if (_held)
            {
                // Just released: snap the knob home and zero movement.
                if (_knob != null) _knob.anchoredPosition = Vector2.zero;
                TouchInput.X = 0f;
            }

            if (held != _held)
            {
                _held = held;
                if (held) Audio.PlayOr("tap", "click", 0.35f);
                for (int i = 0; i < _graphics.Length; i++)
                {
                    if (_graphics[i] == null) continue;
                    var c = _graphics[i].color;
                    c.a = held ? Mathf.Min(0.9f, _idleAlpha[i] * 2.2f) : _idleAlpha[i];
                    _graphics[i].color = c;
                }
            }
        }

        // If the joystick is hidden mid-drag (menu opened, mode switched in
        // Settings) its held state must not linger in TouchInput.
        void OnDisable()
        {
            _fingerId = -1;
            _mouseDrag = false;
            _held = false;
            TouchInput.X = 0f;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] == null) continue;
                var c = _graphics[i].color;
                c.a = _idleAlpha[i];
                _graphics[i].color = c;
            }
        }

        Vector2 LocalOffset(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screen, null, out var p);
            return p - _rt.rect.center;
        }

        bool Contains(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screen, null, out var p))
                return false;
            var r = _rt.rect;
            return Mathf.Abs(p.x - r.center.x) <= r.width * 0.5f * GrabPad &&
                   Mathf.Abs(p.y - r.center.y) <= r.height * 0.5f * GrabPad;
        }
    }
}
