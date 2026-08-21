using System.Collections;
using UnityEngine;

namespace TrustIssues
{
    public enum TrapType
    {
        FakeFloor,  // looks solid, collapses a moment after you stand on it
        LateSpike,  // proximity-sensed spikes rise visibly just before you arrive
        Crusher,    // a block slams down if you go for the high bait
        FakeExit,   // the obvious bright door kills you
        RealExit,   // the unassuming spot that actually wins
        Surprise,   // INVISIBLE kill zone on safe-looking ground — pure unfair
        Dart,       // a projectile fires across the moment you arrive
        Faller,     // an off-screen block drops on you (Thwomp)
        Spring,     // launches you upward (often into hidden spikes)
        Saw,        // a hazard that slides back and forth
        WarpBack,   // yanks you all the way back to the start — rage
        Reverse,    // flips your controls for a few seconds
        SpikeStatic,// an always-visible spike you must jump over
        ArrowRain,  // spikes drop from the ceiling on a timer — time your run
        Checkpoint, // touch it and you respawn here instead of the start
        BreakBlock, // a solid candy wall you must SHOOT to get past
        GrowSpike,  // a blood spike that grows (lethal) and shrinks (safe) on a loop
        Pendulum,   // a blade on a chain that swings across the path — time your run
        FlameJet,   // a floor jet that erupts fire on a loop (cross while it's down)
        Chandelier, // a wide telegraphed drop from the ceiling (a big Faller)
        HolyWater,  // a floor puddle that turns lethal on a pulse (cross while dim)
        BatSwoop,   // a bat that hovers, then dives at you on a telegraph
        // ---- the room itself (see Betrayal.cs) ----
        // Appended, never reordered: the Codex persists unlocks as "codex_" + (int)t
        // and KillZone.trapTag stores the same number, so renumbering these would
        // silently rewrite every player's Bestiary.
        TiltFloor,     // a slab that hinges under your weight and pours you off
        SlideFloor,    // a slab that slides out sideways, opening a pit where you stand
        DropFloor,     // a slab that falls WITH you and lands somewhere else
        RiseFloor,     // ground that lifts you into the ceiling and presses
        SlamWall,      // a wall that drives in from off-lane as you pass
        CeilingVolley, // a row of ceiling teeth that fire down in sequence, at you
        ShyExit        // the coffin backs away when you reach for it — twice
    }

    /// <summary>
    /// One trap, configured by type. Built and reset by GameRoot each life, so
    /// no per-trap reset bookkeeping is needed. The golden-path rule: the
    /// inviting thing betrays you; each trap shows a subtle TELL so a second
    /// death feels earned.
    /// </summary>
    public class Trap : MonoBehaviour
    {
        public TrapType type;
        public Sprite[] frames;   // optional spin/animation frames (e.g. the saw)

        // ---- THE QUIET VARIANT -----------------------------------------------
        // A hand-placed LateSpike is a designed beat: it wants to be big, and it
        // wants its warning crack, because the floor it sits on was authored
        // around it. A spike the castle LEARNED from watching you is a different
        // animal — it appears mid-floor, unannounced, in a spot that was safe last
        // life. At full size with a red crack under it, it stopped reading as the
        // castle being sly and started reading as a bug: a huge spike sitting on a
        // red rectangle nobody drew on purpose.
        //
        // So the learned ones come through small and unmarked. Same trap, same
        // hitbox rules, just sized and lit like a detail rather than a set piece.
        public float visualScale = 1f;   // shrinks the emerged spike
        public bool subtle;              // skip the warning crack entirely
        // True when this trap is drawn with its Bestiary illustration. Those are
        // fully painted, so the "tint it red while lethal" telegraphs have to become
        // brightness changes instead — tinting a painted sprite red erases the art.
        public bool paintedArt;
        /// <summary>
        /// SPIKE STEEL — the blade itself.
        ///
        /// This colour has been wrong twice, and the measurements say why. Painted the
        /// hall's own red, a spike came out at roughly the same brightness as the wall
        /// behind it. Painted black iron, it measured DARKER than the wall (luminance
        /// 20 against 22) and identical to the floor — invisible by construction, on a
        /// stage that is already red and already black.
        ///
        /// A sprite tint can only ever MULTIPLY, so tinting a mid-tone illustration
        /// with a dark colour throws away the art and everything that made it legible.
        /// The Bestiary's spike illustration averages luminance 76 with highlights up
        /// to 239 — all the shape and shadow needed is already painted in. So the
        /// blade is now left nearly untinted, a hair cool, and reads as what it is:
        /// forged steel, several times brighter than anything around it.
        ///
        /// The red the theme wants comes from the rim below, not from the blade. Hue
        /// was never going to do this job — only brightness.
        /// </summary>
        public static readonly Color SpikeRed = new Color(0.94f, 0.92f, 0.97f, 1f);
        /// <summary>
        /// The hot blood edge drawn larger BEHIND the blade. It does two jobs: it
        /// keeps spikes speaking the game's red hazard language, and it separates the
        /// silhouette on a pale floor the way the steel separates it on a dark one —
        /// so a spike reads in every theme, not just the ones it was checked against.
        /// </summary>
        public static readonly Color SpikeRim = new Color(1f, 0.13f, 0.18f, 0.95f);

