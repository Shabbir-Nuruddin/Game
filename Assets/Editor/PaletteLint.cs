using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Editor-only: turns the gothic palette LAW (stone / blood-red / bone / candle-
// gold only — no purple, blue, green, cyan, bright orange) into a repeatable
// scan. Walks every hex literal and float-Color literal under Assets/Scripts and
// flags any saturated colour whose hue falls outside the sanctioned blood/gold
// bands (neutrals — stone/bone/ink — pass on low saturation). Run before any
// commit that touches colour, and after Phase 4 it should report zero (minus the
// justified whitelist). Never compiled into a build.
public static class PaletteLint
{
    // A colour PASSES if it's a neutral (low saturation = stone/bone/ink/black)
    // or its hue sits in the blood-red or candle-gold band. Everything else —
    // purple, blue, green, cyan, bright orange — is a violation.
    const float NeutralSat = 0.30f;      // below this, hue doesn't matter (greys)
    const float VeryDark = 0.24f;        // castle-night atmosphere reads as black; not a violation

    // Files whose colours are a DELIBERATE, owner-approved identity system that
    // the castle palette law doesn't govern. Boss attack telegraphs are colour-
    // coded per boss on purpose (Countess violet, Warlock blue…); the multi-world
    // tints (crypt blue, swamp green, abyss violet…) are the World theming. This
    // keeps the report a focused list of TRAP + castle-UI violations, which is
    // the actual Phase-4 worklist.
    static readonly string[] SkipFiles = { "Boss.cs", "BossAnimator.cs" };

    // Distinctive tail-comment phrases on the intentional multi-world tint tables
    // in GameRoot (crypt/swamp/abyss/void/inferno washes). Matched as substrings —
    // chosen to be phrases that never appear in ordinary code.
    static readonly string[] SkipLineMarks =
        { "cold blue", "sickly green", "— violet", "— teal", "ember", "cold steel" };

    static readonly (float lo, float hi)[] AllowedHue =
    {
        (0.940f, 1.001f),   // blood red (wrapping up to 360)
        (0.000f, 0.045f),   // blood red (wrapping from 0)
        (0.075f, 0.16f),    // candle gold / amber (≈27°–58°)
    };

    // Intentional exceptions the heuristic would trip. Keep this SHORT and
    // justified; every entry is a small debt. hex (upper, no #) → reason.
    static readonly Dictionary<string, string> Whitelist = new()
    {
        // (empty — Phase 4 should not need to add here; if it does, say why)
    };

    [MenuItem("Trust Issues/Palette Lint")]
    public static void Lint()
    {
        var dir = Path.Combine(Application.dataPath, "Scripts");
        var hex = new Regex("\"([0-9A-Fa-f]{6})\"");
        var flt = new Regex(@"new\s+Color\(\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?");
        var report = new StringBuilder();
        int violations = 0;

        foreach (var path in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);
            if (System.Array.IndexOf(SkipFiles, name) >= 0) continue;
            var lines = File.ReadAllLines(path);
            string rel = "Assets/Scripts/" + Path.GetRelativePath(dir, path).Replace('\\', '/');
            for (int i = 0; i < lines.Length; i++)
            {
                bool skipLine = false;
                foreach (var mark in SkipLineMarks)
                    if (lines[i].Contains(mark)) { skipLine = true; break; }
                if (skipLine) continue;
                foreach (Match m in hex.Matches(lines[i]))
                {
                    string h = m.Groups[1].Value.ToUpperInvariant();
                    if (!TryHex(h, out var c)) continue;
                    if (Whitelist.ContainsKey(h)) continue;
                    if (!Passes(c)) { violations++; Log(report, rel, i + 1, "#" + h, c, lines[i]); }
                }
                foreach (Match m in flt.Matches(lines[i]))
                {
                    var c = new Color(F(m, 1), F(m, 2), F(m, 3));
                    if (!Passes(c)) { violations++; Log(report, rel, i + 1, ColorHex(c), c, lines[i]); }
                }
            }
        }

        Debug.Log($"PALETTELINT_BEGIN violations={violations}\n{report}PALETTELINT_END");
    }

    static bool Passes(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (v <= VeryDark) return true;      // near-black
        if (s < NeutralSat) return true;     // stone / bone / ink
        foreach (var band in AllowedHue)
            if (h >= band.lo && h < band.hi) return true;
        return false;
    }

    static void Log(StringBuilder sb, string file, int line, string col, Color c, string src)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        sb.AppendLine($"{file}:{line}  {col}  hue={h*360f:000}° sat={s:0.00} val={v:0.00}   {src.Trim()}");
    }

    static bool TryHex(string h, out Color c)
    {
        c = Color.black;
        if (!int.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, null, out int r)) return false;
        if (!int.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, null, out int g)) return false;
        if (!int.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, null, out int b)) return false;
        c = new Color(r / 255f, g / 255f, b / 255f);
        return true;
    }

    static float F(Match m, int g) => float.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
    static string ColorHex(Color c) =>
        $"({c.r:0.00},{c.g:0.00},{c.b:0.00})";
}
