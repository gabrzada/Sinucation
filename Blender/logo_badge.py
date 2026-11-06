# Emblema do jogo (1024 px, anti-aliasing por supersampling 4x). Paleta do ArtPalette.
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops
import math, sys

S = 4096          # tela de trabalho (4x)
OUT = 1024
FONT = r'C:\Users\lucas\Desktop\FriendSlop\FriendSlop\Assets\_Project\Resources\Fonts\Anton-Regular.ttf'
out_path = sys.argv[1]

def hx(h, a=255): return ((h >> 16) & 255, (h >> 8) & 255, h & 255, a)
NAVY, NAVY_DARK, NAVY_LIGHT = hx(0x1C2A3A), hx(0x111A25), hx(0x2B3D52)
IVORY, MUSTARD, MUSTARD_DARK = hx(0xEFE5CF), hx(0xF2B233), hx(0xC98A17)
CORAL, TEAL = hx(0xEE5B4B), hx(0x25B3A6)
INK = hx(0x0B1118)

img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
c = S / 2

def circle(d, cx, cy, r, fill, outline=None, width=0):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=fill, outline=outline, width=width)

def radial(size, inner, outer, center=(0.5, 0.5), power=1.0):
    """Gradiente radial RGBA (inner no centro → outer na borda)."""
    g = Image.new('RGBA', (size, size))
    px = g.load()
    cx, cy = center[0] * size, center[1] * size
    maxd = size * 0.72
    for y in range(size):
        for x in range(size):
            t = min(1.0, math.hypot(x - cx, y - cy) / maxd) ** power
            px[x, y] = tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(4))
    return g

def paste_masked(base, layer, mask):
    base.alpha_composite(Image.composite(layer, Image.new('RGBA', layer.size, (0, 0, 0, 0)), mask))

# ---- sombra projectada suave
shadow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(shadow)
circle(sd, c + 30, c + 90, S * 0.47, (0, 0, 0, 150))
shadow = shadow.filter(ImageFilter.GaussianBlur(70))
img.alpha_composite(shadow)

d = ImageDraw.Draw(img)
R = S * 0.465
# ---- anel exterior mostarda (chapa industrial) com contorno escuro
circle(d, c, c, R, INK)
circle(d, c, c, R - 34, MUSTARD_DARK)
ring = Image.new('RGBA', (S, S), (0, 0, 0, 0))
rd = ImageDraw.Draw(ring)
circle(rd, c, c, R - 50, MUSTARD)
# brilho no topo do anel
hl = Image.new('RGBA', (S, S), (0, 0, 0, 0))
hd = ImageDraw.Draw(hl)
hd.ellipse((c - R * 0.9, c - R * 1.02, c + R * 0.9, c + R * 0.2), fill=(255, 240, 200, 70))
hl = hl.filter(ImageFilter.GaussianBlur(40))
ring_mask = ring.split()[3]
ring.alpha_composite(Image.composite(hl, Image.new('RGBA', (S, S), (0, 0, 0, 0)), ring_mask))
img.alpha_composite(ring)
d = ImageDraw.Draw(img)

# ---- rebites no anel
Rr = R - 170
for i in range(16):
    a = i / 16 * math.tau + math.tau / 32
    x, y = c + math.cos(a) * Rr, c + math.sin(a) * Rr
    circle(d, x + 8, y + 12, 44, (0, 0, 0, 100))
    circle(d, x, y, 44, IVORY)
    circle(d, x - 12, y - 13, 16, (255, 255, 255, 210))

# ---- disco interior azul-marinho com gradiente
Ri = R - 330
circle(d, c, c, Ri + 34, INK)
disc_mask = Image.new('L', (S, S), 0)
ImageDraw.Draw(disc_mask).ellipse((c - Ri, c - Ri, c + Ri, c + Ri), fill=255)
grad = radial(256, NAVY_LIGHT, NAVY_DARK, (0.5, 0.32), 1.1).resize((S, S), Image.BICUBIC)
paste_masked(img, grad, disc_mask)
d = ImageDraw.Draw(img)
circle(d, c, c, Ri - 40, None, (255, 255, 255, 28), 10)   # linha fina interior

