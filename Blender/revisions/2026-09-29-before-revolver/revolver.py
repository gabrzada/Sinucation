"""
Revólver de brinquedo da roleta (character sheet "PULL TRIGGER"): armação azul-petróleo, tambor mostarda de
6 câmaras, cano com nervura, punho castanho com parafusos e bola vermelha na ponta.
Peças com origem nos pivots do RevolverModel do jogo (frente = +Y no Blender = +Z no Unity).
(Cópia de segurança da versão anterior a 2026-09-29.)
"""
import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
import fsblend as fb  # noqa: E402

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if len(argv) > 0 else os.path.join(os.path.dirname(__file__), 'out')
PREV = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(os.path.dirname(__file__), 'preview')

fb.reset()

# Pivots (Unity x,y,z → Blender x,z,y).
CYL = (0.0, 0.075, 0.04)
HAMMER = (0.0, -0.015, 0.055)
TRIGGER = (0.0, 0.045, 0.012)


def frame():
    p = []
    # Armação central.
    body = fb.box((0.038, 0.1, 0.05), (0, 0.03, 0.035), 'navy', 0.012, 3)
    p.append(body)
    p.append(fb.box((0.032, 0.11, 0.014), (0, 0.048, 0.066), 'mustard', 0.006, 2))
    # Janela do tambor (laterais recortadas simuladas com placas).
    # Cano redondo com nervura e mira.
    barrel = fb.cylinder(0.015, 0.14, (0, 0.17, 0.052), 'navy', rot=(90, 0, 0), verts=24, bevel_w=0.003)
    p.append(barrel)
    p.append(fb.box((0.012, 0.13, 0.012), (0, 0.165, 0.069), 'mustard', 0.004, 2))
    p.append(fb.box((0.006, 0.012, 0.016), (0, 0.228, 0.078), 'mustard', 0.002, 1))
    p.append(fb.cylinder(0.019, 0.02, (0, 0.105, 0.052), 'mustard', rot=(90, 0, 0), verts=24, bevel_w=0.004))
    # Haste do extractor por baixo do cano.
    p.append(fb.cylinder(0.007, 0.08, (0, 0.14, 0.03), 'steel', rot=(90, 0, 0), verts=12))
    # Bola vermelha de brinquedo na ponta, com anel.
    p.append(fb.torus(0.016, 0.005, (0, 0.24, 0.052), 'mustard', rot=(90, 0, 0), major_seg=24, minor_seg=8))
    p.append(fb.sphere(0.02, (0, 0.255, 0.052), 'red', segments=24, rings=14))
    # Guarda-mato.
    guard = fb.torus(0.022, 0.0045, (0, 0.045, 0.006), 'mustard', rot=(0, 90, 0), major_seg=28, minor_seg=8)
    p.append(guard)
    # Punho inclinado para trás com placas castanhas, parafusos e tampa.
    grip = fb.box((0.034, 0.05, 0.11), (0, -0.018, -0.03), 'navy', 0.012, 3, rot=(-18, 0, 0))
    p.append(grip)
    for sx in (-1, 1):
        g = fb.box((0.008, 0.042, 0.09), (sx * 0.019, -0.02, -0.03), 'wood', 0.004, 2, rot=(-18, 0, 0))
        p.append(g)
        p.append(fb.cylinder(0.006, 0.004, (sx * 0.024, -0.017, -0.025), 'mustard', rot=(0, 90, 0), verts=12))
        for k in range(3):
            p.append(fb.box((0.0025, 0.03, 0.004), (sx * 0.0232, -0.024 + (k - 1) * 0.004, -0.05 + k * 0.014), 'dark', 0.001, 1, rot=(-18, 0, 0)))
    p.append(fb.box((0.04, 0.058, 0.016), (0, -0.04, -0.09), 'mustard', 0.006, 2, rot=(-18, 0, 0)))
    return fb.join('RevolverFrame', p)


def drum():
    p = []
    body = fb.cylinder(0.031, 0.046, (0, 0, 0), 'mustard', rot=(90, 0, 0), verts=36, bevel_w=0.006)
    p.append(body)
    for i in range(6):
        a = i * math.pi / 3 + math.pi / 6
        # Estrias entre câmaras (sulcos escuros).
        f = fb.box((0.006, 0.03, 0.006), (math.cos(a) * 0.031, 0, math.sin(a) * 0.031), 'navy', 0.002, 1)
        f.rotation_euler = (0, -a, 0)
        p.append(f)
    for i in range(6):
        a = i * math.pi / 3
        p.append(fb.cylinder(0.0085, 0.006, (math.cos(a) * 0.019, 0.022, math.sin(a) * 0.019), 'dark', rot=(90, 0, 0), verts=16))
    p.append(fb.cylinder(0.008, 0.052, (0, 0, 0), 'steel', rot=(90, 0, 0), verts=16))
    return fb.join('RevolverDrum', p)


def hammer():
    p = [fb.box((0.012, 0.016, 0.03), (0, -0.006, 0.012), 'steel', 0.004, 2, rot=(-20, 0, 0)),
         fb.box((0.018, 0.012, 0.006), (0, -0.014, 0.027), 'steel', 0.002, 1)]
    return fb.join('RevolverHammer', p)


def trigger():
    t = fb.box((0.007, 0.01, 0.026), (0, -0.004, -0.012), 'steel', 0.003, 2, rot=(15, 0, 0))
    return fb.join('RevolverTrigger', [t])


parts = [frame()]
d = drum()
parts.append(d)
h = hammer()
parts.append(h)
t = trigger()
parts.append(t)
fb.export_bytes(os.path.join(OUT, 'revolver.bytes'), parts)

d.location = CYL
h.location = HAMMER
t.location = TRIGGER
fb.setup_preview()
os.makedirs(PREV, exist_ok=True)
fb.render(os.path.join(PREV, 'revolver_side.png'), (0, 0.08, 0.02), 0.55, 90, 5, lens=50)
fb.render(os.path.join(PREV, 'revolver_34.png'), (0, 0.08, 0.02), 0.55, 35, 20, lens=50)
