using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// A player-built map: a short left-to-right strip of cells the player fills in
    /// with floor, pits and traps, plus ONE room rule (the castle's own design
    /// language — every room tells a single lie).
    ///
    /// The whole map serialises to a short text CODE so it can go through the OS
    /// share sheet and be typed back in by a friend. Everything here is pure data +
    /// validation, deliberately free of UI and MonoBehaviours, so it can be audited
    /// headlessly: an un-beatable map must be impossible to publish, not merely
    /// discouraged.
    /// </summary>
    public class CustomMap
    {
        // ---- shape ----
        public const int Cells = 24;        // track length, in cells
        public const float CellW = 3.2f;    // world units per cell

        /// <summary>What occupies one cell.</summary>
        public enum Cell : byte
        {
            Floor = 0,      // plain solid ground
            Gap = 1,        // a pit — fall and die
            Fake = 2,       // looks solid, isn't (counts as a pit for reachability)
            Spike = 3,      // floor + static spike
            Saw = 4,        // floor + saw
            Late = 5,       // floor + late spike (fires as you commit)
            Faller = 6,     // floor + reactive ceiling drop
            Flame = 7,      // floor + flame jet
            Crush = 8,      // floor + crusher (must stay LOW)
            Dart = 9,       // floor + dart lane
        }
        public const int CellKinds = 10;

        /// <summary>The one lie this map tells. Mirrors the castle's RoomRule set.</summary>
        public enum Lie : byte { None = 0, Dark = 1, Reverse = 2, Press = 3, Gravity = 4 }
        public const int LieKinds = 5;

        public Cell[] cells = new Cell[Cells];
        public Lie lie = Lie.None;

        public CustomMap()
        {
            // A blank canvas is solid ground — the player digs pits and adds traps.
            for (int i = 0; i < Cells; i++) cells[i] = Cell.Floor;
        }

        /// <summary>Cells you can stand on. Fake floors deliberately do NOT count.</summary>
        public static bool IsSolid(Cell c) => c != Cell.Gap && c != Cell.Fake;
        public static bool IsHazard(Cell c) => c >= Cell.Spike;

        // ---- validation -------------------------------------------------------
        // The rule the player asked for: "you cannot make the room unbeatable."
        // These are the constraints that guarantee it.

        // Spawn and exit aprons must be clean so you never appear inside a trap or
        // have to make a blind jump into the goal.
        public const int SpawnClear = 2;
        public const int ExitClear = 2;
        // One cell (3.2u) is a comfortable jump; two in a row (6.4u) is beyond a
        // plain jump, and room rules suppress glide/double-jump — so two adjacent
        // non-solid cells is exactly the "unbeatable" case we must forbid.
        public const int MaxRunOfGaps = 1;
        // Enough real ground that a map is a level, not a wall of spikes.
        public const float MinSolidFraction = 0.45f;
        public const float MaxHazardFraction = 0.55f;

        /// <summary>
        /// Returns null when the map is publishable, otherwise a player-facing
        /// reason. Callers must refuse to share/play a map that fails.
        /// </summary>
        public string Validate()
        {
            if (cells == null || cells.Length != Cells) return "This map is corrupted.";

            for (int i = 0; i < SpawnClear; i++)
                if (!IsSolid(cells[i]) || IsHazard(cells[i]))
                    return "The first two cells must be clean ground - that's where the player lands.";

            for (int i = Cells - ExitClear; i < Cells; i++)
                if (!IsSolid(cells[i]) || IsHazard(cells[i]))
                    return "The last two cells must be clean ground - that's where the coffin sits.";

            int run = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (!IsSolid(cells[i]))
                {
                    run++;
                    if (run > MaxRunOfGaps)
                        return "Too many pits in a row - nobody can jump that. Leave ground between them.";
                }
                else run = 0;
            }

            int solid = 0, hazards = 0;
            foreach (var c in cells)
            {
                if (IsSolid(c)) solid++;
                if (IsHazard(c)) hazards++;
            }
            if (solid < Cells * MinSolidFraction)
                return "Not enough solid ground - this map is mostly pit.";
            if (hazards > Cells * MaxHazardFraction)
                return "Too many traps - even you couldn't finish this.";

            return null;   // publishable
        }

        public bool IsValid => Validate() == null;

        // ---- serialisation ----------------------------------------------------
        // Alphabet with no look-alikes (no I/O/0/1) so a code read off a phone
        // screen and typed into another phone doesn't fail on ambiguity.
        const string Abc = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";   // 32 symbols
        const char Prefix = 'M';

        /// <summary>
        /// Pack to a shareable code. Two cells fit in one symbol would need 100
        /// values (>32), so cells go one-per-symbol; the leading symbol carries the
        /// lie. Result is a 26-character code like "MD4A4A...".
        /// </summary>
        public string ToCode()
        {
            var sb = new StringBuilder(Cells + 2);
            sb.Append(Prefix);
            sb.Append(Abc[(int)lie % Abc.Length]);
            foreach (var c in cells) sb.Append(Abc[(int)c % Abc.Length]);
            return sb.ToString();
        }

        /// <summary>Parse a shared code. Returns null if it isn't a usable map.</summary>
        public static CustomMap FromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            code = code.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
            if (code.Length < Cells + 2 || code[0] != Prefix) return null;

            var m = new CustomMap();
            int lieIx = Abc.IndexOf(code[1]);
            if (lieIx < 0) return null;
            m.lie = (Lie)(lieIx % LieKinds);

            for (int i = 0; i < Cells; i++)
            {
                int v = Abc.IndexOf(code[i + 2]);
                if (v < 0) return null;
                m.cells[i] = (Cell)(v % CellKinds);
            }
            // A code that decodes but describes an impossible map is treated as
            // invalid rather than silently loaded — a friend must never receive a
            // map they cannot finish.
            return m.IsValid ? m : null;
        }

        // ---- building ---------------------------------------------------------

        /// <summary>
        /// Turn the map into a real playable Level using the SAME builder the
        /// hand-made floors use, so a custom map inherits every guarantee and
        /// visual treatment the castle already has (ceiling vault, stage camera,
        /// precision platforming).
        /// </summary>
        public Level ToLevel()
        {
            var b = new B();
            var rule = lie switch
            {
                Lie.Dark => RoomRule.Dark,
                Lie.Reverse => RoomRule.Reverse,
                Lie.Press => RoomRule.Press,
                _ => RoomRule.None,
            };
            // One chamber for the whole strip: the rule fires a third of the way in,
            // after the player has seen the room and committed to running it.
            b.Room(rule, 0.33f);

            float gravRuneX = 0f, ceilRuneX = 0f;
            bool gravity = lie == Lie.Gravity;

            for (int i = 0; i < Cells; i++)
            {
                var c = cells[i];
                if (c == Cell.Gap) { b.Gap(CellW); continue; }
                if (c == Cell.Fake) { b.FakeFloor(CellW); continue; }

                float cx = b.Plat(CellW);
                switch (c)
                {
                    case Cell.Spike: b.Spike(cx); break;
                    case Cell.Saw: b.Saw(cx); break;
                    case Cell.Late: b.LateSpike(cx); break;
                    case Cell.Faller: b.Faller(cx); break;
                    case Cell.Flame: b.FlameJet(cx); break;
                    case Cell.Crush: b.Crusher(cx); break;
                    case Cell.Dart: b.Dart(cx); break;
                }
                // Remember two clean spots for the gravity runes.
                if (gravity && i == SpawnClear) gravRuneX = cx;
                if (gravity && i == Cells - ExitClear - 1) ceilRuneX = cx;
            }

            if (gravity && gravRuneX != 0f)
            {
                // The room's auto-ceiling spans the whole strip, so flipping gravity
                // turns it into a walkable road over the top of every trap — a real
                // alternate route rather than a gimmick, and it cannot strand the
                // player because the return rune sits before the exit apron.
                b.GravRune(gravRuneX);
                b.CeilRune(ceilRuneX != 0f ? ceilRuneX : gravRuneX + CellW * 4f);
            }

            return b.Finish();
        }

        // ---- storage ----------------------------------------------------------
        const string SaveKey = "custom_map";
        const string FriendKey = "friend_map";

        public void Save() => SaveTo(SaveKey);
        public void SaveTo(string key) { PlayerPrefs.SetString(key, ToCode()); PlayerPrefs.Save(); }

        public static CustomMap Load() => FromCode(PlayerPrefs.GetString(SaveKey, "")) ?? new CustomMap();
        public static bool HasSaved => !string.IsNullOrEmpty(PlayerPrefs.GetString(SaveKey, ""));

        public static void SaveFriend(string code) { PlayerPrefs.SetString(FriendKey, code); PlayerPrefs.Save(); }
        public static CustomMap LoadFriend() => FromCode(PlayerPrefs.GetString(FriendKey, ""));

        // ---- best times (the competition) -------------------------------------
        // Times are keyed by the map CODE, so "my time on this exact map" is
        // comparable between two phones with no server involved.
        public static string TimeKey(string code) => "maptime_" + (code ?? "").GetHashCode().ToString("X");

        public static float BestTime(string code)
            => PlayerPrefs.GetFloat(TimeKey(code), 0f);

        /// <summary>Record a clear. Returns true when it beat the previous best.</summary>
        public static bool RecordTime(string code, float seconds)
        {
            float prev = BestTime(code);
            if (prev > 0f && seconds >= prev) return false;
            PlayerPrefs.SetFloat(TimeKey(code), seconds);
            PlayerPrefs.Save();
            return true;
        }

        public static string Fmt(float seconds)
            => seconds <= 0f ? "--" : seconds.ToString("0.00") + "s";

        /// <summary>Human-readable palette labels, indexed by Cell.</summary>
        public static readonly string[] CellNames =
        { "GROUND", "PIT", "FAKE", "SPIKE", "SAW", "LATE", "DROP", "FLAME", "CRUSH", "DART" };

        public static readonly string[] LieNames =
        { "NO LIE", "DARKNESS", "REVERSED", "CEILING", "GRAVITY" };
    }
}
