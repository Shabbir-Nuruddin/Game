using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Multi-layer parallax backdrop. Each layer follows the camera by a fraction
    /// (follow≈1 = very far / barely scrolls; follow≈0 = near / scrolls fully),
    /// which sells depth as the camera tracks the player. Layers are wide enough
    /// (overscan) that the small relative drift never reveals an edge on the short
    /// curated levels.
    /// </summary>
    public class Parallax : MonoBehaviour
    {
        struct Layer { public Transform t; public float follow; public float baseX; }
        readonly List<Layer> _layers = new();
        Transform _cam;
        float _camStartX;

        public void Init(Transform cam)
        {
            _cam = cam;
            _camStartX = cam.position.x;
        }

        public void Add(Transform t, float follow)
        {
            _layers.Add(new Layer { t = t, follow = follow, baseX = t.position.x });
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            float dx = _cam.position.x - _camStartX;
            foreach (var l in _layers)
            {
                var p = l.t.position;
                l.t.position = new Vector3(l.baseX + dx * l.follow, p.y, p.z);
            }
        }
    }
}
