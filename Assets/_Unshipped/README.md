# Unshipped art

Artwork that is kept in the project and in version control, but is **not in the
build**.

## Why this folder exists

Everything under `Assets/Resources/` is force-packed into every build whether a
single line of code references it or not — that is what a Resources folder *is*.
It is not like the rest of `Assets/`, where Unity only ships what a scene or
another shipped asset actually points at.

So a retired background left in `Resources/ui/` costs the player a download every
time, forever, for a screen the game no longer draws. These files were doing
exactly that: **8.6 MB of source art**, none of it reachable from code.

Moving them one folder up is enough. They are still here, still in git, still
openable — they simply stop being packed.

## What's in here

| File | Why it isn't shipping |
| --- | --- |
| `escape_ref.jpg`, `gameplay_ref.jpg`, `multiplayer_ref.jpg`, `wardrobe_ref.jpg` | Design references — mockups used while building the screens, never meant to ship |
| `menu_bg.jpg`, `menu_bg_v2.png` | Superseded by `ui/landing_bg.jpg`. `menu_bg_v2` is a painting of an older landing layout (a CONTINUE plate, no record button) that the current screen can't wear |
| `pause_bg.jpg` | The painted pause screen was retired with the other baked screens |
| `castle_bg.jpg`, `settings_bg.jpg` | Superseded by the code-built red-and-black screens |
| `wardrobe_bg_v2.png` | Superseded by `ui/wardrobe_bg.jpg` |
| `endless_theme_atlas.png` | Superseded by `endless_theme_atlas_v2.png`, which is the one `EndlessThemeBackdrop.cs` loads |

## To put one back in the game

Move the file **and its `.meta`** into `Assets/Resources/ui/` (or `art/`), then
load it by name — `Skin.Background(root, "menu_bg_v2")`. Keeping the `.meta` with
it preserves the GUID, so nothing else in the project breaks.

## Before adding anything to Resources

Ask whether the game loads it *by name at runtime*. If it doesn't, it belongs
somewhere else in `Assets/` — every megabyte in `Resources/` is a megabyte of
install size, and install size is the first thing a new player judges you on.
