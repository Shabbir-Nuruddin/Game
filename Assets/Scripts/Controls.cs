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
            Jump  = (KeyCode)PlayerPrefs.GetInt("key_jump",  (int)KeyCode.Space);
            Dash  = (KeyCode)PlayerPrefs.GetInt("key_dash",  (int)KeyCode.K);
            Shoot = (KeyCode)PlayerPrefs.GetInt("key_shoot", (int)KeyCode.F);
            Fly   = (KeyCode)PlayerPrefs.GetInt("key_fly",   (int)KeyCode.LeftShift);
        }

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
