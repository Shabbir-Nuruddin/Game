using System.Collections;
using System.IO;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// The "let me actually SEE it" harness.
    ///
    /// Every screen and every level in this game is built from code, so there is
    /// nothing to look at in the editor and no way to check the castle against the
    /// reference artwork short of installing an APK by hand. ShotBot boots the real
    /// game, walks it through a short itinerary (menu → floor → a spot mid-floor →
    /// the Bestiary), writes a PNG of each and quits.
    ///
    /// It is completely inert unless the player is launched with -shot, so it costs
    /// a shipped build nothing but a few unused bytes:
    ///
    ///   TrustIssues.exe -shot C:\out -floor 1 -warp 6,20 -screen-width 1568 -screen-height 1003
    /// </summary>
    public class ShotBot : MonoBehaviour
    {
        string _dir;
        int _floor = 1;
        readonly System.Collections.Generic.List<float> _warps = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            string dir = null; int floor = 1; string warps = null; bool touch = false;
            for (int i = 0; i < args.Length; i++)
            {
                bool hasNext = i + 1 < args.Length;
                if (args[i] == "-shot" && hasNext) dir = args[i + 1];
                else if (args[i] == "-floor" && hasNext) int.TryParse(args[i + 1], out floor);
                else if (args[i] == "-warp" && hasNext) warps = args[i + 1];
                else if (args[i] == "-touch") touch = true;
            }
            if (string.IsNullOrEmpty(dir)) return;   // a normal launch — do nothing at all

            // -touch forces the PHONE layout on in a desktop shot. The on-screen
            // controls are the part of the game most often checked against the
            // reference artwork, and they're invisible on desktop by default —
            // which meant every button change had to go out as an APK before
            // anyone could see whether it was right.
            if (touch)
            {
                PlayerPrefs.SetInt("opt_touch", 1);
                // Arrow pads, not the joystick: the joystick is deliberately
                // invisible until a thumb lands on it, so a joystick shot shows an
                // empty corner and proves nothing about the movement controls.
                PlayerPrefs.SetInt("opt_joystick", 0);
            }

            var bot = new GameObject("ShotBot").AddComponent<ShotBot>();
            bot._dir = dir;
            bot._floor = Mathf.Max(1, floor);
            if (!string.IsNullOrEmpty(warps))
                foreach (var part in warps.Split(','))
                    if (float.TryParse(part, out float x)) bot._warps.Add(x);
            bot.StartCoroutine(bot.Run());
        }

        IEnumerator Run()
        {
            Directory.CreateDirectory(_dir);
            // The launch video covers everything at sorting order 32000, and it is
            // deliberately un-skippable for its first half-second — faster to simply
            // remove it than to fake a keypress.
            yield return new WaitForSecondsRealtime(0.6f);
            var intro = GameObject.Find("Intro");
            if (intro != null) Destroy(intro);
            // Never let a shot session be silent-but-audible on someone's desktop.
            Audio.StopMusic();

            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("menu");

            var root = GameRoot.I;
            if (root == null) { Debug.Log("SHOTBOT_NO_ROOT"); Application.Quit(); yield break; }

            root.DevOpenWardrobe();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("wardrobe");

            root.DevOpenAuraWardrobe();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("wardrobe_aura");

            root.DevOpenOutfitWardrobe();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("wardrobe_outfit");

            root.DevStartFloor(_floor - 1);
            yield return new WaitForSecondsRealtime(1.6f);   // let the player land and the camera settle
            Probe();
            yield return Shot($"floor{_floor}_spawn");

            for (int i = 0; i < _warps.Count; i++)
            {
                root.DevWarp(_warps[i]);
                yield return new WaitForSecondsRealtime(1.1f);
                yield return Shot($"floor{_floor}_x{Mathf.RoundToInt(_warps[i])}");
            }

            root.DevOpenBestiary();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("bestiary");

            // Both leaderboard states, because they are genuinely different screens:
            // ranked (your row highlighted mid-table, with the rival above you named)
            // and unranked (never scored — the board still has to look alive).
            root.DevOpenLeaderboard("endless", 655);
            yield return new WaitForSecondsRealtime(1.0f);
            yield return Shot("leaderboard_endless");

            root.DevOpenLeaderboard("castle", 0);
            yield return new WaitForSecondsRealtime(1.0f);
            yield return Shot("leaderboard_castle_unranked");

            // The castle map, with progress faked to the floor being shot, so the
            // "you are here" glow can be checked against the right seal.
            root.DevOpenCastle(_floor - 1);
            yield return new WaitForSecondsRealtime(1.0f);
            yield return Shot("castle");

            // Every settings tab, because they are four different layouts sharing one
            // frame and only the one you last opened would otherwise be checked.
            string[] tabs = { "audio", "controls", "difficulty", "legal" };
            for (int t = 0; t < tabs.Length; t++)
            {
                root.DevOpenSettings(t);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("settings_" + tabs[t]);
            }

            root.DevOpenLobby();
            yield return new WaitForSecondsRealtime(1.0f);
            yield return Shot("lobby");

            Debug.Log("SHOTBOT_DONE " + _dir);
            Application.Quit();
        }

        // What the camera can actually SEE, and where the scenery sits inside it.
        // Composition work (how high the moon hangs, how much masonry fills the top
        // of frame) was being done by eye off screenshots; this prints the numbers.
        static void Probe()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Corners of the gameplay plane (z = 0) as the camera frames it.
            var bl = cam.ScreenToWorldPoint(new Vector3(0, 0, -cam.transform.position.z));
            var tr = cam.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, -cam.transform.position.z));
            Debug.Log($"SHOTBOT_VIEW x[{bl.x:F2}..{tr.x:F2}] y[{bl.y:F2}..{tr.y:F2}] " +
                      $"ortho={cam.orthographic} size={cam.orthographicSize:F2} fov={cam.fieldOfView:F1} campos={cam.transform.position}");
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                string n = sr.gameObject.name;
                if (n != "Ceiling" && n != "Vault" && n != "Wall" && n != "ThemeMoon" && n != "ThemeWash") continue;
                var b = sr.bounds;
                Debug.Log($"SHOTBOT_OBJ {n} centre={b.center} size={b.size} order={sr.sortingOrder} col={sr.color}");
            }

            // Who owns a given pixel? Anything full-screen and opaque in the UI will
            // beat every sprite in the world, and that is invisible from the numbers
            // above — so ask the canvas directly, then the world.
            foreach (int sy in new[] { Screen.height - 150, Screen.height / 2 })
            {
                var sp = new Vector2(Screen.width / 2f, sy);
                var canvases = Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None);
                foreach (var g in canvases)
                {
                    if (g.color.a < 0.5f || !g.isActiveAndEnabled) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, sp, null)) continue;
                    Debug.Log($"SHOTBOT_UI y={sy} {Trail(g.transform)} col={g.color} size={g.rectTransform.rect.size}");
                }
                var wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -cam.transform.position.z));
                foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                {
                    if (!sr.enabled || !sr.gameObject.activeInHierarchy || sr.color.a < 0.5f) continue;
                    var b = sr.bounds;
                    if (wp.x < b.min.x || wp.x > b.max.x || wp.y < b.min.y || wp.y > b.max.y) continue;
                    Debug.Log($"SHOTBOT_HIT y={sy} {Trail(sr.transform)} order={sr.sortingOrder} z={sr.transform.position.z:F2} col={sr.color}");
                }
            }
        }

        static string Trail(Transform t)
        {
            string s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        // CaptureScreenshot is asynchronous (the frame has to finish first), so the
        // file is polled for rather than assumed — an early quit would otherwise
        // leave half the itinerary un-written.
        IEnumerator Shot(string name)
        {
            string path = System.IO.Path.Combine(_dir, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float waited = 0f;
            while (!File.Exists(path) && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            Debug.Log("SHOTBOT_SHOT " + path + (File.Exists(path) ? " ok" : " MISSING"));
        }
    }
}
