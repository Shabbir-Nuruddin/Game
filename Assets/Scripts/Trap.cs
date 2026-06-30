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
        GrowSpike,  // a blood spike that grows (lethal) and shrinks (safe) on a loop
        Pendulum,   // a blade on a chain that swings across the path — time your run
        FlameJet,   // a floor jet that erupts fire on a loop (cross while it's down)
        Chandelier, // a wide telegraphed drop from the ceiling (a big Faller)
        HolyWater,  // a floor puddle that turns lethal on a pulse (cross while dim)
        BatSwoop    // a bat that hovers, then dives at you on a telegraph
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
        public Sprite[] frames;   // optional spin/animation frames (e.g. the saw)
        float _animTimer;
        SpriteRenderer _sr;
        BoxCollider2D _col;
        bool _armed = true;
        Transform _spike;
        Transform _faller;
        Vector3 _fallerHome;

        public void Init(TrapType t) { type = t; }

        Vector3 _origin;
        Vector3 _growBaseScale = Vector3.one;
        float _growBottomY;   // platform surface the spike erupts from
        float _growFullH;     // full world height when raised

        void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<BoxCollider2D>();
            _origin = transform.position;
            // GrowSpike and FlameJet both erupt UP from the floor, so they share the
            // base-anchor maths (otherwise they'd shrink toward their own centre).
            if (type == TrapType.GrowSpike || type == TrapType.FlameJet)
            {
                _growBaseScale = transform.localScale;
                float spriteH = (_sr != null && _sr.sprite != null) ? _sr.sprite.bounds.size.y : 1f;
                _growFullH = spriteH * _growBaseScale.y;
                _growBottomY = _origin.y - _growFullH / 2f;
            }

            // A faller hovers above and slams down on a brief telegraph. A chandelier
            // is the same idea but WIDE and gothic — it shares the drop coroutine.
            if (type == TrapType.Faller || type == TrapType.Chandelier)
            {
                bool chand = type == TrapType.Chandelier;
                var sp = Assets.Sprite(chand ? "chandelier" : "rockhead");
                var size = chand ? new Vector2(2.4f, 1.1f) : new Vector2(1.5f, 1.5f);
                var pos = transform.position + Vector3.up * (chand ? 4.0f : 3.5f);
                var go = sp != null
                    ? Theme.SpriteBox(chand ? "Chandelier" : "RockHead", transform, pos, size, sp, 4)
                    : Theme.Box(chand ? "Chandelier" : "RockHead", transform, pos, size,
                                chand ? Theme.Hex("3A2A12") : Theme.Trick, 4);
                if (sp != null && !chand) go.GetComponent<SpriteRenderer>().color = new Color(0.5f, 0.45f, 0.5f);
                if (chand && sp == null) // a couple of candle dots so the box reads as a chandelier
                {
                    Theme.Box("Candle", go.transform, (Vector2)pos + new Vector2(-0.7f, 0.5f), new Vector2(0.14f, 0.4f), Theme.Coin, 5);
                    Theme.Box("Candle", go.transform, (Vector2)pos + new Vector2(0.0f, 0.55f), new Vector2(0.14f, 0.4f), Theme.Coin, 5);
                    Theme.Box("Candle", go.transform, (Vector2)pos + new Vector2(0.7f, 0.5f), new Vector2(0.14f, 0.4f), Theme.Coin, 5);
                }
                var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true;
                var kz = go.AddComponent<KillZone>();
                kz.msg = chand ? "Crushed under the chandelier." : "Crushed by the falling stone.";
                _faller = go.transform;
                _fallerHome = _faller.position;
            }

            // A pendulum: a blade on a chain that swings across the lane below. The
            // trap object itself is the pivot (up high); the chain + blade hang from
            // it as children, so rotating the pivot swings the whole assembly.
            if (type == TrapType.Pendulum)
            {
                const float arm = 3.0f;
                Theme.Box("Chain", transform, transform.position + Vector3.down * (arm / 2f),
                    new Vector2(0.08f, arm), Theme.Hex("2A2230"), 2);
                var sp = Assets.Sprite("pendulum");
                var bladePos = transform.position + Vector3.down * arm;
                var blade = sp != null
                    ? Theme.SpriteBox("Blade", transform, bladePos, new Vector2(1.3f, 1.3f), sp, 3)
                    : Theme.Box("Blade", transform, bladePos, new Vector2(1.0f, 1.0f), Theme.Danger, 3);
                var col = blade.AddComponent<BoxCollider2D>(); col.isTrigger = true; col.size = Vector2.one * 0.7f;
                var kz = blade.AddComponent<KillZone>(); kz.msg = "Sliced by the pendulum blade.";
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
                col.size *= 0.8f; // reliable spike hitbox
                var kz = go.AddComponent<KillZone>(); kz.msg = "Impaled by a falling spike.";
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
            // A saw slides back and forth across its track AND spins.
            if (type == TrapType.Saw)
            {
                transform.position = new Vector3(
                    _origin.x + Mathf.Sin(Time.time * 2.5f) * 2.5f, _origin.y, 0f);
                if (frames != null && frames.Length > 0 && _sr != null)
                {
                    _animTimer += Time.deltaTime;
                    _sr.sprite = frames[Mathf.FloorToInt(_animTimer * 24f) % frames.Length];
                }
            }

            // A growing spike: erupts tall (lethal) then sinks short (safe to
            // cross). Anchored at the base so it visibly rises OUT of the floor.
            else if (type == TrapType.GrowSpike)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f + _origin.x); // 0..1
                float h = Mathf.Max(0.08f, k);                                    // never fully gone
                transform.localScale = new Vector3(_growBaseScale.x, _growBaseScale.y * h, 1f);
                float curH = _growFullH * h;
                transform.position = new Vector3(_origin.x, _growBottomY + curH / 2f, 0f);
                bool lethal = k > 0.55f;
                if (_col != null) _col.enabled = lethal;       // only deadly when grown
                // Brighten as it rises so the lethal phase reads at a glance.
                if (_sr != null)
                    _sr.color = lethal ? Theme.Danger
                                       : new Color(0.55f, 0.12f, 0.14f, 1f); // dim while safe
            }

            // A pendulum blade: swing the pivot back and forth. The chain + blade
            // are children, so they sweep through the lane below.
            else if (type == TrapType.Pendulum)
            {
                float ang = Mathf.Sin(Time.time * 1.6f + _origin.x) * 55f;
                transform.rotation = Quaternion.Euler(0f, 0f, ang);
            }

            // A flame jet: mostly OFF, erupts for a beat with a tiny telegraph. Same
            // base-anchor maths as GrowSpike so the fire shoots UP out of the floor.
            else if (type == TrapType.FlameJet)
            {
                float t = Mathf.Repeat(Time.time * 0.8f + _origin.x, 1f); // 0..1 loop
                bool erupt = t > 0.55f && t < 0.9f;     // lethal window
                bool warn  = t >= 0.45f && t <= 0.55f;  // a flicker of warning
                float h = erupt ? 1f : (warn ? 0.35f : 0.08f);
                transform.localScale = new Vector3(_growBaseScale.x, _growBaseScale.y * h, 1f);
                float curH = _growFullH * h;
                transform.position = new Vector3(_origin.x, _growBottomY + curH / 2f, 0f);
                if (_col != null) _col.enabled = erupt;
                if (_sr != null)
                    _sr.color = erupt ? Theme.Hex("FF7A1A")
                              : warn ? Theme.Hex("FFC24D")
                                     : new Color(1f, 0.5f, 0.1f, 0.25f);
            }

            // Holy water: a flat puddle that turns lethal on a slow pulse. Bright =
            // burning (deadly), dim = safe to cross. No vertical growth.
            else if (type == TrapType.HolyWater)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.0f + _origin.x);
                bool lethal = k > 0.6f;
                if (_col != null) _col.enabled = lethal;
                if (_sr != null)
                    _sr.color = lethal ? new Color(0.85f, 0.97f, 1f, 0.95f)
                                       : new Color(0.5f, 0.8f, 0.95f, 0.4f);
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
            Codex.Unlock(TrapType.FakeFloor);   // you've discovered the treacher-floor
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
                    Codex.Unlock(TrapType.FakeExit);
                    GameRoot.I?.Die("That door? Pure evil.");
                    break;
                case TrapType.RealExit:
                    if (_armed) { _armed = false; GameRoot.I?.ReachExit(); }
                    break;
                case TrapType.Surprise:
                    Codex.Unlock(TrapType.Surprise);
                    GameRoot.I?.Die("Caught in a sunbeam. Vampires burn.");
                    break;
                case TrapType.Dart:
                    if (_armed) { _armed = false; StartCoroutine(FireDart()); }
                    break;
                case TrapType.Faller:
                case TrapType.Chandelier:
                    if (_armed) StartCoroutine(DropReactive());
                    break;
                case TrapType.Spring:
                    Codex.Unlock(TrapType.Spring);
                    LaunchPlayer(other);
                    break;
                case TrapType.WarpBack:
                    Codex.Unlock(TrapType.WarpBack);
                    GameRoot.I?.WarpToStart();
                    break;
                case TrapType.Reverse:
                    Codex.Unlock(TrapType.Reverse);
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
            var kz = dart.AddComponent<KillZone>(); kz.msg = "Skewered by a flying stake."; kz.trapTag = (int)type;
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
            // A longer shake so the drop is clearly readable — you have time to
            // sprint out from under it instead of being flattened on arrival.
            float e = 0f;
            while (e < 0.5f)
            {
                e += Time.deltaTime;
                if (_faller != null) _faller.position = _fallerHome + (Vector3)(Random.insideUnitCircle * 0.08f);
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
            // SLAM — dust + shake even if it misses (weight).
            Fx.Burst(to + Vector3.down * 0.4f, new Color(0.55f, 0.5f, 0.55f, 0.9f), 9, 4.5f, 0.18f, 0.4f, 10f);
            GameRoot.I?.ShakeCam(0.28f, 0.18f);
            Audio.PlayOr("die_slam", "jump", 0.4f);
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
            col.size *= 0.8f; // reliable spike hitbox
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

    /// <summary>
    /// Softly pulses a SpriteRenderer's alpha. Used to give the otherwise-
    /// INVISIBLE sensor traps a faint, breathing tell — visible if you're
    /// paying attention, easy to miss if you're not. Cosmetic.
    /// </summary>
    public class FaintPulse : MonoBehaviour
    {
        public float min = 0.10f, max = 0.34f, speed = 2.4f;
        SpriteRenderer _sr;
        Color _base;
        void Start() { _sr = GetComponent<SpriteRenderer>(); if (_sr != null) _base = _sr.color; }
        void Update()
        {
            if (_sr == null) return;
            var c = _base;
            c.a = Mathf.Lerp(min, max, 0.5f + 0.5f * Mathf.Sin(Time.time * speed));
            _sr.color = c;
        }
    }

    /// <summary>Spins a transform (used for hanging saw blades). Cosmetic.</summary>
    public class Spinner : MonoBehaviour
    {
        public float speed = 320f;
        void Update() => transform.Rotate(0f, 0f, speed * Time.deltaTime);
    }

    /// <summary>Kills the player on contact (trigger or collision). Reusable.</summary>
    public class KillZone : MonoBehaviour
    {
        public string msg = "Bonk.";
        public int trapTag = -1;   // (int)TrapType for the Codex; -1 = not a codex trap

        void Kill()
        {
            // Reveal the trap's Bestiary entry the first time it gets you.
            int tag = trapTag;
            if (tag < 0) { var tr = GetComponentInParent<Trap>(); if (tr != null) tag = (int)tr.type; }
            if (tag >= 0) Codex.Unlock((TrapType)tag);
            GameRoot.I?.Die(msg);
        }
        void OnTriggerEnter2D(Collider2D o)
        {
            if (o.GetComponent<PlayerController>()) Kill();
        }
        void OnCollisionEnter2D(Collision2D c)
        {
            if (c.collider.GetComponent<PlayerController>()) Kill();
        }
    }
}
