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
        // The mockup's third accent. RESTART is neither "carry on" (red) nor "leave"
        // (gold) — it throws away this attempt and only this attempt — so it gets
        // its own stone rather than borrowing one and blurring the other two.
        static readonly Color Violet   = Theme.Hex("7B3FA8");
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

        /// <summary>The pause screen's actions and its two quick switches.</summary>
        public class PauseInfo
        {
            public System.Action resume, restart, endRun, menu;
            // Castle only. Floors now chain straight into each other, so this is
            // where the map lives for anyone who wants to look at their progress
            // rather than being marched through it between every floor.
            public System.Action map;
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
            // The mockup separates the headline from the numbers with a skull sitting
            // between two hairlines, and carries no caption at all — the figure below
            // already reads "6 METRES", so a "YOU SURVIVED" label above it was saying
            // the same thing twice and stealing the divider's line to do it. The rule
            // takes that line instead.
            SkullRule(surface, 242f);
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

            // ---- the three things you might SHARE, in a small row -----------------
            //
            // These used to sit in one flat row of four alongside MAIN MENU, which
            // made leaving the game exactly as prominent as bragging about the run —
            // and put the run's only viral action (CURSE A FRIEND) at the same weight
            // as the button that ends the session. The mockup splits them: the social
            // three stay small and together up here, and the two REAL decisions
            // (go again / stop playing) get the ornate plates underneath.
            (string label, System.Action go)[] social =
            {
                ("SHARE", d.share), ("CURSE A FRIEND", d.curse), ("LEADERBOARD", d.leaderboard),
            };
            const float NavW = 268f, NavGap = 18f;
            float navX = (DesignW - (social.Length * NavW + (social.Length - 1) * NavGap)) * 0.5f;
            for (int i = 0; i < social.Length; i++)
                GoldPlate(surface, navX + i * (NavW + NavGap), 654, NavW, 56,
                          social[i].label, 18, null, social[i].go);

            // ---- and the two that actually decide the session ---------------------
            // Side by side, both ornate, colour-coded the same way the pause screen
            // codes its rows: red carries on, gold walks away.
            const float BigW = 420f, BigH = 88f, BigGap = 36f;
            float bigCy = 776f;
            GemPlate(surface, DesignW * 0.5f - (BigW + BigGap) * 0.5f, bigCy, BigW, BigH,
                     d.retryLabel, 32, BloodHot, d.retry, d.retrySub);
            GemPlate(surface, DesignW * 0.5f + (BigW + BigGap) * 0.5f, bigCy, BigW, BigH,
                     "MAIN MENU", 32, GoldLit, d.menu, "THE CASTLE KEEPS YOUR PLACE");
        }

        /// <summary>
        /// A skull between two hairlines, centred — the death screen's divider.
        /// `cy` is the line the rules sit on, in design coordinates.
        /// </summary>
        static void SkullRule(Transform surface, float cy)
        {
            var faint = new Color(Gold.r, Gold.g, Gold.b, 0.30f);
            foreach (float dir in new[] { -1f, 1f })
            {
                var bar = Crimson.Img(surface, "SkullRule", null, faint);
                Place(bar.rectTransform, DesignW * 0.5f + dir * 300f, cy, 320f, 1.5f);
                bar.raycastTarget = false;
                // A gem where each rule ends, pointing back at the skull.
                var pip = Crimson.Img(surface, "RulePip", null, Blood);
                Place(pip.rectTransform, DesignW * 0.5f + dir * 140f, cy, 9f, 9f);
                pip.rectTransform.localEulerAngles = new Vector3(0, 0, 45f);
                pip.raycastTarget = false;
            }
            var skull = Crimson.Img(surface, "Skull", Gothic.Skull, Gold);
            Place(skull.rectTransform, DesignW * 0.5f, cy, 30f, 30f);
            skull.raycastTarget = false;
        }

        // "m" for metres, nothing for a night count.
        static string Suffix(string unit) => unit == "METRES" ? "m" : "";

        // ==================== PAUSE ====================

        // THE PAUSE SCREEN, rebuilt to the design pack's mockup.
        //
        // WHAT CHANGED AND WHY. The old screen pausedturn you into a 540-wide box
        // floating in a cathedral: a header band, then five or six stacked plates
        // each with a caption under it, then two toggle cells. Everything was the
        // same size and the same two colours, so the eye had nowhere to land and
        // the screen read as a settings dialog — which is exactly the wrong feeling
        // to hand someone in the middle of a run they are enjoying.
        //
        // The mockup does the opposite. No box at all: the castle night IS the
        // screen, the title hangs in it in the dripping face, and there are THREE
        // buttons, colour-coded by what they cost you — resume (red, costs nothing),
        // restart (violet, costs this attempt), leave (gold, costs the run). Fewer,
        // bigger, further apart, and instantly distinguishable at arm's length.
        //
        // Endless and the Castle each add exactly one row, and the whole stack is
        // centred on however many rows it turned out to have.
        public static void Paused(Transform surface, PauseInfo p)
        {
            Ground(surface);
            // The castle night, moon high on the right, exactly as the mockup frames
            // it — and it costs nothing to ship because it is drawn, not photographed.
            Crimson.Backdrop(surface, 300f, -30f, true, 3);
            Vignette(surface, new Color(40 / 255f, 2 / 255f, 10 / 255f, 0.72f));
            Frame(surface);

            // ---- the title, hanging in the night ---------------------------------
            var bloom = Crimson.Img(surface, "PauseGlow", Crimson.Halo,
                                    new Color(224 / 255f, 24 / 255f, 52 / 255f, 0.26f));
            Place(bloom.rectTransform, DesignW * 0.5f, 150f, 1000f, 300f);
            bloom.raycastTarget = false;
            var title = Label(surface, 0, 96f, DesignW, 108f, "PAUSED", 96, BloodHot,
                              TextAnchor.MiddleCenter, true);
            if (Theme.TitleFont != null) title.font = Theme.TitleFont;
            title.raycastTarget = false;

            // ---- the rows this run actually needs --------------------------------
            // Built as a list first so the stack can be centred on its real height.
            // Endless can bank a strong attempt; the Castle can step out to the map.
            var rows = new System.Collections.Generic.List<(string label, Color gem, System.Action go, string sub)>
            {
                ("RESUME", BloodHot, p.resume, null),
                ("RESTART LEVEL", Violet, p.restart, p.restartSub),
            };
            if (p.endRun != null) rows.Add(("END RUN", Gold, p.endRun, "BANK THE DISTANCE YOU HAVE"));
            if (p.map != null) rows.Add(("CASTLE MAP", Gold, p.map, "SEE HOW FAR YOU HAVE GOT"));
            rows.Add(("MAIN MENU", GoldLit, p.menu, "THIS RUN IS LOST"));

            const float BtnW = 700f, BtnH = 96f, Gap = 30f;
            float stackH = rows.Count * BtnH + (rows.Count - 1) * Gap;
            // Centred in the space BELOW the title rather than on the whole screen,
            // so a two-row Castle pause and a four-row Endless one both sit right.
            float top = 300f + Mathf.Max(0f, (DesignH - 300f - 96f - stackH) * 0.5f);

            for (int i = 0; i < rows.Count; i++)
            {
                float cy = top + i * (BtnH + Gap) + BtnH * 0.5f;
                var r = rows[i];
                // The divider above each button, and one under the last — the
                // mockup's rhythm. Drawn on the surface so hovering never nudges it.
                GemRule(surface as RectTransform ?? (RectTransform)surface,
                        DesignW * 0.5f, cy - BtnH * 0.5f - Gap * 0.5f, BtnW * 0.86f, r.gem);
                GemPlate(surface, DesignW * 0.5f, cy, BtnW, BtnH, r.label, 38, r.gem, r.go, r.sub);
            }
            GemRule(surface as RectTransform ?? (RectTransform)surface, DesignW * 0.5f,
                    top + stackH + Gap * 0.5f, BtnW * 0.86f, rows[rows.Count - 1].gem);

            // ---- the two quick switches ------------------------------------------
            // Mid-run is exactly when someone needs the sound off in a hurry, and
            // sending them out to Settings for it loses the session, not the noise.
            // Tucked into the bottom corner now instead of sitting in the main stack,
            // where they were competing with RESUME for the same glance.
            SwitchIn((RectTransform)surface, DesignW * 0.5f - 214f, DesignH - 92f, 200f, 38f,
                     "MUSIC", () => Audio.MusicVol > 0.01f, on => Audio.MusicVol = on ? 1f : 0f);
            SwitchIn((RectTransform)surface, DesignW * 0.5f + 14f, DesignH - 92f, 200f, 38f,
                     "SOUND", () => Audio.SfxVol > 0.01f, on => Audio.SfxVol = on ? 1f : 0f);
        }

        // ==================== THE HALL (pause backdrop) ====================
        //
        // The mockup's pause screen is a place, drawn entirely out of gradients — no
        // photograph, so it costs nothing to ship and stays crisp at any size. Six
        // stained-glass arches down a colonnade, a rose window bleeding through the
        // top wall, three shafts of light with dust turning over in them, a stone
        // floor in perspective and two sconces guttering on the near piers.
        //
        // Every number below is the mockup's own, in its 1600x900 frame.

        static void Hall(Transform surface)
        {
            // One clipped container. The rose window is hung above the top edge and
            // the embers drift past it, so without this they'd spill outside the
            // gold frame and onto the letterbox.
            var hall = Box(surface, "Hall", 0, 0, DesignW, DesignH);
            hall.gameObject.AddComponent<RectMask2D>();

            // The room's own darkness, warmest through the middle third.
            HallImg(hall, "Wash", Crimson.Halo, A(Theme.Hex("1A0710"), 0.55f),
                    DesignW * 0.5f, 306, 1900, 780);

            RoseWindow(hall);
            Arcade(hall);

            // ---- shafts of light through the windows -----------------------------
            (float x, float w, float rot, float dur)[] shafts =
            {
                (170, 150,  12f, 7f), (720, 190, -4f, 9f), (1240, 150, -12f, 8f),
            };
            foreach (var s in shafts)
            {
                var img = HallImg(hall, "Shaft", Shaft, A(Theme.Hex("FFB482"), 0.32f),
                                  s.x + s.w * 0.5f, 380, s.w, 760);
                img.rectTransform.localEulerAngles = new Vector3(0, 0, -s.rot);
                Breathe(img, s.dur);
            }

            StoneFloor(hall);

            // ---- wall sconces ----------------------------------------------------
            foreach (var (x, dur) in new[] { (345f, 3.4f), (1255f, 4.1f) })
            {
                Breathe(HallImg(hall, "SconceGlow", Crimson.Halo, A(Theme.Hex("FF924A"), 0.30f),
                                x, 395, 190, 190), dur);
                Breathe(HallImg(hall, "SconceBloom", Crimson.Halo, A(Theme.Hex("FF8232"), 0.50f),
                                x, 391, 62, 62), dur);
                Breathe(HallImg(hall, "Flame", Theme.Circle, Theme.Hex("FFC46A"),
                                x, 391, 8, 22), dur);
            }

            Embers(hall);

            // ---- the room falls away at the edges --------------------------------
            HallImg(hall, "Vignette", Vig, A(Theme.Hex("030104"), 0.78f),
                    DesignW * 0.5f, DesignH * 0.44f, DesignW * 1.02f, DesignH * 1.16f);
        }

        // The rose window: a stained bloom, its gold rim, and twelve spokes. Hung
        // above the top edge so only its lower two-thirds are in the room.
        static void RoseWindow(RectTransform hall)
        {
            const float CX = DesignW * 0.5f, CY = 100f;
            HallImg(hall, "RoseBloom", Crimson.Halo, A(Theme.Hex("BE1428"), 0.30f), CX, CY, 470, 470);
            HallImg(hall, "RoseCore", Crimson.Halo, A(Theme.Hex("FF785A"), 0.20f), CX, CY, 360, 360);
            HallImg(hall, "RoseRim", Crimson.Ring, A(Gold, 0.40f), CX, 88, 412, 412);

            for (int i = 0; i < 12; i++)
            {
                var img = Crimson.Img(hall, "Spoke", FadeDown, A(Gold, 0.15f));
                var rt = img.rectTransform;
                // Pivoted at the TOP so it swings about the window's hub, exactly as
                // the mockup's `transform-origin: 50% 0` does.
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(CX, -88f);
                rt.sizeDelta = new Vector2(2f, 206f);
                rt.localEulerAngles = new Vector3(0, 0, -i * 30f);
            }
        }

        // Six lit windows between seven piers, brightest at the near end.
        static void Arcade(RectTransform hall)
        {
            (float x, float t, float w, float h, float o, float px, float pw)[] arches =
            {
                (96f,   170f, 112f, 420f, 0.90f, 40f,   56f),
                (280f,  196f, 100f, 372f, 0.72f, 232f,  48f),
                (452f,  220f, 88f,  320f, 0.50f, 408f,  44f),
                (1060f, 220f, 88f,  320f, 0.50f, 1148f, 44f),
                (1220f, 196f, 100f, 372f, 0.72f, 1320f, 48f),
                (1392f, 170f, 112f, 420f, 0.90f, 1504f, 56f),
            };

            foreach (var a in arches)
            {
                // the pier beside it
                HallImg(hall, "Pier", null, A(Theme.Hex("150809"), 0.95f),
                        a.px + a.pw * 0.5f, DesignH * 0.5f, a.pw, DesignH);

                float cx = a.x + a.w * 0.5f, cy = a.t + a.h * 0.5f;
                // the glow it throws into the room
                HallImg(hall, "WindowGlow", Crimson.Halo, A(Theme.Hex("B43C1E"), 0.16f * a.o),
                        cx, cy, a.w + 120f, a.h + 120f);
                // stone reveal, then the glass inside it
                HallImg(hall, "ArchStone", Arch, A(Theme.Hex("7E602A"), 0.60f * a.o), cx, cy, a.w, a.h);
                float iw = a.w - 12f, ih = a.h - 6f;
                HallImg(hall, "ArchGlass", Arch, A(Theme.Hex("BA3428"), 0.55f * a.o),
                        cx, a.t + 6f + ih * 0.5f, iw, ih);
                // The glass darkens toward the sill. A second ARCH here would draw a
                // rounded dome floating in the middle of the window; below the head
                // the light is straight-sided, so this is a plain graded panel.
                var deep = HallImg(hall, "ArchGlassDeep", FadeDown, A(Theme.Hex("260610"), 0.72f * a.o),
                                   cx, a.t + 6f + ih * 0.72f, iw, ih * 0.56f);
                deep.rectTransform.localEulerAngles = new Vector3(0, 0, 180f);

                // leading: one mullion, two transoms, one roundel
                var lead = A(Theme.Hex("0A0508"), 0.78f * a.o);
                HallImg(hall, "Mullion", null, lead,
                        cx, a.t + a.h * 0.61f, 2f, a.h * 0.78f);
                HallImg(hall, "Transom", null, lead, cx, a.t + a.h * 0.52f, iw, 2f);
                HallImg(hall, "Transom", null, lead, cx, a.t + a.h * 0.74f, iw, 2f);
                float ry = a.t + a.h * 0.09f + 13f;
                HallImg(hall, "Roundel", Theme.Circle, A(Theme.Hex("FFAA6E"), 0.50f * a.o),
                        cx, ry, 26, 26);
                HallImg(hall, "RoundelRim", Crimson.Ring, lead, cx, ry, 26, 26);
            }
        }

        // Flagstones receding into the dark, lit only near the arcade.
        static void StoneFloor(RectTransform hall)
        {
            const float Top = DesignH - 250f;
            var fade = Crimson.Img(hall, "FloorFade", FadeDown, A(Theme.Hex("1E0A0E"), 0.85f));
            Place(fade.rectTransform, DesignW * 0.5f, Top + 125f, DesignW, 250f);
            // FadeDown is opaque at its top; the floor wants the opposite.
            fade.rectTransform.localEulerAngles = new Vector3(0, 0, 180f);

            // The grid fades IN as it comes toward you, so the far floor stays dark.
            const float GridTop = DesignH - 210f;
            for (float x = 0; x <= DesignW; x += 120f)
                HallImg(hall, "Flag", null, A(Gold, 0.10f), x, GridTop + 105f, 1f, 210f);
            for (float y = GridTop; y <= DesignH; y += 46f)
            {
                float k = Mathf.Clamp01((y - GridTop) / 210f);
                HallImg(hall, "Flag", null, A(Gold, 0.03f + 0.13f * k), DesignW * 0.5f, y, DesignW, 1f);
            }
        }

        // Nine embers turning over in the light. They rise, fade and start again —
        // on UNSCALED time, because the whole point of this screen is that the game
        // clock has stopped.
        static void Embers(RectTransform hall)
        {
            (float xPct, float bottom, float size, float dur, float delay)[] motes =
            {
                (0.12f, 80, 3, 11, 0f),   (0.23f, 40, 2, 14, 2f),  (0.35f, 110, 4, 9, 4f),
                (0.44f, 60, 2, 13, 1f),   (0.57f, 90, 3, 10, 6f),  (0.66f, 50, 2, 15, 3f),
                (0.78f, 100, 4, 12, 5f),  (0.86f, 70, 2, 10, 7f),  (0.93f, 44, 3, 13, 1.5f),
            };
            foreach (var m in motes)
            {
                var img = HallImg(hall, "Ember", Theme.Circle, A(Theme.Hex("FF9A5A"), 0.55f),
                                  m.xPct * DesignW, DesignH - m.bottom - m.size * 0.5f,
                                  m.size, m.size);
                // The halo is a CHILD of the ember, stretched over it, so it rides
                // along on the one MoteDrift instead of needing a second in step.
                var glow = Crimson.Img(img.transform, "EmberGlow", Crimson.Halo,
                                       A(Theme.Hex("FF783C"), 0.40f));
                var grt = glow.rectTransform;
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
                grt.offsetMin = new Vector2(-5f, -5f); grt.offsetMax = new Vector2(5f, 5f);

                var mote = img.gameObject.AddComponent<MoteDrift>();
                mote.dur = m.dur; mote.delay = m.delay;
            }
        }

        static Image HallImg(RectTransform parent, string name, Sprite sprite, Color col,
                             float cx, float cy, float w, float h)
        {
            var img = Crimson.Img(parent, name, sprite, col);
            Place(img.rectTransform, cx, cy, w, h);
            return img;
        }

        static void Breathe(Image img, float period)
        {
            var b = img.gameObject.AddComponent<BreathePulse>();
            b.period = period;
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ---- procedural sprites for the hall ----------------------------------
        // Built the same way as Crimson's halo and ring: one small texture, cached
        // for the session, stretched to whatever the layout asks for.

        static Sprite _arch;
        /// <summary>
        /// A gothic light: a semi-elliptical head on straight jambs. Both radii are
        /// PROPORTIONAL (half the width across, 29% of the height down), so one
        /// normalised texture stretches to every arch on the screen without
        /// distorting the head.
        ///
        /// 29%, not the 58% written in the mockup's CSS: a browser SHRINKS corner
        /// radii whose neighbours overrun the side they share, and the two top
        /// corners each ask for the full width, so everything is halved before it
        /// is drawn. Taking the 58% at face value gives a head half the window
        /// tall — a bullet, not an arch.
        /// </summary>
        static Sprite Arch
        {
            get
            {
                if (_arch != null) return _arch;
                const int W = 128, H = 256;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                float cx = (W - 1) / 2f, rx = (W - 1) / 2f, ry = H * 0.29f;
                for (int y = 0; y < H; y++)
                {
                    float down = H - 1 - y;              // distance below the crown
                    for (int x = 0; x < W; x++)
                    {
                        float a;
                        if (down >= ry) a = 1f;          // the straight jambs
                        else
                        {
                            float dx = (x - cx) / rx, dy = (ry - down) / ry;
                            a = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * rx * 0.5f);
                        }
                        a = Mathf.Min(a, Mathf.Clamp01((rx - Mathf.Abs(x - cx)) * 0.9f));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
                tex.Apply();
                _arch = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
                return _arch;
            }
        }

        static Sprite _fadeDown;
        /// <summary>Opaque at the top, gone at the bottom. Rotate 180° to invert it.</summary>
        static Sprite FadeDown
        {
            get
            {
                if (_fadeDown != null) return _fadeDown;
                const int W = 4, H = 128;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < H; y++)
                {
                    float a = y / (float)(H - 1);        // texture y grows upward
                    for (int x = 0; x < W; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                tex.Apply();
                _fadeDown = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
                return _fadeDown;
            }
        }

        static Sprite _shaft;
        /// <summary>
        /// A column of light: soft-edged across, dying out three-quarters of the way
        /// down. The softness is baked into the texture because a UI canvas has no
        /// blur to lean on — a hard-edged shaft reads as a white rectangle.
        /// </summary>
        static Sprite Shaft
        {
            get
            {
                if (_shaft != null) return _shaft;
                const int W = 64, H = 128;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                float cx = (W - 1) / 2f;
                for (int y = 0; y < H; y++)
                {
                    float p = 1f - y / (float)(H - 1);   // 0 at the window, 1 at the floor
                    float va = p < 0.42f ? Mathf.Lerp(1f, 0.46f, p / 0.42f)
                             : p < 0.88f ? Mathf.Lerp(0.46f, 0f, (p - 0.42f) / 0.46f) : 0f;
                    for (int x = 0; x < W; x++)
                    {
                        float u = Mathf.Clamp((x - cx) / cx, -1f, 1f);
                        float ha = Mathf.Cos(u * Mathf.PI * 0.5f); ha *= ha;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, va * ha));
                    }
                }
                tex.Apply();
                _shaft = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
                return _shaft;
            }
        }

        static Sprite _vig;
        /// <summary>
        /// The inverse of Crimson's halo: clear in the middle, solid at the corners.
        /// Crimson.Halo can't do this job — it's bright-cored, so tinting it black
        /// blots out the centre of the screen instead of the edges.
        /// </summary>
        static Sprite Vig
        {
            get
            {
                if (_vig != null) return _vig;
                const int S = 128;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                float c = (S - 1) / 2f;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                        // the mockup's own three stops: 0 at the centre, .45 at 60%, 1 at the rim
                        float a = d < 0.6f ? Mathf.Lerp(0f, 0.449f, d / 0.6f)
                                : Mathf.Lerp(0.449f, 1f, Mathf.Clamp01((d - 0.6f) / 0.4f));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                _vig = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _vig;
            }
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

        // ==================== THE GEM PLATE ====================
        //
        // The ornate button from the design pack, and the one shape both the pause
        // and death mockups are built out of: a dark face in the action's own
        // colour, a hairline of that colour running inside the top and bottom
        // edges, and a gold end-cap at each end carrying a cut gem.
        //
        // WHY IT IS ITS OWN THING. The screens already had BloodPlate (red, primary)
        // and GoldPlate (dark, secondary) — two states, so every screen could only
        // ever say "this one" and "the others". The mockups colour-code by WHAT THE
        // BUTTON COSTS YOU instead: red resumes, violet restarts, gold leaves. That
        // needs a plate that takes its accent as an argument, so this one does.
        //
        // All of it is baked or drawn from primitives — no new art files, and it
        // stays sharp at any size, which matters because these screens are the same
        // layout on a phone and a 1440p monitor.

        static Sprite _star;
        /// <summary>
        /// A four-pointed sparkle with concave sides — the ornament dotted through
        /// both mockups. An astroid: |x|^k + |y|^k &lt;= 1 with k below 1 pulls the
        /// edges inward, and 0.5 gives the classic pinched star. Baked white so it
        /// can be tinted per use.
        /// </summary>
        public static Sprite Star
        {
            get
            {
                if (_star != null) return _star;
                const int S = 64;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                float c = (S - 1) / 2f;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float u = Mathf.Abs(x - c) / c, v = Mathf.Abs(y - c) / c;
                        float d = Mathf.Sqrt(u) + Mathf.Sqrt(v);
                        // Soft edge over the last sliver so the points don't stair-step.
                        float a = Mathf.Clamp01((1.02f - d) / 0.10f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                _star = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _star;
            }
        }

        /// <summary>A tinted sparkle at a point, sized in design units.</summary>
        static Image Sparkle(Transform parent, Vector2 anchor, Vector2 pos, float size, Color col)
        {
            var img = Crimson.Img(parent, "Sparkle", Star, col);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// The gold cap that closes each end of a gem plate: a broad gold sparkle
        /// with the action's gem burning in its middle, and a small bright pip
        /// riding on top of that so the stone reads as cut rather than painted.
        /// </summary>
        static void GemCap(RectTransform host, float ax, float h, Color gem)
        {
            var at = new Vector2(ax, 0.5f);
            float dir = ax < 0.5f ? 1f : -1f;
            var pos = new Vector2(dir * h * 0.30f, 0f);
            Sparkle(host, at, pos, h * 1.02f, Gold);
            Sparkle(host, at, pos, h * 0.62f, GoldLit);
            Sparkle(host, at, pos, h * 0.40f, gem);
            Sparkle(host, at, pos, h * 0.16f, BoneLit);
        }

        /// <summary>
        /// A hairline with a gem at its centre — the divider the mockups run above
        /// and below every button. Drawn on the BUTTON's parent, not the button, so
        /// it never moves when a plate is hovered.
        /// </summary>
        static void GemRule(RectTransform parent, float cx, float cy, float w, Color gem)
        {
            var line = Crimson.Img(parent, "Rule", null, new Color(Gold.r, Gold.g, Gold.b, 0.42f));
            Place(line.rectTransform, cx, cy, w, 1.5f);
            line.raycastTarget = false;
            var d = Crimson.Img(parent, "RuleGem", null, gem);
            Place(d.rectTransform, cx, cy, 11f, 11f);
            d.rectTransform.localEulerAngles = new Vector3(0, 0, 45f);
            d.raycastTarget = false;
            var e = Crimson.Img(parent, "RuleGemLit", null, GoldLit);
            Place(e.rectTransform, cx, cy, 4.5f, 4.5f);
            e.rectTransform.localEulerAngles = new Vector3(0, 0, 45f);
            e.raycastTarget = false;
        }

        /// <summary>
        /// One ornate action. `gem` is the accent: it tints the face, both inner
        /// rules and the two end-cap stones, so the button's colour IS its meaning.
        /// </summary>
        static RectTransform GemPlate(Transform surface, float cx, float cy, float w, float h,
                                      string label, int size, Color gem, System.Action go,
                                      string sub = null)
        {
            var host = Box(surface, "Gem_" + label, cx - w * 0.5f, cy - h * 0.5f, w, h);

            // Face: near-black carrying a breath of the gem colour, so three buttons
            // in a column read as three different actions at a glance rather than
            // three identical slabs with different words on them.
            var face = Fill(host, new Color(Mathf.Lerp(0.055f, gem.r, 0.20f),
                                            Mathf.Lerp(0.020f, gem.g, 0.20f),
                                            Mathf.Lerp(0.045f, gem.b, 0.20f), 0.97f), Gold, 2f);

            // The two inner hairlines. These are what make the shape read as forged
            // metal instead of a rounded rectangle — the mockup's whole trick.
            foreach (float t in new[] { 1f, 0f })
            {
                var bar = Crimson.Img(host, "Inner", null, new Color(gem.r, gem.g, gem.b, 0.85f));
                var rt = bar.rectTransform;
                rt.anchorMin = new Vector2(0f, t); rt.anchorMax = new Vector2(1f, t);
                rt.pivot = new Vector2(0.5f, t);
                rt.anchoredPosition = new Vector2(0f, t > 0.5f ? -7f : 7f);
                rt.sizeDelta = new Vector2(-34f, 1.6f);
                bar.raycastTarget = false;
            }

            GemCap(host, 0f, h, gem);
            GemCap(host, 1f, h, gem);

            var t2 = LabelIn(host, 0, sub == null ? 0 : -11f, w, h, label, size, Bone,
                             TextAnchor.MiddleCenter, true);
            if (Theme.MenuFont != null) t2.font = Theme.MenuFont;
            if (sub != null)
                LabelIn(host, 0, h * 0.5f - 4f, w, 18, sub, 13, new Color(Bone.r, Bone.g, Bone.b, 0.52f),
                        TextAnchor.MiddleCenter);

            var btn = host.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.22f, 1.22f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.onClick.AddListener(() => { Audio.Play("click"); go?.Invoke(); });
            return host;
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

    // ==================== the hall's two motions ====================
    //
    // Both run on Time.unscaledTime. The pause screen exists precisely because
    // Time.timeScale is 0, so anything driven by scaled time would sit frozen —
    // a still picture of a room, rather than a room.

    /// <summary>The mockup's `ti-breathe`: a slow opacity swell, 45% to full.</summary>
    public class BreathePulse : MonoBehaviour
    {
        public float period = 3.4f;
        Image _img;
        float _alpha;

        void Awake()
        {
            _img = GetComponent<Image>();
            if (_img != null) _alpha = _img.color.a;
        }

        void Update()
        {
            if (_img == null) return;
            float t = (Mathf.Sin(Time.unscaledTime / Mathf.Max(0.05f, period) * Mathf.PI * 2f) + 1f) * 0.5f;
            var c = _img.color;
            c.a = _alpha * Mathf.Lerp(0.45f, 1f, t);
            _img.color = c;
        }
    }

    /// <summary>
    /// The mockup's `ti-mote`: an ember rises 240 units, brightening over the first
    /// fifth of the climb and guttering out over the rest, then starts again.
    /// </summary>
    public class MoteDrift : MonoBehaviour
    {
        public float dur = 11f, delay, rise = 240f;
        RectTransform _rt;
        Vector2 _home;
        // The ember AND its halo: the glow is a child, and a spark whose core fades
        // while its glow stays put reads as a bug rather than an ember.
        Image[] _imgs;
        float[] _alpha;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _home = _rt.anchoredPosition;
            _imgs = GetComponentsInChildren<Image>(true);
            _alpha = new float[_imgs.Length];
            for (int i = 0; i < _imgs.Length; i++) _alpha[i] = _imgs[i].color.a;
        }

        void Update()
        {
            float t = Mathf.Repeat((Time.unscaledTime - delay) / Mathf.Max(0.1f, dur), 1f);
            _rt.anchoredPosition = _home + new Vector2(0f, rise * t);
            float a = Mathf.Clamp01(t < 0.2f ? t / 0.2f : 1f - (t - 0.2f) / 0.8f);
            for (int i = 0; i < _imgs.Length; i++)
            {
                var c = _imgs[i].color;
                c.a = _alpha[i] * a;
                _imgs[i].color = c;
            }
        }
    }
}
