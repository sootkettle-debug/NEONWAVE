from math import comb
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

# Draw at 8x the largest Windows icon size, then downsample once.
ICON = 256
SCALE = 8
S = ICON * SCALE
OUT = Path("NEONWAVE/app.ico")

def bezier(points, steps=240):
    result = []
    n = len(points) - 1
    for i in range(steps + 1):
        t = i / steps
        x = y = 0.0
        for j, (px, py) in enumerate(points):
            f = comb(n, j) * (1 - t) ** (n - j) * t ** j
            x += px * f
            y += py * f
        result.append((round(x * SCALE), round(y * SCALE)))
    return result

# A bold, uncluttered silhouette stays recognizable at 32–48 px.
base = Image.new("RGBA", (S, S), (5, 8, 24, 255))
tile_mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(tile_mask).rounded_rectangle(
    (8*SCALE, 8*SCALE, 248*SCALE, 248*SCALE),
    radius=48*SCALE, fill=255
)
base.putalpha(tile_mask)

crest = bezier([
    (35, 151), (67, 145), (76, 76), (116, 73),
    (158, 69), (168, 132), (220, 122)
])

# Soft cyan aura, kept inside the tile.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).line(crest, fill=(0, 205, 255, 190),
                          width=34*SCALE, joint="curve")
glow = glow.filter(ImageFilter.GaussianBlur(17*SCALE))
base = Image.alpha_composite(base, glow)

# Build a horizontal neon gradient, then mask it to the thick wave stroke.
grad = Image.new("RGBA", (S, S), (0, 0, 0, 0))
pixels = grad.load()
stops = [(0.0, (0, 238, 255)), (0.52, (104, 112, 255)), (1.0, (255, 48, 190))]
for x in range(S):
    u = x / (S - 1)
    for i in range(len(stops) - 1):
        if stops[i][0] <= u <= stops[i + 1][0]:
            a, ca = stops[i]
            b, cb = stops[i + 1]
            q = (u - a) / (b - a)
            color = tuple(round(ca[c] * (1-q) + cb[c] * q) for c in range(3))
            for y in range(S):
                pixels[x, y] = (*color, 255)
            break

stroke_mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(stroke_mask).line(crest, fill=255, width=24*SCALE, joint="curve")
grad.putalpha(stroke_mask)
base = Image.alpha_composite(base, grad)

# A single broad, clean foam highlight; avoid tiny details that turn to noise.
shine = Image.new("RGBA", (S, S), (0, 0, 0, 0))
shine_path = bezier([
    (42, 143), (73, 135), (84, 87), (117, 84),
    (151, 81), (169, 123), (207, 116)
])
ImageDraw.Draw(shine).line(shine_path, fill=(225, 255, 255, 220),
                           width=3*SCALE, joint="curve")
base = Image.alpha_composite(base, shine)

# Two thick, widely spaced swells remain legible at small icon sizes.
waves = Image.new("RGBA", (S, S), (0, 0, 0, 0))
wd = ImageDraw.Draw(waves)
lower1 = bezier([(52, 174), (91, 198), (153, 195), (204, 166)])
lower2 = bezier([(78, 207), (116, 222), (159, 215), (190, 197)])
wd.line(lower1, fill=(0, 215, 255, 255), width=7*SCALE, joint="curve")
wd.line(lower2, fill=(236, 66, 213, 245), width=6*SCALE, joint="curve")
base = Image.alpha_composite(base, waves)

# One high-quality downsample, then embed every standard Windows size.
icon = base.resize((ICON, ICON), Image.Resampling.LANCZOS)
OUT.parent.mkdir(parents=True, exist_ok=True)
icon.save(OUT, format="ICO", sizes=[
    (16, 16), (24, 24), (32, 32), (48, 48),
    (64, 64), (128, 128), (256, 256)
])
print(f"Created crisp multi-resolution icon: {OUT}")
