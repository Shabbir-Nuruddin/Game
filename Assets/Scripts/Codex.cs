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
            // The room itself — the chapter where the castle stops putting things
            // in your way and starts moving the way itself.
            TrapType.TiltFloor,   TrapType.SlideFloor, TrapType.DropFloor, TrapType.RiseFloor,
            TrapType.SlamWall,    TrapType.CeilingVolley, TrapType.ShyExit,
        };

        static string Key(TrapType t) => "codex_" + (int)t;

        // PREVIEW MODE: when on, every page reads as "known" so you can flip through
        // the whole book without earning it — WITHOUT touching real progress. Turning
        // it back off instantly restores the true locked/unlocked state (the locked
        // silhouette look), so it's a safe look-around toggle, not a cheat that sticks.
        // DEFAULTS OFF FOR RELEASE. It was defaulted ON as a review convenience so
        // the whole book could be flipped through while the art was being made —
        // but that shipped a brand-new player a Bestiary reading 19/19 on first
        // launch, which throws away the discovery loop the book exists for (and
        // made the store screenshot advertise a completed collection). The toggle
        // is still on the Bestiary screen, so looking around costs one tap.
        public static bool PreviewAll
        {
            get => PlayerPrefs.GetInt("codex_preview_all", 0) == 1;
            set { PlayerPrefs.SetInt("codex_preview_all", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The TRUE unlock state (ignores preview) — used by gameplay/progression.</summary>
        public static bool RealKnown(TrapType t) => PlayerPrefs.GetInt(Key(t), 0) == 1;

        /// <summary>What the book DISPLAYS: really earned, or shown because preview is on.</summary>
        public static bool IsKnown(TrapType t) => RealKnown(t) || PreviewAll;

        /// <summary>Fired the first time a trap is revealed, so the HUD can toast it.</summary>
        public static System.Action<TrapType> OnUnlocked;

        /// <summary>Reveal a trap's entry. Returns true the FIRST time (for a toast).</summary>
        public static bool Unlock(TrapType t)
        {
            if (System.Array.IndexOf(Entries, t) < 0) return false;   // not a codex trap
            if (RealKnown(t)) return false;   // real progress only — preview never blocks a genuine unlock
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
            TrapType.TiltFloor     => "The Tipping Slab",
            TrapType.SlideFloor    => "The Floor That Leaves",
            TrapType.DropFloor     => "The Long Way Down",
            TrapType.RiseFloor     => "The Rising Press",
            TrapType.SlamWall      => "The Wall That Arrives",
            TrapType.CeilingVolley => "The Ceiling's Teeth",
            TrapType.ShyExit       => "The Shy Coffin",
            _ => t.ToString(),
        };

        public static string Lore(TrapType t) => t switch
        {
            TrapType.SpikeStatic => "Always visible, always lethal. Just jump it — the game is being honest for once.",
            TrapType.LateSpike   => "A hidden sensor makes it erupt just ahead of you. Watch the ground: stop or jump as soon as the blade appears.",
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
            TrapType.TiltFloor     => "Ordinary stone until your weight is on it, then it hinges and pours you off. It leans before it tips — that lean is the whole warning.",
            TrapType.SlideFloor    => "The floor slides out from under you and leaves a hole. It's still there, just not where you are. Keep running; don't jump.",
            TrapType.DropFloor     => "The slab falls, and takes you with it. It won't kill you — but it decides where you land, and something down there will.",
            TrapType.RiseFloor     => "The ground lifts you toward the ceiling and keeps going. The Crusher taught you to stay low. This kills you for it.",
            TrapType.SlamWall      => "A wall drives in from off-lane as you pass. It grinds first. It always leaves a gap — you just have to be in it.",
            TrapType.CeilingVolley => "Teeth drop out of the ceiling in sequence, aimed where you are. Outrun the wave or let it finish — never stand still under it.",
            TrapType.ShyExit       => "The coffin backs away when you reach for it. Twice. The third time it gives up, so keep walking.",
            _ => "A trap of the castle.",
        };

        // Which sprite represents this trap. Every entry now has its OWN illustration,
        // cut out of the Vampire's Bestiary artwork into Resources/art/traps — the same
        // picture the level draws, so a page you've read is a thing you can recognise
        // mid-run. Several traps used to share a stand-in sprite (the pendulum and saw
        // both showed the blade, the chandelier showed a torch); that's over.
        public static string Art(TrapType t) => "traps/trap_" + ArtKey(t);

        /// <summary>The trap's short art key, shared by the codex page and the level.</summary>
        public static string ArtKey(TrapType t) => t switch
        {
            TrapType.SpikeStatic => "spike",
            TrapType.LateSpike   => "latespike",
            TrapType.GrowSpike   => "growspike",
            TrapType.ArrowRain   => "arrowrain",
            TrapType.FakeFloor   => "fakefloor",
            TrapType.Crusher     => "crusher",
            TrapType.Faller      => "faller",
            TrapType.Chandelier  => "chandelier",
            TrapType.Dart        => "dart",
            TrapType.Saw         => "saw",
            TrapType.Pendulum    => "pendulum",
            TrapType.FlameJet    => "flamejet",
            TrapType.HolyWater   => "holywater",
            TrapType.BatSwoop    => "bat",
            TrapType.Spring      => "spring",
            TrapType.Reverse     => "reverse",
            TrapType.WarpBack    => "warpback",
            TrapType.FakeExit    => "fakeexit",
            TrapType.Surprise    => "surprise",
            // The room-itself traps have no illustration of their own yet, so each
            // borrows the closest existing plate: a floor for the floors, the
            // crusher for anything that presses, the spike rain for the ceiling.
            TrapType.TiltFloor     => "fakefloor",
            TrapType.SlideFloor    => "fakefloor",
            TrapType.DropFloor     => "fakefloor",
            TrapType.RiseFloor     => "crusher",
            TrapType.SlamWall      => "crusher",
            TrapType.CeilingVolley => "arrowrain",
            TrapType.ShyExit       => "fakeexit",
            _ => "spike",                                      // last-ditch: never blank
        };
    }
}
