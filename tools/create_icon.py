from math import comb
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
SCALE = 4
S = SIZE * SCALE
OUT = Path("NEONWAVE/app.ico")

def bezier(points, steps=180):
    result = []
    n = len(points) - 1
    for i in range(steps + 1):
        t = i / steps
        x = y = 0.0
        for j, (px, py) in enumerate(points):
            f = comb(n, j) * (1 - t) ** (n - j) * t ** j
            x += px * f
            y += py * f
        result.append((int(x * SCALE), int(y * SCALE)))
    return result

# Dark rounded tile, rendered oversized and downsampled for clean edges.
base = Image.new("RGBA", (S, S), (7, 10, 27, 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((18*SCALE, 18*SCALE, 1006*SCALE, 1006*SCALE), radius=210*SCALE, fill=255)
base.putalpha(mask)

# Subtle blue-violet ambient glow behind the wave.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
crest = bezier([(145, 620), (275, 580), (340, 310), (505, 300), (655, 285), (710, 510), (875, 490)])
gd.line(crest, fill=(0, 210, 255, 185), width=94*SCALE, joint="curve")
glow = glow.filter(ImageFilter.GaussianBlur(58*SCALE))
base = Image.alpha_composite(base, glow)

# Main wave stroke with a cyan -> violet -> magenta gradient.
wave_mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(wave_mask).line(crest, fill=255, width=54*SCALE, joint="curve")
gradient = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gp = gradient.load()
stops = [(0.0, (0, 238, 255)), (0.48, (100, 125, 255)), (1.0, (255, 45, 190))]
for y in range(S):
    t = y / (S - 1)
    for x in range(S):
        # Gradient follows the diagonal movement of the crest.
        u = max(0.0, min(1.0, 0.58 * (x / (S - 1)) + 0.42 * (1 - t)))
        for k in range(len(stops) - 1):
            if stops[k][0] <= u <= stops[k+1][0]:
                a, ca = stops[k]
                b, cb = stops[k+1]
                q = (u-a)/(b-a)
                color = tuple(round(ca[c]*(1-q)+cb[c]*q) for c in range(3))
                gp[x, y] = (*color, 255)
                break
gradient.putalpha(wave_mask)
base = Image.alpha_composite(base, gradient)

# Bright inner highlight gives the crest a crisp, glassy edge.
highlight = bezier([(160, 590), (285, 550), (355, 350), (500, 340), (645, 325), (720, 470), (850, 455)])
hi = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(hi).line(highlight, fill=(225, 255, 255, 230), width=9*SCALE, joint="curve")
base = Image.alpha_composite(base, hi)

# Two clean flowing under-waves.
under = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ud = ImageDraw.Draw(under)
p1 = bezier([(205, 690), (360, 770), (560, 765), (805, 655)])
p2 = bezier([(290, 795), (455, 850), (630, 820), (760, 755)])
ud.line(p1, fill=(0, 218, 255, 245), width=20*SCALE, joint="curve")
ud.line(p2, fill=(225, 55, 210, 220), width=14*SCALE, joint="curve")
base = Image.alpha_composite(base, under)

# Export a real multi-resolution Windows icon.
final = base.resize((256, 256), Image.Resampling.LANCZOS)
OUT.parent.mkdir(parents=True, exist_ok=True)
final.save(OUT, format="ICO", sizes=[(16,16), (24,24), (32,32), (48,48), (64,64), (128,128), (256,256)])
print(f"Created {OUT} with seven resolutions.")
