"""
Corpo da máquina de sorteio (concept 28): base em degraus com cantoneiras e riscas de perigo, corpo azul com
pilares mostarda aparafusados, canos mostarda por cima, leque de raios atrás da bola 8 e grelhas laterais.
Roda, rolo, lâmpadas, alavanca, fichas e placa continuam no código do jogo.
Frente = -Y no Blender (= -Z local no Unity). Origem no chão.
"""
import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import fsblend as fb  # noqa: E402

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if len(argv) > 0 else os.path.join(os.path.dirname(__file__), 'out')
PREV = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(os.path.dirname(__file__), 'preview')

fb.reset()
p = []

# --- Base em degraus (Unity: centro z -0.1).
p.append(fb.box((2.8, 1.35, 0.36), (0, -0.1, 0.18), 'navy', 0.04, 3))
p.append(fb.box((2.6, 1.2, 0.28), (0, -0.1, 0.48), 'navy', 0.035, 3))
# Friso mostarda do degrau de cima: MOLDURA de 4 barras com o topo 1,5 cm acima do tampo navy.
# Antes era uma placa inteira (2,64 × 1,24) com o topo exactamente à altura do tampo (0,62): as duas faces
# coplanares, de materiais diferentes, disputavam o depth buffer e a base "piscava" (z-fighting) a qualquer
# distância e ângulo. As barras dos topos encaixam entre as laterais (só se tocam nas arestas).
for sy in (-1, 1):
    p.append(fb.box((2.66, 0.07, 0.06), (0, -0.1 + sy * 0.605, 0.605), 'railMustard', 0.015, 2))
for sx in (-1, 1):
    p.append(fb.box((0.07, 1.14, 0.058), (sx * 1.305, -0.1, 0.604), 'railMustard', 0.015, 2))
for sx in (-1, 1):
    for sy in (-1, 1):
        # Cantoneiras mostarda nos cantos da base.
        p.append(fb.box((0.26, 0.26, 0.4), (sx * 1.3, -0.1 + sy * 0.575, 0.2), 'railMustard', 0.03, 3))
        for dz in (0.1, 0.3):
            p.append(fb.sphere(0.018, (sx * 1.3, -0.1 + sy * 0.71, dz), 'steel', scale=(1, 0.5, 1), segments=10, rings=6))
# Faixa de riscas na frente da base.
p.append(fb.box((2.2, 0.03, 0.14), (0, -0.785, 0.2), 'dark', 0.01, 2))
x = -1.04
while x < 1.06:
    p.append(fb.prism([(-0.035, -0.06), (0.025, -0.06), (0.065, 0.06), (0.005, 0.06)], 0.014, 'mustard', center=(x, -0.804, 0.2)))
    x += 0.14
# Grelhas frontais no degrau de cima.
for sx in (-1, 1):
    for k in range(4):
        p.append(fb.box((0.34, 0.02, 0.025), (sx * 0.95, -0.705, 0.42 + k * 0.05), 'dark', 0.006, 2))

# --- Corpo principal.
body = fb.box((2.2, 0.9, 2.8), (0, 0, 2.0), 'navy', 0.05, 3)
p.append(body)
for sx in (-1, 1):
    for sy in (-1, 1):
        pillar = fb.box((0.14, 0.14, 2.86), (sx * 1.1, sy * 0.45, 2.0), 'railMustard', 0.04, 3)
        p.append(pillar)
        for k in range(7):
            p.append(fb.sphere(0.016, (sx * 1.1, sy * 0.45 + (-0.075 if sy < 0 else 0.075), 0.8 + k * 0.4), 'steel', scale=(1, 0.5, 1), segments=10, rings=6))
# Faixas mostarda: topo, e entre rolo e roda.
p.append(fb.box((2.36, 1.04, 0.14), (0, 0, 3.45), 'railMustard', 0.04, 3))
p.append(fb.box((2.1, 0.06, 0.07), (0, -0.46, 2.25), 'railMustard', 0.02, 2))
# Grelhas e emblema nas laterais.
for sx in (-1, 1):
    for k in range(6):
        p.append(fb.box((0.02, 0.5, 0.03), (sx * 1.105, 0.0, 1.0 + k * 0.07), 'dark', 0.008, 2))
    p.append(fb.cylinder(0.2, 0.014, (sx * 1.106, 0.0, 2.4), 'dark', rot=(0, 90, 0), verts=32, bevel_w=0.004))
    p.append(fb.cylinder(0.09, 0.02, (sx * 1.11, 0.0, 2.4), 'ivory', rot=(0, 90, 0), verts=24))

# --- Canos mostarda por cima (sobem de trás e curvam para os lados).
for sx in (-1, 1):
    p.append(fb.cylinder(0.055, 0.5, (sx * 0.95, 0.3, 3.75), 'railMustard', verts=20))
    elbow = fb.torus(0.18, 0.055, (sx * 0.95 - sx * 0.18, 0.3, 4.0), 'railMustard', rot=(90, 0, 0), major_seg=24, minor_seg=12)
    # Só o quarto de curva de cima/fora.
    import bmesh
    fb.apply_all(elbow)
    bm = bmesh.new()
    bm.from_mesh(elbow.data)
    cx = sx * 0.95 - sx * 0.18
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 3.99 or (v.co.x - cx) * sx < -0.01], context='VERTS')
    bm.to_mesh(elbow.data)
    bm.free()
    p.append(elbow)
    p.append(fb.cylinder(0.055, 0.5, (sx * 0.55, 0.3, 4.18), 'railMustard', rot=(0, 90, 0), verts=20))
    for z in (3.6, 3.9):
        p.append(fb.cylinder(0.07, 0.04, (sx * 0.95, 0.3, z), 'steel', verts=20, bevel_w=0.008))
# Leque de raios mostarda atrás da bola 8.
for i in range(9):
    a = math.radians(-60 + i * 15)
    ray = fb.prism([(-0.035, 0.0), (0.035, 0.0), (0.0, 0.42)], 0.03, 'mustard', center=(0, 0, 0))
    fb.apply_all(ray)
    ray.rotation_euler = (0, a, 0)
    ray.location = (0, 0.08, 4.08)
    p.append(ray)

obj = fb.join('RandomizerBody', p)
fb.export_bytes(os.path.join(OUT, 'randomizer.bytes'), [obj], box_uv=True)

fb.setup_preview((0.2, 0.2, 0.24))
os.makedirs(PREV, exist_ok=True)
fb.render(os.path.join(PREV, 'randomizer_34.png'), (0, 0, 2.0), 8.0, 210, 12, lens=45, res=(900, 900))


