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
        Reverse,    // flips your controls for a few seconds
        SpikeStatic,// an always-visible spike you must jump over
        ArrowRain,  // spikes drop from the ceiling on a timer — time your run
        Checkpoint, // touch it and you respawn here instead of the start
        BreakBlock, // a solid candy wall you must SHOOT to get past
        GrowSpike   // a blood spike that grows (lethal) and shrinks (safe) on a loop
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
        Transform _faller;
        Vector3 _fallerHome;

        public void Init(TrapType t) { type = t; }

        Vector3 _origin;
        Vector3 _growBaseScale = Vector3.one;

        void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<BoxCollider2D>();
            _origin = transform.position;
            if (type == TrapType.GrowSpike) _growBaseScale = transform.localScale;

            // A faller is a VISIBLE rock-head hovering above; it slams down with a
            // brief telegraph so you can sprint through (dodgeable, not a cheap kill).
            if (type == TrapType.Faller)
            {
                var sp = Assets.Sprite("rockhead");
                var pos = transform.position + Vector3.up * 3.5f;
                var go = sp != null
                    ? Theme.SpriteBox("RockHead", transform, pos, new Vector2(1.5f, 1.5f), sp, 4)
                    : Theme.Box("RockHead", transform, pos, new Vector2(1.4f, 1.4f), Theme.Trick, 4);
                if (sp != null) go.GetComponent<SpriteRenderer>().color = new Color(0.5f, 0.45f, 0.5f); // stone boulder
                var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true;
                var kz = go.AddComponent<KillZone>(); kz.msg = "Crushed by the falling stone.";
                _faller = go.transform;
                _fallerHome = _faller.position;
            }

            if (type == TrapType.ArrowRain)
                StartCoroutine(Rain());
        }

        // Spikes fall from the ceiling at this column on a loop — time your dash.
        IEnumerator Rain()
        {
            yield return new WaitForSeconds(Random.Range(0f, 1f)); // desync multiple columns
            while (true)
            {
                var sp = Assets.Sprite("spike");
                var spawn = transform.position + Vector3.up * 5.5f;
                var go = sp != null
                    ? Theme.SpriteBox("RainDart", transform, spawn, new Vector2(0.5f, 0.8f), sp, 4)
                    : Theme.Box("RainDart", transform, spawn, new Vector2(0.3f, 0.7f), Theme.Danger, 4);
                var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true;
                var kz = go.AddComponent<KillZone>(); kz.msg = "Look up next time.";
                StartCoroutine(FallDart(go.transform));
                yield return new WaitForSeconds(1.3f);
            }
        }

        IEnumerator FallDart(Transform t)
        {
            float v = 4f, floor = transform.position.y - 1f;
            while (t != null && t.position.y > floor)
            {
                v += 22f * Time.deltaTime;
                t.position += Vector3.down * (v * Time.deltaTime);
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        void Update()
        {
            // A saw slides back and forth across its track.
            if (type == TrapType.Saw)
                transform.position = new Vector3(
                    _origin.x + Mathf.Sin(Time.time * 2.5f) * 2.5f, _origin.y, 0f);

            // A growing spike: pulses tall (lethal) then short (safe to cross).
            else if (type == TrapType.GrowSpike)
            {
                float k = 0.2f + 0.8f * (0.5f + 0.5f * Mathf.Sin(Time.time * 1.8f + _origin.x));
                transform.localScale = new Vector3(_growBaseScale.x, _growBaseScale.y * k, 1f);
                if (_col != null) _col.enabled = k > 0.62f; // only deadly when grown
            }
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
                    if (_armed) StartCoroutine(DropReactive());
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
                case TrapType.Checkpoint:
                    if (_armed) { _armed = false; GameRoot.I?.SetCheckpoint(transform.position); }
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
        // The hovering rock-head shakes (telegraph), slams down, waits, retracts.
        // Sprint through during the shake to survive; dawdle and you're flat.
        IEnumerator DropReactive()
        {
            _armed = false;
            float e = 0f;
            while (e < 0.25f)
            {
                e += Time.deltaTime;
                if (_faller != null) _faller.position = _fallerHome + (Vector3)(Random.insideUnitCircle * 0.06f);
                yield return null;
            }
            Vector3 to = new Vector3(_fallerHome.x, transform.position.y, 0f);
            e = 0f;
            while (e < 0.1f)
            {
                e += Time.deltaTime;
                if (_faller != null) _faller.position = Vector3.Lerp(_fallerHome, to, e / 0.1f);
                yield return null;
            }
            yield return new WaitForSeconds(0.35f);
            e = 0f;
            while (e < 0.3f)
            {
                e += Time.deltaTime;
                if (_faller != null) _faller.position = Vector3.Lerp(to, _fallerHome, e / 0.3f);
                yield return null;
            }
            if (_faller != null) _faller.position = _fallerHome;
            _armed = true;
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
            var sp = Assets.Sprite("spike");
            var pos = transform.position + Vector3.down * 0.9f;
            GameObject go = sp != null
                ? Theme.SpriteBox("Spikes", transform.parent, pos, new Vector2(1f, 1f), sp, 3)
                : Theme.Box("Spikes", transform.parent, pos, new Vector2(0.7f, 0.9f), Theme.Danger, 3);
            if (sp != null) go.GetComponent<SpriteRenderer>().color = Theme.Danger; // blood spikes
            var kz = go.AddComponent<KillZone>();
            kz.msg = "Impaled.";
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
            var sp = Assets.Sprite("rockhead");
            var top = transform.position + Vector3.up * 3.2f;
            GameObject go = sp != null
                ? Theme.SpriteBox("Crusher", transform.parent, top, new Vector2(1.6f, 1.6f), sp, 4)
                : Theme.Box("Crusher", transform.parent, top, new Vector2(transform.localScale.x, 1.4f), Theme.Trick, 4);
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
