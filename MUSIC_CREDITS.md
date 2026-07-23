# Music credits — REQUIRED ATTRIBUTION

All music in Trust Issues is by **Kevin MacLeod** (incompetech.com), licensed under
**Creative Commons Attribution (CC BY)**.

CC BY is free to use commercially — including a paid or ad-supported Play Store
release — **but attribution is mandatory**. Removing these credits makes the game's
music licence invalid. The same text is shown in-game under Settings, and the block
at the bottom of this file should be pasted into the Play Store listing description.

| In-game use | Track | Why it was chosen |
|---|---|---|
| Main menu | Gymnopedie No 1 | Satie's melancholy piano — elegant and unhurried, the aristocratic side of a vampire. Chosen because a menu track is heard more than any other and must never grate. |
| The Castle (floors 1–10) | Ossuary 1 - A Beginning | An ossuary is a chamber of bones; "A Beginning" is literally floor 1. Slow and ominous but melodic, so it survives repetition. |
| The Crypt (floors 11–20) | Bump in the Night | Creeping and cold — the world where the candles start going out. |
| The Swamp (floors 21–30) | Deep Haze | Murky and smothering, matching the swamp's sickly green. |
| The Throne (floors 31–40) | Grim Idol | Grand and menacing — the final world, where the castle stops pretending. |
| Blood Moon | Nightmare Machine | Relentless and mechanical, for the nightly gauntlet. |
| Multiplayer arena | Dark Times | Driving and tense, for a live race. |
| Custom maps | Myst on the Moor | Eerie and open — an unfamiliar place someone else built. |
| Boss fights | (existing track, unchanged) | |

## Attribution block — paste into the Play Store listing

```
Music by Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0
http://creativecommons.org/licenses/by/4.0/

Gymnopedie No 1 · Ossuary 1 - A Beginning · Bump in the Night · Deep Haze
Grim Idol · Nightmare Machine · Dark Times · Myst on the Moor
```

## A note on selection

These were chosen by name, description and composer intent, not by listening.
Swapping any of them is a one-file operation: drop a new MP3 into
`Assets/Resources/audio/` using the same filename (`music_castle.mp3`,
`music_crypt.mp3`, `music_swamp.mp3`, `music_throne.mp3`, `music_bloodmoon.mp3`,
`music_arena.mp3`, `music_void.mp3`, or `music.mp3` for the menu) and rebuild.
No code changes are needed — the game already looks for exactly these names and
falls back to `music.mp3` if one is missing.
