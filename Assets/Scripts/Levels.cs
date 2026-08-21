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
        // ---- THE ROOM ITSELF (Betrayal.cs) ----
        // These consume cursor space like Plat() does, because they ARE the floor.
        // A betrayal placed as an overlay on existing ground would be a second
        // object in the same place; the whole point is that there is nothing else
        // there — the ground you are standing on is the trap.

        // MINIMUM WIDTH OF A BETRAYAL FLOOR, and the reason the first pass of these
        // felt like nothing. Run speed is 7.5 u/s, so a 2.6-wide slab is crossed in
        // 0.35s and a 3-wide one in 0.40s — less time than the traps take to fire.
        // Players reported walking straight over floors that were, on paper, in the
        // middle of collapsing under them. Retuning the traps to fire in ~0.25s only
        // half-fixed it: the slab also has to be wide enough that finishing the
        // crossing isn't an option. At 4.2 the crossing takes 0.56s, so a floor that
        // commits in 0.30s catches you barely half way — decisively, every time.
        //
        // Still well inside a 5.6-unit maximum jump, so every one of them remains
        // clearable by a player who reads the creak on approach. Enforced here in
        // the builder rather than at ~60 call sites so no floor can opt out.
        public const float BetrayalMinW = 4.2f;

        /// <summary>Floor that hinges under your weight and pours you off.</summary>
        public float TiltFloor(float w = 3f)
        { w = Mathf.Max(w, BetrayalMinW); float cx = cur + w / 2f; T(TrapType.TiltFloor, cx, -3f, w, 0.6f); cur += w; return cx; }

        /// <summary>Floor that slides out sideways, opening a pit where you stand.</summary>
        public float SlideFloor(float w = 3f)
        { w = Mathf.Max(w, BetrayalMinW); float cx = cur + w / 2f; T(TrapType.SlideFloor, cx, -3f, w, 0.6f); cur += w; return cx; }

        /// <summary>The trapdoor: falls away beneath you into the dark. Lethal.</summary>
        public float DropFloor(float w = 3.2f)
        { w = Mathf.Max(w, BetrayalMinW); float cx = cur + w / 2f; T(TrapType.DropFloor, cx, -3f, w, 0.6f); cur += w; return cx; }

        /// <summary>Ground that lifts you into the ceiling and presses. Get off it.</summary>
        public float RiseFloor(float w = 3f)
        { w = Mathf.Max(w, BetrayalMinW); float cx = cur + w / 2f; T(TrapType.RiseFloor, cx, -3f, w, 0.6f); cur += w; return cx; }

        /// <summary>
        /// A wall parked off-lane that drives in as you pass. It does NOT advance
        /// the cursor — it's furniture above a floor you lay separately — and the
        /// spec smuggles the throw distance through the size: h = how far it
        /// travels, and a negative w means it comes from the left instead.
        /// </summary>
        public void SlamWall(float x, float throwDist = 3.6f, bool fromLeft = false)
            => T(TrapType.SlamWall, x, -1.9f, fromLeft ? -0.8f : 0.8f, throwDist);

        /// <summary>A row of ceiling teeth that fire down in sequence as you pass under.</summary>
        public void CeilingVolley(float x, int teeth = 5)
            => T(TrapType.CeilingVolley, x, 0f, teeth, 1f);

        /// <summary>The coffin that backs away when you reach for it. Twice, then it gives up.</summary>
        public void ShyExit(float x) => T(TrapType.ShyExit, x, -2f, 1.4f, 1.8f);

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

        // ====================================================================
        // ENDINGS THAT LIE
        //
        // Thirty-eight of forty floors closed with Finish() — an honest, working
        // coffin — so the castle betrayed the ROAD constantly and the DESTINATION
        // almost never. That is backwards for this genre. The whole engine of a
        // troll platformer is certainty: a trap only lands if the player was sure,
        // and nothing makes a player sure like seeing the goal. Betraying the
        // journey makes people careful. Betraying the goal is what makes them
        // shout, and shouting is what gets a game shared.
        //
        // Both of these were buildable from day one — FakeCoffin and FakeDoor have
        // always been here, fully working, and were placed a combined two times.
        // These just make the two good shapes into one-line endings so a floor can
        // pick its lie the same way it picks its hazards.
        // ====================================================================

        /// <summary>
        /// THE ONE YOU RUN AT ISN'T IT. A decoy coffin sits exactly where the floor
        /// looks like it ends — same silhouette, dull brass cross instead of glowing
        /// gold, and an invisible kill zone inside. The real one is further on, past
        /// one more gap you had no reason to think was there.
        ///
        /// The tell is honest and it is on screen, so the second time through this
        /// is free. The first time it is the best joke the floor has.
        /// </summary>
        public Level FinishDecoy()
        {
            Gap(2.5f);
            float decoy = Plat(4f);
            FakeCoffin(decoy);
            Gap(2.6f);
            float endc = Plat(4f);
            CloseRoom();
            T(TrapType.RealExit, endc, -2f, 1.4f, 1.8f);
            L.CamMinX = -1.5f;
            L.CamMaxX = Mathf.Max(-1.5f, cur - 10f);
            return L;
        }

        /// <summary>
        /// THE LAST STEP LIES. The coffin is real, lit, reachable, and everything it
        /// appears to be. The slab you have to cross to touch it is not — it drops
        /// the moment you commit.
        ///
        /// Deliberately the gentler of the two: nothing about the goal is fake, so a
        /// player who has learned to read a betraying floor already owns the answer.
        /// It punishes the sprint-for-the-door reflex specifically, which is the one
        /// reflex every player has by floor ten.
        /// </summary>
        public Level FinishTrapdoor()
        {
            Gap(2.5f);
            DropFloor(3.2f);
            float endc = Plat(4f);
            CloseRoom();
            T(TrapType.RealExit, endc, -2f, 1.4f, 1.8f);
            L.CamMinX = -1.5f;
            L.CamMaxX = Mathf.Max(-1.5f, cur - 10f);
            return L;
        }
    }

    public static class Levels
    {
        public static int Count => 40;

        /// <summary>
        /// Underside of a roomed floor's ceiling (the slab sits at 3.4, 0.6 tall).
        /// The rising press and the ceiling volley both need it, and they're built
        /// in GameRoot where B's own constant isn't reachable.
        /// </summary>
        public const float CeilUnderside = 3.1f;

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

        /// <summary>
        /// THE FIRST NIGHT — the tutorial floor.
        ///
        /// The first version of this taught the wrong game. It spent 18 units (2.2
        /// seconds of holding one direction, nothing happening) teaching RUN, then
        /// showed a spike, then one ambush — and never once showed the thing this
        /// game is actually about, which is that the GROUND lies to you. A player
        /// finished it knowing how to jump over furniture and knowing nothing about
        /// floors that tip, slide or simply leave.
        ///
        /// This one halves the dead opening and spends the space it saves on the
        /// real vocabulary. Five lessons, ending on the two that matter: a floor
        /// that tips you off, and a floor that isn't there any more. Both kill.
        /// That's the honest introduction — the castle does exactly this, forever,
        /// and better to learn it here where the retry is instant.
        ///
        /// Tutorial.cs reads these x positions to know when to speak, so moving a
        /// platform here without moving its constant strands a caption.
        /// </summary>
        public const float TutorialJumpX  = -1.5f;   // right edge of the opening run
        public const float TutorialSpikeX = 7.2f;    // the spike in plain sight
        public const float TutorialLieX   = 18.5f;   // the ambush spike
        public const float TutorialTiltX  = 24.6f;   // the floor that tips
        public const float TutorialDropX  = 31.9f;   // the floor that leaves
        public static Level Tutorial()
        {
            var b = new B();          // cursor starts at spawn - 1.5 = -11.5
            b.Plat(10f);              // -11.5 -> -1.5   learn to run (was 18: too long)
            b.Gap(2.2f);              // one honest jump, no hazard attached
            float a = b.Plat(9f);     // 0.7 -> 9.7
            b.Spike(a + 2f);          // 7.2  a spike you can see from a long way off
            b.Gap(2.2f);
            float c = b.Plat(8f);     // 11.9 -> 19.9
            b.LateSpike(c + 2.6f);    // 18.5 the first thing that lies to you
            // The last two lessons both kill, and they are the first killing floors
            // a new player ever meets. Without this they would replay the run, the
            // jump and both spikes every attempt — the exact "levels are too long"
            // complaint, aimed at the one floor that has to feel welcoming.
            float d = b.Plat(3.2f);
            b.Checkpoint(d);          // 21.5
            b.TiltFloor(3f);          // 24.6 and now the GROUND lies
            b.Plat(4.2f);
            b.DropFloor(3.2f);        // 31.9 …and now it simply leaves
            b.Plat(6f);
            return b.Finish();        // the coffin
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

                // GROUND THAT BETRAYS. Endless used to be an object-placer: every
                // platform was honest stone with a hazard standing on it, so a long
                // run was the same sentence a hundred times with the noun swapped.
                // Roughly one landing in five is now a slab that tips, leaves, or
                // drops (Betrayal.cs) — never straight after a blind beat, never on
                // a glide landing (you need somewhere solid to refill the meter),
                // and never carrying a hazard as well.
                // GROUND THAT BETRAYS, FROM THE FIRST CHUNK.
                //
                // This was gated at difficulty >= 1 and started at a 17% roll, so
                // the opening of a run was all honest stone and the mode's best
                // idea — the floor itself turning on you — barely showed up until
                // a player was already several chunks deep. The whole reason these
                // exist is that they are the ONLY hazard here not answered by
                // jumping, which makes them exactly what the opening was missing.
                //
                // Ungated and opened at 22%, so roughly one landing in four early
                // on. They telegraph on approach (Betrayal.cs), so this raises the
                // variety of the opening without raising its unfairness.
                bool betray = !longGap && !blindHere && !lastWasBlind
                              && rng.Next(100) < 22 + difficulty * 3;
                if (betray)
                {
                    switch (rng.Next(3))
                    {
                        case 0:  b.TiltFloor(3f); break;
                        case 1:  b.SlideFloor(3f); break;
                        default: b.DropFloor(3.2f); break;
                    }
                    b.Plat(2.6f + (float)rng.NextDouble() * 0.8f);   // somewhere to land after it
                    lastWasBlind = false;
                    continue;
                }

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

        /// <summary>
        /// What the platform you land on is MADE of. Blood Moon used to have only
        /// one answer (stone) plus a lying floor you fell through, so every beat in
        /// the mode was "an object is on this platform, jump it". These are the
        /// same betrayals the Castle uses (Betrayal.cs), available as ground.
        ///
        /// The mode has no ceiling — it is a flight mode, so the rooms are open —
        /// which is why the rising press and the ceiling volley are not here: both
        /// need a ceiling to press you into.
        /// </summary>
        enum Ground { Stone, Tilt, Slide, Drop }

        struct Beat
        {
            public int gap;              // how you arrive: walk, glide, or a lying floor
            public Ground ground;        // …and what you arrive ON
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
        // Ground that betrays. These never carry a hazard as well: the platform is
        // already the trap, and a spike on a slab that is tipping you off it is two
        // deaths arriving at once with one answer between them.
        static Beat Tilt()  => new Beat { gap = GapNormal, ground = Ground.Tilt };
        static Beat Slide() => new Beat { gap = GapNormal, ground = Ground.Slide };
        static Beat Drop()  => new Beat { gap = GapNormal, ground = Ground.Drop };

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
                    Tilt(),                              // NEW: the ground is not on your side
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
                    Slide(),                             // NEW: the floor leaves sideways
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
                    Drop(),                              // NEW: it takes you down with it
                    Walk(TrapType.GrowSpike, TrapType.SpikeStatic),
                    Rest(),
                    Walk(TrapType.Chandelier),           // NEW: blind ceiling drop, solo
                    Tilt(),
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
                    Slide(),
                    Glide(),
                    Walk(TrapType.FlameJet),             // NEW: telegraphed eruption
                    Tilt(),
                    Walk(TrapType.SpikeStatic, TrapType.LateSpike),
                    Walk(TrapType.Pendulum),
                    Rest(),
                    Lie(),                               // blind
                    Drop(),
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
                    Tilt(),
                    Glide(),
                    Walk(TrapType.Saw, TrapType.SpikeStatic),
                    Slide(),
                    Walk(TrapType.FlameJet, TrapType.GrowSpike),
                    Lie(),                               // blind
                    Rest(),
                    Walk(TrapType.Reverse),              // the climax — one, only one
                    Drop(),
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
            b.Plat(5f);     // the door: empty and honest — always

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
                // Trimmed from 5.6/5.0/4.2. The old pads made night 5 a 136-unit,
                // eighteen-second night whose longest empty stretch was 12.4 units —
                // the exact corridor problem the Castle rooms were rebuilt to kill,
                // just wearing a red moon. A landing pad only needs enough room to
                // land, read and commit; past that it is walking.
                float w = (beat.rest ? 4.8f : beat.gap == GapGlide ? 4.4f : 3.4f)
                        + (float)rng.NextDouble() * 0.7f;

                // Betraying ground is built NARROWER than honest ground. A tipping
                // slab six units wide is a room you can stand in the middle of and
                // wait out; at three it is a decision.
                float p = beat.ground switch
                {
                    Ground.Tilt  => b.TiltFloor(3f),
                    Ground.Slide => b.SlideFloor(3f),
                    Ground.Drop  => b.DropFloor(3.2f),
                    _            => b.Plat(w),
                };

                if (beat.rest) { b.Checkpoint(p); continue; }
                if (beat.ground != Ground.Stone) continue;   // the ground is the trap
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

            // THE OPENING USED TO BE A SPIKE GALLERY.
            //
            // Endless difficulty is min(7, 1 + chunk/2), so the first two chunks of
            // EVERY run — the only part most players ever see — drew from exactly
            // three entries, two of which were the same plain spike. The third,
            // LateSpike, is answered by the same input. So the mode opened with
            // roughly a hundred metres of "jump the thing", which is why it reads as
            // nothing but jumping: for the stretch that decides whether anyone keeps
            // playing, that is literally all it was.
            //
            // The set below opens WIDER but no harder. Every early addition is a
            // hazard the player can see coming and answers with a DIFFERENT verb:
            // GrowSpike is wait-then-go, Saw and Pendulum are time-the-swing. None
            // of them is blind, so the early game gets more interesting without
            // getting less fair — the blind-trap budget above is untouched.
            var l = new List<TrapType> { TrapType.SpikeStatic, TrapType.GrowSpike };
            if (d >= 1) { l.Add(TrapType.LateSpike); l.Add(TrapType.Saw); }
            if (d >= 2) { l.Add(TrapType.Dart); l.Add(TrapType.Crusher); l.Add(TrapType.Pendulum);
                          l.Add(TrapType.SlamWall); }                                      // the sides join in
            if (d >= 3) { l.Add(TrapType.Faller);
                          l.Add(TrapType.FlameJet); l.Add(TrapType.Chandelier); }        // vampire traps
            if (d >= 4) { l.Add(TrapType.Surprise); l.Add(TrapType.WarpBack);
                          l.Add(TrapType.HolyWater);
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
            t == TrapType.Faller || t == TrapType.Chandelier ||
            // A slam wall owns the whole lane while it is out. Anything sharing the
            // platform has to be cleared in whatever sliver of time the wall leaves,
            // and the pairing logic cannot reason about that — so it goes alone, for
            // the same reason the crusher does.
            t == TrapType.SlamWall;

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
                // THE WALLS JOIN IN.
                //
                // This switch had no case for SlamWall, so the generator could not
                // place one even if the pool offered it — every procedural hazard
                // in the game came at the player from the floor or the ceiling, and
                // "jump it" or "don't be under it" covered the entire mode. A wall
                // that drives in from off-lane is the only threat here answered by
                // a horizontal decision: you go through the gap early or you wait
                // and go behind it. That is a different verb, which is the whole
                // point of adding it.
                //
                // It shoves rather than kills (see GameRoot's SlamWall case) and is
                // held at 2.2 tall so it can always be vaulted, so it can never
                // seal a corridor on a player who arrived a beat late.
                case TrapType.SlamWall: b.SlamWall(p, 3.4f, (p * 7f) % 2f < 1f); break;
                default: b.Spike(p); break;
            }
        }


        // ── THE CASTLE, REBUILT AS ROOMS ────────────────────────────────────
        //
        // WHY EVERY FLOOR BELOW IS SHORT.
        //
        // The old castle measured 115.7 units on the median floor. At the run
        // speed of 7.5 u/s that is 15.4 SECONDS OF HOLDING RIGHT on a perfect,
        // death-free clear — and 46.6% of that distance was ground with nothing
        // on it at all. The median floor's longest empty stretch was 13 units;
        // floor 2's was 25.4, which is three and a half seconds of a rage
        // platformer in which the game asks the player for nothing.
        //
        // That is the whole "it's just walking, dodge one or two traps, next
        // level" complaint, and no amount of new hazards fixes it while the
        // corridor stays that long: adding traps to a 116-unit hall just spreads
        // them further apart. So the corridor is gone. Every floor here is ONE
        // ROOM you can very nearly see all of at once — the camera's half-height
        // is 5.6, which is about 20 units of width at 16:9 — and it is over in
        // three to six seconds when you play it well.
        //
        // THE CONTRACT EVERY FLOOR BELOW KEEPS:
        //
        //   • 24–40 units end to end. No exceptions, including the ★ walls.
        //   • A BEAT EVERY 4–6 UNITS. Nothing on this floor is further than
        //     about half a second from the next thing that wants something.
        //   • NO EMPTY STRETCH OVER 6 UNITS, ever — that was the old floor's
        //     defining feature and it is the one thing that cannot come back.
        //   • ONE IDEA PER FLOOR, named in its comment. The idea is introduced
        //     alone, then complicated exactly once, then the room ends. A floor
        //     that says two things says neither.
        //   • THE ROOM IS THE TRAP as often as the furniture is. Tilting,
        //     sliding, dropping and rising floors (Betrayal.cs) are first-class
        //     here, because the old vocabulary was 68 spikes, 62 saws and 41
        //     pendulums — three objects that all mean "press jump".
        //
        // THE RAMP. Same shape as before, same ★ walls, measured the same way
        // (roughly "expected deaths on a first clear"):
        //
        //   floors 1-8    the teaching climb — one new verb each, nothing cruel
        //   floor  9      ★ THE FIRST WALL — flipped hands over moving ground
        //   floors 10-18  world 2: every verb returns, and starts pairing up
        //   floor  19     ★ THE SECOND WALL — the exam before the Countess
        //   floors 21-29  world 3: the room lies about its own shape
        //   floors 31-39  the last night, with walls at 33 and 37
        //
        // Re-run `Trust Issues → Dump Difficulty X-Ray` after ANY edit here.
        // ─────────────────────────────────────────────────────────────────────

        // ====================================================================
        // WORLD 1 (floors 1-9) — LEARNING WHAT THE CASTLE IS.
        // Each of these teaches exactly one verb and then stops talking. They
        // are the shortest rooms in the game (24-30 units) because a lesson the
        // player has already understood is just a corridor.
        // ====================================================================

        // 1 — TRUST NOTHING. The handshake. One lie, one honest spike, the door.
        // Deliberately the smallest room in the castle: 24 units, about three
        // seconds, and the only thing it teaches is that the floor can be absent.
        // ====================================================================
        // WORLD 1 (floors 1-10) — ONE VERB AT A TIME.
        //
        // The first cut of this world introduced a brand-new killer on floors 2,
        // 3, 4, 5, 6, 7 AND 8 — seven unfamiliar mechanics in seven floors, to a
        // player still learning which thumb does what. Testers said the obstacles
        // "just appear" and that the opening had "no progression", and they were
        // describing exactly that: variety was being mistaken for teaching.
        //
        // Level Devil introduces ONE new idea per door of five levels. This world
        // now introduces four across ten floors, and every one of them follows the
        // same three-beat shape:
        //
        //   SHOW   the mechanic alone on its own floor, with nothing else that can
        //          kill you and wide ground on both sides, so the first time you
        //          meet it you can watch it work (and if it kills you, you KNOW
        //          what did it).
        //   USE    it again a floor later, this time paired with something you
        //          already know, so it becomes a tool instead of a surprise.
        //   MIX    it into the gatehouse recap at floor 10.
        //
        // SlamWall, RisePress and the lying exit are deliberately NOT here. They
        // debut in World 2, which was already using them without ever teaching
        // them.
        // ====================================================================

        // 1 — HONEST GROUND, ONE LIE. Walk, jump, and learn the promise of the
        // game in a single step: the floor is not necessarily a floor.
        static Level L1()
        {
            var b = new B();
            b.Plat(5f);
            b.FakeFloor(2f);                    // THE lie. Walk right and the floor isn't there.
            float a = b.Plat(7f);
            b.Spike(a + 1.8f);                  // the one honest ask, in plain sight
            b.Plat(5f);
            return b.Finish();
        }

        // 2 — STEADY HANDS. Nothing new. A spike and a saw, spaced out, over
        // ground that behaves — the floor where the controls stop being the
        // hard part. Cutting this to make room for another mechanic is what
        // broke the opening the first time.
        static Level L2()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(5f);
            float a = b.Plat(6f); b.Spike(a + 1.5f);
            b.Gap(2.2f);
            float c = b.Plat(6f); b.Saw(c + 1f);
            b.Gap(2.2f);
            b.Plat(5f);
            return b.Finish();
        }

        // 3 — SHOW: THE FLOOR TIPS. Two tilting slabs and nothing else in the
        // room. It creaks and reddens as you walk at it, and the only thing it
        // can teach you is what a tipping floor does.
        static Level L3()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(6f);
            b.TiltFloor();                      // NEW — and the only hazard here
            b.Plat(5f);
            b.TiltFloor();                      // again, so the lesson lands twice
            b.Plat(5f);
            return b.Finish();
        }

        // 4 — USE: TIP INTO THE GAP. Same lean, now with somewhere to fall and a
        // spike you already respect.
        static Level L4()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(5f);
            b.TiltFloor();
            b.Plat(4f);
            b.Gap(2.2f);
            float a = b.Plat(6f); b.Spike(a + 1.5f);
            b.Gap(2.2f);
            b.TiltFloor();
            b.Plat(5f);
            return b.Finish();
        }

        // 5 — SHOW: THE FLOOR LEAVES. The opposite answer to floor 3. This slab
        // is still there — it just isn't under you any more.
        //
        // ONE slab, and nothing else in the room. Playtesting put this floor at
        // ~37 deaths against 6-7 on its neighbours — a five-fold spike on the
        // floor that is supposed to be a gentle SHOW beat. The cause is in the
        // slab, not the layout: SlideSlab travels 3.4 units in 0.19s AWAY from
        // the way you are running, while a 4.2-wide slab takes 0.56s to walk. It
        // is not crossable on foot at all — the only answer is to jump the whole
        // thing — and floor 5 asked a player who had never seen one to solve it
        // twice back to back, with a saw waiting on floor 6.
        //
        // The pair now lives on floor 21 (in the dark, where that difficulty
        // belongs) and floor 5 does what a SHOW floor is for: meet the slab once,
        // on wide honest ground, with a fat clean landing on the far side.
        static Level L5()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(7f);                         // a long honest run-up to read the creak
            b.SlideFloor();                     // NEW — and the ONLY thing in this room
            b.Plat(7f);                         // fat landing, nothing standing on it
            b.Gap(2.2f);
            b.Plat(6f);
            return b.Finish();
        }

        // 6 — USE: TIP OR SLIDE. The two floor betrayals in one room, because
        // telling them apart at a glance is the actual skill.
        //
        // The saw that used to close this floor is gone to floor 22. A player who
        // has seen exactly one slide slab does not also need a moving blade in
        // the same breath — that pairing was the second half of the floor-5/6
        // wall, and this floor's whole job is "tell the two floors apart".
        static Level L6()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(6f);
            b.TiltFloor();                      // taught on 3 and 4
            b.Plat(5f);
            b.SlideFloor();                     // taught on 5
            b.Plat(6f);
            return b.Finish();
        }

        // 7 — SHOW: THE CEILING HAS TEETH. Everything so far came from below.
        // This room's one idea arrives from directly above, aimed at where you
        // are, and the ground is safe the whole way.
        static Level L7()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(6f);
            float a = b.Plat(7f); b.CeilingVolley(a, 5);   // NEW — fires in sequence, at you
            b.Plat(5f);
            float c = b.Plat(6f); b.CeilingVolley(c, 4);
            b.Plat(5f);
            return b.FinishDecoy();
        }

        // 8 — USE: TEETH AND STEEL. Volleys from above with saws on the floor,
        // so "run through ahead of the wave" now costs something.
        static Level L8()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(5f);
            float a = b.Plat(6f); b.CeilingVolley(a, 4);
            b.Gap(2.2f);
            float c = b.Plat(6f); b.Saw(c + 1f);
            b.Gap(2.2f);
            float d = b.Plat(6f); b.CeilingVolley(d, 5);
            // The first time the castle punishes the jump instead of demanding it.
            // Deliberately this early: the lesson "going up is sometimes the wrong
            // answer" has to be planted while the player is still forming the
            // habit, or by floor 20 it is not a subversion, it is a rule change.
            float k = b.Plat(5f); b.Crusher(k + 1f);
            b.Plat(4f);
            return b.Finish();
        }

        // 9 — SHOW: THE TRAPDOOR. The one that actually kills you: the floor
        // falls away into the dark and takes you with it. It gets a room of its
        // own precisely BECAUSE it is lethal — the first time you meet it there
        // must be nothing else to blame.
        static Level L9()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(6f);
            b.DropFloor();                      // NEW — and it does not stop
            b.Plat(6f);
            b.DropFloor();
            // AND THE SIDES OF THE ROOM, WHILE THERE IS STILL ANYONE HERE.
            //
            // The wall used to debut on floor 16. Testers are quitting well before
            // that, which meant the entire "a threat can come from beside you" verb
            // was, in practice, not in the game — everyone who played it met floors
            // that betray and ceilings that bite and nothing else. Three verbs by
            // floor 9 instead of two is the single cheapest thing that makes the
            // early game stop feeling like one input.
            //
            // Shown the way every other mechanic is shown here: alone, over solid
            // ground, with room to watch it arrive. Floor 16 still owns the hard
            // version where they come from both sides.
            float w = b.Plat(6f);
            b.SlamWall(w + 2f, 3.4f);
            b.Plat(5f);
            return b.Finish();
        }

        // 10 — THE GATEHOUSE. The world-1 exam behind a biting portcullis: all
        // four verbs you were taught, once each, in the order you learned them.
        static Level L10()
        {
            var b = new B();
            b.Room(RoomRule.None, 0.2f, true);
            b.Plat(4.5f);
            b.TiltFloor();
            float a = b.Plat(5f); b.CeilingVolley(a, 4);
            b.Gap(2.2f);
            b.SlideFloor();
            b.Plat(4f);
            b.DropFloor();
            b.Plat(4.5f);
            return b.Finish();
        }

        // ====================================================================
        // WORLD 2 (floors 11-19) — THE ROOM LIES ABOUT ITSELF.
        // World 1 taught the verbs one at a time. These pair them, and start
        // taking away the thing you were reading them with: the light.
        // ====================================================================

        // 11 — THE CHAPEL INVERTS. Gravity runes flip which way is down. Kept
        // short and legible: two flips, and the ceiling road is a real route.
        static Level L11()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.OpenCeiling();
            float a = b.Plat(5f); b.GravRune(a + 1.5f);
            float g = a + 3f;
            b.Gap(7f);                          // pit below; you cross it upside-down
            b.CeilRune(g + 4.5f);               // the way back down, mid-crossing
            float c = b.Plat(6f); b.Spike(c + 1.5f);
            b.Plat(4.5f);
            b.CeilSlab(g - 3f, g + 2.5f);
            b.CeilSlab(g + 4f, g + 10f);
            return b.FinishTrapdoor();
        }

        // 12 — THE DARK RETURNS. One room, candles out, and a floor that leaves
        // while you can't see which floor it was.
        static Level L12()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.12f);
            b.Plat(5f);
            b.SlideFloor(3f);                   // it goes while you're unlit
            b.Plat(3.5f);
            float a = b.Plat(6f); b.ShiftSpike(a + 1.8f, a - 1.2f);
            b.Gap(2.2f);
            b.Plat(3.5f);
            b.NightFloor(2.2f);
            b.Plat(4.5f);
            return b.Finish();
        }

        // 13 — SPRING LOADED. Pads that throw you at a ceiling that bites back.
        static Level L13()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            float a = b.Plat(6f); b.Spring(a - 1.5f); b.CeilingVolley(a + 1.5f, 4);
            b.Gap(2.3f);
            float c = b.Plat(6f); b.Spring(c - 1f); b.Spike(c + 2f);
            b.Gap(2.3f);
            b.TiltFloor(3f);
            b.Plat(4.5f);
            return b.FinishDecoy();
        }

        // 14 — THE SUN LIES. Invisible daylight on ground that looks like relief,
        // and a slab that tips you straight into it.
        static Level L14()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(5f);
            float a = b.Plat(6f); b.Surprise(a + 1.5f);
            b.Gap(2.2f);
            b.TiltFloor(3f);
            float c = b.Plat(6f); b.Pendulum(c); b.Surprise(c + 2.4f);
            b.Gap(2.2f);
            b.Plat(5f);
            return b.Finish();
        }

        // 15 — THE CHOIR. A rhythm floor: ceiling volleys on a beat, with a
        // crusher in the middle that demands you break it.
        static Level L15()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            float a = b.Plat(6f); b.ArrowRain(a); b.CeilingVolley(a + 2.5f, 4);
            b.Gap(2.2f);
            float c = b.Plat(5f); b.Crusher(c);
            b.Gap(2.2f);
            float d = b.Plat(6f); b.ArrowRain(d + 1.5f);
            // The wall again, six floors after it was shown — otherwise it is taught
            // once on floor 9 and then absent until 16, which is long enough that
            // its return reads as a new mechanic rather than a remembered one.
            float w = b.Plat(5.5f);
            b.SlamWall(w + 1.8f, 3.2f);
            b.Plat(4f);
            return b.Finish();
        }

        // 16 — SHOW: THE WALL ARRIVES. The sides of the room join in. It gets
        // the opening stretch to itself, on ground that cannot drop you, so the
        // first wall you ever meet is one you are allowed to study — THEN the
        // ferry, where the same trick has somewhere much worse to catch you.
        static Level L16()
        {
            var b = new B();
            b.Room(RoomRule.None);
            float p = b.Plat(6f);
            b.SlamWall(p + 3.5f, 3.4f);         // NEW — alone, over solid ground
            b.Plat(5f);
            b.MoverGap(6.8f);                   // now the ferry
            float a = b.Plat(6f); b.Saw(a + 1.5f);
            // Same reasoning as floor 24: the walls have owned this room from the
            // start, so the ground reads as the one honest surface here. It isn't.
            b.FakeFloor(2.2f);
            float q = b.Plat(5f);
            b.SlamWall(q, 3.2f, true);          // and from the other side
            b.Plat(4f);
            return b.FinishTrapdoor();
        }

        // 17 — THE FLOOR FALLS TWICE. Two drop slabs in a row, so the room you
        // finish in is two shelves below the room you started in.
        static Level L17()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            b.DropFloor(3.2f);
            float a = b.Plat(5f); b.FlameJet(a + 1.2f);
            b.DropFloor(3.2f);
            float c = b.Plat(6f); b.Saw(c); b.Spike(c + 2.5f);
            // AND NOW JUMPING IS WRONG.
            //
            // Everything above this line — the two trapdoors, the flame jet, the
            // saw, the spike — is answered by the same input, and by here the
            // player is not deciding any more, they are jumping on sight. The
            // crusher is the only trap in the game that punishes exactly that, and
            // it was placed on two floors out of forty, so the reflex the castle
            // spends the whole campaign training was never once turned against the
            // player. It gets its own platform: a crusher demands you stay LOW and
            // almost everything else demands you go over, so pairing them makes a
            // platform with no solution.
            float k = b.Plat(5f); b.Crusher(k + 1f);
            b.Plat(4.5f);
            return b.Finish();
        }

        // 18 — THE PRESS AND THE PIT. Rising ground over a room that is mostly
        // hole: being pressed and being dropped answer each other.
        static Level L18()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(5f);
            b.RiseFloor();                      // NEW — safe ground both sides
            b.Plat(5f);
            b.Gap(2.4f);
            float a = b.Plat(5f); b.GrowSpike(a + 1.2f);
            b.Gap(2.4f);
            b.RiseFloor(3f);
            b.Gap(2.4f);
            b.Plat(5f);
            return b.FinishDecoy();
        }

        // 19 — ★ THE SECOND WALL. The exam before the Countess: dark, reversed,
        // and every kind of moving ground in one room. Still 38 units.
        static Level L19()
        {
            var b = new B();
            b.Room(RoomRule.Reverse, 0.12f, true);
            b.Plat(4.5f);
            b.TiltFloor(2.8f);
            b.SlideFloor(2.8f);
            float a = b.Plat(5f); b.CeilingVolley(a, 4);
            b.Gap(2.3f);
            b.DropFloor(3f);
            float c = b.Plat(5f); b.Spike(c + 1.2f);
            b.RiseFloor(2.8f);
            b.Plat(5f);
            return b.Finish();
        }

        // ====================================================================
        // WORLD 3 (floors 21-29) — THE CASTLE STOPS PRETENDING.
        // Every room here combines two betrayals that answer each other, and
        // the exit starts lying as often as the floor does.
        // ====================================================================

        // 21 — SLIDE INTO THE DARK. This is floor 5's old double-slide, moved up
        // to where it belongs: two slabs that leave, back to back, unlit. By now
        // the player has met the slide alone (5), paired with the tilt (6), in
        // the exam (10) and unlit once already (12) — so asking them to jump two
        // in the dark is a test, where on floor 5 it was a wall.
        static Level L21()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.12f);
            b.Plat(5f);
            b.SlideFloor(3f);
            b.Plat(3.5f);
            float a = b.Plat(5f); b.ShiftSpike(a + 1.5f, a - 1.5f);
            b.Gap(2.2f);
            b.SlideFloor(2.8f);
            b.Plat(4.5f);
            return b.Finish();
        }

        // 22 — THE GHOST BRIDGE. A gap you cannot jump, crossed only by trusting
        // the dark — with the floor lying on the far side.
        //
        // Carries floor 6's old closing beat: tilt, then slide, then a saw. That
        // three-in-a-row is a fine ask of a floor-22 player and was far too much
        // of a floor-6 one.
        static Level L22()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.08f);
            b.Plat(4.5f);
            b.GhostFloor(7.2f);                 // only solid once the candles die
            b.Plat(3.5f);
            b.TiltFloor(3f);
            b.Plat(3.5f);
            b.SlideFloor(3f);
            float a = b.Plat(5f); b.Saw(a + 1f);
            b.Plat(4f);
            return b.FinishTrapdoor();
        }

        // 23 — PRESSED FROM BOTH SIDES. A rising floor under a slamming wall.
        static Level L23()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(5f);
            float q = b.Plat(4f);
            b.SlamWall(q + 1f, 3.2f);
            b.RiseFloor(3f);
            b.Gap(2.3f);
            float a = b.Plat(5f); b.HolyWater(a); b.Pendulum(a + 2f);
            b.Plat(4.5f);
            return b.Finish();
        }

        // 24 — THE CEILING HUNTS. Two volleys and a chandelier, in a room narrow
        // enough that there is nowhere to stand and wait.
        static Level L24()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            float a = b.Plat(6f); b.CeilingVolley(a, 5);
            b.Gap(2.2f);
            float c = b.Plat(5f); b.Chandelier(c);
            // WHILE YOU WERE WATCHING THE CEILING.
            //
            // Every threat on this floor comes from above, which quietly teaches
            // the player that the ground is the safe part — they spend the whole
            // room with their eyes up. That is the one and only condition under
            // which a collapsing floor is a joke rather than a cheap shot: it is
            // blind, so it has to be earned by the floor establishing trust first.
            b.FakeFloor(2.2f);
            b.Gap(2.2f);
            float d = b.Plat(6f); b.CeilingVolley(d + 1f, 5);
            b.Plat(4f);
            return b.FinishDecoy();
        }

        // 25 — THE SHY COFFIN, GUARDED. It runs, and the ground it runs across
        // is not ground you can stand on for long.
        static Level L25()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            b.TiltFloor(3f);
            float a = b.Plat(5f); b.FlameJet(a + 1.2f);
            b.Gap(2.2f);
            float e = b.Plat(11f);
            b.Saw(e - 2.5f);
            b.ShyExit(e - 1f);
            return b.FinishBare();
        }

        // 26 — STONE DESCENDS. The ceiling comes down while the floor drops away.
        static Level L26()
        {
            var b = new B();
            b.Room(RoomRule.Press, 0.15f);
            b.Plat(5f);
            b.DropFloor(3.2f);
            float a = b.Plat(5f); b.Spike(a + 1.2f);
            b.Gap(2.3f);
            b.MoverGap(6.6f);
            b.Plat(5f);
            return b.Finish();
        }

        // 27 — THE FLOOR THAT WASN'T. Fake floors and slide floors alternating,
        // so "is this real" is the only question the room asks.
        static Level L27()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4f);
            b.FakeFloor(2f);
            b.Plat(3.5f);
            b.SlideFloor(3f);
            b.Plat(3.5f);
            b.FakeFloor(2f);
            float a = b.Plat(5f); b.Dart(a);
            b.Plat(4.5f);
            return b.FinishTrapdoor();
        }

        // 28 — REVERSED OVER MOVING GROUND. Floor 9's idea, tightened.
        static Level L28()
        {
            var b = new B();
            b.Room(RoomRule.Reverse, 0.15f);
            b.Plat(4.5f);
            b.SlideFloor(3f);
            b.Gap(2.3f);
            b.TiltFloor(3f);
            float a = b.Plat(5f); b.Saw(a + 1f);
            b.Gap(2.3f);
            b.Plat(5f);
            return b.FinishDecoy();
        }

        // 29 — THE GAUNTLET BEFORE THE WARLOCK. Four betrayals, no honest ground
        // longer than five units, and a gate on the way in.
        static Level L29()
        {
            var b = new B();
            b.Room(RoomRule.None, 0.2f, true);
            b.Plat(4.5f);
            b.RiseFloor(2.8f);
            b.TiltFloor(2.8f);
            float a = b.Plat(5f); b.CeilingVolley(a, 4);
            b.SlideFloor(3f);
            float c = b.Plat(5f); b.Spike(c + 1.2f);
            b.Plat(4.5f);
            return b.Finish();
        }

        // ====================================================================
        // WORLD 4 (floors 31-39) — THE LAST NIGHT.
        // The rooms stop being rooms: the ground is mostly things that move,
        // and two of these (33, 37) are the hardest floors in the game.
        // ====================================================================

        // 31 — THE INVERTED HALL. Gravity again, now with a ceiling that bites.
        static Level L31()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.OpenCeiling();
            float a = b.Plat(5f); b.GravRune(a + 1.5f);
            float g = a + 3f;
            b.Gap(7f);
            b.CeilRune(g + 4.5f);
            float c = b.Plat(6f); b.LateSpike(c + 1.5f);
            b.Plat(4.5f);
            b.CeilSlab(g - 3f, g + 2f);
            b.CeilSlab(g + 3.6f, g + 10f);
            return b.Finish();
        }

        // 32 — EVERY FLOOR MOVES. There is exactly one piece of honest stone in
        // this room and it is the one you spawn on.
        static Level L32()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            b.TiltFloor(2.8f);
            b.SlideFloor(2.8f);
            b.DropFloor(3f);
            float a = b.Plat(5f); b.Saw(a + 1f);
            b.RiseFloor(2.8f);
            // Four betrayals deep, every one of them survived by leaving the
            // ground. Its own platform — see floor 17.
            float k = b.Plat(5f); b.Crusher(k + 1f);
            b.Plat(4.5f);
            return b.Finish();
        }

        // 33 — ★ THE IRON CHOIR. Volleys on a beat over ground that leaves.
        static Level L33()
        {
            var b = new B();
            b.Room(RoomRule.None, 0.2f, true);
            b.Plat(4.5f);
            float a = b.Plat(5f); b.CeilingVolley(a, 6);
            b.SlideFloor(3f);
            float c = b.Plat(5f); b.ArrowRain(c); b.CeilingVolley(c + 2f, 5);
            b.TiltFloor(2.8f);
            float d = b.Plat(5f); b.Spike(d + 1.2f);
            b.Plat(4f);
            return b.FinishDecoy();
        }

        // 34 — THE DARK PRESS. Rising ground, unlit.
        static Level L34()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.1f);
            b.Plat(4.5f);
            b.RiseFloor(3f);
            b.Gap(2.3f);
            float a = b.Plat(5f); b.ShiftSpike(a + 1.5f, a - 1.5f);
            b.RiseFloor(3f);
            b.Plat(5f);
            return b.Finish();
        }

        // 35 — THE WALLS CLOSE. Three slamming walls in thirty units.
        static Level L35()
        {
            var b = new B();
            b.Room(RoomRule.None);
            float p = b.Plat(5.5f);
            b.SlamWall(p + 3f, 3.4f);
            float q = b.Plat(5.5f);
            b.SlamWall(q + 1f, 3.2f, true);
            b.Gap(2.3f);
            float r = b.Plat(6f); b.Saw(r);
            b.SlamWall(r + 3.2f, 3.4f);
            b.Plat(5f);
            return b.FinishTrapdoor();
        }

        // 36 — THE FERRY IN THE DARK. The slab ride, unlit, over a real pit.
        static Level L36()
        {
            var b = new B();
            b.Room(RoomRule.Dark, 0.1f);
            b.Plat(4.5f);
            b.MoverGap(6.8f);
            float a = b.Plat(5f); b.Saw(a + 1f);
            b.Gap(2.3f);
            b.TiltFloor(3f);
            b.Plat(4.5f);
            return b.Finish();
        }

        // 37 — ★ DEATH'S PENDULUM. The hardest room in the castle: reversed
        // hands, moving ground, and blades on every beat.
        static Level L37()
        {
            var b = new B();
            b.Room(RoomRule.Reverse, 0.1f, true);
            b.Plat(4.5f);
            float a = b.Plat(5f); b.Pendulum(a);
            b.TiltFloor(2.8f);
            float c = b.Plat(5f); b.Pendulum(c); b.Spike(c + 2.2f);
            b.SlideFloor(2.8f);
            float d = b.Plat(5f); b.Saw(d);
            b.Plat(4.5f);
            return b.FinishDecoy();
        }

        // 38 — THE FALSE DOORS. Coffins everywhere; one is real, and it runs.
        static Level L38()
        {
            var b = new B();
            b.Room(RoomRule.None);
            b.Plat(4.5f);
            float a = b.Plat(6f); b.FakeCoffin(a - 1.5f); b.Spike(a + 2f);
            b.Gap(2.2f);
            b.TiltFloor(3f);
            float e = b.Plat(11f);
            b.FakeCoffin(e - 3.5f);
            b.ShyExit(e - 0.5f);
            return b.FinishBare();
        }

        // 39 — THE LAST HALL. Everything the castle has, once each, in one room.
        static Level L39()
        {
            var b = new B();
            b.Room(RoomRule.None, 0.15f, true);
            b.Plat(4f);
            b.TiltFloor(2.6f);
            float a = b.Plat(4.5f); b.CeilingVolley(a, 5);
            b.SlideFloor(2.6f);
            b.DropFloor(3f);
            float c = b.Plat(4.5f); b.Saw(c);
            b.SlamWall(c + 2.6f, 3.2f);
            b.RiseFloor(2.6f);
            float d = b.Plat(5f); b.Spike(d + 1.2f);
            b.Plat(4f);
            return b.Finish();
        }
    }
}
