using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TrustIssues
{
    /// <summary>
    /// The game's look + the primitive/UI factory. Everything in the game is
    /// built from a single 1x1 white sprite scaled into rectangles, so we need
    /// zero art assets to get a real, playable build. Restyle here in one place.
    /// </summary>
    public static class Theme
    {
        // Working title + mascot name (swappable).
        public const string Title = "TRUST ISSUES";
        public const string Mascot = "Beanie";

        // Palette — "Candy Troll": cute pastel exterior, brutal gameplay.
        public static readonly Color Sky      = Hex("2B2440"); // plum night
        public static readonly Color SkyLow   = Hex("3A3158");
        public static readonly Color Platform = Hex("FFF3E6"); // cream ground
        public static readonly Color PlatEdge = Hex("E6D8C8");
        public static readonly Color Player   = Hex("FF8FB1"); // Beanie pink
        public static readonly Color Danger   = Hex("FF5D73"); // spikes / kill (coral)
        public static readonly Color Trick    = Hex("B388FF"); // fake/portal lavender
        public static readonly Color Coin      = Hex("FFD66B");
        public static readonly Color Exit      = Hex("5BE0A3"); // real exit mint
        public static readonly Color Ink       = Hex("2A2438");
        public static readonly Color Tell      = new Color(0, 0, 0, 0.16f); // subtle hint

        static Sprite _square;
        public static Sprite Square
        {
            get
            {
                if (_square == null)
                {
                    var tex = new Texture2D(8, 8);
                    var px = new Color[64];
                    for (int i = 0; i < 64; i++) px[i] = Color.white;
                    tex.SetPixels(px); tex.Apply();
                    tex.filterMode = FilterMode.Point;
                    // pixelsPerUnit = width => the sprite is exactly 1 world unit.
                    _square = Sprite.Create(tex, new Rect(0, 0, 8, 8),
                        new Vector2(0.5f, 0.5f), 8f);
                }
                return _square;
            }
        }

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }

        static Sprite _grad;
        /// <summary>A soft vertical gradient sprite for backdrops (1 world unit).</summary>
        public static Sprite Gradient(Color top, Color bottom)
        {
            if (_grad != null) return _grad;
            int h = 64;
            var tex = new Texture2D(1, h);
            for (int y = 0; y < h; y++)
                tex.SetPixel(0, y, Color.Lerp(bottom, top, (float)y / (h - 1)));
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            _grad = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), h);
            return _grad;
        }

        /// <summary>A coloured rectangle: a SpriteRenderer scaled to size (world units).</summary>
        public static GameObject Box(string name, Transform parent, Vector2 pos,
            Vector2 size, Color color, int order = 0)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }

        /// <summary>A GameObject showing a sprite scaled to a target world size.</summary>
        public static GameObject SpriteBox(string name, Transform parent, Vector3 pos,
            Vector2 worldSize, Sprite sp, int order)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp; sr.sortingOrder = order;
            var b = sp.bounds.size;
            go.transform.localScale = new Vector3(
                b.x > 0.0001f ? worldSize.x / b.x : 1f,
                b.y > 0.0001f ? worldSize.y / b.y : 1f, 1f);
            return go;
        }

        public static BoxCollider2D AddSolid(GameObject go)
        {
            var c = go.AddComponent<BoxCollider2D>();
            return c; // size 1x1 matches the unit sprite, scaled by transform
        }

        public static BoxCollider2D AddTrigger(GameObject go, Vector2 size)
        {
            var c = go.AddComponent<BoxCollider2D>();
            c.isTrigger = true;
            c.size = size; // in local units (transform scale already applied)
            return c;
        }

        // ---- simple UI (HUD / overlays) on a shared overlay canvas ----
        public static Canvas _canvas;
        public static Canvas Canvas
        {
            get
            {
                if (_canvas == null)
                {
                    var go = new GameObject("UICanvas");
                    _canvas = go.AddComponent<Canvas>();
                    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    var s = go.AddComponent<CanvasScaler>();
                    s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    s.referenceResolution = new Vector2(1920, 1080);
                    go.AddComponent<GraphicRaycaster>();

                    // Without an EventSystem, UI buttons receive NO clicks. We
                    // build everything from code, so we must create one ourselves.
                    if (Object.FindFirstObjectByType<EventSystem>() == null)
                    {
                        var es = new GameObject("EventSystem");
                        es.AddComponent<EventSystem>();
                        es.AddComponent<StandaloneInputModule>();
                    }
                }
                return _canvas;
            }
        }

        static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                    if (_font == null)
                        try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                }
                return _font;
            }
        }

        /// <summary>A clickable menu button (Image + Button + centered label).</summary>
        public static Button Button(Transform parent, string text, Color bg, Color textColor,
            int size, Vector2 anchor, Vector2 pos, Vector2 dim, System.Action onClick)
        {
            var go = new GameObject("Button_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = dim;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.15f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var label = Label(go.transform, text, size, textColor,
                new Vector2(0.5f, 0.5f), Vector2.zero, dim);
            return btn;
        }

        public static Text Label(Transform parent, string text, int size, Color color,
            Vector2 anchor, Vector2 pos, Vector2 dim, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = size; t.fontStyle = FontStyle.Bold;
            t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = dim;
            return t;
        }
    }
}
