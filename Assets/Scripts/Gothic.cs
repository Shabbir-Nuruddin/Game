using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// The black-and-blood UI kit — the code-built twin of the painted menu artwork.
    ///
    /// Several screens (the Crypt Shop, the pause menu, the multiplayer lobby) have no
    /// mockup of their own, so they used to be flat grey rectangles on a see-through
    /// wash: the main-menu artwork bled straight through them and the whole game looked
    /// like two different products stitched together. This class reproduces the
    /// artwork's language in code — opaque castle-stone ground, an ornate gold-and-
    /// blood border, the dripping title, framed plates for cards and buttons — so a
    /// screen with no painting still belongs to the same castle.
    ///
    /// Everything here is generated procedurally (like the rest of Theme), so it needs
    /// no new art files and scales to any resolution.
    /// </summary>
    public static class Gothic
    {
        // ---- palette, sampled off the artwork -------------------------------
        public static readonly Color Ground = new Color(0.043f, 0.024f, 0.051f, 1f);  // the stone dark
        public static readonly Color Plate  = new Color(0.075f, 0.047f, 0.086f, 1f);  // a card's interior
        public static readonly Color Gold   = new Color(0.72f, 0.56f, 0.26f, 1f);     // the frame's hairline
        public static readonly Color Blood  = new Color(0.62f, 0.09f, 0.13f, 1f);     // the painted crimson
        public static readonly Color Bone   = new Color(0.87f, 0.82f, 0.75f, 1f);     // body copy
        public static readonly Color Faint  = new Color(0.62f, 0.55f, 0.52f, 0.75f);  // sub-captions

        // ==================== procedural sprites ====================

        // The ornate border: a 9-sliced ring of hairlines (gold / black / blood) with a
        // red diamond set into each corner, exactly like the painted frames. Drawn from
        // distance-to-nearest-edge so the corners mitre themselves, and every edge band
        // is constant along its length so 9-slice stretching stays clean.
        const int FrameSize = 64, FrameBorder = 20;
        static Sprite _frame;
        public static Sprite Frame
        {
            get
            {
                if (_frame != null) return _frame;
                const int S = FrameSize;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var clear = new Color(0, 0, 0, 0);
                var dark  = new Color(0.05f, 0.03f, 0.06f, 1f);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(S - 1 - x, S - 1 - y));
                        Color c = d <= 1 ? dark
                                : d == 2 || d == 3 ? Gold
                                : d <= 5 ? dark
                                : d == 6 ? new Color(Blood.r * 0.7f, Blood.g * 0.7f, Blood.b * 0.7f, 1f)
                                : d <= 8 ? dark
                                : clear;

                        // Corner diamonds — inside the 9-slice corner block only, so
                        // they never smear when the frame is stretched.
                        int cx = x < S / 2 ? x : S - 1 - x, cy = y < S / 2 ? y : S - 1 - y;
                        if (cx < FrameBorder && cy < FrameBorder)
                        {
                            int md = Mathf.Abs(cx - 10) + Mathf.Abs(cy - 10);
                            if (md <= 4) c = Blood;
                            else if (md <= 6) c = Gold;
                        }
                        tex.SetPixel(x, y, c);
                    }
                tex.Apply();
                _frame = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S, 0,
                    SpriteMeshType.FullRect,
                    new Vector4(FrameBorder, FrameBorder, FrameBorder, FrameBorder));
                return _frame;
            }
        }

        // A single gold-rimmed blood diamond — the little ornament the artwork sets into
        // the middle of a frame's top and bottom rails, and either side of a caption.
        static Sprite _diamond;
        public static Sprite Diamond
        {
            get
            {
                if (_diamond != null) return _diamond;
                const int S = 32;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                float c = (S - 1) / 2f;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float md = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                        Color col = md <= c * 0.62f ? Blood
                                  : md <= c * 0.92f ? Gold
                                                    : new Color(0, 0, 0, 0);
                        tex.SetPixel(x, y, col);
                    }
                tex.Apply();
                _diamond = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _diamond;
            }
        }

        // ==================== screen furniture ====================

        /// <summary>
        /// Lay the castle ground over a whole overlay: opaque near-black stone, a faint
        /// tiled masonry grain, a blood-moon glow up in the corner and the ornate outer
        /// border. Opaque is the point — whatever screen was behind must not show
        /// through (the Crypt Shop used to sit on a 94% wash with the painted main menu
        /// still visible underneath it).
        /// </summary>
        /// <summary>
        /// Wear the ornate border WITHOUT an opaque ground — for overlays that must
        /// still show the level behind them (pause, the death/win result screens).
        /// </summary>
        public static void FrameOnly(Transform root, float pad = 26f)
        {
            // Overlay() already hangs a plain nine-sliced frame on the panel. Ours is
            // richer, so drop that first — two borders stacked at the same inset read
            // as one doubled, muddy edge.
            var stale = root.Find("Frame");
            if (stale != null) { stale.gameObject.SetActive(false); Object.Destroy(stale.gameObject); }
            Border(root, pad);
        }

        public static void Backdrop(Transform root)
        {
            var stale = root.Find("Frame");
            if (stale != null) { stale.gameObject.SetActive(false); Object.Destroy(stale.gameObject); }

            Fill(root, "Ground", Ground, Vector2.zero, Vector2.one);

            // Masonry grain, kept very low so it reads as texture and not as brickwork.
            var stone = Fill(root, "Stone", new Color(0.55f, 0.5f, 0.62f, 0.10f), Vector2.zero, Vector2.one);
            stone.sprite = Theme.StoneTile;
            stone.type = Image.Type.Tiled;
            stone.pixelsPerUnitMultiplier = 0.14f;

            // The blood moon, top-right, echoing the painted screens.
            var moon = new GameObject("Moon", typeof(RectTransform)).AddComponent<Image>();
            moon.transform.SetParent(root, false);
            moon.sprite = Theme.Moon; moon.raycastTarget = false;
            moon.color = new Color(0.55f, 0.06f, 0.09f, 0.34f);
            var mrt = moon.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.82f, 0.84f);
            mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.sizeDelta = new Vector2(560, 560);

            // The castle in silhouette along the bottom, the way the main-menu
            // painting frames its screen. Tinted almost to black so it reads as a
            // skyline behind the content, never as a picture competing with it.
            var castle = Assets.Sprite("bg_castle");
            if (castle != null)
            {
                for (int s = 0; s < 2; s++)   // one either side, the right one mirrored
                {
                    var c = new GameObject("Castle", typeof(RectTransform)).AddComponent<Image>();
                    c.transform.SetParent(root, false);
                    c.sprite = castle; c.raycastTarget = false; c.preserveAspect = true;
                    c.color = new Color(0.30f, 0.06f, 0.10f, 0.5f);
                    var crt = c.rectTransform;
                    crt.anchorMin = crt.anchorMax = new Vector2(s == 0 ? 0.16f : 0.86f, 0.20f);
                    crt.pivot = new Vector2(0.5f, 0.5f);
                    crt.sizeDelta = new Vector2(900, 508);
                    if (s == 1) crt.localScale = new Vector3(-1f, 1f, 1f);
                }
            }

            Border(root, 26);
        }

        // How thick a 9-sliced border renders, in canvas units:
        //     thickness = borderTexels / (spritePPU/refPPU * pixelsPerUnitMultiplier)
        //              = 20 / (64/100 * m)  =  31.25 / m
        // so a BIGGER multiplier means a THINNER border. Getting this backwards makes
        // a card's frame thicker than the card, which is exactly what it looks like.
        public const float ScreenFrameMul = 0.34f;   // ≈ 92 units — a heavy outer frame
        public const float PlateFrameMul  = 3.0f;    // ≈ 10 units — a card/button hairline
        public const float RingFrameMul   = 2.3f;    // ≈ 14 units — a bolder "equipped" ring

        /// <summary>The ornate border, inset by `pad` canvas units from its parent.</summary>
        public static Image Border(Transform root, float pad)
        {
            var img = Fill(root, "Border", Color.white, Vector2.zero, Vector2.one);
            img.sprite = Frame;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ScreenFrameMul;   // corner ornaments read at fullscreen
            var rt = img.rectTransform;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return img;
        }

        /// <summary>
        /// The screen heading in the artwork's voice: the dripping-blood display face in
        /// crimson over a near-black drop shadow, with the sub-caption and its pair of
        /// diamonds beneath. Returns the sub-caption so live screens can update it.
        ///
        /// Anchored to the TOP EDGE, not the centre. The canvas scales on a mix of width
        /// and height, so a tall phone leaves far less than the reference 1080 of
        /// vertical room — centre-offset chrome walks off the top and bottom of the
        /// screen there. Pinning the heading to the top and BACK to the bottom keeps
        /// both on screen on every device.
        /// </summary>
        public static Text Heading(Transform root, string title, string subtitle, float drop = 86f)
        {
            var top = new Vector2(0.5f, 1f);
            var shadow = Theme.Label(root, title, 62, new Color(0, 0, 0, 0.85f), top,
                new Vector2(5, -drop - 5), new Vector2(1700, 124));
            shadow.font = Theme.TitleFont; shadow.raycastTarget = false;
            var head = Theme.Label(root, title, 62, Theme.Player, top, new Vector2(0, -drop), new Vector2(1700, 124));
            head.font = Theme.TitleFont; head.raycastTarget = false;

            if (string.IsNullOrEmpty(subtitle)) return null;
            var sub = Theme.Label(root, subtitle, 26, Faint, top, new Vector2(0, -drop - 72), new Vector2(1500, 44));
            if (Theme.MenuFont != null) sub.font = Theme.MenuFont;
            sub.raycastTarget = false;
            // A diamond either side of the caption, as the artwork sets them.
            for (int s = -1; s <= 1; s += 2)
            {
                float w = Mathf.Min(sub.preferredWidth, 1400f);
                var dm = new GameObject("Dia", typeof(RectTransform)).AddComponent<Image>();
                dm.transform.SetParent(root, false);
                dm.sprite = Diamond; dm.color = Color.white; dm.raycastTarget = false;
                var drt = dm.rectTransform;
                drt.anchorMin = drt.anchorMax = top; drt.pivot = new Vector2(0.5f, 0.5f);
                drt.anchoredPosition = new Vector2(s * (w / 2f + 34f), -drop - 72);
                drt.sizeDelta = new Vector2(18, 18);
            }
            return sub;
        }

        /// <summary>
        /// A framed plate: dark interior + the ornate border. The building block for a
        /// card, a tab, or anything the artwork would have painted as its own panel.
        /// Returns the plate's transform so callers can parent content into it.
        /// </summary>
        public static Image PlateAt(Transform parent, Vector2 pos, Vector2 size, Color fill, float framePad = 0f)
        {
            var go = new GameObject("Plate", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = fill; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            InnerFrame(go.transform, framePad);
            return img;
        }

        /// <summary>The ornate border sized to fill its parent — used inside plates/buttons.</summary>
        public static Image InnerFrame(Transform parent, float pad = 0f)
        {
            var img = Fill(parent, "Frame", Color.white, Vector2.zero, Vector2.one);
            img.sprite = Frame;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = PlateFrameMul;   // a hairline, not a slab
            var rt = img.rectTransform;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return img;
        }

        /// <summary>
        /// A framed menu button in the artwork's style. `primary` wears the blood fill
        /// (the CONTINUE / BACK plates); everything else is near-black with gold type.
        /// Pass `fill` when a card needs its own state colour (owned / affordable / worn).
        /// </summary>
        public static Button Button(Transform parent, string text, Vector2 pos, Vector2 size,
            System.Action onClick, bool primary = false, int fontSize = 34, Vector2 anchor = default,
            Color? fill = null)
        {
            if (anchor == default) anchor = new Vector2(0.5f, 0.5f);
            var go = new GameObject("GBtn_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            var fillCol = fill ?? (primary ? new Color(0.30f, 0.035f, 0.055f, 1f) : Plate);
            img.color = fillCol;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(fillCol, Blood, 0.45f);
            colors.pressedColor = Color.Lerp(fillCol, Color.black, 0.35f);
            colors.disabledColor = new Color(fillCol.r * 0.6f, fillCol.g * 0.6f, fillCol.b * 0.6f, 1f);
            colors.fadeDuration = 0.07f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            InnerFrame(go.transform);
            var label = Theme.Label(go.transform, text, fontSize, primary ? Bone : Theme.Coin,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 34, size.y - 14));
            if (Theme.MenuFont != null) label.font = Theme.MenuFont;
            label.raycastTarget = false;
            return btn;
        }

        /// <summary>
        /// The long "‹ BACK" plate every screen wears along its bottom rail — pinned to
        /// the bottom edge so it can never fall off a tall phone screen.
        /// </summary>
        public static Button Back(Transform parent, System.Action onClick, float lift = 72f)
            => Button(parent, "‹  BACK", new Vector2(0, lift), new Vector2(420, 88), onClick, true, 38,
                      new Vector2(0.5f, 0f));

        /// <summary>A body line in the menu serif, so code-built copy matches the paintings.</summary>
        public static Text Line(Transform parent, string text, int size, Color color,
            Vector2 pos, Vector2 dim, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = Theme.Label(parent, text, size, color, new Vector2(0.5f, 0.5f), pos, dim, align);
            if (Theme.MenuFont != null) t.font = Theme.MenuFont;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false;
            return t;
        }

        // A stretched Image filling its parent between two anchor corners.
        static Image Fill(Transform parent, string name, Color color, Vector2 min, Vector2 max)
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
    }
}
