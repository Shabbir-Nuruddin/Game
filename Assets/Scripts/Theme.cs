using UnityEngine;
using UnityEngine.UI;

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

        // Palette — punchy, meme-y, high contrast.
        public static readonly Color Sky      = Hex("1E1B2E"); // deep night
        public static readonly Color SkyLow   = Hex("2C2742");
        public static readonly Color Platform = Hex("F4F1E9"); // bright safe ground
        public static readonly Color PlatEdge = Hex("CFC8B6");
        public static readonly Color Player   = Hex("FFC83D"); // Beanie yellow
        public static readonly Color Danger   = Hex("FF4D5E"); // spikes / kill
        public static readonly Color Trick    = Hex("8E7CFF"); // fake/bait purple
        public static readonly Color Coin      = Hex("FFD84D");
        public static readonly Color Exit      = Hex("46D17F"); // real exit green
        public static readonly Color Ink       = Hex("20202A");
        public static readonly Color Tell      = new Color(0, 0, 0, 0.18f); // subtle hint

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
