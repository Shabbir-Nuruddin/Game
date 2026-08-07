using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TrustIssues;

// Editor-only: the ENDLESS AUDIT.
//
// Endless chunks are generated, so "is 4km still beatable?" has no author to
// ask. This walks a few hundred hidden chunks across several run seeds and checks the
// things a human playtester would only find by dying:
//
//   • UNCROSSABLE GAP — a hole wider than a plain jump (5.5u; chambered floors
//     suppress the glide and the double-jump) with no slab, no spectral bridge,
//     no portal and no gravity rune to cross it on. This is the one that makes a
//     floor literally impossible, so it's a hard failure.
//   • NO GROUND AT A RESPAWN — a death drops you at the chamber's left edge
//     + 1.3. If that spot is a pit, the floor kills you forever.
//   • AN OVERLONG PRESS ROOM — the vault spans the whole chamber and reaches
//     head height about four seconds after it fires. A press chamber longer
//     than ~22 units can't be run in time.
//   • VISIBLE EXIT — chunk seams must never leak a coffin/floor ending into the
//     continuous distance run.
//
// It also prints the difficulty score (same maths as the Castle X-Ray) so the
// ramp and five pacing rhythms can be tuned against the target death rate.
//
//   Unity.exe -batchmode -quit -projectPath . \
//     -executeMethod DumpEndless.Dump -logFile endless.log
//
// Writes Builds/endless.csv and logs a summary.
public static class DumpEndless
{
    const int Chunks = 64;                                  // how deep to audit
    static readonly int[] Seeds = { 12345, 777, 999999, 42, 31337 };
    static readonly string[] Rhythms = { "balanced", "sprint", "glide", "gauntlet", "breather" };
    // A chambered floor suppresses the bat glide and the double-jump, so a plain
    // running jump (~5.5u) is all you have. An open-sky floor keeps the wings,
    // and a jump held into a glide carries about 12u — which is the whole point
    // of the shape that has no chambers.
    const float PrecisionReach = 5.5f, GlideReach = 12f;

    static readonly HashSet<TrapType> Untelegraphed = new()
    {
        TrapType.FakeFloor, TrapType.Surprise, TrapType.FakeExit,
        TrapType.Faller, TrapType.Chandelier, TrapType.Crusher, TrapType.Dart,
        TrapType.WarpBack, TrapType.Reverse,
    };

    [MenuItem("Trust Issues/Dump Endless Audit")]
    public static void Dump()
    {
        var csv = new StringBuilder();
        csv.AppendLine("seed,chunk,rhythm,rooms,traps,untelegraphed,widest_gap,aid,score,problem");
        var problems = new List<string>();
        var summary = new StringBuilder();

        foreach (int seed in Seeds)
        {
            for (int f = 0; f < Chunks; f++)
            {
                string rhythm = Rhythms[f % Rhythms.Length];
                var lvl = Levels.Generate(seed + f * 7919, Mathf.Min(7, 1 + f / 2), false, f);
                if (lvl == null) { problems.Add($"seed {seed} chunk {f}: null level"); continue; }

                int traps = 0, untel = 0;
                foreach (var t in lvl.Traps)
                {
                    if (t.type == TrapType.Checkpoint || t.type == TrapType.RealExit) continue;
                    traps++;
                    if (Untelegraphed.Contains(t.type)) untel++;
                }

                var problem = new List<string>();
                if (lvl.Traps.Exists(t => t.type == TrapType.RealExit))
                    problem.Add("VISIBLE EXIT");
                float widest = WidestHole(lvl, out string aid);
                float reach = lvl.PrecisionPlatforming ? PrecisionReach : GlideReach;
                if (widest > reach + 0.15f && aid == "none")
                    problem.Add($"UNCROSSABLE {widest:F1}u");

                foreach (var r in lvl.Rooms)
                {
                    if (!Grounded(lvl, r.MinX + 1.3f))
                        problem.Add($"NO RESPAWN GROUND @ {r.MinX + 1.3f:F1}");
                    if (r.Rule == RoomRule.Press && r.MaxX - r.MinX > 22f)
                        problem.Add($"PRESS ROOM {r.MaxX - r.MinX:F0}u");
                }
                float score = (traps - untel) * 0.33f + untel * 1.0f
                            + (widest >= reach - 0.8f && aid == "none" ? 0.5f : 0f);
                string p = string.Join(" | ", problem);
                csv.AppendLine($"{seed},{f},{rhythm},{lvl.Rooms.Count},{traps},{untel},{widest:F2},{aid},{score:F2},{p}");
                if (problem.Count > 0) problems.Add($"seed {seed} chunk {f} ({rhythm}): {p}");

                if (seed == Seeds[0] && f < 24)
                    summary.AppendLine($"{f,3} | {rhythm,-10} | rooms {lvl.Rooms.Count} | traps {traps,2} " +
                                       $"| blind {untel,2} | gap {widest,5:F1} ({aid}) | score {score,5:F1}");
            }
        }

        Directory.CreateDirectory("Builds");
        File.WriteAllText("Builds/endless.csv", csv.ToString());

        Debug.Log("ENDLESS_FIRST_24\n" + summary);
        Debug.Log(problems.Count == 0
            ? $"ENDLESS_AUDIT_OK — {Seeds.Length * Chunks} chunks, no impossible geometry or visible exits"
            : $"ENDLESS_AUDIT_PROBLEMS ({problems.Count})\n" + string.Join("\n", problems.ToArray()));
        Debug.Log("ENDLESS_DONE -> Builds/endless.csv");
    }

