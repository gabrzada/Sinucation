"""
Chapéus e acessórios das personagens. Origem de cada peça no ponto onde o jogo a coloca:
chapéus = centro da base (no topo da cabeça); cachecol/laço = articulação do pescoço; mochila/medalha = coluna.
Materiais: 'hat' (cor do chapéu), 'accent' (cor derivada do jogador), 'ivory', 'brass', 'navy', 'coral', 'teal',
'mustard', 'leather', 'dark'. Frente = +Y (Blender) = +Z (Unity).
"""
import math
import os
import sys

import bmesh
import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import fsblend as fb  # noqa: E402

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if len(argv) > 0 else os.path.join(os.path.dirname(__file__), 'out')
PREV = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(os.path.dirname(__file__), 'preview')

fb.reset()
fb.PREVIEW['hat'] = (0.08, 0.1, 0.16)
fb.PREVIEW['accent'] = (0.55, 0.3, 0.85)


def lathe(profile, mat, verts=36, name='lathe'):
    """Sólido de revolução a partir de (raio, z)."""
    bm = bmesh.new()
    rings = []
    for (r, z) in profile:
        if r < 1e-6:
            v = bm.verts.new((0, 0, z))
            rings.append([v] * verts)
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * j / verts), r * math.sin(2 * math.pi * j / verts), z)) for j in range(verts)])
    for k in range(len(rings) - 1):
        for j in range(verts):
            q = [rings[k][j], rings[k][(j + 1) % verts], rings[k + 1][(j + 1) % verts], rings[k + 1][j]]
            u = []
            for v in q:
                if v not in u:
                    u.append(v)
            if len(u) >= 3:
                bm.faces.new(u)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return fb.from_bmesh(bm, mat, name)


def top_hat():
    p = [lathe([(0.0, 0.0), (0.2, 0.0), (0.25, 0.012), (0.255, 0.024), (0.2, 0.022), (0.16, 0.03), (0.155, 0.2), (0.165, 0.29), (0.17, 0.3), (0.0, 0.305)], 'hat', 40)]
    p.append(lathe([(0.158, 0.035), (0.162, 0.035), (0.159, 0.085), (0.156, 0.085)], 'accent', 40))
    p.append(fb.box((0.05, 0.02, 0.05), (0.0, 0.16, 0.06), 'brass', 0.008, 2))
    return fb.join('TopHat', p)


def party_hat():
    p = [lathe([(0.0, 0.0), (0.145, 0.0), (0.145, 0.012), (0.0, 0.34)], 'accent', 32)]
    # Faixas marfim em anel.
    for (z0, z1) in ((0.06, 0.09), (0.16, 0.19), (0.26, 0.28)):
        r0 = 0.145 * (1 - z0 / 0.34) + 0.004
        r1 = 0.145 * (1 - z1 / 0.34) + 0.004
        p.append(lathe([(r0 - 0.004, z0), (r0, z0), (r1, z1), (r1 - 0.004, z1)], 'ivory', 32))
    p.append(fb.sphere(0.045, (0, 0, 0.35), 'ivory', segments=16, rings=10))
    p.append(fb.torus(0.14, 0.018, (0, 0, 0.01), 'ivory', major_seg=32, minor_seg=8))
    return fb.join('PartyHat', p)


def crown():
    p = [lathe([(0.15, 0.0), (0.175, 0.0), (0.18, 0.07), (0.155, 0.07)], 'brass', 40)]
    for i in range(5):
        a = i * 2 * math.pi / 5 + math.pi / 2
        c, s = math.cos(a), math.sin(a)
        spike = fb.prism([(-0.05, 0.0), (0.05, 0.0), (0.0, 0.1)], 0.02, 'brass', center=(0, 0, 0))
        fb.apply_all(spike)
        spike.rotation_euler = (0, 0, a + math.pi / 2)
        spike.location = (c * 0.165, s * 0.165, 0.065)
        p.append(spike)
        p.append(fb.sphere(0.018, (c * 0.168, s * 0.168, 0.17), 'brass', segments=12, rings=8))
        gem = ('coral', 'teal', 'mustard')[i % 3]
        p.append(fb.sphere(0.02, (c * 0.18, s * 0.18, 0.035), gem, scale=(1, 1, 1.2), segments=12, rings=8))
    return fb.join('Crown', p)


