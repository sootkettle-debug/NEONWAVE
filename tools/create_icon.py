from math import comb
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ICON, SCALE = 256, 10
S = ICON * SCALE
OUT = Path("NEONWAVE/app.ico")

def curve(points, steps=240):
    n=len(points)-1
    result=[]
    for i in range(steps+1):
        t=i/steps
        x=y=0.0
        for j,(px,py) in enumerate(points):
            f=comb(n,j)*(1-t)**(n-j)*t**j
            x+=px*f; y+=py*f
        result.append((round(x*SCALE),round(y*SCALE)))
    return result

def neon_line(layer, points, color, width, glow_color, blur=7):
    mask=Image.new("L",(S,S),0)
    ImageDraw.Draw(mask).line(points,fill=255,width=width*SCALE,joint="curve")
    glow=Image.new("RGBA",(S,S),(*glow_color,0))
    glow.putalpha(mask.filter(ImageFilter.GaussianBlur(blur*SCALE)).point(lambda p:round(p*.8)))
    layer.alpha_composite(glow)
    stroke=Image.new("RGBA",(S,S),(*color,0)); stroke.putalpha(mask)
    layer.alpha_composite(stroke)

im=Image.new("RGBA",(S,S),(5,8,24,255))
mask=Image.new("L",(S,S),0)
ImageDraw.Draw(mask).rounded_rectangle((5*SCALE,5*SCALE,251*SCALE,251*SCALE),radius=43*SCALE,fill=255)
im.putalpha(mask)

# Magenta neon ring
rm=Image.new("L",(S,S),0)
ImageDraw.Draw(rm).ellipse((24*SCALE,24*SCALE,232*SCALE,232*SCALE),outline=255,width=5*SCALE)
rg=Image.new("RGBA",(S,S),(255,35,205,0))
rg.putalpha(rm.filter(ImageFilter.GaussianBlur(11*SCALE)).point(lambda p:round(p*.9)))
im.alpha_composite(rg)
ring=Image.new("RGBA",(S,S),(0,0,0,0))
ImageDraw.Draw(ring).ellipse((24*SCALE,24*SCALE,232*SCALE,232*SCALE),outline=(255,65,218,255),width=5*SCALE)
im.alpha_composite(ring)

# Orange curved trunk and warm highlight
neon_line(im,curve([(128,119),(130,150),(119,181),(111,202)]),(255,137,35),8,(255,92,18))
neon_line(im,curve([(129,132),(129,157),(120,183),(115,194)]),(255,197,92),2,(255,154,45),3)

# Broad green palm fronds
fronds=[
 [(128,119),(100,92),(72,91),(55,105)],
 [(128,119),(105,82),(105,61),(116,48)],
 [(128,119),(132,80),(151,59),(169,57)],
 [(128,119),(159,91),(188,91),(204,105)],
 [(128,119),(158,119),(181,132),(188,149)]
]
for f in fronds:
    neon_line(im,curve(f),(95,255,78),7,(46,255,74))

# Two blue water ripples beneath the palm
neon_line(im,curve([(58,185),(87,169),(112,191),(140,185),(169,170),(198,184)]),(48,205,255),7,(20,165,255),8)
neon_line(im,curve([(78,207),(105,197),(130,211),(155,207),(176,198)]),(38,166,255),4,(24,130,255),5)

OUT.parent.mkdir(parents=True,exist_ok=True)
sizes=[16,24,32,48,64,128,256]
images={n:im.resize((n,n),Image.Resampling.LANCZOS) for n in sizes}
images[256].save(OUT,format="ICO",sizes=[(n,n) for n in sizes])
print(f"Created multi-resolution palm-tree icon: {OUT}")
