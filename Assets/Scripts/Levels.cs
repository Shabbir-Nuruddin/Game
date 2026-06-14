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
        public void LateSpike(float x) => T(TrapType.LateSpike, x, -2.4f, 1.0f, 1.2f);
        public void Dart(float x) => T(TrapType.Dart, x, -2.3f, 1.0f, 1.2f);
        public void Faller(float x) => T(TrapType.Faller, x, -2.3f, 1.2f, 1.2f);
        public void Surprise(float x) => T(TrapType.Surprise, x, -2.2f, 0.8f, 1.0f);
        public void Saw(float x) => T(TrapType.Saw, x, -2.2f, 0.9f, 0.9f);
        public void Reverse(float x) => T(TrapType.Reverse, x, -2.3f, 1.5f, 1.2f);
        public void WarpBack(float x) => T(TrapType.WarpBack, x, -2.3f, 0.8f, 1.2f);
        public void Crusher(float x) { T(TrapType.Crusher, x, -1f, 1.6f, 1.4f); L.Decos.Add(new Deco(x, -0.6f, 0.35f, 0.35f, Theme.Coin)); }
        public void Spring(float x) { T(TrapType.Spring, x, -2.55f, 1.0f, 0.5f); T(TrapType.Surprise, x, -0.4f, 1.4f, 1.4f); }

        public Level Finish()
        {
            float pre = Plat(3.5f);
            Gap(1.6f);
            float endc = Plat(3.5f);
            T(TrapType.FakeExit, endc + 0.6f, -2f, 1f, 2f);
            T(TrapType.RealExit, (pre + endc) / 2f, -5f, 1.3f, 1.2f);
            L.CamMinX = -1.5f;
            L.CamMaxX = Mathf.Max(-1.5f, cur - 10f);
            return L;
        }
    }

    public static class Levels
    {
        public static int Count => 10;

        public static Level Get(int index)
        {
            switch (((index % Count) + Count) % Count)
            {
                case 0: return L1(); case 1: return L2(); case 2: return L3();
                case 3: return L4(); case 4: return L5(); case 5: return L6();
                case 6: return L7(); case 7: return L8(); case 8: return L9();
                default: return L10();
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

        // 3 — darts and a faller.
        static Level L3()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Dart(p2 - 1f); b.Spike(p2 + 1f);
            b.Gap(2.5f);
            float p3 = b.Plat(3.5f); b.Faller(p3);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Crusher(p4);
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

        // 5 — saw + a bait spring into hidden spikes.
        static Level L5()
        {
            var b = new B();
            b.Plat(3.5f);
            b.Gap(2.5f);
            float p2 = b.Plat(4f); b.Saw(p2);
            b.Gap(2.5f);
            float p3 = b.Plat(4f); b.Spring(p3 - 1f);
            b.Gap(2.5f);
            float p4 = b.Plat(3.5f); b.Spike(p4);
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
            float p3 = b.Plat(4f); b.Dart(p3 - 1f); b.Faller(p3 + 1f);
            b.Gap(2.8f);
            float p4 = b.Plat(4f); b.Saw(p4);
            b.FakeFloor(2f);
            float p5 = b.Plat(3.5f); b.Surprise(p5);
            return b.Finish();
        }
    }
}
