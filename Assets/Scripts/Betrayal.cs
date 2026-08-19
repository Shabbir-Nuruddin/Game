using System.Collections;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// THE ROOM ITSELF IS THE TRAP.
    ///
    /// Every hazard the game had before this file was an OBJECT STANDING ON THE
    /// GROUND — a spike, a saw, a swinging blade. The x-ray says so plainly: of
    /// 424 hazards placed across the castle, 68 are spikes, 62 are saws and 41 are
    /// pendulums, and all three are answered by the same input (jump, or wait then
    /// jump). That is why forty floors read as one floor. The ground was the one
    /// thing the player was allowed to trust, so every level became a corridor
    /// with furniture in it.
    ///
    /// These take the ground away. A floor that tips, slides out, drops with you
    /// still on it, or rises to press you into the ceiling is not another obstacle
    /// on the route — it IS the route, betraying you. Same for the walls and the
    /// ceiling, and for the exit that backs away when you reach for it.
    ///
    /// Three rules hold everywhere in this file:
    ///
    ///   1. EVERY BETRAYAL IS SURVIVABLE ON REACTION. Each one telegraphs for at
    ///      least ~0.25s (a shudder, a grind, dust) before it can kill. The joke is
    ///      that you trusted the floor, not that you had no way to know.
    ///   2. NOTHING RESETS ITSELF. The level is rebuilt from scratch every life
    ///      (GameRoot.BuildLevel), so these fire once and stay fired. A floor that
    ///      re-arms behind you turns a reaction test into a memory test.
    ///   3. MOVING GEOMETRY IS KINEMATIC. Anything that carries or pushes the
    ///      player uses a kinematic Rigidbody2D and MovePosition, so Unity resolves
    ///      the contact instead of the player tunnelling through it.
    /// </summary>
    static class Betray
    {
        /// <summary>How close the player has to be before a betrayal wakes up.</summary>
        public const float Sense = 0.9f;

        /// <summary>The tell. Everything here shudders before it does anything.</summary>
        public static IEnumerator Shudder(Transform t, float time, float amp = 0.05f)
        {
            Vector3 home = t.position;
            float e = 0f;
            while (e < time)
            {
                e += Time.deltaTime;
                t.position = home + new Vector3(Mathf.Sin(e * 60f) * amp, 0f, 0f);
                yield return null;
            }
            t.position = home;
        }

        /// <summary>
        /// Grit falling off a slab that is about to betray you. Sized for a PHONE:
        /// at ~120 px per world unit the old 0.07 specks were 8 px of dull grey
        /// against dark stone. These are wider, brighter and spread across the
        /// slab, so the shower reads as "this thing is letting go".
        /// </summary>
        public static void Dust(Vector3 at, int n = 6)
        {
            for (int i = 0; i < n; i++)
            {
                var go = Theme.Box("Grit", null, at + new Vector3(Random.Range(-1.6f, 1.6f), -0.2f, 0f),
                                   new Vector2(0.12f, 0.12f), new Color(0.78f, 0.70f, 0.66f, 0.95f), 4);
                go.AddComponent<Grit>();
            }
        }

        /// <summary>Is the player standing on this slab right now?</summary>
        public static bool Riding(Transform slab, Vector2 size)
        {
            var p = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            if (p == null) return false;
            Vector3 d = p.position - slab.position;
            return Mathf.Abs(d.x) < size.x * 0.5f + 0.35f && d.y > 0f && d.y < size.y * 0.5f + 1.2f;
        }

        /// <summary>Player's distance from a point, or a big number if there's no player.</summary>
        public static float Near(Vector3 at)
        {
            var p = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            return p == null ? 999f : Vector3.Distance(p.position, at);
        }

        /// <summary>
        /// Is the player still WALKING TOWARD this slab, close enough to be warned?
        ///
        /// This is the fix for the mistake that made the first version of every
        /// floor betrayal pointless. They armed on CONTACT and then spent ~0.3s
        /// telegraphing — but at 7.5 u/s a player crosses a 3-unit slab in 0.40s,
        /// so the warning and the trap were fighting over the same fifth of a
        /// second. The slab was still leaning 12 degrees as the player stepped off
        /// the far edge; the drop floor had fallen five centimetres. The telegraph
        /// ate the trap.
        ///
        /// Warning on approach separates them: ~0.4s of groaning while the player
        /// can still stop, jump or turn back, and then the betrayal itself is free
        /// to be fast and final.
        /// </summary>
        public static bool Approaching(Transform t, Vector2 size, float from = 3.0f)
            => Near(t.position) < from + size.x * 0.5f;

        /// <summary>
        /// The approach tell: grit, a groan, and a slab that will not hold still.
        /// Cancels the instant the real betrayal starts (via <paramref name="stop"/>)
        /// so it can never fight the kinematic body for the transform.
        /// </summary>
        /// <summary>The colour a slab flushes to while it is about to betray you.</summary>
        public static readonly Color WarnTint = new Color(0.72f, 0.26f, 0.21f);

        public static IEnumerator Creak(Transform t, System.Func<bool> stop, float time = 0.6f)
        {
            Audio.Play("click", 0.32f);
            Dust(t.position, 6);

            // THE SLAB HAS TO CHANGE COLOUR, not just wobble.
            //
            // The camera shows about 20 world units across, so on a 1080p phone one
            // unit is ~120 px and the old 0.035 shudder was a FOUR PIXEL wiggle —
            // invisible at arm's length to a player watching their own thumb. Now
            // that these floors kill, a tell nobody can see makes the game unfair
            // rather than funny. A whole-slab tint is the only warning that survives
            // a small screen, so the shudder is now the supporting act.
            var sr = t.GetComponent<SpriteRenderer>();
            Color home = sr != null ? sr.color : Color.white;

            Vector3 hp = t.position;
            float e = 0f;
            while (e < time && !stop())
            {
                e += Time.deltaTime;
                t.position = hp + new Vector3(Mathf.Sin(e * 46f) * 0.055f, 0f, 0f);
                if (sr != null)
                {
                    // Pulses rather than holds: a steady colour reads as decoration,
                    // a throb reads as a countdown.
                    float pulse = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(e * 9f));
                    sr.color = Color.Lerp(home, WarnTint, pulse * Mathf.Clamp01(e / 0.15f));
                }
                if (e > time * 0.4f && Random.value < 0.10f) Dust(hp, 1);
                yield return null;
            }
            t.position = hp;
            // Fired → stay red (it is mid-betrayal). Walked away → back to stone, so
            // a slab you chose not to touch doesn't stay marked for the whole floor.
            if (sr != null) sr.color = stop() ? WarnTint : home;
        }
    }

    /// <summary>A speck of falling stone. Purely a tell — it can't hurt you.</summary>
    public class Grit : MonoBehaviour
    {
        float _v, _life = 0.9f;
        void Update()
        {
            _v -= 14f * Time.deltaTime;
            transform.position += new Vector3(0f, _v * Time.deltaTime, 0f);
            _life -= Time.deltaTime;
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) { var c = sr.color; c.a = Mathf.Clamp01(_life); sr.color = c; }
            if (_life <= 0f) Destroy(gameObject);
        }
    }

    // ==================================================================
    // FLOOR BETRAYALS
    // ==================================================================

    /// <summary>
    /// THE TIPPING SLAB. Stand on it and it tips like a see-saw and pours you off.
    /// The tell is a visible lean the instant your weight lands — you have about a
    /// third of a second to get off, which is exactly one reaction.
    ///
    /// It pivots about its CENTRE, not an end: MoveRotation turns a body about its
    /// own origin, and the slab's origin is its middle. That is the better trap
    /// anyway — a see-saw drops whichever side you are standing on, so the same
    /// slab punishes a cautious player edging on and a fast one running across.
    /// </summary>
    public class TiltSlab : MonoBehaviour
    {
        public Vector2 size = new Vector2(3f, 0.6f);
        // 0.05 + 0.22 = fully over a quarter-second after your weight lands, and a
        // 3-unit slab takes 0.40s to walk. So the hinge gives way at roughly
        // two-thirds across: too late to finish, which is the entire point. The
        // first version spent 0.30s on a warning lean and reached 12 degrees
        // before the player was already gone.
        public float tipAngle = -78f, delay = 0.05f, tipTime = 0.22f;

        Rigidbody2D _rb;
        bool _going, _armed;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        void Update()
        {
            if (!_armed && Betray.Approaching(transform, size))
            {
                _armed = true;
                StartCoroutine(Betray.Creak(transform, () => _going));
            }
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Tip());
        }

        IEnumerator Tip()
        {
            Audio.Play("dash", 0.4f);
            Betray.Dust(transform.position, 6);
            yield return new WaitForSeconds(delay);
            float e = 0f;
            while (e < tipTime)
            {
                e += Time.fixedDeltaTime;
                float k = e / tipTime;
                _rb.MoveRotation(Mathf.Lerp(0f, tipAngle, k * k));   // accelerating, like a hinge giving way
                yield return new WaitForFixedUpdate();
            }
            _rb.MoveRotation(tipAngle);
            GameRoot.I?.ShakeCam(0.18f, 0.1f);
        }
    }

    /// <summary>
    /// THE FLOOR THAT LEAVES. A slab that slides sideways out from under you into
    /// the wall, opening a pit where you are standing. Reads completely differently
    /// from the collapsing FakeFloor: that one drops and is gone, this one is
    /// visibly still there, just no longer under your feet — so the instinct it
    /// teaches (run, don't jump) is the opposite one.
    /// </summary>
    public class SlideSlab : MonoBehaviour
    {
        public Vector2 size = new Vector2(3f, 0.6f);
        // Out from under you in 0.24s against the 0.40s you need to cross it, and
        // it travels its own full width so the hole it leaves is a real hole. At
        // the old 0.26 + 0.34 it had shifted about a unit before you were clear —
        // visibly moving, never actually a threat.
        public float travel = 3.4f, delay = 0.05f, slideTime = 0.19f;

        Rigidbody2D _rb;
        bool _going, _armed;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        void Update()
        {
            if (!_armed && Betray.Approaching(transform, size))
            {
                _armed = true;
                StartCoroutine(Betray.Creak(transform, () => _going));
            }
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Slide());
        }

        IEnumerator Slide()
        {
            Vector2 home = transform.position;
            Audio.Play("dash", 0.45f);
            Betray.Dust(transform.position, 6);
            GameRoot.I?.ShakeCam(0.2f, 0.1f);
            yield return new WaitForSeconds(delay);
            float e = 0f;
            while (e < slideTime)
            {
                e += Time.fixedDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, e / slideTime);
                _rb.MovePosition(home + new Vector2(-travel * k, 0f));
                yield return new WaitForFixedUpdate();
            }
            _rb.MovePosition(home + new Vector2(-travel, 0f));
        }
    }

    /// <summary>
    /// THE TRAPDOOR. The floor you are standing on stops being a floor. It falls
    /// away beneath you into the dark and you go down with it.
    ///
    /// The first version of this dropped 1.7 units and stopped, on the reasoning
    /// that max jump height is 2.94 so anything deeper would strand the player in
    /// a hole. That reasoning was sound and the result was worthless: it was a
    /// lift, not a trap — you rode it down and hopped out, which is exactly what
    /// playtesters reported. "Stranded" was the wrong thing to protect against.
    /// The right answer is to drop the slab clean past the kill plane so the
    /// player falls to their death instead of standing in a pit, which is both
    /// more honest and far funnier.
    ///
    /// Fairness lives entirely in the approach: the slab creaks and sheds grit for
    /// ~0.4s while you walk at it, and it is 3.2 wide against a 5.6-unit jump, so
    /// it is always clearable by someone who read the warning.
    /// </summary>
    public class DropSlab : MonoBehaviour
    {
        public Vector2 size = new Vector2(3f, 0.6f);
        // Deep enough to clear the kill plane (floor sits at -3, the plane is at
        // -9), fast enough that you cannot outrun it: gone in a quarter second
        // against the 0.43s it takes to cross.
        public float drop = 8.5f, delay = 0.04f, fallTime = 0.26f;

        Rigidbody2D _rb;
        bool _going, _armed;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        void Update()
        {
            if (!_armed && Betray.Approaching(transform, size))
            {
                _armed = true;
                StartCoroutine(Betray.Creak(transform, () => _going));
            }
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Fall());
        }

        IEnumerator Fall()
        {
            Vector2 home = transform.position;
            Betray.Dust(transform.position, 10);
            Audio.Play("dash", 0.55f);
            GameRoot.I?.ShakeCam(0.3f, 0.12f);
            yield return new WaitForSeconds(delay);
            float e = 0f;
            while (e < fallTime)
            {
                e += Time.fixedDeltaTime;
                float k = e / fallTime;
                _rb.MovePosition(home + new Vector2(0f, -drop * k * k));   // gravity-ish
                yield return new WaitForFixedUpdate();
            }
            _rb.MovePosition(home + new Vector2(0f, -drop));
            Destroy(gameObject, 0.3f);   // it is gone; nothing left to land on
        }
    }

    /// <summary>
    /// THE PRESS FROM BELOW. Ground that rises and crushes you into the ceiling —
    /// the exact inverse of the Crusher, and the reason it matters: the Crusher
    /// taught "stay low", and this floor kills you for it.
    ///
    /// Lethal only in the last stretch of its travel, so the answer is to be off
    /// it (or on top of the block beside it) rather than to have guessed early.
    /// </summary>
    public class RisePress : MonoBehaviour
    {
        public Vector2 size = new Vector2(3f, 0.6f);
        // 5.2 so the slab actually finishes ABOVE the height at which the crushing
        // edge arms (ceiling 3.1, arming when the headroom drops under 1.15, i.e.
        // slab y > 1.65). At the first draft's 4.6 it topped out at exactly 1.6 and
        // the press could never kill anyone — an intimidating lift and nothing more.
        // The slowest of the betrayals, and the last one that could still be
        // outrun: at 0.08 + 0.50 it finished in 0.58s, while a player crosses even
        // a 4.2-wide slab in 0.56s. Now it commits in 0.37s — and because it
        // CARRIES you, you are airborne with it long before that, so stepping off
        // the side stops being a stroll and becomes a fall.
        public float rise = 5.2f, delay = 0.05f, riseTime = 0.32f, ceilY = 3.1f;

        Rigidbody2D _rb;
        bool _going, _armed;
        GameObject _crush;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        void Update()
        {
            if (!_armed && Betray.Approaching(transform, size))
            {
                _armed = true;
                StartCoroutine(Betray.Creak(transform, () => _going));
            }
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Push());
        }

        IEnumerator Push()
        {
            Vector2 home = transform.position;
            Betray.Dust(transform.position, 6);
            Audio.Play("dash", 0.5f);
            GameRoot.I?.ShakeCam(0.22f, 0.12f);
            yield return new WaitForSeconds(delay);

            // The killing edge only exists once the gap to the ceiling is too small
            // to stand in — before that the slab is just a lift.
            float e = 0f;
            while (e < riseTime)
            {
                e += Time.fixedDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, e / riseTime);
                Vector2 at = home + new Vector2(0f, rise * k);
                _rb.MovePosition(at);
                if (_crush == null && ceilY - (at.y + size.y * 0.5f) < 1.15f)
                {
                    _crush = new GameObject("Press");
                    _crush.transform.SetParent(transform, false);
                    _crush.transform.localPosition = new Vector3(0f, size.y * 0.5f + 0.42f, 0f);
                    var c = _crush.AddComponent<BoxCollider2D>();
                    c.isTrigger = true; c.size = new Vector2(size.x, 0.8f);
                    var kz = _crush.AddComponent<KillZone>();
                    kz.msg = "The floor pressed you into the ceiling.";
                    kz.trapTag = (int)TrapType.RiseFloor;
                }
                yield return new WaitForFixedUpdate();
            }
            GameRoot.I?.ShakeCam(0.3f, 0.2f);
        }
    }

    // ==================================================================
    // WALLS AND CEILINGS
    // ==================================================================

    /// <summary>
    /// THE WALL THAT ARRIVES. A slab drives in horizontally from off-lane as you
    /// pass, either squashing you against the far side or simply taking the space
    /// you were about to run through.
    ///
    /// It stops short of a full seal — the surviving gap is always at least a body
    /// wide — so being caught is a reaction failure, never a dead end.
    /// </summary>
    public class SlamWall : MonoBehaviour
    {
        public float travel = 3.6f, delay = 0.3f, slamTime = 0.16f, hold = 1.1f;
        public int dir = -1;              // -1 drives left (into the player's path)

        Rigidbody2D _rb;
        bool _going;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        void Update()
        {
            if (_going || Betray.Near(transform.position) > 5.2f) return;
            _going = true;
            StartCoroutine(Slam());
        }

        IEnumerator Slam()
        {
            Vector2 home = transform.position;
            yield return Betray.Shudder(transform, delay, 0.09f);
            GameRoot.I?.ShakeCam(0.2f, 0.1f);
            Audio.Play("dash", 0.55f);
            float e = 0f;
            while (e < slamTime)
            {
                e += Time.fixedDeltaTime;
                _rb.MovePosition(home + new Vector2(dir * travel * (e / slamTime), 0f));
                yield return new WaitForFixedUpdate();
            }
            _rb.MovePosition(home + new Vector2(dir * travel, 0f));
            yield return new WaitForSeconds(hold);
            // Grinds back out, so the lane it stole is given back rather than the
            // level ending in a wall you cannot pass.
            e = 0f;
            while (e < 0.8f)
            {
                e += Time.fixedDeltaTime;
                _rb.MovePosition(home + new Vector2(dir * travel * (1f - e / 0.8f), 0f));
                yield return new WaitForFixedUpdate();
            }
            _rb.MovePosition(home);
            _going = false;
        }
    }

    /// <summary>
    /// THE CEILING FIRES. A row of spikes buried in the ceiling that shoot DOWN in
    /// sequence, left to right, as the player passes underneath — so the safe
    /// answer is to run through ahead of the wave or wait for it to finish, not to
    /// stand still and time one blade.
    ///
    /// Distinct from ArrowRain, which is one column on a fixed clock and ignores
    /// you completely. This one is aimed at where you are.
    /// </summary>
    public class CeilingVolley : MonoBehaviour
    {
        public int count = 5;
        public float spacing = 1.15f, ceil = 3.1f, step = 0.11f, speed = 22f;
        bool _fired;

        void Update()
        {
            if (_fired || Betray.Near(transform.position) > 6f) return;
            _fired = true;
            StartCoroutine(Fire());
        }

        IEnumerator Fire()
        {
            float x0 = transform.position.x - (count - 1) * spacing * 0.5f;
            // Every tooth shows itself in the ceiling first — the volley is loud
            // before it is lethal.
            var teeth = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                var at = new Vector3(x0 + i * spacing, ceil - 0.35f, 0f);
                teeth[i] = Theme.Box("Tooth", transform, at, new Vector2(0.34f, 0.7f), Trap.SpikeRim, 3);
                teeth[i].transform.localScale = new Vector3(1f, 0.25f, 1f);
            }
            float e = 0f;
            while (e < 0.28f)
            {
                e += Time.deltaTime;
                foreach (var t in teeth)
                    if (t != null) t.transform.localScale = new Vector3(1f, Mathf.Lerp(0.25f, 1f, e / 0.28f), 1f);
                yield return null;
            }
            Audio.Play("click", 0.4f);

            for (int i = 0; i < count; i++)
            {
                if (teeth[i] == null) continue;
                var col = teeth[i].AddComponent<BoxCollider2D>();
                col.isTrigger = true; col.size = new Vector2(0.34f, 0.7f);
                var kz = teeth[i].AddComponent<KillZone>();
                kz.msg = "The ceiling spat teeth at you.";
                kz.trapTag = (int)TrapType.CeilingVolley;
                teeth[i].AddComponent<Faller2>().speed = speed;
                yield return new WaitForSeconds(step);
            }
        }
    }

    /// <summary>A ceiling tooth on its way down. Dies below the floor line.</summary>
    public class Faller2 : MonoBehaviour
    {
        public float speed = 22f;
        void Update()
        {
            transform.position += Vector3.down * speed * Time.deltaTime;
            if (transform.position.y < -8f) Destroy(gameObject);
        }
    }

    // ==================================================================
    // THE EXIT LIES
    // ==================================================================

    /// <summary>
    /// THE SHY COFFIN. Reach for the exit and it slides away from you — twice —
    /// before it gives up and lets itself be caught.
    ///
    /// Bounded ON PURPOSE. An exit that runs forever is not a joke, it is a
    /// softlock: the retreat count is fixed, each retreat is shorter than the
    /// last, and it never crosses the end wall, so the third approach always
    /// lands. The gag has to end with the player winning or it isn't a gag.
    /// </summary>
    public class ShyExit : MonoBehaviour
    {
        public int retreats = 2;
        public float first = 4.2f, sense = 2.6f, limitX = 9999f;

        int _done;
        bool _moving;

        void Update()
        {
            if (_moving || _done >= retreats) return;
            if (Betray.Near(transform.position) > sense) return;
            StartCoroutine(Skitter());
        }

        IEnumerator Skitter()
        {
            _moving = true;
            // Each retreat is shorter, so the chase visibly runs out of road.
            float dist = first * (1f - _done * 0.45f);
            Vector3 home = transform.position;
            Vector3 to = home + new Vector3(dist, 0f, 0f);
            if (to.x > limitX) to.x = limitX;
            Audio.Play("click", 0.4f);
            float e = 0f, t = 0.34f;
            while (e < t)
            {
                e += Time.deltaTime;
                transform.position = Vector3.Lerp(home, to, Mathf.SmoothStep(0f, 1f, e / t));
                yield return null;
            }
            transform.position = to;
            _done++;
            // A beat of stillness so the player can close in rather than chasing a
            // thing that reacts the instant they move.
            yield return new WaitForSeconds(0.25f);
            _moving = false;
        }
    }
}
