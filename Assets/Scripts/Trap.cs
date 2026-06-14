using System.Collections;
using UnityEngine;

namespace TrustIssues
{
    public enum TrapType
    {
        FakeFloor,  // looks solid, collapses a moment after you stand on it
        LateSpike,  // spikes rise up the instant you arrive
        Crusher,    // a block slams down if you go for the high bait
        FakeExit,   // the obvious bright door kills you
        RealExit,   // the unassuming spot that actually wins
        Surprise,   // INVISIBLE kill zone on safe-looking ground — pure unfair
        Dart,       // a projectile fires across the moment you arrive
        Faller,     // an off-screen block drops on you (Thwomp)
        Spring,     // launches you upward (often into hidden spikes)
        Saw,        // a hazard that slides back and forth
        WarpBack,   // yanks you all the way back to the start — rage
        Reverse     // flips your controls for a few seconds
    }

    /// <summary>
    /// One trap, configured by type. Built and reset by GameRoot each life, so
    /// no per-trap reset bookkeeping is needed. The golden-path rule: the
    /// inviting thing betrays you; each trap shows a subtle TELL so a second
    /// death feels earned.
    /// </summary>
    public class Trap : MonoBehaviour
    {
        public TrapType type;
        SpriteRenderer _sr;
        BoxCollider2D _col;
        bool _armed = true;
        Transform _spike;

        public void Init(TrapType t) { type = t; }

        Vector3 _origin;

        void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<BoxCollider2D>();
            _origin = transform.position;
        }

        void Update()
        {
            // A saw slides back and forth across its track.
            if (type == TrapType.Saw)
                transform.position = new Vector3(
                    _origin.x + Mathf.Sin(Time.time * 2.5f) * 2.5f, _origin.y, 0f);
        }

        // ---- solid traps (FakeFloor) use collision; the rest are triggers ----
        void OnCollisionEnter2D(Collision2D c)
        {
            if (type != TrapType.FakeFloor || !_armed) return;
            var pc = c.collider.GetComponent<PlayerController>();
            if (pc == null) return;
            // Only collapse when the player is standing on top of us.
            if (pc.transform.position.y > transform.position.y)
                StartCoroutine(Collapse());
        }

        IEnumerator Collapse()
        {
            _armed = false;
            // A single-frame shudder as the only warning, then the floor is GONE.
            // No time to react the first time — that's the trap. The crack tell
            // means you'll know to jump it next run.
            Vector3 home = transform.position;
            for (int i = 0; i < 3; i++)
            {
                transform.position = home + (Vector3)(Random.insideUnitCircle * 0.05f);
                yield return null;
            }
            _col.enabled = false; // you fall NOW
            float e = 0f;
            Vector3 start = transform.position;
            while (e < 0.3f)
            {
                e += Time.deltaTime;
                transform.position = start + Vector3.down * (e * 14f);
                var col = _sr.color; col.a = 1f - e / 0.3f; _sr.color = col;
                yield return null;
            }
            gameObject.SetActive(false);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var pc = other.GetComponent<PlayerController>();
            if (pc == null) return;

            switch (type)
            {
                case TrapType.LateSpike:
                    if (_armed) StartCoroutine(RaiseSpike(other));
                    break;
                case TrapType.Crusher:
                    if (_armed) StartCoroutine(Crush());
                    break;
                case TrapType.FakeExit:
                    GameRoot.I?.Die("That door? Pure evil.");
                    break;
                case TrapType.RealExit:
                    if (_armed) { _armed = false; GameRoot.I?.ReachExit(); }
                    break;
                case TrapType.Surprise:
                    GameRoot.I?.Die("You did everything right. You still died.");
                    break;
                case TrapType.Dart:
                    if (_armed) { _armed = false; StartCoroutine(FireDart()); }
                    break;
                case TrapType.Faller:
                    if (_armed) { _armed = false; StartCoroutine(DropFaller()); }
                    break;
                case TrapType.Spring:
                    LaunchPlayer(other);
                    break;
                case TrapType.WarpBack:
                    GameRoot.I?.WarpToStart();
                    break;
                case TrapType.Reverse:
                    pc.SetReversed(3f);
                    break;
            }
        }

