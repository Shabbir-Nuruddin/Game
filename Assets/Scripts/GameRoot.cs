using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// Owns the whole game: auto-boots on Play (no manual scene setup), shows a
    /// start screen, builds the level + Beanie from data, handles
    /// death/respawn/win, the death counter, and the HUD. Respawn rebuilds the
    /// level, so every trap resets cleanly with zero per-trap bookkeeping.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }

        enum State { Start, Play, Win }
        State _state = State.Start;

        Camera _cam;
        Transform _levelRoot;
        PlayerController _player;
        Transform _playerVisual;
        Level _level;
        int _deaths;
        bool _dying;
        Text _hud, _toast;
        GameObject _startPanel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindFirstObjectByType<GameRoot>() == null)
                new GameObject("TrustIssues").AddComponent<GameRoot>();
        }

        void Awake()
        {
            I = this;
            SetupCamera();
            BuildBackdrop();
            BuildHUD();
            ShowStart();
        }

        void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.orthographicSize = 5.6f;
            _cam.transform.position = new Vector3(-1.5f, -1.2f, -10f);
            _cam.backgroundColor = Theme.Sky;
            _cam.clearFlags = CameraClearFlags.SolidColor;
        }

        // A gradient sky + soft floating shapes for depth, so it doesn't read as
        // a flat 2D test scene. Sits behind everything (negative sort order).
        void BuildBackdrop()
        {
            var sky = new GameObject("Sky");
            sky.transform.position = new Vector3(-1.5f, -1.2f, 5f);
            sky.transform.localScale = new Vector3(30f, 16f, 1f);
            var sr = sky.AddComponent<SpriteRenderer>();
            sr.sprite = Theme.Gradient(Theme.SkyLow, Theme.Sky);
            sr.sortingOrder = -20;

            // distant soft shapes (parallax-feel, static)
            var shape = new Color(1f, 1f, 1f, 0.04f);
            Theme.Box("Blob", null, new Vector2(-7f, 3.5f), new Vector2(7f, 7f), shape, -15);
            Theme.Box("Blob", null, new Vector2(5f, 2.5f), new Vector2(9f, 9f), shape, -15);
            Theme.Box("Glow", null, new Vector2(3f, -4.8f), new Vector2(3f, 3f),
                new Color(Theme.Exit.r, Theme.Exit.g, Theme.Exit.b, 0.06f), -15); // hint near real exit
        }

        void BuildHUD()
        {
            // Pushed inward so it can't clip on any aspect ratio.
            _hud = Theme.Label(Theme.Canvas.transform, "DEATHS  0", 46, Theme.Player,
                new Vector2(0f, 1f), new Vector2(280, -55), new Vector2(520, 70),
                TextAnchor.MiddleLeft);

            _toast = Theme.Label(Theme.Canvas.transform, "", 60, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1400, 100));
        }

        // -------------------- Start screen --------------------
        void ShowStart()
        {
            _state = State.Start;
            _hud.gameObject.SetActive(false);

            _startPanel = new GameObject("Start", typeof(RectTransform));
            _startPanel.transform.SetParent(Theme.Canvas.transform, false);
            var img = _startPanel.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.35f);
            var rt = _startPanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var t = Theme.Label(_startPanel.transform, Theme.Title, 150, Theme.Trick,
                new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(1600, 200));
            StartCoroutine(Pulse(t.transform));

            Theme.Label(_startPanel.transform, "the level is lying to you.", 48,
                new Color(1, 1, 1, 0.7f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 60), new Vector2(1400, 70));

            Theme.Label(_startPanel.transform, "Reach the GREEN.  Trust nothing.", 40,
                Theme.Exit, new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(1400, 60));

            var press = Theme.Label(_startPanel.transform, "Press  SPACE  to start", 52,
                Theme.Player, new Vector2(0.5f, 0.5f), new Vector2(0, -190), new Vector2(1400, 80));
            StartCoroutine(Blink(press));

            Theme.Label(_startPanel.transform,
                "Move: A/D or  ←  →      Jump: Space / W      Restart: R", 30,
                new Color(1, 1, 1, 0.45f), new Vector2(0.5f, 0f),
                new Vector2(0, 50), new Vector2(1400, 50));

            StartCoroutine(WaitToStart());
        }

        IEnumerator WaitToStart()
        {
            while (!(Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)))
                yield return null;
            Destroy(_startPanel);
            _hud.gameObject.SetActive(true);
            _state = State.Play;
            BuildLevel();
        }

        // -------------------- Level build --------------------
        void BuildLevel()
        {
            _dying = false;
            _level = Levels.One();
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

            SpawnPlayer();
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
                default: // LateSpike / Crusher = invisible sensors
                {
                    var go = Theme.Box(t.type.ToString(), _levelRoot, t.pos, t.size,
                        new Color(0, 0, 0, 0f), 0);
                    Theme.AddTrigger(go, Vector2.one);
                    go.AddComponent<Trap>().Init(t.type);
                    break;
                }
            }
        }

        void SpawnPlayer()
        {
            var go = new GameObject("Beanie");
            go.transform.SetParent(_levelRoot, false);
            go.transform.position = _level.Spawn;
            go.tag = "Player";

            var rb = go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.7f, 0.9f);

            var vis = Theme.Box("Body", go.transform, _level.Spawn, new Vector2(0.8f, 0.9f),
                Theme.Player, 5);
            vis.transform.localPosition = Vector3.zero;
            Theme.Box("EyeL", vis.transform, Vector2.zero, new Vector2(0.16f, 0.22f), Theme.Ink, 6)
                .transform.localPosition = new Vector3(-0.16f, 0.12f, 0);
            Theme.Box("EyeR", vis.transform, Vector2.zero, new Vector2(0.16f, 0.22f), Theme.Ink, 6)
                .transform.localPosition = new Vector3(0.18f, 0.12f, 0);

            _player = go.AddComponent<PlayerController>();
            _playerVisual = vis.transform;
        }

        void Update()
        {
            if (_state == State.Play && _player != null && !_dying &&
                _player.transform.position.y < -9f)
                Die("Gravity wins again.");
        }

        public void Die(string msg = null)
        {
            if (_state != State.Play || _dying) return;
            _dying = true;
            _deaths++;
            if (_hud != null) _hud.text = "DEATHS  " + _deaths;
            StartCoroutine(DieRoutine(msg ?? Juice.DeathLine()));
        }

        IEnumerator DieRoutine(string msg)
        {
            if (_toast != null) _toast.text = msg;
            if (_player != null) _player.Freeze();
            StartCoroutine(Juice.Shake(_cam.transform));
            if (_playerVisual != null) yield return Juice.Squish(_playerVisual);
            yield return new WaitForSecondsRealtime(0.2f);
            if (_toast != null) _toast.text = "";
            Destroy(_levelRoot.gameObject);
            BuildLevel();
        }

        public void Win()
        {
            if (_state != State.Play) return;
            _state = State.Win;
            if (_player != null) _player.Freeze();
            StartCoroutine(WinRoutine());
        }

        IEnumerator WinRoutine()
        {
            var panel = new GameObject("Win", typeof(RectTransform));
            panel.transform.SetParent(Theme.Canvas.transform, false);
            var img = panel.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.8f);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = prt.offsetMax = Vector2.zero;

            Theme.Label(panel.transform, "YOU ESCAPED!", 110, Theme.Exit,
                new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(1400, 160));
            Theme.Label(panel.transform, "(this time)", 44, new Color(1, 1, 1, 0.6f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(900, 60));
            Theme.Label(panel.transform,
                $"Beanie died {_deaths} time" + (_deaths == 1 ? "" : "s") + " 💀",
                64, Theme.Player, new Vector2(0.5f, 0.5f), new Vector2(0, -40),
                new Vector2(1400, 90));
            Theme.Label(panel.transform, "Press  SPACE  to play again", 40,
                new Color(1, 1, 1, 0.7f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -180), new Vector2(1200, 70));

            while (!Input.GetKeyDown(KeyCode.Space)) yield return null;
            Destroy(panel);
            _deaths = 0;
            if (_hud != null) _hud.text = "DEATHS  0";
            Destroy(_levelRoot.gameObject);
            _state = State.Play;
            BuildLevel();
        }

        // -------------------- tiny UI animations --------------------
        IEnumerator Pulse(Transform t)
        {
            while (t != null)
            {
                float s = 1f + Mathf.Sin(Time.unscaledTime * 2f) * 0.03f;
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
        }

        IEnumerator Blink(Text t)
        {
            while (t != null)
            {
                var c = t.color;
                c.a = 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
                t.color = c;
                yield return null;
            }
        }
    }
}
