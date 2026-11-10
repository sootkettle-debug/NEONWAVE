from math import comb
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ICON = 256
SCALE = 12
S = ICON * SCALE
OUT = Path("NEONWAVE/app.ico")

def curve(points, steps=320):
    n = len(points) - 1
    result = []
    for i in range(steps + 1):
        t = i / steps
        x = y = 0.0
        for j, (px, py) in enumerate(points):
            f = comb(n, j) * (1-t)**(n-j) * t**j
            x += px*f
            y += py*f
        result.append((round(x*SCALE), round(y*SCALE)))
    return result

im = Image.new("RGBA", (S, S), (4, 7, 22, 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((5*SCALE, 5*SCALE, 251*SCALE, 251*SCALE),
                                       radius=43*SCALE, fill=255)
im.putalpha(mask)

crest = curve([(27, 151), (66, 151), (75, 77), (119, 72),
               (165, 67), (168, 137), (229, 119)])
stroke = Image.new("L", (S, S), 0)
ImageDraw.Draw(stroke).line(crest, fill=255, width=38*SCALE, joint="curve")

glow_mask = stroke.filter(ImageFilter.GaussianBlur(17*SCALE))
glow = Image.new("RGBA", (S, S), (0, 205, 255, 0))
glow.putalpha(glow_mask.point(lambda p: round(p * 0.72)))
im = Image.alpha_composite(im, glow)

grad = Image.new("RGBA", (S, S), (0, 0, 0, 0))
p = grad.load()
stops = [(0.0, (0, 245, 255)), (0.48, (80, 150, 255)), (1.0, (255, 45, 190))]
for x in range(S):
    u = x / (S-1)
    for k in range(2):
        if stops[k][0] <= u <= stops[k+1][0]:
            a, ca = stops[k]
            b, cb = stops[k+1]
            q = (u-a)/(b-a)
            c = tuple(round(ca[j]*(1-q)+cb[j]*q) for j in range(3))
            for y in range(S):
                p[x, y] = (*c, 255)
            break
grad.putalpha(stroke)
im = Image.alpha_composite(im, grad)

waves = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(waves)
d.line(curve([(43, 183), (91, 207), (157, 202), (213, 177)]),
       fill=(0, 218, 255, 255), width=12*SCALE, joint="curve")
d.line(curve([(77, 218), (119, 235), (171, 225), (201, 207)]),
       fill=(244, 65, 211, 255), width=10*SCALE, joint="curve")
im = Image.alpha_composite(im, waves)

OUT.parent.mkdir(parents=True, exist_ok=True)
sizes = [16, 24, 32, 48, 64, 128, 256]
images = {n: im.resize((n, n), Image.Resampling.LANCZOS) for n in sizes}
images[256].save(OUT, format="ICO", sizes=sizes)
print(f"Created multi-resolution icon: {OUT}")
