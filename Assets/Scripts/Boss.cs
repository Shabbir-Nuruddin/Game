using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// A rage-bait boss. Consistent with the one-hit game: EVERY boss attack is a
    /// one-shot kill, so there's no "the bullet didn't register" — if it touches you,
    /// you're dust. The danger is fair-but-mean: each attack telegraphs, but they
    /// come fast, from many angles, with a signature troll fake-out. The boss always
    /// FACES the player. You whittle its HP with your blaster between dodges; die and
    /// the whole fight resets (that's the rage loop). Built by GameRoot.SetupBoss.
    /// </summary>
    public class Boss : MonoBehaviour
    {
        // If the boss source art faces LEFT by default, flip this so facing is correct.
        const bool ArtFacesLeft = false;

        // World-space half-extents of the VISIBLE body — used for the one-shot CONTACT
        // kill, so dying matches actually touching the boss (the sprite displays at
        // ~2.6 units, but the creature body is smaller).
        const float BodyHalfW = 0.85f, BodyHalfH = 1.10f;
        // The BULLET hurtbox is the same width but a TALL column: the boss hovers high
        // above the ground, so this lets your floor-level shots still reach it without
        // forcing a jump — while keeping side shots from phantom-hitting past the body.
        const float HurtHalfH = 2.5f;

        int _tier, _hp, _hpMax;
        float _minX, _maxX;
        SpriteRenderer _sr;
        Color _baseColor;
        float _flash, _bob, _baseScaleX = 1f, _baseScaleY = 1f;
        bool _enraged, _dashing, _dead;
        float _faceDir = 1f;                 // current facing (-1/+1), smoothed with a deadzone
        int _step;                           // pattern counter — drives each boss's signature rotation

        // Multi-PHASE escalation: higher tiers have MORE phases, so the fight visibly
        // changes shape as you grind the boss down (not just a single enrage flip).
        //   tier 1–2 → 2 phases (normal, final)   tier 3–4 → 3 phases (normal, mid, final)
        // _enraged stays true only in the FINAL phase, preserving existing pattern chaining.
        int _phase, _maxPhase;
        // Held true during the cinematic intro so the boss can't act / kill on contact
        // until the cutscene finishes (GameRoot flips it off).
        public bool IntroHold = true;
        Transform _hazards;                  // container for spawned spikes/bolts/bats (cleared on defeat)

        RectTransform _hpFill, _hpChip;
        float _fracTarget = 1f, _chipFrac = 1f;
        GameObject _hpRoot, _promptRoot;
        Text _promptText;
        bool _everHit;                       // hide the "press F" prompt after the first hit

        Transform Player => GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
        float HoverY => -0.3f + Mathf.Sin(_bob * 2f) * 0.4f;

        public void Init(int tier, float minX, float maxX)
        {
            _tier = Mathf.Clamp(tier, 1, 4);
            _minX = minX; _maxX = maxX;
            // 20,28,36,44 at Normal — shorter fights = less luck-of-the-draw — then
            // scaled by difficulty so Casual clears in fewer clips, Nightmare is full.
            _hpMax = _hp = Mathf.Max(6, Mathf.RoundToInt((12 + _tier * 8) * Diff.BossHpMul));
            _maxPhase = _tier >= 3 ? 2 : 1;          // bigger bosses get an extra phase
            _baseScaleX = Mathf.Abs(transform.localScale.x);
            _baseScaleY = transform.localScale.y;
        }

        void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            _baseColor = _sr != null ? _sr.color : Color.white;

            // Bullet hurtbox. The collider size is LOCAL and gets multiplied by the
            // boss transform scale (≈2.6×), so divide the desired WORLD size by the
            // base scale — otherwise the hitbox balloons far past the visible body.
            // Body width (no phantom side-hits) × a tall column (ground shots reach the
            // hovering boss).
            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(BodyHalfW * 2f / _baseScaleX, HurtHalfH * 2f / _baseScaleY);
            col.offset = Vector2.zero;

            // Hazards the boss spawns (spikes, bolts, bats) live under this container,
            // parented to the level root, so killing the boss can wipe them all at
            // once — otherwise a half-finished spike row persists and walls the exit.
            _hazards = new GameObject("BossHazards").transform;
            _hazards.SetParent(transform.parent, false);

            Audio.PlayOr("boss_roar", "death", 0.7f);
            BuildHpBar();
            StartCoroutine(Brain());
        }

        void Update()
        {
            if (_dead) return;
            _bob += Time.deltaTime;

            if (_flash > 0f)
            {
                _flash -= Time.deltaTime * 5f;
                if (_sr != null) _sr.color = Color.Lerp(_baseColor, Color.white, Mathf.Clamp01(_flash));
            }

            var pl = Player;
            if (pl != null && !_dashing) FaceToward(pl.position.x);

            // HP bar "chip": the pale trailing layer eases down toward the real value
            // so a hit reads as a quick white sliver draining, not an instant jump.
            if (_hpChip != null && _chipFrac > _fracTarget)
            {
                _chipFrac = Mathf.MoveTowards(_chipFrac, _fracTarget, Time.deltaTime * 0.9f);
                _hpChip.anchorMax = new Vector2(0.005f + 0.99f * _chipFrac, 1f);
            }

            // Gentle pulse on the "PRESS F" prompt so a new player's eye catches it.
            if (_promptText != null && !_everHit)
            {
                float a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 3f));
                var c = _promptText.color; c.a = a; _promptText.color = c;
            }

            // One-shot contact: overlap the BODY (an AABB matching the hurtbox, not a
            // mismatched circle) and you're dust. Disabled during the cinematic intro.
            if (pl != null && !IntroHold)
            {
                Vector2 d = (Vector2)pl.position - (Vector2)transform.position;
                if (Mathf.Abs(d.x) < BodyHalfW && Mathf.Abs(d.y) < BodyHalfH)
                    GameRoot.I.HitPlayer($"Caught by {BossName()}.");
            }
        }

        // ---------- the attack brain ----------
        IEnumerator Brain()
        {
            // Wait out the cinematic intro before the boss does anything.
            while (IntroHold && !_dead) yield return null;
            yield return Wait(1.1f);
            while (!_dead)
            {
                // PHASE check: each crossed threshold is a telegraphed escalation event.
                int newPhase = ComputePhase();
                if (newPhase > _phase)
                {
                    _phase = newPhase;
                    _enraged = _phase >= _maxPhase;   // enrage = the FINAL phase
                    GameRoot.I?.ScreenFlash();
                    GameRoot.I?.ShakeCam(0.5f, 0.4f);
                    Fx.Ring(transform.position, new Color(1f, 0.15f, 0.15f, 0.7f), 4f, 0.5f);
                    Audio.PlayOr("boss_roar", "death", 0.7f);
                    GameRoot.I?.BossToast(_enraged ? "ENRAGED" : $"PHASE {_phase + 1}");
                    GameRoot.I?.SlowMoBurst(0.25f, 0.5f);   // a beat to read the shift
                    yield return Wait(0.5f);
                }
                yield return RunRandomPattern();
                // ALWAYS leave a real shoot window — even enraged it never drops below
                // ~0.7s, and the boss reads as VULNERABLE during it so players learn the
                // rhythm: dodge the pattern, then punish in the opening.
                float gap = Mathf.Max(0.7f, (_enraged ? 0.9f - _tier * 0.05f : 1.2f - _tier * 0.06f));
                yield return OpenWindow(gap);
            }
        }

        // Which phase the current HP fraction puts us in (more phases for bigger tiers).
        int ComputePhase()
        {
            float f = _hpMax > 0 ? (float)_hp / _hpMax : 0f;
            if (_maxPhase <= 1) return f <= 0.5f ? 1 : 0;
            return f <= 0.34f ? 2 : (f <= 0.67f ? 1 : 0);
        }

        // Each boss runs a SIGNATURE rotation (not a shared random grab-bag), so the
        // four fights feel distinct. Enrage (phase 2) escalates each boss's own
        // identity rather than just speeding everything up. All patterns telegraph.
        IEnumerator RunRandomPattern()
        {
            int s = _step++;
            switch (_tier)
            {
                case 1:  return GhoulPool(s);
                case 2:  return CountessPool(s);
                case 3:  return WarlockPool(s);
                default: return VampireLordPool(s);
            }
        }

        // Tier 1 — THE GHOUL: a lumbering bruiser. Pure fundamentals (dash + spikes +
        // the odd fan), slow tells. Enrage = relentless double-dashes.
        IEnumerator GhoulPool(int s)
        {
            switch (s % 4)
            {
                case 0: return Pattern_Dash();
                case 1: return Pattern_GroundSpikes();
                case 2: return _phase >= 1 ? Pattern_Minefield() : Pattern_Dash();   // phase 2 adds the minefield
                default: return Pattern_Fan();
            }
        }

        // Tier 2 — THE COUNTESS: a trickster duelist. The fake-out is her signature;
        // enrage chains a tight fan straight off the bait.
        IEnumerator CountessPool(int s)
        {
            switch (s % 5)
            {
                case 0: return _enraged ? Chain(Pattern_FakeOut(), Pattern_Fan()) : Pattern_FakeOut();
                case 1: return Pattern_Fan();
                case 2: return _phase >= 1 ? Pattern_Minefield() : Pattern_GroundSpikes();
                case 3: return _enraged ? Chain(Pattern_FakeOut(), Pattern_Fan()) : Pattern_FakeOut();
                default: return Pattern_Dash();
            }
        }

        // Tier 3 — THE WARLOCK: a zone-control caster who owns the air. No dashes —
        // he stays back and rains bolts; enrage chains a fan onto the rain so you
        // weave AND dodge a volley.
        IEnumerator WarlockPool(int s)
        {
            switch (s % 5)
            {
                case 0: return _enraged ? Chain(Pattern_BoltRain(), Pattern_Fan()) : Pattern_BoltRain();
                case 1: return Pattern_Fan();
                case 2: return _phase >= 1 ? Pattern_Minefield() : Pattern_GroundSpikes();
                case 3: return _phase >= 2 ? Chain(Pattern_BoltRain(), Pattern_Minefield()) : Pattern_BoltRain();
                default: return Pattern_Fan();
            }
        }

        // Tier 4 — THE VAMPIRE LORD: the full kit, scripted not random. Summons bats,
        // dashes, rains, fakes. Enrage chains a fan onto the rain (bats already double
        // at tier 4, dashes already double when enraged).
        IEnumerator VampireLordPool(int s)
        {
            switch (s % 6)
            {
                case 0: return Pattern_Dash();
                case 1: return _enraged ? Chain(Pattern_BoltRain(), Pattern_Fan()) : Pattern_BoltRain();
                case 2: return Pattern_SummonBats();
                case 3: return _phase >= 2 ? Chain(Pattern_Fan(), Pattern_Minefield()) : Pattern_Fan();
                case 4: return Pattern_FakeOut();
                default: return _phase >= 1 ? Pattern_Minefield() : Pattern_GroundSpikes();
            }
        }

        // Enrage combo: two patterns in sequence — but with a short, TELEGRAPHED beat
        // between them so the second half is anticipated, not a blind-side. (Readable
        // pressure, not chaos.)
        IEnumerator Chain(IEnumerator a, IEnumerator b)
        {
            yield return a;
            if (_dead) yield break;
            Warn(0.32f);
            yield return Wait(0.32f);
            if (!_dead) yield return b;
        }

        // A fanned volley aimed at where you ARE — move and it sweeps past you.
        IEnumerator Pattern_Fan()
        {
            yield return Telegraph(0.35f);
            var pl = Player; if (pl == null) yield break;
            Vector2 aim = ((Vector2)pl.position - (Vector2)transform.position).normalized;
            int bolts = 2 + _tier;                       // 3..6 (was 4..8) — readable safe lanes
            float spread = 15f, start = -(bolts - 1) * spread / 2f;
            for (int i = 0; i < bolts; i++)
            {
                float ang = start + i * spread;
                SpawnBolt((Vector2)(Quaternion.Euler(0, 0, ang) * aim), 7f + _tier * 0.6f);  // slower = dodgeable on reaction
            }
            Audio.PlayOr("die_screech", "death", 0.4f);
            yield return Wait(0.25f);
        }

        // Spikes erupt from the floor across the arena — but a safe GAP is left near
        // where you stand. Stand in the gap. (Enraged: the gap is narrower.)
        IEnumerator Pattern_GroundSpikes()
        {
            var pl = Player; if (pl == null) yield break;
            Warn(0.7f);                                          // "!" — spikes are about to erupt
            float gapX = pl.position.x;                          // gap telegraphs on you...
            float gapW = _enraged ? 2.0f : 2.4f;             // still tighter when enraged, but reachable
            float left = _minX + 1f, right = _maxX - 1f;
            var markers = new System.Collections.Generic.List<GameObject>();
            for (float x = left; x <= right; x += 1.4f)
            {
                if (Mathf.Abs(x - gapX) < gapW) continue;        // ...leave the safe gap
                markers.Add(GroundMarker(x));
            }
            yield return Wait(Mathf.Max(0.8f, 0.85f + (4 - _tier) * 0.05f));  // time to reach the gap — floored so even tier 4 is reachable
            foreach (var m in markers) { if (m != null) Destroy(m); }
            var spikes = new System.Collections.Generic.List<GameObject>();
            for (float x = left; x <= right; x += 1.4f)
            {
                if (Mathf.Abs(x - gapX) < gapW) continue;
                spikes.Add(GroundSpike(x));
            }
            yield return Wait(0.55f);
            foreach (var s in spikes) if (s != null) Destroy(s);
        }

        // MINEFIELD: a scatter of discrete telegraphed eruption zones (not a wall with
        // one gap — you weave BETWEEN them). More zones as tier/phase climb. A later-
        // phase signature so the fight keeps changing shape, not just "dash + shoot".
        IEnumerator Pattern_Minefield()
        {
            int mines = 3 + _tier / 2 + _phase;
            float left = _minX + 1.5f, right = _maxX - 1.5f;
            var xs = new System.Collections.Generic.List<float>();
            var markers = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < mines; i++)
            {
                float x = Random.Range(left, right);
                xs.Add(x); markers.Add(GroundMarker(x));
            }
            Warn(0.7f);
            yield return Wait(0.75f);
            foreach (var m in markers) if (m != null) Destroy(m);
            var spikes = new System.Collections.Generic.List<GameObject>();
            foreach (float x in xs) spikes.Add(GroundSpike(x));
            yield return Wait(0.5f);
            foreach (var s in spikes) if (s != null) Destroy(s);
            yield return Wait(0.2f);
        }

        // The lord rears back, then DASHES across at your height. Jump it.
        IEnumerator Pattern_Dash()
        {
            var pl = Player; if (pl == null) yield break;
            int dashes = _enraged ? 2 : 1;
            for (int d = 0; d < dashes && !_dead; d++)
            {
                pl = Player; if (pl == null) yield break;
                float fromX = pl.position.x < (_minX + _maxX) / 2f ? _maxX - 0.5f : _minX + 0.5f;
                float toX = (Mathf.Approximately(fromX, _maxX - 0.5f)) ? _minX + 0.5f : _maxX - 0.5f;
                float y = Mathf.Clamp(pl.position.y + 0.1f, -2.2f, 1.5f);
                _dashing = true;
                // reposition to the dash start, THEN face the way the dash will go
                // (facing must be relative to the new position, not the old one).
                transform.position = new Vector3(fromX, y, 0f);
                SetFacing(Mathf.Sign(toX - fromX));
                // POSITIONAL telegraph: a red lane across the arena at the dash height,
                // so the player sees EXACTLY where to jump before the charge.
                var lane = Theme.Box("DashLane", _hazards != null ? _hazards : transform.parent,
                    new Vector2((_minX + _maxX) / 2f, y), new Vector2(_maxX - _minX, 0.12f), Theme.Danger, 5);
                var lsr = lane.GetComponent<SpriteRenderer>();
                var lc = lsr.color; lc.a = 0.5f; lsr.color = lc;
                lane.AddComponent<FaintPulse>();
                yield return Telegraph(0.45f);
                if (lane != null) Destroy(lane);
                float t = 0f;
                while (t < 1f && !_dead)
                {
                    t += Time.deltaTime * 3.2f * Diff.BossSpeedMul;
                    transform.position = new Vector3(Mathf.Lerp(fromX, toX, t), y, 0f);
                    yield return null;
                }
                _dashing = false;
                yield return Wait(0.2f);
            }
        }

        // TROLL: a full attack wind-up... that fizzles. The instant you panic-dodge,
        // a fast aimed bolt punishes the over-react.
        IEnumerator Pattern_FakeOut()
        {
            yield return Telegraph(0.5f);
            // the bait: flash to "fire" but do nothing
            if (_sr != null) _sr.color = _baseColor;
            Audio.PlayOr("click", "shoot", 0.5f);
            yield return Wait(0.45f);                 // you relax / scramble here
            var pl = Player; if (pl == null) yield break;
            Vector2 aim = ((Vector2)pl.position - (Vector2)transform.position).normalized;
            SpawnBolt(aim, 10f + _tier * 0.6f);       // the real punish — fast enough to bite panic, slow enough to read
            yield return Wait(0.2f);
        }

        // Bolts rain from the ceiling with weave-through gaps.
        IEnumerator Pattern_BoltRain()
        {
            int waves = 2 + (_enraged ? 1 : 0);
            Warn(0.7f);                                          // "!" — bolts are about to rain
            for (int w = 0; w < waves && !_dead; w++)
            {
                float gap = Random.Range(_minX + 2f, _maxX - 2f);
                for (float x = _minX + 1f; x <= _maxX - 1f; x += 1.5f)
                {
                    if (Mathf.Abs(x - gap) < 1.6f) continue;
                    MakeBolt(new Vector3(x, 5f, 0f)).AddComponent<BossBolt>()
                        .Init(Vector2.down, (7.5f + _tier) * Diff.BossSpeedMul, BoltFrames());
                }
                yield return Wait(0.7f);
            }
        }

        IEnumerator Pattern_SummonBats()
        {
            yield return Telegraph(0.3f);
            int n = _tier >= 4 ? 2 : 1;
            for (int i = 0; i < n; i++)
            {
                var frames = Assets.Sheet("bat_fly", 32);
                var sp = (frames != null && frames.Length > 0) ? frames[0] : Theme.Bat;
                var bat = Theme.SpriteBox("Bat", _hazards != null ? _hazards : transform.parent,
                    transform.position + Vector3.up * (0.3f + i * 0.4f), new Vector2(0.95f, 0.95f), sp, 4);
                // Tint blood-red from frame ONE (BatEnemy re-applies it in Start, but this
                // avoids a white/dark flash) and wrap in a glow so summoned bats are obvious.
                bat.GetComponent<SpriteRenderer>().color = new Color(1f, 0.22f, 0.22f, 1f);
                Fx.Glow(bat, new Color(1f, 0.2f, 0.2f, 0.55f), 1.5f, 3);
                bat.AddComponent<BatEnemy>().Init(frames);
            }
            yield return Wait(0.3f);
        }

        // ---------- helpers ----------
        IEnumerator Telegraph(float dur)
        {
            dur *= Diff.BossTelegraphMul;        // Casual/Normal get longer, more readable wind-ups
            dur = Mathf.Max(0.45f, dur);         // anticipation never shrinks below a reaction floor,
                                                 // even on the hardest tiers (they get COMPLEXITY, not faster tells)
            Warn(dur);                           // pop a "!" + audio cue so the incoming one-shot is unmistakable
            Fx.Ring(transform.position, new Color(1f, 0.25f, 0.25f, 0.6f), 2.4f, dur);  // wind-up ring
            float e = 0f;
            while (e < dur && !_dead)
            {
                e += Time.deltaTime;
                if (_sr != null) _sr.color = Color.Lerp(_baseColor, Theme.Danger, 0.5f + 0.5f * Mathf.Sin(Time.time * 32f));
                transform.position = new Vector3(transform.position.x, HoverY, 0f);
                yield return null;
            }
            if (_sr != null) _sr.color = _baseColor;
        }

        // The shoot window between patterns: drift toward the player (keeps pressure on)
        // but pulse a pale "vulnerable" glow so the player learns THIS is the beat to
        // shoot. Same drift as Wait, plus the tell.
        IEnumerator OpenWindow(float dur)
        {
            Color glow = Color.Lerp(_baseColor, Color.white, 0.4f);
            float e = 0f;
            while (e < dur && !_dead)
            {
                e += Time.deltaTime;
                if (!_dashing)
                {
                    var pl = Player;
                    float tx = pl != null ? Mathf.Clamp(pl.position.x, _minX, _maxX) : transform.position.x;
                    float nx = Mathf.MoveTowards(transform.position.x, tx, (1.6f + _tier * 0.5f) * Time.deltaTime);
                    transform.position = new Vector3(nx, HoverY, 0f);
                }
                if (_sr != null && _flash <= 0f)
                    _sr.color = Color.Lerp(_baseColor, glow, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f));
                yield return null;
            }
            if (_sr != null && _flash <= 0f) _sr.color = _baseColor;
        }

        // Drift toward the player while idle so it keeps the pressure on.
        IEnumerator Wait(float dur)
        {
            float e = 0f;
            while (e < dur && !_dead)
            {
                e += Time.deltaTime;
                if (!_dashing)
                {
                    var pl = Player;
                    float tx = pl != null ? Mathf.Clamp(pl.position.x, _minX, _maxX) : transform.position.x;
                    float nx = Mathf.MoveTowards(transform.position.x, tx, (1.6f + _tier * 0.5f) * Time.deltaTime);
                    transform.position = new Vector3(nx, HoverY, 0f);
                }
                yield return null;
            }
        }

        // A small "!" above the lord whenever it winds up a one-shot attack, so the
        // danger is always announced a beat before it can kill you.
        void Warn(float dur)
        {
            Audio.PlayOr("boss_hit", "click", 0.3f);   // audio tell — every wind-up has a sound, not just a "!"
            var go = new GameObject("BossWarn");
            go.transform.SetParent(_hazards != null ? _hazards : transform.parent, false);
            go.transform.position = transform.position + Vector3.up * 1.7f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = "!"; tm.fontSize = 64; tm.characterSize = 0.12f; tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.85f, 0.1f);
            go.GetComponent<MeshRenderer>().sortingOrder = 30;   // above everything in the arena
            go.AddComponent<WarnMark>().Init(Mathf.Max(0.25f, dur));
        }

        void SpawnBolt(Vector2 dir, float speed)
        {
            MakeBolt(transform.position).AddComponent<BossBolt>().Init(dir, speed * Diff.BossSpeedMul, BoltFrames());
        }

        // A bolt object: animated vfx orb if the art is present, else a coloured box.
        // ALWAYS tinted hot blood-red and wrapped in a pulsing glow halo so it reads
        // as a lethal projectile against the near-black sky (players reported bolts
        // looking "black/invisible" when they were the old necro-purple).
        GameObject MakeBolt(Vector3 pos)
        {
            var parent = _hazards != null ? _hazards : transform.parent;
            var frames = BoltFrames();
            var go = (frames != null && frames.Length > 0)
                ? Theme.SpriteBox("BossBolt", parent, pos, new Vector2(0.75f, 0.75f), frames[0], 6)
                : Theme.Box("BossBolt", parent, pos, new Vector2(0.6f, 0.6f), Theme.Danger, 6);
            go.GetComponent<SpriteRenderer>().color = Theme.Danger;          // bright red core
            Fx.Glow(go, new Color(1f, 0.18f, 0.18f, 0.7f), 1.5f, 5);          // red halo, sits behind the core
            return go;
        }

        static Sprite[] BoltFrames() => Assets.Sheet("bolt", 32);

        GameObject GroundMarker(float x)
        {
            var m = Theme.Box("SpikeWarn", _hazards != null ? _hazards : transform.parent, new Vector2(x, -2.55f),
                new Vector2(1.2f, 0.18f), Theme.Danger, 5);
            var c = m.GetComponent<SpriteRenderer>().color; c.a = 0.5f; m.GetComponent<SpriteRenderer>().color = c;
            m.AddComponent<FaintPulse>();
            return m;
        }

        GameObject GroundSpike(float x)
        {
            var parent = _hazards != null ? _hazards : transform.parent;
            var sp = Assets.Sprite("spike");
            var go = sp != null
                ? Theme.SpriteBox("LordSpike", parent, new Vector3(x, -2.0f, 0f), new Vector2(1.1f, 1.3f), sp, 5)
                : Theme.Box("LordSpike", parent, new Vector2(x, -2.0f), new Vector2(0.8f, 1.2f), Theme.Danger, 5);
            if (sp != null) go.GetComponent<SpriteRenderer>().color = Theme.Danger;
            var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true; col.size *= 0.8f;
            var kz = go.AddComponent<KillZone>(); kz.msg = $"Impaled on {BossName()}'s spikes.";
            return go;
        }

        void FaceToward(float x)
        {
            // Deadzone so the lord doesn't jitter-flip every frame when the player is
            // roughly level with it — only turn once they're clearly to one side.
            float dx = x - transform.position.x;
            if (dx < -0.4f) _faceDir = -1f;        // -1 = face left
            else if (dx > 0.4f) _faceDir = 1f;
            ApplyFacing();
        }

        void SetFacing(float dir)
        {
            if (Mathf.Abs(dir) > 0.01f) _faceDir = Mathf.Sign(dir);
            ApplyFacing();
        }

        // Facing reads via SpriteRenderer.flipX (so the transform scale — and thus the
        // collider — is never inverted) PLUS a subtle lean toward the player, which is
        // what actually sells direction on the near-front-facing boss art. Writes only
        // flipX/rotation, never position, so it never fights Telegraph/Wait/Dash.
        void ApplyFacing()
        {
            if (_sr != null) _sr.flipX = ArtFacesLeft ? (_faceDir > 0f) : (_faceDir < 0f);
            transform.localRotation = Quaternion.Euler(0f, 0f, -_faceDir * 6f);
        }

        // Display name of this boss (matches the HP-bar title), for death-cause text.
        string BossName()
        {
            switch (_tier)
            {
                case 1:  return "the Ghoul";
                case 2:  return "the Countess";
                case 3:  return "the Warlock";
                default: return "the Vampire Lord";
            }
        }

        public void Hit() => Hit(1);
        public void Hit(int dmg)
        {
            if (_dead) return;
            _hp = Mathf.Max(0, _hp - Mathf.Max(1, dmg));
            _flash = 1f;
            if (!_everHit) { _everHit = true; HidePrompt(); }   // they've found the blaster
            Audio.PlayOr("boss_hit", "click", 0.6f);
            Fx.Burst(transform.position, new Color(0.8f, 0.2f, 0.95f, 0.95f), 6, 4.5f, 0.13f, 0.35f, 8f);
            UpdateHpBar();
            if (_hp <= 0) Defeat();
        }

        void HidePrompt()
        {
            if (_promptRoot != null) Destroy(_promptRoot);
            _promptRoot = null; _promptText = null;
        }

        void Defeat()
        {
            _dead = true;
            Audio.PlayOr("boss_die", "win", 0.8f);
            // A satisfying shatter: sprite explosion + bursts + ring + screen shake.
            Fx.Explosion(transform.position, 4f);
            Fx.Burst(transform.position, new Color(0.85f, 0.2f, 0.95f, 1f), 28, 9f, 0.2f, 0.8f, 14f);
            Fx.Burst(transform.position, Theme.Danger, 16, 6f, 0.18f, 0.7f, 12f);
            Fx.Ring(transform.position, new Color(1f, 0.9f, 0.7f, 0.8f), 5f, 0.6f);
            GameRoot.I?.ShakeCam(0.6f, 0.5f);
            HidePrompt();
            // Wipe every hazard the boss spawned (spike rows, bolts, bats) so nothing
            // lethal is left blocking the walk to the exit coffin.
            if (_hazards != null) Destroy(_hazards.gameObject);
            if (_hpRoot != null) Destroy(_hpRoot);
            GameRoot.I?.BossDefeated();
            Destroy(gameObject);
        }

        // ---------- HP bar ----------
        void BuildHpBar()
        {
            string[] names = { "", "THE GHOUL", "THE COUNTESS", "THE WARLOCK", "THE VAMPIRE LORD" };

            _hpRoot = new GameObject("BossHP", typeof(RectTransform));
            _hpRoot.transform.SetParent(Theme.Canvas.transform, false);
            var rrt = _hpRoot.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0, -28);
            rrt.sizeDelta = new Vector2(860, 96);

            // Boss name ABOVE the bar (not crammed on top of it). Gothic title font,
            // sized to fit the width so it never clips into the bar below.
            Theme.Label(_hpRoot.transform, names[_tier], 40, Theme.Player,
                new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(860, 48)).font = Theme.TitleFont;

            // The bar itself sits in the lower portion of the root.
            var barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(_hpRoot.transform, false);
            var brt = barGo.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0, 6);
            brt.sizeDelta = new Vector2(0, 34);

            var bg = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(barGo.transform, false);
            bg.color = new Color(0.08f, 0.02f, 0.04f, 0.94f);
            var barFrame = Theme.NineSlice("bar_frame", 8);   // gothic bar container
            if (barFrame != null) { bg.sprite = barFrame; bg.type = Image.Type.Sliced; bg.color = Color.white; }
            var bgr = bg.rectTransform; bgr.anchorMin = Vector2.zero; bgr.anchorMax = Vector2.one;
            bgr.offsetMin = bgr.offsetMax = Vector2.zero;

            // Pale "chip" layer (eases down behind the red, showing recent damage).
            var chip = new GameObject("Chip", typeof(RectTransform)).AddComponent<Image>();
            chip.transform.SetParent(barGo.transform, false);
            chip.color = new Color(1f, 0.85f, 0.85f, 0.85f);
            _hpChip = chip.rectTransform;
            _hpChip.anchorMin = new Vector2(0, 0); _hpChip.anchorMax = new Vector2(1, 1);
            _hpChip.offsetMin = new Vector2(5, 5); _hpChip.offsetMax = new Vector2(-5, -5);

            // Main red fill (snaps to current HP for crisp feedback).
            var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.transform.SetParent(barGo.transform, false);
            fill.color = Theme.Danger;
            _hpFill = fill.rectTransform;
            _hpFill.anchorMin = new Vector2(0, 0); _hpFill.anchorMax = new Vector2(1, 1);
            _hpFill.offsetMin = new Vector2(5, 5); _hpFill.offsetMax = new Vector2(-5, -5);

            // Segment ticks so the bar reads as chunks draining (juicier than a slab).
            int segs = Mathf.Clamp(_hpMax / 8, 4, 12);
            for (int i = 1; i < segs; i++)
            {
                var tick = new GameObject("Tick", typeof(RectTransform)).AddComponent<Image>();
                tick.transform.SetParent(barGo.transform, false);
                tick.color = new Color(0f, 0f, 0f, 0.5f);
                var tr = tick.rectTransform;
                tr.anchorMin = new Vector2(i / (float)segs, 0f);
                tr.anchorMax = new Vector2(i / (float)segs, 1f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.anchoredPosition = Vector2.zero;
                tr.sizeDelta = new Vector2(3f, -10f);
            }

            BuildPrompt();
        }

        // A persistent, pulsing prompt so new players learn the blaster key. It sits
        // just under the HP bar, calls out F (and J), and disappears the instant the
        // boss takes its first hit. Touch players get a FIRE button elsewhere.
        void BuildPrompt()
        {
            _promptRoot = new GameObject("BossPrompt", typeof(RectTransform));
            _promptRoot.transform.SetParent(Theme.Canvas.transform, false);
            var prt = _promptRoot.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0, -132);
            prt.sizeDelta = new Vector2(800, 56);

            var pill = _promptRoot.AddComponent<Image>();
            pill.color = new Color(0.05f, 0f, 0.02f, 0.78f);

            _promptText = Theme.Label(_promptRoot.transform, $"GRAB THE WEAPON, THEN  {Controls.Name(Controls.Shoot)}  TO BLAST IT   (ONE TOUCH KILLS YOU)",
                24, Theme.Coin, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 52));
            _promptText.raycastTarget = false;
        }

        void UpdateHpBar()
        {
            if (_hpFill == null) return;
            _fracTarget = Mathf.Clamp01((float)_hp / _hpMax);
            _hpFill.anchorMax = new Vector2(0.005f + 0.99f * _fracTarget, 1f);
            // chip starts from the previous (higher) value and eases down in Update
            if (_chipFrac < _fracTarget) _chipFrac = _fracTarget;
        }

        void OnDestroy() { if (_hpRoot != null) Destroy(_hpRoot); if (_promptRoot != null) Destroy(_promptRoot); }
    }

    /// <summary>
    /// A boss projectile. ONE-SHOT kill. Uses a SWEPT distance test (segment from
    /// last frame to this frame vs the player) so a fast bolt can never tunnel past
    /// you between frames — fixing "the bullet didn't kill me". Animated if art exists.
    /// </summary>
    public class BossBolt : MonoBehaviour
    {
        Vector2 _vel; float _life = 5f, _anim;
        Sprite[] _frames; SpriteRenderer _sr;
        Vector3 _prev;
        const float HitRadius = 0.5f;

        public void Init(Vector2 dir, float speed, Sprite[] frames = null)
        {
            _vel = dir.normalized * speed;
            _frames = frames;
            _sr = GetComponent<SpriteRenderer>();
            _prev = transform.position;
        }

        void Update()
        {
            _prev = transform.position;
            transform.position += (Vector3)(_vel * Time.deltaTime);

            if (_frames != null && _frames.Length > 0 && _sr != null)
            {
                _anim += Time.deltaTime;
                _sr.sprite = _frames[Mathf.FloorToInt(_anim * 14f) % _frames.Length];
            }

            var pl = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            if (pl != null && SegmentDist(_prev, transform.position, pl.position) < HitRadius)
            {
                GameRoot.I.HitPlayer("Struck down by a cursed bolt.");
                Destroy(gameObject);
                return;
            }

            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }

        // Shortest distance from point p to segment a-b (sweep-aware hit test).
        static float SegmentDist(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(a, p);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }
    }

    /// <summary>
    /// The little "!" danger marker over the boss during a wind-up: pops in, blinks,
    /// drifts up, then fades and removes itself. Pure feedback — no collision.
    /// </summary>
    public class WarnMark : MonoBehaviour
    {
        float _life, _max;
        TextMesh _tm;

        public void Init(float dur)
        {
            _max = _life = dur;
            _tm = GetComponent<TextMesh>();
        }

        void Update()
        {
            _life -= Time.deltaTime;
            float t = Mathf.Clamp01(1f - _life / _max);
            float pop = Mathf.Min(1f, t * 5f);                       // quick scale-in
            transform.localScale = Vector3.one * (0.6f + 0.5f * pop);
            transform.position += Vector3.up * (0.4f * Time.deltaTime);
            if (_tm != null)
            {
                float blink = 0.55f + 0.45f * Mathf.Sin(Time.time * 22f);
                var c = _tm.color; c.a = blink * Mathf.Clamp01(_life * 4f); _tm.color = c;
            }
            if (_life <= 0f) Destroy(gameObject);
        }
    }
}
