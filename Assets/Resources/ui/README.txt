TRUST ISSUES — MENU SKIN ART
============================

Drop your exact menu artwork into THIS folder (Assets/Resources/ui/) as PNG
files with these exact names. The moment a file is here, that screen renders
your art instead of the code-built menu. If a file is missing, that screen
just uses the old look — nothing breaks.

Use 1920 x 1080 (16:9) PNGs so they line up with no stretching.

FILES
-----
menu_bg.png        -> Main menu                    (WIRED)
castle_bg.png      -> The Castle / level select    (WIRED)
settings_bg.png    -> Settings screen              (WIRED)
bestiary_bg.png    -> Vampire's Bestiary           (WIRED)
wardrobe_bg.png    -> Wardrobe                     (WIRED)
leaderboard_bg.png -> Leaderboard                  (WIRED)
shop_bg.png        -> The Crypt Shop               (WIRED — drop the file in)
pause_bg.png       -> Pause menu                   (WIRED)

THE CRYPT SHOP (shop_bg)
------------------------
Save the Crypt Shop artwork here as shop_bg.png (or .jpg). Only the OUTER
chrome of that picture is used — the frame, gargoyles, drapes, candles, skulls
and the painted "THE CRYPT SHOP" title. Everything inside the panel (the shard
count, the card grid, every "NEED N MORE") is covered and drawn live, because
those numbers change with your money and the shelf changes with the tab.
The painted "‹ BACK" plate along the bottom stays and just gets a tap-zone.

Nothing is required: with no file there the shop wears the code-built gothic
version of the same screen, with identical layout.

LEAVE THESE SPOTS BLANK IN menu_bg.png
--------------------------------------
These values change while you play, so the game paints them itself on top.
Draw the button frame / icon, but NOT the words, for:

  * the big CONTINUE button  (floor number changes)
  * the DIFFICULTY line      (NORMAL / CASUAL / NIGHTMARE changes)
  * the SHOP cell            (shard balance changes)
  * the BESTIARY cell        (x/19 count changes)
  * the bottom notice line   (nightly messages appear here)

Every OTHER button (BLOOD MOON, THE CASTLE, ENDLESS NIGHT, MULTIPLAYER,
WARDROBE, SETTINGS, LEADERBOARD) can keep its painted text — the game only
lays an invisible tap-zone over it.

If the live text doesn't sit perfectly over your art, tell me and I'll nudge
the numbers in BuildSkinnedMenu() until it's pixel-perfect.
