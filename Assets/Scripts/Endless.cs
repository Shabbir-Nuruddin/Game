using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// ENDLESS NIGHT's floor factory.
    ///
    /// The old Endless built every floor the same way: one flat corridor, a pit
    /// every few units, a random hazard on each platform. Floor 3 and floor 30
    /// were the same room with different furniture, which is exactly the
    /// "levels feel samey" verdict the Castle fixed by giving each floor ONE
    /// lie it tells you. This does the same thing for the infinite half of the
    /// game, procedurally.
    ///
    /// A floor is a SHAPE, not a difficulty number. Sixteen shapes exist — the
    /// blackout, the spectral bridges, the descending ceiling, the cursed hands,
    /// the hall that repeats, the chase, the coffin roulette, the lullaby, the
    /// inversion, the open sky, the chasm, the biting doors, the warrens, the
    /// blades, the furnace, the liars — and every one owns a different verb.
    /// Shapes are dealt from a SHUFFLED DECK: you see all sixteen before any of
    /// them comes back, and the deck reshuffles each pass, so the descent never
    /// settles into a pattern and no two runs are dealt the same order.
    ///
    /// Everything else is rolled per floor on top of that: how many chambers,
    /// how long each one is, which four or five hazards make up this floor's
    /// vocabulary, where the honest breather room sits, and (deep down) which
    /// rule from a completely different shape gets smuggled into one chamber.
    /// The same shape twice, twenty floors apart, is not the same floor.
    ///
    /// Every tenth floor is a boss instead — a landmark in a mode that
    /// otherwise has no landmarks, and the reason "floor 30" means something.
    ///
    /// Determinism matters: a floor is rebuilt from scratch on every death, so
    /// Build(floor, seed) must return the identical level every time. Nothing
    /// here keeps state between calls; everything derives from (floor, seed).
    /// </summary>
    public static class EndlessFloors
    {
        // A boss every tenth floor, cycling the four of them. Endless has
        // checkpoints and infinite retries, so a wall here ends a RUN, not a
        // save file — which is what gives an endless score chase any stakes.
        public const int BossEvery = 10;

        delegate Level Shape(Depth d);

        /// <summary>
        /// One entry per floor shape. `minFloor` keeps the deeper, stranger
        /// shapes (gravity, chasms, the repeating hall) out of the mouth of the
        /// descent — analytics put 101 deaths on the OLD Endless floor 1, and
        /// the fix is that the first floors are shapes you can read at a glance.
        /// `guest` is whether this shape's geometry can survive a borrowed rule
        /// from another shape (a mover-ride chamber with a descending ceiling
        /// would be a coin flip, so the chasm says no).
        /// </summary>
        static readonly (string name, string tag, int minFloor, bool guest, Shape make)[] Deck =
        {
            ("THE BLACKOUT",        "the candles in here don't stay lit",     1, true,  Blackout),
            ("THE LIARS",           "nothing you stand on is load-bearing",   1, true,  Liars),
            ("THE BLADE CHOIR",     "everything here swings on a count",      1, true,  BladeChoir),
            ("THE LONG FALL",       "open sky — and things hanging in it",    1, false, LongFall),
            ("COFFIN ROULETTE",     "only one of them is a way out",          3, true,  Roulette),
            ("THE FURNACE",         "fire below, and worse standing still",   3, true,  Furnace),
            ("THE PRESS",           "the ceiling in here doesn't stay put",   4, true,  Press),
            ("WRONG HANDS",         "this room won't let you keep your own",  4, true,  WrongHands),
            ("THE LULLABY",         "the castle would rather you slept",      4, true,  Lullaby),
            ("THE CHASE",           "the way out runs from you",              5, false, Chase),
            ("IRON TEETH",          "every door in here bites",               5, true,  IronTeeth),
            ("THE CHASM",           "the floor moves, or there isn't one",    6, false, Chasm),
            ("THE HALL THAT REPEATS","leaving is how you stay",               7, true,  LoopHall),
            ("THE WARRENS",         "the doors don't lead where they point",  7, false, Warrens),
            ("FAITH",               "the bridge is only there in the dark",   8, true,  Faith),
            ("THE INVERSION",       "down is a decision here",               10, false, Inversion),
        };

        // ================= public API =================

        /// <summary>Floor `floorNumber` (1-based) of the run seeded `runSeed`.</summary>
        public static Level Build(int floorNumber, int runSeed)
        {
            if (IsBossFloor(floorNumber)) return Levels.BossRoom(BossTier(floorNumber));
            var d = new Depth(floorNumber, runSeed);
            var entry = Deck[PickIndex(floorNumber, runSeed)];
            d.allowGuest = entry.guest;
            d.Roll();
            return entry.make(d);
        }

        /// <summary>The floor's name, for the card between floors.</summary>
        public static string NameFor(int floorNumber, int runSeed) =>
            IsBossFloor(floorNumber) ? "SOMETHING IS WAITING"
                                     : Deck[PickIndex(floorNumber, runSeed)].name;

        /// <summary>The one-line promise under the name.</summary>
        public static string TagFor(int floorNumber, int runSeed) =>
            IsBossFloor(floorNumber) ? "the floor is a room, and the room is occupied"
                                     : Deck[PickIndex(floorNumber, runSeed)].tag;

        public static bool IsBossFloor(int floorNumber) =>
            floorNumber > 0 && floorNumber % BossEvery == 0;

        static int BossTier(int floorNumber) =>
            ((floorNumber / BossEvery - 1) % 4) + 1;   // 10→1, 20→2, 30→3, 40→4, 50→1 …

        // ================= the deck =================

        // Boss floors don't consume a card, so the deck keeps dealing cleanly
        // across them (floor 11 gets the card floor 10 would have had).
        static int Slot(int floorNumber) => (floorNumber - 1) - (floorNumber - 1) / BossEvery;

        /// <summary>
        /// Which shape floor `floorNumber` gets. Walked forward from the
        /// surface, because the one rule that matters most — you never get the
        /// same shape you just played — can only be enforced by knowing what
        /// the last two floors actually were. A few hundred integer steps once
        /// per floor build, next to nothing beside laying the geometry.
        /// </summary>
        static int PickIndex(int floorNumber, int runSeed)
        {
            int prev = -1, prev2 = -1, ix = 0;
            for (int f = 1; f <= floorNumber; f++)
            {
                if (IsBossFloor(f)) continue;      // a boss doesn't spend a card
                ix = Deal(f, runSeed, prev, prev2);
                prev2 = prev; prev = ix;
            }
            return ix;
        }

        static int Deal(int floorNumber, int runSeed, int prev, int prev2)
        {
            int n = Deck.Length;
            int slot = Slot(floorNumber);
            var order = Order(runSeed, slot / n, n);
            int pos = slot % n;
            // Walk forward from this pass's card to the first shape that is
            // both unlocked at this depth and not one of the last two floors.
            // Near the surface only a handful are unlocked, so this walk is
            // what stops the first floors from stalling on one shape.
            for (int k = 0; k < n; k++)
            {
                int ix = order[(pos + k) % n];
                if (floorNumber < Deck[ix].minFloor) continue;
                if (ix == prev || ix == prev2) continue;
                return ix;
            }
            // Only reachable in the first few floors, where barely anything is
            // unlocked. Drop the "one apart" half of the rule, never the
            // "twice in a row" half.
            for (int k = 0; k < n; k++)
            {
                int ix = order[(pos + k) % n];
                if (floorNumber >= Deck[ix].minFloor && ix != prev) return ix;
            }
            return order[pos];
        }

        // How many floors must pass before a shape may come back. A shuffled
        // deck only promises "all sixteen before any repeat" WITHIN a pass —
        // straddle the reshuffle and the blackout you just did can be dealt
        // again two floors later, which is exactly the "wait, this again?" the
        // whole system exists to prevent.
        const int Spacing = 4;

        // One shuffled pass of the deck, repaired so nothing that closed the
        // last pass opens this one. Deterministic from (seed, pass).
        static int[] Order(int runSeed, int pass, int n)
        {
            var order = Raw(runSeed, pass, n);
            if (pass <= 0 || n < Spacing * 3) return order;

            var prev = Raw(runSeed, pass - 1, n);
            var closing = new HashSet<int>();
            for (int i = n - Spacing; i < n; i++) closing.Add(prev[i]);

            // Push each offender into the MIDDLE of the pass — never the tail,
            // so this pass's own closing cards stay exactly what the raw
            // shuffle made them and the next pass can be repaired against them
            // without having to re-derive every pass before it.
            for (int i = 0; i < Spacing; i++)
            {
                if (!closing.Contains(order[i])) continue;
                for (int j = Spacing; j < n - Spacing; j++)
                {
                    if (closing.Contains(order[j])) continue;
                    int t = order[i]; order[i] = order[j]; order[j] = t;
                    break;
                }
            }
            return order;
        }

        static int[] Raw(int runSeed, int pass, int n)
        {
            var rng = new System.Random(runSeed * 31 + pass * 7919 + 13);
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
            return order;
        }

        // ================= this floor's dice =================

        /// <summary>
        /// Everything a shape needs to know about how deep it is being built.
        /// Rolled once per floor from (floor, seed) so a rebuild after death is
        /// byte-identical to the floor that just killed you.
        /// </summary>
        class Depth
        {
            public readonly System.Random rng;
            public readonly int floor;      // 1-based
            public readonly int tier;       // 0-4: how much of the vocabulary is unlocked
            public int rooms;               // chambers on this floor
            public int bays;                // platforms per chamber
            public int haz;                 // hazards per platform
            public bool allowGuest;

            public List<TrapType> palette = new();
            public int honestRoom = -1;     // the breather that makes the next lie land
            public int guestRoom = -1;      // one chamber that borrows another shape's rule
            public RoomRule guest = RoomRule.None;
            public int gates;               // biting doorways left to hand out
            public RoomRule current;        // the rule of the chamber being laid right now

            // Rage traps are the mode's identity and also its quit button, so
            // they're rationed per floor rather than left to the dice.
            int _warps, _blind, _blindLeft;

            // Everything that kills you with no tell the first time. These ARE
            // the game — but they're also the entire death count, so a floor
            // gets a fixed allowance of them and spends visible hazards after
            // that. Without this, deep floors drifted to a dozen unavoidable
            // deaths apiece, which reads as noise rather than difficulty.
            static readonly HashSet<TrapType> Blinding = new()
            {
                TrapType.FakeFloor, TrapType.LateSpike, TrapType.Surprise, TrapType.FakeExit,
                TrapType.Faller, TrapType.Chandelier, TrapType.Crusher, TrapType.Dart,
                TrapType.WarpBack, TrapType.Reverse,
            };

            public Depth(int floorNumber, int runSeed)
            {
                floor = floorNumber;
                rng = new System.Random(runSeed * 7919 + floorNumber * 104729 + 7);
                tier = floor <= 3 ? 0 : floor <= 7 ? 1 : floor <= 12 ? 2 : floor <= 19 ? 3 : 4;
            }

            /// <summary>Roll the shape-independent dice for this floor.</summary>
            public void Roll()
            {
                // Chambers grow with depth but stop at five: past that a floor
                // stops being a floor and starts being a shift.
                rooms = floor <= 6 ? 3 : floor <= 13 ? 4 : 5;
                bays = tier >= 3 ? 3 : 2;
                haz = tier >= 3 && Chance(30) ? 2 : 1;
                gates = tier >= 2 && Chance(50) ? 1 : 0;
                // 3 blind deaths near the surface, 11 at the bottom.
                _blindLeft = 3 + tier * 2;

                BuildPalette();

                // Never the first chamber: the honest room is a rest, and you
                // can't rest before you've been threatened.
                honestRoom = rooms >= 3 ? 1 + Next(rooms - 1) : -1;

                if (allowGuest && tier >= 2 && rooms >= 4)
                {
                    for (int tries = 0; tries < 4; tries++)
                    {
                        int ix = 1 + Next(rooms - 2);          // never first, never last
                        if (ix == honestRoom) continue;
                        guestRoom = ix;
                        // Press is deliberately NOT loanable. The slab spans the
                        // whole chamber and reaches the floor about four seconds
                        // after it fires, so a press room has to be BUILT to that
                        // budget — dropping one into a chamber laid out for
                        // something else makes a room that can't be crossed.
                        var pool = new[] { RoomRule.Dark, RoomRule.Reverse, RoomRule.Loop };
                        guest = pool[Next(pool.Length)];
                        break;
                    }
                }
            }

            // This floor's whole hazard vocabulary — four or five types drawn
            // from what the depth has unlocked. Two floors of the same SHAPE
            // built from different palettes don't play alike, and a small
            // palette per floor is what makes each one feel authored.
            void BuildPalette()
            {
                var allowed = new List<TrapType>
                { TrapType.SpikeStatic, TrapType.GrowSpike, TrapType.Saw };
                if (tier >= 1) { allowed.Add(TrapType.LateSpike); allowed.Add(TrapType.Pendulum); allowed.Add(TrapType.BatSwoop); }
                if (tier >= 2) { allowed.Add(TrapType.Dart); allowed.Add(TrapType.FlameJet); allowed.Add(TrapType.HolyWater); allowed.Add(TrapType.Faller); }
                if (tier >= 3) { allowed.Add(TrapType.Chandelier); allowed.Add(TrapType.Crusher); allowed.Add(TrapType.Surprise); allowed.Add(TrapType.ArrowRain); }
                if (tier >= 4) { allowed.Add(TrapType.WarpBack); }

                int take = Mathf.Min(allowed.Count, 4 + tier / 2);
                for (int i = allowed.Count - 1; i > 0; i--)
                {
                    int j = Next(i + 1);
                    var t = allowed[i]; allowed[i] = allowed[j]; allowed[j] = t;
                }
                // A spike is always in the mix. It's the game's full stop — the
                // thing every other hazard is punctuation around.
                palette.Clear();
                palette.Add(TrapType.SpikeStatic);
                for (int i = 0; i < allowed.Count && palette.Count < take; i++)
                    if (allowed[i] != TrapType.SpikeStatic) palette.Add(allowed[i]);
            }

            public int Next(int n) => rng.Next(Mathf.Max(1, n));
            public bool Chance(int pct) => rng.Next(100) < pct;
            public float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            /// <summary>
            /// Spend one of this floor's unavoidable deaths. False once the
            /// allowance is gone — the caller then places something the player
            /// can actually see coming.
            /// </summary>
            public bool TakeBlind()
            {
                if (_blindLeft <= 0) return false;
                _blindLeft--;
                return true;
            }

            /// <summary>A hazard from this floor's vocabulary, honouring the rations.</summary>
            public TrapType Pick()
            {
                var t = palette[Next(palette.Count)];
                if (t == TrapType.WarpBack)
                {
                    // One rage-teleport a floor. Two is where people close the tab.
                    if (_warps >= 1) t = TrapType.SpikeStatic;
                    else _warps++;
                }
                if (t == TrapType.Surprise)
                {
                    // The invisible kill zone is the least fair thing in the game.
                    // Two per floor is a running joke; five is a bug report.
                    if (_blind >= 2) t = TrapType.GrowSpike;
                    else _blind++;
                }
                if (Blinding.Contains(t) && !TakeBlind()) return Visible();
                return t;
            }

            /// <summary>Something on this floor's palette that you can SEE.</summary>
            public TrapType Visible()
            {
                for (int i = 0; i < palette.Count; i++)
                {
                    var t = palette[(Next(palette.Count) + i) % palette.Count];
                    if (!Blinding.Contains(t)) return t;
                }
                return TrapType.SpikeStatic;
            }

            /// <summary>The rule chamber `i` actually runs.</summary>
            public RoomRule RuleFor(int i, RoomRule signature)
            {
                if (i == honestRoom) return RoomRule.None;
                if (i == guestRoom && guest != RoomRule.None && guest != signature) return guest;
                return signature;
            }

            /// <summary>Should this chamber's doorway have a portcullis in it?</summary>
            public bool Gate(int i)
            {
                if (i == 0 || gates <= 0) return false;   // never the entry room
                if (!Chance(35)) return false;
                gates--;
                return true;
            }

            /// <summary>How far into a chamber its rule fires. Never at the door.</summary>
            public float Trigger() => Range(0.14f, 0.32f);
        }

        // ================= shared geometry =================

        /// <summary>
        /// Open a chamber running `rule`. Everything goes through here so the
        /// dice know which rule is live while the chamber is being laid.
        /// </summary>
        static void Enter(B b, Depth d, RoomRule rule, bool gated)
        {
            d.current = rule;
            b.Room(rule, d.Trigger(), gated);
        }

        /// <summary>
        /// Open chamber `i`. Every chamber MUST start with plain ground: a death
        /// respawns you at the chamber's left edge + 1.3, so the first thing
        /// past a doorway is always something to stand on.
        /// </summary>
        static void Open(B b, Depth d, int i, RoomRule signature)
        {
            Enter(b, d, d.RuleFor(i, signature), d.Gate(i));
            b.Plat(d.Range(3.4f, 4.4f));
        }

        /// <summary>
        /// A chamber under the descending vault, built to the only budget that
        /// matters: the slab spans the whole room and touches head height about
        /// four seconds after the rule fires. At 7.5 units a second, with jumps
        /// and one hazard to read, ~19 units is a hard run. Longer than that
        /// isn't difficulty, it's a room that cannot be crossed — so press
        /// chambers are sized here and nowhere else.
        /// </summary>
        static void PressRoom(B b, Depth d, bool gated, bool last)
        {
            Enter(b, d, RoomRule.Press, gated);
            b.Plat(3.6f);                                // the look, and the respawn ground
            Bay(b, d, d.Range(8f, 9f), d.haz);           // one long run — just don't stop
            // Finish() bolts a gap and the coffin platform onto whichever
            // chamber is last, which is another ~6.5 units of room the vault
            // also spans. On the last chamber that tail IS the final run.
            if (last) return;
            b.Gap(d.Range(2.2f, 2.5f));
            Bay(b, d, d.Range(4f, 5f), 1);
        }

        /// <summary>
        /// A platform with hazards on it. Hazards keep 1.3 clear of both edges
        /// (that's the take-off and the landing) and 2.4 from each other, and
        /// the stay-low hazards — crusher, thwomp, chandelier, warp rune — are
        /// never given a neighbour, because "jump this" and "don't jump" on one
        /// platform is not a hard platform, it's an impossible one.
        /// </summary>
        static float Bay(B b, Depth d, float w, int count)
        {
            float c = b.Plat(w);
            Arm(b, d, c, w, count);
            return c;
        }

        static void Arm(B b, Depth d, float c, float w, int count)
        {
            float span = w - 2.6f;
            if (count <= 0 || span < 0.4f) return;

            var first = d.Pick();
            if (Levels.Soloist(first) || count == 1 || span < 2.4f)
            {
                Levels.PlaceHazard(b, first, c + (span > 1.2f ? d.Range(-0.4f, 0.6f) : 0f));
                return;
            }
            int n = Mathf.Min(count, 1 + Mathf.FloorToInt(span / 2.4f));
            float step = span / n;
            for (int i = 0; i < n; i++)
            {
                var t = i == 0 ? first : d.Pick();
                if (Levels.Soloist(t)) t = TrapType.SpikeStatic;   // it doesn't get to share
                Levels.PlaceHazard(b, t, c - span / 2f + step * (i + 0.5f));
            }
        }

        /// <summary>
        /// The pit between two platforms. Sometimes it's a jump, sometimes it's
        /// a floor that lies, sometimes it's a slab you have to ride. Roomed
        /// floors suppress the bat glide, so a plain jump (~5.5u) is the ceiling
        /// on anything you're expected to clear on your own.
        /// </summary>
        static void Cross(B b, Depth d)
        {
            int roll = d.Next(100);
            if (d.tier >= 1 && roll < 16 && d.TakeBlind()) { b.FakeFloor(d.Range(2.0f, 2.3f)); return; }
            // Never a slab ride under a descending ceiling: waiting for the slab
            // and outrunning the vault are opposite instructions, and a room
            // that gives both at once is a coin flip, not a level.
            if (d.tier >= 2 && roll < 24 && d.current != RoomRule.Press)
            { b.MoverGap(d.Range(6.8f, 7.2f), d.Range(0.9f, 1.4f)); return; }
            b.Gap(d.Range(2.2f, 2.7f));
        }

        /// <summary>`bays` platforms with a pit between each — a chamber's body.</summary>
        static void Hall(B b, Depth d, int bays, int haz)
        {
            for (int i = 0; i < bays; i++)
            {
                Cross(b, d);
                Bay(b, d, d.Range(4.4f, 6.4f), haz);
            }
        }

        /// <summary>The ordinary body of a chamber at this floor's dice.</summary>
        static void Body(B b, Depth d) => Hall(b, d, d.bays, d.haz);

        // ================= the sixteen shapes =================

        // THE BLACKOUT — the candles die mid-run. Floor you watched stops
        // existing; the spike you memorised is somewhere else when the light
        // comes back. Your memory of the room is the trap.
        static Level Blackout(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Open(b, d, i, RoomRule.Dark);
                if (d.Chance(70)) b.NightFloor(d.Range(1.8f, 2.3f));
                float w = d.Range(5.2f, 6.4f);
                float c = Bay(b, d, w, 1);
                // Both spots have to be on this slab, or the spike relocates
                // into thin air and the lie stops being a lie.
                if (d.Chance(65)) b.ShiftSpike(c + w * 0.26f, c - w * 0.26f);
                Body(b, d);
            }
            return b.Finish();
        }

        // FAITH — spectral bridges that only turn solid once the lights are
        // out, over spans no jump can cross. The whole floor asks one question:
        // will you walk into the dark on the promise of a shimmer?
        static Level Faith(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Open(b, d, i, RoomRule.Dark);
                if (i == d.honestRoom) { Body(b, d); continue; }
                b.GhostFloor(7.2f);                     // unjumpable lit — that's the point
                Bay(b, d, d.Range(4.6f, 6f), 1);
                if (d.tier >= 2 && d.Chance(45)) b.NightFloor(d.Range(1.8f, 2.2f));
                Hall(b, d, Mathf.Max(1, d.bays - 1), d.haz);
            }
            return b.Finish();
        }

        // THE PRESS — the vault comes down. Long uninterrupted runs, because
        // the only counterplay to a descending ceiling is not stopping.
        static Level Press(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                // The breather chamber is the one that gets to be a real room:
                // it's the only place on this floor you're allowed to stand
                // still, which is what makes the next ceiling land.
                if (i == d.honestRoom) { Open(b, d, i, RoomRule.None); Body(b, d); continue; }
                PressRoom(b, d, d.Gate(i), i == d.rooms - 1);
            }
            return b.Finish();
        }

        // WRONG HANDS — the curse takes your controls inside the room and gives
        // them back at the doorway. Kept geometrically SIMPLE on purpose:
        // reversed hands are already the difficulty, and stacking precision on
        // top of them is how you get a floor nobody finishes.
        static Level WrongHands(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Open(b, d, i, RoomRule.Reverse);
                bool cursed = d.RuleFor(i, RoomRule.Reverse) == RoomRule.Reverse;
                Bay(b, d, d.Range(6f, 8f), cursed ? 1 : d.haz);
                b.Gap(d.Range(2.2f, 2.5f));             // a plain jump, backwards
                Bay(b, d, d.Range(5f, 6.5f), cursed ? 1 : d.haz);
                if (!cursed) Body(b, d);
            }
            return b.Finish();
        }

        // THE HALL THAT REPEATS — walk out of the doorway and you're back at
        // the start of a room you can see all of. The gaslight floor: nothing
        // is hidden, and you still can't leave until you JUMP the rune.
        static Level LoopHall(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                // The final chamber can't loop (its doorway is the way out), so
                // it plays honest and dense instead.
                Open(b, d, i, i == d.rooms - 1 ? RoomRule.None : RoomRule.Loop);
                Bay(b, d, d.Range(7f, 9f), 1);
                Body(b, d);
            }
            return b.Finish();
        }

        // THE CHASE — the coffin bolts the moment you reach for it and only
        // corners itself against the end wall. Four honest chambers of setup,
        // then the run.
        static Level Chase(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms - 1; i++)
            {
                Open(b, d, i, RoomRule.None);
                Body(b, d);
            }
            // The finale is hand-shaped: the lane it runs down stays READABLE
            // (one thing to swerve round, not three) or the chase becomes a
            // dice roll instead of a joke.
            d.current = RoomRule.Flee;
            b.Room(RoomRule.Flee, 0.05f, d.tier >= 2);
            float start = b.Plat(3.5f);
            b.Gap(d.Range(2.2f, 2.5f));                 // plain jumps only down the lane
            Bay(b, d, d.Range(5f, 6f), 1);
            b.Gap(d.Range(2.2f, 2.5f));
            Bay(b, d, d.Range(9f, 12f), 1);
            b.Plat(d.Range(4f, 5f));
            b.ExitAt(start + 0.6f);                     // one step ahead of you. it knows.
            return b.FinishBare();
        }

        // COFFIN ROULETTE — dull-brass fakes with an invisible kill inside,
        // standing exactly where the real one would. The tell is the cross:
        // gold glows, brass doesn't.
        static Level Roulette(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Open(b, d, i, RoomRule.None);
                if (i != d.honestRoom)
                {
                    float w = d.Range(6.5f, 8f);
                    float c = b.Plat(w);
                    // A fake coffin hides an invisible kill, so each one spends
                    // a blind death. When the floor's allowance runs out the
                    // chamber gets a visible hazard instead — the shape stays
                    // the shape, it just stops being a guessing game.
                    if (d.TakeBlind()) b.FakeCoffin(c - w * 0.22f);
                    else Levels.PlaceHazard(b, d.Visible(), c - w * 0.22f);
                    if (d.tier >= 2 && d.Chance(50)) Levels.PlaceHazard(b, d.Pick(), c + w * 0.26f);
                    // A bright pink DOOR, in a game where the only exits are
                    // coffins. Deep floors only — it's a punchline, not a tax.
                    if (d.tier >= 3 && d.Chance(30) && d.TakeBlind())
                    { b.Gap(d.Range(2.2f, 2.5f)); float e = b.Plat(5f); b.FakeDoor(e); }
                }
                Body(b, d);
            }
            return b.Finish();
        }

        // THE LULLABY — runes that put you to sleep, and the only way to wake
        // up is to stop touching the controls. A rage game demanding the one
        // thing a rage player cannot do.
        static Level Lullaby(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                // One chamber sings you to sleep with the vault already coming
                // down — the thesis of the shape, and the reason it isn't just a
                // slow floor. It's built to the press budget, so the rune goes
                // on the one long run and nothing follows it.
                if (i == 1 && d.tier >= 2 && i != d.honestRoom)
                {
                    Enter(b, d, RoomRule.Press, false);
                    b.Plat(3.6f);
                    float pw = d.Range(8f, 9f);
                    float pc = b.Plat(pw);
                    b.SleepRune(pc - pw * 0.2f);
                    Levels.PlaceHazard(b, d.Pick(), pc + pw * 0.28f);
                    b.Gap(d.Range(2.2f, 2.5f));
                    b.Plat(d.Range(4f, 5f));
                    continue;
                }
                Open(b, d, i, RoomRule.None);
                float w = d.Range(6.5f, 9f);
                float c = b.Plat(w);
                b.SleepRune(c - w * 0.2f);
                if (d.tier >= 2 && d.Chance(55)) b.SleepRune(c + w * 0.2f);
                Levels.PlaceHazard(b, d.Pick(), c + w * 0.35f);
                Body(b, d);
            }
            return b.Finish();
        }

        // THE INVERSION — gravity runes. Cross one and you fall UP; the ceiling
        // is the road over gaps no jump will ever cross, and the drop rune is
        // the only way back down. The geometry here is the Chapel's, proven
        // numbers and all, because "generated" and "physics-breaking" is not a
        // combination to improvise.
        static Level Inversion(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Enter(b, d, RoomRule.None, d.Gate(i));
                bool holes = d.tier >= 3 && i > 0 && d.Chance(40);
                if (holes)
                {
                    // Hand-laid vault with a hole in it: walk off the ceiling
                    // road and you fall up into the sky, which kills exactly
                    // like a pit. Slab, hole, slab — the crossing from the
                    // Chapel, spaced to the unit.
                    b.OpenCeiling();
                    float a = b.Plat(5.5f); b.GravRune(a + 1.2f);
                    float g = a + 2.75f;
                    b.Gap(9f);
                    float c = b.Plat(9.5f); b.CeilRune(c - 3.95f);
                    Levels.PlaceHazard(b, d.Pick(), c + 1.5f);
                    b.CeilSlab(g - 5.5f, g + 2f);
                    b.CeilSlab(g + 4.2f, g + 10.5f);
                }
                else
                {
                    float a = b.Plat(5.5f); b.GravRune(a + 1.2f);
                    if (d.tier >= 3 && d.Chance(35)) b.DudRune(a + 2.4f);   // one of them is dead
                    b.Gap(8f);
                    float c = b.Plat(8f); b.CeilRune(c - 2.5f);
                    Levels.PlaceHazard(b, d.Pick(), c + 1.5f);
                }
                b.Plat(d.Range(2.5f, 3.5f));
            }
            return b.Finish();
        }

        // THE LONG FALL — the only shape with no chambers at all, which means
        // it's the only one where the bat wings work. Open sky, wide glide
        // gaps, and the hanging blades the flight modes hang in the upper air.
        // It exists so the descent occasionally opens out instead of squeezing.
        static Level LongFall(Depth d)
        {
            var b = new B();
            b.Plat(4.2f);                                // a safe doorstep
            int segments = 5 + Mathf.Min(5, d.floor / 3);
            bool lastLong = false;
            for (int i = 0; i < segments; i++)
            {
                if (d.tier >= 1 && !lastLong && d.Chance(26))
                { b.Gap(d.Range(6.0f, 6.8f)); lastLong = true; }        // jump, then hold glide
                else if (d.tier >= 1 && d.Chance(20))
                { b.FakeFloor(d.Range(2.0f, 2.3f)); lastLong = false; }
                else { b.Gap(d.Range(2.4f, 3.0f)); lastLong = false; }

                float w = (lastLong ? 5.2f : 4.2f) + d.Range(0f, 1.4f);
                float c = Bay(b, d, w, d.haz);
                // Half the way in, a coffin-shaped mercy: no chambers here means
                // no stage respawn, so the floor gives one back by hand.
                if (i == segments / 2) b.Checkpoint(c - w * 0.42f);
                // A pad that throws you at the vault. There's no vault here.
                if (d.tier >= 2 && d.Chance(18)) { b.Gap(2.4f); float s = b.Plat(3.2f); b.Spring(s); }
            }
            return b.Finish();
        }

        // THE CHASM — bobbing stone slabs over pits too wide to jump. The floor
        // under you is a timing puzzle rather than a place.
        static Level Chasm(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Enter(b, d, RoomRule.None, d.Gate(i));
                b.Plat(d.Range(3.6f, 4.4f));
                b.MoverGap(d.Range(6.8f, 7.2f), d.Range(1.0f, 1.5f));
                Bay(b, d, d.Range(4.6f, 6f), 1);
                if (d.tier >= 2 && d.Chance(60))
                {
                    // Two rides back to back, with a perch between them barely
                    // wide enough to stand on and nothing on it — the perch is
                    // the hazard.
                    b.MoverGap(d.Range(6.8f, 7.2f), d.Range(1.1f, 1.6f));
                    b.Plat(d.Range(2.8f, 3.4f));
                }
                Hall(b, d, 1, d.haz);
            }
            return b.Finish();
        }

        // IRON TEETH — the doorway itself is the threat. You've walked through
        // fifty of them and stopped looking; these cycle shut on a spiked
        // portcullis, so the safest-looking metre of the floor bites.
        static Level IronTeeth(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Enter(b, d, d.RuleFor(i, RoomRule.None), i > 0);   // every door, not one
                b.Plat(d.Range(3.4f, 4.2f));
                Body(b, d);
                // A tight run at the next set of teeth: arrive wrong and it's
                // already closing.
                if (d.tier >= 2 && d.Chance(50)) { Cross(b, d); Bay(b, d, d.Range(4f, 5f), 1); }
            }
            return b.Finish();
        }

        // THE WARRENS — pads that move you. The gap is unjumpable and the pad
        // is the only road, which makes "step into the glowing thing" mandatory
        // in a game whose entire lesson is not to.
        static Level Warrens(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Enter(b, d, RoomRule.None, d.Gate(i));
                float a = b.Plat(d.Range(4.6f, 5.4f));
                float padA = a + 1.5f;
                b.Gap(9f);                              // no jump crosses this
                float c = b.Plat(d.Range(5.5f, 6.5f));
                b.PortalAt(padA, -2f, c - 1.6f, -2f);
                Levels.PlaceHazard(b, d.Pick(), c + 1.8f);
                Hall(b, d, 1, d.haz);
            }
            return b.Finish();
        }

        // THE BLADE CHOIR — nothing hidden anywhere. Every hazard is visible,
        // on a loop, and the floor is one long question about whether you can
        // count. The honest shape, which is what makes the dishonest ones land.
        static Level BladeChoir(Depth d)
        {
            var b = new B();
            var choir = new[] { TrapType.Saw, TrapType.Pendulum, TrapType.GrowSpike, TrapType.FlameJet };
            for (int i = 0; i < d.rooms; i++)
            {
                Open(b, d, i, RoomRule.None);
                for (int k = 0; k < d.bays + 1; k++)
                {
                    Cross(b, d);
                    float w = d.Range(5.5f, 7f);
                    float c = b.Plat(w);
                    // Straight from the choir, not the floor palette: this shape
                    // is defined by what it REFUSES to use.
                    var t = choir[d.Next(d.tier >= 1 ? choir.Length : 2)];
                    Levels.PlaceHazard(b, t, c - w * 0.18f);
                    if (d.tier >= 2 && d.Chance(55))
                        Levels.PlaceHazard(b, choir[d.Next(choir.Length)], c + w * 0.24f);
                }
            }
            return b.Finish();
        }

        // THE FURNACE — fire from the floor and blessed water that pulses
        // lethal. Both are on timers, both punish standing still, and the
        // chamber that presses turns "wait for the gap" into a joke.
        static Level Furnace(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                // The last chamber closes the vault on the fire: the flame is on
                // a cycle you'd normally wait out, and waiting is now the thing
                // that kills you. Built to the press budget — one run, one jet.
                if (i == d.rooms - 1 && d.tier >= 2)
                {
                    Enter(b, d, RoomRule.Press, false);
                    b.Plat(3.6f);
                    float pw = d.Range(8f, 9f);
                    float pc = b.Plat(pw);
                    b.FlameJet(pc - pw * 0.22f);
                    if (d.Chance(60)) b.HolyWater(pc + pw * 0.22f);
                    continue;   // Finish() supplies the last run out from under it
                }
                Open(b, d, i, RoomRule.None);
                float w = d.Range(6.5f, 8.5f);
                float c = b.Plat(w);
                b.FlameJet(c - w * 0.24f);
                if (d.Chance(60)) b.HolyWater(c + w * 0.22f);
                Cross(b, d);
                float w2 = d.Range(5f, 6.5f);
                float c2 = b.Plat(w2);
                if (d.tier >= 1) b.HolyWater(c2 - w2 * 0.2f);
                Levels.PlaceHazard(b, d.Pick(), c2 + w2 * 0.24f);
                Hall(b, d, Mathf.Max(1, d.bays - 1), 1);
            }
            return b.Finish();
        }

        // THE LIARS — the original thesis, concentrated. Floors that aren't
        // floors, spikes that arrive after you've landed, ground that was never
        // there. Every death on this shape is one you agreed to.
        static Level Liars(Depth d)
        {
            var b = new B();
            for (int i = 0; i < d.rooms; i++)
            {
                Open(b, d, i, RoomRule.None);
                // The shape is "the ground is a liar", so its opening move is
                // always a floor that isn't one — but every one of those is a
                // death you couldn't have dodged, so they come out of the
                // floor's allowance like everything else. Spent, the chamber
                // opens with an honest jump instead and the LIE lands harder
                // for being rarer.
                if (d.TakeBlind()) b.FakeFloor(d.Range(2.0f, 2.3f));
                else b.Gap(d.Range(2.2f, 2.6f));
                float c = b.Plat(d.Range(5f, 6.5f));
                if (d.TakeBlind()) b.LateSpike(c + 1.2f);   // it rises the instant you arrive
                else b.GrowSpike(c + 1.2f);
                Cross(b, d);
                float w = d.Range(5f, 6.5f);
                float c2 = b.Plat(w);
                Levels.PlaceHazard(b, d.Pick(), c2 + w * 0.2f);
                Hall(b, d, Mathf.Max(1, d.bays - 1), 1);
            }
            return b.Finish();
        }
    }
}
