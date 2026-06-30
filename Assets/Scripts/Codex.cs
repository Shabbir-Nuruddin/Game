using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// The "Vampire's Bestiary" — a persistent trap codex. The first time a trap
    /// KILLS you, its entry unlocks (PlayerPrefs flag per trap type). The book is a
    /// completion goal AND a teaching tool: each revealed entry names the trap and
    /// tells you how to read/beat it. Locked entries show as silhouettes.
    /// </summary>
    public static class Codex
    {
        // Traps that are wins/checkpoints/not really "deaths" are excluded from the book.
        public static readonly TrapType[] Entries =
        {
            TrapType.SpikeStatic, TrapType.LateSpike, TrapType.GrowSpike, TrapType.ArrowRain,
            TrapType.FakeFloor,   TrapType.Crusher,   TrapType.Faller,    TrapType.Chandelier,
            TrapType.Dart,        TrapType.Saw,       TrapType.Pendulum,  TrapType.FlameJet,
            TrapType.HolyWater,   TrapType.BatSwoop,  TrapType.Spring,    TrapType.Reverse,
            TrapType.WarpBack,    TrapType.FakeExit,  TrapType.Surprise,
        };

        static string Key(TrapType t) => "codex_" + (int)t;

        public static bool IsKnown(TrapType t) => PlayerPrefs.GetInt(Key(t), 0) == 1;

        /// <summary>Fired the first time a trap is revealed, so the HUD can toast it.</summary>
        public static System.Action<TrapType> OnUnlocked;

        /// <summary>Reveal a trap's entry. Returns true the FIRST time (for a toast).</summary>
        public static bool Unlock(TrapType t)
        {
            if (System.Array.IndexOf(Entries, t) < 0) return false;   // not a codex trap
            if (IsKnown(t)) return false;
            PlayerPrefs.SetInt(Key(t), 1); PlayerPrefs.Save();
            Analytics.Track("codex_unlock", new System.Collections.Generic.Dictionary<string, object>
            { { "trap", t.ToString() } });
            OnUnlocked?.Invoke(t);
            return true;
        }

        public static int KnownCount()
        {
            int n = 0;
            foreach (var t in Entries) if (IsKnown(t)) n++;
            return n;
        }
        public static int Total => Entries.Length;

        public static string Title(TrapType t) => t switch
        {
            TrapType.SpikeStatic => "Iron Spike",
            TrapType.LateSpike   => "Ambush Spike",
            TrapType.GrowSpike   => "Breathing Spike",
            TrapType.ArrowRain   => "Spike Rain",
            TrapType.FakeFloor   => "Treacher-Floor",
            TrapType.Crusher     => "Bait Crusher",
            TrapType.Faller      => "Thwomp Stone",
            TrapType.Chandelier  => "The Chandelier",
            TrapType.Dart        => "Stake Launcher",
            TrapType.Saw         => "Whirling Blade",
            TrapType.Pendulum    => "Pendulum Blade",
            TrapType.FlameJet    => "Flame Jet",
            TrapType.HolyWater   => "Holy Water",
            TrapType.BatSwoop    => "Swooping Bat",
            TrapType.Spring      => "Cursed Spring",
            TrapType.Reverse     => "Hex of Confusion",
            TrapType.WarpBack    => "Banishment Rune",
            TrapType.FakeExit    => "False Coffin",
            TrapType.Surprise    => "Sunbeam Trap",
            _ => t.ToString(),
        };

        public static string Lore(TrapType t) => t switch
        {
            TrapType.SpikeStatic => "Always visible, always lethal. Just jump it — the game is being honest for once.",
            TrapType.LateSpike   => "Looks like clear ground, then erupts the instant you land. Keep moving; never trust a perfect platform.",
            TrapType.GrowSpike   => "Grows lethal and shrinks safe on a loop. It's red when it can kill — cross while it's dim and low.",
            TrapType.ArrowRain   => "Spikes drop from the ceiling on a timer. Watch the rhythm, then sprint through the gap.",
            TrapType.FakeFloor   => "The most inviting platform collapses a beat after you stand on it. It shudders first — that's your warning.",
            TrapType.Crusher     => "Reach for the high bait and a block slams down. The reward is the trap; stay low.",
            TrapType.Faller      => "A heavy stone hangs above and drops when you're under it. It shakes before it falls.",
            TrapType.Chandelier  => "A wide gothic Thwomp. Same idea as the stone, but it covers far more ground — don't linger beneath.",
            TrapType.Dart        => "A stake fires across the lane the moment you arrive. Arrive low or already jumping.",
            TrapType.Saw         => "A blade that slides back and forth. Read its track and slip past at the far end of its swing.",
            TrapType.Pendulum    => "A blade on a chain sweeping the lane. Time your run to the bottom of its arc.",
            TrapType.FlameJet    => "A floor jet that erupts on a loop. It flickers a warning before it fires — cross while it's down.",
            TrapType.HolyWater   => "A puddle that turns lethal on a pulse. It brightens before it burns — cross while it's dim.",
            TrapType.BatSwoop    => "A bat hovers, flares red, then dives at where you stand. The flare is your dodge cue — move on it.",
            TrapType.Spring      => "Launches you skyward — often into something worse. Know what's above before you bounce.",
            TrapType.Reverse     => "Hexes your controls backwards for a few seconds. Don't panic; just invert your instincts.",
            TrapType.WarpBack    => "Yanks you all the way back to the start. Pure spite. The safe-looking shortcut is the bait.",
            TrapType.FakeExit    => "The brightest, most obvious door is death. The real way out is never the showy one.",
            TrapType.Surprise    => "An invisible sunbeam on safe-looking ground. Cruel, but it always sits where you'd relax.",
            _ => "A trap of the castle.",
        };

        // Which sprite best represents this trap in the book (falls back gracefully).
        public static string Art(TrapType t) => t switch
        {
            TrapType.SpikeStatic or TrapType.LateSpike or TrapType.GrowSpike or TrapType.ArrowRain => "spike",
            TrapType.Saw => "saw",
            TrapType.Faller or TrapType.Crusher => "rockhead",
            TrapType.Chandelier => "chandelier",
            TrapType.Pendulum => "pendulum",
            TrapType.BatSwoop => "bat_fly",
            TrapType.Spring => "trampoline",
            TrapType.FakeExit => "door",
            TrapType.Dart => "bolt",
            _ => "",
        };
    }
}
