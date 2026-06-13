# Morning Words — Setup Guide

A calm, senior-friendly daily word game. This guide gets the prototype running
on your machine. Take it one step at a time — you can't break anything.

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
   type **GameBootstrap**, and select it.
4. Press the big **▶ Play** button at the top.

You should see **Morning Words** with a grid of big tiles and an on-screen
keyboard. Click letters, then **ENTER** to guess.

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
| `Assets/Scripts/WordGame.cs` | The pure game rules (guessing, scoring). No Unity code — easy to test. |
| `Assets/Scripts/WordList.cs` | The starter word bank + "word of the day" logic. |
| `Assets/Scripts/GameBootstrap.cs` | Builds the whole senior-friendly screen from code. |
| `Packages/manifest.json` | Tells Unity which built-in packages we use. |
| `.gitignore` | Keeps Unity's auto-generated junk out of git. |

## Ideas we'll add next (in rough order)

- A nicer font (TextMeshPro) and softer tile animations
- Keyboard keys that change colour as you learn letters
- A "you've played X days in a row" streak (the daily-ritual hook)
- A friendly results screen you can share with family (the social hook)
- Settings: even-bigger text, read-aloud clue (accessibility)
- A real, larger word list loaded from a file
