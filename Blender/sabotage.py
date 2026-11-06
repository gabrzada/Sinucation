"""
Corpo da máquina de sabotagem (concept 27): perfil de arcade com frisos mostarda nas laterais, cabeceira,
grelhas de ventilação, altifalante, faixa de riscas de perigo, pés de borracha e emblema △○□8 nas laterais.
As partes interactivas (ecrã, botões, ranhuras, carta, lâmpadas, placa) continuam no código do jogo.
Frente = -Y no Blender (= -Z local no Unity). Origem no chão.
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

HALF_W = 0.62
# Perfil lateral (y = profundidade Blender = z Unity, z = altura).
PROFILE = [(0.45, 0.08), (0.45, 1.97), (-0.31, 1.97), (-0.12, 1.1), (-0.2, 1.07), (-0.45, 0.95), (-0.45, 0.08)]


def extrude_profile(points, x0, x1, mat):
    bm = bmesh.new()
    a = [bm.verts.new((x0, y, z)) for (y, z) in points]
    b = [bm.verts.new((x1, y, z)) for (y, z) in points]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    n = len(points)
    for i in range(n):
        bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return fb.from_bmesh(bm, mat, 'prof', smooth=False)


def offset_profile(points, d):
    """Desloca o polígono (convexo-ish) para fora (d>0) pelas bissectrizes."""
    n = len(points)
    # Orientação.
    area = sum(points[i][0] * points[(i + 1) % n][1] - points[(i + 1) % n][0] * points[i][1] for i in range(n))
    sign = 1 if area > 0 else -1
    out = []
    for i in range(n):
        p0, p1, p2 = points[i - 1], points[i], points[(i + 1) % n]
        e0 = (p1[0] - p0[0], p1[1] - p0[1])
        e1 = (p2[0] - p1[0], p2[1] - p1[1])
        l0 = math.hypot(*e0)
        l1 = math.hypot(*e1)
        n0 = (e0[1] / l0 * sign, -e0[0] / l0 * sign)
        n1 = (e1[1] / l1 * sign, -e1[0] / l1 * sign)
        bis = (n0[0] + n1[0], n0[1] + n1[1])
        bl = math.hypot(*bis)
        bis = (bis[0] / bl, bis[1] / bl)
        cos = bis[0] * n0[0] + bis[1] * n0[1]
        k = d / max(cos, 0.3)
        out.append((p1[0] + bis[0] * k, p1[1] + bis[1] * k))
    return out


parts = []
body = extrude_profile(PROFILE, -HALF_W, HALF_W, 'navy')
fb.bevel(body, 0.03, 3, 'ANGLE', 30)
parts.append(body)

# Frisos mostarda a contornar o perfil nas duas laterais.
for side in (-1, 1):
    x = side * (HALF_W + 0.012)
    outer = extrude_profile(offset_profile(PROFILE, 0.035), x - 0.02, x + 0.02, 'railMustard')
    inner = extrude_profile(offset_profile(PROFILE, -0.045), x - 0.05, x + 0.05, 'railMustard')
    m = outer.modifiers.new('cut', 'BOOLEAN')
    m.operation = 'DIFFERENCE'
    m.solver = 'EXACT'
    m.object = inner
    fb.apply_all(outer)
    bpy.data.objects.remove(inner)
    fb.bevel(outer, 0.008, 2, 'ANGLE', 30)
    parts.append(outer)
    # Parafusos no friso.
    for (y, z) in [(0.43, 0.2), (0.43, 1.0), (0.43, 1.8), (-0.43, 0.2), (-0.43, 0.8), (0.0, 1.95)]:
        parts.append(fb.sphere(0.014, (side * (HALF_W + 0.035), y, z), 'steel', scale=(0.5, 1, 1), segments=10, rings=6))
    # Emblema △○□ + bola 8 na lateral.
    sx = side * (HALF_W + 0.004)
    parts.append(fb.cylinder(0.12, 0.012, (sx, 0.12, 1.35), 'dark', rot=(0, 90, 0), verts=32, bevel_w=0.003))
    parts.append(fb.cylinder(0.055, 0.016, (sx + side * 0.004, 0.12, 1.35), 'ivory', rot=(0, 90, 0), verts=24))
    tri = fb.prism([(-0.07, -0.06), (0.07, -0.06), (0.0, 0.06)], 0.016, 'mustard', center=(0, 0, 0))
    fb.apply_all(tri)
    tri.rotation_euler = (0, 0, math.radians(90))
    tri.location = (sx, 0.16, 1.62)
    parts.append(tri)
    parts.append(fb.cylinder(0.055, 0.016, (sx, 0.0, 1.63), 'coral', rot=(0, 90, 0), verts=24))
    parts.append(fb.box((0.016, 0.1, 0.1), (sx, -0.13, 1.63), 'teal', 0.01, 2))
    # Grelha de ventilação lateral em baixo.
    for k in range(6):
        parts.append(fb.box((0.012, 0.36, 0.022), (sx, 0.08, 0.3 + k * 0.05), 'dark', 0.006, 2))

# Cabeceira (placa em cima) com moldura mostarda; a placa marfim e o texto vêm do jogo.
marquee = fb.box((1.36, 0.8, 0.34), (0, 0.02, 2.12), 'railMustard', 0.04, 3)
parts.append(marquee)
parts.append(fb.box((1.2, 0.04, 0.28), (0, -0.37, 2.12), 'navy', 0.01, 2))
for x in (-0.62, 0.62):
    for z in (2.0, 2.24):
        parts.append(fb.sphere(0.016, (x, -0.385, z), 'steel', scale=(1, 0.5, 1), segments=10, rings=6))

# Frente de baixo: altifalante redondo, grelha, riscas de perigo e pés.
parts.append(fb.cylinder(0.1, 0.02, (-0.36, -0.455, 0.36), 'railMustard', rot=(90, 0, 0), verts=32, bevel_w=0.004))
parts.append(fb.cylinder(0.085, 0.024, (-0.36, -0.455, 0.36), 'dark', rot=(90, 0, 0), verts=32))
for k in range(5):
    parts.append(fb.box((0.15 - abs(k - 2) * 0.03, 0.03, 0.01), (-0.36, -0.47, 0.32 + k * 0.02), 'steel', 0.003, 1))
for k in range(5):
    parts.append(fb.box((0.12, 0.02, 0.02), (0.47, -0.46, 0.22 + k * 0.045), 'dark', 0.006, 2))
kick = fb.box((1.24, 0.03, 0.1), (0, -0.455, 0.13), 'dark', 0.008, 2)
parts.append(kick)
x = -0.56
while x < 0.58:
    s = fb.prism([(-0.03, -0.045), (0.02, -0.045), (0.05, 0.045), (0.0, 0.045)], 0.012, 'mustard', center=(x, -0.472, 0.13))
    parts.append(s)
    x += 0.12
for sx in (-1, 1):
    for sy in (-1, 1):
        parts.append(fb.cylinder(0.06, 0.08, (sx * 0.52, sy * 0.36 - 0.03, 0.04), 'rubber', verts=24, bevel_w=0.012))

# Traseira: painel de acesso com grelhas.
parts.append(fb.box((0.9, 0.02, 1.0), (0, 0.46, 1.0), 'suitDark', 0.02, 2))
for k in range(4):
    parts.append(fb.box((0.5, 0.02, 0.025), (0, 0.472, 1.6 + k * 0.05), 'dark', 0.006, 2))
parts.append(fb.cylinder(0.08, 0.03, (0, 0.47, 0.35), 'dark', rot=(90, 0, 0), verts=24, bevel_w=0.005))

obj = fb.join('SabotageBody', parts)
fb.export_bytes(os.path.join(OUT, 'sabotage.bytes'), [obj], box_uv=True)

fb.setup_preview((0.2, 0.2, 0.24))
os.makedirs(PREV, exist_ok=True)
fb.render(os.path.join(PREV, 'sabotage_34.png'), (0, 0, 1.1), 4.2, 215, 12, lens=45, res=(900, 900))
fb.render(os.path.join(PREV, 'sabotage_side.png'), (0, 0, 1.1), 4.2, -90, 5, lens=45, res=(900, 900))
