"""
Cut the real SCENERY out of the gameplay reference painting: the blood moon,
the gothic castle, the hanging lantern and the side walls.

Everything here was previously approximated in code or synthesised, and it
showed — a flat disc for a moon, a spire cluster with no base ("a castle
halfway flying in the air"), plain stone bands for walls. The painting has all
of it, in detail, with depth. The only reason it wasn't simply cropped before is
that the painting's own furniture sits in front of each piece:

    moon    - a lantern chain hangs straight down across it, lantern below
    castle  - the same lantern clips its left edge; the bat BUTTON (part of the
              mockup's UI, not its scenery) covers the lower right and the ridge
    lantern - clean, but needs keying off its sky
    walls   - clean, apart from HUD and buttons top and bottom

So each piece is cropped and its occluders are painted out by horizontal
inpainting: masked pixels are filled by pushing in the nearest unmasked pixel on
the same row, then softening. That works here because everything being removed
is NARROW relative to what surrounds it (a chain, a lantern, one button) and the
material either side is continuous — sky, castle mass, hillside. It preserves
the painting's real texture, which is the entire point; a synthesised fill would
put us back where we started.

Writes bgc_moon / bgc_castle / lantern_art / wall_left / wall_right into
Assets/Resources/art/.
"""
import os
from PIL import Image, ImageDraw, ImageFilter

SRC = "Assets/Resources/ui/gameplay_ref.jpg"
ART = "Assets/Resources/art"

im = Image.open(SRC).convert("RGB")


# --------------------------------------------------------------- inpainting
def inpaint(img, mask):
    """Fill every white-in-mask pixel from the nearest clean pixel on its row.

    Scans left-to-right then right-to-left and blends the two, so a hole is fed
    from BOTH sides rather than smearing one edge across it. Only the filled
    area is softened afterwards, so real detail elsewhere stays sharp.
    """
    w, h = img.size
    src, msk = img.load(), mask.load()
    left = Image.new("RGB", (w, h))
    right = Image.new("RGB", (w, h))
    lp, rp = left.load(), right.load()

    for y in range(h):
        last = None
        for x in range(w):
            if msk[x, y] < 128:
                last = src[x, y]
            lp[x, y] = last if last else src[x, y]
        last = None
        for x in range(w - 1, -1, -1):
            if msk[x, y] < 128:
                last = src[x, y]
            rp[x, y] = last if last else src[x, y]

    filled = Image.blend(left, right, 0.5).filter(ImageFilter.GaussianBlur(3.0))
    out = img.copy()
    out.paste(filled, (0, 0), mask.filter(ImageFilter.GaussianBlur(1.5)))
    return out


