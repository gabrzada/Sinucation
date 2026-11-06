"""
Texturas do FriendSlop geradas offline (numpy), repetíveis, com albedo + mapa de normais.
Mesmo padrão das texturas procedurais do jogo (TextureFactory) para as mesmas escalas de UV, mas em 1024 px e
com muito mais detalhe: desgaste nas arestas, riscos, lascas de tinta, sujidade, fissuras, fibras.
Saída: <nome>_albedo.bytes e <nome>_normal.bytes (PNG) em Resources/Textures/FS.
O normal map usa o empacotamento AG do jogo: R=1, G=Y, B=0, A=X.
Uso: blender -b --factory-startup -P textures.py -- <pasta_saida> [<pasta_preview>]
"""
import math
import os
import struct
import sys
import zlib

import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(__file__), 'out_tex')
PREV = os.path.abspath(argv[1]) if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

rng = np.random.default_rng(1234)


def png_bytes(arr):
    """arr: HxWx3 ou HxWx4 uint8 → PNG."""
    h, w, c = arr.shape
    color_type = 6 if c == 4 else 2
    raw = b''.join(b'\x00' + arr[y].tobytes() for y in range(h - 1, -1, -1))  # Unity: v=0 em baixo

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)

    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, color_type, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')


def grid_noise(size, cells, seed):
    """Ruído de valor periódico (bicúbico suave) em [0,1]."""
    r = np.random.default_rng(seed).random((cells, cells))
    coords = (np.arange(size) + 0.5) / size * cells
    i0 = np.floor(coords).astype(int)
    f = coords - i0
    f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells
    i0 %= cells
    a = r[np.ix_(i0, i0)]
    b = r[np.ix_(i0, i1)]
    c = r[np.ix_(i1, i0)]
    d = r[np.ix_(i1, i1)]
    fx = f[None, :]
    fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(size, cells, octaves, seed, gain=0.5):
    total = np.zeros((size, size))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        total += grid_noise(size, cells * (2 ** o), seed + o * 97) * amp
        norm += amp
        amp *= gain
    return total / norm


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def uv(size):
    v = (np.arange(size) + 0.5) / size
    return np.meshgrid(v, v)  # U (x), V (y)


def lerp(a, b, t):
    t = t[..., None] if np.ndim(t) == 2 else t
    return a * (1 - t) + b * t


def col(*c):
    return np.array(c, dtype=np.float64)


def scratches(size, count, seed, length=(0.02, 0.12), width=1.2):
    """Riscos finos em direcções aleatórias (máscara 0..1), periódicos."""
    r = np.random.default_rng(seed)
    m = np.zeros((size, size))
    for _ in range(count):
        x, y = r.random() * size, r.random() * size
        ang = r.random() * math.pi
        ln = (length[0] + r.random() * (length[1] - length[0])) * size
        steps = int(ln)
        dx, dy = math.cos(ang), math.sin(ang)
        strength = 0.4 + r.random() * 0.6
        for s in range(steps):
            px = int(x + dx * s) % size
            py = int(y + dy * s) % size
            m[py, px] = max(m[py, px], strength)
            if width > 1:
                m[(py + 1) % size, px] = max(m[(py + 1) % size, px], strength * 0.5)
    # Suaviza um pouco.
    m = (m + np.roll(m, 1, 0) * 0.3 + np.roll(m, 1, 1) * 0.3) / 1.6
    return np.clip(m, 0, 1)


