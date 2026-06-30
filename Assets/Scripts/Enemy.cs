using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// A bat that hovers HIGH (out of reach) until the player is near, then dives
    /// at where the player is — with a clear telegraph (wing-flare + screech) and a
    /// dodge window, so a death always feels earned. Lethal on contact via KillZone;
    /// after a dive it returns to its perch and waits to swoop again.
    /// Placed by the level builder as TrapType.BatSwoop and built by GameRoot.
    /// </summary>
    public class BatEnemy : MonoBehaviour
    {
        Sprite[] _fly;
        SpriteRenderer _sr;
        Vector3 _home;
        float _animTimer, _timer, _baseScale = 1f;
        Vector3 _diveTo;

        // Attacking bats are tinted a prominent blood-red so they stand out clearly
        // against the gloom (and read as a threat, not decoration).
        static readonly Color BatRed = new Color(1f, 0.22f, 0.22f, 1f);

        enum S { Hover, Telegraph, Dive, Return }
        S _state = S.Hover;

        const float Range = 7.5f;      // only swoops when the player is this close
        const float TeleTime = 0.55f;  // wind-up before the dive (the dodge window) — longer so it's readable
        const float DiveSpeed = 11f;   // slower dive = escapable on reaction
        const float ReturnSpeed = 6f;

        public void Init(Sprite[] fly) { _fly = fly; }

        void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr != null) { _baseScale = Mathf.Abs(transform.localScale.y); _sr.color = BatRed; }
            _home = transform.position;
            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true; col.size = Vector2.one * 0.7f;
            var kz = gameObject.AddComponent<KillZone>(); kz.msg = "Screeched at by a bat."; kz.trapTag = (int)TrapType.BatSwoop;
            _timer = Random.Range(1.2f, 2.8f);  // initial perch before the first swoop
        }

        void Update()
        {
            // Wing-flap animation (if a sheet was supplied) + facing.
            if (_fly != null && _fly.Length > 0 && _sr != null)
            {
                _animTimer += Time.deltaTime;
                _sr.sprite = _fly[Mathf.FloorToInt(_animTimer * 12f) % _fly.Length];
            }

            Transform pl = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;

            switch (_state)
            {
                case S.Hover:
                    transform.position = _home + new Vector3(0f, Mathf.Sin(Time.time * 2f) * 0.2f, 0f);
                    _timer -= Time.deltaTime;
                    if (_timer <= 0f && pl != null && Mathf.Abs(pl.position.x - _home.x) < Range)
                    { _state = S.Telegraph; _timer = TeleTime; FaceToward(pl.position.x); Audio.Play("screech", 0.4f); }
                    break;

                case S.Telegraph:
                    // Flare: puff up and flash red so the dive is unmistakable.
                    _timer -= Time.deltaTime;
                    float flare = 1f + (1f - _timer / TeleTime) * 0.5f;
                    SetScaleY(flare);
                    // Pulse to a hot, bright red so the wind-up still reads against the
                    // already-red bat.
                    if (_sr != null) _sr.color = Color.Lerp(BatRed, new Color(1f, 0.85f, 0.6f), 0.5f + 0.5f * Mathf.Sin(Time.time * 30f));
                    if (_timer <= 0f)
                    {
                        // Aim at the player's position NOW, overshoot slightly past them.
                        _diveTo = pl != null ? pl.position : _home + Vector3.down * 4f;
                        _diveTo += (_diveTo - transform.position).normalized * 1.5f;
                        _state = S.Dive;
                    }
                    break;

                case S.Dive:
                    SetScaleY(1f);
                    if (_sr != null) _sr.color = BatRed;
                    transform.position = Vector3.MoveTowards(transform.position, _diveTo, DiveSpeed * Time.deltaTime);
                    if (Vector3.Distance(transform.position, _diveTo) < 0.1f) _state = S.Return;
                    break;

                case S.Return:
                    transform.position = Vector3.MoveTowards(transform.position, _home, ReturnSpeed * Time.deltaTime);
                    if (Vector3.Distance(transform.position, _home) < 0.1f)
                    { _state = S.Hover; _timer = Random.Range(2.0f, 3.5f); }
                    break;
            }
        }

        void FaceToward(float x)
        {
            float dir = Mathf.Sign(x - transform.position.x);
            var s = transform.localScale;
            transform.localScale = new Vector3(Mathf.Abs(s.x) * (dir >= 0 ? 1f : -1f), s.y, s.z);
        }

        void SetScaleY(float mul)
        {
            var s = transform.localScale;
            transform.localScale = new Vector3(s.x, _baseScale * mul, s.z);
        }
    }
}