def box_mask(size, origin, boxes=(), circles=()):
    """A mask (white = repaint me) for occluders given in PAINTING coordinates."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    ox, oy = origin
    for x0, y0, x1, y1 in boxes:
        d.rectangle([x0 - ox, y0 - oy, x1 - ox, y1 - oy], fill=255)
    for cx, cy, r in circles:
        d.ellipse([cx - r - ox, cy - r - oy, cx + r - ox, cy + r - oy], fill=255)
    return m


def feather(size, frac):
    w, h = size
    m = Image.new("L", size, 255)
    d = ImageDraw.Draw(m)
    b = max(2, int(min(w, h) * frac))
    for i in range(b):
        d.rectangle([i, i, w - 1 - i, h - 1 - i], outline=int(255 * i / b))
    return m.filter(ImageFilter.GaussianBlur(b * 0.35))


# ------------------------------------------------------------------- moon
# Kept WITH its surrounding sky: the moon's halo is painted into that sky, and
# cutting a hard disc out throws the halo away — which is most of why the
# synthesised version read as a sticker.
#
# Inpainting is the WRONG tool for the moon and was tried first. The chain is a
# 34px band with the bright disc on one side and near-black sky on the other,
# so filling from both sides produced a wide grey smear straight down the
# moon's face. Mirroring is right instead: a moon is a disc, its left half is
# completely clean (the chain starts at x 1226, the lantern at x 1200), and
# reflecting that half about the disc's centre rebuilds it with the painting's
# own texture and none of the furniture.
#
# Measured off a gridded 2x crop, not guessed: the disc's leftmost point is
# (1105, 293) and its lowest is (1225, 400), which puts the centre at
# (1215, 293) with r=107. Getting this wrong is what made earlier attempts
# come out as teardrops — the mirror axis has to be the real centre.
MOON_CX, MOON_CY, MOON_R = 1215, 293, 107
PAD = 8                                  # a little sky, to carry the halo
MOON = (MOON_CX - MOON_R - PAD, 186, MOON_CX + MOON_R + PAD, MOON_CY + MOON_R + PAD)
moon = im.crop(MOON)
axis = MOON_CX - MOON[0]
half = moon.crop((0, 0, axis, moon.height))
moon.paste(half.transpose(Image.FLIP_LEFT_RIGHT), (axis, 0))
# ...and the same trick vertically. The ceiling beam clips the disc's top ~10px
# in the painting, so the clean BOTTOM half is reflected upward to replace it.
ay = MOON_CY - MOON[1]
lower = moon.crop((0, ay, moon.width, min(moon.height, ay * 2)))
moon.paste(lower.transpose(Image.FLIP_TOP_BOTTOM), (0, 0))
# Kept WITH its surrounding sky: the halo is painted into that sky, and cutting
# a hard disc out throws the halo away — which is most of why a clean-cut moon
# reads as a sticker.
moon.putalpha(feather(moon.size, 0.15))
moon.save(f"{ART}/bgc_moon.png")
print("bgc_moon", moon.size)

# ----------------------------------------------------------------- castle
# Down to the hillside it stands on. The old crop stopped at y 545 to dodge the
# bat button and that is exactly why the castle floated.
CASTLE = (1262, 282, 1478, 706)
cas = im.crop(CASTLE)
cas = inpaint(cas, box_mask(cas.size, CASTLE[:2],
                            boxes=[(1262, 344, 1292, 500)],      # lantern clipping the left edge
                            circles=[(1456, 618, 96)]))          # the mockup's bat BUTTON (+ its outer ring)
# Key on darkness so the patch's own sky drops out instead of leaving a lighter
# rectangle; lit windows are held in by their redness, which a luma key alone
# would punch holes straight through.
key = Image.new("L", cas.size, 0)
kp, cp = key.load(), cas.load()
for y in range(cas.height):
    for x in range(cas.width):
        r, g, b = cp[x, y]
        luma = 0.299 * r + 0.587 * g + 0.114 * b
        a = 1.0 - min(1.0, max(0.0, (luma - 30.0) / 40.0))
        if r - g > 45 and r > 70:
            a = 1.0
        kp[x, y] = int(255 * a)
key = key.filter(ImageFilter.GaussianBlur(1.4))
cas.putalpha(key)
cas.save(f"{ART}/bgc_castle.png")
print("bgc_castle", cas.size)

# ---------------------------------------------------------------- lantern
# The iron cage, its flame and the ring it hangs from — everything except the
# chain, which the game draws itself at whatever length each ceiling needs.
LANT = (1204, 352, 1292, 496)
lant = im.crop(LANT)
lk = Image.new("L", lant.size, 0)
kp, cp = lk.load(), lant.load()
for y in range(lant.height):
    for x in range(lant.width):
        r, g, b = cp[x, y]
        luma = 0.299 * r + 0.587 * g + 0.114 * b
        # Both the dark ironwork AND the bright flame are the lantern; only the
        # mid-value red sky behind it is not.
        kp[x, y] = 255 if (luma < 46 or luma > 95 or (r > 120 and g > 60)) else 0
lk = lk.filter(ImageFilter.GaussianBlur(0.8))
lant.putalpha(lk)
lant.save(f"{ART}/lantern_art.png")
print("lantern_art", lant.size)

# ----------------------------------------------------------- joystick ring
# The joystick's base was a plain procedural circle while the arrows and the bat
# beside it wore the painting's ornate gold rings — so the one control you look
# at most was the one that looked least like the game. This reuses the jump
# button's ring and punches its middle out, leaving the metal, the corner studs
# and the rim highlights, with the centre clear so the vampire stays visible
# through it.
btn = Image.open(f"{ART}/ui/btn_jump.png").convert("RGBA")
S = btn.width
hole = Image.new("L", (S * 4, S * 4), 255)
ImageDraw.Draw(hole).ellipse(
    [S * 4 * 0.10, S * 4 * 0.10, S * 4 * 0.90, S * 4 * 0.90], fill=0)
hole = hole.resize((S, S), Image.LANCZOS).filter(ImageFilter.GaussianBlur(S * 0.012))
ring = btn.copy()
# Keep the button's own soft outer edge, and clear everything inside the hole.
ring.putalpha(Image.composite(btn.getchannel("A"), Image.new("L", (S, S), 0), hole))
ring.save(f"{ART}/ui/ring_art.png")
print("ring_art", ring.size)

# ------------------------------------------------------------------ walls
# The castle masonry down each edge of the frame, banner and all. Rows are taken
# from the middle of the painting's height, clear of the HUD portrait at the top
# and the control buttons at the bottom, then the strip tiles vertically.
im.crop((22, 240, 132, 700)).save(f"{ART}/wall_left.png")
im.crop((1478, 240, 1560, 700)).save(f"{ART}/wall_right.png")
print("walls written")
