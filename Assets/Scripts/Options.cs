using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// THE FEEL — the four comfort switches the settings redesign adds, in one place so
    /// the screen that shows them and the code that obeys them can never drift apart.
    ///
    /// Every one of these is wired to something real. A settings screen full of switches
    /// that do nothing is worse than a short settings screen, so if a switch is here it
    /// changes the game: shake is read by the camera, spatter by the death sequence,
    /// haptics by the rumble on death, reduced motion by every idle animation in the
    /// menus. All local, in PlayerPrefs, on by default except reduced motion.
    /// </summary>
    public static class Options
    {
        static bool Get(string key, bool def) => PlayerPrefs.GetInt(key, def ? 1 : 0) == 1;
        static void Set(string key, bool v) { PlayerPrefs.SetInt(key, v ? 1 : 0); PlayerPrefs.Save(); }

        /// <summary>Camera shake on hits, deaths and boss beats.</summary>
        public static bool Shake { get => Get("opt_shake", true); set => Set("opt_shake", value); }

        /// <summary>The gore burst and the wall splash when you die.</summary>
        public static bool Spatter { get => Get("opt_gore", true); set => Set("opt_gore", value); }

        /// <summary>The phone rumble on death (mobile only; ignored elsewhere).</summary>
        public static bool Haptics { get => Get("opt_haptics", true); set => Set("opt_haptics", value); }

        /// <summary>
        /// Stills the decorative motion — drifting bats, the pulsing title, the map's
        /// beating floor. Gameplay motion is untouched: this is a comfort setting, not
        /// an easy mode.
        /// </summary>
        public static bool ReducedMotion { get => Get("opt_reduced_motion", false); set => Set("opt_reduced_motion", value); }

        /// <summary>Joystick sits where the thumb lands (true) or always bottom-left.</summary>
        public static bool FloatingStick { get => Get("opt_stick_float", true); set => Set("opt_stick_float", value); }

        /// <summary>Mirror the touch controls for a left-handed grip.</summary>
        public static bool LeftHanded { get => Get("opt_lefty", false); set => Set("opt_lefty", value); }
    }
}