        // A dart flies in from the right the instant you step on the sensor.
        IEnumerator FireDart()
        {
            var dart = Theme.Box("Dart", transform.parent, transform.position + Vector3.right * 5f,
                new Vector2(0.6f, 0.22f), Theme.Danger, 4);
            var kz = dart.AddComponent<KillZone>(); kz.msg = "Didn't see that coming?";
            var col = dart.AddComponent<BoxCollider2D>(); col.isTrigger = true;
            float t = 0f;
            while (t < 2.2f && dart != null)
            {
                t += Time.deltaTime;
                dart.transform.position += Vector3.left * (15f * Time.deltaTime);
                yield return null;
            }
            if (dart != null) Destroy(dart);
        }

        // An off-screen block slams down on the spot you're standing.
        IEnumerator DropFaller()
        {
            var blk = Theme.Box("Faller", transform.parent, transform.position + Vector3.up * 6f,
                new Vector2(1.4f, 1.4f), Theme.Trick, 4);
            var kz = blk.AddComponent<KillZone>(); kz.msg = "From above!";
            var col = blk.AddComponent<BoxCollider2D>(); col.isTrigger = true;
            Vector3 from = blk.transform.position, to = transform.position;
            float t = 0f;
            while (t < 0.16f && blk != null)
            {
                t += Time.deltaTime;
                blk.transform.position = Vector3.Lerp(from, to, t / 0.16f);
                yield return null;
            }
        }

        void LaunchPlayer(Collider2D other)
        {
            var rb = other.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 21f);
                Audio.Play("jump", 0.7f);
            }
        }

        // Spikes hidden under the platform shoot up the instant you arrive.
        // They kill on contact (via KillZone), so JUMPING OVER them survives.
        IEnumerator RaiseSpike(Collider2D player)
        {
            _armed = false;
            var go = Theme.Box("Spikes", transform.parent,
                transform.position + Vector3.down * 0.9f,
                new Vector2(0.7f, 0.9f), Theme.Danger, 3);
            var kz = go.AddComponent<KillZone>();
            kz.msg = "The spikes said hi.";
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            _spike = go.transform;

            float e = 0f; Vector3 from = _spike.position;
            Vector3 to = from + Vector3.up * 0.95f;
            while (e < 0.14f)
            {
                e += Time.deltaTime;
                _spike.position = Vector3.Lerp(from, to, e / 0.14f);
                yield return null;
            }
        }

        // A crusher block slams down the moment you reach for the bait coins.
        IEnumerator Crush()
        {
            _armed = false;
            var go = Theme.Box("Crusher", transform.parent, transform.position + Vector3.up * 3.2f,
                new Vector2(transform.localScale.x, 1.4f), Theme.Trick, 4);
            float e = 0f; Vector3 from = go.transform.position;
            Vector3 to = new Vector3(transform.position.x, transform.position.y, 0f);
            while (e < 0.12f)
            {
                e += Time.deltaTime;
                go.transform.position = Vector3.Lerp(from, to, e / 0.12f);
                yield return null;
            }
            GameRoot.I?.Die("Should've stayed low.");
        }
    }

    /// <summary>Kills the player on contact (trigger or collision). Reusable.</summary>
    public class KillZone : MonoBehaviour
    {
        public string msg = "Bonk.";
        void OnTriggerEnter2D(Collider2D o)
        {
            if (o.GetComponent<PlayerController>()) GameRoot.I?.Die(msg);
        }
        void OnCollisionEnter2D(Collision2D c)
        {
            if (c.collider.GetComponent<PlayerController>()) GameRoot.I?.Die(msg);
        }
    }
}
