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

    public class Level
    {
        public Vector2 Spawn;
        public List<Rect2> Platforms = new();
        public List<TrapSpec> Traps = new();
        public List<Deco> Decos = new();   // coins, tells, arrows (visual only)
    }

    /// <summary>
    /// Hand-authored Level 1. The golden path looks obvious and betrays you at
    /// every step; each trap has a faint TELL (a Deco) so a second run feels fair.
    /// Beatable route: jump the fake floor, don't dawdle on the spike platform,
    /// stay LOW (ignore the bait coins), then DROP into the gap to the green exit
    /// instead of walking to the tempting purple door.
    /// </summary>
    public static class Levels
    {
        public static Level One()
        {
            var L = new Level { Spawn = new Vector2(-9f, -2f) };

            // Safe platforms.
            L.Platforms.Add(new Rect2(-8.5f, -3f, 3f, 0.6f)); // start
            L.Platforms.Add(new Rect2(-3f,  -3f, 3f, 0.6f));  // spike landing
            L.Platforms.Add(new Rect2( 1f,  -3f, 3f, 0.6f));  // low / crusher
            L.Platforms.Add(new Rect2( 5f,  -3f, 3f, 0.6f));  // end (with fake door)

            // Traps.
            L.Traps.Add(new TrapSpec(TrapType.FakeFloor, -6f, -3f, 2f, 0.6f));   // collapses
            L.Traps.Add(new TrapSpec(TrapType.LateSpike, -3f, -2.4f, 2.2f, 1.2f));// spikes pop up
            L.Traps.Add(new TrapSpec(TrapType.Crusher,    1f, -1.0f, 1.8f, 1.4f));// high-bait crush
            L.Traps.Add(new TrapSpec(TrapType.FakeExit,   6f, -2f, 1f, 2f));      // evil door
            L.Traps.Add(new TrapSpec(TrapType.RealExit,   3f, -5f, 1.2f, 1.2f));  // true exit (in gap)

            // Tells (subtle) + bait.
            L.Decos.Add(new Deco(-6f, -2.78f, 1.6f, 0.08f, Theme.Tell));   // crack on fake floor
            L.Decos.Add(new Deco(-3f, -2.55f, 0.45f, 0.18f, Theme.Danger)); // spike nub
            L.Decos.Add(new Deco( 1f, -0.6f, 0.4f, 0.4f, Theme.Coin));      // bait coin
            L.Decos.Add(new Deco( 0.4f, -0.4f, 0.3f, 0.3f, Theme.Coin));    // bait coin
            L.Decos.Add(new Deco( 1.6f, -0.4f, 0.3f, 0.3f, Theme.Coin));    // bait coin
            L.Decos.Add(new Deco( 1f, 0.4f, 1.8f, 0.12f, Theme.Tell));      // crusher shadow
            L.Decos.Add(new Deco( 3f, -2.2f, 0.18f, 0.7f, Theme.Exit));     // arrow stem down
            L.Decos.Add(new Deco( 3f, -2.7f, 0.5f, 0.18f, Theme.Exit));     // arrow head

            return L;
        }
    }
}
