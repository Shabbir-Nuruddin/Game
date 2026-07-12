using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TrustIssues.EditorTools
{
    /// <summary>
    /// Paints the game's app icon from code — the same way every other art
    /// asset in this project is made — and wires it in as the DEFAULT icon for
    /// all platforms (so the Android APK stops shipping the Unity logo).
    ///
    /// Composition (512×512, the game's exact palette): near-black night sky,
    /// a huge blood moon with a soft halo, the castle as a hard black
    /// silhouette with candle-gold windows, and a few bats crossing the moon.
    ///
    /// Menu: Trust Issues → Generate App Icon. Also runnable headless via
    ///   -executeMethod TrustIssues.EditorTools.MakeAppIcon.Build
    /// Deterministic (fixed seed): re-running reproduces the identical PNG.
    /// </summary>
    public static class MakeAppIcon
    {
        const int S = 512;
        const string PngPath = "Assets/AppIcon/app_icon.png";

        [MenuItem("Trust Issues/Generate App Icon")]
        public static void Build()
        {
            var px = new Color[S * S];

            // ---- sky: vertical gradient, near-black up top → dark maroon low ----
            Color skyTop = Hex("070510"), skyLow = Hex("300A14");
            for (int y = 0; y < S; y++)
            {
                var row = Color.Lerp(skyLow, skyTop, (float)y / (S - 1));
                for (int x = 0; x < S; x++) px[y * S + x] = row;
            }

            // ---- sparse dim stars (upper half only, fixed seed) ----
            var rng = new System.Random(13);
            for (int i = 0; i < 90; i++)
            {
                int x = rng.Next(0, S), y = rng.Next(S / 2, S);
                float a = 0.25f + (float)rng.NextDouble() * 0.5f;
                Blend(px, x, y, new Color(1f, 0.92f, 0.85f, a * 0.5f));
            }

            // ---- the blood moon: solid core + soft halo, upper-centre ----
            float mx = S * 0.5f, my = S * 0.62f, core = S * 0.30f, halo = S * 0.44f;
            Color moonCore = Hex("FF3A3C"), moonRim = Hex("B01622");
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - mx) * (x - mx) + (y - my) * (y - my));
                    if (d <= core)
                    {
                        // subtly darker toward the rim so the disc reads as a sphere
                        var c = Color.Lerp(moonCore, moonRim, Mathf.Pow(d / core, 2.2f));
                        px[y * S + x] = c;
                    }
                    else if (d <= halo)
                    {
                        float a = Mathf.Pow(1f - (d - core) / (halo - core), 2f) * 0.55f;
                        Blend(px, x, y, new Color(moonCore.r, moonCore.g * 0.6f, moonCore.b * 0.6f, a));
                    }
                }

            // ---- castle silhouette: skyline height per column, hard black ----
            // Towers flank a tall central keep with a spire; crenellated tops.
            Color silhouette = Hex("0B0810");
            for (int x = 0; x < S; x++)
            {
                int h = SkylineHeight(x);
                for (int y = 0; y < h && y < S; y++) px[y * S + x] = silhouette;
            }

            // ---- candle-gold windows (slim pointed-arch slits) ----
            Color candle = Hex("E0B33A");
            int[]  winX = {  72, 150, 236, 262, 288, 372, 436 };
            int[]  winY = { 120,  70, 150, 200, 150,  80, 130 };
            for (int i = 0; i < winX.Length; i++) Window(px, winX[i], winY[i], candle);

            // ---- bats crossing the moon (the game's own 5-row bat pattern) ----
            string[] bat = { "..X.....X..", ".XXX...XXX.", "XXXXX.XXXXX", "XXXXXXXXXXX", ".XX.XXX.XX." };
            DrawBat(px, bat, 175, 415, 3, silhouette);
            DrawBat(px, bat, 320, 370, 4, silhouette);
            DrawBat(px, bat, 250, 450, 2, silhouette);

            // ---- write the PNG + import it ----
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels(px); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(PngPath));
            File.WriteAllBytes(PngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PngPath, ImportAssetOptions.ForceUpdate);

            // Icons must not be compressed to mush — keep the source crisp.
            var imp = (TextureImporter)AssetImporter.GetAtPath(PngPath);
            imp.textureType = TextureImporterType.Default;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();

            // Default icon for every platform (Android/iOS/desktop all inherit
            // it unless a per-platform override is set later).
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(PngPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MakeAppIcon] wrote {PngPath} and set it as the default app icon.");
        }

        // Castle skyline: silhouette height (px from the bottom) for column x.
        static int SkylineHeight(int x)
        {
            int h;
            if      (x <  36) h = 96;                      // low outer wall
            else if (x < 116) h = 236;                     // left tower
            else if (x < 196) h = 140;                     // wall
            else if (x < 316) h = 290;                     // central keep
            else if (x < 396) h = 150;                     // wall
            else if (x < 476) h = 256;                     // right tower
            else              h = 96;                      // low outer wall
            // spire: triangular roof rising from the keep's top
            if (x >= 216 && x < 296)
            {
                int d = Mathf.Abs(x - 256);
                h = Mathf.Max(h, 290 + (40 - d) * 2);      // peak ≈ 370
            }
            // crenellations: notch every other 12px block on flat tops
            else if ((x / 12) % 2 == 1 && h > 100) h -= 14;
            return h;
        }

        // A slim pointed-arch window slit with a faint warm glow around it.
        static void Window(Color[] px, int cx, int cy, Color candle)
        {
            for (int y = 0; y < 22; y++)
                for (int x = -4; x <= 4; x++)
                {
                    // taper the top 8 rows into a point (gothic arch)
                    int half = y > 14 ? Mathf.Max(0, 4 - (y - 14) / 2) : 4;
                    if (Mathf.Abs(x) <= half) Set(px, cx + x, cy + y, candle);
                }
            for (int y = -6; y < 28; y++)
                for (int x = -9; x <= 9; x++)
                    Blend(px, cx + x, cy + y, new Color(candle.r, candle.g, candle.b, 0.10f));
        }

        static void DrawBat(Color[] px, string[] rows, int cx, int cy, int scale, Color col)
        {
            int w = rows[0].Length, h = rows.Length;
            for (int ry = 0; ry < h; ry++)
                for (int rx = 0; rx < w; rx++)
                {
                    if (rows[ry][rx] != 'X') continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                            Set(px, cx + (rx - w / 2) * scale + sx, cy - ry * scale + sy, col);
                }
        }

        static void Set(Color[] px, int x, int y, Color c)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            px[y * S + x] = c;
        }

        static void Blend(Color[] px, int x, int y, Color c)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            var b = px[y * S + x];
            px[y * S + x] = new Color(
                b.r + (c.r - b.r) * c.a, b.g + (c.g - b.g) * c.a, b.b + (c.b - b.b) * c.a, 1f);
        }

        static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }
    }
}
