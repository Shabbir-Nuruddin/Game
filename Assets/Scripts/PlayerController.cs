using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Beanie's movement. The whole point of a rage platformer is that the
    /// CONTROLS feel great so every death feels like the trap's fault, not the
    /// controller's. So this has the standard "good feel" tricks: coyote time
    /// (you can still jump just after leaving a ledge), jump buffering (a jump
    /// pressed just before landing still fires), and stronger gravity on the way
    /// down for a snappy arc. Plus squash & stretch for personality.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class PlayerController : MonoBehaviour
    {
        public float moveSpeed = 7.5f;
        public float jumpSpeed = 14f;
        public float fallGravity = 5.5f;
        public float riseGravity = 3.4f;
        public float coyoteTime = 0.10f;
        public float jumpBuffer = 0.10f;

        // Optional sprite animation (set by GameRoot if art is present).
        public SpriteRenderer bodyRenderer;
        public Sprite idleSprite, walkSprite, jumpSprite;
        public Sprite[] idleFrames, runFrames;   // multi-frame animation (preferred)
        public Sprite[] deathFrames;             // played once on death (optional)
        public Sprite batSprite;                 // shown while flying (bat form)

        // Bat glide: only works in the AIR (jump first), drains fast, refills slowly.
        // It's a short hop to extend a jump / cross one gap — NOT a fly-over-the-level.
        public float flightMeter = 1f;           // 0..1, read by the HUD
        public float glideFall = 2.2f, flyDrain = 0.95f, flyRefill = 0.45f;
        public bool canFly = true;               // off in The Castle (pure precision mode)
        bool _flying;

        Rigidbody2D _rb;
        BoxCollider2D _col;
        Transform _visual;       // child we squash/stretch (so physics box is stable)
        float _coyote, _buffer;
        bool _grounded;
        float _inputX;
        bool _frozen;
        float _baseX = 1f, _baseY = 1f, _facing = 1f, _animTimer;
        float _reverseTimer;

        public void SetReversed(float duration) { _reverseTimer = duration; }

        // Which way the character is visually facing (+1 right, -1 left). Read by
        // the netcode so a remote ghost mirrors the real player's facing.
        public float Facing => _facing;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<BoxCollider2D>();
            _rb.freezeRotation = true;
            _rb.gravityScale = fallGravity;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            _baseX = Mathf.Abs(_visual.localScale.x);
            _baseY = _visual.localScale.y;
        }

        public void Freeze() { _frozen = true; _rb.linearVelocity = Vector2.zero; _rb.simulated = false; }

        // Plays the death frames once (called by GameRoot on death). Runs as a
        // coroutine so it works even though the controller is frozen.
        public void PlayDeath()
        {
            if (deathFrames != null && deathFrames.Length > 0 && bodyRenderer != null)
                StartCoroutine(DeathAnim());
        }

        System.Collections.IEnumerator DeathAnim()
        {
            foreach (var f in deathFrames)
            {
                bodyRenderer.sprite = f;
                yield return new WaitForSecondsRealtime(0.04f);
            }
        }

        void Fire()
        {
            if (_frozen) return;
            var go = Theme.Box("Bullet", null,
                transform.position + Vector3.right * (_facing * 0.6f),
                new Vector2(0.34f, 0.16f), Theme.Player, 7);
            go.AddComponent<Bullet>().Init(_facing);
            Audio.Play("jump", 0.25f);
        }

        void Update()
        {
            if (_frozen) return;

            _inputX = 0f;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) _inputX -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) _inputX += 1f;

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow) ||
                Input.GetKeyDown(KeyCode.W))
                _buffer = jumpBuffer;

            if (Input.GetKeyDown(KeyCode.R)) GameRoot.I?.Die("Do-over!");

            // On-screen touch controls (phone): override/add to keyboard.
            if (TouchInput.X != 0f) _inputX = TouchInput.X;
            if (TouchInput.ConsumeJump()) _buffer = jumpBuffer;

            // Bat flight: hold Shift (or the on-screen FLY) to glide; drains meter.
            bool flyHeld = canFly && (Input.GetKey(KeyCode.LeftShift) || TouchInput.FlyHeld);
            _flying = flyHeld && flightMeter > 0f && !_grounded; // must be airborne (no ground hover)
            if (_flying) flightMeter = Mathf.Max(0f, flightMeter - flyDrain * Time.deltaTime);
            else if (_grounded) flightMeter = Mathf.Min(1f, flightMeter + flyRefill * Time.deltaTime);

            // Reverse-controls troll: flip horizontal input for a few seconds.
            if (_reverseTimer > 0f) { _reverseTimer -= Time.deltaTime; _inputX = -_inputX; }

            _buffer -= Time.deltaTime;
            _coyote -= Time.deltaTime;

            // Face the way we move + squash/stretch from vertical speed. The
            // squash is relative to the visual's BASE scale, so it works whether
            // the body is a 1-unit box or a sprite scaled up to character size.
            if (_inputX > 0.01f) _facing = 1f;
            else if (_inputX < -0.01f) _facing = -1f;
            float vy = _rb.linearVelocity.y;
            // Bat form is a SEPARATE sprite with its own bounds, so it must be
            // scaled to itself (~0.6u tall) — NOT left at the vampire's base scale,
            // which made it a giant blob. Snap to bat scale while flying.
            if (_flying && batSprite != null)
            {
                float bh = batSprite.bounds.size.y;
                float s = bh > 0.0001f ? 0.6f / bh : _baseY;
                _visual.localScale = new Vector3(_facing * s, s, 1f);
            }
            else
            {
                // Squash/stretch ONLY in the air. On the ground the body sits at its
                // base scale, so standing still doesn't wobble.
                float stretch = _grounded ? 0f : Mathf.Clamp(vy * 0.02f, -0.18f, 0.25f);
                var target = new Vector3(_facing * _baseX * (1f - stretch), _baseY * (1f + stretch), 1f);
                _visual.localScale = Vector3.Lerp(_visual.localScale, target, 14f * Time.deltaTime);
            }

            // Animation: jump pose in the air, run cycle while moving, and a single
            // STILL frame when standing (no idle fidgeting).
            bool moving = Mathf.Abs(_inputX) > 0.01f;
            if (bodyRenderer != null)
            {
                if (_flying && batSprite != null)
                    bodyRenderer.sprite = batSprite;          // bat form
                else if (!_grounded && jumpSprite != null)
                    bodyRenderer.sprite = jumpSprite;
                else if (moving && runFrames != null && runFrames.Length > 0)
                {
                    _animTimer += Time.deltaTime;
                    bodyRenderer.sprite = runFrames[Mathf.FloorToInt(_animTimer * 14f) % runFrames.Length];
                }
                else if (idleFrames != null && idleFrames.Length > 0)
                {
                    // Gentle idle cycle (slow) so standing still has a little life.
                    _animTimer += Time.deltaTime;
                    bodyRenderer.sprite = idleFrames[Mathf.FloorToInt(_animTimer * 6f) % idleFrames.Length];
                }
                else if (idleSprite != null) // single-frame fallback
                {
                    if (moving)
                    {
                        _animTimer += Time.deltaTime;
                        bodyRenderer.sprite = (Mathf.FloorToInt(_animTimer * 10f) % 2 == 0)
                            ? idleSprite : (walkSprite != null ? walkSprite : idleSprite);
                    }
                    else bodyRenderer.sprite = idleSprite;
                }
            }
        }

        void FixedUpdate()
        {
            if (_frozen) return;

            // Ground check: cast our own collider straight down a hair.
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Physics2D.AllLayers);
            var hits = new RaycastHit2D[4];
            int n = _col.Cast(Vector2.down, filter, hits, 0.08f);
            _grounded = false;
            for (int i = 0; i < n; i++)
                if (hits[i].collider != null && hits[i].normal.y > 0.5f) { _grounded = true; break; }
            if (_grounded) _coyote = coyoteTime;

            var v = _rb.linearVelocity;
            v.x = _inputX * moveSpeed;

            if (_buffer > 0f && _coyote > 0f)
            {
                v.y = jumpSpeed;
                _buffer = 0f; _coyote = 0f;
                Audio.Play("jump", 0.5f);
            }

            // Bat form GLIDES: it slows the fall to a gentle descent but cannot
            // truly climb, so flight extends a jump / crosses a gap — it can't be
            // used to fly up and over the whole level. (Was a free upward thrust,
            // which let players cheese past every ground hazard.)
            if (_flying && v.y < -glideFall) v.y = -glideFall;

            _rb.linearVelocity = v;
            _rb.gravityScale = _rb.linearVelocity.y > 0.1f ? riseGravity : fallGravity;
        }
    }
}