        /// <summary>
        /// Paint a spike so it can actually be seen: black iron blade, blood rim.
        /// Every spike in the game (static, grown, ambush, falling) goes through here
        /// so they can never drift apart again.
        /// </summary>
        public static void PaintSpike(GameObject go, bool painted)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            sr.color = painted ? SpikeRed : Theme.Danger;
            if (!painted) return;   // the flat fallback box has no silhouette to outline

            // The rim is a copy of the same sprite, scaled up a hair and pushed one
            // sorting step back. A child (not a sibling) so it follows the blade
            // through every rise, grow and fall animation for free.
            var rim = new GameObject("Rim");
            rim.transform.SetParent(go.transform, false);
            // Scaled from the centre, so it's nudged UP by the same fraction it grew:
            // otherwise the halo sinks below the blade's base and bleeds onto the
            // stone. Wider than it is taller, because it's the vertical silhouette of
            // the blades that has to separate from the floor.
            rim.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            rim.transform.localScale = new Vector3(1.26f, 1.18f, 1f);
            var rimSr = rim.AddComponent<SpriteRenderer>();
            rimSr.sprite = sr.sprite;
            rimSr.color = SpikeRim;
            rimSr.sortingOrder = sr.sortingOrder - 1;
        }
        float _animTimer;
        SpriteRenderer _sr;
        SpriteRenderer _rim;   // the blood outline behind a painted spike, if it has one
        BoxCollider2D _col;
        bool _armed = true;
        Transform _spike;
        Transform _faller;
        Vector3 _fallerHome;

        public void Init(TrapType t) { type = t; }

        Vector3 _origin;
        Vector3 _growBaseScale = Vector3.one;
        float _growBottomY;   // platform surface the spike erupts from
        float _growFullH;     // full world height when raised

        // A late spike is an ambush, not an invisible collision. Its trigger sits
        // ahead of the buried spike so a full-speed player gets roughly a third of
        // a second of warning; the tall trigger also catches a player who jumps.
        // The spike itself stays at this component's transform position.
        const float LateSpikeSensorLead = 2.15f;
        const float LateSpikeSensorWidth = 1.10f;
        const float LateSpikeSensorHeight = 5.00f;
        const float LateSpikeRiseTime = 0.24f;
        const float LateSpikeLethalAt = 0.72f;

        void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            var rimT = transform.Find("Rim");
            if (rimT != null) _rim = rimT.GetComponent<SpriteRenderer>();
            _col = GetComponent<BoxCollider2D>();
            _origin = transform.position;

            if (type == TrapType.LateSpike && _col != null)
            {
                // Levels progress left-to-right. Moving the SENSOR left makes the
                // hazard reveal ahead of the player while its visual still erupts
                // at _origin. A taller sensor prevents jumping from bypassing the
                // reveal and then meeting a spike that was never shown.
                _col.offset = new Vector2(-LateSpikeSensorLead, 1.20f);
                _col.size = new Vector2(LateSpikeSensorWidth, LateSpikeSensorHeight);
            }
            // GrowSpike and FlameJet both erupt UP from the floor, so they share the
            // base-anchor maths (otherwise they'd shrink toward their own centre).
            if (type == TrapType.GrowSpike || type == TrapType.FlameJet)
            {
                _growBaseScale = transform.localScale;
                float spriteH = (_sr != null && _sr.sprite != null) ? _sr.sprite.bounds.size.y : 1f;
                _growFullH = spriteH * _growBaseScale.y;
                _growBottomY = _origin.y - _growFullH / 2f;
            }

