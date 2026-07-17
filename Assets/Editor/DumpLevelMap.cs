using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using TrustIssues;

// Editor-only: draws a side-on map of a built floor to a PNG, so geometry can be
// checked at a glance — dividers on solid floor, doorways reachable, gaps
// jumpable, rules on the rooms you meant. Levels are code-built, so this is the
// only way to SEE one without walking it. Now also overlays the REAL max-jump
// arc (from JumpArcProbe's physics) at every platform edge, so "is this gap
// jumpable?" is answered on the picture instead of by eye. Never compiled into a
// build (lives under Assets/Editor).
public static class DumpLevelMap
{
    const float PPU = 18f;   // pixels per world unit
    const float MinX = -14f, MaxX = 145f, MinY = -6f, MaxY = 5f;   // full-screen stages: floors run ~110-125 wide
    const int RoomedFloors = 10;

    // Physics for the arc overlay — mirrors PlayerController / JumpArcProbe.
    const float MoveSpeed = 7.5f, JumpSpeed = 14f, FallG = 5.5f, RiseG = 3.4f, G = 9.81f, Dt = 0.02f;

    [MenuItem("Trust Issues/Dump Level Map")]
    public static void Dump()
    {
        for (int i = 1; i <= RoomedFloors; i++)
            Draw(GetFloor(i), "level_map_L" + i + ".png");
        Debug.Log("LEVELMAP_DONE roomed 1-" + RoomedFloors);
    }

    // Smoke-render the legacy corridor floors so a shared-code change can be
    // checked for regressions on the floors it wasn't aimed at.
    [MenuItem("Trust Issues/Dump Legacy Floor Maps")]
    public static void DumpLegacy()
    {
        foreach (int f in new[] { 11, 15, 25, 33, 35 })
            Draw(Levels.Get(f - 1), "level_map_L" + f + ".png");   // Get is 0-based
        Debug.Log("LEVELMAP_DONE legacy 11/15/25/33/35");
    }

    static Level GetFloor(int oneBased)
    {
        var m = typeof(Levels).GetMethod("L" + oneBased, BindingFlags.NonPublic | BindingFlags.Static);
        return (Level)m.Invoke(null, null);
    }

    static void Draw(Level lvl, string file)
    {
        int W = Mathf.RoundToInt((MaxX - MinX) * PPU), H = Mathf.RoundToInt((MaxY - MinY) * PPU);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.10f, 0.08f, 0.13f);

        // room bands first, so geometry draws on top
        var bands = new[] { new Color(0.18f,0.16f,0.24f), new Color(0.13f,0.12f,0.19f) };
        for (int i = 0; i < lvl.Rooms.Count; i++)
        {
            var r = lvl.Rooms[i];
            var col = r.Rule == RoomRule.Dark ? new Color(0.05f,0.04f,0.07f) : bands[i % 2];
            FillRect(px, W, H, r.MinX, MinY, r.MaxX - r.MinX, MaxY - MinY, col);
            // Loop rooms have a return-rune at their exit doorway (RoomDirector
            // derives it from the rule, so it's not in a list) — mark it.
            if (r.Rule == RoomRule.Loop)
                FillRect(px, W, H, r.MaxX - 1.9f, -2.7f, 0.5f, 1.4f, new Color(0.9f, 0.2f, 0.25f));
        }
        foreach (var p in lvl.Platforms)
            FillRect(px, W, H, p.pos.x - p.size.x/2f, p.pos.y - p.size.y/2f, p.size.x, p.size.y,
                     new Color(0.65f, 0.62f, 0.70f));
        foreach (var p in lvl.NightFloors)
            FillRect(px, W, H, p.pos.x - p.size.x/2f, p.pos.y - p.size.y/2f, p.size.x, p.size.y,
                     new Color(0.35f, 0.55f, 1f));
        foreach (var p in lvl.GhostFloors)
            FillRect(px, W, H, p.pos.x - p.size.x/2f, p.pos.y - p.size.y/2f, p.size.x, p.size.y,
                     new Color(0.3f, 0.9f, 0.9f));
        foreach (var r in lvl.SleepRunes)
            FillRect(px, W, H, r.x - 0.55f, r.y - 0.2f, 1.1f, 0.4f, new Color(0.93f, 0.85f, 0.5f));
        foreach (var g in lvl.Gates)
            FillRect(px, W, H, g - 0.15f, -2.7f, 0.3f, 1.7f, new Color(1f, 0.5f, 0.1f));
        foreach (var s in lvl.ShiftSpikes)
        {
            FillRect(px, W, H, s.x - 0.35f, -2.75f, 0.7f, 0.7f, new Color(1f, 0.25f, 0.25f));
            FillRect(px, W, H, s.y - 0.35f, -2.75f, 0.7f, 0.7f, new Color(0.55f, 0.1f, 0.1f));
        }
        // Bobbing slabs: the slab at centre plus a thin line marking its travel.
        foreach (var m in lvl.Movers)
        {
            FillRect(px, W, H, m.x - m.w / 2f, m.y - 0.3f, m.w, 0.6f, new Color(0.85f, 0.85f, 0.95f));
            FillRect(px, W, H, m.x - 0.06f, m.y - m.z, 0.12f, m.z * 2f, new Color(0.6f, 0.6f, 0.75f));
        }
        // Portal pads in magenta (debug colour), a thin roof line linking pairs.
        foreach (var pp in lvl.Portals)
        {
            FillRect(px, W, H, pp.a.x - 0.5f, pp.a.y - 1f, 1f, 2f, new Color(1f, 0.3f, 1f));
            FillRect(px, W, H, pp.b.x - 0.5f, pp.b.y - 1f, 1f, 2f, new Color(0.7f, 0.2f, 0.7f));
            float lo = Mathf.Min(pp.a.x, pp.b.x), hi = Mathf.Max(pp.a.x, pp.b.x);
            FillRect(px, W, H, lo, 3.9f, hi - lo, 0.12f, new Color(1f, 0.3f, 1f));
        }
        foreach (var t in lvl.Traps)
        {
            var c = t.type == TrapType.RealExit ? new Color(0.3f,1f,0.4f)
                  : t.type == TrapType.SpikeStatic ? new Color(1f,0.25f,0.25f)
                  : new Color(1f, 0.75f, 0.2f);
            FillRect(px, W, H, t.pos.x - t.size.x/2f, t.pos.y - t.size.y/2f, t.size.x, t.size.y, c);
        }

