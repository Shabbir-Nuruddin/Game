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
        // Roomed floors are PRECISION platforming: no bat glide and no double-jump.
        // The sealed chambers suppress the vampire's supernatural tricks — which is
        // also the only way the "impossible" dark-bridge gaps and the coffin chase
        // stay unbeatable-by-flying (glide clears 12u, a double-jump 9.75u; a plain
        // jump ~5.5). Set automatically when a level uses Room().
        public bool PrecisionPlatforming = false;
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
        // Vertically-oscillating stone platforms: (x, centerY, amplitude, width).
        // "Sometimes the floor below you moved" — the ride across the unjumpable
        // gap, and the timing puzzle under a descending press.
        public List<Vector4> Movers = new();
        // Gravity runes: (x, y, dudFlag, unused). Cross one and you fall toward
        // the ceiling — the Chapel's physics-breaking mechanic. y places it on
        // the floor (negative) or the ceiling (positive); a dud looks identical
        // but fizzles dead on touch.
        public List<Vector4> GravRunes = new();
        // True on any floor with gravity runes: GameRoot adds the mirrored kill
        // plane ("the sky") above the rooms, since an inverted fall through an
        // open ceiling must kill exactly like a pit does.
        public bool HasGravity = false;
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
        bool _openCeiling;   // Chapel rooms: skip the auto ceiling, it's hand-laid

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
            L.PrecisionPlatforming = true;   // any roomed floor = no glide, no double-jump
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
                if (!_openCeiling)
                    L.Platforms.Add(new Rect2(_roomStart + w / 2f, CeilY, w, 0.6f));
            }
            _inRoom = false;
            _openCeiling = false;
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
        /// the lit gap must be genuinely UN-jumpable, so the player has to trust
        /// the dark and walk the spectral bridge. JumpArcProbe: a running jump
        /// clears ~5.5u (base) and 6.55u (best skin) even with glide/double-jump
        /// suppressed on these floors — so ghost spans must be >= ~7 (the earlier
        /// ">3.2" was flat wrong; 3.4 floors were trivially jumped in the light).
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

        // ---- Gravity (the Chapel) ----
        /// <summary>A floor rune that flips gravity: cross it and fall UP to the ceiling.</summary>
        public void GravRune(float x) { L.GravRunes.Add(new Vector4(x, -2.5f, 0f, 0f)); L.HasGravity = true; }
        /// <summary>A rune on the ceiling's underside — the way back DOWN for a ceiling-walker.</summary>
        public void CeilRune(float x) { L.GravRunes.Add(new Vector4(x, CeilY - 0.75f, 0f, 0f)); L.HasGravity = true; }
        /// <summary>Pixel-identical to GravRune, but dead: it fizzles on touch. The Chapel's lie.</summary>
        public void DudRune(float x) { L.GravRunes.Add(new Vector4(x, -2.5f, 1f, 0f)); L.HasGravity = true; }
        /// <summary>A live gravity rune at an explicit height (for runes on ledges).</summary>
        public void GravRuneAt(float x, float y) { L.GravRunes.Add(new Vector4(x, y, 0f, 0f)); L.HasGravity = true; }
        /// <summary>A small raised ledge at an explicit spot (doesn't advance the cursor).</summary>
        public void Ledge(float x, float y, float w) => L.Platforms.Add(new Rect2(x, y, w, 0.4f));

        /// <summary>
        /// This room's auto-ceiling (CloseRoom) is suppressed; lay ceiling
        /// segments by hand with CeilSlab. The holes are the inverted-mode
        /// hazard: fall UP through one and the sky kills you like a pit.
        /// </summary>
        public void OpenCeiling() { _openCeiling = true; L.HasGravity = true; }
        /// <summary>A hand-laid ceiling segment from x0 to x1 (used with OpenCeiling).</summary>
        public void CeilSlab(float x0, float x1) =>
            L.Platforms.Add(new Rect2((x0 + x1) / 2f, CeilY, Mathf.Max(0.5f, x1 - x0), 0.6f));

        /// <summary>
        /// An UNJUMPABLE gap crossed by riding a vertically-bobbing stone slab.
        /// The slab (3 wide) sits mid-gap oscillating ±amp around floor level:
        /// jump on, ride, jump off. Keep gapW ≥ 6.6 so the ride is mandatory
        /// (max plain jump 5.55; best skin 6.55).
        /// </summary>
        public void MoverGap(float gapW, float amp = 1.2f)
        {
            L.Movers.Add(new Vector4(cur + gapW / 2f, -3f, amp, 3f));
            cur += gapW;
        }

        /// <summary>A one-way portal pad at (ax,ay) that drops you at (bx,by).</summary>
        public void PortalAt(float ax, float ay, float bx, float by) =>
            L.Portals.Add(new PortalPair(ax, ay, bx, by));

        /// <summary>Place the real exit yourself (for Flee finales — see FinishBare).</summary>
        public void ExitAt(float x) => T(TrapType.RealExit, x, -2f, 1.4f, 1.8f);

        /// <summary>
        /// The bright pink DOOR that kills you — the v1 lie, back for the deep
        /// castle. Doors are exits in every other game; here only coffins are.
        /// </summary>
        public void FakeDoor(float x) => T(TrapType.FakeExit, x, -2f, 1.7f, 2.1f);

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
                case 18: return L19(); case 20: return L21();
                case 21: return L22(); case 22: return L23(); case 23: return L24();
                case 24: return L25(); case 25: return L26(); case 26: return L27();
                case 27: return L28(); case 28: return L29();
                case 30: return L31(); case 31: return L32(); case 32: return L33();
                case 33: return L34(); case 34: return L35(); case 35: return L36();
                case 36: return L37(); case 37: return L38(); case 38: return L39();
                // Floors 20/30/40 (indices 19/29/39) are boss arenas. GameRoot's
                // Curated path already routes them via BossTierForFloor before
                // ever calling Get(); Get() returns the arena directly too so any
                // other caller stays consistent (no dead hand-built levels).
                case 19: return BossRoom(2);
                case 29: return BossRoom(3);
                default:  return BossRoom(4);   // index 39
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
        // FLOORS 1–10 — TRUE Level Devil structure (playtest-corrected):
        // a stage is ONE FULL SCREEN. You see the whole thing the moment you
        // enter — every saw, every gap, the door — and several mechanics run at
        // once inside that one picture. 5 stages per floor, ~20-27 units each,
        // camera static per stage. Every stage's first 3 units are CLEAN (no
        // hazards) because that's where you respawn. One floor identity colours
        // its stages; classic platforming density everywhere.
        // Trees Hate You rule: every death is a punchline — setup, false
        // confidence, reveal. Never two new ideas at once across floors.
        // ====================================================================

        // 1 — TRUST NOTHING. Fake floors, late spikes, a chandelier over the
        // landing, a falling block, a bobbing slab over a pit, and a biting
        // door — four directions of death across five full screens.
        static Level L1()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: gaps + a spike + the first lying floor
            b.Plat(4f); b.Gap(2.3f);
            float a1 = b.Plat(5f); b.Spike(a1 + 1.5f);
            b.FakeFloor(2.2f);
            float c1 = b.Plat(6f); b.LateSpike(c1 + 1f);

            b.Room(RoomRule.None);              // S2: saw + chandelier over the landing + fake
            b.Plat(3.5f);
            float a2 = b.Plat(4f); b.Saw(a2);
            b.Gap(2.4f);
            float c2 = b.Plat(5f); b.Chandelier(c2 - 1.4f); b.Spike(c2 + 1.6f);
            b.FakeFloor(2f); b.Plat(4f);

            b.Room(RoomRule.None);              // S3: falling block + dart lane + lying floor
            b.Plat(3.5f);
            float a3 = b.Plat(5f); b.Faller(a3 - 1f); b.Dart(a3 + 1.6f);
            b.Gap(2.3f); b.FakeFloor(2.2f);
            float c3 = b.Plat(4f); b.Spike(c3);
            b.Gap(2.4f); b.Plat(3f);

            b.Room(RoomRule.None);              // S4: ride the bobbing slab across the pit
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a4 = b.Plat(5f); b.Spike(a4 - 1.3f); b.Spike(a4 + 1.3f);
            b.Gap(2.3f);
            float c4 = b.Plat(4f); b.Chandelier(c4);
            b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the door bites, then the gauntlet
            b.Plat(3.5f); b.FakeFloor(2f);
            float a5 = b.Plat(4f); b.Saw(a5);
            b.Gap(2.3f);
            float c5 = b.Plat(4f); b.Faller(c5 - 1f); b.LateSpike(c5 + 1.2f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 2 — THE CANDLES GO OUT. Night floors vanish and spikes RELOCATE while
        // the lights are out — with saws and fallers running at the same time.
        // Triggers fire before the first night floor so the lie lands mid-run.
        static Level L2()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.28f);       // S1: the floor you watched stops existing
            b.Plat(6f); b.NightFloor(2.2f);
            float a1 = b.Plat(5f); b.Spike(a1 + 1.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.2f);        // S2: saw + a spike that moves in the dark
            b.Plat(4f);
            float a2 = b.Plat(7f); b.Saw(a2 - 2f); b.ShiftSpike(a2 + 2.4f, a2 + 0.6f);
            b.Gap(2.3f); b.NightFloor(2f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // S3: two vanishing floors + a falling block
            b.Plat(3.5f); b.NightFloor(2f);
            float a3 = b.Plat(4f); b.Faller(a3);
            b.NightFloor(2.2f);
            float c3 = b.Plat(5f); b.Spike(c3 + 1.5f);
            b.Gap(2.3f); b.Plat(3f);

            b.Room(RoomRule.None);              // S4: lights stay on — the relief before S5
            b.Plat(3.5f); b.Gap(2.4f);
            float a4 = b.Plat(4f); b.Saw(a4);
            b.Gap(2.4f);
            float c4 = b.Plat(5f); b.LateSpike(c4);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.13f);       // S5: everything at once, in the dark
            b.Plat(3.5f); b.NightFloor(2.2f);
            float a5 = b.Plat(4f); b.ShiftSpike(a5 + 1.2f, a5 + 0.5f);
            b.Gap(2.3f);
            float c5 = b.Plat(4f); b.Faller(c5);
            b.NightFloor(2f); b.Plat(2f);
            return b.Finish();
        }

        // 3 — THE COFFIN FLEES. Four dense classic screens, then the chase: the
        // coffin waits one step ahead of the spawn and bolts across a pulsing
        // puddle, a dart lane and spikes until the end wall corners it.
        static Level L3()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: dart + twin spikes + lying floor
            b.Plat(4f);
            float a1 = b.Plat(5f); b.Dart(a1);
            b.Gap(2.3f);
            float c1 = b.Plat(4f); b.Spike(c1 - 1f); b.Spike(c1 + 1f);
            b.FakeFloor(2f); b.Plat(3.5f);

            b.Room(RoomRule.None);              // S2: the bobbing slab, then a saw, then acid
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a2 = b.Plat(5f); b.Saw(a2);
            b.Gap(2.3f);
            float c2 = b.Plat(4f); b.HolyWater(c2);
            b.Plat(2.5f);

            b.Room(RoomRule.None);              // S3: pendulum + lying floor + dart
            b.Plat(3.5f);
            float a3 = b.Plat(5f); b.Pendulum(a3);
            b.Gap(2.4f); b.FakeFloor(2.2f);
            float c3 = b.Plat(5f); b.Dart(c3 + 1f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: faller + saw/spike pinch
            b.Plat(3.5f); b.Gap(2.3f);
            float a4 = b.Plat(4f); b.Faller(a4);
            b.Gap(2.4f);
            float c4 = b.Plat(5f); b.Saw(c4 - 1.2f); b.Spike(c4 + 1.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: THE CHASE (gate slams behind you)
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(5f); b.Spike(a5 + 1f);
            b.Gap(2.3f);
            float c5 = b.Plat(9f); b.HolyWater(c5 - 2.5f); b.Dart(c5 + 1f); b.Spike(c5 + 3f);
            b.Plat(5f);
            b.ExitAt(p5 + 0.6f);   // one step ahead of you. it knows.
            return b.FinishBare();
        }

        // 4 — THE CRYPT PRESS. The whole ceiling descends over full screens of
        // hazards; S4 makes you ride the bobbing slab UNDER the press cycle.
        static Level L4()
        {
            var b = new B();
            b.Room(RoomRule.Press, 0.3f);       // S1: one long run — just don't stop
            b.Plat(4f);
            float a1 = b.Plat(12f); b.Spike(a1 + 2f);
            b.Plat(4f);

            b.Room(RoomRule.Press, 0.25f);      // S2: press + lying floor + saw
            b.Plat(3.5f); b.FakeFloor(2.2f);
            float a2 = b.Plat(5f); b.Spike(a2);
            b.Gap(2.3f);
            float c2 = b.Plat(5f); b.Saw(c2);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S3: open sky — chandelier + late spike
            b.Plat(3.5f); b.Gap(2.4f);
            float a3 = b.Plat(4f); b.Chandelier(a3);
            b.Gap(2.4f);
            float c3 = b.Plat(4f); b.LateSpike(c3);
            b.Plat(3.5f);

            b.Room(RoomRule.Press, 0.22f);      // S4: ride the slab while the ceiling drops
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a4 = b.Plat(5f); b.HolyWater(a4);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.Press, 0.2f, true); // S5: gate, press, two lying floors, saw
            b.Plat(3.5f); b.FakeFloor(2f);
            float a5 = b.Plat(4f); b.Spike(a5);
            b.FakeFloor(2f);
            float c5 = b.Plat(4f); b.Saw(c5);
            b.Plat(2.5f);
            return b.Finish();
        }

        // 5 — THE LULLABY. Sleep runes on full screens: nap under a bat, nap
        // under the press, or thread a rune field with a saw running.
        static Level L5()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: one rune, one spike — learn the nap
            b.Plat(4f);
            float a1 = b.Plat(5f); b.SleepRune(a1);
            b.Gap(2.3f);
            float c1 = b.Plat(5f); b.Spike(c1 + 1.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S2: nap here and the bat has dinner
            b.Plat(3.5f);
            float a2 = b.Plat(6f); b.SleepRune(a2 - 1f); b.Bat(a2 - 0.5f);
            b.Gap(2.3f);
            float c2 = b.Plat(5f); b.Saw(c2);
            b.Plat(3.5f);

            b.Room(RoomRule.Press, 0.25f);      // S3: the thesis — a rune under the press
            b.Plat(3.5f);
            float a3 = b.Plat(7f); b.SleepRune(a3 - 1.5f); b.Spike(a3 + 2f);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.HolyWater(c3);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: the rune field + a second bat
            b.Plat(4f);
            float a4 = b.Plat(9f); b.SleepRune(a4 - 3f); b.SleepRune(a4 - 0.5f);
            b.SleepRune(a4 + 2f); b.Bat(a4 + 0.3f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.Press, 0.3f, true); // S5: runes + saw under the press
            b.Plat(3.5f);
            float a5 = b.Plat(8f); b.SleepRune(a5 - 2.4f); b.SleepRune(a5 + 0.2f); b.Saw(a5 + 2.6f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 6 — THE CURSED HAND. Controls flip mid-screen and stay flipped for
        // the rest of the stage: reversed gaps, a reversed dart dodge, twin
        // pendulums on the honest screen so your eyes never rest.
        static Level L6()
        {
            var b = new B();
            b.Room(RoomRule.Reverse, 0.3f);     // S1: the moonwalk teach + one spike
            b.Plat(5f);
            float a1 = b.Plat(8f); b.Spike(a1 + 2.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Reverse, 0.25f);    // S2: two gaps, backwards
            b.Plat(3.5f); b.Gap(2.3f); b.Plat(3f); b.Gap(2.4f);
            float a2 = b.Plat(5f); b.Spike(a2);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: honest hands, dishonest ceiling
            b.Plat(3.5f);
            float a3 = b.Plat(6f); b.Pendulum(a3 - 1.5f); b.Pendulum(a3 + 1.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.Dart(c3);
            b.Plat(3f);

            b.Room(RoomRule.Reverse, 0.2f);     // S4: dodge a dart with flipped hands
            b.Plat(3.5f);
            float a4 = b.Plat(5f); b.Dart(a4);
            b.FakeFloor(2.2f);
            float c4 = b.Plat(5f); b.Spike(c4 - 1.2f); b.Spike(c4 + 1.2f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.Reverse, 0.15f, true); // S5: the reversed gauntlet
            b.Plat(3.5f);
            float a5 = b.Plat(4f); b.Saw(a5);
            b.Gap(2.4f);
            float c5 = b.Plat(4f); b.Pendulum(c5);
            b.FakeFloor(2f); b.Plat(2f);
            return b.Finish();
        }

        // 7 — FAITH IN THE DARK. Spectral bridges (7.2u — unjumpable lit, best
        // skin clears 6.55) that only exist once the candles die, mixed with
        // vanishing floors, fallers and a shore that rearranges.
        static Level L7()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: honest screen — saw + gaps
            b.Plat(3.5f); b.Gap(2.2f);
            float a1 = b.Plat(5f); b.Saw(a1);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Dark, 0.15f);       // S2: the first bridge of faith
            b.Plat(4f); b.GhostFloor(7.2f);
            float a2 = b.Plat(6f); b.Spike(a2 + 1.8f);
            b.Gap(2.3f); b.Plat(3f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: dark deletes one floor, builds another
            b.Plat(3.5f); b.NightFloor(2f);
            float a3 = b.Plat(3f); b.Faller(a3);
            b.GhostFloor(7.2f);
            float c3 = b.Plat(4f); b.ShiftSpike(c3 + 1.2f, c3 + 0.4f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: chandelier + spike in honest light
            b.Plat(3.5f); b.Gap(2.4f);
            float a4 = b.Plat(5f); b.Chandelier(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(4f); b.Spike(c4);
            b.Plat(3f);

            b.Room(RoomRule.Dark, 0.12f);       // S5: bridge, spike, vanishing floor, coffin
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a5 = b.Plat(3f); b.Spike(a5);
            b.NightFloor(2f); b.Plat(2f);
            return b.Finish();
        }

        // 8 — THE ENDLESS HALL. Doorway runes silently loop you back across a
        // full screen you can SEE all of — that's the gaslight. Grow-spike
        // clocks and darts run while you time the rune jump; the hall gives up
        // after five loops so nobody is stuck forever.
        static Level L8()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: honest density
            b.Plat(4f);
            float a1 = b.Plat(5f); b.Saw(a1);
            b.Gap(2.3f);
            float c1 = b.Plat(5f); b.Spike(c1 + 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.Loop);              // S2: first loop + a grow-spike clock
            b.Plat(4f);
            float a2 = b.Plat(8f); b.GrowSpike(a2);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Loop);              // S3: loop a screen with real gaps
            b.Plat(3.5f); b.Gap(2.4f);
            float a3 = b.Plat(6f); b.Dart(a3);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Loop);              // S4: saw + grow spike + the rune jump
            b.Plat(3.5f);
            float a4 = b.Plat(7f); b.Saw(a4 - 1.5f); b.GrowSpike(a4 + 2f);
            b.Gap(2.3f); b.Plat(7f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the exit door bites; the runway lies
            b.Plat(3.5f); b.FakeFloor(2.2f);
            float a5 = b.Plat(5f); b.LateSpike(a5);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 9 — COFFIN ROULETTE. Dull-brass fakes among flame jets and acid, dark
        // screens where coffins loom out of the candlelight — and the one true
        // glowing coffin flees through its brothers when you reach for it.
        static Level L9()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the tell, taught cheaply
            b.Plat(4f);
            float a1 = b.Plat(6f); b.FakeCoffin(a1 - 1f);
            b.Gap(2.3f);
            float c1 = b.Plat(5f); b.Spike(c1 + 1.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S2: fire guards a coffin that's ALSO lying
            b.Plat(3.5f);
            float a2 = b.Plat(6f); b.FlameJet(a2 - 1.5f); b.FakeCoffin(a2 + 1.5f);
            b.Gap(2.3f);
            float c2 = b.Plat(5f); b.HolyWater(c2);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.16f);       // S3: coffins loom out of the dark, saw runs
            b.Plat(4f);
            float a3 = b.Plat(9f); b.FakeCoffin(a3 - 2.5f); b.FakeCoffin(a3 + 1f); b.Saw(a3 + 3.2f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.None);              // S4: lying floor into the coffin-and-fire pinch
            b.Plat(3.5f); b.FakeFloor(2f);
            float a4 = b.Plat(5f); b.FakeCoffin(a4 + 0.7f); b.FlameJet(a4 - 1.3f);
            b.Gap(2.4f);
            float c4 = b.Plat(4f); b.Dart(c4);
            b.Plat(3.5f);

            b.Room(RoomRule.Flee, 0.05f);       // S5: the slalom chase through the fakes
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(12f); b.FakeCoffin(a5 - 3.5f); b.FakeCoffin(a5 - 0.5f);
            b.FakeCoffin(a5 + 2.5f); b.HolyWater(a5 + 4.5f);
            b.Plat(5f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }

        // 10 — THE FINAL EXAM. Five screens, every lie in the castle — plus the
        // portal room: two pads, one crosses the impossible gap, one shuttles
        // you back to the start. No boss; the exam IS the boss.
        static Level L10()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.16f);       // S1: night floor + moving spike + saw
            b.Plat(3.5f); b.NightFloor(2f);
            float a1 = b.Plat(6f); b.Saw(a1 - 1.5f); b.ShiftSpike(a1 + 2f, a1 + 0.8f);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Press, 0.2f);       // S2: press over acid and a lying floor
            b.Plat(3.5f);
            float a2 = b.Plat(5f); b.HolyWater(a2);
            b.FakeFloor(2.2f);
            float c2 = b.Plat(5f); b.Spike(c2 + 1f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: THE PORTAL ROOM — pick a door
            float p3 = b.Plat(7f);
            b.Gap(7.5f);                        // unjumpable: a portal is the only way over
            float q3 = b.Plat(10f); b.GrowSpike(q3 - 1f); b.Saw(q3 + 2f);
            // Pads sit clear of the spawn zone, with a jumpable gap between them
            // so you can reach the far pad without touching the near one.
            b.PortalAt(p3 + 2f, -2f, q3 - 3.5f, -2f);     // the RIGHT door
            b.PortalAt(p3 - 0.5f, -2f, p3 - 2.9f, -2f);   // the joke door (back you go)

            b.Room(RoomRule.Reverse, 0.2f);     // S4: rune + pendulum with flipped hands
            b.Plat(3.5f);
            float a4 = b.Plat(5f); b.SleepRune(a4);
            b.FakeFloor(2.2f);
            float c4 = b.Plat(5f); b.Pendulum(c4);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: the last chase, gate slammed behind
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(6f); b.Dart(a5 + 1f);
            b.Gap(2.3f);
            float c5 = b.Plat(9f); b.Spike(c5 - 2f); b.GrowSpike(c5 + 1f); b.Spike(c5 + 3f);
            b.Plat(4f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }

        // 11 — THE CHAPEL INVERTS. The first floor after the exam breaks the last
        // rule left standing: gravity. Spectral runes flip you onto the ceiling;
        // every unjumpable gap on this floor is crossed upside-down. Stage story:
        // S1 teaches the flip, S2 opens the sky (fall up = die), S3 puts a rune
        // ON your ceiling path that must be jumped, S4 lies with a dead rune,
        // S5 is the full inverted exam. (Replaced the old bat corridor — a
        // corridor floor had no business following the floor-10 exam anyway.)
        static Level L11()
        {
            var b = new B();

            b.Room(RoomRule.None);              // S1: the rune, the gap, the ceiling walk
            float a1 = b.Plat(6f); b.GravRune(a1 + 1.5f);
            b.Gap(8f);                          // unjumpable — the ceiling is the road
            float c1 = b.Plat(8f); b.CeilRune(c1 - 2.5f); b.Spike(c1 + 1f);

            b.Room(RoomRule.None);              // S2: the ceiling has a hole; the sky is a pit
            b.OpenCeiling();
            float a2 = b.Plat(5.5f); b.GravRune(a2 + 1.2f);
            float g2 = a2 + 2.75f;              // right edge of the start platform
            b.Gap(9f);
            float c2 = b.Plat(9.5f); b.CeilRune(c2 - 3.95f); b.Saw(c2 + 0.5f); b.Spike(c2 + 2.5f);
            // Hand-laid ceiling: a slab, a 2.2 hole you must jump while inverted
            // (walk off the edge and you fall UP into the sky), then the slab
            // that carries you to the drop rune. Runs out just past that rune,
            // so skipping the drop is also the sky.
            b.CeilSlab(g2 - 5.5f, g2 + 2f);
            b.CeilSlab(g2 + 4.2f, g2 + 10.5f);

            b.Room(RoomRule.None);              // S3: a rune ON the ceiling road — jump it or drop into the pit
            float a3 = b.Plat(5.5f); b.GravRune(a3 + 1.2f);
            float g3 = a3 + 2.75f;              // gap start: everything below the crossing is pit
            b.Gap(8f);
            b.CeilRune(g3 + 4f);                // mid-pit: touching it drops you into the void
            float c3 = b.Plat(8f); b.CeilRune(c3 - 2.5f); b.Spike(c3 + 1.5f);
            b.Plat(2.5f);

            b.Room(RoomRule.None);              // S4: the dead rune. The real one is behind you, on a ledge.
            float a4 = b.Plat(9f);
            b.Ledge(a4 - 1.3f, -1f, 2f); b.GravRuneAt(a4 - 1.3f, -0.45f);
            b.DudRune(a4 + 3.1f);               // sits right at the lip of the gap, glowing its lie
            b.Gap(8f);
            float c4 = b.Plat(6f); b.CeilRune(c4 - 2.3f); b.Spike(c4 + 1.5f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the inverted exam, behind a biting gate
            b.OpenCeiling();
            float a5 = b.Plat(4.5f); b.GravRune(a5 + 1.2f);
            float g5 = a5 + 2.25f;
            b.Gap(7.5f);
            b.CeilRune(g5 + 6.3f);              // the jump-this rune, mid-crossing
            float c5 = b.Plat(6f); b.CeilRune(c5 - 2.2f); b.LateSpike(c5 + 1f);
            // Slab, hole, slab: the hole comes FIRST this time, then the rune —
            // two different inverted jumps back to back before the drop.
            b.CeilSlab(g5 - 4.5f, g5 + 1.5f);
            b.CeilSlab(g5 + 3.7f, g5 + 9f);
            return b.Finish();
        }

        // ====================================================================
        // WORLD 2 (floors 12-19) — "the castle stops teaching". Every rule from
        // world 1 returns meaner and starts pairing up; springs, sunbeams,
        // arrow timers and crushers join the vocabulary. Full-screen stages,
        // entry-clean, exam at 19. Floor 11 (the Chapel Inverts) opens the world.
        // ====================================================================

        // 12 — THE DARK RETURNS. World 1's dark, without the training wheels:
        // ghost bridges and vanishing floors in the same stage, spikes that
        // relocate, and things that move on their own while you can't see.
        static Level L12()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.28f);       // S1: it starts lying immediately
            b.Plat(5.5f); b.NightFloor(2.2f);
            float a1 = b.Plat(5f); b.Saw(a1);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.18f);       // S2: faller + moving spike, unlit
            b.Plat(4f);
            float a2 = b.Plat(7f); b.Faller(a2 - 2f); b.ShiftSpike(a2 + 2.2f, a2 + 0.4f);
            b.Gap(2.3f); b.NightFloor(2f); b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: a ghost bridge with a bat on the far shore
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a3 = b.Plat(5f); b.Bat(a3);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None);              // S4: lit relief with teeth
            b.Plat(4f); b.Gap(2.4f);
            float a4 = b.Plat(6f); b.Saw(a4 - 1.3f); b.Spike(a4 + 1.5f);
            b.FakeFloor(2f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.12f, true); // S5: night floor, moving spike, ghost bridge out
            b.Plat(3.5f); b.NightFloor(2f);
            float a5 = b.Plat(4f); b.ShiftSpike(a5 + 1.3f, a5 + 0.5f);
            b.GhostFloor(7.2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 13 — SPRING LOADED. Launch pads that throw you into hidden sunbeams:
        // the most inviting mushroom in the castle is the one that kills you.
        static Level L13()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the first pad, right past a landing
            b.Plat(4.5f); b.Gap(2.3f);
            float a1 = b.Plat(7f); b.Spring(a1 + 0.5f); b.Spike(a1 + 2.4f);
            b.Plat(5f);

            b.Room(RoomRule.None);              // S2: pad between a faller and a chandelier
            b.Plat(3.5f);
            float a2 = b.Plat(5f); b.Faller(a2 - 1f); b.Spring(a2 + 1.4f);
            b.Gap(2.4f);
            float c2 = b.Plat(5f); b.Chandelier(c2);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: ride the slab, dodge the pad
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a3 = b.Plat(5f); b.Spring(a3 - 1f); b.Dart(a3 + 1.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Press, 0.22f);      // S4: sprint the press lane, thread the pads
            b.Plat(3.5f);
            float a4 = b.Plat(9f); b.Spring(a4 - 2.5f); b.Spike(a4); b.Spring(a4 + 2.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None, 0.35f, true); // S5: gate, saw, pad, faller
            b.Plat(3.5f); b.FakeFloor(2f);
            float a5 = b.Plat(5f); b.Saw(a5 - 1.5f); b.Spring(a5 + 1f);
            b.Gap(2.3f);
            float c5 = b.Plat(4f); b.Faller(c5);
            b.Plat(2.5f);
            return b.Finish();
        }

        // 14 — THE SUN LIES. Patches of daylight burn the undead — invisible
        // ground that kills, always placed exactly where relief should be.
        static Level L14()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the first sunbeam, after a calm gap
            b.Plat(4.5f); b.Gap(2.3f);
            float a1 = b.Plat(7f); b.Surprise(a1 - 0.8f); b.Spike(a1 + 1.8f);
            b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: pendulum, lying floor, then the sun
            b.Plat(3.5f);
            float a2 = b.Plat(5f); b.Pendulum(a2);
            b.FakeFloor(2.2f);
            float c2 = b.Plat(5f); b.Surprise(c2 + 0.6f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: chandelier + sun + a diving bat
            b.Plat(3.5f); b.Gap(2.4f);
            float a3 = b.Plat(5f); b.Chandelier(a3 - 1f); b.Surprise(a3 + 1.4f);
            b.Gap(2.3f);
            float c3 = b.Plat(4f); b.Bat(c3);
            b.Plat(3f);

            b.Room(RoomRule.Dark, 0.2f);        // S4: sunbeams you REALLY can't see now
            b.Plat(4f); b.NightFloor(2f);
            float a4 = b.Plat(6f); b.Pendulum(a4 - 1.5f); b.Surprise(a4 + 1.7f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the sun guards the coffin road
            b.Plat(3.5f); b.FakeFloor(2f);
            float a5 = b.Plat(6f); b.Surprise(a5 - 1.5f); b.Pendulum(a5 + 0.8f);
            b.Gap(2.3f);
            float c5 = b.Plat(4f); b.LateSpike(c5);
            b.Plat(2.5f);
            return b.Finish();
        }

        // 15 — ARROW CHOIR. Ceiling timers rain bolts on a beat; crushers force
        // you LOW while everything else wants you jumping.
        static Level L15()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: learn the rain's rhythm
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.ArrowRain(a1 - 1.5f); b.ArrowRain(a1 + 1.5f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: crusher lane, then rain + spike
            b.Plat(3.5f); b.Gap(2.3f);
            float a2 = b.Plat(4f); b.Crusher(a2);
            b.Gap(2.4f);
            float c2 = b.Plat(5f); b.ArrowRain(c2 - 1f); b.Spike(c2 + 1.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: rain-saw-rain corridor
            b.Plat(3.5f);
            float a3 = b.Plat(7f); b.ArrowRain(a3 - 2.2f); b.Saw(a3); b.ArrowRain(a3 + 2.2f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None);              // S4: ride the slab into the rain
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a4 = b.Plat(5f); b.ArrowRain(a4 - 1f); b.Spike(a4 + 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.None, 0.35f, true); // S5: crusher, double rain, lying runway
            b.Plat(3.5f);
            float a5 = b.Plat(4f); b.Crusher(a5);
            b.Gap(2.3f);
            float c5 = b.Plat(6f); b.ArrowRain(c5 - 1.5f); b.ArrowRain(c5 + 0.5f); b.Dart(c5 + 2.2f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 16 — THE LONG SLEEP. Sleep runes under arrow timers: nap on the wrong
        // tile and the choir turns you into a pincushion.
        static Level L16()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the thesis — a rune directly under the rain
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.SleepRune(a1 - 0.5f); b.ArrowRain(a1 - 0.5f); b.Spike(a1 + 2f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: rune + roosting bat + saw
            b.Plat(3.5f); b.Gap(2.4f);
            float a2 = b.Plat(6f); b.SleepRune(a2 - 1.5f); b.Bat(a2 - 1f);
            b.Gap(2.3f);
            float c2 = b.Plat(4f); b.Saw(c2);
            b.Plat(3f);

            b.Room(RoomRule.Press, 0.22f);      // S3: runes + acid under the descending crypt
            b.Plat(4f);
            float a3 = b.Plat(7f); b.SleepRune(a3 - 2f); b.SleepRune(a3 + 0.5f); b.HolyWater(a3 + 2.5f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.Dark, 0.2f);        // S4: runes you can barely see
            b.Plat(4f); b.NightFloor(2f);
            float a4 = b.Plat(7f); b.SleepRune(a4 - 1f); b.ArrowRain(a4 - 1f); b.Spike(a4 + 2f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the dormitory — three runes, one choir
            b.Plat(3.5f);
            float a5 = b.Plat(9f); b.SleepRune(a5 - 3f); b.Bat(a5 - 2.6f);
            b.SleepRune(a5); b.ArrowRain(a5); b.SleepRune(a5 + 2.6f);
            b.Gap(2.3f); b.Plat(2.5f);
            return b.Finish();
        }

        // 17 — FIRE SERMON. Flame jets and holy water in overlapping rhythms;
        // one crossing is made from a bobbing slab over the flames.
        static Level L17()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: two jets, one beat
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.FlameJet(a1 - 1.5f); b.FlameJet(a1 + 1.5f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: acid + fire + a diving bat
            b.Plat(3.5f);
            float a2 = b.Plat(6f); b.HolyWater(a2 - 1.5f); b.FlameJet(a2 + 1f);
            b.Gap(2.4f);
            float c2 = b.Plat(4f); b.Bat(c2);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: the ferry over the fire pit
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a3 = b.Plat(5f); b.FlameJet(a3 - 1f); b.HolyWater(a3 + 1.5f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.Reverse, 0.2f);     // S4: flipped hands over the flame beat
            b.Plat(3.5f);
            float a4 = b.Plat(7f); b.FlameJet(a4 - 1.5f); b.FlameJet(a4 + 1.5f);
            b.Gap(2.3f);
            float c4 = b.Plat(4f); b.HolyWater(c4);
            b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the sermon's crescendo
            b.Plat(3.5f);
            float a5 = b.Plat(5f); b.FlameJet(a5);
            b.FakeFloor(2.2f);
            float c5 = b.Plat(5f); b.HolyWater(c5 - 1f); b.FlameJet(c5 + 1.5f);
            b.Plat(2.5f);
            return b.Finish();
        }

        // 18 — GRAVEYARD SHIFT. The roulette's coffins come back mid-world,
        // guarded by fire, hidden in the dark, once across a ghost bridge.
        static Level L18()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: remember the tell (dull cross = lie)
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.FakeCoffin(a1 - 1f); b.Spike(a1 + 1.8f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.16f);       // S2: coffins loom + a saw in the dark
            b.Plat(4f);
            float a2 = b.Plat(8f); b.FakeCoffin(a2 - 2f); b.FakeCoffin(a2 + 1f); b.Saw(a2 + 3f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.13f);       // S3: a ghost bridge to a coffin and fire
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a3 = b.Plat(6f); b.FakeCoffin(a3 - 1f); b.FlameJet(a3 + 1.2f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: fire + coffin + dart, lit
            b.Plat(3.5f); b.FakeFloor(2f);
            float a4 = b.Plat(5f); b.FlameJet(a4 - 1.2f); b.FakeCoffin(a4 + 1f);
            b.Gap(2.4f);
            float c4 = b.Plat(4f); b.Dart(c4);
            b.Plat(3f);

            b.Room(RoomRule.Flee, 0.05f);       // S5: the chase through the graveyard
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(11f); b.FakeCoffin(a5 - 3f); b.FakeCoffin(a5);
            b.FlameJet(a5 + 2f); b.FakeCoffin(a5 + 4f);
            b.Plat(5f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }

        // 19 — WORLD EXAM II. Everything world 2 taught, plus a gravity crossing
        // (floor 11's lesson) and a portal choice, ending in a gated chase.
        static Level L19()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.16f);       // S1: dark + rain + moving spike
            b.Plat(3.5f); b.NightFloor(2f);
            float a1 = b.Plat(6f); b.ArrowRain(a1 - 1f); b.ShiftSpike(a1 + 2f, a1 + 0.6f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.None);              // S2: the ceiling road (remember the Chapel?)
            float a2 = b.Plat(6f); b.GravRune(a2 + 1.5f);
            b.Gap(8f);
            float c2 = b.Plat(8f); b.CeilRune(c2 - 2.5f); b.FlameJet(c2 + 1.5f);

            b.Room(RoomRule.Press, 0.2f);       // S3: spring + acid + rune under the press
            b.Plat(3.5f);
            float a3 = b.Plat(6f); b.Spring(a3 - 1f); b.HolyWater(a3 + 1.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.SleepRune(c3);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: pick a door
            float p4 = b.Plat(7f);
            b.Gap(7.5f);
            float q4 = b.Plat(9f); b.GrowSpike(q4 - 1f); b.Bat(q4 + 2f);
            b.PortalAt(p4 + 2f, -2f, q4 - 3f, -2f);
            b.PortalAt(p4 - 0.5f, -2f, p4 - 2.9f, -2f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: the gated chase
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(6f); b.FlameJet(a5 + 1f);
            b.Gap(2.3f);
            float c5 = b.Plat(9f); b.Spike(c5 - 2f); b.ArrowRain(c5); b.Spike(c5 + 2.5f);
            b.Plat(4f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }

        // ====================================================================
        // WORLD 3 (floors 21-29) — "blood rites". Pairings get cruel:
        // contradictory instincts (stay low vs keep moving), double ferry
        // rides, portal choices that subvert last world's answers, and the
        // pink DOOR lie returns. Floor 20 is the Countess; her level slot is
        // served by Levels.BossRoom via Get(), so no dead body lives here.
        // ====================================================================

        // 21 — THE CRUSHER COURT. Crushers demand you stay LOW; chandeliers and
        // fallers demand you keep moving. The floor argues with itself and you
        // pay for whichever instinct wins.
        static Level L21()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the argument, stated plainly
            b.Plat(4.5f); b.Gap(2.3f);
            float a1 = b.Plat(4f); b.Crusher(a1);
            b.Gap(2.3f);
            float c1 = b.Plat(5f); b.Chandelier(c1);
            b.Plat(4f);

            b.Room(RoomRule.None);              // S2: duck the block, dodge the sky
            b.Plat(3.5f);
            float a2 = b.Plat(4f); b.Crusher(a2);
            b.Gap(2.4f);
            float c2 = b.Plat(5f); b.Faller(c2 - 1f); b.Spike(c2 + 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.None);              // S3: crusher, lying floor, crusher
            b.Plat(3.5f); b.Gap(2.3f);
            float a3 = b.Plat(4f); b.Crusher(a3);
            b.FakeFloor(2.2f);
            float c3 = b.Plat(4f); b.Crusher(c3);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.18f);       // S4: the argument, in the dark
            b.Plat(4f); b.NightFloor(2f);
            float a4 = b.Plat(4f); b.Crusher(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(5f); b.Chandelier(c4 - 1f); b.Spike(c4 + 1.5f);
            b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: both instincts punished at once
            b.Plat(3.5f);
            float a5 = b.Plat(4f); b.Crusher(a5);
            b.Gap(2.4f);
            float c5 = b.Plat(6f); b.Saw(c5 - 1.5f); b.Chandelier(c5 + 1f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 22 — CANDLE MASSACRE. World 1's dark was a lesson; this is a purge.
        // Everything that CAN move in the dark does.
        static Level L22()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.25f);       // S1: saw + relocating spike + vanish
            b.Plat(5f); b.NightFloor(2.2f);
            float a1 = b.Plat(6f); b.Saw(a1 - 1.5f); b.ShiftSpike(a1 + 1.8f, a1 + 0.4f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // S2: ghost bridge to a faller ambush
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a2 = b.Plat(5f); b.Faller(a2 - 1f); b.ShiftSpike(a2 + 1.5f, a2 + 0.6f);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: two vanishing floors, a bat between
            b.Plat(3.5f); b.NightFloor(2f);
            float a3 = b.Plat(3.5f); b.Bat(a3);
            b.NightFloor(2f);
            float c3 = b.Plat(5f); b.Spike(c3 + 1.5f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: lit — but nothing here is calm
            b.Plat(3.5f); b.Gap(2.4f);
            float a4 = b.Plat(6f); b.Saw(a4 - 1.2f); b.Dart(a4 + 1.4f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.Dark, 0.12f, true); // S5: the massacre
            b.Plat(3.5f); b.NightFloor(2.2f);
            float a5 = b.Plat(4f); b.ShiftSpike(a5 + 1.2f, a5 + 0.5f);
            b.GhostFloor(7.2f);
            float c5 = b.Plat(3f); b.Faller(c5);
            return b.Finish();
        }

        // 23 — THE FERRYMAN. The bobbing slabs own this floor: double rides,
        // rides under the press, rides into the arrow choir.
        static Level L23()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: one calm ferry
            b.Plat(4.5f); b.MoverGap(6.8f);
            float a1 = b.Plat(6f); b.Spike(a1 + 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.None);              // S2: two ferries, acid between
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a2 = b.Plat(4f); b.HolyWater(a2);
            b.MoverGap(6.8f); b.Plat(3.5f);

            b.Room(RoomRule.Press, 0.2f);       // S3: the ferry under the descending crypt
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a3 = b.Plat(5f); b.FlameJet(a3);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None);              // S4: rain on the dock, then the ride
            b.Plat(3.5f); b.Gap(2.3f);
            float a4 = b.Plat(4f); b.ArrowRain(a4);
            b.MoverGap(7.2f);
            float c4 = b.Plat(4f); b.Spike(c4);
            b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: ferry into a saw/spike shore
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a5 = b.Plat(5f); b.Saw(a5 - 1f); b.Spike(a5 + 1.5f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 24 — PORTAL PANDEMONIUM. Every stage is a door choice — and the right
        // answer MOVES between stages. Learned "right side wins"? Stage 2 heard.
        static Level L24()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: right pad wins (like the exam taught)
            float p1 = b.Plat(7f);
            b.Gap(7.5f);
            float q1 = b.Plat(8f); b.Spike(q1 + 1f);
            b.PortalAt(p1 + 2f, -2f, q1 - 3f, -2f);
            b.PortalAt(p1 - 0.5f, -2f, p1 - 2.9f, -2f);

            b.Room(RoomRule.None);              // S2: SUBVERTED — now the LEFT pad crosses
            float p2 = b.Plat(7f);
            b.Gap(7.5f);
            float q2 = b.Plat(8f); b.Saw(q2 + 1.5f);
            b.PortalAt(p2 - 0.5f, -2f, q2 - 3f, -2f);
            b.PortalAt(p2 + 2f, -2f, p2 - 2.9f, -2f);

            b.Room(RoomRule.Dark, 0.15f);       // S3: choose your door by candlelight
            b.Plat(4f); b.NightFloor(2f);
            float p3 = b.Plat(5f);
            b.Gap(7.5f);
            float q3 = b.Plat(7f); b.Spike(q3 + 1.5f);
            b.PortalAt(p3 + 1f, -2f, q3 - 2.5f, -2f);
            b.PortalAt(p3 - 1f, -2f, p3 - 3f, -2f);

            b.Room(RoomRule.None);              // S4: the double hop over a grow-spike island
            float p4 = b.Plat(5f);
            b.Gap(6.8f);
            float m4 = b.Plat(3.5f); b.GrowSpike(m4);
            b.Gap(6.8f);
            float q4 = b.Plat(5f);
            b.PortalAt(p4 + 1.5f, -2f, m4 - 0.9f, -2f);
            b.PortalAt(m4 + 0.9f, -2f, q4 - 1.5f, -2f);

            b.Room(RoomRule.None, 0.35f, true); // S5: gated finale — pick fast, the door bites
            float p5 = b.Plat(6f);
            b.Gap(7.5f);
            float q5 = b.Plat(7f); b.Saw(q5 - 1f); b.Spike(q5 + 2f);
            b.PortalAt(p5 + 1.8f, -2f, q5 - 2.8f, -2f);
            b.PortalAt(p5 - 0.5f, -2f, p5 - 2.7f, -2f);
            return b.Finish();
        }

        // 25 — THE HUNGRY FLOOR. The ground itself is the enemy: fake floors,
        // night floors and launch pads, until you trust nothing you stand on.
        static Level L25()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: two bites out of the ground
            b.Plat(4.5f); b.FakeFloor(2.2f);
            float a1 = b.Plat(5f); b.Spike(a1 + 1.5f);
            b.FakeFloor(2f); b.Plat(4f);

            b.Room(RoomRule.None);              // S2: pad, lie, faller
            b.Plat(3.5f);
            float a2 = b.Plat(5f); b.Spring(a2 - 1f);
            b.FakeFloor(2.2f);
            float c2 = b.Plat(5f); b.Faller(c2);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.18f);       // S3: WHICH floor lies? (one vanishes, one collapses)
            b.Plat(4f); b.NightFloor(2f); b.FakeFloor(2f);
            float a3 = b.Plat(5f); b.Spike(a3 + 1.5f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None);              // S4: pad between two lies, dart to finish
            b.Plat(3.5f); b.FakeFloor(2f);
            float a4 = b.Plat(4f); b.Spring(a4 + 0.5f);
            b.FakeFloor(2.2f);
            float c4 = b.Plat(4f); b.Dart(c4);
            b.Gap(2.3f); b.Plat(3f);

            b.Room(RoomRule.Dark, 0.12f, true); // S5: the floor eats in the dark
            b.Plat(3.5f); b.NightFloor(2f); b.FakeFloor(2f);
            float a5 = b.Plat(5f); b.Spring(a5 - 1f); b.ShiftSpike(a5 + 1.8f, a5 + 0.6f);
            b.Plat(2.5f);
            return b.Finish();
        }

        // 26 — WAKE THE DEAD. The lullaby returns among fire and bats — every
        // nap spot has a different predator.
        static Level L26()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: rune by a flame jet
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.SleepRune(a1 - 1f); b.FlameJet(a1 + 1.2f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: the dormitory of bats
            b.Plat(4.5f);
            float a2 = b.Plat(7f); b.SleepRune(a2 - 2f); b.Bat(a2 - 1.6f);
            b.SleepRune(a2 + 1f); b.Bat(a2 + 1.4f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.Press, 0.2f);       // S3: runes + fire + acid under the press
            b.Plat(3.5f);
            float a3 = b.Plat(8f); b.SleepRune(a3 - 1.5f); b.FlameJet(a3 + 0.5f); b.HolyWater(a3 + 2.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Reverse, 0.18f);    // S4: flipped hands past the rune
            b.Plat(3.5f);
            float a4 = b.Plat(6f); b.SleepRune(a4); b.Dart(a4 + 2f);
            b.Gap(2.3f);
            float c4 = b.Plat(4f); b.FlameJet(c4);
            b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: sleep through THIS
            b.Plat(3.5f);
            float a5 = b.Plat(8f); b.SleepRune(a5 - 2.5f); b.Bat(a5 - 2.1f);
            b.FlameJet(a5); b.SleepRune(a5 + 2.2f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 27 — THE SWINGING GALLERY. Pendulums in choirs, saws on rails,
        // grow-spikes keeping time underneath.
        static Level L27()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the twin swing
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.Pendulum(a1 - 1.5f); b.Pendulum(a1 + 1.5f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: three metronomes
            b.Plat(4f);
            float a2 = b.Plat(7f); b.Pendulum(a2 - 2f); b.GrowSpike(a2); b.Pendulum(a2 + 2f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S3: saw + swing, then the clock
            b.Plat(3.5f); b.Gap(2.4f);
            float a3 = b.Plat(6f); b.Saw(a3 - 1.5f); b.Pendulum(a3 + 0.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(4f); b.GrowSpike(c3);
            b.Plat(3f);

            b.Room(RoomRule.Dark, 0.16f);       // S4: swings you can hear but not see
            b.Plat(4f); b.NightFloor(2f);
            float a4 = b.Plat(6f); b.Pendulum(a4 - 1f); b.Saw(a4 + 1.5f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the full gallery
            b.Plat(3.5f);
            float a5 = b.Plat(8f); b.Pendulum(a5 - 2.5f); b.GrowSpike(a5 - 0.5f);
            b.Pendulum(a5 + 1.5f); b.Saw(a5 + 3.2f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 28 — NO EXIT. The castle fills with bright pink DOORS — exits in every
        // other game, deaths in this one — and the real coffin flees the lineup.
        static Level L28()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the door lie, taught once
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.FakeDoor(a1 - 1f); b.Spike(a1 + 1.8f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None);              // S2: two doors, one saw, zero exits
            b.Plat(3.5f);
            float a2 = b.Plat(8f); b.FakeDoor(a2 - 2f); b.Saw(a2 - 0.3f); b.FakeDoor(a2 + 1.8f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // S3: doors glowing in the dark, bat overhead
            b.Plat(4f); b.NightFloor(2f);
            float a3 = b.Plat(7f); b.FakeDoor(a3 - 1.5f); b.Bat(a3); b.FakeDoor(a3 + 1.8f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.Loop);              // S4: the hall loops AND the doors lie
            b.Plat(3.5f);
            float a4 = b.Plat(7f); b.FakeDoor(a4 - 1.5f); b.GrowSpike(a4 + 1f);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: chase the coffin past the doors
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(11f); b.FakeDoor(a5 - 3f); b.FakeDoor(a5 - 0.5f);
            b.FakeDoor(a5 + 2f); b.HolyWater(a5 + 3.8f);
            b.Plat(5f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }

        // 29 — WORLD EXAM III. Ghost bridges, the ceiling road, the ferry under
        // the press, flipped hands, and a chase past three clocks.
        static Level L29()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.14f);       // S1: bridge + saw + moving spike
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a1 = b.Plat(5f); b.Saw(a1 - 1f); b.ShiftSpike(a1 + 1.5f, a1 + 0.5f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S2: the ceiling road, with a swing waiting
            float a2 = b.Plat(6f); b.GravRune(a2 + 1.5f);
            b.Gap(8.5f);
            float c2 = b.Plat(8f); b.CeilRune(c2 - 2.8f); b.Pendulum(c2 + 1.5f);

            b.Room(RoomRule.Press, 0.18f);      // S3: ferry + rune + fire, ceiling falling
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a3 = b.Plat(5f); b.SleepRune(a3); b.FlameJet(a3 + 1.8f);
            b.Plat(3f);

            b.Room(RoomRule.Reverse, 0.16f);    // S4: flipped through the pinch
            b.Plat(3.5f);
            float a4 = b.Plat(5f); b.Dart(a4);
            b.FakeFloor(2.2f);
            float c4 = b.Plat(5f); b.GrowSpike(c4 - 1f); b.Spike(c4 + 1.3f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: the rites end in a chase
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(6f); b.ArrowRain(a5 - 1f); b.FlameJet(a5 + 1.5f);
            b.Gap(2.3f);
            float c5 = b.Plat(9f); b.Spike(c5 - 2.5f); b.Saw(c5); b.GrowSpike(c5 + 2.5f);
            b.Plat(4f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }

        // ====================================================================
        // WORLD 4 (floors 31-39) — "the last night". Everything the castle
        // knows, layered. Floors 30 and 40 are the Warlock and the Lord; their
        // slots return Levels.BossRoom via Get(), so no dead bodies live here.
        // ====================================================================

        // 31 — EVERYTHING LIES. Fake floors, fake coffins, fake doors, real
        // sunbeams: a floor where honesty is the exception.
        static Level L31()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: coffin between two lying floors
            b.Plat(4.5f); b.FakeFloor(2f);
            float a1 = b.Plat(5f); b.FakeCoffin(a1 - 0.8f); b.Spike(a1 + 1.6f);
            b.FakeFloor(2.2f); b.Plat(4f);

            b.Room(RoomRule.None);              // S2: door + dart between the bites
            b.Plat(3.5f); b.FakeFloor(2f);
            float a2 = b.Plat(5f); b.Dart(a2 - 1.3f); b.FakeDoor(a2 + 0.8f);
            b.FakeFloor(2f);
            float c2 = b.Plat(4f); b.LateSpike(c2);
            b.Plat(3f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: vanish, collapse, and a moving spike
            b.Plat(3.5f); b.NightFloor(2f); b.FakeFloor(2f);
            float a3 = b.Plat(5f); b.FakeCoffin(a3 - 0.8f); b.ShiftSpike(a3 + 1.8f, a3 + 0.7f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None);              // S4: faller + sunbeam among the lies
            b.Plat(3.5f); b.FakeFloor(2.2f);
            float a4 = b.Plat(4f); b.Faller(a4);
            b.FakeFloor(2f);
            float c4 = b.Plat(4f); b.Surprise(c4 + 0.8f);
            b.Gap(2.3f); b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: door, coffin, saw — none of them true
            b.Plat(3.5f); b.FakeFloor(2f);
            float a5 = b.Plat(5f); b.FakeDoor(a5 - 1f); b.FakeCoffin(a5 + 1.2f);
            b.FakeFloor(2.2f);
            float c5 = b.Plat(4f); b.Saw(c5);
            b.Plat(2f);
            return b.Finish();
        }

        // 32 — THE BLACK MASS. Every stage is dark. The candles never really
        // come back; the doorway relights are the only mercy left.
        static Level L32()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.25f);       // S1: the service begins
            b.Plat(5f); b.NightFloor(2f);
            float a1 = b.Plat(5f); b.Saw(a1);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // S2: bridge + faller + moving spike
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a2 = b.Plat(5f); b.Faller(a2 - 0.5f); b.ShiftSpike(a2 + 1.5f, a2 + 0.6f);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: vanish, bat, bridge
            b.Plat(3.5f); b.NightFloor(2f);
            float a3 = b.Plat(4f); b.Bat(a3);
            b.GhostFloor(7.2f);
            float c3 = b.Plat(4f); b.Spike(c3 + 1f);

            b.Room(RoomRule.Dark, 0.13f);       // S4: the swinging dark
            b.Plat(3.5f); b.NightFloor(2.2f);
            float a4 = b.Plat(4f); b.ShiftSpike(a4 + 1.3f, a4 + 0.5f);
            b.Gap(2.3f);
            float c4 = b.Plat(5f); b.Pendulum(c4);
            b.Plat(3f);

            b.Room(RoomRule.Dark, 0.1f, true);  // S5: communion
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a5 = b.Plat(3.5f); b.ShiftSpike(a5 + 1f, a5 + 0.3f);
            b.NightFloor(2f); b.Plat(2f);
            return b.Finish();
        }

        // 33 — IRON CHOIR. Every stage door is a portcullis: five gates, five
        // rhythms, presses grinding behind them.
        static Level L33()
        {
            var b = new B();
            b.Room(RoomRule.None, 0.35f, true); // S1: the first verse — gate + spike
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.Spike(a1 + 1f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.Press, 0.25f, true);// S2: gate into the press lane
            b.Plat(3.5f);
            float a2 = b.Plat(10f); b.HolyWater(a2 - 1.5f); b.Spike(a2 + 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.None, 0.35f, true); // S3: gate, saw, faller
            b.Plat(3.5f); b.Gap(2.4f);
            float a3 = b.Plat(5f); b.Saw(a3);
            b.Gap(2.3f);
            float c3 = b.Plat(4f); b.Faller(c3);
            b.Plat(3.5f);

            b.Room(RoomRule.Press, 0.2f, true); // S4: gate, ferry, fire — under the press
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a4 = b.Plat(5f); b.FlameJet(a4);
            b.Plat(3.5f);

            b.Room(RoomRule.Press, 0.18f, true);// S5: the choir sings all at once
            b.Plat(3.5f); b.FakeFloor(2f);
            float a5 = b.Plat(5f); b.Spike(a5 - 1f); b.Saw(a5 + 1.3f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 34 — FLOOD OF FIRE. Jets and acid own the ground; the ferry is the
        // only dry road, and even the dark burns.
        static Level L34()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: three burners on one beat
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.FlameJet(a1 - 2f); b.HolyWater(a1); b.FlameJet(a1 + 2f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None);              // S2: ferry over the flood
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a2 = b.Plat(6f); b.FlameJet(a2 - 1.5f); b.FlameJet(a2 + 1.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.16f);       // S3: fire you can only see when it flares
            b.Plat(4f); b.NightFloor(2f);
            float a3 = b.Plat(6f); b.FlameJet(a3 - 1f); b.HolyWater(a3 + 1.5f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.None);              // S4: duck the block, cross the burners
            b.Plat(3.5f);
            float a4 = b.Plat(4f); b.Crusher(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.FlameJet(c4 - 1.5f); b.HolyWater(c4 + 1f);
            b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the flood crests
            b.Plat(3.5f);
            float a5 = b.Plat(8f); b.FlameJet(a5 - 2.5f); b.HolyWater(a5 - 0.5f);
            b.FlameJet(a5 + 1.5f); b.Spike(a5 + 3.2f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 35 — SPIDER'S PATIENCE. Three clocks tick at once — grow spikes,
        // arrow timers, sleep runes. Rushing and waiting both kill.
        static Level L35()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: two clocks
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.GrowSpike(a1 - 1f); b.ArrowRain(a1 + 1f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: the triple metronome
            b.Plat(4f);
            float a2 = b.Plat(7f); b.GrowSpike(a2 - 2f); b.GrowSpike(a2); b.GrowSpike(a2 + 2f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S3: nap between the clocks
            b.Plat(4f);
            float a3 = b.Plat(7f); b.SleepRune(a3 - 1f); b.GrowSpike(a3 + 1f); b.ArrowRain(a3 + 3f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // S4: clocks in the dark
            b.Plat(4f); b.NightFloor(2f);
            float a4 = b.Plat(6f); b.GrowSpike(a4 - 1f); b.ArrowRain(a4 + 1.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the web
            b.Plat(3.5f);
            float a5 = b.Plat(9f); b.GrowSpike(a5 - 3f); b.ArrowRain(a5 - 1f);
            b.SleepRune(a5 + 0.5f); b.GrowSpike(a5 + 2.5f);
            b.FakeFloor(2f); b.Plat(2.5f);
            return b.Finish();
        }

        // 36 — THE SCATTERED KEY. Portal mazes: pads that lie about their side,
        // chains over grow-spike islands, doors chosen by candlelight.
        static Level L36()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: right side wins…
            float p1 = b.Plat(7f);
            b.Gap(7.5f);
            float q1 = b.Plat(8f); b.GrowSpike(q1 + 1f);
            b.PortalAt(p1 + 2f, -2f, q1 - 3f, -2f);
            b.PortalAt(p1 - 0.5f, -2f, p1 - 2.9f, -2f);

            b.Room(RoomRule.None);              // S2: …no it doesn't
            float p2 = b.Plat(7f);
            b.Gap(7.5f);
            float q2 = b.Plat(8f); b.Saw(q2 + 1.5f);
            b.PortalAt(p2 - 0.5f, -2f, q2 - 3f, -2f);
            b.PortalAt(p2 + 2f, -2f, p2 - 2.9f, -2f);

            b.Room(RoomRule.Dark, 0.12f);       // S3: find the key in the dark
            b.Plat(4f); b.NightFloor(2f);
            float p3 = b.Plat(5f);
            b.Gap(7.5f);
            float q3 = b.Plat(7f); b.Spike(q3 + 1.5f);
            b.PortalAt(p3 + 1f, -2f, q3 - 2.5f, -2f);
            b.PortalAt(p3 - 1f, -2f, p3 - 3f, -2f);

            b.Room(RoomRule.None);              // S4: the two-hop over the island
            float p4 = b.Plat(5f);
            b.Gap(6.8f);
            float m4 = b.Plat(3.5f); b.GrowSpike(m4);
            b.Gap(6.8f);
            float q4 = b.Plat(5f);
            b.PortalAt(p4 + 1.5f, -2f, m4 - 0.9f, -2f);
            b.PortalAt(m4 + 0.9f, -2f, q4 - 1.5f, -2f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the last lock
            float p5 = b.Plat(6f);
            b.Gap(7.5f);
            float q5 = b.Plat(7f); b.Pendulum(q5 - 1f); b.Spike(q5 + 2f);
            b.PortalAt(p5 + 1.8f, -2f, q5 - 2.8f, -2f);
            b.PortalAt(p5 - 0.5f, -2f, p5 - 2.7f, -2f);
            return b.Finish();
        }

        // 37 — DEATH'S PENDULUM. Swings, launch pads and crushers — three ways
        // of being moved somewhere you didn't choose.
        static Level L37()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the swing and the pad
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.Pendulum(a1 - 1f); b.Spring(a1 + 1.5f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: duck, then thread the twin swing
            b.Plat(3.5f); b.Gap(2.3f);
            float a2 = b.Plat(4f); b.Crusher(a2);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.Pendulum(c2 - 1.5f); b.Pendulum(c2 + 1.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: pad into swing into lie
            b.Plat(3.5f);
            float a3 = b.Plat(6f); b.Spring(a3 - 1.5f); b.Pendulum(a3 + 0.5f);
            b.FakeFloor(2f);
            float c3 = b.Plat(4f); b.Dart(c3);
            b.Plat(3f);

            b.Room(RoomRule.Reverse, 0.16f);    // S4: flipped hands under the swings
            b.Plat(3.5f);
            float a4 = b.Plat(6f); b.Pendulum(a4 - 1.5f); b.Pendulum(a4 + 1.5f);
            b.Gap(2.3f);
            float c4 = b.Plat(4f); b.Spring(c4 - 0.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the full mechanism
            b.Plat(3.5f);
            float a5 = b.Plat(4f); b.Crusher(a5);
            b.Gap(2.3f);
            float c5 = b.Plat(7f); b.Pendulum(c5 - 2f); b.Spring(c5) ; b.Pendulum(c5 + 2f);
            b.Plat(2.5f);
            return b.Finish();
        }

        // 38 — THE GAUNTLET OF LIES. The rules themselves rotate stage by stage:
        // hands, halls, candles, ceiling — each stage breaks a different law.
        static Level L38()
        {
            var b = new B();
            b.Room(RoomRule.Reverse, 0.25f);    // S1: your hands lie
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.Saw(a1);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.Loop);              // S2: the hall lies
            b.Plat(3.5f);
            float a2 = b.Plat(7f); b.GrowSpike(a2 - 1f); b.Dart(a2 + 1.5f);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: the candles lie
            b.Plat(3.5f); b.NightFloor(2f); b.GhostFloor(7.2f);
            float a3 = b.Plat(4f); b.Spike(a3 + 1f);
            b.Plat(3f);

            b.Room(RoomRule.Press, 0.18f);      // S4: the ceiling lies
            b.Plat(4f);
            float a4 = b.Plat(9f); b.HolyWater(a4 - 1.5f); b.Spike(a4 + 1f);
            b.FakeFloor(2f); b.Plat(3f);

            b.Room(RoomRule.Reverse, 0.12f, true); // S5: all of them, backwards
            b.Plat(3.5f);
            float a5 = b.Plat(5f); b.Pendulum(a5);
            b.FakeFloor(2.2f);
            float c5 = b.Plat(4f); b.Dart(c5);
            b.Gap(2.3f); b.Plat(2.5f);
            return b.Finish();
        }

        // 39 — THE FINAL ASCENT. The last staged floor before the Vampire Lord:
        // the ceiling road, the ferry, the portal chain, and a chase past
        // everything the castle ever learned about you.
        static Level L39()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.13f);       // S1: dark bridge + saw + moving spike
            b.Plat(3.5f); b.NightFloor(2f); b.GhostFloor(7.2f);
            float a1 = b.Plat(5f); b.Saw(a1 - 1f); b.ShiftSpike(a1 + 1.5f, a1 + 0.6f);
            b.Plat(2.5f);

            b.Room(RoomRule.None);              // S2: the longest ceiling road
            float a2 = b.Plat(6f); b.GravRune(a2 + 1.5f);
            b.Gap(9f);
            float c2 = b.Plat(9f); b.CeilRune(c2 - 3.2f); b.Saw(c2 + 0.5f); b.Spike(c2 + 2.5f);

            b.Room(RoomRule.Press, 0.16f);      // S3: ferry + rune + fire, ceiling coming
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a3 = b.Plat(6f); b.SleepRune(a3 - 1f); b.FlameJet(a3 + 1.3f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: the portal chain, one last time
            float p4 = b.Plat(5f);
            b.Gap(6.8f);
            float m4 = b.Plat(3.5f); b.GrowSpike(m4);
            b.Gap(6.8f);
            float q4 = b.Plat(5f); b.Spike(q4 + 1f);
            b.PortalAt(p4 + 1.5f, -2f, m4 - 0.9f, -2f);
            b.PortalAt(m4 + 0.9f, -2f, q4 - 1.8f, -2f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: the coffin runs one last time
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(6f); b.ArrowRain(a5 - 1f); b.Dart(a5 + 1f);
            b.Gap(2.3f);
            float c5 = b.Plat(10f); b.Spike(c5 - 3f); b.GrowSpike(c5 - 0.5f);
            b.Saw(c5 + 2f); b.Spike(c5 + 3.8f);
            b.Plat(4f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }
    }
}
