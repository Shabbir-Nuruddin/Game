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
        public const string Mascot = "the Heir";

        // Palette — VAMPIRE: black night, blood red, candle gold.
        public static readonly Color Sky      = Hex("0E0A12"); // near-black night
        public static readonly Color SkyLow   = Hex("1C1020"); // dark maroon
        public static readonly Color Platform = Hex("443F4E"); // castle stone
        public static readonly Color PlatEdge = Hex("7A1622"); // blood-red top
        public static readonly Color Player   = Hex("E23B3B"); // blood red (text/title)
        public static readonly Color Danger   = Hex("FF2E2E"); // hazards (blood)
        public static readonly Color Trick    = Hex("7A2BB0"); // portals (necro purple)
        public static readonly Color Coin      = Hex("E0B33A"); // candle gold
        public static readonly Color Exit      = Hex("E0B33A"); // the goal (gold glow)
        public static readonly Color Ink       = Hex("0B0810"); // near-black text
        public static readonly Color Tell      = new Color(0, 0, 0, 0.18f);

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

        // A 9-sliced sprite loaded from Resources/art with a runtime border, so UI
        // frames/bars stretch cleanly (corners stay crisp). Cached; null if missing
        // (callers fall back to plain boxes). Used for the gothic UI skin.
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _nine = new();
        public static Sprite NineSlice(string name, int border)
        {
            if (_nine.TryGetValue(name, out var s)) return s;
            var tex = Resources.Load<Texture2D>("art/" + name);
            if (tex == null) { _nine[name] = null; return null; }
            tex.filterMode = FilterMode.Point;
            var b = new Vector4(border, border, border, border);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), tex.width, 0, SpriteMeshType.FullRect, b);
            _nine[name] = s;
            return s;
        }

        // A code-generated bat silhouette (used for the player's bat/fly form).
        static Sprite _bat;
        public static Sprite Bat
        {
            get
            {
                if (_bat != null) return _bat;
                string[] rows =
                {
                    "..X.....X..",
                    ".XXX...XXX.",
                    "XXXXX.XXXXX",
                    "XXXXXXXXXXX",
                    ".XX.XXX.XX.",
                };
                int w = rows[0].Length, h = rows.Length;
                var tex = new Texture2D(w, h) { filterMode = FilterMode.Point };
                var body = Hex("1A1420"); var clear = new Color(0, 0, 0, 0);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        tex.SetPixel(x, h - 1 - y, rows[y][x] == 'X' ? body : clear);
                tex.Apply();
                _bat = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
                return _bat;
            }
        }

        // A tileable castle-stone block (dark slate with mortar lines + subtle
        // grain). Replaces the off-theme pink candy tile on every floor. Tiled
        // by the platform builder; the blood-red lip is a SEPARATE strip on top
        // so it shows once, not on every repeat.
        static Sprite _stone;
        public static Sprite StoneTile
        {
            get
            {
                if (_stone != null) return _stone;
                const int W = 16, H = 16;
                var tex = new Texture2D(W, H) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                var baseCol  = Hex("3A3340"); // castle stone
                var mortar   = Hex("231D2A"); // dark seams between bricks
                var light    = Hex("47404F"); // top-lit face of each brick
                var rng = new System.Random(7);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        // Running-bond brick: 8-wide bricks, offset every 8 rows.
                        bool seam = (y % 8 == 0) || (((x + (y / 8) * 4) % 8) == 0);
                        Color c = seam ? mortar : (y % 8 >= 6 ? mortar : (y % 8 <= 1 ? light : baseCol));
                        // subtle per-pixel grain so it doesn't read as flat
                        float n = (float)(rng.NextDouble() - 0.5) * 0.06f;
                        c = new Color(Mathf.Clamp01(c.r + n), Mathf.Clamp01(c.g + n), Mathf.Clamp01(c.b + n), 1f);
                        tex.SetPixel(x, y, c);
                    }
                tex.Apply();
                _stone = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
                return _stone;
            }
        }

        // A round "blood seal" medallion: dark stone centre with a blood-red rim,
        // used for level-select nodes so they read as gothic, not candy.
        static Sprite _disc;
        public static Sprite Disc
        {
            get
            {
                if (_disc != null) return _disc;
                const int S = 72;
                float c = (S - 1) / 2f, R = 34f, inner = 27f;
                var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var centre = Hex("241B2E"); var rim = Hex("8E1420"); var clear = new Color(0, 0, 0, 0);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        Color col = d <= inner ? centre : rim;
                        // soft 1.5px alpha falloff at the outer edge so it isn't jagged
                        col.a *= Mathf.Clamp01((R - d) / 1.5f);
                        if (d > R) col = clear;
                        tex.SetPixel(x, y, col);
                    }
                tex.Apply();
                _disc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _disc;
            }
        }

        // A filled "blood moon": a warm red-orange orb that fades to a soft glow
        // at the rim. Used purely as a menu-backdrop element. White at the core so
        // an Image tint controls the exact hue.
        static Sprite _moon;
        public static Sprite Moon
        {
            get
            {
                if (_moon != null) return _moon;
                const int S = 128;
                float c = (S - 1) / 2f, core = S * 0.34f, glow = S * 0.5f;
                var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        // solid core, then a soft halo that fades to nothing.
                        float a = d <= core ? 1f : Mathf.Clamp01((glow - d) / (glow - core)) * 0.5f;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                _moon = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _moon;
            }
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
                    // Balance width/height so UI doesn't clip off-screen on odd
                    // fullscreen aspect ratios (16:9, 16:10, ultrawide, etc.).
                    s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    s.matchWidthOrHeight = 0.5f;
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
                    // Prefer the dropped-in pixel font (Resources/fonts/body.ttf);
                    // fall back to a built-in font so text never disappears.
                    _font = Resources.Load<Font>("fonts/body");
                    if (_font == null)
                        try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                    if (_font == null)
                        try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                }
                return _font;
            }
        }

        // A gothic display font for big headings (Resources/fonts/title.ttf =
        // Nosifer). Falls back to the body font if it isn't present.
        static Font _titleFont;
        public static Font TitleFont
        {
            get
            {
                if (_titleFont == null) _titleFont = Resources.Load<Font>("fonts/title");
                if (_titleFont == null) _titleFont = Font;
                return _titleFont;
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