def normal_from_height(h, strength):
    dx = (np.roll(h, 1, 1) - np.roll(h, -1, 1)) * strength
    dy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * strength
    # Linha y cresce com v (v=0 em baixo): n = (-dh/du, -dh/dv, 1), igual ao TextureFactory do jogo.
    n = np.stack([dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    out = np.zeros(h.shape + (4,), dtype=np.uint8)
    out[..., 0] = 255
    out[..., 1] = np.clip((n[..., 1] * 0.5 + 0.5) * 255, 0, 255).astype(np.uint8)
    out[..., 2] = 0
    out[..., 3] = np.clip((n[..., 0] * 0.5 + 0.5) * 255, 0, 255).astype(np.uint8)
    return out


def to_srgb8(c):
    c = np.clip(c, 0, 1)
    return (c * 255 + 0.5).astype(np.uint8)


def save(name, albedo, height, strength):
    with open(os.path.join(OUT, name + '_albedo.bytes'), 'wb') as f:
        f.write(png_bytes(to_srgb8(albedo)))
    with open(os.path.join(OUT, name + '_normal.bytes'), 'wb') as f:
        f.write(png_bytes(normal_from_height(height, strength)))
    if PREV:
        os.makedirs(PREV, exist_ok=True)
        with open(os.path.join(PREV, 'tex_' + name + '.png'), 'wb') as f:
            f.write(png_bytes(to_srgb8(albedo)))
    print('texture', name, albedo.shape)


# Paleta (sRGB, igual à ArtPalette).
NAVY = col(0.106, 0.165, 0.259)
NAVY_DARK = col(0.055, 0.078, 0.125)
NAVY_LIGHT = col(0.19, 0.27, 0.39)
MUSTARD = col(0.957, 0.702, 0.192)
MUSTARD_DARK = col(0.74, 0.5, 0.1)
IVORY = col(0.95, 0.9, 0.8)
FELT = col(0.078, 0.62, 0.53)


def concrete(size=1024):
    U, V = uv(size)
    n = fbm(size, 8, 6, 3)
    grain = grid_noise(size, 256, 5)
    spots = smooth(0.58, 0.78, fbm(size, 4, 4, 9))
    cracks_raw = np.abs(fbm(size, 6, 4, 21) - 0.5)
    cracks = smooth(0.012, 0.0, cracks_raw) * smooth(0.55, 0.7, fbm(size, 3, 3, 33))
    su = np.minimum(np.mod(U * 2, 1), 1 - np.mod(U * 2, 1))
    sv = np.minimum(np.mod(V * 2, 1), 1 - np.mod(V * 2, 1))
    edge = np.minimum(su, sv)
    seam = smooth(0.006, 0.0, edge)
    seam_bevel = smooth(0.02, 0.004, edge)
    # Laje: cada quadrado com tom ligeiramente diferente.
    slab = (np.floor(U * 2) + np.floor(V * 2) * 3) % 4 / 4.0
    base = lerp(col(0.155, 0.165, 0.195), col(0.25, 0.25, 0.27), n)
    base = base * (0.94 + slab[..., None] * 0.1)
    base = base * (0.9 + grain[..., None] * 0.18)
    base = lerp(base, col(0.1, 0.11, 0.14), spots * 0.55)
    base = lerp(base, col(0.06, 0.06, 0.07), cracks * 0.8)
    base = lerp(base, col(0.3, 0.29, 0.28), seam_bevel * 0.25)
    base = lerp(base, col(0.05, 0.05, 0.06), seam)
    # Manchas de óleo escuras e salpicos de tinta mostarda gasta.
    oil = smooth(0.7, 0.85, fbm(size, 5, 3, 41))
    base = lerp(base, col(0.07, 0.07, 0.09), oil * 0.6)
    flecks = smooth(0.985, 0.995, grid_noise(size, 180, 55)) * smooth(0.6, 0.7, fbm(size, 4, 2, 57))
    base = lerp(base, MUSTARD * 0.7, flecks * 0.8)
    height = n * 0.6 + grain * 0.15 - seam * 1.2 - seam_bevel * 0.3 - cracks * 0.6
    save('concrete', base, height, 5.0)


def metal_panel(size=1024):
    U, V = uv(size)
    n = fbm(size, 16, 5, 5)
    pu, pv = np.mod(U * 2, 1), np.mod(V * 2, 1)
    edge = np.minimum(np.minimum(pu, 1 - pu), np.minimum(pv, 1 - pv))
    seam = smooth(0.008, 0.0, edge)
    bevel = smooth(0.04, 0.008, edge)
    rivet = np.zeros_like(U)
    for cx in (0.05, 0.95):
        for cy in (0.05, 0.28, 0.5, 0.72, 0.95):
            d = np.sqrt((pu - cx) ** 2 + (pv - cy) ** 2)
            rivet = np.maximum(rivet, smooth(0.02, 0.008, d))
            d = np.sqrt((pu - cy) ** 2 + (pv - cx) ** 2)
            rivet = np.maximum(rivet, smooth(0.02, 0.008, d))
    wear_noise = fbm(size, 12, 5, 11)
    wear = smooth(0.55, 0.72, wear_noise) * np.clip(bevel * 1.4 + rivet, 0, 1)
    wear = np.maximum(wear, smooth(0.74, 0.8, wear_noise) * 0.8)
    scr = scratches(size, 260, 7, length=(0.01, 0.06))
    grime = smooth(0.4, 0.8, fbm(size, 4, 4, 13)) * (0.4 + 0.6 * (1 - pv))  # escorre para baixo
    c = lerp(NAVY * 0.9, NAVY_LIGHT, n * 0.5)
    c = lerp(c, col(0.03, 0.04, 0.06), grime * 0.45)
    c = lerp(c, col(0.5, 0.49, 0.47), wear * 0.75)
    c = lerp(c, col(0.42, 0.45, 0.52), scr * 0.55)
    c = lerp(c, col(0.03, 0.035, 0.05), seam)
    c = lerp(c, IVORY * 0.72, rivet)
    height = n * 0.15 + rivet * 0.9 - seam * 0.6 + bevel * 0.15 - wear * 0.08 - scr * 0.05
    save('metalpanel', c, height, 6.0)


def worn_mustard(size=1024):
    U, V = uv(size)
    n = fbm(size, 12, 5, 17)
    chip_n = fbm(size, 20, 4, 19)
    chips = smooth(0.715, 0.735, chip_n)
    chips_rim = smooth(0.69, 0.715, chip_n) - chips
    scr = scratches(size, 300, 23, length=(0.01, 0.05))
    grime = smooth(0.45, 0.85, fbm(size, 5, 4, 29))
    c = lerp(MUSTARD, MUSTARD_DARK, n * 0.55)
    c = lerp(c, col(0.55, 0.36, 0.07), grime * 0.35)
    c = lerp(c, col(0.98, 0.84, 0.45), np.clip(chips_rim, 0, 1) * 0.5)
    c = lerp(c, NAVY_DARK * 1.2, chips * 0.9)
    c = lerp(c, col(0.6, 0.55, 0.45), scr * 0.5)
    height = n * 0.2 - chips * 0.5 - scr * 0.1
    save('wornmustard', c, height, 4.0)


def felt(size=512):
    U, V = uv(size)
    n = fbm(size, 32, 4, 1)
    fibers = grid_noise(size, 256, 2) * 0.5 + grid_noise(size, 128, 3) * 0.5
    c = lerp(FELT * 0.86, FELT * 1.1, n * 0.6 + fibers * 0.4)
    height = n * 0.3 + fibers * 0.35
    save('felt', c, height, 2.0)


def wood(size=512):
    U, V = uv(size)
    warp = fbm(size, 4, 4, 29)
    grain = np.sin((U * 40 + warp * 6) * math.pi) * 0.5 + 0.5
    fine = grid_noise(size, 200, 31)
    rings = np.sin((U * 11 + warp * 3) * math.pi) * 0.5 + 0.5
    plank = smooth(0.012, 0.0, np.minimum(np.mod(V * 4, 1), 1 - np.mod(V * 4, 1)))
    knot = smooth(0.08, 0.0, np.sqrt((np.mod(U * 2, 1) - 0.3) ** 2 + (np.mod(V * 4, 1) - 0.55) ** 2 * 4))
    c = lerp(col(0.3, 0.16, 0.08), col(0.52, 0.3, 0.16), grain * 0.7 + rings * 0.3)
    c = c * (0.92 + fine[..., None] * 0.14)
    c = lerp(c, col(0.18, 0.09, 0.05), knot * 0.8)
    c = lerp(c, col(0.08, 0.04, 0.02), plank)
    height = grain * 0.25 + fine * 0.1 - plank * 0.8 - knot * 0.2
    save('wood', c, height, 3.0)


def hazard(size=512):
    U, V = uv(size)
    stripe = (np.mod((U + V) * 4, 1) < 0.5).astype(np.float64)
    edge = np.abs(np.mod((U + V) * 4, 0.5) - 0.25)
    n = fbm(size, 16, 4, 13)
    wear = smooth(0.62, 0.76, n)
    scr = scratches(size, 300, 37)
    c = lerp(NAVY_DARK, MUSTARD, stripe)
    c = c * (0.9 + n[..., None] * 0.2)
    c = lerp(c, col(0.45, 0.42, 0.38), wear * 0.6)
    c = lerp(c, col(0.55, 0.52, 0.46), scr * 0.5)
    height = n * 0.3 - wear * 0.2 + smooth(0.24, 0.25, edge) * 0.05
    save('hazard', c, height, 3.0)


concrete()
metal_panel()
worn_mustard()
felt()
wood()
hazard()

def crate(size=512):
    U, V = uv(size)
    warp = fbm(size, 4, 3, 31)
    grain = np.sin((V * 30 + warp * 5) * math.pi) * 0.5 + 0.5
    fine = grid_noise(size, 160, 33)
    border = (np.minimum(np.minimum(U, 1 - U), np.minimum(V, 1 - V)) < 0.1).astype(np.float64)
    diag = (np.abs(U - V) < 0.06).astype(np.float64)
    frame = np.maximum(border, diag)
    gap = smooth(0.008, 0.0, np.minimum(np.mod(V * 4, 1), 1 - np.mod(V * 4, 1))) * (1 - frame)
    nails = np.zeros_like(U)
    for (cx, cy) in ((0.05, 0.05), (0.95, 0.05), (0.05, 0.95), (0.95, 0.95), (0.5, 0.05), (0.5, 0.95)):
        nails = np.maximum(nails, smooth(0.012, 0.006, np.sqrt((U - cx) ** 2 + (V - cy) ** 2)))
    grime = smooth(0.45, 0.85, fbm(size, 5, 3, 35))
    c = lerp(col(0.4, 0.27, 0.15), col(0.58, 0.41, 0.24), grain * 0.7 + fine * 0.3)
    c = lerp(c, c * 0.72, frame)
    c = lerp(c, col(0.2, 0.13, 0.08), grime * 0.3)
    c = lerp(c, col(0.12, 0.08, 0.05), gap)
    c = lerp(c, col(0.55, 0.55, 0.58), nails)
    height = grain * 0.2 + frame * 0.6 - gap * 0.5 + nails * 0.5
    save('crate', c, height, 4.0)


def grate(size=512):
    U, V = uv(size)
    gu, gv = np.mod(U * 16, 1), np.mod(V * 16, 1)
    bar_u = smooth(0.2, 0.14, gu) + smooth(0.8, 0.86, gu)
    bar_v = smooth(0.2, 0.14, gv) + smooth(0.8, 0.86, gv)
    bar = np.clip(np.maximum(bar_u, bar_v), 0, 1)
    n = fbm(size, 8, 4, 23)
    rust = smooth(0.6, 0.8, fbm(size, 6, 4, 41))
    c = lerp(col(0.02, 0.02, 0.03), lerp(col(0.3, 0.32, 0.36), col(0.45, 0.45, 0.47), n), bar)
    c = lerp(c, col(0.4, 0.22, 0.1), rust * bar * 0.6)
    height = bar * 0.9 + n * 0.1
    save('grate', c, height, 6.0)


crate()
grate()
