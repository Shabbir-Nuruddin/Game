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
    ///
    /// 2.5D mode: instead of faking depth by shifting layers, each layer is placed
    /// at the REAL depth that produces the same apparent scroll rate under the
    /// perspective camera — a layer at depth z scrolls D/(D+z) of the gameplay
    /// plane, so z = D·follow/(1−follow) (capped). It's repositioned/rescaled about
    /// the boot camera ray so the boot framing matches, then the projection does
    /// all the work (and gets the vertical parallax right for free).
    /// </summary>
    public class Parallax : MonoBehaviour
    {
        struct Layer
        {
            public Transform t; public float follow;
            public float baseX; public Vector3 basePos; public Vector3 baseScale;
        }
        readonly List<Layer> _layers = new();
        Transform _cam;
        float _camStartX, _camStartY;
        bool _depth;

        public void Init(Transform cam)
        {
            _cam = cam;
            _camStartX = cam.position.x;
            _camStartY = cam.position.y;
        }

        public void Add(Transform t, float follow)
        {
            _layers.Add(new Layer
            {
                t = t, follow = follow, baseX = t.position.x,
                basePos = t.position, baseScale = t.localScale,
            });
            if (_depth) PlaceAtDepth(_layers[_layers.Count - 1]);
        }

        /// <summary>Switch between flat follow-shifting and real perspective depths.</summary>
        public void SetDepthMode(bool depth, float camDistance)
        {
            _depth = depth;
            _dist = camDistance;
            foreach (var l in _layers)
            {
                if (depth) PlaceAtDepth(l);
                else { l.t.position = l.basePos; l.t.localScale = l.baseScale; }
            }
        }

        float _dist = 15.4f;

        void PlaceAtDepth(Layer l)
        {
            // Apparent-scroll equivalence (see class comment), capped so follow≈0.97
            // doesn't fly off to z≈500; k rescales about the boot camera ray so the
            // layer covers the same screen area it did flat.
            float z = Mathf.Min(120f, _dist * l.follow / Mathf.Max(0.03f, 1f - l.follow));
            float k = (_dist + z) / _dist;
            l.t.position = new Vector3(
                _camStartX + (l.basePos.x - _camStartX) * k,
                _camStartY + (l.basePos.y - _camStartY) * k,
                z);
            l.t.localScale = l.baseScale * k;
        }

        void LateUpdate()
        {
            if (_depth || _cam == null) return;   // perspective does the parallax in 2.5D
            float dx = _cam.position.x - _camStartX;
            foreach (var l in _layers)
            {
                var p = l.t.position;
                l.t.position = new Vector3(l.baseX + dx * l.follow, p.y, p.z);
            }
        }
    }
}