    /// <summary>
    /// Every span you can stand on, in the order they're laid. Fake floors count
    /// (they hold you long enough to cross — the collapse is the joke, not a
    /// wall), as do vanishing floors, spectral bridges and the bobbing slabs.
    /// </summary>
    static List<Vector2> Ground(Level lvl)
    {
        var spans = new List<Vector2>();
        foreach (var p in lvl.Platforms)
        {
            if (Mathf.Abs(p.pos.y - (-3f)) > 0.4f) continue;   // floor level only
            if (p.size.x < 0.8f) continue;                     // that's a wall
            spans.Add(new Vector2(p.pos.x - p.size.x / 2f, p.pos.x + p.size.x / 2f));
        }
        foreach (var t in lvl.Traps)
            if (t.type == TrapType.FakeFloor)
                spans.Add(new Vector2(t.pos.x - t.size.x / 2f, t.pos.x + t.size.x / 2f));
        foreach (var n in lvl.NightFloors) spans.Add(new Vector2(n.pos.x - n.size.x / 2f, n.pos.x + n.size.x / 2f));
        foreach (var g in lvl.GhostFloors) spans.Add(new Vector2(g.pos.x - g.size.x / 2f, g.pos.x + g.size.x / 2f));
        foreach (var m in lvl.Movers) spans.Add(new Vector2(m.x - m.w / 2f, m.x + m.w / 2f));
        spans.Sort((a, b) => a.x.CompareTo(b.x));
        return spans;
    }

    static bool Grounded(Level lvl, float x)
    {
        foreach (var s in Ground(lvl)) if (x >= s.x - 0.05f && x <= s.y + 0.05f) return true;
        return false;
    }

    /// <summary>
    /// The widest hole, and what (if anything) is there to get you over it: a
    /// portal pair straddling it, or a gravity floor where the ceiling is the
    /// road. Anything else and the hole has to be inside jump reach.
    /// </summary>
    static float WidestHole(Level lvl, out string aid)
    {
        aid = "none";
        var spans = Ground(lvl);
        if (spans.Count < 2) return 0f;

        float widest = 0f, holeL = 0f, holeR = 0f, reach = spans[0].y;
        for (int i = 1; i < spans.Count; i++)
        {
            float gap = spans[i].x - reach;
            if (gap > widest) { widest = gap; holeL = reach; holeR = spans[i].x; }
            reach = Mathf.Max(reach, spans[i].y);
        }
        if (widest <= 0f) return 0f;

        foreach (var pp in lvl.Portals)
            if (Mathf.Min(pp.a.x, pp.b.x) <= holeL + 0.5f && Mathf.Max(pp.a.x, pp.b.x) >= holeR - 0.5f)
            { aid = "portal"; return widest; }
        if (lvl.HasGravity) { aid = "grav"; return widest; }
        return widest;
    }

}
