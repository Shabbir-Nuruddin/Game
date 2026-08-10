using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TrustIssues
{
    /// <summary>
    /// CRIMSON — the red-and-black UI language from the art pass.
    ///
    /// The older <see cref="Gothic"/> kit is violet-black stone with candle gold, and it
    /// still dresses the sub-screens that were never redesigned (shop, wardrobe, pause).
    /// The art pass took three screens — the landing, the castle map and settings — and
    /// pulled every other hue out of them: black earth, dried blood, one red moon with
    /// no orange in it, and gold used only as a hairline. This class is that palette and
    /// its furniture, so all three screens are built from one vocabulary instead of each
    /// re-inventing a plate.
    ///
    /// Everything is generated in code (no new art files) and laid out in the canvas's
    /// 1920x1080 reference space.
    /// </summary>
    public static class Crimson
    {
        // ---- palette, sampled straight off the design ------------------------
        public static readonly Color Night     = Theme.Hex("07040A");  // the black behind everything
        public static readonly Color Earth     = Theme.Hex("0C040B");  // a panel's deepest interior
        public static readonly Color Panel     = Theme.Hex("120610");  // a small chip / counter
        public static readonly Color Plate     = Theme.Hex("1C0812");  // a card's interior
        public static readonly Color PlateLit  = Theme.Hex("25101A");  // the selected card
        public static readonly Color Iron      = Theme.Hex("3A1A22");  // an unlit hairline border
        public static readonly Color Rail      = Theme.Hex("6E5427");  // the gold hairline, unlit
        public static readonly Color Gold      = Theme.Hex("C9A24B");  // gold, lit
        public static readonly Color GoldLit   = Theme.Hex("F0D89B");  // gold type on a live button
        public static readonly Color Bone      = Theme.Hex("F2DCC6");  // headings
        public static readonly Color Body      = Theme.Hex("B3A0A6");  // body copy
        public static readonly Color Mute      = Theme.Hex("9A828A");  // captions
        public static readonly Color Dead      = Theme.Hex("4E3A42");  // sealed / disabled
        public static readonly Color Blood     = Theme.Hex("B00A1E");  // the crimson fill
        public static readonly Color BloodHot  = Theme.Hex("E01834");  // the live edge
        public static readonly Color BloodLit  = Theme.Hex("FF4256");  // the lit rim / moon core
        public static readonly Color BloodDeep = Theme.Hex("8E0B1E");  // a deep gate mouth
        public static readonly Color Ember     = Theme.Hex("3E0812");  // a primary button's fill

        // ==================== procedural sprites ====================

        // A blood DROP — the design's currency glyph. It replaces the diamond that used
        // to stand for shards, and it's the one place the currency is allowed a shape of
        // its own: round-bottomed with a point at the top, the way a drop falls.
        static Sprite _drop;
        public static Sprite Drop
        {
            get
            {
                if (_drop != null) return _drop;
                const int S = 48;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var clear = new Color(0, 0, 0, 0);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        // Bulb: a circle in the lower half. Point: a cone narrowing to the top.
                        float bx = x - S * 0.5f, by = y - S * 0.34f;
                        bool bulb = bx * bx + by * by <= (S * 0.30f) * (S * 0.30f);
                        float t = Mathf.InverseLerp(S * 0.97f, S * 0.34f, y);          // 0 at tip, 1 at bulb
                        bool point = y >= S * 0.34f && Mathf.Abs(x - S * 0.5f) <= S * 0.30f * t * t;
                        if (!bulb && !point) { tex.SetPixel(x, y, clear); continue; }
                        // A slick of light on the upper-left of the bulb so it reads wet.
                        float hx = x - S * 0.40f, hy = y - S * 0.42f;
                        bool shine = hx * hx + hy * hy <= (S * 0.09f) * (S * 0.09f);
                        tex.SetPixel(x, y, shine ? BloodLit : Blood);
                    }
                tex.Apply();
                _drop = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _drop;
            }
        }

        // A soft round glow, used for the moon's halo and a medallion's bloom. A flat
        // disc with a hard edge reads as a sticker; this one fades to nothing.
        static Sprite _halo;
        public static Sprite Halo
        {
            get
            {
                if (_halo != null) return _halo;
                const int S = 128;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                float c = (S - 1) / 2f;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                        // Squared falloff: bright core, long faint skirt.
                        float a = Mathf.Clamp01(1f - d); a *= a;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                _halo = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _halo;
            }
        }

        // A ring with a hollow middle — the outline a selected medallion wears.
        static Sprite _ring;
        public static Sprite Ring
        {
            get
            {
                if (_ring != null) return _ring;
                const int S = 96;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                float c = (S - 1) / 2f;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                        bool on = d <= 1f && d >= 0.88f;
                        tex.SetPixel(x, y, on ? Color.white : new Color(0, 0, 0, 0));
                    }
                tex.Apply();
                _ring = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _ring;
            }
        }

        // ==================== screen furniture ====================

        /// <summary>
        /// THE NIGHT: the shared backdrop for all three redesigned screens. Black earth
        /// under a graded crimson sky, one red moon hanging off the top edge with its
        /// halo, a whisper of masonry grain, a castle ridge in silhouette and a few bats
        /// drifting across. `moonSize` and `moonY` place the moon; the landing hangs it
        /// low and big, the map pushes it further off-screen so the road owns the frame.
        /// </summary>
        public static void Backdrop(Transform root, float moonSize = 460f, float moonY = 118f,
                                    bool ridge = true, int bats = 3)
        {
            // Overlay() hangs a pale ornate frame on every panel. It's the old violet
            // kit's frame and it fights this palette, so it goes first.
            var stale = root.Find("Frame");
            if (stale != null) { stale.gameObject.SetActive(false); Object.Destroy(stale.gameObject); }

            // The sky: crimson at the top fading to black at the floor.
            var sky = Stretch(root, "Sky", Color.white, Vector2.zero, Vector2.one);
            sky.sprite = Theme.Gradient(Theme.Hex("22060F"), Night);

            // Masonry grain, barely there — texture, not brickwork.
            var stone = Stretch(root, "Stone", new Color(0.55f, 0.30f, 0.34f, 0.07f), Vector2.zero, Vector2.one);
            stone.sprite = Theme.StoneTile;
            stone.type = Image.Type.Tiled;
            stone.pixelsPerUnitMultiplier = 0.14f;

            // The moon's halo, then the moon. The design's note is the whole point:
            // the orange came out of it, so both are pure red.
            var halo = Img(root, "MoonHalo", Halo, new Color(BloodLit.r, BloodLit.g, BloodLit.b, 0.34f));
            Place(halo, new Vector2(0.5f, 1f), new Vector2(0, moonY), Vector2.one * (moonSize * 2.1f));

            // Theme.Moon carries its own soft rim; a bare circle reads as a red sticker
            // stuck on the sky.
            var moon = Img(root, "Moon", Theme.Moon, BloodHot);
            Place(moon, new Vector2(0.5f, 1f), new Vector2(0, moonY), Vector2.one * moonSize);
            // A lit shoulder on the upper-left, so it's a body and not a red circle.
            var lit = Img(moon.transform, "Lit", Halo, new Color(1f, 0.42f, 0.47f, 0.85f));
            Place(lit, new Vector2(0.36f, 0.64f), Vector2.zero, Vector2.one * (moonSize * 0.62f));

            // The castle ridge along the horizon, black on black, with two lit windows —
            // the only warm points in the picture besides the moon.
            if (ridge)
            {
                var art = Assets.Sprite("bg_castle");
                if (art != null)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        var c = Img(root, "Ridge", art, new Color(0.055f, 0.02f, 0.04f, 1f));
                        c.preserveAspect = true;
                        Place(c, new Vector2(s == 0 ? 0.22f : 0.80f, 0.62f), Vector2.zero, new Vector2(1000, 560));
                        if (s == 1) c.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                    }
                    for (int w = 0; w < 2; w++)
                    {
                        var win = Img(root, "Window", Halo, new Color(1f, 0.26f, 0.34f, 0.9f));
                        Place(win, new Vector2(w == 0 ? 0.235f : 0.775f, 0.615f), Vector2.zero, Vector2.one * 26f);
                    }
                }
            }

            // Ground fog: a crimson haze that swallows the bottom of the frame.
            var fog = Stretch(root, "Fog", Color.white, Vector2.zero, new Vector2(1f, 0.42f));
            fog.sprite = Theme.Gradient(new Color(Blood.r, Blood.g, Blood.b, 0f),
                                        new Color(Blood.r * 0.5f, 0.02f, 0.05f, 0.55f));

            // Bats. Reduced Motion turns their drift off (the sprites stay).
            var batSprite = Assets.Sprite("bat_fly") ?? Theme.BatGlyph;
            for (int i = 0; i < bats; i++)
            {
                var b = Img(root, "Bat", batSprite, new Color(0.14f, 0.05f, 0.08f, 0.9f));
                b.preserveAspect = true;
                float[] bx = { 0.22f, 0.72f, 0.58f }, by = { 0.80f, 0.86f, 0.72f };
                float[] bs = { 62f, 44f, 34f };
                int k = i % 3;
                Place(b, new Vector2(bx[k], by[k]), Vector2.zero, Vector2.one * bs[k]);
                if (!Options.ReducedMotion) b.gameObject.AddComponent<Drift>().Seed(i);
            }
        }

        /// <summary>A drifting, flapping bat. Unscaled time, so it lives through a pause.</summary>
        public class Drift : MonoBehaviour
        {
            float _seed, _speed;
            Vector2 _home;
            RectTransform _rt;
            public void Seed(int i) { _seed = i * 2.7f; _speed = 0.18f + i * 0.045f; }
            void Start() { _rt = GetComponent<RectTransform>(); _home = _rt.anchoredPosition; }
            void Update()
            {
                if (_rt == null) return;
                float t = Time.unscaledTime * _speed + _seed;
                _rt.anchoredPosition = _home + new Vector2(Mathf.Sin(t) * 90f, Mathf.Sin(t * 1.7f) * 42f);
                // A cheap wing-beat: squash the sprite horizontally.
                float flap = 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 7f + _seed));
                _rt.localScale = new Vector3(flap, 1f, 1f);
            }
        }

        /// <summary>
        /// A bordered panel — the design's one container. Every card, counter, tab and
        /// button in the pass is this with a different fill and border colour. Built as
        /// a border rect with the interior inset by two units, because a hairline is what
        /// the design draws and a 9-sliced ornate frame is what it deliberately dropped.
        /// </summary>
        public static RectTransform Panel_(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                           Color fill, Color border, float line = 2f)
        {
            var edge = Img(parent, "Panel", null, border);
            Place(edge, anchor, pos, size);
            var inner = Stretch(edge.transform, "Fill", fill, Vector2.zero, Vector2.one);
            inner.rectTransform.offsetMin = new Vector2(line, line);
            inner.rectTransform.offsetMax = new Vector2(-line, -line);
            return edge.rectTransform;
        }

        /// <summary>
        /// A button in the design's language: a bordered plate with letter-spaced type,
        /// a caption hanging under it, and a red diamond stud on each side. `primary`
        /// wears the crimson fill (ENDLESS NIGHTS, DESCEND); the rest are near-black
        /// with gold type.
        /// </summary>
        public static Button Btn(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size,
                                 System.Action onClick, bool primary = false, int fontSize = 30,
                                 string caption = null, bool studs = false, bool enabled = true)
        {
            var go = new GameObject("CBtn_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var edge = go.AddComponent<Image>();
            edge.color = !enabled ? Iron : primary ? BloodHot : Gold;
            Place(edge, anchor, pos, size);

            var fill = Stretch(go.transform, "Fill", primary ? Ember : Plate, Vector2.zero, Vector2.one);
            fill.rectTransform.offsetMin = new Vector2(2, 2);
            fill.rectTransform.offsetMax = new Vector2(-2, -2);
            fill.raycastTarget = true;                       // the fill is what catches the tap

            if (enabled && onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = fill;
                var colors = btn.colors;
                colors.highlightedColor = Color.Lerp(Color.white, BloodLit, 0.35f);
                colors.pressedColor = new Color(0.55f, 0.5f, 0.5f, 1f);
                colors.fadeDuration = 0.07f;
                btn.colors = colors;
                btn.onClick.AddListener(() => { Audio.Play("click", 0.6f); onClick(); });

                var label = Line(go.transform, text, fontSize,
                    primary ? Theme.Hex("FBE9D6") : GoldLit, Vector2.zero, new Vector2(size.x - 24, size.y));
                label.fontStyle = FontStyle.Bold;
                if (caption != null)
                    Line(go.transform, caption, 18, Mute, new Vector2(0, -size.y * 0.5f - 18f),
                         new Vector2(size.x + 80, 30));
                if (studs) Studs(go.transform, size);
                return btn;
            }

            var dim = Line(go.transform, text, fontSize, Dead, Vector2.zero, new Vector2(size.x - 24, size.y));
            dim.fontStyle = FontStyle.Bold;
            if (caption != null)
                Line(go.transform, caption, 18, Dead, new Vector2(0, -size.y * 0.5f - 18f), new Vector2(size.x + 80, 30));
            if (studs) Studs(go.transform, size);
            return null;
        }

        /// <summary>
        /// A button wearing the PAINTED metal from the landing artwork: a forged end-cap
        /// on each side and a stretched middle between them, with the interior darkened
        /// so type reads against the metal. The caps are separate art files precisely so
        /// a button can be any width without the ironwork smearing — only the plain
        /// middle stretches.
        ///
        /// Falls back to the code-drawn <see cref="Btn"/> when the art isn't installed,
        /// so the menu still works on a checkout without the ui/ pictures.
        /// </summary>
        public static Button MetalBtn(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size,
                                      System.Action onClick, int fontSize = 34, string caption = null,
                                      bool enabled = true)
        {
            var capL = Skin.Cut("btn_capL");
            var capR = Skin.Cut("btn_capR");
            var mid = Skin.Cut("btn_mid");
            if (capL == null || capR == null || mid == null)
                return Btn(parent, text, anchor, pos, size, onClick, false, fontSize, caption, true, enabled);

            var go = new GameObject("MBtn_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            // The cap art is 168x196; keep that ratio so the ironwork never squashes.
            float cap = size.y * (168f / 196f);
            var m = Img(go.transform, "Mid", mid, Color.white);
            Side(m.rectTransform, cap - 5f, cap - 5f);
            var l = Img(go.transform, "CapL", capL, Color.white);
            Edge(l.rectTransform, 0f, cap, true);
            var r = Img(go.transform, "CapR", capR, Color.white);
            Edge(r.rectTransform, 0f, cap, false);

            // The interior wash: the metal is bright enough to eat type on its own.
            var wash = Img(go.transform, "Wash", null, new Color(0.024f, 0.012f, 0.024f, 0.70f));
            var wrt = wash.rectTransform;
            wrt.anchorMin = new Vector2(0, 0); wrt.anchorMax = new Vector2(1, 1);
            wrt.offsetMin = new Vector2(cap - 6f, size.y * 0.14f);
            wrt.offsetMax = new Vector2(-(cap - 6f), -size.y * 0.14f);

            // The design's label is a red gradient with a black drop — two passes of
            // flat type get the same read without a custom shader.
            float ty = caption != null ? size.y * 0.13f : 0f;
            var back = Line(go.transform, text, fontSize, new Color(0, 0, 0, 0.95f),
                            new Vector2(0, ty - 3f), new Vector2(size.x - cap * 1.6f, size.y));
            back.fontStyle = FontStyle.Bold;
            var label = Line(go.transform, text, fontSize, enabled ? BloodLit : Dead,
                             new Vector2(0, ty), new Vector2(size.x - cap * 1.6f, size.y));
            label.fontStyle = FontStyle.Bold;
            if (caption != null)
                Line(go.transform, caption, Mathf.Max(14, Mathf.RoundToInt(fontSize * 0.42f)),
                     enabled ? Theme.Hex("9A6E68") : Dead,
                     new Vector2(0, -size.y * 0.28f), new Vector2(size.x - cap * 1.2f, size.y * 0.34f));

            if (!enabled || onClick == null) return null;

            var hit = Stretch(go.transform, "Hit", new Color(0, 0, 0, 0), Vector2.zero, Vector2.one);
            hit.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.10f);   // hit is clear; tint it to flash
            colors.pressedColor = new Color(1f, 1f, 1f, 0.20f);
            colors.fadeDuration = 0.07f;
            btn.colors = colors;
            btn.onClick.AddListener(() => { Audio.Play("click", 0.6f); onClick(); });
            return btn;
        }

        // Stretch a child across its parent, inset by these margins left and right.
        static void Side(RectTransform rt, float left, float right)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(left, 0); rt.offsetMax = new Vector2(-right, 0);
        }

        // Pin a fixed-width child to one end of its parent, full height.
        static void Edge(RectTransform rt, float inset, float width, bool left)
        {
            rt.anchorMin = new Vector2(left ? 0 : 1, 0); rt.anchorMax = new Vector2(left ? 0 : 1, 1);
            rt.pivot = new Vector2(left ? 0 : 1, 0.5f);
            rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(width, 0);
            rt.anchoredPosition = new Vector2(left ? inset : -inset, 0);
        }

        // The pair of blood diamonds set into a plate's left and right rails.
        static void Studs(Transform parent, Vector2 size)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                var d = Img(parent, "Stud", Gothic.Diamond, Color.white);
                Place(d, new Vector2(0.5f, 0.5f), new Vector2(s * size.x * 0.5f, 0), Vector2.one * 22f);
            }
        }

        /// <summary>
        /// The BLOOD counter: a drop, the balance, and the word. It replaced a diamond
        /// and the words "BLOOD SHARDS" — the design's call, and the shorter label is
        /// what lets it sit in a screen corner without a line break.
        /// </summary>
        public static RectTransform BloodCounter(Transform parent, Vector2 anchor, Vector2 pos, int size = 30)
        {
            float w = size * 9f, h = size * 2f;
            var p = Panel_(parent, anchor, pos, new Vector2(w, h), Panel, Rail);
            var left = new Vector2(0f, 0.5f);
            var d = Img(p, "Drop", Drop, Color.white);
            Place(d, left, new Vector2(size * 0.85f, 0), Vector2.one * (size * 0.9f));
            var v = Line(p.transform, Currency.Balance.ToString("N0"), size, GoldLit,
                         new Vector2(size * 1.5f, 0), new Vector2(size * 4f, h * 0.8f),
                         TextAnchor.MiddleLeft, left);
            v.fontStyle = FontStyle.Bold;
            Line(p.transform, "BLOOD", Mathf.RoundToInt(size * 0.6f), Mute,
                 new Vector2(-size * 0.5f, 0), new Vector2(size * 2.6f, h * 0.8f),
                 TextAnchor.MiddleRight, new Vector2(1f, 0.5f));
            return p;
        }

        /// <summary>
        /// The banner tab row from the settings design: equal-width tabs across the top
        /// of a panel, the live one lit gold on a raised fill. Returns nothing — the
        /// caller rebuilds its body when `onPick` fires.
        /// </summary>
        public static void Tabs(Transform parent, string[] labels, int selected, System.Action<int> onPick,
                                Vector2 anchor, Vector2 pos, float totalWidth, float height = 62f)
        {
            float gap = 12f;
            float w = (totalWidth - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                bool on = i == selected;
                float x = pos.x - totalWidth * 0.5f + w * 0.5f + i * (w + gap);
                var go = new GameObject("Tab_" + labels[i], typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var edge = go.AddComponent<Image>();
                edge.color = on ? Gold : Iron;
                Place(edge, anchor, new Vector2(x, pos.y), new Vector2(w, height));
                var fill = Stretch(go.transform, "Fill", on ? PlateLit : Theme.Hex("0E0510"), Vector2.zero, Vector2.one);
                fill.rectTransform.offsetMin = new Vector2(2, 2);
                fill.rectTransform.offsetMax = new Vector2(-2, -2);
                if (!on)
                {
                    var b = go.AddComponent<Button>();
                    b.targetGraphic = fill;
                    b.onClick.AddListener(() => { Audio.Play("click", 0.6f); onPick(idx); });
                }
                var t = Line(go.transform, labels[i], 24, on ? GoldLit : Mute, Vector2.zero, new Vector2(w - 12, height));
                t.fontStyle = FontStyle.Bold;
            }
        }

        /// <summary>
        /// A volume slider drawn as blood filling a glass: a black trough, a crimson fill
        /// that grows from the left, a gold knob riding its edge, and the value in gold
        /// above. Drag anywhere on the trough.
        /// </summary>
        public static void BloodSlider(Transform parent, Vector2 anchor, Vector2 pos, float width,
                                       string label, string note,
                                       System.Func<float> get, System.Action<float> set)
        {
            Line(parent, label, 26, Theme.Hex("E9E0D2"), pos + new Vector2(-width * 0.5f, 34f),
                 new Vector2(width * 0.7f, 34), TextAnchor.MiddleLeft, anchor).fontStyle = FontStyle.Bold;
            var readout = Line(parent, Mathf.RoundToInt(get() * 100f).ToString(), 26, Gold,
                               pos + new Vector2(width * 0.5f, 34f), new Vector2(width * 0.3f, 34),
                               TextAnchor.MiddleRight, anchor);
            readout.fontStyle = FontStyle.Bold;

            // The trough. It owns the drag, so it must be raycastable.
            var trough = new GameObject("Trough_" + label, typeof(RectTransform));
            trough.transform.SetParent(parent, false);
            var edge = trough.AddComponent<Image>();
            edge.color = Iron;
            Place(edge, anchor, pos, new Vector2(width, 34f));
            var inner = Stretch(trough.transform, "Bed", Theme.Hex("0A0410"), Vector2.zero, Vector2.one);
            inner.rectTransform.offsetMin = new Vector2(2, 2);
            inner.rectTransform.offsetMax = new Vector2(-2, -2);

            // Flat crimson with a lit cap along the top — blood in a glass. A vertical
            // gradient sprite was tried first and read as an almost-black bar: stretched
            // over a 24-unit-tall trough there is nowhere for the gradient to happen.
            var fill = Stretch(trough.transform, "Fill", Blood, Vector2.zero, Vector2.one);
            fill.rectTransform.offsetMin = new Vector2(5, 5);
            fill.rectTransform.offsetMax = new Vector2(-5, -5);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = Mathf.Clamp01(get());

            var knob = Img(trough.transform, "Knob", null, Gold);
            var krt = knob.rectTransform;
            krt.anchorMin = new Vector2(0, 0); krt.anchorMax = new Vector2(0, 1);
            krt.pivot = new Vector2(0.5f, 0.5f);
            krt.sizeDelta = new Vector2(6, 12);

            void Apply(float v)
            {
                v = Mathf.Clamp01(v);
                set(v);
                fill.fillAmount = v;
                readout.text = Mathf.RoundToInt(v * 100f).ToString();
                krt.anchoredPosition = new Vector2(width * v, 0);
            }
            Apply(get());

            var drag = trough.AddComponent<DragBar>();
            drag.Init(edge.rectTransform, Apply);

            if (!string.IsNullOrEmpty(note))
                Line(parent, note, 19, Theme.Hex("7E6C74"), pos + new Vector2(-width * 0.5f, -32f),
                     new Vector2(width, 30), TextAnchor.MiddleLeft, anchor);
        }

        /// <summary>Turns a rect into a scrub bar: tap or drag anywhere sets 0..1.</summary>
        public class DragBar : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            RectTransform _rt;
            System.Action<float> _set;
            public void Init(RectTransform rt, System.Action<float> set) { _rt = rt; _set = set; }
            void Set(PointerEventData e)
            {
                if (_rt == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var p);
                _set(Mathf.Clamp01((p.x + _rt.rect.width * 0.5f) / _rt.rect.width));
            }
            public void OnPointerDown(PointerEventData e) => Set(e);
            public void OnDrag(PointerEventData e) => Set(e);
        }

        /// <summary>
        /// A labelled on/off row: the caption on the left, a hard-edged switch on the
        /// right that fills crimson when it's on. No rounded pill — the design has no
        /// round corners anywhere except the moon and the medallions.
        /// </summary>
        public static void Toggle(Transform parent, Vector2 anchor, Vector2 pos, float width,
                                  string label, System.Func<bool> get, System.Action<bool> set)
        {
            var row = new GameObject("Toggle_" + label, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var edge = row.AddComponent<Image>();
            edge.color = Iron;
            Place(edge, anchor, pos, new Vector2(width, 62f));
            var fill = Stretch(row.transform, "Fill", Panel, Vector2.zero, Vector2.one);
            fill.rectTransform.offsetMin = new Vector2(2, 2);
            fill.rectTransform.offsetMax = new Vector2(-2, -2);

            Line(row.transform, label, 23, Theme.Hex("E9E0D2"), new Vector2(-width * 0.5f + 20f, 0),
                 new Vector2(width - 130f, 40), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;

            var track = Img(row.transform, "Track", null, Iron);
            Place(track, new Vector2(0.5f, 0.5f), new Vector2(width * 0.5f - 52f, 0), new Vector2(70, 32));
            var bed = Stretch(track.transform, "Bed", Theme.Hex("0A0410"), Vector2.zero, Vector2.one);
            bed.rectTransform.offsetMin = new Vector2(2, 2);
            bed.rectTransform.offsetMax = new Vector2(-2, -2);
            var pip = Img(track.transform, "Pip", null, Dead);
            Place(pip, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 20));

            void Paint(bool on)
            {
                track.color = on ? BloodHot : Iron;
                bed.color = on ? Ember : Theme.Hex("0A0410");
                pip.color = on ? BloodLit : Dead;
                pip.rectTransform.anchoredPosition = new Vector2(on ? 17f : -17f, 0);
            }
            Paint(get());

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = fill;
            btn.onClick.AddListener(() =>
            {
                Audio.Play("click", 0.6f);
                bool v = !get();
                set(v);
                Paint(v);
            });
        }

        /// <summary>
        /// A choice card — the wide pick-one tiles used for difficulty and stick mode:
        /// a title, a line of flavour, lit crimson when it's the live choice.
        /// </summary>
        public static void Card(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                string title, string note, bool on, System.Action pick)
        {
            var go = new GameObject("Card_" + title, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var edge = go.AddComponent<Image>();
            edge.color = on ? BloodHot : Iron;
            Place(edge, anchor, pos, size);
            var fill = Stretch(go.transform, "Fill", on ? Theme.Hex("2A0810") : Theme.Hex("0E0510"), Vector2.zero, Vector2.one);
            fill.rectTransform.offsetMin = new Vector2(2, 2);
            fill.rectTransform.offsetMax = new Vector2(-2, -2);
            if (!on)
            {
                var b = go.AddComponent<Button>();
                b.targetGraphic = fill;
                b.onClick.AddListener(() => { Audio.Play("click", 0.6f); pick(); });
            }
            var t = Line(go.transform, title, 30, on ? Bone : Mute, new Vector2(0, size.y * 0.5f - 40f),
                         new Vector2(size.x - 30, 42));
            t.fontStyle = FontStyle.Bold;
            Line(go.transform, note, 21, on ? Body : Theme.Hex("7E6C74"), new Vector2(0, -14f),
                 new Vector2(size.x - 44, size.y - 96));
        }

        // ==================== the castle map's medallion ====================

        /// <summary>
        /// One floor on the road. Cleared floors are cold iron with a gold rim, the live
        /// floor is a beating red heart (the only lit thing on the screen), sealed floors
        /// are almost invisible, and every tenth floor is a wide crimson GATE with a bat
        /// crest over it so it reads as an event from across the screen.
        /// </summary>
        public static RectTransform Medal(Transform parent, Vector2 pos, int floor, string state,
                                          bool gate, bool nemesis, bool selected, System.Action pick)
        {
            float size = gate ? 128f : state == "now" ? 108f : 92f;
            var go = new GameObject("Floor" + floor, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(gate ? 210f : 150f, gate ? 230f : 180f);

            if (gate)
            {
                // The gate arch: a crimson mouth with a gold rail across its top and the
                // bat crest above. Built from two discs and a rect rather than art, so
                // it costs nothing and scales.
                var mouth = Img(go.transform, "Gate", Theme.Disc, state == "lock" ? Theme.Hex("14060E") : BloodDeep);
                Place(mouth, new Vector2(0.5f, 0.5f), new Vector2(0, 4f), new Vector2(size, size));
                var inner = Img(mouth.transform, "Mouth", Theme.Disc, Theme.Hex("07040A"));
                Place(inner, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (size - 22f));
                if (state != "lock")
                {
                    var glow = Img(go.transform, "Glow", Halo, new Color(BloodHot.r, BloodHot.g, BloodHot.b, 0.42f));
                    Place(glow, new Vector2(0.5f, 0.5f), new Vector2(0, 4f), Vector2.one * (size * 2.1f));
                    glow.transform.SetAsFirstSibling();
                }
                var rail = Img(go.transform, "Rail", null, state == "lock" ? Iron : Gold);
                Place(rail, new Vector2(0.5f, 0.5f), new Vector2(0, size * 0.5f + 6f), new Vector2(size + 26f, 5f));
                var crest = Img(go.transform, "Crest", Theme.BatGlyph,
                                state == "lock" ? Iron : new Color(1f, 0.86f, 0.62f, 0.95f));
                crest.preserveAspect = true;
                Place(crest, new Vector2(0.5f, 0.5f), new Vector2(0, size * 0.5f + 42f), new Vector2(120, 54));
            }
            else
            {
                var disc = Img(go.transform, "Medal", Theme.Disc,
                    state == "now" ? Blood : state == "done" ? Theme.Hex("1C0A12") : Theme.Hex("0C0510"));
                Place(disc, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                var rim = Img(disc.transform, "Rim", Ring,
                    state == "now" ? BloodLit : state == "done" ? Rail : Iron);
                Place(rim, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (size + 6f));
                if (state == "now")
                {
                    var glow = Img(go.transform, "Glow", Halo, new Color(BloodHot.r, BloodHot.g, BloodHot.b, 0.5f));
                    Place(glow, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (size * 2.2f));
                    glow.transform.SetAsFirstSibling();
                    if (!Options.ReducedMotion) go.AddComponent<Beat>();
                }
            }

            var num = Line(go.transform, floor.ToString(), gate ? 40 : state == "now" ? 38 : 32,
                state == "now" ? Theme.Hex("FFF0E4") : gate ? Gold : state == "done" ? Theme.Hex("B39A6A") : Dead,
                new Vector2(0, gate ? 4f : 0f), new Vector2(size, size));
            num.fontStyle = FontStyle.Bold;

            // The nemesis crown: the floor that has killed you most wears it in gold.
            if (nemesis)
            {
                var crown = Img(go.transform, "Crown", Gothic.Diamond, Color.white);
                Place(crown, new Vector2(0.5f, 0.5f), new Vector2(0, size * 0.5f + (gate ? 78f : 20f)), Vector2.one * 26f);
            }

            // The selection outline, so a tap is visibly registered even on a floor
            // that isn't lit.
            if (selected)
            {
                var sel = Img(go.transform, "Sel", Ring, Gold);
                Place(sel, new Vector2(0.5f, 0.5f), new Vector2(0, gate ? 4f : 0f), Vector2.one * (size + 26f));
            }

            // The whole cell is the tap target — a 92px disc is a small thumb target on
            // a phone, and the caption underneath belongs to the same floor.
            var hit = Stretch(go.transform, "Hit", new Color(0, 0, 0, 0), Vector2.zero, Vector2.one);
            hit.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.onClick.AddListener(() => { Audio.Play("click", 0.5f); pick(); });
            return rt;
        }

        /// <summary>The live floor's heartbeat — a slow swell of its glow and scale.</summary>
        public class Beat : MonoBehaviour
        {
            void Update()
            {
                float s = 1f + Mathf.Sin(Time.unscaledTime * 3.3f) * 0.05f;
                transform.localScale = new Vector3(s, s, 1f);
            }
        }

        // ==================== small helpers ====================

        /// <summary>
        /// A line of type in the menu face, never a raycast target.
        ///
        /// `pos` means what the alignment says it means: for a left-aligned line it is
        /// where the text STARTS, for a right-aligned one where it ends, and for a
        /// centred one its middle. A uGUI rect is positioned by its centre, so a
        /// left-aligned caption written at x=88 used to be a 520-wide box centred on 88 —
        /// i.e. starting at -172, off the side of the screen. Every clipped caption on
        /// the first pass of these three screens was that one mistake.
        /// </summary>
        public static Text Line(Transform parent, string text, int size, Color color, Vector2 pos, Vector2 dim,
                                TextAnchor align = TextAnchor.MiddleCenter, Vector2 anchor = default)
        {
            if (anchor == default) anchor = new Vector2(0.5f, 0.5f);
            if (align == TextAnchor.MiddleLeft || align == TextAnchor.UpperLeft || align == TextAnchor.LowerLeft)
                pos.x += dim.x * 0.5f;
            else if (align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight)
                pos.x -= dim.x * 0.5f;
            if (align == TextAnchor.UpperLeft || align == TextAnchor.UpperCenter || align == TextAnchor.UpperRight)
                pos.y -= dim.y * 0.5f;
            var t = Theme.Label(parent, text, size, color, anchor, pos, dim, align);
            if (Theme.MenuFont != null) t.font = Theme.MenuFont;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false;
            return t;
        }

        public static Image Img(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite ?? Theme.Square;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static void Place(Image img, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static Image Stretch(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        static Image Img(RectTransform parent, string name, Sprite sprite, Color color)
            => Img(parent.transform, name, sprite, color);

        static Text Line(RectTransform parent, string text, int size, Color color, Vector2 pos, Vector2 dim,
                         TextAnchor align = TextAnchor.MiddleCenter)
            => Line(parent.transform, text, size, color, pos, dim, align);
    }
}
