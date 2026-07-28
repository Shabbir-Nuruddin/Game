using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using TrustIssues;

// Editor-only: the DIFFICULTY X-RAY.
//
// Levels are built in code by a fluent builder, so "how hard is floor 4?" was
// only ever answerable by playing it. That's how floor 1 ended up opening with a
// gap, a spike and a fake floor in its first stage — nobody could see the ramp
// as a whole. This walks every built floor and prints, per STAGE:
//
//   • how many traps are in it, and of what kind
//   • how many of those are UNTELEGRAPHED (kill you with no warning the first
//     time — fake floors, late spikes, invisible surprises, reactive drops)
//   • the widest gap you have to jump
//   • a crude "expected deaths" score
//
// The score is deliberately simple: a telegraphed trap you can see coming costs
// about a third of a death to learn, an untelegraphed one costs a full death
// (you cannot avoid it the first time — that IS the joke), and a gap near the
// jump limit costs half. Summed per floor it lines up with the real analytics
// well enough to tune against, which is the whole point — the target is a floor
// you clear in ~4-5 deaths, not 40.
//
//   Unity.exe -batchmode -quit -projectPath . \
//     -executeMethod DumpDifficulty.Dump -logFile difficulty.log
//
// Writes Builds/difficulty.csv and logs a summary table.
public static class DumpDifficulty
{
    const int Floors = 39;

    // Traps you CANNOT dodge on a first encounter — there is no tell, or the tell
    // arrives after the commitment. These are the game's identity, but they are
    // also its entire death count, so their density per stage is the real dial.
    static readonly HashSet<TrapType> Untelegraphed = new()
    {
        TrapType.FakeFloor, TrapType.LateSpike, TrapType.Surprise, TrapType.FakeExit,
        TrapType.Faller, TrapType.Chandelier, TrapType.Crusher, TrapType.Dart,
        TrapType.WarpBack, TrapType.Reverse,
    };

    // Traps that are fully visible and on a readable cycle — you die to these
    // through mistiming, which is a fair death and cheap to learn.
    static readonly HashSet<TrapType> Cosmetic = new()
    {
        TrapType.Checkpoint, TrapType.RealExit, TrapType.BreakBlock,
    };

    [MenuItem("Trust Issues/Dump Difficulty X-Ray")]
    public static void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("floor,stage,traps,untelegraphed,widest_gap,score");
        var summary = new StringBuilder();
        summary.AppendLine("floor | stages | traps | untel | widest gap | score");

        for (int f = 1; f <= Floors; f++)
        {
            var lvl = GetFloor(f);
            if (lvl == null) continue;                     // 20/30/40 = boss arenas

            int stages = Mathf.Max(1, lvl.Rooms.Count);
            float floorScore = 0f;
            int floorTraps = 0, floorUntel = 0;
            float floorWidest = 0f;

            for (int s = 0; s < stages; s++)
            {
                float minX = stages > 1 ? lvl.Rooms[s].MinX : -999f;
                float maxX = stages > 1
                    ? (s + 1 < stages ? lvl.Rooms[s + 1].MinX : 9999f)
                    : 9999f;

                int traps = 0, untel = 0;
                foreach (var t in lvl.Traps)
                {
                    if (t.pos.x < minX || t.pos.x >= maxX) continue;
                    if (Cosmetic.Contains(t.type)) continue;
                    traps++;
                    if (Untelegraphed.Contains(t.type)) untel++;
                }

                float widest = WidestGap(lvl, minX, maxX);
                // Telegraphed traps are cheap to learn; blind ones cost a life each;
                // a gap within 0.8u of the jump limit is a coin flip until muscle
                // memory sets in.
                float score = (traps - untel) * 0.33f + untel * 1.0f + (widest >= 4.7f ? 0.5f : 0f);

                sb.AppendLine($"{f},{s + 1},{traps},{untel},{widest:F2},{score:F2}");
                floorScore += score; floorTraps += traps; floorUntel += untel;
                floorWidest = Mathf.Max(floorWidest, widest);
            }

            summary.AppendLine($"{f,5} | {stages,6} | {floorTraps,5} | {floorUntel,5} | " +
                               $"{floorWidest,10:F2} | {floorScore,5:F1}");
        }

        Directory.CreateDirectory("Builds");
        File.WriteAllText("Builds/difficulty.csv", sb.ToString());
        Debug.Log("DIFFICULTY_XRAY\n" + summary);
        Debug.Log("DIFFICULTY_DONE -> Builds/difficulty.csv");
    }

    /// <summary>
    /// The widest run of empty floor inside [minX,maxX). Platforms are laid
    /// left-to-right and never overlap, so sorting by left edge and measuring the
    /// gap to the next one is exact. Only near-floor slabs count — walls, ceilings
    /// and ledges are not things you jump ACROSS.
    /// </summary>
    static float WidestGap(Level lvl, float minX, float maxX)
    {
        var spans = new List<Vector2>();   // (left, right) of each floor slab
        foreach (var p in lvl.Platforms)
        {
            if (Mathf.Abs(p.pos.y - (-3f)) > 0.4f) continue;   // floor level only
            if (p.size.x < 0.8f) continue;                     // that's a wall, not a floor
            float l = p.pos.x - p.size.x / 2f, r = p.pos.x + p.size.x / 2f;
            if (r <= minX || l >= maxX) continue;
            spans.Add(new Vector2(l, r));
        }
        // Fake floors read as solid until they betray you, so for "can I physically
        // cross this?" they count as ground — the death they cause is the joke, not
        // an impossible jump.
        foreach (var t in lvl.Traps)
        {
            if (t.type != TrapType.FakeFloor) continue;
            float l = t.pos.x - t.size.x / 2f, r = t.pos.x + t.size.x / 2f;
            if (r <= minX || l >= maxX) continue;
            spans.Add(new Vector2(l, r));
        }
        if (spans.Count < 2) return 0f;
        spans.Sort((a, b) => a.x.CompareTo(b.x));

        float widest = 0f, reach = spans[0].y;
        for (int i = 1; i < spans.Count; i++)
        {
            float gap = spans[i].x - reach;
            if (gap > widest) widest = gap;
            reach = Mathf.Max(reach, spans[i].y);
        }
        return widest;
    }

    /// <summary>Levels.Lnn() are private statics — reflection is the only way in
    /// without loosening their access purely for a tool.</summary>
    static Level GetFloor(int floor)
    {
        var m = typeof(Levels).GetMethod("L" + floor,
                    BindingFlags.NonPublic | BindingFlags.Static);
        return m?.Invoke(null, null) as Level;
    }
}
