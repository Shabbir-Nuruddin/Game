using System.Collections;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>Crossfades the ten-region Endless Night atlas without hard cuts.</summary>
    public sealed class EndlessThemeBackdrop : MonoBehaviour
    {
        const int Columns = 3;
        const int ThemeCount = 6;
        // Camera-child backdrop plane. At local Z=20 the 40-degree perspective
        // camera sees ~14.6 world units vertically; this overscan covers wide and
        // tall windows without exposing an edge. Orthographic mode is covered too.
        const float WorldWidth = 27.5f;
        const float WorldHeight = 15.2f;
        readonly SpriteRenderer[] _layers = new SpriteRenderer[2];
        Sprite[] _themes;
        Coroutine _fade;
        int _front;
        public int CurrentTheme { get; private set; } = -1;

        public void Init(Transform cameraTransform, Parallax parallax)
        {
            var atlas = Resources.Load<Texture2D>("art/endless_theme_atlas_v2");
            if (atlas == null) return;
            _themes = Slice(atlas);
            var highlands = Resources.Load<Texture2D>("art/forsaken_highlands_v1");
            if (highlands != null)
            {
                _themes[0] = Sprite.Create(highlands,
                    new Rect(0f, 0f, highlands.width, highlands.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _themes[0].name = "ForsakenHighlands";
            }
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("EndlessTheme" + i);
                // Follow the camera directly. These are full-screen paintings, not
                // world parallax sprites: registering them with Parallax caused a
                // later crossfade scale assignment to cancel the perspective scale
                // and shrink the art into a small rectangle.
                go.transform.SetParent(cameraTransform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 20f);
                var sr = go.AddComponent<SpriteRenderer>();
                // In front of every legacy castle/parallax layer (-28..-12), but
                // still safely behind platforms and gameplay. At -13 the fog layer
                // and painted castle could obscure the theme almost completely.
                sr.sortingOrder = -11;
                sr.color = new Color(1f, 1f, 1f, 0f);
                _layers[i] = sr;
                SetSprite(sr, _themes[0]);
            }
        }

        public void Show(int theme, float duration)
        {
            if (_themes == null || theme < 0 || theme >= _themes.Length) return;
            if (theme == CurrentTheme && _layers[_front].color.a > 0.99f) return;
            CurrentTheme = theme;
            int next = 1 - _front;
            SetSprite(_layers[next], _themes[theme]);
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(Crossfade(_front, next, Mathf.Max(0.05f, duration)));
        }

        public void Hide(float duration)
        {
            CurrentTheme = -1;
            if (_themes == null) return;
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeOut(Mathf.Max(0.05f, duration)));
        }

        static Sprite[] Slice(Texture2D atlas)
        {
            var result = new Sprite[ThemeCount];
            float w = atlas.width / (float)Columns, h = atlas.height / 2f;
            for (int i = 0; i < result.Length; i++)
            {
                int col = i % Columns;
                int rowFromBottom = 1 - i / Columns;
                var rect = new Rect(col * w, rowFromBottom * h, w, h);
                result[i] = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect);
                result[i].name = "EndlessTheme_" + i;
            }
            return result;
        }

        static void SetSprite(SpriteRenderer sr, Sprite sprite)
        {
            sr.sprite = sprite;
            Vector2 size = sprite.bounds.size;
            sr.transform.localScale = new Vector3(WorldWidth / size.x, WorldHeight / size.y, 1f);
        }

        IEnumerator Crossfade(int from, int to, float duration)
        {
            float fromAlpha = _layers[from].color.a;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float p = Mathf.SmoothStep(0f, 1f, t / duration);
                Alpha(_layers[from], Mathf.Lerp(fromAlpha, 0f, p));
                Alpha(_layers[to], p);
                yield return null;
            }
            Alpha(_layers[from], 0f);
            Alpha(_layers[to], 1f);
            _front = to;
            _fade = null;
        }

        IEnumerator FadeOut(float duration)
        {
            float a0 = _layers[0].color.a, a1 = _layers[1].color.a;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float p = Mathf.SmoothStep(0f, 1f, t / duration);
                Alpha(_layers[0], Mathf.Lerp(a0, 0f, p));
                Alpha(_layers[1], Mathf.Lerp(a1, 0f, p));
                yield return null;
            }
            Alpha(_layers[0], 0f);
            Alpha(_layers[1], 0f);
            _fade = null;
        }

        static void Alpha(SpriteRenderer sr, float value)
        {
            var c = sr.color;
            c.a = value;
            sr.color = c;
        }
    }
}
