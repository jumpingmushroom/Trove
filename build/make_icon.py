"""Thunderstore icon: 256x256 PNG. A map marker whose head holds a small hoard — two berries
and an ore nugget: the two things Trove remembers, in one shape. Bold, so it survives 64px."""
from PIL import Image, ImageDraw, ImageFilter
import math

S = 256
SS = 4                     # supersample for clean curves
W = S * SS

WOOD_DARK = (38, 27, 19)
WOOD_MID = (74, 53, 36)
PIN_FILL = (232, 196, 118)     # warm parchment, reads as "map marker"
PIN_EDGE = (12, 9, 6)
BERRY = (214, 48, 62)          # raspberry
BERRY2 = (168, 32, 46)
ORE = (214, 126, 46)           # copper
LEAF = (126, 196, 74)
OUTLINE = (12, 9, 6)

img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# rounded-square ground, slightly lighter towards the middle
r = 44 * SS
d.rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=WOOD_DARK)
glow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse(
    [W * 0.05, W * 0.12, W * 0.95, W * 0.95], fill=(WOOD_MID[0], WOOD_MID[1], WOOD_MID[2], 175))
glow = glow.filter(ImageFilter.GaussianBlur(28 * SS))
mask = Image.new("L", (W, W), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=255)
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (W, W), (0, 0, 0, 0)), mask))
d = ImageDraw.Draw(img)

# --- the marker: a circle head over a tapering point, drawn as one silhouette ---
cx = W * 0.5
cy = W * 0.395
head_r = W * 0.285
tip_y = W * 0.80

def marker_polygon(rad, tip):
    """Circle of radius rad at (cx, cy) joined to a point at (cx, tip) by its two tangents."""
    dy = tip - cy
    if dy <= rad:
        return []
    alpha = math.asin(rad / dy)          # half-angle of the tangent cone
    pts = []
    # Tangent contact points sit at 90deg +/- alpha (PIL angles: 0 right, 90 down).
    # Sweep from the far contact the long way over the top to the near one, then drop to
    # the tip; starting at the near contact instead bites a notch out of one flank.
    start = math.pi / 2 + alpha
    # arc the long way round the top of the head
    steps = 180
    a0 = start
    a1 = start + 2 * math.pi - 2 * alpha
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / steps
        pts.append((cx + rad * math.cos(a), cy + rad * math.sin(a)))
    pts.append((cx, tip))
    return pts

edge = int(W * 0.030)
d.polygon(marker_polygon(head_r + edge, tip_y + edge * 1.6), fill=PIN_EDGE)
d.polygon(marker_polygon(head_r, tip_y), fill=PIN_FILL)

# inner well, so the hoard sits in a recess rather than on a flat disc
well_r = head_r * 0.76
d.ellipse([cx - well_r, cy - well_r, cx + well_r, cy + well_r], fill=(168, 130, 68))
shade = Image.new("RGBA", (W, W), (0, 0, 0, 0))
ImageDraw.Draw(shade).ellipse([cx - well_r, cy - well_r, cx + well_r, cy + well_r],
                              fill=(60, 42, 20, 150))
shade = shade.filter(ImageFilter.GaussianBlur(7 * SS))
img = Image.alpha_composite(img, shade)
d = ImageDraw.Draw(img)
d.ellipse([cx - well_r, cy - well_r, cx + well_r, cy + well_r], outline=(104, 78, 38), width=int(W * 0.013))

# --- the hoard: two berries and a nugget, outlined so they read small ---
def blob(c, rad, col, o=None):
    o = o or int(W * 0.015)
    d.ellipse([c[0] - rad - o, c[1] - rad - o, c[0] + rad + o, c[1] + rad + o], fill=OUTLINE)
    d.ellipse([c[0] - rad, c[1] - rad, c[0] + rad, c[1] + rad], fill=col)
    # highlight
    hr = rad * 0.34
    hc = (c[0] - rad * 0.32, c[1] - rad * 0.36)
    d.ellipse([hc[0] - hr, hc[1] - hr, hc[0] + hr, hc[1] + hr],
              fill=tuple(min(255, int(v + (255 - v) * 0.45)) for v in col))

br = head_r * 0.35
blob((cx - br * 0.95, cy + br * 0.50), br, BERRY)
blob((cx + br * 1.00, cy + br * 0.44), br * 0.92, BERRY2)
# the nugget sits above and between, a faceted lump rather than a sphere
nug = [(cx - br * 0.62, cy - br * 0.34), (cx - br * 0.12, cy - br * 0.95),
       (cx + br * 0.66, cy - br * 0.72), (cx + br * 0.54, cy - br * 0.06),
       (cx - br * 0.06, cy + br * 0.20)]
o = int(W * 0.015)
d.polygon([(p[0], p[1] + o) for p in nug], fill=OUTLINE)
d.polygon([(p[0] - o, p[1]) for p in nug], fill=OUTLINE)
d.polygon([(p[0] + o, p[1]) for p in nug], fill=OUTLINE)
d.polygon(nug, fill=ORE)
d.polygon([nug[0], nug[1], nug[4]], fill=tuple(min(255, int(v + (255 - v) * 0.30)) for v in ORE))

out = img.resize((S, S), Image.LANCZOS)
out.save("thunderstore/icon.png")
print("wrote thunderstore/icon.png", out.size)
