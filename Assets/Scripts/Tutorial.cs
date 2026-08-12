using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// THE COACH — the on-screen teacher that runs over the tutorial floor.
    ///
    /// Why it exists: new players opened the game and tapped the top button on the
    /// landing, which is BLOOD MOON — the timed, life-limited, hand-authored night
    /// that assumes you already know how to run. They died, learned nothing, and
    /// left. The tutorial floor (Levels.Tutorial) now runs straight out of the
    /// launch video, and this drives the talking over it.
    ///
    /// It teaches four things in four separate stretches of empty ground, one at a
    /// time, and it never blocks play: the caption sits at the top, the animated
    /// thumb hint sits on the joystick, and SKIP is always there for the second
    /// playthrough. Progress is read from the player's x every frame, so dying and
    /// respawning simply rewinds the lesson instead of desynchronising it.
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        // Each lesson starts when the player passes `fromX`. The last one runs to
        // the coffin. Thresholds are tied to the floor's own geometry (see
        // Levels.Tutorial) so moving a platform can't silently strand a caption.
        struct Step
        {
            public float fromX;
            public string title;
            public string body;
            public bool stick;   // show the thumb hint on the joystick during this step
        }

        static readonly Step[] Steps =
        {
            new Step { fromX = -999f, stick = true,
                       title  = "PUSH THE STICK ALL THE WAY",
                       body   = "Hold it out to the edge and you RUN. A small push is only a walk." },
            new Step { fromX = Levels.TutorialJumpX - 5.5f,
                       title  = "THE FLOOR ENDS HERE",
                       body   = "JUMP across. Hold the button longer to jump higher." },
            new Step { fromX = Levels.TutorialSpikeX - 7f,
                       title  = "SPIKES KILL INSTANTLY",
                       body   = "Bright steel, ringed in blood. This one is standing still — go over it." },
            new Step { fromX = Levels.TutorialLieX - 7.5f,
                       title  = "NOW THE REAL LESSON",
                       body   = "Some traps wait until you're close. Nothing in this castle is honest." },
            new Step { fromX = Levels.TutorialLieX + 2.5f,
                       title  = "THE COFFIN IS THE WAY OUT",
                       body   = "Not doors. Not stairs. Only coffins. Climb in." },
        };

        System.Func<Transform> _player;
        System.Action _onSkip;
        Text _title, _body;
        RectTransform _hint;          // the whole joystick hint group
        RectTransform _thumb;         // the white "hand" that slides out to the rim
        Graphic[] _chevrons;
        float _hintRadius;
        int _step = -1;
        float _fullTiltHeld;          // how long they've held a real run
        bool _ranOnce;                // …once they have, the thumb hint retires

        /// <summary>
        /// Start coaching. `stickAnchor`/`stickPos`/`stickRadius` are the joystick's
        /// own layout numbers, handed over by GameRoot so the hint lands exactly on
        /// the stick instead of near it. Pass showStick=false on desktop, where
        /// there is no stick to point at.
        /// </summary>
        public static Tutorial Begin(System.Func<Transform> player, System.Action onSkip,
                                     bool showStick, Vector2 stickAnchor, Vector2 stickPos,
                                     float stickRadius)
        {
            var go = new GameObject("Tutorial");
            var t = go.AddComponent<Tutorial>();
            t._player = player;
            t._onSkip = onSkip;
            t.Build(showStick, stickAnchor, stickPos, stickRadius);
            return t;
        }

        /// <summary>Tear the coach down — captions, skip button, thumb hint and all.</summary>
        public void Close()
        {
            if (_root != null) Destroy(_root.gameObject);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            // Belt and braces: the UI lives on the shared canvas, so it must never
            // outlive this component (a stray caption stuck over the Castle map was
            // exactly the sort of bug this guards).
            if (_root != null) Destroy(_root.gameObject);
        }

        RectTransform _root;

        void Build(bool showStick, Vector2 stickAnchor, Vector2 stickPos, float stickRadius)
        {
            // One container under the shared canvas holds every piece of the coach,
            // so Close() is a single Destroy and nothing can be left behind.
            var rootGo = new GameObject("TutorialUI", typeof(RectTransform));
            rootGo.transform.SetParent(Theme.Canvas.transform, false);
            _root = (RectTransform)rootGo.transform;
            _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            var canvas = _root.transform;

            // The caption plate. Top of the screen, well clear of the thumbs and of
            // the HUD hearts, dark enough to read over any backdrop.
            var plate = new GameObject("TutorialPlate", typeof(RectTransform));
            plate.transform.SetParent(canvas, false);
            var prt = (RectTransform)plate.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0, -104);
            // 1080 wide, not 1180: the hearts bar lives in the top-left corner and the
            // wider plate clipped its right end.
            prt.sizeDelta = new Vector2(1080, 132);
            var edge = plate.AddComponent<Image>();
            edge.sprite = Theme.Square;
            edge.color = Crimson.Rail;
            edge.raycastTarget = false;
            var bed = new GameObject("Bed", typeof(RectTransform)).AddComponent<Image>();
            bed.transform.SetParent(plate.transform, false);
            bed.sprite = Theme.Square;
            bed.color = new Color(0.043f, 0.016f, 0.031f, 0.94f);
            bed.raycastTarget = false;
            var brt = bed.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(2, 2); brt.offsetMax = new Vector2(-2, -2);

            _title = Theme.Label(plate.transform, "", 38, Crimson.GoldLit,
                new Vector2(0.5f, 1f), new Vector2(0, -38), new Vector2(1120, 46));
            _title.raycastTarget = false;
            if (Theme.MenuFont != null) _title.font = Theme.MenuFont;
            _body = Theme.Label(plate.transform, "", 26, Crimson.Bone,
                new Vector2(0.5f, 1f), new Vector2(0, -88), new Vector2(1120, 60));
            _body.raycastTarget = false;
            _body.fontStyle = FontStyle.Normal;
            if (Theme.MenuFont != null) _body.font = Theme.MenuFont;

            // SKIP. Anyone replaying the tutorial (or who already knows the game)
            // must be able to leave in one tap — a tutorial you can't escape is the
            // fastest way to lose the player you were trying to keep.
            // Below the top-right HUD cluster (pause plate, mute chip), not on top of
            // it — at -104 it sat right across them.
            Crimson.Btn(canvas, "SKIP ›", new Vector2(1f, 1f), new Vector2(-124, -252),
                        new Vector2(176, 62), () => _onSkip?.Invoke(), false, 22);

            if (showStick) BuildStickHint(canvas, stickAnchor, stickPos, stickRadius);
        }

        /// <summary>
        /// The thumb hint: a ring the size of the real stick, three chevrons marching
        /// out to its rim, and a white thumb-dot that slides from the middle to the
        /// edge on a loop. That's the whole "you have to push it to the end" lesson
        /// as a picture, which is what every other mobile platformer shows and what
        /// this game never did.
        /// </summary>
        void BuildStickHint(Transform canvas, Vector2 anchor, Vector2 pos, float radius)
        {
            _hintRadius = Mathf.Max(24f, radius);

            var group = new GameObject("StickHint", typeof(RectTransform));
            group.transform.SetParent(canvas, false);
            _hint = (RectTransform)group.transform;
            _hint.anchorMin = _hint.anchorMax = anchor;
            _hint.pivot = new Vector2(0.5f, 0.5f);
            _hint.anchoredPosition = pos;
            _hint.sizeDelta = Vector2.one * (_hintRadius * 2f);

            // The ring — the stick's own outline, so the hint reads as "this thing
            // here", not as a floating decoration.
            var ring = Crimson.Img(group.transform, "Ring", Crimson.Ring, new Color(1f, 1f, 1f, 0.30f));
            Crimson.Place(ring, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (_hintRadius * 2f));

            // Three chevrons pointing at the rim, lit in sequence.
            _chevrons = new Graphic[3];
            for (int i = 0; i < 3; i++)
            {
                var c = Theme.Label(group.transform, "›", 54, new Color(1f, 1f, 1f, 0.25f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(_hintRadius * (0.42f + i * 0.30f), 0f), new Vector2(60, 60));
                c.raycastTarget = false;
                _chevrons[i] = c;
            }

            // The thumb: a plain white disc. A hand GLYPH was the obvious choice and
            // the wrong one — the character isn't in the bundled font on every
            // device, and a missing glyph draws as an empty box sitting on the
            // control the player is being told to use.
            var thumb = Crimson.Img(group.transform, "Thumb", Theme.Circle, new Color(1f, 1f, 1f, 0.92f));
            Crimson.Place(thumb, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (_hintRadius * 0.46f));
            _thumb = thumb.rectTransform;
        }

        void Update()
        {
            var p = _player?.Invoke();
            if (p == null) return;

            // Which lesson are we standing in? Read from position every frame, so a
            // death that sends the player back to the checkpoint rewinds the caption
            // with them instead of leaving them on a lesson they can't see.
            int want = 0;
            for (int i = Steps.Length - 1; i >= 0; i--)
                if (p.position.x >= Steps[i].fromX) { want = i; break; }

            if (want != _step)
            {
                _step = want;
                _title.text = Steps[want].title;
                _body.text = Steps[want].body;
            }

            // The stick hint retires as soon as they've actually held a full run for
            // a moment — the lesson is learned, and leaving it up would be drawing
            // over the control they're now using.
            if (_hint != null)
            {
                if (Mathf.Abs(TouchInput.X) > 0.98f)
                {
                    _fullTiltHeld += Time.unscaledDeltaTime;
                    if (_fullTiltHeld > 0.45f) _ranOnce = true;
                }
                else _fullTiltHeld = 0f;

                bool show = Steps[_step].stick && !_ranOnce;
                if (_hint.gameObject.activeSelf != show) _hint.gameObject.SetActive(show);
                if (show) AnimateHint();
            }
        }

        // One loop: the thumb slides out to the rim, holds there (that's the point —
        // the RIM is where running lives), then snaps back and does it again.
        void AnimateHint()
        {
            const float Period = 1.6f;
            float t = (Time.unscaledTime % Period) / Period;
            // 0.00-0.55 travel out, 0.55-0.85 hold at the rim, 0.85-1.0 snap home.
            float reach = t < 0.55f ? Mathf.SmoothStep(0f, 1f, t / 0.55f)
                        : t < 0.85f ? 1f
                                    : Mathf.SmoothStep(1f, 0f, (t - 0.85f) / 0.15f);
            if (_thumb != null)
                _thumb.anchoredPosition = new Vector2(reach * _hintRadius * 0.92f, 0f);

            // Chevrons light in order as the thumb passes them, so the eye is led
            // outward rather than just seeing three static arrows.
            for (int i = 0; i < _chevrons.Length; i++)
            {
                if (_chevrons[i] == null) continue;
                float at = 0.42f + i * 0.30f;
                float lit = Mathf.Clamp01((reach - at * 0.75f) * 3.2f);
                var c = _chevrons[i].color;
                c.a = Mathf.Lerp(0.22f, 0.95f, lit);
                _chevrons[i].color = c;
            }
        }
    }
}
