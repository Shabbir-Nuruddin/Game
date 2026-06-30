using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Lightweight, asset-free game-feel FX: particle bursts, landing dust, and
    /// expanding telegraph/impact rings. Everything is built from the unit sprite /
    /// glow disc and self-destructs, parented to a DontDestroyOnLoad root so effects
    /// survive the level rebuild on death. Call the statics from anywhere.
    /// </summary>
    public static class Fx
    {
        static Transform _root;
        static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("FX");
                    Object.DontDestroyOnLoad(go);
                    _root = go.transform;
                }
                return _root;
            }
        }

        /// <summary>A spray of small coloured bits with gravity + fade.</summary>
        public static void Burst(Vector3 pos, Color col, int count = 10, float speed = 6f,
            float size = 0.16f, float life = 0.5f, float gravity = 18f)
        {
            for (int i = 0; i < count; i++)
            {
                var go = Theme.Box("FxBit", Root, pos, new Vector2(size, size), col, 8);
                float a = Random.Range(0f, Mathf.PI * 2f);
                var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (speed * Random.Range(0.4f, 1f));
                go.AddComponent<FxBit>().Init(v, life, gravity);
            }
        }

        /// <summary>A soft dust puff (landing / dashing).</summary>
        public static void Dust(Vector3 pos)
            => Burst(pos + Vector3.down * 0.4f, new Color(0.72f, 0.72f, 0.78f, 0.8f),
                     5, 2.5f, 0.16f, 0.35f, 4f);

        /// <summary>An expanding glow ring — telegraphs / impacts.</summary>
        public static void Ring(Vector3 pos, Color col, float maxScale = 2f, float dur = 0.35f)
        {
            var sp = Theme.Moon;
            if (sp == null) return;
            var go = Theme.SpriteBox("FxRing", Root, pos, new Vector2(0.4f, 0.4f), sp, 8);
            go.GetComponent<SpriteRenderer>().color = col;
            go.AddComponent<FxRing>().Init(maxScale, dur);
        }

        /// <summary>
        /// Attaches a soft glow halo as a CHILD of `host` so it travels with it
        /// (e.g. a bolt or a bat). Sized in world units regardless of the host's own
        /// scale, optionally pulsing. Returns the glow GameObject (no collider).
        /// Used to make fast projectiles unmistakable against the dark night sky.
        /// </summary>
        public static GameObject Glow(GameObject host, Color col, float worldSize, int order = 5, bool pulse = true)
        {
            var sp = Theme.Moon;
            if (sp == null || host == null) return null;
            var go = new GameObject("Glow");
            go.transform.SetParent(host.transform, false);
            go.transform.localPosition = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp; sr.color = col; sr.sortingOrder = order;
            // Compensate for the host's (possibly non-uniform) world scale so the halo
            // is `worldSize` units across no matter how the host sprite was fitted.
            var m = sp.bounds.size; var L = host.transform.lossyScale;
            float ax = Mathf.Abs(L.x), ay = Mathf.Abs(L.y);
            go.transform.localScale = new Vector3(
                (m.x > 0.0001f && ax > 0.0001f) ? worldSize / (m.x * ax) : 1f,
                (m.y > 0.0001f && ay > 0.0001f) ? worldSize / (m.y * ay) : 1f, 1f);
            if (pulse)
            {
                var fp = go.AddComponent<FaintPulse>();
                fp.min = Mathf.Clamp01(col.a * 0.55f); fp.max = Mathf.Clamp01(col.a); fp.speed = 9f;
            }
            return go;
        }

        /// <summary>A one-shot animated sprite explosion (falls back to a burst).</summary>
        public static void Explosion(Vector3 pos, float size = 2f, float fps = 28f)
        {
            var frames = Assets.Sheet("explosion", 64);
            if (frames == null || frames.Length == 0)
            { Burst(pos, new Color(1f, 0.6f, 0.2f, 1f), 16, 7f, 0.2f, 0.5f, 8f); Ring(pos, new Color(1f, 0.7f, 0.3f, 0.7f), size * 1.4f, 0.35f); return; }
            var go = Theme.SpriteBox("FxExplosion", Root, pos, new Vector2(size, size), frames[0], 9);
            go.AddComponent<FxAnim>().Init(frames, fps);
        }
    }

    /// <summary>Loops a horizontal sprite-sheet forever (torches, flames, decor).</summary>
    public class LoopAnim : MonoBehaviour
    {
        Sprite[] _f; float _fps, _t; SpriteRenderer _sr;
        public void Init(Sprite[] frames, float fps) { _f = frames; _fps = fps; _sr = GetComponent<SpriteRenderer>(); _t = Random.Range(0f, 1f); }
        void Update()
        {
            if (_f == null || _f.Length == 0 || _sr == null) return;
            _t += Time.deltaTime;
            _sr.sprite = _f[Mathf.FloorToInt(_t * _fps) % _f.Length];
        }
    }

    /// <summary>Plays a horizontal sprite-sheet ONCE, then self-destructs.</summary>
    public class FxAnim : MonoBehaviour
    {
        Sprite[] _f; float _fps, _t; SpriteRenderer _sr;
        public void Init(Sprite[] frames, float fps) { _f = frames; _fps = fps; _sr = GetComponent<SpriteRenderer>(); }
        void Update()
        {
            if (_f == null || _f.Length == 0) { Destroy(gameObject); return; }
            _t += Time.deltaTime;
            int i = Mathf.FloorToInt(_t * _fps);
            if (i >= _f.Length) { Destroy(gameObject); return; }
            if (_sr != null) _sr.sprite = _f[i];
        }
    }

    public class FxBit : MonoBehaviour
    {
        Vector2 _v; float _life, _t, _grav; SpriteRenderer _sr; Color _c;
        public void Init(Vector2 v, float life, float grav)
        { _v = v; _life = life; _grav = grav; _sr = GetComponent<SpriteRenderer>(); _c = _sr != null ? _sr.color : Color.white; }
        void Update()
        {
            _t += Time.deltaTime;
            _v.y -= _grav * Time.deltaTime;
            transform.position += (Vector3)(_v * Time.deltaTime);
            if (_sr != null) { var c = _c; c.a = Mathf.Lerp(_c.a, 0f, _t / _life); _sr.color = c; }
            if (_t >= _life) Destroy(gameObject);
        }
    }

    public class FxRing : MonoBehaviour
    {
        float _max, _dur, _t, _base; SpriteRenderer _sr; Color _c;
        public void Init(float max, float dur)
        { _max = max; _dur = dur; _sr = GetComponent<SpriteRenderer>(); _c = _sr != null ? _sr.color : Color.white; _base = transform.localScale.x; }
        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _dur);
            transform.localScale = Vector3.one * Mathf.Lerp(_base, _max, k);
            if (_sr != null) { var c = _c; c.a = Mathf.Lerp(_c.a, 0f, k); _sr.color = c; }
            if (_t >= _dur) Destroy(gameObject);
        }
    }

    /// <summary>
    /// A single drifting ambient mote (ember/dust speck) in the backdrop. Slowly
    /// floats and twinkles, wrapping around a band so the field never empties. Its
    /// base colour is re-themed per mode/world via Recolor.
    /// </summary>
    public class Mote : MonoBehaviour
    {
        Vector3 _vel; SpriteRenderer _sr; Color _base; float _phase;
        public void Init(Vector3 vel, Color col)
        {
            _vel = vel; _sr = GetComponent<SpriteRenderer>(); _base = col;
            _phase = Random.value * 6.28f;
            if (_sr != null) _sr.color = col;
        }
        public void Recolor(Color col) { _base = col; }
        void Update()
        {
            var p = transform.localPosition + _vel * Time.deltaTime;
            if (p.y > 11.5f) p.y = -11.5f;
            if (p.x > 17.5f) p.x = -17.5f; else if (p.x < -17.5f) p.x = 17.5f;
            transform.localPosition = p;
            if (_sr != null)
            {
                var c = _base;
                c.a = _base.a * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.time * 1.4f + _phase)));
                _sr.color = c;
            }
        }
    }
}
