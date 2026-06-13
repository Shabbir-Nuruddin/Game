using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WordBloom
{
    /// <summary>
    /// One place for the game's identity: name, colours, fonts, and the small
    /// UI/animation helpers everything else reuses. Change the look here once
    /// and it updates everywhere. (Renaming the game = change Name below.)
    /// </summary>
    public static class Brand
    {
        // The working title. Easy to swap later — it only lives here.
        public const string Name = "WordBloom";
        public const string Tagline = "Grow your words.";

        // ---- Palette: warm, cozy, high-contrast (mass-appeal + senior-friendly) ----
        public static readonly Color Bg        = Hex("FFF6E9"); // warm cream
        public static readonly Color BgDeep    = Hex("F4E3C8"); // panel cream
        public static readonly Color Ink       = Hex("3A322B"); // soft near-black
        public static readonly Color InkSoft   = Hex("8A7C6A"); // muted text
        public static readonly Color Primary   = Hex("E8743B"); // warm orange (brand)
        public static readonly Color PrimaryDk = Hex("C85A28");
        public static readonly Color Leaf      = Hex("6FB36A"); // calm green
        public static readonly Color Gold      = Hex("F2B544"); // coins / stars
        public static readonly Color Tile      = Hex("FFFFFF");
        public static readonly Color TileEmpty = Hex("EAD9BD");
        public static readonly Color Wheel     = Hex("8C5A3B"); // letter wheel base
        public static readonly Color Key       = Hex("FBEFD8");

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

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }

        // ---------------- UI factory helpers ----------------

        public static Image Panel(Transform parent, Color color, string name = "Panel")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size,
            FontStyle style = FontStyle.Bold, Color? color = null, string name = "Label")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color ?? Ink;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static (Image img, Button btn, Text label) Button(
            Transform parent, string text, Color bg, int size, Color? textColor = null)
        {
            var img = Panel(parent, bg, "Button_" + text);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            // A gentle press feedback that doesn't need art.
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.12f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            var label = Label(img.transform, text, size, FontStyle.Bold, textColor ?? Color.white);
            Stretch(label.rectTransform);
            return (img, btn, label);
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor,
            Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        // Round-ish corners without art: add a soft shadow + subtle outline.
        public static void Soften(Image img, Color? outline = null)
        {
            var sh = img.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.18f);
            sh.effectDistance = new Vector2(0, -6);
            var ol = img.gameObject.AddComponent<Outline>();
            ol.effectColor = outline ?? new Color(0, 0, 0, 0.08f);
            ol.effectDistance = new Vector2(2, 2);
        }

        // ---------------- Animation (juice) ----------------

        /// <summary>Pop a transform in with an overshoot bounce.</summary>
        public static IEnumerator PopIn(Transform t, float dur = 0.28f, float delay = 0f)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            float e = 0f;
            t.localScale = Vector3.zero;
            while (e < dur)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / dur);
                t.localScale = Vector3.one * EaseOutBack(k);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        /// <summary>Quick scale "punch" for feedback on a correct action.</summary>
        public static IEnumerator Punch(Transform t, float amount = 0.18f, float dur = 0.22f)
        {
            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / dur);
                float s = 1f + Mathf.Sin(k * Mathf.PI) * amount;
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        /// <summary>Horizontal shake to say "no" (wrong word).</summary>
        public static IEnumerator Shake(RectTransform rt, float amount = 24f, float dur = 0.35f)
        {
            Vector2 home = rt.anchoredPosition;
            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float k = e / dur;
                float x = Mathf.Sin(k * Mathf.PI * 6f) * amount * (1f - k);
                rt.anchoredPosition = home + new Vector2(x, 0);
                yield return null;
            }
            rt.anchoredPosition = home;
        }

        /// <summary>Fade a CanvasGroup from a to b.</summary>
        public static IEnumerator Fade(CanvasGroup g, float a, float b, float dur = 0.25f)
        {
            float e = 0f;
            g.alpha = a;
            while (e < dur)
            {
                e += Time.deltaTime;
                g.alpha = Mathf.Lerp(a, b, Mathf.Clamp01(e / dur));
                yield return null;
            }
            g.alpha = b;
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = 2.70158f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
