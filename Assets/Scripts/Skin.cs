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
            go.transform.SetAsFirstSibling();          // behind all live overlays
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
