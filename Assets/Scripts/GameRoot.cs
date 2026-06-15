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
        float _camMin = -1.5f, _camMax = -1.5f;
        const float CamY = -1.2f;

        Text _hud, _toast;
        GameObject _menuPanel, _pausePanel, _touchPanel, _rotatePanel;

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
            _isMobile = Application.isMobilePlatform || Input.touchSupported;
            Time.timeScale = 1f;
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

        // Gradient sky + soft shapes, parented to the camera so they always fill
        // the view as it scrolls.
        void BuildBackdrop()
        {
            var sky = new GameObject("Sky");
            sky.transform.SetParent(_cam.transform, false);
            sky.transform.localPosition = new Vector3(0f, 0f, 20f);
            sky.transform.localScale = new Vector3(34f, 16f, 1f);
            var sr = sky.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.Gradient(Theme.SkyLow, Theme.Sky);
            sr.sortingOrder = -20;

            var shape = new Color(1f, 1f, 1f, 0.04f);
            MakeBackShape(new Vector2(-7f, 4.5f), new Vector2(7f, 7f), shape);
            MakeBackShape(new Vector2(6f, 3.5f), new Vector2(9f, 9f), shape);

            // Faint giant candy floating in the background for theme.
            BgCandy("candy_lolly", new Vector2(-6f, 2f), 5f);
            BgCandy("candy_cherry", new Vector2(7f, 2.5f), 4f);
            BgCandy("candy_cane", new Vector2(0f, -3.5f), 4.5f);
        }

        void BgCandy(string name, Vector2 local, float size)
        {
            var sp = Assets.Sprite(name);
            if (sp == null) return;
            var go = Theme.SpriteBox("BgCandy", _cam.transform, Vector3.zero,
                new Vector2(size, size), sp, -16);
            go.transform.localPosition = new Vector3(local.x, local.y, 18f);
            var sr = go.GetComponent<SpriteRenderer>();
            var c = sr.color; c.a = 0.08f; sr.color = c;
        }

        void MakeBackShape(Vector2 local, Vector2 size, Color c)
        {
            var go = Theme.Box("Blob", _cam.transform, Vector2.zero, size, c, -15);
            go.transform.localPosition = new Vector3(local.x, local.y, 18f);
        }

        void BuildHUD()
        {
            _hud = Theme.Label(Theme.Canvas.transform, "DEATHS  0", 46, Theme.Player,
                new Vector2(0f, 1f), new Vector2(280, -55), new Vector2(520, 70),
                TextAnchor.MiddleLeft);
            _toast = Theme.Label(Theme.Canvas.transform, "", 60, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1400, 100));
            BuildTouchControls();
            BuildRotatePanel();
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
            _state = State.Menu;
            Time.timeScale = 1f;
            Audio.Music("music", 0.3f);
            _hud.gameObject.SetActive(false);
            if (_touchPanel != null) { _touchPanel.SetActive(false); TouchInput.Clear(); }
            _cam.transform.position = new Vector3(-1.5f, CamY, -10f);

            // Soft plum wash over the gradient (keeps the candy mood).
            _menuPanel = Overlay(new Color(Theme.Sky.r, Theme.Sky.g, Theme.Sky.b, 0.5f), out var root);

            // Floating candy decorations.
            MenuCandy(root, "candy_cherry", new Vector2(-720, 300), 130, -15);
            MenuCandy(root, "candy_lolly",  new Vector2(720, 330), 150, 18);
            MenuCandy(root, "candy_cane",   new Vector2(-770, -250), 160, -12);
            MenuCandy(root, "candy_red",    new Vector2(740, -280), 120, 14);
            MenuCandy(root, "coin",         new Vector2(-560, 60), 90, 0);
            MenuCandy(root, "portal",       new Vector2(580, 30), 110, 10);

            // Title with a lavender drop-shadow, in candy pink.
            Theme.Label(root, Theme.Title, 150, Theme.Trick,
                new Vector2(0.5f, 0.5f), new Vector2(8, 222), new Vector2(1600, 200));
            var title = Theme.Label(root, Theme.Title, 150, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(1600, 200));
            StartCoroutine(Pulse(title.transform));

            Theme.Label(root, "a cute little nightmare \U0001F36C", 46,
                new Color(1f, 0.86f, 0.92f, 0.9f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 110), new Vector2(1400, 70));

            Theme.Button(root, "NEW GAME", Theme.Player, Theme.Ink, 54,
                new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(480, 120),
                ShowStory);

            Theme.Button(root, $"LEVELS ({Levels.Count})", Theme.Trick, Theme.Ink, 48,
                new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(480, 110),
                ShowLevelSelect);

            int saved = PlayerPrefs.GetInt("ti_level", 0);
            if (saved > 0)
                Theme.Button(root, $"CONTINUE • Lvl {saved + 1}", new Color(1, 1, 1, 0.25f), Color.white, 42,
                    new Vector2(0.5f, 0.5f), new Vector2(0, -250), new Vector2(480, 100),
                    () => StartGame(saved));

            Theme.Label(root, "don't trust the floor.", 32,
                new Color(1, 1, 1, 0.4f), new Vector2(0.5f, 0f),
                new Vector2(0, 50), new Vector2(1400, 50));
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

            Theme.Label(root, "CANDY MAP", 90, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 440), new Vector2(1400, 120));
            Theme.Label(root, $"{Levels.Count} levels — pick your poison", 36, new Color(1, 1, 1, 0.6f),
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

            // dotted candy trail between nodes
            for (int i = 0; i < Levels.Count - 1; i++)
                for (int d = 1; d <= 3; d++)
                {
                    var p = Vector2.Lerp(pos[i], pos[i + 1], d / 4f);
                    var dot = new GameObject("Dot", typeof(RectTransform));
                    dot.transform.SetParent(root, false);
                    var di = dot.AddComponent<Image>();
                    di.color = new Color(1, 1, 1, 0.22f);
                    var drt = di.rectTransform;
                    drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
                    drt.pivot = new Vector2(0.5f, 0.5f);
                    drt.anchoredPosition = p; drt.sizeDelta = new Vector2(20, 20);
                }

            string[] candies = { "candy_lolly", "candy_cherry", "candy_red", "coin", "portal" };
            for (int i = 0; i < Levels.Count; i++)
            {
                int lvl = i;
                var sp = Assets.Sprite(candies[i % candies.Length]);
                var go = new GameObject("Node" + (i + 1), typeof(RectTransform));
                go.transform.SetParent(root, false);
                var img = go.AddComponent<Image>();
                if (sp != null) { img.sprite = sp; img.color = Color.white; img.preserveAspect = true; }
                else img.color = Theme.Trick;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos[i]; rt.sizeDelta = new Vector2(150, 150);
                var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
                btn.onClick.AddListener(() => StartGame(lvl));
                Theme.Label(go.transform, (i + 1).ToString(), 50, Theme.Ink,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 150));
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
        void GoreBurst(Vector3 pos)
        {
            for (int i = 0; i < 14; i++)
            {
                var g = Theme.Box("Gore", transform, pos, new Vector2(0.18f, 0.18f), Theme.Danger, 8);
                StartCoroutine(GoreBit(g.transform,
                    new Vector2(Random.Range(-5f, 5f), Random.Range(3f, 8f))));
            }
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
            if (_menuPanel != null) Destroy(_menuPanel);
            _levelIndex = levelIndex;
            _hasCheckpoint = false;
            _hud.text = "DEATHS  " + _deaths;            // keep the running total
            _hud.gameObject.SetActive(true);
            if (_touchPanel != null) _touchPanel.SetActive(_isMobile); // phone only
            _state = State.Play;
            BuildLevel();
            if (levelIndex == 0) ShowHint("A / D  or  ← →  to move    •    SPACE to jump");
        }

        // ==================== LEVEL ====================
        void BuildLevel()
        {
            _dying = false;
            _level = Levels.Get(_levelIndex);
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

            SpawnPlayer();
            SnapCamera();
        }

        // A platform: candy tile (tiled) if the sprite is present, else a cream box.
        void BuildPlatform(Rect2 p)
        {
            var sp = Assets.Sprite("platform");
            if (sp != null)
            {
                var go = new GameObject("Platform");
                go.transform.SetParent(_levelRoot, false);
                go.transform.position = p.pos;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = p.size;
                sr.sortingOrder = 1;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = p.size;
            }
            else
            {
                var go = Theme.Box("Platform", _levelRoot, p.pos, p.size, Theme.Platform, 1);
                Theme.AddSolid(go);
                Theme.Box("Edge", _levelRoot, p.pos + new Vector2(0, p.size.y / 2f - 0.05f),
                    new Vector2(p.size.x, 0.1f), Theme.PlatEdge, 2);
            }
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
                    // Must look IDENTICAL to a real platform (same candy tile).
                    var sp = Assets.Sprite("platform");
                    GameObject go;
                    if (sp != null)
                    {
                        go = new GameObject("FakeFloor");
                        go.transform.SetParent(_levelRoot, false);
                        go.transform.position = t.pos;
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = sp; sr.drawMode = SpriteDrawMode.Tiled;
                        sr.size = t.size; sr.sortingOrder = 1;
                        var col = go.AddComponent<BoxCollider2D>(); col.size = t.size;
                    }
                    else
                    {
                        go = Theme.Box("FakeFloor", _levelRoot, t.pos, t.size, Theme.Platform, 1);
                        Theme.AddSolid(go);
                    }
                    go.AddComponent<Trap>().Init(TrapType.FakeFloor);
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
                    var sp = Assets.Sprite("door");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("RealExit", _levelRoot, t.pos, new Vector2(1.5f, 1.5f), sp, 2)
                        : Theme.Box("RealExit", _levelRoot, t.pos, t.size, Theme.Exit, 2);
                    // natural gold trophy = the real goal (no muddy tint)
                    FitTrigger(go, 0.85f);
                    go.AddComponent<Trap>().Init(TrapType.RealExit);
                    break;
                }
                case TrapType.SpikeStatic:
                {
                    var sp = Assets.Sprite("spike");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Spikes", _levelRoot, t.pos, t.size, sp, 3)
                        : Theme.Box("Spikes", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    var c = go.AddComponent<BoxCollider2D>(); c.isTrigger = true;
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Spikes. Obviously.";
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
                    var sp = Assets.Sprite("saw");
                    GameObject go = sp != null
                        ? Theme.SpriteBox("Saw", _levelRoot, t.pos, new Vector2(1.1f, 1.1f), sp, 3)
                        : Theme.Box("Saw", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    FitTrigger(go, 0.5f); // forgiving hitbox (was a full grid cell)
                    var kz = go.AddComponent<KillZone>(); kz.msg = "Sliced.";
                    go.AddComponent<Trap>().Init(TrapType.Saw);
                    break;
                }
                default: // LateSpike / Crusher / Surprise / Dart / Faller / WarpBack / Reverse = invisible sensors
                {
                    var go = Theme.Box(t.type.ToString(), _levelRoot, t.pos, t.size,
                        new Color(0, 0, 0, 0f), 0);
                    Theme.AddTrigger(go, Vector2.one);
                    go.AddComponent<Trap>().Init(t.type);
                    break;
                }
            }
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
            col.size = new Vector2(0.7f, 0.9f);

            // Prefer the animated Pink Man (sliced from sheets); then a single
            // beanie sprite; then a pink box with eyes — so it always runs.
            SpriteRenderer bodySr = null;
            Transform vis;
            var pmIdle = Assets.Sheet("pinkman_idle", 32);
            var pmRun = Assets.Sheet("pinkman_run", 32);
            var pmJump = Assets.Sheet("pinkman_jump", 32);
            var beanie = Assets.Sprite("beanie_idle");
            Sprite firstFrame = (pmIdle != null && pmIdle.Length > 0) ? pmIdle[0] : beanie;

            if (firstFrame != null)
            {
                var b = new GameObject("Body");
                b.transform.SetParent(go.transform, false);
                b.transform.localPosition = Vector3.zero;
                bodySr = b.AddComponent<SpriteRenderer>();
                bodySr.sprite = firstFrame;
                bodySr.sortingOrder = 5;
                float h = firstFrame.bounds.size.y;
                float s = h > 0.0001f ? 1.3f / h : 1f;
                b.transform.localScale = new Vector3(s, s, 1f);
                vis = b.transform;
            }
            else
            {
                var b = Theme.Box("Body", go.transform, _level.Spawn, new Vector2(0.8f, 0.9f),
                    Theme.Player, 5);
                b.transform.localPosition = Vector3.zero;
                Theme.Box("EyeL", b.transform, Vector2.zero, new Vector2(0.16f, 0.22f), Theme.Ink, 6)
                    .transform.localPosition = new Vector3(-0.16f, 0.12f, 0);
                Theme.Box("EyeR", b.transform, Vector2.zero, new Vector2(0.16f, 0.22f), Theme.Ink, 6)
                    .transform.localPosition = new Vector3(0.18f, 0.12f, 0);
                vis = b.transform;
            }

            _player = go.AddComponent<PlayerController>();
            _playerVisual = vis;
            if (bodySr != null)
            {
                _player.bodyRenderer = bodySr;
                if (pmIdle != null && pmIdle.Length > 0)
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
            if (Input.touchSupported || Application.isMobilePlatform)
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

            if (_state == State.Play && _player != null && !_dying &&
                _player.transform.position.y < -9f)
                Die("Gravity wins again.");
        }

        // ==================== death / respawn ====================
        public void SetCheckpoint(Vector3 pos)
        {
            _checkpoint = pos + Vector3.up * 0.6f;
            _hasCheckpoint = true;
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
            FlashRed();
        }

        public void Die(string msg = null)
        {
            if (_state != State.Play || _dying) return;
            _dying = true;
            _deaths++;
            Audio.Play("death", 0.9f);
            FlashRed();
            if (_hud != null) _hud.text = "DEATHS  " + _deaths;
            StartCoroutine(DieRoutine(msg ?? Juice.DeathLine()));
        }

        IEnumerator DieRoutine(string msg)
        {
            if (_toast != null) _toast.text = msg;
            if (_player != null) { GoreBurst(_player.transform.position); _player.Freeze(); }
            StartCoroutine(Juice.Shake(_cam.transform, 0.55f, 0.3f));
            if (_playerVisual != null) yield return Juice.Squish(_playerVisual);
            yield return new WaitForSecondsRealtime(0.2f);
            if (_toast != null) _toast.text = "";
            Destroy(_levelRoot.gameObject);
            BuildLevel();
        }

        // ==================== level progression / win ====================
        public void ReachExit()
        {
            if (_state != State.Play) return;
            if (_player != null) _player.Freeze();

            if (_levelIndex + 1 < Levels.Count)
            {
                _levelIndex++;
                PlayerPrefs.SetInt("ti_level", _levelIndex); PlayerPrefs.Save();
                Audio.Play("levelup", 0.7f);
                StartCoroutine(NextLevelFlash());
            }
            else
            {
                _state = State.Win;
                Audio.Play("win", 0.7f);
                StartCoroutine(WinRoutine());
            }
        }

        IEnumerator NextLevelFlash()
        {
            _state = State.Win; // block input briefly
            if (_toast != null) _toast.text = $"LEVEL {_levelIndex + 1}";
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
            var panel = Overlay(new Color(0, 0, 0, 0.8f), out var root);
            Theme.Label(root, "YOU BEAT IT!", 110, Theme.Exit,
                new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(1400, 160));
            Theme.Label(root, $"Beanie died {_deaths} time" + (_deaths == 1 ? "" : "s") + " \U0001F480",
                64, Theme.Player, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1400, 90));
            Theme.Button(root, "MAIN MENU", Theme.Trick, Theme.Ink, 50,
                new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(460, 120),
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
