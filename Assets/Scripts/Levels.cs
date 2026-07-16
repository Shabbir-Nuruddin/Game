using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    public struct Rect2 { public Vector2 pos, size; public Rect2(float x, float y, float w, float h)
        { pos = new Vector2(x, y); size = new Vector2(w, h); } }

    public struct TrapSpec { public TrapType type; public Vector2 pos, size;
        public TrapSpec(TrapType t, float x, float y, float w, float h)
        { type = t; pos = new Vector2(x, y); size = new Vector2(w, h); } }

    public struct Deco { public Vector2 pos, size; public Color color;
        public Deco(float x, float y, float w, float h, Color c)
        { pos = new Vector2(x, y); size = new Vector2(w, h); color = c; } }

    public struct PortalPair { public Vector2 a, b;
        public PortalPair(float ax, float ay, float bx, float by)
        { a = new Vector2(ax, ay); b = new Vector2(bx, by); } }

    /// <summary>
    /// The ONE lie a room tells. This is the fix for "every level is the same 3
    /// platforms with different obstacles": a trap is an object you dodge, but a
    /// rule breaks a promise the whole room was built on. Exactly one per room,
    /// introduced then retired, so nothing ever settles into a pattern.
    /// </summary>
    public enum RoomRule
    {
        None,       // an honest room — the breather that makes the next lie land
        Dark,       // the candles go out; you can only see a small circle around you
        Flee,       // the coffin in this room runs away from you until it's cornered
        Press,      // the ceiling descends — keep moving or the crypt closes
        Reverse,    // the curse takes your hands: controls flip while you're in here
        Loop,       // walking out the doorway puts you back at the start; JUMP the rune
    }

    /// <summary>One room of a level: an X slice of the run, plus the rule it breaks.</summary>
    public struct RoomSpec
    {
        public float MinX, MaxX;
        public RoomRule Rule;
        // Where the rule FIRES, which is usually not the doorway. A dark room that
        // is already dark when you walk in is just a dim room — you creep through
        // it. The rule has to land after you've seen the layout and committed to
        // running it, so what it takes away is something you were relying on.
        public float TriggerX;
        public RoomSpec(float minX, float maxX, RoomRule rule, float triggerX)
        { MinX = minX; MaxX = maxX; Rule = rule; TriggerX = triggerX; }
    }

    public class Level
    {
        public Vector2 Spawn;
        public float CamMinX = -1.5f, CamMaxX = -1.5f;
        public int BossTier = 0;   // 0 = normal level; >0 = a boss arena of that tier
        public List<Rect2> Platforms = new();
        public List<TrapSpec> Traps = new();
        public List<Deco> Decos = new();
        public List<PortalPair> Portals = new();
        // Empty on the old corridor levels (11-40, Endless, Daily, Versus), which
        // keep their original behaviour untouched. Non-empty = a roomed level.
        public List<RoomSpec> Rooms = new();
        // Floor that only exists while the candles are lit. Identical to a real
        // platform in every way until its room goes dark, then it's simply gone.
        public List<Rect2> NightFloors = new();
        // The inverse lie: floor that only exists in the DARK. A faint shimmer in
        // the light; solid spectral stone once the candles die. Spans gaps too
        // wide to jump, so crossing means trusting the dark — floor 7's whole
        // lesson, and the exact opposite of what floor 2 taught.
        public List<Rect2> GhostFloors = new();
        // Sleep runes: step on one and the castle sings you to sleep. Fighting it
        // makes it worse; only stillness wakes you. (The gag the boss approved:
        // the game demands the one thing a rage-game player cannot do — nothing.)
        public List<Vector2> SleepRunes = new();
        // Every internal doorway (divider-wall x). Gets a stone arch frame so the
        // passages read as DOORS between chambers, not gaps between pillars.
        public List<float> Doorways = new();
        // Doorways with a spiked portcullis that slams on approach and then
        // cycles. The doorway itself becomes a threat — the one element the
        // player has crossed fifty times and stopped looking at.
        public List<float> Gates = new();
        // Spikes that RELOCATE while the lights are out: (litX, darkX). The
        // playtest verdict this answers: "you already know exactly where the
        // trap is going to be." Not in the dark you don't — the room you
        // memorised is not the room you're crossing. Your candle circle shows
        // the truth; your memory is the trap.
        public List<Vector4> ShiftSpikes = new();   // x = litX, y = darkX (z,w unused)
    }

    /// <summary>
    /// Fluent builder that lays platforms left-to-right with controlled gaps, so
    /// levels are GUARANTEED beatable: every gap is jumpable (<= 3), spike
    /// platforms are wide enough to land + run + jump, and crushers never share a
    /// platform with something you must jump over. The "unfair" comes from
    /// untelegraphed traps (fake floors, invisible deaths), not impossible jumps.
    /// </summary>
    class B
    {
        public Level L = new();
        float cur;  // left edge of the next piece

        public B(float spawnX = -10f) { L.Spawn = new Vector2(spawnX, -2f); cur = spawnX - 1.5f; }

        public float Plat(float w) { float cx = cur + w / 2f; L.Platforms.Add(new Rect2(cx, -3f, w, 0.6f)); cur += w; return cx; }
        public float FakeFloor(float w) { float cx = cur + w / 2f; L.Traps.Add(new TrapSpec(TrapType.FakeFloor, cx, -3f, w, 0.6f)); cur += w; return cx; }
        public void Gap(float w) { cur += w; }

        void T(TrapType t, float x, float y, float w, float h) => L.Traps.Add(new TrapSpec(t, x, y, w, h));
        public void Spike(float x) => T(TrapType.SpikeStatic, x, -2.4f, 0.7f, 0.7f);
        public void GrowSpike(float x) => T(TrapType.GrowSpike, x, -2.0f, 0.7f, 1.4f);
        public void ArrowRain(float x) => T(TrapType.ArrowRain, x, -3f, 0.5f, 0.5f);
        public void Checkpoint(float x) => T(TrapType.Checkpoint, x, -2f, 1f, 1.6f);
        public void BreakWall(float x) => T(TrapType.BreakBlock, x, -0.7f, 0.7f, 4f); // shoot to pass
        public void LateSpike(float x) => T(TrapType.LateSpike, x, -2.4f, 1.0f, 1.2f);
        public void Dart(float x) => T(TrapType.Dart, x, -2.3f, 1.0f, 1.2f);
        public void Faller(float x) => T(TrapType.Faller, x, -2.3f, 1.2f, 1.2f);
        public void Surprise(float x) => T(TrapType.Surprise, x, -2.2f, 0.8f, 1.0f);
        public void Saw(float x) => T(TrapType.Saw, x, -2.2f, 0.9f, 0.9f);
        public void Reverse(float x) => T(TrapType.Reverse, x, -2.3f, 1.5f, 1.2f);
        public void WarpBack(float x) => T(TrapType.WarpBack, x, -2.3f, 0.8f, 1.2f);
        public void Crusher(float x) => T(TrapType.Crusher, x, -1f, 1.6f, 1.4f); // no coin tell — jump up here and you're crushed
        public void Spring(float x) { T(TrapType.Spring, x, -2.55f, 1.0f, 0.5f); T(TrapType.Surprise, x, -0.4f, 1.4f, 1.4f); }
        // --- vampire traps ---
        public void Pendulum(float x) => T(TrapType.Pendulum, x, 1.0f, 0.45f, 0.25f);   // pivot high; blade swings below
        public void FlameJet(float x) => T(TrapType.FlameJet, x, -2.0f, 0.8f, 1.6f);    // erupts up from the floor
        public void Chandelier(float x) => T(TrapType.Chandelier, x, -2.3f, 1.2f, 1.2f);// reactive ceiling drop (wide)
        public void HolyWater(float x) => T(TrapType.HolyWater, x, -2.55f, 1.4f, 0.4f); // floor puddle, pulses lethal
        public void Bat(float x) => T(TrapType.BatSwoop, x, 1.8f, 0.6f, 0.6f);          // hovers, then dives

        // ---- Rooms ----
        // A roomed level is still one continuous left-to-right run, but it's cut
        // into chambers by a ceiling and a wall with a doorway punched through it.
        // That alone kills the "endless corridor" read; the rule on each room is
        // what kills the "same level again" read.
        public const float CeilY = 3.4f;    // high enough that a full jump clears the doorway comfortably
        const float DoorTopY = -1.1f;       // headroom of the gap you walk through
        RoomRule _roomRule = RoomRule.None;
        float _roomStart, _roomTrigger;
        bool _inRoom;

        /// <summary>
        /// Open a new chamber here, walling it off from the last one.
        /// triggerFrac is how far into the room the rule fires, 0-1. Default 0.35:
        /// you get a clear look at the room, start running it, and THEN it lies.
        /// gated puts a cycling spiked portcullis in this room's ENTRY doorway.
        /// </summary>
        public void Room(RoomRule rule, float triggerFrac = 0.35f, bool gated = false)
        {
            bool first = !_inRoom;
            CloseRoom();
            _roomTrigger = triggerFrac;
            if (!first)
            {
                // The divider: solid from the doorway's head up to the ceiling, so
                // the player ducks through the gap at floor level.
                float wy = (DoorTopY + CeilY) / 2f;
                L.Platforms.Add(new Rect2(cur, wy, 0.6f, CeilY - DoorTopY));
                L.Doorways.Add(cur);
                if (gated) L.Gates.Add(cur);
            }
            _roomStart = cur; _roomRule = rule; _inRoom = true;
        }

        /// <summary>
        /// A spike that stands at litX… until the room goes dark, when it silently
        /// relocates to darkX. Both spots must be on solid floor.
        /// </summary>
        public void ShiftSpike(float litX, float darkX) =>
            L.ShiftSpikes.Add(new Vector4(litX, darkX, 0f, 0f));

        // Close the open chamber, capping it with a ceiling across its full span.
        void CloseRoom()
        {
            if (!_inRoom) return;
            float w = cur - _roomStart;
            if (w > 0.5f)
            {
                L.Rooms.Add(new RoomSpec(_roomStart, cur, _roomRule,
                                         _roomStart + w * Mathf.Clamp01(_roomTrigger)));
                L.Platforms.Add(new Rect2(_roomStart + w / 2f, CeilY, w, 0.6f));
            }
            _inRoom = false;
        }

        /// <summary>
        /// Floor that's real until the lights die. Keep these NARROW — when it
        /// vanishes it must leave a gap you can still jump blind, or the room
        /// becomes impossible rather than mean.
        /// </summary>
        public float NightFloor(float w)
        {
            float cx = cur + w / 2f;
            L.NightFloors.Add(new Rect2(cx, -3f, w, 0.6f));
            cur += w;
            return cx;
        }

        /// <summary>
        /// Floor that only exists in the dark. The opposite constraint applies:
        /// keep these WIDER than a max jump (> 3.2) so the lit gap is clearly
        /// impossible — that's what forces the player to trust the dark.
        /// </summary>
        public float GhostFloor(float w)
        {
            float cx = cur + w / 2f;
            L.GhostFloors.Add(new Rect2(cx, -3f, w, 0.6f));
            cur += w;
            return cx;
        }

        /// <summary>A rune on the floor that naps you. Jumpable — that's the counterplay.</summary>
        public void SleepRune(float x) => L.SleepRunes.Add(new Vector2(x, -2.5f));

        /// <summary>Place the real exit yourself (for Flee finales — see FinishBare).</summary>
        public void ExitAt(float x) => T(TrapType.RealExit, x, -2f, 1.4f, 1.8f);

        /// <summary>
        /// A convincing fake coffin: same silhouette as the real exit, but its
        /// cross is dull brass instead of glowing gold — that's the tell — and
        /// "inside" is an invisible kill zone. Roulette floors are built on these.
        /// Its top is low enough to jump over once you've stopped trusting it.
        /// </summary>
        public void FakeCoffin(float x)
        {
            var dull = Theme.Hex("6B5A2E");
            L.Decos.Add(new Deco(x, -2f, 1.4f, 2.05f, Theme.Hex("140C08")));
            L.Decos.Add(new Deco(x, -2f, 1.15f, 1.9f, Theme.Hex("3A2418")));
            L.Decos.Add(new Deco(x, -1.9f, 0.18f, 0.95f, dull));
            L.Decos.Add(new Deco(x, -1.55f, 0.62f, 0.18f, dull));
            T(TrapType.Surprise, x, -2.1f, 0.8f, 1.5f);
        }

        /// <summary>
        /// Close out a level whose LAST room places its own exit (Flee finales,
        /// coffin roulette): no auto coffin, but cap the run with a full-height
        /// end wall so a fleeing coffin visibly corners itself instead of
        /// stopping at invisible air.
        /// </summary>
        public Level FinishBare()
        {
            L.Platforms.Add(new Rect2(cur + 0.3f, (CeilY - 2.7f) / 2f, 0.6f, CeilY + 2.7f));
            CloseRoom();
            L.CamMinX = -1.5f;
            L.CamMaxX = Mathf.Max(-1.5f, cur - 10f);
            return L;
        }

        public Level Finish()
        {
            Gap(2.5f);
            float endc = Plat(4f);
            CloseRoom();   // no-op on the old corridor levels, which never open one
            T(TrapType.RealExit, endc, -2f, 1.4f, 1.8f); // the one clear goal
            L.CamMinX = -1.5f;
            L.CamMaxX = Mathf.Max(-1.5f, cur - 10f);
            return L;
        }
    }

    public static class Levels
    {
        public static int Count => 40;

        // A boss arena: one solid floor (NO pits — a fair fight), bounding walls,
        // and the player spawned at the left. GameRoot spawns the boss + the (sealed)
        // exit, which opens when the boss dies. Tier scales the boss, not the room.
        public static Level BossRoom(int tier)
        {
            var L = new Level { BossTier = Mathf.Clamp(tier, 1, 4) };
            L.Spawn = new Vector2(-8f, -2f);
            L.Platforms.Add(new Rect2(0f, -3f, 26f, 0.6f));     // arena floor
            L.Platforms.Add(new Rect2(-13.2f, 1f, 0.6f, 9f));   // left wall
            L.Platforms.Add(new Rect2(13.2f, 1f, 0.6f, 9f));    // right wall
            L.CamMinX = -6f; L.CamMaxX = 6f;
            return L;
        }

        public static Level Get(int index)
        {
            switch (((index % Count) + Count) % Count)
            {
                case 0: return L1(); case 1: return L2(); case 2: return L3();
                case 3: return L4(); case 4: return L5(); case 5: return L6();
                case 6: return L7(); case 7: return L8(); case 8: return L9();
                case 9: return L10(); case 10: return L11(); case 11: return L12();
                case 12: return L13(); case 13: return L14(); case 14: return L15();
                case 15: return L16(); case 16: return L17(); case 17: return L18();
                case 18: return L19(); case 19: return L20(); case 20: return L21();
                case 21: return L22(); case 22: return L23(); case 23: return L24();
                case 24: return L25(); case 25: return L26(); case 26: return L27();
                case 27: return L28(); case 28: return L29(); case 29: return L30();
                case 30: return L31(); case 31: return L32(); case 32: return L33();
                case 33: return L34(); case 34: return L35(); case 35: return L36();
                case 36: return L37(); case 37: return L38(); case 38: return L39();
                default: return L40();
            }
        }

        // ---- Procedural generator (powers Endless + Daily) ----
        // Uses the same B builder, so every generated level is guaranteed
        // beatable (jumpable gaps, one spaced hazard per platform). Difficulty
        // grows the hazard variety and length.
        public static Level Generate(int seed, int difficulty)
        {
            var rng = new System.Random(seed);
            difficulty = Mathf.Max(0, difficulty);
            var pool = HazardPool(difficulty);
            var b = new B();
            b.Plat(3.7f); // safe start

            // These levels are FLIGHT modes (Endless / Blood Moon), so the player
            // can bat-glide. We cap inverted-controls to ONE per level (it was the
            // most-complained-about Blood Moon pain) and sprinkle a few wide gaps
            // that can only be cleared by jumping then holding glide.
            bool reverseUsed = false, lastWasLong = false;

            int segments = Mathf.Clamp(5 + difficulty, 5, 11);
            for (int i = 0; i < segments; i++)
            {
                // A wide GLIDE gap: too far for a plain jump, crossable with bat form.
                bool longGap = difficulty >= 3 && !lastWasLong && rng.Next(100) < 22;
                if (longGap) { b.Gap(6.0f + (float)rng.NextDouble() * 0.7f); lastWasLong = true; }
                else if (difficulty >= 1 && rng.Next(100) < 18 + difficulty * 3) { b.FakeFloor(2f); lastWasLong = false; }
                else { b.Gap(2.4f + (float)rng.NextDouble() * 0.5f); lastWasLong = false; }

                // A wider platform after a glide gap = a fair landing + meter refill.
                float p = b.Plat((longGap ? 4.6f : 3.6f) + (float)rng.NextDouble() * 1.3f);

                var first = NextHazard(pool, rng, ref reverseUsed);
                PlaceHazard(b, first, p);

                // A second hazard deeper in, for variety — but NEVER pair anything
                // with a Crusher. A Crusher demands you stay LOW (jump and the block
                // slams you), while almost every other hazard demands you JUMP OVER
                // it. Combine the two and the platform is physically impossible.
                // We also keep the rage-teleport (WarpBack) solo.
                if (difficulty >= 4 && !Soloist(first) && rng.Next(100) < 30)
                {
                    var second = NextHazard(pool, rng, ref reverseUsed);
                    if (!Soloist(second))
                        PlaceHazard(b, second, p + 1.6f);
                }
            }
            b.Gap(2.4f);
            return b.Finish();
        }

        // Pick a hazard, but allow at most ONE inverted-controls trap per level —
        // after that, Reverse is swapped for an ordinary spike.
        static TrapType NextHazard(List<TrapType> pool, System.Random rng, ref bool reverseUsed)
        {
            var t = pool[rng.Next(pool.Count)];
            if (t == TrapType.Reverse)
            {
                if (reverseUsed) return TrapType.SpikeStatic;
                reverseUsed = true;
            }
            return t;
        }

        static List<TrapType> HazardPool(int d)
        {
            var l = new List<TrapType> { TrapType.SpikeStatic, TrapType.SpikeStatic };
            if (d >= 1) l.Add(TrapType.LateSpike);
            if (d >= 2) { l.Add(TrapType.Dart); l.Add(TrapType.Crusher); l.Add(TrapType.GrowSpike); }
            if (d >= 3) { l.Add(TrapType.Faller);
                          l.Add(TrapType.Pendulum); l.Add(TrapType.Chandelier); }        // vampire traps
            if (d >= 4) { l.Add(TrapType.Saw); l.Add(TrapType.Surprise); l.Add(TrapType.WarpBack);
                          l.Add(TrapType.FlameJet); l.Add(TrapType.HolyWater);
                          l.Add(TrapType.Reverse); }                                       // inverted controls (rare)
            if (d >= 5) { l.Add(TrapType.ArrowRain); l.Add(TrapType.BatSwoop); }
            return l;
        }

        // Hazards that must stand ALONE on a platform: crushers (stay-low), the
        // warp rune (rage teleport), and the reactive ceiling drops (Faller /
        // Chandelier) — pairing a drop with another hazard forces you to stop
        // right under it, which is what made the night-3 "falling box" unfair.
        static bool Soloist(TrapType t) =>
            t == TrapType.Crusher || t == TrapType.WarpBack ||
            t == TrapType.Faller || t == TrapType.Chandelier;

        static void PlaceHazard(B b, TrapType t, float p)
        {
            switch (t)
            {
                case TrapType.LateSpike: b.LateSpike(p); break;
                case TrapType.Dart: b.Dart(p); break;
                case TrapType.Faller: b.Faller(p); break;
                case TrapType.Crusher: b.Crusher(p); break;
                case TrapType.Saw: b.Saw(p); break;
                case TrapType.ArrowRain: b.ArrowRain(p); break;
                case TrapType.Surprise: b.Surprise(p); break;
                case TrapType.GrowSpike: b.GrowSpike(p); break;
                case TrapType.Reverse: b.Reverse(p); break;       // flips controls for a few seconds
                case TrapType.WarpBack: b.WarpBack(p); break;     // cursed rune yanks you to the start
                case TrapType.Pendulum: b.Pendulum(p); break;
                case TrapType.FlameJet: b.FlameJet(p); break;
                case TrapType.Chandelier: b.Chandelier(p); break;
                case TrapType.HolyWater: b.HolyWater(p); break;
                case TrapType.BatSwoop: b.Bat(p); break;
                default: b.Spike(p); break;
            }
        }

        // ====================================================================
        // FLOORS 1–20 — the teaching castle. ONE new mechanic is introduced per
        // floor on wide, forgiving ground, then gently combined, so a new player
        // ramps up instead of slamming into a wall. Floors 10 & 20 are fair
        // "gauntlet" gates (and become BOSS rooms once Phase 2 lands).
        // ====================================================================

        // 1 — five chambers, four different DIRECTIONS of death. The playtest
        // verdict on the old floors was "something's ahead at floor level, jump
        // it" — one answer solved everything. So floor 1's job is now to break
        // that habit on day one: the floor lies (below), a spike pops late
        // (timing), a chandelier falls on the landing you'd pause on (above),
        // and the last doorway itself bites (the portcullis debut).
        static Level L1()
        {
            var b = new B();
            b.Room(RoomRule.None); b.Plat(5.5f);                                 // trust me
            b.Room(RoomRule.None); b.Plat(2.5f); b.FakeFloor(2.2f); b.Plat(3f);  // …don't stop on it
            b.Room(RoomRule.None); float a = b.Plat(6f); b.LateSpike(a - 1.2f);  // ambush AT the entry, not mid-room
            b.Room(RoomRule.None); b.Plat(2.5f); b.Gap(2.3f);
                                   float c = b.Plat(4.5f); b.Chandelier(c - 0.9f); // falls on the LANDING
            b.Room(RoomRule.None, 0.35f, true); b.Plat(4f);                      // the door has teeth
            return b.Finish();
        }

        // 2 — THE CANDLES GO OUT, and the room you memorised goes with them.
        //
        // Two lies share the theme. First: the light is holding the floor up
        // (night floors vanish). Second, the new one: the dark REARRANGES —
        // spikes silently relocate while the lights are out, so the layout you
        // studied on the way in is a trap in itself. Your candle circle shows
        // the truth; trusting your memory over your eyes is what kills you.
        // That's aimed square at "you already know exactly where the trap is."
        static Level L2()
        {
            var b = new B();
            b.Room(RoomRule.None);        b.Plat(6f);                            // lit and honest
            // the lights die exactly as you step onto the floor you were promised
            b.Room(RoomRule.Dark, 0.42f); b.Plat(3f); b.NightFloor(2f); b.Plat(3f);
            // spikes at the far end… you watch them, the lights die, and the dark
            // quietly walks them back toward you
            // Dark spot stays well AHEAD of where the player stands when the
            // lights die — the spike must move into their PATH, never onto them.
            b.Room(RoomRule.Dark, 0.3f);  float a = b.Plat(7f);
                                          b.ShiftSpike(a + 1.8f, a + 0.2f);
            b.Room(RoomRule.None);        b.Plat(3f); b.Gap(2.3f); b.Plat(3f);   // lights up: breathe
            b.Room(RoomRule.Dark, 0.3f);  b.Plat(2.5f); b.NightFloor(2f);
                                          float c = b.Plat(4.5f);
                                          b.ShiftSpike(c + 1.4f, c - 0.6f);
            return b.Finish();
        }

        // ====================================================================
        // FLOORS 1–10: one NEW lie per floor, introduced gently, twisted, then
        // combined with exactly ONE older lie. Never two new ideas at once, and
        // no floor reuses last floor's twist at full strength — the Level Devil
        // grammar (theirs: doors of 5 stages, one theme per door, zero bosses).
        // ====================================================================

        // 3 — THE COFFIN FLEES. The exit has been the one honest thing in the
        // game for two floors; that's exactly why it's the next thing to lie.
        // Rooms 2-4 are ordinary traversal so the finale lands on calm nerves:
        // the coffin sits RIGHT THERE at the mouth of room 5… and bolts, dragging
        // you through a spike-and-gap chase until it corners itself at the wall.
        static Level L3()
        {
            var b = new B();
            b.Room(RoomRule.None); b.Plat(6f);
            b.Room(RoomRule.None); float a = b.Plat(6.5f); b.Dart(a);            // death arrives SIDEWAYS
            b.Room(RoomRule.None); b.Plat(2.6f); b.FakeFloor(2f); b.Plat(2.6f);
            b.Room(RoomRule.None); b.Plat(3f); b.Gap(2.3f);
                                   float c = b.Plat(3.2f); b.HolyWater(c + 0.6f); // the floor itself pulses
            // The chase: a gate slams behind you, and the coffin drags you over a
            // pulsing puddle and through a dart lane before the wall corners it.
            b.Room(RoomRule.Flee, 0.35f, true);
                                   float p = b.Plat(4f); b.Spike(p + 1.2f);
                                   b.Gap(2.3f);
                                   float q = b.Plat(6f); b.HolyWater(q - 0.8f); b.Dart(q + 1.4f);
                                   b.ExitAt(p - 1.2f);   // right there. almost touching.
            return b.FinishBare();
        }

        // 4 — THE CRYPT PRESS. The ceiling was scenery until now; now it wants
        // you dead. Slow enough to outwalk — the press kills the two things
        // panic makes you do: freezing to watch it, and stopping to think.
        static Level L4()
        {
            var b = new B();
            b.Room(RoomRule.None);         b.Plat(6f);
            b.Room(RoomRule.Press, 0.3f);  b.Plat(7f);                        // just RUN
            b.Room(RoomRule.None);         b.Plat(3f); b.Gap(2.3f); b.Plat(3f);
            b.Room(RoomRule.Press, 0.25f); float a = b.Plat(7f);
                                           b.HolyWater(a - 1f);               // ceiling above, pulse below
            b.Room(RoomRule.Press, 0.2f);  b.Plat(2.5f); b.FakeFloor(2f); b.Plat(3f);
            return b.Finish();                                                // …and the floor lies
        }

        // 5 — THE LULLABY. Sleep runes: step on one and you're out. Mashing
        // resets the wake timer (with escalating scolding); only stillness ends
        // it. Room 4 is the floor's thesis — a rune under a descending press.
        // The disciplined sleeper walks out with a full second to spare; the
        // masher greases the crypt. Room 5: rune-hop under a press, or nap once
        // and sprint the rest on a knife's edge.
        static Level L5()
        {
            var b = new B();
            b.Room(RoomRule.None);         b.Plat(6f);
            b.Room(RoomRule.None);         float a = b.Plat(7f); b.SleepRune(a);
            // a bat roosts above the second rune: nap here and you're dinner.
            // (The playtest's favourite moment was sleep+press — "you actually
            // can't do shit" — so the floor now has THREE different answers to
            // "what eats me if I nap here": nothing, a bat, the ceiling.)
            b.Room(RoomRule.None);         float c = b.Plat(7f); b.SleepRune(c - 1.5f); b.Bat(c - 1f);
            b.Room(RoomRule.Press, 0.22f); float d = b.Plat(7.5f); b.SleepRune(d - 1.2f);
            b.Room(RoomRule.Press, 0.3f);  float e = b.Plat(8f); b.SleepRune(e - 2.6f);
                                           b.SleepRune(e - 0.2f); b.SleepRune(e + 2.2f);
            return b.Finish();
        }

        // 6 — THE CURSED HAND. Controls flip partway into the room and stay
        // flipped until you leave it. Room 2 is flat and harmless so the
        // moonwalk is a joke before it's a weapon; then a reversed gap jump;
        // then a reversed spike gauntlet all the way to the coffin.
        static Level L6()
        {
            var b = new B();
            b.Room(RoomRule.None);           b.Plat(6f);
            b.Room(RoomRule.Reverse, 0.35f); b.Plat(7f);
            b.Room(RoomRule.Reverse, 0.3f);  b.Plat(3f); b.Gap(2.3f); b.Plat(3.5f);
            b.Room(RoomRule.None);           float a = b.Plat(6f); b.Pendulum(a); // death swings OVERHEAD
            b.Room(RoomRule.Reverse, 0.15f); b.Plat(2.5f); float c = b.Plat(2.8f); b.Dart(c);
                                             b.Gap(2.3f); b.Plat(3f);              // hands flip BEFORE the dart, or the gag misfires
            return b.Finish();
        }

        // 7 — FAITH IN THE DARK. The inversion floor: floor 2 taught "the dark
        // takes the ground away", so floor 7 makes the dark GIVE it — spectral
        // bridges that only exist once the candles die, spanning gaps too wide
        // to jump lit. Room 3 runs both lies at once: the dark deletes one
        // floor and builds another. Trust nothing; then trust the dark.
        static Level L7()
        {
            var b = new B();
            b.Room(RoomRule.None);        b.Plat(3f); b.Gap(2.2f); b.Plat(3f);
            b.Room(RoomRule.Dark, 0.2f);  b.Plat(3f); b.GhostFloor(3.4f); b.Plat(3f);
            b.Room(RoomRule.Dark, 0.18f); b.Plat(2.5f); b.NightFloor(2f); b.Plat(2.2f);
                                          b.GhostFloor(3.4f); float g = b.Plat(2.5f);
                                          b.ShiftSpike(g + 0.5f, g - 0.3f);   // the far shore rearranges too
            b.Room(RoomRule.None);        b.Plat(3f); b.Gap(2.3f); b.Plat(3f);
            b.Room(RoomRule.Dark, 0.2f);  b.Plat(2.5f); b.GhostFloor(3.4f); b.Plat(2f);
                                          b.NightFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 8 — THE ENDLESS HALL. Walk across the rune at the doorway and you're
        // silently back at the start of an identical room. The tell is the rune
        // itself: JUMP it. Room 3 loops a gap room; room 4 puts a spike right
        // before the rune (one arc clears both); the hall gives up after five
        // loops — mercy disguised as boredom, so nobody can be stuck forever.
        static Level L8()
        {
            var b = new B();
            b.Room(RoomRule.None); b.Plat(6f);
            b.Room(RoomRule.Loop); b.Plat(7.5f);
            b.Room(RoomRule.Loop); b.Plat(3f); b.Gap(2.3f); b.Plat(3.2f);
            b.Room(RoomRule.Loop); float a = b.Plat(7f); b.GrowSpike(a + 0.8f);  // spike clock, THEN rune jump, THEN the gate's rhythm
            b.Room(RoomRule.None, 0.35f, true);                                  // …and the exit door bites
                                   b.Plat(2.5f); b.FakeFloor(2.2f); b.Plat(2.8f);
            return b.Finish();
        }

        // 9 — COFFIN ROULETTE. The castle fills its halls with coffins that are
        // ALMOST yours — same box, same cross, but dull brass where the real
        // one glows gold. Room 1 kills the unobservant once, cheaply, to teach
        // the tell. Room 3 hides coffins in the dark so they loom out of the
        // candle circle. The finale: three dull coffins and the one true glowing
        // one… which flees through the lineup when you reach for it (floor 3's
        // lie, resurrected exactly when you'd stopped watching for it).
        static Level L9()
        {
            var b = new B();
            b.Room(RoomRule.None);        float a = b.Plat(8f); b.FakeCoffin(a - 1f);
            b.Room(RoomRule.None);        b.Plat(3f); b.Gap(2.3f);
                                          float c = b.Plat(4.5f); b.FakeCoffin(c + 0.6f);
            b.Room(RoomRule.Dark, 0.25f); float d = b.Plat(9f); b.FakeCoffin(d - 2f); b.FakeCoffin(d + 1.5f);
            b.Room(RoomRule.None);        b.Plat(2.5f); b.FakeFloor(2f);
                                          float e = b.Plat(4.5f); b.FlameJet(e - 0.6f);
                                          b.FakeCoffin(e + 1.2f);   // fire guards a coffin that's ALSO lying
            b.Room(RoomRule.Flee);        float p = b.Plat(12f); b.FakeCoffin(p - 3.4f);
                                          b.FakeCoffin(p - 0.9f); b.FakeCoffin(p + 1.6f);
                                          b.HolyWater(p + 3f);
                                          b.ExitAt(p - 5f);
            return b.FinishBare();
        }

        // 10 — THE FINAL EXAM. No boss — the research is unambiguous that this
        // genre's tension comes from "what's the next lie", and a health-bar
        // fight is a different game (bosses still hold floors 20/30/40). Six
        // rooms, every rule from the world, and the exam is allowed to break the
        // one-lie-per-room law because breaking its own laws IS the castle's
        // final lesson. Ends with the coffin fleeing across a spike field.
        static Level L10()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.3f);    b.Plat(2.5f); b.NightFloor(2f); b.Plat(2.8f);
            b.Room(RoomRule.Press, 0.25f);  float a = b.Plat(6.5f); b.HolyWater(a);
            b.Room(RoomRule.Reverse, 0.3f); b.Plat(3f); b.Gap(2.3f);
                                            float s = b.Plat(3.2f); b.SleepRune(s + 0.8f);
            b.Room(RoomRule.Loop, 0.35f, true);
                                            b.Plat(7f); b.FakeFloor(2f); b.Plat(2f);
            b.Room(RoomRule.Dark, 0.2f);    b.Plat(2.5f); b.GhostFloor(3.4f);
                                            float g = b.Plat(2.5f); b.ShiftSpike(g + 0.6f, g - 0.4f);
            b.Room(RoomRule.Flee);          float p = b.Plat(10f); b.Dart(p - 2f); b.Spike(p + 1f);
                                            b.ExitAt(p - 3.8f);
            return b.FinishBare();
        }

        // 11 — BATS: they hover, then dive on a telegraph. Keep moving to dodge.
        static Level L11()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.4f);
            float p2 = b.Plat(5f); b.Bat(p2);
            b.Gap(2.4f);
            float p3 = b.Plat(4.5f); b.Spike(p3);
            b.FakeFloor(2f);                       // the crypt lies too
            float p4 = b.Plat(4.5f); b.Dart(p4);
            return b.Finish();
        }

        // 12 — bats wheeling over a saw lane.
        static Level L12()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.Saw(p2); b.Bat(p2 + 1.6f);
            b.Gap(2.5f);
            float p3 = b.Plat(4.5f); b.LateSpike(p3); b.Bat(p3 + 1.4f);
            b.Gap(2.5f);
            float p4 = b.Plat(4.5f); b.Chandelier(p4);
            return b.Finish();
        }

        // 13 — a swinging blade, a dart, and a circling bat.
        static Level L13()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.Pendulum(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(5f); b.Dart(p3 - 1f); b.Bat(p3 + 1.6f);
            b.Gap(2.5f);
            float p4 = b.Plat(4.5f); b.Spike(p4);
            return b.Finish();
        }

        // 14 — the SUN: invisible burning ground, flagged by a faint sunbeam. Jump it.
        static Level L14()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.Surprise(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(4.5f); b.Pendulum(p3);
            b.FakeFloor(2f);                       // sunbeam, blade, THEN the lying floor
            float p4 = b.Plat(4.5f); b.LateSpike(p4);
            return b.Finish();
        }

        // 15 — sunbeams between drops and a diving bat.
        static Level L15()
        {
            var b = new B();
            b.Plat(4.5f);
            b.FakeFloor(2f);
            float p2 = b.Plat(5f); b.Surprise(p2 - 1f); b.Dart(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(4.5f); b.Chandelier(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(4.5f); b.Bat(p4);
            return b.Finish();
        }

        // 16 — a longer trial with a mid checkpoint.
        static Level L16()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.Saw(p2 - 1.3f); b.Spike(p2 + 1.3f);
            b.Gap(2.5f);
            float p3 = b.Plat(4.8f); b.Checkpoint(p3 - 1.5f); b.Surprise(p3 + 1f);
            b.Gap(2.5f);
            float p4 = b.Plat(4.5f); b.Pendulum(p4);
            return b.Finish();
        }

        // 17 — FLAME JETS erupt from the floor. Cross while they're down.
        static Level L17()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.FlameJet(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(5f); b.FlameJet(p3 - 1.3f); b.Spike(p3 + 1.3f);
            b.Gap(2.5f);
            float p4 = b.Plat(5f); b.FlameJet(p4 - 1.3f); b.Dart(p4 + 1.3f);
            return b.Finish();
        }

        // 18 — HOLY WATER puddles flare on a pulse. Step through while dim.
        static Level L18()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.HolyWater(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(5f); b.HolyWater(p3 - 1.3f); b.FlameJet(p3 + 1.3f);
            b.Gap(2.5f);
            float p4 = b.Plat(4.5f); b.Bat(p4);
            return b.Finish();
        }

        // 19 — a cursed rune (warps you back), flames, and a blade.
        static Level L19()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.FlameJet(p2 - 1.3f); b.WarpBack(p2 + 1.3f);
            b.Gap(2.5f);
            float p3 = b.Plat(4.5f); b.Pendulum(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(4.5f); b.HolyWater(p4);
            return b.Finish();
        }

        // 20 — GATE: the vampire traps together, with a checkpoint.
        static Level L20()
        {
            var b = new B();
            b.Plat(4.5f);
            b.Gap(2.6f);
            float p2 = b.Plat(5f); b.Pendulum(p2 - 1.3f); b.Surprise(p2 + 1.3f);
            b.Gap(2.6f);
            float p3 = b.Plat(4.8f); b.Checkpoint(p3 - 1.5f); b.FlameJet(p3 + 1f);
            b.Gap(2.6f);
            float p4 = b.Plat(5f); b.Chandelier(p4 - 1.3f); b.Bat(p4 + 1.3f);
            b.Gap(2.6f);
            float p5 = b.Plat(4.5f); b.HolyWater(p5);
            return b.Finish();
        }

        // ====================================================================
        // FLOORS 21–40 — the deep castle. Difficulty keeps climbing: hazards
        // pair up tighter, inversions and warp-runes show up more, and gaps
        // creep wider. Floor 40 is the boss room. Every platform still obeys the
        // golden rule — a Crusher (stay LOW) never shares ground with a jump-over
        // hazard — so all of these are beatable, just mean.
        // ====================================================================

        // 21 — reverse meets a spike: jump it with your controls flipped.
        static Level L21()
        {
            var b = new B();
            b.Plat(4f);
            b.Gap(2.5f);
            float p2 = b.Plat(4.6f); b.Reverse(p2 - 1.2f); b.Spike(p2 + 1.2f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Dart(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.6f); b.Faller(p4);
            return b.Finish();
        }

        // 22 — saws and a twin growing-spike pinch.
        static Level L22()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.6f);
            float p2 = b.Plat(4.6f); b.Saw(p2);
            b.Gap(2.6f);
            float p3 = b.Plat(4.4f); b.GrowSpike(p3 - 1.1f); b.GrowSpike(p3 + 1.1f);
            b.Gap(2.6f);
            float p4 = b.Plat(3.6f); b.LateSpike(p4);
            return b.Finish();
        }

        // 23 — the warp-rune + dart combo, then an invisible patch.
        static Level L23()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.5f);
            float p2 = b.Plat(4.6f); b.WarpBack(p2 - 1.2f); b.Dart(p2 + 1.2f);
            b.FakeFloor(2f);
            float p3 = b.Plat(4f); b.Surprise(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.6f); b.Spike(p4);
            return b.Finish();
        }

        // 24 — ceiling arrows into a faller/dart pincer, capped by a crusher.
        static Level L24()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.ArrowRain(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(4.6f); b.Faller(p3 - 1.1f); b.Dart(p3 + 1.1f);
            b.Gap(2.6f);
            float p4 = b.Plat(3.6f); b.Crusher(p4);
            return b.Finish();
        }

        // 25 — milestone: a mid-checkpoint before the back half.
        static Level L25()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.6f);
            float p2 = b.Plat(4.6f); b.Saw(p2);
            b.Gap(2.6f);
            float p3 = b.Plat(4.8f); b.Checkpoint(p3 - 1.6f); b.Surprise(p3 + 1f);
            b.Gap(2.6f);
            float p4 = b.Plat(4.4f); b.Dart(p4 - 1f); b.Spike(p4 + 1f);
            b.Gap(2.6f);
            float p5 = b.Plat(3.6f); b.LateSpike(p5);
            return b.Finish();
        }

        // 26 — flipped from the first step, then a warp-rune and a fake floor.
        static Level L26()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.Gap(2.5f);
            float p2 = b.Plat(4.4f); b.Spike(p2 - 1f); b.WarpBack(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(4.6f); b.Saw(p3);
            b.FakeFloor(2f);
            float p4 = b.Plat(4f); b.Faller(p4);
            return b.Finish();
        }

        // 27 — an arrow-rain corridor with a dart down the middle.
        static Level L27()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.5f);
            float p2 = b.Plat(5.2f); b.ArrowRain(p2 - 1.6f); b.Dart(p2); b.ArrowRain(p2 + 1.6f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Faller(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.6f); b.Spike(p4);
            return b.Finish();
        }

        // 28 — a wall of growing spikes, then saw and crusher.
        static Level L28()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.5f);
            float p2 = b.Plat(5.2f); b.GrowSpike(p2 - 1.6f); b.GrowSpike(p2); b.GrowSpike(p2 + 1.6f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Saw(p3);
            b.Gap(2.6f);
            float p4 = b.Plat(3.6f); b.Crusher(p4);
            return b.Finish();
        }

        // 29 — flipped controls into invisible deaths and a dart.
        static Level L29()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.FakeFloor(2f);
            float p2 = b.Plat(4.6f); b.Surprise(p2 - 1.2f); b.Dart(p2 + 1.2f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.ArrowRain(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.6f); b.Faller(p4);
            return b.Finish();
        }

        // 30 — milestone: long, with a checkpoint buried in the middle.
        static Level L30()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.7f);
            float p2 = b.Plat(5f); b.Spike(p2 - 1.5f); b.LateSpike(p2 + 0.6f);
            b.Gap(2.7f);
            float p3 = b.Plat(4.8f); b.Checkpoint(p3 - 1.6f); b.Saw(p3 + 1f);
            b.Gap(2.7f);
            float p4 = b.Plat(4f); b.WarpBack(p4);
            b.FakeFloor(2f);
            float p5 = b.Plat(4.4f); b.Surprise(p5 - 1f); b.Dart(p5 + 1f);
            return b.Finish();
        }

        // 31 — flip, saws, twin darts, faller.
        static Level L31()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1.2f);
            b.Gap(2.6f);
            float p2 = b.Plat(4.6f); b.Saw(p2);
            b.Gap(2.6f);
            float p3 = b.Plat(4.6f); b.Dart(p3 - 1.1f); b.Dart(p3 + 1.1f);
            b.Gap(2.6f);
            float p4 = b.Plat(4f); b.Faller(p4);
            return b.Finish();
        }

        // 32 — a heavier arrow storm, an invisible patch, then a crusher.
        static Level L32()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.6f);
            float p2 = b.Plat(5.6f); b.ArrowRain(p2 - 1.9f); b.ArrowRain(p2); b.ArrowRain(p2 + 1.9f);
            b.Gap(2.6f);
            float p3 = b.Plat(4f); b.Surprise(p3);
            b.Gap(2.7f);
            float p4 = b.Plat(3.6f); b.Crusher(p4);
            return b.Finish();
        }

        // 33 — kitchen sink with a checkpoint and a warp-rune finish.
        static Level L33()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.7f);
            float p2 = b.Plat(5f); b.GrowSpike(p2 - 1.5f); b.Dart(p2 + 1.5f);
            b.Gap(2.7f);
            float p3 = b.Plat(4.8f); b.Checkpoint(p3 - 1.6f); b.Saw(p3 + 1f);
            b.FakeFloor(2f);
            float p4 = b.Plat(4.6f); b.Surprise(p4 - 1.2f); b.LateSpike(p4 + 1.2f);
            b.Gap(2.7f);
            float p5 = b.Plat(4f); b.WarpBack(p5);
            return b.Finish();
        }

        // 34 — flip, warp + faller, arrows + dart, a saw to end.
        static Level L34()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.Gap(2.6f);
            float p2 = b.Plat(4.6f); b.WarpBack(p2 - 1.2f); b.Faller(p2 + 1.2f);
            b.Gap(2.6f);
            float p3 = b.Plat(4.6f); b.ArrowRain(p3 - 1f); b.Dart(p3 + 1f);
            b.Gap(2.6f);
            float p4 = b.Plat(4f); b.Saw(p4);
            return b.Finish();
        }

        // 35 — milestone: a triple-hazard platform and a checkpoint.
        static Level L35()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.8f);
            float p2 = b.Plat(5.2f); b.Dart(p2 - 1.6f); b.Spike(p2); b.Saw(p2 + 1.6f);
            b.Gap(2.8f);
            float p3 = b.Plat(4.8f); b.Checkpoint(p3 - 1.6f); b.GrowSpike(p3 + 1f);
            b.Gap(2.8f);
            float p4 = b.Plat(4.6f); b.Faller(p4 - 1.2f); b.LateSpike(p4 + 1.2f);
            b.FakeFloor(2f);
            float p5 = b.Plat(4f); b.Surprise(p5);
            return b.Finish();
        }

        // 36 — reverse hell: flipped twice, with a warp-rune sting.
        static Level L36()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.Gap(2.6f);
            float p2 = b.Plat(4.6f); b.Reverse(p2 - 1.2f); b.Spike(p2 + 1.2f);
            b.Gap(2.6f);
            float p3 = b.Plat(4.6f); b.Saw(p3);
            b.Gap(2.6f);
            float p4 = b.Plat(4.4f); b.Dart(p4 - 1f); b.WarpBack(p4 + 1f);
            return b.Finish();
        }

        // 37 — arrow storm hiding an invisible patch, saw, then a pincer.
        static Level L37()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.7f);
            float p2 = b.Plat(5.2f); b.ArrowRain(p2 - 1.6f); b.Surprise(p2); b.ArrowRain(p2 + 1.6f);
            b.Gap(2.7f);
            float p3 = b.Plat(4.6f); b.Saw(p3);
            b.FakeFloor(2f);
            float p4 = b.Plat(4.6f); b.Faller(p4 - 1.2f); b.Dart(p4 + 1.2f);
            return b.Finish();
        }

        // 38 — a crusher maze: crushers stand alone, danger fills the gaps between.
        static Level L38()
        {
            var b = new B();
            b.Plat(3.6f);
            b.Gap(2.6f);
            float p2 = b.Plat(3.8f); b.Crusher(p2);
            b.Gap(2.6f);
            float p3 = b.Plat(4.6f); b.Spike(p3 - 1.2f); b.Dart(p3 + 1.2f);
            b.Gap(2.6f);
            float p4 = b.Plat(3.8f); b.Crusher(p4);
            b.Gap(2.6f);
            float p5 = b.Plat(4f); b.Saw(p5);
            return b.Finish();
        }

        // 39 — the penultimate trial: everything, tight, before the boss.
        static Level L39()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.Gap(2.7f);
            float p2 = b.Plat(5.2f); b.Dart(p2 - 1.6f); b.ArrowRain(p2); b.GrowSpike(p2 + 1.6f);
            b.FakeFloor(2f);
            float p3 = b.Plat(4.6f); b.Surprise(p3 - 1.2f); b.Saw(p3 + 1.2f);
            b.Gap(2.7f);
            float p4 = b.Plat(4.6f); b.Faller(p4 - 1.2f); b.WarpBack(p4 + 1.2f);
            b.Gap(2.7f);
            float p5 = b.Plat(4f); b.LateSpike(p5);
            return b.Finish();
        }

        // 40 — THE BOSS ROOM. One long descent through three phases. Checkpoints
        // sit FAR apart (one after each phase), so a slip late in a phase costs
        // you the whole stretch. No warp-runes here — the room is brutal on its
        // own, and a rune that ignores checkpoints would just be unfair.
        static Level L40()
        {
            var b = new B();
            b.Plat(4.4f);                                          // the gate: safe staging

            // ---- Phase 1: the approach ----
            b.Gap(2.7f);
            float a1 = b.Plat(4.8f); b.Spike(a1 - 1.3f); b.Dart(a1 + 1.3f);
            b.Gap(2.7f);
            float a2 = b.Plat(4.6f); b.Saw(a2);
            b.FakeFloor(2f);
            float a3 = b.Plat(4.8f); b.Reverse(a3 - 1.3f); b.LateSpike(a3 + 1.3f);
            b.Gap(2.7f);
            float cp1 = b.Plat(4f); b.Checkpoint(cp1);             // CHECKPOINT — far in

            // ---- Phase 2: the gauntlet ----
            b.Gap(2.8f);
            float g1 = b.Plat(5.2f); b.GrowSpike(g1 - 1.6f); b.ArrowRain(g1 + 1.6f);
            b.Gap(2.8f);
            float g2 = b.Plat(3.8f); b.Crusher(g2);               // crusher stands alone
            b.Gap(2.8f);
            float g3 = b.Plat(5.2f); b.Faller(g3 - 1.6f); b.Dart(g3 + 1.6f);
            b.FakeFloor(2f);
            float g4 = b.Plat(4.8f); b.Surprise(g4 - 1.3f); b.Saw(g4 + 1.3f);
            b.Gap(2.8f);
            float cp2 = b.Plat(4f); b.Checkpoint(cp2);            // CHECKPOINT — far again

            // ---- Phase 3: the throne ----
            b.Gap(2.9f);
            float t1 = b.Plat(5.4f); b.ArrowRain(t1 - 1.7f); b.Reverse(t1); b.ArrowRain(t1 + 1.7f);
            b.Gap(2.9f);
            float t2 = b.Plat(4.8f); b.GrowSpike(t2 - 1.3f); b.Dart(t2 + 1.3f);
            b.FakeFloor(2f);
            float t3 = b.Plat(4.8f); b.Surprise(t3 - 1.3f); b.LateSpike(t3 + 1.3f);
            b.Gap(2.7f);
            float t4 = b.Plat(4.2f); b.Saw(t4);
            return b.Finish();                                    // the coffin: escape at last
        }
    }
}
