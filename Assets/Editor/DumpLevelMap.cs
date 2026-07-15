using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using TrustIssues;

// Editor-only: draws a side-on map of a built Castle floor to a PNG, so room
// geometry can be checked at a glance — dividers standing on solid floor,
// doorways actually reachable, gaps jumpable, rules on the rooms you meant.
// The levels are built in code, so this is the only way to SEE one without
// launching and walking it. Rooms are shaded by rule (Dark rooms near-black).
// Never compiled into a build (it lives under Assets/Editor).
public static class DumpLevelMap
{
    const float PPU = 22f;   // pixels per world unit
    const float MinX = -14f, MaxX = 46f, MinY = -6f, MaxY = 5f;
    const int Floors = 2;    // grow as the roomed floors land

    [MenuItem("Trust Issues/Dump Level Map")]
    public static void Dump()
    {
        for (int i = 0; i < Floors; i++)
        {
            var m = typeof(Levels).GetMethod("L" + (i + 1), BindingFlags.NonPublic | BindingFlags.Static);
            var lvl = (Level)m.Invoke(null, null);
            Draw(lvl, "level_map_L" + (i + 1) + ".png");
        }
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
        }
        foreach (var p in lvl.Platforms)
            FillRect(px, W, H, p.pos.x - p.size.x/2f, p.pos.y - p.size.y/2f, p.size.x, p.size.y,
                     new Color(0.65f, 0.62f, 0.70f));
        // Floor that stops existing when the lights die — drawn distinctly so the
        // gap it leaves behind can be checked against a blind jump.
        foreach (var p in lvl.NightFloors)
            FillRect(px, W, H, p.pos.x - p.size.x/2f, p.pos.y - p.size.y/2f, p.size.x, p.size.y,
                     new Color(0.35f, 0.55f, 1f));
        foreach (var t in lvl.Traps)
        {
            var c = t.type == TrapType.RealExit ? new Color(0.3f,1f,0.4f)
                  : t.type == TrapType.SpikeStatic ? new Color(1f,0.25f,0.25f)
                  : new Color(1f, 0.75f, 0.2f);
            FillRect(px, W, H, t.pos.x - t.size.x/2f, t.pos.y - t.size.y/2f, t.size.x, t.size.y, c);
        }
        // spawn
        FillRect(px, W, H, lvl.Spawn.x - 0.3f, lvl.Spawn.y - 0.3f, 0.6f, 0.6f, new Color(0.3f,0.8f,1f));

        tex.SetPixels(px); tex.Apply();
        var path = Path.Combine(Path.GetTempPath(), file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log("LEVELMAP_DUMPED: " + path + "  rooms=" + lvl.Rooms.Count);
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
