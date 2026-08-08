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
//     time — fake floors, invisible surprises, reactive drops)
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
        TrapType.FakeFloor, TrapType.Surprise, TrapType.FakeExit,
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
        // SHAPE, tracked separately from difficulty. A campaign can have forty
        // floors with forty different ideas in them and still feel like one
        // long floor, because every one of them is the same length, cut into
        // the same number of chambers, at the same pace. That's a repetition
        // the difficulty score cannot see, so it gets its own table.
        var shape = new StringBuilder();
        shape.AppendLine("floor | rooms | length | longest room | rules");
        var roomCounts = new Dictionary<int, int>();
        var lengthBuckets = new Dictionary<int, int>();
        var problems = new List<string>();

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

            // ---- can it actually be played? ----
            // These floors are hand-laid by the metre, so a mistyped platform
            // width is a floor nobody can finish, and the only way that used to
            // surface was a player getting stuck. Same two checks the Endless
            // audit runs: no hole wider than a jump without something to cross
            // it on, and solid ground under every chamber's respawn point.
            float reach = lvl.PrecisionPlatforming ? 5.5f : 12f;
            float hole = WidestHole(lvl, out bool aided);
            if (hole > reach + 0.15f && !aided)
                problems.Add($"floor {f}: UNCROSSABLE {hole:F1}u hole");
            foreach (var r in lvl.Rooms)
                if (!Grounded(lvl, r.MinX + 1.3f))
                    problems.Add($"floor {f}: NO RESPAWN GROUND at {r.MinX + 1.3f:F1}");

            // ---- shape ----
            float lo = float.MaxValue, hi = float.MinValue, longest = 0f;
            foreach (var p in lvl.Platforms)
            {
                lo = Mathf.Min(lo, p.pos.x - p.size.x / 2f);
                hi = Mathf.Max(hi, p.pos.x + p.size.x / 2f);
            }
            var rules = new List<string>();
            foreach (var r in lvl.Rooms)
            {
                longest = Mathf.Max(longest, r.MaxX - r.MinX);
                rules.Add(r.Rule.ToString().Substring(0, 2));
            }
            float len = hi - lo;
            int rc = lvl.Rooms.Count;
            roomCounts[rc] = roomCounts.TryGetValue(rc, out var rn) ? rn + 1 : 1;
            int bucket = Mathf.FloorToInt(len / 25f) * 25;
            lengthBuckets[bucket] = lengthBuckets.TryGetValue(bucket, out var ln) ? ln + 1 : 1;
            shape.AppendLine($"{f,5} | {rc,5} | {len,6:F0} | {longest,12:F0} | {string.Join(" ", rules.ToArray())}");
        }

        Directory.CreateDirectory("Builds");
        File.WriteAllText("Builds/difficulty.csv", sb.ToString());
        Debug.Log("DIFFICULTY_XRAY\n" + summary);

        // The variety verdict, in two lines: how many floors share a room count,
        // and how many share a length. If either is dominated by a single value,
        // the campaign has one shape wearing forty costumes.
        var spread = new StringBuilder();
        spread.Append("rooms per floor: ");
        foreach (var kv in roomCounts) spread.Append($"{kv.Key}→{kv.Value} floors   ");
        spread.AppendLine();
        spread.Append("length (units):  ");
        foreach (var kv in lengthBuckets) spread.Append($"{kv.Key}-{kv.Key + 24}→{kv.Value}   ");
        Debug.Log("CASTLE_SHAPE\n" + shape + "\n" + spread);
        Debug.Log(problems.Count == 0
            ? "CASTLE_PLAYABLE_OK — every floor crossable, every chamber has respawn ground"
            : $"CASTLE_PROBLEMS ({problems.Count})\n" + string.Join("\n", problems.ToArray()));

        DumpBloodMoon();
        DumpGenerated();
        DumpTrapCoverage();
        Debug.Log("DIFFICULTY_DONE -> Builds/difficulty.csv");
    }

    /// <summary>
    /// The same x-ray, pointed at BLOOD MOON. The Castle got audited for a year
    /// while the daily mode — the one that asks people to come back every night —
    /// was never scored at all, which is how it ended up harder than any Castle
    /// floor. Blood Moon is meant to sit a step ABOVE the Castle's ramp and no
    /// more, so this prints each night's score next to the Castle floor it should
    /// feel like, plus the two things that actually make a night unfinishable:
    /// blind traps with no honest ground between them, and no checkpoints.
    /// </summary>
    static void DumpBloodMoon()
    {
        var t = new StringBuilder();
        t.AppendLine("night | plats | traps | untel | checkpts | widest gap | score");
        var problems = new List<string>();

        // Three different days, because the seed varies the spacing — if the ramp
        // only holds on one day's layout it does not hold.
        int[] seeds = { 20260808, 20260809, 20261225 };
        // The authored curve, verified by this tool. Night 1 sits at Castle floor
        // 1-2; night 5 sits between Castle 24 and 29 — a step above where a new
        // player will be in the campaign, which is exactly what a nightly
        // challenge should feel like, and nowhere near the old ~14.
        float[] target = { 1.5f, 3.2f, 4.8f, 5.6f, 6.8f };

        for (int night = 1; night <= 5; night++)
        {
            float scoreSum = 0f; int runs = 0;
            int traps = 0, untel = 0, checkpts = 0, plats = 0;
            float widest = 0f;

            foreach (int seed in seeds)
            {
                var lvl = Levels.BloodMoonNight(seed * 31 + (night - 1) * 7919, night);
                int tr = 0, un = 0, cp = 0;
                float lastBlindX = -9999f;
                foreach (var sp in lvl.Traps)
                {
                    if (sp.type == TrapType.Checkpoint) { cp++; continue; }
                    if (Cosmetic.Contains(sp.type)) continue;
                    tr++;
                    if (!Untelegraphed.Contains(sp.type)) continue;
                    un++;
                    // THE TROLL RHYTHM: a blind trap needs honest ground in front
                    // of it, or the player never gets to form the instinct the
                    // trap is punishing — it just reads as a wall.
                    if (sp.pos.x - lastBlindX < 7f)
                        problems.Add($"night {night} (seed {seed}): blind traps only " +
                                     $"{sp.pos.x - lastBlindX:F1}u apart at x={sp.pos.x:F1}");
                    lastBlindX = sp.pos.x;
                }
                float g = WidestGap(lvl, -9999f, 9999f);
                // Glide gaps are INTENDED to be uncrossable on foot (that is the
                // flight lesson), so they are scored, not flagged. Anything past
                // the bat's reach is a different matter.
                if (g > 7.5f) problems.Add($"night {night} (seed {seed}): {g:F1}u gap — past glide reach");
                if (night >= 2 && cp == 0) problems.Add($"night {night} (seed {seed}): NO CHECKPOINT");

                scoreSum += (tr - un) * 0.33f + un * 1.0f + (g >= 4.7f ? 0.5f : 0f);
                traps = tr; untel = un; checkpts = cp; plats = lvl.Platforms.Count;
                widest = Mathf.Max(widest, g); runs++;
            }

            float score = scoreSum / Mathf.Max(1, runs);
            t.AppendLine($"{night,5} | {plats,5} | {traps,5} | {untel,5} | {checkpts,8} | " +
                         $"{widest,10:F2} | {score,5:F1}");
            if (Mathf.Abs(score - target[night - 1]) > 1.6f)
                problems.Add($"night {night}: score {score:F1} is off its authored target {target[night - 1]:F1}");
        }

        Debug.Log("BLOODMOON_XRAY (Castle floors run 1.3 → 8.6 across FORTY floors)\n" + t);
        Debug.Log(problems.Count == 0
            ? "BLOODMOON_OK — five nights, ramped, checkpointed, every blind trap spaced"
            : $"BLOODMOON_PROBLEMS ({problems.Count})\n" + string.Join("\n", problems.ToArray()));
    }

    /// <summary>
    /// Endless and the Versus race track are rolled fresh every time, so the only
    /// honest audit is a big sample. Walks many seeds of each and reports the
    /// score spread plus anything physically uncrossable — a generated level that
    /// cannot be finished is a far worse bug than a hard one, and it only ever
    /// showed up as a player standing at a hole.
    /// </summary>
    static void DumpGenerated()
    {
        var t = new StringBuilder();
        t.AppendLine("mode | sample | avg traps | avg untel | worst gap | avg score | worst score");
        var problems = new List<string>();

        // Endless tiers 1-7 over its rhythm profiles, and the race track as it is
        // actually built (difficulty 2, race pool).
        Sample(t, problems, "endless", 240, i =>
            Levels.Generate(9001 + i * 7919, Mathf.Min(7, 1 + i / 2), false, i));
        Sample(t, problems, "versus", 120, i =>
            Levels.Generate(4200 + i * 101, 2, race: true));

        Debug.Log("GENERATED_XRAY\n" + t);
        Debug.Log(problems.Count == 0
            ? "GENERATED_OK — every sampled Endless chunk and race track is crossable"
            : $"GENERATED_PROBLEMS ({problems.Count})\n" + string.Join("\n", problems.ToArray()));
    }

    static void Sample(StringBuilder t, List<string> problems, string name, int n,
                       System.Func<int, Level> make)
    {
        float sumTraps = 0f, sumUntel = 0f, sumScore = 0f, worstGap = 0f, worstScore = 0f;
        for (int i = 0; i < n; i++)
        {
            var lvl = make(i);
            int tr = 0, un = 0;
            foreach (var sp in lvl.Traps)
            {
                if (Cosmetic.Contains(sp.type)) continue;
                tr++;
                if (Untelegraphed.Contains(sp.type)) un++;
            }
            float hole = WidestHole(lvl, out bool aided);
            // 12u is the bat's practical reach (jump + a full glide). Past that a
            // generated chunk is a dead end, not a challenge.
            if (hole > 12f && !aided) problems.Add($"{name} #{i}: UNCROSSABLE {hole:F1}u hole");
            float g = WidestGap(lvl, -9999f, 9999f);
            float score = (tr - un) * 0.33f + un * 1.0f + (g >= 4.7f ? 0.5f : 0f);
            sumTraps += tr; sumUntel += un; sumScore += score;
            worstGap = Mathf.Max(worstGap, g); worstScore = Mathf.Max(worstScore, score);
        }
        t.AppendLine($"{name,7} | {n,6} | {sumTraps / n,9:F1} | {sumUntel / n,9:F1} | " +
                     $"{worstGap,9:F1} | {sumScore / n,9:F1} | {worstScore,11:F1}");
    }

    /// <summary>
    /// Every trap in the Bestiary has to be EARNABLE. A page unlocks the first
    /// time that trap kills you, so a trap type sitting in Codex.Entries that no
    /// level actually builds is a collection nobody on earth can complete —
    /// invisible from inside the game, and exactly the kind of thing a player
    /// grinds for hours before working out it was never possible. This walks
    /// every hand-built floor, all five Blood Moon nights and a wide sample of
    /// generated content, and reports which book pages are reachable where.
    /// </summary>
    static void DumpTrapCoverage()
    {
        var seen = new Dictionary<TrapType, List<string>>();
        void Note(Level lvl, string where)
        {
            if (lvl == null) return;
            foreach (var t in lvl.Traps)
            {
                if (!seen.TryGetValue(t.type, out var list)) seen[t.type] = list = new List<string>();
                if (!list.Contains(where)) list.Add(where);
            }
        }

        for (int f = 1; f <= Floors; f++) Note(GetFloor(f), "castle");
        for (int n = 1; n <= 5; n++) Note(Levels.BloodMoonNight(20260809 * 31 + (n - 1) * 7919, n), "bloodmoon");
        for (int i = 0; i < 240; i++) Note(Levels.Generate(9001 + i * 7919, Mathf.Min(7, 1 + i / 2), false, i), "endless");
        for (int i = 0; i < 120; i++) Note(Levels.Generate(4200 + i * 101, Mathf.Min(3, 2 + i / 2), race: true), "versus");

        var t2 = new StringBuilder();
        t2.AppendLine("bestiary page | reachable in");
        var unreachable = new List<string>();
        foreach (var entry in Codex.Entries)
        {
            bool found = seen.TryGetValue(entry, out var where);
            t2.AppendLine($"{entry,-14}| {(found ? string.Join(" ", where.ToArray()) : "NOWHERE")}");
            if (!found) unreachable.Add(entry.ToString());
        }
        Debug.Log($"TRAP_COVERAGE ({Codex.Entries.Length} book pages)\n" + t2);
        Debug.Log(unreachable.Count == 0
            ? "TRAP_COVERAGE_OK — every Bestiary page can actually be earned in play"
            : $"TRAP_COVERAGE_PROBLEMS — unearnable pages: {string.Join(", ", unreachable.ToArray())}");
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

    /// <summary>
    /// Every span the player can stand on, left to right. Fake floors count —
    /// they hold you long enough to cross, and the collapse is the joke, not a
    /// wall — as do vanishing floors, spectral bridges and bobbing slabs.
    /// </summary>
    static List<Vector2> Ground(Level lvl)
    {
        var spans = new List<Vector2>();
        foreach (var p in lvl.Platforms)
        {
            if (Mathf.Abs(p.pos.y - (-3f)) > 0.4f) continue;
            if (p.size.x < 0.8f) continue;
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

    /// <summary>The widest hole, and whether a portal or a gravity rune crosses it.</summary>
    static float WidestHole(Level lvl, out bool aided)
    {
        aided = false;
        var spans = Ground(lvl);
        if (spans.Count < 2) return 0f;
        float widest = 0f, l = 0f, r = 0f, reach = spans[0].y;
        for (int i = 1; i < spans.Count; i++)
        {
            float gap = spans[i].x - reach;
            if (gap > widest) { widest = gap; l = reach; r = spans[i].x; }
            reach = Mathf.Max(reach, spans[i].y);
        }
        foreach (var pp in lvl.Portals)
            if (Mathf.Min(pp.a.x, pp.b.x) <= l + 0.5f && Mathf.Max(pp.a.x, pp.b.x) >= r - 0.5f) aided = true;
        if (lvl.HasGravity) aided = true;   // the ceiling is the road
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
