"""
Build the CASTLE BACKDROP PLATE out of the gameplay reference painting.

Why this exists: the shipped backdrop layers are low-detail — their castle is
one blobby spire cluster where the painting has a full gothic skyline. No amount
of colour tuning grows spires, so the art itself is rebuilt from the painting.

The painting can't just be cropped: lanterns, chains, a chandelier, the player,
the saw, the platform and the on-screen buttons all sit in front of the
background. So the plate is COMPOSITED out of the painting's clean regions, the
way a matte painter would:

  1. Sky      — a per-row MEDIAN across the full width. Whatever hangs in front
                is a minority of pixels on any row, so the median rejects it and
                what survives is the true sky gradient.
  2. Clouds   — a clean sky patch, mirror-tiled and heavily blurred over the
                gradient, so the sky has grain without a visible repeat.
  3. Mountains— the one clean band of valley, mirror-tiled across the width with
                a TOP-ONLY feather so the near valley isn't erased.
  4. Moon     — the pre-cleaned sphere from cut_moon.py (the painting's own disc
                has a lantern chain across it).
  5. Castle   — cropped WITH its own sky and feathered in. Keying was tried and
                is unreliable: the spires sit against the bright moon on one side
                and near-black sky on the other, so no threshold holds. Since the
                patch's sky came from the same painting as the plate's, a
                feathered composite is invisible.

SCALE IS THE THING THAT MATTERS HERE. The first version pasted patches at
W/1350, on the assumption that the painting's "window" filled the plate. It
doesn't — the plate is 32 world units wide against a camera that sees 18.4, so
the plate's pixels-per-world-unit is LOWER than the painting's, and every patch
came out about twice its proper size (a moon 70% of screen height). Everything
below goes through to_plate(), which converts a painting pixel to a plate pixel
through world units, so sizes and positions are correct by construction.

Writes Assets/Resources/art/bgc_plate.png.
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageChops

SRC = "Assets/Resources/ui/gameplay_ref.jpg"
MOON_ART = "Assets/Resources/art/moon_blood.png"
OUT = "Assets/Resources/art/bgc_plate.png"

# --- geometry ---------------------------------------------------------------
REF_W, REF_H = 1568, 1003     # the painting, which frames exactly one screen
VIEW_W = 18.4                 # world units the camera sees across (ShotBot probe)
PLATE_UNITS = 32.0            # world width of the plate (what AddParallax scales to)
W, H = 2350, 1240

PPU_REF = REF_W / VIEW_W          # painting pixels per world unit
PPU_PLATE = W / PLATE_UNITS       # plate pixels per world unit
SCALE = PPU_PLATE / PPU_REF       # ~0.86


def to_plate(xp, yp):
    """A painting pixel -> the plate pixel showing the same world point.
    The plate is centred on the camera, so screen centre maps to plate centre."""
    return (W / 2.0 + (xp - REF_W / 2.0) * SCALE,
            H / 2.0 + (yp - REF_H / 2.0) * SCALE)


# --- source regions, each stopping short of a piece of furniture -------------
SKY_TOP, SKY_BOT = 200, 520          # rows to take the sky gradient from
VALLEY = (560, 490, 950, 688)        # above the platform (700), left of the spikes (960)
CLOUD = (205, 230, 380, 350)         # clean sky: clear of the left wall and the first chain
CASTLE = (1302, 245, 1478, 545)      # spires: right of the lantern (1290), above the bat button (545)
MOON_C = (1200, 300)                 # the painted moon's centre
MOON_D = 245                         # ...and its diameter, in painting pixels

im = Image.open(SRC).convert("RGB")
px = im.load()


def median_row(y, x0=135, x1=1478):
    """The sky colour on row y, immune to whatever is hanging in front of it."""
    vals = sorted((px[x, y] for x in range(x0, x1)), key=lambda p: sum(p))
    return vals[len(vals) // 2]


# ------------------------------------------------------------- 1. sky ramp
ramp = [median_row(y) for y in range(SKY_TOP, SKY_BOT)]
plate = Image.new("RGB", (W, H))
draw = ImageDraw.Draw(plate)
_, ramp_y0 = to_plate(0, SKY_TOP)
_, ramp_y1 = to_plate(0, SKY_BOT)
for y in range(H):
    t = (y - ramp_y0) / max(1.0, ramp_y1 - ramp_y0)
    draw.line([(0, y), (W, y)], fill=ramp[min(len(ramp) - 1, max(0, int(t * (len(ramp) - 1))))])

# ---------------------------------------------------------- 2. cloud grain
patch = im.crop(CLOUD)
pw, ph = patch.size
tile = Image.new("RGB", (pw * 2, ph * 2))
tile.paste(patch, (0, 0))
tile.paste(patch.transpose(Image.FLIP_LEFT_RIGHT), (pw, 0))
tile.paste(tile.crop((0, 0, pw * 2, ph)).transpose(Image.FLIP_TOP_BOTTOM), (0, ph))
clouds = Image.new("RGB", (W, H))
for oy in range(0, H, ph * 2):
    for ox in range(0, W, pw * 2):
        clouds.paste(tile, (ox, oy))
# Heavy blur, light blend: any sharper and the tile seams read as vertical
# banding across the whole sky, which is worse than having no grain at all.
clouds = clouds.filter(ImageFilter.GaussianBlur(9.0))
plate = Image.blend(plate, ImageChops.add(plate, clouds, scale=2.4, offset=-6), 0.30)


def edge_mask(size, frac):
    """Fade the outer `frac` of a patch to nothing on all four sides."""
    w, h = size
    m = Image.new("L", size, 255)
    d = ImageDraw.Draw(m)
    b = max(2, int(min(w, h) * frac))
    for i in range(b):
        d.rectangle([i, i, w - 1 - i, h - 1 - i], outline=int(255 * i / b))
    return m.filter(ImageFilter.GaussianBlur(b * 0.35))


# ----------------------------------------------------------- 3. mountains
band = im.crop(VALLEY)
strip = Image.new("RGB", (band.width * 2, band.height))
strip.paste(band, (0, 0))
strip.paste(band.transpose(Image.FLIP_LEFT_RIGHT), (band.width, 0))
strip = strip.resize((int(strip.width * SCALE), int(strip.height * SCALE)), Image.LANCZOS)

# TOP-ONLY feather: an all-round one also faded the band's BOTTOM out, which
# erased the near valley and left the ridges floating in a dark field.
mtn = Image.new("L", strip.size, 255)
md_ = ImageDraw.Draw(mtn)
fade = int(strip.height * 0.22)
for i in range(fade):
    md_.line([(0, i), (strip.width, i)], fill=int(255 * i / fade))
mtn = mtn.filter(ImageFilter.GaussianBlur(7))

_, horizon = to_plate(0, VALLEY[1])
for ox in range(0, W, strip.width):
    plate.paste(strip, (ox, int(horizon)), mtn)

# ---------------------------------------------------------------- 4. moon
moon = Image.open(MOON_ART).convert("RGBA")
# The PNG is 256 wide but its DISC is ~196 of that; the rest is halo. Size it so
# the disc lands at the painted diameter, not the whole sprite.
full = int(MOON_D * SCALE * 256.0 / 196.0)
moon = moon.resize((full, full), Image.LANCZOS)
mx, my = to_plate(*MOON_C)
plate.paste(moon, (int(mx - full / 2), int(my - full / 2)), moon)

# -------------------------------------------------------------- 5. castle
# Pasted after the moon so the spires bite into it exactly as painted — that
# overlap is most of what makes the skyline read as distance.
cas = im.crop(CASTLE)
cas = cas.resize((int(cas.width * SCALE), int(cas.height * SCALE)), Image.LANCZOS)
cx, cy = to_plate(CASTLE[0], CASTLE[1])

# The patch carries its own sky, and that sky is brighter than the plate's here
# (it's sitting in the moon's glow), so a plain feathered paste left a faint
# lighter RECTANGLE around the spires. Fix: multiply the feather by a darkness
# key, so only the dark stone of the spires is opaque and the patch's sky drops
# out entirely. Lit windows are kept by their redness — a pure luma key would
# punch holes straight through them.
key = Image.new("L", cas.size, 0)
kp, cp = key.load(), cas.load()
for y in range(cas.height):
    for x in range(cas.width):
        r, g, b = cp[x, y]
        luma = 0.299 * r + 0.587 * g + 0.114 * b
        a = 1.0 - min(1.0, max(0.0, (luma - 26.0) / 34.0))     # dark -> opaque
        if r - g > 45 and r > 70:                              # a lit window
            a = 1.0
        kp[x, y] = int(255 * a)
key = key.filter(ImageFilter.GaussianBlur(1.2))
plate.paste(cas, (int(cx), int(cy)), ImageChops.multiply(key, edge_mask(cas.size, 0.10)))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
plate.save(OUT)
print(f"wrote {OUT} at {W}x{H} (patch scale {SCALE:.3f}, moon {full}px)")
