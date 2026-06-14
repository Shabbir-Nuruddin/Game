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

        Rigidbody2D _rb;
        BoxCollider2D _col;
        Transform _visual;       // child we squash/stretch (so physics box is stable)
        float _coyote, _buffer;
        bool _grounded;
        float _inputX;
        bool _frozen;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<BoxCollider2D>();
            _rb.freezeRotation = true;
            _rb.gravityScale = fallGravity;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
        }

        public void Freeze() { _frozen = true; _rb.linearVelocity = Vector2.zero; _rb.simulated = false; }

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

            _buffer -= Time.deltaTime;
            _coyote -= Time.deltaTime;

            // Face the way we move + squash/stretch from vertical speed.
            if (Mathf.Abs(_inputX) > 0.01f)
                _visual.localScale = new Vector3(Mathf.Sign(_inputX) * Mathf.Abs(_visual.localScale.x),
                    _visual.localScale.y, 1f);
            float vy = _rb.linearVelocity.y;
            float stretch = Mathf.Clamp(vy * 0.02f, -0.18f, 0.25f);
            float sx = Mathf.Sign(_visual.localScale.x);
            _visual.localScale = Vector3.Lerp(_visual.localScale,
                new Vector3(sx * (1f - stretch), 1f + stretch, 1f), 12f * Time.deltaTime);
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
            }

            _rb.linearVelocity = v;
            _rb.gravityScale = _rb.linearVelocity.y > 0.1f ? riseGravity : fallGravity;
        }
    }
}
