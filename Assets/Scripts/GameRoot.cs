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
        Image _flyBar;        // flight-meter fill

        // ---- analytics ----
        float _levelStartRealtime;  // when the current level attempt began (for durations)
        float _heartbeatTimer;      // emits a "still playing" ping every few seconds
        string ModeName => _mode.ToString();
        int LevelDurationMs => Mathf.RoundToInt((Time.realtimeSinceStartup - _levelStartRealtime) * 1000f);
        float _camMin = -1.5f, _camMax = -1.5f;
        const float CamY = -1.2f;

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
            Analytics.Track("session_start", new System.Collections.Generic.Dictionary<string, object>
            {
                { "platform", Application.platform.ToString() },
                { "mobile", Application.isMobilePlatform },
                { "screen", Screen.width + "x" + Screen.height },
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
            ResetProgressOncePerVersion();
            SetupCamera();
            BuildBackdrop();
            BuildHUD();
            ShowMenu();
        }

        void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera"); go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.orthographicSize = 5.6f;
            _cam.transform.position = new Vector3(-1.5f, CamY, -10f);
            _cam.backgroundColor = Theme.Sky;
            _cam.clearFlags = CameraClearFlags.SolidColor;
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

            BuildTouchControls();
            BuildRotatePanel();
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

            MakeTouch("‹", -1, new Vector2(0f, 0f), new Vector2(170, 170), new Vector2(210, 210));
            MakeTouch("›", 1, new Vector2(0f, 0f), new Vector2(410, 170), new Vector2(210, 210));
            MakeTouch("JUMP", 0, new Vector2(1f, 0f), new Vector2(-200, 170), new Vector2(260, 260));
            MakeTouch("FLY", 3, new Vector2(1f, 0f), new Vector2(-470, 220), new Vector2(190, 190));
            _touchPanel.SetActive(false);
        }

        void MakeTouch(string label, int dir, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Touch_" + label, typeof(RectTransform));
            go.transform.SetParent(_touchPanel.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            go.AddComponent<TouchButton>().dir = dir;
            var t = Theme.Label(go.transform, label, dir == 0 ? 44 : 90, new Color(1, 1, 1, 0.85f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
        }

        // ==================== MAIN MENU ====================
        void ShowMenu()
        {
            // Leaving a race: drop the room and the rival ghosts.
            if (_mode == Mode.Versus) { Net.Leave(); ClearGhosts(); _mode = Mode.Curated; }
            _state = State.Menu;
            Time.timeScale = 1f;
            Audio.Music("music", 0.3f);
            _hud.gameObject.SetActive(false);
            if (_touchPanel != null) { _touchPanel.SetActive(false); TouchInput.Clear(); }
            _cam.transform.position = new Vector3(-1.5f, CamY, -10f);

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
            Theme.Label(root, Theme.Title, 132, Theme.Ink,
                new Vector2(0.5f, 0.5f), new Vector2(6, 356), new Vector2(1600, 200));
            var title = Theme.Label(root, Theme.Title, 132, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 362), new Vector2(1600, 200));
            StartCoroutine(Pulse(title.transform));

            // Four mode buttons — one consistent size, evenly spaced.
            var dim = new Vector2(460, 78);
            Theme.Button(root, "BLOOD MOON", new Color(0.6f, 0.08f, 0.12f), Color.white, 40,
                new Vector2(0.5f, 0.5f), new Vector2(0, 150), dim, StartDaily);
            Theme.Button(root, "THE CASTLE", new Color(0.28f, 0.24f, 0.32f), Color.white, 40,
                new Vector2(0.5f, 0.5f), new Vector2(0, 50), dim, ShowLevelSelect);
            Theme.Button(root, "ENDLESS NIGHT", Theme.Trick, Color.white, 40,
                new Vector2(0.5f, 0.5f), new Vector2(0, -50), dim, StartEndless);
            Theme.Button(root, "MULTIPLAYER", new Color(0.5f, 0.12f, 0.16f), Color.white, 40,
                new Vector2(0.5f, 0.5f), new Vector2(0, -150), dim, ShowVersusLobby);

            // Settings — secondary, smaller, set apart below the main stack.
            Theme.Button(root, "SETTINGS", new Color(1, 1, 1, 0.12f), new Color(1, 1, 1, 0.85f), 30,
                new Vector2(0.5f, 0.5f), new Vector2(0, -262), new Vector2(300, 60), ShowSettings);
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

            Theme.Label(root, "SETTINGS", 90, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 400), new Vector2(1400, 120));

            // Controls reference (the keyboard dump removed from the main menu).
            Theme.Label(root, "CONTROLS", 40, new Color(1, 1, 1, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1200, 60));
            string[] controls =
            {
                "← →   or   A D       move",
                "SPACE                jump",
                "hold SHIFT           bat-glide  (Endless & Blood Moon)",
                "R                    restart",
                "ESC                  pause",
            };
            for (int i = 0; i < controls.Length; i++)
                Theme.Label(root, controls[i], 32, new Color(1, 1, 1, 0.65f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, 170 - i * 56), new Vector2(1100, 48),
                    TextAnchor.MiddleCenter);

            // Audio toggles — independent of the master HUD mute.
            Theme.Label(root, "AUDIO", 40, new Color(1, 1, 1, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(1200, 60));
            MakeAudioToggle(root, new Vector2(-150, -250), "MUSIC",
                () => !Audio.MusicMuted, v => Audio.MusicMuted = !v);
            MakeAudioToggle(root, new Vector2(150, -250), "SFX",
                () => !Audio.SfxMuted, v => Audio.SfxMuted = !v);

            Theme.Button(root, "‹ BACK", new Color(1, 1, 1, 0.25f), Color.white, 44,
                new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(360, 100), ShowMenu);
        }

        // A labelled ON/OFF toggle backed by getter/setter delegates.
        void MakeAudioToggle(Transform root, Vector2 pos, string name,
            System.Func<bool> get, System.Action<bool> set)
        {
            Button btn = null;
            System.Action refresh = null;
            btn = Theme.Button(root, $"{name}: {(get() ? "ON" : "OFF")}",
                get() ? new Color(0.4f, 0.12f, 0.16f) : new Color(0.2f, 0.2f, 0.24f),
                Color.white, 30, new Vector2(0.5f, 0.5f), pos, new Vector2(260, 80),
                () => { set(!get()); if (!Audio.Muted) Audio.Play("click", 0.6f); refresh(); });
            refresh = () =>
            {
                var label = btn.GetComponentInChildren<Text>();
                if (label != null) label.text = $"{name}: {(get() ? "ON" : "OFF")}";
                // Theme.Button keeps normalColor white and multiplies it with the
                // Image colour, so just swap the Image colour to recolour the button.
                var img = btn.GetComponent<Image>();
                if (img != null) img.color = get() ? new Color(0.4f, 0.12f, 0.16f) : new Color(0.2f, 0.2f, 0.24f);
            };
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

            Theme.Label(root, "THE CASTLE", 90, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 440), new Vector2(1400, 120));
            Theme.Label(root, $"{Levels.Count} floors — pick your poison", 36, new Color(0.85f, 0.7f, 0.72f, 0.7f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 360), new Vector2(1300, 60));

            int cols = 5;
            float spX = 360f, spY = 195f, startX = -((cols - 1) * spX) / 2f, startY = 240f;
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
                    drt.anchoredPosition = p; drt.sizeDelta = new Vector2(20, 20);
                }

            // Each floor is a blood-seal medallion. Floors are LOCKED until you
            // clear the one before — locked nodes are dark and not clickable.
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
                rt.anchoredPosition = pos[i]; rt.sizeDelta = new Vector2(120, 120);
                if (!locked)
                {
                    var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
                    btn.onClick.AddListener(() => StartGame(lvl));
                }
                // Floor number — bright when unlocked, ghosted when locked (the
                // dark disc already reads as "sealed", and this avoids relying on
                // an emoji glyph the built-in font may not have).
                Theme.Label(go.transform, (i + 1).ToString(), 46,
                    locked ? new Color(1, 1, 1, 0.22f) : Color.white,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 120));
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

        void ShowHint(string msg)
        {
            var t = Theme.Label(Theme.Canvas.transform, msg, 34, new Color(1, 1, 1, 0.7f),
                new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(1400, 60));
            StartCoroutine(FadeOutLabel(t, 2.5f));
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

        void StartGame(int levelIndex)
        {
            Audio.Play("click");
            _mode = Mode.Curated;
            BeginRun(levelIndex);
            if (levelIndex == 0)
                ShowHint(_isMobile
                    ? "‹ › move   •   JUMP   •   trust nothing"
                    : "← → / A D move   •   SPACE jump   •   R restart   •   trust nothing");
        }

        void StartDaily()
        {
            Audio.Play("click");
            _mode = Mode.Daily;
            BeginRun(0);
            ShowHint("BLOOD MOON — same run for everyone tonight.  3 lives.  Jump, then hold SHIFT/FLY to glide as a bat.");
        }

        void StartEndless()
        {
            Audio.Play("click");
            _mode = Mode.Endless;
            _endlessSeed = new System.Random().Next(1, 1000000);
            BeginRun(0);
            ShowHint("ENDLESS NIGHT — 3 lives, +1 per floor.  Jump, then hold SHIFT/FLY to glide.  How deep can you go?");
        }

        // ==================== VERSUS (multiplayer) ====================
        // A lobby: HOST makes a room code, JOIN enters one. The code seeds the
        // shared race track so everyone runs the identical level and sees each
        // other live.
        void ShowVersusLobby()
        {
            Audio.Play("click");
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
            BeginRun(0);
            ShowHint($"ROOM {Net.RoomCode}  •  race to the coffin  •  first one there wins. Jump, hold SHIFT to glide.");
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
                sr.sprite = sp; sr.sortingOrder = 4;        // just behind the local player (5)
                sr.color = new Color(0.6f, 0.85f, 1f, 0.55f); // spectral blue, see-through
                float h = sp.bounds.size.y; baseScale = h > 0.0001f ? 1.35f / h : 1f;
                b.transform.localScale = new Vector3(baseScale, baseScale, 1f);
                vis = b.transform;
            }
            else
            {
                var b = Theme.Box("GBody", go.transform, Vector2.zero, new Vector2(0.8f, 0.9f),
                    new Color(0.6f, 0.85f, 1f, 0.55f), 4);
                b.transform.localPosition = Vector3.zero;
                vis = b.transform;
            }
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
            Analytics.Track("versus_result", new System.Collections.Generic.Dictionary<string, object>
            {
                { "won", youWon },
                { "total_deaths", _deaths },
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
            Theme.Label(root, youWon ? "first to the coffin \U0001FA78" : "a faster vampire beat you to it",
                46, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(1500, 70));
            Theme.Label(root, $"{_deaths} death" + (_deaths == 1 ? "" : "s") + " on the way",
                32, Theme.Coin, new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1400, 50));
            Theme.Button(root, "LEAVE RACE", new Color(0.28f, 0.24f, 0.32f), Color.white, 44,
                new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(560, 120),
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
            if (_menuPanel != null) Destroy(_menuPanel);
            _levelIndex = levelIndex;
            _hasCheckpoint = false;
            // Castle deaths are a LIFETIME tally that persists across menu visits
            // and sessions; Endless/Blood Moon deaths are per-run (for the score).
            _deaths = _mode == Mode.Curated ? PlayerPrefs.GetInt("castle_deaths", 0) : 0;
            // Curated and Versus both retry forever (a race death just sends you
            // back to start); Endless/Daily are 3 lives for score.
            _hearts = (_mode == Mode.Curated || _mode == Mode.Versus) ? -1 : 3;
            _hud.gameObject.SetActive(true);
            if (_touchPanel != null) _touchPanel.SetActive(_isMobile); // phone only
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
                // Versus: ONE shared race track, identical for everyone in the
                // room (the room code seeds it, so a new room = a new layout).
                // Kept EASY (difficulty 1: just spikes/late-spikes, no invisible
                // sun traps) so it stays a fun race, not a rage level.
                case Mode.Versus:  return Levels.Generate(Net.Seed, 1);
                default:           return Levels.Get(_levelIndex);
            }
        }

        static int DailySeed()
        {
            var d = System.DateTime.UtcNow.Date;
            return d.Year * 10000 + d.Month * 100 + d.Day;
        }

        // Bump this to force EVERY player (each browser has its own save) back to
        // Floor 1 on their next load — used for a fresh start across friends.
        const int ProgressVersion = 2;
        static void ResetProgressOncePerVersion()
        {
            if (PlayerPrefs.GetInt("progress_version", 0) == ProgressVersion) return;
            PlayerPrefs.SetInt("castle_unlocked", 0);
            PlayerPrefs.SetInt("ti_level", 0);
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
            _level = CurrentLevel();
            _camMin = _level.CamMinX; _camMax = _level.CamMaxX;
            _levelRoot = new GameObject("Level").transform;

            foreach (var p in _level.Platforms)
                BuildPlatform(p);
            foreach (var d in _level.Decos)
                Theme.Box("Deco", _levelRoot, d.pos, d.size, d.color, 2);
            foreach (var t in _level.Traps)
                BuildTrap(t);
            foreach (var pp in _level.Portals)
                BuildPortals(pp);
            BuildAerialHazards();

            SpawnPlayer();
            SnapCamera();
            UpdateHud();

            _levelStartRealtime = Time.realtimeSinceStartup;
            Analytics.Track("level_start", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
            });
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
                var kz = go.AddComponent<KillZone>(); kz.msg = "Caught in the blades.";
            }
        }

        void UpdateHud()
        {
            if (_hud == null) return;
            string left = _mode == Mode.Endless ? $"FLOOR {_levelIndex + 1}    "
                        : _mode == Mode.Daily ? $"NIGHT {_levelIndex + 1}/{DailyLen}    "
                        : _mode == Mode.Versus ? $"RACE {Net.RoomCode}  ({Net.PlayerCount})    " : "";
            string hearts = _hearts >= 0 ? "    " + new string('♥', Mathf.Max(0, _hearts)) : "";
            _hud.text = left + "DEATHS " + _deaths + hearts;
        }

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
            // Single blood-red lip across the top edge (not tiled into the stone).
            // Parented to the FLOOR (not the level root) so a collapsing fake floor
            // takes its lip down with it — no red line left floating in mid-air.
            var edge = Theme.Box("Edge", go.transform, pos + new Vector2(0, size.y / 2f - 0.06f),
                new Vector2(size.x, 0.12f), Theme.PlatEdge, 2);
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
            switch (t.type)
            {
                case TrapType.FakeFloor:
                {
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
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Impaled.";
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
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Shredded.";
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
                default: // LateSpike / Crusher / Surprise / Dart / Faller / Reverse = invisible sensors
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

            var sun = Theme.Hex("FFE9A8"); // pale daylight gold
            float floorY = -2.5f;          // floors sit with top ~ -2.7

            // A SMALL, low shimmer hugging the floor — a hint, not a barrier. (It
            // used to be a tall shaft that read like a wall blocking the path.)
            float w = Mathf.Min(t.size.x, 0.8f);
            var beam = Theme.Box("SunBeam", _levelRoot,
                new Vector2(t.pos.x, floorY + 0.45f), new Vector2(w * 0.5f, 0.9f), sun, 2);
            var bsr = beam.GetComponent<SpriteRenderer>();
            bsr.color = new Color(sun.r, sun.g, sun.b, 0.06f);
            var bp = beam.AddComponent<FaintPulse>(); bp.min = 0.03f; bp.max = 0.09f;

            // the hot patch where it touches the ground
            var patch = Theme.Box("SunPatch", _levelRoot, new Vector2(t.pos.x, floorY),
                new Vector2(w * 0.8f, 0.16f), sun, 3);
            patch.GetComponent<SpriteRenderer>().color = new Color(sun.r, sun.g, sun.b, 0.28f);
            var pp = patch.AddComponent<FaintPulse>(); pp.min = 0.12f; pp.max = 0.32f;
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
            go.transform.position = _hasCheckpoint ? _checkpoint : (Vector3)_level.Spawn;
            go.tag = "Player";

            go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<BoxCollider2D>();
            // Collider matched to the VISIBLE vampire body — wide enough that you
            // die when you actually touch a hazard (too narrow let you stand right
            // next to spikes unharmed), but not the full padded sprite frame.
            col.size = new Vector2(0.55f, 0.85f);
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
            Sprite firstFrame = haveVamp ? vIdle[0]
                : (pmIdle != null && pmIdle.Length > 0) ? pmIdle[0] : beanie;

            if (firstFrame != null)
            {
                var b = new GameObject("Body");
                b.transform.SetParent(go.transform, false);
                // Vampire frames have shadow/padding at the bottom; nudge down so
                // the character's feet sit on the floor, not floating above it.
                b.transform.localPosition = haveVamp ? new Vector3(0f, -0.12f, 0f) : Vector3.zero;
                bodySr = b.AddComponent<SpriteRenderer>();
                bodySr.sprite = firstFrame;
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
            _player.canFly = _mode != Mode.Curated; // The Castle is pure precision — no bat flight
            _playerVisual = vis;
            if (bodySr != null)
            {
                _player.bodyRenderer = bodySr;
                _player.batSprite = Theme.Bat;
                if (haveVamp)
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

        // ==================== camera & loop ====================
        void SnapCamera()
        {
            if (_player == null) return;
            float x = Mathf.Clamp(_player.transform.position.x, _camMin, _camMax);
            _cam.transform.position = new Vector3(x, CamY, -10f);
        }

        void LateUpdate()
        {
            if (_state == State.Play && _player != null)
            {
                float x = Mathf.Clamp(_player.transform.position.x, _camMin, _camMax);
                var p = _cam.transform.position;
                _cam.transform.position = new Vector3(
                    Mathf.Lerp(p.x, x, 10f * Time.unscaledDeltaTime), CamY, -10f);
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
                    else if (_state == State.Play) Time.timeScale = 1f;
                }
            }

            if ((_state == State.Play || _state == State.Paused) &&
                (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)))
                TogglePause();

            // "Still playing" ping every 15s — powers the time-spent-per-level view
            // and lets the dashboard see sessions that never reach an exit.
            if (_state == State.Play)
            {
                _heartbeatTimer += Time.unscaledDeltaTime;
                if (_heartbeatTimer >= 15f)
                {
                    _heartbeatTimer = 0f;
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
            Analytics.Track("death", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
                { "cause", msg ?? "unknown" },
                { "duration_ms", LevelDurationMs },
            });
            if (_mode == Mode.Curated)
            { PlayerPrefs.SetInt("castle_deaths", _deaths); PlayerPrefs.Save(); } // persist lifetime tally
            if (_hearts > 0) _hearts--;     // lose a heart (Endless/Daily); Curated = -1 (infinite)
            Audio.Play("death", 0.9f);
            FlashRed();
            UpdateHud();
            StartCoroutine(DieRoutine(msg ?? Juice.DeathLine()));
        }

        IEnumerator DieRoutine(string msg)
        {
            if (_toast != null) _toast.text = msg;
            Vector3 deathPos = _player != null ? _player.transform.position : Vector3.zero;
            if (_player != null)
            {
                GoreBurst(deathPos);
                BloodSplash(deathPos);
                _player.Freeze();
            }
            StartCoroutine(Juice.Shake(_cam.transform, 0.55f, 0.3f));
            // Vampire has a real death animation; play it instead of the squish.
            if (_player != null && _player.deathFrames != null && _player.deathFrames.Length > 0)
            {
                _player.PlayDeath();
                yield return new WaitForSecondsRealtime(0.35f);
            }
            else if (_playerVisual != null) yield return Juice.Squish(_playerVisual);
            yield return new WaitForSecondsRealtime(0.2f);
            if (_toast != null) _toast.text = "";
            Destroy(_levelRoot.gameObject);
            if (_hearts == 0) RunOver();   // out of hearts -> the run ends
            else BuildLevel();
        }

        // Out of hearts in Endless/Daily — end the run and show the result.
        void RunOver()
        {
            _state = State.Win;
            if (_mode == Mode.Endless && _levelIndex > PlayerPrefs.GetInt("best_endless", 0))
            { PlayerPrefs.SetInt("best_endless", _levelIndex); PlayerPrefs.Save(); }
            Analytics.Track("run_end", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "final_level_index", _levelIndex },
                { "total_deaths", _deaths },
                { "reason", "out_of_lives" },
            });
            Audio.Play("death", 0.7f);

            var panel = Overlay(new Color(0.05f, 0f, 0.02f, 0.85f), out var root);
            Theme.Label(root, "YOU PERISHED", 100, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(1400, 150));
            string reached = _mode == Mode.Endless ? $"reached floor {_levelIndex + 1}"
                                                    : $"fell on night {_levelIndex + 1}/{DailyLen}";
            Theme.Label(root, reached + $"   •   {_deaths} deaths", 50, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1400, 70));

            string share = _mode == Mode.Endless
                ? $"“I reached FLOOR {_levelIndex + 1} of Endless Night in Trust Issues \U0001F987 — beat that”"
                : $"“I only reached night {_levelIndex + 1} of tonight's Blood Moon \U0001F987”";
            Theme.Label(root, share, 30, Theme.Coin,
                new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1500, 60));
            Theme.Label(root, "(screenshot & share your run)", 24, new Color(1, 1, 1, 0.45f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -55), new Vector2(1200, 40));

            Theme.Button(root, "BACK TO THE CASTLE", new Color(0.28f, 0.24f, 0.32f), Color.white, 44,
                new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(640, 120),
                () => { Destroy(panel); ShowMenu(); });
        }

        // ==================== level progression / win ====================
        public void ReachExit()
        {
            if (_state != State.Play) return;
            if (_player != null) _player.Freeze();

            Analytics.Track("level_complete", new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", ModeName },
                { "level_index", _levelIndex },
                { "duration_ms", LevelDurationMs },
                { "deaths", _deaths },
            });

            // Versus: first to the coffin wins. Tell the room and show the result.
            if (_mode == Mode.Versus)
            {
                if (_raceOver) return;
                _raceOver = true;
                Net.SendWin();
                VersusResult(true);
                return;
            }

            Audio.Play("levelup", 0.7f);
            if (_hearts >= 0) _hearts = Mathf.Min(5, _hearts + 1); // bank a heart per floor

            if (_mode == Mode.Endless)
            {
                _levelIndex++;
                if (_levelIndex > PlayerPrefs.GetInt("best_endless", 0))
                { PlayerPrefs.SetInt("best_endless", _levelIndex); PlayerPrefs.Save(); }
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
                    { PlayerPrefs.SetInt(key, _deaths); PlayerPrefs.Save(); }
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
            else { UnlockCastle(Levels.Count - 1); TrackRunComplete(); _state = State.Win; Audio.Play("win", 0.7f); StartCoroutine(WinRoutine()); }
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
            _state = State.Play;
            BuildLevel();
        }

        IEnumerator WinRoutine()
        {
            PlayerPrefs.SetInt("ti_level", 0); PlayerPrefs.Save();
            var panel = Overlay(new Color(0, 0, 0, 0.85f), out var root);
            bool daily = _mode == Mode.Daily;
            Theme.Label(root, daily ? "YOU SURVIVED THE NIGHT" : "YOU ESCAPED THE CASTLE", daily ? 80 : 90, Theme.Exit,
                new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(1600, 160));
            Theme.Label(root, $"died {_deaths} time" + (_deaths == 1 ? "" : "s") + " \U0001FA78",
                60, Theme.Player, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1400, 90));

            string share = daily
                ? $"“I cleared tonight's Blood Moon in Trust Issues with {_deaths} deaths \U0001F987 — beat that”"
                : $"“I escaped the castle in Trust Issues — {_deaths} deaths \U0001F987”";
            Theme.Label(root, share, 30, Theme.Coin,
                new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1600, 60));
            Theme.Label(root, "(screenshot & share)", 24, new Color(1, 1, 1, 0.45f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -55), new Vector2(1200, 40));

            Theme.Button(root, "MAIN MENU", Theme.Trick, Theme.Ink, 50,
                new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(460, 120),
                () => { Destroy(panel); Destroy(_levelRoot.gameObject); ShowMenu(); });
            yield break;
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
                new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(1000, 130));
            Theme.Button(root, "RESUME", Theme.Exit, Theme.Ink, 52,
                new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(460, 120), Resume);
            Theme.Button(root, "RESTART LEVEL", Theme.Trick, Theme.Ink, 46,
                new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(560, 120), RestartLevel);
            Theme.Button(root, "MAIN MENU", new Color(1, 1, 1, 0.25f), Color.white, 46,
                new Vector2(0.5f, 0.5f), new Vector2(0, -210), new Vector2(560, 120), QuitToMenu);
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