        // Max-jump arc from every solid platform's right-top edge: a faint dotted
        // parabola showing exactly how far a running, held jump reaches. If the
        // next platform's left edge sits under the arc, the gap is jumpable; if a
        // ghost/night gap needs to be UN-jumpable, the arc must fall short of it.
        foreach (var p in lvl.Platforms)
        {
            float edgeX = p.pos.x + p.size.x / 2f;
            float edgeY = p.pos.y + p.size.y / 2f;
            DrawJumpArc(px, W, H, edgeX, edgeY);
        }

        FillRect(px, W, H, lvl.Spawn.x - 0.3f, lvl.Spawn.y - 0.3f, 0.6f, 0.6f, new Color(0.3f,0.8f,1f));

        tex.SetPixels(px); tex.Apply();
        var path = Path.Combine(Path.GetTempPath(), file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log("LEVELMAP_DUMPED: " + path + "  rooms=" + lvl.Rooms.Count);
    }

    // Trace the base (no-glide, no-double-jump) max jump and plot it as dots.
    static void DrawJumpArc(Color[] px, int W, int H, float startX, float startY)
    {
        float vx = MoveSpeed, vy = JumpSpeed, x = 0f, y = 0f;
        var dot = new Color(1f, 1f, 1f, 1f);
        for (int i = 0; i < 400; i++)
        {
            float gScale = vy > 0.1f ? RiseG : FallG;
            vy -= G * gScale * Dt;
            x += vx * Dt; y += vy * Dt;
            if (i % 3 == 0) Plot(px, W, H, startX + x, startY + y, dot);
            if (y < -1.2f && vy < 0f) break;   // ~one platform-height below takeoff
        }
    }

    static void Plot(Color[] px, int W, int H, float wx, float wy, Color c)
    {
        int x = Mathf.RoundToInt((wx - MinX) * PPU), y = Mathf.RoundToInt((wy - MinY) * PPU);
        if (x < 0 || x >= W || y < 0 || y >= H) return;
        px[y * W + x] = c;
    }

    static void FillRect(Color[] px, int W, int H, float x, float y, float w, float h, Color c)
    {
        int x0 = Mathf.RoundToInt((x - MinX) * PPU), x1 = Mathf.RoundToInt((x + w - MinX) * PPU);
        int y0 = Mathf.RoundToInt((y - MinY) * PPU), y1 = Mathf.RoundToInt((y + h - MinY) * PPU);
        for (int yy = Mathf.Max(0, y0); yy < Mathf.Min(H, y1); yy++)
            for (int xx = Mathf.Max(0, x0); xx < Mathf.Min(W, x1); xx++)
                px[yy * W + xx] = c;
    }
}
