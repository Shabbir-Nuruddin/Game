"""
Cut the on-screen controls and the HUD portrait straight OUT of the gameplay
reference painting, so the shipped UI is the artwork itself rather than a
hand-coded approximation of it. The buttons in the painting are circular gold
rings; this finds each ring by its gold pixels, squares up the crop on the
ring's true centre, and masks everything outside the circle to transparent.

Writes into Assets/Resources/ui/ as PNGs with alpha.
"""
import os
from PIL import Image, ImageDraw, ImageFilter

SRC = "Assets/Resources/ui/gameplay_ref.jpg"
OUT = "Assets/Resources/ui"

im = Image.open(SRC).convert("RGB")
px = im.load()


def is_gold(p):
    """The ring's metal: warm, mid-bright, and clearly not the near-black bg."""
    r, g, b = p
    return r > 90 and g > 60 and b < r - 25 and (r + g + b) > 200


def ring_bbox(box):
    """Bounding box of the gold ring inside `box`, in image coordinates."""
    x0, y0, x1, y1 = box
    xs, ys = [], []
    for y in range(y0, y1):
        for x in range(x0, x1):
            if is_gold(px[x, y]):
                xs.append(x)
                ys.append(y)
    if not xs:
        raise SystemExit("no ring found in %s" % (box,))
    return min(xs), min(ys), max(xs), max(ys)


def cut_circle(box, name, pad=3):
    bx0, by0, bx1, by1 = ring_bbox(box)
    cx, cy = (bx0 + bx1) / 2.0, (by0 + by1) / 2.0
    r = max(bx1 - bx0, by1 - by0) / 2.0 + pad
    left, top = int(round(cx - r)), int(round(cy - r))
    size = int(round(r * 2))

    crop = im.crop((left, top, left + size, top + size)).convert("RGBA")

    # Circular alpha, feathered by a pixel so the rim doesn't stair-step when
    # the UI scales it down on a phone. Inset a few pixels: the painted rings
    # aren't perfect circles, so a mask cut exactly to the bounding box leaves
    # slivers of castle wall showing inside the corners of the disc.
    inset = int(round(r * 0.045)) * 4
    mask = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(mask).ellipse(
        (inset, inset, size * 4 - 1 - inset, size * 4 - 1 - inset), fill=255)
    mask = mask.resize((size, size), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.6))
    crop.putalpha(mask)

    crop.save(os.path.join(OUT, name + ".png"))
    print(f"{name}: centre=({cx:.0f},{cy:.0f}) r={r:.0f} -> {size}x{size}")


# Generous search boxes; the gold-pixel scan finds the true ring inside each.
# Kept clear of the screen's own corner filigree at the bottom-left, which is
# also gold and would otherwise drag this ring's detected bounds out of round.
cut_circle((105, 752, 292, 925), "btn_left")
cut_circle((285, 735, 480, 945), "btn_right")
cut_circle((1290, 730, 1500, 945), "btn_jump")
cut_circle((1380, 540, 1545, 710), "btn_bat")

# The portrait plate is a rectangle, not a circle — take it as-is.
im.crop((4, 4, 146, 126)).convert("RGBA").save(os.path.join(OUT, "hud_portrait.png"))
print("hud_portrait: 142x122")
