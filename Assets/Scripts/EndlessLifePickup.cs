using UnityEngine;

namespace TrustIssues
{
    /// <summary>Optional risk-route reward in Endless Night.</summary>
    public sealed class EndlessLifePickup : MonoBehaviour
    {
        public Transform visual;
        Vector3 _home;
        bool _taken;

        void Start() => _home = transform.position;

        void Update()
        {
            transform.position = _home + Vector3.up * (Mathf.Sin(Time.time * 3.2f) * 0.12f);
            if (visual != null) visual.Rotate(0f, 0f, 45f * Time.deltaTime);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (_taken || !other.CompareTag("Player")) return;
            _taken = true;
            GameRoot.I?.CollectEndlessLife(gameObject);
            Destroy(gameObject);
        }
    }
}