# ---- formas: triângulo (mostarda), círculo (coral), quadrado (teal), com contorno, sombra e brilho
def shape_layer(draw_fn, color, dark):
    lay = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    ld = ImageDraw.Draw(lay)
    draw_fn(ld, (0, 0, 0, 110), 14, 22)          # sombra
    lay = lay.filter(ImageFilter.GaussianBlur(10))
    ld = ImageDraw.Draw(lay)
    draw_fn(ld, INK, 0, 0, grow=54)             # contorno grosso (autocolante)
    draw_fn(ld, dark, 0, 0)                     # base escura
    draw_fn(ld, color, 0, -14, shrink=10)       # face
    return lay

sy = c - Ri * 0.33
sz = Ri * 0.37
def tri(ld, fill, dx, dy, grow=0, shrink=0):
    cx, cy = c - Ri * 0.56 + dx, sy + dy
    h = sz * 1.1 + grow - shrink
    ld.polygon([(cx, cy - h * 0.62), (cx + h * 0.62, cy + h * 0.46), (cx - h * 0.62, cy + h * 0.46)], fill=fill)
def cir(ld, fill, dx, dy, grow=0, shrink=0):
    circle(ld, c + dx, sy - sz * 0.18 + dy, sz * 0.58 + grow / 2 - shrink / 2, fill)
def sq(ld, fill, dx, dy, grow=0, shrink=0):
    cx, cy = c + Ri * 0.56 + dx, sy + dy
    h = sz * 0.52 + grow / 2 - shrink / 2
    ld.rounded_rectangle((cx - h, cy - h, cx + h, cy + h), radius=h * 0.28, fill=fill)

for fn, col, dk in ((tri, MUSTARD, MUSTARD_DARK), (cir, CORAL, hx(0xB93E31)), (sq, TEAL, hx(0x1A8076))):
    img.alpha_composite(shape_layer(fn, col, dk))

# ---- bola 8 com sombreado e brilho
bx, by, br = c, c + Ri * 0.40, Ri * 0.47
d = ImageDraw.Draw(img)
circle(d, bx + 18, by + 30, br, (0, 0, 0, 120))
img = Image.alpha_composite(img, img.filter(ImageFilter.GaussianBlur(0)))
ball_mask = Image.new('L', (S, S), 0)
ImageDraw.Draw(ball_mask).ellipse((bx - br, by - br, bx + br, by + br), fill=255)
circle(ImageDraw.Draw(img), bx, by, br + 30, INK)
bgrad = radial(256, (70, 76, 88, 255), (6, 8, 12, 255), (0.36, 0.3), 0.9).resize((S, S), Image.BICUBIC)
paste_masked(img, bgrad, ball_mask)
d = ImageDraw.Draw(img)
wr = br * 0.46
circle(d, bx, by - br * 0.02, wr + 10, (0, 0, 0, 90))
circle(d, bx, by - br * 0.02, wr, IVORY)
font = ImageFont.truetype(FONT, int(wr * 1.35))
tb = d.textbbox((0, 0), "8", font=font)
d.text((bx - (tb[0] + tb[2]) / 2, by - br * 0.02 - (tb[1] + tb[3]) / 2), "8", font=font, fill=NAVY_DARK)
spec = Image.new('RGBA', (S, S), (0, 0, 0, 0))
ImageDraw.Draw(spec).ellipse((bx - br * 0.62, by - br * 0.86, bx - br * 0.12, by - br * 0.5), fill=(255, 255, 255, 150))
spec = spec.filter(ImageFilter.GaussianBlur(18))
paste_masked(img, spec, ball_mask)

img.resize((OUT, OUT), Image.LANCZOS).save(out_path)
print('saved', out_path)
