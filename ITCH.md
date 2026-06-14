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

## 2. Build the game (WebGL)

1. **Save your scene** (the build needs at least one): `File → Save As…` →
   `Assets/Scenes/Main.unity`. (The game auto-boots, so an empty scene is fine.)
2. `File → Build Settings` (Unity 6: **Build Profiles**).
3. Select **WebGL** → **Switch Platform**. *(Missing? Unity Hub → Installs → your
   version → Add Modules → WebGL Build Support, then come back.)*
4. **Add Open Scenes** so `Main` is in the scene list.
5. ⚠️ **Critical for itch:** `Edit → Project Settings → Player → Publishing
   Settings → Compression Format = Disabled`. (Otherwise itch often shows a
   black screen / load error.)
6. Click **Build**, choose an empty folder like `Builds/Web`, and wait.
   You'll get an `index.html`, plus `Build/` and `TemplateData/` folders.

## 3. Put it on itch.io

1. Make a free account at **itch.io** → top-right → **Upload new project**
   (or Dashboard → **Create new project**).
2. Fill in **Title**, **tagline**, **description**, **tags** from section 1.
3. **Kind of project: HTML**.
4. **Zip** the *contents* of `Builds/Web` so that `index.html` is at the TOP
   level of the zip (not inside an extra folder).
5. **Upload** the zip → tick **"This file will be played in the browser."**
6. **Embed options:** set a fixed size like **1280 × 720**, tick
   **"Fullscreen button"** and **"Enable scrollbars"** off.
7. Add a **cover image** (a screenshot of the game) + 2–3 screenshots.
8. Set visibility, **Save & view page**, then **Publish**.
9. Share the link. 🎉

---

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
