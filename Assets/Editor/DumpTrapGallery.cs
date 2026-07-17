using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TrustIssues;

// Editor-only: renders the game's PROCEDURAL, code-drawn art to a single PNG
// sheet composited over the real dark backdrop, so on-theme-ness and shape can
// be judged without launching. Fable can't see a running game, so this is how
// the palette/visual work (chandelier, pendulum, flame, holy water, etc.) gets
// eyeballed. Extend `Entries()` as Phase-4 adds new procedural generators.
//
// Also lays out the palette as swatches, so any off-band colour (the whole point
// of the palette law) is visible next to the art that uses it.
public static class DumpTrapGallery
{
    const int Cell = 128, Pad = 14, Cols = 5, Label = 18;

    [MenuItem("Trust Issues/Dump Trap Gallery")]
    public static void Dump()
    {
        var swatches = Swatches();
        var art = Entries();

        int rows = 1 + Mathf.CeilToInt(art.Count / (float)Cols);   // row 0 = palette
        int W = Cols * (Cell + Pad) + Pad;
        int H = rows * (Cell + Pad + Label) + Pad;
        var bg = Hex("120A16");                                     // the castle night
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = bg;

        // Row 0: palette swatches — the law made visible.
        for (int i = 0; i < swatches.Count && i < Cols; i++)
            Swatch(px, W, H, i, 0, swatches[i].Item2);

        // Remaining rows: procedural art over the backdrop.
        for (int i = 0; i < art.Count; i++)
        {
            int col = i % Cols, row = 1 + i / Cols;
            Blit(px, W, H, col, row, art[i].Item2, bg);
        }

        tex.SetPixels(px); tex.Apply();
        var path = Path.Combine(Path.GetTempPath(), "trap_gallery.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log("TRAPGALLERY_DUMPED: " + path + "  art=" + art.Count);
    }

    // (label, sprite). Grow this as procedural generators land in Phase 4.
    static List<(string, Sprite)> Entries() => new()
    {
        ("Square",   Theme.Square),
        ("Disc",     Theme.Disc),
        ("Ring",     Theme.Ring),
        ("Circle",   Theme.Circle),
        ("Bat",      Theme.Bat),
        ("BatGlyph", Theme.BatGlyph),
        ("Stone",    Theme.StoneTile),
        ("Moon",     Theme.Moon),
    };

    // (label, color) — the sanctioned palette bands.
    static List<(string, Color)> Swatches() => new()
    {
        ("stone",  Theme.Platform),
        ("blood",  Theme.PlatEdge),
        ("danger", Theme.Danger),
        ("gold",   Theme.Coin),
        ("sky",    Theme.Sky),
    };

    static void Swatch(Color[] px, int W, int H, int col, int row, Color c)
    {
        int x0 = Pad + col * (Cell + Pad), y0 = H - (Pad + (row + 1) * (Cell + Label) + row * Pad);
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
                px[(y0 + y) * W + (x0 + x)] = c;
    }

    // Composite a sprite's texture (alpha-over) centred in a cell, on the backdrop.
    static void Blit(Color[] px, int W, int H, int col, int row, Sprite sp, Color bg)
    {
        int x0 = Pad + col * (Cell + Pad), y0 = H - (Pad + (row + 1) * (Cell + Label) + row * Pad);
        if (sp == null) return;
        var src = sp.texture;
        int sw = src.width, sh = src.height;
        float scale = Mathf.Min(Cell / (float)sw, Cell / (float)sh) * 0.9f;
        int dw = Mathf.RoundToInt(sw * scale), dh = Mathf.RoundToInt(sh * scale);
        int ox = x0 + (Cell - dw) / 2, oy = y0 + (Cell - dh) / 2;
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                var c = src.GetPixelBilinear((x + 0.5f) / dw, (y + 0.5f) / dh);
                int di = (oy + y) * W + (ox + x);
                if (di < 0 || di >= px.Length) continue;
                px[di] = Color.Lerp(bg, new Color(c.r, c.g, c.b), c.a);
            }
    }

    static Color Hex(string h) => Theme.Hex(h);
}
