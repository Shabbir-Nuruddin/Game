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
        // kill. Generous on purpose: the boss hovers above the floor, so this reaches
        // DOWN far enough that walking under/into it is lethal (players reported being
        // "inside the boss" without dying). The check below also shifts the centre down.
        public const float BodyHalfW = 1.15f;
        const float BodyHalfH = 1.65f;
        const float BodyDrop = 0.5f;   // shift the kill box down toward the floor
        // The BULLET hurtbox is the same width but a TALL column: the boss hovers high
        // above the ground, so this lets your floor-level shots still reach it without
        // forcing a jump — while keeping side shots from phantom-hitting past the body.
        // Public: Bullet runs its own swept test against this column (see Combat.cs).
        public const float HurtHalfH = 2.5f;

        // Last frame's position, used by the swept contact/bullet tests so motion
        // BETWEEN frames is covered (same idea as BossBolt's sweep, both directions).
        public Vector3 PrevPos { get; private set; }

        int _tier, _hp, _hpMax;
        float _minX, _maxX;
        SpriteRenderer _sr;
        BossAnimator _anim;                  // the ONE writer of colour/scale/lean (see BossAnimator.cs)
        Color _baseColor;
        float _bob, _baseScaleX = 1f, _baseScaleY = 1f;
        bool _enraged, _dashing, _dead;
        float _faceDir = 1f;                 // current facing (-1/+1), smoothed with a deadzone
        int _step;                           // pattern counter — drives each boss's signature rotation
        int _fakeouts;                       // Countess tell: fake-outs after the first pulse VIOLET (learnable)

        // ---- identity rework: each boss MOVES differently, and each has its own
        // vulnerability hook so the shooting rhythm differs per fight ----
        enum MoveMode { Hover, Grounded, Teleport, Perch }
        MoveMode _moveMode = MoveMode.Hover;
        const float GroundedY = -1.9f;       // the Shockwave slam height — feet on the deck
        bool  _shielded;                     // Warlock rhythm: bullets deflect while this is up
        bool  _usesShield;                   // tier 3 (and the Lord's Mist form): shield re-arms after each open window
        bool  _stunned;                      // Ghoul wall-slam: the earned 2× damage window
        bool  _contactSafe;                  // suppress the contact one-shot (stun / form swap)
        float _blinkTimer = 2f;              // Teleport idle: countdown to the next blink
        float _preBlink = -1f;               // >=0 while the pre-blink shimmer runs (the tell)
        int   _perchIndex;                   // Perch mode: which anchor he sits on
        float _bodyHalfHNow = BodyHalfH;     // contact half-height NOW (Ghoul ducks during his charge)
        string _contactCause;                // pattern-specific contact death text (null = generic)
        float _stepDust;                     // Grounded walk: footfall dust cadence
        readonly System.Collections.Generic.List<BossDecoy> _decoys =
            new System.Collections.Generic.List<BossDecoy>();
        bool _toastStun, _toastShield, _toastDecoy;   // one-time teaching toasts

        // Phase escalation can INTERRUPT the running pattern (set by Hit, consumed by
        // Brain) so crossing a threshold reads as the boss reacting NOW.
        bool _phaseInterrupt, _patternDone;
        Coroutine _currentPattern;

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
        Image _chipImg;                      // tinted hotter each phase so the bar itself escalates
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
            // Phase count scales with tier: Ghoul 2, Countess/Warlock 3, Lord 4.
            // Every threshold is a telegraphed escalation that can add NEW attacks,
            // so a fight visibly changes shape two or three times, not once.
            _maxPhase = _tier <= 1 ? 1 : (_tier >= 4 ? 3 : 2);
            // Movement personality per boss: the Ghoul stalks the floor, the Countess
            // only ever teleports, the Warlock casts from high perches, and the Lord
            // starts grounded as THE BRUTE (his forms re-set this on each phase).
            _moveMode = _tier == 2 ? MoveMode.Teleport
                      : _tier == 3 ? MoveMode.Perch
                      : _tier == 1 ? MoveMode.Grounded
                      : MoveMode.Grounded;             // tier 4 phase 0 = Brute form
            _usesShield = _shielded = _tier == 3;      // the Warlock opens shielded
            // Floor the base scale: a zero scale would explode the collider size math
            // below into an infinite hurtbox.
            _baseScaleX = Mathf.Max(0.01f, Mathf.Abs(transform.localScale.x));
            _baseScaleY = Mathf.Max(0.01f, Mathf.Abs(transform.localScale.y));
            PrevPos = transform.position;
        }

        void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            _baseColor = _sr != null ? _sr.color : Color.white;
            // All colour/scale/lean writes go through the animator from here on —
            // it holds off while the intro cutscene owns the transform.
            _anim = gameObject.AddComponent<BossAnimator>();
            _anim.Init(_sr, _baseColor, new Vector2(_baseScaleX, _baseScaleY), ArtFacesLeft);
            _anim.Hold = IntroHold;
            if (_shielded) _anim.SetShielded(true);   // the Warlock walks in shielded

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
            if (_anim != null) _anim.Hold = IntroHold;

            var pl = Player;
            if (pl != null && !_dashing) FaceToward(pl.position.x);

            // HP bar "chip": the pale trailing layer eases down toward the real value
            // so a hit reads as a quick white sliver draining, not an instant jump.
            // Drain rate scales with the gap (with a floor) so even a single 2-damage
            // chip on a fat HP pool stays visible for a beat instead of vanishing in
            // a couple of frames.
            if (_hpChip != null && _chipFrac > _fracTarget)
            {
                float rate = Mathf.Max(0.06f, (_chipFrac - _fracTarget) * 2.5f);
                _chipFrac = Mathf.MoveTowards(_chipFrac, _fracTarget, Time.deltaTime * rate);
                _hpChip.anchorMax = new Vector2(0.005f + 0.99f * _chipFrac, 1f);
            }

            // Gentle pulse on the "PRESS F" prompt so a new player's eye catches it.
            if (_promptText != null && !_everHit)
            {
                float a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 3f));
                var c = _promptText.color; c.a = a; _promptText.color = c;
            }

            // One-shot contact: SWEPT test of the player's path this frame — taken
            // RELATIVE to the boss, so a fast player, a dashing boss, or both at once
            // can never tunnel through the body between frames (the old once-per-frame
            // overlap poll was why you sometimes ran straight through unharmed).
            // The player's own half-extents fatten the box (Minkowski sum), and the
            // centre still shifts down toward the floor. Disabled during the intro.
            if (pl != null && !IntroHold)
            {
                if (!_prevInit) { _prevPlayer = pl.position; _prevInit = true; }
                Vector2 rel0 = (Vector2)_prevPlayer - (Vector2)PrevPos;
                Vector2 rel1 = (Vector2)pl.position - (Vector2)transform.position;
                // _contactSafe: a stunned/dissolving boss is a free-touch zone (the
                // whole point of the punish window). _bodyHalfHNow: the Ghoul ducks
                // during his charge so the charge stays jumpable.
                if (!_contactSafe && SegmentVsAabb(rel0, rel1, new Vector2(0f, -BodyDrop),
                                  BodyHalfW + 0.30f, _bodyHalfHNow + 0.45f))
                    GameRoot.I.HitPlayer(_contactCause ?? $"Caught by {BossName()}.");
                _prevPlayer = pl.position;
            }
            PrevPos = transform.position;
        }

        Vector3 _prevPlayer; bool _prevInit;

        // Does segment a→b touch the axis-aligned box (center c, half-extents hw/hh)?
        // Standard slab test; shared by the contact kill above and Bullet (Combat.cs).
        public static bool SegmentVsAabb(Vector2 a, Vector2 b, Vector2 c, float hw, float hh)
        {
            Vector2 d = b - a;
            float tMin = 0f, tMax = 1f;
            for (int axis = 0; axis < 2; axis++)
            {
                float da = axis == 0 ? d.x : d.y;
                float aa = axis == 0 ? a.x : a.y;
                float lo = (axis == 0 ? c.x - hw : c.y - hh);
                float hi = (axis == 0 ? c.x + hw : c.y + hh);
                if (Mathf.Abs(da) < 1e-6f)
                {
                    if (aa < lo || aa > hi) return false;   // parallel and outside the slab
                }
                else
                {
                    float t1 = (lo - aa) / da, t2 = (hi - aa) / da;
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    tMin = Mathf.Max(tMin, t1);
                    tMax = Mathf.Min(tMax, t2);
                    if (tMin > tMax) return false;
                }
            }
            return true;
        }

        // ---------- the attack brain ----------
        IEnumerator Brain()
        {
            // Wait out the cinematic intro before the boss does anything.
            while (IntroHold && !_dead) yield return null;
            yield return Wait(1.1f);
            while (!_dead)
            {
                // Defensive reset: if a dash pattern was ever cut short, don't let a
                // stale flag freeze facing/drift for the rest of the fight.
                _dashing = false;
                // PHASE check: each crossed threshold is a telegraphed escalation event.
                int newPhase = ComputePhase();
                if (newPhase > _phase)
                {
                    _phase = newPhase;
                    _enraged = _phase >= _maxPhase;   // enrage = the FINAL phase
                    if (_tier >= 4)
                    {
                        // The Lord doesn't just escalate — he SHAPESHIFTS. The swap
                        // beat replaces the generic escalation entirely.
                        yield return FormSwap(_phase);
                    }
                    else
                    {
                        GameRoot.I?.ScreenFlash();
                        GameRoot.I?.ShakeCam(0.5f, 0.4f);
                        Fx.Ring(transform.position, new Color(1f, 0.15f, 0.15f, 0.7f), 4f, 0.5f);
                        Audio.PlayOr("boss_roar", "death", 0.7f);
                        GameRoot.I?.BossToast(_enraged ? "ENRAGED" : $"PHASE {_phase + 1}");
                        GameRoot.I?.SlowMoBurst(0.25f, 0.5f);   // a beat to read the shift
                        // The final phase reads at a glance: a persistent dark-red rim
                        // pulse on the body, and the HP bar's chip layer runs hot.
                        if (_enraged) _anim?.SetRim(new Color(0.55f, 0.04f, 0.08f), 0.45f);
                    }
                    if (_chipImg != null) _chipImg.color = _enraged
                        ? new Color(1f, 0.45f, 0.3f, 0.9f) : new Color(1f, 0.72f, 0.5f, 0.88f);
                    if (_tier < 4) yield return Wait(0.5f);
                }

                // Run the pattern as a CHILD coroutine so crossing a phase threshold
                // (detected in Hit) can cut it short — the boss reacts to the wound
                // NOW instead of politely finishing its old move first.
                _phaseInterrupt = false;
                _patternDone = false;
                _currentPattern = StartCoroutine(RunPattern(RunRandomPattern()));
                while (!_patternDone && !_phaseInterrupt && !_dead) yield return null;
                if (_dead) yield break;
                if (_phaseInterrupt && !_patternDone)
                {
                    if (_currentPattern != null) StopCoroutine(_currentPattern);
                    CleanupPattern();
                    continue;   // loop top plays the escalation beat immediately
                }

                // ALWAYS leave a real shoot window — even enraged it never drops below
                // ~0.7s, and the boss reads as VULNERABLE during it so players learn the
                // rhythm: dodge the pattern, then punish in the opening.
                float gap = Mathf.Max(0.7f, (_enraged ? 0.9f - _tier * 0.05f : 1.2f - _tier * 0.06f));
                // Shield-rhythm bosses (the Warlock; the Lord's Mist form) get a
                // LONGER window — it's their only damage window, so it must fit a
                // meaningful burst of the 5-shot clip.
                if (_usesShield)
                    gap = Diff.Current == Difficulty.Casual ? 2.4f
                        : Diff.Current == Difficulty.Normal ? 2.0f : 1.7f;
                yield return OpenWindow(gap);
                // The Warlock swaps casting perches between patterns — never mid-cast,
                // so the relocation is readable downtime, not a dodge tax.
                if (_moveMode == MoveMode.Perch && !_dead) yield return RelocatePerch();
            }
        }

        IEnumerator RunPattern(IEnumerator pattern)
        {
            yield return pattern;
            _patternDone = true;
        }

        // A stopped pattern leaves its TELEGRAPH markers behind (its local cleanup
        // never runs). Sweep them by name; live hazards (spikes/bolts/bats/lightning)
        // stay and finish naturally so the interrupt never deletes a threat mid-dodge.
        void CleanupPattern()
        {
            _dashing = false;
            _stunned = false;
            _contactSafe = false;
            _bodyHalfHNow = BodyHalfH;          // un-duck (charge cut short mid-flight)
            _anchorOverride = float.NaN;        // drop any pattern-held height
            _contactCause = null;
            ClearDecoys();
            _anim?.ResetChannels();
            _anim?.SetShielded(_shielded);      // re-assert: the shield is a STATE, not a pulse
            if (_hazards == null) return;
            for (int i = _hazards.childCount - 1; i >= 0; i--)
            {
                var child = _hazards.GetChild(i);
                switch (child.name)
                {
                    case "SpikeWarn":
                    case "BoltWarn":
                    case "DashLane":
                    case "ChargeLane":
                    case "FogWarn":
                    case "PerchMark":
                    case "SafeGlow":
                    case "BossWarn":
                        Destroy(child.gameObject);
                        break;
                }
            }
        }

        // Shatter every live decoy (real hit / pattern interrupt / defeat). The
        // punish-on-shot path goes through TryInterceptDecoy instead.
        void ClearDecoys()
        {
            for (int i = _decoys.Count - 1; i >= 0; i--)
                if (_decoys[i] != null) _decoys[i].Shatter();
            _decoys.Clear();
        }

        // Which phase the current HP fraction puts us in: thresholds split the HP
        // bar evenly across however many phases this tier has (2 phases → 50%,
        // 3 → 67/34%, 4 → 75/50/25%).
        int ComputePhase()
        {
            float f = _hpMax > 0 ? (float)_hp / _hpMax : 0f;
            int phases = _maxPhase + 1;
            return Mathf.Clamp(Mathf.FloorToInt((1f - f) * phases), 0, _maxPhase);
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

        // Tier 1 — THE GHOUL: a grounded bruiser (2 phases). Everything he does now
        // touches the floor. His signature is the wall-slam charge: bait it from near
        // a wall and he eats the masonry — stunned, free to touch, taking DOUBLE
        // damage. Read it from mid-arena and he stops short with only a small recover.
        IEnumerator GhoulPool(int s)
        {
            switch (s % 5)
            {
                case 0: return Pattern_WallCharge();
                case 1: return Pattern_GroundSpikes();
                case 2: return Pattern_Shockwave();
                case 3: return _phase >= 1 ? Pattern_Minefield() : Pattern_WallCharge();
                default: return _phase >= 1 ? Chain(Pattern_Shockwave(), Pattern_WallCharge())
                                            : Pattern_GroundSpikes();
            }
        }

        // Tier 2 — THE COUNTESS: a teleporting trickster (3 phases). She never walks —
        // she blinks. Her signature MIRROR WALTZ hides her among mist decoys; the
        // fake-out stays her calling card; nothing she does touches the floor. Peak
        // "trust nothing": phase 1 chains decoys straight into a fake-out.
        IEnumerator CountessPool(int s)
        {
            switch (s % 6)
            {
                case 0: return Pattern_MirrorWaltz(_phase >= 2 ? 2 : 1);
                case 1: return _enraged ? Chain(Pattern_FakeOut(), Pattern_Volley()) : Pattern_FakeOut();
                case 2: return Pattern_Volley();
                case 3: return _phase >= 1 ? Chain(Pattern_MirrorWaltz(1), Pattern_FakeOut()) : Pattern_Fan();
                case 4: return Pattern_BlinkStrike();
                default: return Pattern_Fan();
            }
        }

        // Tier 3 — THE WARLOCK: an anchored caster (3 phases). He owns the air from
        // three high perches — out of blaster reach until his shield shatters and he
        // sinks into range between casts (the fight is a RHYTHM of openings). His
        // signature CURSED TIDE squeezes the arena; Lightning is his and his alone.
        IEnumerator WarlockPool(int s)
        {
            switch (s % 6)
            {
                case 0: return _enraged ? Chain(Pattern_BoltRain(), Pattern_Nova()) : Pattern_BoltRain();
                case 1: return Pattern_Nova();
                case 2: return Pattern_CursedTide();
                case 3: return _phase >= 1 ? Pattern_Lightning() : Pattern_BoltRain();
                case 4: return _phase >= 2 ? Chain(Pattern_CursedTide(0.8f), Pattern_Nova()) : Pattern_Nova();
                default: return Pattern_BoltRain();
            }
        }

        // Tier 4 — THE VAMPIRE LORD: a shapeshifter finale (4 phases = 4 FORMS).
        // Each form wears a boss you've already beaten — Brute (Ghoul), Swarm
        // (Countess), Mist (Warlock) — before THE TRUE LORD drops every disguise.
        // Scripted rotations (no RNG) so each form teaches its loop fast; _step
        // resets on every swap so a form always opens with its defining move.
        IEnumerator VampireLordPool(int s)
        {
            switch (_phase)
            {
                case 0:   // THE BRUTE — grounded; bait the charge (he shakes stuns off faster)
                    switch (s % 3)
                    {
                        case 0:  return Pattern_WallCharge();
                        case 1:  return Pattern_Shockwave();
                        default: return Pattern_GroundSpikes();
                    }
                case 1:   // THE SWARM — blinks, mist decoys, real bats
                    switch (s % 4)
                    {
                        case 0:  return Pattern_MirrorWaltz(1);
                        case 1:  return Pattern_Volley();
                        case 2:  return Pattern_FakeOut();
                        default: return Pattern_SummonBats();
                    }
                case 2:   // THE MIST — perched, shielded, zoning (smaller tide)
                    switch (s % 3)
                    {
                        case 0:  return Pattern_BoltRain();
                        case 1:  return Pattern_Nova();
                        default: return Pattern_CursedTide(0.7f);
                    }
                default:  // THE TRUE LORD — every lesson at once, always hittable
                    switch (s % 4)
                    {
                        case 0:  return Chain(Pattern_WallCharge(), Pattern_Volley());
                        case 1:  return Chain(Pattern_Nova(), Pattern_Shockwave());
                        case 2:  return Pattern_MirrorWaltz(1);
                        default: return Pattern_Lightning();
                    }
            }
        }

        // The Lord's phase beat: dissolve into a scatter of bats, flash, reform in
        // the new shape. Contact-safe throughout (a cutscene must never body-check),
        // and the reposition goes through TeleportTo so the swept contact test
        // re-anchors cleanly.
        IEnumerator FormSwap(int phase)
        {
            _contactSafe = true;
            GameRoot.I?.SlowMoBurst(0.25f, 0.5f);
            GameRoot.I?.ShakeCam(0.4f, 0.35f);
            Audio.PlayOr("boss_roar", "death", 0.8f);
            float e = 0f;                                  // dissolve...
            while (e < 0.45f && !_dead) { e += Time.deltaTime; _anim?.SetGhost(1f - e / 0.5f); yield return null; }
            BatBurst(6);
            GameRoot.I?.ScreenFlash();
            ApplyForm(phase);
            TeleportTo(FormAnchor(phase));
            e = 0f;                                        // ...and reform
            while (e < 0.35f && !_dead) { e += Time.deltaTime; _anim?.SetGhost(0.3f + 0.7f * e / 0.35f); yield return null; }
            _anim?.SetGhost(1f);
            GameRoot.I?.BossToast(FormName(phase));
            Fx.Ring(transform.position, new Color(0.85f, 0.15f, 0.2f, 0.7f), 3f, 0.4f);
            _step = 0;
            _contactSafe = false;
            yield return Wait(0.4f);
        }

        // Movement mode + hooks + rim tint per form. The rim colour is the at-a-
        // glance form indicator (violet swarm, arcane mist, blood-red true lord).
        void ApplyForm(int phase)
        {
            switch (phase)
            {
                case 1:   // THE SWARM — Countess callback
                    _moveMode = MoveMode.Teleport; _usesShield = false; _shielded = false;
                    _anim?.SetRim(new Color(0.5f, 0.2f, 0.6f), 0.3f);
                    break;
                case 2:   // THE MIST — Warlock callback
                    _moveMode = MoveMode.Perch; _usesShield = true; _shielded = true;
                    _anim?.SetRim(new Color(0.2f, 0.35f, 0.7f), 0.35f);
                    break;
                case 3:   // THE TRUE LORD — the existing enrage red
                    _moveMode = MoveMode.Hover; _usesShield = false; _shielded = false;
                    _anim?.SetRim(new Color(0.55f, 0.04f, 0.08f), 0.45f);
                    break;
                default:  // THE BRUTE — Ghoul callback (the fight's opening form)
                    _moveMode = MoveMode.Grounded; _usesShield = false; _shielded = false;
                    break;
            }
            _anim?.SetShielded(_shielded);
            UpdateShieldBubble();
        }

        string FormName(int phase) =>
            phase == 1 ? "THE SWARM" : phase == 2 ? "THE MIST"
          : phase >= 3 ? "THE TRUE LORD" : "THE BRUTE";

        // Where each form reforms: always the far side from the player (a swap must
        // never drop a boss on your head), at the form's natural height.
        Vector3 FormAnchor(int phase)
        {
            if (phase == 2) { var p = PerchPos(_perchIndex); return new Vector3(p.x, p.y, 0f); }
            float mid = (_minX + _maxX) / 2f;
            var pl = Player;
            float side = (pl != null && pl.position.x > mid) ? -1f : 1f;
            return new Vector3(mid + side * 4f, phase == 0 ? GroundedY : -0.3f, 0f);
        }

        // A cosmetic scatter of bat silhouettes for the form swap — pure spectacle,
        // no colliders (the REAL bats come from Pattern_SummonBats).
        void BatBurst(int n)
        {
            var frames = Assets.Sheet("bat_fly", 32);
            var sp = (frames != null && frames.Length > 0) ? frames[0] : Theme.Bat;
            for (int i = 0; i < n; i++)
            {
                var go = Theme.SpriteBox("FormBat", _hazards != null ? _hazards : transform.parent,
                    transform.position, new Vector2(0.7f, 0.7f), sp, 8);
                go.GetComponent<SpriteRenderer>().color = new Color(0.9f, 0.15f, 0.2f, 1f);
                go.AddComponent<FormBat>().Init(frames, Random.Range(0f, 360f));
            }
            Audio.PlayOr("die_screech", "death", 0.5f);
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
            bool enraged = _enraged;                             // snapshot: a phase flip mid-pattern must not change the rules under the player
            Warn(0.7f);                                          // "!" — spikes are about to erupt
            float gapX = pl.position.x;                          // gap telegraphs on you...
            float gapW = enraged ? 2.0f : 2.4f;              // still tighter when enraged, but reachable
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

        // LIGHTNING STORM: a real set-piece. Warning beams mark columns of the arena,
        // then thick bolts crash down one after another (rolling storm) with a flash +
        // shake on each strike. A late-phase signature so the fight builds to spectacle.
        IEnumerator Pattern_Lightning()
        {
            int strikes = 2 + _tier / 2 + _phase;
            float left = _minX + 1.5f, right = _maxX - 1.5f;
            var parent = _hazards != null ? _hazards : transform.parent;
            var xs = new System.Collections.Generic.List<float>();
            var warns = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < strikes; i++)
            {
                float x = Random.Range(left, right);
                xs.Add(x);
                var w = Theme.Box("BoltWarn", parent, new Vector2(x, 1.5f),
                    new Vector2(0.18f, 13f), new Color(1f, 0.92f, 0.45f, 0.55f), 5);
                var fp = w.AddComponent<FaintPulse>(); fp.min = 0.25f; fp.max = 0.75f; fp.speed = 16f;
                warns.Add(w);
            }
            Warn(0.7f);
            yield return Wait(0.7f);
            foreach (var w in warns) if (w != null) Destroy(w);
            // Strike each marked column with a bright bolt, lethal for a brief window.
            foreach (float x in xs)
            {
                if (_dead) yield break;
                var b = Theme.Box("Lightning", parent, new Vector2(x, 1.5f),
                    new Vector2(0.5f, 13f), new Color(0.95f, 0.97f, 1f, 1f), 6);
                var col = b.AddComponent<BoxCollider2D>(); col.isTrigger = true; col.size = new Vector2(0.7f, 13f);
                var kz = b.AddComponent<KillZone>(); kz.msg = $"Struck down by {BossName()}'s lightning.";
                GameRoot.I?.ScreenFlash();
                GameRoot.I?.ShakeCam(0.25f, 0.16f);
                Audio.PlayOr("die_screech", "death", 0.4f);
                StartCoroutine(DestroyAfter(b, 0.22f));
                yield return Wait(0.12f);
            }
            yield return Wait(0.3f);
        }

        IEnumerator DestroyAfter(GameObject go, float t)
        {
            yield return new WaitForSeconds(t);
            if (go != null) Destroy(go);
        }

        // The lord rears back, then DASHES across at your height. Jump it.
        IEnumerator Pattern_Dash()
        {
            var pl = Player; if (pl == null) yield break;
            int dashes = _enraged ? 2 : 1;   // snapshot at entry — see Pattern_GroundSpikes
            for (int d = 0; d < dashes && !_dead; d++)
            {
                pl = Player; if (pl == null) yield break;
                float fromX = pl.position.x < (_minX + _maxX) / 2f ? _maxX - 0.5f : _minX + 0.5f;
                float toX = (Mathf.Approximately(fromX, _maxX - 0.5f)) ? _minX + 0.5f : _maxX - 0.5f;
                float y = Mathf.Clamp(pl.position.y + 0.1f, -2.2f, 1.5f);
                _dashing = true;
                // reposition to the dash start, THEN face the way the dash will go
                // (facing must be relative to the new position, not the old one).
                // TeleportTo (not a bare position write) so the swept contact test
                // re-anchors — the bare write could phantom-kill across the arena.
                TeleportTo(new Vector3(fromX, y, 0f));
                SetFacing(Mathf.Sign(toX - fromX));
                // POSITIONAL telegraph: a red lane across the arena at the dash height,
                // so the player sees EXACTLY where to jump before the charge.
                var lane = Theme.Box("DashLane", _hazards != null ? _hazards : transform.parent,
                    new Vector2((_minX + _maxX) / 2f, y), new Vector2(_maxX - _minX, 0.12f), Theme.Danger, 5);
                var lsr = lane.GetComponent<SpriteRenderer>();
                var lc = lsr.color; lc.a = 0.5f; lsr.color = lc;
                lane.AddComponent<FaintPulse>();
                // Direction chevrons along the lane: two rotated slats forming a ">"
                // that points the way the charge goes. Named "DashLane" so a phase
                // interrupt's CleanupPattern sweeps them; siblings of the lane (NOT
                // children — Theme.Box sizes via localScale, and a child would be
                // stretched by the lane's arena-wide scale).
                float dashDir = Mathf.Sign(toX - fromX);
                var chevrons = new System.Collections.Generic.List<GameObject>();
                for (int ci = 1; ci <= 3; ci++)
                {
                    float cx = Mathf.Lerp(fromX, toX, ci / 4f);
                    var up = Theme.Box("DashLane", lane.transform.parent, new Vector2(cx - dashDir * 0.12f, y + 0.14f),
                        new Vector2(0.34f, 0.09f), Theme.Danger, 5);
                    up.transform.rotation = Quaternion.Euler(0f, 0f, dashDir * -35f);
                    var dn = Theme.Box("DashLane", lane.transform.parent, new Vector2(cx - dashDir * 0.12f, y - 0.14f),
                        new Vector2(0.34f, 0.09f), Theme.Danger, 5);
                    dn.transform.rotation = Quaternion.Euler(0f, 0f, dashDir * 35f);
                    chevrons.Add(up); chevrons.Add(dn);
                }
                // Longer wind-up + a slower sweep: the old charge crossed the arena in
                // ~0.3s (≈80 u/s) — physically impossible to dodge on reaction, only by
                // pre-jumping the telegraph. ~0.55s (≈45 u/s) is still scary but jumpable
                // when you SEE it coming, which is the whole contract of the lane tell.
                yield return Telegraph(0.6f);
                if (lane != null) Destroy(lane);
                foreach (var ch in chevrons) if (ch != null) Destroy(ch);
                _anim?.Lunge();   // release snap: stretch into the charge
                float t = 0f;
                while (t < 1f && !_dead)
                {
                    t += Time.deltaTime * 1.9f * Diff.BossSpeedMul;
                    transform.position = new Vector3(Mathf.Lerp(fromX, toX, t), y, 0f);
                    yield return null;
                }
                _dashing = false;
                yield return Wait(0.35f);   // a real breath before a second (enraged) dash
            }
        }

        // Decoy alpha is the primary tell — Casual fakes are noticeably fainter, so
        // learning to read the masquerade is gentler there.
        float DecoyAlpha() =>
            Diff.Current == Difficulty.Casual ? 0.68f
          : Diff.Current == Difficulty.Normal ? 0.80f : 0.88f;

        void SpawnDecoy(Vector3 pos, float life)
        {
            var parent = _hazards != null ? _hazards : transform.parent;
            var sp = _sr != null ? _sr.sprite : null;
            // Same silhouette as the real boss (whatever sprite she currently wears —
            // the Lord's Swarm form gets boss4 copies for free).
            var go = sp != null
                ? Theme.SpriteBox("Decoy", parent, pos, new Vector2(2.6f, 2.6f), sp, 4)
                : Theme.Box("Decoy", parent, pos, new Vector2(2.0f, 2.6f), Theme.Hex("2A0A12"), 4);
            var d = go.AddComponent<BossDecoy>();
            d.Init(this, DecoyAlpha(), life);
            _decoys.Add(d);
            Fx.Burst(pos, new Color(0.72f, 0.32f, 0.95f, 0.8f), 6, 4f, 0.12f, 0.3f, 5f);
        }

        // THE COUNTESS's signature MIRROR WALTZ: she dissolves and reappears among
        // mist copies. Every figure winds up the same violet strike — but the fakes
        // pulse a half-beat LATE and sit fainter (both learnable), and only the real
        // one fires. Shooting a fake wastes the bullet and buys its revenge volley;
        // wounding HER shatters the whole masquerade at once.
        IEnumerator Pattern_MirrorWaltz(int decoyCount)
        {
            if (Diff.Current == Difficulty.Casual) decoyCount = 1;   // Casual: always one fake
            var pl = Player; if (pl == null) yield break;
            Warn(0.45f);
            float e = 0f;   // the dissolve is the "she's moving" tell
            while (e < 0.25f && !_dead) { e += Time.deltaTime; _anim?.SetGhost(1f - e / 0.3f); yield return null; }
            if (_dead) yield break;

            // Slots spread across the arena: none on the player, none overlapping.
            float left = _minX + 2f, right = _maxX - 2f;
            var slots = new System.Collections.Generic.List<float>();
            int guard = 0;
            while (slots.Count < decoyCount + 1 && guard++ < 80)
            {
                float x = Random.Range(left, right);
                pl = Player;
                if (pl != null && Mathf.Abs(x - pl.position.x) < 3f) continue;
                bool ok = true;
                foreach (float s in slots) if (Mathf.Abs(s - x) < 4f) { ok = false; break; }
                if (ok) slots.Add(x);
            }
            if (slots.Count == 0)
                slots.Add(Mathf.Clamp((pl != null ? pl.position.x : 0f) + 5f, left, right));

            int realIdx = Random.Range(0, slots.Count);
            TeleportTo(new Vector3(slots[realIdx], HoverY, 0f));
            _anim?.SetGhost(1f);
            for (int i = 0; i < slots.Count; i++)
                if (i != realIdx) SpawnDecoy(new Vector3(slots[i], HoverY, 0f), 6f);

            // Synchronized wind-up. Mimic gets the FINAL telegraph duration (the
            // Telegraph coroutine applies the difficulty stretch itself).
            float strike = Mathf.Max(0.45f, 0.5f * Diff.BossTelegraphMul);
            foreach (var d in _decoys) if (d != null) d.Mimic(strike);
            yield return Telegraph(0.5f, new Color(0.72f, 0.32f, 0.95f));
            if (_dead) yield break;

            pl = Player; if (pl == null) yield break;
            Vector2 aim = ((Vector2)pl.position - (Vector2)transform.position).normalized;
            for (int i = -1; i <= 1; i++)
                SpawnBolt((Vector2)(Quaternion.Euler(0f, 0f, i * 12f) * aim), 8.5f);
            Audio.PlayOr("shoot", "click", 0.45f);
            Audio.PlayOr("click", "shoot", 0.4f);   // the decoys' dry snap — the ear tell
            yield return Wait(0.3f);
        }

        // A quick assassin's blink: shimmer, vanish, reappear across your blind side,
        // one fast aimed bolt. Keeps her teleport identity present between waltzes.
        IEnumerator Pattern_BlinkStrike()
        {
            var pl = Player; if (pl == null) yield break;
            float e = 0f;
            while (e < 0.25f && !_dead) { e += Time.deltaTime; _anim?.SetGhost(1f - e / 0.3f); yield return null; }
            if (_dead) yield break;
            pl = Player;
            if (pl == null) { _anim?.SetGhost(1f); yield break; }
            float side = transform.position.x >= pl.position.x ? -1f : 1f;   // cross to the OTHER side
            float x = Mathf.Clamp(pl.position.x + side * 3.2f, _minX + 1f, _maxX - 1f);
            if (Mathf.Abs(x - pl.position.x) < 3f)   // wall clamp shoved her onto you — mirror back
                x = Mathf.Clamp(pl.position.x - side * 3.2f, _minX + 1f, _maxX - 1f);
            TeleportTo(new Vector3(x, HoverY, 0f));
            _anim?.SetGhost(1f);
            yield return Telegraph(0.5f, new Color(0.72f, 0.32f, 0.95f));
            if (_dead) yield break;
            pl = Player; if (pl == null) yield break;
            SpawnBolt(((Vector2)pl.position - (Vector2)transform.position).normalized, 10.5f);
            yield return Wait(0.25f);
        }

        // THE GHOUL's signature: a floor-level shoulder charge. The lane shows the
        // full possible run (his feet to the wall); the ENDPOINT locks at the end of
        // the wind-up — your position then, plus a 2.5-unit overshoot. Stand near a
        // wall and hop away late, and the overshoot carries him into the masonry:
        // stunned, contact-safe, double damage. Read it from mid-arena and he stops
        // short with only a small recover. The jackpot is engineered, never free.
        IEnumerator Pattern_WallCharge()
        {
            var pl = Player; if (pl == null) yield break;
            _anchorOverride = GroundedY;   // floor charge even from a hovering form
            float dir = pl.position.x >= transform.position.x ? 1f : -1f;
            SetFacing(dir);
            float wallX = dir > 0f ? _maxX - 0.9f : _minX + 0.9f;
            float laneY = GroundedY - 0.45f;
            float x0 = transform.position.x;
            var parent = _hazards != null ? _hazards : transform.parent;
            // Lane + direction chevrons at floor height (same telegraph language as
            // the dash lane — players already know how to read it). Chevrons are
            // SIBLINGS of the lane: Theme.Box sizes via localScale, and a child
            // would be stretched by the lane's arena-long scale.
            var lane = Theme.Box("ChargeLane", parent, new Vector2((x0 + wallX) / 2f, laneY),
                new Vector2(Mathf.Max(0.5f, Mathf.Abs(wallX - x0)), 0.12f), Theme.Danger, 5);
            var lsr = lane.GetComponent<SpriteRenderer>();
            var lc = lsr.color; lc.a = 0.5f; lsr.color = lc;
            lane.AddComponent<FaintPulse>();
            var chevrons = new System.Collections.Generic.List<GameObject>();
            for (int ci = 1; ci <= 3; ci++)
            {
                float chx = Mathf.Lerp(x0, wallX, ci / 4f);
                var up = Theme.Box("ChargeLane", parent, new Vector2(chx - dir * 0.12f, laneY + 0.14f),
                    new Vector2(0.34f, 0.09f), Theme.Danger, 5);
                up.transform.rotation = Quaternion.Euler(0f, 0f, dir * -35f);
                var dn = Theme.Box("ChargeLane", parent, new Vector2(chx - dir * 0.12f, laneY - 0.14f),
                    new Vector2(0.34f, 0.09f), Theme.Danger, 5);
                dn.transform.rotation = Quaternion.Euler(0f, 0f, dir * 35f);
                chevrons.Add(up); chevrons.Add(dn);
            }
            yield return Telegraph(0.6f);
            if (lane != null) Destroy(lane);
            foreach (var ch in chevrons) if (ch != null) Destroy(ch);
            if (_dead) yield break;

            // Endpoint LOCK. If you crossed behind him during the wind-up, the charge
            // whiffs short (a matador dodge) — brave, and it denies him the wall.
            pl = Player;
            bool onSide = pl != null && Mathf.Sign(pl.position.x - transform.position.x) == dir;
            float endX = onSide
                ? Mathf.Clamp(pl.position.x + 2.5f * dir, _minX + 0.9f, _maxX - 0.9f)
                : Mathf.Clamp(x0 + 4f * dir, _minX + 0.9f, _maxX - 0.9f);
            bool slams = Mathf.Abs(endX - wallX) < 0.1f;

            _anim?.Lunge();
            _dashing = true;                 // freeze facing/drift for the run
            _bodyHalfHNow = 1.2f;            // head-down shoulder charge: jumpable
            _contactCause = $"Crushed by {BossName()}'s charge.";
            float speed = 13f * Diff.BossSpeedMul, dustT = 0f;
            while (!_dead && Mathf.Abs(transform.position.x - endX) > 0.02f)
            {
                float x = Mathf.MoveTowards(transform.position.x, endX, speed * Time.deltaTime);
                transform.position = new Vector3(x, GroundedY, 0f);
                dustT -= Time.deltaTime;
                if (dustT <= 0f) { dustT = 0.07f; Fx.Dust(transform.position + Vector3.down * 0.6f); }
                yield return null;
            }
            _bodyHalfHNow = BodyHalfH;
            _dashing = false;
            _anchorOverride = float.NaN;
            _contactCause = null;
            if (_dead) yield break;

            if (slams)
            {
                // MASONRY. Rubble, roar, and the fight's jackpot window.
                GameRoot.I?.ShakeCam(0.5f, 0.4f);
                GameRoot.I?.ScreenFlash();
                Fx.Burst(transform.position + new Vector3(dir * 0.9f, 0.3f, 0f),
                    new Color(0.6f, 0.55f, 0.5f, 1f), 14, 6f, 0.16f, 0.5f, 10f);
                Fx.Ring(transform.position, new Color(1f, 0.85f, 0.35f, 0.7f), 2.2f, 0.35f);
                Audio.PlayOr("boss_hit", "death", 0.8f);
                yield return Stunned(StunDur());
            }
            else
            {
                // Stopped short: a small, honest recover — not the jackpot.
                _anim?.SetVulnerable(true);
                yield return Wait(ShortRecover());
                _anim?.SetVulnerable(false);
            }
        }

        // Casual gets a long, luxurious punish window; Nightmare a tight one. The
        // Lord's Brute form (tier 4) shakes it off 20% faster in every case.
        float StunDur()
        {
            float d = Diff.Current == Difficulty.Casual ? 3.2f
                    : Diff.Current == Difficulty.Normal ? 2.6f : 2.0f;
            return _tier >= 4 ? d * 0.8f : d;
        }

        float ShortRecover() =>
            Diff.Current == Difficulty.Casual ? 1.0f
          : Diff.Current == Difficulty.Normal ? 0.8f : 0.7f;

        // The earned punish window: out cold, free to touch, every shot bites double
        // (see Hit). Phase crossings are deferred while this runs — the escalation
        // beat must never steal the window (Brain catches the crossing right after).
        IEnumerator Stunned(float dur)
        {
            _stunned = true; _contactSafe = true;
            _anim?.SetStunned(true);
            if (!_toastStun) { _toastStun = true; GameRoot.I?.BossToast("STUNNED — UNLOAD!"); }
            // Dizzy stars riding him (WarnMark already knows how to follow the boss).
            var go = new GameObject("BossWarn");
            go.transform.SetParent(_hazards != null ? _hazards : transform.parent, false);
            go.transform.position = transform.position + Vector3.up * 1.7f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = "* * *"; tm.fontSize = 48; tm.characterSize = 0.12f; tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.85f, 0.35f);
            go.GetComponent<MeshRenderer>().sortingOrder = 30;
            go.AddComponent<WarnMark>().Init(dur, transform);
            float e = 0f;
            while (e < dur && !_dead) { e += Time.deltaTime; yield return null; }   // out cold: no drift
            _stunned = false; _contactSafe = false;
            _anim?.SetStunned(false);
            _anim?.Impact();                          // the shake-off
            Audio.PlayOr("boss_roar", "death", 0.5f);
        }

        // TROLL: a full attack wind-up... that fizzles. The instant you panic-dodge,
        // a fast aimed bolt punishes the over-react. After the FIRST fake, the wind-up
        // pulses VIOLET instead of red — a tell you can learn ("violet = don't flinch"),
        // which turns her from random-feeling into a duel you get better at.
        IEnumerator Pattern_FakeOut()
        {
            Color pulse = _fakeouts++ == 0 ? Theme.Danger : new Color(0.72f, 0.32f, 0.95f);
            yield return Telegraph(0.5f, pulse);
            // the bait: "fire"... but nothing comes out
            Audio.PlayOr("click", "shoot", 0.5f);
            yield return Wait(0.45f);                 // you relax / scramble here
            var pl = Player; if (pl == null) yield break;
            Vector2 aim = ((Vector2)pl.position - (Vector2)transform.position).normalized;
            SpawnBolt(aim, 10f + _tier * 0.6f);       // the real punish — fast enough to bite panic, slow enough to read
            yield return Wait(0.2f);
        }

        // Bolts rain from the ceiling with weave-through gaps. Each wave now shows a
        // faint pale column over its safe gap for a beat BEFORE the bolts drop — the
        // dodge is a read ("get to the light"), not a guess.
        IEnumerator Pattern_BoltRain()
        {
            int waves = 2 + (_enraged ? 1 : 0);   // snapshot at entry — see Pattern_GroundSpikes
            Warn(0.7f);                                          // "!" — bolts are about to rain
            var parent = _hazards != null ? _hazards : transform.parent;
            for (int w = 0; w < waves && !_dead; w++)
            {
                float gap = Random.Range(_minX + 2f, _maxX - 2f);
                var glow = Theme.Box("SafeGlow", parent, new Vector2(gap, 1.5f),
                    new Vector2(2.6f, 13f), new Color(0.75f, 0.9f, 1f, 0.13f), 4);
                var gp = glow.AddComponent<FaintPulse>(); gp.min = 0.07f; gp.max = 0.18f; gp.speed = 7f;
                yield return Wait(0.35f);
                for (float x = _minX + 1f; x <= _maxX - 1f; x += 1.5f)
                {
                    if (Mathf.Abs(x - gap) < 1.6f) continue;
                    MakeBolt(new Vector3(x, 5f, 0f)).AddComponent<BossBolt>()
                        .Init(Vector2.down, (7.5f + _tier) * Diff.BossSpeedMul, BoltFrames());
                }
                yield return Wait(0.7f);
                if (glow != null) Destroy(glow);
            }
        }

        // SHOCKWAVE: the boss slams to the deck and sends ground waves racing both
        // ways along the floor — JUMP them. Later phases send a second, faster pair.
        // The Ghoul's signature: a bruiser attack you dodge with the game's one
        // core verb, but under time pressure.
        IEnumerator Pattern_Shockwave()
        {
            yield return Telegraph(0.5f);
            // slam down to the floor for the impact
            float y0 = transform.position.y, t = 0f;
            while (t < 1f && !_dead)
            {
                t += Time.deltaTime * 6f;
                transform.position = new Vector3(transform.position.x, Mathf.Lerp(y0, -1.9f, t), 0f);
                yield return null;
            }
            var parent = _hazards != null ? _hazards : transform.parent;
            int pairs = _phase >= 1 ? 2 : 1;
            for (int w = 0; w < pairs && !_dead; w++)
            {
                GameRoot.I?.ShakeCam(0.3f, 0.25f);
                Audio.PlayOr("boss_hit", "death", 0.6f);
                Fx.Ring(transform.position + Vector3.down * 0.5f, new Color(1f, 0.55f, 0.2f, 0.7f), 2.5f, 0.3f);
                SpawnWave(parent, -1f, (6.5f + w * 2f) * Diff.BossSpeedMul);
                SpawnWave(parent, 1f, (6.5f + w * 2f) * Diff.BossSpeedMul);
                yield return Wait(0.55f);
            }
            yield return Wait(0.25f);
        }

        void SpawnWave(Transform parent, float dir, float speed)
        {
            var go = Theme.Box("Shockwave", parent,
                new Vector2(transform.position.x + dir * 1.2f, -2.35f),
                new Vector2(0.7f, 0.8f), new Color(1f, 0.5f, 0.15f, 0.95f), 6);
            go.AddComponent<FaintPulse>();
            go.AddComponent<BossWave>().Init(dir, speed, _minX, _maxX, BossName());
        }

        // VOLLEY: a burst of bolts, each aimed at where you are RIGHT THEN — standing
        // still is death, steady movement walks the whole burst. The Countess's
        // dueling pressure; grows a shot per phase.
        IEnumerator Pattern_Volley()
        {
            yield return Telegraph(0.4f);
            int shots = 3 + _phase;
            for (int i = 0; i < shots && !_dead; i++)
            {
                var pl = Player; if (pl == null) yield break;
                Vector2 aim = ((Vector2)pl.position - (Vector2)transform.position).normalized;
                SpawnBolt(aim, 8.5f + _tier * 0.5f);
                Audio.PlayOr("shoot", "click", 0.35f);
                yield return Wait(0.28f);
            }
            yield return Wait(0.2f);
        }

        // NOVA: a radial ring of bolts flung outward in spokes — the dodge is
        // standing in a gap (or jumping the low spokes). Spoke count grows with
        // phase but the gaps stay walkable. The Warlock's zone-control signature.
        IEnumerator Pattern_Nova()
        {
            yield return Telegraph(0.55f);
            int spokes = 6 + _phase * 2;
            float offset = Random.Range(0f, 360f);
            for (int i = 0; i < spokes; i++)
            {
                float a = (offset + i * 360f / spokes) * Mathf.Deg2Rad;
                SpawnBolt(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), 6.5f + _tier * 0.5f);
            }
            Audio.PlayOr("die_screech", "death", 0.45f);
            yield return Wait(0.35f);
        }

        // THE WARLOCK's arena denial: cursed fog seeps in from a wall and claims
        // ground while he keeps casting into the squeeze. The kill edge sits INSET
        // behind the visible face — the fog looks scarier than it bites, so a death
        // to it always feels earned. Enrage sends fog from BOTH walls at once (the
        // safe centre is guaranteed: both claims stop well short of the middle).
        IEnumerator Pattern_CursedTide(float claimMul = 1f)
        {
            bool both = _enraged;
            float side = Random.value < 0.5f ? -1f : 1f;
            var parent = _hazards != null ? _hazards : transform.parent;

            var warns = new System.Collections.Generic.List<GameObject>();
            void MakeWarn(float s)
            {
                var w = Theme.Box("FogWarn", parent,
                    new Vector2(s > 0f ? _maxX - 0.6f : _minX + 0.6f, 0.5f),
                    new Vector2(1.2f, 8f), new Color(0.45f, 0.9f, 0.5f, 0.4f), 4);
                var fp = w.AddComponent<FaintPulse>(); fp.min = 0.2f; fp.max = 0.55f; fp.speed = 10f;
                warns.Add(w);
            }
            if (both) { MakeWarn(-1f); MakeWarn(1f); } else MakeWarn(side);
            Warn(0.7f);
            yield return Wait(0.7f * Diff.BossTelegraphMul);
            foreach (var w in warns) if (w != null) Destroy(w);
            if (_dead) yield break;

            float speed = Diff.Current == Difficulty.Casual ? 1.6f
                        : Diff.Current == Difficulty.Normal ? 2.0f : 2.4f;
            float claim = (Diff.Current == Difficulty.Casual ? 3.5f
                        : Diff.Current == Difficulty.Normal ? 4.5f : 5.5f) * claimMul;
            float inset = Diff.Current == Difficulty.Casual ? 0.8f
                        : Diff.Current == Difficulty.Normal ? 0.6f : 0.5f;
            void MakeFog(float s)
            {
                var go = new GameObject("CursedFog");
                go.transform.SetParent(parent, false);
                go.AddComponent<FogWall>().Init(s > 0f ? _maxX : _minX, -s, speed, claim,
                                                2.0f, 1.5f, inset, BossName());
            }
            if (both) { MakeFog(-1f); MakeFog(1f); } else MakeFog(side);
            Audio.PlayOr("portal", "death", 0.5f);

            // Cast pressure into the squeezed space while the tide advances + holds.
            yield return Wait(claim / speed * 0.6f);
            if (!_dead) yield return Pattern_Volley();
            yield return Wait(0.6f);   // the fog recedes on its own timeline
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
                // avoids a white/dark flash). No glow disc — the bright red reads fine.
                bat.GetComponent<SpriteRenderer>().color = new Color(1f, 0.22f, 0.22f, 1f);
                bat.AddComponent<BatEnemy>().Init(frames);
            }
            yield return Wait(0.3f);
        }

        // ---------- helpers ----------
        IEnumerator Telegraph(float dur) { return Telegraph(dur, Theme.Danger); }

        // The pulse colour is a per-attack tell (the Countess's learnable violet
        // fake-out, for example). Colour + the anticipation crouch both run through
        // the animator so nothing fights the hit flash.
        IEnumerator Telegraph(float dur, Color pulse)
        {
            dur *= Diff.BossTelegraphMul;        // Casual/Normal get longer, more readable wind-ups
            dur = Mathf.Max(0.45f, dur);         // anticipation never shrinks below a reaction floor,
                                                 // even on the hardest tiers (they get COMPLEXITY, not faster tells)
            Warn(dur);                           // pop a "!" + audio cue so the incoming one-shot is unmistakable
            Fx.Ring(transform.position, new Color(1f, 0.25f, 0.25f, 0.6f), 2.4f, dur);  // wind-up ring
            _anim?.BeginTelegraph(pulse);
            float e = 0f;
            while (e < dur && !_dead)
            {
                e += Time.deltaTime;
                // Hold the mode's anchor height (eased — a grounded boss telegraphing
                // right after the intro shouldn't snap to the floor in one frame).
                transform.position = new Vector3(transform.position.x,
                    Mathf.MoveTowards(transform.position.y, AnchorY(), 8f * Time.deltaTime), 0f);
                yield return null;
            }
            _anim?.EndTelegraph();
        }

        // The shoot window between patterns: stalk the player (keeps pressure on)
        // but pulse a pale "vulnerable" glow so the player learns THIS is the beat to
        // shoot. Shield-rhythm bosses add the full ceremony: the shield SHATTERS at
        // the window's start and the boss SINKS into blaster range (bullets fly flat
        // at player height, so his perch altitude is immunity the rest of the time) —
        // then the shield snaps back up as the window closes.
        IEnumerator OpenWindow(float dur)
        {
            bool shieldRhythm = _usesShield;
            if (shieldRhythm)
            {
                _shielded = false;
                _anim?.SetShielded(false);
                UpdateShieldBubble();
                Fx.Ring(transform.position, new Color(0.5f, 0.8f, 1f, 0.8f), 3f, 0.35f);
                Audio.PlayOr("boss_hit", "click", 0.6f);
            }
            _anim?.SetVulnerable(true);   // pale slow pulse = "THIS is the beat to shoot"
            float e = 0f;
            while (e < dur && !_dead)
            {
                e += Time.deltaTime;
                if (shieldRhythm)
                {
                    // The opening is a POSTURE: he dips down into range instead of
                    // drifting sideways, so the moment reads across the arena.
                    transform.position = new Vector3(transform.position.x,
                        Mathf.MoveTowards(transform.position.y, -0.4f, 5f * Time.deltaTime), 0f);
                }
                else DriftStep();
                yield return null;
            }
            _anim?.SetVulnerable(false);
            if (shieldRhythm && !_dead)
            {
                _shielded = true;
                _anim?.SetShielded(true);
                UpdateShieldBubble();
                Audio.PlayOr("portal", "click", 0.4f);
            }
        }

        // The visible arcane bubble wrapping a shielded boss — a CHILD object, so the
        // animator's single-writer rule over the boss's own renderer stays intact.
        GameObject _bubble;
        void UpdateShieldBubble()
        {
            if (_bubble == null && _shielded)
            {
                _bubble = new GameObject("ShieldBubble");
                _bubble.transform.SetParent(transform, false);
                var sr = _bubble.AddComponent<SpriteRenderer>();
                sr.sprite = Theme.Moon;                      // soft glow disc, tinted arcane
                sr.color = new Color(0.45f, 0.7f, 1f, 0.22f);
                sr.sortingOrder = 7;
                // Compensate the parent's ~2.6 base scale so the bubble wraps the body.
                float s = 4.2f;
                _bubble.transform.localScale = new Vector3(
                    s / Mathf.Max(0.01f, _baseScaleX), s / Mathf.Max(0.01f, _baseScaleY), 1f);
            }
            if (_bubble != null) _bubble.SetActive(_shielded);
        }

        // Glide to the next perch between patterns. A faint glow disc marks the
        // destination a beat early, so even the movement telegraphs.
        IEnumerator RelocatePerch()
        {
            int next = (_perchIndex + (Random.value < 0.5f ? 1 : 2)) % 3;   // never the same perch
            var dest = PerchPos(next);
            var mark = Theme.Box("PerchMark", _hazards != null ? _hazards : transform.parent,
                dest, new Vector2(1.6f, 0.25f), new Color(0.45f, 0.7f, 1f, 0.4f), 4);
            mark.AddComponent<FaintPulse>();
            yield return Wait(0.3f);
            _perchIndex = next;
            Vector3 from = transform.position, to = new Vector3(dest.x, dest.y, 0f);
            float t = 0f;
            while (t < 1f && !_dead)
            {
                t += Time.deltaTime / 0.5f;
                transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                if (Random.value < 0.25f)   // trailing arcane streamer
                    Fx.Burst(transform.position, new Color(0.45f, 0.7f, 1f, 0.5f), 1, 1.5f, 0.08f, 0.25f, 2f);
                yield return null;
            }
            if (mark != null) Destroy(mark);
        }

        // Stalk the player while idle so it keeps the pressure on.
        IEnumerator Wait(float dur)
        {
            float e = 0f;
            while (e < dur && !_dead)
            {
                e += Time.deltaTime;
                DriftStep();
                yield return null;
            }
        }

        // The height this boss idles at — movement personality lives here. Patterns
        // that need a specific height regardless of mode (the TRUE LORD doing a
        // floor charge from hover) set the override for their duration.
        float _anchorOverride = float.NaN;
        float AnchorY()
        {
            if (!float.IsNaN(_anchorOverride)) return _anchorOverride;
            switch (_moveMode)
            {
                case MoveMode.Grounded: return GroundedY;
                case MoveMode.Perch:    return PerchPos(_perchIndex).y + Mathf.Sin(_bob * 2f) * 0.15f;
                default:                return HoverY;
            }
        }

        // The Warlock's three casting perches, computed from the arena bounds. High
        // on purpose: bullets fly flat at player height, so a perched Warlock is out
        // of reach — he DESCENDS during his open windows (see OpenWindow), which is
        // exactly the rhythm the fight teaches.
        Vector2 PerchPos(int i)
        {
            float mid = (_minX + _maxX) / 2f;
            switch (((i % 3) + 3) % 3)
            {
                case 0:  return new Vector2(Mathf.Max(_minX + 2.5f, mid - 8f), 1.4f);
                case 1:  return new Vector2(mid, 2.1f);
                default: return new Vector2(Mathf.Min(_maxX - 2.5f, mid + 8f), 1.4f);
            }
        }

        // Instant reposition with mist FX. CRITICAL: re-anchors BOTH swept-collision
        // ends (boss PrevPos + the player-side _prevPlayer) so the next frame never
        // sweeps a phantom body across the arena — the old dash reposition skipped
        // this and could kill a player the boss never actually touched.
        void TeleportTo(Vector3 pos)
        {
            var mist = new Color(0.72f, 0.32f, 0.95f, 0.9f);
            Fx.Burst(transform.position, mist, 8, 5f, 0.14f, 0.35f, 6f);
            transform.position = pos;
            PrevPos = pos;
            _prevInit = false;
            Fx.Burst(pos, mist, 8, 5f, 0.14f, 0.35f, 6f);
            Fx.Ring(pos, new Color(0.72f, 0.32f, 0.95f, 0.6f), 1.8f, 0.25f);
        }

        // One tick of idle movement, split by movement personality. Hover: the old
        // drift — hold a lane ~2.8 units to your side (pressure you can see coming,
        // never a body-check). The other modes are what make each boss FEEL distinct.
        void DriftStep()
        {
            if (_dashing) return;
            switch (_moveMode)
            {
                case MoveMode.Grounded: GroundedDrift(); return;
                case MoveMode.Teleport: TeleportDrift(); return;
                case MoveMode.Perch:    PerchDrift();    return;
            }
            float tx = transform.position.x;
            var pl = Player;
            if (pl != null)
            {
                float side = transform.position.x >= pl.position.x ? 1f : -1f;
                tx = Mathf.Clamp(pl.position.x + side * 2.8f, _minX + 1f, _maxX - 1f);
            }
            float nx = Mathf.MoveTowards(transform.position.x, tx, (1.6f + _tier * 0.5f) * Time.deltaTime);
            // Ease back to hover height (the shockwave slam leaves the boss at the
            // floor — snapping up in one frame read as a glitch).
            float ny = Mathf.MoveTowards(transform.position.y, HoverY, 6f * Time.deltaTime);
            transform.position = new Vector3(nx, ny, 0f);
        }

        // Ghoul: a heavy floor walk — slow, deliberate, dust at every footfall. He
        // holds the same side-lane as the hover drift but can never leave the deck.
        void GroundedDrift()
        {
            float tx = transform.position.x;
            var pl = Player;
            if (pl != null)
            {
                float side = transform.position.x >= pl.position.x ? 1f : -1f;
                tx = Mathf.Clamp(pl.position.x + side * 2.8f, _minX + 1f, _maxX - 1f);
            }
            float nx = Mathf.MoveTowards(transform.position.x, tx, 1.3f * Time.deltaTime);
            float ny = Mathf.MoveTowards(transform.position.y, GroundedY, 6f * Time.deltaTime);
            bool walking = Mathf.Abs(nx - transform.position.x) > 0.001f;
            transform.position = new Vector3(nx, ny, 0f);
            _stepDust -= Time.deltaTime;
            if (walking && _stepDust <= 0f && Mathf.Abs(ny - GroundedY) < 0.2f)
            {
                _stepDust = 0.35f;
                Fx.Dust(transform.position + Vector3.down * 0.6f);
            }
        }

        // Countess: she NEVER slides. She holds position (bobbing), and every few
        // beats she shimmers translucent (the tell), then blinks to a mist-burst
        // spot beside you. Timer-driven — no coroutine, so a pattern starting
        // mid-shimmer can't leave her half-ghosted.
        void TeleportDrift()
        {
            float ny = Mathf.MoveTowards(transform.position.y, HoverY, 6f * Time.deltaTime);
            transform.position = new Vector3(transform.position.x, ny, 0f);
            if (_preBlink >= 0f)
            {
                _preBlink -= Time.deltaTime;
                _anim?.SetGhost(Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(_preBlink / 0.25f)));
                if (_preBlink < 0f)
                {
                    _anim?.SetGhost(1f);
                    var pl = Player;
                    if (pl != null)
                    {
                        float x = Mathf.Clamp(
                            pl.position.x + (Random.value < 0.5f ? -1f : 1f) * Random.Range(3.0f, 4.5f),
                            _minX + 1f, _maxX - 1f);
                        // Arena-wall clamp can shove the spot onto the player — mirror out.
                        if (Mathf.Abs(x - pl.position.x) < 3.0f)
                            x = Mathf.Clamp(pl.position.x - Mathf.Sign(x - pl.position.x) * 3.4f,
                                            _minX + 1f, _maxX - 1f);
                        TeleportTo(new Vector3(x, HoverY, 0f));
                    }
                }
                return;
            }
            _blinkTimer -= Time.deltaTime;
            if (_blinkTimer <= 0f)
            {
                // Casual blinks ~1.75s apart, Normal ~1.44s, Nightmare ~1.25s.
                _blinkTimer = 1.25f * Diff.BossTelegraphMul;
                _preBlink = 0.25f;
                Fx.Burst(transform.position, new Color(0.72f, 0.32f, 0.95f, 0.7f), 4, 3f, 0.1f, 0.25f, 4f);
            }
        }

        // Warlock: sit ON the current perch (he swaps perches between patterns via
        // RelocatePerch, never mid-cast).
        void PerchDrift()
        {
            var p = PerchPos(_perchIndex);
            float ny = p.y + Mathf.Sin(_bob * 2f) * 0.15f;
            transform.position = new Vector3(
                Mathf.MoveTowards(transform.position.x, p.x, 6f * Time.deltaTime),
                Mathf.MoveTowards(transform.position.y, ny, 4f * Time.deltaTime), 0f);
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
            // Follow the boss: during a dash telegraph it repositions, and a "!" left
            // hanging where the boss WAS points the player at empty air.
            go.AddComponent<WarnMark>().Init(Mathf.Max(0.25f, dur), transform);
        }

        void SpawnBolt(Vector2 dir, float speed)
        {
            MakeBolt(transform.position).AddComponent<BossBolt>().Init(dir, speed * Diff.BossSpeedMul, BoltFrames());
        }

        // A bolt object: animated vfx orb if the art is present, else a coloured box.
        // Tinted a hot blood-red so it reads clearly against the night sky (no glow
        // disc — that looked like a floating yellow circle).
        GameObject MakeBolt(Vector3 pos)
        {
            var parent = _hazards != null ? _hazards : transform.parent;
            var frames = BoltFrames();
            var go = (frames != null && frames.Length > 0)
                ? Theme.SpriteBox("BossBolt", parent, pos, new Vector2(0.8f, 0.8f), frames[0], 6)
                : Theme.Box("BossBolt", parent, pos, new Vector2(0.55f, 0.55f), Theme.Danger, 6);
            go.GetComponent<SpriteRenderer>().color = new Color(1f, 0.27f, 0.2f, 1f);   // bright blood-red
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

        // Facing decisions live here (deadzone above); the actual flipX + lean writes
        // belong to the animator so they can never fight squash/stretch or the intro.
        void ApplyFacing() => _anim?.SetFacing(_faceDir);

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

        // A shot that crosses a decoy pops the DECOY instead of the boss (called by
        // Bullet before its boss-body sweep). World-space segment test — decoys never
        // move, so the bullet's own travel is the whole relative motion.
        public bool TryInterceptDecoy(Vector2 a, Vector2 b)
        {
            for (int i = _decoys.Count - 1; i >= 0; i--)
            {
                var d = _decoys[i];
                if (d == null) { _decoys.RemoveAt(i); continue; }
                Vector2 c = d.transform.position;
                if (SegmentVsAabb(a - c, b - c, Vector2.zero, BodyHalfW + 0.2f, HurtHalfH * 0.9f))
                {
                    _decoys.RemoveAt(i);
                    d.Punish();
                    if (!_toastDecoy) { _toastDecoy = true; GameRoot.I?.BossToast("A DECOY! ONLY THE REAL ONE FLINCHES"); }
                    return true;
                }
            }
            return false;
        }

        // The revenge volley a shot decoy fires (called by BossDecoy after its warn
        // beat): a few aimed bolts from where the illusion stood. Casual gets two
        // slow ones; the count/speed sting grows with difficulty.
        public void DecoyVolley(Vector3 from)
        {
            var pl = Player; if (pl == null) return;
            int shots = Diff.Current == Difficulty.Casual ? 2 : 3;
            float speed = Diff.Current == Difficulty.Casual ? 8f
                        : Diff.Current == Difficulty.Normal ? 9f : 9.5f;
            Vector2 aim = ((Vector2)pl.position - (Vector2)from).normalized;
            float spread = 10f, start = -(shots - 1) * spread / 2f;
            for (int i = 0; i < shots; i++)
            {
                var bolt = MakeBolt(from);
                bolt.AddComponent<BossBolt>().Init(
                    (Vector2)(Quaternion.Euler(0, 0, start + i * spread) * aim),
                    speed * Diff.BossSpeedMul, BoltFrames());
            }
            Audio.PlayOr("die_screech", "death", 0.4f);
        }

        public void Hit() => Hit(1);
        public void Hit(int dmg)
        {
            if (_dead) return;
            // Shielded (Warlock rhythm): the shot DEFLECTS — no damage, no phase
            // interrupt. The opening comes on the shield's rhythm, not the trigger.
            if (_shielded)
            {
                Fx.Burst(transform.position, new Color(0.5f, 0.75f, 1f, 0.9f), 5, 4f, 0.12f, 0.3f, 6f);
                Audio.PlayOr("click", "boss_hit", 0.45f);
                if (!_toastShield) { _toastShield = true; GameRoot.I?.BossToast("SHIELDED — WAIT FOR THE OPENING"); }
                return;
            }
            // Stunned (Ghoul wall-slam): the punish window the player ENGINEERED —
            // every shot bites double while he's seeing stars.
            _hp = Mathf.Max(0, _hp - Mathf.Max(1, dmg) * (_stunned ? 2 : 1));
            _anim?.HitFlash();
            _anim?.Impact();
            // The FLINCH: wounding the real boss shatters every mist decoy at once —
            // a correct read through the Mirror Waltz is rewarded with clarity.
            if (_decoys.Count > 0) ClearDecoys();
            if (!_everHit) { _everHit = true; HidePrompt(); }   // they've found the blaster
            Audio.PlayOr("boss_hit", "click", 0.6f);
            Fx.Burst(transform.position, new Color(0.8f, 0.2f, 0.95f, 0.95f), 6, 4.5f, 0.13f, 0.35f, 8f);
            UpdateHpBar();
            // Crossing a phase threshold interrupts the running pattern (see Brain) —
            // but never mid-stun: the escalation beat must not steal the damage window
            // the player just earned. Brain's loop-top check catches it right after.
            if (ComputePhase() > _phase && !_stunned) _phaseInterrupt = true;
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
            _dashing = false;   // never leave a cut-short dash's flag behind
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
            rrt.anchoredPosition = new Vector2(0, -10);   // pinned to the very top; corner HUD sits below it
            rrt.sizeDelta = new Vector2(820, 92);

            // Boss name ABOVE the bar (not crammed on top of it). Gothic title font,
            // sized to fit the width so it never clips into the bar below.
            Theme.Label(_hpRoot.transform, names[_tier], 34, Theme.Player,
                new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(820, 44)).font = Theme.TitleFont;

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
            _chipImg = chip;
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
    /// A shockwave that races along the arena floor. ONE-SHOT kill, jump it. Swept
    /// segment test like BossBolt so it can't tunnel past the player; dies at the
    /// arena walls with a puff.
    /// </summary>
    public class BossWave : MonoBehaviour
    {
        float _dir, _speed, _minX, _maxX;
        Vector3 _prev;
        string _boss = "the boss";
        const float HitRadius = 0.62f;

        public void Init(float dir, float speed, float minX, float maxX, string bossName)
        {
            _dir = Mathf.Sign(dir); _speed = speed;
            _minX = minX; _maxX = maxX;
            _boss = bossName;
            _prev = transform.position;
        }

        void Update()
        {
            _prev = transform.position;
            transform.position += Vector3.right * (_dir * _speed * Time.deltaTime);

            var pl = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            if (pl != null && SegDist(_prev, transform.position, pl.position) < HitRadius)
            {
                GameRoot.I.HitPlayer($"Flattened by {_boss}'s shockwave.");
                Destroy(gameObject);
                return;
            }
            if (transform.position.x < _minX + 0.5f || transform.position.x > _maxX - 0.5f)
            {
                Fx.Dust(transform.position);
                Destroy(gameObject);
            }
        }

        static float SegDist(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(a, p);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }
    }

    /// <summary>
    /// A cosmetic form-swap bat: spirals outward, flaps, fades, self-destructs.
    /// No collision — pure spectacle for the Vampire Lord's shapeshift beat.
    /// </summary>
    public class FormBat : MonoBehaviour
    {
        Sprite[] _frames; SpriteRenderer _sr;
        Vector2 _vel; float _life = 0.8f; const float MaxLife = 0.8f;
        float _anim, _swirl;

        public void Init(Sprite[] frames, float angleDeg)
        {
            _frames = frames;
            _sr = GetComponent<SpriteRenderer>();
            float a = angleDeg * Mathf.Deg2Rad;
            _vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(4f, 7f);
            _swirl = Random.value < 0.5f ? -180f : 180f;   // deg/s curve to the flight
        }

        void Update()
        {
            _vel = Quaternion.Euler(0f, 0f, _swirl * Time.deltaTime) * _vel;
            transform.position += (Vector3)(_vel * Time.deltaTime);
            if (_frames != null && _frames.Length > 0 && _sr != null)
            {
                _anim += Time.deltaTime;
                _sr.sprite = _frames[Mathf.FloorToInt(_anim * 16f) % _frames.Length];
            }
            _life -= Time.deltaTime;
            if (_sr != null) { var c = _sr.color; c.a = Mathf.Clamp01(_life / MaxLife); _sr.color = c; }
            if (_life <= 0f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// A wall of cursed fog that seeps in from an arena wall, claims ground, holds,
    /// then recedes and removes itself. The kill boundary sits INSET behind the
    /// visible leading face, so the fog reads scarier than it bites — deaths to it
    /// are always deep inside the cloud, never a graze. Spawned by Pattern_CursedTide,
    /// parented under BossHazards so a boss defeat wipes it with everything else.
    /// </summary>
    public class FogWall : MonoBehaviour
    {
        float _wallX, _dir, _speed, _claim, _holdLeft, _recede, _inset;
        float _extent;            // current claimed distance out from the wall
        int _phase;               // 0 = advance, 1 = hold, 2 = recede
        string _boss = "the boss";
        GameObject _body;
        float _puffT;

        public void Init(float wallX, float dir, float speed, float claim,
                         float hold, float recede, float inset, string bossName)
        {
            _wallX = wallX; _dir = Mathf.Sign(dir); _speed = speed; _claim = claim;
            _holdLeft = hold; _recede = recede; _inset = inset; _boss = bossName;
            _body = Theme.Box("FogBody", transform, new Vector2(_wallX, 0.6f),
                new Vector2(0.05f, 8.5f), new Color(0.4f, 0.85f, 0.45f, 0.32f), 6);
            var fp = _body.AddComponent<FaintPulse>(); fp.min = 0.24f; fp.max = 0.4f; fp.speed = 5f;
        }

        void Update()
        {
            switch (_phase)
            {
                case 0:
                    _extent += _speed * Time.deltaTime;
                    if (_extent >= _claim) { _extent = _claim; _phase = 1; }
                    break;
                case 1:
                    _holdLeft -= Time.deltaTime;
                    if (_holdLeft <= 0f) _phase = 2;
                    break;
                default:
                    _extent -= (_claim / Mathf.Max(0.1f, _recede)) * Time.deltaTime;
                    if (_extent <= 0f) { Destroy(gameObject); return; }
                    break;
            }

            // Stretch the body from the wall to the leading edge.
            if (_body != null)
            {
                _body.transform.position = new Vector3(_wallX + _dir * _extent / 2f, 0.6f, 0f);
                _body.transform.localScale = new Vector3(Mathf.Max(0.05f, _extent), 8.5f, 1f);
            }

            // Leading-edge puffs so the advance is unmissable.
            float edgeX = _wallX + _dir * _extent;
            _puffT -= Time.deltaTime;
            if (_puffT <= 0f && _phase == 0)
            {
                _puffT = 0.4f;
                Fx.Dust(new Vector3(edgeX, Random.Range(-2.2f, 1.5f), 0f));
            }

            // The bite: only DEEP inside the cloud, past the inset.
            var pl = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            if (pl != null && _extent > _inset)
            {
                float killEdge = _wallX + _dir * (_extent - _inset);
                bool inside = _dir > 0f ? pl.position.x < killEdge : pl.position.x > killEdge;
                if (inside) GameRoot.I.HitPlayer($"Swallowed by {_boss}'s cursed tide.");
            }
        }
    }

    /// <summary>
    /// A mist decoy of the Countess (and the Lord's Swarm form). Pure illusion: no
    /// collider, no contact kill. Its tells are learnable — it sits at lower alpha
    /// than the real boss and its strike pulse lags a half-beat behind hers. Shooting
    /// it wastes the bullet AND triggers a telegraphed revenge volley from where it
    /// stood; hitting the REAL boss shatters every decoy at once (the flinch).
    /// </summary>
    public class BossDecoy : MonoBehaviour
    {
        Boss _owner;
        SpriteRenderer _sr;
        Color _tint;
        float _life, _bob;
        bool _mimicking, _punishing;
        float _mimicLeft;

        public void Init(Boss owner, float alpha, float life)
        {
            _owner = owner;
            _life = life;
            _bob = Random.value * 6f;
            _sr = GetComponent<SpriteRenderer>();
            // Violet-washed and translucent: the alpha IS the tell (Diff-scaled by
            // the spawner — Casual decoys are noticeably fainter).
            _tint = new Color(0.78f, 0.62f, 0.95f, alpha);
            if (_sr != null) _sr.color = _tint;
        }

        // Mirror the real boss's strike wind-up — but lagging half a beat (phase
        // offset) so a player watching closely can pick the fake from the real.
        public void Mimic(float dur) { _mimicking = true; _mimicLeft = dur; }

        void Update()
        {
            if (_punishing) return;
            _bob += Time.deltaTime;
            transform.position += Vector3.up * (Mathf.Sin(_bob * 2f) * 0.4f * Time.deltaTime);
            var pl = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            if (pl != null && _sr != null)
                _sr.flipX = pl.position.x < transform.position.x;

            if (_sr != null)
            {
                if (_mimicking)
                {
                    _mimicLeft -= Time.deltaTime;
                    if (_mimicLeft <= 0f) { _mimicking = false; _sr.color = _tint; }
                    else
                    {
                        // Same hot pulse as the real telegraph (32 Hz) but offset by π —
                        // the half-beat lag a sharp eye can learn to read.
                        float p = 0.5f + 0.5f * Mathf.Sin(Time.time * 32f + Mathf.PI);
                        _sr.color = Color.Lerp(_tint, new Color(0.72f, 0.32f, 0.95f, _tint.a), p);
                    }
                }
                else
                {
                    // faint idle shimmer so it never reads as a solid body
                    var c = _tint;
                    c.a = _tint.a * (0.9f + 0.1f * Mathf.Sin(_bob * 3f));
                    _sr.color = c;
                }
            }

            _life -= Time.deltaTime;
            if (_life <= 0f) Shatter();
        }

        // Popped without punishment: real-boss flinch, pattern cleanup, or timeout.
        public void Shatter()
        {
            if (this == null) return;
            Fx.Burst(transform.position, new Color(0.72f, 0.32f, 0.95f, 0.85f), 7, 4.5f, 0.12f, 0.3f, 5f);
            Fx.Ring(transform.position, new Color(0.72f, 0.32f, 0.95f, 0.5f), 1.4f, 0.22f);
            Audio.PlayOr("click", "boss_hit", 0.35f);
            Destroy(gameObject);
        }

        // Shot by the player: flare hot for a readable warn beat, then the revenge
        // volley fires from where the illusion stood (via the owner, so bolts get
        // the boss's own art/speed/parenting).
        public void Punish()
        {
            if (_punishing) return;
            _punishing = true;
            StartCoroutine(PunishRoutine());
        }

        System.Collections.IEnumerator PunishRoutine()
        {
            float warn = 0.5f * Diff.BossTelegraphMul, e = 0f;
            while (e < warn && _sr != null)
            {
                e += Time.deltaTime;
                _sr.color = Color.Lerp(_tint, new Color(1f, 0.3f, 0.4f, _tint.a),
                                       0.5f + 0.5f * Mathf.Sin(Time.time * 24f));
                yield return null;
            }
            if (_owner != null) _owner.DecoyVolley(transform.position);
            Shatter();
        }
    }

    /// <summary>
    /// The little "!" danger marker over the boss during a wind-up: pops in, blinks,
    /// drifts up, then fades and removes itself. Pure feedback — no collision.
    /// </summary>
    public class WarnMark : MonoBehaviour
    {
        float _life, _max, _drift;
        TextMesh _tm;
        Transform _follow;   // ride the boss so the "!" stays over the actual threat

        public void Init(float dur, Transform follow = null)
        {
            _max = _life = dur;
            _tm = GetComponent<TextMesh>();
            _follow = follow;
        }

        void Update()
        {
            _life -= Time.deltaTime;
            float t = Mathf.Clamp01(1f - _life / _max);
            float pop = Mathf.Min(1f, t * 5f);                       // quick scale-in
            transform.localScale = Vector3.one * (0.6f + 0.5f * pop);
            _drift += 0.4f * Time.deltaTime;
            if (_follow != null)
                transform.position = _follow.position + Vector3.up * (1.7f + _drift);
            else
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
