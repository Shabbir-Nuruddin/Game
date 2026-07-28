"""
Build the BLOOD MOON from the gameplay reference painting.

The game drew a flat untextured disc, which is the main reason the sky read as a
gradient with a sticker on it rather than a place.

Cutting the moon out wholesale doesn't work: the lantern chain hangs straight
down across it, the lantern itself covers the bottom, and the ceiling beam clips
the top, so every crop drags in furniture, and mirroring to hide the furniture
just produced a symmetric hourglass.

So instead: take one PATCH of clean lunar surface from inside the disc, mirror-
tile it to fill the square, then build a real sphere out of it — circular alpha,
limb darkening so the rim falls off like a lit ball rather than a cookie cutter,
and a soft outer halo. The painting supplies the texture; the geometry is
generated, which is what makes it clean.

Writes Assets/Resources/art/moon_blood.png.
"""
import math
from PIL import Image, ImageFilter

SRC = "Assets/Resources/ui/gameplay_ref.jpg"
OUT = "Assets/Resources/art/moon_blood.png"

# Measured off the painting at 2x: the disc runs x 1085..1290, y 210..390.
# This patch sits in the mid-tone mare on its UPPER LEFT — the only quarter with
# no furniture over it and no hard dark feature in it. An earlier patch reached
# to x=1220 and dragged in the chain's shadow, which then mirror-tiled into a
# black wedge across the finished moon.
PATCH = (1105, 250, 1195, 340)
S = 256                      # output size
HALO = 1.30                  # halo reach, in disc radii

im = Image.open(SRC).convert("RGB")
patch = im.crop(PATCH)
pw, ph = patch.size

# --- mirror-tile the patch out to the full square -------------------------
# Mirroring rather than repeating means the tile seams line up in value, so no
# grid shows up across the finished sphere.
tile = Image.new("RGB", (pw * 2, ph * 2))
tile.paste(patch, (0, 0))
tile.paste(patch.transpose(Image.FLIP_LEFT_RIGHT), (pw, 0))
top = tile.crop((0, 0, pw * 2, ph))
tile.paste(top.transpose(Image.FLIP_TOP_BOTTOM), (0, ph))

surface = Image.new("RGB", (S, S))
for oy in range(0, S, ph * 2):
    for ox in range(0, S, pw * 2):
        surface.paste(tile, (ox, oy))
surface = surface.filter(ImageFilter.GaussianBlur(0.8))   # knock the seams back

# --- shade it into a sphere ------------------------------------------------
out = Image.new("RGBA", (S, S))
sp, op = surface.load(), out.load()
c = (S - 1) / 2.0
R = S / 2.0 / HALO           # the disc itself; the rest of the square is halo

for y in range(S):
    for x in range(S):
        d = math.hypot(x - c, y - c) / R
        r, g, b = sp[x, y]
        if d <= 1.0:
            # Limb darkening: a lit sphere loses brightness toward its edge.
            # Without this the disc reads as a flat sticker no matter how good
            # the texture on it is.
            k = 0.55 + 0.45 * math.sqrt(max(0.0, 1.0 - d * d))
            a = 255 if d < 0.985 else int(255 * (1.0 - (d - 0.985) / 0.015))
            op[x, y] = (int(r * k), int(g * k), int(b * k), a)
        else:
            # Halo: the painting's moon bleeds a warm red into the sky around it.
            t = max(0.0, 1.0 - (d - 1.0) / (HALO - 1.0))
            op[x, y] = (int(r * 0.55), int(g * 0.32), int(b * 0.32), int(150 * t * t))

out.save(OUT)
print(f"wrote {OUT} at {S}x{S} (disc r={R:.0f}px, halo to {S/2:.0f}px)")
