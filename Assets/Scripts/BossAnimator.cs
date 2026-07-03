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
        }

        // ---- channels ----
        public void HitFlash()            { _hitFlash = 1f; }
        public void Impact()              { _squash = new Vector2(1.06f, 0.94f); }                    // quick pop when shot
        public void Anticipate()          { _squashTarget = new Vector2(0.92f, 1.10f); }              // crouch before an attack
        public void Lunge()               { _squash = new Vector2(1.18f, 0.85f); _squashTarget = Vector2.one; } // snap at attack release
        public void BeginTelegraph(Color pulse) { _telegraphing = true; _teleColor = pulse; Anticipate(); }
        public void EndTelegraph()        { _telegraphing = false; _squashTarget = Vector2.one; }
        public void SetVulnerable(bool v) { _vulnerable = v; }
        public void SetRim(Color rim, float amount) { _rim = rim; _rimAmt = Mathf.Clamp01(amount); }
        public void SetFacing(float dir)  { if (Mathf.Abs(dir) > 0.01f) _faceDir = Mathf.Sign(dir); }

        // Called when a pattern is interrupted mid-flight (phase escalation): drop
        // every transient channel so nothing is left mid-pulse or mid-crouch.
        public void ResetChannels()
        {
            _telegraphing = false;
            _vulnerable = false;
            _squashTarget = Vector2.one;
        }

        void LateUpdate()
        {
            // Decay runs even while held/telegraphing so a flash can never stick.
            if (_hitFlash > 0f) _hitFlash -= Time.deltaTime * 5f;
            if (Hold || _sr == null) return;

            // ---- colour (priority compose) ----
            Color baseEff = _rimAmt > 0f
                ? Color.Lerp(_base, _rim, _rimAmt * (0.7f + 0.3f * Mathf.Sin(Time.time * 3f)))
                : _base;
            Color c;
            if (_hitFlash > 0f)      c = Color.Lerp(baseEff, Color.white, Mathf.Clamp01(_hitFlash));
            else if (_telegraphing)  c = Color.Lerp(baseEff, _teleColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 32f));
            else if (_vulnerable)    c = Color.Lerp(baseEff, Color.Lerp(baseEff, Color.white, 0.4f), 0.5f + 0.5f * Mathf.Sin(Time.time * 6f));
            else                     c = baseEff;
            _sr.color = c;

            // ---- squash/stretch (multiplicative over the true scale) ----
            _squash = Vector2.Lerp(_squash, _squashTarget, 1f - Mathf.Exp(-9f * Time.deltaTime));
            transform.localScale = new Vector3(_baseScale.x * _squash.x, _baseScale.y * _squash.y, 1f);

            // ---- facing: flip via the renderer (never the scale — that would
            // invert colliders) plus the subtle lean that sells direction ----
            _sr.flipX = _artFacesLeft ? (_faceDir > 0f) : (_faceDir < 0f);
            transform.localRotation = Quaternion.Euler(0f, 0f, -_faceDir * 6f);
        }
    }
}
