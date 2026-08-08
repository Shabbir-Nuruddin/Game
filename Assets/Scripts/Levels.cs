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
        // A trampoline pad you can bounce on OR jump over — a real boost, not a
        // death sentence. It used to have an INVISIBLE kill-zone bolted right above
        // it, so any bounce was an unavoidable, un-telegraphed death ("kills no
        // matter what"). That's gone: a spring is now beatable like every other
        // trap. Levels that want danger above a spring must place a VISIBLE hazard.
        public void Spring(float x) => T(TrapType.Spring, x, -2.55f, 1.0f, 0.5f);
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
            int t = Mathf.Clamp(tier, 1, 4);
            var L = new Level { BossTier = t };

            // MILESTONE SCALING. Floors 20/30/40 are the landmarks of the run and
            // they should not all be fought in the same box. The arena grows with
            // the tier, so the Countess gets room to teleport, the Warlock gets
            // room to keep his distance, and the Lord's hall is the biggest space
            // in the castle. It also lengthens the fight honestly — more ground
            // to close rather than more health to chew through, which is the
            // difference between a longer fight and a more tedious one.
            float half = 13.2f + (t - 1) * 3.4f;       // 13.2 → 23.4
            float floorW = half * 2f - 0.4f;

            L.Spawn = new Vector2(-(half - 5.2f), -2f);
            L.Platforms.Add(new Rect2(0f, -3f, floorW, 0.6f));   // arena floor
            L.Platforms.Add(new Rect2(-half, 1f, 0.6f, 9f));     // left wall
            L.Platforms.Add(new Rect2(half, 1f, 0.6f, 9f));      // right wall
            L.CamMinX = -(half - 7.2f); L.CamMaxX = half - 7.2f;
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
        public static Level Generate(int seed, int difficulty, bool race = false, int endlessRhythm = -1)
        {
            var rng = new System.Random(seed);
            difficulty = Mathf.Max(0, difficulty);
            var pool = HazardPool(difficulty, race);
            var b = new B();
            b.Plat(3.7f); // safe start

            // These levels are FLIGHT modes (Endless / Blood Moon), so the player
            // can bat-glide. We cap inverted-controls to ONE per level (it was the
            // most-complained-about Blood Moon pain) and sprinkle a few wide gaps
            // that can only be cleared by jumping then holding glide.
            bool reverseUsed = false, lastWasLong = false;

            // Endless rotates five pacing profiles (balanced, sprint, glide,
            // gauntlet, breather). Difficulty still rises, but cadence changes so
            // the mode does not become one repeated procedural sentence.
            int style = endlessRhythm < 0 ? 0 : endlessRhythm % 5;
            int segments = endlessRhythm < 0 ? Mathf.Clamp(5 + difficulty, 5, 11)
                : new[] { 8, 6, 9, 11, 7 }[style];

            // THE TROLL RHYTHM, ENFORCED (see the doctrine above L21). The x-ray
            // caught this generator averaging 5.2 UNTELEGRAPHED traps per chunk,
            // with the worst roll scoring 14.8 expected deaths — the dice were
            // free to put a lie on every single platform, and a chunk where every
            // platform lies is not a troll level, it is a memory test with no
            // honest ground to form the instinct on. Two hard rules now hold:
            // a blind trap costs from a per-chunk BUDGET (~one per three
            // platforms), and two blind beats can never sit back to back.
            int blindBudget = Mathf.Max(1, Mathf.CeilToInt(segments / 3f));
            bool lastWasBlind = false;

            for (int i = 0; i < segments; i++)
            {
                // A wide GLIDE gap: too far for a plain jump, crossable with bat form.
                int glideChance = endlessRhythm < 0 ? 22 : new[] { 18, 8, 42, 22, 12 }[style];
                int fakeChance = endlessRhythm < 0 ? 18 + difficulty * 3
                    : new[] { 20, 12, 18, 32, 8 }[style] + difficulty;
                bool longGap = difficulty >= 3 && !lastWasLong && rng.Next(100) < glideChance;
                bool blindHere = false;   // has this platform already lied to you?

                if (longGap) { b.Gap(6.0f + (float)rng.NextDouble() * 0.7f); lastWasLong = true; }
                // A lying floor IS a blind trap — it spends from the same budget as
                // one, which is what stops a chunk becoming a row of trapdoors.
                else if (difficulty >= 1 && blindBudget > 0 && !lastWasBlind
                         && rng.Next(100) < fakeChance)
                { b.FakeFloor(2f); lastWasLong = false; blindBudget--; blindHere = true; }
                else { b.Gap(2.4f + (float)rng.NextDouble() * 0.5f); lastWasLong = false; }

                // A wider platform after a glide gap = a fair landing + meter refill.
                float p = b.Plat((longGap ? 4.6f : 3.6f) + (float)rng.NextDouble() * 1.3f);

                var first = NextHazard(pool, rng, ref reverseUsed);
                if (Blind(first))
                {
                    // Out of budget, or the player has had no honest ground since
                    // the last lie — downgrade to something they can see coming.
                    // They still die here; they just get to feel it was their fault.
                    if (blindHere || lastWasBlind || blindBudget <= 0) first = Telegraphed(pool, rng);
                    else { blindBudget--; blindHere = true; }
                }
                PlaceHazard(b, first, p);

                // A second hazard deeper in, for variety — but NEVER pair anything
                // with a Crusher. A Crusher demands you stay LOW (jump and the block
                // slams you), while almost every other hazard demands you JUMP OVER
                // it. Combine the two and the platform is physically impossible.
                // We also keep the rage-teleport (WarpBack) solo. The partner is
                // always TELEGRAPHED and never lands on a platform that already
                // hides something: a blind trap you're stacked on top of reads as a
                // bug, not a joke.
                int pairChance = endlessRhythm < 0 ? 30 : new[] { 24, 18, 28, 42, 12 }[style];
                if (difficulty >= 4 && !Soloist(first) && !blindHere && rng.Next(100) < pairChance)
                {
                    var second = Telegraphed(pool, rng);
                    if (!Soloist(second))
                        PlaceHazard(b, second, p + 1.6f);
                }
                lastWasBlind = blindHere;
            }
            b.Gap(2.4f);
            var generated = b.Finish();
            // Endless chunks are implementation detail, not levels. Remove the
            // coffin gate; GameRoot advances automatically on the final safe pad.
            if (endlessRhythm >= 0)
                generated.Traps.RemoveAll(t => t.type == TrapType.RealExit);
            return generated;
        }

        // ── BLOOD MOON — FIVE AUTHORED NIGHTS ───────────────────────────────
        // Blood Moon used to be Generate(seed, 1 + night), i.e. the same dice the
        // Endless mode rolls. That is why nobody finished it: at difficulty 4 the
        // pool opens ALL AT ONCE (saws, flame jets, warp runes, reversed controls,
        // invisible surprises) and the generator was free to stack a blind trap on
        // every platform and pair them on top. Night 5 routinely built 13+ hazards
        // with 7 of them unavoidable-on-sight — an ~14 expected-death floor, run
        // one-hit, on a life pool of 7.
        //
        // A daily mode has to be FINISHABLE TONIGHT or it is not a daily mode, so
        // the five nights are now authored beat by beat, exactly like a Castle
        // floor, and the seed only varies the spacing:
        //
        //   night 1   ~1.8   spikes only. The handshake — and the flight tutorial.
        //   night 2   ~3.5   THE FLOOR LIES (fake floors) + growing spikes.
        //   night 3   ~4.8   FROM ABOVE (pendulum, chandelier).
        //   night 4   ~6.2   THE CASTLE BITES (saw, dart, flame jet).
        //   night 5   ~7.6   THE MOON TAKES (holy water, crusher, one Reverse).
        //
        // For scale: the Castle ramps 1.3 → 8.6 across FORTY floors. Blood Moon
        // covers nearly the same range in five, so night for night it always sits
        // a step above the Castle — which is the point — but it now opens below
        // Castle floor 5 instead of above Castle floor 19.
        //
        // THE RULES THIS TABLE KEEPS (see THE TROLL RHYTHM above L21):
        //   • every night opens on honest ground with no hazard on it;
        //   • ONE new trap family per night, and it is always revealed ALONE on a
        //     wide platform, never in a pair and never straight after a blind one;
        //   • never two untelegraphed beats back to back;
        //   • REST beats carry a checkpoint — two of them from night 3 — so a
        //     death costs a third of a night, not the whole climb.
        // ─────────────────────────────────────────────────────────────────────
        const int GapNormal = 0, GapGlide = 1, GapFake = 2;

        struct Beat
        {
            public int gap;              // how you arrive: walk, glide, or a lying floor
            public TrapType? hazard;     // the one thing on this platform (null = clean)
            public TrapType? extra;      // a SECOND telegraphed hazard, deeper in
            public bool rest;            // wide clean platform + checkpoint
        }

        static Beat Walk(TrapType? h = null, TrapType? extra = null)
            => new Beat { gap = GapNormal, hazard = h, extra = extra };
        static Beat Glide(TrapType? h = null)
            => new Beat { gap = GapGlide, hazard = h };
        static Beat Lie()                       // the floor that isn't there
            => new Beat { gap = GapFake };
        static Beat Rest()
            => new Beat { gap = GapNormal, rest = true };

        static Beat[] NightScore(int n)
        {
            switch (n)
            {
                // NIGHT 1 — FIRST BLOOD. Nothing here can kill you without showing
                // itself first. Beat 3 is a glide gap on purpose: it is the only
                // thing in the mode that TEACHES the bat, so it sits early, alone,
                // with a fat landing pad and nothing waiting on the other side.
                case 1: return new[]
                {
                    Walk(),                              // honest ground
                    Walk(TrapType.SpikeStatic),
                    Glide(),                             // fly, land clean
                    Rest(),
                    Walk(TrapType.SpikeStatic),
                    Walk(TrapType.LateSpike),            // NEW: the spike that waits
                };
                // NIGHT 2 — THE FLOOR LIES. The mode's first blind beat, and the
                // whole game's thesis. Two of them, spaced as far apart as the
                // night allows, with the checkpoint between.
                case 2: return new[]
                {
                    Walk(),
                    Lie(),                               // NEW: blind — the floor goes
                    Walk(TrapType.GrowSpike),            // NEW: telegraphed, rises under you
                    Rest(),
                    Glide(),
                    Walk(TrapType.SpikeStatic),
                    Lie(),                               // blind #2, well after the first
                };
                // NIGHT 3 — FROM ABOVE. The ceiling joins in. Pendulum first (fully
                // visible, learn the timing), chandelier later (the blind version of
                // the same idea) — the reveal-then-betray pairing the game runs on.
                case 3: return new[]
                {
                    Walk(),
                    Walk(TrapType.Pendulum),             // NEW: telegraphed swing
                    Walk(TrapType.SpikeStatic, TrapType.GrowSpike),
                    Rest(),
                    Lie(),                               // blind
                    Glide(),
                    Walk(TrapType.GrowSpike, TrapType.SpikeStatic),
                    Rest(),
                    Walk(TrapType.Chandelier),           // NEW: blind ceiling drop, solo
                    Walk(TrapType.Pendulum, TrapType.SpikeStatic),
                };
                // NIGHT 4 — THE CASTLE BITES. The machinery night: saw, dart, flame.
                // Two blind beats (dart, one lying floor) with four platforms of
                // honest ground between them.
                case 4: return new[]
                {
                    Walk(),
                    Walk(TrapType.Saw),                  // NEW: telegraphed, on a cycle
                    Walk(TrapType.GrowSpike, TrapType.SpikeStatic),
                    Rest(),
                    Walk(TrapType.Dart),                 // NEW: blind, fires from the wall
                    Glide(),
                    Walk(TrapType.FlameJet),             // NEW: telegraphed eruption
                    Walk(TrapType.SpikeStatic, TrapType.LateSpike),
                    Walk(TrapType.Pendulum),
                    Rest(),
                    Lie(),                               // blind
                    Walk(TrapType.Saw, TrapType.SpikeStatic),
                    Walk(TrapType.FlameJet),
                };
                // NIGHT 5 — THE MOON TAKES. The climax, and the ONLY floor in the
                // mode that steals your controls. Reverse lands second-to-last, on
                // a clean wide platform, right after a checkpoint — so the run's
                // hardest idea costs you seconds, not the night.
                default: return new[]
                {
                    Walk(),
                    Walk(TrapType.HolyWater),            // NEW: telegraphed, pulses
                    Walk(TrapType.Pendulum, TrapType.SpikeStatic),
                    Rest(),
                    Walk(TrapType.Crusher),              // NEW: blind, always solo
                    Glide(),
                    Walk(TrapType.Saw, TrapType.SpikeStatic),
                    Walk(TrapType.FlameJet, TrapType.GrowSpike),
                    Lie(),                               // blind
                    Rest(),
                    Walk(TrapType.Reverse),              // the climax — one, only one
                    Walk(TrapType.HolyWater, TrapType.SpikeStatic),
                    Walk(TrapType.Saw),
                    Walk(TrapType.LateSpike),
                };
            }
        }

        /// <summary>
        /// Build one authored Blood Moon night. The BEATS are fixed (that is the
        /// difficulty contract); the seed only shifts gap and platform widths, so
        /// tonight's castle is laid out differently from last night's without ever
        /// changing how hard it is or which trap teaches what.
        /// </summary>
        public static Level BloodMoonNight(int seed, int night)
        {
            var rng = new System.Random(seed);
            var b = new B();
            b.Plat(6.5f);   // the door: long, empty, honest — always

            foreach (var beat in NightScore(Mathf.Clamp(night, 1, 5)))
            {
                switch (beat.gap)
                {
                    // Too far to jump, comfortable to glide. Blood Moon is a flight
                    // mode and this is where it says so.
                    case GapGlide: b.Gap(5.4f + (float)rng.NextDouble() * 0.5f); break;
                    // A "floor" that drops out from under you. It IS the gap.
                    case GapFake:  b.FakeFloor(2.2f); break;
                    default:       b.Gap(2.3f + (float)rng.NextDouble() * 0.45f); break;
                }

                // Landing pads are deliberately wider than the Endless generator's
                // (3.6): you need room to land, read the platform and commit. Rest
                // pads and glide landings are wider still.
                float w = (beat.rest ? 5.6f : beat.gap == GapGlide ? 5.0f : 4.2f)
                        + (float)rng.NextDouble() * 0.9f;
                float p = b.Plat(w);

                if (beat.rest) { b.Checkpoint(p); continue; }
                if (beat.hazard.HasValue) PlaceHazard(b, beat.hazard.Value, p);
                if (beat.extra.HasValue)  PlaceHazard(b, beat.extra.Value, p + 1.8f);
            }
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

        static List<TrapType> HazardPool(int d, bool race = false)
        {
            // MULTIPLAYER RACE: deliberately TINY + simple. The fun of a race is the
            // SABOTAGE buttons (curse = flip their controls, snuff = blind them, quake
            // = shake their screen) — those only work if the level itself is easy
            // enough to actually finish. So the track is just spikes to jump and the
            // odd overhead bat; falling floors come from the FakeFloor gap logic. No
            // saws/darts/crushers/flame-jets/etc. — a hard level + sabotage = unbeatable.
            // A SMALL set, weighted so spikes dominate: enough variety that no two
            // rounds feel the same (and each seed lays them out differently), but
            // every hazard here is a simple read — jump it, or time one swing.
            if (race)
                return new List<TrapType>
                {
                    TrapType.SpikeStatic, TrapType.SpikeStatic, TrapType.SpikeStatic,
                    TrapType.Saw, TrapType.BatSwoop,
                };

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

        /// <summary>
        /// Traps that kill with NO tell the first time you meet them — the game's
        /// comedy and its entire death count. Kept in lockstep with the difficulty
        /// x-ray's own list, because the generator's budget and the audit that
        /// grades it have to agree on what "blind" means.
        /// </summary>
        internal static bool Blind(TrapType t) =>
            t == TrapType.FakeFloor || t == TrapType.Surprise || t == TrapType.FakeExit ||
            t == TrapType.Faller || t == TrapType.Chandelier || t == TrapType.Crusher ||
            t == TrapType.Dart || t == TrapType.WarpBack || t == TrapType.Reverse;

        /// <summary>A hazard from this pool you can actually SEE coming.</summary>
        static TrapType Telegraphed(List<TrapType> pool, System.Random rng)
        {
            // Walk from a random offset so the choice stays varied without
            // allocating a filtered copy every platform.
            int start = rng.Next(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                var t = pool[(start + i) % pool.Count];
                if (!Blind(t)) return t;
            }
            return TrapType.SpikeStatic;   // early tiers are spikes anyway
        }

        // Hazards that must stand ALONE on a platform: crushers (stay-low), the
        // warp rune (rage teleport), and the reactive ceiling drops (Faller /
        // Chandelier) — pairing a drop with another hazard forces you to stop
        // right under it, which is what made the night-3 "falling box" unfair.
        internal static bool Soloist(TrapType t) =>
            t == TrapType.Crusher || t == TrapType.WarpBack ||
            t == TrapType.Faller || t == TrapType.Chandelier;

        internal static void PlaceHazard(B b, TrapType t, float p)
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

        // ── THE CASTLE'S DIFFICULTY RAMP ────────────────────────────────────
        // Forty floors need a CURVE, not forty separate opinions. The x-ray
        // score is roughly "expected deaths on a first clear", and this is the
        // shape the whole castle is tuned to (2026-08-08 pass):
        //
        //   floors 1-8    1.3 → 4.4   the teaching climb. Nothing here should
        //                             cost more than a handful of tries.
        //   floor  9      7.8  ★      THE FIRST WALL. Flipped hands among the
        //                             liars — the first floor you have to learn.
        //   floors 10-18  4.2 → 6.0   world 2 recovers and climbs again.
        //   floor  19     8.0  ★      THE SECOND WALL, the exam before the
        //                             Countess: every rule at once, backwards.
        //   floors 21-29  5.5 → 7.8   world 3 restarts lower, climbs higher.
        //   floors 31-39  6.5 → 8.5   the last night, with two walls inside it:
        //   floors 33, 37 8.8 / 9.6 ★ the Iron Choir and Death's Pendulum.
        //
        // Everything between the ★ floors is a RAMP, not a plateau: each floor
        // is worth a little more than the one before it, so a wall is felt as a
        // wall and the floor after it is felt as relief. Traps stay SPARSE —
        // one blind beat per stage, honest ground either side (see THE TROLL
        // RHYTHM above L21). The difficulty in the ★ floors comes from stolen
        // CONTROL (reversed hands, closing ceilings, gates) rather than from
        // piling more spikes into the same screen.
        //
        // Re-run `Trust Issues → Dump Difficulty X-Ray` after ANY edit here.
        // ─────────────────────────────────────────────────────────────────────

        // ====================================================================
        // FLOORS 1–10. Each floor is one continuous hall the camera scrolls
        // down (no per-room screen lock, no walls sealed behind you — that
        // read as a chain of separate levels rather than one castle). It's
        // still built out of 5 chambers via b.Room(), ~20-27 units each, each
        // with its own ceiling and a doorway squeeze into the next — that's
        // real level geometry, not a stage boundary, and each chamber still
        // owns the ONE rule it introduces or retires. Floor 1 is the only
        // exception: it's a trial, not an exam, so it skips rooms entirely.
        // Trees Hate You rule: every death is a punchline — setup, false
        // confidence, reveal. Never two new ideas at once across floors.
        // ====================================================================

        // 1 — TRUST NOTHING. The trial. Analytics (60+ testers) put 407 deaths
        // here — the #1 onboarding wall — when this ran five stages and a
        // dozen-plus hazards deep. A first level should be a HANDSHAKE, not an
        // exam: one lie, taught clean, then one ordinary jump so the run still
        // asks for a single skill, then the door. Nothing here repeats and
        // nothing here is a stage — it's one short corridor, over in seconds,
        // that teaches the game's whole thesis (the floor lies) and nothing else.
        static Level L1()
        {
            var b = new B();
            b.Plat(7f);
            b.FakeFloor(2f);          // THE lie. Walk right and the floor isn't there.
            float a = b.Plat(10f);
            b.Spike(a + 3f);          // the one ordinary ask, in plain sight, far from the lie
            b.Plat(6f);
            return b.Finish();
        }

        // 2 — MOVING TEETH. The beginner's first full floor: one moving hazard
        // per stage, one collapsing floor, then a gate-and-saw graduation test.
        static Level L2()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: watch a spike breathe, then cross
            b.Plat(7f);
            float a1 = b.Plat(9f); b.GrowSpike(a1 + 2f);
            b.Plat(5f);

            b.Room(RoomRule.None);              // S2: one saw with generous space around it
            b.Plat(6f); b.Gap(2.2f);
            float a2 = b.Plat(10f); b.Saw(a2 + 1.5f);
            b.Plat(6f);

            b.Room(RoomRule.None);              // S3: the floor lies once, then gives a wide landing
            b.Plat(7f); b.FakeFloor(1.8f); b.Plat(10f);

            b.Room(RoomRule.None);              // S4: an ambush spike can be escaped on reflex
            b.Plat(7f);
            float a4 = b.Plat(11f); b.LateSpike(a4 + 2f);
            b.Plat(5f);

            b.Room(RoomRule.None, 0.35f, true); // S5: wait out the gate, then read one moving saw
            b.Plat(7f);
            float a5 = b.Plat(11f); b.Saw(a5 + 2f);
            b.Gap(2.1f); b.Plat(7f);
            return b.Finish();
        }

        // 3 — THE COFFIN FLEES. Four one-hazard practice rooms, then a readable
        // chase. Each stage introduces one timing idea without stacking another.
        static Level L3()
        {
            var b = new B();
            // Thinned hard: this scored 9.8 on the difficulty x-ray — as punishing
            // as floor 25 — while sitting third in the game. Every stage kept its
            // IDEA and lost the pile-on around it, so the floor still introduces
            // the dart, the slab ride, the pendulum and the chase, one at a time.
            b.Room(RoomRule.None);              // S1: the dart — one new thing, in the open
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.Dart(a1);
            b.Gap(2.3f);
            b.Plat(8f);

            b.Room(RoomRule.None);              // S2: ordinary jumps establish confidence
            b.Plat(6f); b.Gap(2.2f);
            float a2 = b.Plat(8f); b.Saw(a2 + 2f);
            b.Plat(5f);

            b.Room(RoomRule.None);              // S3: pendulum, then a floor that lies
            b.Plat(4f);
            float a3 = b.Plat(6f); b.Pendulum(a3);
            b.Gap(2.2f); b.Plat(8f);

            b.Room(RoomRule.None);              // S4: the drop from above, on its own
            b.Plat(4f); b.Gap(2.3f);
            float a4 = b.Plat(6f); b.Faller(a4);
            b.Gap(2.2f); b.Plat(9f);

            b.Room(RoomRule.Flee, 0.12f);       // S5: THE CHASE, without a gate tax
            float p5 = b.Plat(5f);
            float a5 = b.Plat(8f); b.Spike(a5 + 2f);
            b.Gap(2.1f);
            // The chase is the whole point of this stage, so the lane it runs
            // through is kept READABLE — one hazard to swerve round, not three.
            b.Plat(10f);
            b.Plat(5f);
            b.ExitAt(p5 + 0.6f);   // one step ahead of you. it knows.
            return b.FinishBare();
        }

        // 4 — THE GATEHOUSE. No global time pressure yet: gates, moving steel and
        // one falling chandelier test the trap vocabulary learned so far.
        static Level L4()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: generous runway, then two visible asks
            b.Plat(7f);
            float a1 = b.Plat(13f); b.Spike(a1 + 3f); b.Pendulum(a1 - 3f);
            b.Plat(6f);

            b.Room(RoomRule.None, 0.35f, true); // S2: first gate, then remember that floors lie
            b.Plat(7f); b.FakeFloor(1.8f); b.Plat(10f);

            // S3: open sky. The chandelier is a big, loud, one-off idea and it
            // used to share a stage with a late spike — two blind kills in one
            // screen, which reads as the game cheating rather than trolling.
            b.Room(RoomRule.None);
            b.Plat(4f); b.Gap(2.4f);
            float a3 = b.Plat(6f); b.Chandelier(a3);
            b.Gap(2.4f);
            b.Plat(5f);

            b.Room(RoomRule.None);              // S4: moving steel between two clean jumps
            b.Plat(7f); b.Gap(2.1f);
            float a4 = b.Plat(9f); b.Saw(a4 + 1.5f);
            b.Gap(2.1f); b.Plat(7f);

            b.Room(RoomRule.None, 0.35f, true); // S5: final gate, one readable hazard
            b.Plat(6f);
            float a5 = b.Plat(10f); b.Spike(a5 + 2.5f);
            b.Gap(2.1f); b.Plat(7f);
            return b.Finish();
        }

        // 5 — THE LULLABY. Sleep runes grow from isolated lessons into a final
        // rune-field test, without introducing global time pressure this early.
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

            b.Room(RoomRule.None);              // S3: learn the rune cleanly before pressure returns
            b.Plat(3.5f);
            float a3 = b.Plat(7f); b.SleepRune(a3 - 1.5f); b.Spike(a3 + 2f);
            b.Gap(2.3f);
            float c3 = b.Plat(6f); b.HolyWater(c3 - 1f); b.Pendulum(c3 + 1.5f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: the rune field, then the ceiling's first joke
            b.Plat(4f);
            float a4 = b.Plat(9f); b.SleepRune(a4 - 3f); b.SleepRune(a4 - 0.5f);
            b.SleepRune(a4 + 2f); b.Bat(a4 + 0.3f);
            b.Gap(2.3f);
            // Floor 5 was EASIER than floors 3 and 4 (2.0 vs 3.0 on the x-ray),
            // which stalls the climb right where it should be gathering pace.
            // One blind drop, on wide honest ground, after the runes are read.
            float c4 = b.Plat(6f); b.Faller(c4);
            b.Plat(4f);

            b.Room(RoomRule.None);              // S5: one final rune, one blade to time
            b.Plat(6f);
            float a5 = b.Plat(11f); b.SleepRune(a5); b.Saw(a5 + 3f);
            b.Gap(2.1f); b.Plat(6f);
            return b.Finish();
        }

        // 6 — THE CURSED HAND. Controls flip mid-screen and stay flipped for
        // the rest of the stage: reversed gaps, a reversed dart dodge, twin
        // pendulums on the honest screen so your eyes never rest.
        static Level L6()
        {
            var b = new B();
            b.Room(RoomRule.Reverse, 0.3f);     // S1: the moonwalk teach + two things to read
            b.Plat(5f);
            float a1 = b.Plat(8f); b.Spike(a1 + 2.5f); b.GrowSpike(a1 - 2f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Reverse, 0.25f);    // S2: two gaps, backwards
            b.Plat(3.5f); b.Gap(2.3f); b.Plat(3f); b.Gap(2.4f);
            float a2 = b.Plat(5f); b.Spike(a2);
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S3: honest hands, honest hazards — the breath before S4
            b.Plat(3.5f);
            float a3 = b.Plat(6f); b.Pendulum(a3 - 1.5f); b.Pendulum(a3 + 1.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.Saw(c3);
            b.Plat(3f);

            // S4: dodge a dart with flipped hands. Reversed controls already ARE
            // the difficulty here — stacking a lying floor and a twin-spike pinch
            // on top of them was asking for pixel precision with the wrong hands.
            // The floor sat at 5.0 while floors 7 and 8 sat at 3.8 and 4.0, so the
            // ONE blind kill on the whole floor lives here, where the joke is.
            b.Room(RoomRule.Reverse, 0.2f);
            b.Plat(4f);
            float a4 = b.Plat(6f); b.Dart(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Spike(c4 + 1.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Reverse, 0.28f);    // S5: one clean reversed timing test
            b.Plat(6f); b.Gap(2.2f);
            float c5 = b.Plat(9f); b.Pendulum(c5 + 1.5f); b.Spike(c5 - 2.5f);
            b.Plat(6f);
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

            b.Room(RoomRule.Dark, 0.25f);       // S3: dark swaps one floor for one bridge
            b.Plat(5f); b.NightFloor(1.8f);
            b.Plat(4f); b.GhostFloor(7.2f); b.Plat(7f);

            b.Room(RoomRule.None);              // S4: chandelier + spike in honest light
            b.Plat(3.5f); b.Gap(2.4f);
            float a4 = b.Plat(5f); b.Chandelier(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(4f); b.Spike(c4);
            b.Plat(3f);

            b.Room(RoomRule.Dark, 0.22f);       // S5: bridge plus two visible landing tests
            b.Plat(5f); b.GhostFloor(7.2f);
            float a5 = b.Plat(9f); b.Spike(a5 + 2.5f); b.GrowSpike(a5 - 2f);
            b.Plat(5f);
            return b.Finish();
        }

        // 8 — THE ENDLESS HALL. Doorway runes silently loop you back across a
        // full screen you can SEE all of — that's the gaslight. Grow-spike
        // clocks and darts run while you time the rune jump; the hall gives up
        // after three loops so nobody is stuck forever.
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

            b.Room(RoomRule.None);              // S5: final runway, no gate stacked on the traps
            b.Plat(5f); b.FakeFloor(1.8f);
            float a5 = b.Plat(8f); b.Spike(a5 + 2f); b.Saw(a5 - 2f);
            b.Plat(4f);
            return b.Finish();
        }

        // 9 — COFFIN ROULETTE. ★ THE FIRST WALL. Dull-brass fakes among flame
        // jets and acid, dark screens where coffins loom out of the candlelight,
        // a crossing you make with your hands flipped — and the one true glowing
        // coffin flees past a final decoy when you reach for it.
        //
        // Eight floors of climbing land here, and this is the first floor the
        // castle expects you to LEARN rather than walk. It runs six chambers
        // where its neighbours run five, and the extra weight is deliberately
        // not extra spikes: it's the reversed room and the ferry, which take
        // your control away instead of filling the screen. (~7.8 on the x-ray
        // against ~4.4 for floor 8 — a wall you can feel, then floor 10 lets go.)
        static Level L9()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the tell, taught cheaply
            b.Plat(4f);
            float a1 = b.Plat(6f); b.FakeCoffin(a1 - 1f);
            b.Gap(2.3f);
            float c1 = b.Plat(5f); b.Spike(c1 + 1.5f);
            b.Plat(3.5f);

            // Thinned from a 12.0 x-ray score (10 blind kills) — the worst wall in
            // the first half of the game. The fake coffin is a great lie, but four
            // of them in a stage stops being a lie and becomes a guessing game.
            b.Room(RoomRule.None);              // S2: fire guards a coffin that's ALSO lying
            b.Plat(4f);
            float a2 = b.Plat(7f); b.FlameJet(a2 - 1.5f); b.FakeCoffin(a2 + 1.5f);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.Saw(c2 + 1f);
            b.Plat(4f);

            b.Room(RoomRule.Dark, 0.16f);       // S3: a coffin looms out of the dark, saw runs
            b.Plat(4f);
            float a3 = b.Plat(9f); b.FakeCoffin(a3 - 2f); b.Saw(a3 + 3.2f);
            b.Gap(2.3f);
            float c3 = b.Plat(6f); b.GrowSpike(c3 + 1f);
            b.Plat(4f);

            // S4: THE WALL INSIDE THE WALL. A dull-brass coffin is a reading
            // test, so this stage takes your hands away while you take it. No
            // extra kill boxes — the same lie, told to someone walking backwards.
            b.Room(RoomRule.Reverse, 0.22f);
            b.Plat(5f);
            float a4 = b.Plat(10f); b.FakeCoffin(a4 + 1.5f); b.Pendulum(a4 - 2.5f);
            b.Gap(2.2f); b.Plat(6f);

            b.Room(RoomRule.None);              // S4b: the ferry over the pit, with the fire lit
            b.Plat(4.5f); b.MoverGap(6.8f);
            float a4b = b.Plat(7f); b.FlameJet(a4b + 1.5f);
            b.Plat(5f);

            b.Room(RoomRule.Flee, 0.12f);       // S5: chase past one learned fake
            float p5 = b.Plat(5f);
            float a5 = b.Plat(13f); b.FakeCoffin(a5 + 1.5f);
            b.Plat(5f);
            b.ExitAt(p5 + 0.6f);
            return b.FinishBare();
        }

        // 10 — THE FINAL EXAM. Every lie in the castle — plus the portal room:
        // two pads, one crosses the impossible gap, one shuttles you back to the
        // start. No boss; the exam IS the boss.
        //
        // MILESTONE FLOOR. 10/20/30/40 are the landmarks of the run, and a
        // landmark you clear in the same ninety seconds as floor 9 isn't one.
        // This is the longest floor in world 1 by design — SEVEN chambers where
        // its neighbours run five — so that reaching it feels like arriving
        // somewhere and finishing it feels like it cost something. It is not
        // meaner per-screen than floor 9; it is simply more of them, which is
        // the difference between a hard floor and an exhausting one.
        static Level L10()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.38f);       // S1: night floor + moving spike (see both lit first)
            b.Plat(3.5f); b.NightFloor(2f);
            float a1 = b.Plat(8f); b.ShiftSpike(a1 + 2f, a1 + 0.8f);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.None);              // S2: one pulsing puddle, read before crossing
            b.Plat(6f);
            float a2 = b.Plat(11f); b.HolyWater(a2 + 1.5f);
            b.Gap(2.1f); b.Plat(7f);

            b.Room(RoomRule.None);              // S2b: the collapse, re-examined with room to react
            b.Plat(6f);
            float a2b = b.Plat(9f); b.Saw(a2b + 2f);
            b.FakeFloor(2f);
            b.Plat(7f);

            b.Room(RoomRule.None);              // S3: THE PORTAL ROOM — pick a door
            float p3 = b.Plat(7f);
            b.Gap(7.5f);                        // unjumpable: a portal is the only way over
            float q3 = b.Plat(10f); b.GrowSpike(q3 + 1.5f);
            // Pads sit clear of the spawn zone, with a jumpable gap between them
            // so you can reach the far pad without touching the near one.
            b.PortalAt(p3 + 2f, -2f, q3 - 3.5f, -2f);     // the RIGHT door
            b.PortalAt(p3 - 0.5f, -2f, p3 - 2.9f, -2f);   // the joke door (back you go)

            b.Room(RoomRule.Reverse, 0.3f);     // S4: one pendulum with flipped hands
            b.Plat(6f);
            float c4 = b.Plat(10f); b.Pendulum(c4 + 1.5f);
            b.Gap(2.2f); b.Plat(6f);

            b.Room(RoomRule.Press, 0.30f);      // S4b: the ceiling remembers you too
            b.Plat(6f);
            float c4b = b.Plat(10f); b.GrowSpike(c4b - 1.5f); b.Spike(c4b + 2.5f);
            b.Gap(2.2f); b.Plat(6f);

            b.Room(RoomRule.Flee, 0.18f);       // S5: the last chase tests, rather than piles on
            float p5 = b.Plat(5f);
            float a5 = b.Plat(8f); b.Dart(a5 + 2f);
            b.Gap(2.1f);
            float c5 = b.Plat(11f); b.Spike(c5 + 2.5f);
            b.Plat(5f);
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

        // 12 — THE DARK RETURNS. World 1's dark without the training wheels —
        // and world 2's opening statement, which is about SHAPE. Every floor up
        // to this one has been five long halls, 100 units end to end, in exactly
        // the same rhythm. This is SEVEN CRAMPED CELLS, one idea each, a door
        // slamming behind every one of them. The castle didn't get harder here,
        // it got NARROWER — and that lands before a single trap fires.
        static Level L12()
        {
            var b = new B();
            // Seven cells with three traps between them scored 2.8 — EASIER than
            // floor 4, in the floor that opens world 2. Each cell now asks one
            // honest question as well as telling its lie, so the shape still
            // reads as "narrower" without the world opening on a stroll.
            b.Room(RoomRule.Dark, 0.30f);       // I: a floor that stops existing. Nothing else.
            b.Plat(4f); b.NightFloor(2.1f);
            float a1 = b.Plat(5f); b.Spike(a1 + 1.5f);

            b.Room(RoomRule.Dark, 0.25f);       // II: the spike moves while you can't watch it
            float a2 = b.Plat(8f); b.ShiftSpike(a2 + 2.2f, a2 - 1.4f); b.GrowSpike(a2 - 2.6f);

            b.Room(RoomRule.None);              // III: lit — the blades are seen properly, as a mercy
            float a3 = b.Plat(7f); b.Saw(a3); b.Pendulum(a3 + 2.5f);

            b.Room(RoomRule.Dark, 0.22f);       // IV: something drops, unlit
            float a4 = b.Plat(4.5f); b.Faller(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(5f); b.Spike(c4 + 1.2f);

            b.Room(RoomRule.Dark, 0.15f);       // V: the bridge that's only there in the dark
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a5 = b.Plat(5f); b.Saw(a5 + 1.2f);

            b.Room(RoomRule.None, 0.35f, true); // VI: the door bites and the floor lies
            b.Plat(3.5f); b.FakeFloor(2f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // VII: all of it, in the smallest room yet
            b.Plat(3.5f); b.NightFloor(2f);
            float a7 = b.Plat(5f); b.ShiftSpike(a7 + 1.4f, a7 - 1.1f); b.FlameJet(a7 - 2f);
            return b.Finish();
        }

        // 13 — SPRING LOADED. Launch pads that throw you at things the ground
        // never showed you. THREE rooms, each one long question — coming
        // straight off floor 12's seven slamming doors, a floor with almost none
        // reads as open space, which is exactly the wrong thing to feel safe in.
        static Level L13()
        {
            var b = new B();
            b.Room(RoomRule.None);              // R1: the pad, and the ceiling's answer to it
            b.Plat(5f); b.Gap(2.3f);
            float a1 = b.Plat(8f); b.Spring(a1 - 1.5f); b.Spike(a1 + 1.5f);
            b.Gap(2.4f);
            float c1 = b.Plat(7f); b.Chandelier(c1);
            b.Plat(4f);

            b.Room(RoomRule.None);              // R2: pads and spikes, without an untaught room rule
            b.Plat(3.6f);
            float a2 = b.Plat(9f); b.Spring(a2 - 2.5f); b.Spike(a2); b.Spring(a2 + 2.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None, 0.35f, true); // R3: the ferry, then the biting door
            b.Plat(4f); b.MoverGap(6.8f);
            float a3 = b.Plat(6f); b.Spring(a3 - 1.2f); b.Dart(a3 + 1.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(6f); b.Saw(c3 - 1f); b.Spike(c3 + 1.5f);
            b.Plat(3f);
            return b.Finish();
        }

        // 14 — THE SUN LIES. Patches of daylight burn the undead — invisible
        // ground that kills, always placed exactly where relief should be.
        // FOUR long rooms, not five short ones — and three sunbeams, not five.
        // The x-ray had this floor at NINE unavoidable deaths, the second-worst
        // in the game: a joke told nine times isn't a joke, it's a toll. The sun
        // still lies three times; everything guarding it now announces itself.
        static Level L14()
        {
            var b = new B();
            b.Room(RoomRule.None);              // I: the first beam, after a calm gap
            b.Plat(5f); b.Gap(2.3f);
            float a1 = b.Plat(8f); b.Surprise(a1 - 0.8f); b.Spike(a1 + 2.2f);
            b.Gap(2.4f);
            float c1 = b.Plat(6f); b.Pendulum(c1);
            b.Plat(4f);

            b.Room(RoomRule.None);              // II: honest blades, then the sun again
            b.Plat(4f);
            float a2 = b.Plat(7f); b.Pendulum(a2 - 1.5f); b.GrowSpike(a2 + 2f);
            b.Gap(2.4f);
            // Two sunbeams, not three: the joke is the beam appearing where the
            // relief should be, and it stops being a joke on its third telling.
            float c2 = b.Plat(7f); b.Saw(c2 - 2f); b.Pendulum(c2 + 1.4f);
            b.Plat(4f);

            b.Room(RoomRule.Dark, 0.2f);        // III: you couldn't see it lit. Now try it unlit.
            b.Plat(4f); b.NightFloor(2f);
            float a3 = b.Plat(8f); b.Pendulum(a3 - 2f); b.Surprise(a3 + 2f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.None, 0.35f, true); // IV: the coffin road, guarded honestly
            b.Plat(4f);
            float a4 = b.Plat(8f); b.Saw(a4 - 2f); b.Pendulum(a4 + 1.5f);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Bat(c4);
            b.Plat(3f);
            return b.Finish();
        }

        // 15 — ARROW CHOIR. Ceiling timers rain bolts on a beat; crushers force
        // you LOW while everything else wants you jumping.
        // SIX rooms — the longest floor in the world, because a rhythm needs
        // bars to be a rhythm, and because after a three-room floor the length
        // itself is the surprise.
        static Level L15()
        {
            var b = new B();
            b.Room(RoomRule.None);              // 1: learn the beat — one bar of it
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.ArrowRain(a1 + 1.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None);              // 2: the crusher — the one thing that wants you LOW
            b.Plat(4f); b.Gap(2.3f);
            float a2 = b.Plat(5f); b.Crusher(a2);
            b.Gap(2.4f); b.Plat(4f);

            b.Room(RoomRule.None);              // 3: rain, then the blade
            b.Plat(3.5f);
            float a3 = b.Plat(8f); b.ArrowRain(a3 - 2.4f); b.Saw(a3 + 1f); b.GrowSpike(a3 + 3.2f);
            b.Plat(4f);

            b.Room(RoomRule.None);              // 4: the ferry, mid-choir
            b.Plat(4f); b.MoverGap(6.8f);
            float a4 = b.Plat(6f); b.ArrowRain(a4 - 1f); b.Spike(a4 + 1.8f);
            b.Plat(4f);

            b.Room(RoomRule.Reverse, 0.25f);    // 5: same beat, wrong hands
            b.Plat(4f);
            float a5 = b.Plat(7f); b.ArrowRain(a5);
            b.Gap(2.3f); b.Plat(4f);

            // 6: the crescendo, behind a biting door. One blind beat (the dart),
            // not two — a crusher AND a dart in the last cell of a six-cell floor
            // was the difference between a rhythm test and a memory test.
            b.Room(RoomRule.None, 0.35f, true);
            b.Plat(3.5f);
            float a6 = b.Plat(5f); b.Saw(a6);
            b.Gap(2.3f);
            float c6 = b.Plat(7f); b.ArrowRain(c6 - 1.8f); b.Dart(c6 + 2.6f);
            b.Plat(3f);
            return b.Finish();
        }

        // 16 — THE LONG SLEEP. Sleep runes under arrow timers: nap on the wrong
        // tile and the choir turns you into a pincushion.
        //
        // ONE ROOM. No doorways, no chambers, no stage to fall back to — the
        // only floor in the castle built as a single unbroken hall, because a
        // floor about never waking up should not have doors in it. One
        // coffin-shaped mercy sits halfway down; past that it's you, the runes,
        // and the length of the room.
        //
        // The single room stays — it's the point — but the hall is now long
        // enough to earn it. At five clusters this scored 2.3 on the x-ray,
        // easier than floor 6 while sitting at 16, because a room with no
        // doorways also has no gate tax and no waiting: it was pure walking.
        // Eight clusters, and the choir overlaps the runes properly.
        static Level L16()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(5f);
            float a1 = b.Plat(8f); b.SleepRune(a1 - 1f); b.ArrowRain(a1 - 1f); b.Spike(a1 + 2.4f);
            b.Gap(2.4f);
            float a2 = b.Plat(7f); b.SleepRune(a2 - 1.5f); b.Bat(a2 - 1f); b.Saw(a2 + 2f);
            b.Gap(2.3f);
            float a3 = b.Plat(6.5f); b.Checkpoint(a3 - 2f); b.Saw(a3 + 1.6f); b.Bat(a3); // the halfway mercy
            b.Gap(2.4f);
            float a4 = b.Plat(8f); b.SleepRune(a4 - 2f); b.HolyWater(a4 + 1f); b.ArrowRain(a4 + 2.6f);
            b.Gap(2.3f);
            // Past the mercy the runes start sharing tiles with the thing that
            // punishes standing still — nap here and the choir is already drawn.
            float a5 = b.Plat(9f); b.SleepRune(a5 - 2.5f); b.FlameJet(a5 - 0.5f);
            b.SleepRune(a5 + 2f); b.Pendulum(a5 + 2f);
            b.Gap(2.4f);
            float a6 = b.Plat(8f); b.SleepRune(a6 - 2f); b.Bat(a6);
            b.Gap(2.3f);
            // The one thing a hall with no doors can still surprise you with: the
            // ceiling. Everything else on this floor is a clock you can hear.
            float a7 = b.Plat(7f); b.HolyWater(a7 - 1.5f); b.Faller(a7 + 1.8f);
            b.Gap(2.4f);
            float a8 = b.Plat(10f); b.SleepRune(a8 - 3f); b.Saw(a8 - 1.5f);
            b.SleepRune(a8 + 0.5f); b.ArrowRain(a8 + 0.5f); b.SleepRune(a8 + 3f);
            b.Plat(3f);
            return b.Finish();
        }

        // 17 — FIRE SERMON. Flame jets and holy water in overlapping rhythms;
        // one crossing is made from a bobbing slab over the flames.
        // SIX cells, each a single verse: the sermon is delivered one line at a
        // time and you are never made to hear two at once.
        static Level L17()
        {
            var b = new B();
            b.Room(RoomRule.None);              // I: one jet. Learn its breath.
            float a1 = b.Plat(8f); b.FlameJet(a1);

            b.Room(RoomRule.None);              // II: two jets, out of phase
            float a2 = b.Plat(9f); b.FlameJet(a2 - 2f); b.FlameJet(a2 + 2f);

            b.Room(RoomRule.None);              // III: the water, which is worse
            float a3 = b.Plat(8f); b.HolyWater(a3 - 1.5f); b.FlameJet(a3 + 2f); b.Bat(a3);

            b.Room(RoomRule.None);              // IV: the ferry over the fire pit
            b.Plat(4f); b.MoverGap(6.8f);
            float a4 = b.Plat(6f); b.FlameJet(a4 + 1f); b.HolyWater(a4 - 1.5f);

            b.Room(RoomRule.Reverse, 0.2f);     // V: same beat, flipped hands
            b.Plat(4f);
            float a5 = b.Plat(8f); b.FlameJet(a5 - 2f); b.FlameJet(a5 + 2f);

            b.Room(RoomRule.None, 0.35f, true); // VI: the amen, behind a biting door
            b.Plat(3.5f);
            float a6 = b.Plat(6f); b.FlameJet(a6);
            b.FakeFloor(2.2f);
            float c6 = b.Plat(6f); b.HolyWater(c6 - 1f); b.Bat(c6 + 1.5f);
            b.Plat(3f);
            return b.Finish();
        }

        // 18 — GRAVEYARD SHIFT. The roulette's coffins come back mid-world,
        // guarded by fire, hidden in the dark, once across a ghost bridge.
        // THREE rooms and THREE fakes. The x-ray had this at ten unavoidable
        // deaths — the worst floor in world 2 — because six dull-brass coffins
        // stop being a lie and become a coin you're made to flip six times. It's
        // short and mean now instead of long and arbitrary.
        static Level L18()
        {
            var b = new B();
            b.Room(RoomRule.None);              // I: remember the tell — gold glows, brass doesn't
            b.Plat(5f);
            float a1 = b.Plat(8f); b.FakeCoffin(a1 - 1f); b.Spike(a1 + 2.2f);
            b.Gap(2.3f);
            float c1 = b.Plat(6f); b.FlameJet(c1); b.Pendulum(c1 + 2f);
            b.Plat(4f);

            b.Room(RoomRule.Dark, 0.14f);       // II: a bridge of faith, with a liar on the far shore
            b.Plat(4f); b.GhostFloor(7.2f);
            float a2 = b.Plat(7f); b.FakeCoffin(a2 - 1.2f); b.Saw(a2 + 2.2f);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.GrowSpike(c2 + 1f); b.Bat(c2 - 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.Flee, 0.05f);       // III: the chase, past one last liar
            float p3 = b.Plat(3.5f);
            float a3 = b.Plat(12f); b.FakeCoffin(a3 - 3f); b.FlameJet(a3 + 2f);
            b.Saw(a3 + 4.5f);
            b.Plat(5f);
            b.ExitAt(p3 + 0.6f);
            return b.FinishBare();
        }

        // 19 — WORLD EXAM II. ★ THE SECOND WALL, and the last door before the
        // Countess. Everything world 2 taught: the dark, the ceiling road, open
        // ground that lies, a crossing made with your hands flipped, and a gated
        // chase that asks for all of it at once.
        //
        // FIVE long rooms, each a whole discipline rather than a single trick.
        // The step up from floor 18 is deliberate and large (6.0 → ~8.0): a world
        // exam should be the floor people talk about, and floor 21 opens the next
        // world back down at 5.5 so the wall reads as a summit, not a new normal.
        static Level L19()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.16f);       // I: the dark, with everything it learned
            b.Plat(4f); b.NightFloor(2f);
            float a1 = b.Plat(7f); b.ArrowRain(a1 - 1.5f); b.ShiftSpike(a1 + 2.4f, a1 + 0.4f);
            b.Gap(2.3f);
            float c1 = b.Plat(4.5f); b.Faller(c1);
            b.GhostFloor(7.2f); b.Plat(4f);

            b.Room(RoomRule.None);              // II: the ceiling road — the Chapel's lesson, examined
            float a2 = b.Plat(6f); b.GravRune(a2 + 1.5f);
            b.Gap(8f);
            float c2 = b.Plat(9f); b.CeilRune(c2 - 3f); b.FlameJet(c2 + 1.5f); b.Spike(c2 + 3.5f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // III: a nap, acid and a launch in open space
            b.Plat(3.6f);
            float a3 = b.Plat(9f); b.Spring(a3 - 2.5f); b.HolyWater(a3 + 0.5f); b.SleepRune(a3 + 3f);
            b.Gap(2.3f);
            float c3 = b.Plat(6f); b.FakeDoor(c3);      // world 1 said coffins. world 2 says it again.
            b.Plat(4f);

            // IV: the exam takes your hands. One blind dart inside the reversal —
            // the reversal itself is the difficulty, and it needs honest hazards
            // around it to be read at all.
            b.Room(RoomRule.Reverse, 0.22f);
            b.Plat(4f);
            float a4r = b.Plat(7f); b.Saw(a4r - 1.5f); b.Dart(a4r + 2f);
            b.Gap(2.3f);
            float c4r = b.Plat(7f); b.Spike(c4r + 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.Flee, 0.05f, true); // V: the gated chase
            float p4 = b.Plat(3.5f);
            float a4 = b.Plat(7f); b.Pendulum(a4 - 2f); b.FlameJet(a4 + 1f);
            b.Gap(2.3f);
            float c4 = b.Plat(11f); b.Spike(c4 - 3f); b.ArrowRain(c4); b.GrowSpike(c4 + 3f);
            b.Plat(4f);
            b.ExitAt(p4 + 0.6f);
            return b.FinishBare();
        }

        // ====================================================================
        // WORLD 3 (floors 21-29) — "blood rites". Pairings get cruel:
        // contradictory instincts (stay low vs keep moving), double ferry
        // rides, portal choices that subvert last world's answers, and the
        // pink DOOR lie returns. Floor 20 is the Countess; her level slot is
        // served by Levels.BossRoom via Get(), so no dead body lives here.
        // ====================================================================

        // ── THE TROLL RHYTHM ────────────────────────────────────────────────
        // The rule the back half of the castle kept breaking, written down so it
        // stops getting broken. A troll death is a JOKE, and a joke has three
        // beats: setup, commitment, reveal. Strip the setup and you don't have a
        // harder joke, you have no joke — just a wall the player memorises.
        //
        //   1. ONE untelegraphed trap per stage. Never two in a row.
        //   2. Every stage opens on honest ground and closes on a wide landing.
        //      The landing is the beat after the punchline; without it the player
        //      is already dying again before the last death registered as funny.
        //   3. The lie fires where you've COMMITTED, not at the doorway.
        //   4. Telegraphed hazards carry the skill; blind ones carry the comedy.
        //      A floor with no telegraphed hazards asks for memory, not play.
        //
        // The x-ray (Trust Issues → Dump Difficulty X-Ray) scores this: aim ~4-6
        // expected deaths on a world-3 floor, and keep `untelegraphed` at or
        // under the stage count. Floors 21/25/28/31 were 10.7-18.7 before this.
        // ─────────────────────────────────────────────────────────────────────

        // 21 — THE CRUSHER COURT. Crushers demand you stay LOW; chandeliers and
        // fallers demand you keep moving. The floor argues with itself and you
        // pay for whichever instinct wins.
        //
        // The argument is the idea; it does not need EVERY beat to be a blind
        // kill. Crushers and chandeliers are both untelegraphed, so the old
        // version stacked two or three per stage and scored 13.0 — the player
        // never got long enough on solid ground to form the instinct the floor
        // is supposed to be punishing. One blind beat per stage, and the
        // contradiction actually reads.
        static Level L21()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: STAY LOW, taught on its own
            b.Plat(5f); b.Gap(2.3f);
            float a1 = b.Plat(5f); b.Crusher(a1);        // the blind beat
            b.Gap(2.3f);
            float c1 = b.Plat(7f); b.Saw(c1 + 1.5f);     // visible work to settle on
            b.Plat(5f);

            // S2: KEEP MOVING, the opposite lesson. World 3 OPENS here, straight
            // off the exam wall at floor 19 — so the crusher is the only thing on
            // this floor that kills blind, and everything else announces itself.
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            float a2 = b.Plat(7f); b.GrowSpike(a2 - 1.5f); b.Spike(a2 + 2f);
            b.Gap(2.4f);
            float c2 = b.Plat(6f); b.Pendulum(c2);
            b.Plat(5f);

            b.Room(RoomRule.None);              // S3: the two instincts, one after the other
            b.Plat(4.5f); b.Gap(2.3f);
            float a3 = b.Plat(5f); b.Crusher(a3);        // the blind beat
            b.Gap(2.3f);
            float c3 = b.Plat(7f); b.Pendulum(c3);
            b.Plat(4.5f);

            b.Room(RoomRule.Dark, 0.20f);       // S4: the argument in the dark — no blind trap needed
            b.Plat(4.5f); b.NightFloor(2f);
            float a4 = b.Plat(7f); b.ShiftSpike(a4 + 2f, a4 + 0.6f);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Saw(c4);
            b.Plat(4.5f);

            b.Room(RoomRule.None, 0.35f, true); // S5: both instincts, finally at once
            b.Plat(4.5f);
            float a5 = b.Plat(5f); b.Crusher(a5);        // the blind beat — duck…
            b.Gap(2.4f);
            float c5 = b.Plat(8f); b.Saw(c5 - 2f); b.FlameJet(c5 + 2f);  // …and now you can't stay low
            b.Plat(4f);
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
            float c3 = b.Plat(5f); b.Spike(c3 + 1.5f); b.GrowSpike(c3 - 1.5f);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: lit — but nothing here is calm
            b.Plat(3.5f); b.Gap(2.4f);
            float a4 = b.Plat(6f); b.Saw(a4 - 1.2f); b.Dart(a4 + 1.4f);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Pendulum(c4);
            b.Plat(4.5f);

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

            b.Room(RoomRule.None);              // S3: learn the ferry before global time pressure exists
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a3 = b.Plat(6f); b.FlameJet(a3 - 1f); b.Spike(a3 + 1.5f);
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
            float q1 = b.Plat(8f); b.Spike(q1 + 1f); b.Pendulum(q1 + 3f);
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
            float q3 = b.Plat(7f); b.Spike(q3 + 1.5f); b.Saw(q3 - 1.5f);
            b.PortalAt(p3 + 1f, -2f, q3 - 2.5f, -2f);
            b.PortalAt(p3 - 1f, -2f, p3 - 3f, -2f);

            b.Room(RoomRule.None);              // S4: the double hop over a grow-spike island
            float p4 = b.Plat(5f);
            b.Gap(6.8f);
            float m4 = b.Plat(3.5f); b.GrowSpike(m4);
            b.Gap(6.8f);
            // The landing you were relieved to reach. One blind beat on the whole
            // floor, and it waits until the last hop is behind you.
            float q4 = b.Plat(6.5f); b.Faller(q4 + 1.5f);
            b.PortalAt(p4 + 1.5f, -2f, m4 - 0.9f, -2f);
            b.PortalAt(m4 + 0.9f, -2f, q4 - 2.2f, -2f);

            b.Room(RoomRule.None, 0.35f, true); // S5: gated finale — pick fast, the door bites
            float p5 = b.Plat(6f);
            b.Gap(7.5f);
            float q5 = b.Plat(7f); b.Saw(q5 - 1f); b.Spike(q5 + 2f); b.GrowSpike(q5 - 2.5f);
            b.PortalAt(p5 + 1.8f, -2f, q5 - 2.8f, -2f);
            b.PortalAt(p5 - 0.5f, -2f, p5 - 2.7f, -2f);
            return b.Finish();
        }

        // 25 — THE HUNGRY FLOOR. The ground itself is the enemy: fake floors,
        // night floors and launch pads, until you trust nothing you stand on.
        static Level L25()
        {
            // Six fake floors in five stages scored 10.7: the ground bit so
            // often that "the ground might bite" stopped being a surprise and
            // became the baseline. One collapse per stage, with real floor
            // either side, puts the fear back — see THE TROLL RHYTHM above L21.
            var b = new B();
            b.Room(RoomRule.None);              // S1: the ground bites once, cleanly
            b.Plat(5f);
            float a1 = b.Plat(7f); b.Spike(a1 + 2f);
            b.FakeFloor(2.2f);                          // the blind beat
            b.Plat(5f);

            b.Room(RoomRule.None);              // S2: the pad, then the sky
            b.Plat(4.5f);
            float a2 = b.Plat(7f); b.Spring(a2 - 1.5f); b.GrowSpike(a2 + 2f);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.Faller(c2);        // the blind beat
            b.Plat(4.5f);

            b.Room(RoomRule.Dark, 0.18f);       // S3: in the dark, the VANISHING floor is enough
            b.Plat(4.5f); b.NightFloor(2f);
            float a3 = b.Plat(7f); b.Spike(a3 + 2f); b.Saw(a3 - 2f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.None);              // S4: bounce over honest ground, then the dart
            b.Plat(4.5f);
            float a4 = b.Plat(7f); b.Spring(a4 - 1.5f); b.Saw(a4 + 2f);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Dart(c4);          // the blind beat
            b.Plat(4.5f);

            b.Room(RoomRule.Dark, 0.15f, true); // S5: dark, a pad, and a spike that moved
            b.Plat(4f); b.NightFloor(2f);
            float a5 = b.Plat(8f); b.Spring(a5 - 2f); b.ShiftSpike(a5 + 2f, a5 + 0.6f);
            b.Gap(2.3f);
            float c5 = b.Plat(6f); b.Faller(c5 + 1f);   // the hungry floor's last word
            b.Plat(4f);
            return b.Finish();
        }

        // 26 — WAKE THE DEAD. The lullaby returns among fire and bats — every
        // nap spot has a different predator.
        static Level L26()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: rune by a flame jet
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.SleepRune(a1 - 1.5f); b.FlameJet(a1 + 0.5f); b.GrowSpike(a1 + 2.5f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: the dormitory of bats
            b.Plat(4.5f);
            float a2 = b.Plat(7f); b.SleepRune(a2 - 2f); b.Bat(a2 - 1.6f);
            b.SleepRune(a2 + 1f); b.Bat(a2 + 1.4f);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.Chandelier(c2);     // the blind beat: the ceiling wakes up too
            b.Plat(5f);

            b.Room(RoomRule.Press, 0.42f);      // S3: first crypt press — one readable flame, one jump
            b.Plat(7f);
            float a3 = b.Plat(12f); b.FlameJet(a3 + 2f); b.Spike(a3 - 2.5f);
            b.Gap(2.2f);
            float c3 = b.Plat(7f); b.Dart(c3);           // the blind beat, once the ceiling has stopped
            b.Plat(6f);

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

            b.Room(RoomRule.None);              // S2: two metronomes, then the ceiling joins in
            b.Plat(4f);
            float a2 = b.Plat(7f); b.Pendulum(a2 - 2f); b.GrowSpike(a2 + 2f);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.Faller(c2);         // the blind beat
            b.Plat(4.5f);

            b.Room(RoomRule.None);              // S3: saw + swing, then the thing that isn't a clock
            b.Plat(3.5f); b.Gap(2.4f);
            float a3 = b.Plat(6f); b.Saw(a3 - 1.5f); b.Pendulum(a3 + 0.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.Dart(c3);           // the blind beat
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
            // Nine fake doors scored 10.7. Past the third one the player simply
            // stops walking into doors, and every later door is scenery — the
            // lie taught its own counter and then kept charging for it. ONE door
            // per stage, each in a spot the last one wasn't, so the question
            // "is this one real?" stays live to the end.
            var b = new B();
            b.Room(RoomRule.None);              // S1: the door lie, taught once
            b.Plat(5f);
            float a1 = b.Plat(7f); b.FakeDoor(a1 - 1f); b.Spike(a1 + 2f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.None);              // S2: the door is past real work now
            b.Plat(4.5f);
            float a2 = b.Plat(8f); b.Saw(a2 - 2f); b.Pendulum(a2 + 1f); b.GrowSpike(a2 + 3f);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.FakeDoor(c2);
            b.Plat(4.5f);

            b.Room(RoomRule.Dark, 0.15f);       // S3: it glows in the dark. of course it does.
            b.Plat(4.5f); b.NightFloor(2f);
            float a3 = b.Plat(8f); b.FakeDoor(a3 - 2f); b.Bat(a3 + 1f); b.Spike(a3 + 3f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.Loop);              // S4: the hall loops — the door isn't the trap here
            b.Plat(4.5f);
            float a4 = b.Plat(8f); b.GrowSpike(a4 - 1.5f); b.Saw(a4 + 2f); b.Spike(a4 + 3.6f);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: chase the coffin past one last door
            float p5 = b.Plat(4f);
            float a5 = b.Plat(11f); b.FakeDoor(a5 - 2f); b.HolyWater(a5 + 2.5f);
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
            b.Gap(2.3f);
            float d2 = b.Plat(6f); b.Chandelier(d2);     // right where the ground felt safe again

            b.Room(RoomRule.Press, 0.18f);      // S3: ferry + rune + fire, ceiling falling
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a3 = b.Plat(5f); b.SleepRune(a3); b.FlameJet(a3 + 1.8f);
            b.Plat(3f);

            // S4: flipped through the pinch. ONE blind beat here, not two — a dart
            // and a collapsing floor with the wrong hands was the same death twice.
            b.Room(RoomRule.Reverse, 0.16f);
            b.Plat(3.5f);
            float a4 = b.Plat(5f); b.Dart(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(5f); b.GrowSpike(c4 - 1f); b.Spike(c4 + 1.3f);
            b.Gap(2.3f); b.Plat(3.5f);

            b.Room(RoomRule.Flee, 0.05f, true); // S5: the rites end in a chase
            float p5 = b.Plat(3.5f);
            float a5 = b.Plat(6f); b.ArrowRain(a5 - 1f); b.FlameJet(a5 + 1.5f);
            b.FakeFloor(2.2f);                  // the chase lane gives way. of course it does.
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
        //
        // Rebuilt to THE TROLL RHYTHM (see the doctrine note above L21). The old
        // version scored 18.7 on the x-ray — 18 of its 20 traps killed with no
        // tell — which is not "everything lies", it's static. A lie needs honest
        // ground either side of it or there's nothing to betray: when the floor
        // has bitten you four times in a row you stop believing any of it, the
        // joke dies, and what's left is memorisation. Same theme, same trap
        // vocabulary, but each stage now tells ONE lie off a run-up you trusted.
        static Level L31()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: honest ground, then the ground bites once
            b.Plat(5f);
            float a1 = b.Plat(7f); b.Spike(a1 + 2f);      // fair, visible, builds the rhythm
            b.FakeFloor(2f);                             // THE lie
            b.Plat(5.5f);                                // wide landing: the punchline needs a beat

            b.Room(RoomRule.None);              // S2: the pink door, framed by real work
            b.Plat(4.5f);
            float a2 = b.Plat(7f); b.Pendulum(a2 - 1.5f); b.LateSpike(a2 + 2f);
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.FakeDoor(c2);       // THE lie — doors are exits everywhere but here
            b.Plat(4.5f);

            b.Room(RoomRule.Dark, 0.20f);       // S3: no blind lie at all — the dark IS the hazard
            b.Plat(4f); b.NightFloor(2f);
            float a3 = b.Plat(7f); b.ShiftSpike(a3 + 2f, a3 + 0.6f);
            b.Gap(2.3f); b.Plat(5f);

            b.Room(RoomRule.None);              // S4: sunbeam and saw seen coming, then the sky drops
            b.Plat(4.5f);
            float a4 = b.Plat(7f); b.FlameJet(a4 - 1.5f); b.Saw(a4 + 2f);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Faller(c4);         // THE lie
            b.Plat(4.5f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the finale — one dull cross among the real work
            b.Plat(4f);
            float a5 = b.Plat(8f); b.GrowSpike(a5 - 2f); b.Saw(a5 + 2f);
            b.Gap(2.3f);
            float c5 = b.Plat(6f); b.FakeCoffin(c5);     // THE lie — brass cross, not gold. that's the tell
            b.Plat(4f);
            return b.Finish();
        }

        // 32 — THE BLACK MASS. Every stage is dark. The candles never really
        // come back; the doorway relights are the only mercy left.
        static Level L32()
        {
            var b = new B();
            // Five dark stages with four traps in them scored 3.8 — the same as
            // floor 7, in the second-to-last world. Darkness is atmosphere, not
            // difficulty: it only costs you when there is something in it.
            b.Room(RoomRule.Dark, 0.25f);       // S1: the service begins
            b.Plat(5f); b.NightFloor(2f);
            float a1 = b.Plat(6f); b.Saw(a1 - 1f); b.GrowSpike(a1 + 1.5f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // S2: bridge + faller + moving spike
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a2 = b.Plat(5f); b.Faller(a2 - 0.5f); b.ShiftSpike(a2 + 1.5f, a2 + 0.6f);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: vanish, bat, bridge
            b.Plat(3.5f); b.NightFloor(2f);
            float a3 = b.Plat(4f); b.Bat(a3);
            b.GhostFloor(7.2f);
            float c3 = b.Plat(5f); b.Spike(c3 + 1f); b.Saw(c3 - 1.5f);

            b.Room(RoomRule.Dark, 0.13f);       // S4: the swinging dark, and something thrown through it
            b.Plat(3.5f); b.NightFloor(2.2f);
            float a4 = b.Plat(4f); b.ShiftSpike(a4 + 1.3f, a4 + 0.5f);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Pendulum(c4 - 1f); b.Dart(c4 + 1.5f);
            b.Plat(3f);

            b.Room(RoomRule.Dark, 0.1f, true);  // S5: communion — and the aisle gives way
            b.Plat(3.5f); b.GhostFloor(7.2f);
            float a5 = b.Plat(3.5f); b.ShiftSpike(a5 + 1f, a5 + 0.3f);
            b.NightFloor(2f); b.Plat(3f); b.FakeFloor(2f); b.Plat(3f);
            return b.Finish();
        }

        // 33 — IRON CHOIR. ★ THE THIRD WALL. Every stage door is a portcullis,
        // and behind most of them the ceiling is already coming down. Six gates,
        // six rhythms — the longest floor in world 4 and the meanest, because
        // the one thing it never gives you is TIME to stand still and look.
        //
        // The wall is built out of stolen control, not extra hazards: the gates
        // decide when you may move, the presses decide how long you may take,
        // and the fifth verse flips your hands while both are still true. One
        // blind beat per verse, honest ground either side of each (~8.8 on the
        // x-ray; floor 32 before it sits at 6.8 and floor 34 after it at 7.2).
        static Level L33()
        {
            var b = new B();
            b.Room(RoomRule.None, 0.35f, true); // S1: the first verse — gate, spike, and a block that drops
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.Spike(a1 + 1f);
            b.Gap(2.3f);
            float c1 = b.Plat(6f); b.Crusher(c1);        // the blind beat: this choir wants you LOW
            b.Plat(5f);

            b.Room(RoomRule.Press, 0.25f, true);// S2: gate into the press lane, and the lane lies
            b.Plat(3.5f);
            float a2 = b.Plat(10f); b.HolyWater(a2 - 1.5f); b.Spike(a2 + 1.5f);
            b.FakeFloor(2f);                            // the blind beat
            b.Plat(5f);

            b.Room(RoomRule.None, 0.35f, true); // S3: gate, saw, faller
            b.Plat(3.5f); b.Gap(2.4f);
            float a3 = b.Plat(5f); b.Saw(a3);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.Faller(c3);         // the blind beat
            b.Plat(4f);

            b.Room(RoomRule.Press, 0.2f, true); // S4: gate, ferry, fire — under the press
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a4 = b.Plat(7f); b.FlameJet(a4 - 1f); b.Saw(a4 + 2f);
            b.Plat(4f);

            // S5: the verse where the choir turns on the organist. Gated, and
            // your hands are wrong for all of it. The hazards are deliberately
            // ones you can SEE — the reversal is the difficulty here.
            b.Room(RoomRule.Reverse, 0.2f, true);
            b.Plat(4f);
            float a5 = b.Plat(7f); b.Pendulum(a5 - 1.5f); b.Spike(a5 + 2f);
            b.Gap(2.3f);
            float c5 = b.Plat(6f); b.Dart(c5);           // the blind beat
            b.Plat(4f);

            b.Room(RoomRule.Press, 0.18f, true);// S6: the choir sings all at once
            b.Plat(3.5f);
            float a6 = b.Plat(6f); b.Spike(a6 - 1f); b.Saw(a6 + 1.3f);
            b.FakeFloor(2f);                            // the blind beat
            b.Plat(4f);
            return b.Finish();
        }

        // 34 — FLOOD OF FIRE. Jets and acid own the ground; the ferry is the
        // only dry road, and even the dark burns.
        static Level L34()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: three burners on one beat
            b.Plat(4.5f);
            float a1 = b.Plat(7f); b.FlameJet(a1 - 2f); b.HolyWater(a1 + 1f);
            b.Gap(2.3f); b.Plat(4f);

            b.Room(RoomRule.None);              // S2: ferry over the flood
            b.Plat(3.5f); b.MoverGap(6.8f);
            float a2 = b.Plat(6f); b.FlameJet(a2 - 1.5f); b.FlameJet(a2 + 1.5f);
            b.Plat(3.5f);

            b.Room(RoomRule.Dark, 0.16f);       // S3: fire you can only see when it flares
            b.Plat(4f); b.NightFloor(2f);
            float a3 = b.Plat(6f); b.FlameJet(a3 - 1f); b.HolyWater(a3 + 1.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.Faller(c3);        // the blind beat, on the far shore
            b.Plat(3.5f);

            b.Room(RoomRule.None);              // S4: duck the block, cross the burners
            b.Plat(3.5f);
            float a4 = b.Plat(4f); b.Crusher(a4);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.FlameJet(c4 - 1.5f); b.HolyWater(c4 + 1f);
            b.Plat(3f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the flood crests
            b.Plat(3.5f);
            float a5 = b.Plat(8f); b.FlameJet(a5 - 2.5f); b.HolyWater(a5 - 0.5f);
            b.Spike(a5 + 3.2f);
            b.FakeFloor(2f); b.Plat(3.5f);
            return b.Finish();
        }

        // 35 — SPIDER'S PATIENCE. Three clocks tick at once — grow spikes,
        // arrow timers, sleep runes. Rushing and waiting both kill.
        static Level L35()
        {
            var b = new B();
            // Thirteen clocks and one lie scored 5.0 — a floor you solve by
            // waiting. The clocks are the SETUP; each stage now has one thing in
            // it that no amount of patience answers.
            b.Room(RoomRule.None);              // S1: two clocks, then the ceiling
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.GrowSpike(a1 - 1f); b.ArrowRain(a1 + 1f);
            b.Gap(2.3f);
            float c1 = b.Plat(6f); b.Faller(c1);         // the blind beat
            b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: the double metronome
            b.Plat(4f);
            float a2 = b.Plat(7f); b.GrowSpike(a2 - 2f); b.GrowSpike(a2 + 2f);
            b.Gap(2.3f); b.Plat(4.5f);

            b.Room(RoomRule.None);              // S3: nap between the clocks
            b.Plat(4f);
            float a3 = b.Plat(7f); b.SleepRune(a3 - 1f); b.GrowSpike(a3 + 1f); b.ArrowRain(a3 + 3f);
            b.Gap(2.3f);
            float c3 = b.Plat(6f); b.Chandelier(c3);     // the blind beat
            b.Plat(4f);

            b.Room(RoomRule.Dark, 0.15f);       // S4: clocks in the dark, and one thing thrown
            b.Plat(4f); b.NightFloor(2f);
            float a4 = b.Plat(6f); b.GrowSpike(a4 - 1f); b.ArrowRain(a4 + 1.5f);
            b.Gap(2.3f);
            float c4 = b.Plat(6f); b.Dart(c4);           // the blind beat
            b.Plat(4f);

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
            float q2 = b.Plat(8f); b.Saw(q2 + 1.5f); b.Spike(q2 - 1.5f);
            b.PortalAt(p2 - 0.5f, -2f, q2 - 3f, -2f);
            b.PortalAt(p2 + 2f, -2f, p2 - 2.9f, -2f);

            b.Room(RoomRule.Dark, 0.12f);       // S3: find the key in the dark
            b.Plat(4f); b.NightFloor(2f);
            float p3 = b.Plat(5f);
            b.Gap(7.5f);
            float q3 = b.Plat(8f); b.Spike(q3 + 1.5f); b.Saw(q3 + 3.2f);
            b.Gap(2.3f);
            float r3 = b.Plat(5f); b.Faller(r3);        // the blind beat, on the door you chose right
            b.Plat(3.5f);
            b.PortalAt(p3 + 1f, -2f, q3 - 3f, -2f);
            b.PortalAt(p3 - 1f, -2f, p3 - 3f, -2f);

            b.Room(RoomRule.None);              // S4: the two-hop over the island
            float p4 = b.Plat(5f);
            b.Gap(6.8f);
            float m4 = b.Plat(3.5f); b.GrowSpike(m4);
            b.Gap(6.8f);
            float q4 = b.Plat(6f); b.Spike(q4 + 1.8f);
            b.PortalAt(p4 + 1.5f, -2f, m4 - 0.9f, -2f);
            b.PortalAt(m4 + 0.9f, -2f, q4 - 1.5f, -2f);

            b.Room(RoomRule.None, 0.35f, true); // S5: the last lock
            float p5 = b.Plat(6f);
            b.Gap(7.5f);
            float q5 = b.Plat(7f); b.Pendulum(q5 - 1f); b.Spike(q5 + 2f);
            b.Gap(2.3f);
            float r5 = b.Plat(6f); b.Chandelier(r5); b.GrowSpike(r5 + 2.2f);
            b.Plat(3.5f);
            b.PortalAt(p5 + 1.8f, -2f, q5 - 2.8f, -2f);
            b.PortalAt(p5 - 0.5f, -2f, p5 - 2.7f, -2f);
            return b.Finish();
        }

        // 37 — DEATH'S PENDULUM. ★ THE LAST WALL. Swings, launch pads and
        // crushers — three different ways of being moved somewhere you did not
        // choose — and two whole rooms where your hands belong to the castle.
        //
        // This is the hardest floor in the game that isn't a boss (~9.6 on the
        // x-ray), and it earns it the same way floor 33 does: SIX rooms, one
        // blind beat each, honest visible work either side of every one. Nothing
        // here is a guess. It's just that for most of the floor, the thing
        // deciding where you end up isn't you. Floor 38 steps back down to 8.0.
        static Level L37()
        {
            var b = new B();
            b.Room(RoomRule.None);              // S1: the swing and the pad, then the ceiling
            b.Plat(4.5f);
            float a1 = b.Plat(6f); b.Pendulum(a1 - 1f); b.Spring(a1 + 1.5f);
            b.Gap(2.3f);
            float c1 = b.Plat(6f); b.Chandelier(c1);     // the blind beat
            b.Plat(4.5f);

            b.Room(RoomRule.None);              // S2: duck, then thread the swing
            b.Plat(3.5f); b.Gap(2.3f);
            float a2 = b.Plat(4f); b.Crusher(a2);        // the blind beat
            b.Gap(2.3f);
            float c2 = b.Plat(6f); b.Pendulum(c2 - 1.5f); b.Spring(c2 + 1.5f);
            b.Plat(4f);

            b.Room(RoomRule.None);              // S3: pad into swing into lie
            b.Plat(3.5f);
            float a3 = b.Plat(6f); b.Spring(a3 - 1.5f); b.Pendulum(a3 + 0.5f);
            b.FakeFloor(2f);                            // the blind beat
            b.Plat(5f);

            b.Room(RoomRule.Reverse, 0.16f);    // S4: flipped hands under the swings
            b.Plat(3.5f);
            float a4 = b.Plat(6f); b.Pendulum(a4 - 1.5f); b.Pendulum(a4 + 1.5f);
            b.Gap(2.3f);
            float c4 = b.Plat(5f); b.Dart(c4);           // the blind beat
            b.Plat(3.5f);

            b.Room(RoomRule.Reverse, 0.2f);     // S5: still flipped, now bouncing
            b.Plat(3.5f);
            float a5 = b.Plat(6f); b.Spring(a5 - 1f); b.Saw(a5 + 1.8f);
            b.Gap(2.3f);
            float c5 = b.Plat(5f); b.Faller(c5);         // the blind beat
            b.Plat(4f);

            b.Room(RoomRule.None, 0.35f, true); // S6: the full mechanism, hands returned
            b.Plat(3.5f);
            float a6 = b.Plat(4f); b.Crusher(a6);        // the blind beat
            b.Gap(2.3f);
            float c6 = b.Plat(7f); b.Pendulum(c6 - 2f); b.Spring(c6 + 1.5f);
            b.Plat(3.5f);
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
            b.Gap(2.3f);
            float c1 = b.Plat(6f); b.Crusher(c1);        // the blind beat, with the wrong hands
            b.Plat(4.5f);

            b.Room(RoomRule.Loop);              // S2: the hall lies
            b.Plat(3.5f);
            float a2 = b.Plat(7f); b.GrowSpike(a2 - 1f); b.Dart(a2 + 1.5f);
            b.Gap(2.3f); b.Plat(6f);

            b.Room(RoomRule.Dark, 0.14f);       // S3: the candles lie
            b.Plat(3.5f); b.NightFloor(2f); b.GhostFloor(7.2f);
            float a3 = b.Plat(5f); b.Spike(a3 + 1f); b.Saw(a3 - 1.5f);
            b.Gap(2.3f);
            float c3 = b.Plat(5f); b.Chandelier(c3);     // the blind beat
            b.Plat(3f);

            b.Room(RoomRule.Press, 0.18f);      // S4: the ceiling lies
            b.Plat(4f);
            float a4 = b.Plat(9f); b.HolyWater(a4 - 1.5f); b.Spike(a4 + 1f);
            b.FakeFloor(2f); b.Plat(3f);

            b.Room(RoomRule.Reverse, 0.12f, true); // S5: all of them, backwards
            b.Plat(3.5f);
            float a5 = b.Plat(6f); b.Pendulum(a5 - 1f); b.Saw(a5 + 1.8f);
            b.Gap(2.3f);
            float c5 = b.Plat(5f); b.Dart(c5);           // the blind beat
            b.Gap(2.3f); b.Plat(3.5f);
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
            // A coffin, at the exact moment you most want the floor to be over,
            // with the ceiling already coming down. Brass cross. It always was.
            b.Gap(2.3f);
            float c3 = b.Plat(6f); b.FakeCoffin(c3);
            b.Plat(3f);

            b.Room(RoomRule.None);              // S4: the portal chain, one last time
            float p4 = b.Plat(5f);
            b.Gap(6.8f);
            float m4 = b.Plat(3.5f); b.GrowSpike(m4);
            b.Gap(6.8f);
            float q4 = b.Plat(6.5f); b.Spike(q4 + 1f); b.Faller(q4 + 2.8f);
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
