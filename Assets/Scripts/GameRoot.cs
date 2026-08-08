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

        // ---- back-button navigation ----
        // The Android hardware BACK key arrives as KeyCode.Escape. It used to be
        // handled ONLY during play, so on every menu/shop/settings screen the press
        // fell through to Android's default and CLOSED THE APP — you had to force-
        // quit to change modes. Every screen now points _onBack at the same thing
        // its on-screen "‹ BACK" button does, and one handler routes the key there.
        System.Action _onBack;
        float _quitArmedUntil;    // main-menu "press back again to quit" window

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

        enum Mode { Curated, Endless, Daily, Versus, Custom }
        // The map currently being played in Mode.Custom (yours or a friend's), and
        // the code it came from — the code is the identity a best time is filed under.
        CustomMap _customMap;
        string _customCode = "";
        float _customStart;      // realtime the attempt began, for the race clock
        Mode _mode = Mode.Curated;
        int _endlessSeed;
        // Endless is presented as one continuous distance run. Internally it is
        // generated in bounded chunks so a long session never grows an unbounded
        // scene. Chunk boundaries are deliberately absent from the HUD/game flow.
        float _endlessBankedMeters;
        float _endlessPeakMeters;
        int _endlessLastHudMeters = -1;
        bool _endlessRevivePending;
        readonly System.Collections.Generic.List<Vector3> _endlessSafeHistory = new();
        readonly System.Collections.Generic.HashSet<int> _endlessLifeClaimed = new();
        const int DailyLen = 5;

        // The link that rides along with every share. PLACEHOLDER — point this at
        // the Play Store listing (or the itch/web build) once the game is live and
        // every share message starts pulling real players back in.
        public const string GameLink = "https://trust-issues.game";
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
        EndlessThemeBackdrop _endlessBackdrop;

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
            // Lock to landscape at runtime as well as in Player Settings, so the
            // phone just OPENS in landscape like every other game instead of nagging
            // the player to rotate. AutoRotation + both landscape directions means
            // it still flips 180 when they hold the phone the other way, but never
            // drops into portrait — so the old "rotate your phone" panel can no
            // longer trigger on a real device.
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            // Keep simulating when the window isn't focused — otherwise a second
            // (unfocused) instance pauses, its Photon keepalive stops, and the
            // server times it out (the "no ghost" / AppOutOfFocus disconnect).
            Application.runInBackground = true;
            // WebGL leaves the frame rate at the platform default, which on some
            // mobile browsers throttles well below the display refresh — every
            // touch read (Update()) then happens less often, which is exactly
            // what players feel as "movement is delayed." Pin it explicitly.
            Application.targetFrameRate = 60;
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
            // The launch video plays OVER a fully-built menu rather than delaying
            // it, so the game is already live the instant the intro clears — no
            // second load, and nothing can strand the player if the clip fails.
            // Menu music is held back so it doesn't fight the video's own audio.
            Audio.StopMusic();
            Intro.Play(() => Audio.Music("music", 0.3f));
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
            // The painted backdrop plate has the moon in it already, sitting against
            // the castle skyline exactly as painted. Building the separate
            // camera-parented one on top of that would put TWO moons in the sky.
            if (Theme.Moon != null && Assets.Sprite("bgc_plate") == null)
            {
                // Sized and placed off the gameplay artwork: the moon's disc is about a
                // quarter of the screen height, sitting high on the right where the
                // castle spires cut into it. It used to be nearly half the screen and
                // sat almost in the corner, which read as a wash rather than a moon.
                var moon = new GameObject("ThemeMoon");
                moon.transform.SetParent(_cam.transform, false);
                // Measured off the artwork (its moon centres at 78% across, 32% down):
                // just inside the right edge and high, not jammed into the corner.
                moon.transform.localPosition = new Vector3(4.4f, 0.9f, 21f);
                var mb = Theme.Moon.bounds.size;
                float ms = 2.9f / Mathf.Max(0.0001f, mb.y);
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
                // This must be created before any of the backdrop branches return.
                // The painted castle plate is present in production builds and used
                // to return before EndlessThemeBackdrop existed, leaving Endless on
                // the red castle artwork forever.
                _endlessBackdrop = root.AddComponent<EndlessThemeBackdrop>();
                _endlessBackdrop.Init(_cam.transform, _parallax);

                // The imported backdrop art is a BLUE mountain range, and a multiply
                // tint can't take blue out of it — every attempt just came out a darker
                // blue. So the layers were recoloured once, offline, onto the exact
                // ramp sampled from the gameplay artwork (near-black stone lit only by
                // the blood moon) and saved as bgv_*. Those go in untinted; the old
                // blue files stay as the fallback if the recoloured set is missing.
                bool v = Assets.Sprite("bgv_sky") != null;
                // Each layer is stepped DOWN in brightness the nearer it is, which is
                // how the painting reads: far mountains catch what light there is,
                // the ridge in front of you is almost a silhouette. Going in at full
                // white made every layer equally bright, so the whole hall behind the
                // player was one flat mauve field with no depth in it at all.
                // THE PLATE. Built by tools/build_backdrop.py out of the gameplay
                // painting itself — sky, moon, gothic skyline and mountain valley
                // composited from the painting's clean regions. It replaces the sky,
                // far and castle layers wholesale, because the old art simply did not
                // contain what the painting contains: its castle is one blobby spire
                // cluster where the painting has a skyline, and no amount of colour
                // tuning grows spires. The near/mid ridges stay in front of it, so
                // there's still real parallax movement rather than a static mural.
                if (Assets.Sprite("bgc_plate") != null)
                {
                    // yCenter 0.35 = the camera's own centre height. The plate is
                    // composited against that assumption (see to_plate in the tool),
                    // so anything else slides the whole skyline off its painted spot.
                    // yCenter is printed by tools/build_backdrop.py — it's derived
                    // from where the painted window sits on screen, so the skyline
                    // lands exactly where it was painted. Don't eyeball it.
                    AddParallax("bgc_plate", Color.white, -1.41f, -26, 0.955f);
                    // Only ONE layer rides in front of it. bgv_mid used to as well and
                    // its tower silhouette landed as a big black blob right where the
                    // castle should be, hiding the spires the plate exists to show.
                    AddParallax("bgv_near", new Color(0.17f, 0.15f, 0.17f, 1f), -0.8f, -14, 0.66f);
                    return;
                }

                Color Dim(float k) => new Color(k, k * 0.94f, k * 1.02f, 1f);
                // Levels re-measured against the painting: its sky sits at (25,13,21)
                // while this was rendering (44,18,28), so the whole stack came down.
                // The SPREAD between layers widened at the same time — the far ridge
                // is now nearly twice the value of the near one. That gap is what
                // atmospheric perspective actually is, and it's what makes the valley
                // read as receding distance instead of one flat mauve field. Bringing
                // everything down by a flat factor would have kept it just as flat,
                // only darker.
                //          sprite                        tint                          yCenter order follow alpha
                AddParallax(v ? "bgv_sky" : "bg_sky",       v ? Dim(0.34f) : Theme.Hex("16101F"), 1.2f,  -28, 0.97f);
                AddParallax(v ? "bgv_far" : "bg_far",       v ? Dim(0.52f) : Theme.Hex("2A2038"), 0.9f,  -24, 0.90f);
                AddParallax(v ? "bgv_castle" : "bg_castle", v ? Dim(0.62f) : Theme.Hex("531B26"), 0.6f,  -20, 0.82f);
                AddParallax(v ? "bgv_mid" : "bg_mid",       v ? Dim(0.30f) : Theme.Hex("241A30"), 0.2f,  -17, 0.72f);
                AddParallax(v ? "bgv_near" : "bg_near",     v ? Dim(0.15f) : Theme.Hex("140E1C"), -0.4f, -14, 0.60f);
                // The fog used to be a full-bleed haze at 30% over all of it, and that
                // is what turned the hall into one flat mauve field with no ridges and
                // no castle in it. In the painting the air is CLEAR: the depth comes
                // from the layers being different brightnesses, not from smoke.
                AddParallax(v ? "bgv_fog" : "bg_fog",       v ? Dim(0.55f) : Theme.Hex("3A1622"), 0.4f,  -12, 0.55f, 0.09f);

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
        // Endless cycles three THEMES (abyss/void/inferno) but has one identity, and
        // only ever had one track file — the other two names resolved to nothing and
        // fell back to the MENU music, so a deep Endless run kept dropping into the
        // title theme. All three now point at the same Endless track.
        static readonly string[] ThemeMusic =
        { "music_castle", "music_crypt", "music_swamp", "music_throne", "music_bloodmoon",
          "music_endless", "music_endless", "music_endless", "music_arena" };
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
        // The per-theme colour wash over the whole backdrop (alpha baked in), so each
        // world reads as a different place.
        //
        // It used to be twice this strong, and that one number was the difference
        // between the game and its own artwork: at alpha 0.46 the castle wasn't a
        // night with a blood moon in it, it was a flat pink field with everything —
        // mountains, castle, spires, fog — flooded out behind it. Sampling the
        // gameplay painting settles the argument: its sky is (21,11,20) and its
        // deepest mountain (37,17,29), i.e. almost black with a violet lean. So the
        // wash is now a TINT on a night rather than a filter over one — every layer
        // of backdrop art is visible again, and the only genuinely bright things on
        // screen are the moon, the candles and the blood.
        static readonly Color[] ThemeWash =
        {
            new Color(0.26f, 0.13f, 0.22f, 0.20f),  // castle  — violet-black night
            new Color(0.10f, 0.22f, 0.55f, 0.30f),  // crypt   — cold blue
            new Color(0.10f, 0.42f, 0.16f, 0.30f),  // swamp   — sickly green
            new Color(0.52f, 0.34f, 0.08f, 0.28f),  // throne  — hot amber
            new Color(0.70f, 0.05f, 0.12f, 0.36f),  // blood moon — searing red (its whole point)
            new Color(0.36f, 0.10f, 0.58f, 0.30f),  // abyss   — violet
            new Color(0.04f, 0.48f, 0.44f, 0.30f),  // void    — teal
            new Color(0.68f, 0.20f, 0.04f, 0.32f),  // inferno — ember orange
            new Color(0.18f, 0.24f, 0.40f, 0.28f),  // arena   — cold steel
        };
        // A big themed MOON in the upper sky — a bold, obvious per-theme anchor so the
        // modes read as completely different places at a glance.
        static readonly Color[] ThemeMoon =
        {
            new Color(0.64f, 0.13f, 0.20f, 0.88f),  // castle  — blood moon (the painting's deep crimson)
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

        // The versus rotation: each race lands in a different world, ordered so
        // consecutive rounds are maximally different (cold steel → searing red →
        // cold blue → ember → violet → green → teal → gold → crimson).
        static readonly int[] VersusThemes = { 8, 4, 1, 7, 5, 2, 6, 3, 0 };

        static readonly string[] EndlessThemeNames =
        {
            "FORSAKEN HIGHLANDS", "FROZEN WASTES", "CURSED CATHEDRAL",
            "INFERNAL DEPTHS", "SHADOW REALM", "OBLIVION NIGHT"
        };

        static int EndlessThemeForFloor(int floor)
        {
            // floor is zero-based: Frozen Wastes begins as the HUD enters Floor 5.
            if (floor < 4) return 0;
            if (floor < 8) return 1;
            if (floor < 12) return 2;
            if (floor < 16) return 3;
            if (floor < 20) return 4;
            return 5;
        }

        // How visible the castle parallax is per theme — faded to a distant ruin in the
        // open "void" modes (Endless) so they don't read as "the same castle" as Castle.
        static readonly float[] ThemeCastleVis =
        { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.22f, 0.18f, 0.30f, 0.55f };
        // Moon world-diameter per theme (Blood Moon looms huge; the arena's is small).
        // Measured against the artwork: its moon is a disc about a quarter of the
        // screen height, high on the right where the castle spires cut into it. The
        // view is 11.2 units tall, so the Castle's moon is ~2.9 — it was 7.5, which
        // is two thirds of the screen and reads as a red sky, not a moon.
        static readonly float[] ThemeMoonSize =
        { 2.9f, 2.6f, 2.8f, 3.0f, 6.0f, 3.4f, 3.0f, 3.6f, 2.4f };

        public static int WorldOf(int floorIdx) => Mathf.Clamp((floorIdx / 10) % 4, 0, 3);

        int _curTheme = -1;
        int _curShadeFloor = -1;       // which floor the current per-floor shade was built for
        SpriteRenderer _washSr;        // the themed colour wash (see BuildBackdrop)
        SpriteRenderer _moonSr;        // the big themed moon (bold per-theme anchor)

        /// <summary>
        /// A per-FLOOR shade laid on top of the world theme. The world only changes
        /// every 10 floors (WorldOf), and the analytics say almost nobody survives
        /// floor 10 — so without this, every floor a real player ever sees is the
        /// SAME picture. That is the single biggest reason the game "looks samey".
        /// Each floor rotates the hue a little and breathes the brightness, so
        /// consecutive floors read as different rooms of one castle rather than one
        /// repeated screen. Deterministic (same floor = same look, every time).
        /// </summary>
        static Color ShadeFor(Color c, int floorIdx, float hueSpan, float valSpan)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // Golden-angle-ish stepping: neighbouring floors land far apart on the
            // wheel, so floor 3 never looks like floor 4, but it never drifts so far
            // that the world stops reading as "the crypt" / "the swamp".
            float f = Mathf.Sin(floorIdx * 2.399963f);        // stable, in [-1,1]
            h = Mathf.Repeat(h + f * hueSpan, 1f);
            v = Mathf.Clamp01(v * (1f + f * valSpan));
            var o = Color.HSVToRGB(h, s, v);
            o.a = c.a;                                        // keep the tuned alpha
            return o;
        }
        readonly System.Collections.Generic.List<Mote> _motes = new();

        // Pick the backdrop theme from the current mode/progress, then apply it.
        void ThemeBackdrop()
        {
            int idx;
            int endlessTheme = -1;
            switch (_mode)
            {
                case Mode.Daily:   idx = 4; break;                         // Blood Moon
                // Every versus ROUND races somewhere new — the arena travels through
                // the whole castle (steel → blood moon → crypt → inferno → …) so a
                // long match never replays the same picture. Both players share the
                // round number, so they always see the same place.
                case Mode.Versus:  idx = VersusThemes[_versusRound % VersusThemes.Length]; break;
                case Mode.Custom:  idx = 6; break;                         // The Void — a built place
                case Mode.Endless: idx = 5 + (_levelIndex / 10) % 3; break;// Abyss → Void → Inferno
                default:           idx = WorldOf(_levelIndex); break;      // Castle worlds
            }
            if (_mode == Mode.Endless)
            {
                endlessTheme = EndlessThemeForFloor(_levelIndex);
            }
            bool changed = idx != _curTheme ||
                (_mode == Mode.Endless && _endlessBackdrop != null &&
                 _endlessBackdrop.CurrentTheme != endlessTheme);
            ApplyTheme(idx);
            if (_mode == Mode.Endless)
            {
                if (_washSr != null) { var c = _washSr.color; c.a = 0f; _washSr.color = c; }
                if (_moonSr != null) { var c = _moonSr.color; c.a = 0f; _moonSr.color = c; }
            }
            if (_endlessBackdrop != null)
            {
                if (_mode == Mode.Endless) _endlessBackdrop.Show(endlessTheme, 3.5f);
                else _endlessBackdrop.Hide(0.35f);
            }
            // Announce a new region as you cross into it (Castle worlds / Endless depths),
            // but not on the first floor, on a death-respawn, or inside a boss arena.
            if (changed && _state == State.Play && _levelIndex > 0 && !InBossRoom &&
                (_mode == Mode.Curated || _mode == Mode.Endless))
                ShowBanner(_mode == Mode.Endless
                    ? $"ENTERING {EndlessThemeNames[endlessTheme]}"
                    : $"ENTERING {ThemeNames[idx]}", "the world shifts around you");
        }

        // Recolour the sky, parallax layers and ambient motes for a theme.
        void ApplyTheme(int idx)
        {
            idx = Mathf.Clamp(idx, 0, ThemeTint.Length - 1);
            // Re-apply when EITHER the world or the floor changes — the per-floor
            // shade below is what stops ten floors of a world looking identical.
            // In Versus the floor is always 0, so the ROUND drives the shade instead;
            // otherwise every race would be tinted identically.
            int shadeFloor = _mode == Mode.Versus ? _versusRound : _levelIndex;
            if (idx == _curTheme && shadeFloor == _curShadeFloor) return;
            _curTheme = idx;
            _curShadeFloor = shadeFloor;
            var t = ThemeTint[idx];
            float castleVis = ThemeCastleVis[idx];
            // Half-strength on the parallax: the backdrop art now carries the castle's
            // own colour, so a full world tint would wash the painting away. The theme
            // wash, sky and moon still swing the full amount, which is what actually
            // makes one world read differently from another.
            var pt = Color.Lerp(Color.white, t, 0.5f);
            for (int i = 0; i < _bgSr.Count; i++)
            {
                if (_bgSr[i] == null) continue;
                var b = _bgBase[i];
                _bgSr[i].color = new Color(b.r * pt.r, b.g * pt.g, b.b * pt.b, b.a * castleVis);
            }
            // Every visible backdrop layer gets the per-floor shade, so the whole
            // picture shifts together instead of one element looking recoloured.
            var sky   = ShadeFor(ThemeSky[idx],    shadeFloor, 0.035f, 0.30f);
            var wash  = ShadeFor(ThemeWash[idx],   shadeFloor, 0.050f, 0.18f);
            var moonC = ShadeFor(ThemeMoon[idx],   shadeFloor, 0.045f, 0.12f);
            var accent= ShadeFor(ThemeAccent[idx], shadeFloor, 0.060f, 0.15f);
            if (_skySr != null) _skySr.color = sky;
            if (_washSr != null) _washSr.color = wash;             // the actually-visible colour shift
            if (_moonSr != null && Theme.Moon != null)             // bold per-theme anchor (colour + size)
            {
                // Red themes (castle 0, blood moon 4) wear the PAINTED moon cut from
                // the reference artwork, and it goes on essentially untinted — the
                // crimson and the maria are already in the paint, and multiplying the
                // theme colour over it would only mud it. Every other theme keeps the
                // procedural disc, which has to stay tintable to be a pale blue crypt
                // moon or a sickly green swamp one.
                bool painted = (idx == 0 || idx == 4) && Theme.MoonArt != null;
                _moonSr.sprite = painted ? Theme.MoonArt : Theme.Moon;
                _moonSr.color = painted ? new Color(1f, 1f, 1f, 0.95f) : moonC;
                float mb = _moonSr.sprite.bounds.size.y;
                // Breathe the moon's size per floor too — the silhouette changes,
                // not just the palette, so the skyline itself looks like a new place.
                float sizeMul = 1f + 0.18f * Mathf.Sin(shadeFloor * 1.61803f);
                float ms = mb > 0.0001f ? (ThemeMoonSize[idx] * sizeMul) / mb : 1f;
                _moonSr.transform.localScale = new Vector3(ms, ms, 1f);
            }
            if (_cam != null) _cam.backgroundColor = sky;
            foreach (var m in _motes) if (m != null) m.Recolor(accent);
        }

        // The gameplay artwork frames its play area the way the menus frame theirs:
        // a band of castle stone down each edge with the ornate gold-and-blood border
        // outside it. Built once, shown only while a level is actually running, and
        // never a raycast target — it's a picture frame, not a wall.
        GameObject _levelFrame;
        void BuildLevelFrame()
        {
            _levelFrame = new GameObject("LevelFrame", typeof(RectTransform));
            _levelFrame.transform.SetParent(Theme.Canvas.transform, false);
            var rt = _levelFrame.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Stone band on the top and both sides. The BOTTOM is deliberately left
            // open: that's where the thumb controls live, and a band under them just
            // eats screen. Kept thin — every unit of it is playfield you can't see.
            const float Side = 30f, Top = 26f;
            AddFrameBand(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -Top), new Vector2(0, 0));
            AddFrameBand(rt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0, 0), new Vector2(Side, 0));
            AddFrameBand(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-Side, 0), new Vector2(0, 0));

            // The ornate border, right at the screen edge.
            Gothic.Border(_levelFrame.transform, 0f);
            _levelFrame.SetActive(false);
        }

        // One masonry band along an edge of the level frame.
        void AddFrameBand(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject("Band", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = Theme.StoneTile; img.type = Image.Type.Tiled;
            img.pixelsPerUnitMultiplier = 0.16f;
            img.color = new Color(0.30f, 0.26f, 0.30f, 1f);
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = aMin; r.anchorMax = aMax;
            r.offsetMin = offMin; r.offsetMax = offMax;
        }

        // ---- the HUD, laid out off the gameplay artwork ----------------------
        // Every position here is measured from the painting (1568x1003) and scaled
        // by 1920/1568 into canvas units, then anchored to the CORNER it belongs to
        // — a tall phone gives the canvas far less than 1080 of height, so anything
        // positioned from the centre walks off the screen there.
        Image[] _heartPips;      // the lives row beside the portrait
        Text _stageText;         // "STAGE 2 / 5" under the title
        GameObject _hudChrome;   // everything that shows only while a floor is running

        void BuildHUD()
        {
            BuildLevelFrame();

            // One container for the whole play-time HUD, so showing and hiding it is
            // a single switch instead of six.
            _hudChrome = new GameObject("HudChrome", typeof(RectTransform));
            _hudChrome.transform.SetParent(Theme.Canvas.transform, false);
            var hrt = _hudChrome.GetComponent<RectTransform>();
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            var chrome = _hudChrome.transform;
            var topLeft = new Vector2(0f, 1f);

            BuildPortraitPlate(chrome);

            // The title, centred along the top rail in the artwork's Roman serif:
            // the floor in blood red, the world it belongs to in candle gold.
            _hud = Theme.Label(chrome, "", 46, Theme.Player,
                new Vector2(0.5f, 1f), new Vector2(0, -56), new Vector2(1500, 76));
            if (Theme.MenuFont != null) _hud.font = Theme.MenuFont;
            _hud.supportRichText = true;
            _hud.raycastTarget = false;

            _stageText = Theme.Label(chrome, "", 27, new Color(0.80f, 0.75f, 0.70f, 0.85f),
                new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(900, 40));
            if (Theme.MenuFont != null) _stageText.font = Theme.MenuFont;
            _stageText.raycastTarget = false;

            _toast = Theme.Label(Theme.Canvas.transform, "", 60, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1400, 100));

            // The blood bar under the portrait — the bat-flight meter, wearing the
            // artwork's ornate end-capped frame instead of a plain black box.
            var barBg = new GameObject("FlyBarBg", typeof(RectTransform));
            barBg.transform.SetParent(chrome, false);
            var bgi = barBg.AddComponent<Image>();
            bgi.color = new Color(0.05f, 0.02f, 0.03f, 0.85f);
            bgi.raycastTarget = false;
            var brt = bgi.rectTransform;
            brt.anchorMin = brt.anchorMax = topLeft; brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(181, -74); brt.sizeDelta = new Vector2(250, 34);
            var fill = new GameObject("FlyBarFill", typeof(RectTransform));
            fill.transform.SetParent(barBg.transform, false);
            _flyBar = fill.AddComponent<Image>();
            _flyBar.color = new Color(0.62f, 0.06f, 0.10f, 1f);   // the painted blood red
            _flyBar.raycastTarget = false;
            var frt = _flyBar.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.anchoredPosition = new Vector2(3, 0);
            frt.sizeDelta = new Vector2(BarFillW, 26);
            // The bar's own frame, drawn over the fill so the fill reads as liquid
            // inside it. bar_frame is the dropped-in art; the ornate border stands in
            // if it ever goes missing.
            var barArt = Assets.Sprite("bar_frame");
            var barFrame = new GameObject("FlyBarFrame", typeof(RectTransform)).AddComponent<Image>();
            barFrame.transform.SetParent(barBg.transform, false);
            barFrame.sprite = barArt != null ? barArt : Gothic.Frame;
            barFrame.type = Image.Type.Sliced;
            barFrame.pixelsPerUnitMultiplier = barArt != null ? 0.35f : Gothic.PlateFrameMul;
            barFrame.raycastTarget = false;
            var bfr = barFrame.rectTransform;
            bfr.anchorMin = Vector2.zero; bfr.anchorMax = Vector2.one;
            bfr.offsetMin = new Vector2(-6, -6); bfr.offsetMax = new Vector2(6, 6);

            // PAUSE, top-right, as the artwork draws it: a framed stone plate rather
            // than a floating letterform. (It exists because pausing used to be
            // Escape-ONLY — on a phone that meant no way back to the main menu, and
            // Blood Moon, which restarts instead of ending, became a one-way trap.)
            _pauseBtn = Gothic.Button(chrome, "II", new Vector2(-82, -78), new Vector2(84, 84),
                TogglePause, false, 40, new Vector2(1f, 1f));
            _pauseBtn.gameObject.SetActive(false);   // shown only while actually playing

            // Mute sits under it, smaller — the artwork keeps the top rail clean, but
            // a player who wants silence right now must not have to dig for it.
            _muteBtn = Gothic.Button(chrome, Audio.Muted ? "✕" : "♪", new Vector2(-82, -176),
                new Vector2(68, 68), ToggleMute, false, 30, new Vector2(1f, 1f));

            // Blood-shard counter, left of the pause plate. The diamond icon is a
            // 45°-rotated Image — the pixel font has no ♦ glyph.
            _shardHud = new GameObject("Shards", typeof(RectTransform));
            _shardHud.transform.SetParent(chrome, false);
            var srt = _shardHud.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-140, -50); srt.sizeDelta = new Vector2(240, 60);
            var dia = new GameObject("Dia", typeof(RectTransform)).AddComponent<Image>();
            dia.transform.SetParent(_shardHud.transform, false);
            dia.color = Theme.Coin; dia.raycastTarget = false;
            var drt = dia.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 0.5f); drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(24, 0); drt.sizeDelta = new Vector2(22, 22);
            drt.localRotation = Quaternion.Euler(0, 0, 45f);
            _shardText = Theme.Label(_shardHud.transform, Currency.Balance.ToString(), 38, Theme.Coin,
                new Vector2(0f, 0.5f), new Vector2(140, 0), new Vector2(190, 56), TextAnchor.MiddleLeft);
            if (Theme.MenuFont != null) _shardText.font = Theme.MenuFont;
            _shardText.raycastTarget = false;
            _shardHud.SetActive(false);                    // shown alongside the title during play
            Currency.OnEarned += OnShardsEarned;

            BuildHudFooter(chrome);
            _hudChrome.SetActive(false);

            BuildTouchControls();
            BuildTrollButtons();
            BuildRotatePanel();
        }

        const float BarFillW = 244f;   // the blood bar's full-length fill

        // The character plate in the top-left corner of the artwork: an ornate frame
        // with the Heir's portrait in it, and the lives row beside it.
        void BuildPortraitPlate(Transform chrome)
        {
            var topLeft = new Vector2(0f, 1f);
            var plate = new GameObject("Portrait", typeof(RectTransform)).AddComponent<Image>();
            plate.transform.SetParent(chrome, false);
            plate.color = new Color(0.055f, 0.028f, 0.045f, 0.95f);
            plate.raycastTarget = false;
            var prt = plate.rectTransform;
            prt.anchorMin = prt.anchorMax = topLeft; prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(22, -10); prt.sizeDelta = new Vector2(150, 140);

            // THE PORTRAIT. This used to blow the player's own 64px idle sprite up
            // to 132px and sit it in a plain box — so the top-left corner of the
            // screen was just a second, blurrier copy of the character you were
            // already looking at, which is exactly how it read to players. The
            // artwork puts a PAINTED portrait in a gold filigree frame there, so
            // that painted plate (cut from the reference) is what goes in now.
            var portrait = Assets.Sprite("ui/hud_portrait");
            if (portrait != null)
            {
                // The cut already includes its own frame, so the plate behind it
                // becomes invisible rather than drawing a second border around it.
                plate.color = new Color(0f, 0f, 0f, 0f);
                var img = new GameObject("Face", typeof(RectTransform)).AddComponent<Image>();
                img.transform.SetParent(plate.transform, false);
                img.sprite = portrait; img.preserveAspect = true; img.raycastTarget = false;
                var irt = img.rectTransform;
                irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
                irt.offsetMin = irt.offsetMax = Vector2.zero;
            }
            else
            {
                // Fallback: the old sprite-in-a-box, so a missing cut still leaves
                // a readable HUD rather than an empty corner.
                var frames = Assets.Grid("vamp_idle_sheet", 64, 3);
                Sprite face = (frames != null && frames.Length > 0) ? frames[0] : Assets.Sprite("vamp_idle");
                if (face != null)
                {
                    var img = new GameObject("Face", typeof(RectTransform)).AddComponent<Image>();
                    img.transform.SetParent(plate.transform, false);
                    img.sprite = face; img.preserveAspect = true; img.raycastTarget = false;
                    img.color = Skins.Shade(Skins.Current);
                    var irt = img.rectTransform;
                    irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f); irt.pivot = new Vector2(0.5f, 0.5f);
                    irt.anchoredPosition = Vector2.zero; irt.sizeDelta = new Vector2(132, 132);
                }
                Gothic.InnerFrame(plate.transform);
            }

            // The lives row. It only appears in the modes that HAVE lives — the
            // Castle lets you retry forever, and a row of hearts that never empties
            // would be the screen telling you a comfortable lie.
            _heartPips = new Image[Diff.MaxHearts];
            for (int i = 0; i < _heartPips.Length; i++)
            {
                var h = new GameObject("Heart", typeof(RectTransform)).AddComponent<Image>();
                h.transform.SetParent(chrome, false);
                h.sprite = Gothic.Heart; h.raycastTarget = false; h.preserveAspect = true;
                var rt = h.rectTransform;
                rt.anchorMin = rt.anchorMax = topLeft; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(205 + i * 60, -38);
                rt.sizeDelta = new Vector2(46, 46);
                h.gameObject.SetActive(false);
                _heartPips[i] = h;
            }
        }

        // The ornament along the bottom rail of the artwork: a stone bar with a skull
        // set at each end and a blood diamond at its heart. Purely decorative, and
        // deliberately narrow so it sits BETWEEN the two thumb clusters on a phone.
        void BuildHudFooter(Transform chrome)
        {
            var bottom = new Vector2(0.5f, 0f);
            var bar = new GameObject("HudFooter", typeof(RectTransform)).AddComponent<Image>();
            bar.transform.SetParent(chrome, false);
            bar.color = new Color(0.075f, 0.047f, 0.062f, 0.92f);
            bar.raycastTarget = false;
            var rt = bar.rectTransform;
            rt.anchorMin = rt.anchorMax = bottom; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, 48); rt.sizeDelta = new Vector2(560, 60);
            Gothic.InnerFrame(bar.transform);

            var gem = new GameObject("Gem", typeof(RectTransform)).AddComponent<Image>();
            gem.transform.SetParent(bar.transform, false);
            gem.sprite = Gothic.Diamond; gem.raycastTarget = false;
            var grt = gem.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f); grt.pivot = grt.anchorMin;
            grt.sizeDelta = new Vector2(34, 34);

            for (int s = -1; s <= 1; s += 2)
            {
                var sk = new GameObject("Skull", typeof(RectTransform)).AddComponent<Image>();
                sk.transform.SetParent(bar.transform, false);
                sk.sprite = Gothic.Skull; sk.raycastTarget = false; sk.preserveAspect = true;
                sk.color = new Color(0.62f, 0.58f, 0.54f, 0.85f);
                var skrt = sk.rectTransform;
                skrt.anchorMin = skrt.anchorMax = new Vector2(0.5f, 0.5f); skrt.pivot = skrt.anchorMin;
                skrt.anchoredPosition = new Vector2(s * 200, 0);
                skrt.sizeDelta = new Vector2(38, 38);
            }
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
        Button _pauseBtn;   // on-screen pause (phones have no Esc key)
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

        // Phones sit much smaller in the hand than the desktop test window the
        // layout was tuned in — APK testers reported the stick and JUMP as "too
        // small" to hit reliably. One multiplier scales every touch-control size
        // AND offset (offsets too, so the cluster spreads instead of overlapping);
        // desktop (Settings > ON-SCREEN PADS) keeps the compact layout.
        // ---- touch-control preferences (Settings > CONTROLS on a phone) --------
        // PAD SIZE: 0 small, 1 normal, 2 large. Multiplies the mobile base scale.
        static int PadSizePref => PlayerPrefs.GetInt("opt_pad_size", 1);
        static float TouchScale => (Application.isMobilePlatform ? 1.35f : 1f)
                                 * (PadSizePref == 0 ? 0.84f : PadSizePref == 2 ? 1.16f : 1f);
        // JOYSTICK: floating (appears under your thumb) or fixed to the corner.
        // Floating is the default — see TouchJoystick for why.
        static bool FloatingStick => PlayerPrefs.GetInt("opt_stick_float", 1) == 1;
        // LEFT-HANDED mirrors the whole layout: movement on the right, actions left.
        static bool LeftHanded => PlayerPrefs.GetInt("opt_lefty", 0) == 1;

        // On-screen buttons for phones (also work with the mouse). Hidden in menus.
        void BuildTouchControls()
        {
            _touchPanel = new GameObject("Touch", typeof(RectTransform));
            _touchPanel.transform.SetParent(Theme.Canvas.transform, false);
            var rt = _touchPanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            float k = TouchScale;
            // Left-handed flips which bottom corner each cluster lives in. Anchoring
            // to the opposite corner and negating every x offset mirrors the whole
            // layout without duplicating a single position.
            bool lh = LeftHanded;
            var moveAnchor = new Vector2(lh ? 1f : 0f, 0f);
            var actAnchor  = new Vector2(lh ? 0f : 1f, 0f);
            float ms = lh ? -1f : 1f;   // mirror movement offsets
            float acts = lh ? -1f : 1f; // ...and action offsets

            // Movement: either the two bare arrow glyphs (no circle, no background —
            // just the glyph) or the virtual joystick, whichever Settings > MOVE has
            // picked. Both are built; only one is ever shown, toggled every frame in
            // UpdateTouchLayout() like the ability buttons.
            // These are the RINGS CUT OUT OF THE REFERENCE PAINTING (art/ui/btn_*),
            // not glyphs approximating it — position and diameter below are the
            // painting's own measurements scaled from its 1568x1003 frame onto the
            // 1080-high UI canvas, so the finished screen matches the artwork
            // rather than resembling it. MakeArtButton falls back to the old bare
            // glyph automatically if a PNG is ever missing.
            _btnLeft  = MakeArtButton("btn_left",  "‹", -1, moveAnchor, new Vector2(213 * ms, 232) * k, 207f * k);
            _btnRight = MakeArtButton("btn_right", "›",  1, moveAnchor, new Vector2(398 * ms, 237) * k, 194f * k);
            // Base size drives the knob, the travel radius and the grab pad (see
            // TouchJoystick.Setup), so this one number is the whole stick's feel.
            // Trimmed 240 -> 205: with the stick now drawn under your thumb rather
            // than parked in the corner, it no longer needs to be a big target, and
            // a smaller ring covers less of the floor you're trying to read.
            _joystick = MakeJoystick(moveAnchor, new Vector2(250 * ms, 185) * k, 205f * k, lh);
            // …action cluster in the other corner. JUMP is always there; the rest are
            // shown contextually (bat in Blood Moon/Endless, dash if the skin grants
            // it, SHOOT only while holding a loaded gun) via UpdateTouchLayout(). JUMP
            // is a bare up-arrow (no circle, no background, no label) per the same
            // "just the arrow" styling as the movement glyphs.
            // Jump is the single most-mashed button in the game, and it had been
            // sized up twice for that (170 -> 200 -> 232). On a phone the mobile
            // 1.35x lands on top of that, and it finished up a third of the screen
            // high — a dinner plate over the level you're trying to read. Back to
            // 168: still the biggest control on the screen and still a comfortable
            // thumb target, without eating the playfield.
            // Pulled in and up from the painting's own (187, 178). The shipped
            // screen draws an ornate border frame around the play area that the
            // reference mockup doesn't have, and at the painting's exact numbers
            // all three rings sat underneath it with their bottoms cut off.
            MakeArtButton("btn_jump", "▲", 0, actAnchor, new Vector2(-190 * acts, 215) * k, 168f * k);
            _btnFly   = MakeArtButton("btn_bat", "", 3, actAnchor, new Vector2(-121 * acts, 415) * k, 160f * k);
            _btnDash  = MakeTouch("DASH",  4, actAnchor, new Vector2(-140 * acts, 350) * k, new Vector2(130, 130) * k, 0.24f);
            _btnShoot = MakeGunButton(actAnchor, new Vector2(-360 * acts, 310) * k, new Vector2(130, 130) * k);
            SyncMoveMode();
            _touchPanel.SetActive(false);
        }

        /// <summary>
        /// Tear the pads down and lay them out again — needed when a Settings change
        /// alters their SIZE or which corner they live in (a live toggle can't just
        /// flip a flag for those, the rects have to be rebuilt).
        /// </summary>
        void RebuildTouchControls()
        {
            // Deactivate before destroying: Destroy is deferred to end of frame, and a
            // still-live TouchButton would keep writing into TouchInput until then.
            if (_touchPanel != null) { _touchPanel.SetActive(false); Destroy(_touchPanel); }
            TouchInput.Clear();
            _btnFly = _btnDash = _btnShoot = _btnLeft = _btnRight = _joystick = null;
            BuildTouchControls();
            UpdateTouchLayout();
        }

        GameObject _btnFly, _btnDash, _btnShoot, _btnLeft, _btnRight, _joystick;

        // ==================== SABOTAGE (Versus troll buttons) ====================
        // A right-edge column of buttons that let racers troll each other, Drive
        // Ahead style. Each fires a Photon EvTroll at the rival; their client
        // applies the effect to THEIR OWN vampire. We deliberately only mess with
        // the victim's VISION and CONTROLS (never spawn a death-trap on their
        // track) so a troll can make them blow a jump — real stakes — but can
        // never turn a beatable track un-winnable. Shared per-button cooldown
        // keeps it spicy, not a strobe. Works with mouse AND touch (Unity Button).
        struct TrollDef { public string label; public Color col; public string sentMsg; public string hitMsg; }
        static readonly TrollDef[] Trolls =
        {
            new TrollDef { label = "SNUFF",  col = new Color(0.10f, 0.10f, 0.16f), sentMsg = "You snuffed their candles!",   hitMsg = "SNUFFED — the lights went out!" },
            new TrollDef { label = "CURSE",  col = new Color(0.42f, 0.12f, 0.52f), sentMsg = "You cursed their hands!",       hitMsg = "CURSED — your hands are flipped!" },
            new TrollDef { label = "QUAKE",  col = new Color(0.55f, 0.30f, 0.08f), sentMsg = "You shook their whole world!",  hitMsg = "QUAKE — the castle is shaking!" },
        };
        const float TrollCooldown = 5f;   // seconds between uses of each button
        GameObject _trollPanel;
        Button[] _trollBtns;
        Text[] _trollLabels;
        float[] _trollCd;
        GameObject _blackout;             // full-screen dim used by the SNUFF troll

        void BuildTrollButtons()
        {
            _trollPanel = new GameObject("Trolls", typeof(RectTransform));
            _trollPanel.transform.SetParent(Theme.Canvas.transform, false);
            var rt = _trollPanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            float k = TouchScale;
            _trollBtns = new Button[Trolls.Length];
            _trollLabels = new Text[Trolls.Length];
            _trollCd = new float[Trolls.Length];
            // Left edge, vertically centred — clear of the bottom-left movement pad
            // and the bottom-right action cluster (which the DASH button can reach).
            for (int i = 0; i < Trolls.Length; i++)
            {
                int idx = i;
                var size = new Vector2(150, 108) * k;
                var pos = new Vector2(95, 128 - i * 128) * k;   // left-centre column
                var btn = Theme.Button(_trollPanel.transform, Trolls[i].label, Trolls[i].col, Color.white, 30,
                    new Vector2(0f, 0.5f), pos, size, () => FireTroll(idx));
                _trollBtns[i] = btn;
                _trollLabels[i] = btn.GetComponentInChildren<Text>();
            }
            _trollPanel.SetActive(false);
        }

        // Local player pressed a sabotage button.
        void FireTroll(int type)
        {
            if (_mode != Mode.Versus || _state != State.Play || _raceOver) return;
            if (type < 0 || type >= Trolls.Length || _trollCd[type] > 0f) return;
            _trollCd[type] = TrollCooldown;
            Net.SendTroll(type);
            Audio.Play("click");
            RoomToast(Trolls[type].sentMsg);
        }

        // A rival trolled us — apply it to our own vampire (vision/controls only).
        void ReceiveTroll(int actor, int type)
        {
            if (_mode != Mode.Versus || _state != State.Play || _raceOver) return;
            if (type < 0 || type >= Trolls.Length) return;
            RoomToast(Trolls[type].hitMsg);
            switch (type)
            {
                case 0: StartCoroutine(TrollBlackout(1.6f)); break;                       // SNUFF
                case 1: if (_player != null) _player.SetReversed(1.9f); break;            // CURSE
                case 2: ShakeCam(0.55f, 1.3f); break;                                     // QUAKE
            }
        }

        // Full-screen dim that fades in fast and out slow — the SNUFF troll.
        IEnumerator TrollBlackout(float hold)
        {
            if (_blackout == null)
            {
                _blackout = new GameObject("Blackout", typeof(RectTransform));
                _blackout.transform.SetParent(Theme.Canvas.transform, false);
                var brt = _blackout.GetComponent<RectTransform>();
                brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
                brt.offsetMin = brt.offsetMax = Vector2.zero;
                var bi = _blackout.AddComponent<Image>();
                bi.color = new Color(0f, 0f, 0f, 0f);
                bi.raycastTarget = false;   // never eat the victim's own button/joystick input
            }
            var img = _blackout.GetComponent<Image>();
            _blackout.transform.SetAsLastSibling();
            _blackout.SetActive(true);
            // A hole of dim around nothing — near-total blackout with a faint edge
            // so it reads as "the candles went out," not "the game froze."
            for (float t = 0; t < 0.18f; t += Time.unscaledDeltaTime)
            { img.color = new Color(0f, 0f, 0f, Mathf.Lerp(0f, 0.92f, t / 0.18f)); yield return null; }
            img.color = new Color(0f, 0f, 0f, 0.92f);
            yield return new WaitForSecondsRealtime(hold);
            for (float t = 0; t < 0.5f; t += Time.unscaledDeltaTime)
            { img.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.92f, 0f, t / 0.5f)); yield return null; }
            _blackout.SetActive(false);
        }

        // Ticked from Update(): show the sabotage column only during a live race,
        // count down each button's cooldown, and grey a button while it's cooling.
        void UpdateTrollButtons()
        {
            // The scoreboard rides at the top for the whole race (independent of the
            // troll column, which hides once someone reaches the coffin).
            if (_versusHud != null)
                _versusHud.SetActive(_mode == Mode.Versus && _state == State.Play && Net.InRoom);

            if (_trollPanel == null) return;
            bool show = _mode == Mode.Versus && _state == State.Play && !_raceOver && Net.InRoom;
            if (_trollPanel.activeSelf != show) _trollPanel.SetActive(show);
            if (!show) return;
            for (int i = 0; i < _trollBtns.Length; i++)
            {
                if (_trollCd[i] > 0f) _trollCd[i] = Mathf.Max(0f, _trollCd[i] - Time.unscaledDeltaTime);
                bool ready = _trollCd[i] <= 0f;
                if (_trollBtns[i] != null) _trollBtns[i].interactable = ready;
                if (_trollLabels[i] != null)
                    _trollLabels[i].text = ready ? Trolls[i].label : Mathf.CeilToInt(_trollCd[i]).ToString();
            }
        }

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
            SyncMoveMode();
        }

        // Joystick is the DEFAULT movement mode (testers reached for a stick
        // first and took a while to find it in Settings); arrows are the opt-in.
        // Single source of truth for that default — it's read from three places.
        const int MoveModeDefault = 1;   // 1 = joystick, 0 = arrow pads
        static bool JoystickMode => PlayerPrefs.GetInt("opt_joystick", MoveModeDefault) == 1;

        // Settings > MOVE: JOYSTICK toggle picks arrows or the stick. Re-checked
        // every frame (cheap, change-guarded) so flipping it in Settings takes
        // effect the instant Play resumes, with no rebuild needed.
        void SyncMoveMode()
        {
            bool joystick = JoystickMode;
            SyncTouchButton(_btnLeft, !joystick);
            SyncTouchButton(_btnRight, !joystick);
            SyncTouchButton(_joystick, joystick);
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
            // Font tracks the pad size so the mobile scale-up grows the text too.
            int fontSize = Mathf.RoundToInt(((dir == -1 || dir == 1) ? 0.43f : (label.Length > 4 ? 0.15f : 0.18f)) * size.y);
            Theme.Label(go.transform, label, fontSize, new Color(1, 1, 1, 0.9f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return go;
        }

        /// <summary>
        /// A control button drawn with a piece of the reference painting — the
        /// ornate gold ring with its arrow (or bat) already inside it.
        ///
        /// The whole button is ONE sprite, which is the point: the rings in the
        /// artwork have hand-painted highlights, corner studs and an inner shadow
        /// that no amount of code-drawn circle-plus-glyph was ever going to match.
        /// The root rect stays square at the full diameter because that square is
        /// the finger hit zone TouchButton tests against.
        ///
        /// `size` is a DIAMETER, not a Vector2 — these are circles, and letting
        /// them be non-square was how the old pads ended up subtly oval.
        /// Falls back to the bare text glyph if the PNG isn't present, so a
        /// missing cut can never leave the player with an invisible control.
        /// </summary>
        GameObject MakeArtButton(string art, string fallbackGlyph, int dir,
                                 Vector2 anchor, Vector2 pos, float size)
        {
            var sprite = Assets.Sprite("ui/" + art);
            if (sprite == null)
                return MakeArrowGlyph(string.IsNullOrEmpty(fallbackGlyph) ? "▲" : fallbackGlyph,
                                      dir, anchor, pos, new Vector2(size, size));

            var go = new GameObject("Touch_" + art, typeof(RectTransform));
            go.transform.SetParent(_touchPanel.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            // Idles a little under full so the button never competes with the
            // vampire for attention; TouchButton lifts it on press.
            img.color = new Color(1f, 1f, 1f, 0.82f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(size, size);

            var tb = go.AddComponent<TouchButton>();
            tb.dir = dir;
            tb.SetFeedback(new Graphic[] { img });
            return go;
        }

        // A bare arrow/chevron glyph — no Image, no circle, no background — just
        // the character itself sitting over an invisible hit box. Kept as the
        // fallback for MakeArtButton when a painted cut is missing.
        GameObject MakeArrowGlyph(string glyph, int dir, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Touch_" + glyph, typeof(RectTransform));
            go.transform.SetParent(_touchPanel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var tb = go.AddComponent<TouchButton>();
            tb.dir = dir;
            // Glyph fills the button (font tracks size) and idles brighter than the
            // old 0.55 — on a sunlit phone screen the faint glyphs disappeared.
            var label = Theme.Label(go.transform, glyph, Mathf.RoundToInt(size.y * 0.5f),
                new Color(1, 1, 1, 0.72f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
            tb.SetFeedback(new Graphic[] { label });
            return go;
        }

        // A bare bat silhouette — no circle, no background, no "BAT" label — to
        // match the gun button and the arrow glyphs beside it. The root keeps the
        // full square size because that's the finger hit zone (TouchButton tests
        // against it); only the artwork is 2:1.
        GameObject MakeBatButton(Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Touch_BAT", typeof(RectTransform));
            go.transform.SetParent(_touchPanel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var tb = go.AddComponent<TouchButton>();
            tb.dir = 3;

            var icon = new GameObject("Glyph", typeof(RectTransform));
            icon.transform.SetParent(go.transform, false);
            var irt = (RectTransform)icon.transform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(size.x, size.x * 0.5f);   // the glyph is 2:1
            var img = icon.AddComponent<Image>();
            img.sprite = Theme.BatGlyph;
            img.color = new Color(1f, 1f, 1f, 0.45f);             // same idle alpha as the gun
            tb.SetFeedback(new Graphic[] { img });
            return go;
        }

        // A small pistol silhouette built from stacked rects (same technique as
        // the world-space gun on the player) instead of the plain circular pad —
        // reads as "a gun" rather than a generic button.
        GameObject MakeGunButton(Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Touch_GUN", typeof(RectTransform));
            go.transform.SetParent(_touchPanel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var tb = go.AddComponent<TouchButton>();
            tb.dir = 2;

            var col = new Color(1f, 1f, 1f, 0.45f);
            // Icon geometry was drawn for a 130-unit button; scale the parts with
            // the actual size so the mobile scale-up grows the artwork too.
            float g = size.x / 130f;
            var parts = new[]
            {
                GunIconPart(go.transform, "Body",   new Vector2(-6, -4) * g,  new Vector2(58, 34) * g, col),
                GunIconPart(go.transform, "Barrel", new Vector2(38, 6) * g,   new Vector2(56, 16) * g, col),
                GunIconPart(go.transform, "Grip",   new Vector2(-22, -28) * g,new Vector2(20, 34) * g, col),
            };
            tb.SetFeedback(parts);
            return go;
        }

        static Graphic GunIconPart(Transform parent, string name, Vector2 pos, Vector2 size, Color col)
        {
            var go = new GameObject("Part_" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = Theme.Square;
            img.color = col;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return img;
        }

        // A semi-transparent virtual joystick: a hollow ring base plus a solid knob
        // that snaps to the drag and back to centre on release.
        //
        // FLOATING (the default): the root is an invisible catch ZONE covering the
        // movement half of the screen, and the ring is a child that jumps under your
        // thumb on touch and disappears on release — so nothing is drawn over the
        // level until you're actually steering. FIXED parks the ring in the corner
        // the old way, for players who want a stick that's always in one place.
        GameObject MakeJoystick(Vector2 anchor, Vector2 pos, float baseSize, bool lefty)
        {
            bool floating = FloatingStick;
            var go = new GameObject("Touch_JOYSTICK", typeof(RectTransform));
            go.transform.SetParent(_touchPanel.transform, false);
            var rt = (RectTransform)go.transform;
            if (floating)
            {
                // The movement half, stopping short of the top so the HUD, the pause
                // button and the mute icons stay tappable.
                rt.anchorMin = new Vector2(lefty ? 0.52f : 0f, 0f);
                rt.anchorMax = new Vector2(lefty ? 1f : 0.48f, 0.80f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(baseSize, baseSize);
            }

            // The visible ring. In fixed mode it simply fills the root rect; in
            // floating mode it's moved to the touch point every press.
            var baseGo = new GameObject("Base", typeof(RectTransform));
            baseGo.transform.SetParent(go.transform, false);
            var baseImg = baseGo.AddComponent<Image>();
            // The painting's own gold ring with its middle punched out, so the stick
            // matches the arrows and the bat sitting next to it. It used to be a
            // plain procedural circle at 18% alpha — the one control you stare at
            // most was the only one that didn't look like the game. Carries more
            // alpha than the old ring because it's artwork now, not a hint line,
            // but the centre is clear so it never hides the vampire.
            var ringArt = Assets.Sprite("ui/ring_art");
            baseImg.sprite = ringArt != null ? ringArt : Theme.Ring;
            baseImg.color = new Color(1f, 1f, 1f, ringArt != null ? 0.5f : 0.18f);
            baseImg.raycastTarget = false;
            var brt = baseImg.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f); brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(baseSize, baseSize);

            var knobGo = new GameObject("Knob", typeof(RectTransform));
            knobGo.transform.SetParent(baseGo.transform, false);
            var knobImg = knobGo.AddComponent<Image>();
            knobImg.sprite = Theme.Circle;
            knobImg.color = new Color(1f, 1f, 1f, 0.3f);
            knobImg.raycastTarget = false;
            var knobRt = knobImg.rectTransform;
            knobRt.anchorMin = knobRt.anchorMax = new Vector2(0.5f, 0.5f); knobRt.pivot = new Vector2(0.5f, 0.5f);
            knobRt.anchoredPosition = Vector2.zero; knobRt.sizeDelta = new Vector2(baseSize * 0.45f, baseSize * 0.45f);

            var js = go.AddComponent<TouchJoystick>();
            js.Setup(brt, knobRt, new Graphic[] { baseImg, knobImg }, baseSize * 0.5f, floating);
            return go;
        }

        // ==================== MAIN MENU ====================
        void ShowMenu()
        {
            // Leaving a race: drop the room and the rival ghosts.
            if (_mode == Mode.Versus) { Net.Leave(); ClearGhosts(); _mode = Mode.Curated; }
            Memory.RunEndedCleanly();   // back at the menu = not a rage-quit
            Rumor.Disarm();
            _wipeArmed = false;          // leaving Settings always disarms the wipe
            _state = State.Menu;
            _onBack = BackFromMainMenu;  // top level: back arms "press again to quit"
            Time.timeScale = 1f;
            ApplyTheme(0);               // menu always shows the Castle mood
            Audio.Music("music", 0.3f);
            if (_hudChrome != null) _hudChrome.SetActive(false);
            if (_shardHud != null) _shardHud.SetActive(false);
            if (_touchPanel != null) { _touchPanel.SetActive(false); TouchInput.Clear(); }
            _rig.SetFrame(-1.5f, CamY, NormalCamSize);   // reset in case we left a zoomed-out boss arena

            // Destroy any existing menu panel FIRST — otherwise the previous
            // screen (e.g. the level-select map) leaks and stays on top, since
            // reassigning _menuPanel orphaned it. This was the "map stays" bug.
            if (_menuPanel != null) Destroy(_menuPanel);

            _menuPanel = Overlay(Crimson.Night, out var root);

            // NIGHTLY TITHE granted up front so the balance shown already includes
            // today's payout.
            int tithe = Currency.GrantDailyIfDue();

            // THE LANDING, as the art pass draws it. The painted menu (menu_bg_v2) is a
            // picture of the OLD layout — a CONTINUE plate, no record button, shards
            // instead of blood — so it can't wear this one; the screen is built in code
            // from the same red-and-black vocabulary as the map and settings.
            BuildLanding(root, tithe);
            TrackMenuShown();
        }

        // Which face the record plate is showing. Tapping it flips between your best
        // depth and the "no depth recorded" taunt — the design's own interaction, and
        // the reason a first-time player sees a challenge instead of three dashes.
        bool _recordFlipped;

        /// <summary>
        /// THE LANDING. Black night, one red moon, the title, and three ways down:
        /// tonight's Blood Moon, the Castle you're partway through, and Endless.
        /// Your deepest run is its own plate on the left — off the Endless button,
        /// where it used to hide — and everything transient lives along the bottom.
        /// </summary>
        void BuildLanding(Transform root, int tithe)
        {
            Crimson.Backdrop(root, 340f, -40f, true, 3);

            // ---- Title ------------------------------------------------------------
            var top = new Vector2(0.5f, 1f);
            var shadow = Theme.Label(root, Theme.Title, 104, Theme.Hex("480610"), top,
                new Vector2(6, -168), new Vector2(1700, 200));
            shadow.font = Theme.TitleFont; shadow.raycastTarget = false;
            var title = Theme.Label(root, Theme.Title, 104, Theme.Hex("F2ECE1"), top,
                new Vector2(0, -162), new Vector2(1700, 200));
            title.font = Theme.TitleFont; title.raycastTarget = false;
            if (!Options.ReducedMotion) StartCoroutine(Pulse(title.transform));
            Crimson.Line(root, "DESCEND  ·  DISTRUST  ·  DIE", 26, Crimson.Gold,
                new Vector2(0, -262), new Vector2(1200, 44), TextAnchor.MiddleCenter, top)
                .fontStyle = FontStyle.Bold;

            // ---- The three ways down -----------------------------------------------
            // Blood Moon and the Castle sit side by side with their own state written
            // underneath them; Endless is the wide crimson plate below, because it's
            // the run you come back for.
            var mid = new Vector2(0.5f, 0.5f);
            Crimson.Btn(root, "BLOOD MOON", mid, new Vector2(-250, 128), new Vector2(430, 96),
                StartDaily, false, 30, $"TONIGHT  ·  {DailyLen} FLOORS", true);
            Crimson.Btn(root, "THE CASTLE", mid, new Vector2(250, 128), new Vector2(430, 96),
                ShowLevelSelect, false, 30,
                $"FLOOR {Mathf.Min(CastleUnlocked + 1, Levels.Count)} / {Levels.Count}", true);
            Crimson.Btn(root, "ENDLESS NIGHTS", mid, new Vector2(0, -10), new Vector2(880, 92),
                StartEndless, true, 34);

            // ---- YOUR DEEPEST NIGHT -------------------------------------------------
            int deepest = PlayerPrefs.GetInt("best_endless_distance", 0);
            bool has = deepest > 0 && !_recordFlipped;
            // A wide bar UNDER the three ways down rather than a card floating off the
            // left edge: on a narrow screen the centred buttons and a left-anchored
            // plate walk straight into each other.
            var plate = Crimson.Panel_(root, mid, new Vector2(-330, -168),
                                       new Vector2(640, 190), Theme.Hex("1B0C16"), Crimson.Rail);
            Crimson.Line(plate.transform, "YOUR DEEPEST NIGHT", 19, Crimson.Mute,
                new Vector2(28, -30), new Vector2(400, 30), TextAnchor.MiddleLeft, new Vector2(0f, 1f))
                .fontStyle = FontStyle.Bold;
            if (has)
            {
                Crimson.Line(plate.transform, $"{deepest:N0}m", 50, Crimson.GoldLit,
                    new Vector2(28, -86), new Vector2(300, 62), TextAnchor.MiddleLeft, new Vector2(0f, 1f))
                    .fontStyle = FontStyle.Bold;
                Crimson.Line(plate.transform, DepthTitle(deepest), 20, Crimson.BloodHot,
                    new Vector2(28, -134), new Vector2(340, 30), TextAnchor.MiddleLeft, new Vector2(0f, 1f))
                    .fontStyle = FontStyle.Bold;
                Crimson.Btn(plate.transform, "SHARE", new Vector2(1f, 0.5f), new Vector2(-110, -14),
                    new Vector2(180, 62), () => StartCoroutine(ShareCard.CaptureAndShare(
                        "trust_issues_depth.png",
                        $"I got {deepest}m down in TRUST ISSUES before the castle took me.")),
                    false, 22);
            }
            else
            {
                Crimson.Line(plate.transform, "— — —", 38, Crimson.Dead,
                    new Vector2(28, -84), new Vector2(200, 52), TextAnchor.MiddleLeft, new Vector2(0f, 1f));
                Crimson.Line(plate.transform, "no depth recorded. the castle isn't impressed.",
                    20, Crimson.Mute, new Vector2(28, -122), new Vector2(360, 56),
                    TextAnchor.UpperLeft, new Vector2(0f, 1f));
                Crimson.Btn(plate.transform, "SET ONE", new Vector2(1f, 0.5f), new Vector2(-110, -14),
                    new Vector2(190, 62), StartEndless, false, 21);
            }

            // ---- What the castle remembers about you --------------------------------
            // The greeting is shown HERE, not in the notice stack, so it reads as the
            // castle talking to you rather than as one more banner along the bottom.
            string greet = Memory.MenuGreeting();
            Crimson.Line(root, greet ?? "\"You came back. The stairs remember your weight.\"",
                24, Theme.Hex("9B848C"), new Vector2(30, -168), new Vector2(600, 172),
                TextAnchor.MiddleLeft, mid);

            // ---- The footer ----------------------------------------------------------
            // The design's three buttons, plus the two it dropped: Multiplayer and the
            // Leaderboard are working features, and a feature you can't reach is a
            // feature you don't have.
            var bot = new Vector2(0.5f, 0f);
            var fdim = new Vector2(300, 68);
            Crimson.Btn(root, "WARDROBE", bot, new Vector2(-620, 200), fdim, ShowWardrobe, false, 22);
            Crimson.Btn(root, $"BESTIARY {Codex.KnownCount()}/{Codex.Total}", bot, new Vector2(-310, 200), fdim,
                ShowCodex, false, 20);
            Crimson.Btn(root, "MULTIPLAYER", bot, new Vector2(0, 200), fdim, ShowVersusLobby, false, 21);
            Crimson.Btn(root, "LEADERBOARD", bot, new Vector2(310, 200), fdim,
                () => ShowLeaderboard("daily"), false, 20);
            Crimson.Btn(root, "SETTINGS", bot, new Vector2(620, 200), fdim, ShowSettings, false, 22);

            // Balance top-left, streak top-right, exactly as the design frames them.
            Crimson.BloodCounter(root, new Vector2(0f, 1f), new Vector2(190, -56), 30);
            if (Meta.Streak > 0 && Meta.StreakAlive)
            {
                var s = Crimson.Panel_(root, new Vector2(1f, 1f), new Vector2(-170, -56),
                                       new Vector2(280, 58), Crimson.Panel, Crimson.Rail);
                Crimson.Line(s.transform, $"STREAK {Meta.Streak} NIGHTS", 21, Crimson.Gold,
                    Vector2.zero, new Vector2(260, 40)).fontStyle = FontStyle.Bold;
            }

            // The transient stack (tithe, curse, greeting) keeps the bottom rail.
            BuildMenuNotices(root, tithe);

            // The record plate flips between its two faces on tap — but only once
            // there's something to flip to, so a real record never hides itself by
            // accident on a mis-tap.
            if (deepest > 0)
            {
                var edge = plate.GetComponent<Image>();
                edge.raycastTarget = true;          // the plate itself has to catch the tap
                var flip = plate.gameObject.AddComponent<Button>();
                flip.targetGraphic = edge;
                flip.onClick.AddListener(() => { _recordFlipped = !_recordFlipped; ShowMenu(); });
            }
        }

        // A title for how deep you've been. Local, honest, and no server needed —
        // the design's "RANK 412" would need a live leaderboard behind it.
        static string DepthTitle(int metres) =>
            metres >= 2000 ? "THE CASTLE'S EQUAL"
          : metres >= 1200 ? "ABYSS WALKER"
          : metres >= 700 ? "DEEP DWELLER"
          : metres >= 300 ? "STAIR-TREADER"
          : "TOURIST";



        // ---- The notice strip --------------------------------------------------
        // Everything transient — tonight's tithe, a live streak, a curse someone laid
        // on you. Stacked UPWARDS from just above the footer row so no combination of
        // them can collide with a button or with each other. The castle's greeting is
        // NOT here: the landing gives it its own line beside the moon.
        void BuildMenuNotices(Transform root, int tithe)
        {
            var notices = new System.Collections.Generic.List<(string text, int size, Color col)>();
            if (tithe > 0)
                notices.Add(($"NIGHTLY TITHE:  +{tithe} BLOOD", 26, Crimson.Gold));
            if (Meta.Streak > 0 && Meta.StreakAlive)
                notices.Add(($"BLOOD MOON STREAK: {Meta.Streak} NIGHTS — keep it alive", 24, Crimson.Gold));
            if (Curse.Pending != null)
                notices.Add(($"{Curse.Pending.nick} CURSED YOU from floor {Curse.Pending.floor + 1} of {Curse.Pending.mode}. Break it.",
                             24, Crimson.BloodLit));
            if (Memory.MenuGreeting() != null && !_greetTracked)
            {
                _greetTracked = true;
                Analytics.Track("haunt_greeting", new System.Collections.Generic.Dictionary<string, object>());
            }
            for (int i = 0; i < notices.Count; i++)
                Crimson.Line(root, notices[i].text, notices[i].size, notices[i].col,
                    new Vector2(0, 296 + (notices.Count - 1 - i) * 40), new Vector2(1400, 38),
                    TextAnchor.MiddleCenter, new Vector2(0.5f, 0f));
        }



        // ==================== SKINNED SUB-SCREENS ====================
        // Each paints its exact artwork and lays invisible tap-zones over the buttons
        // the art draws. Coordinates are fractions from the top-left of that screen's
        // mockup. Painted labels/values stay as-is (the picture supplies the look);
        // the zones just make them work.

        // WARDROBE — 10 character cards in a 5x2 grid, same order as Skins.All.
        //
        // The painting bakes EVERY card's caption into the picture: THE HEIR is drawn
        // as EQUIPPED and the other nine as LOCKED with their hints, forever. So the
        // screen used to lie — buy a skin, wear it, come back, and the art still said
        // locked. Each card's caption block is covered and re-drawn live: real name,
        // real lock state, the ability you actually get, and a gold ring on whichever
        // skin you're really wearing. Rects are measured off the 1600x900 mockup.
        // Measured off the 1600x912 wardrobe mockup. Two 4-wide grids on the right:
        // the CHARACTER grid up top, the STYLE grid below. The big portrait panel on
        // the left is painted and stays painted — only THE PRINCE has full-body art,
        // so there is nothing to swap in for the other nine yet.
        static readonly float[] WardrobeColX = { 0.4745f, 0.5855f, 0.6975f, 0.8085f };
        static readonly float[] WardrobeRowTop = { 0.253f, 0.423f, 0.633f, 0.763f };
        static readonly float[] WardrobeRowBot = { 0.415f, 0.585f, 0.755f, 0.885f };
        static readonly float[] WardrobeNameY  = { 0.372f, 0.542f, 0.722f, 0.852f };
        // The painted card interiors, sampled off the artwork so a caption chip
        // disappears into its own card instead of reading as a black bar.
        static readonly Color[] WardrobeInterior =
        {
            new Color(0.052f, 0.010f, 0.010f), new Color(0.039f, 0.028f, 0.022f),
            new Color(0.035f, 0.029f, 0.026f), new Color(0.073f, 0.052f, 0.039f),
            new Color(0.071f, 0.047f, 0.035f), new Color(0.030f, 0.023f, 0.021f),
            new Color(0.072f, 0.058f, 0.053f), new Color(0.028f, 0.021f, 0.017f),
            new Color(0.048f, 0.032f, 0.024f), new Color(0.075f, 0.039f, 0.028f),
        };

        void BuildSkinnedWardrobe(Transform root, GameObject panel)
        {
            // The painting labels its tiles (THE PRINCE, THE HEIR, THE SPECTRE…) but
            // those are pictures of names, and the roster they'd have to match is a
            // different list in a different order. Rather than let a tile called
            // "THE SPECTRE" equip Golden Cursed, every caption is chipped out and
            // rewritten from the real skin list — the same rule every other skinned
            // screen in this game follows.
            const float HalfW = 0.0520f;    // caption chip half-width, inside the painted card
            for (int i = 0; i < Skins.All.Count && i < 16; i++)
            {
                var s = Skins.All[i];
                int r = i / 4, col = i % 4;
                if (r >= WardrobeRowTop.Length) break;
                float cx = WardrobeColX[col];
                float top = WardrobeRowTop[r], bot = WardrobeRowBot[r], ny = WardrobeNameY[r];
                bool unlocked = Skins.IsUnlocked(s);
                bool equipped = Skins.CurrentId == s.id;
                string sid = s.id; var sdef = s;

                // Hide the painted caption, then write the true one over it.
                Skin.Chip(root, cx - HalfW, ny, cx + HalfW, ny + 0.050f, WardrobeInterior[i]);

                var nameT = Skin.LiveText(root, s.name.ToUpperInvariant(), cx - HalfW, ny + 0.001f, cx + HalfW, ny + 0.024f,
                    22, unlocked ? Gothic.Bone : new Color(0.62f, 0.55f, 0.52f, 0.75f));
                if (Theme.MenuFont != null) nameT.font = Theme.MenuFont;
                Skin.Fit(nameT, 22, 11);

                var stateT = Skin.LiveText(root,
                    unlocked ? (equipped ? "EQUIPPED" : "TAP TO WEAR") : "LOCKED",
                    cx - HalfW, ny + 0.026f, cx + HalfW, ny + 0.048f,
                    17, equipped ? Theme.Coin
                       : unlocked ? new Color(0.70f, 0.63f, 0.60f, 0.85f)
                                  : new Color(0.78f, 0.24f, 0.26f, 0.95f));
                if (Theme.MenuFont != null) stateT.font = Theme.MenuFont;
                Skin.Fit(stateT, 17, 10);

                // A candle-gold ring on the skin you're actually wearing (the artwork
                // paints its glow permanently around the first card).
                if (equipped)
                {
                    var ring = Skin.Slot(root, "EquipRing", cx - 0.0545f, top, cx + 0.0545f, bot)
                        .gameObject.AddComponent<Image>();
                    ring.sprite = Gothic.Frame; ring.type = Image.Type.Sliced;
                    ring.pixelsPerUnitMultiplier = Gothic.RingFrameMul;
                    ring.color = Theme.Coin; ring.raycastTarget = false;
                }

                Skin.Zone(root, cx - 0.0545f, top, cx + 0.0545f, bot,
                    unlocked ? (System.Action)(() => { Skins.Equip(sid); Destroy(panel); ShowWardrobe(); })
                             : (System.Action)(() => ShowHint(sdef.unlockHint)), "skin_" + sid);
            }
            Skin.Zone(root, 0.43f, 0.905f, 0.57f, 0.975f, () => { Destroy(panel); ShowMenu(); }, "back");
        }

        // LEADERBOARD — the artwork paints a sample table (DraculaX, NightStalker) and
        // one fixed heading. Both were pure decoration: whoever actually topped the
        // board never appeared. The painted rows and heading are covered and the real
        // ones drawn in their place, keeping the frame, headers and BACK plate.
        // Rects measured off the 1600x900 mockup.
        static readonly Color BoardInterior = new Color(0.030f, 0.022f, 0.028f, 1f);
        void BuildSkinnedLeaderboard(Transform root, GameObject panel, string mode)
        {
            string heading = mode == "daily" ? "BLOOD MOON — TONIGHT (FEWEST DEATHS)"
                           : mode == "endless" ? "ENDLESS NIGHT — LONGEST DISTANCE"
                                               : "THE CASTLE — FEWEST DEATHS";
            // Painted heading + blurb out, live ones in.
            Skin.Chip(root, 0.24f, 0.205f, 0.76f, 0.240f, BoardInterior);
            var head = Skin.LiveText(root, heading, 0.20f, 0.205f, 0.80f, 0.240f, 27, Theme.Player);
            if (Theme.MenuFont != null) head.font = Theme.MenuFont;
            Skin.Fit(head, 27, 15);

            Skin.Chip(root, 0.20f, 0.255f, 0.80f, 0.335f, BoardInterior);
            var status = Skin.LiveText(root, "summoning the dead…", 0.18f, 0.255f, 0.82f, 0.335f, 26, Gothic.Faint);
            if (Theme.MenuFont != null) status.font = Theme.MenuFont;

            // Headers: the art paints four columns (RANK/SOUL/FLOOR/DEATHS) but a board
            // only ever ranks by ONE number, so the last two are replaced by the single
            // column this mode is actually sorted on.
            Skin.Chip(root, 0.540f, 0.393f, 0.832f, 0.436f, BoardInterior);
            BoardCell(root, mode == "endless" ? "METRES" : "DEATHS", 0.600f, 0.395f, 0.820f, 0.432f, Gothic.Faint);

            // The painted table holds three sample rows; eight live ones fit the same
            // band once they're set at the real line height.
            const float RowsTop = 0.437f, RowsBot = 0.792f;
            const int MaxRows = 8;
            Skin.Chip(root, 0.178f, RowsTop, 0.832f, RowsBot, BoardInterior);

            Leaderboard.Fetch(mode, mode == "daily" ? "today" : "all", entries =>
            {
                if (status == null) return;
                int rank = Leaderboard.MyRank(entries);
                // The line above the table does the motivating: it names where the
                // player stands and who is directly in front of them, which is the
                // only part of a leaderboard most people actually read.
                status.text = rank == 0
                    ? "You are unranked. † marks the castle's own dead — beat one."
                    : $"YOU ARE #{rank} OF {entries.Count}" +
                      (rank > 1 ? $"   ·   next: {entries[rank - 2].nick} on " +
                                  $"{entries[rank - 2].value}{(mode == "endless" ? " m" : " deaths")}"
                                : "   ·   nothing above you");

                // Window the rows around the player so they always see themselves
                // even once they're deep in the table (a board that scrolls your own
                // score off the screen is a board you stop opening).
                int first = 0;
                if (rank > 0 && entries.Count > MaxRows)
                    first = Mathf.Clamp(rank - 1 - MaxRows / 2, 0, entries.Count - MaxRows);

                float rowH = (RowsBot - RowsTop) / MaxRows;
                for (int i = 0; i < MaxRows && first + i < entries.Count; i++)
                {
                    var e = entries[first + i];
                    int place = first + i + 1;
                    float t0 = RowsTop + i * rowH, t1 = t0 + rowH;
                    // Your own row in blood red, the leader in candle gold, and the
                    // house dead dimmed under a dagger so nobody mistakes a castle
                    // character for another player.
                    var col = e.you ? Theme.Player : place == 1 ? Theme.Coin
                            : e.ghost ? Gothic.Faint : Gothic.Bone;
                    string name = (e.ghost ? "† " : "") + e.nick + (e.you ? "   (you)" : "");
                    BoardCell(root, $"{place}", 0.185f, t0, 0.255f, t1, col);
                    BoardCell(root, name, 0.285f, t0, 0.560f, t1, col, TextAnchor.MiddleLeft);
                    BoardCell(root, e.value + (mode == "endless" ? " m" : ""),
                        0.600f, t0, 0.820f, t1, col);
                }
            });

            Skin.Zone(root, 0.40f, 0.855f, 0.60f, 0.955f, () => { Destroy(panel); ShowMenu(); }, "back");
        }

        // One live cell in the painted leaderboard table.
        void BoardCell(Transform root, string text, float x0, float top0, float x1, float top1,
            Color col, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = Skin.LiveText(root, text, x0, top0, x1, top1, 30, col, align: align);
            if (Theme.MenuFont != null) t.font = Theme.MenuFont;
            Skin.Fit(t, 30, 14);
        }

        // Funnel: how many sessions actually reach an interactive menu (the gap
        // between session_start and this is load/boot bounce).
        void TrackMenuShown()
        {
            if (_menuShownTracked) return;
            _menuShownTracked = true;
            Analytics.Track("menu_shown", new System.Collections.Generic.Dictionary<string, object>
            {
                { "returning", !Memory.IsFirstSession },
                { "has_curse", Curse.Pending != null },
            });
        }
        bool _greetTracked;      // one analytics ping per session, not per menu visit
        bool _menuShownTracked;  // same contract for the funnel's menu_shown
        bool _firstInputTracked; // ...and for the first gameplay keypress


        // ==================== TRAP CODEX (BESTIARY) ====================
        // A persistent book of every trap. Each page is revealed the first time the
        // trap gets you (or you trigger it), teaching how to read/beat it next time.
        void ShowCodex()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.96f), out var root);
            _onBack = ShowMenu;

            // HYBRID: the artwork supplies the frame, gargoyles, candles, title AND all
            // nineteen ornate card frames — one per trap, which is exactly the book's
            // length. So nothing is covered wholesale any more: each card's interior is
            // chipped and its real page drawn inside its own painted frame. (The painted
            // lore can't just be left alone — it has typos, and a locked trap must not
            // give its answer away before it has killed you.)
            bool skinned = Skin.Background(root, "bestiary_bg") != null;
            if (!skinned)
            {
                Gothic.Backdrop(root);
                Gothic.Heading(root, "VAMPIRE'S BESTIARY", null);
            }

            // Live tally over the painted "9 / 19". Anchored in ARTWORK fractions,
            // not canvas units: the canvas is shorter than the painting on a tall
            // phone, so a canvas-positioned caption slides off its painted spot.
            string tally = $"{Codex.KnownCount()} / {Codex.Total} CATALOGUED  —  DIE TO A NEW TRAP TO REVEAL ITS PAGE";
            if (skinned)
            {
                Skin.Chip(root, 0.255f, 0.1420f, 0.775f, 0.1640f, new Color(0.045f, 0.033f, 0.026f));
                var tl = Skin.LiveText(root, tally, 0.20f, 0.1420f, 0.83f, 0.1640f, 25, Gothic.Faint);
                if (Theme.MenuFont != null) tl.font = Theme.MenuFont;
                Skin.Fit(tl, 25, 12);

                // The painting supplies all nineteen ornate card frames, so the pages
                // are drawn INSIDE them rather than over the top: each card's interior
                // is chipped out (its painted lore has typos, and a locked trap must
                // not show its answer) and the real icon, name and lore go in its place.
                for (int i = 0; i < Codex.Entries.Length; i++)
                    CodexPage(root, Codex.Entries[i], CodexColX[i % 5], CodexRowTop[i / 5]);

                // The artwork leaves the last cell of the bottom row empty — that's
                // where the PREVIEW toggle lives.
                bool prevOn = Codex.PreviewAll;
                Skin.Chip(root, CodexColX[4] - CodexHalfW, CodexRowTop[3],
                          CodexColX[4] + CodexHalfW, CodexRowTop[3] + CodexCardH, CodexInterior);
                var pv = Skin.Slot(root, "Preview", CodexColX[4] - CodexHalfW, CodexRowTop[3],
                                   CodexColX[4] + CodexHalfW, CodexRowTop[3] + CodexCardH);
                var pvImg = pv.gameObject.AddComponent<Image>();
                pvImg.color = prevOn ? new Color(0.30f, 0.035f, 0.055f, 1f) : Gothic.Plate;
                var pvBtn = pv.gameObject.AddComponent<Button>();
                pvBtn.targetGraphic = pvImg;
                pvBtn.onClick.AddListener(() => { Codex.PreviewAll = !Codex.PreviewAll; ShowCodex(); });
                Gothic.InnerFrame(pv);
                var pvT = Skin.LiveText(root, prevOn ? "PREVIEW\nON" : "PREVIEW\nOFF",
                    CodexColX[4] - CodexHalfW, CodexRowTop[3] + 0.055f,
                    CodexColX[4] + CodexHalfW, CodexRowTop[3] + 0.110f, 24, Theme.Coin);
                if (Theme.MenuFont != null) pvT.font = Theme.MenuFont;

                Skin.Zone(root, 0.41f, 0.910f, 0.59f, 0.970f, ShowMenu, "back");
            }
            else
            {
                Gothic.Line(root, tally, 25, Gothic.Faint, new Vector2(0, 344), new Vector2(1600, 40));
                var entries = Codex.Entries;
                const int cols = 5;
                var card = new Vector2(300, 156);
                float stepX = 316f, stepY = 166f, startX = -((cols - 1) * stepX) / 2f, startY = 224f;
                for (int i = 0; i < entries.Length; i++)
                    BuildCodexCard(root, entries[i],
                        new Vector2(startX + (i % cols) * stepX, startY - (i / cols) * stepY), card);
                bool prev = Codex.PreviewAll;
                Gothic.Button(root, prev ? "PREVIEW: ON" : "PREVIEW: OFF",
                    new Vector2(startX + 4 * stepX, startY - 3 * stepY), card,
                    () => { Codex.PreviewAll = !Codex.PreviewAll; ShowCodex(); }, prev, 26);
                Gothic.Back(root, ShowMenu);
            }
        }

        // The Bestiary artwork's card grid, measured off the 1600x953 painting: five
        // columns, four rows, and the interior of each painted frame.
        static readonly float[] CodexColX   = { 0.20813f, 0.35625f, 0.50438f, 0.65250f, 0.80063f };
        static readonly float[] CodexRowTop = { 0.18363f, 0.38510f, 0.57712f, 0.76495f };
        const float CodexHalfW = 0.0650f, CodexCardH = 0.16475f;
        static readonly Color CodexInterior = new Color(0.040f, 0.035f, 0.026f, 1f);

        /// <summary>
        /// One page drawn inside its painted frame: chip the interior, then set the
        /// trap's own illustration, its name and its lore — or the undiscovered
        /// silhouette if it hasn't killed you yet.
        /// </summary>
        void CodexPage(Transform root, TrapType t, float cx, float top)
        {
            bool known = Codex.IsKnown(t);
            Skin.Chip(root, cx - CodexHalfW, top, cx + CodexHalfW, top + CodexCardH, CodexInterior);

            if (known)
            {
                var sp = Assets.Sprite(Codex.Art(t));
                if (sp != null)
                {
                    var rt = Skin.Slot(root, "Art", cx - 0.045f, top + 0.006f, cx + 0.045f, top + 0.070f);
                    var img = rt.gameObject.AddComponent<Image>();
                    img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
                }
            }
            else
            {
                var q = Skin.LiveText(root, "?", cx - 0.045f, top + 0.006f, cx + 0.045f, top + 0.070f,
                    44, new Color(0.62f, 0.55f, 0.52f, 0.30f));
                if (Theme.MenuFont != null) q.font = Theme.MenuFont;
            }

            var title = Skin.LiveText(root, known ? Codex.Title(t).ToUpperInvariant() : "UNDISCOVERED",
                cx - 0.062f, top + 0.070f, cx + 0.062f, top + 0.092f,
                24, known ? Theme.Coin : new Color(0.62f, 0.55f, 0.52f, 0.55f));
            if (Theme.MenuFont != null) title.font = Theme.MenuFont;
            Skin.Fit(title, 24, 12);

            var lore = Skin.LiveText(root, known ? Codex.Lore(t) : "die to this trap to reveal its page",
                cx - 0.060f, top + 0.096f, cx + 0.060f, top + 0.158f,
                17, known ? new Color(0.82f, 0.76f, 0.70f, 0.90f) : new Color(0.62f, 0.55f, 0.52f, 0.38f));
            if (Theme.MenuFont != null) lore.font = Theme.MenuFont;
            lore.verticalOverflow = VerticalWrapMode.Truncate;
            Skin.Fit(lore, 17, 10);
        }

        // One bestiary page, in the artwork's language: an ornate framed plate with the
        // trap's icon, its name in the menu serif, and the lore beneath. A revealed page
        // wears a faint blood tint; an undiscovered one stays cold and near-black.
        void BuildCodexCard(Transform root, TrapType t, Vector2 pos, Vector2 size)
        {
            bool known = Codex.IsKnown(t);
            var c = new Vector2(0.5f, 0.5f);
            var plate = Gothic.PlateAt(root, pos, size,
                known ? new Color(0.105f, 0.055f, 0.075f, 1f) : new Color(0.050f, 0.038f, 0.062f, 1f));
            var ct = plate.transform;

            Sprite sp = known ? Assets.Sprite(Codex.Art(t)) : null;
            if (sp != null)
            {
                var img = new GameObject("Art", typeof(RectTransform)).AddComponent<Image>();
                img.transform.SetParent(ct, false);
                img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
                var irt = img.rectTransform;   // painted illustration — never tinted
                irt.anchorMin = irt.anchorMax = c; irt.pivot = c;
                irt.anchoredPosition = new Vector2(0, 44); irt.sizeDelta = new Vector2(48, 48);
            }
            else
            {
                Theme.Label(ct, "?", 42, new Color(0.62f, 0.55f, 0.52f, 0.30f), c,
                    new Vector2(0, 44), new Vector2(80, 64)).raycastTarget = false;
            }

            Gothic.Line(ct, known ? Codex.Title(t).ToUpperInvariant() : "UNDISCOVERED", 19,
                known ? Gothic.Bone : new Color(0.62f, 0.55f, 0.52f, 0.55f),
                new Vector2(0, 10), new Vector2(size.x - 28, 24));

            // Best-fit keeps the longest lore lines inside the plate instead of spilling
            // over its painted border, however wide the running font turns out to be.
            var lore = Gothic.Line(ct, known ? Codex.Lore(t) : "die to this trap to reveal its page", 14,
                known ? new Color(0.80f, 0.72f, 0.68f, 0.82f) : new Color(0.62f, 0.55f, 0.52f, 0.38f),
                new Vector2(0, -40), new Vector2(size.x - 38, 68));
            lore.verticalOverflow = VerticalWrapMode.Truncate;
            Skin.Fit(lore, 14, 9);
        }






        // ==================== SETTINGS ====================
        // Which settings tab is open. Kept between rebuilds — flipping a switch
        // redraws the screen and you should land back where you were.
        int _settingsTab;

        void ShowSettings()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(Crimson.Night, out var root);
            _onBack = ShowMenu;

            BuildSettings(root);
        }

        /// <summary>
        /// SETTINGS, in four banner tabs: the noise, the feel of the controls, how cruel
        /// the castle should be, and the legal shelf. The difficulty picker used to be a
        /// chip on the main menu that cycled on tap and told you nothing about what it
        /// changed; here it's three cards that say what each one does.
        /// </summary>
        void BuildSettings(Transform root)
        {
            // The phone release has one supported movement scheme. Clear preferences
            // from older builds that exposed arrows, pads and replay-ghost switches.
            PlayerPrefs.SetInt("opt_joystick", 1);
            PlayerPrefs.SetInt("opt_touch", 1);
            PlayerPrefs.SetInt("opt_replay_ghost", 0);
            PlayerPrefs.Save();

            Crimson.Backdrop(root, 460f, 240f, false, 0);

            var top = new Vector2(0f, 1f);
            var mark = Crimson.Img(root, "Mark", Gothic.Diamond, Color.white);
            Crimson.Place(mark, top, new Vector2(70, -62), Vector2.one * 22f);
            Crimson.Line(root, "SETTINGS", 40, Crimson.Bone, new Vector2(104, -62), new Vector2(500, 56),
                TextAnchor.MiddleLeft, top).fontStyle = FontStyle.Bold;
            Crimson.BloodCounter(root, new Vector2(1f, 1f), new Vector2(-190, -62), 26);

            string[] tabs = { "AUDIO", "CONTROLS", "DIFFICULTY", "DATA & LEGAL" };
            _settingsTab = Mathf.Clamp(_settingsTab, 0, tabs.Length - 1);
            Crimson.Tabs(root, tabs, _settingsTab, i => { _settingsTab = i; ShowSettings(); },
                         new Vector2(0.5f, 1f), new Vector2(0, -160), 1560f, 66f);

            // The body panel every tab draws into.
            var body = Crimson.Panel_(root, new Vector2(0.5f, 0.5f), new Vector2(0, -30),
                                      new Vector2(1560, 640), Theme.Hex("180A12"), Crimson.Rail);

            switch (_settingsTab)
            {
                case 0: BuildAudioTab(body.transform); break;
                case 1: BuildControlsTab(body.transform); break;
                case 2: BuildDifficultyTab(body.transform); break;
                default: BuildLegalTab(body.transform); break;
            }

            Crimson.Btn(root, "‹  BACK", new Vector2(0.5f, 0f), new Vector2(0, 74),
                        new Vector2(360, 76), ShowMenu, true, 28);
        }

        // ---- AUDIO: the noise on the left, the feel on the right -----------------
        void BuildAudioTab(Transform body)
        {
            var tl = new Vector2(0f, 1f);
            Crimson.Line(body, "THE NOISE", 24, Crimson.Gold, new Vector2(360, -50),
                new Vector2(600, 34), TextAnchor.MiddleLeft, tl).fontStyle = FontStyle.Bold;

            Crimson.BloodSlider(body, tl, new Vector2(360, -140), 620f, "MUSIC",
                "organ, distant, never cheerful", () => Audio.MusicVol, v => Audio.MusicVol = v);
            Crimson.BloodSlider(body, tl, new Vector2(360, -290), 620f, "SCREAMS & STEEL",
                "the part that tells you what hit you", () => Audio.SfxVol, v => Audio.SfxVol = v);
            Crimson.BloodSlider(body, tl, new Vector2(360, -440), 620f, "THE CASTLE'S VOICE",
                "it only speaks when something matters", () => Voice.Volume, v => Voice.Volume = v);

            Crimson.Line(body, "THE FEEL", 24, Crimson.Gold, new Vector2(-420, -50),
                new Vector2(600, 34), TextAnchor.MiddleLeft, new Vector2(1f, 1f)).fontStyle = FontStyle.Bold;

            var tr = new Vector2(1f, 1f);
            Crimson.Toggle(body, tr, new Vector2(-330, -120), 560f, "SCREEN SHAKE",
                () => Options.Shake, v => Options.Shake = v);
            Crimson.Toggle(body, tr, new Vector2(-330, -196), 560f, "BLOOD SPATTER",
                () => Options.Spatter, v => Options.Spatter = v);
            Crimson.Toggle(body, tr, new Vector2(-330, -272), 560f, "RUMBLE",
                () => Options.Haptics, v => Options.Haptics = v);
            Crimson.Toggle(body, tr, new Vector2(-330, -348), 560f, "REDUCED MOTION",
                () => Options.ReducedMotion, v => Options.ReducedMotion = v);

            Crimson.Line(body, "Music by Kevin MacLeod (incompetech.com), licensed CC BY 4.0. " +
                               "Full attribution lives under DATA & LEGAL, where it belongs.",
                19, Theme.Hex("7E6C74"), new Vector2(-330, -450), new Vector2(540, 100),
                TextAnchor.UpperLeft, tr);
        }

        // ---- CONTROLS: how the stick behaves, and which hand it belongs to -------
        void BuildControlsTab(Transform body)
        {
            var tl = new Vector2(0f, 1f);
            Crimson.Line(body, "TOUCH", 24, Crimson.Gold, new Vector2(360, -50),
                new Vector2(600, 34), TextAnchor.MiddleLeft, tl).fontStyle = FontStyle.Bold;

            Crimson.Card(body, tl, new Vector2(240, -190), new Vector2(360, 190),
                "FLOATING STICK", "thumb lands anywhere, the stick comes to you",
                Options.FloatingStick, () => { Options.FloatingStick = true; ApplyControlChange(); });
            Crimson.Card(body, tl, new Vector2(640, -190), new Vector2(360, 190),
                "FIXED STICK", "always bottom-left, muscle memory",
                !Options.FloatingStick, () => { Options.FloatingStick = false; ApplyControlChange(); });

            Crimson.Toggle(body, tl, new Vector2(440, -350), 760f, "LEFT-HANDED MIRROR",
                () => Options.LeftHanded, v => { Options.LeftHanded = v; ApplyControlChange(); });

            Crimson.Line(body, "Movement is the joystick. The arrows and D-pads were cut — " +
                               "one scheme, tuned properly, beats four that nearly work.",
                20, Theme.Hex("7E6C74"), new Vector2(80, -440), new Vector2(720, 90),
                TextAnchor.UpperLeft, tl);

            // A still of where the controls actually land, so the choice above is a
            // picture and not a paragraph.
            var tr = new Vector2(1f, 1f);
            var preview = Crimson.Panel_(body, tr, new Vector2(-330, -280), new Vector2(560, 440),
                                         Theme.Hex("0A0410"), Crimson.Iron);
            Crimson.Line(preview.transform, "LIVE PREVIEW", 19, Crimson.Rail, new Vector2(0, -26),
                new Vector2(400, 30), TextAnchor.MiddleCenter, new Vector2(0.5f, 1f));
            bool lefty = Options.LeftHanded;
            var stick = Crimson.Img(preview, "Stick", Crimson.Ring, Crimson.BloodHot);
            Crimson.Place(stick, new Vector2(lefty ? 0.78f : 0.22f, 0.22f), Vector2.zero, Vector2.one * 120f);
            var jump = Crimson.Img(preview, "Jump", Crimson.Ring, Crimson.Gold);
            Crimson.Place(jump, new Vector2(lefty ? 0.22f : 0.78f, 0.22f), Vector2.zero, Vector2.one * 100f);
            var hero = Assets.Sprite("vamp_idle");
            if (hero != null)
            {
                var h = Crimson.Img(preview, "Hero", hero, Color.white);
                h.preserveAspect = true;
                Crimson.Place(h, new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(110, 150));
            }
        }

        // A control setting changed shape or corner — the pads have to be rebuilt,
        // not just re-flagged (see RebuildTouchControls).
        void ApplyControlChange()
        {
            if (_touchPanel != null) RebuildTouchControls();
            ShowSettings();
        }

        // ---- DIFFICULTY: three cards that say what they change ------------------
        void BuildDifficultyTab(Transform body)
        {
            var tl = new Vector2(0f, 1f);
            Crimson.Line(body, "HOW CRUEL SHOULD THE CASTLE BE", 24, Crimson.Gold,
                new Vector2(70, -50), new Vector2(900, 34), TextAnchor.MiddleLeft, tl)
                .fontStyle = FontStyle.Bold;

            string[] names = { "MORTAL", "CURSED", "BLOODLET" };
            string[] notes =
            {
                "Eight hearts, gentler bosses, and no sunrise chasing you down the hall.",
                "As intended. Five hearts, the traps lie, and the castle learns from your deaths.",
                "Three hearts, one-shot bosses, and every trap the castle knows about you.",
            };
            for (int i = 0; i < 3; i++)
            {
                int d = i;
                Crimson.Card(body, tl, new Vector2(320 + i * 480f, -240), new Vector2(440, 280),
                    names[i], notes[i], (int)Diff.Current == i,
                    () => { Diff.Current = (Difficulty)d; ShowSettings(); });
            }

            string[] flavour =
            {
                "\"Mercy. How disappointing.\"",
                "\"Good. You want it to mean something.\"",
                "\"You will not survive this. Begin.\"",
            };
            Crimson.Line(body, flavour[(int)Diff.Current], 26, Crimson.Body,
                new Vector2(0, 90), new Vector2(1300, 60), TextAnchor.MiddleCenter, new Vector2(0.5f, 0f));
        }

        // ---- DATA & LEGAL --------------------------------------------------------
        void BuildLegalTab(Transform body)
        {
            var tl = new Vector2(0f, 1f);
            Crimson.Line(body, "DATA & LEGAL", 24, Crimson.Gold, new Vector2(70, -50),
                new Vector2(600, 34), TextAnchor.MiddleLeft, tl).fontStyle = FontStyle.Bold;

            Crimson.Btn(body, "PRIVACY POLICY", tl, new Vector2(410, -140), new Vector2(660, 92),
                () => ShowLegalDocument("PRIVACY POLICY", "legal/privacy"), false, 26);
            Crimson.Btn(body, "TERMS OF USE", tl, new Vector2(1120, -140), new Vector2(660, 92),
                () => ShowLegalDocument("TERMS OF USE", "legal/terms"), false, 26);
            Crimson.Btn(body, "MUSIC CREDITS (CC-BY)", tl, new Vector2(410, -250), new Vector2(660, 92),
                () => ShowLegalDocument("MUSIC CREDITS", "legal/music"), false, 26);

            // WIPE SAVE, behind a confirm. The castle forgetting you is the one
            // irreversible button on the screen, so it can't be a single tap.
            var warn = Crimson.Panel_(body, new Vector2(0.5f, 0f), new Vector2(0, 150),
                                      new Vector2(1380, 140), Theme.Hex("16050C"), Crimson.BloodDeep);
            Crimson.Line(warn.transform,
                "Erase every floor, every death, every drop of blood. The castle forgets you.",
                23, Crimson.Body, new Vector2(40, 0), new Vector2(880, 90),
                TextAnchor.MiddleLeft, new Vector2(0f, 0.5f));
            Crimson.Btn(warn.transform, _wipeArmed ? "TAP AGAIN TO ERASE" : "WIPE SAVE",
                new Vector2(1f, 0.5f), new Vector2(-190, 0), new Vector2(330, 76),
                () =>
                {
                    if (!_wipeArmed) { _wipeArmed = true; ShowSettings(); return; }
                    PlayerPrefs.DeleteAll();
                    PlayerPrefs.Save();
                    _wipeArmed = false;
                    _mapSel = -1;
                    ShowMenu();
                }, _wipeArmed, 22);
        }

        // First tap arms WIPE SAVE, second one does it. Reset whenever settings closes.
        bool _wipeArmed;

        void ShowLegalDocument(string title, string resourceName)
        {
            Audio.Play("click");
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(0.035f, 0.008f, 0.022f, 1f), out var root);
            Gothic.Backdrop(root);
            _onBack = ShowSettings;
            Gothic.Heading(root, title, "READ INSIDE THE GAME - NO BROWSER REQUIRED");

            var viewportGo = new GameObject("LegalViewport", typeof(RectTransform));
            viewportGo.transform.SetParent(root, false);
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 0.5f);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = new Vector2(0, 35);
            viewport.sizeDelta = new Vector2(1320, 650);
            var viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = new Color(0.018f, 0.009f, 0.016f, 0.96f);
            viewportGo.AddComponent<RectMask2D>();
            Gothic.InnerFrame(viewportGo.transform);

            var contentGo = new GameObject("LegalText", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;

            var asset = Resources.Load<TextAsset>(resourceName);
            string body = asset != null ? asset.text :
                "This legal document could not be loaded. Please reinstall the game.";
            var legal = contentGo.AddComponent<Text>();
            legal.font = Theme.MenuFont != null ? Theme.MenuFont : Theme.Font;
            legal.fontSize = 25;
            legal.color = new Color(0.91f, 0.87f, 0.80f, 1f);
            legal.alignment = TextAnchor.UpperLeft;
            legal.horizontalOverflow = HorizontalWrapMode.Wrap;
            legal.verticalOverflow = VerticalWrapMode.Overflow;
            legal.lineSpacing = 1.22f;
            legal.supportRichText = false;
            legal.text = body;
            var legalRt = legal.rectTransform;
            legalRt.anchorMin = new Vector2(0, 1);
            legalRt.anchorMax = new Vector2(1, 1);
            legalRt.pivot = new Vector2(0.5f, 1);
            legalRt.offsetMin = new Vector2(48, 0);
            legalRt.offsetMax = new Vector2(-48, 0);
            Canvas.ForceUpdateCanvases();
            content.sizeDelta = new Vector2(0, Mathf.Max(viewport.rect.height, legal.preferredHeight + 80));
            legalRt.sizeDelta = new Vector2(legalRt.sizeDelta.x, content.sizeDelta.y - 60);

            var scroll = viewportGo.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            Gothic.Back(root, ShowSettings);
        }



        // Short, darkly-funny premise shown before a new game.
        void ShowStory()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.88f), out var root);
            _onBack = ShowMenu;

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
        // Floors per castle tier — matches WorldOf()'s 10-floor worlds.
        const int FloorsPerWorld = 10;

        /// <summary>
        /// THE CASTLE map — one continuous climb you drag with your thumb.
        ///
        /// This replaced a four-tab grid: ten seals a page, four pages, no way to
        /// see the run as a whole. Tabs are a filing cabinet, and the castle is
        /// supposed to be a PLACE. Now all 40 floors are a single winding road
        /// that you scroll — floor 1 at the bottom, floor 40 at the top — so the
        /// distance you've climbed is a physical length you drag past, and the
        /// dark stretch above you is visibly how far there is left to go.
        ///
        /// Three things the map has to say, borrowed from what makes Level
        /// Devil's map worth scrolling back through:
        ///   • WHERE YOU ARE — the live floor wears a ring nothing else has.
        ///   • WHAT IT COST — every floor carries its lifetime death count, so
        ///     the map is a record of the fight, not a list of buttons.
        ///   • WHAT'S COMING — bosses sit bigger on the road, milestone floors
        ///     are marked, and locked floors are visible but dead.
        /// </summary>
        // THE ROAD DOWN — which floor's plate is showing. Kept between rebuilds so
        // tapping a floor doesn't lose your place.
        int _mapSel = -1;

        void ShowLevelSelect()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(Crimson.Night, out var root);
            _onBack = ShowMenu;

            // Red and black only. The painted 40-seal grid can't be the map any more
            // (it's a picture of a list, and this is a road), so the screen is built
            // from the night itself: black earth, one red moon, the castle ridge.
            Crimson.Backdrop(root, 520f, 150f, true, 3);

            int unlocked = CastleUnlocked;
            int count = Levels.Count;
            if (_mapSel < 0 || _mapSel >= count) _mapSel = Mathf.Min(unlocked, count - 1);

            // THE NEMESIS: the floor that has killed you most wears the gold crown.
            // A tie goes to the shallower floor — the first wall you hit is the one
            // that actually stopped you.
            int nemesis = -1, worst = 2;
            for (int i = 0; i < count; i++)
                if (FloorDeaths(i) > worst) { worst = FloorDeaths(i); nemesis = i; }

            // ---- The scroll viewport --------------------------------------------
            var viewGo = new GameObject("MapViewport", typeof(RectTransform));
            viewGo.transform.SetParent(root, false);
            var view = viewGo.GetComponent<RectTransform>();
            view.anchorMin = view.anchorMax = new Vector2(0.5f, 0.5f);
            view.pivot = new Vector2(0.5f, 0.5f);
            view.anchoredPosition = new Vector2(0, 10);
            view.sizeDelta = new Vector2(1800, 840);
            var viewImg = viewGo.AddComponent<Image>();
            viewImg.color = new Color(0, 0, 0, 0.001f);   // invisible, but raycastable so it catches the drag
            viewGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("MapRoad", typeof(RectTransform));
            contentGo.transform.SetParent(view, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0.5f, 0f);
            content.anchorMax = new Vector2(0.5f, 0f);
            content.pivot = new Vector2(0.5f, 0f);

            // ---- Node placement: a road through the grounds ----------------------
            // The wave is what stops 40 evenly-spaced discs reading as a spreadsheet:
            // the eye follows a curve, and the switchback gives each floor its own
            // spot on the wall rather than a row and a column. The whole road leans
            // RIGHT of centre so the selection plate can sit over the bottom-left of
            // the screen without ever covering a floor.
            const float RowSpacing = 196f;   // vertical distance between floors
            const float Swing = 250f;        // how far the road leans off its centre
            const float RoadX = 320f;        // ...and where that centre is
            const float BottomPad = 150f, TopPad = 210f;
            float roadH = BottomPad + (count - 1) * RowSpacing + TopPad;
            content.sizeDelta = new Vector2(1800, roadH);

            var pos = new Vector2[count];
            for (int i = 0; i < count; i++)
                pos[i] = new Vector2(RoadX + Mathf.Sin(i * 0.62f) * Swing, BottomPad + i * RowSpacing);

            // ---- The road itself --------------------------------------------------
            // Dried blood on black earth: a wide dark bed, a lighter track laid over
            // it, and iron fence posts down the side. Drawn from short segments
            // between floors, so the road bends with the switchback instead of being
            // a dotted line pretending to be one.
            for (int i = 0; i < count - 1; i++)
            {
                bool walked = i < unlocked;
                var a = pos[i]; var b = pos[i + 1];
                int steps = 9;
                for (int d = 0; d <= steps; d++)
                {
                    var p = Vector2.Lerp(a, b, d / (float)steps);
                    // The bed — always there, always nearly black.
                    var bed = Crimson.Img(content, "Bed", Theme.Disc, Theme.Hex("12070E"));
                    var brt = bed.rectTransform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
                    brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.anchoredPosition = p;
                    brt.sizeDelta = new Vector2(46, 46);
                    // The track — dried blood where you've walked, near-nothing ahead.
                    var track = Crimson.Img(content, "Track", Theme.Disc,
                        walked ? Theme.Hex("5A1522") : new Color(0.17f, 0.07f, 0.10f, 0.55f));
                    var trt = track.rectTransform;
                    trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
                    trt.pivot = new Vector2(0.5f, 0.5f);
                    trt.anchoredPosition = p;
                    trt.sizeDelta = new Vector2(30, 30);
                }
                // One iron post per stretch, off to the side of the road.
                var post = Crimson.Img(content, "Post", null, Crimson.Iron);
                var prt = post.rectTransform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = Vector2.Lerp(a, b, 0.5f) + new Vector2(i % 2 == 0 ? -74f : 74f, 0f);
                prt.sizeDelta = new Vector2(4, 40);
            }

            // ---- World banners: the road passes through four parts of the castle --
            for (int wI = 0; wI < Mathf.CeilToInt(count / (float)FloorsPerWorld); wI++)
            {
                int firstFloor = wI * FloorsPerWorld;
                if (firstFloor >= count) break;
                // Sat ON the road, between the world's gate and its first floor. Out at
                // the left margin it collided with the selection plate.
                var banner = Crimson.Line(content,
                    ThemeNames[Mathf.Clamp(wI, 0, ThemeNames.Length - 1)], 28,
                    unlocked >= firstFloor ? Crimson.Gold : Crimson.Dead,
                    new Vector2(RoadX + 300f, pos[firstFloor].y - 40f), new Vector2(400, 44),
                    TextAnchor.MiddleLeft, new Vector2(0.5f, 0f));
                banner.fontStyle = FontStyle.Bold;
            }

            // ---- The floors ------------------------------------------------------
            RectTransform hereNode = null;
            for (int i = 0; i < count; i++)
            {
                int idx = i;                       // captured by the click handler
                bool locked = idx > unlocked;
                bool cleared = idx < unlocked;
                bool here = idx == unlocked;
                // Every tenth floor is an EVENT: 20/30/40 are the bosses, 10 is the
                // exam. Both are built as gates so they read from across the screen —
                // but the caption tells the truth about which one it is.
                bool boss = BossTierForFloor(idx) > 0;
                bool gate = (idx + 1) % FloorsPerWorld == 0;
                string state = locked ? "lock" : here ? "now" : "done";

                var node = Crimson.Medal(content, pos[i], idx + 1, state, gate,
                    idx == nemesis, idx == _mapSel, () => { _mapSel = idx; ShowLevelSelect(); });
                if (here) hereNode = node;

                // The caption under the floor — what it is, or what it cost.
                int fd = FloorDeaths(idx);
                string cap = locked ? "SEALED"
                           : here ? "YOU ARE HERE"
                           : gate ? (boss ? "BOSS GATE" : "THE EXAM")
                           : fd > 0 ? (fd == 1 ? "1 DEATH" : fd + " DEATHS")
                           : "CLEARED";
                Crimson.Line(node, cap, 19,
                    here ? Crimson.BloodLit : gate ? Crimson.Gold : locked ? Crimson.Dead : Crimson.Rail,
                    new Vector2(0, -(gate ? 106f : 74f)), new Vector2(240, 30)).fontStyle = FontStyle.Bold;
            }

            // ---- Wire the scroll -------------------------------------------------
            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.09f;
            scroll.scrollSensitivity = 46f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;

            // Open on the floor you're actually playing, not at floor 1 — after
            // floor 25 the live edge is a long way up an 8000-pixel road.
            Canvas.ForceUpdateCanvases();
            if (hereNode != null && roadH > view.rect.height)
            {
                float target = Mathf.Clamp01(
                    (hereNode.anchoredPosition.y - view.rect.height * 0.42f) /
                    (roadH - view.rect.height));
                scroll.verticalNormalizedPosition = target;
            }

            // ---- Header: the title, and what the climb has cost so far -------------
            var top = new Vector2(0f, 1f);
            var mark = Crimson.Img(root, "Mark", Gothic.Diamond, Color.white);
            Crimson.Place(mark, top, new Vector2(56, -58), Vector2.one * 22f);
            var title = Crimson.Line(root, "THE CASTLE", 44, Crimson.Bone,
                new Vector2(88, -58), new Vector2(520, 60), TextAnchor.MiddleLeft, top);
            title.fontStyle = FontStyle.Bold;

            int totalDeaths = 0;
            for (int i = 0; i < count; i++) totalDeaths += FloorDeaths(i);
            Crimson.Line(root,
                $"FLOOR {Mathf.Min(unlocked + 1, count)} OF {count}  ·  {totalDeaths} " +
                (totalDeaths == 1 ? "DEATH" : "DEATHS"),
                22, Crimson.Mute, new Vector2(88, -104), new Vector2(700, 36), TextAnchor.MiddleLeft, top);

            // ---- World tabs: jump the road to a part of the castle ------------------
            // Not a filter — the road is one continuous climb. Tapping a world scrolls
            // there, and a world you haven't reached is dead type, so the tabs double
            // as a progress bar for the whole descent.
            for (int wI = 0; wI < 4 && wI * FloorsPerWorld < count; wI++)
            {
                int first = wI * FloorsPerWorld;
                bool reached = unlocked >= first;
                System.Action jump = null;
                if (reached) jump = () => { _mapSel = first; ShowLevelSelect(); };
                Crimson.Btn(root, ThemeNames[Mathf.Clamp(wI, 0, ThemeNames.Length - 1)],
                    new Vector2(1f, 1f), new Vector2(-820 + wI * 200f, -66), new Vector2(190, 54),
                    jump, false, 20, null, false, reached);
            }

            // ---- The selection plate ------------------------------------------------
            // Tap a floor and this is what the castle says about it. It sits over the
            // bottom-left of the screen, which the road deliberately leaves empty.
            int sel = _mapSel;
            bool selLocked = sel > unlocked, selHere = sel == unlocked;
            bool selBoss = BossTierForFloor(sel) > 0;
            bool selGate = (sel + 1) % FloorsPerWorld == 0;
            var plate = Crimson.Panel_(root, new Vector2(0f, 0f), new Vector2(410, 330),
                                       new Vector2(740, 330), Crimson.Plate, Crimson.BloodDeep);

            var kindMark = Crimson.Img(plate, "Kind", Gothic.Diamond, Color.white);
            Crimson.Place(kindMark, new Vector2(0f, 1f), new Vector2(34, -34), Vector2.one * 18f);
            Crimson.Line(plate.transform,
                selLocked ? "SEALED FLOOR" : selHere ? "CURRENT FLOOR"
                          : selGate ? (selBoss ? "BOSS GATE" : "THE EXAM") : "CLEARED",
                20, Crimson.Mute, new Vector2(60, -34), new Vector2(520, 32),
                TextAnchor.MiddleLeft, new Vector2(0f, 1f)).fontStyle = FontStyle.Bold;

            Crimson.Line(plate.transform, (sel + 1).ToString(), 46, Crimson.Bone,
                new Vector2(38, -88), new Vector2(90, 60), TextAnchor.MiddleLeft, new Vector2(0f, 1f))
                .fontStyle = FontStyle.Bold;
            Crimson.Line(plate.transform, Floors.Name(sel).ToUpperInvariant(), 28, Theme.Hex("E9D6C4"),
                new Vector2(112, -88), new Vector2(660, 56), TextAnchor.MiddleLeft, new Vector2(0f, 1f))
                .fontStyle = FontStyle.Bold;

            Crimson.Line(plate.transform, Floors.Rule(sel, !selLocked), 23, Crimson.Body,
                new Vector2(38, -168), new Vector2(740, 80), TextAnchor.UpperLeft, new Vector2(0f, 1f));

            int selDeaths = FloorDeaths(sel);
            float selBest = FloorBest(sel);
            string stat = selLocked
                ? $"CLEAR FLOOR {sel} TO BREAK THE SEAL"
                : selBest > 0f
                    ? $"{selDeaths} DEATHS  ·  BEST {Mathf.FloorToInt(selBest / 60f)}:{Mathf.FloorToInt(selBest % 60f):00}"
                    : selDeaths > 0 ? $"{selDeaths} DEATHS  ·  NEVER CLEARED" : "NEVER ATTEMPTED";
            Crimson.Line(plate.transform, stat, 20, Crimson.Mute,
                new Vector2(38, -262), new Vector2(480, 34), TextAnchor.MiddleLeft, new Vector2(0f, 1f));

            int enter = sel;
            System.Action descend = null;
            if (!selLocked) descend = () => StartGame(enter);
            Crimson.Btn(plate.transform, selLocked ? "SEALED" : selHere ? "DESCEND" : "REVISIT",
                new Vector2(1f, 0f), new Vector2(-120, 52), new Vector2(212, 62),
                descend, selHere, 22, null, false, !selLocked);

            Crimson.BloodCounter(root, new Vector2(1f, 0f), new Vector2(-210, 74), 28);
            // BACK lives in the top-right, out of the road's way and clear of the
            // selection plate that owns the bottom-left corner.
            Crimson.Btn(root, "‹  BACK", new Vector2(1f, 1f), new Vector2(-140, -150), new Vector2(220, 60),
                        ShowMenu, false, 22);
        }

        // ==================== MAP EDITOR ====================
        // Build a map, prove it's beatable, share the code, race a friend on it.
        // The whole point of the feature: a trap you made is an invitation, and an
        // invitation is the only thing that brings a new player in by itself.
        CustomMap _editMap;
        int _editBrush = (int)CustomMap.Cell.Spike;   // what tapping a cell paints
        Text _editStatus;

        void ShowMapEditor()
        {
            Audio.Play("click");
            _state = State.Menu;
            _editMap ??= CustomMap.Load();
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(0.05f, 0.02f, 0.06f, 0.93f), out var root);
            _onBack = ShowMenu;
            var c = new Vector2(0.5f, 0.5f);

            Theme.Label(root, "BUILD A TRAP", 66, Theme.Player,
                c, new Vector2(0, 452), new Vector2(1400, 100)).font = Theme.TitleFont;
            Theme.Label(root, "tap a tool, then tap the track. share the code and watch them suffer.",
                24, new Color(1, 1, 1, 0.5f), c, new Vector2(0, 396), new Vector2(1500, 40));

            // ---- palette -----------------------------------------------------
            float px = -760f;
            for (int i = 0; i < CustomMap.CellKinds; i++)
            {
                int kind = i;
                bool sel = _editBrush == i;
                var col = EditCellColor((CustomMap.Cell)i);
                Theme.Button(root, CustomMap.CellNames[i],
                    sel ? col : new Color(col.r * 0.45f, col.g * 0.45f, col.b * 0.45f, 0.95f),
                    sel ? Theme.Ink : Color.white, 19,
                    c, new Vector2(px + i * 169f, 316), new Vector2(160, 56),
                    () => { _editBrush = kind; ShowMapEditor(); });
            }

            // ---- the track ---------------------------------------------------
            // Two rows of 12 so each cell stays big enough for a thumb.
            const int perRow = 12;
            float cellW = 122f, cellH = 96f;
            for (int i = 0; i < CustomMap.Cells; i++)
            {
                int ix = i;
                int r = i / perRow, q = i % perRow;
                var cell = _editMap.cells[i];
                bool locked = i < CustomMap.SpawnClear || i >= CustomMap.Cells - CustomMap.ExitClear;
                var col = EditCellColor(cell);
                if (locked) col = new Color(col.r * 0.5f, col.g * 0.5f, col.b * 0.5f, 1f);
                var btn = Theme.Button(root, locked ? "" : CustomMap.CellNames[(int)cell].Substring(0, 2),
                    col, Theme.Ink, 20, c,
                    new Vector2(-((perRow - 1) * cellW) / 2f + q * cellW, 176 - r * (cellH + 10)),
                    new Vector2(cellW - 8, cellH),
                    locked ? (System.Action)null : () =>
                    {
                        _editMap.cells[ix] = (CustomMap.Cell)_editBrush;
                        ShowMapEditor();
                    });
                if (locked) btn.interactable = false;
                // Number the track so a builder can describe a spot out loud.
                Theme.Label(btn.transform, (i + 1).ToString(), 15, new Color(0, 0, 0, 0.45f),
                    new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(60, 20));
            }

            // ---- the one lie -------------------------------------------------
            Theme.Label(root, "THE LIE THIS ROOM TELLS", 22, new Color(1, 1, 1, 0.55f),
                c, new Vector2(0, -20), new Vector2(900, 34));
            for (int i = 0; i < CustomMap.LieKinds; i++)
            {
                int lie = i;
                bool sel = (int)_editMap.lie == i;
                Theme.Button(root, CustomMap.LieNames[i],
                    sel ? Theme.Trick : new Color(1, 1, 1, 0.13f), Color.white, 21,
                    c, new Vector2(-520 + i * 260f, -70), new Vector2(248, 56),
                    () => { _editMap.lie = (CustomMap.Lie)lie; ShowMapEditor(); });
            }

            // ---- validation + actions ----------------------------------------
            string problem = _editMap.Validate();
            _editStatus = Theme.Label(root, problem ?? "This map is fair. Ship it.", 24,
                problem != null ? new Color(1f, 0.45f, 0.45f) : new Color(0.5f, 1f, 0.6f),
                c, new Vector2(0, -136), new Vector2(1600, 40));

            bool ok = problem == null;
            var playBtn = Theme.Button(root, "TEST IT", ok ? Theme.Exit : new Color(1, 1, 1, 0.12f),
                ok ? Theme.Ink : new Color(1, 1, 1, 0.4f), 32,
                c, new Vector2(-500, -216), new Vector2(300, 92),
                ok ? (System.Action)(() => { _editMap.Save(); PlayCustom(_editMap, _editMap.ToCode()); }) : null);
            playBtn.interactable = ok;

            var shareBtn = Theme.Button(root, "SHARE CODE", ok ? new Color(0.5f, 0.12f, 0.16f) : new Color(1, 1, 1, 0.12f),
                ok ? Color.white : new Color(1, 1, 1, 0.4f), 28,
                c, new Vector2(-170, -216), new Vector2(300, 92),
                ok ? (System.Action)(() =>
                {
                    _editMap.Save();
                    string code = _editMap.ToCode();
                    NativeShare.ShareText(
                        "I built a trap in Trust Issues. Beat my map if you can. Code: " + code, GameLink);
                    Analytics.Track("map_shared", new System.Collections.Generic.Dictionary<string, object>
                    { { "lie", _editMap.lie.ToString() } });
                    BossToast("CODE SENT - " + code);
                }) : null);
            shareBtn.interactable = ok;

            Theme.Button(root, "CLEAR", new Color(0.3f, 0.12f, 0.14f), Color.white, 26,
                c, new Vector2(160, -216), new Vector2(280, 92),
                () => { _editMap = new CustomMap(); ShowMapEditor(); });

            Theme.Button(root, "PLAY A FRIEND'S", new Color(0.32f, 0.08f, 0.4f), Color.white, 24,
                c, new Vector2(480, -216), new Vector2(320, 92), ShowLoadMap);

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 40,
                new Vector2(0.5f, 0f), new Vector2(0, 34), new Vector2(320, 88), ShowMenu);
        }

        static Color EditCellColor(CustomMap.Cell c) => c switch
        {
            CustomMap.Cell.Floor => new Color(0.55f, 0.52f, 0.58f),
            CustomMap.Cell.Gap => new Color(0.10f, 0.08f, 0.12f),
            CustomMap.Cell.Fake => new Color(0.42f, 0.34f, 0.46f),
            CustomMap.Cell.Spike => new Color(0.80f, 0.22f, 0.24f),
            CustomMap.Cell.Saw => new Color(0.85f, 0.45f, 0.20f),
            CustomMap.Cell.Late => new Color(0.72f, 0.18f, 0.42f),
            CustomMap.Cell.Faller => new Color(0.55f, 0.30f, 0.70f),
            CustomMap.Cell.Flame => new Color(0.90f, 0.55f, 0.15f),
            CustomMap.Cell.Crush => new Color(0.40f, 0.40f, 0.75f),
            _ => new Color(0.30f, 0.65f, 0.70f),
        };

        // Enter a friend's code and race their map.
        void ShowLoadMap()
        {
            Audio.Play("click");
            _state = State.Menu;
            if (_menuPanel != null) Destroy(_menuPanel);
            _menuPanel = Overlay(new Color(0.05f, 0.02f, 0.06f, 0.93f), out var root);
            _onBack = ShowMapEditor;   // back returns to the editor, not the main menu
            var c = new Vector2(0.5f, 0.5f);

            Theme.Label(root, "SOMEONE'S TRAP", 62, Theme.Player,
                c, new Vector2(0, 330), new Vector2(1400, 100)).font = Theme.TitleFont;
            Theme.Label(root, "paste the code they sent you", 26, new Color(1, 1, 1, 0.5f),
                c, new Vector2(0, 254), new Vector2(1200, 40));

            var input = MakeInput(root, new Vector2(0, 150), new Vector2(900, 104), "CODE");
            input.characterLimit = 40;
            // Codes are letters AND digits and must survive a paste, so the strict
            // 4-char alphanumeric validation the race lobby uses is wrong here.
            input.characterValidation = InputField.CharacterValidation.None;

            var status = Theme.Label(root, "", 26, new Color(1f, 0.5f, 0.5f),
                c, new Vector2(0, 60), new Vector2(1500, 40));

            Theme.Button(root, "PLAY IT", Theme.Exit, Theme.Ink, 34,
                c, new Vector2(0, -40), new Vector2(420, 100), () =>
                {
                    var m = CustomMap.FromCode(input.text);
                    if (m == null)
                    { status.text = "That code isn't a valid map."; Audio.Play("death", 0.4f); return; }
                    CustomMap.SaveFriend(input.text.Trim().ToUpperInvariant());
                    PlayCustom(m, m.ToCode());
                });

            var friend = CustomMap.LoadFriend();
            if (friend != null)
                Theme.Button(root, "REPLAY THE LAST ONE", new Color(0.32f, 0.08f, 0.4f), Color.white, 26,
                    c, new Vector2(0, -170), new Vector2(520, 88),
                    () => PlayCustom(friend, friend.ToCode()));

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 40,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(320, 92), ShowMapEditor);
        }

        void PlayCustom(CustomMap map, string code)
        {
            _customMap = map;
            _customCode = code;
            _mode = Mode.Custom;
            TrackModeSelected("Custom", 0);
            BeginRun(0);
            _customStart = Time.realtimeSinceStartup;   // clock starts on the first frame
            float best = CustomMap.BestTime(code);
            ShowBanner("THEIR TRAP", best > 0f
                ? $"your best on this map: {CustomMap.Fmt(best)} - beat it"
                : "clear it once, then race the clock");
        }

        // Cleared a custom map: the time, whether it's a new best, and the two
        // things that make it social - send your time, or send the map on.
        void ShowCustomResult(float secs, bool newBest)
        {
            var c = new Vector2(0.5f, 0.5f);
            var panel = Overlay(new Color(0.04f, 0.02f, 0.06f, 0.9f), out var root);
            _onBack = () => { Destroy(panel); if (_levelRoot != null) Destroy(_levelRoot.gameObject); ShowMenu(); };
            Gothic.FrameOnly(root);
            ResultTitle(root, "CLEARED", 210f, 72).color = Theme.Exit;
            Gothic.Line(root, CustomMap.Fmt(secs), 66, Gothic.Bone, new Vector2(0, 108), new Vector2(1000, 100));
            Gothic.Line(root, newBest ? "NEW BEST ON THIS MAP" : $"your best: {CustomMap.Fmt(CustomMap.BestTime(_customCode))}",
                30, Theme.Coin, new Vector2(0, 36), new Vector2(1200, 44));
            Gothic.Line(root, $"{_floorDeaths} deaths", 26, Gothic.Faint, new Vector2(0, -8), new Vector2(1000, 40));

            string code = _customCode;
            Gothic.Button(root, "CHALLENGE THEM", new Vector2(-340, -120), new Vector2(400, 96), () =>
                {
                    NativeShare.ShareText(
                        $"I beat this Trust Issues map in {CustomMap.Fmt(secs)} with {_floorDeaths} deaths. Try it. Code: {code}",
                        GameLink);
                    Analytics.Track("map_challenge", new System.Collections.Generic.Dictionary<string, object>
                    { { "seconds", secs } });
                }, false, 28);
            Gothic.Button(root, "AGAIN", new Vector2(80, -120), new Vector2(300, 96),
                () => { Destroy(panel); PlayCustom(_customMap, code); }, true, 30);
            Gothic.Button(root, "EDITOR", new Vector2(400, -120), new Vector2(300, 96),
                () => { Destroy(panel); if (_levelRoot != null) Destroy(_levelRoot.gameObject); ShowMapEditor(); },
                false, 28);
            Gothic.Button(root, "MAIN MENU", new Vector2(0, -246), new Vector2(400, 92),
                () => { Destroy(panel); if (_levelRoot != null) Destroy(_levelRoot.gameObject); ShowMenu(); },
                false, 30);
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
            // Settings > SCREEN SHAKE. Off means off everywhere — the boss beats and
            // the death punch route through here too.
            if (_cam != null && Options.Shake) StartCoroutine(Juice.Shake(_cam.transform, amount, dur));
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
                case "Daily":   StartDaily();   break;
                case "Endless": StartEndless(); break;
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
                case "Daily":   return "CONTINUE — BLOOD MOON";
                case "Endless": return "START — ENDLESS NIGHT";
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
                    : $"← → / A D move   •   {Controls.Name(Controls.Jump)} jump   •   tap {Controls.Name(Controls.Fly)} to fly" + extra + "   •   R restart   •   trust nothing",
                    Memory.IsFirstSession ? 6f : 2.5f);   // first-timers get time to actually read it
            }
            else
                // The standing bargain, restated every time a Castle floor opens
                // from the map (used to only show on an auto-chained floor, so
                // tapping a floor from the map never mentioned it at all).
                ShowHint("BONUS: under 5 deaths on this floor → +5 shards", 2f);
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
            ShowHint($"BLOOD MOON — {Diff.StartHearts + 2} lives, +1 per night. Fall too many times and the night just resets. Tap {Controls.Name(Controls.Fly)}/FLY to take off and glide as a bat — no jump needed.");
        }

        void StartEndless()
        {
            Audio.Play("click");
            _mode = Mode.Endless;
            TrackModeSelected("Endless", 0);
            _endlessSeed = new System.Random().Next(1, 1000000);
            BeginRun(0);
            ShowBanner("ENDLESS NIGHT", "one road • no finish • how far can you survive?");
            ShowHint($"Risk paths hide extra lives. A life revives you at safe ground behind your fall. Tap {Controls.Name(Controls.Fly)}/FLY to take off and glide.");
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
            // Opaque gothic ground + the artwork's frame, moon and stone — this screen
            // had no painting of its own and used to be plain boxes on a see-through
            // wash, which read as a different game from the menu you'd just left.
            _menuPanel = Overlay(new Color(0.05f, 0.01f, 0.03f, 1f), out var root);
            Gothic.Backdrop(root);
            _onBack = () => { Net.Leave(); ShowMenu(); };   // drop the room on the way out

            Gothic.Heading(root, "MULTIPLAYER", "RACE A FRIEND TO THE COFFIN — SAME TRACK, LIVE GHOSTS");

            if (!Net.Available)
            {
                Gothic.PlateAt(root, new Vector2(0, 40), new Vector2(1100, 240), Gothic.Plate);
                Gothic.Line(root, "Multiplayer needs the Photon PUN 2 package imported.\nImport it in Unity, then this screen goes live.",
                    32, new Color(0.85f, 0.55f, 0.55f, 0.9f), new Vector2(0, 40), new Vector2(1020, 200));
                Gothic.Back(root, ShowMenu);
                return;
            }

            // PAINTED LOBBY. The artwork draws the heading, the name plate, HOST, the
            // CODE box, JOIN, the hint and BACK — so all that goes on top is the one
            // control that must be REAL (the code field, because you have to be able
            // to type a room code into it), the tap-zones, and the status line.
            // Rects measured off the 1536x1024 mockup.
            //
            // The name plate is DISPLAY ONLY. Names come from a fixed safe vocabulary
            // (Meta.Nick), so there is no free-text field for a player to type a slur
            // into and no user-generated content for the store review to worry about.
            //
            // The painted name reads "Heir-609" and the painted status reads
            // "CREATING ROOM…". Both are baked pictures of a moment, so both are
            // chipped out and replaced with live values — otherwise the screen would
            // tell every player they're called Heir-609 and permanently mid-connect.
            if (Skin.Background(root, "versus_bg") != null)
            {
                var plate = new Color(0.026f, 0.014f, 0.016f, 1f);

                Skin.Chip(root, 0.418f, 0.288f, 0.642f, 0.352f, plate);
                var safeName = Skin.LiveText(root, Net.PlayerName,
                    0.424f, 0.292f, 0.636f, 0.348f, 34, Gothic.Bone);
                if (Theme.MenuFont != null) safeName.font = Theme.MenuFont;

                Skin.Zone(root, 0.300f, 0.395f, 0.700f, 0.487f,
                    () => { SetLobbyStatus("Creating room…"); Net.Host(StartVersus, LobbyError); }, "host");

                Skin.Chip(root, 0.303f, 0.527f, 0.492f, 0.603f, plate);
                var codeSlot = Skin.Slot(root, "CodeSlot", 0.309f, 0.531f, 0.486f, 0.599f);
                var codeIn = MakeInput(codeSlot, Vector2.zero, Vector2.zero, "CODE");
                FillParent(codeIn);

                Skin.Zone(root, 0.512f, 0.527f, 0.686f, 0.603f,
                    () => { SetLobbyStatus("Joining…"); Net.Join(codeIn.text, StartVersus, LobbyError); }, "join");

                Skin.Chip(root, 0.330f, 0.652f, 0.670f, 0.700f, plate);
                _lobbyStatus = Skin.LiveText(root, "", 0.30f, 0.652f, 0.70f, 0.700f, 30, Theme.Coin);

                Skin.Zone(root, 0.42f, 0.882f, 0.58f, 0.958f,
                    () => { Net.Leave(); ShowMenu(); }, "back");
                return;
            }

            // YOUR NAME — persisted, and shown to the rival you race. Pre-filled with
            // the stored name; every keystroke saves it.
            Gothic.Line(root, "YOUR RACER", 26, Gothic.Faint, new Vector2(-360, 190),
                new Vector2(300, 40), TextAnchor.MiddleRight);
            Gothic.PlateAt(root, new Vector2(60, 190), new Vector2(520, 84), Gothic.Plate);
            Gothic.Line(root, Net.PlayerName, 34, Gothic.Bone, new Vector2(60, 190),
                new Vector2(480, 70));

            // HOST
            Gothic.Button(root, "HOST A RACE", new Vector2(0, 76), new Vector2(560, 100),
                () => { SetLobbyStatus("Creating room…"); Net.Host(StartVersus, LobbyError); }, true, 44);

            // JOIN: a code box + button
            var input = MakeInput(root, new Vector2(-130, -46), new Vector2(340, 96), "CODE");
            Gothic.Button(root, "JOIN", new Vector2(180, -46), new Vector2(300, 96),
                () => { SetLobbyStatus("Joining…"); Net.Join(input.text, StartVersus, LobbyError); }, false, 40);

            _lobbyStatus = Gothic.Line(root, "", 30, Theme.Coin, new Vector2(0, -150), new Vector2(1400, 56));

            Gothic.Line(root, "Share the 4-letter code with whoever you want to race.\nWorks across phones, laptops — anyone with the link.",
                25, Gothic.Faint, new Vector2(0, -240), new Vector2(1400, 110));

            Gothic.Back(root, () => { Net.Leave(); ShowMenu(); });
        }

        // Stretch a code-built control to fill the painted slot it was dropped into.
        // MakeInput positions itself with an explicit centre and size, which is right
        // for the code-built layouts but wrong on a skinned screen where the artwork
        // decides where things go — there the slot rect is the truth.
        static void FillParent(InputField f)
        {
            var rt = (RectTransform)f.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void SetLobbyStatus(string s) { if (_lobbyStatus != null) _lobbyStatus.text = s; }
        void LobbyError(string s) { SetLobbyStatus(s); }

        // A minimal uppercase code input field, built from code like everything else.
        InputField MakeInput(Transform parent, Vector2 pos, Vector2 size, string placeholder)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            // Sunk into the stone with the same ornate border as every other control.
            var img = go.AddComponent<Image>(); img.color = new Color(0.020f, 0.012f, 0.026f, 1f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            Gothic.InnerFrame(go.transform);

            var ph = Theme.Label(go.transform, placeholder, 40, new Color(0.62f, 0.55f, 0.52f, 0.45f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 40, size.y - 16));
            var txt = Theme.Label(go.transform, "", 40, Gothic.Bone,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 40, size.y - 16));
            // The text and the placeholder STRETCH inside the box instead of carrying
            // a fixed size. On a skinned screen the box is built at zero size and then
            // stretched into its painted slot, which left both labels sized (-40,-16) —
            // an inside-out rect that draws nothing, so every letter you typed in the
            // multiplayer lobby was invisible. Stretching keeps them right in both the
            // code-built layout and the painted one.
            foreach (var t in new[] { ph, txt })
            {
                var trt = t.rectTransform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(20, 8); trt.offsetMax = new Vector2(-20, -8);
            }
            if (Theme.MenuFont != null) { ph.font = Theme.MenuFont; txt.font = Theme.MenuFont; }

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
            UpdateVersusScore();
            BeginRun(0);
            ShowBanner($"ROOM {Net.RoomCode}", $"race to the coffin • a win is a point • FIRST TO {VersusMatchPoints} takes the match");
            ShowHint($"Race to the coffin, then a NEW track loads. Tap the SABOTAGE buttons to troll your rival. Tap {Controls.Name(Controls.Fly)} to take off and glide.");
        }

        // Match score across rounds (continuous multiplayer).
        int _versusRound, _versusWins, _versusLosses;
        // ---- The versus scoreboard -------------------------------------------
        // A framed plate at top-centre (clear of the top-left HUD and the top-right
        // mute/shard icons), in the menus' visual language: near-black plate, ornate
        // gold frame, serif type. Reads at a glance mid-race:
        //
        //        YOUR NAME      3  –  1      RIVAL NAME
        //             ROUND 5  ·  FIRST TO 5
        //
        // Scores are the main-menu blood red; the leader's name wears candle gold.
        GameObject _versusHud;
        UnityEngine.UI.Text _vsYouName, _vsYouScore, _vsRivalName, _vsRivalScore, _vsRound;
        static readonly Color VsBone = new Color(0.90f, 0.86f, 0.82f, 0.92f);

        void BuildVersusHud()
        {
            if (_versusHud != null) return;
            _versusHud = new GameObject("VersusHud", typeof(RectTransform));
            _versusHud.transform.SetParent(Theme.Canvas.transform, false);
            var rt = (RectTransform)_versusHud.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -12f);
            rt.sizeDelta = new Vector2(820f, 112f);   // room for two 14-char names

            var plate = _versusHud.AddComponent<Image>();
            plate.color = new Color(0.05f, 0.02f, 0.035f, 0.88f);
            plate.raycastTarget = false;

            // The same ornate gold frame the menus use, so the HUD belongs to the game.
            var frameSp = Theme.NineSlice("panel_frame", 16);
            if (frameSp != null)
            {
                var fr = new GameObject("Frame", typeof(RectTransform));
                fr.transform.SetParent(_versusHud.transform, false);
                var fi = fr.AddComponent<Image>();
                fi.sprite = frameSp; fi.type = Image.Type.Sliced;
                fi.pixelsPerUnitMultiplier = 0.42f;
                fi.color = new Color(0.86f, 0.72f, 0.42f, 0.95f);
                fi.raycastTarget = false;
                var frt = fi.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(-8, -8); frt.offsetMax = new Vector2(8, 8);
            }

            // Columns: [name ▸][score] – [score][◂ name], names pushed outward so a
            // long name can never crowd the digits.
            _vsYouName    = VsLabel("",  27, VsBone,        new Vector2(-250, 16), new Vector2(264, 40), TextAnchor.MiddleRight);
            _vsYouScore   = VsLabel("0", 52, Theme.Player,  new Vector2(-78, 16),  new Vector2(84, 62),  TextAnchor.MiddleCenter);
            VsLabel("–", 40, new Color(0.80f, 0.70f, 0.45f, 0.80f), new Vector2(0, 16), new Vector2(50, 56), TextAnchor.MiddleCenter);
            _vsRivalScore = VsLabel("0", 52, Theme.Player,  new Vector2(78, 16),   new Vector2(84, 62),  TextAnchor.MiddleCenter);
            _vsRivalName  = VsLabel("",  27, VsBone,        new Vector2(250, 16),  new Vector2(264, 40), TextAnchor.MiddleLeft);
            _vsRound      = VsLabel("",  20, new Color(0.85f, 0.70f, 0.35f, 0.85f),
                                    new Vector2(0, -32), new Vector2(780, 30), TextAnchor.MiddleCenter);
        }

        // A scoreboard label in the menu serif (matches the skin artwork's type).
        UnityEngine.UI.Text VsLabel(string txt, int size, Color col, Vector2 pos, Vector2 dim, TextAnchor align)
        {
            var t = Theme.Label(_versusHud.transform, txt, size, col, new Vector2(0.5f, 0.5f), pos, dim, align);
            if (Theme.MenuFont != null) t.font = Theme.MenuFont;
            t.raycastTarget = false;
            return t;
        }

        // Refresh the scoreboard's live values (names, points, round line).
        void UpdateVersusScore()
        {
            BuildVersusHud();
            bool youLead = _versusWins > _versusLosses, rivalLead = _versusLosses > _versusWins;
            _vsYouName.text    = Net.PlayerName.ToUpperInvariant();
            _vsRivalName.text  = Net.RivalName().ToUpperInvariant();
            _vsYouName.color   = youLead ? Theme.Coin : VsBone;
            _vsRivalName.color = rivalLead ? Theme.Coin : VsBone;
            _vsYouScore.text   = _versusWins.ToString();
            _vsRivalScore.text = _versusLosses.ToString();
            _vsRound.text      = $"ROUND {_versusRound + 1}   ·   {ThemeNames[VersusThemes[_versusRound % VersusThemes.Length]]}   ·   FIRST TO {VersusMatchPoints}";
        }

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
            UpdateVersusScore();   // new round + new arena name on the scoreboard
            ShowHint($"ROUND {_versusRound + 1}  •  you {_versusWins} – {_versusLosses} rival.  Race!");
        }

        void HookNet()
        {
            if (_netHooked) return;
            _netHooked = true;
            Net.OnState += OnRemoteState;
            Net.OnLeft  += OnRemoteLeft;
            Net.OnWin   += OnRemoteWin;
            Net.OnTroll += ReceiveTroll;
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

        // First to this many round wins takes the whole match (Drive Ahead style).
        const int VersusMatchPoints = 5;

        // Local player crossed the finish first (or a rival did) — end the race.
        void VersusResult(bool youWon)
        {
            _state = State.Win;
            if (youWon) { Badges.Award("versus_win"); _versusWins++; } else _versusLosses++;
            UpdateVersusScore();
            if (_versusWins >= 3) Badges.Award("versus_streak3");

            // Match point reached? Show the champion screen instead of NEXT ROUND.
            if (_versusWins >= VersusMatchPoints || _versusLosses >= VersusMatchPoints)
            { VersusMatchOver(_versusWins >= VersusMatchPoints); return; }
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
            Gothic.FrameOnly(root);
            _onBack = () => { Destroy(panel); LeaveVersus(); };
            var rt2 = ResultTitle(root, youWon ? "YOU WON THE RACE" : "YOU LOST THE RACE",
                160f, youWon ? 70 : 64);
            if (youWon) rt2.color = Theme.Exit;
            Gothic.Line(root, youWon ? "first to the coffin" : "a faster vampire beat you to it",
                40, Gothic.Bone, new Vector2(0, 60), new Vector2(1500, 70));
            // Running match score across rounds — the "one more round" hook.
            Gothic.Line(root, $"MATCH:  YOU {_versusWins}  –  {_versusLosses} RIVAL",
                38, Theme.Coin, new Vector2(0, 0), new Vector2(1400, 56));
            Gothic.Button(root, "NEXT ROUND", new Vector2(0, -120), new Vector2(560, 116),
                () => { Destroy(panel); NextVersusRound(); }, true, 44);
            Gothic.Button(root, "LEAVE RACE", new Vector2(0, -250), new Vector2(460, 100),
                () => { Destroy(panel); LeaveVersus(); }, false, 38);
        }

        // Somebody hit VersusMatchPoints — the match is decided. Big champion
        // screen, then REMATCH (same room, scores wiped, keep racing the same
        // rival) or LEAVE. The room stays open on rematch so no re-code needed.
        void VersusMatchOver(bool youWon)
        {
            _state = State.Win;
            if (youWon) Badges.Award("versus_match");
            Analytics.Track("versus_match", new System.Collections.Generic.Dictionary<string, object>
            {
                { "won", youWon }, { "wins", _versusWins }, { "losses", _versusLosses },
            });
            Audio.Play(youWon ? "win" : "death", 0.8f);
            var panel = Overlay(new Color(0.05f, 0f, 0.02f, 0.9f), out var root);
            Gothic.FrameOnly(root);
            _onBack = () => { Destroy(panel); LeaveVersus(); };
            var mt = ResultTitle(root, youWon ? "MATCH WON" : "MATCH LOST", 170f, youWon ? 78 : 70);
            if (youWon) mt.color = Theme.Exit;
            Gothic.Line(root, youWon ? $"first to {VersusMatchPoints} — you are the true heir"
                                     : $"your rival reached {VersusMatchPoints} first",
                40, Gothic.Bone, new Vector2(0, 70), new Vector2(1500, 70));
            Gothic.Line(root, $"FINAL:  YOU {_versusWins}  –  {_versusLosses} RIVAL",
                44, Theme.Coin, new Vector2(0, 0), new Vector2(1400, 60));
            Gothic.Button(root, "REMATCH", new Vector2(0, -120), new Vector2(560, 116),
                () => { Destroy(panel); _versusWins = 0; _versusLosses = 0; NextVersusRound(); }, true, 44);
            Gothic.Button(root, "LEAVE RACE", new Vector2(0, -250), new Vector2(460, 100),
                () => { Destroy(panel); LeaveVersus(); }, false, 38);
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
        // ---- dev screenshot hooks (see ShotBot) ------------------------------
        // The whole game is built from code, so these three lines are the only way
        // to LOOK at a floor without a phone in hand: a headless player launched
        // with -shot drives them and writes PNGs. Harmless in a shipped build —
        // nothing calls them unless that flag is on the command line.
        public void DevStartFloor(int levelIndex)
        {
            _mode = Mode.Curated;
            BeginRun(Mathf.Clamp(levelIndex, 0, Levels.Count - 1));
        }

        public void DevWarp(float x)
        {
            if (_player == null) return;
            _player.transform.position = new Vector3(x, _player.transform.position.y + 0.4f, 0f);
            SnapCamera();
        }

        public void DevOpenBestiary() => ShowCodex();

        /// <summary>Open a leaderboard with a plausible personal best already banked,
        /// so a screenshot run can prove the "you are #N" row actually renders in
        /// the right place instead of only ever showing the unranked state.</summary>
        public void DevOpenLeaderboard(string mode, int myScore)
        {
            if (myScore > 0) Leaderboard.Submit(mode, myScore);
            ShowLeaderboard(mode);
        }

        // Open the race lobby with something typed into both boxes, so a screenshot
        // run can prove the letters are actually visible in the painted slots.
        public void DevOpenLobby()
        {
            ShowVersusLobby();
            foreach (var f in Object.FindObjectsByType<InputField>(FindObjectsSortMode.None))
                f.text = f.characterLimit == 4 ? "WXYZ" : "Type Me 42";
        }

        // Open the castle map with progress faked to a given floor, so a screenshot
        // run can check that the "you are here" glow actually lands on that seal.
        public void DevOpenCastle(int unlocked)
        {
            PlayerPrefs.SetInt("castle_unlocked", Mathf.Max(0, unlocked));
            ShowLevelSelect();
        }

        // Open Settings on a given tab, so a screenshot run can check all four of
        // them rather than only whichever one happened to be remembered.
        public void DevOpenSettings(int tab)
        {
            _settingsTab = tab;
            ShowSettings();
        }

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
            if (_mode == Mode.Endless)
            {
                _endlessBankedMeters = 0f;
                _endlessPeakMeters = 0f;
                _endlessLastHudMeters = -1;
                _endlessRevivePending = false;
                _endlessSafeHistory.Clear();
                _endlessLifeClaimed.Clear();
            }
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
            // back to start); Blood Moon gets a difficulty-scaled pool of lives,
            // while Endless begins with no revives and rewards them on risk paths.
            // Blood Moon gets a +2 cushion on top so a single attempt reaches
            // deeper into the 5 nights before it loops — it beat NO ONE before.
            // Custom joins Curated/Versus on infinite retries — a time trial ends
            // when you finish it, never because you ran out of lives.
            _hearts = (_mode == Mode.Curated || _mode == Mode.Versus || _mode == Mode.Custom) ? -1
                     : _mode == Mode.Daily ? Diff.StartHearts + 2
                     : 0; // Endless lives are revive tokens earned on risk paths.
            if (_hudChrome != null) _hudChrome.SetActive(true);
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
                // Blood Moon is no longer a dice roll. Easing the generator's
                // difficulty NUMBER was never enough — the generator can still put
                // a blind trap on every platform, so the five nights are authored
                // beat by beat in Levels.BloodMoonNight (one new trap family per
                // night, never two blind beats in a row, rest platforms carrying
                // real checkpoints). The seed only varies the spacing, so tonight
                // looks different from last night without being harder.
                case Mode.Daily:
                    return Levels.BloodMoonNight(DailySeed() * 31 + _levelIndex * 7919,
                                                 _levelIndex + 1);
                // Endless ramps every two hidden generation chunks and caps at
                // tier 7. The five rhythm profiles keep deep runs varied without
                // escalating into procedurally impossible layouts.
                case Mode.Endless: return Levels.Generate(
                    _endlessSeed + _levelIndex * 7919,
                    Mathf.Min(7, 1 + _levelIndex / 2), false, _levelIndex);
                // Versus: a shared race track, identical for everyone in the room.
                // The room code + ROUND number seed it, so each round is a fresh
                // (still deterministic) layout and the match runs continuously.
                //
                // The hazard SET never grows — the race pool is spikes, one saw,
                // the odd overhead bat, and falling floors, full stop. A hard track
                // plus sabotage buttons is unbeatable, and the sabotage (curse /
                // snuff / quake) is the actual game here. What DOES grow is the
                // track: rounds 1-2 are short and flat so the first race anyone
                // ever runs is winnable, and from round 3 it lengthens by a beat
                // and starts opening glide gaps — so a long match escalates instead
                // of replaying the same seven jumps with new numbers.
                case Mode.Versus:  return Levels.Generate(Net.Seed + _versusRound * 101,
                                                          Mathf.Min(3, 2 + _versusRound / 2), race: true);
                // A player-built map. CustomMap.ToLevel goes through the same B
                // builder as every hand-made floor, so it inherits the ceiling
                // vault, the stage camera and the beatability guarantees.
                case Mode.Custom:  return (_customMap ?? new CustomMap()).ToLevel();
                default:
                    int bt = BossTierForFloor(_levelIndex);          // Castle floors 10/20/30/40
                    return bt > 0 ? Levels.BossRoom(bt) : Levels.Get(_levelIndex);
            }
        }

        // Curated boss floors. Floor 10 is deliberately NOT one any more: the
        // first world is pure "what's the next lie" platforming (Level Devil
        // sustains 200+ stages with zero bosses — the genre's tension and a
        // health-bar fight are different games), and floor 10 is its final
        // exam instead. The tier-1 Ghoul is benched, not deleted — he can come
        // back as an Endless mini-boss. Floors 20/30/40 keep their bosses as
        // world-capping spectacles once the player is invested.
        // (The old AddMidCheckpoint helper is gone: it hunted for one hazard-free
        // spot in a procedurally generated night, which on a dense night meant NO
        // checkpoint at all. Blood Moon nights now author their own rest platforms
        // — two of them from night 3 — so the safety is designed, not searched for.)

        static int BossTierForFloor(int idx)
        {
            switch (idx) { case 19: return 2; case 29: return 3; case 39: return 4; }
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

        // Lifetime deaths on one castle floor, kept forever so the map can show
        // the cost of every floor you've walked. Not reset by clearing a floor —
        // the scar is the point.
        const string FloorDeathKey = "ti_fd_";
        static int FloorDeaths(int idx) => PlayerPrefs.GetInt(FloorDeathKey + idx, 0);

        // Fastest clean run of one castle floor, in seconds. 0 = never cleared. The
        // map's selection plate reads it back as "BEST 1:42" — a floor you've beaten
        // is worth returning to only if the screen gives you a number to beat.
        const string FloorBestKey = "ti_fb_";
        static float FloorBest(int idx) => PlayerPrefs.GetFloat(FloorBestKey + idx, 0f);
        static void RecordFloorBest(int idx, float seconds)
        {
            if (seconds <= 0f) return;
            float prev = FloorBest(idx);
            if (prev > 0f && prev <= seconds) return;
            PlayerPrefs.SetFloat(FloorBestKey + idx, seconds);
            PlayerPrefs.Save();
        }
        static void AddFloorDeath(int idx)
        {
            PlayerPrefs.SetInt(FloorDeathKey + idx, FloorDeaths(idx) + 1);
            PlayerPrefs.Save();
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
            // Built exactly like a real platform — same stone, same lip — because
            // it has to be indistinguishable right up until the lights die.
            foreach (var nf in _level.NightFloors)
                BuildStoneFloor("NightFloor", nf.pos, nf.size, null)
                    .AddComponent<NightFloor>().Configure(nf.pos.x, false);
            // …and the inverse: spectral floor that's a shimmer in the light and
            // only turns solid in the dark (floor 7's whole lesson).
            foreach (var gf in _level.GhostFloors)
                BuildStoneFloor("GhostFloor", gf.pos, gf.size, null)
                    .AddComponent<NightFloor>().Configure(gf.pos.x, true);
            // Bobbing stone slabs — the ride across unjumpable pits. Kinematic
            // body so the engine carries the player as it moves.
            foreach (var mv in _level.Movers)
            {
                var go = BuildStoneFloor("Mover", new Vector2(mv.x, mv.y), new Vector2(mv.w, 0.6f), null);
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<VertPlat>().amp = mv.z;
            }
            foreach (var d in _level.Decos)
                Theme.Box("Deco", _levelRoot, d.pos, d.size, d.color, 2);
            foreach (var t in _level.Traps)
                BuildTrap(t);
            if (_mode == Mode.Endless) SpawnEndlessLifePickup();
            foreach (var pp in _level.Portals)
                BuildPortals(pp);
            _rumorFloorUsed = false;
            BuildReactiveTraps();   // the "Trust Issues" learned traps from past deaths
            PlaceLanterns();
            BuildAerialHazards();
            if (_mode == Mode.Daily && _levelIndex == 1 && Rumor.HiddenDoor)
                BuildHiddenDoor();  // tonight's rumor (2): the ghost door of night 2

            SpawnPlayer();
            if (_mode == Mode.Endless && _player != null)
            {
                _endlessSafeHistory.Clear();
                _endlessSafeHistory.Add(_player.transform.position);
            }
            // Roomed levels (the rebuilt Castle floors) get a director to run the
            // per-room rules, lock the camera per chamber, and draw the room
            // dots. Corridor levels don't.
            if (_level.Rooms.Count > 0 && _player != null)
                _levelRoot.gameObject.AddComponent<RoomDirector>()
                    .Init(_level, _player.transform, _levelRoot);
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
            // Sunrise budget. Blood Moon used to run 11 + plats*1.8 — about 25s on
            // night 1, THREE TIMES tighter than Castle's ~78s — while also being
            // one-hit, life-limited and checkpoint-less. That triple penalty, not
            // the traps, is what stopped anyone finishing it. Blood Moon now gets
            // Castle's budget, and night 1 gets an extra grace cushion on top so
            // the mode's front door is genuinely learnable.
            float sunBudget = 16f + plats * 2.2f;
            if (_mode == Mode.Daily) sunBudget += Mathf.Max(0f, 14f - _levelIndex * 3.5f);
            // Custom maps are TIME TRIALS: the race clock is the only pressure, so a
            // second hidden timer chasing you would just be noise.
            _sunThreshold = (_mode == Mode.Versus || _mode == Mode.Custom || InBossRoom || !Diff.SunRise) ? 999f
                          : _mode == Mode.Curated ? 16f + plats * 2.2f
                          : _mode == Mode.Daily   ? sunBudget
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
            // Floors 1-4 are onboarding. Stable retries teach an answer; adding a
            // fresh hazard where a beginner paused only turns learning into luck.
            if (_mode == Mode.Curated && _levelIndex < 4) return;
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
            _calledPaid.Clear(); _pendingCalls.Clear();   // fresh floor = fresh Called It payouts
            _lastT = null; _lastP = null; _recT.Clear(); _recP.Clear();
            _bossIntroedTier = -1;   // a fresh floor → the next boss plays its full cutscene
            _floorDeaths = 0;        // per-floor death count (curse duels compare this)
            _stageIndex = 0;         // fresh floor starts at stage 1; banked progress is per-floor
            Currency.ResetFloorPayouts();   // a new floor re-opens the death-shard window
        }
        int _floorDeaths;

        // ==================== stages ====================
        // The Level Devil structure: a roomed floor is 5 discrete SUB-LEVELS.
        // Crossing a stage's exit doorway BANKS it — the dot fills gold, the
        // castle seals the door behind you, and from then on death only restarts
        // the CURRENT stage (the whole level still rebuilds each life, which
        // resets every trap for free; only the spawn point advances). Banked
        // progress lives here as one int: the highest stage reached this floor.
        // It survives deaths, and resets on a new floor / mode start / the pause
        // menu's full RESTART. Deaths still count and still pay blood shards —
        // cheap stage retries are the license for stages to be MEANER.
        int _stageIndex;
        public int StageIndex => _stageIndex;

        // Called by RoomDirector when the player crosses into room `idx`.
        // Only forward progress banks; re-entering on respawn is a no-op.
        public void BankStage(int idx)
        {
            if (idx <= _stageIndex || _level == null || _level.Rooms.Count == 0) return;
            _stageIndex = idx;
            Audio.PlayOr("levelup", "click", 0.5f);   // the stage-cleared chime
            ShakeCam(0.12f, 0.08f);                   // …and the castle sealing behind you
        }

        // Gothic ambience: the lanterns the artwork hangs down every hall — an iron
        // cage on a chain from the vault, a live flame inside it, a warm pool of
        // light around it. They used to be bare flames floating at head height with
        // nothing holding them up, which is the detail that made a castle hall read
        // as a dark room with some fire in it.
        void PlaceLanterns()
        {
            if (_mode == Mode.Versus) return;
            var frames = Assets.Sheet("torch", 32);

            const float CeilY = 2.9f;      // the vault's underside
            const float LampY = 1.05f;     // where the lantern hangs — clear of the jump apex
            for (float x = _level.Spawn.x + 3f; x <= _levelEndX - 1f; x += 6f)
            {
                // The chain, drawn link by link and passing BEHIND the ceiling stone
                // (order 0) so it looks bolted into the vault rather than stuck on it.
                float top = LampY + 0.42f;
                var chain = new GameObject("Chain");
                chain.transform.SetParent(_levelRoot, false);
                chain.transform.position = new Vector3(x, (top + CeilY) / 2f, 0f);
                var csr = chain.AddComponent<SpriteRenderer>();
                csr.sprite = Gothic.ChainLink;
                csr.drawMode = SpriteDrawMode.Tiled;
                csr.size = new Vector2(0.16f, CeilY - top);
                csr.sortingOrder = 0;
                if (_mode == Mode.Endless)
                    csr.color = new Color(0.20f, 0.25f, 0.31f, 0.95f);

                // The pool of candlelight first, so everything else sits inside it.
                var glow = Theme.SpriteBox("LanternGlow", _levelRoot, new Vector3(x, LampY, 0f),
                    new Vector2(3.0f, 3.0f), Theme.Moon, 0);
                glow.GetComponent<SpriteRenderer>().color = _mode == Mode.Endless
                    ? new Color(1f, 0.62f, 0.24f, 0.12f)
                    : new Color(1f, 0.52f, 0.18f, 0.16f);
                var fp = glow.AddComponent<FaintPulse>();
                fp.min = _mode == Mode.Endless ? 0.07f : 0.09f;
                fp.max = _mode == Mode.Endless ? 0.14f : 0.20f;
                fp.speed = _mode == Mode.Endless ? 3.2f : 5f;

                // The flame inside the glass, then the cage over the top of it.
                if (frames != null && frames.Length > 0)
                {
                    var fire = Theme.SpriteBox("Flame", _levelRoot, new Vector3(x, LampY - 0.03f, 0f),
                        new Vector2(0.42f, 0.52f), frames[0], 1);
                    fire.AddComponent<LoopAnim>().Init(frames, 10f);
                }
                var lantern = Theme.SpriteBox("Lantern", _levelRoot, new Vector3(x, LampY + 0.06f, 0f),
                    _mode == Mode.Endless ? new Vector2(0.70f, 1.02f) : new Vector2(0.62f, 0.94f),
                    Gothic.LanternCage, 2);
                if (_mode == Mode.Endless)
                    lantern.GetComponent<SpriteRenderer>().color = new Color(0.72f, 0.80f, 0.88f, 1f);
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
            UpdateHearts();
            string gold = ColorUtility.ToHtmlStringRGB(Theme.Coin);
            if (InBossRoom)
            {
                string shield = _bossHp > 0 ? "SHIELD " + new string('*', _bossHp) : "";
                string ammoB = _player != null && _player.ammo > 0 ? "     AMMO " + _player.ammo : "";
                _hud.text = shield + ammoB;
                if (_stageText != null) _stageText.text = "";
                return;
            }
            // The artwork's own top line: the floor in blood red, the place it's in
            // in candle gold, then the tally that never stops climbing.
            string place = _mode == Mode.Endless
                 ? $"DISTANCE {CurrentEndlessMeters} M   <color=#{gold}>•   BEST {Mathf.Max(CurrentEndlessMeters, PlayerPrefs.GetInt("best_endless_distance", 0))} M</color>"
                 : _mode == Mode.Daily ? $"NIGHT {_levelIndex + 1}/{DailyLen}"
                 : _mode == Mode.Versus ? $"RACE {Net.RoomCode}"
                 : $"FLOOR {_levelIndex + 1}   <color=#{gold}>•   {WorldNames[WorldOf(_levelIndex)]}</color>";
            _hud.text = place + "     DEATHS " + _deaths;

            // …and the stage counter beneath it, which the HUD had nowhere to say
            // even though every roomed floor is scored in stages.
            if (_stageText == null) return;
            int stages = _level != null ? _level.Rooms.Count : 0;
            _stageText.text = stages > 1 ? $"STAGE {Mathf.Min(_stageIndex + 1, stages)} / {stages}" : "";
        }

        // Light one pip per life left, dim the ones already spent. Hidden entirely in
        // the modes that retry forever — see BuildPortraitPlate.
        void UpdateHearts()
        {
            if (_heartPips == null) return;
            for (int i = 0; i < _heartPips.Length; i++)
            {
                if (_heartPips[i] == null) continue;
                bool show = _hearts >= 0 && i < Mathf.Max(_hearts, Diff.StartHearts);
                if (_heartPips[i].gameObject.activeSelf != show) _heartPips[i].gameObject.SetActive(show);
                if (show)
                    _heartPips[i].color = i < _hearts ? Color.white : new Color(0.28f, 0.20f, 0.24f, 0.85f);
            }
        }

        // Lets the player controller refresh the ammo readout as a clip is spent.
        public void RefreshHud() => UpdateHud();

        int CurrentEndlessMeters
        {
            get
            {
                if (_mode != Mode.Endless) return 0;
                float local = 0f;
                if (_player != null && _level != null)
                    local = Mathf.Clamp(_player.transform.position.x - _level.Spawn.x,
                                        0f, Mathf.Max(0f, _levelEndX - _level.Spawn.x));
                return Mathf.Max(0, Mathf.FloorToInt(Mathf.Max(_endlessPeakMeters,
                    _endlessBankedMeters + local)));
            }
        }

        /// <summary>
        /// The stone colour for the world currently being played, derived from the
        /// same theme table the backdrop uses so floor and sky always agree. Kept
        /// desaturated and dark — the stone should read as "this world's rock",
        /// never as coloured plastic that fights the hazards for attention.
        /// </summary>
        /// <summary>The world's stone, darkened to the value the artwork gives a ledge.</summary>
        Color FloorStone
        {
            get { var c = WorldStone; return new Color(c.r * 0.52f, c.g * 0.52f, c.b * 0.55f, 1f); }
        }

        Color WorldStone
        {
            get
            {
                int idx = Mathf.Clamp(_curTheme < 0 ? 0 : _curTheme, 0, ThemeMoon.Length - 1);
                var t = ThemeMoon[idx];
                // Pull most of the way back to neutral grey so hazards stay readable.
                const float k = 0.30f;
                return new Color(Mathf.Lerp(0.80f, t.r, k),
                                 Mathf.Lerp(0.80f, t.g, k),
                                 Mathf.Lerp(0.80f, t.b, k), 1f);
            }
        }

        // A platform: castle-stone tile (tiled) with a single blood-red lip on top.
        // EXCEPT room ceilings, which used to be built by this exact same call and
        // so rendered as a floor (bright blood lip and all) glued to the top of the
        // screen — the single reason every stage read as a sealed box instead of a
        // hall. Ceilings now get a recessed vault look; the COLLIDER is untouched,
        // so the gravity ceiling-road and the descending press still behave exactly
        // as before.
        void BuildPlatform(Rect2 p)
        {
            bool ceiling = p.pos.y > 2.5f && p.size.x > p.size.y * 2f;
            // …and the third case, which used to fall through to the floor builder:
            // the DIVIDER WALLS between chambers. They were getting a blood-red
            // landing lip and gore running down them, so every wall in the castle
            // read as a floor stood on its end. A wall is masonry, and in the
            // artwork it carries the banners.
            bool wall = p.size.y > p.size.x * 1.4f;
            if (ceiling) BuildCeilingVault(p);
            else if (wall) BuildWall(p);
            else BuildStoneFloor("Platform", p.pos, p.size, null);
        }

        // A masonry wall: castle stone, a lit corner down the side the candles reach,
        // and — if it's tall enough to carry one — the crimson banner the artwork
        // hangs down every wall of the hall.
        void BuildWall(Rect2 p)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = p.pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.StoneTile;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = p.size;
            sr.sortingOrder = 1;
            var stone = WorldStone;
            sr.color = new Color(stone.r * 0.46f, stone.g * 0.46f, stone.b * 0.50f, 1f);
            go.AddComponent<BoxCollider2D>().size = p.size;   // gameplay untouched

            // A hairline of candlelight down each face, so the column has edges
            // against a near-black backdrop instead of dissolving into it.
            for (int s = -1; s <= 1; s += 2)
            {
                var lit = Theme.Box("WallEdge", go.transform, Vector2.zero,
                    new Vector2(0.055f, p.size.y), new Color(0.46f, 0.40f, 0.46f, 0.40f), 2);
                lit.transform.localPosition = new Vector3(s * (p.size.x / 2f - 0.03f), 0f, 0f);
            }

            // The banner. Seeded off the wall's own position so a given floor always
            // dresses itself the same way — décor that reshuffled every retry would
            // read as flicker on a screen you stare at for fifty attempts.
            if (p.size.y < 2.2f || p.size.x < 0.5f) return;
            var rng = new System.Random(Mathf.RoundToInt(p.pos.x * 61.7f + p.pos.y * 13.1f));
            if (rng.Next(3) == 0) return;                     // not every wall wears one
            float bh = Mathf.Min(2.4f, p.size.y * 0.7f);
            var ban = Theme.SpriteBox("Banner", go.transform,
                new Vector3(p.pos.x, p.pos.y + p.size.y / 2f - bh / 2f - 0.15f, 0f),
                new Vector2(bh * 0.34f, bh), Gothic.Banner, 2);
            ban.transform.localPosition = new Vector3(0f, p.size.y / 2f - bh / 2f - 0.15f, 0f);
        }

        // A vaulted ceiling: dark recessed stone that falls AWAY from the eye, with
        // pointed arch ribs hanging along its underside. Same collider as any
        // platform — this is purely how it reads.
        void BuildCeilingVault(Rect2 p)
        {
            var go = new GameObject("Ceiling");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = p.pos;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = p.size;                       // unchanged — gameplay identical

            // The stone is a CHILD, drawn taller than the ceiling really is so it runs
            // up past the top of the frame. The collider above keeps the true
            // thickness, so nothing about gameplay changes — but the strip of empty
            // black sky that used to sit above every ceiling (a quarter of the screen,
            // in a game whose artwork has masonry right up to the frame) is gone.
            const float Overhead = 5f;
            var vault = new GameObject("Vault");
            vault.transform.SetParent(go.transform, false);
            vault.transform.localPosition = new Vector3(0f, Overhead / 2f, 0f);
            var sr = vault.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.StoneTile;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(p.size.x, p.size.y + Overhead);
            sr.sortingOrder = 1;
            // Measured against the painting rather than argued about. Sampling the
            // reference's ceiling beam gives (26,19,22); this was rendering (68,55,65)
            // — nearly three times too bright — which turned the top quarter of the
            // screen into a pale grey slab and was the single biggest reason the hall
            // read as washed out. The vault is lit by lantern-fire from below, but in
            // the artwork that light only catches the ribs and the lower courses; the
            // mass of it is dark stone against a dark sky.
            sr.color = new Color(0.38f, 0.33f, 0.34f, 1f);

            // A soft underline along the underside so the surface still reads as
            // solid ground when the Chapel flips gravity and you walk on it.
            var lip = Theme.Box("VaultLip", go.transform, Vector2.zero,
                new Vector2(p.size.x, 0.09f), new Color(0.11f, 0.07f, 0.10f, 0.95f), 2);
            lip.transform.localPosition = new Vector3(0f, -p.size.y / 2f + 0.045f, 0f);

            // Gothic ribs hanging off the vault every few units — the detail that
            // turns a flat bar into architecture. Purely decorative (no colliders).
            int ribs = Mathf.Max(1, Mathf.FloorToInt(p.size.x / 4.2f));
            float step = p.size.x / (ribs + 1);
            for (int i = 1; i <= ribs; i++)
            {
                float lx = -p.size.x / 2f + step * i;
                // keystone block
                var key = Theme.Box("Rib", go.transform, Vector2.zero,
                    new Vector2(0.5f, 0.42f), new Color(0.26f, 0.22f, 0.26f, 1f), 2);
                key.transform.localPosition = new Vector3(lx, -p.size.y / 2f - 0.16f, 0f);
                // two short shoulders, stepped down, to suggest a pointed arch
                var shL = Theme.Box("RibL", go.transform, Vector2.zero,
                    new Vector2(0.30f, 0.24f), new Color(0.22f, 0.18f, 0.22f, 1f), 2);
                shL.transform.localPosition = new Vector3(lx - 0.42f, -p.size.y / 2f - 0.07f, 0f);
                var shR = Theme.Box("RibR", go.transform, Vector2.zero,
                    new Vector2(0.30f, 0.24f), new Color(0.22f, 0.18f, 0.22f, 1f), 2);
                shR.transform.localPosition = new Vector3(lx + 0.42f, -p.size.y / 2f - 0.07f, 0f);
            }
        }

        // Shared builder for real platforms AND fake floors so they look IDENTICAL
        // (same stone tile, same blood lip). `trapType` non-null tags it as a trap.
        GameObject BuildStoneFloor(string name, Vector2 pos, Vector2 size, TrapType? trapType)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.StoneTile;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            sr.sortingOrder = 1;
            // The stonework itself takes the world's colour. Backdrops already
            // changed per world, but the FLOOR you stare at for the whole level was
            // the identical grey everywhere — so the crypt and the swamp still felt
            // like the same place. This is the other half of the sameness fix.
            // Halved, because in the artwork the LEDGES are the dark thing in the
            // room and the ceiling is the lit one; the tile is now bright enough
            // that a floor taking it neat would out-glare the walls.
            bool highlandsStone = _mode == Mode.Endless;
            sr.color = highlandsStone
                ? new Color(0.22f, 0.28f, 0.36f, 1f)
                : FloorStone;
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
                var stone = FloorStone;               // extruded sides share the world's rock
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
                    ssr.color = new Color(stone.r * shade[i], stone.g * shade[i], stone.b * shade[i], 1f);
                }
            }
            // Single blood-red lip across the top edge (not tiled into the stone).
            // Parented to the FLOOR (not the level root) so a collapsing fake floor
            // takes its lip down with it — no red line left floating in mid-air.
            // On a huge boss-arena floor a full-width bright red line looked messy, so
            // wide floors get a much subtler, darker, thinner lip.
            bool wide = size.x > 15f;
            Color lipCol = highlandsStone
                ? new Color(0.48f, 0.58f, 0.70f, wide ? 0.52f : 0.82f)
                : wide ? new Color(Theme.PlatEdge.r * 0.5f, Theme.PlatEdge.g * 0.4f, Theme.PlatEdge.b * 0.4f, 0.5f)
                       : Theme.PlatEdge;
            float lipH = highlandsStone ? (wide ? 0.06f : 0.09f) : (wide ? 0.07f : 0.12f);
            var edge = Theme.Box("Edge", go.transform, pos + new Vector2(0, size.y / 2f - 0.06f),
                new Vector2(size.x, lipH), lipCol, 2);
            edge.transform.localPosition = new Vector3(0, size.y / 2f - 0.06f, 0);

            // A pale highlight hairline right under the blood lip. In the artwork every
            // ledge catches a sliver of moonlight along its top face — it's what stops a
            // platform reading as a flat bar and makes the edge you have to land on
            // legible against a near-black backdrop.
            if (!wide)
            {
                var lit = Theme.Box("LitEdge", go.transform, Vector2.zero,
                    new Vector2(size.x, 0.05f), highlandsStone
                        ? new Color(0.62f, 0.72f, 0.82f, 0.48f)
                        : new Color(0.62f, 0.56f, 0.60f, 0.5f), 2);
                lit.transform.localPosition = new Vector3(0, size.y / 2f - 0.135f, 0);
            }

            // Blood running down the face, the way it drips off every ledge in the
            // artwork. Seeded from the platform's own position so a given floor always
            // drips the same way — a level that reshuffled its gore each retry would
            // read as flicker on a screen you're staring at for fifty attempts.
            if (!wide && size.x >= 1.6f && !highlandsStone)
            {
                var rng = new System.Random(Mathf.RoundToInt(pos.x * 73.3f + pos.y * 19.7f));
                int drips = Mathf.Clamp(Mathf.RoundToInt(size.x / 2.6f), 1, 5);
                for (int i = 0; i < drips; i++)
                {
                    float dx = (float)(rng.NextDouble() - 0.5) * (size.x - 0.5f);
                    float len = 0.20f + (float)rng.NextDouble() * 0.45f;
                    var drip = Theme.Box("Drip", go.transform, Vector2.zero,
                        new Vector2(0.09f, len), new Color(0.42f, 0.05f, 0.08f, 0.85f), 2);
                    drip.transform.localPosition = new Vector3(dx, size.y / 2f - 0.10f - len / 2f, 0);
                    // The bead at the bottom of the run, a touch brighter than the trail.
                    var bead = Theme.Box("Bead", go.transform, Vector2.zero,
                        new Vector2(0.14f, 0.13f), new Color(0.55f, 0.06f, 0.10f, 0.9f), 2);
                    bead.transform.localPosition = new Vector3(dx, size.y / 2f - 0.10f - len, 0);
                }
            }

            // Forsaken Highlands: cold ruined masonry with roots and broken support
            // fragments. Decorative children only; collider and trap identity above
            // remain byte-for-byte unchanged.
            if (highlandsStone && !wide && size.x >= 1.6f)
            {
                var rng = new System.Random(Mathf.RoundToInt(pos.x * 73.3f + pos.y * 19.7f));
                int roots = Mathf.Clamp(Mathf.RoundToInt(size.x / 3.2f), 1, 4);
                for (int i = 0; i < roots; i++)
                {
                    float dx = (float)(rng.NextDouble() - 0.5) * (size.x - 0.5f);
                    float len = 0.25f + (float)rng.NextDouble() * 0.55f;
                    var root = Theme.Box("Root", go.transform, Vector2.zero,
                        new Vector2(0.055f, len), new Color(0.08f, 0.11f, 0.10f, 0.92f), 2);
                    root.transform.localPosition = new Vector3(dx, -size.y / 2f - len / 2f + 0.06f, 0f);
                }
                int supports = Mathf.Max(1, Mathf.FloorToInt(size.x / 4.5f));
                for (int i = 0; i < supports; i++)
                {
                    float sx = -size.x * 0.32f + (i + 0.5f) * (size.x * 0.64f / supports);
                    var block = Theme.Box("BrokenSupport", go.transform, Vector2.zero,
                        new Vector2(0.48f, 0.30f), new Color(0.12f, 0.16f, 0.21f, 1f), 1);
                    block.transform.localPosition = new Vector3(sx, -size.y / 2f - 0.13f, 0f);
                }
            }

            if (trapType.HasValue) go.AddComponent<Trap>().Init(trapType.Value);
            return go;
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
                    // The Bestiary calls this the FALSE COFFIN — "the brightest, most
                    // obvious door is death" — and paints it. It used to be the
                    // generic door sprite dyed pink, which matched neither the book
                    // nor the castle.
                    var painted = Assets.TrapArt("fakeexit");
                    var sp = painted ?? Assets.Sprite("door");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("FakeExit", _levelRoot, t.pos,
                                          painted != null ? new Vector2(1.9f, 2.2f) : new Vector2(1.7f, 2.1f), sp, 2)
                        : Theme.Box("FakeExit", _levelRoot, t.pos, t.size, Theme.Trick, 2);
                    if (painted == null && sp != null) go.GetComponent<SpriteRenderer>().color = new Color(1f, 0.45f, 0.5f);
                    // The bait is that it looks WELCOMING, so it keeps a warm glow
                    // behind it either way — the lie is the invitation, not the paint.
                    var lure = Theme.SpriteBox("FakeGlow", go.transform, t.pos, new Vector2(3.0f, 3.0f), Theme.Moon, 1);
                    lure.GetComponent<SpriteRenderer>().color = new Color(1f, 0.72f, 0.35f, 0.16f);
                    var lp = lure.AddComponent<FaintPulse>(); lp.min = 0.09f; lp.max = 0.20f; lp.speed = 1.6f;
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
                    // Visuals are CHILDREN of the exit (not the level root) so a
                    // fleeing coffin takes its body with it instead of leaving an
                    // invisible trigger running around a haunted-looking husk.
                    var cb = Theme.Box("CoffinBack", go.transform, t.pos, new Vector2(1.4f, 2.05f), Theme.Hex("140C08"), 1);
                    cb.transform.localPosition = Vector3.zero;
                    var cf = Theme.Box("Coffin", go.transform, t.pos, new Vector2(1.15f, 1.9f), Theme.Hex("3A2418"), 2);
                    cf.transform.localPosition = Vector3.zero;
                    var cv = Theme.Box("CrossV", go.transform, t.pos, new Vector2(0.18f, 0.95f), Theme.Exit, 3);
                    cv.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                    var ch = Theme.Box("CrossH", go.transform, t.pos, new Vector2(0.62f, 0.18f), Theme.Exit, 3);
                    ch.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                    break;
                }
                case TrapType.SpikeStatic:
                {
                    // The Bestiary's own Iron Spike illustration, drawn as painted —
                    // the old flat sprite needed a red tint to read as lethal.
                    var painted = Assets.TrapArt("spike");
                    var sp = painted ?? Assets.Sprite("spike");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Spikes", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("Spikes", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    // Blood red: the Bestiary's iron is multiplied onto the artwork's
                    // colour, so a spike reads from across the hall (see Trap.SpikeRed).
                    if (sp != null) go.GetComponent<SpriteRenderer>().color = painted != null ? Trap.SpikeRed : Theme.Danger;
                    FitTrigger(go, 0.85f); // reliable: roughly the full visible spike
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Impaled."; kz.trapTag = (int)TrapType.SpikeStatic;
                    break;
                }
                case TrapType.GrowSpike:
                {
                    var painted = Assets.TrapArt("growspike");
                    var sp = painted ?? Assets.Sprite("spike");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("GrowSpike", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("GrowSpike", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    if (sp != null) go.GetComponent<SpriteRenderer>().color = painted != null ? Trap.SpikeRed : Theme.Danger;
                    FitTrigger(go, 0.85f); // reliable spike hitbox
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Skewered.";
                    var gtrap = go.AddComponent<Trap>();
                    gtrap.paintedArt = painted != null;
                    gtrap.Init(TrapType.GrowSpike);
                    break;
                }
                case TrapType.Checkpoint:
                {
                    var go = new GameObject("Checkpoint");
                    go.transform.SetParent(_levelRoot, false);
                    go.transform.position = t.pos;
                    var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true;
                    col.size = new Vector2(1.2f, 1.6f);
                    // A candle on an iron stand — the castle's own way of marking a
                    // place. It used to be an ice-blue pole with a green flag, the
                    // last two colours in the game that belonged to another palette.
                    Theme.Box("Stand", go.transform, t.pos + new Vector2(0f, 0.05f),
                        new Vector2(0.12f, 1.3f), new Color(0.20f, 0.17f, 0.22f, 1f), 2);
                    Theme.Box("Foot", go.transform, t.pos + new Vector2(0f, -0.58f),
                        new Vector2(0.62f, 0.14f), new Color(0.26f, 0.22f, 0.26f, 1f), 2);
                    Theme.Box("Wax", go.transform, t.pos + new Vector2(0f, 0.72f),
                        new Vector2(0.26f, 0.44f), new Color(0.72f, 0.14f, 0.18f, 1f), 3);
                    var flame = Theme.SpriteBox("Flame", go.transform, t.pos + new Vector2(0f, 1.02f),
                        new Vector2(0.26f, 0.34f), Theme.Moon, 4);
                    flame.GetComponent<SpriteRenderer>().color = new Color(1f, 0.82f, 0.42f, 0.95f);
                    var cf = flame.AddComponent<FaintPulse>(); cf.min = 0.7f; cf.max = 1f; cf.speed = 7f;
                    var halo = Theme.SpriteBox("CheckGlow", go.transform, t.pos + new Vector2(0f, 0.9f),
                        new Vector2(2.4f, 2.4f), Theme.Moon, 1);
                    halo.GetComponent<SpriteRenderer>().color = new Color(1f, 0.68f, 0.28f, 0.16f);
                    go.AddComponent<Trap>().Init(TrapType.Checkpoint);
                    break;
                }
                case TrapType.Reverse:
                {
                    // THE HEX OF CONFUSION. The Bestiary paints it as a spiral rune
                    // and tells you to invert your instincts — but in the level it was
                    // an invisible sensor, so the only way to learn it was to walk
                    // into nothing and lose your hands. Now the rune is on the floor,
                    // turning slowly, exactly as the book draws it.
                    var painted = Assets.TrapArt("reverse");
                    GameObject go = painted != null
                        ? Theme.SpriteBox("Reverse", _levelRoot, t.pos, new Vector2(1.3f, 1.3f), painted, 2)
                        : Theme.Box("Reverse", _levelRoot, t.pos, t.size, Theme.Trick, 2);
                    if (painted != null) go.AddComponent<Spinner>().speed = 26f;   // a slow, wrong turn
                    Theme.AddTrigger(go, painted != null ? Vector2.one * 0.7f : Vector2.one);
                    go.AddComponent<Trap>().Init(TrapType.Reverse);
                    break;
                }
                case TrapType.BreakBlock:
                {
                    // A cracked stone block you must SHOOT. It was lavender "candy",
                    // the one survivor of the pre-vampire palette, sitting in a hall
                    // of blood and masonry.
                    var go = Theme.Box("BreakBlock", _levelRoot, t.pos, t.size, new Color(0.34f, 0.29f, 0.34f, 1f), 2);
                    var bsr = go.GetComponent<SpriteRenderer>();
                    bsr.sprite = Theme.StoneTile;
                    bsr.drawMode = SpriteDrawMode.Tiled;
                    bsr.size = t.size;
                    go.transform.localScale = Vector3.one;      // tiled mode sizes it, not scale
                    Theme.AddSolid(go).size = t.size;
                    // The crack that says "this one is different" — a blood-red fissure
                    // down its face, so a shootable block reads without a colour code.
                    var crack = Theme.Box("Crack", go.transform, Vector2.zero,
                        new Vector2(0.10f, t.size.y * 0.72f), new Color(0.62f, 0.09f, 0.13f, 0.9f), 3);
                    crack.transform.localPosition = Vector3.zero;
                    go.AddComponent<Breakable>();
                    break;
                }
                case TrapType.Spring:
                {
                    var sp = Assets.TrapArt("spring") ?? Assets.Sprite("trampoline");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Spring", _levelRoot, t.pos, new Vector2(t.size.x, 0.7f), sp, 3)
                        : Theme.Box("Spring", _levelRoot, t.pos, t.size, Theme.Coin, 3);
                    FitTrigger(go, 0.8f);
                    go.AddComponent<Trap>().Init(TrapType.Spring);
                    break;
                }
                case TrapType.Saw:
                {
                    // The Bestiary's Whirling Blade is one painted disc, so it spins by
                    // ROTATION; the older imported saw is an 8-frame strip the Trap
                    // cycles instead. Either way it also slides along its track.
                    var painted = Assets.TrapArt("saw");
                    var frames = painted != null ? null : Assets.Sheet("saw", 38);
                    var sp = painted ?? ((frames != null && frames.Length > 0) ? frames[0] : null);
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Saw", _levelRoot, t.pos, new Vector2(1.25f, 1.25f), sp, 3)
                        : Theme.Box("Saw", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    if (painted != null) go.AddComponent<Spinner>().speed = 320f;
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
                    var painted = Assets.TrapArt("warpback");
                    var sp = painted ?? Assets.Sprite("portal");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("WarpBack", _levelRoot, t.pos,
                                          painted != null ? new Vector2(1.5f, 1.5f) : new Vector2(1.3f, 2f), sp, 2)
                        : Theme.Box("WarpBack", _levelRoot, t.pos, new Vector2(1f, 1.8f), Theme.Trick, 2);
                    var wsr = go.GetComponent<SpriteRenderer>();
                    if (painted == null) wsr.color = new Color(0.55f, 0.2f, 0.8f, 0.85f);   // necro-purple swirl
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
                    var painted = Assets.TrapArt("flamejet");
                    var sp = painted ?? Assets.Sprite("flame");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("FlameJet", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("FlameJet", _levelRoot, t.pos, t.size, Theme.Hex("FF7A1A"), 3);
                    FitTrigger(go, 0.8f);
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Burned by the flame jet.";
                    var ftrap = go.AddComponent<Trap>();
                    ftrap.paintedArt = painted != null;   // telegraph by brightness, not by dye
                    ftrap.Init(TrapType.FlameJet);
                    break;
                }
                case TrapType.HolyWater:
                {
                    var painted = Assets.TrapArt("holywater");
                    var sp = painted ?? Assets.Sprite("holywater");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("HolyWater", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("HolyWater", _levelRoot, t.pos, t.size, new Color(0.5f, 0.8f, 0.95f, 0.5f), 3);
                    FitTrigger(go, 0.9f);
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Burned by holy water.";
                    var wtrap = go.AddComponent<Trap>();
                    wtrap.paintedArt = painted != null;
                    wtrap.Init(TrapType.HolyWater);
                    break;
                }
                case TrapType.BatSwoop:
                {
                    // The Bestiary's own Swooping Bat, so what the page shows is what
                    // hangs off the rafters. It rides in untinted and only flares red
                    // on the wind-up — which is precisely the dodge cue the page
                    // promises. (The old pixel flap-sheet stands in if the cut-out
                    // ever goes missing.)
                    var painted = Assets.TrapArt("bat");
                    var frames = painted != null ? null : Assets.Sheet("bat_fly", 32);
                    var sp = painted ?? ((frames != null && frames.Length > 0) ? frames[0] : Theme.Bat);
                    var go = Theme.SpriteBox("Bat", _levelRoot, t.pos,
                        painted != null ? new Vector2(1.25f, 0.95f) : new Vector2(0.95f, 0.95f), sp, 4);
                    go.AddComponent<BatEnemy>().Init(frames, painted != null);
                    break;
                }
                default: // LateSpike / Crusher / Surprise / Dart / Faller / Chandelier = invisible sensors
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
            // Stage floors: death restarts the CURRENT stage, not the floor —
            // banked stages stay banked (the Level Devil loop). Every stage opens
            // on solid ground, so entry + 1.3 always lands on its first platform.
            if (_level.Rooms.Count > 0 && _stageIndex > 0)
            {
                var r = _level.Rooms[Mathf.Min(_stageIndex, _level.Rooms.Count - 1)];
                spawnAt = new Vector3(r.MinX + 1.3f, -2f, 0f);
            }
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

            // Phones show a whole ~20-27-unit stage on a palm-sized screen, which
            // left the character reading as a speck (APK feedback: "sprite too
            // small"). Grow the VISUAL only — the collider is untouched, so every
            // hazard hitbox and every JumpArcProbe-tuned gap behaves identically;
            // the hurtbox just sits a little inside the art, which errs in the
            // player's favour.
            float vk = Application.isMobilePlatform ? 1.2f : 1f;

            if (firstFrame != null)
            {
                var b = new GameObject("Body");
                b.transform.SetParent(go.transform, false);
                // Vampire frames have shadow/padding at the bottom; nudge down so
                // the character's feet sit on the floor, not floating above it.
                // The enlarged mobile sprite is re-anchored about the FEET (the
                // collider bottom at local y≈-0.445) so scaling up doesn't sink
                // the boots into the floor.
                const float footY = -0.445f;
                float baseNudge = useVamp ? -0.12f : 0f;
                b.transform.localPosition = new Vector3(0f, baseNudge - (footY - baseNudge) * (vk - 1f), 0f);
                bodySr = b.AddComponent<SpriteRenderer>();
                bodySr.sprite = firstFrame;
                bodySr.color = WardrobeCosmetics.PlayerTint(Skins.Shade(skin));
                bodySr.sortingOrder = 5;
                float h = firstFrame.bounds.size.y;
                float s = (h > 0.0001f ? 1.35f / h : 1f) * vk;
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
            _player.visualMul = vk;   // bat form reads its size from this too
            // Roomed floors are precision mode: glide and double-jump are
            // suppressed so the dark-bridge gaps and the coffin chase can't be
            // flown over (see Level.PrecisionPlatforming). Corridor floors
            // (11-40 / Endless / Blood Moon) keep both.
            bool precision = _level.PrecisionPlatforming;
            _player.canFly = !precision;
            // Skin-granted abilities (dash / double-jump / speed / phase).
            _player.moveMul = skin.moveMul;
            _player.jumpMul = skin.jumpMul;
            _player.dashEnabled = skin.dash;
            _player.extraAirJumps = precision ? 0 : skin.airJumps;
            WardrobeCosmetics.AttachAura(go);
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
                _rig.SetFrame(Mathf.Lerp(_rig.FrameX, x, 10f * Time.unscaledDeltaTime), CamY, NormalCamSize);
            }
        }

        void Update()
        {
            // Orientation prompt is now a WEB-ONLY fallback. The native app locks
            // itself to landscape (Player Settings + the Screen.orientation calls in
            // Awake), so on Android/iOS it simply opens sideways like any other game
            // and this panel can never trigger. A phone BROWSER can't be force-
            // rotated reliably, so WebGL keeps the prompt as a last resort.
            if (_isMobile && Application.platform == RuntimePlatform.WebGLPlayer)
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
            UpdateTrollButtons();   // Versus sabotage column: visibility + cooldowns

            // The pause button rides the same rule as the rest of the play HUD:
            // visible whenever the player is actually in a level (so there is
            // always a route back to the menu), hidden on menus and result screens.
            if (_pauseBtn != null)
            {
                bool showPause = _state == State.Play;
                if (_pauseBtn.gameObject.activeSelf != showPause)
                    _pauseBtn.gameObject.SetActive(showPause);
            }
            // The stone-and-gold picture frame follows the same rule — it belongs to
            // the level, not to the menus (which bring their own frames).
            if (_levelFrame != null)
            {
                bool showFrame = _state == State.Play || _state == State.Paused;
                if (_levelFrame.activeSelf != showFrame) _levelFrame.SetActive(showFrame);
            }

            // Desktop convenience: 1/2/3 fire the three sabotage trolls in a race.
            if (_mode == Mode.Versus && _state == State.Play)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) FireTroll(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) FireTroll(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) FireTroll(2);
            }

            // Desktop Esc AND the Android hardware BACK key both arrive here.
            if (Input.GetKeyDown(KeyCode.Escape)) HandleBackButton();

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

            if (_state == State.Play && _mode == Mode.Endless && _player != null && !_dying)
                TrackEndlessRun();

            // The Chapel's mirror of the pit: inverted gravity + an open ceiling
            // means you can fall UP out of the room. The sky is just as lethal.
            if (_state == State.Play && _player != null && !_dying &&
                _level != null && _level.HasGravity &&
                _player.transform.position.y > B.CeilY + 3.6f)
                Die("You fell into the sky.");

            UpdateCalledIt();   // pay out any survived trap-fires

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
                        new Vector2(BarFillW * Mathf.Clamp01(_player.flightMeter), 26f);
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

        // ==================== rooms ====================
        // Roomed floors used to get a locked, zoomed-out camera per chamber
        // (Level Devil style — one static shot per stage). That's gone: the
        // camera now just follows the player down the whole run like every
        // other floor (see SnapCamera/LateUpdate), which is what makes a
        // roomed level feel continuous instead of a chain of separate screens.

        public void RoomToast(string msg) { if (_toast != null) StartCoroutine(FlashToast(msg)); }

        // The lullaby's voice. Scold lines rotate and are throttled so a
        // button-masher gets a steady drip of mockery, not a strobe.
        static readonly string[] SleepScolds =
        {
            "You are asleep right now.",
            "Struggling only deepens the slumber.",
            "Shhh. The castle is rocking you.",
            "Stop. Mashing. Sleep.",
            "Every button you press is another sheep.",
        };
        float _scoldNext; int _scoldIx;
        public void SleepStart() => RoomToast("The castle sings you to sleep… (be still)");
        public void SleepWake()  => RoomToast("…you wake up.");
        public void SleepScold()
        {
            if (Time.unscaledTime < _scoldNext) return;
            _scoldNext = Time.unscaledTime + 0.8f;
            RoomToast(SleepScolds[_scoldIx++ % SleepScolds.Length]);
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

        void TrackEndlessRun()
        {
            float local = Mathf.Clamp(_player.transform.position.x - _level.Spawn.x,
                0f, Mathf.Max(0f, _levelEndX - _level.Spawn.x));
            _endlessPeakMeters = Mathf.Max(_endlessPeakMeters, _endlessBankedMeters + local);

            int metres = CurrentEndlessMeters;
            if (metres != _endlessLastHudMeters)
            {
                _endlessLastHudMeters = metres;
                UpdateHud();
            }

            // No coffin, door, score break or floor-complete screen: reaching the
            // last safe pad quietly streams the next bounded procedural chunk.
            if (_player.transform.position.x >= _levelEndX - 1.1f)
            {
                ReachExit();
                return;
            }

            // Keep a short monotonic trail of grounded, hazard-clear positions.
            // A revive selects the newest sample at least 2.5m behind the death.
            if (!_player.IsGrounded || !EndlessRespawnClear(_player.transform.position.x)) return;
            if (_endlessSafeHistory.Count == 0 ||
                _player.transform.position.x >= _endlessSafeHistory[_endlessSafeHistory.Count - 1].x + 0.65f)
                _endlessSafeHistory.Add(_player.transform.position);
        }

        bool EndlessRespawnClear(float x)
        {
            bool grounded = _level.Platforms.Exists(p =>
                Mathf.Abs(p.pos.y + 3f) < 0.6f && x >= p.pos.x - p.size.x / 2f + 0.45f &&
                x <= p.pos.x + p.size.x / 2f - 0.45f);
            if (!grounded) return false;
            foreach (var t in _level.Traps)
                if (t.type != TrapType.RealExit && Mathf.Abs(t.pos.x - x) < 2.0f) return false;
            return true;
        }

        void PrepareEndlessRevive(float deathX)
        {
            Vector3 safe = _level.Spawn;
            float target = deathX - 2.5f;
            for (int i = _endlessSafeHistory.Count - 1; i >= 0; i--)
                if (_endlessSafeHistory[i].x <= target) { safe = _endlessSafeHistory[i]; break; }
            safe.y = -2f;
            _checkpoint = safe + Vector3.up * 0.25f;
            _hasCheckpoint = true;
        }

        public void CollectEndlessLife(GameObject pickup)
        {
            if (_mode != Mode.Endless || _state != State.Play ||
                _endlessLifeClaimed.Contains(_levelIndex)) return;
            _endlessLifeClaimed.Add(_levelIndex);
            _hearts = Mathf.Min(3, _hearts + 1);
            Audio.PlayOr("levelup", "win", 0.65f);
            if (pickup != null) Fx.Burst(pickup.transform.position, Theme.Exit, 16, 5f, 0.15f, 0.45f, 3f);
            RoomToast("EXTRA LIFE BANKED — your next death rewinds");
            Analytics.Track("endless_life_collected", new System.Collections.Generic.Dictionary<string, object>
            {
                { "distance", CurrentEndlessMeters }, { "lives", _hearts },
            });
            UpdateHud();
        }

        void SpawnEndlessLifePickup()
        {
            // One deterministic opportunity every third chunk. The pickup sits on
            // a small elevated ledge: the safe route continues below, while the
            // player must commit to a jump/glide line to earn the revive.
            if (_endlessLifeClaimed.Contains(_levelIndex) || _levelIndex % 3 != 1 ||
                _level.Platforms.Count < 5) return;
            Rect2 chosen = _level.Platforms[_level.Platforms.Count * 2 / 3];
            for (int i = _level.Platforms.Count * 2 / 3; i < _level.Platforms.Count - 1; i++)
                if (_level.Platforms[i].size.x >= 3.6f) { chosen = _level.Platforms[i]; break; }

            Vector2 ledgePos = new Vector2(chosen.pos.x, 0.05f);
            BuildStoneFloor("RiskLifeLedge", ledgePos, new Vector2(2.2f, 0.45f), null);
            var go = new GameObject("EndlessLife");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = ledgePos + Vector2.up * 0.95f;
            var sp = Gothic.Heart;
            GameObject visual = sp != null
                ? Theme.SpriteBox("Heart", go.transform, go.transform.position,
                    new Vector2(0.8f, 0.8f), sp, 6)
                : Theme.Box("Heart", go.transform, go.transform.position,
                    new Vector2(0.65f, 0.65f), Theme.Exit, 6);
            var col = go.AddComponent<CircleCollider2D>(); col.isTrigger = true; col.radius = 0.55f;
            go.AddComponent<EndlessLifePickup>().visual = visual.transform;
            var label = new GameObject("RiskLabel").AddComponent<TextMesh>();
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            label.text = "RISK PATH  +1 LIFE"; label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center; label.characterSize = 0.055f; label.fontSize = 44;
            label.color = Theme.Coin;
        }

        IEnumerator FlashToast(string msg)
        {
            if (_toast == null) yield break;
            _toast.text = msg;
            yield return new WaitForSecondsRealtime(0.9f);
            if (_toast != null && _toast.text == msg) _toast.text = "";
        }

        // ==================== "CALLED IT" — the prediction economy ====================
        // Dodging a trap you TRIGGERED used to feel identical to never touching
        // it. Now a reactive trap that fires and fails to kill you within the
        // window pays a shard and shouts about it — reading the room becomes a
        // visible, paid skill, retroactively on every reactive trap in the game.
        // Paid once per trap spot per floor-visit: the paid set survives the
        // death-rebuild, so die-retrigger-dodge can't be farmed.
        readonly System.Collections.Generic.HashSet<string> _calledPaid = new();
        struct PendingCall { public string key; public float due; public Vector3 pos; }
        readonly System.Collections.Generic.List<PendingCall> _pendingCalls = new();
        const float CalledItWindow = 1.1f;   // must outlive the trap's whole lethal beat

        // Traps report the moment they irrevocably fire (spike up, floor gone,
        // dart loosed, block slammed). Survival is judged CalledItWindow later.
        public void TrapFired(TrapType type, Vector3 pos)
        {
            if (_state != State.Play || _dying) return;
            string key = $"{ModeName}:{_levelIndex}:{(int)type}:{pos.x:0.0}";
            if (_calledPaid.Contains(key)) return;
            foreach (var p in _pendingCalls) if (p.key == key) return;
            _pendingCalls.Add(new PendingCall { key = key, due = Time.time + CalledItWindow, pos = pos });
        }

        void UpdateCalledIt()
        {
            if (_pendingCalls.Count == 0) return;
            // A death voids every open call — the trap won, nothing was "called".
            if (_state != State.Play || _dying) { _pendingCalls.Clear(); return; }
            for (int i = _pendingCalls.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pendingCalls[i].due) continue;
                var call = _pendingCalls[i];
                _pendingCalls.RemoveAt(i);
                _calledPaid.Add(call.key);
                Currency.Earn(1, "called_it");
                ShardFloater.SpawnText(call.pos + Vector3.up * 0.6f, "CALLED IT!", Theme.Coin);
                ShardFloater.Spawn(call.pos, 1);
                Audio.PlayOr("called", "levelup", 0.4f);
            }
        }

        public void WarpToStart()
        {
            if (_state != State.Play || _player == null) return;
            // Drag back to the last checkpoint, not the level start, when one is
            // set. Warping past a checkpoint made deliberately DYING (which
            // respawns at the checkpoint) strictly better than obeying the trap —
            // so the rune taught suicide. Now it never sends you further back than
            // a death would, so it's a real punishment instead of a bad joke.
            Vector3 dest = (_hasCheckpoint && _checkpoint.x > _level.Spawn.x) ? _checkpoint : _level.Spawn;
            _player.transform.position = dest;
            _player.SetGravityDir(1);   // spawns/checkpoints are floor-side; never warp in upside-down
            Audio.Play("portal", 0.5f);
            // A toast — NOT the red death flash — so it reads as "the rune dragged
            // you back", not a death (it doesn't cost a death).
            if (_toast != null) StartCoroutine(FlashToast("The rune drags you back…"));
        }

        public void Die(string msg = null)
        {
            if (_state != State.Play || _dying) return;

            _dying = true;
            _deaths++;
            // A short rumble on death — on a phone the screen shake alone is easy to
            // miss mid-thumb. Opt-out lives in Settings > RUMBLE.
            // (Handheld only exists on the phone platforms — a desktop/dev build
            // won't compile against it at all, hence the guard as well as the check.)
#if UNITY_ANDROID || UNITY_IOS
            if (Application.isMobilePlatform && Options.Haptics)
                Handheld.Vibrate();
#endif
            _floorDeaths++;
            // Persist the tally PER FLOOR so the castle map can show what each
            // one cost you. Level Devil's map does this and it's most of why
            // scrolling back through it is fun: the number is a scar, and a
            // floor that took you 40 tries is a story you can point at.
            if (_mode == Mode.Curated) AddFloorDeath(_levelIndex);
            Vector2 deathPos = PlayerTransform != null ? (Vector2)PlayerTransform.position : Vector2.zero;
            if (_mode == Mode.Endless && _level != null)
                _endlessPeakMeters = Mathf.Max(_endlessPeakMeters, _endlessBankedMeters +
                    Mathf.Clamp(deathPos.x - _level.Spawn.x, 0f, Mathf.Max(0f, _levelEndX - _level.Spawn.x)));
            _endlessRevivePending = _mode == Mode.Endless && _hearts > 0;
            if (_endlessRevivePending) PrepareEndlessRevive(deathPos.x);
            Analytics.Track("death", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
                { "stage", _stageIndex },      // which sub-level the floor kills at — the new tuning dial
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
                            deathPos, msg ?? "unknown");
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

            // Death keeps concise physical feedback from the hazard itself. There
            // is deliberately no narrator, insult, spoken roast, or death caption:
            // the castle only speaks during rare story milestones now.
            string cause = Juice.Categorize(msg);
            Audio.PlayOr(Juice.DeathSfx(cause), "death", 1f);
            FlashRed();
            StartCoroutine(HitStop(0.08f));        // a punchy freeze-frame on impact
            UpdateHud();
            RecordReactiveTrap();                  // the game LEARNS where you felt safe
            // Every 10th death, dangle the next unlock — the moment a bored player
            // quits is exactly when the shop should whisper. Rides the hint bar so
            // the roast toast (and the instant retry) stay untouched.
#if false
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
#endif
            StartCoroutine(DieRoutine());
        }

        IEnumerator DieRoutine()
        {
            // Snapshot this attempt's path so the next try races it as a ghost.
            if (_recP.Count > 1) { _lastT = _recT.ToArray(); _lastP = _recP.ToArray(); }
            Vector3 deathPos = _player != null ? _player.transform.position : Vector3.zero;
            if (_player != null)
            {
                Fx.Explosion(deathPos, 1.7f);     // a quick blast under the gore
                // Settings > BLOOD SPATTER. The blast and the death animation stay —
                // you still need to see that you died — but the gore comes off.
                if (Options.Spatter)
                {
                    GoreBurst(deathPos);
                    BloodSplash(deathPos);
                }
                _player.PlayDeath();
                _player.Freeze();
            }
            if (Options.Shake) StartCoroutine(Juice.Shake(_cam.transform, 0.45f, 0.22f));
            CinematicPunch(deathPos, 0.18f, 0.3f);   // 2.5D: a quick lean toward the kill
            // NEAR-INSTANT retry — the heart of the "just one more try" loop.
            yield return new WaitForSecondsRealtime(0.18f);
            Destroy(_levelRoot.gameObject);
            if (_mode == Mode.Endless)
            {
                if (_endlessRevivePending)
                {
                    _endlessRevivePending = false;
                    BuildLevel();
                }
                else RunOver();
                yield break;
            }
            if (_hearts == 0)
            {
                // Endless never hard-ends on lives — drop back to the last checkpoint
                // segment with a fresh pool and keep going. Blood Moon now AUTO-RESTARTS
                // from night 1 (no trip to the menu to re-pick it — the #1 friction
                // complaint); every other heart mode still ends on a result screen.
                if (_mode == Mode.Daily) BloodMoonRestart();
                else RunOver();
            }
            else BuildLevel();
        }

        // Blood Moon: out of lives → loop straight back to night 1 with a fresh
        // pool of lives, no menu round-trip. Testers were bouncing off the mode
        // and then having to re-select it from the menu every single time; this
        // keeps the "just one more try" loop unbroken. Deaths keep accumulating
        // (the mode still scores on fewest-deaths-to-clear, so a sloppy loop
        // honestly costs you), and it stays tonight's shared seed.
        void BloodMoonRestart()
        {
            _levelIndex = 0;
            _hearts = Diff.StartHearts + 2;   // same generous cushion as a fresh start
            _hasCheckpoint = false;
            ResetFloorState();
            Audio.Play("levelup", 0.5f);
            ShowBanner("THE NIGHT RESETS",
                       $"back to night 1 • {_hearts} fresh lives • trust nothing");
            BuildLevel();
        }

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
            // Roomed floors keep their hazards in SEPARATE lists that this used to
            // be blind to — so a learned spike could bank right at a gated doorway
            // and wall the floor off (a forced standing start into a spike you
            // can't clear). Exclude all of them.
            foreach (var d in _level.Doorways)
                if (Mathf.Abs(d - x) < 2.6f) return false;            // doorways / gates / loop runes: chokepoints
            foreach (var sr in _level.SleepRunes)
                if (Mathf.Abs(sr.x - x) < 2.2f) return false;
            foreach (var gr in _level.GravRunes)
                if (Mathf.Abs(gr.x - x) < 2.2f) return false;   // a spike on a rune = forced death
            foreach (var ss in _level.ShiftSpikes)
                if (Mathf.Abs(ss.x - x) < 2.2f || Mathf.Abs(ss.y - x) < 2.2f) return false;
            return true;
        }

        // Out of hearts in Endless/Daily — end the run and show the result.
        void RunOver()
        {
            Memory.RunEndedCleanly();   // reached a result screen = not a rage-quit
            _state = State.Win;
            int endlessMetres = CurrentEndlessMeters;
            if (_mode == Mode.Endless && endlessMetres > PlayerPrefs.GetInt("best_endless_distance", 0))
            { PlayerPrefs.SetInt("best_endless_distance", endlessMetres); PlayerPrefs.Save(); _newBest = true; }
            Analytics.Track("run_end", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "final_level_index", _levelIndex }, { "distance", endlessMetres },
                { "total_deaths", _deaths },
                { "reason", "out_of_lives" },
            });
            Audio.Play("death", 0.7f);

            var panel = Overlay(new Color(0.05f, 0f, 0.02f, 0.85f), out var root);
            ResultTitle(root, "YOU PERISHED", 200f, 84);
            string reached = _mode == Mode.Endless ? $"survived {endlessMetres} metres"
                                                    : $"fell on night {_levelIndex + 1}/{DailyLen}";
            Gothic.Line(root, reached + $"   ·   {_deaths} deaths", 46, Gothic.Bone,
                new Vector2(0, 90), new Vector2(1400, 70));

            string lbMode = _mode == Mode.Endless ? "endless" : "daily";
            Leaderboard.Submit(lbMode, _mode == Mode.Endless ? endlessMetres : _deaths);
            string brag = _mode == Mode.Endless
                ? $"I survived {endlessMetres} METRES in Endless Night in Trust Issues \U0001F987 — beat that"
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

            // Custom map cleared: stop the clock, bank the best, offer the challenge.
            if (_mode == Mode.Custom)
            {
                float secs = Time.realtimeSinceStartup - _customStart;
                bool best = CustomMap.RecordTime(_customCode, secs);
                Analytics.Track("custom_clear", new System.Collections.Generic.Dictionary<string, object>
                { { "seconds", secs }, { "deaths", _floorDeaths }, { "best", best } });
                _state = State.Win;
                Audio.Play("win", 0.7f);
                ShowCustomResult(secs, best);
                return;
            }

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
            if (_hearts >= 0 && _mode != Mode.Endless)
                _hearts = Mathf.Min(Diff.MaxHearts, _hearts + 1); // Endless lives only come from risk paths

            // Floor cleared → shards: +10 base, +15 more the FIRST time a Castle
            // floor falls, +5 for the under-5-deaths goal. Paid before the mode
            // branches so every non-Versus mode earns the same way.
            {
                bool firstClear = _mode == Mode.Curated &&
                    (_levelIndex + 1 == Levels.Count ? !Badges.Has("castle_clear")
                                                     : _levelIndex >= CastleUnlocked);
                // Rebalanced upward. The old rate (10 a floor) put the cheapest
                // charm 15 clears away and the best one 60 clears away — far past
                // where anyone was still playing, which is why the currency felt
                // like it did nothing. Clearing is now clearly the best income,
                // first clears pay a real bounty, and every 5th floor pays a
                // milestone so long sessions keep landing rewards.
                int shardPay = 15 + (firstClear ? 25 : 0) + (_floorDeaths < 5 ? 10 : 0);
                if (firstClear && (_levelIndex + 1) % 5 == 0) shardPay += 50;   // milestone floor
                Currency.Earn(shardPay, firstClear ? "first_clear" : "floor_clear");
                if (_player != null) ShardFloater.Spawn(_player.transform.position, shardPay);
            }

            if (_mode == Mode.Endless)
            {
                // The automatic hand-off fires 1.1m before the platform edge, so
                // bank only ground the player actually crossed.
                float chunkMetres = Mathf.Max(0f, (_levelEndX - 1.1f) - _level.Spawn.x);
                _endlessBankedMeters += chunkMetres;
                _endlessPeakMeters = Mathf.Max(_endlessPeakMeters, _endlessBankedMeters);
                _levelIndex++;
                PlayerPrefs.SetInt("best_endless", Mathf.Max(_levelIndex, PlayerPrefs.GetInt("best_endless", 0))); // legacy unlock compatibility
                int metres = CurrentEndlessMeters;
                if (metres > PlayerPrefs.GetInt("best_endless_distance", 0))
                { PlayerPrefs.SetInt("best_endless_distance", metres); _newBest = true; }
                PlayerPrefs.Save();
                if (metres >= 500) Badges.Award("endless10");
                if (metres >= 1000) Badges.Award("endless20");
                // Post the distance as it is BANKED, not only when the run ends.
                // An Endless run that gets quit out of (or backgrounded, or killed
                // by the OS) used to never reach the result screen, so the best
                // distance of the session was simply never ranked.
                Leaderboard.Submit("endless", metres);
                StartCoroutine(ContinueEndless());
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
            // Curated — a floor win no longer chains straight into the next one.
            // Auto-advancing meant a "cleared it" moment and a "here's a harder
            // one" moment happened in the same breath, so neither registered.
            // The player is dropped back on the Castle map and has to tap the
            // newly-unlocked seal themselves — the beat where the win lands.
            if (_levelIndex + 1 < Levels.Count)
            {
                int clearedFloor = _levelIndex + 1;
                RecordFloorBest(_levelIndex, LevelDurationMs / 1000f);
                _levelIndex++;
                PlayerPrefs.SetInt("ti_level", _levelIndex);
                UnlockCastle(_levelIndex);     // beating a floor unlocks the next
                PlayerPrefs.Save();
                if (IsStoryMilestone(clearedFloor) &&
                    PlayerPrefs.GetInt($"ti_story_seen_{clearedFloor}", 0) == 0)
                    StartCoroutine(StoryInterlude(clearedFloor));
                else
                    StartCoroutine(FloorClearedFlash());
            }
            else { RecordFloorBest(_levelIndex, LevelDurationMs / 1000f);
                   UnlockCastle(Levels.Count - 1); Badges.Award("castle_clear"); TrackRunComplete(); _state = State.Win; Audio.Play("win", 0.7f); StartCoroutine(WinRoutine()); }
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
                _toast.text = _mode == Mode.Daily ? $"NIGHT {_levelIndex + 1}" : $"LEVEL {_levelIndex + 1}";
            yield return new WaitForSecondsRealtime(0.9f);
            if (_toast != null) _toast.text = "";
            Destroy(_levelRoot.gameObject);
            _hasCheckpoint = false;
            ResetFloorState();          // new floor — clear section checkpoint + learned traps
            _state = State.Play;
            BuildLevel();
        }

        IEnumerator ContinueEndless()
        {
            // A tiny generation seam, not a score/floor beat. The player never
            // leaves play and no floor number or completion overlay is shown.
            _state = State.Win;
            yield return new WaitForSecondsRealtime(0.08f);
            if (_levelRoot != null) Destroy(_levelRoot.gameObject);
            _hasCheckpoint = false;
            _endlessSafeHistory.Clear();
            ResetFloorState();
            _state = State.Play;
            BuildLevel();
        }

        // The Castle tells its story rarely, after a meaningful stretch of play,
        // rather than heckling the player on every death. These moments happen
        // only on the first clear of each milestone and never interrupt a retry.
        static bool IsStoryMilestone(int floor) =>
            floor == 10 || floor == 18 || floor == 26 || floor == 33 || floor == 38;

        string StoryDeathAside()
        {
            if (_deaths == 0)
                return "He has done it without dying once. I dislike him already.";
            if (_deaths < 25)
                return $"He has died only {_deaths} time{(_deaths == 1 ? "" : "s")}. The castle finds this personally offensive.";
            if (_deaths < 68)
                return $"We prepared sixty-eight graves for him. He has used only {_deaths}. How inconsiderate.";
            if (_deaths < 100)
                return $"He has fallen {_deaths} times - fewer than the hundred graves prepared in his name.";
            return $"He has died {_deaths} times. Most creatures would call that a warning. He has mistaken it for directions.";
        }

        static string StoryTitle(int floor)
        {
            switch (floor)
            {
                case 10: return "TEN FLOORS BELOW";
                case 18: return "THE CASTLE LISTENS";
                case 26: return "STONE DESCENDS";
                case 33: return "NO LONGER A GUEST";
                case 38: return "TWO FLOORS REMAIN";
                default: return "THE CASTLE SPEAKS";
            }
        }

        string StoryText(int floor)
        {
            string deaths = StoryDeathAside();
            switch (floor)
            {
                case 10:
                    return "The castle had already chosen an ending for this one. A nameless young vampire walks in, trusts the first stone, and becomes a stain beneath it. Yet here he stands, ten floors below the moon, still carrying the irritating habit of getting back up. " + deaths + " The castle did not expect him to reach this door. Neither did I.";
                case 18:
                    return "He has learned the rhythm now: the pause before the spike, the breath before the gate. Death has stopped sending him away. It only teaches him the next lie. " + deaths + " That is troublesome. A castle can frighten a visitor. It is much harder to frighten someone who has begun to understand it.";
                case 26:
                    return "The crypt tried weight. Stone, iron, and a ceiling descending like a verdict. He ran beneath it and survived. " + deaths + " Somewhere above, old hinges are waking. The castle is no longer playing with him. It is preparing for him.";
                case 33:
                    return "Thirty-three floors. He no longer looks like a guest. The gates recognize his footsteps. The dead have begun leaving room when he passes. " + deaths + " I once thought this was a story about a foolish vampire trying to conquer a castle. I may have mistaken which one was being conquered.";
                case 38:
                    return "Only two floors remain. Do not celebrate him yet. Hope is simply the castle's last trap, and he is standing exactly where it wants him. Still... " + deaths + " He has paid for every lesson in blood and remembered almost all of them. If he reaches the throne, the thing waiting there will have to call him by his name.";
                default:
                    return deaths;
            }
        }

        IEnumerator StoryInterlude(int clearedFloor)
        {
            _state = State.Win;
            Memory.RunEndedCleanly();
            PlayerPrefs.SetInt($"ti_story_seen_{clearedFloor}", 1);
            PlayerPrefs.Save();

            bool skipRequested = false;
            bool narrationStarted = false;
            string story = StoryText(clearedFloor);

            // An unadorned black panel deliberately breaks from the normal map and
            // result-screen frames: the interruption should feel unexpected.
            var panel = new GameObject($"StoryInterlude_{clearedFloor}", typeof(RectTransform));
            panel.transform.SetParent(Theme.Canvas.transform, false);
            var background = panel.AddComponent<Image>();
            background.color = Color.black;
            var panelRT = background.rectTransform;
            panelRT.anchorMin = Vector2.zero; panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = panelRT.offsetMax = Vector2.zero;
            var group = panel.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.blocksRaycasts = true; group.interactable = true;

            var chapter = Theme.Label(panel.transform, "THE CASTLE SPEAKS", 25,
                new Color(0.72f, 0.08f, 0.12f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 330), new Vector2(1300, 50));
            chapter.font = Theme.MenuFont != null ? Theme.MenuFont : Theme.Font;
            chapter.raycastTarget = false;

            var title = Theme.Label(panel.transform, StoryTitle(clearedFloor), 64, Gothic.Bone,
                new Vector2(0.5f, 0.5f), new Vector2(0, 245), new Vector2(1600, 100));
            title.font = Theme.TitleFont; title.raycastTarget = false;

            var body = Theme.Label(panel.transform, story, 34, new Color(0.88f, 0.85f, 0.80f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 5), new Vector2(1420, 430));
            body.font = Theme.MenuFont != null ? Theme.MenuFont : Theme.Font;
            body.fontStyle = FontStyle.Normal;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            body.lineSpacing = 1.18f;
            body.raycastTarget = false;

            Gothic.Button(panel.transform, "SKIP", new Vector2(0, -390), new Vector2(250, 68),
                () => skipRequested = true, false, 27);
            _onBack = () => skipRequested = true;

            float fade = 0f;
            while (fade < 0.6f && !skipRequested)
            {
                fade += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(fade / 0.6f);
                yield return null;
            }

            if (!skipRequested)
            {
                group.alpha = 1f;
                if (_levelRoot != null) Destroy(_levelRoot.gameObject);
                Voice.Narrate(story);
                narrationStarted = true;

                int words = story.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
                float readingTime = Mathf.Clamp(words / 2.35f + 2f, 11f, 40f);
                float elapsed = 0f;
                while (elapsed < readingTime && !skipRequested)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (narrationStarted) Voice.Stop();
            Analytics.Track("story_interlude", new System.Collections.Generic.Dictionary<string, object>
            {
                { "floor", clearedFloor },
                { "deaths", _deaths },
                { "skipped", skipRequested },
            });

            // Build the Castle map behind the still-opaque panel, then reveal it.
            // This prevents a one-frame flash of the completed level on phones.
            if (_levelRoot != null) Destroy(_levelRoot.gameObject);
            _hasCheckpoint = false;
            ResetFloorState();
            ShowLevelSelect();
            panel.transform.SetAsLastSibling();

            fade = group.alpha;
            while (fade > 0f)
            {
                fade -= Time.unscaledDeltaTime / 0.35f;
                group.alpha = Mathf.Clamp01(fade);
                yield return null;
            }

            if (panel != null) Destroy(panel);
        }

        // Castle only: the floor-clear beat. A short banner instead of the full
        // WinRoutine result screen (this isn't the end of the run, just one
        // floor of it), then back to the Castle map — the player has to tap
        // the seal that just lit up to actually start the next floor.
        IEnumerator FloorClearedFlash()
        {
            _state = State.Win;   // block input during the banner
            Memory.RunEndedCleanly();   // reached a result beat = not a rage-quit
            if (_toast != null) _toast.text = $"FLOOR {_levelIndex} CLEARED";
            Audio.Play("win", 0.6f);
            yield return new WaitForSecondsRealtime(1.1f);
            if (_toast != null) _toast.text = "";
            if (_levelRoot != null) Destroy(_levelRoot.gameObject);
            _hasCheckpoint = false;
            ResetFloorState();
            ShowLevelSelect();
        }

        IEnumerator WinRoutine()
        {
            Memory.RunEndedCleanly();   // reached a result screen = not a rage-quit
            PlayerPrefs.SetInt("ti_level", 0); PlayerPrefs.Save();
            var panel = Overlay(new Color(0, 0, 0, 0.85f), out var root);
            bool daily = _mode == Mode.Daily;
            ResultTitle(root, daily ? "YOU SURVIVED THE NIGHT" : "YOU ESCAPED THE CASTLE",
                200f, daily ? 62 : 70).color = Theme.Exit;   // gold for a win, blood for a death
            Gothic.Line(root, $"died {_deaths} time" + (_deaths == 1 ? "" : "s"),
                52, Theme.Player, new Vector2(0, 90), new Vector2(1400, 90));

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

        // A result screen's heading in the artwork's voice: the dripping display face
        // in crimson over a near-black shadow, exactly like every painted screen's
        // title. Shared so death, win and versus results all read the same.
        Text ResultTitle(Transform root, string text, float y, int size)
        {
            var c = new Vector2(0.5f, 0.5f);
            var shadow = Theme.Label(root, text, size, new Color(0, 0, 0, 0.85f),
                c, new Vector2(5, y - 5), new Vector2(1600, 170));
            shadow.font = Theme.TitleFont; shadow.raycastTarget = false;
            var t = Theme.Label(root, text, size, Theme.Player, c, new Vector2(0, y), new Vector2(1600, 170));
            t.font = Theme.TitleFont; t.raycastTarget = false;
            return t;
        }

        // Shared footer for result screens: a real brag line, the newest badge, and
        // SHARE (captures a PNG card) + LEADERBOARD + MENU buttons.
        void ResultFooter(Transform root, GameObject panel, string brag, string lbMode)
        {
            // Back on a result screen = the MAIN MENU button (tear the level down too).
            _onBack = () => { Destroy(panel); if (_levelRoot != null) Destroy(_levelRoot.gameObject); ShowMenu(); };
            var c = new Vector2(0.5f, 0.5f);
            // The ornate border in place of Overlay's plain one, so a result screen
            // reads as the same castle as the menus — but see-through, because the
            // floor that just killed you should still be visible behind it.
            Gothic.FrameOnly(root);
            if (_newBest)
                Theme.Label(root, "NEW BEST!", 38, Theme.Exit,
                    c, new Vector2(0, 34), new Vector2(800, 52)).font = Theme.TitleFont;
            // Display strips the emoji (the pixel font can't draw it); the SHARE text
            // keeps it (renders fine on social).
            string shown = brag.Replace("\U0001F987", "").Replace("  ", " ").Trim();
            Gothic.Line(root, "“" + shown + "”", 28, Theme.Coin, new Vector2(0, -10), new Vector2(1600, 60));
            var nb = Badges.Newest;
            if (nb != null)
                Gothic.Line(root, "NEW BADGE UNLOCKED — " + nb.name, 26, Gothic.Bone,
                    new Vector2(0, -54), new Vector2(1200, 44));
            // Broke a friend's curse this run? The brag carries the receipt.
            if (Curse.LastBroken != null)
                brag += $" and I broke {Curse.LastBroken.nick}'s curse";
            // Sanitize strips the typographic dashes/quotes the player asked to keep
            // out of shared messages, and the link rides along so a share is
            // actually clickable instead of orphan text.
            string bragFinal = NativeShare.Sanitize(brag);
            Gothic.Button(root, "SHARE", new Vector2(-350, -150), new Vector2(310, 96),
                () =>
                {
                    Analytics.Track("share_tapped", new System.Collections.Generic.Dictionary<string, object>
                    { { "mode", ModeName }, { "level_index", _levelIndex } });
                    StartCoroutine(NativeShare.ShareScreenshot("trust-issues", bragFinal, GameLink));
                }, true, 34);
            // Haunt a friend: a link that spawns YOUR ghost on this floor in THEIR game.
            Gothic.Button(root, "CURSE A FRIEND", new Vector2(0, -150), new Vector2(310, 96), () =>
                {
                    var d = new Curse.Data
                    {
                        nick = Meta.Nick, floor = _levelIndex, deaths = _floorDeaths,
                        cause = Memory.LastKillerName, mode = ModeName,
                    };
                    string link = Curse.BuildLink(d);
                    string challenge = _mode == Mode.Endless
                        ? $"survive {CurrentEndlessMeters} metres"
                        : $"survive floor {_levelIndex + 1}";
                    string msg = NativeShare.Sanitize(
                        $"I cursed you in Trust Issues \U0001F987 {challenge} with {_floorDeaths} deaths or less, or my ghost stays");
                    // Goes through the OS share sheet on phone (WhatsApp, Instagram,
                    // Bluetooth...) rather than silently filling a clipboard.
                    NativeShare.ShareText(msg, link);
                    Analytics.Track("curse_sent", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "floor", _levelIndex }, { "mode", ModeName },
                    });
                    BossToast("CURSE READY - SEND IT TO THEM");
                }, false, 26);
            Gothic.Button(root, "LEADERBOARD", new Vector2(350, -150), new Vector2(310, 96),
                () => { Destroy(panel); ShowLeaderboard(lbMode); }, false, 30);
            Gothic.Button(root, "MAIN MENU", new Vector2(0, -270), new Vector2(420, 100),
                () => { Destroy(panel); if (_levelRoot != null) Destroy(_levelRoot.gameObject); ShowMenu(); },
                false, 34);
        }

        void ShowLeaderboard(string mode)
        {
            Audio.Play("click");
            var c = new Vector2(0.5f, 0.5f);
            var panel = Overlay(new Color(0.04f, 0.02f, 0.06f, 1f), out var root);
            _onBack = () => { Destroy(panel); ShowMenu(); };
            if (Skin.Background(root, "leaderboard_bg") != null) { BuildSkinnedLeaderboard(root, panel, mode); return; }

            Gothic.Backdrop(root);
            string scope = mode == "daily" ? "today" : "all";
            string heading = mode == "daily" ? "BLOOD MOON — TONIGHT (FEWEST DEATHS)"
                           : mode == "endless" ? "ENDLESS NIGHT — LONGEST DISTANCE"
                                               : "THE CASTLE — FEWEST DEATHS";
            Gothic.Heading(root, "LEADERBOARD", heading);
            Gothic.PlateAt(root, new Vector2(0, -30), new Vector2(1080, 560), Gothic.Plate);
            var list = Gothic.Line(root, "summoning the dead…", 32, Gothic.Bone,
                new Vector2(0, -30), new Vector2(1000, 520), TextAnchor.UpperCenter);
            Leaderboard.Fetch(mode, scope, entries =>
            {
                if (list == null) return;
                int rank = Leaderboard.MyRank(entries);
                string gold = ColorUtility.ToHtmlStringRGB(Theme.Coin);
                string blood = ColorUtility.ToHtmlStringRGB(Theme.Player);
                string faint = ColorUtility.ToHtmlStringRGB(Gothic.Faint);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(rank == 0
                    ? $"<color=#{faint}>You are unranked — † marks the castle's own dead. Beat one.</color>\n"
                    : $"<color=#{blood}>YOU ARE #{rank} OF {entries.Count}</color>\n");
                // Same windowing rule as the skinned board: never scroll the player
                // off their own leaderboard.
                const int Rows = 11;
                int first = (rank > 0 && entries.Count > Rows)
                    ? Mathf.Clamp(rank - 1 - Rows / 2, 0, entries.Count - Rows) : 0;
                for (int i = 0; i < Rows && first + i < entries.Count; i++)
                {
                    var e = entries[first + i];
                    int place = first + i + 1;
                    string col = e.you ? blood : place == 1 ? gold : e.ghost ? faint : null;
                    string row = $"{place}.   {(e.ghost ? "† " : "")}{e.nick}{(e.you ? "   (you)" : "")}" +
                                 $"      {e.value}{(mode == "endless" ? " m" : "")}";
                    sb.AppendLine(col == null ? row : $"<color=#{col}>{row}</color>");
                }
                list.text = sb.ToString();
            });
            Gothic.Back(root, () => { Destroy(panel); ShowMenu(); });
        }

        void ShowWardrobe()
        {
            Audio.Play("click");
            var c = new Vector2(0.5f, 0.5f);
            var panel = Overlay(new Color(0.04f, 0.02f, 0.06f, 0.92f), out var root);
            _onBack = () => { Destroy(panel); ShowMenu(); };
            BuildWardrobeTabs(root, panel);
            return;
#pragma warning disable CS0162
            if (Skin.Background(root, "wardrobe_bg") != null) { BuildSkinnedWardrobe(root, panel); return; }
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
                var card = Theme.Button(root, "", bg, Color.white, 1, c, pos, new Vector2(320, 236),
                    unlocked ? (System.Action)(() => { Skins.Equip(sid); Destroy(panel); ShowWardrobe(); })
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
#pragma warning restore CS0162
        }

        int _wardrobeTab;

        void BuildWardrobeTabs(Transform root, GameObject panel)
        {
            var c = new Vector2(0.5f, 0.5f);
            // The artwork is deliberately an empty template. Every character, label,
            // unlock state and tap target below is live Unity UI rather than a baked screenshot.
            if (Skin.Background(root, "wardrobe_avatar_bg_v3") == null) Gothic.Backdrop(root);

            string[] tabs = { "AVATAR", "AURA", "OUTFIT" };
            float[] tabX = { 0.306f, 0.500f, 0.694f };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i;
                float x = tabX[i];
                Skin.Chip(root, x - 0.095f, 0.166f, x + 0.095f, 0.217f,
                    i == _wardrobeTab ? new Color(0.30f, 0.025f, 0.035f, 0.96f)
                                      : new Color(0.025f, 0.018f, 0.018f, 0.94f));
                var tabText = Skin.LiveText(root, tabs[i], x - 0.085f, 0.170f, x + 0.085f, 0.213f,
                    28, i == _wardrobeTab ? Theme.Exit : Theme.Coin);
                if (Theme.MenuFont != null) tabText.font = Theme.MenuFont;
                tabText.fontStyle = FontStyle.Bold; Skin.Fit(tabText, 28, 15);
                Skin.Zone(root, x - 0.10f, 0.158f, x + 0.10f, 0.221f,
                    () => { _wardrobeTab = tab; Destroy(panel); ShowWardrobe(); }, "wardrobe_tab_" + i);
            }

            var vampFrames = Assets.Grid("vamp_idle_sheet", 64, 3);
            Sprite vamp = vampFrames != null && vampFrames.Length > 0 ? vampFrames[0] : Assets.Sprite("vamp_idle");
            var pinkFrames = Assets.Sheet("pinkman_idle", 32);
            Sprite pink = pinkFrames != null && pinkFrames.Length > 0 ? pinkFrames[0] : vamp;
            var avatar = Skins.Current;
            Sprite selectedSprite = avatar.pinkman ? pink : vamp;

            const int cols = 5;
            const float pitchX = 240f, pitchY = 292f;
            const float startY = 103f;
            int count = _wardrobeTab == 0 ? Skins.All.Count
                      : _wardrobeTab == 1 ? WardrobeCosmetics.Auras.Count
                                          : WardrobeCosmetics.Outfits.Count;
            for (int i = 0; i < count; i++)
            {
                int row = i / cols, col = i % cols;
                Vector2 pos = new Vector2((col - 2) * pitchX, startY - row * pitchY);
                if (_wardrobeTab == 0)
                {
                    var def = Skins.All[i]; string id = def.id;
                    bool unlocked = Skins.IsUnlocked(def), equipped = Skins.CurrentId == id;
                    WardrobeCard(root, panel, pos, def.name, def.unlockHint, unlocked, equipped,
                        def.pinkman ? pink : vamp, Skins.Shade(def), Color.clear,
                        () => { Skins.Equip(id); Destroy(panel); ShowWardrobe(); });
                }
                else
                {
                    var list = _wardrobeTab == 1 ? WardrobeCosmetics.Auras : WardrobeCosmetics.Outfits;
                    var def = list[i]; string id = def.id;
                    bool unlocked = WardrobeCosmetics.IsUnlocked(def);
                    bool equipped = _wardrobeTab == 1 ? WardrobeCosmetics.CurrentAuraId == id
                                                     : WardrobeCosmetics.CurrentOutfitId == id;
                    Color avatarTint = Skins.Shade(avatar);
                    Color previewTint = _wardrobeTab == 2 && id != "classic"
                        ? Color.Lerp(avatarTint, def.color, 0.42f) : avatarTint;
                    Color aura = _wardrobeTab == 1 ? def.color : Color.clear;
                    System.Action equip = _wardrobeTab == 1
                        ? (System.Action)(() => WardrobeCosmetics.EquipAura(id))
                        : (System.Action)(() => WardrobeCosmetics.EquipOutfit(id));
                    WardrobeCard(root, panel, pos, def.name, def.hint, unlocked, equipped,
                        selectedSprite, previewTint, aura,
                        () => { equip(); Destroy(panel); ShowWardrobe(); });
                }
            }

            var backText = Skin.LiveText(root, "‹ BACK", 0.425f, 0.900f, 0.575f, 0.956f, 34, Theme.Coin);
            if (Theme.MenuFont != null) backText.font = Theme.MenuFont;
            backText.fontStyle = FontStyle.Bold; Skin.Fit(backText, 34, 18);
            Skin.Zone(root, 0.39f, 0.875f, 0.61f, 0.975f,
                () => { Destroy(panel); ShowMenu(); }, "back");
        }

        void WardrobeCard(Transform root, GameObject panel, Vector2 pos, string name, string challenge,
            bool unlocked, bool equipped, Sprite preview, Color previewTint, Color aura,
            System.Action equip)
        {
            var c = new Vector2(0.5f, 0.5f);
            var card = Theme.Button(root, "", Color.clear, Color.white, 1, c, pos, new Vector2(225, 280),
                unlocked ? equip : (System.Action)(() => ShowHint(challenge)));
            var ct = card.transform;
            if (equipped)
            {
                var ring = new GameObject("EquippedFrame", typeof(RectTransform)).AddComponent<Image>();
                ring.transform.SetParent(ct, false); ring.sprite = Gothic.Frame; ring.type = Image.Type.Sliced;
                ring.pixelsPerUnitMultiplier = Gothic.RingFrameMul; ring.color = Theme.Exit; ring.raycastTarget = false;
                ring.rectTransform.anchorMin = Vector2.zero; ring.rectTransform.anchorMax = Vector2.one;
                ring.rectTransform.offsetMin = new Vector2(-3, -3); ring.rectTransform.offsetMax = new Vector2(3, 3);
            }
            if (aura.a > 0.01f)
            {
                var glow = new GameObject("AuraPreview", typeof(RectTransform)).AddComponent<Image>();
                glow.transform.SetParent(ct, false); glow.sprite = Gothic.Diamond; glow.raycastTarget = false;
                glow.color = new Color(aura.r, aura.g, aura.b, unlocked ? 0.50f : 0.12f);
                glow.rectTransform.sizeDelta = new Vector2(145, 145);
                glow.rectTransform.anchoredPosition = new Vector2(0, 26);
            }
            if (preview != null)
            {
                var pv = new GameObject("AvatarPreview", typeof(RectTransform)).AddComponent<Image>();
                pv.transform.SetParent(ct, false); pv.sprite = preview; pv.preserveAspect = true; pv.raycastTarget = false;
                pv.color = unlocked ? previewTint : new Color(0.035f, 0.03f, 0.045f, 0.96f);
                pv.rectTransform.sizeDelta = new Vector2(125, 125); pv.rectTransform.anchoredPosition = new Vector2(0, 28);
            }
            var nameText = Theme.Label(ct, name.ToUpperInvariant(), 21,
                unlocked ? Gothic.Bone : new Color(0.5f, 0.45f, 0.48f),
                c, new Vector2(0, 111), new Vector2(215, 34));
            if (Theme.MenuFont != null) nameText.font = Theme.MenuFont;
            nameText.fontStyle = FontStyle.Bold; nameText.raycastTarget = false;
            string state = equipped ? "EQUIPPED" : unlocked ? "TAP TO EQUIP" : challenge;
            var line = Theme.Label(ct, state, unlocked ? 16 : 14,
                equipped ? Theme.Coin : unlocked ? new Color(0.76f, 0.70f, 0.66f) : new Color(0.88f, 0.35f, 0.38f),
                c, new Vector2(0, -103), new Vector2(205, 58));
            if (Theme.MenuFont != null) line.font = Theme.MenuFont;
            line.horizontalOverflow = HorizontalWrapMode.Wrap; line.verticalOverflow = VerticalWrapMode.Truncate;
            line.raycastTarget = false;
        }

#if false // Shop feature removed.
        // ==================== retired shop implementation ====================
        // Where blood shards go: charms, purchasable skins, death effects, trails and
        // gravestone taunts. Laid out to the shop artwork — a heading carrying the two
        // live numbers, three tabs, a 3-across grid of framed cards whose last row is
        // CENTRED (a lone pair hanging off the left edge was the giveaway that this was
        // a plain loop and not a designed shelf), then the next-goal line and the BACK
        // rail. Every card wears its own emblem instead of the same tinted diamond.
        //
        // Everything under the title hangs off the TOP edge and the rail off the BOTTOM,
        // and the grid measures the canvas and shrinks its rows to fit — a tall phone
        // gets far less than the reference 1080 of height, and centre-anchored shelves
        // walked straight off the bottom there.
        //
        // If the shop painting is dropped in as Resources/ui/shop_bg, the art supplies
        // the frame, gargoyles, drapes, candles and title, and everything it baked
        // INSIDE the panel (a sample shard count, a frozen grid, "NEED 129 MORE") is
        // covered and rebuilt live — otherwise the picture would lie about your money.
        static readonly Color ShopInterior = new Color(0.032f, 0.020f, 0.028f, 1f);

        /// <summary>One assembled shop card. Built before layout so the grid knows how
        /// many it must fit before it picks its row height.</summary>
        class ShopEntry
        {
            public string name, desc;
            public int price;
            public bool owned, worn;
            public int sealedFloor;          // > 0 => locked until that castle floor
            public Sprite icon;              // emblem; null => a tinted diamond
            public bool artIcon;             // painted art: show as-is, never tinted
            public Color tint = Color.white;
            public System.Action buy, equip;
        }

        void ShowShop()
        {
            Audio.Play("click");
            Analytics.Track("shop_open", new System.Collections.Generic.Dictionary<string, object>
            {
                { "balance", Currency.Balance },
            });
            // OPAQUE ground. On the old 94%-transparent wash the main-menu artwork
            // underneath still showed through — the painted BLOOD MOON / THE CASTLE
            // buttons were visible straight through the shop's cards.
            var panel = Overlay(new Color(0.04f, 0.02f, 0.06f, 1f), out var root);
            _onBack = () => { Destroy(panel); ShowMenu(); };

            // Wear the painting when it's there; otherwise the code-built twin, which
            // uses the same stone, moon, frame and lettering.
            bool painted = Skin.Background(root, "shop_bg") != null;
            if (painted) Skin.Chip(root, 0.148f, 0.110f, 0.852f, 0.884f, ShopInterior);
            else Gothic.Backdrop(root);

            // The header states the two things that were invisible before: what you
            // have, and what you have to DO to open the next shelf. Both numbers are
            // live — the artwork bakes in a sample balance, which is why it's covered.
            int floors = Charms.FloorsCleared;
            string sub = $"{Currency.Balance} BLOOD SHARDS     ·     {floors} FLOORS CLEARED";
            if (painted)
            {
                var subT = Skin.LiveText(root, sub, 0.18f, 0.116f, 0.82f, 0.158f, 26, Gothic.Faint);
                if (Theme.MenuFont != null) subT.font = Theme.MenuFont;
                Skin.Fit(subT, 26, 14);
            }
            else Gothic.Heading(root, "THE CRYPT SHOP", sub);

            // Laid out from the top edge down (see the note above the method).
            var top = new Vector2(0.5f, 1f);
            const float TabsY = 226f;        // tab row centre, below the heading
            const float BlurbY = 292f;       // the "what this shelf is" line
            const float GridTop = 328f;      // top edge of the first card row
            const float BottomBand = 210f;   // room kept for the goal line + BACK rail

            // ---- tabs ---------------------------------------------------------
            string[] tabs = { "CHARMS", "SKINS", "STYLE" };
            string[] blurb =
            {
                "Wear ONE. These change how the castle treats you.",
                "Who you are on the way down.",
                "Death effects, trails and last words.",
            };
            for (int t = 0; t < tabs.Length; t++)
            {
                int tab = t;
                bool sel = _shopTab == t;
                Gothic.Button(root, tabs[t], new Vector2(-424 + t * 424, -TabsY), new Vector2(404, 72),
                    () => { _shopTab = tab; Destroy(panel); ShowShop(); }, sel, 30, top);
            }
            Gothic.Line(root, blurb[Mathf.Clamp(_shopTab, 0, 2)], 24, Gothic.Faint,
                new Vector2(0, -BlurbY), new Vector2(1500, 38), TextAnchor.MiddleCenter, top);

            // ---- assemble this shelf ------------------------------------------
            var entries = new System.Collections.Generic.List<ShopEntry>();
            if (_shopTab == 0)
            {
                foreach (var d in Charms.All)
                {
                    var def = d;
                    bool gated = !Charms.Unlocked(def);
                    entries.Add(new ShopEntry
                    {
                        name = def.name,
                        // A gated charm shows the FLOOR it opens at, not a dead button:
                        // the requirement is the point, so it has to be readable.
                        desc = gated ? $"Locked - clear floor {def.reqFloors}" : def.desc,
                        price = def.price,
                        owned = Charms.Owns(def.id),
                        worn = Charms.IsWorn(def.id),
                        sealedFloor = gated ? def.reqFloors : 0,
                        icon = CharmIcon(def.id),
                        artIcon = true,                       // emblems carry their own colour
                        tint = def.tint,
                        buy = () => { if (Charms.Buy(def)) Charms.Equip(def.id); },
                        equip = () => Charms.Equip(def.id),
                    });
                }
            }
            else if (_shopTab == 1)
            {
                // Skin preview art (same sources as the Wardrobe previews).
                var vampFrames = Assets.Grid("vamp_idle_sheet", 64, 3);
                Sprite vampSp = (vampFrames != null && vampFrames.Length > 0) ? vampFrames[0] : null;
                var pmFrames = Assets.Sheet("pinkman_idle", 32);
                Sprite pmSp = (pmFrames != null && pmFrames.Length > 0) ? pmFrames[0] : null;
                foreach (var s in Skins.All)
                {
                    if (s.price <= 0) continue;              // achievement skins live in the Wardrobe
                    var sd = s;
                    bool owned = Skins.IsUnlocked(sd);
                    entries.Add(new ShopEntry
                    {
                        name = sd.name, desc = "a different face for the fall", price = sd.price,
                        owned = owned, worn = owned && Skins.CurrentId == sd.id,
                        icon = sd.pinkman ? pmSp : vampSp,
                        tint = Skins.Shade(sd),
                        buy = () => { if (Shop.BuySkin(sd)) Skins.Equip(sd.id); },
                        equip = () => Skins.Equip(sd.id),
                    });
                }
            }
            else
            {
                foreach (var it in Shop.All)
                {
                    var item = it;
                    bool owned = Shop.Owns(item.id);
                    bool equipped = owned && Shop.Equipped(item.kind) == item.id;
                    entries.Add(new ShopEntry
                    {
                        name = item.name, desc = item.desc, price = item.price,
                        owned = owned, worn = equipped,
                        icon = TauntIcon(item.id),           // null for fx/trails → tinted diamond
                        artIcon = item.kind == "taunt",      // bone glyphs, shown as drawn
                        tint = item.tint,
                        buy = () => { if (Shop.Buy(item)) Shop.Equip(item.kind, item.id); },
                        // Owned items TOGGLE: tap to wear, tap again to take off.
                        equip = () => Shop.Equip(item.kind, equipped ? "" : item.id),
                    });
                }
            }

            // ---- the grid ------------------------------------------------------
            // Three across on every shelf, so the tabs, the cards and the artwork's
            // painted columns all line up. Rows are then whatever height is left.
            const int Cols = 3;
            const float ColPitch = 428f, CardW = 400f;
            float canvasH = ((RectTransform)root).rect.height;
            if (canvasH < 400f) canvasH = 1080f;             // before the first layout pass
            int rows = Mathf.Max(1, (entries.Count + Cols - 1) / Cols);
            float band = Mathf.Max(200f, canvasH - GridTop - BottomBand);
            float pitch = band / rows;
            float cardH = Mathf.Clamp(pitch - 24f, 120f, 268f);

            for (int i = 0; i < entries.Count; i++)
            {
                int r = i / Cols, col = i % Cols;
                // How many sit in THIS row — the last, short row is centred rather
                // than left-packed, which is what the artwork does.
                int inRow = Mathf.Min(Cols, entries.Count - r * Cols);
                float x = (col - (inRow - 1) / 2f) * ColPitch;
                float y = -(GridTop + r * pitch + cardH / 2f);
                ShopCard(root, panel, entries[i], new Vector2(x, y), new Vector2(CardW, cardH), top);
            }

            // Always name the next goal, so leaving the shop still leaves a target.
            var goal = Charms.NextGoal();
            if (goal != null)
            {
                int need = goal.price - Currency.Balance;
                string line = !Charms.Unlocked(goal)
                    ? $"NEXT: {goal.name} — opens when you clear floor {goal.reqFloors}"
                    : need > 0 ? $"NEXT: {goal.name} — {need} more shards"
                               : $"NEXT: {goal.name} — you can afford it NOW";
                Gothic.Line(root, line, 24, Theme.Coin, new Vector2(0, 148), new Vector2(1500, 38),
                    TextAnchor.MiddleCenter, new Vector2(0.5f, 0f));
            }

            // The painting draws its own BACK plate, so there it only needs a tap-zone.
            if (painted) Skin.Zone(root, 0.40f, 0.888f, 0.60f, 0.962f, () => { Destroy(panel); ShowMenu(); }, "back");
            else Gothic.Back(root, () => { Destroy(panel); ShowMenu(); });
        }
        int _shopTab;   // which shelf the Crypt Shop is showing

        // The emblem on a charm card. Reuses art the game already ships wherever the
        // meaning matches (a coin for double pay, the bat for a longer glide, a torch
        // for seeing in the dark, the reverse rune for shaking off reversed controls)
        // and falls back to the procedural gravestone for the ward.
        static Sprite CharmIcon(string id) => id switch
        {
            "charm_gravedigger" => Assets.Sprite("coin"),
            "charm_wings"       => Assets.TrapArt("bat"),
            "charm_candle"      => Assets.Sprite("torch"),
            "charm_steady"      => Assets.TrapArt("reverse"),
            _                   => Gothic.Tomb,
        };

        // Taunts are three different jokes, so they get three different emblems —
        // as identical white diamonds they read as one item printed three times.
        // Death effects and trails keep the tinted diamond (their colour IS the item).
        static Sprite TauntIcon(string id) => id switch
        {
            "taunt_skill" => Gothic.Skull,
            "taunt_easy"  => Gothic.Tomb,
            "taunt_meant" => Gothic.Bones,
            _             => null,
        };

        // One shop card: emblem, name, flavour line, and a state footer — EQUIPPED /
        // tap to wear / BUY — N / SEALED — CLEAR FLOOR N / NEED N MORE. Every row of
        // the card is placed as a fraction of the card's own height, so the tall
        // two-row shelves and the short three-row STYLE shelf both stay tidy. Any
        // successful action rebuilds the screen so every card reflects the new state.
        void ShopCard(Transform root, GameObject panel, ShopEntry e, Vector2 pos, Vector2 size, Vector2 anchor)
        {
            bool affordable = Currency.Balance >= e.price;
            bool sealed_ = e.sealedFloor > 0 && !e.owned;
            // Fills sampled off the artwork: worn items glow blood, owned ones sit in
            // cold stone, buyable ones catch a little candle warmth, sealed ones stay dead.
            var bg = e.worn ? new Color(0.30f, 0.045f, 0.075f, 1f)
                   : e.owned ? new Color(0.085f, 0.062f, 0.105f, 1f)
                   : sealed_ ? new Color(0.052f, 0.040f, 0.065f, 1f)
                   : affordable ? new Color(0.125f, 0.088f, 0.055f, 1f) : new Color(0.052f, 0.040f, 0.065f, 1f);

            var card = Gothic.Button(root, "", pos, size, () =>
            {
                // A sealed charm can't be bought at any price — say what opens it
                // rather than silently doing nothing.
                if (sealed_) { BossToast($"SEALED - clear floor {e.sealedFloor} first"); return; }
                if (e.owned) { e.equip(); Audio.Play("click", 0.7f); }
                else if (affordable)
                {
                    e.buy();
                    Audio.PlayOr("levelup", "win", 0.7f);
                    if (_shardText != null) _shardText.text = Currency.Balance.ToString();
                }
                else { ShowHint($"{e.price - Currency.Balance} more shards — the castle pays for blood"); return; }
                Destroy(panel); ShowShop();
            }, false, 30, anchor, bg);
            var ct = card.transform;

            if (e.worn)   // a candle-gold ring marks the current pick
            {
                var ring = new GameObject("EquipRing", typeof(RectTransform)).AddComponent<Image>();
                ring.transform.SetParent(ct, false); ring.raycastTarget = false;
                ring.sprite = Gothic.Frame; ring.type = Image.Type.Sliced;
                ring.pixelsPerUnitMultiplier = Gothic.RingFrameMul;
                ring.color = Theme.Coin;
                var rrt = ring.rectTransform; rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
                rrt.offsetMin = new Vector2(-5, -5); rrt.offsetMax = new Vector2(5, 5);
            }

            float h = size.y;
            var em = new GameObject("Emblem", typeof(RectTransform)).AddComponent<Image>();
            em.transform.SetParent(ct, false); em.raycastTarget = false;
            em.preserveAspect = true;
            if (e.icon != null)
            {
                em.sprite = e.icon;
                // Painted art is never tinted (a colour multiply only muddies it); a
                // skin preview wears its costume colour, and anything not yet owned
                // stays a mystery silhouette.
                em.color = e.artIcon ? (sealed_ ? new Color(0.62f, 0.58f, 0.58f, 1f) : Color.white)
                         : e.owned ? e.tint
                                   : new Color(0.05f, 0.04f, 0.06f, 0.95f);
            }
            else
            {
                em.sprite = Gothic.Diamond;    // the artwork's own ornament, tinted per item
                em.color = e.tint;
            }
            float emSize = Mathf.Clamp(h * 0.32f, 46f, 92f);
            var ert = em.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f); ert.pivot = new Vector2(0.5f, 0.5f);
            ert.anchoredPosition = new Vector2(0, h * 0.21f);
            ert.sizeDelta = new Vector2(emSize, emSize);

            float w = size.x - 30f;
            Skin.Fit(Gothic.Line(ct, e.name.ToUpperInvariant(), 27,
                e.owned || affordable ? Gothic.Bone : new Color(0.62f, 0.55f, 0.52f, 0.55f),
                new Vector2(0, -h * 0.12f), new Vector2(w, 34)), 27, 14);
            Skin.Fit(Gothic.Line(ct, e.desc, 17, new Color(0.72f, 0.65f, 0.62f, 0.70f),
                new Vector2(0, -h * 0.26f), new Vector2(w - 12, h * 0.22f)), 17, 11);

            string footer = e.worn ? "EQUIPPED — tap to remove"
                          : e.owned ? "tap to wear"
                          : sealed_ && affordable ? $"SEALED — CLEAR FLOOR {e.sealedFloor}"
                          : affordable ? $"BUY — {e.price}"
                          : $"NEED {e.price - Currency.Balance} MORE";
            Skin.Fit(Gothic.Line(ct, footer, 20,
                e.worn ? Theme.Coin : e.owned ? new Color(0.72f, 0.65f, 0.62f, 0.6f)
                       : sealed_ ? new Color(0.72f, 0.62f, 0.40f, 0.85f)
                       : affordable ? Theme.Coin : new Color(0.78f, 0.28f, 0.30f, 0.9f),
                new Vector2(0, -h * 0.40f), new Vector2(w, 28)), 20, 12);
        }

