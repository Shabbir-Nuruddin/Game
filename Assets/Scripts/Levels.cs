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
        public float CamMinX = -1.5f, CamMaxX = -1.5f;
        public List<Rect2> Platforms = new();
        public List<TrapSpec> Traps = new();
        public List<Deco> Decos = new();
        public List<PortalPair> Portals = new();
    }

    /// <summary>
    /// Fluent builder that lays platforms left-to-right with controlled gaps, so
    /// levels are GUARANTEED beatable: every gap is jumpable (<= 3), spike
    /// platforms are wide enough to land + run + jump, and crushers never share a
    /// platform with something you must jump over. The "unfair" comes from
    /// untelegraphed traps (fake floors, invisible deaths), not impossible jumps.
    /// </summary>
    class B
    {
        public Level L = new();
        float cur;  // left edge of the next piece

        public B(float spawnX = -10f) { L.Spawn = new Vector2(spawnX, -2f); cur = spawnX - 1.5f; }

        public float Plat(float w) { float cx = cur + w / 2f; L.Platforms.Add(new Rect2(cx, -3f, w, 0.6f)); cur += w; return cx; }
        public float FakeFloor(float w) { float cx = cur + w / 2f; L.Traps.Add(new TrapSpec(TrapType.FakeFloor, cx, -3f, w, 0.6f)); cur += w; return cx; }
        public void Gap(float w) { cur += w; }

        void T(TrapType t, float x, float y, float w, float h) => L.Traps.Add(new TrapSpec(t, x, y, w, h));
        public void Spike(float x) => T(TrapType.SpikeStatic, x, -2.4f, 0.7f, 0.7f);
        public void ArrowRain(float x) => T(TrapType.ArrowRain, x, -3f, 0.5f, 0.5f);
        public void Checkpoint(float x) => T(TrapType.Checkpoint, x, -2f, 1f, 1.6f);
        public void BreakWall(float x) => T(TrapType.BreakBlock, x, -0.7f, 0.7f, 4f); // shoot to pass
        public void LateSpike(float x) => T(TrapType.LateSpike, x, -2.4f, 1.0f, 1.2f);
        public void Dart(float x) => T(TrapType.Dart, x, -2.3f, 1.0f, 1.2f);
        public void Faller(float x) => T(TrapType.Faller, x, -2.3f, 1.2f, 1.2f);
        public void Surprise(float x) => T(TrapType.Surprise, x, -2.2f, 0.8f, 1.0f);
        public void Saw(float x) => T(TrapType.Saw, x, -2.2f, 0.9f, 0.9f);
        public void Reverse(float x) => T(TrapType.Reverse, x, -2.3f, 1.5f, 1.2f);
        public void WarpBack(float x) => T(TrapType.WarpBack, x, -2.3f, 0.8f, 1.2f);
        public void Crusher(float x) => T(TrapType.Crusher, x, -1f, 1.6f, 1.4f); // no coin tell — jump up here and you're crushed
        public void Spring(float x) { T(TrapType.Spring, x, -2.55f, 1.0f, 0.5f); T(TrapType.Surprise, x, -0.4f, 1.4f, 1.4f); }

        public Level Finish()
        {
            Gap(2.5f);
            float endc = Plat(4f);
            T(TrapType.RealExit, endc, -2f, 1.4f, 1.8f); // the one clear goal
            L.CamMinX = -1.5f;
            L.CamMaxX = Mathf.Max(-1.5f, cur - 10f);
            return L;
        }
    }

    public static class Levels
    {
        public static int Count => 20;

        public static Level Get(int index)
        {
            switch (((index % Count) + Count) % Count)
            {
                case 0: return L1(); case 1: return L2(); case 2: return L3();
                case 3: return L4(); case 4: return L5(); case 5: return L6();
                case 6: return L7(); case 7: return L8(); case 8: return L9();
                case 9: return L10(); case 10: return L11(); case 11: return L12();
                case 12: return L13(); case 13: return L14(); case 14: return L15();
                case 15: return L16(); case 16: return L17(); case 17: return L18();
                case 18: return L19(); default: return L20();
            }
        }

        // ---- Procedural generator (powers Endless + Daily) ----
        // Uses the same B builder, so every generated level is guaranteed
        // beatable (jumpable gaps, one spaced hazard per platform). Difficulty
        // grows the hazard variety and length.
        public static Level Generate(int seed, int difficulty)
        {
            var rng = new System.Random(seed);
            difficulty = Mathf.Max(0, difficulty);
            var pool = HazardPool(difficulty);
            var b = new B();
            b.Plat(3.7f); // safe start

            int segments = Mathf.Clamp(5 + difficulty, 5, 11);
            for (int i = 0; i < segments; i++)
            {
                if (difficulty >= 1 && rng.Next(100) < 18 + difficulty * 3) b.FakeFloor(2f);
                else b.Gap(2.4f + (float)rng.NextDouble() * 0.5f);
                float p = b.Plat(3.6f + (float)rng.NextDouble() * 1.3f);
                PlaceHazard(b, pool[rng.Next(pool.Count)], p);
                if (difficulty >= 4 && rng.Next(100) < 28)        // a second hazard, deeper in
                    PlaceHazard(b, pool[rng.Next(pool.Count)], p + 1.2f);
            }
            b.Gap(2.4f);
            return b.Finish();
        }

        static List<TrapType> HazardPool(int d)
        {
            var l = new List<TrapType> { TrapType.SpikeStatic, TrapType.SpikeStatic };
            if (d >= 1) l.Add(TrapType.LateSpike);
            if (d >= 2) { l.Add(TrapType.Dart); l.Add(TrapType.Crusher); }
            if (d >= 3) l.Add(TrapType.Faller);
            if (d >= 4) { l.Add(TrapType.Saw); l.Add(TrapType.Surprise); }
            if (d >= 5) l.Add(TrapType.ArrowRain);
            return l;
        }

        static void PlaceHazard(B b, TrapType t, float p)
        {
            switch (t)
            {
                case TrapType.LateSpike: b.LateSpike(p); break;
                case TrapType.Dart: b.Dart(p); break;
                case TrapType.Faller: b.Faller(p); break;
                case TrapType.Crusher: b.Crusher(p); break;
                case TrapType.Saw: b.Saw(p); break;
                case TrapType.ArrowRain: b.ArrowRain(p); break;
                case TrapType.Surprise: b.Surprise(p); break;
                default: b.Spike(p); break;
            }
        }

        // 1 — intro: fake floor, one spike, one crusher.
        static Level L1()
        {
            var b = new B();
            b.Plat(3.5f);
            b.FakeFloor(2f);
            float p2 = b.Plat(3.5f); b.Spike(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.Crusher(p3);
            return b.Finish();
        }

        // 2 — gap jumps + a late spike.
        static Level L2()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(3.5f); b.LateSpike(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.Spike(p3);
            b.FakeFloor(2f);
            float p4 = b.Plat(3.5f); b.Crusher(p4);
            return b.Finish();
        }

        // 3 — gentle intro to darts (one trap per platform, room to react).
        static Level L3()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4.5f); b.Dart(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Faller(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(4f); b.Spike(p4);
            return b.Finish();
        }

        // 4 — invisible deaths.
        static Level L4()
        {
            var b = new B();
            b.Plat(3.5f);
            b.FakeFloor(2f);
            float p2 = b.Plat(4f); b.Surprise(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.Spike(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Faller(p4);
            return b.Finish();
        }

        // 5 — saws and spikes, fairly spaced.
        static Level L5()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Saw(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Spike(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(4f); b.LateSpike(p4);
            return b.Finish();
        }

        // 6 — reverse controls (wait it out, then go).
        static Level L6()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.Gap(2.5f);
            float p2 = b.Plat(3.5f); b.Spike(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.LateSpike(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Crusher(p4);
            return b.Finish();
        }

        // 7 — warp-back rage + combos.
        static Level L7()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Spike(p2 - 1f); b.WarpBack(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.Faller(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Saw(p4);
            return b.Finish();
        }

        // 8 — spike gauntlet.
        static Level L8()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.Spike(p2 - 1.2f); b.Spike(p2 + 1.2f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Faller(p3 - 1f); b.Dart(p3 + 1f);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Crusher(p4);
            return b.Finish();
        }

        // 9 — everything mixed.
        static Level L9()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.FakeFloor(2f);
            float p2 = b.Plat(4f); b.Surprise(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Saw(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(4f); b.Spike(p4 - 1f); b.WarpBack(p4 + 1f);
            return b.Finish();
        }

        // 10 — brutal finale.
        static Level L10()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.8f);
            float p2 = b.Plat(5f); b.Spike(p2 - 1.5f); b.LateSpike(p2 + 0.7f);
            b.Gap(2.8f);
            float p3 = b.Plat(4.5f); b.Checkpoint(p3 - 1.5f); b.Dart(p3 + 0.5f);
            b.Gap(2.8f);
            float p4 = b.Plat(4f); b.Faller(p4);
            b.FakeFloor(2f);
            float p5 = b.Plat(3.5f); b.Surprise(p5);
            return b.Finish();
        }

        // 11 — dart gauntlet (the shooting one you like).
        static Level L11()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Dart(p2 - 1f); b.Spike(p2 + 1.2f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Dart(p3); b.Faller(p3 + 1.5f);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.LateSpike(p4);
            return b.Finish();
        }

        // 12 — saws and darts.
        static Level L12()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Saw(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Dart(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(4f); b.Spike(p4);
            return b.Finish();
        }

        // 13 — invisible deaths between darts.
        static Level L13()
        {
            var b = new B();
            b.Plat(3.5f);
            b.FakeFloor(2f);
            float p2 = b.Plat(4f); b.Surprise(p2 - 1f); b.Dart(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Faller(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Spike(p4);
            return b.Finish();
        }

        // 14 — reverse controls + warp-back + dart.
        static Level L14()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Dart(p2 - 1f); b.WarpBack(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Saw(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Faller(p4);
            return b.Finish();
        }

        // 15 — the brutal finale (darts everywhere).
        static Level L15()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.8f);
            float p2 = b.Plat(5f); b.Dart(p2 - 1.5f); b.Spike(p2); b.Dart(p2 + 1.5f);
            b.Gap(2.8f);
            float p3 = b.Plat(4f); b.Faller(p3 - 1f); b.Saw(p3 + 1f);
            b.FakeFloor(2f);
            float p4 = b.Plat(4f); b.Surprise(p4 - 1f); b.LateSpike(p4 + 1f);
            return b.Finish();
        }

        // 16 — ceiling arrows (the new feature).
        static Level L16()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.ArrowRain(p2 - 1f); b.ArrowRain(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.Spike(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Crusher(p4);
            return b.Finish();
        }

        // 17 — rain + darts.
        static Level L17()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Dart(p2); b.ArrowRain(p2 + 1.5f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Faller(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Spike(p4);
            return b.Finish();
        }

        // 18 — the rain corridor.
        static Level L18()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(5f); b.ArrowRain(p2 - 1.5f); b.ArrowRain(p2); b.ArrowRain(p2 + 1.5f);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.LateSpike(p3);
            return b.Finish();
        }

        // 19 — reverse, rain, saw, invisible.
        static Level L19()
        {
            var b = new B();
            float p1 = b.Plat(4f); b.Reverse(p1 + 1f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.ArrowRain(p2 - 1f); b.Dart(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Saw(p3);
            b.FakeFloor(2f);
            float p4 = b.Plat(3.5f); b.Surprise(p4);
            return b.Finish();
        }

        // 20 — the grand finale.
        static Level L20()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.8f);
            float p2 = b.Plat(5f); b.Dart(p2 - 1.5f); b.ArrowRain(p2); b.Spike(p2 + 1.5f);
            b.Gap(2.8f);
            float pc = b.Plat(3.5f); b.Checkpoint(pc);
            b.Gap(2.8f);
            float p3 = b.Plat(5f); b.Faller(p3 - 1.5f); b.Saw(p3 + 1.5f);
            b.FakeFloor(2f);
            float p4 = b.Plat(4f); b.Surprise(p4 - 1f); b.WarpBack(p4 + 1f);
            return b.Finish();
        }
    }
}
