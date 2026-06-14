using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// A teleporter. Walk into it and you're whisked to its linked partner.
    /// A short shared cooldown stops you instantly bouncing back. Portals can be
    /// a genuine shortcut OR a troll (one that drops you onto spikes) — that's a
    /// level-design choice via where the partner sits.
    /// </summary>
    public class Portal : MonoBehaviour
    {
        public Vector3 target;
        static float _cooldownUntil;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponent<PlayerController>() == null) return;
            if (Time.time < _cooldownUntil) return;
            _cooldownUntil = Time.time + 0.45f;
            other.transform.position = target + Vector3.up * 0.25f;
        }
    }
}
