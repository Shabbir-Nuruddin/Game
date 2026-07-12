using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// The ONE writer of the boss's SpriteRenderer colour, scale (squash/stretch)
    /// and lean. Before this, Telegraph / OpenWindow / the hit flash all wrote
    /// _sr.color directly and fought each other (stuck flashes, colour flicker when
    /// two coroutines overlapped). Now everything is a channel composed here each
    /// LateUpdate with a fixed priority:
    ///     hit flash  >  telegraph pulse  >  vulnerable glow  >  base (+enrage rim)
    /// Squash/stretch gives attacks an anticipation crouch and a lunge snap — the
    /// cheap, readable "animation" a single-sprite boss can actually have.
    /// </summary>
    public class BossAnimator : MonoBehaviour
    {
        SpriteRenderer _sr;
        Color _base;
        Color _rim; float _rimAmt;              // persistent enrage shift of the base colour
        float _hitFlash;                        // 1 -> 0, decays fast
        bool _telegraphing; Color _teleColor;   // hot pulse during a wind-up
        bool _vulnerable;                       // pale slow pulse during the shoot window
        bool _shield;                           // arcane sheen while shots deflect (Warlock)
        bool _stun;                             // hot gold pulse + dizzy wobble (Ghoul wall-slam)
        float _ghost = 1f;                      // alpha multiplier: teleport shimmer / form dissolve

        Vector2 _baseScale = Vector2.one;       // the boss's true transform scale (≈2.6)
        Vector2 _squash = Vector2.one;          // current multiplicative squash
        Vector2 _squashTarget = Vector2.one;    // eases toward this every frame

        float _faceDir = 1f;
        bool _artFacesLeft;

        // True during the intro cutscene: GameRoot animates the transform then, so
        // the animator must not touch scale/rotation/colour until control returns.
        public bool Hold;

        public void Init(SpriteRenderer sr, Color baseColor, Vector2 baseScale, bool artFacesLeft)
        {
            _sr = sr;
            _base = baseColor;
            _baseScale = baseScale;
            _artFacesLeft = artFacesLeft;
            Apply();
        }

        // ---- channels ----
        public void HitFlash()            { _hitFlash = 1f; }
        public void Impact()              { _squash = new Vector2(1.06f, 0.94f); }                    // quick pop when shot
        public void Anticipate()          { _squashTarget = new Vector2(0.92f, 1.10f); }              // crouch before an attack
        public void Lunge()               { _squash = new Vector2(1.18f, 0.85f); _squashTarget = Vector2.one; } // snap at attack release
        public void BeginTelegraph(Color pulse) { _telegraphing = true; _teleColor = pulse; Anticipate(); }
        public void EndTelegraph()        { _telegraphing = false; _squashTarget = Vector2.one; }
        public void SetVulnerable(bool v) { _vulnerable = v; }
        public void SetRim(Color rim, float amount) { _rim = rim; _rimAmt = Mathf.Clamp01(amount); Apply(); }
        public void SetFacing(float dir)  { if (Mathf.Abs(dir) > 0.01f) _faceDir = Mathf.Sign(dir); }
        public void SetShielded(bool v)   { _shield = v; Apply(); }
        // Stunned = slumped squash + the wobble in LateUpdate; the gold pulse says
        // "free hits" from across the arena.
        public void SetStunned(bool v)    { _stun = v; _squashTarget = v ? new Vector2(1.12f, 0.88f) : Vector2.one; Apply(); }
        public void SetGhost(float a)     { _ghost = Mathf.Clamp01(a); Apply(); }

        // Called when a pattern is interrupted mid-flight (phase escalation): drop
        // every transient channel so nothing is left mid-pulse or mid-crouch.
        // (Shield is deliberately cleared too — Boss.CleanupPattern re-asserts it
        // from the authoritative _shielded state right after.)
        public void ResetChannels()
        {
            _telegraphing = false;
            _vulnerable = false;
            _stun = false;
            _shield = false;
            _ghost = 1f;
            _squashTarget = Vector2.one;
        }

        // The single place the boss colour is decided (priority compose). Channel
        // setters call Apply() so a change lands the SAME frame it's set — the
        // enrage rim used to lag its "ENRAGED" toast by one LateUpdate.
        // Priority: hit flash > shield > telegraph > stun > vulnerable > base(+rim).
        // Ghost multiplies the final alpha so a dissolve dims ANY state uniformly.
        Color Compose()
        {
            Color baseEff = _rimAmt > 0f
                ? Color.Lerp(_base, _rim, _rimAmt * (0.7f + 0.3f * Mathf.Sin(Time.time * 3f)))
                : _base;
            Color c;
            if (_hitFlash > 0f)    c = Color.Lerp(baseEff, Color.white, Mathf.Clamp01(_hitFlash));
            else if (_shield)      c = Color.Lerp(baseEff, new Color(0.45f, 0.65f, 1f), 0.30f + 0.10f * Mathf.Sin(Time.time * 4f));
            else if (_telegraphing) c = Color.Lerp(baseEff, _teleColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 32f));
            else if (_stun)        c = Color.Lerp(baseEff, new Color(1f, 0.85f, 0.35f), 0.45f + 0.25f * Mathf.Sin(Time.time * 10f));
            else if (_vulnerable)  c = Color.Lerp(baseEff, Color.Lerp(baseEff, Color.white, 0.4f), 0.5f + 0.5f * Mathf.Sin(Time.time * 6f));
            else                   c = baseEff;
            c.a *= _ghost;
            return c;
        }

        void Apply() { if (!Hold && _sr != null) _sr.color = Compose(); }

        void LateUpdate()
        {
            // Decay runs even while held/telegraphing so a flash can never stick.
            if (_hitFlash > 0f) _hitFlash -= Time.deltaTime * 5f;
            if (Hold || _sr == null) return;

            // ---- colour (priority compose) ----
            _sr.color = Compose();

            // ---- squash/stretch (multiplicative over the true scale) ----
            _squash = Vector2.Lerp(_squash, _squashTarget, 1f - Mathf.Exp(-9f * Time.deltaTime));
            transform.localScale = new Vector3(_baseScale.x * _squash.x, _baseScale.y * _squash.y, 1f);

            // ---- facing: flip via the renderer (never the scale — that would
            // invert colliders) plus the subtle lean that sells direction, and a
            // dizzy wobble on top while stunned (seeing stars) ----
            _sr.flipX = _artFacesLeft ? (_faceDir > 0f) : (_faceDir < 0f);
            float wobble = _stun ? Mathf.Sin(Time.time * 9f) * 7f : 0f;
            transform.localRotation = Quaternion.Euler(0f, 0f, -_faceDir * 6f + wobble);
        }
    }
}
