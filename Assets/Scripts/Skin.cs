using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// The "use the exact artwork" skin layer. Each menu screen can be painted with
    /// a full-screen image the user drops into Resources/ui/ (e.g. ui/menu_bg). When
    /// that image is present the screen renders the art itself and we lay ONLY the
    /// live pieces on top — transparent tap-zones over the drawn buttons, and live
    /// text over the spots whose value changes (floor number, shard balance, etc.).
    /// When the image is absent everything falls back to the code-built menu, so the
    /// game never breaks while art is still being added screen-by-screen.
    ///
    /// All positions are given as fractions of the screen measured from the TOP-LEFT
    /// of the artwork (x→right, y→down), exactly how you'd measure them off the
    /// mockup. They're resolution-independent: the background and every overlay are
    /// anchored to the same canvas rect, so they always line up as the art scales.
    /// </summary>
    public static class Skin
    {
        /// <summary>Is a skin image present for this screen? (art/ui/&lt;name&gt;)</summary>
        public static bool Has(string name) => Load(name) != null;

        /// <summary>
        /// A cut from the artwork used as a piece rather than a backdrop (the
        /// "you are here" halo, say). Null when the file isn't there, so callers
        /// can leave the spot bare instead of drawing a placeholder.
        /// </summary>
        public static Sprite Cut(string name) => Load(name);

        static readonly System.Collections.Generic.Dictionary<string, Sprite> _cache = new();
        static Sprite Load(string name)
        {
            if (_cache.TryGetValue(name, out var s)) return s;
            var tex = Resources.Load<Texture2D>("ui/" + name);
            s = tex != null
                ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f)
                : null;
            _cache[name] = s;
            return s;
        }

        /// <summary>
        /// Paint a full-screen background from Resources/ui/&lt;name&gt;. Returns the
        /// Image (drawn behind everything else added afterwards) or null if the file
        /// isn't there yet, so callers can branch to the classic layout.
        /// </summary>
        public static Image Background(Transform root, string name)
        {
            var sp = Load(name);
            if (sp == null) return null;
            var go = new GameObject("Skin_" + name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            // Draw the art IN FRONT of whatever the Overlay already put down (its dark
            // wash + code-built ornate frame) so those don't tint the picture — the
            // artwork supplies its own frame. Tap-zones/live text are added by the
            // caller AFTER this, so they still land on top of the art.
            go.transform.SetAsLastSibling();
            var img = go.AddComponent<Image>();
            img.sprite = sp;
            img.raycastTarget = false;                 // clicks pass through to the tap-zones
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;   // fill the whole screen
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        // Convert a top-left-origin fraction rect into anchor min/max (Unity's origin
        // is bottom-left, so the vertical axis flips).
        static void Anchors(float x0, float top0, float x1, float top1, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(x0, 1f - top1);
            max = new Vector2(x1, 1f - top0);
        }

        /// <summary>
        /// A transparent, clickable rectangle placed over a button drawn in the art.
        /// The artwork supplies the look; this just catches the tap.
        /// </summary>
        public static Button Zone(Transform root, float x0, float top0, float x1, float top1,
            System.Action onClick, string name = "zone")
        {
            var go = new GameObject("Tap_" + name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0f);        // invisible but raycastable
            Anchors(x0, top0, x1, top1, out var min, out var max);
            var rt = img.rectTransform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            // A faint flash on press so a tap on the painted button still feels alive.
            var colors = btn.colors;
            colors.normalColor = new Color(1, 1, 1, 0f);
            colors.highlightedColor = new Color(1, 1, 1, 0.06f);
            colors.pressedColor = new Color(1, 1, 1, 0.14f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>
        /// The colour of a painted button's interior. Measured off the artwork: the
        /// insides of every framed control are effectively pure black, so a chip in
        /// this colour hides the baked-in text without leaving a visible patch.
        /// </summary>
        public static readonly Color Interior = new Color(0.012f, 0.006f, 0.008f, 1f);
        /// <summary>The crimson the artwork uses for a control's LABEL ("JUMP:").</summary>
        public static readonly Color LabelRed = new Color(0.76f, 0.22f, 0.27f, 1f);
        /// <summary>The warm bone-white the artwork uses for a control's VALUE ("SPACE").</summary>
        public static readonly Color ValueBone = new Color(0.82f, 0.75f, 0.67f, 1f);
        /// <summary>The blood red of the painted volume bars.</summary>
        public static readonly Color BarRed = new Color(0.53f, 0.045f, 0.05f, 1f);

        /// <summary>
        /// An opaque patch that hides a baked-in value in the artwork, so a live one
        /// can be drawn over the top. Never raycasts — taps belong to the Zone below.
        /// </summary>
        public static Image Chip(Transform root, float x0, float top0, float x1, float top1, Color color)
        {
            var go = new GameObject("Chip", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
            Anchors(x0, top0, x1, top1, out var min, out var max);
            var rt = img.rectTransform;
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        /// <summary>
        /// An empty rect positioned by the same top-left fractions as everything else,
        /// for callers that need to build a real widget (a Slider, say) in artwork space.
        /// </summary>
        public static RectTransform Slot(Transform root, string name,
            float x0, float top0, float x1, float top1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = (RectTransform)go.transform;
            Anchors(x0, top0, x1, top1, out var min, out var max);
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Let a caption shrink to stay inside its painted frame. The game's font is
        /// wider than the one in the mockup, and bound key names vary enormously
        /// ("K" vs "BACKSPACE"), so any fixed size would eventually spill out over
        /// the ornate border. Best-fit caps at the artwork's own size and only ever
        /// scales down, so short captions still match the painting exactly.
        /// </summary>
        public static Text Fit(Text t, int maxSize, int minSize = 10)
        {
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = maxSize;
            t.resizeTextMinSize = minSize;
            // Best-fit measures against the rect, so overflow must not be allowed to
            // "solve" the overflow for it.
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>
        /// The caption for a painted control, in the artwork's own two-tone style:
        /// crimson label, bone-white value. Uses rich text so both live in one Text.
        /// </summary>
        public static string Caption(string label, string value) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(LabelRed)}>{label}:</color>" +
            $"  <color=#{ColorUtility.ToHtmlStringRGB(ValueBone)}>{value}</color>";

        /// <summary>
        /// A live value painted OVER a spot where the artwork baked in a sample number
        /// (SHOP 65, FLOOR 1…). Lays an opaque chip to hide the painted text, then the
        /// real value on top — so the menu shows your actual balance/floor/difficulty
        /// instead of a frozen picture. Tune the chip colour to the button's interior.
        /// </summary>
        public static Text LiveValue(Transform root, string text, float x0, float top0, float x1, float top1,
            int size, Color color, Color chip, bool title = false)
        {
            var cg = new GameObject("Chip", typeof(RectTransform));
            cg.transform.SetParent(root, false);
            var ci = cg.AddComponent<Image>();
            ci.color = chip; ci.raycastTarget = false;
            Anchors(x0, top0, x1, top1, out var cmin, out var cmax);
            var crt = ci.rectTransform;
            crt.anchorMin = cmin; crt.anchorMax = cmax; crt.offsetMin = crt.offsetMax = Vector2.zero;
            return LiveText(root, text, x0, top0, x1, top1, size, color, title);
        }

        /// <summary>
        /// Live text laid over the artwork for a value that changes (floor number,
        /// difficulty, shard balance, a nightly line…). Sits in the rect you measured
        /// off the mockup; use the title font for headings via the `title` flag.
        /// </summary>
        public static Text LiveText(Transform root, string text, float x0, float top0, float x1, float top1,
            int size, Color color, bool title = false, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Live", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var t = go.AddComponent<Text>();
            t.font = title ? Theme.TitleFont : Theme.Font;
            t.text = text; t.fontSize = size; t.fontStyle = FontStyle.Bold;
            t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;                   // never eats a tap meant for a zone
            Anchors(x0, top0, x1, top1, out var min, out var max);
            var rt = t.rectTransform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }
    }
}
