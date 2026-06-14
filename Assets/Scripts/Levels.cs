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

    public class Level
    {
        public Vector2 Spawn;
        public float CamMinX = -1.5f, CamMaxX = -1.5f;  // static camera by default
        public List<Rect2> Platforms = new();
        public List<TrapSpec> Traps = new();
        public List<Deco> Decos = new();
        public List<PortalPair> Portals = new();
    }

    public static class Levels
    {
        public static int Count => 4;

        public static Level Get(int index)
        {
            switch (((index % Count) + Count) % Count)
            {
                case 1: return Two();
                case 2: return Three();
                case 3: return Four();
                default: return One();
            }
        }

        // Standard fake-finish: a pre-gap platform, then a gap with the REAL exit
        // at its bottom, then an end platform whose bright door is a LIE.
        static void Finish(Level L, float x)
        {
            L.Platforms.Add(new Rect2(x + 1.5f, -3f, 3f, 0.6f));   // pre-gap platform
            L.Platforms.Add(new Rect2(x + 6f, -3f, 3f, 0.6f));     // end platform
            L.Traps.Add(new TrapSpec(TrapType.FakeExit, x + 6.5f, -2f, 1f, 2f));
            L.Traps.Add(new TrapSpec(TrapType.RealExit, x + 3.75f, -5f, 1.3f, 1.2f));
        }

        // -------------------- LEVEL 1 (the unfair gauntlet) --------------------
        // No tells. Traps look IDENTICAL to safe ground. You will die the first
        // time at each one with no warning — then you memorise and feel clever.
        public static Level One()
        {
            var L = new Level { Spawn = new Vector2(-9f, -2f) };

            L.Platforms.Add(new Rect2(-8.5f, -3f, 3f, 0.6f)); // start (safe)
            L.Platforms.Add(new Rect2(-3f,  -3f, 3f, 0.6f));  // "safe" platform (it isn't)
            L.Platforms.Add(new Rect2( 1f,  -3f, 3f, 0.6f));  // bait-coin platform
            L.Platforms.Add(new Rect2( 5f,  -3f, 3f, 0.6f));  // the fake finish

            // The obvious path forward collapses (looks just like real floor).
            L.Traps.Add(new TrapSpec(TrapType.FakeFloor, -6f, -3f, 2f, 0.6f));
            // A totally normal-looking platform that kills you mid-stride. Pure unfair.
            L.Traps.Add(new TrapSpec(TrapType.Surprise, -2.6f, -2.2f, 0.8f, 1.0f));
            // Reach for the shiny coins -> crushed from above.
            L.Traps.Add(new TrapSpec(TrapType.Crusher, 1f, -1.0f, 1.8f, 1.4f));
            // The bright, obvious "exit" door is a lie and kills you.
            L.Traps.Add(new TrapSpec(TrapType.FakeExit, 6f, -2f, 1f, 2f));
            // The REAL exit: drop into the scary-looking gap before the door.
            L.Traps.Add(new TrapSpec(TrapType.RealExit, 3f, -5f, 1.3f, 1.2f));

            // Only bait remains visible (luring you into the crusher).
            L.Decos.Add(new Deco(1f, -0.6f, 0.4f, 0.4f, Theme.Coin));
            L.Decos.Add(new Deco(0.4f, -0.4f, 0.3f, 0.3f, Theme.Coin));
            L.Decos.Add(new Deco(1.6f, -0.4f, 0.3f, 0.3f, Theme.Coin));
            return L;
        }

        // -------------------- LEVEL 2 (longer, meaner, portals) --------------------
        public static Level Two()
        {
            var L = new Level { Spawn = new Vector2(-10f, -2f) };
            L.CamMinX = -1.5f; L.CamMaxX = 13f;   // camera follows right

            // Section 1: warm-up that immediately betrays you (no warning).
            L.Platforms.Add(new Rect2(-9.5f, -3f, 3f, 0.6f));   // start
            L.Traps.Add(new TrapSpec(TrapType.FakeFloor, -6.5f, -3f, 2f, 0.6f));

            // Section 2: a "safe" platform with an invisible death, then a crusher.
            L.Platforms.Add(new Rect2(-3.5f, -3f, 3f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.Surprise, -3.8f, -2.2f, 0.8f, 1.0f));
            L.Traps.Add(new TrapSpec(TrapType.LateSpike, -2.4f, -2.4f, 1.0f, 1.2f));

            L.Platforms.Add(new Rect2(0.5f, -3f, 3f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.Crusher, 0.5f, -1.0f, 1.8f, 1.4f));
            L.Decos.Add(new Deco(0.5f, -0.6f, 0.4f, 0.4f, Theme.Coin));
            L.Decos.Add(new Deco(1.1f, -0.4f, 0.3f, 0.3f, Theme.Coin));

            // A deadly wide gap — the ONLY way across is the portal.
            L.Portals.Add(new PortalPair(2.3f, -2.2f, 8.5f, -2.2f));

            // Section 3: portal landing (safe room), then a spike, then fake floor.
            L.Platforms.Add(new Rect2(9.5f, -3f, 4f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.LateSpike, 11f, -2.4f, 1.0f, 1.2f));
            L.Traps.Add(new TrapSpec(TrapType.FakeFloor, 12.5f, -3f, 2f, 0.6f));

            L.Platforms.Add(new Rect2(15.5f, -3f, 3f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.Crusher, 15.5f, -1.0f, 1.8f, 1.4f));
            L.Decos.Add(new Deco(15.5f, -0.5f, 0.4f, 0.4f, Theme.Coin));

            // Section 4: the fake finish. Bright door kills; drop into the gap.
            L.Platforms.Add(new Rect2(20f, -3f, 3f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.FakeExit, 21f, -2f, 1f, 2f));
            L.Traps.Add(new TrapSpec(TrapType.RealExit, 17.7f, -5f, 1.3f, 1.2f));
            return L;
        }

        // -------------------- LEVEL 3 (incoming: darts, fallers, saw) --------------------
        public static Level Three()
        {
            var L = new Level { Spawn = new Vector2(-10f, -2f) };
            L.CamMinX = -1.5f; L.CamMaxX = 12f;

            L.Platforms.Add(new Rect2(-9.5f, -3f, 3f, 0.6f));   // start
            L.Traps.Add(new TrapSpec(TrapType.Dart, -9f, -2.3f, 1.4f, 1.2f)); // dart flies at you

            L.Platforms.Add(new Rect2(-4.5f, -3f, 4f, 0.6f));   // -6.5..-2.5
            L.Traps.Add(new TrapSpec(TrapType.Faller, -5f, -2.3f, 1.4f, 1.2f)); // block drops
            L.Traps.Add(new TrapSpec(TrapType.Surprise, -3f, -2.2f, 0.8f, 1.0f)); // invisible death

            L.Platforms.Add(new Rect2(0f, -3f, 3f, 0.6f));      // -1.5..1.5
            L.Traps.Add(new TrapSpec(TrapType.Saw, 0f, -2.2f, 0.9f, 0.9f)); // sliding saw

            Finish(L, 2.5f);
            return L;
        }

        // -------------------- LEVEL 4 (cruelty: reverse, warp-back, spring) -----------
        public static Level Four()
        {
            var L = new Level { Spawn = new Vector2(-10f, -2f) };
            L.CamMinX = -1.5f; L.CamMaxX = 14f;

            L.Platforms.Add(new Rect2(-9.5f, -3f, 3f, 0.6f));   // start
            L.Traps.Add(new TrapSpec(TrapType.Reverse, -8.5f, -2.3f, 1.5f, 1.2f)); // controls flip

            L.Platforms.Add(new Rect2(-4.5f, -3f, 4f, 0.6f));   // -6.5..-2.5
            L.Traps.Add(new TrapSpec(TrapType.Dart, -6f, -2.3f, 1.2f, 1.2f));
            L.Traps.Add(new TrapSpec(TrapType.Faller, -3.5f, -2.3f, 1.4f, 1.2f));

            // A tempting spring on the next platform launches you into a hidden death.
            L.Platforms.Add(new Rect2(0f, -3f, 3f, 0.6f));      // -1.5..1.5
            L.Traps.Add(new TrapSpec(TrapType.Spring, -0.8f, -2.55f, 1.0f, 0.5f));
            L.Traps.Add(new TrapSpec(TrapType.Surprise, -0.8f, -0.4f, 1.4f, 1.4f)); // above the spring
            L.Traps.Add(new TrapSpec(TrapType.WarpBack, 1.1f, -2.3f, 0.8f, 1.2f));  // yanked to start

            L.Platforms.Add(new Rect2(4f, -3f, 3f, 0.6f));      // 2.5..5.5
            L.Traps.Add(new TrapSpec(TrapType.Saw, 4f, -2.2f, 0.9f, 0.9f));

            Finish(L, 6.5f);
            return L;
        }
    }
}
