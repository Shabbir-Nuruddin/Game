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
        float _camMin = -1.5f, _camMax = -1.5f;
        const float CamY = -1.2f;

        Text _hud, _toast;
        GameObject _menuPanel, _pausePanel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindFirstObjectByType<GameRoot>() == null)
                new GameObject("TrustIssues").AddComponent<GameRoot>();
        }

        void Awake()
        {
            I = this;
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
        }

        // ==================== MAIN MENU ====================
        void ShowMenu()
        {
            _state = State.Menu;
            Time.timeScale = 1f;
            _hud.gameObject.SetActive(false);
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

            Theme.Button(root, "NEW GAME", Theme.Player, Theme.Ink, 56,
                new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(480, 130),
                () => StartGame(0));

            int saved = PlayerPrefs.GetInt("ti_level", 0);
            if (saved > 0)
                Theme.Button(root, $"CONTINUE  •  Level {saved + 1}", Theme.Trick, Theme.Ink, 46,
                    new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(580, 120),
                    () => StartGame(saved));

            Theme.Label(root, "don't trust the floor.", 32,
                new Color(1, 1, 1, 0.4f), new Vector2(0.5f, 0f),
                new Vector2(0, 60), new Vector2(1400, 50));
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
            _deaths = 0;
            _hud.text = "DEATHS  0";
            _hud.gameObject.SetActive(true);
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
            {
                var go = Theme.Box("Platform", _levelRoot, p.pos, p.size, Theme.Platform, 1);
                Theme.AddSolid(go);
                Theme.Box("Edge", _levelRoot, p.pos + new Vector2(0, p.size.y / 2f - 0.05f),
                    new Vector2(p.size.x, 0.1f), Theme.PlatEdge, 2);
            }
            foreach (var d in _level.Decos)
                Theme.Box("Deco", _levelRoot, d.pos, d.size, d.color, 2);
            foreach (var t in _level.Traps)
                BuildTrap(t);
            foreach (var pp in _level.Portals)
                BuildPortals(pp);

            SpawnPlayer();
            SnapCamera();
        }

        void BuildTrap(TrapSpec t)
        {
            switch (t.type)
            {
                case TrapType.FakeFloor:
                {
                    var go = Theme.Box("FakeFloor", _levelRoot, t.pos, t.size, Theme.Platform, 1);
                    Theme.AddSolid(go);
                    go.AddComponent<Trap>().Init(TrapType.FakeFloor);
                    break;
                }
                case TrapType.FakeExit:
                {
                    var go = Theme.Box("FakeExit", _levelRoot, t.pos, t.size, Theme.Trick, 2);
                    Theme.AddTrigger(go, Vector2.one);
                    go.AddComponent<Trap>().Init(TrapType.FakeExit);
                    Theme.Box("DoorKnob", go.transform, t.pos + new Vector2(0.25f, 0),
                        new Vector2(0.15f, 0.15f), Theme.Coin, 3);
                    break;
                }
                case TrapType.RealExit:
                {
                    var go = Theme.Box("RealExit", _levelRoot, t.pos, t.size, Theme.Exit, 2);
                    Theme.AddTrigger(go, Vector2.one);
                    go.AddComponent<Trap>().Init(TrapType.RealExit);
                    break;
                }
                case TrapType.Spring:
                {
                    var go = Theme.Box("Spring", _levelRoot, t.pos, t.size, Theme.Coin, 3);
                    Theme.AddTrigger(go, Vector2.one);
                    go.AddComponent<Trap>().Init(TrapType.Spring);
                    break;
                }
                case TrapType.Saw:
                {
                    var go = Theme.Box("Saw", _levelRoot, t.pos, t.size, Theme.Danger, 3);
                    Theme.AddTrigger(go, Vector2.one);
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
            go.transform.position = _level.Spawn;
            go.tag = "Player";

            go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.7f, 0.9f);

            // Use the pink character sprite if present; otherwise fall back to a
            // pink box with eyes (so the game still runs without art).
            SpriteRenderer bodySr = null;
            Transform vis;
            var idle = Assets.Sprite("beanie_idle");
            if (idle != null)
            {
                var b = new GameObject("Body");
                b.transform.SetParent(go.transform, false);
                b.transform.localPosition = Vector3.zero;
                bodySr = b.AddComponent<SpriteRenderer>();
                bodySr.sprite = idle;
                bodySr.sortingOrder = 5;
                float h = idle.bounds.size.y;
                float s = h > 0.0001f ? 1.15f / h : 1f; // scale sprite to ~1.15 units tall
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
                _player.idleSprite = idle;
                _player.walkSprite = Assets.Sprite("beanie_walk");
                _player.jumpSprite = Assets.Sprite("beanie_walk");
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
            if ((_state == State.Play || _state == State.Paused) &&
                (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)))
                TogglePause();

            if (_state == State.Play && _player != null && !_dying &&
                _player.transform.position.y < -9f)
                Die("Gravity wins again.");
        }

        // ==================== death / respawn ====================
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
            if (_player != null) _player.Freeze();
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
