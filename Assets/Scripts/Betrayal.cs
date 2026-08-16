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

        /// <summary>Grit falling off a slab that is about to betray you.</summary>
        public static void Dust(Vector3 at, int n = 6)
        {
            for (int i = 0; i < n; i++)
            {
                var go = Theme.Box("Grit", null, at + new Vector3(Random.Range(-0.8f, 0.8f), -0.2f, 0f),
                                   new Vector2(0.07f, 0.07f), new Color(0.55f, 0.5f, 0.52f, 0.8f), 4);
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
        public float tipAngle = -72f, delay = 0.30f, tipTime = 0.42f;

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
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Tip());
        }

        IEnumerator Tip()
        {
            Audio.Play("click", 0.35f);
            Betray.Dust(transform.position, 5);
            // A small warning lean, then the real one. Two stages so a fast player
            // can read "this is going" and still be gone before it commits.
            float e = 0f;
            while (e < delay)
            {
                e += Time.deltaTime;
                _rb.MoveRotation(Mathf.Lerp(0f, tipAngle * 0.12f, e / delay));
                yield return null;
            }
            e = 0f;
            float from = tipAngle * 0.12f;
            while (e < tipTime)
            {
                e += Time.deltaTime;
                float k = e / tipTime;
                _rb.MoveRotation(Mathf.Lerp(from, tipAngle, k * k));   // accelerating, like a hinge giving way
                yield return null;
            }
            _rb.MoveRotation(tipAngle);
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
        public float travel = 3.4f, delay = 0.26f, slideTime = 0.34f;

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
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Slide());
        }

        IEnumerator Slide()
        {
            Vector2 home = transform.position;
            Audio.Play("click", 0.3f);
            yield return Betray.Shudder(transform, delay, 0.06f);
            Betray.Dust(transform.position, 4);
            float e = 0f;
            while (e < slideTime)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, e / slideTime);
                _rb.MovePosition(home + new Vector2(-travel * k, 0f));
                yield return null;
            }
            _rb.MovePosition(home + new Vector2(-travel, 0f));
        }
    }

    /// <summary>
    /// THE FLOOR THAT TAKES YOU WITH IT. The slab drops as one piece, with the
    /// player still standing on it, and stops at a lower level.
    ///
    /// This one is deliberately NOT lethal by itself — it is the game's only
    /// betrayal that is survivable by doing nothing, which is what makes it
    /// useful: it drops you somewhere ELSE. Whatever is waiting down there is the
    /// actual hazard, and a level can use it as a fork rather than a punishment.
    /// </summary>
    public class DropSlab : MonoBehaviour
    {
        public Vector2 size = new Vector2(3f, 0.6f);
        // 1.7, and it is a HARD CEILING on this number. Jump speed is 14 against a
        // rise gravity of 3.4 on Unity's -9.81, which is a maximum jump height of
        // 2.94 units — so a slab that drops further than that leaves the player
        // standing in a hole they cannot climb out of, and the floor becomes
        // unfinishable. The first draft of this used 3.2 and did exactly that.
        public float drop = 1.7f, delay = 0.34f, fallTime = 0.5f;

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
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Fall());
        }

        IEnumerator Fall()
        {
            Vector2 home = transform.position;
            yield return Betray.Shudder(transform, delay, 0.07f);
            Betray.Dust(transform.position, 7);
            Audio.Play("dash", 0.45f);
            float e = 0f;
            while (e < fallTime)
            {
                e += Time.deltaTime;
                float k = e / fallTime;
                _rb.MovePosition(home + new Vector2(0f, -drop * k * k));   // gravity-ish
                yield return null;
            }
            _rb.MovePosition(home + new Vector2(0f, -drop));
            GameRoot.I?.ShakeCam(0.25f, 0.16f);
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
        public float rise = 5.2f, delay = 0.38f, riseTime = 0.85f, ceilY = 3.1f;

        Rigidbody2D _rb;
        bool _going;
        GameObject _crush;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        void Update()
        {
            if (_going || !Betray.Riding(transform, size)) return;
            _going = true;
            StartCoroutine(Push());
        }

        IEnumerator Push()
        {
            Vector2 home = transform.position;
            yield return Betray.Shudder(transform, delay, 0.05f);
            Audio.Play("dash", 0.4f);

            // The killing edge only exists once the gap to the ceiling is too small
            // to stand in — before that the slab is just a lift.
            float e = 0f;
            while (e < riseTime)
            {
                e += Time.deltaTime;
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
                yield return null;
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
                e += Time.deltaTime;
                _rb.MovePosition(home + new Vector2(dir * travel * (e / slamTime), 0f));
                yield return null;
            }
            _rb.MovePosition(home + new Vector2(dir * travel, 0f));
            yield return new WaitForSeconds(hold);
            // Grinds back out, so the lane it stole is given back rather than the
            // level ending in a wall you cannot pass.
            e = 0f;
            while (e < 0.8f)
            {
                e += Time.deltaTime;
                _rb.MovePosition(home + new Vector2(dir * travel * (1f - e / 0.8f), 0f));
                yield return null;
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
