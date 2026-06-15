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
            transform.position += Vector3.right * (_dir * 18f * Time.deltaTime);
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
}
