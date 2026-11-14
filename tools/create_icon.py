from pathlib import Path
from PIL import Image, ImageDraw

# Draw large, then downsample for crisp small Windows icon sizes.
ICON = 256
SCALE = 4
S = ICON * SCALE
PURPLE = (50, 16, 78, 255)
BLACK = (0, 0, 0, 255)
OUT = Path("NEONWAVE/app.ico")

im = Image.new("RGBA", (S, S), PURPLE)
draw = ImageDraw.Draw(im)

# Bold crescent crest: a C-shaped moon opening to the right.
# The offset cut-out creates the open side without outlines or decoration.
draw.ellipse((54*SCALE, 28*SCALE, 190*SCALE, 164*SCALE), fill=BLACK)
draw.ellipse((91*SCALE, 8*SCALE, 227*SCALE, 144*SCALE), fill=PURPLE)

# A single broad, smooth wave (~) beneath the crescent.
def bezier(points, steps=320):
    result = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        x = (u**3 * points[0][0] +
             3*u*u*t * points[1][0] +
             3*u*t*t * points[2][0] +
             t**3 * points[3][0])
        y = (u**3 * points[0][1] +
             3*u*u*t * points[1][1] +
             3*u*t*t * points[2][1] +
             t**3 * points[3][1])
        result.append((round(x*SCALE), round(y*SCALE)))
    return result

upper = bezier([(43, 190), (96, 151), (154, 224), (213, 184)])
lower = bezier([(43, 222), (96, 183), (154, 256), (213, 216)])
draw.polygon(upper + lower[::-1], fill=BLACK)

# Export the same artwork at all standard Windows icon resolutions.
OUT.parent.mkdir(parents=True, exist_ok=True)
sizes = [16, 24, 32, 48, 64, 128, 256]
images = {n: im.resize((n, n), Image.Resampling.LANCZOS) for n in sizes}
images[256].save(OUT, format="ICO", sizes=[(n, n) for n in sizes])
print(f"Created DARKWAVE moon-and-wave icon: {OUT}")
