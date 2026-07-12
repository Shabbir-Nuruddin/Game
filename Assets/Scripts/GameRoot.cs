using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// Owns the whole game: auto-boots on Play, runs a main menu (New Game /
    /// Continue), builds levels from data, follows the player with the camera,
    /// handles death/respawn, level progression, a pause menu, and the win flow.
    /// Respawn rebuilds the level, so every trap resets with zero bookkeeping.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }

        enum State { Menu, Play, Paused, Win }
        State _state = State.Menu;

        Camera _cam;
        Transform _levelRoot;
        PlayerController _player;
        Transform _playerVisual;
        // Where the player is right now (null between lives) — read by enemies/bosses.
        public Transform PlayerTransform => _player != null ? _player.transform : null;

        // Sun-rise pressure: dawdle past _sunThreshold seconds and daylight floods
        // the level from behind — a lethal advancing wall. The vampire must keep ahead.
        bool _sunRising;
        float _sunThreshold = 999f, _sunWallX;
        GameObject _sunWall;

        // True while fighting a boss (the arena floor at _level.BossTier > 0).
        bool InBossRoom => _level != null && _level.BossTier > 0;

        // ---- core feel & loop (Round 4) ----
        float _levelEndX;                                  // right edge of the floor (near-miss calc)
        float _levelStartX;                                // left edge — clamps server-sourced echo graves
        // Reactive "Trust Issues" traps: where the player lingered safely → on retry
        // a late-spike appears there. Accumulates with deaths; resets per floor.
        readonly System.Collections.Generic.Dictionary<int, float> _linger = new();
        readonly System.Collections.Generic.List<float> _ghostTrapX = new();

        // Ghost replay: race your PREVIOUS attempt on this floor.
        readonly System.Collections.Generic.List<float> _recT = new();
        readonly System.Collections.Generic.List<Vector3> _recP = new();
        float[] _lastT; Vector3[] _lastP; float _recTimer;
        bool _newBest;       // this run beat your stored best → celebrate on the result screen
        bool _reactiveAdded; // a reactive trap was just learned → play the troll laugh next build
        Level _level;
        int _levelIndex;
        int _deaths;
        bool _dying;
        Vector3 _checkpoint;
        bool _hasCheckpoint;

        enum Mode { Curated, Endless, Daily, Versus }
        Mode _mode = Mode.Curated;
        int _endlessSeed;
        const int DailyLen = 5;
        int _hearts;          // lives in Endless/Daily; -1 = infinite (Curated)
        int _bossHp;          // chip-hits left in a boss arena (the rest of the game is one-shot)
        float _bossIFrames;   // brief mercy window after taking a boss chip hit
        int _bossGen;         // bumped each boss (re)build so stale pickup coroutines bail
        // The live boss this arena, if any — Bullet runs its swept hit test against
        // this directly (trigger events vs the transform-animated boss dropped hits).
        public Boss ActiveBoss { get; private set; }
        GameObject _gunPickup;// the active weapon pickup in the arena (null while held)
        const int BossClip = 5;   // shots granted per weapon pickup
        int _bossIntroedTier = -1;// which boss already played its cutscene this run (skip on retries)
        Image _flyBar;        // flight-meter fill

        // ---- analytics ----
        float _levelStartRealtime;  // when the current level attempt began (for durations)
        float _heartbeatTimer;      // emits a "still playing" ping every few seconds
        string ModeName => _mode.ToString();
        int LevelDurationMs => Mathf.RoundToInt((Time.realtimeSinceStartup - _levelStartRealtime) * 1000f);
        float _camMin = -1.5f, _camMax = -1.5f;
        const float CamY = -1.2f;
        const float NormalCamSize = 5.6f;   // platforming zoom; boss arenas pull back to show the whole room

        // 2.5D depth mode (perspective camera + real-depth parallax + platform
        // extrusions + cinematic dollies). Default ON; the settings toggle flips
        // back to the classic flat camera instantly if a machine struggles.
        public static bool Depth25 => PlayerPrefs.GetInt("opt_25d", 1) == 1;
        CameraRig _rig;
        Vector3 _moonBaseLocal, _moonBaseScale;   // flat-mode placement, rescaled for depth mode

        Text _hud, _toast;
        GameObject _menuPanel, _pausePanel, _touchPanel, _rotatePanel;
        Parallax _parallax;

        // ---- multiplayer (Versus race) ----
        readonly System.Collections.Generic.Dictionary<int, Ghost> _ghosts = new();
        float _netSendTimer;
        bool _netHooked;
        bool _raceOver;
        Text _lobbyStatus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindFirstObjectByType<GameRoot>() == null)
                new GameObject("TrustIssues").AddComponent<GameRoot>();
        }

        bool _isMobile;

        void Awake()
        {
            I = this;
            Analytics.Init();
            Memory.SessionStart();   // snapshot absence/rage-quit BEFORE anything overwrites them
            Analytics.Track("session_start", new System.Collections.Generic.Dictionary<string, object>
            {
                { "platform", Application.platform.ToString() },
                { "mobile", Application.isMobilePlatform },
                { "screen", Screen.width + "x" + Screen.height },
                // The join key for the whole first-60-seconds funnel: every other
                // event of a new player's session hangs off this flag.
                { "first_session", Memory.IsFirstSession },
            });
            // Real mobile only. On WebGL, `Input.touchSupported` is true on any
            // touch-capable LAPTOP, which wrongly showed phone buttons in the
            // browser — desktop web is keyboard-only.
            _isMobile = Application.isMobilePlatform;
            // Keep simulating when the window isn't focused — otherwise a second
            // (unfocused) instance pauses, its Photon keepalive stops, and the
            // server times it out (the "no ghost" / AppOutOfFocus disconnect).
            Application.runInBackground = true;
            Time.timeScale = 1f;
            // Toast when a new Bestiary page is revealed (drives the "gotta catch 'em all").
            Codex.OnUnlocked = t => ShowBanner("NEW BESTIARY PAGE", $"{Codex.Title(t)} — read it in the book");
            ResetProgressOncePerVersion();
            Curse.Boot(Application.absoluteURL);   // did someone open a ?haunt= link?
            SetupCamera();
            BuildBackdrop();
            ApplyDepthMode();   // place parallax/moon for flat or 2.5D per opt_25d
            BuildHUD();
            ShowMenu();
        }

        // Re-place everything whose position depends on the camera projection.
        // Called at boot and live from the settings toggle. (Platform depth slices
        // are per-level and appear on the next floor build.)
        void ApplyDepthMode()
        {
            float dist = CameraRig.DistanceFor(NormalCamSize);
            _parallax?.SetDepthMode(Depth25, dist);
            if (_moonSr != null)
            {
                // A camera-child quad at local depth z shows tan(fov/2)·z half-height
                // under perspective vs the fixed ortho size — scale position AND size
                // by the ratio so the moon keeps its screen spot.
                float f = Depth25
                    ? CameraRig.HalfTan * Mathf.Abs(_moonBaseLocal.z) / NormalCamSize : 1f;
                _moonSr.transform.localPosition =
                    new Vector3(_moonBaseLocal.x * f, _moonBaseLocal.y * f, _moonBaseLocal.z);
                _moonSr.transform.localScale = _moonBaseScale * f;
            }
        }

        void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera"); go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
            }
            _cam.backgroundColor = Theme.Sky;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // The rig owns placement/projection from here on (flat OR 2.5D); shake
            // keeps perturbing the camera's local position under the rig parent —
            // which also makes boss-fight shakes actually visible (the old direct
            // position writes in LateUpdate stomped them every frame).
            _rig = CameraRig.Attach(_cam);
            _rig.SetFrame(-1.5f, CamY, NormalCamSize);
        }

        // A multi-layer gothic parallax castle. The source art is a blue ICE
        // castle, but each layer is tinted deep crimson/indigo (white ice ×
        // dark tint = dark silhouette) so it reads as a blood-moon castle at
        // night. Falls back to a flat dim image if the layers aren't present.
        void BuildBackdrop()
        {
            // Solid night fill behind everything (parented to the camera so it
            // always fills the view; transparent parts of layers show this).
            var sky = new GameObject("Sky");
            sky.transform.SetParent(_cam.transform, false);
            sky.transform.localPosition = new Vector3(0f, 0f, 25f);
            sky.transform.localScale = new Vector3(60f, 30f, 1f);
            var sr = sky.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.Square; sr.color = Theme.Sky; sr.sortingOrder = -30;
            _skySr = sr;

            // THE FIX for "every background looks the same": the parallax castle art is
            // near-black, so multiplying a theme tint over it stays near-black (no visible
            // change). This semi-transparent COLOUR WASH sits in front of the whole
            // backdrop (but BEHIND gameplay at order >= 0), so each mode/world reads as a
            // clearly different colour. ApplyTheme sets its colour.
            var wash = new GameObject("ThemeWash");
            wash.transform.SetParent(_cam.transform, false);
            wash.transform.localPosition = new Vector3(0f, 0f, 22f);
            wash.transform.localScale = new Vector3(60f, 30f, 1f);
            var wsr = wash.AddComponent<SpriteRenderer>();
            wsr.sprite = Theme.Square; wsr.sortingOrder = -10;   // over parallax, under gameplay
            wsr.color = ThemeWash[0];
            _washSr = wsr;

            // A big themed moon in the upper sky (in FRONT of the wash so it stays bold),
            // camera-parented so it's always visible. Its colour changes per theme.
            if (Theme.Moon != null)
            {
                var moon = new GameObject("ThemeMoon");
                moon.transform.SetParent(_cam.transform, false);
                moon.transform.localPosition = new Vector3(6.5f, 4.6f, 21f);
                var mb = Theme.Moon.bounds.size;
                float ms = 7.5f / Mathf.Max(0.0001f, mb.y);
                moon.transform.localScale = new Vector3(ms, ms, 1f);
                var msr = moon.AddComponent<SpriteRenderer>();
                msr.sprite = Theme.Moon; msr.sortingOrder = -8; msr.color = ThemeMoon[0];
                _moonSr = msr;
                // Remember flat-mode placement: depth mode pushes camera-child quads
                // through a perspective projection, so the moon is rescaled about the
                // view axis to keep the same screen spot/size (see ApplyDepthMode).
                _moonBaseLocal = moon.transform.localPosition;
                _moonBaseScale = moon.transform.localScale;
            }

            BuildAmbient();   // drifting motes so every backdrop has motion, not a still image

            if (Assets.Sprite("bg_castle") != null)
            {
                var root = new GameObject("Parallax");
                _parallax = root.AddComponent<Parallax>();
                _parallax.Init(_cam.transform);
                float camX = _cam.transform.position.x;
                //          sprite      tint(multiply)    yCenter order  follow alpha
                AddParallax("bg_sky",    Theme.Hex("16101F"), 1.2f,  -28, 0.97f);
                AddParallax("bg_far",    Theme.Hex("2A2038"), 0.9f,  -24, 0.90f);
                AddParallax("bg_castle", Theme.Hex("531B26"), 0.6f,  -20, 0.82f); // blood-red castle
                AddParallax("bg_mid",    Theme.Hex("241A30"), 0.2f,  -17, 0.72f);
                AddParallax("bg_near",   Theme.Hex("140E1C"), -0.4f, -14, 0.60f);
                AddParallax("bg_fog",    Theme.Hex("3A1622"), 0.4f,  -12, 0.55f, 0.30f);
                return;
            }

            // Fallback: the old flat vampire forest image, camera-parented.
            var bg = Assets.Sprite("bg_vampire");
            if (bg != null)
            {
                var go = new GameObject("BG");
                go.transform.SetParent(_cam.transform, false);
                go.transform.localPosition = new Vector3(0f, 1.5f, 19f);
                var bsr = go.AddComponent<SpriteRenderer>();
                bsr.sprite = bg; bsr.sortingOrder = -18;
                bsr.color = new Color(0.7f, 0.65f, 0.75f, 1f);
                var b = bg.bounds.size;
                go.transform.localScale = new Vector3(24f / b.x, 16f / b.y, 1f);
            }
        }

        // A drifting field of glowing motes (embers/dust) parented to the camera, so
        // every backdrop has gentle ambient motion instead of a static image. Their
        // colour is themed per mode/world by ApplyTheme.
        void BuildAmbient()
        {
            var root = new GameObject("Ambient");
            root.transform.SetParent(_cam.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, 20f);
            // Order -9 keeps the motes IN FRONT of the colour wash (-10) so they read
            // clearly; bigger + a touch faster so the motion is actually noticeable.
            for (int i = 0; i < 44; i++)
            {
                var go = Theme.Box("Mote", root.transform, Vector2.zero, new Vector2(0.13f, 0.13f), Color.white, -9);
                float s = Random.Range(0.6f, 2.0f);
                go.transform.localPosition = new Vector3(Random.Range(-17f, 17f), Random.Range(-11f, 11f), 0f);
                go.transform.localScale = new Vector3(0.13f * s, 0.13f * s, 1f);
                var m = go.AddComponent<Mote>();
                m.Init(new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.15f, 0.7f), 0f),
                       new Color(1f, 1f, 1f, 0.6f));
                _motes.Add(m);
            }
        }

        // One parallax layer: the sprite scaled wide (keeping aspect) and tinted,
        // centred on the camera's start so depth reads from the very first frame.
        void AddParallax(string sprite, Color tint, float yCenter, int order, float follow, float alpha = 1f)
        {
            var sp = Assets.Sprite(sprite);
            if (sp == null || _parallax == null) return;
            var go = new GameObject(sprite);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp; sr.sortingOrder = order;
            var c = tint; c.a = alpha; sr.color = c;
            const float worldWidth = 32f; // overscan so drift never reveals an edge
            float k = worldWidth / sp.bounds.size.x;
            go.transform.localScale = new Vector3(k, k, 1f);
            go.transform.position = new Vector3(_cam.transform.position.x, yCenter, 12f);
            _parallax.Add(go.transform, follow);
            _bgSr.Add(sr); _bgBase.Add(sr.color);   // remembered so worlds can re-tint
        }

        // ---- Worlds: each 10-floor segment (Castle → Crypt → Swamp → Throne) gets
        // its own colour mood by multiplying a tint over the parallax layers. Cheap,
        // no new art, and makes every stretch (and every clip) look distinct. ----
        readonly System.Collections.Generic.List<SpriteRenderer> _bgSr = new();
        readonly System.Collections.Generic.List<Color> _bgBase = new();
        SpriteRenderer _skySr;

        static readonly string[] WorldNames = { "THE CASTLE", "THE CRYPT", "THE SWAMP", "THE THRONE" };

        // Each MODE gets its own backdrop identity, and Castle/Endless rotate through
        // several so the world visibly changes as you progress (not one static image):
        //   0 Castle · 1 Crypt · 2 Swamp · 3 Throne   (Castle, by 10-floor world)
        //   4 Blood Moon                              (Daily — its own intense look)
        //   5 Abyss · 6 Void · 7 Inferno              (Endless — cycles, never Blood Moon)
        //   8 Arena                                   (Versus)
        static readonly string[] ThemeNames =
        { "THE CASTLE", "THE CRYPT", "THE SWAMP", "THE THRONE", "BLOOD MOON", "THE ABYSS", "THE VOID", "THE INFERNO", "THE ARENA" };
        // Per-theme gameplay music (drop Resources/audio/music_<x>.mp3 in whenever
        // it's ready — until then every theme quietly falls back to "music" via
        // Audio.MusicOr, so a missing track never leaves a floor silent).
        static readonly string[] ThemeMusic =
        { "music_castle", "music_crypt", "music_swamp", "music_throne", "music_bloodmoon", "music_abyss", "music_void", "music_inferno", "music_arena" };
        // Tint MULTIPLIED over the (dark crimson) parallax art — strong enough that each
        // theme reads as a different place, not a faint colour wash.
        static readonly Color[] ThemeTint =
        {
            new Color(1.00f, 1.00f, 1.00f),   // castle
            new Color(0.58f, 0.82f, 1.45f),   // crypt — cold blue
            new Color(0.66f, 1.35f, 0.70f),   // swamp — sickly green
            new Color(1.45f, 1.00f, 0.55f),   // throne — hot gold
            new Color(1.60f, 0.40f, 0.46f),   // blood moon — searing red
            new Color(1.20f, 0.55f, 1.55f),   // abyss — violet
            new Color(0.50f, 1.25f, 1.40f),   // void — teal
            new Color(1.70f, 0.85f, 0.40f),   // inferno — ember orange
            new Color(1.05f, 1.08f, 1.20f),   // arena — cold steel
        };
        static readonly Color[] ThemeSky =
        {
            Theme.Hex("16080E"), Theme.Hex("0A1630"), Theme.Hex("0A2010"), Theme.Hex("241806"),
            Theme.Hex("2A0610"), Theme.Hex("160830"), Theme.Hex("042220"), Theme.Hex("2A1004"),
            Theme.Hex("10141C"),
        };
        // The big visible difference between themes: a translucent colour wash over the
        // whole backdrop (alpha baked in). Without this every backdrop reads near-black.
        // Pushed strong so each mode/world is UNMISTAKABLY a different colour.
        static readonly Color[] ThemeWash =
        {
            new Color(0.42f, 0.12f, 0.17f, 0.46f),  // castle  — crimson
            new Color(0.10f, 0.22f, 0.55f, 0.52f),  // crypt   — cold blue
            new Color(0.10f, 0.42f, 0.16f, 0.52f),  // swamp   — sickly green
            new Color(0.52f, 0.34f, 0.08f, 0.50f),  // throne  — hot amber
            new Color(0.70f, 0.05f, 0.12f, 0.56f),  // blood moon — searing red
            new Color(0.36f, 0.10f, 0.58f, 0.52f),  // abyss   — violet
            new Color(0.04f, 0.48f, 0.44f, 0.52f),  // void    — teal
            new Color(0.68f, 0.20f, 0.04f, 0.54f),  // inferno — ember orange
            new Color(0.18f, 0.24f, 0.40f, 0.48f),  // arena   — cold steel
        };
        // A big themed MOON in the upper sky — a bold, obvious per-theme anchor so the
        // modes read as completely different places at a glance.
        static readonly Color[] ThemeMoon =
        {
            new Color(0.88f, 0.22f, 0.26f, 0.92f),  // castle  — blood moon
            new Color(0.72f, 0.84f, 1.00f, 0.88f),  // crypt   — pale blue
            new Color(0.72f, 1.00f, 0.66f, 0.82f),  // swamp   — sickly green
            new Color(1.00f, 0.82f, 0.42f, 0.92f),  // throne  — gold
            new Color(1.00f, 0.16f, 0.20f, 0.96f),  // blood moon — searing red
            new Color(0.78f, 0.46f, 1.00f, 0.88f),  // abyss   — violet
            new Color(0.46f, 0.96f, 1.00f, 0.88f),  // void    — teal
            new Color(1.00f, 0.56f, 0.20f, 0.92f),  // inferno — ember
            new Color(0.82f, 0.88f, 1.00f, 0.82f),  // arena   — cold white
        };
        // Drifting-mote (ember/star) colour per theme — the animated ambient layer.
        static readonly Color[] ThemeAccent =
        {
            new Color(0.95f, 0.30f, 0.30f, 0.55f), new Color(0.55f, 0.75f, 1.00f, 0.55f),
            new Color(0.55f, 1.00f, 0.60f, 0.55f), new Color(1.00f, 0.78f, 0.35f, 0.55f),
            new Color(1.00f, 0.25f, 0.28f, 0.65f), new Color(0.80f, 0.45f, 1.00f, 0.55f),
            new Color(0.50f, 0.95f, 1.00f, 0.55f), new Color(1.00f, 0.55f, 0.20f, 0.65f),
            new Color(0.80f, 0.85f, 1.00f, 0.50f),
        };

        // How visible the castle parallax is per theme — faded to a distant ruin in the
        // open "void" modes (Endless) so they don't read as "the same castle" as Castle.
        static readonly float[] ThemeCastleVis =
        { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.22f, 0.18f, 0.30f, 0.55f };
        // Moon world-diameter per theme (Blood Moon looms huge; the arena's is small).
        static readonly float[] ThemeMoonSize =
        { 7.5f, 6.0f, 6.5f, 7.0f, 12.0f, 8.0f, 7.0f, 8.5f, 5.0f };

        public static int WorldOf(int floorIdx) => Mathf.Clamp((floorIdx / 10) % 4, 0, 3);

        int _curTheme = -1;
        SpriteRenderer _washSr;        // the themed colour wash (see BuildBackdrop)
        SpriteRenderer _moonSr;        // the big themed moon (bold per-theme anchor)
        readonly System.Collections.Generic.List<Mote> _motes = new();

        // Pick the backdrop theme from the current mode/progress, then apply it.
        void ThemeBackdrop()
        {
            int idx;
            switch (_mode)
            {
                case Mode.Daily:   idx = 4; break;                         // Blood Moon
                case Mode.Versus:  idx = 8; break;                         // Arena
                case Mode.Endless: idx = 5 + (_levelIndex / 10) % 3; break;// Abyss → Void → Inferno
                default:           idx = WorldOf(_levelIndex); break;      // Castle worlds
            }
            bool changed = idx != _curTheme;
            ApplyTheme(idx);
            // Announce a new region as you cross into it (Castle worlds / Endless depths),
            // but not on the first floor, on a death-respawn, or inside a boss arena.
            if (changed && _state == State.Play && _levelIndex > 0 && !InBossRoom &&
                (_mode == Mode.Curated || _mode == Mode.Endless))
                ShowBanner($"ENTERING {ThemeNames[idx]}", "the world shifts around you");
        }

        // Recolour the sky, parallax layers and ambient motes for a theme.
        void ApplyTheme(int idx)
        {
            idx = Mathf.Clamp(idx, 0, ThemeTint.Length - 1);
            if (idx == _curTheme) return;
            _curTheme = idx;
            var t = ThemeTint[idx];
            float castleVis = ThemeCastleVis[idx];
            for (int i = 0; i < _bgSr.Count; i++)
            {
                if (_bgSr[i] == null) continue;
                var b = _bgBase[i];
                _bgSr[i].color = new Color(b.r * t.r, b.g * t.g, b.b * t.b, b.a * castleVis);
            }
            if (_skySr != null) _skySr.color = ThemeSky[idx];
            if (_washSr != null) _washSr.color = ThemeWash[idx];   // the actually-visible colour shift
            if (_moonSr != null && Theme.Moon != null)             // bold per-theme anchor (colour + size)
            {
                _moonSr.color = ThemeMoon[idx];
                float mb = Theme.Moon.bounds.size.y;
                float ms = mb > 0.0001f ? ThemeMoonSize[idx] / mb : 1f;
                _moonSr.transform.localScale = new Vector3(ms, ms, 1f);
            }
            if (_cam != null) _cam.backgroundColor = ThemeSky[idx];
            foreach (var m in _motes) if (m != null) m.Recolor(ThemeAccent[idx]);
        }

        void BuildHUD()
        {
            _hud = Theme.Label(Theme.Canvas.transform, "DEATHS  0", 46, Theme.Player,
                new Vector2(0f, 1f), new Vector2(280, -55), new Vector2(520, 70),
                TextAnchor.MiddleLeft);
            _toast = Theme.Label(Theme.Canvas.transform, "", 60, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1400, 100));

            // Bat-flight meter (top-left under the deaths line).
            var barBg = new GameObject("FlyBarBg", typeof(RectTransform));
            barBg.transform.SetParent(Theme.Canvas.transform, false);
            var bgi = barBg.AddComponent<Image>(); bgi.color = new Color(0, 0, 0, 0.5f);
            var brt = bgi.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 1f);
            // Pushed right so the "BAT" label (sits to the bar's left) is fully on
            // screen — it was clipping off the left edge in Endless/Blood Moon.
            brt.anchoredPosition = new Vector2(150, -110); brt.sizeDelta = new Vector2(320, 26);
            Theme.Label(barBg.transform, "BAT", 22, new Color(1, 1, 1, 0.75f),
                new Vector2(0f, 0.5f), new Vector2(-46, 0), new Vector2(80, 30));
            var fill = new GameObject("FlyBarFill", typeof(RectTransform));
            fill.transform.SetParent(barBg.transform, false);
            _flyBar = fill.AddComponent<Image>(); _flyBar.color = Theme.Player;
            var frt = _flyBar.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.anchoredPosition = new Vector2(2, 0);
            frt.sizeDelta = new Vector2(316, 22);

            // Mute toggle (top-right). Reflects the saved preference immediately.
            _muteBtn = Theme.Button(Theme.Canvas.transform, Audio.Muted ? "✕" : "♪",
                new Color(0, 0, 0, 0.45f), Color.white, 40,
                new Vector2(1f, 1f), new Vector2(-70, -64), new Vector2(96, 96), ToggleMute);

            // Blood-shard counter (top-right, left of the mute button). The diamond
            // icon is a 45°-rotated Image — the pixel font has no ♦ glyph (same
            // reason the hearts HUD uses '*' pips).
            _shardHud = new GameObject("Shards", typeof(RectTransform));
            _shardHud.transform.SetParent(Theme.Canvas.transform, false);
            var srt = _shardHud.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-130, -50); srt.sizeDelta = new Vector2(240, 60);
            var dia = new GameObject("Dia", typeof(RectTransform)).AddComponent<Image>();
            dia.transform.SetParent(_shardHud.transform, false);
            dia.color = Theme.Coin; dia.raycastTarget = false;
            var drt = dia.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 0.5f); drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(24, 0); drt.sizeDelta = new Vector2(20, 20);
            drt.localRotation = Quaternion.Euler(0, 0, 45f);
            _shardText = Theme.Label(_shardHud.transform, Currency.Balance.ToString(), 38, Theme.Coin,
                new Vector2(0f, 0.5f), new Vector2(140, 0), new Vector2(190, 56), TextAnchor.MiddleLeft);
            _shardText.raycastTarget = false;
            _shardHud.SetActive(false);                    // shown alongside _hud during play
            Currency.OnEarned += OnShardsEarned;

            BuildTouchControls();
            BuildRotatePanel();
        }

        GameObject _shardHud;
        Text _shardText;

        // HUD reaction to any shard gain: tick the number, pop the counter. Uses
        // unscaled time because deaths freeze-frame the game (HitStop).
        void OnShardsEarned(int amount, string source)
        {
            if (_shardText != null) _shardText.text = Currency.Balance.ToString();
            if (_shardHud != null && _shardHud.activeInHierarchy)
                StartCoroutine(PopOnce(_shardHud.transform));
        }

        IEnumerator PopOnce(Transform t)
        {
            float e = 0f;
            while (e < 0.22f && t != null)
            {
                e += Time.unscaledDeltaTime;
                float s = 1f + Mathf.Sin(Mathf.Clamp01(e / 0.22f) * Mathf.PI) * 0.25f;
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        Button _muteBtn;
        void ToggleMute()
        {
            Audio.Muted = !Audio.Muted;
            var label = _muteBtn != null ? _muteBtn.GetComponentInChildren<Text>() : null;
            if (label != null) label.text = Audio.Muted ? "✕" : "♪";
            if (!Audio.Muted) Audio.Play("click", 0.6f);
        }

        // Phone: ask the player to hold the phone sideways (landscape) — this is
        // a side-scroller, so portrait is unplayable. Desktop never sees this.
        void BuildRotatePanel()
        {
            _rotatePanel = new GameObject("Rotate", typeof(RectTransform));
            _rotatePanel.transform.SetParent(Theme.Canvas.transform, false);
            var img = _rotatePanel.AddComponent<Image>();
            img.color = new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.98f);
            var rt = _rotatePanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Theme.Label(_rotatePanel.transform, "↻", 220, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(400, 300));
            Theme.Label(_rotatePanel.transform, "Rotate your phone\nto landscape", 56, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(1400, 250));
            _rotatePanel.SetActive(false);
        }

        // On-screen buttons for phones (also work with the mouse). Hidden in menus.
        void BuildTouchControls()
        {
            _touchPanel = new GameObject("Touch", typeof(RectTransform));
            _touchPanel.transform.SetParent(Theme.Canvas.transform, false);
            var rt = _touchPanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Small circular pads hugging the corners so they hide as little of the
            // playfield as possible (TouchButton pads the HIT zone ~25% past the
            // visual, so "small" stays comfortably tappable). Positions keep every
            // hit zone clear of its neighbours — overlapping zones would fire two
            // actions with one finger. Movement arrows bottom-left…
            MakeTouch("‹", -1, new Vector2(0f, 0f), new Vector2(145, 145), new Vector2(150, 150), 0.16f);
            MakeTouch("›", 1, new Vector2(0f, 0f), new Vector2(345, 145), new Vector2(150, 150), 0.16f);
            // …action cluster bottom-right. JUMP is always there; the rest are shown
            // contextually (bat in Blood Moon/Endless, dash if the skin grants it,
            // SHOOT only while holding a loaded gun) via UpdateTouchLayout().
            MakeTouch("JUMP", 0, new Vector2(1f, 0f), new Vector2(-150, 145), new Vector2(170, 170), 0.18f);
            _btnFly   = MakeTouch("BAT",   3, new Vector2(1f, 0f), new Vector2(-360, 120), new Vector2(130, 130), 0.20f);
            _btnDash  = MakeTouch("DASH",  4, new Vector2(1f, 0f), new Vector2(-140, 350), new Vector2(130, 130), 0.18f);
            _btnShoot = MakeTouch("SHOOT", 2, new Vector2(1f, 0f), new Vector2(-360, 310), new Vector2(130, 130), 0.22f);
            _touchPanel.SetActive(false);
        }

        GameObject _btnFly, _btnDash, _btnShoot;

        // On-screen controls show on real mobile browsers (isMobilePlatform already
        // detects those on WebGL and excludes touch laptops) OR when force-enabled in
        // Settings (handy for testing the layout on desktop).
        bool TouchControlsOn => Application.isMobilePlatform || PlayerPrefs.GetInt("opt_touch", 0) == 1;

        // Show only the action buttons that are usable right now. Polled every frame
        // from Update() (and still called on level builds / gun events), so the
        // cluster tracks live state in EVERY mode — a gun picked up mid-arena grows
        // a SHOOT button the same frame, an emptied clip removes it, a skin swap
        // adds/removes DASH. All SetActive calls are change-guarded so the per-frame
        // poll costs nothing when nothing changed.
        void UpdateTouchLayout()
        {
            if (_touchPanel == null) return;
            bool show = TouchControlsOn && _state == State.Play;
            if (_touchPanel.activeSelf != show) _touchPanel.SetActive(show);
            if (!show) return;
            // BAT only in modes that allow flight; DASH only if the equipped skin grants
            // it; SHOOT only while you actually HOLD a weapon (ammo > 0) in a boss arena.
            SyncTouchButton(_btnFly,   _player != null && _player.canFly);
            SyncTouchButton(_btnDash,  _player != null && _player.dashEnabled);
            SyncTouchButton(_btnShoot, _player != null && _player.canShoot && _player.ammo > 0);
        }

        static void SyncTouchButton(GameObject btn, bool on)
        {
            if (btn != null && btn.activeSelf != on) btn.SetActive(on);
        }

        GameObject MakeTouch(string label, int dir, Vector2 anchor, Vector2 pos, Vector2 size, float alpha)
        {
            var go = new GameObject("Touch_" + label, typeof(RectTransform));
            go.transform.SetParent(_touchPanel.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = Theme.Circle;   // round pad, not a screen-hogging square
            img.color = new Color(1f, 1f, 1f, alpha);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            go.AddComponent<TouchButton>().dir = dir;
            int fontSize = (dir == -1 || dir == 1) ? 64 : (label.Length > 4 ? 20 : 24);
            Theme.Label(go.transform, label, fontSize, new Color(1, 1, 1, 0.9f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return go;
        }

        // ==================== MAIN MENU ====================
        void ShowMenu()
        {
            // Leaving a race: drop the room and the rival ghosts.
            if (_mode == Mode.Versus) { Net.Leave(); ClearGhosts(); _mode = Mode.Curated; }
            Memory.RunEndedCleanly();   // back at the menu = not a rage-quit
            Rumor.Disarm();
            _state = State.Menu;
            Time.timeScale = 1f;
            ApplyTheme(0);               // menu always shows the Castle mood
            Audio.Music("music", 0.3f);
            _hud.gameObject.SetActive(false);
            if (_shardHud != null) _shardHud.SetActive(false);
            if (_touchPanel != null) { _touchPanel.SetActive(false); TouchInput.Clear(); }
            _rig.SetFrame(-1.5f, CamY, NormalCamSize);   // reset in case we left a zoomed-out boss arena

            // Destroy any existing menu panel FIRST — otherwise the previous
            // screen (e.g. the level-select map) leaks and stays on top, since
            // reassigning _menuPanel orphaned it. This was the "map stays" bug.
            if (_menuPanel != null) Destroy(_menuPanel);

            // A lighter wash than the sub-screens: the animated vampire backdrop
            // (built next) should read clearly through it, while the solid-coloured
            // buttons stay legible on top.
            _menuPanel = Overlay(new Color(0.05f, 0.02f, 0.06f, 0.45f), out var root);

            // Animated menu backdrop (blood moon, drifting fog, bats, lightning).
            // Parented under the menu panel, so it's torn down automatically with
            // every screen transition — no leak into gameplay.
            BuildMenuScene(root);

            // Title — blood red with a near-black shadow, sat up near the top so the
            // button stack has room beneath it.
            var titleShadow = Theme.Label(root, Theme.Title, 96, Theme.Ink,
                new Vector2(0.5f, 0.5f), new Vector2(6, 356), new Vector2(1700, 220));
            titleShadow.font = Theme.TitleFont;
            var title = Theme.Label(root, Theme.Title, 96, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 362), new Vector2(1700, 220));
            title.font = Theme.TitleFont;
            StartCoroutine(Pulse(title.transform));

            // THE button. One click into the game — new players go straight to
            // floor 1, returners resume. Pulses like the title so the eye lands
            // on it first; the mode grid below is for players who want to choose.
            var play = Theme.Button(root, PlayNowCaption(), new Color(0.62f, 0.10f, 0.14f), Color.white,
                52, new Vector2(0.5f, 0.5f), new Vector2(0, 168), new Vector2(640, 118), PlayNow);
            StartCoroutine(Pulse(play.transform));

            // Difficulty selector — tap to cycle Casual → Normal → Nightmare.
            MakeDifficultyChip(root, new Vector2(0, 76), new Vector2(400, 48));

            // Four mode buttons, demoted to a compact 2×2 grid under PLAY —
            // same callbacks and behavior, just no longer the first decision a
            // brand-new player is forced to make.
            var dim = new Vector2(390, 64);
            Theme.Button(root, "BLOOD MOON", new Color(0.6f, 0.08f, 0.12f), Color.white, 28,
                new Vector2(0.5f, 0.5f), new Vector2(-205, 0), dim, StartDaily);
            Theme.Button(root, "THE CASTLE", new Color(0.28f, 0.24f, 0.32f), Color.white, 28,
                new Vector2(0.5f, 0.5f), new Vector2(205, 0), dim, ShowLevelSelect);
            Theme.Button(root, "ENDLESS NIGHT", Theme.Trick, Color.white, 28,
                new Vector2(0.5f, 0.5f), new Vector2(-205, -76), dim, StartEndless);
            Theme.Button(root, "MULTIPLAYER", new Color(0.5f, 0.12f, 0.16f), Color.white, 28,
                new Vector2(0.5f, 0.5f), new Vector2(205, -76), dim, ShowVersusLobby);

            // NIGHTLY TITHE: the first menu visit of each UTC day pays out, scaled
            // by the Blood Moon streak — granted BEFORE the shop button reads the
            // balance, so its caption already includes today's payout.
            int tithe = Currency.GrantDailyIfDue();

            // Secondary row — Shop / Wardrobe / Bestiary / Settings / Leaderboard.
            // The SHOP leads and wears gold with a live balance: it's the standing
            // ad for the death economy ("your deaths bought you something").
            var sdim = new Vector2(260, 60);
            Theme.Button(root, $"SHOP  {Currency.Balance}", new Color(0.5f, 0.38f, 0.1f, 0.4f), Theme.Coin, 26,
                new Vector2(0.5f, 0.5f), new Vector2(-520, -262), sdim, ShowShop);
            Theme.Button(root, "WARDROBE", new Color(1, 1, 1, 0.12f), new Color(1, 1, 1, 0.85f), 26,
                new Vector2(0.5f, 0.5f), new Vector2(-260, -262), sdim, ShowWardrobe);
            Theme.Button(root, $"BESTIARY {Codex.KnownCount()}/{Codex.Total}", new Color(1, 1, 1, 0.12f), new Color(1, 1, 1, 0.85f), 24,
                new Vector2(0.5f, 0.5f), new Vector2(0, -262), sdim, ShowCodex);
            Theme.Button(root, "SETTINGS", new Color(1, 1, 1, 0.12f), new Color(1, 1, 1, 0.85f), 26,
                new Vector2(0.5f, 0.5f), new Vector2(260, -262), sdim, ShowSettings);
            Theme.Button(root, "LEADERBOARD", new Color(1, 1, 1, 0.12f), new Color(1, 1, 1, 0.85f), 24,
                new Vector2(0.5f, 0.5f), new Vector2(520, -262), sdim, () => ShowLeaderboard("daily"));

            // The tithe banner — the "come back tomorrow" hook with teeth.
            if (tithe > 0)
            {
                var titheLbl = Theme.Label(root, $"NIGHTLY TITHE: +{tithe} BLOOD SHARDS", 28, Theme.Coin,
                    new Vector2(0.5f, 0.5f), new Vector2(0, -168), new Vector2(1200, 46));
                StartCoroutine(Pulse(titheLbl.transform));
            }

            // Daily streak — the "come back tomorrow" hook (bottom strip).
            if (Meta.Streak > 0 && Meta.StreakAlive)
                Theme.Label(root, $"BLOOD MOON STREAK: {Meta.Streak} DAYS — keep it alive", 28, Theme.Coin,
                    new Vector2(0.5f, 0.5f), new Vector2(0, -338), new Vector2(1200, 46));

            // A pending curse outranks everything: name the challenger.
            if (Curse.Pending != null)
                Theme.Label(root,
                    $"{Curse.Pending.nick} CURSED YOU from floor {Curse.Pending.floor + 1} of {Curse.Pending.mode}. Break it.",
                    26, new Color(1f, 0.35f, 0.4f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -412), new Vector2(1400, 44));

            // The castle remembers: absence / rage-quit / nemesis greeting.
            string greet = Memory.MenuGreeting();
            if (greet != null)
            {
                Theme.Label(root, greet, 24, new Color(1f, 0.55f, 0.55f, 0.8f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -382), new Vector2(1300, 40));
                if (!_greetTracked)
                {
                    _greetTracked = true;
                    Analytics.Track("haunt_greeting", new System.Collections.Generic.Dictionary<string, object>());
                }
            }

            // Funnel: how many sessions actually reach an interactive menu (the
            // gap between session_start and this is load/boot bounce).
            if (!_menuShownTracked)
            {
                _menuShownTracked = true;
                Analytics.Track("menu_shown", new System.Collections.Generic.Dictionary<string, object>
                {
                    { "returning", !Memory.IsFirstSession },
                    { "has_curse", Curse.Pending != null },
                });
            }
        }
        bool _greetTracked;      // one analytics ping per session, not per menu visit
        bool _menuShownTracked;  // same contract for the funnel's menu_shown
        bool _firstInputTracked; // ...and for the first gameplay keypress

        // A compact difficulty chip that cycles Casual → Normal → Nightmare in place,
        // recolouring + relabelling itself. Shown on the menu and (via the same helper)
        // the Settings screen, so the choice is always one tap away.
        void MakeDifficultyChip(Transform root, Vector2 pos, Vector2 size)
        {
            Button btn = null;
            System.Func<string> cap = () => $"DIFFICULTY:  {Diff.Name}";
            System.Func<Color> col = () =>
                Diff.Current == Difficulty.Casual ? new Color(0.16f, 0.40f, 0.22f)
              : Diff.Current == Difficulty.Nightmare ? new Color(0.52f, 0.07f, 0.10f)
              : new Color(0.30f, 0.26f, 0.34f);
            btn = Theme.Button(root, cap(), col(), Color.white, 28,
                new Vector2(0.5f, 0.5f), pos, size, () =>
                {
                    Diff.Current = (Difficulty)(((int)Diff.Current + 1) % 3);
                    if (!Audio.Muted) Audio.Play("click", 0.6f);
                    var img = btn.GetComponent<Image>(); if (img != null) img.color = col();
                    var t = btn.GetComponentInChildren<Text>(); if (t != null) t.text = cap();
                });
        }

        // ==================== TRAP CODEX (BESTIARY) ====================
        // A persistent book of every trap. Each page is revealed the first time the
        // trap gets you (or you trigger it), teaching how to read/beat it next time.
        void ShowCodex()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.96f), out var root);

            var title = Theme.Label(root, "VAMPIRE'S BESTIARY", 72, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 452), new Vector2(1600, 110));
            if (Theme.TitleFont != null) title.font = Theme.TitleFont;
            Theme.Label(root, $"{Codex.KnownCount()} / {Codex.Total} catalogued — die to a new trap to reveal its page",
                26, Theme.Coin, new Vector2(0.5f, 0.5f), new Vector2(0, 388), new Vector2(1600, 44));

            var entries = Codex.Entries;
            const int cols = 5;
            var card = new Vector2(300, 182);
            float stepX = 320f, stepY = 190f, startX = -((cols - 1) * stepX) / 2f, startY = 272f;
            for (int i = 0; i < entries.Length; i++)
                BuildCodexCard(root, entries[i],
                    new Vector2(startX + (i % cols) * stepX, startY - (i / cols) * stepY), card);

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 40,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), ShowMenu);
        }

        void BuildCodexCard(Transform root, TrapType t, Vector2 pos, Vector2 size)
        {
            bool known = Codex.IsKnown(t);
            var c = new Vector2(0.5f, 0.5f);
            // Card background (UI Image — the menu is screen-space, not world sprites).
            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(root, false);
            var bg = cardGo.AddComponent<Image>();
            bg.color = known ? new Color(0.12f, 0.08f, 0.14f, 0.96f) : new Color(0.06f, 0.05f, 0.08f, 0.96f);
            bg.raycastTarget = false;
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = c; rt.pivot = c;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var ct = cardGo.transform;

            Sprite sp = known ? Assets.Sprite(Codex.Art(t)) : null;
            if (sp != null)
            {
                var img = new GameObject("Art", typeof(RectTransform)).AddComponent<Image>();
                img.transform.SetParent(ct, false);
                img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
                img.color = t == TrapType.BatSwoop ? new Color(1f, 0.3f, 0.3f) : Color.white;
                var irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = c; irt.pivot = c;
                irt.anchoredPosition = new Vector2(0, 46); irt.sizeDelta = new Vector2(58, 58);
            }
            else
            {
                Theme.Label(ct, known ? "•" : "?", 56,
                    known ? Theme.Danger : new Color(1, 1, 1, 0.22f), c,
                    new Vector2(0, 46), new Vector2(80, 80)).raycastTarget = false;
            }

            Theme.Label(ct, known ? Codex.Title(t) : "UNDISCOVERED", 22,
                known ? Color.white : new Color(1, 1, 1, 0.5f), c,
                new Vector2(0, 6), new Vector2(size.x - 16, 28)).raycastTarget = false;
            var lore = Theme.Label(ct, known ? Codex.Lore(t) : "die to this trap to reveal its page", 13,
                known ? new Color(1, 1, 1, 0.72f) : new Color(1, 1, 1, 0.32f), c,
                new Vector2(0, -44), new Vector2(size.x - 30, 92));
            lore.horizontalOverflow = HorizontalWrapMode.Wrap;   // keep text INSIDE the card (no bleed across)
            lore.raycastTarget = false;
        }

        // ==================== ANIMATED MENU BACKDROP ====================
        // A looping vampire scene built from existing sprites, layered above the
        // dark wash but behind the title/buttons. All elements are UI children of
        // the menu panel, so destroying the panel (every transition) stops their
        // coroutines automatically — they bail the moment their target is null.
        //
        // TODO: optional licensed video loop — swap/overlay a UnityEngine.Video
        // .VideoPlayer rendering to a RawImage here without touching the layout.
        void BuildMenuScene(Transform root)
        {
            // Blood moon, upper area, with a slow scale pulse (reuses Pulse).
            var moon = new GameObject("Moon", typeof(RectTransform));
            moon.transform.SetParent(root, false);
            var mImg = moon.AddComponent<Image>();
            mImg.sprite = Theme.Moon;
            mImg.color = new Color(0.78f, 0.12f, 0.14f, 0.9f);
            var mrt = mImg.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);
            mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.anchoredPosition = new Vector2(560, 250);
            mrt.sizeDelta = new Vector2(300, 300);
            StartCoroutine(Pulse(moon.transform));

            // A low band of drifting fog along the bottom of the screen.
            var fogSprite = Assets.Sprite("bg_fog");
            for (int i = 0; i < 2; i++)
            {
                var fog = new GameObject("Fog" + i, typeof(RectTransform));
                fog.transform.SetParent(root, false);
                var fImg = fog.AddComponent<Image>();
                fImg.sprite = fogSprite != null ? fogSprite : Theme.Square;
                fImg.color = new Color(0.35f, 0.1f, 0.16f, 0.22f);
                var frt = fImg.rectTransform;
                frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 0f);
                frt.pivot = new Vector2(0.5f, 0.5f);
                frt.sizeDelta = new Vector2(1400, 360);
                StartCoroutine(MenuFog(frt, i * 1400f));
            }

            // A handful of bats gliding across the night sky.
            for (int i = 0; i < 5; i++)
            {
                var bat = new GameObject("Bat" + i, typeof(RectTransform));
                bat.transform.SetParent(root, false);
                var bImg = bat.AddComponent<Image>();
                bImg.sprite = Theme.Bat;
                bImg.color = new Color(0.08f, 0.05f, 0.08f, 0.9f);
                var brt = bImg.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                StartCoroutine(MenuBat(brt, i));
            }

            // Occasional pale lightning flash over the whole backdrop.
            var flash = new GameObject("Lightning", typeof(RectTransform));
            flash.transform.SetParent(root, false);
            var lImg = flash.AddComponent<Image>();
            lImg.color = new Color(0.8f, 0.78f, 0.85f, 0f);
            lImg.raycastTarget = false;
            var lrt = lImg.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            StartCoroutine(MenuLightning(lImg));
        }

        // Fog band: scroll left, wrapping by its own width so two copies tile.
        IEnumerator MenuFog(RectTransform rt, float startX)
        {
            float x = startX;
            while (rt != null)
            {
                x -= 14f * Time.unscaledDeltaTime;
                if (x <= -1400f) x += 2800f;
                rt.anchoredPosition = new Vector2(x, 120f);
                yield return null;
            }
        }

        // One bat: drift across the screen on a gentle sine bob, then respawn off
        // the opposite edge at a new height/speed. Index just staggers the start.
        IEnumerator MenuBat(RectTransform rt, int index)
        {
            var rng = new System.Random(index * 7 + 13);
            float t = (float)rng.NextDouble() * 6f;
            while (rt != null)
            {
                float dir = (index % 2 == 0) ? 1f : -1f;
                float speed = 90f + (float)rng.NextDouble() * 70f;
                float y = 120f + (float)rng.NextDouble() * 320f;
                float size = 34f + (float)rng.NextDouble() * 26f;
                float x = dir > 0 ? -1100f : 1100f;
                rt.sizeDelta = new Vector2(size, size * 0.5f);
                rt.localScale = new Vector3(dir, 1f, 1f);
                while (rt != null && x * dir < 1100f)
                {
                    x += dir * speed * Time.unscaledDeltaTime;
                    t += Time.unscaledDeltaTime;
                    rt.anchoredPosition = new Vector2(x, y + Mathf.Sin(t * 3f) * 22f);
                    yield return null;
                }
            }
        }

        // Lightning: mostly dark, with a brief double-flash at random intervals.
        IEnumerator MenuLightning(Image img)
        {
            var rng = new System.Random(91);
            while (img != null)
            {
                yield return new WaitForSecondsRealtime(4f + (float)rng.NextDouble() * 7f);
                for (int strike = 0; strike < 2 && img != null; strike++)
                {
                    float peak = 0.28f + (float)rng.NextDouble() * 0.12f;
                    for (float a = peak; a > 0f && img != null; a -= Time.unscaledDeltaTime * 1.6f)
                    {
                        var c = img.color; c.a = a; img.color = c;
                        yield return null;
                    }
                    if (img != null) { var c = img.color; c.a = 0f; img.color = c; }
                    yield return new WaitForSecondsRealtime(0.12f);
                }
            }
        }

        // ==================== SETTINGS ====================
        void ShowSettings()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.92f), out var root);

            Theme.Label(root, "SETTINGS", 86, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 440), new Vector2(1400, 120));

            // ---- Rebindable ability keys ----
            Theme.Label(root, "CONTROLS", 40, new Color(1, 1, 1, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 350), new Vector2(1200, 60));
            Theme.Label(root, "click an action, then press the new key  (Esc cancels)", 24, Theme.Coin,
                new Vector2(0.5f, 0.5f), new Vector2(0, 305), new Vector2(1200, 40));
            MakeRebindButton(root, new Vector2(-250, 240), "JUMP", "jump");
            MakeRebindButton(root, new Vector2(250, 240), "SHOOT", "shoot");
            MakeRebindButton(root, new Vector2(-250, 156), "DASH", "dash");
            MakeRebindButton(root, new Vector2(250, 156), "BAT-GLIDE", "fly");
            Theme.Label(root, "move:  A D / ← →        restart:  R        pause:  Esc", 26,
                new Color(1, 1, 1, 0.6f), new Vector2(0.5f, 0.5f), new Vector2(0, 86), new Vector2(1200, 44));

            // ---- Audio volume sliders (0–100%) — independent of the master HUD mute ----
            Theme.Label(root, "AUDIO", 40, new Color(1, 1, 1, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1200, 60));
            MakeVolumeSlider(root, new Vector2(0, -55), "MUSIC",
                () => Audio.MusicVol, v => Audio.MusicVol = v);
            MakeVolumeSlider(root, new Vector2(0, -125), "SFX",
                () => Audio.SfxVol, v => { Audio.SfxVol = v; });
            MakeVolumeSlider(root, new Vector2(0, -195), "VOICE",
                () => Voice.Volume, v => Voice.Volume = v);

            // ---- Gameplay ----
            MakeDifficultyChip(root, new Vector2(0, -252), new Vector2(620, 58));
            Theme.Label(root, Diff.Blurb, 22, new Color(1, 1, 1, 0.6f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -294), new Vector2(1200, 36));
            MakeToggle(root, new Vector2(-290, -348), "REPLAY GHOST", "opt_replay_ghost", 0);
            MakeToggle(root, new Vector2(290, -348), "ON-SCREEN PADS", "opt_touch", 0);
            MakeToggle(root, new Vector2(0, -414), "2.5D DEPTH", "opt_25d", 1, 560f, ApplyDepthMode);

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 44,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), ShowMenu);
        }

        // A rebind button: shows "ACTION:  Key"; click it, then press any key to set
        // the new binding (Esc cancels). Backed by the Controls store.
        void MakeRebindButton(Transform root, Vector2 pos, string label, string action)
        {
            Button btn = null;
            System.Func<string> caption = () => $"{label}:  {Controls.Name(Controls.Get(action))}";
            btn = Theme.Button(root, caption(), new Color(0.24f, 0.17f, 0.28f, 0.95f), Color.white, 28,
                new Vector2(0.5f, 0.5f), pos, new Vector2(430, 74),
                () => StartCoroutine(CaptureKey(btn, label, action, caption)));
        }

        // Listen for the next key press and bind it to `action`. Esc cancels.
        System.Collections.IEnumerator CaptureKey(Button btn, string label, string action,
            System.Func<string> caption)
        {
            var t = btn.GetComponentInChildren<Text>();
            if (t != null) t.text = $"{label}:  press a key…";
            yield return null;   // swallow the click frame
            while (true)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) break;          // cancel, keep old binding
                KeyCode picked = KeyCode.None;
                foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (kc == KeyCode.None || (int)kc >= 323 || !Controls.Bindable(kc)) continue; // skip mouse/joystick + OS keys
                    if (Input.GetKeyDown(kc)) { picked = kc; break; }
                }
                if (picked != KeyCode.None)
                {
                    Controls.Set(action, picked);
                    if (!Audio.Muted) Audio.Play("click", 0.6f);
                    break;
                }
                yield return null;
            }
            if (t != null) t.text = caption();
        }

        // A labelled 0–100% volume slider backed by getter/setter delegates. Built
        // from a UnityEngine.UI.Slider so it can be dragged or clicked anywhere on
        // the track. Layout per row: [NAME]   [====track====]   [ 70% ].
        void MakeVolumeSlider(Transform root, Vector2 pos, string name,
            System.Func<float> get, System.Action<float> set)
        {
            var c = new Vector2(0.5f, 0.5f);
            Theme.Label(root, name, 30, Color.white, c,
                pos + new Vector2(-360, 0), new Vector2(220, 50), TextAnchor.MiddleRight);

            // Track container (the Slider component lives here).
            var sgo = new GameObject("Slider_" + name, typeof(RectTransform));
            sgo.transform.SetParent(root, false);
            var srt = sgo.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = c; srt.pivot = c;
            srt.anchoredPosition = pos; srt.sizeDelta = new Vector2(440, 38);
            var slider = sgo.AddComponent<Slider>();

            // Background bar.
            var bgImg = new GameObject("BG", typeof(RectTransform)).AddComponent<Image>();
            bgImg.transform.SetParent(sgo.transform, false);
            bgImg.color = new Color(0.16f, 0.14f, 0.2f, 0.95f);
            StretchBand(bgImg.rectTransform, 0.32f);

            // Fill (red, grows with value).
            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.transform.SetParent(sgo.transform, false);
            StretchBand(fillArea, 0.32f);
            var fillImg = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fillImg.transform.SetParent(fillArea, false);
            fillImg.color = Theme.Player;
            var fr = fillImg.rectTransform;
            fr.anchorMin = new Vector2(0, 0); fr.anchorMax = new Vector2(1, 1);
            fr.offsetMin = fr.offsetMax = Vector2.zero;

            // Handle.
            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.transform.SetParent(sgo.transform, false);
            handleArea.anchorMin = new Vector2(0, 0); handleArea.anchorMax = new Vector2(1, 1);
            handleArea.offsetMin = new Vector2(14, 0); handleArea.offsetMax = new Vector2(-14, 0);
            var handleImg = new GameObject("Handle", typeof(RectTransform)).AddComponent<Image>();
            handleImg.transform.SetParent(handleArea, false);
            handleImg.color = Theme.Coin;
            var hr = handleImg.rectTransform;
            hr.sizeDelta = new Vector2(26, 0);   // width fixed; Slider stretches it to the track height

            slider.targetGraphic = handleImg;
            slider.fillRect = fr;
            slider.handleRect = hr;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f;
            slider.SetValueWithoutNotify(get());

            var pct = Theme.Label(root, Mathf.RoundToInt(get() * 100f) + "%", 28, Theme.Coin, c,
                pos + new Vector2(300, 0), new Vector2(120, 50), TextAnchor.MiddleLeft);

            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                pct.text = Mathf.RoundToInt(v * 100f) + "%";
            });
        }

        // A simple ON/OFF toggle button backed by a PlayerPrefs int (0/1). The caption
        // shows the live state; clicking flips and persists it.
        void MakeToggle(Transform root, Vector2 pos, string label, string prefKey, int def, float width = 560f,
            System.Action onChanged = null)
        {
            Button btn = null;
            System.Func<string> caption = () =>
                $"{label}:  {(PlayerPrefs.GetInt(prefKey, def) == 1 ? "ON" : "OFF")}";
            // Smaller font + a width that fits the caption, so it never spills out of the box.
            btn = Theme.Button(root, caption(), new Color(0.24f, 0.17f, 0.28f, 0.95f), Color.white, 22,
                new Vector2(0.5f, 0.5f), pos, new Vector2(width, 68), () =>
                {
                    int cur = PlayerPrefs.GetInt(prefKey, def);
                    PlayerPrefs.SetInt(prefKey, cur == 1 ? 0 : 1);
                    PlayerPrefs.Save();
                    if (!Audio.Muted) Audio.Play("click", 0.6f);
                    onChanged?.Invoke();   // live-apply hooks (e.g. 2.5D re-placement)
                    var t = btn.GetComponentInChildren<Text>();
                    if (t != null) t.text = caption();
                });
        }

        // Anchor a child as a horizontal band centred vertically in its parent,
        // occupying the middle `frac` of the height (used for slider bg/fill bars).
        static void StretchBand(RectTransform rt, float frac)
        {
            rt.anchorMin = new Vector2(0f, 0.5f - frac / 2f);
            rt.anchorMax = new Vector2(1f, 0.5f + frac / 2f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Short, darkly-funny premise shown before a new game.
        void ShowStory()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.88f), out var root);

            Theme.Label(root, "BEANIE'S BAD DAY", 80, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 360), new Vector2(1600, 120));

            string[] lines =
            {
                "Beanie only wanted one piece of candy.",
                "But the Candy Kingdom is not what it seems.",
                "Every floor, every door, every sweet little thing…",
                "…wants Beanie dead.",
                "20 levels of pure betrayal stand in the way.",
                "Trust nothing. Especially the cute parts.",
            };
            for (int i = 0; i < lines.Length; i++)
                Theme.Label(root, lines[i], 40, new Color(1f, 0.9f, 0.95f, 0.9f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, 200 - i * 80), new Vector2(1700, 60));

            Theme.Button(root, "BEGIN ›", Theme.Exit, Theme.Ink, 56,
                new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(420, 130),
                () => StartGame(0));
            Theme.Button(root, "‹ back", new Color(1, 1, 1, 0.2f), Color.white, 36,
                new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(260, 90), ShowMenu);
        }

        // A CANDY MAP: levels are sweets along a snaking trail, not a boring grid.
        void ShowLevelSelect()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.7f), out var root);

            Theme.Label(root, "THE CASTLE", 78, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 440), new Vector2(1400, 120)).font = Theme.TitleFont;
            Theme.Label(root, $"{Levels.Count} floors — pick your poison", 36, new Color(0.85f, 0.7f, 0.72f, 0.7f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 360), new Vector2(1300, 60));

            // Adaptive grid: widen to 8 columns past 20 floors and shrink the
            // spacing/medallions so the whole snake fits between the title and the
            // BACK button no matter how many floors there are.
            int cols = Levels.Count <= 20 ? 5 : 8;
            int rows = (Levels.Count + cols - 1) / cols;
            float spX = Mathf.Min(360f, 1640f / cols);
            float topY = 280f, botY = -330f;          // clear of the title and BACK button
            float spY = rows > 1 ? Mathf.Min(195f, (topY - botY) / (rows - 1)) : 0f;
            float startX = -((cols - 1) * spX) / 2f, startY = topY;
            float node = Mathf.Clamp(Mathf.Min(spX, spY <= 0f ? 120f : spY) * 0.62f, 70f, 120f);
            var pos = new Vector2[Levels.Count];
            for (int i = 0; i < Levels.Count; i++)
            {
                int r = i / cols, c = i % cols;
                if (r % 2 == 1) c = cols - 1 - c;     // snake the path
                pos[i] = new Vector2(startX + c * spX, startY - r * spY);
            }

            // dotted blood trail between nodes
            for (int i = 0; i < Levels.Count - 1; i++)
                for (int d = 1; d <= 3; d++)
                {
                    var p = Vector2.Lerp(pos[i], pos[i + 1], d / 4f);
                    var dot = new GameObject("Dot", typeof(RectTransform));
                    dot.transform.SetParent(root, false);
                    var di = dot.AddComponent<Image>();
                    di.color = new Color(0.7f, 0.12f, 0.16f, 0.4f); // faint blood trail
                    var drt = di.rectTransform;
                    drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
                    drt.pivot = new Vector2(0.5f, 0.5f);
                    drt.anchoredPosition = p; drt.sizeDelta = new Vector2(node * 0.16f, node * 0.16f);
                }

            // Each floor is a blood-seal medallion. Floors are LOCKED until you
            // clear the one before — locked nodes are dark and not clickable.
            // Progress is the ONLY key: the old "unlock all" test toggle is gone.
            int unlocked = CastleUnlocked;
            for (int i = 0; i < Levels.Count; i++)
            {
                int lvl = i;
                bool locked = i > unlocked;
                var go = new GameObject("Node" + (i + 1), typeof(RectTransform));
                go.transform.SetParent(root, false);
                var img = go.AddComponent<Image>();
                img.sprite = Theme.Disc;
                img.color = locked ? new Color(0.22f, 0.2f, 0.24f, 0.9f) : Color.white;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos[i]; rt.sizeDelta = new Vector2(node, node);
                if (!locked)
                {
                    var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
                    btn.onClick.AddListener(() => StartGame(lvl));
                }
                // Floor number — bright when unlocked, ghosted when locked (the
                // dark disc already reads as "sealed", and this avoids relying on
                // an emoji glyph the built-in font may not have).
                Theme.Label(go.transform, (i + 1).ToString(), Mathf.RoundToInt(46 * node / 120f),
                    locked ? new Color(1, 1, 1, 0.22f) : Color.white,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(node, node));
            }

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 44,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), ShowMenu);
        }

        void MenuCandy(Transform root, string sprite, Vector2 pos, float size, float rot)
        {
            var sp = Assets.Sprite(sprite);
            if (sp == null) return;
            var go = new GameObject("Candy", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.sprite = sp; img.preserveAspect = true;
            img.color = new Color(1, 1, 1, 0.9f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(size, size);
            rt.localRotation = Quaternion.Euler(0, 0, rot);
            StartCoroutine(Bob(rt, Random.Range(0f, 6f)));
        }

        IEnumerator Bob(RectTransform rt, float phase)
        {
            Vector2 home = rt.anchoredPosition;
            while (rt != null)
            {
                rt.anchoredPosition = home + new Vector2(0, Mathf.Sin(Time.unscaledTime * 1.5f + phase) * 12f);
                yield return null;
            }
        }

        // A spray of red bits flung from the death spot (parented to GameRoot so
        // it survives the level rebuild on respawn). Bloody-candy payoff.
        static readonly Color Blood = Theme.Hex("8E0E18");
        void GoreBurst(Vector3 pos)
        {
            for (int i = 0; i < 26; i++)
            {
                float sz = Random.Range(0.14f, 0.34f);
                var col = Random.value < 0.5f ? Blood : Theme.Danger;
                var g = Theme.Box("Gore", transform, pos, new Vector2(sz, sz), col, 8);
                StartCoroutine(GoreBit(g.transform,
                    new Vector2(Random.Range(-7f, 7f), Random.Range(3f, 10f))));
            }
        }

        // A one-shot blood-splash sprite animation at the death spot. Parented to
        // GameRoot (not the level) so it survives the level rebuild on respawn.
        void BloodSplash(Vector3 pos)
        {
            var frames = Assets.Sheet("blood", 70);
            if (frames == null || frames.Length == 0) return;
            var go = new GameObject("BloodSplash");
            go.transform.SetParent(transform, false);
            go.transform.position = pos + Vector3.down * 0.2f;
            go.transform.localScale = Vector3.one * 1.6f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 9;
            sr.color = Color.white; // sheet is already dark red
            StartCoroutine(BloodSplashAnim(go, sr, frames));
        }

        IEnumerator BloodSplashAnim(GameObject go, SpriteRenderer sr, Sprite[] frames)
        {
            for (int i = 0; i < frames.Length && sr != null; i++)
            {
                sr.sprite = frames[i];
                yield return new WaitForSecondsRealtime(0.04f);
            }
            // Linger as a stain, then fade.
            float e = 0f;
            while (e < 0.6f && sr != null)
            {
                e += Time.unscaledDeltaTime;
                var c = sr.color; c.a = 1f - e / 0.6f; sr.color = c;
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        IEnumerator GoreBit(Transform t, Vector2 vel)
        {
            var sr = t.GetComponent<SpriteRenderer>();
            float life = 0.6f, e = 0f;
            while (e < life && t != null)
            {
                e += Time.deltaTime;
                vel.y -= 20f * Time.deltaTime;
                t.position += (Vector3)(vel * Time.deltaTime);
                if (sr != null) { var c = sr.color; c.a = 1f - e / life; sr.color = c; }
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        // Public camera shake (used by the boss for hits / enrage / defeat).
        public void ShakeCam(float amount, float dur)
        {
            if (_cam != null) StartCoroutine(Juice.Shake(_cam.transform, amount, dur));
        }

        // Cinematic dolly punch-in toward a world point (2.5D only): eases in and
        // back out over `dur` on unscaled time so it plays through slow-mo beats.
        // Used by the boss summon, the boss kill and the player-death punch.
        public void CinematicPunch(Vector2 focus, float amount, float dur)
        {
            if (!Depth25 || _rig == null) return;
            StartCoroutine(PunchRoutine(focus, amount, dur));
        }

        IEnumerator PunchRoutine(Vector2 focus, float amount, float dur)
        {
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(e / dur) * Mathf.PI);   // in… hold… out
                _rig.SetPunch(focus, amount * k);
                yield return null;
            }
            _rig.SetPunch(focus, 0f);
        }

        // Big red full-screen pulse (boss enrage). Reuses the death-flash machinery.
        public void ScreenFlash() => FlashRed();

        // A short centred announcement (boss enrage / phases).
        public void BossToast(string msg) { if (_toast != null) StartCoroutine(FlashToast(msg)); }

        // A brief freeze-frame on impact — makes hits feel weighty (Level-Devil punch).
        IEnumerator HitStop(float dur)
        {
            if (Time.timeScale <= 0f) yield break;     // don't fight an existing pause
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(dur);
            Time.timeScale = _state == State.Paused ? 0f : 1f;
        }

        // Dramatic slow-motion (boss defeat) — savour the kill.
        public void SlowMoBurst(float scale, float dur) => StartCoroutine(SlowMo(scale, dur));
        IEnumerator SlowMo(float scale, float dur)
        {
            if (Time.timeScale <= 0f) yield break;
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(dur);
            Time.timeScale = _state == State.Paused ? 0f : 1f;
        }

        void FlashRed()
        {
            var go = new GameObject("Flash", typeof(RectTransform));
            go.transform.SetParent(Theme.Canvas.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0.2f, 0.3f, 0.45f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            StartCoroutine(FadeFlash(img));
        }

        IEnumerator FadeFlash(Image img)
        {
            float e = 0f; Color c = img.color;
            while (e < 0.3f && img != null)
            {
                e += Time.unscaledDeltaTime;
                var cc = c; cc.a = Mathf.Lerp(c.a, 0f, e / 0.3f); img.color = cc;
                yield return null;
            }
            if (img != null) Destroy(img.gameObject);
        }

        // `hold` = seconds before the fade starts. A brand-new player reading a
        // controls hint for the first time needs more than the veteran default.
        void ShowHint(string msg, float hold = 2.5f)
        {
            var t = Theme.Label(Theme.Canvas.transform, msg, 34, new Color(1, 1, 1, 0.7f),
                new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(1400, 60));
            StartCoroutine(FadeOutLabel(t, hold));
        }

        // A larger one-shot banner near the top of the screen (gothic title + a small
        // subtitle), auto-fading. Used for the Blood Moon "tonight's date" freshness cue.
        void ShowBanner(string title, string sub)
        {
            var t = Theme.Label(Theme.Canvas.transform, title, 54, Theme.Player,
                new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(1500, 80));
            if (Theme.TitleFont != null) t.font = Theme.TitleFont;
            var s = Theme.Label(Theme.Canvas.transform, sub, 28, new Color(1, 1, 1, 0.72f),
                new Vector2(0.5f, 1f), new Vector2(0, -184), new Vector2(1400, 50));
            StartCoroutine(FadeOutLabel(t, 3.2f));
            StartCoroutine(FadeOutLabel(s, 3.2f));
        }

        IEnumerator FadeOutLabel(Text t, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            float e = 0f; Color c = t.color;
            while (e < 1f && t != null)
            {
                e += Time.unscaledDeltaTime;
                var cc = c; cc.a = Mathf.Lerp(c.a, 0f, e); t.color = cc;
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        // Funnel: which mode a session commits to, and via which path. PlayNow
        // flips the source to "play_button" around its routing so the same
        // starters report honestly for both entrances.
        string _modeSelectSource = "menu";
        void TrackModeSelected(string mode, int floor)
        {
            Analytics.Track("mode_selected", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", mode }, { "source", _modeSelectSource }, { "floor", floor },
            });
        }

        // A player with no history: brand-new device, or someone who bounced off
        // the menu before ever committing to a mode. Both get the "straight into
        // floor 1" treatment.
        bool FreshPlayer => Memory.IsFirstSession ||
                            (CastleUnlocked == 0 && PlayerPrefs.GetString("ti_last_mode", "") == "");

        /// <summary>
        /// The one-click entrance. New players drop straight into Castle floor 1
        /// (via StartGame, so the control hint fires); returning players resume
        /// whatever they last played. Deliberately does NOT auto-route to a
        /// pending cursed floor — the curse label + level select own that path,
        /// and the one big button must stay predictable.
        /// </summary>
        void PlayNow()
        {
            _modeSelectSource = "play_button";
            if (FreshPlayer) StartGame(0);
            else switch (PlayerPrefs.GetString("ti_last_mode", "Curated"))
            {
                case "Endless": StartEndless(); break;
                case "Daily":   StartDaily();   break;
                default:        StartGame(Mathf.Min(CastleUnlocked, Levels.Count - 1)); break;
            }
            _modeSelectSource = "menu";
        }

        // What the big button promises — mirrors PlayNow's routing exactly.
        string PlayNowCaption()
        {
            if (FreshPlayer) return "PLAY";
            switch (PlayerPrefs.GetString("ti_last_mode", "Curated"))
            {
                case "Endless": return "CONTINUE — ENDLESS NIGHT";
                case "Daily":   return "CONTINUE — BLOOD MOON";
                default:        return $"CONTINUE — FLOOR {Mathf.Min(CastleUnlocked, Levels.Count - 1) + 1}";
            }
        }

        void StartGame(int levelIndex)
        {
            Audio.Play("click");
            _mode = Mode.Curated;
            TrackModeSelected("Curated", levelIndex);
            BeginRun(levelIndex);
            if (levelIndex == 0)
            {
                string extra = Skins.Current.dash ? $"   •   {Controls.Name(Controls.Dash)} dash"
                             : Skins.Current.airJumps > 0 ? "   •   double-jump" : "";
                ShowHint(_isMobile
                    ? "‹ › move   •   JUMP   •   trust nothing"
                    : $"← → / A D move   •   {Controls.Name(Controls.Jump)} jump   •   hold {Controls.Name(Controls.Fly)} to glide" + extra + "   •   R restart   •   trust nothing",
                    Memory.IsFirstSession ? 6f : 2.5f);   // first-timers get time to actually read it
            }
        }

        void StartDaily()
        {
            Audio.Play("click");
            _mode = Mode.Daily;
            TrackModeSelected("Daily", 0);
            Meta.RecordDailyPlay();                 // advance the daily streak + feed badges
            if (Meta.Streak >= 3) Badges.Award("streak3");
            if (Meta.Streak >= 7) Badges.Award("streak7");
            Rumor.Arm(DailySeed());                 // tonight's hidden rule (shared worldwide)
            BeginRun(0);
            var now = System.DateTime.UtcNow;
            var left = now.Date.AddDays(1) - now;   // until tonight's run rotates
            ShowBanner($"TONIGHT'S BLOOD MOON — {now:MMM d}",
                       $"rumor: \"{Rumor.CrypticLine}\" • resets in {(int)left.TotalHours}h {left.Minutes}m");
            ShowHint($"BLOOD MOON — {Diff.StartHearts} lives, +1 per floor.  Jump, then hold {Controls.Name(Controls.Fly)}/FLY to glide as a bat.");
        }

        void StartEndless()
        {
            Audio.Play("click");
            _mode = Mode.Endless;
            TrackModeSelected("Endless", 0);
            _endlessSeed = new System.Random().Next(1, 1000000);
            BeginRun(0);
            ShowBanner("ENDLESS NIGHT", $"checkpoint every {Diff.CheckpointEvery} floors • you never truly die • how deep can you go?");
            ShowHint($"Fall and you drop to your last checkpoint with fresh lives.  Jump, then hold {Controls.Name(Controls.Fly)}/FLY to glide.");
        }

        // ==================== VERSUS (multiplayer) ====================
        // A lobby: HOST makes a room code, JOIN enters one. The code seeds the
        // shared race track so everyone runs the identical level and sees each
        // other live.
        void ShowVersusLobby()
        {
            Audio.Play("click");
            TrackModeSelected("Versus", 0);
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(0.05f, 0.01f, 0.03f, 0.9f), out var root);

            Theme.Label(root, "MULTIPLAYER", 90, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 400), new Vector2(1400, 120));
            Theme.Label(root, "race a friend to the coffin — same track, live ghosts",
                34, new Color(0.85f, 0.7f, 0.72f, 0.8f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 315), new Vector2(1500, 60));

            if (!Net.Available)
            {
                Theme.Label(root, "Multiplayer needs the Photon PUN 2 package imported.\nImport it in Unity, then this screen goes live.",
                    36, new Color(1f, 0.7f, 0.7f, 0.9f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 80), new Vector2(1500, 200));
                Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 44,
                    new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), ShowMenu);
                return;
            }

            // HOST
            Theme.Button(root, "HOST A RACE", new Color(0.6f, 0.08f, 0.12f), Color.white, 48,
                new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(560, 100),
                () => { SetLobbyStatus("Creating room…"); Net.Host(StartVersus, LobbyError); });

            // JOIN: a code box + button
            var input = MakeInput(root, new Vector2(-110, 20), new Vector2(360, 96), "CODE");
            Theme.Button(root, "JOIN", Theme.Trick, Color.white, 44,
                new Vector2(0.5f, 0.5f), new Vector2(190, 20), new Vector2(300, 96),
                () => { SetLobbyStatus("Joining…"); Net.Join(input.text, StartVersus, LobbyError); });

            _lobbyStatus = Theme.Label(root, "", 32, Theme.Coin,
                new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(1500, 60));

            Theme.Label(root, "Share the 4-letter code with whoever you want to race.\nWorks across phones, laptops — anyone with the link.",
                26, new Color(1, 1, 1, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -210), new Vector2(1500, 120));

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 44,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100),
                () => { Net.Leave(); ShowMenu(); });
        }

        void SetLobbyStatus(string s) { if (_lobbyStatus != null) _lobbyStatus.text = s; }
        void LobbyError(string s) { SetLobbyStatus(s); }

        // A minimal uppercase code input field, built from code like everything else.
        InputField MakeInput(Transform parent, Vector2 pos, Vector2 size, string placeholder)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0.12f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;

            var ph = Theme.Label(go.transform, placeholder, 44, new Color(1, 1, 1, 0.35f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var txt = Theme.Label(go.transform, "", 44, Color.white,
                new Vector2(0.5f, 0.5f), Vector2.zero, size);

            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = txt;
            input.placeholder = ph;
            input.characterLimit = 4;
            input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            return input;
        }

        // Called once we're in a room — kick off the shared race.
        void StartVersus()
        {
            HookNet();
            ClearGhosts();
            _raceOver = false;
            _mode = Mode.Versus;
            _endlessSeed = Net.Seed;
            _versusRound = 0; _versusWins = 0; _versusLosses = 0;   // fresh match
            _netSendTimer = 0f;          // broadcast our position on the very next frame
            BeginRun(0);
            ShowBanner($"ROOM {Net.RoomCode}", "race the coffin every round • first to the most wins • it never stops");
            ShowHint($"Race to the coffin — winner takes the round, then a NEW track loads. Jump, hold {Controls.Name(Controls.Fly)} to glide.");
        }

        // Match score across rounds (continuous multiplayer).
        int _versusRound, _versusWins, _versusLosses;

        // Start the next race in the SAME room: a new deterministic track (seed +
        // round), ghosts cleared, room kept open. Both clients advance one round per
        // race, so they stay on the same layout.
        void NextVersusRound()
        {
            if (_mode != Mode.Versus) return;
            _versusRound++;
            _raceOver = false;
            ClearGhosts();
            _netSendTimer = 0f;
            BeginRun(0);
            ShowHint($"ROUND {_versusRound + 1}  •  you {_versusWins} – {_versusLosses} rival.  Race!");
        }

        void HookNet()
        {
            if (_netHooked) return;
            _netHooked = true;
            Net.OnState += OnRemoteState;
            Net.OnLeft  += OnRemoteLeft;
            Net.OnWin   += OnRemoteWin;
        }

        // A remote player's position arrived — make/update their ghost.
        void OnRemoteState(int actor, Vector3 pos, bool faceLeft)
        {
            if (_mode != Mode.Versus) return;
            if (!_ghosts.TryGetValue(actor, out var g) || g == null)
            { g = CreateGhost(actor); _ghosts[actor] = g; }
            g.SetTarget(pos, faceLeft);
        }

        void OnRemoteLeft(int actor)
        {
            if (_ghosts.TryGetValue(actor, out var g) && g != null) Destroy(g.gameObject);
            _ghosts.Remove(actor);
        }

        void OnRemoteWin(int actor)
        {
            if (_mode != Mode.Versus || _raceOver) return;
            _raceOver = true;
            if (_player != null) _player.Freeze();
            VersusResult(false);
        }

        // A translucent ghost vampire. Parented to GameRoot (NOT the level) so it
        // survives the level rebuild that happens on every death/respawn.
        Ghost CreateGhost(int actor)
        {
            var go = new GameObject("Ghost" + actor);
            go.transform.SetParent(transform, false);
            var ghost = go.AddComponent<Ghost>();

            var frames = Assets.Grid("vamp_idle_sheet", 64, 3);
            Sprite sp = (frames != null && frames.Length > 0) ? frames[0] : null;
            Transform vis; float baseScale = 1f;
            if (sp != null)
            {
                var b = new GameObject("GBody");
                b.transform.SetParent(go.transform, false);
                b.transform.localPosition = new Vector3(0f, -0.12f, 0f);
                var sr = b.AddComponent<SpriteRenderer>();
                sr.sprite = sp; sr.sortingOrder = 6;        // clearly visible, above platforms
                sr.color = new Color(0.65f, 0.9f, 1f, 0.85f); // spectral blue, bright
                float h = sp.bounds.size.y; baseScale = h > 0.0001f ? 1.35f / h : 1f;
                b.transform.localScale = new Vector3(baseScale, baseScale, 1f);
                vis = b.transform;
            }
            else
            {
                var b = Theme.Box("GBody", go.transform, Vector2.zero, new Vector2(0.8f, 0.9f),
                    new Color(0.65f, 0.9f, 1f, 0.85f), 6);
                b.transform.localPosition = Vector3.zero;
                vis = b.transform;
            }

            // A floating name tag so you can tell who's who in the race.
            var label = new GameObject("GName");
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            var tm = label.AddComponent<TextMesh>();
            tm.text = Net.NickOf(actor);
            tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.1f; tm.fontSize = 40;
            tm.color = new Color(0.8f, 0.92f, 1f, 0.95f);
            label.GetComponent<MeshRenderer>().sortingOrder = 7;

            ghost.Bind(vis, baseScale);
            return ghost;
        }

        void ClearGhosts()
        {
            foreach (var kv in _ghosts) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _ghosts.Clear();
        }

        // Local player crossed the finish first (or a rival did) — end the race.
        void VersusResult(bool youWon)
        {
            _state = State.Win;
            if (youWon) { Badges.Award("versus_win"); _versusWins++; } else _versusLosses++;
            if (_versusWins >= 3) Badges.Award("versus_streak3");
            Analytics.Track("versus_result", new System.Collections.Generic.Dictionary<string, object>
            {
                { "won", youWon },
                { "total_deaths", _deaths },
                { "round", _versusRound },
                { "wins", _versusWins },
                { "losses", _versusLosses },
            });
            Analytics.Track("run_end", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "final_level_index", _levelIndex },
                { "total_deaths", _deaths },
                { "reason", youWon ? "versus_won" : "versus_lost" },
            });
            Audio.Play(youWon ? "win" : "death", 0.7f);
            var panel = Overlay(new Color(0.05f, 0f, 0.02f, 0.85f), out var root);
            Theme.Label(root, youWon ? "YOU WON THE RACE" : "YOU LOST THE RACE",
                youWon ? 90 : 84, youWon ? Theme.Exit : Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(1600, 150));
            Theme.Label(root, youWon ? "first to the coffin" : "a faster vampire beat you to it",
                44, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1500, 70));
            // Running match score across rounds — the "one more round" hook.
            Theme.Label(root, $"MATCH:  YOU {_versusWins}  –  {_versusLosses} RIVAL",
                40, Theme.Coin, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1400, 56));
            Theme.Button(root, "NEXT ROUND", Theme.Exit, Theme.Ink, 46,
                new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(560, 116),
                () => { Destroy(panel); NextVersusRound(); });
            Theme.Button(root, "LEAVE RACE", new Color(0.28f, 0.24f, 0.32f), Color.white, 40,
                new Vector2(0.5f, 0.5f), new Vector2(0, -250), new Vector2(460, 100),
                () => { Destroy(panel); LeaveVersus(); });
        }

        void LeaveVersus()
        {
            Net.Leave();
            ClearGhosts();
            if (_levelRoot != null) Destroy(_levelRoot.gameObject);
            ShowMenu();
        }

        // Common setup for any run. A new run starts the death count fresh;
        // restarting/respawning within a run keeps it.
        void BeginRun(int levelIndex)
        {
            // Hard guarantee: whatever state timeScale was left in (a stray
            // pause, an orientation-check race — see the Update() rotate-panel
            // comment) a freshly started run must never inherit frozen time.
            Time.timeScale = 1f;
            if (_menuPanel != null) Destroy(_menuPanel);
            Memory.RunStarted();   // if this flag survives to next boot, they rage-quit
            Curse.ClearBroken();   // counter-brag receipts don't carry across runs
            _levelIndex = levelIndex;
            _level1StartTracked = false;   // one level1_start per RUN, not per respawn
            // Remember what they chose so the menu's PLAY button can resume it
            // next session (Versus needs a lobby, so it never resumes).
            if (_mode != Mode.Versus)
            {
                PlayerPrefs.SetString("ti_last_mode", ModeName);
                PlayerPrefs.Save();
            }
            _hasCheckpoint = false;
            _newBest = false;
            ResetFloorState();
            // Castle deaths are a LIFETIME tally that persists across menu visits
            // and sessions; Endless/Blood Moon deaths are per-run (for the score).
            _deaths = _mode == Mode.Curated ? PlayerPrefs.GetInt("castle_deaths", 0) : 0;
            // Curated and Versus both retry forever (a race death just sends you
            // back to start); Endless/Daily get a difficulty-scaled pool of lives.
            _hearts = (_mode == Mode.Curated || _mode == Mode.Versus) ? -1 : Diff.StartHearts;
            _hud.gameObject.SetActive(true);
            if (_shardHud != null)
            {
                _shardHud.SetActive(true);
                if (_shardText != null) _shardText.text = Currency.Balance.ToString();
            }
            if (_touchPanel != null) _touchPanel.SetActive(TouchControlsOn); // refined per-level by UpdateTouchLayout
            _state = State.Play;
            Analytics.Track("mode_start", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
            });
            // Music plays on the main menu only — silence it for gameplay so it's
            // not the same loop droning through every level. ShowMenu() restarts it.
            Audio.StopMusic();
            BuildLevel();
        }

        // ==================== LEVEL ====================
        // Which level to build, per mode. Generated levels are deterministic per
        // (seed, index), so retrying a level after death is identical.
        Level CurrentLevel()
        {
            switch (_mode)
            {
                case Mode.Daily:   return Levels.Generate(DailySeed() * 31 + _levelIndex * 7919, 2 + _levelIndex);
                case Mode.Endless: return Levels.Generate(_endlessSeed + _levelIndex * 7919, _levelIndex + 2);
                // Versus: a shared race track, identical for everyone in the room.
                // The room code + ROUND number seed it, so each round is a fresh
                // (still deterministic) layout and the match runs continuously. Kept
                // EASY (difficulty 1) so it stays a fun race, not a rage level.
                case Mode.Versus:  return Levels.Generate(Net.Seed + _versusRound * 101, 1);
                default:
                    int bt = BossTierForFloor(_levelIndex);          // Castle floors 10/20/30/40
                    return bt > 0 ? Levels.BossRoom(bt) : Levels.Get(_levelIndex);
            }
        }

        // Curated floors 10/20/30/40 (0-based 9/19/29/39) are boss arenas, tiers 1-4.
        static int BossTierForFloor(int idx)
        {
            switch (idx) { case 9: return 1; case 19: return 2; case 29: return 3; case 39: return 4; }
            return 0;
        }

        static int DailySeed()
        {
            var d = System.DateTime.UtcNow.Date;
            return d.Year * 10000 + d.Month * 100 + d.Day;
        }

        // Bump this to force EVERY player (each browser has its own save) back to
        // Floor 1 on their next load — used for a fresh start across friends.
        // v3: removed the "unlock all floors" test toggle and reset everyone to
        // Castle floor 1 (last-mode cleared so PLAY routes to the Castle, not a
        // resumed Endless/Blood Moon run).
        const int ProgressVersion = 3;
        static void ResetProgressOncePerVersion()
        {
            if (PlayerPrefs.GetInt("progress_version", 0) == ProgressVersion) return;
            PlayerPrefs.SetInt("castle_unlocked", 0);
            PlayerPrefs.SetInt("ti_level", 0);
            PlayerPrefs.DeleteKey("opt_unlock_all");   // the retired test toggle
            PlayerPrefs.DeleteKey("ti_last_mode");     // PLAY starts fresh in the Castle
            PlayerPrefs.SetInt("progress_version", ProgressVersion);
            PlayerPrefs.Save();
        }

        // Highest Castle floor the player has unlocked (0 = only floor 1).
        // Beating a floor unlocks the next; the level-select locks the rest.
        static int CastleUnlocked => PlayerPrefs.GetInt("castle_unlocked", 0);
        static void UnlockCastle(int idx)
        {
            if (idx > CastleUnlocked) { PlayerPrefs.SetInt("castle_unlocked", idx); PlayerPrefs.Save(); }
        }

        void BuildLevel()
        {
            _dying = false;
            ActiveBoss = null;   // the old level root (and any boss in it) is torn down below
            _recT.Clear(); _recP.Clear(); _recTimer = 0f;   // fresh recording for this attempt
            _level = CurrentLevel();
            _camMin = _level.CamMinX; _camMax = _level.CamMaxX;
            _levelRoot = new GameObject("Level").transform;

            // Floor extents — right edge feeds the near-miss narrator, both edges
            // clamp echo graves whose X comes from the server.
            _levelEndX = _levelStartX = _level.Spawn.x;
            foreach (var p in _level.Platforms)
            {
                _levelEndX = Mathf.Max(_levelEndX, p.pos.x + p.size.x / 2f);
                _levelStartX = Mathf.Min(_levelStartX, p.pos.x - p.size.x / 2f);
            }

            ThemeBackdrop();   // pick the backdrop by mode + progress (distinct per mode)

            foreach (var p in _level.Platforms)
                BuildPlatform(p);
            foreach (var d in _level.Decos)
                Theme.Box("Deco", _levelRoot, d.pos, d.size, d.color, 2);
            foreach (var t in _level.Traps)
                BuildTrap(t);
            foreach (var pp in _level.Portals)
                BuildPortals(pp);
            _rumorFloorUsed = false;
            BuildReactiveTraps();   // the "Trust Issues" learned traps from past deaths
            PlaceTorches();         // gothic ambience — flickering wall sconces
            BuildAerialHazards();
            if (_mode == Mode.Daily && _levelIndex == 1 && Rumor.HiddenDoor)
                BuildHiddenDoor();  // tonight's rumor (2): the ghost door of night 2

            SpawnPlayer();
            SpawnFirstSessionPrompts(); // faint in-world key hints, first boot + floor 1 only
            SpawnReplayGhost();      // race your previous attempt
            SpawnDeathEchoes();      // tombstones of real other players who died here
            SpawnCurseGhost();       // the friend who cursed you haunts their floor
            SnapCamera();

            // Boss arena setup (spawns the boss, gives the player a pip buffer +
            // the blaster, plays the boss theme). Normal floors disarm the blaster
            // and silence any lingering boss music.
            if (InBossRoom) SetupBoss(_level.BossTier);
            else
            {
                if (_player != null) _player.canShoot = false;
                // Resume/switch to this theme's track — idempotent if it's already
                // playing, so it also quietly restores after a boss fight ends.
                int mi = Mathf.Clamp(_curTheme, 0, ThemeMusic.Length - 1);
                Audio.MusicOr(ThemeMusic[mi], "music", 0.3f);
            }
            UpdateHud();
            UpdateTouchLayout();   // match the on-screen action cluster to this floor

            // Arm the sun-rise clock for this attempt. Longer levels get more time;
            // Versus and boss arenas are exempt.
            _sunRising = false; _sunWall = null;
            int plats = _level.Platforms.Count;
            _sunThreshold = (_mode == Mode.Versus || InBossRoom || !Diff.SunRise) ? 999f
                          : _mode == Mode.Curated ? 16f + plats * 2.2f
                                                  : 11f + plats * 1.8f;

            _levelStartRealtime = Time.realtimeSinceStartup;
            Analytics.Track("level_start", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
            });
            // Funnel: floor 1 of the campaign is THE make-or-break moment for a
            // new player. Once per run (death respawns rebuild the level but must
            // not refire). Tab-close abandons are derived server-side (a
            // level1_start with no level1_complete) — the FlushBeacon blur/close
            // hooks guarantee this start event ships even if the tab dies.
            if (_mode == Mode.Curated && _levelIndex == 0 && !_level1StartTracked)
            {
                _level1StartTracked = true;
                Analytics.Track("level1_start", new System.Collections.Generic.Dictionary<string, object>
                {
                    { "first_session", Memory.IsFirstSession },
                });
            }
        }
        bool _level1StartTracked;

        bool _rumorFloorUsed;    // rumor 1: only the FIRST fake floor of night 3 holds
        bool _rumorMoonSpared;   // rumor 3: this floor had learned spikes that never armed

        // A small gold crown + glow over the player's nemesis trap (the one that has
        // killed them the most). The castle knows who your bully is — and says so.
        void CrownNemesis(Vector2 pos)
        {
            var band = Theme.Box("NemCrown", _levelRoot, pos, new Vector2(0.34f, 0.12f),
                new Color(1f, 0.8f, 0.25f, 0.9f), 4);
            Theme.Box("NemCrownSpike", _levelRoot, pos + new Vector2(-0.10f, 0.10f),
                new Vector2(0.08f, 0.14f), new Color(1f, 0.8f, 0.25f, 0.9f), 4);
            Theme.Box("NemCrownSpike", _levelRoot, pos + new Vector2(0f, 0.13f),
                new Vector2(0.08f, 0.2f), new Color(1f, 0.85f, 0.3f, 0.95f), 4);
            Theme.Box("NemCrownSpike", _levelRoot, pos + new Vector2(0.10f, 0.10f),
                new Vector2(0.08f, 0.14f), new Color(1f, 0.8f, 0.25f, 0.9f), 4);
            var fp = band.AddComponent<FaintPulse>(); fp.min = 0.6f; fp.max = 1f; fp.speed = 4f;
        }

        // TONIGHT'S RUMOR (2): a ghost door hides just behind where night 2 begins.
        // Walking into it is a RealExit — straight to the next night — and proof.
        void BuildHiddenDoor()
        {
            Vector2 pos = _level.Spawn + new Vector2(-2.4f, 0.55f);
            var sp = Assets.Sprite("door");
            GameObject go = sp != null
                ? Theme.SpriteBox("HiddenDoor", _levelRoot, pos, new Vector2(1.5f, 1.9f), sp, 1)
                : Theme.Box("HiddenDoor", _levelRoot, pos, new Vector2(1.2f, 1.8f), Theme.Hex("2A3550"), 1);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.color = new Color(0.65f, 0.75f, 1f, 0.4f);   // moonlit, barely-there
            var fp = go.AddComponent<FaintPulse>(); fp.min = 0.25f; fp.max = 0.5f; fp.speed = 2.5f;
            FitTrigger(go, 0.85f);
            go.AddComponent<RumorZone>();                    // proof first…
            go.AddComponent<Trap>().Init(TrapType.RealExit); // …then it whisks you onward
        }

        // The "Trust Issues" reactive traps: a late-spike sprouts at each spot you
        // lingered on in a past attempt (banked in RecordReactiveTrap). A faint mark
        // makes it learnable on the retry. Always a jump-over (never blocks the floor).
        void BuildReactiveTraps()
        {
            // TONIGHT'S RUMOR (3): the moon protects the marked — every spot the
            // castle learned about you stays quiet tonight. Proof lands at the exit.
            if (_mode == Mode.Daily && Rumor.MoonProtects)
            {
                if (_ghostTrapX.Count > 0) _rumorMoonSpared = true;
                _reactiveAdded = false;
                return;
            }
            if (_reactiveAdded)   // the game just learned a new spot — it laughs at you
            { Audio.Play("troll", 0.5f); _reactiveAdded = false; }
            foreach (float gx in _ghostTrapX)
            {
                BuildTrap(new TrapSpec(TrapType.LateSpike, gx, -2.4f, 1.0f, 1.2f));
                var mk = Theme.Box("LearnedMark", _levelRoot, new Vector2(gx, -2.62f),
                    new Vector2(0.7f, 0.12f), Theme.Danger, 4);
                var c = mk.GetComponent<SpriteRenderer>().color; c.a = 0.32f;
                mk.GetComponent<SpriteRenderer>().color = c;
            }
        }

        // If a friend's curse targets THIS floor of THIS mode, their red ghost is
        // waiting (visual + taunts only, never lethal — the menace is social).
        void SpawnCurseGhost()
        {
            var d = Curse.Pending;
            if (d == null || _mode == Mode.Versus) return;
            if (d.mode != ModeName || d.floor != _levelIndex) return;
            var frames = Assets.Grid("vamp_idle_sheet", 64, 3);
            Sprite sp = (frames != null && frames.Length > 0) ? frames[0] : Theme.Square;
            var go = Theme.SpriteBox("CurseGhost", _levelRoot,
                _level.Spawn + new Vector2(2.2f, 1.1f), new Vector2(1.05f, 1.05f), sp, 3);
            go.GetComponent<SpriteRenderer>().color = new Color(1f, 0.3f, 0.3f, 0.5f);
            if (frames != null && frames.Length > 1) go.AddComponent<LoopAnim>().Init(frames, 6f);
            go.AddComponent<Bobber>();
            go.AddComponent<CurseGhost>().Init(d);
        }

        // Two faint key prompts floating in the world on a brand-new player's very
        // first floor: "← →" over the spawn, the jump key just before the first
        // gap. Parented under the level root so they tear down with everything
        // else; TextMesh styling matches the echo tombstone labels. Floor 1's
        // layout is fixed (plat 5 / gap 2.3 / ...), so offsets from Spawn are safe.
        void SpawnFirstSessionPrompts()
        {
            if (!(Memory.IsFirstSession && _mode == Mode.Curated && _levelIndex == 0)) return;
            MakeWorldPrompt("← →", _level.Spawn + new Vector2(0f, 1.35f));
            MakeWorldPrompt($"{Controls.Name(Controls.Jump)} ↑", _level.Spawn + new Vector2(3.6f, 1.5f));
        }

        void MakeWorldPrompt(string text, Vector2 pos)
        {
            var go = new GameObject("FirstSessionPrompt");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 48; tm.characterSize = 0.045f;
            tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 1f, 1f, 0.45f);   // present, not shouting
            go.GetComponent<MeshRenderer>().sortingOrder = 6;
        }

        // Tombstones of REAL other players who died on this floor, fetched from the
        // analytics backend (session-cached, so death-retries don't refetch; offline
        // or route-not-deployed -> silently nothing). The level-root token guards
        // against the fetch landing after a rebuild/menu exit.
        void SpawnDeathEchoes()
        {
            if (_mode == Mode.Versus) return;   // live races stay clean
            var root = _levelRoot;
            float spawnX = _level.Spawn.x, endX = _levelEndX;
            float minX = _levelStartX, maxX = _levelEndX;
            Echo.Fetch(ModeName, _levelIndex, _mode == Mode.Daily ? DailySeed() : 0, list =>
            {
                if (root == null || root != _levelRoot) return;   // level was rebuilt meanwhile
                Echo.SpawnMarkers(root, list, spawnX, endX, minX, maxX);
            });
        }

        // Wipe per-floor loop state (section checkpoint + learned traps + ghost
        // recording) when a NEW floor begins — NOT on a death-respawn (those keep
        // their progress/learning/ghost).
        void ResetFloorState()
        {
            _linger.Clear(); _ghostTrapX.Clear(); _reactiveAdded = false;
            _lastT = null; _lastP = null; _recT.Clear(); _recP.Clear();
            _bossIntroedTier = -1;   // a fresh floor → the next boss plays its full cutscene
            _floorDeaths = 0;        // per-floor death count (curse duels compare this)
            Currency.ResetFloorPayouts();   // a new floor re-opens the death-shard window
        }
        int _floorDeaths;

        // Gothic ambience: flickering torch sconces along the level with a warm glow.
        void PlaceTorches()
        {
            if (_mode == Mode.Versus) return;
            var frames = Assets.Sheet("torch", 32);
            if (frames == null || frames.Length == 0) return;   // no torch art → skip silently
            for (float x = _level.Spawn.x + 3f; x <= _levelEndX - 1f; x += 6f)
            {
                float y = 1.2f;   // upper-background sconce height (off the play plane)
                var go = Theme.SpriteBox("Torch", _levelRoot, new Vector3(x, y, 0f), new Vector2(1f, 1f), frames[0], 1);
                go.AddComponent<LoopAnim>().Init(frames, 10f);
                var glow = Theme.SpriteBox("TorchGlow", go.transform, new Vector3(x, y, 0f), new Vector2(2.4f, 2.4f), Theme.Moon, 0);
                glow.GetComponent<SpriteRenderer>().color = new Color(1f, 0.55f, 0.2f, 0.2f);
                var fp = glow.AddComponent<FaintPulse>(); fp.min = 0.1f; fp.max = 0.24f; fp.speed = 6f;
            }
        }

        // The replay-ghost (a faint blue echo of your last attempt) is opt-in and
        // lives ONLY in the Castle campaign. Players found it following them around
        // in Blood Moon / Endless, so it's force-disabled there (and always in Versus).
        public static bool ReplayGhostOn => PlayerPrefs.GetInt("opt_replay_ghost", 0) == 1;

        // A faint blue echo of your last attempt, racing alongside you.
        void SpawnReplayGhost()
        {
            if (_lastP == null || _lastP.Length < 2) return;
            if (_mode != Mode.Curated) return;   // Castle only — never in Versus/Daily/Endless
            if (!ReplayGhostOn) return;          // opt-in (default OFF)
            var frames = Assets.Grid("vamp_idle_sheet", 64, 3);
            Sprite sp = (frames != null && frames.Length > 0) ? frames[0] : Theme.Square;
            var go = new GameObject("ReplayGhost");
            go.transform.SetParent(_levelRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp; sr.sortingOrder = 3;                 // behind the live player (5)
            sr.color = new Color(0.7f, 0.78f, 1f, 0.38f);       // faint blue "echo"
            float h = sp.bounds.size.y; float s = h > 0.0001f ? 1.35f / h : 1f;
            go.transform.localScale = new Vector3(s, s, 1f);
            go.transform.position = _lastP[0];
            go.AddComponent<GhostReplay>().Init(_lastT, _lastP);
        }

        // A FEW hanging saw-blades on chains in the upper air — gothic and
        // deliberate, not a wall of spikes. They sit above the jump apex (~y+1.1)
        // so ground play is untouched; they deter the high "fly-over" route and
        // add castle atmosphere. Only in the flight modes (Endless / Blood Moon).
        void BuildAerialHazards()
        {
            if (_mode == Mode.Curated) return;   // The Castle has no flight to punish
            if (_mode == Mode.Versus) return;    // a fair race — no hanging-saw gauntlet
            if (_level.Platforms.Count == 0) return;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in _level.Platforms)
            {
                minX = Mathf.Min(minX, p.pos.x - p.size.x / 2f);
                maxX = Mathf.Max(maxX, p.pos.x + p.size.x / 2f);
            }

            var saw = Assets.Sheet("saw", 38);
            var blade = (saw != null && saw.Length > 0) ? saw[0] : Assets.Sprite("saw");
            const float ceilingY = 4.6f;   // chain anchor, just above view
            const float bladeY = 2.5f;     // blade centre — clears the jump apex
            // Sparse: one blade roughly every 7 units, none near spawn or exit.
            for (float x = minX + 7f; x <= maxX - 6f; x += 7f)
            {
                // chain
                Theme.Box("Chain", _levelRoot, new Vector2(x, (ceilingY + bladeY) / 2f + 0.4f),
                    new Vector2(0.09f, ceilingY - bladeY), Theme.Hex("2A2230"), 2);

                // spinning blade
                var pos = new Vector3(x, bladeY, 0f);
                GameObject go = blade != null
                    ? Theme.SpriteBox("AirSaw", _levelRoot, pos, new Vector2(1.25f, 1.25f), blade, 3)
                    : Theme.Box("AirSaw", _levelRoot, pos, new Vector2(1f, 1f), Theme.Danger, 3);
                if (blade != null) go.AddComponent<Spinner>();
                FitTrigger(go, 0.66f); // matches the visible blade
                var kz = go.AddComponent<KillZone>(); kz.msg = "Caught in the blades."; kz.trapTag = (int)TrapType.Saw;
            }
        }

        void UpdateHud()
        {
            if (_hud == null) return;
            // In a boss arena the centred boss name + HP bar own the top of the screen,
            // so keep the corner HUD MINIMAL (just your shield + ammo) — no FLOOR/DEATHS
            // clutter overlapping the boss title. '*' pips render in the pixel font (♥ did not).
            if (InBossRoom)
            {
                string shield = _bossHp > 0 ? "SHIELD " + new string('*', _bossHp) : "";
                string ammoB = _player != null && _player.ammo > 0 ? "     AMMO " + _player.ammo : "";
                _hud.text = shield + ammoB;
                return;
            }
            string left = _mode == Mode.Endless ? $"FLOOR {_levelIndex + 1}    "
                        : _mode == Mode.Daily ? $"NIGHT {_levelIndex + 1}/{DailyLen}    "
                        : _mode == Mode.Versus ? $"RACE {Net.RoomCode}  ({Net.PlayerCount})    "
                        : $"FLOOR {_levelIndex + 1}  •  {WorldNames[WorldOf(_levelIndex)]}    ";
            string hearts = _hearts >= 0 ? "    LIVES " + Mathf.Max(0, _hearts) : "";
            _hud.text = left + "DEATHS " + _deaths + hearts;
        }

        // Lets the player controller refresh the ammo readout as a clip is spent.
        public void RefreshHud() => UpdateHud();

        // A platform: castle-stone tile (tiled) with a single blood-red lip on top.
        void BuildPlatform(Rect2 p) => BuildStoneFloor("Platform", p.pos, p.size, null);

        // Shared builder for real platforms AND fake floors so they look IDENTICAL
        // (same stone tile, same blood lip). `trapType` non-null tags it as a trap.
        void BuildStoneFloor(string name, Vector2 pos, Vector2 size, TrapType? trapType)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.StoneTile;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            sr.sortingOrder = 1;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            // 2.5D: stacked darker copies behind the face read as extruded stone
            // sides/tops under the perspective camera (same sortingOrder — the
            // rig's orthographic transparency sort tie-breaks by depth). Children
            // of the floor, so a collapsing fake floor takes its depth with it —
            // fakes MUST stay indistinguishable from real platforms.
            if (Depth25)
            {
                float[] shade = { 0.72f, 0.55f, 0.4f };
                for (int i = 0; i < 3; i++)
                {
                    var slice = new GameObject("DepthSlice");
                    slice.transform.SetParent(go.transform, false);
                    slice.transform.localPosition = new Vector3(0f, 0f, 0.25f * (i + 1));
                    var ssr = slice.AddComponent<SpriteRenderer>();
                    ssr.sprite = Theme.StoneTile;
                    ssr.drawMode = SpriteDrawMode.Tiled;
                    ssr.size = size;                      // copy the SIZE, not the scale
                    ssr.sortingOrder = 1;
                    ssr.color = new Color(shade[i], shade[i], shade[i], 1f);
                }
            }
            // Single blood-red lip across the top edge (not tiled into the stone).
            // Parented to the FLOOR (not the level root) so a collapsing fake floor
            // takes its lip down with it — no red line left floating in mid-air.
            // On a huge boss-arena floor a full-width bright red line looked messy, so
            // wide floors get a much subtler, darker, thinner lip.
            bool wide = size.x > 15f;
            Color lipCol = wide ? new Color(Theme.PlatEdge.r * 0.5f, Theme.PlatEdge.g * 0.4f, Theme.PlatEdge.b * 0.4f, 0.5f)
                                : Theme.PlatEdge;
            float lipH = wide ? 0.07f : 0.12f;
            var edge = Theme.Box("Edge", go.transform, pos + new Vector2(0, size.y / 2f - 0.06f),
                new Vector2(size.x, lipH), lipCol, 2);
            edge.transform.localPosition = new Vector3(0, size.y / 2f - 0.06f, 0);
            if (trapType.HasValue) go.AddComponent<Trap>().Init(trapType.Value);
        }

        // A trigger sized to the actual sprite (× scale), not a full grid cell —
        // so kill hitboxes match what you see.
        BoxCollider2D FitTrigger(GameObject go, float scale)
        {
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) col.size = sr.sprite.bounds.size * scale;
            return col;
        }

        void BuildTrap(TrapSpec t)
        {
            // Nemesis crowning: the trap type that's killed you most wears a small
            // gold crown — the castle knows exactly who your bully is.
            if (_mode != Mode.Versus && Memory.Nemesis == (int)t.type)
                CrownNemesis(t.pos + new Vector2(0f, t.size.y / 2f + 0.42f));

            switch (t.type)
            {
                case TrapType.FakeFloor:
                {
                    // TONIGHT'S RUMOR (1): the first fake floor of night 3 holds true.
                    // Built as a REAL platform (identical look either way); standing on
                    // it proves the rumor via the trigger above it.
                    if (_mode == Mode.Daily && _levelIndex == 2 && Rumor.FloorHolds && !_rumorFloorUsed)
                    {
                        _rumorFloorUsed = true;
                        BuildStoneFloor("Platform", t.pos, t.size, null);
                        var proof = new GameObject("RumorProof");
                        proof.transform.SetParent(_levelRoot, false);
                        proof.transform.position = t.pos + new Vector2(0f, t.size.y / 2f + 0.3f);
                        var pc = proof.AddComponent<BoxCollider2D>();
                        pc.isTrigger = true; pc.size = new Vector2(t.size.x, 0.55f);
                        proof.AddComponent<RumorZone>();
                        break;
                    }
                    // Must look IDENTICAL to a real platform (same stone tile + lip).
                    BuildStoneFloor("FakeFloor", t.pos, t.size, TrapType.FakeFloor);
                    break;
                }
                case TrapType.FakeExit:
                {
                    var sp = Assets.Sprite("door");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("FakeExit", _levelRoot, t.pos, new Vector2(1.7f, 2.1f), sp, 2)
                        : Theme.Box("FakeExit", _levelRoot, t.pos, t.size, Theme.Trick, 2);
                    if (sp != null) go.GetComponent<SpriteRenderer>().color = new Color(1f, 0.45f, 0.5f);
                    FitTrigger(go, 0.7f);
                    go.AddComponent<Trap>().Init(TrapType.FakeExit);
                    break;
                }
                case TrapType.RealExit:
                {
                    // A code-built coffin with a glowing gold cross = the one goal.
                    var go = new GameObject("RealExit");
                    go.transform.SetParent(_levelRoot, false);
                    go.transform.position = t.pos;
                    var col = go.AddComponent<BoxCollider2D>();
                    col.isTrigger = true; col.size = new Vector2(1.1f, 1.7f);
                    go.AddComponent<Trap>().Init(TrapType.RealExit);
                    Theme.Box("CoffinBack", _levelRoot, t.pos, new Vector2(1.4f, 2.05f), Theme.Hex("140C08"), 1);
                    Theme.Box("Coffin", _levelRoot, t.pos, new Vector2(1.15f, 1.9f), Theme.Hex("3A2418"), 2);
                    Theme.Box("CrossV", _levelRoot, t.pos + new Vector2(0, 0.1f), new Vector2(0.18f, 0.95f), Theme.Exit, 3);
                    Theme.Box("CrossH", _levelRoot, t.pos + new Vector2(0, 0.45f), new Vector2(0.62f, 0.18f), Theme.Exit, 3);
                    break;
                }
                case TrapType.SpikeStatic:
                {
                    var sp = Assets.Sprite("spike");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Spikes", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("Spikes", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    if (sp != null) go.GetComponent<SpriteRenderer>().color = Theme.Danger; // blood
                    FitTrigger(go, 0.85f); // reliable: roughly the full visible spike
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Impaled."; kz.trapTag = (int)TrapType.SpikeStatic;
                    break;
                }
                case TrapType.GrowSpike:
                {
                    var sp = Assets.Sprite("spike");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("GrowSpike", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("GrowSpike", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    if (sp != null) go.GetComponent<SpriteRenderer>().color = Theme.Danger;
                    FitTrigger(go, 0.85f); // reliable spike hitbox
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Skewered.";
                    go.AddComponent<Trap>().Init(TrapType.GrowSpike);
                    break;
                }
                case TrapType.Checkpoint:
                {
                    var go = new GameObject("Checkpoint");
                    go.transform.SetParent(_levelRoot, false);
                    go.transform.position = t.pos;
                    var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true;
                    col.size = new Vector2(1.2f, 1.6f);
                    Theme.Box("Pole", go.transform, t.pos + new Vector2(0f, 0.1f),
                        new Vector2(0.14f, 1.5f), Theme.Hex("BFE9FF"), 2);
                    Theme.Box("Flag", go.transform, t.pos + new Vector2(0.32f, 0.5f),
                        new Vector2(0.5f, 0.34f), Theme.Exit, 3);
                    go.AddComponent<Trap>().Init(TrapType.Checkpoint);
                    break;
                }
                case TrapType.BreakBlock:
                {
                    // A solid candy wall you must SHOOT (lavender = "breakable").
                    var go = Theme.Box("BreakBlock", _levelRoot, t.pos, t.size, Theme.Trick, 2);
                    Theme.AddSolid(go);
                    go.AddComponent<Breakable>();
                    break;
                }
                case TrapType.Spring:
                {
                    var sp = Assets.Sprite("trampoline");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Spring", _levelRoot, t.pos, new Vector2(t.size.x, 0.7f), sp, 3)
                        : Theme.Box("Spring", _levelRoot, t.pos, t.size, Theme.Coin, 3);
                    FitTrigger(go, 0.8f);
                    go.AddComponent<Trap>().Init(TrapType.Spring);
                    break;
                }
                case TrapType.Saw:
                {
                    // The saw sprite is an 8-frame spin strip; show frame 0 and let
                    // the Trap cycle it (it also slides the saw along its track).
                    var frames = Assets.Sheet("saw", 38);
                    var sp = (frames != null && frames.Length > 0) ? frames[0] : null;
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Saw", _levelRoot, t.pos, new Vector2(1.1f, 1.1f), sp, 3)
                        : Theme.Box("Saw", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    FitTrigger(go, 0.66f); // matches the visible blade
                    // TONIGHT'S RUMOR (0): the saws lie — same spin, same slide,
                    // but no kill. Touching one and living proves the rumor.
                    if (_mode == Mode.Daily && Rumor.SawsLie)
                        go.AddComponent<RumorZone>();
                    else
                    {
                        var kz = go.AddComponent<KillZone>(); kz.msg = "Shredded.";
                    }
                    var trap = go.AddComponent<Trap>();
                    trap.frames = frames;
                    trap.Init(TrapType.Saw);
                    break;
                }
                case TrapType.WarpBack:
                {
                    // A VISIBLE cursed rune that drags you back to the start. Was
                    // an invisible trigger that just teleported you with a red
                    // flash — it read as a buggy "death" that didn't count. Now
                    // it's a clear, intentional trap you can see and choose to dodge.
                    var sp = Assets.Sprite("portal");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("WarpBack", _levelRoot, t.pos, new Vector2(1.3f, 2f), sp, 2)
                        : Theme.Box("WarpBack", _levelRoot, t.pos, new Vector2(1f, 1.8f), Theme.Trick, 2);
                    var wsr = go.GetComponent<SpriteRenderer>();
                    wsr.color = new Color(0.55f, 0.2f, 0.8f, 0.85f);   // necro-purple swirl
                    go.AddComponent<Spinner>().speed = 70f;            // slow ominous swirl
                    FitTrigger(go, 0.55f);
                    go.AddComponent<Trap>().Init(TrapType.WarpBack);
                    break;
                }
                case TrapType.Pendulum:
                {
                    // A ceiling bracket; the Trap hangs a chain + blade from it and
                    // swings the whole thing. Rotating the pivot does the work.
                    var go = Theme.Box("Pendulum", _levelRoot, t.pos, new Vector2(0.45f, 0.25f), Theme.Hex("2A2230"), 2);
                    go.AddComponent<Trap>().Init(TrapType.Pendulum);
                    break;
                }
                case TrapType.FlameJet:
                {
                    var sp = Assets.Sprite("flame");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("FlameJet", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("FlameJet", _levelRoot, t.pos, t.size, Theme.Hex("FF7A1A"), 3);
                    FitTrigger(go, 0.8f);
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Burned by the flame jet.";
                    go.AddComponent<Trap>().Init(TrapType.FlameJet);
                    break;
                }
                case TrapType.HolyWater:
                {
                    var sp = Assets.Sprite("holywater");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("HolyWater", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("HolyWater", _levelRoot, t.pos, t.size, new Color(0.5f, 0.8f, 0.95f, 0.5f), 3);
                    FitTrigger(go, 0.9f);
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Burned by holy water.";
                    go.AddComponent<Trap>().Init(TrapType.HolyWater);
                    break;
                }
                case TrapType.BatSwoop:
                {
                    var frames = Assets.Sheet("bat_fly", 32);   // 128x32 strip = 4 frames
                    var sp = (frames != null && frames.Length > 0) ? frames[0] : Theme.Bat;
                    var go = Theme.SpriteBox("Bat", _levelRoot, t.pos, new Vector2(0.95f, 0.95f), sp, 4);
                    // Blood-red from frame one so swooping bats are unmistakable (no glow disc).
                    go.GetComponent<SpriteRenderer>().color = new Color(1f, 0.22f, 0.22f, 1f);
                    go.AddComponent<BatEnemy>().Init(frames);
                    break;
                }
                default: // LateSpike / Crusher / Surprise / Dart / Faller / Chandelier / Reverse = invisible sensors
                {
                    var go = Theme.Box(t.type.ToString(), _levelRoot, t.pos, t.size,
                        new Color(0, 0, 0, 0f), 0);
                    Theme.AddTrigger(go, Vector2.one);
                    go.AddComponent<Trap>().Init(t.type);
                    AddSensorTell(t);   // only Surprise gets a tell (faint sunbeam)
                    break;
                }
            }
        }

        // The ONLY truly invisible, kill-on-touch trap is Surprise — safe-looking
        // ground that just kills you. Darts/late-spikes are visible hazards and
        // get NO tell. Here we mark the cursed ground with a faint shaft of
        // SUNLIGHT (death to a vampire): a warm beam from above + a glowing patch
        // on the floor, both gently pulsing. Subtle enough to miss on a careless
        // run, readable if you're watching.
        void AddSensorTell(TrapSpec t)
        {
            if (t.type != TrapType.Surprise) return;
            if (t.pos.y > -1.5f) return;   // skip air-placed sensors (e.g. spring spikes)

            var gold  = Theme.Hex("FFE6A0"); // pale daylight gold
            var amber = Theme.Hex("FFB347"); // warmer body of the sun
            float floorY = -2.5f;            // floors sit with top ~ -2.7
            float sunY   = floorY + 1.85f;   // the orb hovers above the cursed ground
            var sunPos   = new Vector2(t.pos.x, sunY);

            // --- slow-spinning ray spokes radiating behind the orb ---
            var rays = new GameObject("SunRays");
            rays.transform.SetParent(_levelRoot, false);
            rays.transform.position = sunPos;
            for (int i = 0; i < 8; i++)
            {
                var ray = Theme.Box("Ray", rays.transform, sunPos, new Vector2(0.13f, 2.6f), gold, 1);
                ray.transform.localRotation = Quaternion.Euler(0, 0, i * 45f);
                ray.GetComponent<SpriteRenderer>().color = new Color(gold.r, gold.g, gold.b, 0.14f);
            }
            rays.AddComponent<Spinner>().speed = 9f;   // lazy, ominous turn

            // --- a soft shaft of daylight spilling DOWN onto the cursed ground ---
            var beam = Theme.Box("SunBeam", _levelRoot,
                new Vector2(t.pos.x, (sunY + floorY) / 2f), new Vector2(0.9f, sunY - floorY), gold, 1);
            beam.GetComponent<SpriteRenderer>().color = new Color(gold.r, gold.g, gold.b, 0.07f);
            var bp = beam.AddComponent<FaintPulse>(); bp.min = 0.05f; bp.max = 0.12f;

            // --- the sun orb itself: a warm glowing disc that gently breathes ---
            var orb = Theme.SpriteBox("Sun", _levelRoot, sunPos, new Vector2(1.5f, 1.5f), Theme.Moon, 2);
            orb.GetComponent<SpriteRenderer>().color = new Color(amber.r, amber.g, amber.b, 0.9f);
            var op = orb.AddComponent<FaintPulse>(); op.min = 0.72f; op.max = 0.96f; op.speed = 1.8f;
            // a hot near-white core for depth
            var core = Theme.SpriteBox("SunCore", orb.transform, sunPos, new Vector2(0.85f, 0.85f), Theme.Moon, 3);
            core.GetComponent<SpriteRenderer>().color = new Color(1f, 0.97f, 0.85f, 0.95f);

            // --- the hot pool of sunlight on the floor: THIS is the kill tell ---
            var patch = Theme.SpriteBox("SunPatch", _levelRoot, new Vector2(t.pos.x, floorY + 0.04f),
                new Vector2(1.5f, 0.55f), Theme.Moon, 3);
            patch.GetComponent<SpriteRenderer>().color = new Color(gold.r, gold.g, gold.b, 0.42f);
            var pp = patch.AddComponent<FaintPulse>(); pp.min = 0.28f; pp.max = 0.52f;
        }

        void BuildPortals(PortalPair pp)
        {
            var a = MakePortal("PortalA", pp.a);
            var b = MakePortal("PortalB", pp.b);
            a.target = b.transform.position;
            b.target = a.transform.position;
        }

        Portal MakePortal(string name, Vector2 pos)
        {
            var go = Theme.Box(name, _levelRoot, pos, new Vector2(1.1f, 2f), Theme.Trick, 2);
            var sr = go.GetComponent<SpriteRenderer>();
            var c = sr.color; c.a = 0.7f; sr.color = c;
            Theme.AddTrigger(go, Vector2.one);
            // swirl mark so it reads as a portal
            Theme.Box("Swirl", go.transform, pos, new Vector2(0.4f, 0.4f), Theme.Coin, 3);
            return go.AddComponent<Portal>();
        }

        void SpawnPlayer()
        {
            var go = new GameObject("Beanie");
            go.transform.SetParent(_levelRoot, false);
            // Respawn at the level start, OR at a deliberately-placed checkpoint if
            // you've reached one. We deliberately do NOT track "wherever you last
            // stood" — in a one-hit game that parks you right next to whatever just
            // killed you, so you'd respawn straight into the same death over and over.
            Vector3 spawnAt = _level.Spawn;
            if (_hasCheckpoint && _checkpoint.x > spawnAt.x) spawnAt = _checkpoint;
            go.transform.position = spawnAt;
            go.tag = "Player";

            go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<BoxCollider2D>();
            // Collider matched to the VISIBLE vampire body — wide enough that you
            // die when you actually touch a hazard (too narrow let you stand right
            // next to spikes unharmed), but not the full padded sprite frame.
            // ROUNDED corners (edgeRadius): a sharp box catches on the seam where
            // two flush platforms meet — the classic "runs into an invisible wall"
            // snag, far worse at phone frame rates where the physics step is
            // coarser. The box is shrunk by the radius so the overall footprint
            // (and therefore hazard fairness) is unchanged.
            const float corner = 0.06f;
            col.size = new Vector2(0.55f - corner * 2f, 0.85f - corner * 2f);
            col.edgeRadius = corner;
            col.offset = new Vector2(0f, -0.02f);

            // Animated vampire (4-direction grid sheets, side-profile row) ->
            // Pink Man -> beanie -> coloured box. The vampire frames are 64px in a
            // grid where rows are directions; VampRow picks the side profile.
            // If the vampire ever "moonwalks" (faces backward while moving), flip
            // VampFaceLeft — that mirrors the base art to match the movement code.
            const int VampRow = 3;          // bottom row = right-facing profile
            const bool VampFaceLeft = false; // set true if the chosen row faces left
            SpriteRenderer bodySr = null;
            Transform vis;
            var vIdle  = Assets.Grid("vamp_idle_sheet", 64, VampRow);
            var vRun   = Assets.Grid("vamp_run_sheet", 64, VampRow);
            var vWalk  = Assets.Grid("vamp_walk_sheet", 64, VampRow);
            var vDeath = Assets.Grid("vamp_death_sheet", 64, VampRow);
            bool haveVamp = vIdle != null && vIdle.Length > 0;

            var pmIdle = Assets.Sheet("pinkman_idle", 32);
            var pmRun = Assets.Sheet("pinkman_run", 32);
            var pmJump = Assets.Sheet("pinkman_jump", 32);
            var beanie = Assets.Sprite("beanie_idle");

            // Equipped cosmetic skin: choose the base sprite set, then tint it.
            var skin = Skins.Current;
            bool wantPink = skin.pinkman && pmIdle != null && pmIdle.Length > 0;
            bool useVamp = haveVamp && !wantPink;
            Sprite firstFrame = useVamp ? vIdle[0]
                : (pmIdle != null && pmIdle.Length > 0) ? pmIdle[0] : beanie;

            if (firstFrame != null)
            {
                var b = new GameObject("Body");
                b.transform.SetParent(go.transform, false);
                // Vampire frames have shadow/padding at the bottom; nudge down so
                // the character's feet sit on the floor, not floating above it.
                b.transform.localPosition = useVamp ? new Vector3(0f, -0.12f, 0f) : Vector3.zero;
                bodySr = b.AddComponent<SpriteRenderer>();
                bodySr.sprite = firstFrame;
                bodySr.color = Skins.Shade(skin);  // cosmetic skin colour (softened so it keeps detail)
                bodySr.sortingOrder = 5;
                float h = firstFrame.bounds.size.y;
                float s = h > 0.0001f ? 1.35f / h : 1f;
                b.transform.localScale = new Vector3((VampFaceLeft ? -s : s), s, 1f);
                vis = b.transform;
            }
            else
            {
                var b = Theme.Box("Body", go.transform, _level.Spawn, new Vector2(0.8f, 0.9f),
                    Theme.Player, 5);
                b.transform.localPosition = Vector3.zero;
                vis = b.transform;
            }

            _player = go.AddComponent<PlayerController>();
            _player.canFly = true;   // bat glide everywhere — the Castle gets it back (meter still gates it)
            // Skin-granted abilities (dash / double-jump / speed / phase).
            _player.moveMul = skin.moveMul;
            _player.jumpMul = skin.jumpMul;
            _player.dashEnabled = skin.dash;
            _player.extraAirJumps = skin.airJumps;
            Shop.AttachTrail(go);   // equipped cosmetic wake (pure Fx, no gameplay)
            _playerVisual = vis;
            if (bodySr != null)
            {
                _player.bodyRenderer = bodySr;
                _player.batSprite = Theme.Bat;
                if (useVamp)
                {
                    _player.idleFrames = vIdle;
                    _player.runFrames = (vRun != null && vRun.Length > 0) ? vRun
                                      : (vWalk != null && vWalk.Length > 0) ? vWalk : vIdle;
                    _player.jumpSprite = _player.runFrames[_player.runFrames.Length / 2];
                    _player.deathFrames = vDeath;
                }
                else if (pmIdle != null && pmIdle.Length > 0)
                {
                    _player.idleFrames = pmIdle;
                    _player.runFrames = pmRun;
                    _player.jumpSprite = (pmJump != null && pmJump.Length > 0) ? pmJump[0] : null;
                }
                else
                {
                    _player.idleSprite = beanie;
                    _player.walkSprite = Assets.Sprite("beanie_walk");
                    _player.jumpSprite = Assets.Sprite("beanie_walk");
                }
            }
        }

        // Loads "<prefix>1".."<prefix>N" as a frame array (skips missing).
        static Sprite[] LoadFrames(string prefix, int n)
        {
            var list = new System.Collections.Generic.List<Sprite>();
            for (int i = 1; i <= n; i++)
            {
                var s = Assets.Sprite(prefix + i);
                if (s != null) list.Add(s);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        // ==================== sun-rise pressure ====================
        // Daylight floods in from behind: a bright wall that creeps right. Catch the
        // player's x and they burn. Resets every life (BuildLevel re-arms the clock).
        void StartSunrise()
        {
            _sunRising = true;
            _sunWallX = _player.transform.position.x - 9f;  // dawn breaks behind you
            _sunWall = Theme.Box("Sunrise", _levelRoot, new Vector2(_sunWallX - 20f, 0f),
                new Vector2(40f, 40f), Theme.Hex("FFE6A0"), 6);
            _sunWall.GetComponent<SpriteRenderer>().color = new Color(1f, 0.95f, 0.7f, 0.1f);
            if (_toast != null) StartCoroutine(FlashToast("The sun is rising — RUN!"));
        }

        void TickSunrise()
        {
            _sunWallX += 3.2f * Time.deltaTime;   // the creep speed (tuned to be escapable)
            if (_sunWall != null)
            {
                _sunWall.transform.position = new Vector3(_sunWallX - 20f, 0f, 0f);
                float d = _player.transform.position.x - _sunWallX;
                float a = Mathf.Clamp01(1f - d / 8f) * 0.6f;
                _sunWall.GetComponent<SpriteRenderer>().color =
                    new Color(1f, 0.95f, 0.7f, Mathf.Max(0.1f, a));
            }
            if (_sunWallX >= _player.transform.position.x)
                Die("Caught in the sunrise. Vampires burn.");
        }

        // ==================== boss arenas ====================
        // Per-boss intro flavour — reinforces that the four fights are distinct.
        static readonly string[] BossTitles = { "", "THE GHOUL", "THE COUNTESS", "THE WARLOCK", "THE VAMPIRE LORD" };
        // Each tag now teaches the boss's SIGNATURE mechanic, not just flavour —
        // the intro card is the one guaranteed read before the fight.
        static readonly string[] BossTags =
        {
            "",
            "a grounded bruiser — bait his charge into the wall",
            "a teleporting trickster — only the real one flinches",
            "an anchored storm — his shield falls when the spell ends",
            "wears every face you've beaten — remember your lessons",
        };

        void SetupBoss(int tier)
        {
            _bossGen++;                     // invalidate any pending pickup respawns
            _gunPickup = null;
            // Start the fight UNARMED: you must dodge to a weapon pickup, grab a clip,
            // blast the boss, then dodge to the next one. canShoot just means "blaster
            // mechanic is live in this arena"; ammo gates the actual firing + the gun.
            if (_player != null) { _player.canShoot = true; _player.ammo = 0; }
            // A small health buffer in boss arenas ONLY, so a single mis-read isn't an
            // instant death. Difficulty-scaled (Nightmare = one-shot). Resets here on
            // every (re)build of the fight.
            _bossHp = Diff.BossPlayerHearts;
            _bossIFrames = 0f;
            if (_player != null && _player.bodyRenderer != null) _player.bodyRenderer.enabled = true;

            // Calm the busy parallax scenery so the duel reads clearly: a dark haze
            // across the arena, behind the platforms/boss (order 0 < platforms at 1)
            // but in front of the world backdrop (order -18). Makes the boss pop.
            float mid = (_camMin + _camMax) / 2f;
            Theme.Box("BossHaze", _levelRoot, new Vector2(mid, 0f),
                new Vector2((_camMax - _camMin) + 40f, 30f), new Color(0.03f, 0.01f, 0.05f, 0.5f), 0);

            float cx = mid + 3f;   // boss sits right of centre
            var sp = Assets.Sprite("boss" + tier);
            GameObject go = sp != null
                ? Theme.SpriteBox("Boss", _levelRoot, new Vector3(cx, -0.4f, 0f), new Vector2(2.6f, 2.6f), sp, 4)
                : Theme.Box("Boss", _levelRoot, new Vector2(cx, -0.4f), new Vector2(2.0f, 2.6f), Theme.Hex("2A0A12"), 4);
            var bsr = go.GetComponent<SpriteRenderer>();
            // The boss art is fully painted per tier now, so show it at TRUE colour
            // (an old wash tinted the detailed sprites pink/red and muddied them).
            if (sp != null) bsr.color = Color.white;
            else
            {
                // fallback silhouette with glowing eyes (only if art is missing)
                Theme.Box("BossEyeL", go.transform, new Vector2(cx - 0.4f, 0.3f), new Vector2(0.3f, 0.3f), Theme.Danger, 5);
                Theme.Box("BossEyeR", go.transform, new Vector2(cx + 0.4f, 0.3f), new Vector2(0.3f, 0.3f), Theme.Danger, 5);
            }
            var boss = go.AddComponent<Boss>();
            boss.Init(tier, _camMin - 4f, _camMax + 4f);
            ActiveBoss = boss;
            Audio.Music("music_boss", 0.45f);

            int ti = Mathf.Clamp(tier, 1, 4);
            if (_bossIntroedTier == tier)
            {
                // A retry of the same fight — skip the cutscene, hand control straight back.
                boss.IntroHold = false;
                ShowBanner(BossTitles[ti], BossTags[ti]);
                SpawnGunPickup();
            }
            else
            {
                // First time facing this boss this run — play the cinematic reveal. The
                // cutscene unfreezes the player and drops the first weapon at its end.
                _bossIntroedTier = tier;
                StartCoroutine(BossIntro(ti, boss, go));
                StartCoroutine(BossIntroWatchdog());
            }
        }

        // Safety net for BossIntro: the full cutscene runs ~3.5s. If anything
        // stalls it (a device quirk, a coroutine that silently dies), the
        // player must never be stuck frozen — walking into a boss floor
        // (which happens automatically every time Castle is resumed on an
        // unbeaten boss) would otherwise look exactly like "the game is
        // broken, my character won't move."
        IEnumerator BossIntroWatchdog()
        {
            yield return new WaitForSecondsRealtime(9f);
            if (_player != null) _player.Unfreeze();
        }

        // A short cinematic before a boss fight: letterbox bars slide in, the boss
        // punches up to full size with a roar + red ring, its name slams in, then
        // control returns and the first weapon drops. Held off on retries (see above).
        IEnumerator BossIntro(int ti, Boss boss, GameObject bossGo)
        {
            if (_player != null) _player.Freeze();
            // Hide the boss entirely until it's summoned into being.
            Vector3 full = bossGo != null ? bossGo.transform.localScale : Vector3.one;
            Vector3 bpos = bossGo != null ? bossGo.transform.position
                                          : new Vector3((_camMin + _camMax) / 2f + 3f, -0.4f, 0f);
            if (bossGo != null) bossGo.transform.localScale = full * 0.01f;

            const float barH = 130f;
            var top = CineBar(true);
            var bot = CineBar(false);
            for (float e = 0f; e < 1f; e += Time.unscaledDeltaTime / 0.3f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e));
                SetBarHeight(top, barH * k); SetBarHeight(bot, barH * k);
                yield return null;
            }

            // ---- SUMMON (Yu-Gi-Oh style): a glowing blood-seal forms and spins faster
            // and faster while energy sparks converge on it. ----
            var seal = Theme.Disc != null ? MakeSummonSprite("SummonSeal", Theme.Disc, bpos, new Color(1f, 0.2f, 0.22f, 0.95f), 3) : null;
            var glow = Theme.Moon != null ? MakeSummonSprite("SummonGlow", Theme.Moon, bpos, new Color(1f, 0.12f, 0.16f, 0.55f), 2) : null;
            // 2.5D: the camera leans in on the summoning seal, releasing as the boss
            // bursts into being (covers the ~1s spin + the 0.35s burst).
            CinematicPunch(bpos, 0.55f, 1.6f);
            float sealB = (seal != null && Theme.Disc != null) ? Theme.Disc.bounds.size.y : 1f;
            float glowB = (glow != null && Theme.Moon != null) ? Theme.Moon.bounds.size.y : 1f;
            Audio.PlayOr("portal", "boss_roar", 0.6f);
            float spin = 0f;
            for (float e = 0f; e < 1f; e += Time.unscaledDeltaTime / 1.0f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e));
                spin += Time.unscaledDeltaTime * (160f + 720f * k);   // accelerating spin
                float sz = Mathf.Lerp(0.3f, 5.0f, k);
                if (seal != null && sealB > 0.0001f)
                {
                    seal.transform.localScale = Vector3.one * (sz / sealB);
                    seal.transform.rotation = Quaternion.Euler(0f, 0f, spin);
                }
                if (glow != null && glowB > 0.0001f)
                {
                    float pulse = 1.15f + 0.12f * Mathf.Sin(Time.unscaledTime * 18f);
                    glow.transform.localScale = Vector3.one * (sz * pulse / glowB);
                }
                if (Random.value < 0.6f)   // sparks converging on the seal
                    Fx.Burst(bpos + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(3f, 6f)),
                             new Color(1f, 0.35f, 0.35f, 1f), 1, 0.5f, 0.13f, 0.25f, 0f);
                yield return null;
            }

            // ---- BURST: blinding flash, shockwave, the boss punches into existence. ----
            var flash = FullFlash(new Color(1f, 0.95f, 0.95f, 0.96f));
            Audio.PlayOr("boss_roar", "death", 0.95f);
            ShakeCam(0.6f, 0.5f);
            Fx.Burst(bpos, new Color(1f, 0.3f, 0.3f, 1f), 26, 9f, 0.2f, 0.6f, 6f);
            Fx.Ring(bpos, new Color(1f, 0.9f, 0.85f, 0.9f), 7f, 0.6f);
            for (float e = 0f; e < 1f; e += Time.unscaledDeltaTime / 0.35f)
            {
                float k = Mathf.Clamp01(e);
                if (bossGo != null) bossGo.transform.localScale = Vector3.Lerp(full * 0.01f, full * 1.15f, k);
                if (flash != null) { var c = flash.color; c.a = Mathf.Lerp(0.96f, 0f, k); flash.color = c; }
                yield return null;
            }
            if (bossGo != null) bossGo.transform.localScale = full;
            if (flash != null) Destroy(flash.gameObject);
            if (seal != null) Destroy(seal);
            if (glow != null) Destroy(glow);

            // Name slam + tagline.
            var nameT = Theme.Label(Theme.Canvas.transform, BossTitles[ti], 104, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 44), new Vector2(1700, 160));
            if (Theme.TitleFont != null) nameT.font = Theme.TitleFont;
            var tagT = Theme.Label(Theme.Canvas.transform, BossTags[ti], 32, new Color(1, 1, 1, 0.82f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(1500, 70));
            yield return new WaitForSecondsRealtime(1.5f);
            if (nameT != null) Destroy(nameT.gameObject);
            if (tagT != null) Destroy(tagT.gameObject);

            // Retract the bars.
            for (float e = 0f; e < 1f; e += Time.unscaledDeltaTime / 0.3f)
            {
                float k = 1f - Mathf.Clamp01(e);
                SetBarHeight(top, barH * k); SetBarHeight(bot, barH * k);
                yield return null;
            }
            if (top != null) Destroy(top.gameObject);
            if (bot != null) Destroy(bot.gameObject);

            // Fight on: release the boss, hand control back, drop the first weapon.
            if (boss != null) boss.IntroHold = false;
            if (_player != null) _player.Unfreeze();
            SpawnGunPickup();
        }

        // A world-space sprite used by the summon cutscene (seal / glow).
        GameObject MakeSummonSprite(string name, Sprite sp, Vector3 pos, Color col, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp; sr.color = col; sr.sortingOrder = order;
            return go;
        }

        // A full-screen UI flash (fades out over the burst). Returns the Image.
        Image FullFlash(Color col)
        {
            var go = new GameObject("Flash", typeof(RectTransform));
            go.transform.SetParent(Theme.Canvas.transform, false);
            var img = go.AddComponent<Image>();
            img.color = col; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        // A full-width cinematic letterbox bar pinned to the top or bottom edge.
        Image CineBar(bool top)
        {
            var go = new GameObject(top ? "CineTop" : "CineBot", typeof(RectTransform));
            go.transform.SetParent(Theme.Canvas.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.94f); img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, 0f);
            return img;
        }
        void SetBarHeight(Image bar, float h)
        {
            if (bar != null) bar.rectTransform.sizeDelta = new Vector2(0f, h);
        }

        // Drop a weapon pickup somewhere on the LEFT side of the arena (away from the
        // boss, which sits right), low to the ground so the player must move to grab it.
        void SpawnGunPickup()
        {
            if (!InBossRoom) return;
            float mid = (_camMin + _camMax) / 2f;
            float bossX = mid + 3f;
            float px = _player != null ? _player.transform.position.x : _camMin;
            // Pick a spot you must TRAVEL to: well away from where you stand, and clear
            // of the boss — so the loop is genuinely dodge → run → grab → shoot.
            float x = px; int tries = 0;
            do { x = Random.Range(_camMin + 2.5f, _camMax - 2.5f); tries++; }
            while (tries < 16 && (Mathf.Abs(x - px) < 5.5f || Mathf.Abs(x - bossX) < 3f));
            var pos = new Vector3(x, -2.0f, 0f);

            var go = new GameObject("GunPickup");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = pos;
            // A bigger, clearer stake-launcher (grip + dark body + barrel + a pulsing
            // red muzzle) so it reads at a glance, plus a floating "WEAPON" label.
            var grip = Theme.Box("PuGrip", go.transform, pos, new Vector2(0.24f, 0.34f), Theme.Hex("2A2530"), 5);
            grip.transform.localPosition = new Vector3(-0.24f, -0.22f, 0f);
            Theme.Box("PuBody", go.transform, pos, new Vector2(1.02f, 0.38f), Theme.Hex("3A3440"), 5);
            var barrel = Theme.Box("PuBarrel", go.transform, pos, new Vector2(0.72f, 0.2f), Theme.Hex("7A7480"), 5);
            barrel.transform.localPosition = new Vector3(0.42f, 0.03f, 0f);
            var tip = Theme.Box("PuTip", go.transform, pos, new Vector2(0.2f, 0.3f), Theme.Danger, 6);
            tip.transform.localPosition = new Vector3(0.8f, 0.03f, 0f);
            var tp = tip.AddComponent<FaintPulse>(); tp.min = 0.5f; tp.max = 1f; tp.speed = 8f;
            var mark = new GameObject("PuMark"); mark.transform.SetParent(go.transform, false);
            mark.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            var tm = mark.AddComponent<TextMesh>();
            tm.text = "WEAPON"; tm.fontSize = 40; tm.characterSize = 0.09f; tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center; tm.color = Theme.Coin;
            mark.GetComponent<MeshRenderer>().sortingOrder = 7;
            var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true; col.size = new Vector2(2.0f, 1.9f);
            go.AddComponent<Bobber>();                 // gentle float so it reads as "grab me"
            go.AddComponent<GunPickup>().Init(BossClip);
            _gunPickup = go;
        }

        // The held weapon was collected — no pickup in the arena until the clip is spent.
        // Refresh the touch layout so the phone SHOOT button appears now that you're armed.
        public void OnGunCollected() { _gunPickup = null; UpdateHud(); UpdateTouchLayout(); }

        // The clip ran dry — after a short dodge gap, drop a fresh weapon elsewhere.
        public void OnGunEmpty()
        {
            UpdateTouchLayout();   // hide the phone SHOOT button — you're empty again
            if (!InBossRoom || _gunPickup != null) return;
            StartCoroutine(RespawnGunAfter(1.4f, _bossGen));
        }

        IEnumerator RespawnGunAfter(float delay, int gen)
        {
            yield return new WaitForSeconds(delay);
            // Bail if the fight ended / rebuilt / the player already grabbed one.
            if (gen != _bossGen || !InBossRoom || _state != State.Play || _gunPickup != null) yield break;
            SpawnGunPickup();
        }

        // A boss hit (contact, bolt, dash, or spike). Outside boss arenas the game is
        // one-shot; INSIDE one, the player has a small buffer (3 pips) so a single slip
        // isn't instant death. After a chip hit there's a brief mercy window so one
        // volley can't eat every pip in consecutive frames.
        public void HitPlayer(string cause)
        {
            if (_state != State.Play || _dying || !InBossRoom) return;
            if (_bossIFrames > 0f) return;            // still in the mercy window
            if (_bossHp > 1)
            {
                _bossHp--;
                _bossIFrames = 1.1f;
                ScreenFlash();
                ShakeCam(0.35f, 0.25f);
                Audio.PlayOr("boss_hit", "death", 0.6f);
                UpdateHud();
                return;
            }
            Die(cause);                                // last pip spent — this one kills
        }

        // The boss is dead: open the coffin so the player can leave, hush the theme.
        public void BossDefeated()
        {
            // Savour the kill in 2.5D: lean in on the shatter during the slow-mo.
            if (ActiveBoss != null)
                CinematicPunch(ActiveBoss.transform.position, 0.35f, 1.1f);
            ActiveBoss = null;                        // bullets in flight stop probing it
            _bossGen++;                               // stop any pending pickup respawn
            if (_gunPickup != null) { Destroy(_gunPickup); _gunPickup = null; }
            if (_player != null) { _player.canShoot = false; _player.ammo = 0; }
            SlowMoBurst(0.35f, 0.7f);            // savour the kill
            Audio.StopMusic();
            int tier = _level != null ? _level.BossTier : 0;
            if (tier >= 1) Badges.Award("boss" + Mathf.Clamp(tier, 1, 4));
            if (_toast != null) StartCoroutine(FlashToast("THE LORD FALLS  -  flee RIGHT to the coffin"));
            SpawnExitCoffin(new Vector2(_camMax + 4f, -2f));
        }

        void SpawnExitCoffin(Vector2 pos)
        {
            var go = new GameObject("RealExit");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = pos;
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true; col.size = new Vector2(1.1f, 1.7f);
            go.AddComponent<Trap>().Init(TrapType.RealExit);
            Theme.Box("CoffinBack", _levelRoot, pos, new Vector2(1.4f, 2.05f), Theme.Hex("140C08"), 1);
            Theme.Box("Coffin", _levelRoot, pos, new Vector2(1.15f, 1.9f), Theme.Hex("3A2418"), 2);
            Theme.Box("CrossV", _levelRoot, pos + new Vector2(0, 0.1f), new Vector2(0.18f, 0.95f), Theme.Exit, 3);
            Theme.Box("CrossH", _levelRoot, pos + new Vector2(0, 0.45f), new Vector2(0.62f, 0.18f), Theme.Exit, 3);
            // A gold glow pulsing on the coffin so the way out is unmistakable.
            Fx.Ring(pos, new Color(0.88f, 0.7f, 0.25f, 0.8f), 3.2f, 0.7f);
        }

        // ==================== camera & loop ====================
        void SnapCamera()
        {
            if (_player == null) return;
            if (InBossRoom) { PositionBossCam(); return; }
            float x = Mathf.Clamp(_player.transform.position.x, _camMin, _camMax);
            _rig.SetFrame(x, CamY, NormalCamSize);
        }

        // Boss arenas pull the camera WAY back and lock it on the room centre, so the
        // entire battlefield — every telegraph, bolt and spike — is visible at once.
        // The half-height/aspect math holds for the perspective rig too: width scales
        // with height at the gameplay plane exactly like an ortho camera.
        void PositionBossCam()
        {
            const float halfArena = 14.2f;          // walls sit at ±13.2; show a little past them
            const float topY = 5.2f, botY = -3.6f;  // floor to the bolt-rain ceiling
            float sizeForWidth = halfArena / Mathf.Max(0.1f, _cam.aspect);
            float sizeForHeight = (topY - botY) / 2f;
            _rig.SetFrame(0f, (topY + botY) / 2f, Mathf.Max(sizeForWidth, sizeForHeight));
        }

        void LateUpdate()
        {
            if (_state == State.Play && _player != null)
            {
                if (InBossRoom) { PositionBossCam(); return; }   // locked, no follow
                float x = Mathf.Clamp(_player.transform.position.x, _camMin, _camMax);
                _rig.SetFrame(Mathf.Lerp(_rig.FrameX, x, 10f * Time.unscaledDeltaTime),
                              CamY, NormalCamSize);
            }
        }

        void Update()
        {
            // On phones, force landscape with a rotate prompt (desktop is exempt).
            if (_isMobile)
            {
                bool portrait = Screen.height > Screen.width;
                if (_rotatePanel != null && _rotatePanel.activeSelf != portrait)
                {
                    _rotatePanel.SetActive(portrait);
                    if (portrait) { _rotatePanel.transform.SetAsLastSibling(); Time.timeScale = 0f; }
                    // Resume whenever we're not deliberately paused. The old
                    // `_state == State.Play` guard left Time.timeScale stuck at 0
                    // FOREVER if a brief orientation misread (a WebGL canvas
                    // resize/viewport-bar hiccup — common right after opening a
                    // big new UI panel) landed while still on a menu screen: the
                    // very next level you entered inherited timeScale=0, so
                    // gravity and every jump/move calculation (they run in
                    // FixedUpdate, which Unity never calls when timeScale is 0)
                    // silently stopped — the character hangs frozen mid-air and
                    // no button does anything. This hit Castle far more than
                    // Blood Moon/Endless because only Castle's Level Select is an
                    // extra screen transition between the main menu and Play.
                    else if (_state != State.Paused) Time.timeScale = 1f;
                }
            }

            // Keep the phone action cluster honest every frame (change-guarded, so
            // this is free when nothing changed): SHOOT only while a collected gun
            // still has ammo, DASH only with a dash-granting skin, BAT only where
            // flight is allowed. Rebuild-time-only updates missed mid-level changes.
            UpdateTouchLayout();

            if ((_state == State.Play || _state == State.Paused) &&
                Input.GetKeyDown(KeyCode.Escape))   // Esc ONLY — P was hijacking a letter key mid-play
                TogglePause();

            // "Still playing" ping every 15s — powers the time-spent-per-level view
            // and lets the dashboard see sessions that never reach an exit.
            if (_state == State.Play)
            {
                // Funnel: the moment the player actually starts PLAYING (vs. only
                // watching) — the last step of the first-60-seconds funnel.
                if (!_firstInputTracked && Input.anyKeyDown)
                {
                    _firstInputTracked = true;
                    Analytics.Track("first_input", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "ms_since_boot", (int)(Time.realtimeSinceStartup * 1000f) },
                    });
                }
                _heartbeatTimer += Time.unscaledDeltaTime;
                if (_heartbeatTimer >= 15f)
                {
                    _heartbeatTimer = 0f;
                    Memory.Touch();   // "last seen" for the absence-aware greeting
                    Analytics.Track("heartbeat", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "mode", ModeName },
                        { "level_index", _levelIndex },
                    });
                }
            }

            if (_state == State.Play && _player != null && !_dying &&
                _player.transform.position.y < -9f)
                Die("Gravity wins again.");

            // Boss-arena mercy window: count it down and blink the player so the
            // invulnerability is legible. Always restore the sprite when it ends.
            if (_bossIFrames > 0f)
            {
                _bossIFrames -= Time.deltaTime;
                if (_player != null && _player.bodyRenderer != null)
                    _player.bodyRenderer.enabled = _bossIFrames <= 0f || ((int)(Time.unscaledTime * 12f) % 2 == 0);
            }

            // Record the path (~20 Hz) for the ghost-of-your-last-attempt racer.
            if (_state == State.Play && _player != null && !_dying && _mode != Mode.Versus)
            {
                _recTimer += Time.deltaTime;
                if (_recTimer >= 0.05f)
                {
                    _recTimer = 0f;
                    _recT.Add(Time.realtimeSinceStartup - _levelStartRealtime);
                    _recP.Add(_player.transform.position);
                }
            }

            // Reactive-trap "linger" tracking (which floor spots you dawdle on).
            if (_state == State.Play && _player != null && !_dying && !InBossRoom && _player.IsGrounded)
            {
                var pp = _player.transform.position;
                int bucket = Mathf.RoundToInt(pp.x);
                _linger.TryGetValue(bucket, out float lt);
                _linger[bucket] = lt + Time.deltaTime;
            }

            // Sun-rise pressure (skips Versus / boss arenas via the 999s threshold).
            if (_state == State.Play && _player != null && !_dying)
            {
                float elapsed = Time.realtimeSinceStartup - _levelStartRealtime;
                if (!_sunRising && elapsed > _sunThreshold) StartSunrise();
                if (_sunRising) TickSunrise();
            }

            if (_flyBar != null)
            {
                var barObj = _flyBar.transform.parent.gameObject;
                bool show = _state == State.Play && _player != null && _player.canFly;
                if (barObj.activeSelf != show) barObj.SetActive(show);
                if (show)
                    _flyBar.rectTransform.sizeDelta =
                        new Vector2(316f * Mathf.Clamp01(_player.flightMeter), 22f);
            }

            // Broadcast our position to the room ~15x/sec so rivals see our ghost.
            if (_mode == Mode.Versus && _state == State.Play && _player != null && Net.InRoom)
            {
                _netSendTimer -= Time.unscaledDeltaTime;
                if (_netSendTimer <= 0f)
                {
                    _netSendTimer = 1f / 15f;
                    Net.SendState(_player.transform.position, _player.Facing < 0f);
                }
            }
        }

        // ==================== death / respawn ====================
        public void SetCheckpoint(Vector3 pos)
        {
            _checkpoint = pos + Vector3.up * 0.6f;
            _hasCheckpoint = true;
            Analytics.Track("checkpoint", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
            });
            Audio.Play("levelup", 0.4f);
            if (_toast != null) StartCoroutine(FlashToast("Checkpoint!"));
        }

        IEnumerator FlashToast(string msg)
        {
            if (_toast == null) yield break;
            _toast.text = msg;
            yield return new WaitForSecondsRealtime(0.9f);
            if (_toast != null && _toast.text == msg) _toast.text = "";
        }

        public void WarpToStart()
        {
            if (_state != State.Play || _player == null) return;
            _player.transform.position = _level.Spawn;
            Audio.Play("portal", 0.5f);
            // A purple toast — NOT the red death flash — so it reads as "the rune
            // dragged you back", not a death (it doesn't cost a death).
            if (_toast != null) StartCoroutine(FlashToast("The rune drags you back…"));
        }

        public void Die(string msg = null)
        {
            if (_state != State.Play || _dying) return;
            _dying = true;
            _deaths++;
            _floorDeaths++;
            // If the nemesis trap just scored, its kill-streak taunt rides the roast.
            if (msg != null) msg = Memory.DecorateRoast(msg);
            Vector2 deathPos = PlayerTransform != null ? (Vector2)PlayerTransform.position : Vector2.zero;
            Analytics.Track("death", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
                { "cause", msg ?? "unknown" },
                { "duration_ms", LevelDurationMs },
                { "x", deathPos.x },
                { "y", deathPos.y },
                { "nick", Meta.Nick },
            });
            // Did you die RIGHT before the exit? The narrator twists the knife (and
            // the shard payout sweetens it) — computed here so both can use it.
            bool nearMiss = _player != null && _levelEndX > 0f &&
                            _player.transform.position.x > _levelEndX - 6f;
            // Feed the haunting layer: this death becomes a tombstone other players
            // find on this floor — wearing your equipped gravestone taunt, if any.
            // Versus stays clean — it's a live race.
            if (_mode != Mode.Versus)
                Echo.Report(ModeName, _levelIndex, _mode == Mode.Daily ? DailySeed() : 0,
                            deathPos, (msg ?? "unknown") + Shop.TauntSuffix());
            if (_mode == Mode.Curated)
            {
                PlayerPrefs.SetInt("castle_deaths", _deaths); PlayerPrefs.Save(); // persist lifetime tally
                if (_deaths >= 100) Badges.Award("die100");
            }
            // The castle pays for blood: shards per death (capped per floor-visit so
            // clearing always beats farming) — a failed try still moves the meta on.
            if (_mode != Mode.Versus)
            {
                int shardPay = Currency.DeathPayout(nearMiss);
                if (shardPay > 0)
                {
                    Currency.Earn(shardPay, "death");
                    ShardFloater.Spawn(deathPos, shardPay);
                }
            }
            if (_hearts > 0) _hearts--;     // lose a heart (Endless/Daily); Curated = -1 (infinite)

            // A death SOUNDS like what killed you (spikes squelch, crusher slams,
            // daylight burns…) AND the vampire lets out a dying groan, so there's
            // always loud, layered feedback. The game also ROASTS you, meaner each death.
            string cause = Juice.Categorize(msg);
            Audio.PlayOr(Juice.DeathSfx(cause), "death", 1f);
            Audio.PlayVoice("death_voice", 0.85f);     // the vampire perishes — scaled by the VOICE slider
            FlashRed();
            StartCoroutine(HitStop(0.08f));        // a punchy freeze-frame on impact
            UpdateHud();
            RecordReactiveTrap();                  // the game LEARNS where you felt safe
            string roast = Juice.Roast(cause, _deaths, _levelIndex + 1, nearMiss);
            Voice.Speak(roast);                    // the game mocks you OUT LOUD (WebGL TTS)
            // Every 10th death, dangle the next unlock — the moment a bored player
            // quits is exactly when the shop should whisper. Rides the hint bar so
            // the roast toast (and the instant retry) stay untouched.
            if (_deaths % 10 == 0 && _mode != Mode.Versus)
            {
                var nxt = Shop.NextUnlock();
                if (nxt != null)
                {
                    int need = Shop.UnlockPrice(nxt) - Currency.Balance;
                    ShowHint(need > 0
                        ? $"{need} more shards until {Shop.UnlockName(nxt)} — the Crypt Shop waits"
                        : $"You can afford {Shop.UnlockName(nxt)}. The Crypt Shop waits.", 2.2f);
                }
            }
            StartCoroutine(DieRoutine(roast));
        }

        IEnumerator DieRoutine(string msg)
        {
            // Snapshot this attempt's path so the next try races it as a ghost.
            if (_recP.Count > 1) { _lastT = _recT.ToArray(); _lastP = _recP.ToArray(); }
            // Show the roast but DON'T block on it — it lingers on the canvas while
            // you're already retrying (the toast survives the level rebuild).
            if (_toast != null) { _toast.text = msg; StartCoroutine(ClearToastAfter(msg, 1.2f)); }
            Vector3 deathPos = _player != null ? _player.transform.position : Vector3.zero;
            if (_player != null)
            {
                Fx.Explosion(deathPos, 1.7f);     // a quick blast under the gore
                GoreBurst(deathPos);
                BloodSplash(deathPos);
                Shop.PlayDeathFx(deathPos);       // equipped cosmetic death effect, layered on top
                _player.PlayDeath();
                _player.Freeze();
            }
            StartCoroutine(Juice.Shake(_cam.transform, 0.45f, 0.22f));
            CinematicPunch(deathPos, 0.18f, 0.3f);   // 2.5D: a quick lean toward the kill
            // NEAR-INSTANT retry — the heart of the "just one more try" loop.
            yield return new WaitForSecondsRealtime(0.18f);
            Destroy(_levelRoot.gameObject);
            if (_hearts == 0)
            {
                // Endless never hard-ends on lives — drop back to the last checkpoint
                // segment with a fresh pool and keep going. Blood Moon still ends (it's
                // a fixed nightly challenge), as does any other heart mode.
                if (_mode == Mode.Endless) EndlessCheckpointRespawn();
                else RunOver();
            }
            else BuildLevel();
        }

        // Endless: out of lives → bank best depth, fall back to the start of the
        // current checkpoint segment, refill hearts, and continue. The run only ever
        // ends when the player chooses "END RUN" from the pause menu.
        void EndlessCheckpointRespawn()
        {
            if (_levelIndex > PlayerPrefs.GetInt("best_endless", 0))
            { PlayerPrefs.SetInt("best_endless", _levelIndex); PlayerPrefs.Save(); }
            int seg = (_levelIndex / Diff.CheckpointEvery) * Diff.CheckpointEvery;
            _levelIndex = seg;
            _hearts = Diff.StartHearts;
            _hasCheckpoint = false;
            ResetFloorState();
            Audio.Play("levelup", 0.5f);
            ShowBanner("CHECKPOINT HOLDS",
                       $"back to floor {seg + 1} • {Diff.StartHearts} fresh lives • the night goes on");
            BuildLevel();
        }

        IEnumerator ClearToastAfter(string msg, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (_toast != null && _toast.text == msg) _toast.text = "";
        }

        // The game LEARNS: bank the spot where you lingered longest this attempt, so
        // on the next retry a late-spike sprouts there. Avoidable (jump it) — never
        // makes a floor impossible — but it punishes the "safe spot" you trusted.
        void RecordReactiveTrap()
        {
            if (InBossRoom || _mode == Mode.Versus) { _linger.Clear(); return; }
            if (_mode == Mode.Curated && _levelIndex < 5) { _linger.Clear(); return; } // floors 1-5 stay welcoming
            if (_ghostTrapX.Count < Diff.ReactiveTrapCap)
            {
                float bestX = 0f, bestT = 0.55f; bool found = false;
                foreach (var kv in _linger)
                {
                    float x = kv.Key;
                    if (x <= _level.Spawn.x + 2f || x >= _levelEndX - 2f) continue;
                    if (kv.Value > bestT) { bestT = kv.Value; bestX = x; found = true; }
                }
                // Place the spike JUST AHEAD of the comfort spot — never on the
                // respawn point itself (no instant-death loop), and it betrays the
                // path you were about to take. Fall back to the comfort spot if the
                // spot ahead isn't safely jumpable.
                float trapX = Mathf.Min(bestX + 1.2f, _levelEndX - 2f);
                if (!SafeSpikeSpot(trapX)) trapX = bestX;
                // ONLY commit it where you can actually run up and jump it. If neither
                // spot is safely jumpable, learn nothing this floor — better no trap
                // than one that drops into a gap / lone foothold and walls the floor off.
                if (found && SafeSpikeSpot(trapX) &&
                    !_ghostTrapX.Exists(g => Mathf.Abs(g - trapX) < 1.5f))
                { _ghostTrapX.Add(trapX); _reactiveAdded = true; }
            }
            _linger.Clear();
        }

        // A learned reactive spike is only FAIR if you can run up and jump it. This
        // guards against the spike landing somewhere that walls the floor off:
        //  • requires continuous ground-level floor across the whole run-up/landing
        //    span (so it can't drop into a gap or onto a single-tile foothold), and
        //  • keeps clear of every other hazard (so you never leap one death into
        //    another). If it returns false we simply don't learn that spot.
        bool SafeSpikeSpot(float x)
        {
            const float clear = 1.6f;   // stride of run-up + landing room each side of the ~1-wide spike
            for (float sx = x - clear; sx <= x + clear + 0.001f; sx += 0.4f)
            {
                bool grounded = _level.Platforms.Exists(p =>
                    Mathf.Abs(p.pos.y + 3f) < 0.6f &&                 // a real ground floor (not a high ledge / wall)
                    sx >= p.pos.x - p.size.x / 2f &&
                    sx <= p.pos.x + p.size.x / 2f);
                if (!grounded) return false;                          // a gap (or lone foothold) in the span
            }
            foreach (var t in _level.Traps)
                if (Mathf.Abs(t.pos.x - x) < 2.2f) return false;      // another hazard too close
            return true;
        }

        // Out of hearts in Endless/Daily — end the run and show the result.
        void RunOver()
        {
            Memory.RunEndedCleanly();   // reached a result screen = not a rage-quit
            _state = State.Win;
            if (_mode == Mode.Endless && _levelIndex > PlayerPrefs.GetInt("best_endless", 0))
            { PlayerPrefs.SetInt("best_endless", _levelIndex); PlayerPrefs.Save(); _newBest = true; }
            Analytics.Track("run_end", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "final_level_index", _levelIndex },
                { "total_deaths", _deaths },
                { "reason", "out_of_lives" },
            });
            Audio.Play("death", 0.7f);

            var panel = Overlay(new Color(0.05f, 0f, 0.02f, 0.85f), out var root);
            Theme.Label(root, "YOU PERISHED", 84, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(1400, 150)).font = Theme.TitleFont;
            string reached = _mode == Mode.Endless ? $"reached floor {_levelIndex + 1}"
                                                    : $"fell on night {_levelIndex + 1}/{DailyLen}";
            Theme.Label(root, reached + $"   •   {_deaths} deaths", 50, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1400, 70));

            string lbMode = _mode == Mode.Endless ? "endless" : "daily";
            Leaderboard.Submit(lbMode, _mode == Mode.Endless ? _levelIndex + 1 : _deaths);
            string brag = _mode == Mode.Endless
                ? $"I reached FLOOR {_levelIndex + 1} of Endless Night in Trust Issues \U0001F987 — beat that"
                : $"I fell on night {_levelIndex + 1} of tonight's Blood Moon \U0001F987";
            if (_mode == Mode.Daily && Rumor.Discovered)
                brag += $" — and I proved the rumor: \"{Rumor.CrypticLine}\"";
            ResultFooter(root, panel, brag, lbMode);
        }

        // ==================== level progression / win ====================
        public void ReachExit()
        {
            if (_state != State.Play) return;
            if (_player != null) _player.Freeze();
            // Rumor (3) proof: you crossed a floor whose learned spikes stayed quiet.
            if (_mode == Mode.Daily && _rumorMoonSpared) { _rumorMoonSpared = false; Rumor.Discover(); }
            // Curse duel: match-or-beat the sender's deaths on their floor to return it.
            if (Curse.Pending != null && Curse.Pending.mode == ModeName &&
                Curse.Pending.floor == _levelIndex && _floorDeaths <= Curse.Pending.deaths)
            {
                ShowBanner("CURSE RETURNED",
                    $"{Curse.Pending.nick}'s ghost released — {_floorDeaths} deaths to their {Curse.Pending.deaths}");
                Audio.PlayOr("levelup", "win", 0.7f);
                Curse.MarkBroken();
            }

            Analytics.Track("level_complete", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
                { "duration_ms", LevelDurationMs },
                { "deaths", _deaths },
            });
            // Funnel: floor 1 conversion — the counterpart of level1_start.
            if (_mode == Mode.Curated && _levelIndex == 0)
                Analytics.Track("level1_complete", new System.Collections.Generic.Dictionary<string, object>
                {
                    { "duration_ms", LevelDurationMs },
                    { "deaths", _floorDeaths },
                });

            // Versus: first to the coffin wins. Tell the room and show the result.
            if (_mode == Mode.Versus)
            {
                if (_raceOver) return;
                _raceOver = true;
                Currency.Earn(10, "versus_win");   // winner's purse (losses pay nothing)
                Net.SendWin();
                VersusResult(true);
                return;
            }

            Audio.Play("levelup", 0.7f);
            // Clear payoff: gold burst + ring + a little shake (dopamine close).
            if (_player != null)
            {
                Fx.Burst(_player.transform.position, Theme.Exit, 18, 6f, 0.18f, 0.6f, 6f);
                Fx.Ring(_player.transform.position, new Color(1f, 0.9f, 0.5f, 0.7f), 3f, 0.5f);
                ShakeCam(0.22f, 0.18f);
            }
            if (_hearts >= 0) _hearts = Mathf.Min(Diff.MaxHearts, _hearts + 1); // bank a heart per floor

            // Floor cleared → shards: +10 base, +15 more the FIRST time a Castle
            // floor falls, +5 for the under-5-deaths goal. Paid before the mode
            // branches so every non-Versus mode earns the same way.
            {
                bool firstClear = _mode == Mode.Curated &&
                    (_levelIndex + 1 == Levels.Count ? !Badges.Has("castle_clear")
                                                     : _levelIndex >= CastleUnlocked);
                int shardPay = 10 + (firstClear ? 15 : 0) + (_floorDeaths < 5 ? 5 : 0);
                Currency.Earn(shardPay, firstClear ? "first_clear" : "floor_clear");
                if (_player != null) ShardFloater.Spawn(_player.transform.position, shardPay);
            }

            if (_mode == Mode.Endless)
            {
                _levelIndex++;
                if (_levelIndex > PlayerPrefs.GetInt("best_endless", 0))
                { PlayerPrefs.SetInt("best_endless", _levelIndex); PlayerPrefs.Save(); _newBest = true; }
                if (_levelIndex + 1 >= 10) Badges.Award("endless10");
                if (_levelIndex + 1 >= 20) Badges.Award("endless20");
                // Crossing a checkpoint boundary banks a new safe fall-back floor.
                if (_levelIndex % Diff.CheckpointEvery == 0)
                {
                    Badges.Award("endless" + _levelIndex);   // milestone badge (endless5/10/15/…)
                    ShowBanner($"CHECKPOINT — FLOOR {_levelIndex + 1}",
                               "you'll respawn here if you fall • keep climbing");
                }
                StartCoroutine(NextLevelFlash());
                return;
            }
            if (_mode == Mode.Daily)
            {
                if (_levelIndex + 1 < DailyLen) { _levelIndex++; StartCoroutine(NextLevelFlash()); }
                else
                {
                    string key = "daily_" + DailySeed();
                    if (_deaths < PlayerPrefs.GetInt(key, int.MaxValue))
                    { PlayerPrefs.SetInt(key, _deaths); PlayerPrefs.Save(); _newBest = true; }
                    TrackRunComplete();
                    _state = State.Win; Audio.Play("win", 0.7f); StartCoroutine(WinRoutine());
                }
                return;
            }
            // Curated
            if (_levelIndex + 1 < Levels.Count)
            {
                _levelIndex++;
                PlayerPrefs.SetInt("ti_level", _levelIndex);
                UnlockCastle(_levelIndex);     // beating a floor unlocks the next
                PlayerPrefs.Save();
                StartCoroutine(NextLevelFlash());
            }
            else { UnlockCastle(Levels.Count - 1); Badges.Award("castle_clear"); TrackRunComplete(); _state = State.Win; Audio.Play("win", 0.7f); StartCoroutine(WinRoutine()); }
        }

        // The player finished the whole mode (last night / last castle floor).
        void TrackRunComplete()
        {
            Analytics.Track("run_end", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "final_level_index", _levelIndex },
                { "total_deaths", _deaths },
                { "reason", "completed" },
            });
        }

        IEnumerator NextLevelFlash()
        {
            _state = State.Win; // block input briefly
            if (_toast != null)
                _toast.text = _mode == Mode.Endless ? $"FLOOR {_levelIndex + 1}"
                            : _mode == Mode.Daily ? $"NIGHT {_levelIndex + 1}" : $"LEVEL {_levelIndex + 1}";
            yield return new WaitForSecondsRealtime(0.9f);
            if (_toast != null) _toast.text = "";
            Destroy(_levelRoot.gameObject);
            _hasCheckpoint = false;
            ResetFloorState();          // new floor — clear section checkpoint + learned traps
            _state = State.Play;
            BuildLevel();
            // The standing bargain, restated as each Castle floor opens: clean
            // floors pay extra (the +5 goal chip in ReachExit).
            if (_mode == Mode.Curated)
                ShowHint("BONUS: under 5 deaths on this floor → +5 shards", 2f);
        }

        IEnumerator WinRoutine()
        {
            Memory.RunEndedCleanly();   // reached a result screen = not a rage-quit
            PlayerPrefs.SetInt("ti_level", 0); PlayerPrefs.Save();
            var panel = Overlay(new Color(0, 0, 0, 0.85f), out var root);
            bool daily = _mode == Mode.Daily;
            Theme.Label(root, daily ? "YOU SURVIVED THE NIGHT" : "YOU ESCAPED THE CASTLE", daily ? 80 : 90, Theme.Exit,
                new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(1600, 160));
            Theme.Label(root, $"died {_deaths} time" + (_deaths == 1 ? "" : "s"),
                60, Theme.Player, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1400, 90));

            string lbMode = daily ? "daily" : "castle";
            Leaderboard.Submit(lbMode, _deaths);
            string brag = daily
                ? $"I cleared tonight's Blood Moon in Trust Issues with {_deaths} deaths \U0001F987 — beat that"
                : $"I escaped the castle in Trust Issues — {_deaths} deaths \U0001F987";
            if (daily && Rumor.Discovered)
                brag += $" — and I proved the rumor: \"{Rumor.CrypticLine}\"";
            ResultFooter(root, panel, brag, lbMode);
            yield break;
        }

        // Shared footer for result screens: a real brag line, the newest badge, and
        // SHARE (captures a PNG card) + LEADERBOARD + MENU buttons.
        void ResultFooter(Transform root, GameObject panel, string brag, string lbMode)
        {
            var c = new Vector2(0.5f, 0.5f);
            if (_newBest)
                Theme.Label(root, "NEW BEST!", 38, Theme.Exit,
                    c, new Vector2(0, 34), new Vector2(800, 52)).font = Theme.TitleFont;
            // Display strips the emoji (the pixel font can't draw it); the SHARE text
            // keeps it (renders fine on social).
            string shown = brag.Replace("\U0001F987", "").Replace("  ", " ").Trim();
            Theme.Label(root, "“" + shown + "”", 28, Theme.Coin,
                c, new Vector2(0, -10), new Vector2(1600, 60));
            var nb = Badges.Newest;
            if (nb != null)
                Theme.Label(root, "NEW BADGE UNLOCKED — " + nb.name, 26, Color.white,
                    c, new Vector2(0, -54), new Vector2(1200, 44));
            // The "next unlock" teaser: every result screen names the goal your
            // shards are working toward — the research-backed anti-boredom line.
            var nxt = Shop.NextUnlock();
            if (nxt != null)
            {
                int bal = Currency.Balance, price = Shop.UnlockPrice(nxt);
                Theme.Label(root, bal >= price
                        ? $"{bal} BLOOD SHARDS — {Shop.UnlockName(nxt)} is affordable NOW"
                        : $"{bal} BLOOD SHARDS — {price - bal} more until {Shop.UnlockName(nxt)}",
                    20, Theme.Coin, c, new Vector2(0, -89), new Vector2(1400, 26));
            }
            // Broke a friend's curse this run? The brag carries the receipt.
            if (Curse.LastBroken != null)
                brag += $" — and I broke {Curse.LastBroken.nick}'s curse";
            string bragFinal = brag;
            Theme.Button(root, "SHARE", new Color(0.5f, 0.12f, 0.16f), Color.white, 34,
                c, new Vector2(-350, -150), new Vector2(310, 96),
                () => StartCoroutine(ShareCard.CaptureAndShare("trust-issues.png", bragFinal)));
            // Haunt a friend: a link that spawns YOUR ghost on this floor in THEIR game.
            Theme.Button(root, "CURSE A FRIEND", new Color(0.32f, 0.08f, 0.4f), Color.white, 26,
                c, new Vector2(0, -150), new Vector2(310, 96), () =>
                {
                    var d = new Curse.Data
                    {
                        nick = Meta.Nick, floor = _levelIndex, deaths = _floorDeaths,
                        cause = Memory.LastKillerName, mode = ModeName,
                    };
                    string link = Curse.BuildLink(d);
                    int shared = Curse.ShareLink(link,
                        $"I cursed you in Trust Issues \U0001F987 survive floor {_levelIndex + 1} with {_floorDeaths} deaths or less, or my ghost stays");
                    Analytics.Track("curse_sent", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "floor", _levelIndex }, { "mode", ModeName }, { "share_result", shared },
                    });
                    // Toast what actually happened — the old toast claimed success
                    // even on browsers where every share path failed.
                    BossToast(shared == 2 ? "CURSE LINK READY — SEND IT"
                            : shared == 1 ? "CURSE LINK COPIED — PASTE IT ANYWHERE"
                                          : "COPY BLOCKED — LINK: " + link);
                });
            Theme.Button(root, "LEADERBOARD", new Color(0.28f, 0.24f, 0.32f), Color.white, 30,
                c, new Vector2(350, -150), new Vector2(310, 96), () => { Destroy(panel); ShowLeaderboard(lbMode); });
            Theme.Button(root, "MAIN MENU", new Color(1, 1, 1, 0.22f), Color.white, 34,
                c, new Vector2(0, -270), new Vector2(420, 100),
                () => { Destroy(panel); if (_levelRoot != null) Destroy(_levelRoot.gameObject); ShowMenu(); });
        }

        void ShowLeaderboard(string mode)
        {
            Audio.Play("click");
            var c = new Vector2(0.5f, 0.5f);
            var panel = Overlay(new Color(0.04f, 0.02f, 0.06f, 0.92f), out var root);
            Theme.Label(root, "LEADERBOARD", 70, Theme.Player, c, new Vector2(0, 400), new Vector2(1400, 120)).font = Theme.TitleFont;
            string scope = mode == "daily" ? "today" : "all";
            string heading = mode == "daily" ? "Blood Moon — tonight (fewest deaths)"
                           : mode == "endless" ? "Endless Night — deepest floor"
                                               : "The Castle — fewest deaths";
            Theme.Label(root, heading, 32, Theme.Coin, c, new Vector2(0, 310), new Vector2(1400, 50));
            var list = Theme.Label(root, "summoning the dead…", 34, Color.white,
                c, new Vector2(0, -30), new Vector2(1000, 540), TextAnchor.UpperCenter);
            Leaderboard.Fetch(mode, scope, entries =>
            {
                if (list == null) return;
                if (entries.Count == 0)
                { list.text = "No souls ranked yet — be the first.\n(or the leaderboard server isn't live yet)"; return; }
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < entries.Count && i < 15; i++)
                    sb.AppendLine($"{i + 1}.   {entries[i].nick}      {entries[i].value}");
                list.text = sb.ToString();
            });
            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 40,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), () => { Destroy(panel); ShowMenu(); });
        }

        void ShowWardrobe()
        {
            Audio.Play("click");
            var c = new Vector2(0.5f, 0.5f);
            var panel = Overlay(new Color(0.04f, 0.02f, 0.06f, 0.92f), out var root);
            Theme.Label(root, "WARDROBE", 70, Theme.Player, c, new Vector2(0, 420), new Vector2(1400, 120)).font = Theme.TitleFont;
            Theme.Label(root, "cosmetic look + a signature mobility trick — never pay-to-win", 28, Theme.Coin, c, new Vector2(0, 348), new Vector2(1400, 50));

            // Preview sprites: the vampire idle frame (most skins) and the Pink-Man
            // frame (the "pink" skin). Shown tinted so you actually SEE the costume.
            var vampFrames = Assets.Grid("vamp_idle_sheet", 64, 3);
            Sprite vampSp = (vampFrames != null && vampFrames.Length > 0) ? vampFrames[0] : null;
            var pmFrames = Assets.Sheet("pinkman_idle", 32);
            Sprite pmSp = (pmFrames != null && pmFrames.Length > 0) ? pmFrames[0] : null;

            // 3 rows now (the Crypt Shop skins joined the roster) — tightened row
            // spacing so the bottom row clears the BACK button.
            int cols = 4; float spX = 350f, spY = 240f, startX = -((cols - 1) * spX) / 2f, startY = 200f;
            for (int i = 0; i < Skins.All.Count; i++)
            {
                var s = Skins.All[i];
                int r = i / cols, col = i % cols;
                var pos = new Vector2(startX + col * spX, startY - r * spY);
                bool unlocked = Skins.IsUnlocked(s);
                bool equipped = Skins.CurrentId == s.id;
                var bg = equipped ? new Color(0.42f, 0.11f, 0.15f, 0.96f)
                       : unlocked ? new Color(0.16f, 0.13f, 0.2f, 0.95f) : new Color(0.1f, 0.1f, 0.13f, 0.95f);
                string sid = s.id; var sdef = s;

                // The whole card is one button (Image + Button). We build its contents
                // ourselves so the sprite, name and ability each get their own line and
                // never overlap. Empty label text — the children below are the content.
                // Locked PRICED skins route to the Crypt Shop (that's where they're
                // bought); other locked skins just restate their achievement hint.
                var card = Theme.Button(root, "", bg, Color.white, 1, c, pos, new Vector2(320, 236),
                    unlocked ? (System.Action)(() => { Skins.Equip(sid); Destroy(panel); ShowWardrobe(); })
                    : sdef.price > 0 ? (System.Action)(() => { Destroy(panel); ShowShop(); })
                                     : (System.Action)(() => ShowHint(sdef.unlockHint)));
                var ct = card.transform;

                // Gold ring around the equipped card so the current pick is obvious.
                if (equipped)
                {
                    var ring = new GameObject("EquipRing", typeof(RectTransform)).AddComponent<Image>();
                    ring.transform.SetParent(ct, false); ring.raycastTarget = false;
                    var frame = Theme.NineSlice("panel_frame", 16);
                    if (frame != null) { ring.sprite = frame; ring.type = Image.Type.Sliced; ring.pixelsPerUnitMultiplier = 0.12f; }
                    ring.color = Theme.Coin;
                    var rrt = ring.rectTransform; rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
                    rrt.offsetMin = new Vector2(-4, -4); rrt.offsetMax = new Vector2(4, 4);
                }

                // Sprite preview — tinted for unlocked skins, a dark mystery silhouette
                // when locked (so the look stays a surprise until you earn it).
                Sprite preview = s.pinkman ? pmSp : vampSp;
                if (preview != null)
                {
                    var pv = new GameObject("Preview", typeof(RectTransform)).AddComponent<Image>();
                    pv.transform.SetParent(ct, false);
                    pv.sprite = preview; pv.preserveAspect = true; pv.raycastTarget = false;
                    pv.color = unlocked ? Skins.Shade(s) : new Color(0.05f, 0.04f, 0.06f, 0.95f);
                    var prt = pv.rectTransform;
                    prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.anchoredPosition = new Vector2(0, 58); prt.sizeDelta = new Vector2(104, 104);
                }

                // Name.
                Theme.Label(ct, s.name, 28, unlocked ? Color.white : new Color(1, 1, 1, 0.55f),
                    c, new Vector2(0, -22), new Vector2(304, 36)).raycastTarget = false;

                // Ability line (unlocked) or unlock hint (locked) — its own row, no overlap.
                if (unlocked)
                {
                    Theme.Label(ct, s.ability, 19, Theme.Coin, c, new Vector2(0, -56), new Vector2(300, 30)).raycastTarget = false;
                    Theme.Label(ct, equipped ? "EQUIPPED" : "tap to wear", 18,
                        equipped ? Theme.Exit : new Color(1, 1, 1, 0.45f), c,
                        new Vector2(0, -90), new Vector2(300, 28)).raycastTarget = false;
                }
                else
                {
                    Theme.Label(ct, "LOCKED", 20, new Color(1, 0.5f, 0.5f, 0.85f), c,
                        new Vector2(0, -54), new Vector2(300, 30)).raycastTarget = false;
                    var hint = Theme.Label(ct, s.unlockHint, 15, new Color(1, 1, 1, 0.5f), c,
                        new Vector2(0, -88), new Vector2(280, 48));
                    hint.horizontalOverflow = HorizontalWrapMode.Wrap;   // wrap inside the card
                    hint.raycastTarget = false;
                }
            }

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 40,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), () => { Destroy(panel); ShowMenu(); });
        }

        // ==================== the Crypt Shop ====================
        // Where blood shards go: purchasable skins + death effects + trails +
        // gravestone taunts. Cosmetics ONLY — the shop sells looks and trash talk,
        // never power. Cards follow the Wardrobe pattern (whole card = one button).
        void ShowShop()
        {
            Audio.Play("click");
            Analytics.Track("shop_open", new System.Collections.Generic.Dictionary<string, object>
            {
                { "balance", Currency.Balance },
            });
            var c = new Vector2(0.5f, 0.5f);
            var panel = Overlay(new Color(0.04f, 0.02f, 0.06f, 0.92f), out var root);
            Theme.Label(root, "THE CRYPT SHOP", 70, Theme.Player, c, new Vector2(0, 420), new Vector2(1400, 120)).font = Theme.TitleFont;
            Theme.Label(root, $"{Currency.Balance} BLOOD SHARDS — the castle takes shards, never skill", 28, Theme.Coin,
                c, new Vector2(0, 348), new Vector2(1400, 50));

            // Skin preview art (same sources as the Wardrobe previews).
            var vampFrames = Assets.Grid("vamp_idle_sheet", 64, 3);
            Sprite vampSp = (vampFrames != null && vampFrames.Length > 0) ? vampFrames[0] : null;
            var pmFrames = Assets.Sheet("pinkman_idle", 32);
            Sprite pmSp = (pmFrames != null && pmFrames.Length > 0) ? pmFrames[0] : null;

            // One flat list: the priced skins first (the aspirational shelf), then
            // death effects, trails, and taunts from the Shop catalog.
            int cols = 4; float spX = 350f, spY = 244f, startX = -((cols - 1) * spX) / 2f, startY = 196f;
            int slot = 0;

            foreach (var s in Skins.All)
            {
                if (s.price <= 0) continue;
                var sd = s;
                bool owned = Skins.IsUnlocked(sd);
                bool equipped = owned && Skins.CurrentId == sd.id;
                ShopCard(root, panel, slot++, cols, spX, spY, startX, startY,
                    sd.name, "skin — pure style", sd.price, owned, equipped,
                    sd.pinkman ? pmSp : vampSp, Skins.Shade(sd),
                    buy: () => { if (Shop.BuySkin(sd)) Skins.Equip(sd.id); },
                    equip: () => Skins.Equip(sd.id));
            }
            foreach (var it in Shop.All)
            {
                var item = it;
                bool owned = Shop.Owns(item.id);
                bool equipped = owned && Shop.Equipped(item.kind) == item.id;
                ShopCard(root, panel, slot++, cols, spX, spY, startX, startY,
                    item.name, item.desc, item.price, owned, equipped,
                    null, item.tint,
                    buy: () => { if (Shop.Buy(item)) Shop.Equip(item.kind, item.id); },
                    // Owned items TOGGLE: tap to wear, tap again to take off.
                    equip: () => Shop.Equip(item.kind, equipped ? "" : item.id));
            }

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 40,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), () => { Destroy(panel); ShowMenu(); });
        }

        // One shop card: preview (sprite or a tinted diamond), name, flavor line,
        // and a state footer — BUY (gold, affordable) / NEED N MORE (dim) /
        // EQUIPPED / tap to wear. Any successful action rebuilds the screen.
        void ShopCard(Transform root, GameObject panel, int slot, int cols,
            float spX, float spY, float startX, float startY,
            string name, string desc, int price, bool owned, bool equipped,
            Sprite preview, Color tint, System.Action buy, System.Action equip)
        {
            var c = new Vector2(0.5f, 0.5f);
            int r = slot / cols, col = slot % cols;
            var pos = new Vector2(startX + col * spX, startY - r * spY);
            bool affordable = Currency.Balance >= price;
            var bg = equipped ? new Color(0.42f, 0.11f, 0.15f, 0.96f)
                   : owned ? new Color(0.16f, 0.13f, 0.2f, 0.95f)
                   : affordable ? new Color(0.22f, 0.18f, 0.1f, 0.95f) : new Color(0.1f, 0.1f, 0.13f, 0.95f);

            var card = Theme.Button(root, "", bg, Color.white, 1, c, pos, new Vector2(320, 224), () =>
            {
                if (owned) { equip(); Audio.Play("click", 0.7f); }
                else if (affordable)
                {
                    buy();
                    Audio.PlayOr("levelup", "win", 0.7f);
                    if (_shardText != null) _shardText.text = Currency.Balance.ToString();
                }
                else { ShowHint($"{price - Currency.Balance} more shards — the castle pays for blood"); return; }
                Destroy(panel); ShowShop();   // rebuild so every card reflects the new state
            });
            var ct = card.transform;

            if (equipped)   // same gold ring the Wardrobe uses for the current pick
            {
                var ring = new GameObject("EquipRing", typeof(RectTransform)).AddComponent<Image>();
                ring.transform.SetParent(ct, false); ring.raycastTarget = false;
                var frame = Theme.NineSlice("panel_frame", 16);
                if (frame != null) { ring.sprite = frame; ring.type = Image.Type.Sliced; ring.pixelsPerUnitMultiplier = 0.12f; }
                ring.color = Theme.Coin;
                var rrt = ring.rectTransform; rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
                rrt.offsetMin = new Vector2(-4, -4); rrt.offsetMax = new Vector2(4, 4);
            }

            // Preview: the skin sprite when there is one, else a tinted diamond
            // swatch (death effects / trails / taunts have no sprite of their own).
            if (preview != null)
            {
                var pv = new GameObject("Preview", typeof(RectTransform)).AddComponent<Image>();
                pv.transform.SetParent(ct, false);
                pv.sprite = preview; pv.preserveAspect = true; pv.raycastTarget = false;
                pv.color = owned ? tint : new Color(0.05f, 0.04f, 0.06f, 0.95f);   // mystery silhouette until bought
                var prt = pv.rectTransform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = new Vector2(0, 52); prt.sizeDelta = new Vector2(96, 96);
            }
            else
            {
                var sw = new GameObject("Swatch", typeof(RectTransform)).AddComponent<Image>();
                sw.transform.SetParent(ct, false); sw.raycastTarget = false;
                sw.color = tint;
                var srt2 = sw.rectTransform;
                srt2.anchorMin = srt2.anchorMax = new Vector2(0.5f, 0.5f); srt2.pivot = new Vector2(0.5f, 0.5f);
                srt2.anchoredPosition = new Vector2(0, 52); srt2.sizeDelta = new Vector2(52, 52);
                srt2.localRotation = Quaternion.Euler(0, 0, 45f);
            }

            Theme.Label(ct, name, 27, owned || affordable ? Color.white : new Color(1, 1, 1, 0.55f),
                c, new Vector2(0, -20), new Vector2(304, 34)).raycastTarget = false;
            var descL = Theme.Label(ct, desc, 16, new Color(1, 1, 1, 0.55f),
                c, new Vector2(0, -50), new Vector2(290, 40));
            descL.horizontalOverflow = HorizontalWrapMode.Wrap;
            descL.raycastTarget = false;

            string footer = equipped ? "EQUIPPED — tap to remove"
                          : owned ? "tap to wear"
                          : affordable ? $"BUY — {price}" : $"NEED {price - Currency.Balance} MORE";
            Theme.Label(ct, footer, 19,
                equipped ? Theme.Exit : owned ? new Color(1, 1, 1, 0.5f)
                         : affordable ? Theme.Coin : new Color(1, 0.5f, 0.5f, 0.7f),
                c, new Vector2(0, -86), new Vector2(300, 28)).raycastTarget = false;
        }

        // ==================== pause ====================
        void TogglePause()
        {
            if (_state == State.Play) Pause();
            else if (_state == State.Paused) Resume();
        }

        void Pause()
        {
            _state = State.Paused;
            Analytics.Track("pause", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
            });
            Time.timeScale = 0f;
            _pausePanel = Overlay(new Color(0, 0, 0, 0.6f), out var root);
            Theme.Label(root, "PAUSED", 96, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1000, 130));
            // Endless never ends on lives, so it needs an explicit "END RUN" to bank
            // your depth and see the score — that's the 4-button layout.
            bool endless = _mode == Mode.Endless;
            Theme.Button(root, "RESUME", Theme.Exit, Theme.Ink, 52,
                new Vector2(0.5f, 0.5f), new Vector2(0, endless ? 130 : 70), new Vector2(460, 116), Resume);
            Theme.Button(root, "RESTART LEVEL", Theme.Trick, Theme.Ink, 44,
                new Vector2(0.5f, 0.5f), new Vector2(0, endless ? 6 : -70), new Vector2(560, 116), RestartLevel);
            if (endless)
                Theme.Button(root, "END RUN — bank score", new Color(0.55f, 0.1f, 0.13f), Color.white, 42,
                    new Vector2(0.5f, 0.5f), new Vector2(0, -118), new Vector2(560, 116), EndRun);
            Theme.Button(root, "MAIN MENU", new Color(1, 1, 1, 0.25f), Color.white, 44,
                new Vector2(0.5f, 0.5f), new Vector2(0, endless ? -242 : -210), new Vector2(560, 116), QuitToMenu);
        }

        // End an Endless run on purpose: unpause and show the result/leaderboard screen.
        void EndRun()
        {
            if (_pausePanel != null) Destroy(_pausePanel);
            Time.timeScale = 1f;
            RunOver();
        }

        void Resume()
        {
            if (_pausePanel != null) Destroy(_pausePanel);
            Analytics.Track("resume", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
            });
            Time.timeScale = 1f;
            _state = State.Play;
        }

        void RestartLevel()
        {
            if (_pausePanel != null) Destroy(_pausePanel);
            Time.timeScale = 1f;
            _state = State.Play;
            _hasCheckpoint = false;
            Destroy(_levelRoot.gameObject);
            BuildLevel();
        }

        void QuitToMenu()
        {
            if (_pausePanel != null) Destroy(_pausePanel);
            // Funnel: an explicit floor-1 walk-away (tab-closes are derived
            // server-side from a level1_start with no level1_complete).
            if (_mode == Mode.Curated && _levelIndex == 0)
                Analytics.Track("level1_abandon", new System.Collections.Generic.Dictionary<string, object>
                {
                    { "duration_ms", LevelDurationMs },
                    { "deaths", _floorDeaths },
                });
            Analytics.Track("run_end", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "final_level_index", _levelIndex },
                { "total_deaths", _deaths },
                { "reason", "quit" },
            });
            Time.timeScale = 1f;
            if (_levelRoot != null) Destroy(_levelRoot.gameObject);
            ShowMenu();
        }

        // ==================== helpers ====================
        GameObject Overlay(Color bg, out Transform root)
        {
            var panel = new GameObject("Overlay", typeof(RectTransform));
            panel.transform.SetParent(Theme.Canvas.transform, false);
            var img = panel.AddComponent<Image>();
            img.color = bg;
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Gothic ornate frame around the panel (behind the content, no raycast).
            var frameSp = Theme.NineSlice("panel_frame", 16);
            if (frameSp != null)
            {
                var fr = new GameObject("Frame", typeof(RectTransform));
                fr.transform.SetParent(panel.transform, false);
                var fi = fr.AddComponent<Image>();
                fi.sprite = frameSp; fi.type = Image.Type.Sliced; fi.raycastTarget = false;
                fi.pixelsPerUnitMultiplier = 0.22f;   // scale the ornate corners up so they read at fullscreen
                fi.color = new Color(0.95f, 0.9f, 0.92f, 0.95f);
                var frt = fi.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(26, 26); frt.offsetMax = new Vector2(-26, -26);
            }

            root = panel.transform;
            return panel;
        }

        IEnumerator Pulse(Transform t)
        {
            while (t != null)
            {
                float s = 1f + Mathf.Sin(Time.unscaledTime * 2f) * 0.03f;
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
        }
    }
}
