# Art & Sound — what to grab and how to import it

The game currently draws everything from coloured boxes (zero assets). To make it
look like a real game, we drop in an **art pack + sound pack**, then I rewire the
code to use those sprites instead of boxes. Here's exactly what to get and how.

## 1. Where to get free, commercial-safe assets

**Best starting point: [Kenney.nl](https://kenney.nl/assets) — 100% free, CC0
(no credit needed, fine to sell).** No account required. Grab these:

| Pack (search on kenney.nl) | Use for |
|---|---|
| **Pixel Platformer** | Character (Beanie), ground tiles, spikes, coins |
| **Platformer Pack Redux** | Alternate character + tiles if you prefer |
| **Particle Pack** | Death poof, portal sparkles |
| **UI Pack** | Menu buttons, panels |
| **Interface Sounds** / **Digital Audio** | Click, jump, coin, death SFX |
| **Music Jingles / Music Loops** | Menu + level background music |

Other good free sources: **itch.io** (search "platformer asset pack", filter Free),
**OpenGameArt.org**, and the **Unity Asset Store** (filter Free).

For a *unique* look later: generate sprites with an AI image tool, or hire a pixel
artist on Fiverr (a small character + tileset is usually cheap).

## What we specifically need (give me these and I wire them in):
- **Character**: idle, run (a few frames), jump, "squished/dead" — for Beanie
- **Tiles**: ground/platform, and a darker "crack" tile for fake floors
- **Hazards**: a spike sprite, a crusher block
- **Props**: coin, a portal sprite (or a glowy circle), a door (fake exit), a flag/door (real exit)
- **Background**: 1–2 layers (sky + distant shapes) for parallax
- **SFX**: jump, land, coin, death, portal, win
- **Music**: 1 menu loop, 1 level loop

## 2. How to import into Unity (step by step)

1. **Download** a pack (usually a `.zip`). Unzip it.
2. In Unity's **Project** window, open `Assets`. **Create a folder** `Art`
   (right-click → Create → Folder). Make `Art/Sprites`, `Art/Audio`.
3. **Drag the image/audio files** from your unzipped folder straight into those
   Unity folders. Unity imports them automatically.
4. **For a single sprite** (one image = one object): click it, in the Inspector
   set **Texture Type = Sprite (2D and UI)**, and **Pixels Per Unit** to taste
   (start at 16 or 32 for pixel art), then **Apply**.
5. **For a sprite SHEET** (many frames in one image): select it, set
   **Sprite Mode = Multiple**, open **Sprite Editor** → **Slice** → *Grid By Cell
   Size* or *Automatic* → **Apply**. It splits into individual frames.
6. **Pixel-art crispness:** select the texture → **Filter Mode = Point (no filter)**,
   **Compression = None** → Apply. (Stops it looking blurry.)

## 3. Then tell me, and I'll:
- Swap the coloured boxes for your sprites (platforms, Beanie, spikes, portal, exits).
- Add simple frame animations (idle/run/jump) and the death squish on the real sprite.
- Add an `AudioSource` + hook up jump/death/coin/portal/win SFX and background music.
- Add particle poofs on death and portal sparkles.

You don't need to wire anything in code — just get the files into `Assets/Art`
and I'll do the integration. Start with **Kenney's Pixel Platformer + a sound
pack**; that alone will transform how it looks and feels.