            // A faller hovers above and slams down on a brief telegraph. A chandelier
            // is the same idea but WIDE and gothic — it shares the drop coroutine.
            if (type == TrapType.Faller || type == TrapType.Chandelier)
            {
                bool chand = type == TrapType.Chandelier;
                // The Bestiary page's own illustration first — it's already painted in
                // the castle's palette, so it goes in untinted.
                var painted = Assets.TrapArt(chand ? "chandelier" : "faller");
                var sp = painted ?? Assets.Sprite(chand ? "chandelier" : "rockhead");
                var size = chand ? new Vector2(2.4f, 1.1f) : new Vector2(1.5f, 1.5f);
                if (painted != null && chand) size = new Vector2(2.4f, 2.0f);   // the candelabra is taller than the old bar
                var pos = transform.position + Vector3.up * (chand ? 4.0f : 3.5f);
                var go = sp != null
                    ? Theme.SpriteBox(chand ? "Chandelier" : "RockHead", transform, pos, size, sp, 4)
                    : Theme.Box(chand ? "Chandelier" : "RockHead", transform, pos, size,
                                chand ? Theme.Hex("4A3016") : Theme.Trick, 4);   // bronze body
                if (painted == null && sp != null && !chand) go.GetComponent<SpriteRenderer>().color = new Color(0.5f, 0.45f, 0.5f);
                // WROUGHT IRON, not gold. The old imported sprite is a bright brass
                // blob that reads as a mystery cheese block against the night sky
                // (playtest: "how does that fit the theme") — tint it down to
                // black iron so only its shape and the drop threat read.
                if (painted == null && sp != null && chand) go.GetComponent<SpriteRenderer>().color = new Color(0.34f, 0.3f, 0.38f);
                if (chand && sp == null) // fallback art: the exact hanging fixture from the game art
                {
                    // A bronze bar with a red gem at its heart and a row of red spikes
                    // hanging beneath, on a chain to the ceiling — matches the chandelier
                    // drawn in the gameplay art and reads instantly as "this can DROP".
                    var bronze = Theme.Hex("8A5A26");
                    Theme.Box("Chain", go.transform, (Vector2)pos + new Vector2(0f, 2.3f), new Vector2(0.10f, 4f), Theme.Hex("4A3016"), 3);
                    Theme.Box("Bar",   go.transform, (Vector2)pos + new Vector2(0f, 0.14f), new Vector2(2.3f, 0.30f), bronze, 5);
                    var gem = Theme.Box("Gem", go.transform, (Vector2)pos + new Vector2(0f, 0.14f), new Vector2(0.34f, 0.34f), Theme.Danger, 6);
                    gem.transform.rotation = Quaternion.Euler(0f, 0f, 45f);   // a red diamond
                    for (int i = -2; i <= 2; i++)   // a row of red spikes hanging below the bar
                    {
                        var spk = Theme.Box("Spike", go.transform, (Vector2)pos + new Vector2(i * 0.46f, -0.22f),
                            new Vector2(0.24f, 0.34f), Theme.Danger, 5);
                        spk.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                    }
                }
                var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true;
                var kz = go.AddComponent<KillZone>();
                kz.msg = chand ? "Crushed under the chandelier." : "Crushed by the falling stone.";
                _faller = go.transform;
                _fallerHome = _faller.position;
            }

            // A pendulum: a blade on a chain that swings across the lane below. The
            // trap object itself is the pivot (up high); the chain + blade hang from
            // it as children, so rotating the pivot swings the whole assembly.
            if (type == TrapType.Pendulum)
            {
                const float arm = 3.0f;
                Theme.Box("Chain", transform, transform.position + Vector3.down * (arm / 2f),
                    new Vector2(0.08f, arm), Theme.Hex("2A2230"), 2);
                var sp = Assets.TrapArt("pendulum") ?? Assets.Sprite("pendulum");
                var bladePos = transform.position + Vector3.down * arm;
                var blade = sp != null
                    ? Theme.SpriteBox("Blade", transform, bladePos, new Vector2(1.5f, 1.3f), sp, 3)
                    : Theme.Box("Blade", transform, bladePos, new Vector2(1.0f, 1.0f), Theme.Danger, 3);
                var col = blade.AddComponent<BoxCollider2D>(); col.isTrigger = true; col.size = Vector2.one * 0.7f;
                var kz = blade.AddComponent<KillZone>(); kz.msg = "Sliced by the pendulum blade.";
            }

