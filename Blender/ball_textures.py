"""
Texturas das bolas de bilhar (equirectangulares, para a esfera do Unity): cor do jogo standard, faixa das listradas
a ±30° do equador (metade da altura da bola), círculo branco dos números como calota esférica exacta e números
desenhados por projecção ORTOGRÁFICA no plano tangente — vistos de frente, na bola, ficam sem distorção.
Dois números por bola (lados opostos), 6 e 9 sublinhados. Branca com pontos vermelhos (mostram a rotação/efeito).
Supersampling 2× com redução Lanczos (bordos limpos). Fonte: Oswald (SIL OFL, Resources/Fonts).

Uso:  python Blender/ball_textures.py  →  Assets/_Project/Resources/Textures/Balls/ball_0..15.png
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Resources', 'Textures', 'Balls')
FONT = os.path.join(ROOT, 'Assets', '_Project', 'Resources', 'Fonts', 'Oswald.ttf')

W, H = 1024, 512
SS = 2

# Cores (sRGB) do jogo standard de 15 bolas; as listradas repetem 1–7.
COLORS = {
    1: (250, 192, 0), 2: (20, 64, 168), 3: (214, 34, 30), 4: (84, 40, 146), 5: (244, 106, 18),
    6: (14, 124, 66), 7: (122, 22, 30), 8: (20, 20, 22),
}
IVORY = np.array((246, 242, 230), dtype=np.float32)
INK = np.array((16, 16, 18), dtype=np.float32)
RED_DOT = np.array((200, 30, 34), dtype=np.float32)

STRIPE_LAT = math.radians(30.0)
SPOT_RADIUS = math.radians(28.0)
SPOT_CENTERS = (math.pi * 0.5, math.pi * 1.5)  # u = 0.25 e 0.75 (como a textura antiga)


def color_of(n):
    return np.array(COLORS[((n - 1) % 8) + 1] if n > 0 else (246, 242, 230), dtype=np.float32)


def sphere_grid(w, h):
    u = (np.arange(w, dtype=np.float64) + 0.5) / w
    v = 1.0 - (np.arange(h, dtype=np.float64) + 0.5) / h  # linha 0 do PNG = topo (v = 1)
    lon = u * 2.0 * math.pi
    lat = (v - 0.5) * math.pi
    return np.meshgrid(lon, lat)


def smooth_edge(value, edge, width):
    """1 dentro (value < edge), 0 fora, com uma rampa de 'width' (antialiasing)."""
    return np.clip((edge - value) / width + 0.5, 0.0, 1.0)


def number_image(text, size=512):
    img = Image.new('L', (size, size), 0)
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, int(size * 0.72))
    try:
        font.set_variation_by_name('Bold')
    except Exception:
        pass
    box = draw.textbbox((0, 0), text, font=font)
    tw, th = box[2] - box[0], box[3] - box[1]
    x = (size - tw) / 2 - box[0]
    y = (size - th) / 2 - box[1] - size * 0.02
    draw.text((x, y), text, fill=255, font=font)
    if text in ('6', '9'):
        # Sublinhado (distingue o 6 do 9 em qualquer rotação).
        bw = size * 0.3
        by = (size + th) / 2 + size * 0.045
        draw.rectangle([size / 2 - bw / 2, by, size / 2 + bw / 2, by + size * 0.045], fill=255)
    return np.asarray(img, dtype=np.float32) / 255.0


def sample(img, tx, ty):
    """Amostragem bilinear de uma imagem [0,1] nas coordenadas normalizadas (fora = 0)."""
    h, w = img.shape
    x = tx * w - 0.5
    y = ty * h - 0.5
    x0 = np.floor(x).astype(np.int64)
    y0 = np.floor(y).astype(np.int64)
    fx = x - x0
    fy = y - y0
    out = np.zeros_like(tx)
    for dx, dy, wgt in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
        xi = x0 + dx
        yi = y0 + dy
        ok = (xi >= 0) & (xi < w) & (yi >= 0) & (yi < h)
        vals = np.zeros_like(tx)
        vals[ok] = img[yi[ok], xi[ok]]
        out += vals * wgt
    return out


def make(n):
    w, h = W * SS, H * SS
    lon, lat = sphere_grid(w, h)
    px = 2.0 * math.pi / w  # tamanho angular de um píxel no equador
    rgb = np.empty((h, w, 3), dtype=np.float32)
    rgb[:] = IVORY
    if n == 0:
        # Branca: seis pontos vermelhos (direcções de um octaedro) — vê-se a bola a rodar e o efeito.
        dirs = [(0, 0), (math.pi / 2, 0), (math.pi, 0), (3 * math.pi / 2, 0), (0, math.pi / 2), (0, -math.pi / 2)]
        for lon0, lat0 in dirs:
            cosd = np.sin(lat) * math.sin(lat0) + np.cos(lat) * math.cos(lat0) * np.cos(lon - lon0)
            d = np.arccos(np.clip(cosd, -1, 1))
            a = smooth_edge(d, math.radians(4.2), px)[..., None]
            rgb = rgb * (1 - a) + RED_DOT * a
    else:
        base = color_of(n)
        if n >= 9:
            a = smooth_edge(np.abs(lat), STRIPE_LAT, px)[..., None]
            rgb = rgb * (1 - a) + base * a
        else:
            rgb[:] = base
        digits = number_image(str(n))
        half = math.sin(SPOT_RADIUS)
        for lon0 in SPOT_CENTERS:
            cosd = np.cos(lat) * np.cos(lon - lon0)
            d = np.arccos(np.clip(cosd, -1, 1))
            a = smooth_edge(d, SPOT_RADIUS, px)[..., None]
            rgb = rgb * (1 - a) + IVORY * a
            # Plano tangente (projecção ortográfica): X para a direita, Y para cima, visto de fora.
            front = cosd > 0
            X = np.cos(lat) * np.sin(lon - lon0)
            Y = np.sin(lat)
            tx = 0.5 + X / (2.0 * half)
            ty = 0.5 - Y / (2.0 * half)
            ink = np.where(front, sample(digits, tx, ty), 0.0) * np.clip((SPOT_RADIUS - d) / px, 0, 1)
            ink = ink[..., None]
            rgb = rgb * (1 - ink) + INK * ink
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), 'RGB')
    return img.resize((W, H), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    for n in range(16):
        path = os.path.join(OUT, 'ball_%d.png' % n)
        make(n).save(path, optimize=True)
        print('saved', path)
    # Folha de contacto para rever.
    sheet = Image.new('RGB', (W // 2 * 4, H // 2 * 4), (40, 40, 40))
    for n in range(16):
        im = Image.open(os.path.join(OUT, 'ball_%d.png' % n)).resize((W // 2, H // 2))
        sheet.paste(im, ((n % 4) * W // 2, (n // 4) * H // 2))
    sheet.save(os.path.join(HERE, 'preview', 'balls_sheet.png'))


if __name__ == '__main__':
    main()
