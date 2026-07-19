using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// Tags floor whose existence is tied to the candles. Two polarities:
    /// normal (solid in light, gone in the dark — floor 2's lie) and ghost
    /// (a faint shimmer in light, solid spectral stone in the dark — floor 7's
    /// inversion of that lie). Toggled by RoomDirector via SetSolid; colliders
    /// and every child sprite (stone face, blood lip, 2.5D depth slices) are
    /// driven together.
    /// </summary>
    public class NightFloor : MonoBehaviour
    {
        public float x;        // world centre, used to decide which room owns it
        public bool ghost;     // false: solid-in-light. true: solid-in-dark.

        BoxCollider2D _col;
        SpriteRenderer[] _srs = System.Array.Empty<SpriteRenderer>();
        Color[] _base = System.Array.Empty<Color>();
        static readonly Color SpectralTint = new Color(0.62f, 0.78f, 1f);

        public void Configure(float worldX, bool isGhost)
        {
            x = worldX; ghost = isGhost;
            _col = GetComponent<BoxCollider2D>();
            _srs = GetComponentsInChildren<SpriteRenderer>(true);
            _base = new Color[_srs.Length];
            for (int i = 0; i < _srs.Length; i++) _base[i] = _srs[i].color;
            SetSolid(!ghost);   // ghosts start as a faint promise; normal floors start real
        }

        public void SetSolid(bool solid)
        {
            if (_col != null) _col.enabled = solid;
            for (int i = 0; i < _srs.Length; i++)
            {
                if (_srs[i] == null) continue;
                var c = _base[i];
                if (ghost)
                {
                    // Spectral in both states; the tell in the light is that you
                    // can see the wall through it.
                    c = new Color(c.r * SpectralTint.r, c.g * SpectralTint.g, c.b * SpectralTint.b,
                                  c.a * (solid ? 0.9f : 0.22f));
                }
                else c.a = solid ? _base[i].a : 0f;
                _srs[i].color = c;
            }
        }
    }

    /// <summary>
    /// A rune on the floor that puts you to sleep (the castle's lullaby). One
    /// nap per rune per life — waking up on top of it must not re-trigger it.
    /// The glyph dims once spent, which doubles as the "this one's done" tell.
    /// </summary>
    public class SleepRuneZone : MonoBehaviour
    {
        bool _armed = true;
        SpriteRenderer[] _glyph;

        public void SetGlyph(SpriteRenderer[] parts) => _glyph = parts;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!_armed) return;
            var pc = other.GetComponent<PlayerController>();
            if (pc == null) return;
            _armed = false;
            pc.CastleSleep();
            if (_glyph != null)
                foreach (var g in _glyph)
                    if (g != null) { var c = g.color; c.a *= 0.25f; g.color = c; }
        }
    }

    /// <summary>
    /// A spectral rune that INVERTS gravity: cross it and you fall toward the
    /// ceiling (or back toward the floor). This is the Chapel's whole language —
    /// the one truly physics-breaking object in the castle. It's a toggle, so
    /// the same rune can be reused, and it stays armed across the run.
    /// A DUD rune looks identical until touched, when it fizzles dark and does
    /// nothing — the Chapel's lie. The real one is always somewhere in the room.
    /// </summary>
    public class GravityRuneZone : MonoBehaviour
    {
        public bool dud;
        float _cooldown;               // physics can graze a trigger twice in a flip
        bool _spent;                   // duds fizzle once, then just sit there dead
        SpriteRenderer[] _glyph;

        public void SetGlyph(SpriteRenderer[] parts) => _glyph = parts;

        void Update() { if (_cooldown > 0f) _cooldown -= Time.deltaTime; }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (_cooldown > 0f) return;
            var pc = other.GetComponent<PlayerController>();
            if (pc == null) return;
            _cooldown = 0.35f;

            if (dud)
            {
                if (_spent) return;
                _spent = true;
                // The fizzle IS the punchline: a dark flicker, a dead little
                // noise, and the gap you were sprinting at is still unjumpable.
                // The breathing pulses must die first or they'd re-brighten the
                // corpse every frame.
                foreach (var p in GetComponentsInChildren<FaintPulse>()) Destroy(p);
                if (_glyph != null)
                    foreach (var g in _glyph)
                        if (g != null) { var c = g.color; c.a *= 0.18f; g.color = c; }
                Audio.PlayOr("tap", "click", 0.5f);
                GameRoot.I?.RoomToast("The rune is dead. Someone used it all up.");
                return;
            }

            pc.SetGravityDir(pc.GravDir > 0f ? -1 : 1);
            // Sell the flip HARD — this is the game's most disorienting beat and
            // it must read as "the rune did that", never "physics glitched".
            Fx.Burst(transform.position, new Color(0.62f, 0.78f, 1f, 0.9f), 12, 5f, 0.22f, 0.5f, 8f);
            Audio.PlayOr("portal", "jump", 0.6f);
            GameRoot.I?.ShakeCam(0.18f, 0.12f);
        }
    }

    /// <summary>
    /// The Endless Hall: cross the rune at the doorway on FOOT and you're
    /// silently back at the start of an identical room. Jumping it passes. The
    /// hall gets bored after five loops and lets you through — mercy disguised
    /// as boredom, so nobody is ever truly stuck (an unwinnable room reads as a
    /// broken game, and this project can't afford that twice).
    /// </summary>
    public class LoopZone : MonoBehaviour
    {
        public float returnX;      // where the loop dumps you (the room's mouth)
        int _loops;
        bool _released;            // the hall gave up — crossings now pass freely
        SpriteRenderer[] _glyph;

        public void SetGlyph(SpriteRenderer[] parts) => _glyph = parts;

        void OnTriggerEnter2D(Collider2D other)
        {
            var pc = other.GetComponent<PlayerController>();
            if (pc == null) return;
            if (_released) return;             // walk on through — the hall is done with you
            _loops++;

            // The FIFTH crossing is the one it relents on: message, disarm, and
            // crucially let this crossing PASS instead of yanking you back (the
            // old code both said "…fine. Go." AND teleported you — an off-by-one
            // that read as a broken promise).
            if (_loops >= 5)
            {
                _released = true;
                GameRoot.I?.RoomToast("…fine. Go.");
                if (_glyph != null)
                    foreach (var g in _glyph)
                        if (g != null) { var c = g.color; c.a *= 0.2f; g.color = c; }
                return;
            }

            var p = pc.transform.position;
            pc.transform.position = new Vector3(returnX, p.y, p.z);
            if (_loops == 2) GameRoot.I?.RoomToast("Déjà vu…");
            else if (_loops == 4) GameRoot.I?.RoomToast("The hall is enjoying this.");
        }
    }

    /// <summary>
    /// The crypt press: a full-room stone slab that grinds down from the
    /// ceiling once the room's rule fires, then cycles (down, rest, back up)
    /// so a player who retreated isn't softlocked behind a sealed room — on
    /// the next dip it's a timing puzzle instead. Touching it is death.
    /// </summary>
    public class PressSlab : MonoBehaviour
    {
        const float TopY = 2.5f, BotY = -2.1f;      // centre travel: flush under ceiling → flush on floor
        const float DownSpeed = 0.85f, UpSpeed = 1.7f;
        const float DwellBottom = 2.2f, DwellTop = 0.9f;
        int _state;        // 0 descend, 1 dwell bottom, 2 rise, 3 dwell top
        float _t;

        void Update()
        {
            var p = transform.position;
            switch (_state)
            {
                case 0:
                    p.y -= DownSpeed * Time.deltaTime;
                    if (p.y <= BotY) { p.y = BotY; _state = 1; _t = DwellBottom; }
                    break;
                case 1: _t -= Time.deltaTime; if (_t <= 0f) _state = 2; break;
                case 2:
                    p.y += UpSpeed * Time.deltaTime;
                    if (p.y >= TopY) { p.y = TopY; _state = 3; _t = DwellTop; }
                    break;
                default: _t -= Time.deltaTime; if (_t <= 0f) _state = 0; break;
            }
            transform.position = p;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponent<PlayerController>() != null)
                GameRoot.I?.Die("The crypt closed.");
        }
    }

    /// <summary>
    /// A spiked portcullis in a doorway. It hangs harmlessly overhead until you
    /// approach, then SLAMS — and settles into a slow grind cycle (down, dwell,
    /// rise, pause) for as long as you're near. The doorway is the one element
    /// the player crosses fifty times and stops looking at; that's exactly why
    /// it's the one that bites. Lethal only while it's low — brushing the bars
    /// as they rise past head height doesn't kill, or passing would be pixel-luck.
    /// </summary>
    public class Portcullis : MonoBehaviour
    {
        const float UpY = -0.15f, DownY = -1.85f;   // centre travel (bars are 1.7 tall)
        const float SlamTime = 0.22f, RiseTime = 1.4f;
        const float DwellDown = 1.1f, DwellUp = 1.2f;
        const float SenseR = 2.1f;                   // how close counts as "approaching"

        Transform _player;
        BoxCollider2D _col;
        int _state;      // 0 armed-up, 1 slam, 2 down, 3 rise, 4 up-dwell
        float _t;

        public void Setup(Transform player, BoxCollider2D col) { _player = player; _col = col; }

        void Update()
        {
            var p = transform.position;
            switch (_state)
            {
                case 0:
                    if (_player != null && Mathf.Abs(_player.position.x - p.x) < SenseR)
                    {
                        _state = 1;
                        GameRoot.I?.ShakeCam(0.2f, 0.12f);
                    }
                    break;
                case 1:
                    p.y -= (UpY - DownY) / SlamTime * Time.deltaTime;
                    if (p.y <= DownY) { p.y = DownY; _state = 2; _t = DwellDown; }
                    break;
                case 2: _t -= Time.deltaTime; if (_t <= 0f) _state = 3; break;
                case 3:
                    p.y += (UpY - DownY) / RiseTime * Time.deltaTime;
                    if (p.y >= UpY) { p.y = UpY; _state = 4; _t = DwellUp; }
                    break;
                default:
                    _t -= Time.deltaTime;
                    if (_t <= 0f) _state = 0;   // re-arm; slams again if you're still loitering
                    break;
            }
            transform.position = p;
            // Bars only bite while their spikes are at body height.
            if (_col != null) _col.enabled = (p.y - 0.85f) < -1.55f;
        }

        void OnTriggerEnter2D(Collider2D o) { Bite(o); }
        void OnTriggerStay2D(Collider2D o)  { Bite(o); }
        void Bite(Collider2D o)
        {
            if (o.GetComponent<PlayerController>() != null)
                GameRoot.I?.Die("The doorway bit.");
        }
    }

    /// <summary>
    /// A spike that stands somewhere else in the dark. Looks and kills exactly
    /// like a normal spike — the difference is that when its room's candles die,
    /// it is silently already at its other spot. The layout you memorised on the
    /// way in is the trap; the candle circle around you is the only truth.
    /// </summary>
    public class ShiftSpikeMark : MonoBehaviour
    {
        public float litX, darkX;
        public bool IsDark { get; private set; }
        public void Place(bool dark)
        {
            IsDark = dark;
            var p = transform.position;
            p.x = dark ? darkX : litX;
            transform.position = p;
        }
    }

    /// <summary>
    /// The stone fill that seals a completed stage's doorway behind you. Solid —
    /// banked stages are committed, exactly like Level Devil's discrete screens —
    /// but the collider only arms once the player is clearly past it, so the
    /// wall can never materialise inside (or shove) the body crossing it.
    /// </summary>
    public class StageSeal : MonoBehaviour
    {
        Transform _player;
        BoxCollider2D _col;
        public void Arm(Transform player, BoxCollider2D col) { _player = player; _col = col; }
        void Update()
        {
            if (_col == null) { enabled = false; return; }
            if (_player == null || _player.position.x > transform.position.x + 1.0f)
            {
                _col.enabled = true;
                enabled = false;
            }
        }
    }

    /// <summary>
    /// A vertically-bobbing stone slab — "sometimes the floor below you moved".
    /// Kinematic so the physics engine carries the player properly while it
    /// rises. Rides across gaps too wide to jump; under a crypt press it becomes
    /// a two-clock timing puzzle (cross when the slab is up AND the press isn't).
    /// </summary>
    public class VertPlat : MonoBehaviour
    {
        public float amp = 1.2f;
        Rigidbody2D _rb;
        Vector2 _home;
        float _t;
        void Awake() { _rb = GetComponent<Rigidbody2D>(); _home = transform.position; }
        void FixedUpdate()
        {
            _t += Time.fixedDeltaTime;
            var p = _home + Vector2.up * (Mathf.Sin(_t * 1.4f) * amp);
            if (_rb != null) _rb.MovePosition(p);
            else transform.position = p;
        }
    }

    /// <summary>A little "z" that drifts up off a sleeping vampire and fades.</summary>
    public class ZzzFloat : MonoBehaviour
    {
        float _life = 1.1f;
        TextMesh _tm;
        void Start() { _tm = GetComponent<TextMesh>(); }
        void Update()
        {
            transform.position += new Vector3(0.25f, 0.85f, 0f) * Time.deltaTime;
            _life -= Time.deltaTime;
            if (_tm != null) { var c = _tm.color; c.a = Mathf.Clamp01(_life); _tm.color = c; }
            if (_life <= 0f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Runs the per-room rules on a roomed level.
    ///
    /// The genre insight this serves: a trap is an object you learn to dodge
    /// once, but a RULE breaks a promise the room was built on, and that's what
    /// keeps players guessing past the first few floors. Each room owns one
    /// rule; this watches which room the player is standing in, locks the
    /// camera to that room (so chambers read as SCREENS, not a corridor with
    /// pillars — the complaint from the last playtest), runs the active rule,
    /// and draws the room-progress dots.
    ///
    /// Built and owned by GameRoot.BuildLevel; torn down with the level root,
    /// and the whole level rebuilds on death, so every rule resets for free.
    /// Levels with no rooms (11-40, Endless, Daily, Versus) never create one.
    /// </summary>
    public class RoomDirector : MonoBehaviour
    {
        List<RoomSpec> _rooms;
        Transform _player;
        PlayerController _pc;
        NightFloor[] _nightFloors = System.Array.Empty<NightFloor>();
        readonly List<ShiftSpikeMark> _shiftSpikes = new();
        Transform _fleeExit;                  // the RealExit, if a Flee room owns one
        Collider2D _fleeCol;                   // its trigger — OFF while fleeing so a pass-through can't win
        bool _fleeToasted, _fleeCornered;
        readonly List<GameObject> _slabs = new();
        bool[] _slabSpawned;
        Transform _levelRoot;
        int _active = -1;
        bool _fired;                          // has the ACTIVE room's rule tripped yet?
        int _reverseToastRoom = -1;

        // --- Dark rule ---
        GameObject _darkGO;
        RectTransform _darkRT;
        Image _darkImg;
        float _darkT;              // 0 = lit, 1 = fully dark
        bool _darkWanted;
        // Candles get SNUFFED, they don't dim. A slow fade reads as a mood effect
        // and gives the player time to stroll to safety; a fast one is an event
        // that happens TO them. The half-second of dying light is also the tell.
        const float DarkFade = 0.55f;

        // --- Room dots (the "five stages" readout, Level Devil's door dots) ---
        GameObject _dotsGO;
        Image[] _dots = System.Array.Empty<Image>();
        Text _roomLabel;

        public void Init(Level level, Transform player, Transform levelRoot)
        {
            _rooms = level.Rooms; _player = player; _levelRoot = levelRoot;
            _pc = player.GetComponent<PlayerController>();
            _nightFloors = levelRoot.GetComponentsInChildren<NightFloor>(true);
            _slabSpawned = new bool[_rooms.Count];

            // The fleeing coffin: find the RealExit if any Flee room contains it.
            // Its trigger starts OFF — you can only claim it once it's cornered,
            // never by having it flee THROUGH you (which used to win instantly).
            foreach (var t in levelRoot.GetComponentsInChildren<Trap>(true))
                if (t.type == TrapType.RealExit && RoomAt(t.transform.position.x) is int ri &&
                    ri >= 0 && _rooms[ri].Rule == RoomRule.Flee)
                {
                    _fleeExit = t.transform;
                    _fleeCol = t.GetComponent<Collider2D>();
                    if (_fleeCol != null) _fleeCol.enabled = false;
                }

            // Loop zones live at the doorway of every Loop room. Never on the
            // final room — its "doorway" is the end of the level, past the exit.
            for (int i = 0; i < _rooms.Count - 1; i++)
                if (_rooms[i].Rule == RoomRule.Loop) BuildLoopZone(_rooms[i]);

            foreach (var r in level.SleepRunes) BuildSleepRune(r);
            foreach (var gr in level.GravRunes) BuildGravRune(gr);
            foreach (var g in level.Gates) BuildGate(g);
            foreach (var s in level.ShiftSpikes) BuildShiftSpike(s.x, s.y);
            BuildDots();
            BuildCurtains();
            // Frame the spawn room before the first rendered frame, so the level
            // never flashes the old wide corridor zoom for an instant.
            int start = RoomAt(player.position.x);
            if (start >= 0) EnterRoom(start);
        }

        void LateUpdate()
        {
            if (_rooms == null || _rooms.Count == 0 || _player == null) return;

            int idx = RoomAt(_player.position.x);
            if (idx != _active && idx >= 0) EnterRoom(idx);
            if (_active < 0) return;

            var room = _rooms[_active];

            // The rule fires once the player is deep enough in — and STAYS fired
            // while they're in this room, so backing toward the doorway can't
            // turn the room honest again and let them scout it from safety.
            if (!_fired && _player.position.x >= room.TriggerX)
            {
                _fired = true;
                OnRuleFired(room);
            }

            _darkWanted = _fired && room.Rule == RoomRule.Dark;
            UpdateDark();
            UpdateNightFloors();

            // The curse only holds while you're in its room: topping the timer
            // up every frame means it lapses on its own the moment you leave.
            if (_fired && room.Rule == RoomRule.Reverse && _pc != null)
                _pc.SetReversed(0.12f);

            UpdateFlee(room);
        }

        int RoomAt(float x)
        {
            for (int i = 0; i < _rooms.Count; i++)
                if (x >= _rooms[i].MinX && x < _rooms[i].MaxX) return i;
            return -1;   // past the end wall — keep whatever was active
        }

        void EnterRoom(int idx)
        {
            _active = idx;
            _fired = false;
            var r = _rooms[idx];
            // Crossing forward BANKS the stage you just finished (no-op on
            // respawn re-entry or the first room) — chime + shake live in
            // GameRoot.BankStage, so respawn seal rebuilds stay silent.
            if (GameRoot.I != null) GameRoot.I.BankStage(idx);
            int banked = GameRoot.I != null ? GameRoot.I.StageIndex : 0;
            SealUpTo(banked);
            // Lock the camera to this chamber: crossing a doorway is a screen
            // transition, not a scroll. This is what makes five rooms FEEL like
            // five rooms instead of one corridor with pillars in it.
            if (GameRoot.I != null) GameRoot.I.FocusRoom(r.MinX, r.MaxX);
            // Slide the darkness up to this room's walls.
            if (_curtainL != null) _curtainL.transform.position = new Vector3(r.MinX - 17f, 0.35f, 0f);
            if (_curtainR != null) _curtainR.transform.position = new Vector3(r.MaxX + 17f, 0.35f, 0f);
            // Dots: banked stages fill GOLD and stay filled (the Level Devil
            // readout — progress you cannot lose), the live stage is bright
            // white, the future is dim.
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null) continue;
                if (i == idx)
                { _dots[i].color = new Color(1f, 1f, 1f, 0.95f); _dots[i].rectTransform.localScale = Vector3.one * 1.25f; }
                else if (i < banked)
                { _dots[i].color = Theme.Coin; _dots[i].rectTransform.localScale = Vector3.one; }
                else
                { _dots[i].color = new Color(1f, 1f, 1f, 0.30f); _dots[i].rectTransform.localScale = Vector3.one; }
            }
            if (_roomLabel != null) _roomLabel.text = $"STAGE {idx + 1} / {_rooms.Count}";
        }

        // Seal every doorway behind the highest stage reached: banked stages are
        // committed. Rebuilt from scratch after each death (the level root is
        // destroyed), so this also restores the walls on respawn.
        readonly HashSet<int> _sealed = new();
        void SealUpTo(int banked)
        {
            for (int j = 1; j <= banked && j < _rooms.Count; j++)
            {
                if (!_sealed.Add(j)) continue;
                float x = _rooms[j].MinX;
                var go = new GameObject("StageSeal");
                go.transform.SetParent(_levelRoot, false);
                go.transform.position = new Vector3(x, -1.9f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Theme.StoneTile;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(0.6f, 1.6f);   // fills the doorway gap exactly (floor top to lintel)
                sr.sortingOrder = 1;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.6f, 1.6f);
                col.enabled = false;                  // arms once the player is clear — never inside them
                go.AddComponent<StageSeal>().Arm(_player, col);
            }
        }

        void OnRuleFired(RoomSpec room)
        {
            switch (room.Rule)
            {
                case RoomRule.Press:
                    if (!_slabSpawned[_active]) { _slabSpawned[_active] = true; BuildSlab(room); }
                    break;
                case RoomRule.Reverse:
                    if (_reverseToastRoom != _active)
                    {
                        _reverseToastRoom = _active;
                        GameRoot.I?.RoomToast("Your hands are not your own.");
                    }
                    break;
            }
        }

        // ---------------- The fleeing coffin ----------------
        // The exit bolts when you get close, at just under your run speed — you
        // gain on it slowly, which is the joke — until it corners itself against
        // the end wall, where it becomes catchable. It floats over gaps (a
        // haunted coffin; the CHASER is the one who must respect the spikes).
        //
        // Three rules keep the chase honest: it flees only RIGHTWARD toward the
        // end wall (never back into the entry corner, which used to let you pen
        // it at the doorway and skip the gauntlet); it flees only while the
        // room's rule has fired (respects TriggerX like everything else); and its
        // trigger is OFF until cornered, so it can't be won by having it slide
        // through you.
        void UpdateFlee(RoomSpec room)
        {
            if (_fleeExit == null || room.Rule != RoomRule.Flee || !_fired) return;
            var p = _fleeExit.position;
            float dx = p.x - _player.position.x;           // >0: coffin is ahead (to the right)
            float rightWall = room.MaxX - 1.1f;

            // Flee only when chased from behind and not yet cornered.
            bool canFlee = dx > 0f && dx < 4.2f && p.x < rightWall - 0.01f;
            if (canFlee)
            {
                p.x = Mathf.Min(p.x + 6.3f * Time.deltaTime, rightWall);
                _fleeExit.position = p;
                if (!_fleeToasted) { _fleeToasted = true; GameRoot.I?.RoomToast("The coffin declines."); }
            }

            // Cornered = pinned at the wall. Only then does it become catchable.
            bool cornered = p.x >= rightWall - 0.01f;
            if (cornered && !_fleeCornered)
            {
                _fleeCornered = true;
                if (_fleeCol != null) _fleeCol.enabled = true;
                GameRoot.I?.RoomToast("Cornered. Take it.");
            }
        }

        // ---------------- Dark ----------------
        void UpdateDark()
        {
            _darkT = Mathf.MoveTowards(_darkT, _darkWanted ? 1f : 0f, Time.unscaledDeltaTime / DarkFade);
            if (_darkT <= 0.001f)
            {
                if (_darkGO != null) _darkGO.SetActive(false);
                return;
            }
            if (_darkGO == null) BuildDark();
            if (!_darkGO.activeSelf) _darkGO.SetActive(true);

            var canvasRT = (RectTransform)Theme.Canvas.transform;
            var size = canvasRT.rect.size;
            float diag = Mathf.Sqrt(size.x * size.x + size.y * size.y);
            float s = diag * 2.2f;                 // ≥ 2× diagonal ⇒ covers every corner from anywhere
            _darkRT.sizeDelta = new Vector2(s, s);

            var cam = Camera.main;
            if (cam != null)
            {
                Vector2 sp = cam.WorldToScreenPoint(_player.position + Vector3.up * 0.3f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, sp, Theme.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam,
                    out var local);
                _darkRT.anchoredPosition = local;
            }

            var c = _darkImg.color;
            // Never quite 1 — a hair of visibility keeps it "a dark room" instead
            // of "the game crashed", which matters to already-suspicious players.
            c.a = _darkT * 0.965f;
            _darkImg.color = c;
        }

        void BuildDark()
        {
            _darkGO = new GameObject("DarkMask", typeof(RectTransform));
            _darkGO.transform.SetParent(Theme.Canvas.transform, false);
            _darkRT = (RectTransform)_darkGO.transform;
            _darkRT.anchorMin = _darkRT.anchorMax = _darkRT.pivot = new Vector2(0.5f, 0.5f);
            _darkImg = _darkGO.AddComponent<Image>();
            _darkImg.sprite = Theme.DarkMask;
            _darkImg.color = new Color(0.02f, 0.01f, 0.03f, 0f);
            _darkImg.raycastTarget = false;
            _darkGO.transform.SetAsFirstSibling();   // above the world, below HUD/menus
        }

        // Night floors vanish the instant their room commits to dark (darkT>0.5)
        // rather than fading with the light — you're meant to run onto ground you
        // watched yourself walk over and find nothing. Ghost floors are the same
        // switch, opposite polarity.
        void UpdateNightFloors()
        {
            bool dark = _darkWanted && _darkT > 0.5f;
            for (int i = 0; i < _nightFloors.Length; i++)
            {
                var nf = _nightFloors[i];
                if (nf == null) continue;
                bool mine = nf.x >= _rooms[_active].MinX && nf.x < _rooms[_active].MaxX;
                bool solid = nf.ghost ? (mine && dark) : !(mine && dark);
                nf.SetSolid(solid);
            }
            // Shift spikes teleport the instant the room commits either way. In
            // the dark you can't see the move happen, which is the whole trick —
            // BUT a spike must never materialise on top of (or right in front of)
            // the player: that's an uncounterable death. If the target spot is
            // within the guard radius, defer the move a frame; because darkX is
            // always placed AHEAD of the player, they walk INTO a visible spike
            // rather than have one appear inside them.
            const float ShiftGuard = 2.5f;
            for (int i = 0; i < _shiftSpikes.Count; i++)
            {
                var ss = _shiftSpikes[i];
                if (ss == null) continue;
                bool mine = ss.litX >= _rooms[_active].MinX && ss.litX < _rooms[_active].MaxX;
                bool wantDark = mine && dark;
                if (wantDark == ss.IsDark) continue;                 // already where it wants to be
                float targetX = wantDark ? ss.darkX : ss.litX;
                if (Mathf.Abs(targetX - _player.position.x) < ShiftGuard) continue; // too close — wait
                ss.Place(wantDark);
            }
        }

        // ---------------- built pieces ----------------

        void BuildSlab(RoomSpec room)
        {
            float w = room.MaxX - room.MinX - 0.5f;
            var go = new GameObject("CryptPress");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = new Vector3(room.MinX + (room.MaxX - room.MinX) / 2f, 2.5f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.StoneTile;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(w, 1.2f);
            sr.color = new Color(0.75f, 0.7f, 0.72f);
            sr.sortingOrder = 4;
            // A blood-red grinding edge so the underside reads as the dangerous part.
            var lip = new GameObject("Lip");
            lip.transform.SetParent(go.transform, false);
            lip.transform.localPosition = new Vector3(0f, -0.62f, 0f);
            var lsr = lip.AddComponent<SpriteRenderer>();
            lsr.sprite = Theme.Square;
            lsr.color = Theme.PlatEdge;
            lsr.sortingOrder = 5;
            lsr.transform.localScale = new Vector3(w, 0.1f, 1f);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(w - 0.15f, 1.05f);
            go.AddComponent<PressSlab>();
            _slabs.Add(go);
        }

        void BuildLoopZone(RoomSpec room)
        {
            var go = new GameObject("LoopZone");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = new Vector3(room.MaxX - 1.35f, -2.35f, 0f);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.1f, 0.6f);    // low — jumping it clears comfortably
            var lz = go.AddComponent<LoopZone>();
            lz.returnX = room.MinX + 1.4f;
            lz.SetGlyph(BuildRuneGlyph(go.transform, new Color(0.85f, 0.15f, 0.2f, 0.8f)));
        }

        void BuildSleepRune(Vector2 pos)
        {
            var go = new GameObject("SleepRune");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = new Vector3(pos.x, -2.5f, 0f);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.1f, 0.35f);   // hugging the floor — jump it
            var sz = go.AddComponent<SleepRuneZone>();
            // Candle-bone gold, not purple — the lullaby is candlelight, and the
            // palette here is stone/blood/bone/gold only (playtest: "where did
            // purple come from?"). A soft haze of the same light sells "drowsy"
            // without leaving the theme.
            var candle = new Color(0.93f, 0.85f, 0.6f, 0.85f);
            sz.SetGlyph(BuildRuneGlyph(go.transform, candle));
            for (int i = 0; i < 2; i++)
            {
                var m = new GameObject("Haze" + i);
                m.transform.SetParent(go.transform, false);
                m.transform.localPosition = new Vector3(i == 0 ? -0.2f : 0.25f, 0.35f + i * 0.25f, 0f);
                m.transform.localScale = Vector3.one * (i == 0 ? 0.8f : 0.5f);
                var sr = m.AddComponent<SpriteRenderer>();
                sr.sprite = Theme.Disc;
                sr.color = new Color(candle.r, candle.g, candle.b, 0.10f);
                sr.sortingOrder = 3;
            }
        }

        // A gravity rune: (x, y, dud flag, unused). Spectral blue-white — the
        // same palette as the ghost floors, so "pale blue = the castle's dead
        // magic" stays one consistent visual promise. Floor runes point their
        // chevrons UP (you'll fall that way), ceiling runes point DOWN.
        void BuildGravRune(Vector4 spec)
        {
            bool onCeiling = spec.y > 0f;
            var go = new GameObject(spec.z > 0.5f ? "GravRuneDud" : "GravRune");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = new Vector3(spec.x, spec.y, 0f);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.1f, 0.5f);    // hugs its surface — jumping past skips it
            var gz = go.AddComponent<GravityRuneZone>();
            gz.dud = spec.z > 0.5f;

            var spectral = new Color(0.62f, 0.78f, 1f, 0.85f);
            var parts = new List<SpriteRenderer>(BuildRuneGlyph(go.transform, spectral));
            // Two stacked chevrons pointing the way you'll fall (^ from rotated
            // bars), floating just off the rune. A dud draws them IDENTICALLY —
            // the lie only breaks on touch.
            float dir = onCeiling ? -1f : 1f;
            for (int c = 0; c < 2; c++)
            {
                for (int sSide = -1; sSide <= 1; sSide += 2)
                {
                    var barGo = new GameObject("Chev" + c + (sSide < 0 ? "L" : "R"));
                    barGo.transform.SetParent(go.transform, false);
                    barGo.transform.localPosition =
                        new Vector3(sSide * 0.14f, dir * (0.42f + c * 0.3f), 0f);
                    barGo.transform.localRotation = Quaternion.Euler(0f, 0f, sSide * dir * -35f);
                    var sr = barGo.AddComponent<SpriteRenderer>();
                    sr.sprite = Theme.Square;
                    sr.color = spectral;
                    sr.sortingOrder = 3;
                    barGo.transform.localScale = new Vector3(0.34f, 0.09f, 1f);
                    parts.Add(sr);
                }
            }
            foreach (var p in parts) p.gameObject.AddComponent<FaintPulse>().max = 0.85f;
            gz.SetGlyph(parts.ToArray());
        }

        // Everything outside the active chamber drowns in darkness: two huge
        // near-black drapes flanking the room, repositioned at each doorway.
        // This is what turns "a corridor you can see three rooms of" into ONE
        // SCREEN PER ROOM — and it's thematically free, because an unlit castle
        // is exactly what this castle is. (The flat gray "arch" frames tried
        // first looked like goalposts — playtest: "you added some type of
        // doors… that does not look good" — so the doorways are now framed by
        // light instead of geometry.)
        GameObject _curtainL, _curtainR;
        void BuildCurtains()
        {
            var shade = new Color(0.035f, 0.015f, 0.05f, 0.97f);
            _curtainL = Theme.Box("CurtainL", _levelRoot, Vector2.zero, new Vector2(34f, 16f), shade, 60);
            _curtainR = Theme.Box("CurtainR", _levelRoot, Vector2.zero, new Vector2(34f, 16f), shade, 60);
        }

        void BuildGate(float x)
        {
            var go = new GameObject("Portcullis");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = new Vector3(x, -0.15f, 0f);
            var iron = new Color(0.17f, 0.15f, 0.19f);
            for (int i = -1; i <= 1; i++)
            {
                var bar = new GameObject("Bar");
                bar.transform.SetParent(go.transform, false);
                bar.transform.localPosition = new Vector3(i * 0.22f, 0f, 0f);
                var sr = bar.AddComponent<SpriteRenderer>();
                sr.sprite = Theme.Square;
                sr.color = iron;
                sr.sortingOrder = 4;
                bar.transform.localScale = new Vector3(0.09f, 1.7f, 1f);
                var tip = new GameObject("Tip");
                tip.transform.SetParent(go.transform, false);
                tip.transform.localPosition = new Vector3(i * 0.22f, -0.89f, 0f);
                var tsr = tip.AddComponent<SpriteRenderer>();
                tsr.sprite = Theme.Square;
                tsr.color = Theme.Danger;
                tsr.sortingOrder = 4;
                tip.transform.localScale = new Vector3(0.09f, 0.14f, 1f);
            }
            var crossbar = new GameObject("Crossbar");
            crossbar.transform.SetParent(go.transform, false);
            crossbar.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var csr = crossbar.AddComponent<SpriteRenderer>();
            csr.sprite = Theme.Square;
            csr.color = iron;
            csr.sortingOrder = 4;
            crossbar.transform.localScale = new Vector3(0.62f, 0.1f, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.55f, 1.6f);
            go.AddComponent<Portcullis>().Setup(_player, col);
        }

        // Identical to a normal spike in every pixel — that's the point.
        void BuildShiftSpike(float litX, float darkX)
        {
            var sp = Assets.Sprite("spike");
            var pos = new Vector2(litX, -2.4f);
            var size = new Vector2(0.7f, 0.7f);
            GameObject go = sp != null
                ? Theme.SpriteBox("ShiftSpike", _levelRoot, pos, size, sp, 3)
                : Theme.Box("ShiftSpike", _levelRoot, pos, size, Theme.Danger, 3);
            if (sp != null) go.GetComponent<SpriteRenderer>().color = Theme.Danger;
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) col.size = sr.sprite.bounds.size * 0.85f;
            var kz = go.AddComponent<KillZone>();
            kz.msg = "The dark moved them.";
            kz.trapTag = (int)TrapType.SpikeStatic;
            var mark = go.AddComponent<ShiftSpikeMark>();
            mark.litX = litX; mark.darkX = darkX;
            _shiftSpikes.Add(mark);
        }

        // A flat glowing floor-glyph: a strip plus two ticks. Deliberately the
        // SAME shape for sleep (purple) and loop (red) runes — "glowing marks on
        // the floor are never good news" becomes one lesson, learned once.
        SpriteRenderer[] BuildRuneGlyph(Transform parent, Color c)
        {
            var parts = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var g = new GameObject("Glyph" + i);
                g.transform.SetParent(parent, false);
                var sr = g.AddComponent<SpriteRenderer>();
                sr.sprite = Theme.Square;
                sr.color = c;
                sr.sortingOrder = 3;
                if (i == 0) { g.transform.localPosition = new Vector3(0, -0.12f, 0); g.transform.localScale = new Vector3(1.15f, 0.12f, 1f); }
                else
                {
                    float s = i == 1 ? -1f : 1f;
                    g.transform.localPosition = new Vector3(s * 0.3f, 0.06f, 0);
                    g.transform.localScale = new Vector3(0.12f, 0.28f, 1f);
                }
                parts[i] = sr;
            }
            return parts;
        }

        void BuildDots()
        {
            if (_rooms.Count < 2) return;
            _dotsGO = new GameObject("RoomDots", typeof(RectTransform));
            _dotsGO.transform.SetParent(Theme.Canvas.transform, false);
            var rt = (RectTransform)_dotsGO.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -18f);
            _dots = new Image[_rooms.Count];
            float span = (_rooms.Count - 1) * 24f;
            for (int i = 0; i < _rooms.Count; i++)
            {
                var d = new GameObject("Dot" + i, typeof(RectTransform));
                d.transform.SetParent(_dotsGO.transform, false);
                var drt = (RectTransform)d.transform;
                drt.anchoredPosition = new Vector2(-span / 2f + i * 24f, 0f);
                drt.sizeDelta = new Vector2(13f, 13f);
                var img = d.AddComponent<Image>();
                img.sprite = Theme.Disc;
                img.color = new Color(1f, 1f, 1f, 0.30f);
                img.raycastTarget = false;
                _dots[i] = img;
            }
            // "STAGE 3 / 5" under the dots: the dots alone were too quiet — the
            // playtest read the whole floor as one corridor, so the sub-level
            // structure says its own name. Sits at -46 so its rect clears the
            // dot band (they used to overlap and crowd each other).
            _roomLabel = Theme.Label(_dotsGO.transform, "STAGE 1 / " + _rooms.Count, 22,
                new Color(1f, 1f, 1f, 0.55f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -46f), new Vector2(320f, 30f));
        }

        void OnDestroy()
        {
            if (_darkGO != null) Destroy(_darkGO);
            if (_dotsGO != null) Destroy(_dotsGO);
        }
    }
}
