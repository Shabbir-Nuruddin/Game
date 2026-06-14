# Trust Issues — Setup Guide

A troll/rage platformer. This gets it running. You can't break anything.

## Step 1 — Open the project

You already have Unity 6 installed and this folder open. If not: Unity Hub →
**Add** → **Add project from disk** → choose
`C:\Users\Shabbir\Desktop\Github\Game`, then open it.

## Step 2 — Press Play

1. The game **builds itself on Play** — no GameObject to create, nothing to wire
   up (it uses an auto-boot script).
2. If your old WordBloom scene is still open with a `WordBloom`/`Game` object in
   the Hierarchy, just delete that object (right-click → Delete). Not required,
   but keeps things clean.
3. Press the **▶ Play** button.

You should see a dark screen titled **TRUST ISSUES**, the bright platforms, and
**Beanie** (the yellow blob) on the left.

## Step 3 — Play Level 1

- **Move:** `A`/`D` or `←`/`→`
- **Jump:** `Space` or `W`
- **Restart:** `R`

Goal: reach the **green** exit. But the level lies to you:
- the floor **collapses**, spikes **pop up**, a block **crushes** you if you grab
  the coins, and the bright **purple door is a trap**.
- Every trap has a faint **tell** — once you die to it, you'll know next time.
- The **real exit is green**, hidden by *dropping into the gap* before the door.

Your **death counter** (top-left) is the bragging-rights stat we'll let players
share later.

## Step 4 — Tell me what happened

- If it plays: tell me what **feels** off — jump too floaty? a trap feel unfair
  (died with no warning)? too easy/hard? That feedback is how we make it *good*.
- If you see **red errors** in the **Console** (Window → General → Console), copy
  them to me and I'll fix immediately.

## What's in the project

| File | What it does |
|------|--------------|
| `Assets/Scripts/Theme.cs` | Look + the primitive/UI factory (builds everything from one white sprite — no art needed yet). |
| `Assets/Scripts/PlayerController.cs` | Beanie's movement: coyote time, jump buffer, snappy gravity, squash & stretch. |
| `Assets/Scripts/Trap.cs` | The traps (fake floor, late spikes, crusher, fake/real exit) + a reusable KillZone. |
| `Assets/Scripts/Levels.cs` | Level 1 as data (platforms, traps, tells, bait). |
| `Assets/Scripts/Juice.cs` | Screen shake + comedic death lines. |
| `Assets/Scripts/GameRoot.cs` | Auto-boot, builds the level, death/respawn/win, death counter, HUD. |

## Next up

Real art + sound (the big "feel" jump), 5–10 more levels with new traps, a
"share your death count" button, and a WebGL build to send to testers/streamers.
