"""
Revólver da roleta, remodelado a partir das referências (Taurus 66 / Rossi RP63 inox, S&W de 4"): armação e cano em
inox escovado, cano com nervura ventilada e "underlug" inteiro com o extractor, mira de rampa com inserto vermelho,
mira traseira regulável, tambor canelado de 6 câmaras, cão e gatilho de aço, guarda-mato arredondado e punho de
borracha preta com gomos para os dedos e medalhão dourado. Proporções levemente "chunky" (bordos arredondados,
peças um pouco mais grossas) para ler bem ao lado das personagens de vinil — sem perder o desenho de um revólver real.

Peças com origem nos pivots do RevolverModel do jogo (frente = +Y no Blender = +Z no Unity; cima = +Z Blender = +Y Unity):
  RevolverFrame  origem = centro do punho, onde a mão agarra
  RevolverDrum   origem = eixo do tambor (CYL)
  RevolverHammer origem = eixo do cão (HAMMER)
  RevolverTrigger origem = eixo do gatilho (TRIGGER)
Boca do cano (Muzzle) em Unity (0, 0.049, 0.245) — tem de bater com AvatarVisual.GunMuzzleLocal.
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
fb.PREVIEW['steel'] = (0.74, 0.76, 0.79)
fb.PREVIEW['steelDark'] = (0.36, 0.38, 0.41)
fb.PREVIEW['rubber'] = (0.05, 0.05, 0.055)
fb.PREVIEW['brass'] = (0.86, 0.66, 0.2)
fb.PREVIEW['red'] = (0.9, 0.12, 0.1)

# Pivots em coordenadas do Blender (x, y=frente, z=cima).
BORE_Z = 0.049              # altura do eixo do cano (= câmara de cima do tambor)
CYL = (0.0, 0.047, 0.035)   # eixo do tambor: 1,4 cm abaixo do cano (a câmara de cima alinha com o cano)
HAMMER = (0.0, -0.008, 0.05)
TRIGGER = (0.0, 0.03, 0.016)
MUZZLE_Y = 0.245


def frame():
    p = []
    # Armação em "janela": bloco de trás, ponte por cima do tambor, travessa por baixo e frente à volta do cano
    # (o tambor fica à vista e encaixa entre o escudo de recuo e a frente, como nas referências).
    p.append(fb.box((0.028, 0.065, 0.058), (0, 0.014, 0.029), 'steel', 0.006, 3))          # bloco de trás
    p.append(fb.box((0.031, 0.006, 0.05), (0, 0.0445, 0.035), 'steel', 0.002, 1))          # escudo de recuo
    p.append(fb.box((0.024, 0.078, 0.012), (0, 0.075, 0.062), 'steel', 0.004, 2))          # ponte (top strap)
    p.append(fb.box((0.026, 0.07, 0.014), (0, 0.075, 0.007), 'steel', 0.004, 2))           # travessa de baixo
    p.append(fb.box((0.026, 0.026, 0.06), (0, 0.103, 0.036), 'steel', 0.005, 2))           # frente (cone de forçamento)
    # Mira traseira regulável (bloco escuro com entalhe) sobre a ponte.
    p.append(fb.box((0.013, 0.016, 0.007), (0, 0.047, 0.0715), 'steelDark', 0.002, 1))
    p.append(fb.box((0.004, 0.017, 0.004), (0, 0.047, 0.0755), 'rubber', 0.0008, 1))
    # Cano redondo, um pouco mais grosso junto à armação.
    p.append(fb.cylinder(0.0105, 0.132, (0, 0.179, BORE_Z), 'steel', rot=(90, 0, 0), verts=28, bevel_w=0.002, radius2=0.0095))
    # Underlug inteiro por baixo do cano, com a ponta do extractor.
    p.append(fb.box((0.017, 0.13, 0.02), (0, 0.179, BORE_Z - 0.016), 'steel', 0.006, 3))
    p.append(fb.cylinder(0.0045, 0.006, (0, 0.246, BORE_Z - 0.017), 'steelDark', rot=(90, 0, 0), verts=12))
    # Nervura ventilada: quatro pontes com janelas entre elas + tira de topo.
    p.append(fb.box((0.009, 0.126, 0.0035), (0, 0.18, BORE_Z + 0.0162), 'steel', 0.001, 1))
    for k in range(4):
        p.append(fb.box((0.008, 0.012, 0.007), (0, 0.124 + k * 0.037, BORE_Z + 0.011), 'steel', 0.0015, 1))
    # Mira da frente: rampa com inserto vermelho (como o Taurus da referência).
    p.append(fb.box((0.006, 0.02, 0.012), (0, 0.23, BORE_Z + 0.02), 'steel', 0.002, 1, rot=(-18, 0, 0)))
    p.append(fb.box((0.0062, 0.006, 0.004), (0, 0.235, BORE_Z + 0.027), 'red', 0.001, 1))
    # Boca do cano: coroa escura.
    p.append(fb.cylinder(0.0055, 0.004, (0, MUZZLE_Y - 0.001, BORE_Z), 'rubber', rot=(90, 0, 0), verts=16))
    # Guarda-mato arredondado (só a metade de baixo fica fora da armação).
    guard = fb.torus(0.02, 0.0035, (0, 0.034, 0.002), 'steel', rot=(0, 90, 0), major_seg=32, minor_seg=8)
    p.append(guard)
    # Parafusos da placa lateral.
    for sx in (-1, 1):
        for (y, z) in ((0.03, 0.03), (0.004, 0.016)):
            p.append(fb.cylinder(0.0028, 0.002, (sx * 0.0145, y, z), 'steelDark', rot=(0, 90, 0), verts=10))
        # Trinco do tambor (lado esquerdo) — um só.
        if sx < 0:
            p.append(fb.box((0.004, 0.014, 0.008), (-0.0155, 0.012, 0.042), 'steelDark', 0.0015, 1))
    # Punho de borracha: corpo inclinado para trás, gomos para os dedos à frente e medalhão dourado.
    grip = fb.box((0.032, 0.046, 0.1), (0, -0.018, -0.03), 'rubber', 0.014, 3, rot=(-16, 0, 0))
    p.append(grip)
    for k in range(3):
        p.append(fb.sphere(0.0095, (0, 0.004 - k * 0.006, -0.005 - k * 0.026), 'rubber', scale=(1.55, 0.9, 1.0), segments=16, rings=10))
    p.append(fb.box((0.034, 0.05, 0.014), (0, -0.034, -0.08), 'rubber', 0.006, 2, rot=(-16, 0, 0)))  # base do punho
    for sx in (-1, 1):
        p.append(fb.cylinder(0.0055, 0.002, (sx * 0.0166, -0.025, -0.052), 'brass', rot=(0, 90, 0), verts=16))
        # Textura "pontilhada" da borracha: pequenos relevos.
        for i in range(3):
            for j in range(2):
                p.append(fb.sphere(0.0022, (sx * 0.0158, -0.012 - j * 0.013 - i * 0.004, -0.018 - i * 0.02), 'rubber', segments=8, rings=5))
    # Espinha do punho (inox) a ligar a armação ao punho, por trás.
    p.append(fb.box((0.016, 0.012, 0.04), (0, -0.02, 0.012), 'steel', 0.004, 2, rot=(-16, 0, 0)))
    return fb.join('RevolverFrame', p)


def drum():
    p = []
    # Tambor inox com 6 caneluras entre as câmaras e bordo chanfrado.
    p.append(fb.cylinder(0.0205, 0.044, (0, 0.022, 0), 'steel', rot=(90, 0, 0), verts=36, bevel_w=0.004))
    for i in range(6):
        a = i * math.pi / 3 + math.pi / 6
        f = fb.box((0.0055, 0.03, 0.0055), (math.cos(a) * 0.0205, 0.02, math.sin(a) * 0.0205), 'steelDark', 0.0022, 2)
        f.rotation_euler = (0, -a, 0)
        p.append(f)
    for i in range(6):
        a = i * math.pi / 3 + math.pi / 2  # uma câmara em cima (alinhada com o cano)
        p.append(fb.cylinder(0.0048, 0.004, (math.cos(a) * 0.014, 0.0445, math.sin(a) * 0.014), 'rubber', rot=(90, 0, 0), verts=14))
    p.append(fb.cylinder(0.0055, 0.05, (0, 0.022, 0), 'steelDark', rot=(90, 0, 0), verts=16))
    return fb.join('RevolverDrum', p)


def hammer():
    # Cão encaixado no bloco de trás; só a espora serrilhada sai por cima (é o que roda quando arma).
    p = [fb.box((0.008, 0.012, 0.024), (0, -0.002, 0.006), 'steel', 0.003, 2, rot=(-20, 0, 0)),
         fb.box((0.013, 0.012, 0.005), (0, -0.009, 0.018), 'steelDark', 0.0015, 1, rot=(-20, 0, 0))]
    return fb.join('RevolverHammer', p)


def trigger():
    t = fb.box((0.0065, 0.009, 0.024), (0, -0.002, -0.011), 'steel', 0.0028, 2, rot=(12, 0, 0))
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
fb.render(os.path.join(PREV, 'revolver_side.png'), (0, 0.08, 0.02), 0.5, 90, 5, lens=50)
fb.render(os.path.join(PREV, 'revolver_34.png'), (0, 0.08, 0.02), 0.5, 35, 20, lens=50)
