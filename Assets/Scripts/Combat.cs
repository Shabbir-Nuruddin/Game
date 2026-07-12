using UnityEngine;

namespace TrustIssues
{
    /// <summary>Marks an object a blaster shot can destroy (candy walls/gates).</summary>
    public class Breakable : MonoBehaviour { }

    /// <summary>A blaster shot: flies straight, pops breakable blocks, then dies.</summary>
    public class Bullet : MonoBehaviour
    {
        float _dir, _life = 1.4f;

        public void Init(float dir)
        {
            _dir = Mathf.Sign(dir);
            var c = gameObject.AddComponent<BoxCollider2D>();
            c.isTrigger = true;
            // Sized close to the 0.4x0.18 visual — this collider only has to hit the
            // chunky candy walls now; the boss uses the swept test in Update instead.
            c.size = new Vector2(0.5f, 0.3f);
            // A kinematic body so trigger events actually fire against the
            // static candy walls (2D triggers need a Rigidbody on one side).
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            // REQUIRED: kinematic bodies don't report triggers vs static colliders
            // (the candy walls) without this — was why Level 5 couldn't be cleared.
            rb.useFullKinematicContacts = true;
        }

        void Update()
        {
            Vector3 prev = transform.position;
            transform.position += Vector3.right * (_dir * 18f * Time.deltaTime);

            // Boss hits use a SWEPT segment test (this frame's travel, relative to the
            // boss so its own dash motion counts too) against the tall hurt column —
            // the old trigger-event path (kinematic bullet vs a static trigger on a
            // transform-animated boss) dropped hits, i.e. "the shot went through it".
            var boss = GameRoot.I != null ? GameRoot.I.ActiveBoss : null;
            if (boss != null && !boss.IntroHold)
            {
                // Decoys soak the shot FIRST (Countess's Mirror Waltz): popping one
                // wastes the bullet and triggers its telegraphed revenge volley.
                if (boss.TryInterceptDecoy(prev, transform.position))
                {
                    Destroy(gameObject);
                    return;
                }
                Vector2 rel0 = (Vector2)(prev - boss.PrevPos);
                Vector2 rel1 = (Vector2)(transform.position - boss.transform.position);
                if (Boss.SegmentVsAabb(rel0, rel1, Vector2.zero,
                                       Boss.BodyHalfW + 0.2f, Boss.HurtHalfH + 0.1f))
                {
                    boss.Hit(2);      // each collected shot bites — a clip is meaningful
                    Destroy(gameObject);
                    return;
                }
            }

            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }

        void OnTriggerEnter2D(Collider2D o)
        {
            if (o.GetComponent<Breakable>() != null)
            {
                Destroy(o.gameObject);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// A weapon pickup that sits in a boss arena. Walk into it to load a fresh clip,
    /// then blast the boss until it's spent — at which point another one appears
    /// elsewhere, forcing the dodge → grab → shoot rhythm. Built by GameRoot.
    /// </summary>
    public class GunPickup : MonoBehaviour
    {
        int _clip;
        public void Init(int clip) { _clip = clip; }

        void OnTriggerEnter2D(Collider2D o)
        {
            var pc = o.GetComponent<PlayerController>();
            if (pc == null) return;
            pc.GiveAmmo(_clip);
            Audio.PlayOr("levelup", "click", 0.6f);
            Fx.Burst(transform.position, new Color(1f, 0.85f, 0.3f, 1f), 12, 6f, 0.16f, 0.4f, 4f);
            Fx.Ring(transform.position, new Color(1f, 0.8f, 0.3f, 0.85f), 1.6f, 0.3f);
            GameRoot.I?.OnGunCollected();
            Destroy(gameObject);
        }
    }

    /// <summary>Gentle vertical bob so a pickup reads as "grab me". Cosmetic.</summary>
    public class Bobber : MonoBehaviour
    {
        Vector3 _home; float _t;
        void Start() { _home = transform.position; _t = Random.value * 6f; }
        void Update()
        {
            _t += Time.deltaTime;
            transform.position = _home + new Vector3(0f, Mathf.Sin(_t * 2.5f) * 0.18f, 0f);
        }
    }
}
