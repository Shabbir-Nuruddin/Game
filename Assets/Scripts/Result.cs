using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// THE DEATH SCREEN AND THE PAUSE SCREEN, built to the design pack's mockup.
    ///
    /// Both were the same shape before: a heading, a line of text, and a column of
    /// buttons. For a rage game that is the wrong shape — the screen a player sees
    /// most often was the one telling them least. The design turns death into a
    /// scoreboard: how far you got, how that compares to your own record, what
    /// actually killed you, what it paid, and a single enormous way back in that
    /// never passes through a menu.
    ///
    /// Everything here is laid out in the mockup's own 1600x900 frame, top-left
    /// origin, using the numbers straight off the design. GameRoot wraps it in a
    /// scaled fit-surface, so those numbers mean the same thing on every screen and
    /// there is no second set of "phone" offsets to keep in sync.
    /// </summary>
    public static class Result
    {
        // ---- the design's palette, sampled from the mockup ----------------------
        static readonly Color Gold     = Theme.Hex("C9A24B");
        static readonly Color GoldLit  = Theme.Hex("E8C87A");
        static readonly Color Bone     = Theme.Hex("F0E2CE");
        static readonly Color BoneLit  = Theme.Hex("FFEDDC");
        static readonly Color Blood    = Theme.Hex("B9152A");
        static readonly Color BloodHot = Theme.Hex("E01834");
        static readonly Color BloodLit = Theme.Hex("E0384C");
        static readonly Color Ember    = Theme.Hex("8E0C1A");
        static readonly Color Caption  = Theme.Hex("8A7068");
        static readonly Color Faint    = Theme.Hex("7A625C");
        static readonly Color Rail     = Theme.Hex("7A5A2C");
        static readonly Color RailDim  = Theme.Hex("5A3A24");
        static readonly Color Ink      = Theme.Hex("0A0409");
        static readonly Color PlateTop = new Color(22 / 255f, 8 / 255f, 12 / 255f, 0.95f);
        static readonly Color BtnFace  = new Color(30 / 255f, 10 / 255f, 16 / 255f, 0.95f);

        public const float DesignW = 1600f, DesignH = 900f;

        // ==================== the data each screen needs ====================

        /// <summary>Everything the death screen puts on the wall. GameRoot fills it.</summary>
        public class DeathInfo
        {
            public string modeLabel = "ENDLESS NIGHT";  // top-left caption
            public string runLabel = "RUN 1";           // …and the line under it
            public int blood, earned;                   // balance, and what this run paid
            public string headline = "YOU PERISHED";
            public string unit = "METRES";              // METRES for Endless, NIGHTS for Blood Moon
            public int score, best;                     // this run, and your record
            public string verdict = "";                 // the taunt under the number
            public bool newBest;
            public string killedBy = "THE DARK";
            public string timeBelow = "0:00";
            public System.Action retry, share, curse, leaderboard, menu;
            public string retryLabel = "DESCEND AGAIN";
            public string retrySub = "NEW SEED  ·  NO MENU";
        }

        /// <summary>The pause screen's four actions and its two quick switches.</summary>
        public class PauseInfo
        {
            public System.Action resume, restart, endRun, menu;
            public string restartSub = "THIS FLOOR, FROM THE TOP";
        }

        // ==================== DEATH ====================

        public static void Death(Transform surface, DeathInfo d)
        {
            Ground(surface);
            Vignette(surface, new Color(58 / 255f, 3 / 255f, 14 / 255f, 0.80f));
            Frame(surface);

            // ---- header strip (mockup: left/right 82, top 62) --------------------
            Label(surface, 82, 62, 400, 14, d.modeLabel, 15, Caption, TextAnchor.UpperLeft);
            Label(surface, 82, 83, 400, 20, d.runLabel, 22, Gold, TextAnchor.UpperLeft, true);

            // The blood chip, right-aligned: a drop, the balance, and what tonight paid.
            var chip = Box(surface, "BloodChip", 1518 - 300, 56, 300, 40);
            Fill(chip, new Color(14 / 255f, 6 / 255f, 10 / 255f, 0.9f), Rail);
            var drop = Crimson.Img(chip, "Drop", Theme.Circle, Theme.Hex("C21024"));
            Crimson.Place(drop, new Vector2(0f, 0.5f), new Vector2(26, 0), new Vector2(17, 21));
            LabelIn(chip, 44, 0, 110, 40, d.blood.ToString("N0"), 24, GoldLit, TextAnchor.MiddleLeft, true);
            LabelIn(chip, 150, 0, 138, 40, $"+{d.earned} THIS RUN", 14, Faint, TextAnchor.MiddleRight);

            // ---- the headline, in the dripping face ------------------------------
            // The mockup gives it a red bloom, not a drop shadow. A hard offset copy
            // was tried first and read as a printing error — the dripping face already
            // has an outline of its own, so a second silhouette behind it just doubles
            // every letter. A soft blood halo does the lifting instead.
            var bloom = Crimson.Img(surface, "HeadGlow", Crimson.Halo,
                                    new Color(224 / 255f, 24 / 255f, 52 / 255f, 0.30f));
            Place(bloom.rectTransform, DesignW * 0.5f, 166, 1200, 340);
            var head = Label(surface, 0, 116, DesignW, 100, d.headline, 92, BloodHot,
                             TextAnchor.MiddleCenter, true);
            if (Theme.TitleFont != null) head.font = Theme.TitleFont;

            // ---- the number that matters ----------------------------------------
            Label(surface, 0, 234, DesignW, 16, "YOU SURVIVED", 15, Caption, TextAnchor.MiddleCenter);
            // Number and unit sit side by side on a shared baseline. Two boxes butted
            // against the centre rather than one string, so the huge figure keeps its
            // own weight and the unit stays small beside it.
            //
            // The unit's box is dropped 16 below the number's: bottom-aligning two very
            // different font sizes lines up their DESCENDER boxes, not their baselines,
            // which left METRES visibly sitting in a hole beside the figure.
            Label(surface, DesignW * 0.5f - 470, 256, 460, 120, d.score.ToString("N0"), 104,
                  Bone, TextAnchor.LowerRight, true);
            Label(surface, DesignW * 0.5f + 16, 240, 400, 120, d.unit, 38, Blood,
                  TextAnchor.LowerLeft, true);
            if (!string.IsNullOrEmpty(d.verdict))
                Label(surface, 0, 392, DesignW, 26, d.verdict, 22, Theme.Hex("D9B25E"),
                      TextAnchor.MiddleCenter);

            // ---- this run against your record ------------------------------------
            const float ColX = 400f, ColW = 800f;
            Label(surface, ColX, 454, 300, 14, "THIS RUN", 15, Caption, TextAnchor.UpperLeft);
            Label(surface, ColX, 474, 300, 30, $"{d.score:N0}{Suffix(d.unit)}", 27, BloodLit,
                  TextAnchor.UpperLeft, true);
            Label(surface, ColX + ColW - 300, 454, 300, 14, "YOUR RECORD", 15, Caption, TextAnchor.UpperRight);
            Label(surface, ColX + ColW - 300, 474, 300, 30, $"{d.best:N0}{Suffix(d.unit)}", 27, GoldLit,
                  TextAnchor.UpperRight, true);
            // The middle verdict: how far short, or that there's nothing left to beat.
            bool beat = d.newBest || d.score >= d.best;
            string gap = beat ? "NEW RECORD"
                              : $"{(d.best - d.score):N0}{Suffix(d.unit)} SHORT";
            Label(surface, ColX + 200, 470, ColW - 400, 26, gap, 21, beat ? GoldLit : Caption,
                  TextAnchor.MiddleCenter, true);

            // The bar. It fills to this run's share of the record, and the gold tick
            // at the far right IS the record — so the empty stretch between them is
            // the thing you came back to close.
            var trough = Box(surface, "Bar", ColX, 516, ColW, 14);
            Fill(trough, Ink, RailDim);
            float pct = d.best > 0 ? Mathf.Clamp01((float)d.score / d.best) : (d.score > 0 ? 1f : 0f);
            var fill = Crimson.Img(trough, "Fill", null, BloodHot);
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(pct, 1);
            frt.offsetMin = new Vector2(1, 1); frt.offsetMax = new Vector2(-1, -1);
            var tick = Crimson.Img(trough, "Tick", null, GoldLit);
            var trt = tick.rectTransform;
            trt.anchorMin = new Vector2(1, 0); trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(1, 0.5f);
            trt.offsetMin = new Vector2(-3, -6); trt.offsetMax = new Vector2(0, 6);

            // ---- the three-cell breakdown ----------------------------------------
            var cells = Box(surface, "Stats", ColX, 552, ColW, 74);
            Fill(cells, PlateTop, RailDim);
            (string k, string v, Color ink)[] stats =
            {
                ("KILLED BY",    d.killedBy,        BloodLit),
                ("TIME BELOW",   d.timeBelow,       Bone),
                ("BLOOD EARNED", $"+{d.earned}",    GoldLit),
            };
            for (int i = 0; i < stats.Length; i++)
            {
                float cw = ColW / 3f;
                LabelIn(cells, i * cw + 22, 14, cw - 44, 14, stats[i].k, 15, Caption, TextAnchor.UpperLeft);
                LabelIn(cells, i * cw + 22, 36, cw - 44, 26, stats[i].v, 23, stats[i].ink, TextAnchor.UpperLeft, true);
                if (i < stats.Length - 1)   // hairline divider between cells
                {
                    var div = Crimson.Img(cells, "Div", null, Theme.Hex("33201E"));
                    Crimson.Place(div, new Vector2(0f, 0.5f), new Vector2((i + 1) * cw, 0), new Vector2(1, 74));
                }
            }

            // ---- the one big way back in ------------------------------------------
            BloodPlate(surface, 518, 654, 564, 82, d.retryLabel, 34, d.retrySub, d.retry);

            // ---- and the four smaller ways out --------------------------------------
            (string label, System.Action go)[] nav =
            {
                ("SHARE", d.share), ("CURSE A FRIEND", d.curse),
                ("LEADERBOARD", d.leaderboard), ("MAIN MENU", d.menu),
            };
            const float NavW = 216f, NavGap = 14f;
            float navX = (DesignW - (nav.Length * NavW + (nav.Length - 1) * NavGap)) * 0.5f;
            for (int i = 0; i < nav.Length; i++)
                GoldPlate(surface, navX + i * (NavW + NavGap), 756, NavW, 54,
                          nav[i].label, 17, null, nav[i].go);

            Label(surface, 0, DesignH - 60, DesignW, 16,
                  "TAP DESCEND AGAIN TO GO STRAIGHT BACK DOWN", 13, Theme.Hex("6E5A54"),
                  TextAnchor.MiddleCenter);
        }

        // "m" for metres, nothing for a night count.
        static string Suffix(string unit) => unit == "METRES" ? "m" : "";

        // ==================== PAUSE ====================

        public static void Paused(Transform surface, PauseInfo p)
        {
            Ground(surface);
            Vignette(surface, new Color(20 / 255f, 4 / 255f, 10 / 255f, 0.78f));
            Frame(surface);

            bool endless = p.endRun != null;
            float panelX = (DesignW - 540f) * 0.5f;
            var panel = Box(surface, "PausePanel", panelX, 0f, 540f, 100f);
            Fill(panel, new Color(24 / 255f, 9 / 255f, 13 / 255f, 0.97f), Rail);
            Studs(panel, 11f, Blood, -6f);

            // Header band — a lit strip across the top of the panel, as the mockup has
            // it. 118 tall, not 96: PAUSED is set in the dripping face, and its drips
            // ran straight down into the line underneath at the shorter height.
            var band = Box(panel, "Band", 0, 0, 540, 118);
            band.anchorMin = band.anchorMax = new Vector2(0f, 1f);
            Fill(band, new Color(120 / 255f, 10 / 255f, 24 / 255f, 0.30f), Theme.Hex("4A2A22"));
            var paused = LabelIn(band, 0, 20, 540, 48, "PAUSED", 46, Bone, TextAnchor.MiddleCenter, true);
            if (Theme.TitleFont != null) paused.font = Theme.TitleFont;
            LabelIn(band, 0, 88, 540, 16, "THE CASTLE IS WAITING. IT DOESN'T MIND.", 14,
                    Caption, TextAnchor.MiddleCenter);

            float y = 144f;
            BloodPlateIn(panel, 44, y, 452, 78, "RESUME", 30, "ESC  ·  NOTHING LOST", p.resume);
            y += 78f + 13f;
            GoldPlateIn(panel, 44, y, 452, 62, "RESTART FLOOR", 21, p.restartSub, p.restart);
            y += 62f + 13f;
            if (endless)
            {
                GoldPlateIn(panel, 44, y, 452, 62, "END RUN", 21, "BANK THE DISTANCE YOU HAVE", p.endRun);
                y += 62f + 13f;
            }
            GoldPlateIn(panel, 44, y, 452, 62, "MAIN MENU", 21, "THIS RUN IS LOST", p.menu);
            y += 62f + 16f;

            // The two quick switches. Mid-run is exactly when someone needs the sound
            // off in a hurry, and making them walk out to Settings to do it is how you
            // lose the session instead of the noise.
            SwitchIn(panel, 44, y, 220, 40, "MUSIC", () => Audio.MusicVol > 0.01f,
                     on => Audio.MusicVol = on ? 1f : 0f);
            SwitchIn(panel, 276, y, 220, 40, "SOUND", () => Audio.SfxVol > 0.01f,
                     on => Audio.SfxVol = on ? 1f : 0f);

            // The panel is sized from its contents rather than guessed at, then centred
            // on what it turned out to be — Endless has one row more than the Castle,
            // and a fixed y left one of the two sitting high on the screen.
            float finalH = y + 40f + 26f;
            panel.sizeDelta = new Vector2(540f, finalH);
            panel.anchoredPosition = new Vector2(panelX, -(DesignH - finalH) * 0.5f);
        }

        // ==================== the design's furniture ====================

        /// <summary>
        /// The black these screens are painted on.
        ///
        /// Not decoration — a necessity. The shared Overlay draws an ornate nine-slice
        /// frame whose CENTRE is stone, so any screen that doesn't lay its own opaque
        /// ground over it inherits a big brown field behind the type. This design
        /// brings its own gold-and-blood frame, so it wants that one gone.
        ///
        /// Deliberately oversized: the fit-surface is smaller than the canvas on most
        /// devices, and the ground has to reach the screen edge, not the surface edge.
        /// </summary>
        static void Ground(Transform surface)
        {
            var img = Crimson.Img(surface, "Ground", null, Theme.Hex("050307"));
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(DesignW * 2.4f, DesignH * 2.4f);
        }

        // The full-bleed blood vignette both screens sit in.
        static void Vignette(Transform surface, Color core)
        {
            var wash = Crimson.Img(surface, "Vignette", Crimson.Halo, core);
            var rt = wash.rectTransform;
            // Oversized so the soft edge of the halo falls outside the frame instead
            // of ending in a visible circle.
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, DesignH * 0.06f);
            rt.sizeDelta = new Vector2(DesignW * 1.9f, DesignH * 2.1f);
        }

        // Gold rule inset 26, backed by a blood ring — the border the whole design
        // vocabulary hangs off, with a lit diamond in each corner.
        static void Frame(Transform surface)
        {
            Edge(surface, 26f, 2f, Gold);
            Edge(surface, 34f, 2f, Ember);
            (float x, float y)[] corners = { (26, 26), (DesignW - 26, 26), (26, DesignH - 26), (DesignW - 26, DesignH - 26) };
            foreach (var c in corners)
            {
                Diamond(surface, c.x, c.y, 34f, Blood, Gold);
                Diamond(surface, c.x, c.y, 12f, Theme.Hex("F04058"), default);
            }
        }

        // Four hairlines forming a rectangle inset by `inset`.
        static void Edge(Transform surface, float inset, float w, Color col)
        {
            void Bar(string n, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
            {
                var img = Crimson.Img(surface, n, null, col);
                var rt = img.rectTransform;
                rt.anchorMin = aMin; rt.anchorMax = aMax;
                rt.offsetMin = offMin; rt.offsetMax = offMax;
            }
            Bar("T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(inset, -inset - w), new Vector2(-inset, -inset));
            Bar("B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(inset, inset), new Vector2(-inset, inset + w));
            Bar("L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(inset, inset), new Vector2(inset + w, -inset));
            Bar("R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-inset - w, inset), new Vector2(-inset, -inset));
        }

        // A square turned 45°, optionally outlined by a slightly larger one behind it.
        static void Diamond(Transform surface, float x, float y, float size, Color fill, Color outline)
        {
            if (outline.a > 0f)
            {
                var o = Crimson.Img(surface, "DiamondEdge", null, outline);
                Place(o.rectTransform, x, y, size + 4f, size + 4f);
                o.rectTransform.localEulerAngles = new Vector3(0, 0, 45f);
            }
            var img = Crimson.Img(surface, "Diamond", null, fill);
            Place(img.rectTransform, x, y, size, size);
            img.rectTransform.localEulerAngles = new Vector3(0, 0, 45f);
        }

        /// <summary>
        /// Small diamonds in the corners of a plate — the design's signature detail.
        /// A POSITIVE inset sits them inside the plate (the gold studs on a button); a
        /// NEGATIVE one hangs them off the corners (the blood pips on a panel).
        /// </summary>
        static void Studs(RectTransform host, float size, Color col, float inset)
        {
            (float ax, float ay)[] at = { (0, 0), (1, 0), (0, 1), (1, 1) };
            foreach (var a in at)
            {
                var img = Crimson.Img(host, "Stud", null, col);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(a.ax, a.ay);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(a.ax == 0 ? inset : -inset, a.ay == 0 ? inset : -inset);
                rt.sizeDelta = new Vector2(size, size);
                rt.localEulerAngles = new Vector3(0, 0, 45f);
            }
        }

        /// <summary>
        /// The primary action: a blood plate with a gold border and four gold studs.
        /// Deliberately enormous — on a death screen the way back into the game
        /// should be the only thing a thumb can reasonably land on.
        /// </summary>
        static void BloodPlate(Transform surface, float x, float y, float w, float h,
                               string label, int size, string sub, System.Action go)
        {
            var host = Box(surface, "Primary_" + label, x, y, w, h);
            BuildBloodPlate(host, label, size, sub, go);
        }

        static void BloodPlateIn(RectTransform parent, float x, float y, float w, float h,
                                 string label, int size, string sub, System.Action go)
        {
            var host = BoxIn(parent, "Primary_" + label, x, y, w, h);
            BuildBloodPlate(host, label, size, sub, go);
        }

        static void BuildBloodPlate(RectTransform host, string label, int size, string sub, System.Action go)
        {
            Fill(host, Ember, Gold, 2f);
            Studs(host, 9f, GoldLit, 8f);
            var t = LabelIn(host, 0, sub == null ? 0 : host.sizeDelta.y * 0.5f - 30f,
                            host.sizeDelta.x, 40, label, size, BoneLit, TextAnchor.MiddleCenter, true);
            if (Theme.MenuFont != null) t.font = Theme.MenuFont;
            if (sub != null)
                LabelIn(host, 0, host.sizeDelta.y * 0.5f + 8f, host.sizeDelta.x, 18, sub, 13,
                        Theme.Hex("E09A94"), TextAnchor.MiddleCenter);
            Clickable(host, go, Ember, Theme.Hex("B4101F"));
        }

        /// <summary>A secondary action: dark face, gold rule, gold type.</summary>
        static void GoldPlate(Transform surface, float x, float y, float w, float h,
                              string label, int size, string sub, System.Action go)
        {
            var host = Box(surface, "Nav_" + label, x, y, w, h);
            BuildGoldPlate(host, label, size, sub, go);
        }

        static void GoldPlateIn(RectTransform parent, float x, float y, float w, float h,
                                string label, int size, string sub, System.Action go)
        {
            var host = BoxIn(parent, "Nav_" + label, x, y, w, h);
            BuildGoldPlate(host, label, size, sub, go);
        }

        static void BuildGoldPlate(RectTransform host, string label, int size, string sub, System.Action go)
        {
            Fill(host, BtnFace, Theme.Hex("8A6E34"));
            float h = host.sizeDelta.y;
            var t = LabelIn(host, 0, sub == null ? 0 : h * 0.5f - 24f, host.sizeDelta.x, 30,
                            label, size, GoldLit, TextAnchor.MiddleCenter, true);
            if (Theme.MenuFont != null) t.font = Theme.MenuFont;
            if (sub != null)
                LabelIn(host, 0, h * 0.5f + 6f, host.sizeDelta.x, 16, sub, 12,
                        Theme.Hex("8A6E68"), TextAnchor.MiddleCenter);
            Clickable(host, go, BtnFace, new Color(70 / 255f, 10 / 255f, 22 / 255f, 0.95f));
        }

        /// <summary>A compact on/off cell — the pause screen's quick sound switches.</summary>
        static void SwitchIn(RectTransform parent, float x, float y, float w, float h,
                             string label, System.Func<bool> get, System.Action<bool> set)
        {
            var host = BoxIn(parent, "Switch_" + label, x, y, w, h);
            var face = Fill(host, BtnFace, Theme.Hex("8A6E34"));
            var text = LabelIn(host, 0, 0, w, h, "", 14, GoldLit, TextAnchor.MiddleCenter, true);

            void Paint(bool on)
            {
                text.text = label + (on ? "  ON" : "  OFF");
                text.color = on ? GoldLit : Theme.Hex("6E5A54");
                face.color = on ? new Color(60 / 255f, 10 / 255f, 20 / 255f, 0.95f) : Ink;
            }
            Paint(get());

            var btn = host.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.onClick.AddListener(() =>
            {
                Audio.Play("click", 0.6f);
                bool v = !get();
                set(v);
                Paint(v);
            });
        }

        // Wire a plate up as a button, with the design's hover/press colour.
        static void Clickable(RectTransform host, System.Action go, Color normal, Color lit)
        {
            var face = host.Find("Fill").GetComponent<Image>();
            var btn = host.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(() => { Audio.Play("click"); go?.Invoke(); });
        }

        // ==================== placement primitives ====================
        //
        // Every one of these takes the mockup's own coordinates: x/y from the TOP-LEFT
        // of the design frame, y growing downward. Translating once, here, is what
        // keeps the layout code above readable as a description of the picture.

        static void Place(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, -cy);
            rt.sizeDelta = new Vector2(w, h);
        }

        static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static RectTransform BoxIn(RectTransform parent, string name, float x, float y, float w, float h)
            => Box(parent, name, x, y, w, h);

        // A panel face: a one-pixel edge colour with the fill inset inside it, which
        // is how every framed surface in this design is drawn.
        static Image Fill(RectTransform host, Color face, Color edge, float weight = 1f)
        {
            var edgeImg = host.gameObject.GetComponent<Image>() ?? host.gameObject.AddComponent<Image>();
            edgeImg.sprite = Theme.Square;
            edgeImg.color = edge;
            var go = new GameObject("Fill", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var img = go.AddComponent<Image>();
            img.sprite = Theme.Square;
            img.color = face;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(weight, weight); rt.offsetMax = new Vector2(-weight, -weight);
            return img;
        }

        static Text Label(Transform parent, float x, float y, float w, float h, string s, int size,
                          Color col, TextAnchor align, bool bold = false)
        {
            var box = Box(parent, "T_" + (s.Length > 12 ? s.Substring(0, 12) : s), x, y, w, h);
            return Write(box, s, size, col, align, bold);
        }

        static Text LabelIn(RectTransform parent, float x, float y, float w, float h, string s, int size,
                            Color col, TextAnchor align, bool bold = false)
        {
            var box = BoxIn(parent, "T_" + (s.Length > 12 ? s.Substring(0, 12) : s), x, y, w, h);
            return Write(box, s, size, col, align, bold);
        }

        static Text Write(RectTransform box, string s, int size, Color col, TextAnchor align, bool bold)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(box, false);
            var t = go.AddComponent<Text>();
            t.font = Theme.MenuFont != null ? Theme.MenuFont : Theme.Font;
            t.text = s;
            t.fontSize = size;
            t.color = col;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }
    }
}
