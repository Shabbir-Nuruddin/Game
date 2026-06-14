# Trust Issues — itch.io publishing kit

Everything you need to put the game online and get feedback. Copy/paste the page
text, follow the steps, and read the "Does it auto-update?" section — it's the
thing most first-time devs get wrong.

---

## 1. The page text (copy/paste into itch)

**Title:**
```
Trust Issues
```

**Short tagline (the "Project's short description / tagline" field):**
```
A cute candy platformer that desperately wants you dead.
```

**Cover / page description (the big text box):**
```
Beanie only wanted ONE piece of candy. 🍬

The Candy Kingdom looks adorable — pink, soft, harmless. It is lying to you.
Every floor, every door, every sweet little thing wants Beanie dead. The floor
you're standing on? Fake. That obvious trophy? A trap. Those spikes weren't
there a second ago.

Trust Issues is a fast, funny rage-platformer in the spirit of Level Devil and
Unfair Mario. You WILL die — a lot — and it's supposed to be hilarious. Learn
the traps, dodge the darts, time the falling blocks, and bluff your way to the
real exit.

▶ 20 hand-built levels of betrayal
▶ Hidden spikes, collapsing floors, dart shooters, falling rock-heads, saws,
  reverse controls, warp-backs, ceiling arrow-rain, and invisible deaths
▶ A death counter to flex (or cry about)
▶ Checkpoints on the longer levels so you don't (completely) lose your mind

How far can you get? Tell me your death count.

— Controls —
Move: A / D or ← →     Jump: Space / W     Pause: Esc     Restart: R
```

**Tags (add these in the Tags field):**
```
rage, troll, platformer, difficult, pixel-art, funny, unfair, 2d, hard, meme
```

**Genre:** Platformer
**Kind of project:** HTML (so it plays in the browser)
**Pricing:** No payments (free) — best for getting feedback fast.

---

## 2. Build the game (Unity 6 → Web)

> In Unity 6 the platform is called **"Web"** (it used to be "WebGL"). Same thing.

1. **Save your scene** (the build needs at least one): `File → Save As…` →
   `Assets/Scenes/Main.unity`. (The game auto-boots, so an empty scene is fine.)
2. Open **`File → Build Profiles`** (Unity 6) — or `File → Build Settings` on
   older versions.
3. Select **Web** → **Switch Platform**. *(Missing? Unity Hub → Installs → your
   version → ⚙ → Add Modules → **WebGL Build Support** → install, then reopen.)*
4. **Add Open Scenes** (or drag `Main` into the Scene List) so it's checked.
5. ⚠️ **The #1 black-screen fix —** `Edit → Project Settings → Player → Web tab →
   Publishing Settings → Compression Format = Disabled`. (With compression on,
   Unity makes `*.js.gz` files itch's server won't run, so you get a black
   screen.) Set it to **Disabled** before building.
6. *(Recommended)* In the same **Player → Web** settings, under **Resolution and
   Presentation**, you can leave the default template. Our game already scales to
   any size via the Canvas Scaler, so you don't need a custom template.
7. *(If it won't load later — see troubleshooting)* Under **Player → Web →
   Other Settings**, if you see **"Enable Native C/C++ Multithreading"**, leave
   it **OFF** — on requires special server headers itch needs a toggle for.
8. Click **Build**, choose an **empty** folder like `Builds/Web`, and wait.
   You'll get **`index.html`**, plus **`Build/`** and **`TemplateData/`** folders.

## 3. Put it on itch.io

1. Make a free account at **itch.io** → top-right → **Upload new project**
   (or Dashboard → **Create new project**).
2. Fill in **Title**, **tagline**, **description**, **tags** from section 1.
3. **Kind of project: HTML**.
4. **Zip correctly (most common mistake):** open the `Builds/Web` folder, select
   **`index.html` + `Build` + `TemplateData`**, and zip *those*. The `index.html`
   MUST be at the **root of the zip** — NOT inside a `Web/` folder. (If itch says
   *"index.html not found"*, you zipped the folder instead of its contents.)
5. **Upload** the zip → tick **"This file will be played in the browser."**
6. **Embed options:** set a fixed size like **1280 × 720**, tick
   **"Fullscreen button"** on. Leave **"SharedArrayBuffer support"** OFF unless
   the game won't load (then try turning it on).
7. Add a **cover image** (a screenshot) + 2–3 screenshots.
8. Set visibility, **Save & view page**, then **Publish**.
9. Share the link. 🎉

---

## 3.5 Troubleshooting (if the page loads but the game doesn't)

- **Black screen / stuck loading bar** → 90% of the time it's compression. Re-build
  with **Player → Web → Publishing Settings → Compression Format = Disabled**.
- **"index.html not found" on upload** → you zipped the *folder*. Re-zip the
  *contents* so `index.html` is at the top of the zip.
- **Loads forever / fails near the end** → in Build Profiles tick **Development
  Build**, rebuild, upload that; then open the browser **Console (F12)** to read
  the actual error.
- **Game is tiny / has a border** → set the itch **Embed size to 1280 × 720** and
  tick the fullscreen button.
- **"SharedArrayBuffer" error in console** → turn OFF "Enable Native C/C++
  Multithreading" in Player settings and rebuild, OR tick **SharedArrayBuffer
  support** in the itch embed options.

## 4. ❗ Does it auto-update when I change the code?

**No.** This is the key thing to understand:

- itch.io hosts the **static build you uploaded** — a snapshot. It has no
  connection to your Unity project or your GitHub repo.
- When you change code in Unity, the **live itch game does NOT change.** Players
  keep seeing the old version until you upload a new build.

**To push an update, every time:**
1. Make code changes in Unity.
2. **Re-build WebGL** (section 2).
3. Re-zip and **re-upload** the new zip to the same itch project (it replaces the
   old file). The link stays the same.

**Make updates painless with Butler (itch's uploader tool):**
- Install **butler** (itch.io's CLI): https://itchio.itch.io/butler
- One-time: `butler login`
- Then each update is one command:
  ```
  butler push Builds/Web YOUR_ITCH_USERNAME/trust-issues:html
  ```
- This uploads only what changed and is way faster than the web uploader. Same
  link, players get the new version next load.

> TL;DR: GitHub = your source code (auto-saved as you commit). itch = a published
> snapshot you must re-build + re-upload to update. They're separate.

---

## 5. Getting useful feedback

Pin a comment / ask testers these 4 things:
1. **How far did you get?** (which level stopped you)
2. **What's your death count?**
3. **Did any death feel impossible vs. unfair-but-learnable?**
4. **Did you laugh? Would you send it to a friend?**

That tells us which levels to tune and whether the hook lands.
