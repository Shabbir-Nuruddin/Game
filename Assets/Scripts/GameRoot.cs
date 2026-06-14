using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// Owns the whole game: auto-boots on Play (no manual scene setup), builds
    /// the level + Beanie from data, handles death/respawn/win, the death
    /// counter, and the HUD. Respawn simply rebuilds the level, so every trap
    /// resets cleanly with zero per-trap bookkeeping.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }

        Camera _cam;
        Transform _levelRoot;
        PlayerController _player;
        Transform _playerVisual;
        Level _level;
        int _deaths;
        bool _dying, _won;
        Text _hud, _toast;

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
            BuildHUD();
            BuildLevel();
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

        void BuildHUD()
        {
            _hud = Theme.Label(Theme.Canvas.transform, "DEATHS  0", 46, Theme.Platform,
                new Vector2(0f, 1f), new Vector2(170, -60), new Vector2(400, 70), TextAnchor.MiddleLeft);

            Theme.Label(Theme.Canvas.transform, Theme.Title, 42, Theme.Trick,
                new Vector2(0.5f, 1f), new Vector2(0, -55), new Vector2(700, 70));

            Theme.Label(Theme.Canvas.transform,
                "Move: A/D or  ←  →     Jump: Space / W     Restart: R",
                30, new Color(1, 1, 1, 0.5f), new Vector2(0.5f, 0f),
                new Vector2(0, 45), new Vector2(1400, 50));

            _toast = Theme.Label(Theme.Canvas.transform, "", 64, Theme.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1400, 100));
        }

        void BuildLevel()
        {
            _dying = _won = false;
            _level = Levels.One();

            _levelRoot = new GameObject("Level").transform;

            foreach (var p in _level.Platforms)
            {
                var go = Theme.Box("Platform", _levelRoot, p.pos, p.size, Theme.Platform, 1);
                Theme.AddSolid(go);
                // a little top edge for depth
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

            // physics body
            var rb = go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.7f, 0.9f);

            // visual child (so we can squash/stretch without touching the collider)
            var vis = Theme.Box("Body", go.transform, _level.Spawn, new Vector2(0.8f, 0.9f),
                Theme.Player, 5);
            vis.transform.localPosition = Vector3.zero;
            // eyes for personality
            Theme.Box("EyeL", vis.transform, Vector2.zero, new Vector2(0.16f, 0.22f), Theme.Ink, 6)
                .transform.localPosition = new Vector3(-0.16f, 0.12f, 0);
            Theme.Box("EyeR", vis.transform, Vector2.zero, new Vector2(0.16f, 0.22f), Theme.Ink, 6)
                .transform.localPosition = new Vector3(0.18f, 0.12f, 0);

            _player = go.AddComponent<PlayerController>();
            _playerVisual = vis.transform;
        }

        void Update()
        {
            if (_player != null && !_dying && !_won && _player.transform.position.y < -9f)
                Die("Gravity wins again.");
        }

        public void Die(string msg = null)
        {
            if (_dying || _won) return;
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
            yield return new WaitForSecondsRealtime(0.25f);
            if (_toast != null) _toast.text = "";
            Destroy(_levelRoot.gameObject);
            BuildLevel();
        }

        public void Win()
        {
            if (_won) return;
            _won = true;
            if (_player != null) _player.Freeze();
            StartCoroutine(WinRoutine());
        }

        IEnumerator WinRoutine()
        {
            var overlay = Theme.Label(Theme.Canvas.transform, "", 0, Color.clear,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var panel = new GameObject("Win", typeof(RectTransform));
            panel.transform.SetParent(Theme.Canvas.transform, false);
            var img = panel.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.78f);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = prt.offsetMax = Vector2.zero;

            Theme.Label(panel.transform, "YOU ESCAPED!", 110, Theme.Exit,
                new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(1400, 160));
            Theme.Label(panel.transform, "(this time)", 44, new Color(1, 1, 1, 0.6f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(900, 60));
            Theme.Label(panel.transform, $"Beanie died {_deaths} time" + (_deaths == 1 ? "" : "s") + " 💀",
                64, Theme.Player, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1400, 90));
            Theme.Label(panel.transform, "Press  SPACE  to play again",
                40, new Color(1, 1, 1, 0.7f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -180), new Vector2(1200, 70));

            // wait for SPACE, then restart fresh (deaths reset for a clean run)
            while (!Input.GetKeyDown(KeyCode.Space)) yield return null;
            Destroy(panel);
            _deaths = 0;
            if (_hud != null) _hud.text = "DEATHS  0";
            Destroy(_levelRoot.gameObject);
            BuildLevel();
        }
    }
}
