from pathlib import Path
from math import hypot, sin, pi
from PIL import Image, ImageDraw

# Render at high resolution, then downsample for crisp Windows icon sizes.
ICON = 256
SCALE = 4
S = ICON * SCALE
WHITE = (255, 255, 255, 255)
BLACK = (0, 0, 0, 255)
TRANSPARENT = (0, 0, 0, 0)
OUT = Path("NEONWAVE/app.ico")

im = Image.new("RGBA", (S, S), TRANSPARENT)
draw = ImageDraw.Draw(im)

# Round white badge with transparent corners.
margin = 10 * SCALE
draw.ellipse((margin, margin, S - margin, S - margin), fill=WHITE)

# One bold, smooth tilde wave. Its thickness eases to zero at both ends
# so the silhouette finishes in clean, sharp tapered points.
p0 = (34.0, 112.0)
p1 = (91.0, 158.0)
p2 = (158.0, 91.0)
p3 = (222.0, 132.0)

def bezier(t):
    u = 1.0 - t
    return (
        u**3 * p0[0] + 3*u*u*t * p1[0] + 3*u*t*t * p2[0] + t**3 * p3[0],
        u**3 * p0[1] + 3*u*u*t * p1[1] + 3*u*t*t * p2[1] + t**3 * p3[1],
    )

upper = []
lower = []
steps = 800
max_half_width = 17.0
for i in range(steps + 1):
    t = i / steps
    x, y = bezier(t)
    before = bezier(max(0.0, t - 0.001))
    after = bezier(min(1.0, t + 0.001))
    dx, dy = after[0] - before[0], after[1] - before[1]
    length = hypot(dx, dy) or 1.0
    nx, ny = -dy / length, dx / length
    half_width = max_half_width * (sin(pi * t) ** 0.72)
    upper.append(((x + nx * half_width) * SCALE,
                  (y + ny * half_width) * SCALE))
    lower.append(((x - nx * half_width) * SCALE,
                  (y - ny * half_width) * SCALE))

draw.polygon(upper + lower[::-1], fill=BLACK)

OUT.parent.mkdir(parents=True, exist_ok=True)
sizes = [16, 24, 32, 48, 64, 128, 256]
images = {n: im.resize((n, n), Image.Resampling.LANCZOS) for n in sizes}
images[256].save(OUT, format="ICO", sizes=[(n, n) for n in sizes])
print(f"Created DARKWAVE round wave icon: {OUT}")
