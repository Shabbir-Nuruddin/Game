TRUST ISSUES — MENU SKIN ART
============================

Drop your exact menu artwork into THIS folder (Assets/Resources/ui/) as PNG
files with these exact names. The moment a file is here, that screen renders
your art instead of the code-built menu. If a file is missing, that screen
just uses the old look — nothing breaks.

Use 1920 x 1080 (16:9) PNGs so they line up with no stretching.

FILES
-----
menu_bg.png       -> Main menu (WIRED UP NOW — add this first)
castle_bg.png     -> The Castle / level select   (coming next)
settings_bg.png   -> Settings screen             (coming next)
bestiary_bg.png   -> Vampire's Bestiary           (coming next)

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
