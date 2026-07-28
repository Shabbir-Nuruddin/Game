"""
Build the CASTLE BACKDROP PLATE from the gameplay reference painting.

APPROACH. Earlier versions rebuilt the backdrop by compositing pieces — a
synthesised sky ramp, a tiled mountain band, the moon and castle pasted on as
rectangular patches. Every version showed its seams, because each patch carries
its own sky and no two skies matched. Worse, rebuilding the sky threw away the
relationships the painting already has: the glow around the moon, the way the
spires darken against it, the haze on the far ridges.

So nothing is rebuilt now. The plate IS the painting's background, with the
foreground furniture ERASED from it:

  1. Crop the painting's background window (inside the walls, below the vault).
  2. Mask every foreground object — the three lanterns and their chains and
     glows, the chandelier, the player, the saw, the spikes, the platforms, and
     the mockup's own on-screen buttons, which are UI and not scenery.
  3. Fill those holes by DIFFUSION: blur the whole image, paste the known
     pixels back over it, repeat. Each pass pulls colour a little further into
     each hole from its rim, so holes close smoothly from their surroundings
     while every unmasked pixel stays bit-for-bit original. Sky and hillside are
     smooth, so this is invisible on them; and the moon, the castle and the
     ridges are never masked at all, so they keep every bit of painted detail.
  4. Widen to the plate's 32 world units by mirroring the clean left end
     outward, so the level can scroll without running off the art.

Writes Assets/Resources/art/bgc_plate.png.
"""
import os
from PIL import Image, ImageDraw, ImageFilter

SRC = "Assets/Resources/ui/gameplay_ref.jpg"
OUT = "Assets/Resources/art/bgc_plate.png"

# --- geometry ---------------------------------------------------------------
REF_W = 1568                  # the painting frames exactly one screen
VIEW_W = 18.4                 # world units the camera sees across (ShotBot probe)
PLATE_UNITS = 32.0            # world width of the plate (what AddParallax scales to)

# The background window: inside the side walls, below the vault, down past the
# platforms into the lower valley.
WIN = (152, 196, 1472, 944)

# Every foreground object in that window, generously boxed. These are the
# painting's furniture and the mockup's UI — neither belongs in a backdrop.
FURNITURE = [
    (382, 196, 522, 545),      # left lantern: chain, cage, glow
    (818, 196, 868, 390),      # the chandelier's chain
    (686, 362, 1008, 498),     # the chandelier itself
    (1188, 196, 1308, 548),    # right lantern: chain, cage, glow
    (252, 600, 368, 728),      # the vampire
    (402, 600, 532, 728),      # the saw blade
    (940, 676, 1058, 722),     # floor spikes
    (136, 694, 664, 790),      # left platform
    (832, 694, 1478, 790),     # right platform
    (72, 730, 484, 944),       # the two arrow buttons
    (1272, 716, 1512, 944),    # the jump button
    (1366, 526, 1548, 714),    # the bat button
    (496, 900, 1074, 944),     # the footer ornament
]


def diffuse_inpaint(img, mask, passes=70, radius=7):
    """Close the masked holes by repeated blur-and-restore.

    The masked area is WIPED FIRST with a very heavy blur of the whole image.
    Without that wipe the holes never actually clear: blur-and-restore converges
    on the right answer eventually, but a platform or a button survives dozens of
    passes as a soft grey ghost of itself, which is exactly what the first
    attempt produced. Wiping destroys the object immediately and leaves the
    passes doing what they're good at — smoothing the fill into its rim.

    Unmasked pixels are pasted back every pass, so the moon, the spires and the
    ridges come through completely untouched.
    """
    keep = mask.point(lambda v: 255 - v)
    work = img.copy()
    work.paste(img.filter(ImageFilter.GaussianBlur(70)), (0, 0), mask)
    work.paste(img, (0, 0), keep)
    for _ in range(passes):
        work = work.filter(ImageFilter.GaussianBlur(radius))
        work.paste(img, (0, 0), keep)
    return work


im = Image.open(SRC).convert("RGB")
win = im.crop(WIN)

# ------------------------------------------------------------------- mask
mask = Image.new("L", win.size, 0)
d = ImageDraw.Draw(mask)
for x0, y0, x1, y1 in FURNITURE:
    d.rectangle([x0 - WIN[0], y0 - WIN[1], x1 - WIN[0], y1 - WIN[1]], fill=255)
# Soften the mask edges so the fill blends into the kept pixels instead of
# meeting them on a hard line.
mask = mask.filter(ImageFilter.GaussianBlur(3))
mask = mask.point(lambda v: 255 if v > 40 else v * 3)

clean = diffuse_inpaint(win, mask)

# --------------------------------------------------------------- the plate
# Plate pixels-per-world-unit is lower than the painting's (32 units of plate vs
# 18.4 of view), so the window is scaled DOWN into plate space. Getting this
# wrong is what once produced a moon filling 70% of the screen.
DENSITY = 0.862                   # plate px per painting px (see header)
W = int(PLATE_UNITS * (REF_W / VIEW_W) * DENSITY)
PPU = W / PLATE_UNITS             # plate pixels per world unit
body_w = int(clean.width * DENSITY)
body_h = int(clean.height * DENSITY)
body = clean.resize((body_w, body_h), Image.LANCZOS)

# The painted window is shorter than the camera's view, so the plate is padded
# above and below by repeating its edge rows. Without this the backdrop simply
# stops partway up the screen and the clear colour shows through underneath the
# level — the window covers only about three quarters of the view's height.
PAD_TOP, PAD_BOT = 80, 220
H = body_h + PAD_TOP + PAD_BOT
padded = Image.new("RGB", (body_w, H))
padded.paste(body, (0, PAD_TOP))
padded.paste(body.crop((0, 0, body_w, 1)).resize((body_w, PAD_TOP)), (0, 0))
padded.paste(body.crop((0, body_h - 1, body_w, body_h)).resize((body_w, PAD_BOT)),
             (0, PAD_TOP + body_h))
body = padded

# Where the plate has to sit so its painted content lands where it was painted.
# Painting row 0 is the top of the screen, which is world y +6.23; the view is
# 11.76 units tall over 1003 rows.
win_top_world = 6.23 - WIN[1] * (11.76 / 1003.0)
plate_top_world = win_top_world + PAD_TOP / PPU
y_centre = plate_top_world - (H / PPU) / 2.0

plate = Image.new("RGB", (W, H))
left = (W - body_w) // 2

# The plate is wider than the painted window, so the remainder is filled by
# repeating only the window's LEFT PORTION — plain sky, hillside and haze, with
# no landmark in it. Mirroring the whole window (the obvious approach) puts a
# second blood moon and a second castle on the plate, and on a wide level you
# scroll far enough to see both at once.
filler = body.crop((0, 0, int(body_w * 0.42), H))
fw = filler.width
fflip = filler.transpose(Image.FLIP_LEFT_RIGHT)
x, i = left - fw, 0
while x > -fw:
    plate.paste(fflip if i % 2 else filler, (x, 0)); x -= fw; i += 1
x, i = left + body_w, 0
while x < W:
    plate.paste(filler if i % 2 else fflip, (x, 0)); x += fw; i += 1
plate.paste(body, (left, 0))      # the painted window itself always wins

os.makedirs(os.path.dirname(OUT), exist_ok=True)
plate.save(OUT)
print(f"wrote {OUT} at {W}x{H} (window {body_w}x{body_h})")
print(f"PLATE_Y_CENTRE = {y_centre:.2f}   <- pass this as AddParallax's yCenter")