#endif
        // ==================== pause ====================
        void TogglePause()
        {
            if (_state == State.Play) Pause();
            else if (_state == State.Paused) Resume();
        }

        // The single home for the BACK key (desktop Esc + Android hardware back).
        // The golden rule: it must ALWAYS do something in-app, so Android can never
        // fall through to its default and close the game.
        void HandleBackButton()
        {
            switch (_state)
            {
                case State.Play:   Pause();  break;   // back opens the pause menu (which has MAIN MENU)
                case State.Paused: Resume(); break;   // back closes it again
                default:                              // Menu / Win screens
                    if (_onBack != null) _onBack();
                    else ShowMenu();                  // safety net: never nothing
                    break;
            }
        }

        // Standard mobile pattern for the top-level menu: one back arms a prompt,
        // a second back within the window actually exits. Prevents a stray press
        // from closing the app while still giving a real way out.
        void BackFromMainMenu()
        {
            if (Time.unscaledTime <= _quitArmedUntil) { Application.Quit(); return; }
            _quitArmedUntil = Time.unscaledTime + 2f;
            ShowHint("Press back again to quit", 2f);
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
            // FULLY OPAQUE. The old pause let the level show through at 20%, and the
            // level is lit by torches and candle gold — so the "black and red" pause
            // screen came out washed orange. Nothing behind it shows now; it wears the
            // same black/red ground, blood moon and castle skyline as the main menu.
            _pausePanel = Overlay(new Color(0.02f, 0.008f, 0.025f, 1f), out var root);
            // If a pause artwork is dropped in (Resources/ui/pause_bg), wear it.
            bool painted = Skin.Background(root, "pause_bg") != null;
            if (!painted) Gothic.Backdrop(root);

            // Endless also allows a deliberate "END RUN" to bank the current
            // distance without throwing away a strong attempt.
            bool endless = _mode == Mode.Endless;

            // PAINTED PAUSE. The artwork already draws the heading and all three
            // button plates, so nothing is built on top of it except the tap-zones —
            // and, in Endless, the one button the painting doesn't have. Rects
            // measured off the 1497x1051 mockup.
            if (painted)
            {
                Skin.Zone(root, 0.28f, 0.395f, 0.70f, 0.525f, Resume,       "resume");
                Skin.Zone(root, 0.28f, 0.545f, 0.70f, 0.675f, RestartLevel, "restart");
                Skin.Zone(root, 0.28f, 0.700f, 0.70f, 0.835f, QuitToMenu,   "menu");
                if (endless)
                {
                    // Endless has a fourth action the painting never anticipated, so
                    // it gets a real drawn button below the painted three rather than
                    // an invisible zone over empty stone nobody would ever find.
                    Gothic.Button(root, "END RUN — BANK SCORE", new Vector2(0, -352f),
                                  new Vector2(540, 84), EndRun, false, 28);
                }
                return;
            }

            // The pause menu now wears the same gothic plate as the painted screens:
            // a framed slab of castle stone rather than four loose coloured bars
            // floating over the level.
            float plateH = endless ? 612f : 500f;
            if (!painted)
            {
                // Slightly lifted off the backdrop (which is Gothic.Ground) so the
                // slab still reads as a panel rather than dissolving into the wall.
                var plate = Gothic.PlateAt(root, Vector2.zero, new Vector2(660, plateH), Gothic.Plate);
                var grain = new GameObject("Grain", typeof(RectTransform)).AddComponent<Image>();
                grain.transform.SetParent(plate.transform, false);
                grain.sprite = Theme.StoneTile; grain.type = Image.Type.Tiled;
                grain.pixelsPerUnitMultiplier = 0.14f; grain.raycastTarget = false;
                grain.color = new Color(0.55f, 0.5f, 0.62f, 0.10f);
                var grt = grain.rectTransform;
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
                grt.offsetMin = new Vector2(8, 8); grt.offsetMax = new Vector2(-8, -8);
                grain.transform.SetAsFirstSibling();   // under the frame, over the fill
            }

            float titleY = plateH / 2f - 90f;
            var shadow = Theme.Label(root, "PAUSED", 72, new Color(0, 0, 0, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(5, titleY - 5), new Vector2(1000, 130));
            shadow.font = Theme.TitleFont; shadow.raycastTarget = false;
            var pausedTitle = Theme.Label(root, "PAUSED", 72, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, titleY), new Vector2(1000, 130));
            pausedTitle.font = Theme.TitleFont;   // the dripping-blood heading, like every other screen
            pausedTitle.raycastTarget = false;

            float y = titleY - 120f;
            var btnSize = new Vector2(540, 94);
            Gothic.Button(root, "RESUME", new Vector2(0, y), btnSize, Resume, true, 40);
            y -= 110f;
            Gothic.Button(root, "RESTART LEVEL", new Vector2(0, y), btnSize, RestartLevel, false, 34);
            y -= 110f;
            if (endless)
            {
                Gothic.Button(root, "END RUN — BANK SCORE", new Vector2(0, y), btnSize, EndRun, false, 30);
                y -= 110f;
            }
            Gothic.Button(root, "MAIN MENU", new Vector2(0, y), btnSize, QuitToMenu, false, 34);
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
            _stageIndex = 0;         // the pause-menu RESTART is the FULL floor restart
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
