using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Rebindable action keys, persisted in PlayerPrefs. Movement (A/D + arrows) and
    /// the up-keys (W / ↑) stay fixed; the player can rebind the ABILITY keys —
    /// jump, dash, shoot (boss blaster) and bat-glide — from the Settings screen.
    /// PlayerController reads these every frame, so a rebind takes effect instantly.
    /// </summary>
    public static class Controls
    {
        public static KeyCode Jump  { get; private set; }
        public static KeyCode Dash  { get; private set; }
        public static KeyCode Shoot { get; private set; }
        public static KeyCode Fly   { get; private set; }

        static Controls() { Reload(); }

        public static void Reload()
        {
            Jump  = Sane((KeyCode)PlayerPrefs.GetInt("key_jump",  (int)KeyCode.Space),     KeyCode.Space);
            Dash  = Sane((KeyCode)PlayerPrefs.GetInt("key_dash",  (int)KeyCode.K),         KeyCode.K);
            Shoot = Sane((KeyCode)PlayerPrefs.GetInt("key_shoot", (int)KeyCode.F),         KeyCode.F);
            Fly   = Sane((KeyCode)PlayerPrefs.GetInt("key_fly",   (int)KeyCode.LeftShift), KeyCode.LeftShift);
        }

        // OS-reserved keys must never be bindings: the browser eats them (WebGL), and
        // a rebind capture that misfired on one left players with a "RightMeta" jump
        // key and prompts reading "RightMeta ↑". A stored bad key falls back to the
        // action's default.
        public static bool Bindable(KeyCode k) =>
            k != KeyCode.None && k != KeyCode.Escape && k != KeyCode.Menu &&
            !((int)k >= (int)KeyCode.RightMeta && (int)k <= (int)KeyCode.RightWindows); // Meta/Cmd/Win block (309–312)

        static KeyCode Sane(KeyCode stored, KeyCode def) => Bindable(stored) ? stored : def;

        public static KeyCode Get(string action) => action switch
        {
            "jump"  => Jump,
            "dash"  => Dash,
            "shoot" => Shoot,
            "fly"   => Fly,
            _       => KeyCode.None,
        };

        public static void Set(string action, KeyCode key)
        {
            if (!Bindable(key)) return;   // never persist an OS-reserved key
            switch (action)
            {
                case "jump":  Jump  = key; PlayerPrefs.SetInt("key_jump",  (int)key); break;
                case "dash":  Dash  = key; PlayerPrefs.SetInt("key_dash",  (int)key); break;
                case "shoot": Shoot = key; PlayerPrefs.SetInt("key_shoot", (int)key); break;
                case "fly":   Fly   = key; PlayerPrefs.SetInt("key_fly",   (int)key); break;
            }
            PlayerPrefs.Save();
        }

        // A short, friendly display name for a key (Space, LShift, ←, etc.).
        public static string Name(KeyCode k) => k switch
        {
            KeyCode.Space      => "Space",
            KeyCode.LeftShift  => "L-Shift",
            KeyCode.RightShift => "R-Shift",
            KeyCode.LeftControl  => "L-Ctrl",
            KeyCode.RightControl => "R-Ctrl",
            KeyCode.LeftAlt    => "L-Alt",
            KeyCode.RightAlt   => "R-Alt",
            KeyCode.UpArrow    => "Up",
            KeyCode.DownArrow  => "Down",
            KeyCode.LeftArrow  => "Left",
            KeyCode.RightArrow => "Right",
            KeyCode.Return     => "Enter",
            _                  => k.ToString(),
        };
    }
}
