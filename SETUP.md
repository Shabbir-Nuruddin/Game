# WordBloom — Setup Guide

A cozy, level-based word game (Word Cookies / Wordscapes style): spell words from
a wheel of letters to fill the board, beat the level, and grow your streak. This
guide gets it running on your machine. Take it one step at a time — you can't
break anything.

---

## Step 1 — Install Unity (one time)

1. Go to **https://unity.com/download** and install **Unity Hub**.
2. Open Unity Hub → **Installs** tab → **Install Editor** → pick the latest
   **LTS** version (a green "LTS" label). LTS = the stable one.
3. When it asks which **modules** to add, tick **Android Build Support**
   (we'll need it later to put the game on a phone). You can add it later too.
4. Create a free **Unity account** when prompted and sign in. Choose the
   **Personal** (free) plan — it's free until you earn $200,000/year.

> This whole step happens on your computer. I never need your login or any keys.

---

## Step 2 — Open this project

1. In Unity Hub → **Projects** tab → **Add** → **Add project from disk**.
2. Choose this folder:
   `C:\Users\Shabbir\Desktop\Github\Game`
3. If Hub warns the project was made with a different version, click
   **"Open with [your installed version]"** — that's fine.
4. The first open takes a few minutes while Unity builds its `Library/` folder.
   That's normal and only happens once. (We never commit `Library/` — see
   `.gitignore`.)

---

## Step 3 — Wire up the one GameObject (30 seconds, one time)

The game builds its whole screen from code, so there is only **one** thing to set up:

1. In the **Project** window (bottom), open `Assets/Scenes`. If there's no scene,
   make one: top menu **File → New Scene → Basic (Built-in)**, then
   **File → Save As…** and save it as `Assets/Scenes/Main.unity`.
2. In the **Hierarchy** window (left), right-click → **Create Empty**.
   Rename it to `Game`.
3. With `Game` selected, in the **Inspector** (right) click **Add Component**,
   type **Game**, and select the **Game** script.
4. Press the big **▶ Play** button at the top.

You should see the **WordBloom** home screen with a **PLAY** button. Tap it to
start Level 1: tap letters on the wheel to spell a word, then **ENTER**. Found
words fill the board; finish them all to clear the level.

> Tip: in the Game view, set the aspect ratio dropdown to a **portrait phone**
> ratio (e.g. 9:16) so it looks like a phone.

---

## Step 4 — Tell me what happened

- If it works: tell me, and we'll start making it prettier and adding features.
- If you see **red errors** in the **Console** window (menu **Window → General →
  Console**), copy the text and paste it to me. I'll fix it.

---

## What's in the project

| File | What it does |
|------|--------------|
| `Assets/Scripts/Brand.cs` | The game's identity: name, colours, fonts, UI + animation helpers. |
| `Assets/Scripts/Levels.cs` | Hand-authored levels (wheel letters + words to find). |
| `Assets/Scripts/WordPuzzle.cs` | Pure level logic (found words, bonus, hints). No Unity code. |
| `Assets/Scripts/Game.cs` | Builds the Home + gameplay screens, wheel input, animations, coins, save. |
| `Packages/manifest.json` | Tells Unity which built-in packages we use. |
| `.gitignore` | Keeps Unity's auto-generated junk out of git. |

## What works right now

- Home screen with animated title + **PLAY**
- Word-Cookies-style gameplay: tap the letter wheel, **ENTER** to submit
- Answer tiles that pop in when you find a word; wrong words shake
- **Bonus words** earn coins; **Hint** spends coins to reveal a letter
- **Level-complete** celebration with stars, then **NEXT**
- Progress + coins **saved** between sessions (10 hand-made levels to start)

## Ideas we'll add next (in rough order)

- Real art + sound (juice) via asset packs — the biggest "feel" upgrade
- TextMeshPro for crisper text and nicer fonts
- A **level map / journey** screen instead of a plain level number
- A **daily streak** + daily puzzle (the habit hook)
- A friendly **share-with-family** results card (the social/growth hook)
- Auto-generated levels from a big dictionary (hundreds of levels)
- Settings: even-bigger text, read-aloud (accessibility)
