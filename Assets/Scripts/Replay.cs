using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Plays back a recorded path as a translucent "ghost" so every retry is a race
    /// against your PREVIOUS attempt on the same floor. Seeing yourself pull ahead
    /// (or watching where you died last time slide past) is pure near-miss dopamine
    /// — the cheapest, strongest "one more try" hook. Driven by the same realtime
    /// clock the attempt uses, so it stays in sync.
    /// When the recording runs out (you reached where you finished/died last time),
    /// the ghost fades out and removes itself — otherwise a frozen grey vampire just
    /// stands there for the rest of the run, which reads as a glitch.
    /// </summary>
    public class GhostReplay : MonoBehaviour
    {
        float[] _t; Vector3[] _p; float _start; int _i;
        SpriteRenderer _sr; float _baseAlpha; float _fadeStart = -1f;
        const float FadeDur = 0.6f;

        public void Init(float[] times, Vector3[] points)
        {
            _t = times; _p = points; _start = Time.realtimeSinceStartup; _i = 0;
            _sr = GetComponent<SpriteRenderer>();
            _baseAlpha = _sr != null ? _sr.color.a : 1f;
        }

        void Update()
        {
            if (_t == null || _t.Length < 2) return;
            float e = Time.realtimeSinceStartup - _start;
            while (_i < _t.Length - 1 && _t[_i + 1] <= e) _i++;

            if (_i >= _t.Length - 1)
            {
                // End of the recorded run — park at the final spot, then fade away
                // instead of lingering as a frozen silhouette.
                transform.position = _p[_p.Length - 1];
                if (_fadeStart < 0f) _fadeStart = Time.realtimeSinceStartup;
                float k = (Time.realtimeSinceStartup - _fadeStart) / FadeDur;
                if (_sr != null)
                {
                    var c = _sr.color; c.a = Mathf.Lerp(_baseAlpha, 0f, k); _sr.color = c;
                }
                if (k >= 1f) Destroy(gameObject);
                return;
            }

            float span = Mathf.Max(0.0001f, _t[_i + 1] - _t[_i]);
            float frac = Mathf.Clamp01((e - _t[_i]) / span);
            transform.position = Vector3.Lerp(_p[_i], _p[_i + 1], frac);
        }
    }
}
