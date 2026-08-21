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
        // off-center tap still grabs the stick (FIXED mode only — in floating
        // mode the catch area is already the whole zone).
        const float GrabPad = 1.4f;

        // FLOATING mode: this component's own rect is a big invisible catch ZONE
        // (one half of the screen), and the visible ring is a child that jumps to
        // wherever the thumb lands, then hides again on release. That's the layout
        // every modern touch platformer uses, and it's the fix for "my character
        // hides beneath the joystick" — with nothing drawn until you touch, and the
        // ring drawn around your thumb rather than in a corner you have to reach
        // for, the stick is never sitting on top of the action.
        //
        // FIXED mode keeps the old behaviour: the rect IS the ring, always visible.
        /// <summary>The colour the knob glows at full running speed — the UI's "on" gold.</summary>
        static readonly Color RunHot = new Color(1f, 0.80f, 0.42f);

        RectTransform _rt, _base, _knob;
        Graphic[] _graphics = System.Array.Empty<Graphic>();
        float[] _idleAlpha = System.Array.Empty<float>();
        float _radius;
        bool _floating;
        Vector2 _origin;    // where the thumb landed, in zone-local space
        int _fingerId = -1; // -1 = not tracking a finger (or the mouse fallback)
        bool _mouseDrag;
        bool _held;

        public void Setup(RectTransform baseRt, RectTransform knob, Graphic[] feedbackGraphics,
            float radius, bool floating)
        {
            _rt = (RectTransform)transform;
            _base = baseRt;
            _knob = knob;
            _radius = radius;
            _floating = floating;
            _graphics = feedbackGraphics ?? System.Array.Empty<Graphic>();
            _idleAlpha = new float[_graphics.Length];
            for (int i = 0; i < _graphics.Length; i++)
                _idleAlpha[i] = _graphics[i] != null ? _graphics[i].color.a : 1f;
            if (_floating && _base != null) _base.gameObject.SetActive(false);
        }

        // Park the ring under the thumb (floating) and reveal it.
        void PlaceBase(Vector2 zoneLocal)
        {
            _origin = _floating ? zoneLocal : Vector2.zero;
            if (_base == null) return;
            if (_floating)
            {
                _base.anchoredPosition = _origin;
                if (!_base.gameObject.activeSelf) _base.gameObject.SetActive(true);
            }
        }

        void HideBase()
        {
            if (_floating && _base != null && _base.gameObject.activeSelf)
                _base.gameObject.SetActive(false);
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
                    knobOffset = LocalOffset(t.position) - _origin;
                    break;
                }
                if (!stillDown) _fingerId = -1;
            }
            else if (_mouseDrag)
            {
                if (Input.GetMouseButton(0))
                {
                    held = true;
                    knobOffset = LocalOffset(Input.mousePosition) - _origin;
                }
                else _mouseDrag = false;
            }
            else
            {
                // Not tracking anything: look for a fresh press. In floating mode that
                // means anywhere in the zone, and the ring is planted right there.
                if (Input.touchCount > 0)
                {
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        var t = Input.GetTouch(i);
                        if (t.phase != TouchPhase.Began) continue;
                        if (!Contains(t.position)) continue;
                        _fingerId = t.fingerId;
                        held = true;
                        PlaceBase(LocalOffset(t.position));
                        knobOffset = Vector2.zero;   // the stick starts centred on your thumb
                        break;
                    }
                }
                else if (Input.GetMouseButtonDown(0) && Contains(Input.mousePosition))
                {
                    _mouseDrag = true;
                    held = true;
                    PlaceBase(LocalOffset(Input.mousePosition));
                    knobOffset = Vector2.zero;
                }
            }

            if (held)
            {
                knobOffset = Vector2.ClampMagnitude(knobOffset, _radius);
                if (_knob != null) _knob.anchoredPosition = knobOffset;
                float raw = _radius > 0.01f ? knobOffset.x / _radius : 0f;
                TouchInput.X = Shape(raw);

                // SHOW THE PLAYER WHEN THEY ARE ACTUALLY RUNNING.
                //
                // A stick with an invisible threshold is a stick nobody learns.
                // Testers pushed it a third of the way, got a jog, and concluded
                // the character was sluggish — they had no way to know there was
                // more speed available, because nothing on screen changed between
                // "jogging" and "flat out".
                //
                // The knob now goes hot at full speed: it brightens to the blood
                // gold the rest of the UI uses for "yes, this is on". One frame of
                // colour teaches the whole control, permanently, without a tutorial
                // caption telling anybody to push harder.
                if (_knob != null)
                {
                    var kg = _knob.GetComponent<Graphic>();
                    if (kg != null)
                    {
                        bool running = AtRunSpeed(raw);
                        var want = running ? RunHot : Color.white;
                        var c = kg.color;
                        // Lerped rather than snapped so it reads as the stick
                        // heating up, not as a light bulb flicking on and off at
                        // the threshold when a thumb rests right on the boundary.
                        var lit = Color.Lerp(new Color(c.r, c.g, c.b), want, 14f * Time.deltaTime);
                        kg.color = new Color(lit.r, lit.g, lit.b, c.a);
                    }
                }
            }
            else if (_held)
            {
                // Just released: snap the knob home, hide a floating ring, stop moving.
                if (_knob != null)
                {
                    _knob.anchoredPosition = Vector2.zero;
                    var kg = _knob.GetComponent<Graphic>();
                    if (kg != null)
                    {
                        var c = kg.color;                    // cool off, keep the authored alpha
                        kg.color = new Color(1f, 1f, 1f, c.a);
                    }
                }
                HideBase();
                TouchInput.X = 0f;
            }

            if (held != _held)
            {
                _held = held;
                if (held) Audio.PlayOr("tap", "click", 0.35f);
                // Capped low (not the ~0.9 the buttons use) — this feedback fires
                // the instant a finger grabs the stick, i.e. right when the player
                // is moving and needs to actually SEE their character, not a
                // near-opaque disc sitting on top of it.
                for (int i = 0; i < _graphics.Length; i++)
                {
                    if (_graphics[i] == null) continue;
                    var c = _graphics[i].color;
                    c.a = held ? Mathf.Min(0.45f, _idleAlpha[i] * 1.8f) : _idleAlpha[i];
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
            HideBase();
            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] == null) continue;
                var c = _graphics[i].color;
                c.a = _idleAlpha[i];
                _graphics[i].color = c;
            }
        }

        // Tilt → speed response.
        //
        // The squared curve this used to run made the whole ring a walk: at 70% tilt
        // you got half speed, and only a thumb pinned exactly on the rim ran. Players
        // read that as "it doesn't run unless I drag past the circle" — because on a
        // real thumb the rim is where the ring visually ends, and they were pushing
        // outside it hunting for a run that should already have arrived.
        //
        // So the stick now reaches FULL RUN before the rim (at RunAt of the travel)
        // and holds it out to the edge and beyond, which is what "push it all the way
        // and you're running" means. Below that band it still ramps smoothly for
        // placing yourself on a ledge, but with a gentler curve than the old square,
        // so a half-tilt is a real jog rather than a crawl. Max speed is unchanged —
        // 1 is still 1 — so every gap tuned by JumpArcProbe stays clearable.
        const float Dead = 0.14f;    // resting-thumb jitter
        // FULL SPEED FROM HALF A TILT.
        //
        // Was 0.62. Watching testers, essentially nobody pushes a floating stick
        // to its rim — the thumb rotates around its own knuckle, so a natural,
        // committed-feeling push lands somewhere around half deflection and stays
        // there. Every one of those players was running at ~85% speed while
        // believing they were at full pelt, which is exactly the report that came
        // back as "the movement is not smooth" and "sometimes the jump is shorter."
        // It was never the jump: it was the run-up.
        //
        // 0.5 means a normal thumb push is genuinely maximum speed, and the band
        // below it is still a real analogue ramp for edging up to a ledge. Max
        // speed itself is unchanged — 1 is still 1 — so every gap tuned by
        // JumpArcProbe stays exactly as clearable as it was.
        const float RunAt = 0.50f;
        static float Shape(float raw)
        {
            float a = Mathf.Min(1f, Mathf.Abs(raw));
            if (a < Dead) return 0f;
            if (a >= RunAt) return Mathf.Sign(raw);
            float t = (a - Dead) / (RunAt - Dead);
            // Slight ease-in so the first millimetre off the deadzone is a step, not
            // a lurch; 0.35 minimum keeps a nudge from being an unmovable crawl.
            return Mathf.Sign(raw) * Mathf.Lerp(0.35f, 1f, t * t);
        }

        /// <summary>Is the stick pushed far enough to be at full running speed?</summary>
        public static bool AtRunSpeed(float raw) => Mathf.Abs(raw) >= RunAt;

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
            // No grab padding in floating mode: the rect is already a screen-half-sized
            // zone, and padding it would reach across into the action buttons.
            float pad = _floating ? 1f : GrabPad;
            return Mathf.Abs(p.x - r.center.x) <= r.width * 0.5f * pad &&
                   Mathf.Abs(p.y - r.center.y) <= r.height * 0.5f * pad;
        }
    }
}
