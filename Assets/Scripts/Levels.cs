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
        public static int Count => 2;

        public static Level Get(int index)
        {
            switch (((index % Count) + Count) % Count)
            {
                case 1: return Two();
                default: return One();
            }
        }

        // -------------------- LEVEL 1 (gentle intro) --------------------
        public static Level One()
        {
            var L = new Level { Spawn = new Vector2(-9f, -2f) };

            L.Platforms.Add(new Rect2(-8.5f, -3f, 3f, 0.6f));
            L.Platforms.Add(new Rect2(-3f,  -3f, 3f, 0.6f));
            L.Platforms.Add(new Rect2( 1f,  -3f, 3f, 0.6f));
            L.Platforms.Add(new Rect2( 5f,  -3f, 3f, 0.6f));

            L.Traps.Add(new TrapSpec(TrapType.FakeFloor, -6f, -3f, 2f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.LateSpike, -3f, -2.4f, 2.2f, 1.2f));
            L.Traps.Add(new TrapSpec(TrapType.Crusher,    1f, -1.0f, 1.8f, 1.4f));
            L.Traps.Add(new TrapSpec(TrapType.FakeExit,   6f, -2f, 1f, 2f));
            L.Traps.Add(new TrapSpec(TrapType.RealExit,   3f, -5f, 1.2f, 1.2f));

            L.Decos.Add(new Deco(-6f, -2.78f, 1.6f, 0.08f, Theme.Tell));
            L.Decos.Add(new Deco(-3f, -2.55f, 0.45f, 0.18f, Theme.Danger));
            L.Decos.Add(new Deco( 1f, -0.6f, 0.4f, 0.4f, Theme.Coin));
            L.Decos.Add(new Deco( 0.4f, -0.4f, 0.3f, 0.3f, Theme.Coin));
            L.Decos.Add(new Deco( 1.6f, -0.4f, 0.3f, 0.3f, Theme.Coin));
            L.Decos.Add(new Deco( 1f, 0.4f, 1.8f, 0.12f, Theme.Tell));
            L.Decos.Add(new Deco( 3f, -2.2f, 0.18f, 0.7f, Theme.Exit));
            L.Decos.Add(new Deco( 3f, -2.7f, 0.5f, 0.18f, Theme.Exit));
            return L;
        }

        // -------------------- LEVEL 2 (longer, meaner, portals) --------------------
        public static Level Two()
        {
            var L = new Level { Spawn = new Vector2(-10f, -2f) };
            L.CamMinX = -1.5f; L.CamMaxX = 13f;   // camera follows right

            // Section 1: warm-up that immediately betrays you.
            L.Platforms.Add(new Rect2(-9.5f, -3f, 3f, 0.6f));   // start
            L.Traps.Add(new TrapSpec(TrapType.FakeFloor, -6.5f, -3f, 2f, 0.6f));
            L.Decos.Add(new Deco(-6.5f, -2.78f, 1.6f, 0.08f, Theme.Tell));

            // Section 2: spike + crusher gauntlet.
            L.Platforms.Add(new Rect2(-3.5f, -3f, 3f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.LateSpike, -3.5f, -2.4f, 2.4f, 1.2f));
            L.Decos.Add(new Deco(-3.5f, -2.55f, 0.45f, 0.18f, Theme.Danger));

            L.Platforms.Add(new Rect2(0.5f, -3f, 3f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.Crusher, 0.5f, -1.0f, 1.8f, 1.4f));
            L.Decos.Add(new Deco(0.5f, -0.6f, 0.4f, 0.4f, Theme.Coin));
            L.Decos.Add(new Deco(1.1f, -0.4f, 0.3f, 0.3f, Theme.Coin));
            L.Decos.Add(new Deco(0.5f, 0.4f, 1.8f, 0.12f, Theme.Tell));

            // A deadly wide gap (2..8) — the ONLY way across is the portal.
            L.Portals.Add(new PortalPair(2.3f, -2.2f, 8.5f, -2.2f));

            // Section 3: portal landing -> more traps.
            L.Platforms.Add(new Rect2(9f, -3f, 3f, 0.6f));      // portal exit landing
            L.Traps.Add(new TrapSpec(TrapType.LateSpike, 9.3f, -2.4f, 2.2f, 1.2f));
            L.Decos.Add(new Deco(9.3f, -2.55f, 0.45f, 0.18f, Theme.Danger));

            L.Traps.Add(new TrapSpec(TrapType.FakeFloor, 12.5f, -3f, 2f, 0.6f));
            L.Decos.Add(new Deco(12.5f, -2.78f, 1.6f, 0.08f, Theme.Tell));

            L.Platforms.Add(new Rect2(15.5f, -3f, 3f, 0.6f));
            L.Traps.Add(new TrapSpec(TrapType.Crusher, 15.5f, -1.0f, 1.8f, 1.4f));
            L.Decos.Add(new Deco(15.5f, -0.5f, 0.4f, 0.4f, Theme.Coin));
            L.Decos.Add(new Deco(15.5f, 0.4f, 1.8f, 0.12f, Theme.Tell));

            // Section 4: the finish — obvious bright door kills; drop into the
            // gap to the mint exit instead.
            L.Platforms.Add(new Rect2(20f, -3f, 3f, 0.6f));     // end platform
            L.Traps.Add(new TrapSpec(TrapType.FakeExit, 21f, -2f, 1f, 2f));
            L.Traps.Add(new TrapSpec(TrapType.RealExit, 17.7f, -5f, 1.3f, 1.2f));
            L.Decos.Add(new Deco(17.7f, -2.2f, 0.18f, 0.7f, Theme.Exit));
            L.Decos.Add(new Deco(17.7f, -2.7f, 0.5f, 0.18f, Theme.Exit));
            return L;
        }
    }
}