            if (type == TrapType.ArrowRain)
                StartCoroutine(Rain());
        }

        // Spikes fall from the ceiling at this column on a loop — time your dash.
        IEnumerator Rain()
        {
            yield return new WaitForSeconds(Random.Range(0f, 1f)); // desync multiple columns
            while (true)
            {
                var painted = Assets.TrapArt("arrowrain");
                var sp = painted ?? Assets.Sprite("spike");
                var spawn = transform.position + Vector3.up * 5.5f;
                var go = sp != null
                    ? Theme.SpriteBox("RainDart", transform, spawn, new Vector2(0.8f, 0.6f), sp, 4)
                    : Theme.Box("RainDart", transform, spawn, new Vector2(0.3f, 0.7f), Theme.Danger, 4);
                if (sp != null) PaintSpike(go, painted != null);   // black blade, blood rim — visible mid-fall
                var col = go.AddComponent<BoxCollider2D>(); col.isTrigger = true;
                col.size *= 0.8f; // reliable spike hitbox
                var kz = go.AddComponent<KillZone>(); kz.msg = "Impaled by a falling spike.";
                StartCoroutine(FallDart(go.transform));
                yield return new WaitForSeconds(1.3f);
            }
        }

        IEnumerator FallDart(Transform t)
        {
            float v = 4f, floor = transform.position.y - 1f;
            while (t != null && t.position.y > floor)
            {
                v += 22f * Time.deltaTime;
                t.position += Vector3.down * (v * Time.deltaTime);
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        void Update()
        {
            // A saw slides back and forth across its track AND spins.
            if (type == TrapType.Saw)
            {
                transform.position = new Vector3(
                    _origin.x + Mathf.Sin(Time.time * 2.5f) * 2.5f, _origin.y, 0f);
                if (frames != null && frames.Length > 0 && _sr != null)
                {
                    _animTimer += Time.deltaTime;
                    _sr.sprite = frames[Mathf.FloorToInt(_animTimer * 24f) % frames.Length];
                }
            }

            // A growing spike: erupts tall (lethal) then sinks short (safe to
            // cross). Anchored at the base so it visibly rises OUT of the floor.
            else if (type == TrapType.GrowSpike)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f + _origin.x); // 0..1
                k = k * k * (3f - 2f * k);   // smoothstep: snaps up, LINGERS tall, snaps down — a blade, not a bobbing float
                float h = Mathf.Max(0.08f, k);                                    // never fully gone
                transform.localScale = new Vector3(_growBaseScale.x, _growBaseScale.y * h, 1f);
                float curH = _growFullH * h;
                transform.position = new Vector3(_origin.x, _growBottomY + curH / 2f, 0f);
                bool lethal = k > 0.55f;
                if (_col != null) _col.enabled = lethal;       // only deadly when grown
                // The lethal phase has to read at a glance. The blade itself is black
                // iron now (see SpikeRed), and dimming black reads as nothing at all —
                // so on painted spikes the BLOOD RIM carries the telegraph: it burns
                // when the blade is up and goes cold when it's safe to cross.
                if (_rim != null)
                    _rim.color = lethal ? SpikeRim : SpikeRim * 0.30f;
                else if (_sr != null)
                    _sr.color = lethal ? Theme.Danger : new Color(0.55f, 0.12f, 0.14f, 1f); // flat fallback: red = lethal
            }

            // A pendulum blade: swing the pivot back and forth. The chain + blade
            // are children, so they sweep through the lane below.
            else if (type == TrapType.Pendulum)
            {
                float ang = Mathf.Sin(Time.time * 1.6f + _origin.x) * 55f;
                transform.rotation = Quaternion.Euler(0f, 0f, ang);
            }

            // A flame jet: mostly OFF, erupts for a beat with a tiny telegraph. Same
            // base-anchor maths as GrowSpike so the fire shoots UP out of the floor.
            else if (type == TrapType.FlameJet)
            {
                float t = Mathf.Repeat(Time.time * 0.8f + _origin.x, 1f); // 0..1 loop
                bool erupt = t > 0.55f && t < 0.9f;     // lethal window
                bool warn  = t >= 0.45f && t <= 0.55f;  // a flicker of warning
                float h = erupt ? 1f : (warn ? 0.35f : 0.08f);
                transform.localScale = new Vector3(_growBaseScale.x, _growBaseScale.y * h, 1f);
                float curH = _growFullH * h;
                transform.position = new Vector3(_origin.x, _growBottomY + curH / 2f, 0f);
                if (_col != null) _col.enabled = erupt;
                // Painted fire is already fire — dyeing it orange only flattens the
                // illustration, so the lethal window reads as BRIGHTNESS instead
                // (same rule the grow-spike above follows).
                if (_sr != null)
                    _sr.color = paintedArt
                        ? (erupt ? Color.white
                                 : warn ? new Color(0.85f, 0.80f, 0.78f, 0.85f)
                                        : new Color(0.55f, 0.45f, 0.42f, 0.30f))
                        : (erupt ? Theme.Hex("FF7A1A")
                                 : warn ? Theme.Hex("FFC24D")
                                        : new Color(1f, 0.5f, 0.1f, 0.25f));
            }

            // Holy water: a flat puddle that turns lethal on a slow pulse. Bright =
            // burning (deadly), dim = safe to cross. No vertical growth.
            else if (type == TrapType.HolyWater)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.0f + _origin.x);
                bool lethal = k > 0.6f;
                if (_col != null) _col.enabled = lethal;
                if (_sr != null)
                    _sr.color = paintedArt
                        ? (lethal ? Color.white : new Color(0.62f, 0.66f, 0.70f, 0.45f))
                        : (lethal ? new Color(0.85f, 0.97f, 1f, 0.95f)
                                  : new Color(0.5f, 0.8f, 0.95f, 0.4f));
            }
        }

        // ---- solid traps (FakeFloor) use collision; the rest are triggers ----
        void OnCollisionEnter2D(Collision2D c)
        {
            if (type != TrapType.FakeFloor || !_armed) return;
            var pc = c.collider.GetComponent<PlayerController>();
            if (pc == null) return;
            // Only collapse when the player is standing on top of us.
            if (pc.transform.position.y > transform.position.y)
                StartCoroutine(Collapse());
        }

        IEnumerator Collapse()
        {
            _armed = false;
            Codex.Unlock(TrapType.FakeFloor);   // you've discovered the treacher-floor
            GameRoot.I?.TrapFired(type, transform.position);   // survive the drop = CALLED IT
            // Give a sharp-eyed player a tiny rescue window: the slab reddens and
            // shakes before it gives way. Most first-timers still fall, but they
            // SEE why and can plausibly save it with a fast jump.
            Vector3 home = transform.position;
            Color baseColor = _sr != null ? _sr.color : Color.white;
            float warn = 0f;
            while (warn < 0.22f)
            {
                warn += Time.deltaTime;
                float k = Mathf.Clamp01(warn / 0.22f);
                transform.position = home + (Vector3)(Random.insideUnitCircle * Mathf.Lerp(0.025f, 0.09f, k));
                if (_sr != null) _sr.color = Color.Lerp(baseColor, new Color(0.72f, 0.18f, 0.2f, baseColor.a), k * 0.7f);
                yield return null;
            }
            _col.enabled = false; // you fall NOW
            float e = 0f;
            Vector3 start = transform.position;
            while (e < 0.42f)
            {
                e += Time.deltaTime;
                transform.position = start + Vector3.down * (e * 10f);
                transform.rotation = Quaternion.Euler(0f, 0f, e * 55f);
                var col = _sr.color; col.a = 1f - e / 0.42f; _sr.color = col;
                yield return null;
            }
            gameObject.SetActive(false);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var pc = other.GetComponent<PlayerController>();
            if (pc == null) return;

            switch (type)
            {
                case TrapType.LateSpike:
                    TryRaiseLateSpike(other, pc);
                    break;
                case TrapType.Crusher:
                    if (_armed) StartCoroutine(Crush());
                    break;
                case TrapType.FakeExit:
                    Codex.Unlock(TrapType.FakeExit);
                    Juice.ReportTrap((int)TrapType.FakeExit);   // so the roast is bespoke to this trap
                    GameRoot.I?.Die("That door? Pure evil.");
                    break;
                case TrapType.RealExit:
                    if (_armed) { _armed = false; GameRoot.I?.ReachExit(); }
                    break;
                case TrapType.Surprise:
                    Codex.Unlock(TrapType.Surprise);
                    Juice.ReportTrap((int)TrapType.Surprise);
                    GameRoot.I?.Die("Caught in a sunbeam. Vampires burn.");
                    break;
                case TrapType.Dart:
                    if (_armed) { _armed = false; StartCoroutine(FireDart()); }
                    break;
                case TrapType.Faller:
                case TrapType.Chandelier:
                    if (_armed) StartCoroutine(DropReactive());
                    break;
                case TrapType.Spring:
                    Codex.Unlock(TrapType.Spring);
                    LaunchPlayer(other);
                    break;
                case TrapType.WarpBack:
                    Codex.Unlock(TrapType.WarpBack);
                    GameRoot.I?.WarpToStart();
                    break;
                case TrapType.Reverse:
                    Codex.Unlock(TrapType.Reverse);
                    pc.SetReversed(3f);
                    break;
                case TrapType.Checkpoint:
                    if (_armed) { _armed = false; GameRoot.I?.SetCheckpoint(transform.position); }
                    break;
            }
        }

        void OnTriggerStay2D(Collider2D other)
        {
            // If the player entered the sensor while backtracking or standing
            // still, arm it as soon as they turn toward the still-ahead spike.
            // This also makes very slow approaches deterministic: the warning is
            // based on distance, never on whether one physics-enter event happened.
            if (type != TrapType.LateSpike || !_armed) return;
            var pc = other.GetComponent<PlayerController>();
            if (pc != null) TryRaiseLateSpike(other, pc);
        }

        void TryRaiseLateSpike(Collider2D player, PlayerController pc)
        {
            if (!_armed) return;

            // Never reveal behind the player. Castle/Blood Moon routes advance to
            // the right, so a player who has already crossed the hazard can safely
            // backtrack without causing a spike to pop up behind their feet.
            if (pc.transform.position.x >= transform.position.x - 0.25f) return;
            var rb = player.attachedRigidbody;
            if (rb != null && rb.linearVelocity.x < -0.05f) return;

            StartCoroutine(RaiseSpike());
        }

        // A dart flies in from the right the instant you step on the sensor.
        IEnumerator FireDart()
        {
            GameRoot.I?.TrapFired(type, transform.position);   // dodge the stake = CALLED IT
            // The Bestiary's STAKE LAUNCHER fires actual stakes; this fired a red
            // rectangle. Same flight, same timing — it just looks like the page now.
            var stake = Assets.TrapArt("dart");
            var dart = stake != null
                ? Theme.SpriteBox("Dart", transform.parent, transform.position + Vector3.right * 5f,
                    new Vector2(1.0f, 0.5f), stake, 4)
                : Theme.Box("Dart", transform.parent, transform.position + Vector3.right * 5f,
                    new Vector2(0.6f, 0.22f), Theme.Danger, 4);
            if (stake != null) dart.transform.localScale =
                new Vector3(-dart.transform.localScale.x, dart.transform.localScale.y, 1f);   // point the way it flies
            var kz = dart.AddComponent<KillZone>(); kz.msg = "Skewered by a flying stake."; kz.trapTag = (int)type;
            var col = dart.AddComponent<BoxCollider2D>(); col.isTrigger = true;
            float t = 0f;
            while (t < 2.2f && dart != null)
            {
                t += Time.deltaTime;
                dart.transform.position += Vector3.left * (15f * Time.deltaTime);
                yield return null;
            }
            if (dart != null) Destroy(dart);
        }

        // An off-screen block slams down on the spot you're standing.
        // The hovering rock-head shakes (telegraph), slams down, waits, retracts.
        // Sprint through during the shake to survive; dawdle and you're flat.
        IEnumerator DropReactive()
        {
            _armed = false;

            // ---- IT AIMS WHERE YOU'RE GOING, NOT WHERE YOU ARE -----------------
            //
            // The old version shook for half a second directly above its authored
            // spot and then dropped there. At 7.5 u/s a player covers 3.75 units
            // during that shake, which is wider than the block — so anyone simply
            // holding right walked out from under it every time and the trap did
            // nothing. That is the "you can just walk fast and nothing happens"
            // note, and it was true of every Faller and Chandelier in the game.
            //
            // Now the block STALKS during the first part of the shake: it tracks a
            // point out ahead of the player, scaled by how fast they are actually
            // moving, so running is what puts you under it. Sprinting is no longer
            // a free answer — it is the thing being punished.
            //
            // Then it COMMITS. Tracking stops at LockAt and the remaining shake
            // happens over the spot it has chosen, so the prediction is shown to
            // the player before it becomes lethal. That is the whole difference
            // between "the game read me" and "the game cheated": it guesses out
            // loud, and you still get a window to prove it wrong.
            const float ShakeTime = 0.55f, LockAt = 0.32f;
            const float Lead = 0.42f;    // seconds of the player's velocity to lead by
            const float Reach = 3.6f;    // …but never further than this from home

            var playerT = GameRoot.I != null ? GameRoot.I.PlayerTransform : null;
            var playerRb = playerT != null ? playerT.GetComponent<Rigidbody2D>() : null;
            float aimX = _fallerHome.x;

            float e = 0f;
            while (e < ShakeTime)
            {
                e += Time.deltaTime;
                if (e < LockAt && playerT != null)
                {
                    float vx = playerRb != null ? playerRb.linearVelocity.x : 0f;
                    float want = playerT.position.x + vx * Lead;
                    // Clamped to a leash around the authored spot. Without this the
                    // block chases across the whole floor, which stops reading as a
                    // trap in a room and starts reading as a bug following you.
                    want = Mathf.Clamp(want, _fallerHome.x - Reach, _fallerHome.x + Reach);
                    aimX = Mathf.Lerp(aimX, want, 10f * Time.deltaTime);
                }
                if (_faller != null)
                    _faller.position = new Vector3(aimX, _fallerHome.y, _fallerHome.z)
                                     + (Vector3)(Random.insideUnitCircle * 0.08f);
                yield return null;
            }
            // Fall from where it COMMITTED, not from its authored perch — the block
            // has stalked sideways by now, and lerping from _fallerHome would snap
            // it back across the room for one frame and then drop it diagonally.
            Vector3 from = new Vector3(aimX, _fallerHome.y, _fallerHome.z);
            Vector3 to = new Vector3(aimX, transform.position.y, 0f);
            e = 0f;
            while (e < 0.1f)
            {
                e += Time.deltaTime;
                if (_faller != null) _faller.position = Vector3.Lerp(from, to, e / 0.1f);
                yield return null;
            }
            // SLAM — dust + shake even if it misses (weight).
            // Reported at the SLAM (not the shake): the shake is the dodge window,
            // the slam is the moment the trap has committed and missed.
            GameRoot.I?.TrapFired(type, to);
            Fx.Burst(to + Vector3.down * 0.4f, new Color(0.55f, 0.5f, 0.55f, 0.9f), 9, 4.5f, 0.18f, 0.4f, 10f);
            GameRoot.I?.ShakeCam(0.28f, 0.18f);
            Audio.PlayOr("die_slam", "jump", 0.4f);
            yield return new WaitForSeconds(0.35f);
            e = 0f;
            while (e < 0.3f)
            {
                e += Time.deltaTime;
                if (_faller != null) _faller.position = Vector3.Lerp(to, _fallerHome, e / 0.3f);
                yield return null;
            }
            if (_faller != null) _faller.position = _fallerHome;
            _armed = true;
        }

        void LaunchPlayer(Collider2D other)
        {
            var rb = other.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 21f);
                Audio.Play("jump", 0.7f);
            }
        }

        // The forward sensor reveals this spike before the player reaches it. It
        // rises harmlessly at first, then becomes lethal once most of the blade is
        // visibly above ground. Continuing forward kills; stopping or jumping on
        // reaction survives.
        IEnumerator RaiseSpike()
        {
            _armed = false;
            GameRoot.I?.TrapFired(type, transform.position);   // clear the ambush = CALLED IT
            var painted = Assets.TrapArt("latespike");
            var sp = painted ?? Assets.Sprite("spike");
            var pos = transform.position + Vector3.down * 0.9f;
            float vs = Mathf.Max(0.2f, visualScale);
            GameObject go = sp != null
                ? Theme.SpriteBox("Spikes", transform.parent, pos, (painted != null ? new Vector2(1.4f, 0.9f) : new Vector2(1f, 1f)) * vs, sp, 3)
                : Theme.Box("Spikes", transform.parent, pos, new Vector2(0.7f, 0.9f) * vs, Theme.Danger, 3);
            // Black iron with a blood rim, so an ambush spike is unmistakable the
            // instant it clears the floor (see PaintSpike).
            if (sp != null) PaintSpike(go, painted != null);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size *= 0.8f; // reliable spike hitbox
            col.enabled = false; // readable warning first; danger only after emergence
            var kz = go.AddComponent<KillZone>();
            kz.msg = "Impaled.";
            kz.trapTag = (int)TrapType.LateSpike;
            _spike = go.transform;

            // A hairline crack appears before the spike moves. It is deliberately
            // brief: a surprise on attempt one, actionable information thereafter.
            // The learned spikes skip it — see `subtle`. On those it was the thing
            // reading as a stray red rectangle under the trap, and a spike the
            // castle grew specifically because you felt safe there is not supposed
            // to announce itself anyway.
            if (!subtle)
            {
                var crack = Theme.Box("SpikeCrack", transform.parent,
                    new Vector2(transform.position.x, -2.66f), new Vector2(1.05f, 0.08f),
                    new Color(0.9f, 0.16f, 0.2f, 0.9f), 4);
                Vector3 crackScale = crack.transform.localScale;
                float warning = 0f;
                while (warning < 0.12f)
                {
                    warning += Time.deltaTime;
                    crack.transform.localScale = new Vector3(
                        crackScale.x * (0.55f + warning * 3.75f), crackScale.y, crackScale.z);
                    yield return null;
                }
                if (crack != null) Destroy(crack);
            }

            // Ease-out with a small overshoot-and-settle: the spike PUNCHES up,
            // pokes a hair past its mark, and sits back. A linear slide read as
            // a texture glitch, not an attack ("the moving aspect does not look
            // good") — the overshoot is what sells impact at this sprite size.
            float e = 0f; Vector3 from = _spike.position;
            Vector3 to = from + Vector3.up * 0.95f;
            GameRoot.I?.ShakeCam(0.1f, 0.07f);
            while (e < LateSpikeRiseTime)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / LateSpikeRiseTime);
                float ease = 1f + 1.7f * Mathf.Pow(k - 1f, 3f) + 0.7f * Mathf.Pow(k - 1f, 2f); // back-out
                _spike.position = Vector3.LerpUnclamped(from, to, ease);
                if (!col.enabled && k >= LateSpikeLethalAt) col.enabled = true;
                yield return null;
            }
            _spike.position = to;
            col.enabled = true;
        }

        // A crusher block slams down the moment you reach for the bait coins.
        IEnumerator Crush()
        {
            _armed = false;
            var sp = Assets.TrapArt("crusher") ?? Assets.Sprite("rockhead");
            var top = transform.position + Vector3.up * 3.2f;
            GameObject go = sp != null
                ? Theme.SpriteBox("Crusher", transform.parent, top, new Vector2(1.6f, 1.6f), sp, 4)
                : Theme.Box("Crusher", transform.parent, top, new Vector2(transform.localScale.x, 1.4f), Theme.Trick, 4);
            float e = 0f; Vector3 from = go.transform.position;
            Vector3 to = new Vector3(transform.position.x, transform.position.y, 0f);
            while (e < 0.12f)
            {
                e += Time.deltaTime;
                go.transform.position = Vector3.Lerp(from, to, e / 0.12f);
                yield return null;
            }
            Juice.ReportTrap((int)TrapType.Crusher);
            GameRoot.I?.Die("Should've stayed low.");
        }
    }

    /// <summary>
    /// Softly pulses a SpriteRenderer's alpha. Used to give the otherwise-
    /// INVISIBLE sensor traps a faint, breathing tell — visible if you're
    /// paying attention, easy to miss if you're not. Cosmetic.
    /// </summary>
    public class FaintPulse : MonoBehaviour
    {
        public float min = 0.10f, max = 0.34f, speed = 2.4f;
        SpriteRenderer _sr;
        Color _base;
        void Start() { _sr = GetComponent<SpriteRenderer>(); if (_sr != null) _base = _sr.color; }
        void Update()
        {
            if (_sr == null) return;
            var c = _base;
            c.a = Mathf.Lerp(min, max, 0.5f + 0.5f * Mathf.Sin(Time.time * speed));
            _sr.color = c;
        }
    }

    /// <summary>Spins a transform (used for hanging saw blades). Cosmetic.</summary>
    public class Spinner : MonoBehaviour
    {
        public float speed = 320f;
        void Update() => transform.Rotate(0f, 0f, speed * Time.deltaTime);
    }

    /// <summary>Kills the player on contact (trigger or collision). Reusable.</summary>
    public class KillZone : MonoBehaviour
    {
        public string msg = "Bonk.";
        public int trapTag = -1;   // (int)TrapType for the Codex; -1 = not a codex trap

        void Kill()
        {
            // Reveal the trap's Bestiary entry the first time it gets you.
            int tag = trapTag;
            if (tag < 0) { var tr = GetComponentInParent<Trap>(); if (tr != null) tag = (int)tr.type; }
            if (tag >= 0) Codex.Unlock((TrapType)tag);
            Memory.RecordKill(tag);   // lifetime tally + streak → the nemesis system
            // Tell the roast system EXACTLY what killed you. The cause text can't
            // tell a Crusher from a Chandelier, and the bespoke line ("It creaked
            // first.") is the one that reads as the castle watching you.
            Juice.ReportTrap(tag);
            GameRoot.I?.Die(msg);
        }
        void OnTriggerEnter2D(Collider2D o)
        {
            if (o.GetComponent<PlayerController>()) Kill();
        }
        void OnCollisionEnter2D(Collision2D c)
        {
            if (c.collider.GetComponent<PlayerController>()) Kill();
        }
    }
}
