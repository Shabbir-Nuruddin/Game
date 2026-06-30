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
        bool _grounded, _wasGrounded;
        float _inputX;
        bool _frozen;
        float _baseX = 1f, _baseY = 1f, _facing = 1f, _animTimer;
        float _reverseTimer;
        float _fireCd;
        public bool canShoot = false;            // on only in boss arenas
        public int ammo = 0;                     // boss arenas: shots left in the held weapon
        public void GiveAmmo(int n) { ammo = Mathf.Max(ammo, n); }

        // ---- skin-granted traits/abilities (set by GameRoot from the equipped skin) ----
        public float moveMul = 1f, jumpMul = 1f;
        public bool dashEnabled = false;
        public int extraAirJumps = 0;            // double-jump etc.
        public float dashSpeed = 19f, dashDur = 0.16f, dashCooldown = 0.85f;
        float _dashCdLeft, _dashLeft, _dashDir;
        int _airJumpsLeft;
        bool _isDashing;

        // Visible boss-arena blaster (a small procedural "stake-launcher") parented to
        // the player ROOT so it never inherits the body's squash/stretch. Shown only
        // while armed (canShoot) and not in bat form.
        Transform _gunRoot;
        const float GunReach = 0.7f;   // muzzle distance in front of the player centre

        public void SetReversed(float duration) { _reverseTimer = duration; }

        // Read by GameRoot for section-checkpoints + reactive-trap tracking.
        public bool IsGrounded => _grounded;

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
        // Hand control back (used after the cinematic boss intro).
        public void Unfreeze() { _frozen = false; _rb.simulated = true; }

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
            Vector3 muzzle = transform.position + new Vector3(_facing * GunReach, -0.04f, 0f);
            var go = Theme.Box("Bullet", null, muzzle, new Vector2(0.4f, 0.18f), Theme.Danger, 7);
            go.AddComponent<Bullet>().Init(_facing);
            // Small muzzle spark — no glow ring (that read as a yellow circle).
            Fx.Burst(muzzle, new Color(1f, 0.82f, 0.32f, 1f), 5, 4.5f, 0.1f, 0.12f, 0f);
            Audio.PlayOr("shoot", "jump", 0.5f);
        }

        // Build the procedural blaster once (dark body + barrel + grip + glowing red
        // muzzle), parented to the player root and initially hidden.
        void EnsureGun()
        {
            if (_gunRoot != null) return;
            var root = new GameObject("Blaster");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            _gunRoot = root.transform;
            GunPart("Body",   new Vector3(0.12f, -0.02f, 0f), new Vector2(0.34f, 0.22f), Theme.Hex("2A2630"), 6);
            GunPart("Barrel", new Vector3(0.46f,  0.00f, 0f), new Vector2(0.44f, 0.12f), Theme.Hex("4A4450"), 6);
            GunPart("Grip",   new Vector3(0.02f, -0.16f, 0f), new Vector2(0.14f, 0.18f), Theme.Hex("1F1B26"), 6);
            GunPart("Muzzle", new Vector3(0.66f,  0.00f, 0f), new Vector2(0.10f, 0.14f), Theme.Danger, 7);
            root.SetActive(false);
        }

        void GunPart(string name, Vector3 local, Vector2 size, Color col, int order)
        {
            var go = Theme.Box(name, _gunRoot, Vector2.zero, size, col, order);
            go.transform.localPosition = local;
        }

        void Update()
        {
            if (_frozen) return;

            _inputX = 0f;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) _inputX -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) _inputX += 1f;

            // Jump: the rebindable jump key, plus the fixed up-keys (W / ↑).
            if (Input.GetKeyDown(Controls.Jump) || Input.GetKeyDown(KeyCode.UpArrow) ||
                Input.GetKeyDown(KeyCode.W))
                _buffer = jumpBuffer;

            if (Input.GetKeyDown(KeyCode.R)) GameRoot.I?.Die("Do-over!");

            // Vampire DASH (skin ability): a quick mist-burst in the facing direction.
            _dashCdLeft -= Time.deltaTime;
            if (dashEnabled && _dashCdLeft <= 0f && _dashLeft <= 0f &&
                (Input.GetKeyDown(Controls.Dash) || TouchInput.ConsumeDash()))
            {
                _dashLeft = dashDur; _dashDir = _facing; _dashCdLeft = dashCooldown;
                Audio.PlayOr("dash", "jump", 0.5f);
            }
            if (_dashLeft > 0f) _dashLeft -= Time.deltaTime;
            _isDashing = _dashLeft > 0f;

            // Blaster — only armed in boss arenas AND only while you hold a weapon you
            // collected (ammo > 0). Run out and you must dodge to the next pickup.
            _fireCd -= Time.deltaTime;
            bool wantFire = Input.GetKeyDown(Controls.Shoot) || TouchInput.ConsumeFire();
            if (canShoot && _fireCd <= 0f && ammo > 0 && wantFire)
            {
                Fire(); _fireCd = 0.3f;
                if (--ammo <= 0) GameRoot.I?.OnGunEmpty();   // clip spent → trigger a new pickup
                GameRoot.I?.RefreshHud();
            }

            // On-screen touch controls (phone): override/add to keyboard.
            if (TouchInput.X != 0f) _inputX = TouchInput.X;
            if (TouchInput.ConsumeJump()) _buffer = jumpBuffer;

            // Bat flight: hold the glide key (or the on-screen FLY) to glide; drains meter.
            bool flyHeld = canFly && (Input.GetKey(Controls.Fly) || TouchInput.FlyHeld);
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

            // Show/aim the visible blaster only while you actually HOLD a weapon
            // (collected ammo) in a boss arena and you're on foot.
            EnsureGun();
            if (_gunRoot != null)
            {
                bool showGun = canShoot && ammo > 0 && !_flying && !_frozen;
                if (_gunRoot.gameObject.activeSelf != showGun) _gunRoot.gameObject.SetActive(showGun);
                if (showGun) _gunRoot.localScale = new Vector3(_facing, 1f, 1f);   // flip to face
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

            // Landing dust on a real impact (juice).
            if (_grounded && !_wasGrounded && _rb.linearVelocity.y < -3f)
                Fx.Dust(transform.position + Vector3.down * 0.4f);
            _wasGrounded = _grounded;

            var v = _rb.linearVelocity;
            v.x = _inputX * moveSpeed * moveMul;

            if (_grounded) _airJumpsLeft = extraAirJumps;   // refill double-jumps on landing

            // A dash overrides horizontal movement with a flat mist-burst.
            if (_isDashing) { v.x = _dashDir * dashSpeed; v.y = 0f; }

            if (_buffer > 0f && _coyote > 0f)
            {
                v.y = jumpSpeed * jumpMul;
                _buffer = 0f; _coyote = 0f;
                Audio.Play("jump", 0.5f);
                Fx.Dust(transform.position + Vector3.down * 0.4f);
            }
            else if (_buffer > 0f && !_grounded && _coyote <= 0f && _airJumpsLeft > 0)
            {
                v.y = jumpSpeed * jumpMul;                   // mid-air (double) jump
                _buffer = 0f; _airJumpsLeft--;
                Audio.Play("jump", 0.6f);
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
