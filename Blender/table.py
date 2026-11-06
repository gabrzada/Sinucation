"""
Mesa de bilhar industrial (concepts 30/32): tabelas mostarda gastas com braçadeiras escuras, caçapas recortadas
com aros de aço, corpo azul-petróleo com painéis e parafusos, faixa de riscas de perigo e pés robustos.
Origem no chão, no centro da mesa. O pano (felt) continua a ser gerado no jogo.
Uso: blender -b --factory-startup -P table.py -- <pasta_saida_bytes> <pasta_previews>
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

# Mesmas medidas do jogo (BilliardsPhysicsSettings / TableView).
HL, HW = 1.12, 0.56
TOP = 0.62
CUSH_D, CUSH_H = 0.045, 0.04
RAIL_W, RAIL_H = 0.15, 0.08
CG, SG = 0.075, 0.062
RAIL_TOP = TOP + RAIL_H - 0.02
RAIL_BOTTOM = TOP - 0.03
OUT_X = HL + CUSH_D + RAIL_W
OUT_Y = HW + CUSH_D + RAIL_W
POCKETS = [(-HL - 0.016, HW + 0.016, 0.07), (0.0, HW + 0.034, 0.062), (HL + 0.016, HW + 0.016, 0.07),
           (-HL - 0.016, -HW - 0.016, 0.07), (0.0, -HW - 0.034, 0.062), (HL + 0.016, -HW - 0.016, 0.07)]


def boolean_cut(target, cutter):
    m = target.modifiers.new('cut', 'BOOLEAN')
    m.operation = 'DIFFERENCE'
    m.solver = 'EXACT'
    m.object = cutter
    fb.apply_all(target)
    bpy.data.objects.remove(cutter)
    return target


def pocket_cutters(extra=0.0, z0=0.5, z1=1.0):
    objs = []
    for (x, y, r) in POCKETS:
        objs.append(fb.cylinder(r + extra, z1 - z0, (x, y, (z0 + z1) / 2), 'dark', verts=40))
    for o in objs[1:]:
        fb.apply_all(o)
    return fb.join('cutters', objs)


def rails():
    """Moldura das tabelas numa só peça: rectângulo exterior menos o interior, menos as caçapas."""
    outer = fb.box((OUT_X * 2, OUT_Y * 2, RAIL_TOP - RAIL_BOTTOM), (0, 0, (RAIL_TOP + RAIL_BOTTOM) / 2), 'railMustard')
    fb.bevel(outer, 0.025, 3)
    fb.apply_all(outer)
    inner = fb.box(((HL + CUSH_D) * 2, (HW + CUSH_D) * 2, 0.5), (0, 0, TOP), 'railMustard')
    boolean_cut(outer, inner)
    boolean_cut(outer, pocket_cutters(0.0, RAIL_BOTTOM - 0.1, RAIL_TOP + 0.1))
    outer.name = 'rails'
    return outer


def cushions():
    """Borrachas com nariz inclinado, entre caçapas, na cor do pano."""
    parts = []
    segs = [((-HL + CG, HW), (-SG, HW)), ((SG, HW), (HL - CG, HW)), ((-HL + CG, -HW), (-SG, -HW)), ((SG, -HW), (HL - CG, -HW)),
            ((-HL, -HW + CG), (-HL, HW - CG)), ((HL, -HW + CG), (HL, HW - CG))]
    for (a, b) in segs:
        ax, ay = a
        bx, by = b
        horizontal = abs(bx - ax) > abs(by - ay)
        # Perfil: base contra a tabela, nariz a apontar para o centro.
        inset = 0.016
        if horizontal:
            s = 1 if ay > 0 else -1
            x0, x1 = min(ax, bx), max(ax, bx)
            y_in, y_out = ay, ay + s * CUSH_D
            v = [(x0, y_out), (x0 + inset, y_in), (x1 - inset, y_in), (x1, y_out)]
        else:
            s = 1 if ax > 0 else -1
            y0, y1 = min(ay, by), max(ay, by)
            x_in, x_out = ax, ax + s * CUSH_D
            v = [(x_out, y0), (x_in, y0 + inset), (x_in, y1 - inset), (x_out, y1)]
        # Sólido simples: tampa superior inclinada do nariz (dentro) para a tabela (fora).
        bm = bmesh.new()
        (o0, i0, i1, o1) = v
        bot = [bm.verts.new((o0[0], o0[1], TOP)), bm.verts.new((i0[0], i0[1], TOP)), bm.verts.new((i1[0], i1[1], TOP)), bm.verts.new((o1[0], o1[1], TOP))]
        nose = [bm.verts.new((i0[0], i0[1], TOP + CUSH_H * 0.7)), bm.verts.new((i1[0], i1[1], TOP + CUSH_H * 0.7))]
        top = [bm.verts.new((o0[0], o0[1], TOP + CUSH_H)), bm.verts.new((o1[0], o1[1], TOP + CUSH_H))]
        bm.faces.new((bot[0], bot[3], bot[2], bot[1]))
        bm.faces.new((bot[1], bot[2], nose[1], nose[0]))
        bm.faces.new((nose[0], nose[1], top[1], top[0]))
        bm.faces.new((top[0], top[1], bot[3], bot[0]))
        bm.faces.new((bot[0], bot[1], nose[0], top[0]))
        bm.faces.new((bot[3], top[1], nose[1], bot[2]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        o = fb.from_bmesh(bm, 'cushion', 'cushion', smooth=False)
        fb.bevel(o, 0.004, 2)
        parts.append(o)
    return parts


def rail_details():
    parts = []
    # Braçadeiras escuras a meio das tabelas longas e junto às caçapas (como no concept).
    for sx in (-1, 1):
        for sy in (-1, 1):
            x = sx * HL * 0.5
            parts.append(fb.box((0.16, RAIL_W + 0.03, 0.03), (x, sy * (HW + CUSH_D + RAIL_W / 2), RAIL_TOP + 0.004), 'navy', 0.01, 2))
            for bx in (-0.05, 0.05):
                parts.append(fb.cylinder(0.011, 0.012, (x + bx, sy * (HW + CUSH_D + RAIL_W / 2), RAIL_TOP + 0.022), 'steel', verts=12, bevel_w=0.003))
    for sx in (-1, 1):
        parts.append(fb.box((RAIL_W + 0.03, 0.14, 0.03), (sx * (HL + CUSH_D + RAIL_W / 2), 0, RAIL_TOP + 0.004), 'navy', 0.01, 2))
        for by in (-0.045, 0.045):
            parts.append(fb.cylinder(0.011, 0.012, (sx * (HL + CUSH_D + RAIL_W / 2), by, RAIL_TOP + 0.022), 'steel', verts=12, bevel_w=0.003))
    # Diamantes de mira (marfim) encastrados no topo.
    for i in range(1, 8):
        if i == 4:
            continue
        x = -HL + i * (HL * 2 / 8)
        if abs(abs(x) - HL * 0.5) < 0.09:
            continue
        for sy in (-1, 1):
            d = fb.box((0.024, 0.024, 0.006), (x, sy * (HW + CUSH_D + RAIL_W * 0.42), RAIL_TOP + 0.001), 'ivory', 0.003, 1, rot=(0, 0, 45))
            parts.append(d)
    for i in range(1, 4):
        y = -HW + i * (HW * 2 / 4)
        if abs(y) < 0.1:
            continue
        for sx in (-1, 1):
            parts.append(fb.box((0.024, 0.024, 0.006), (sx * (HL + CUSH_D + RAIL_W * 0.42), y, RAIL_TOP + 0.001), 'ivory', 0.003, 1, rot=(0, 0, 45)))
    # Parafusos ao longo do bordo exterior.
    n = 18
    for i in range(n):
        x = -OUT_X + 0.12 + i * (OUT_X * 2 - 0.24) / (n - 1)
        if any(abs(x - px) < 0.12 for (px, py, r) in POCKETS):
            continue
        for sy in (-1, 1):
            parts.append(fb.sphere(0.008, (x, sy * (OUT_Y - 0.025), RAIL_TOP), 'steel', scale=(1, 1, 0.5), segments=10, rings=5))
    return parts


def pockets():
    parts = []
    for (x, y, r) in POCKETS:
        corner = abs(x) > 0.5
        # Placa escura aparafusada à volta da caçapa.
        plate_r = r + 0.05
        plate = fb.cylinder(plate_r, 0.02, (x, y, RAIL_TOP + 0.002), 'navy', verts=40, bevel_w=0.006)
        fb.apply_all(plate)
        # Só a parte que fica sobre a tabela (fora do pano).
        clip = fb.box(((HL + CUSH_D) * 2 - 0.004, (HW + CUSH_D) * 2 - 0.004, 0.2), (0, 0, RAIL_TOP), 'navy')
        boolean_cut(plate, clip)
        hole = fb.cylinder(r, 0.1, (x, y, RAIL_TOP), 'dark', verts=40)
        boolean_cut(plate, hole)
        parts.append(plate)
        # Aro de aço, forro escuro e fundo.
        rim = fb.torus(r, 0.009, (x, y, RAIL_TOP + 0.004), 'steel', major_seg=40, minor_seg=8)
        fb.apply_all(rim)
        # O aro elevado fica fora do pano, deixando livre a passagem do taco e da mão de apoio.
        cloth_cut = fb.box((HL * 2, HW * 2, 0.35), (0, 0, RAIL_TOP), 'steel')
        boolean_cut(rim, cloth_cut)
        parts.append(rim)
        liner = fb.cylinder(r, 0.24, (x, y, RAIL_TOP - 0.13), 'dark', verts=32)
        inner = fb.cylinder(r - 0.008, 0.26, (x, y, RAIL_TOP - 0.11), 'dark', verts=32)
        boolean_cut(liner, inner)
        # O forro e o fundo escuro também ficam fora do pano: antes subiam 5 cm acima do pano à frente da boca e
        # a bola que entrava (ou parava na boca) aparecia "cortada" por essa parede preta. O corte começa 2 mm
        # abaixo do pano, para a face cortada ficar escondida debaixo dele.
        boolean_cut(liner, fb.box((HL * 2, HW * 2, 0.35), (0, 0, TOP - 0.002 + 0.175), 'dark'))
        parts.append(liner)
        floor = fb.cylinder(r - 0.004, 0.004, (x, y, TOP + 0.003), 'dark', verts=32)
        boolean_cut(floor, fb.box((HL * 2, HW * 2, 0.35), (0, 0, TOP - 0.002 + 0.175), 'dark'))
        parts.append(floor)
        # Parafusos da placa.
        for k in range(3):
            a = math.atan2(y, x) + (k - 1) * 0.9
            parts.append(fb.sphere(0.009, (x + math.cos(a) * (r + 0.03), y + math.sin(a) * (r + 0.03), RAIL_TOP + 0.013), 'steel', scale=(1, 1, 0.5), segments=10, rings=5))
    return parts


def body():
    parts = []
    bw, bd = OUT_X - 0.03, OUT_Y - 0.03
    z0, z1 = 0.13, RAIL_BOTTOM
    zm = (z0 + z1) * 0.5
    ph = (z1 - z0) * 0.62
    apron = fb.box((bw * 2, bd * 2, z1 - z0), (0, 0, (z0 + z1) / 2), 'navy', 0.03, 3)
    parts.append(apron)
    # Painéis/escotilhas rebaixados com puxadores e parafusos.
    for sy in (-1, 1):
        for px in (-0.62, 0.0, 0.62):
            parts.append(fb.box((0.5, 0.012, ph), (px, sy * bd, zm), 'suitDark', 0.012, 2))
            parts.append(fb.box((0.16, 0.02, 0.03), (px, sy * (bd + 0.008), zm + ph * 0.22), 'steel', 0.008, 2))
            for (dx, dz) in ((-0.22, ph * 0.4), (0.22, ph * 0.4), (-0.22, -ph * 0.4), (0.22, -ph * 0.4)):
                parts.append(fb.sphere(0.01, (px + dx, sy * (bd + 0.006), zm + dz), 'steel', scale=(1, 0.5, 1), segments=10, rings=5))
    for sx in (-1, 1):
        parts.append(fb.box((0.012, 0.8, ph), (sx * bw, 0, zm), 'suitDark', 0.012, 2))
        parts.append(fb.box((0.02, 0.26, 0.05), (sx * (bw + 0.006), 0, zm), 'mustard', 0.01, 2))
    # Cantoneiras mostarda a envolver os cantos do corpo.
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(fb.box((0.2, 0.05, z1 - z0 + 0.02), (sx * (bw - 0.07), sy * (bd + 0.01), (z0 + z1) / 2), 'railMustard', 0.012, 2))
            parts.append(fb.box((0.05, 0.2, z1 - z0 + 0.02), (sx * (bw + 0.01), sy * (bd - 0.07), (z0 + z1) / 2), 'railMustard', 0.012, 2))
            for dz in (-0.14, 0.0, 0.14):
                parts.append(fb.sphere(0.011, (sx * (bw - 0.07), sy * (bd + 0.037), (z0 + z1) / 2 + dz), 'steel', scale=(1, 0.5, 1), segments=10, rings=5))
    # Faixa de riscas de perigo na base do corpo.
    band_z = z0 + 0.035
    parts.append(fb.box((bw * 2 + 0.02, bd * 2 + 0.02, 0.07), (0, 0, band_z), 'suitDark', 0.01, 2))
    stripe_w, gap = 0.05, 0.13

    def stripes_along(length, fixed, horizontal, sign):
        x = -length + 0.1
        while x < length - 0.1:
            pts = [(-stripe_w / 2, -0.03), (stripe_w / 2, -0.03), (stripe_w / 2 + 0.05, 0.03), (-stripe_w / 2 + 0.05, 0.03)]
            p = fb.prism(pts, 0.012, 'mustard', center=(0, 0, 0))
            fb.apply_all(p)
            if horizontal:
                p.location = (x, sign * (fixed + 0.012), band_z)
            else:
                p.rotation_euler = (0, 0, math.radians(90))
                p.location = (sign * (fixed + 0.012), x, band_z)
            parts.append(p)
            x += gap

    stripes_along(bw, bd, True, 1)
    stripes_along(bw, bd, True, -1)
    stripes_along(bd, bw, False, 1)
    stripes_along(bd, bw, False, -1)
    # Pés robustos com sapata mostarda e parafusos.
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (bw - 0.2), sy * (bd - 0.16)
            leg = fb.box((0.22, 0.22, z0 + 0.02), (x, y, (z0 + 0.02) * 0.5), 'navy', 0.03, 3)
            parts.append(leg)
            parts.append(fb.box((0.3, 0.3, 0.055), (x, y, 0.0275), 'railMustard', 0.02, 3))
            parts.append(fb.box((0.235, 0.235, 0.025), (x, y, z0 - 0.005), 'mustard', 0.01, 2))
            for (dx, dy) in ((-0.11, -0.11), (0.11, -0.11), (-0.11, 0.11), (0.11, 0.11)):
                parts.append(fb.cylinder(0.014, 0.012, (x + dx, y + dy, 0.06), 'steel', verts=12, bevel_w=0.003))
    return parts


frame_parts = [rails()] + rail_details() + pockets() + body()
frame = fb.join('TableFrame', frame_parts)
cush = fb.join('Cushions', cushions())
fb.export_bytes(os.path.join(OUT, 'table.bytes'), [frame, cush], box_uv=True)

# Pré-visualização com um pano provisório.
felt = fb.box((HL * 2 + 0.02, HW * 2 + 0.02, 0.04), (0, 0, TOP - 0.02), 'felt')
fb.setup_preview((0.2, 0.2, 0.24))
os.makedirs(PREV, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PREV, 'table.blend'))
if '--no-render' in argv:
    sys.exit(0)
fb.render(os.path.join(PREV, 'table_34.png'), (0, 0, 0.5), 4.2, 35, 28, lens=40, res=(1200, 800))
fb.render(os.path.join(PREV, 'table_corner.png'), (1.1, 0.56, 0.75), 1.2, 50, 35, lens=40, res=(1000, 800))