def beanie():
    p = []
    dome = lathe([(0.0, 0.25), (0.08, 0.245), (0.15, 0.22), (0.2, 0.17), (0.235, 0.1), (0.245, 0.04), (0.245, 0.0), (0.2, 0.0)], 'hat', 36)
    p.append(dome)
    # Nervuras de malha.
    for i in range(18):
        a = i * 2 * math.pi / 18
        rib = fb.box((0.01, 0.01, 0.1), (math.cos(a) * 0.236, math.sin(a) * 0.236, 0.07), 'hat', 0.005, 2, rot=(0, 0, math.degrees(a)))
        p.append(rib)
    p.append(fb.torus(0.245, 0.035, (0, 0, 0.02), 'accent', major_seg=40, minor_seg=10, scale=(1, 1, 1.2)))
    p.append(fb.sphere(0.07, (0, 0, 0.27), 'ivory', segments=16, rings=10))
    return fb.join('Beanie', p)


def scarf():
    p = [fb.torus(0.085, 0.035, (0, 0, 0.035), 'accent', major_seg=32, minor_seg=12, scale=(1, 1, 0.9))]
    for i, (x, rz) in enumerate(((0.035, 6), (0.075, -8))):
        tail = fb.box((0.065, 0.025, 0.2), (x, 0.15, -0.07 - i * 0.02), 'accent', 0.012, 3, rot=(8, 0, rz))
        p.append(tail)
        for k in range(4):
            p.append(fb.box((0.01, 0.018, 0.03), (x - 0.024 + k * 0.016, 0.15, -0.18 - i * 0.02), 'ivory', 0.003, 1, rot=(8, 0, rz)))
        p.append(fb.box((0.066, 0.027, 0.012), (x, 0.15, -0.12 - i * 0.02), 'ivory', 0.004, 1, rot=(8, 0, rz)))
    return fb.join('Scarf', p)


def bow():
    p = []
    for side in (-1, 1):
        loop = fb.sphere(0.045, (side * 0.05, 0.14, -0.02), 'accent', scale=(1.1, 0.45, 0.8), segments=20, rings=12)
        loop.rotation_euler = (0, math.radians(side * -15), 0)
        p.append(loop)
        tail = fb.box((0.028, 0.012, 0.06), (side * 0.022, 0.138, -0.07), 'accent', 0.008, 2, rot=(0, side * 18, 0))
        p.append(tail)
    p.append(fb.sphere(0.024, (0, 0.15, -0.02), 'accent', segments=16, rings=10))
    return fb.join('Bow', p)


def backpack():
    p = []
    bag = fb.box((0.28, 0.14, 0.3), (0, -0.19, 0.22), 'navy')
    fb.bevel(bag, 0.05, 4)
    fb.subsurf(bag, 1)
    p.append(bag)
    pocket = fb.box((0.22, 0.06, 0.14), (0, -0.27, 0.16), 'leather', 0.03, 3)
    p.append(pocket)
    p.append(fb.box((0.23, 0.065, 0.05), (0, -0.275, 0.235), 'leather', 0.015, 2))
    p.append(fb.box((0.05, 0.02, 0.035), (0, -0.305, 0.215), 'brass', 0.006, 2))
    for sx in (-1, 1):
        p.append(fb.box((0.04, 0.02, 0.26), (sx * 0.09, -0.12, 0.25), 'leather', 0.008, 2))
        p.append(fb.cylinder(0.012, 0.3, (sx * 0.14, -0.19, 0.22), 'accent', verts=12))
    p.append(fb.torus(0.03, 0.008, (0, -0.19, 0.39), 'leather', rot=(0, 90, 0), major_seg=16, minor_seg=6))
    return fb.join('Backpack', p)


def medal():
    p = []
    for side in (-1, 1):
        rib = fb.box((0.028, 0.008, 0.1), (side * 0.018, 0.128, 0.3), 'accent', 0.004, 2, rot=(0, side * -22, 0))
        p.append(rib)
    p.append(fb.cylinder(0.034, 0.012, (0, 0.134, 0.225), 'brass', rot=(90, 0, 0), verts=32, bevel_w=0.004))
    star = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5
        r = 0.022 if i % 2 == 0 else 0.009
        star.append((math.cos(a) * r, math.sin(a) * r))
    st = fb.prism(star, 0.006, 'ivory', center=(0, 0.142, 0.225))
    p.append(st)
    return fb.join('Medal', p)


objs = [top_hat(), party_hat(), crown(), beanie(), scarf(), bow(), backpack(), medal()]
fb.export_bytes(os.path.join(OUT, 'accessories.bytes'), objs)

fb.setup_preview()
os.makedirs(PREV, exist_ok=True)
for i, o in enumerate(objs):
    o.location = ((i % 4) * 0.6 - 0.9, 0, (i // 4) * -0.7)
fb.render(os.path.join(PREV, 'accessories.png'), (0, 0, -0.2), 3.2, 20, 18, lens=50, res=(1200, 900))
