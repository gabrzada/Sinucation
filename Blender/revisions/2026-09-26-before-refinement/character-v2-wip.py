"""
Corpo das personagens v2 — "bonequinho de vinil" (inspirado no character sheet, com proporções próprias):
cabeça geométrica grande (vem do jogo), tronco cheio em forma de pera, braços e pernas roliços e mais compridos
(para jogar bilhar a sério), luvas grandes com TRÊS POSES modeladas:
  - GloveX_Open   : mão relaxada (luva de desenho animado);
  - GloveR_Fist   : punho fechado com um canal cilíndrico por onde passa o taco / o cabo do revólver;
  - GloveL_Bridge : mão espalmada na mesa com o polegar levantado a formar o "V" onde o taco assenta.
Cada peça tem a origem na articulação do rig do jogo (ver AvatarVisual). Medidas do rig v2 (Unity, y para cima):
  ancas 0.56 · coxa 0.21 · canela 0.195 · tornozelo→sola 0.105 · coluna→ombro (±0.21, 0.37) · pescoço 0.44
  braço 0.18 · antebraço 0.17 · pulso→centro do punho 0.085.
Frame canónico da mão (Unity): origem no pulso, dedos para −Y, polegar para +Z;
  mão direita: palma para −X; mão esquerda: palma para +X.
Uso: blender -b --factory-startup -P character.py -- <pasta_saida_bytes> <pasta_previews>
"""
import math
import os
import sys

import bmesh
import bpy
import mathutils

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import fsblend as fb  # noqa: E402

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if len(argv) > 0 else os.path.join(os.path.dirname(__file__), 'out')
PREV = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(os.path.dirname(__file__), 'preview')

fb.reset()

# Medidas do rig (Blender: x direita, y frente, z cima).
HIPS = 0.56
SPINE_UP = 0.04
TORSO_H = 0.44
SHOULDER = (0.21, 0.37)
THIGH = 0.21
SHIN = 0.195
UPPER_ARM = 0.18
FOREARM = 0.17
GRIP = 0.08  # pulso → centro do punho (canal do taco)
PIVOT_X = 0.085


def soft(obj, bevel_w, segs=4, sub=1):
    fb.bevel(obj, bevel_w, segs)
    if sub:
        fb.subsurf(obj, sub)
    return obj


def shape_verts(obj, fn):
    for v in obj.data.vertices:
        v.co = fn(v.co.copy())



K = 1.0 / 0.57  # raio nominal da metaball → raio real da superfície (calibrado)


def B(co, r, st=2.0):
    return dict(type='BALL', co=co, radius=r * K, stiffness=st)


def E(co, half, rot=(0, 0, 0), st=2.0):
    return dict(type='ELLIPSOID', co=co, radius=0.1, size=tuple(h * K / 0.1 for h in half), rot=rot, stiffness=st)


def C(co, half_len, r, rot=(0, 0, 0), st=2.0):
    """Cápsula ao longo do eixo x local (rodada por rot), com meio-comprimento half_len e raio r."""
    return dict(type='CAPSULE', co=co, radius=r * K, size=(half_len, 1, 1), rot=rot, stiffness=st)


def shrinkwrap_onto(obj, target, offset):
    m = obj.modifiers.new('wrap', 'SHRINKWRAP')
    m.target = target
    m.wrap_method = 'NEAREST_SURFACEPOINT'
    m.offset = offset
    return obj


def cut_channel(obj, co, radius, rot):
    fb.apply_all(obj)
    hole = fb.cylinder(radius, 0.4, co, 'ivory', rot=rot, verts=28)
    m = obj.modifiers.new('cut', 'BOOLEAN')
    m.operation = 'DIFFERENCE'
    m.solver = 'EXACT'
    m.object = hole
    fb.apply_all(obj)
    bpy.data.objects.remove(hole)
    return obj


# ------------------------------------------------------------------------------------------ tronco

def torso():
    p = []
    body = fb.meta('TorsoBody', [
        E((0, 0.012, 0.15), (0.172, 0.132, 0.15)),   # barriga
        E((0, 0.0, 0.3), (0.166, 0.118, 0.13)),      # peito
        B((0.13, 0.0, 0.37), 0.06),                  # ombros arredondados
        B((-0.13, 0.0, 0.37), 0.06),
    ], 'suit', 0.006)
    p.append(body)

    def on_body(o, off=0.006):
        shrinkwrap_onto(o, body, off)
        return o

    # Camisola marfim no decote.
    p.append(on_body(fb.box((0.13, 0.03, 0.07), (0, 0.11, TORSO_H - 0.05), 'ivory', 0.012, 3), 0.004))
    # Suspensórios (assentes no corpo), molas douradas.
    for side in (-1, 1):
        x = side * 0.085
        strap = fb.box((0.05, 0.012, 0.36), (x, 0.13, 0.245), 'ivory', 0.005, 2)
        fb.subsurf(strap, 1)
        p.append(on_body(strap, 0.006))
        top = fb.box((0.05, 0.27, 0.012), (x, 0.0, TORSO_H - 0.01), 'ivory', 0.005, 2)
        fb.subsurf(top, 1)
        p.append(on_body(top, 0.006))
        back = fb.box((0.05, 0.012, 0.4), (x * 0.5, -0.13, 0.24), 'ivory', 0.005, 2, rot=(0, side * 17, 0))
        fb.subsurf(back, 1)
        p.append(on_body(back, 0.006))
        p.append(fb.box((0.058, 0.016, 0.03), (x, 0.138, 0.35), 'brass', 0.007, 2))
        p.append(fb.cylinder(0.009, 0.012, (x, 0.147, 0.35), 'dark', rot=(90, 0, 0), verts=12))
    # Cinto largo com fivela e passadores.
    p.append(fb.torus(0.0, 0.0, (0, 0, 0), 'leather') if False else fb.box((0.36, 0.28, 0.062), (0, 0.006, 0.04), 'leather', 0.03, 3))
    p.append(fb.box((0.085, 0.02, 0.066), (0, 0.15, 0.04), 'brass', 0.012, 3))
    p.append(fb.box((0.048, 0.014, 0.034), (0, 0.158, 0.04), 'leather', 0.006, 2))
    for x in (-0.125, 0.125):
        p.append(fb.box((0.02, 0.014, 0.072), (x, 0.137, 0.04), 'leather', 0.004, 2))
    # Bolsa à direita e rádio à esquerda (nas ancas, de lado).
    p.append(fb.box((0.058, 0.085, 0.09), (0.19, -0.01, -0.005), 'leather', 0.02, 3))
    p.append(fb.box((0.062, 0.089, 0.032), (0.19, -0.01, 0.034), 'leather', 0.012, 2))
    p.append(fb.sphere(0.009, (0.222, -0.01, 0.026), 'brass', segments=12, rings=8))
    p.append(fb.box((0.05, 0.055, 0.075), (-0.19, -0.005, 0.0), 'suitDark', 0.016, 3))
    p.append(fb.box((0.014, 0.024, 0.03), (-0.216, -0.005, 0.02), 'brass', 0.004, 2))
    p.append(fb.cylinder(0.004, 0.06, (-0.2, 0.012, 0.06), 'dark', verts=8))
    # Emblema △○□ no peito.
    ez = 0.27
    p.append(fb.cylinder(0.054, 0.016, (0, 0.128, ez), 'brass', rot=(90, 0, 0), verts=40, bevel_w=0.005))
    p.append(fb.cylinder(0.044, 0.014, (0, 0.133, ez), 'dark', rot=(90, 0, 0), verts=40))
    p.append(fb.prism([(-0.015, -0.011), (0.015, -0.011), (0.0, 0.014)], 0.009, 'mustard', center=(-0.02, 0.142, ez + 0.004)))
    p.append(fb.cylinder(0.011, 0.009, (0.0, 0.142, ez + 0.016), 'coral', rot=(90, 0, 0), verts=20))
    p.append(fb.box((0.021, 0.009, 0.021), (0.021, 0.142, ez - 0.008), 'teal', 0.004, 2))
    # Remendo mostarda nas costas.
    patch = fb.box((0.14, 0.012, 0.06), (0, -0.125, 0.34), 'mustard', 0.01, 2)
    fb.subsurf(patch, 1)
    p.append(on_body(patch, 0.004))
    return fb.join('Torso', p)


def pelvis():
    body = fb.meta('PelvisBody', [E((0, 0, -0.045), (0.168, 0.128, 0.09))], 'suit', 0.006)
    seam = fb.box((0.012, 0.26, 0.12), (0, 0, -0.06), 'suitDark', 0.005, 2)
    shrinkwrap_onto(seam, body, 0.002)
    return fb.join('Pelvis', [body, seam])


def collar():
    ring = fb.cylinder(0.082, 0.05, (0, 0, 0.03), 'ivory', verts=36, bevel_w=0.02)
    return fb.join('Collar', [ring])


# ------------------------------------------------------------------------------------------ pernas e botas

def thigh():
    leg = fb.capsule(0.074, 0.066, THIGH - 0.02, (0, 0, 0), 'suit')
    return fb.join('Thigh', [leg])


def shin():
    leg = fb.capsule(0.066, 0.058, SHIN - 0.045, (0, 0, 0), 'suit')
    # Joelheira marfim almofadada (character sheet).
    pad = fb.meta('KneePad', [E((0, 0.052, -0.02), (0.052, 0.026, 0.05))], 'ivory', 0.004)
    hem = fb.cylinder(0.07, 0.03, (0, 0, -(SHIN - 0.045)), 'suitDark', verts=32, bevel_w=0.012)
    return fb.join('Shin', [leg, pad, hem])


def boot():
    p = []

    def slab(z, height, mat, grow=0.0):
        o = fb.box((0.17 + grow, 0.31 + grow, height), (0, 0.04, z), mat)
        for v in o.data.vertices:
            if v.co.y > 0.14:
                v.co.x *= 0.85
        fb.bevel(o, min(0.032, height * 0.45), 4)
        return o

    p.append(slab(-0.087, 0.036, 'tint', 0.006))
    p.append(slab(-0.062, 0.017, 'ivory'))
    for i in range(4):
        p.append(fb.box((0.145, 0.013, 0.008), (0, -0.07 + i * 0.075, -0.1), 'dark', 0.003, 1))
    upper = fb.box((0.155, 0.27, 0.115), (0, 0.035, -0.005), 'suitDark')
    soft(upper, 0.05, 4, 1)
    p.append(upper)
    toe = fb.box((0.148, 0.095, 0.072), (0, 0.13, -0.028), 'ivory')
    soft(toe, 0.034, 4, 1)
    p.append(toe)
    p.append(fb.box((0.068, 0.09, 0.022), (0, 0.06, 0.045), 'ivory', 0.009, 2, rot=(-20, 0, 0)))
    for i in range(3):
        p.append(fb.box((0.08, 0.012, 0.012), (0, 0.032 + i * 0.03, 0.053 - i * 0.01), 'ivory', 0.004, 2, rot=(-20, 0, 0)))
    p.append(fb.torus(0.065, 0.022, (0, 0, 0.05), 'ivory', major_seg=36, minor_seg=12))
    p.append(fb.cylinder(0.071, 0.055, (0, 0, 0.034), 'ivory', verts=36, bevel_w=0.013))
    p.append(fb.box((0.034, 0.022, 0.055), (0, -0.098, 0.022), 'tint', 0.009, 2))
    for side in (-1, 1):
        p.append(fb.cylinder(0.012, 0.006, (side * 0.079, 0.0, 0.0), 'brass', rot=(0, 90, 0), verts=16))
    return fb.join('Boot', p)


# ------------------------------------------------------------------------------------------ braços

def upper_arm():
    ball = fb.sphere(0.074, (0, 0, -0.006), 'suit', segments=28, rings=16)
    arm = fb.capsule(0.061, 0.055, UPPER_ARM - 0.03, (0, 0, -0.012), 'suit')
    return fb.join('UpperArm', [ball, arm])


def forearm():
    arm = fb.capsule(0.056, 0.05, FOREARM - 0.05, (0, 0, 0), 'suit')
    roll = fb.torus(0.056, 0.015, (0, 0, -0.012), 'suitDark', major_seg=28, minor_seg=10)
    return fb.join('Forearm', [arm, roll])


# ------------------------------------------------------------------------------------------ luvas (mão direita; a esquerda é espelhada)

def cuff(p):
    p.append(fb.cylinder(0.05, 0.03, (0, 0, -0.01), 'ivory', verts=32, bevel_w=0.01, radius2=0.058))
    p.append(fb.torus(0.056, 0.012, (0, 0, -0.026), 'ivory', major_seg=32, minor_seg=10))


def glove_open():
    """Mão relaxada: luva grande e fofa, três dedos gordos ligeiramente dobrados para a palma, polegar à frente."""
    p = []
    cuff(p)
    el = [E((0.0, 0.004, -0.075), (0.035, 0.052, 0.047))]
    for y, extra in ((-0.031, -0.004), (0.0, 0.0), (0.031, -0.002)):
        el.append(C((-0.008, y, -0.118 + extra), 0.018, 0.022, rot=(0, 90 - 22, 0)))
    el.append(C((-0.02, 0.05, -0.066), 0.016, 0.021, rot=(0, 60, 62)))
    p.append(fb.meta('GloveOpenHand', el, 'ivory', 0.0035))
    for y in (-0.024, 0.0, 0.024):
        p.append(fb.box((0.004, 0.006, 0.04), (0.035, y, -0.09), 'ivoryDark', 0.0015, 1))
    return fb.join('GloveR_Open', p)


def glove_fist():
    """
    Punho fechado à volta de um canal (eixo +y Blender = +z Unity, centro a GRIP abaixo do pulso, raio 1,5 cm):
    dedos enrolados por fora do canal (nós em +x), pontas a voltar pelo lado da palma, polegar por cima.
    """
    p = []
    cuff(p)
    x0, z0 = -0.004, -GRIP
    el = [E((0.012, 0.0, -0.05), (0.036, 0.05, 0.03))]
    for y in (-0.036, -0.012, 0.012, 0.036):
        for ang in (55, 10, -40, -90, -140):
            a = math.radians(ang)
            el.append(B((x0 + math.cos(a) * 0.03, y, z0 + math.sin(a) * 0.03), 0.019))
    el.append(C((-0.036, 0.012, z0 + 0.018), 0.02, 0.019, rot=(0, 0, 90)))
    fist = fb.meta('GloveFistHand', el, 'ivory', 0.0035)
    cut_channel(fist, (x0, 0, z0), 0.0145, (90, 0, 0))
    p.append(fist)
    return fb.join('GloveR_Fist', p)


def glove_bridge_left():
    """
    Mão esquerda em ponte (frame canónico da esquerda: palma +x, polegar +y, dedos −z em Blender).
    Palma espalmada, quatro dedos em leque (pontas a tocar na mesa), polegar levantado encostado ao indicador:
    o "V" entre o polegar e o nó do indicador corre ao longo dos dedos e é onde o taco assenta.
    """
    p = []
    cuff(p)
    el = [E((0.012, 0.0, -0.07), (0.022, 0.052, 0.046))]
    for i, ang in enumerate((-26, -9, 8, 24)):
        a = math.radians(ang)
        d = (0.0, math.sin(a), -math.cos(a))
        base = (0.014, -0.03 + i * 0.02, -0.095)
        c = tuple(base[k] + d[k] * 0.022 for k in range(3))
        el.append(C(c, 0.018, 0.017, rot=(90 + ang, 0, 90)))
    el.append(C((-0.018, 0.058, -0.068), 0.024, 0.02, rot=(0, 90, 0)))   # polegar levantado, ao longo dos dedos
    el.append(C((-0.004, 0.026, -0.088), 0.016, 0.018, rot=(0, 90, 0)))  # nó do indicador
    p.append(fb.meta('GloveBridgeHand', el, 'ivory', 0.0035))
    return fb.join('GloveL_Bridge', p)


# ------------------------------------------------------------------------------------------ boné (igual ao anterior)

def cut_below(obj, z):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < z], context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()


def cap():
    parts = []
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.5, segments=32, ring_count=14)
    dome = bpy.context.active_object
    bm = bmesh.new()
    bm.from_mesh(dome.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.001], context='VERTS')
    bm.to_mesh(dome.data)
    bm.free()
    dome.scale = (0.47, 0.47, 0.36)
    solid = dome.modifiers.new('solid', 'SOLIDIFY')
    solid.thickness = 0.03
    fb._finish(dome, 'navy')
    parts.append(dome)
    for i in range(6):
        seam = fb.torus(0.236, 0.004, (0, 0, 0), 'suitDark', rot=(90, 0, i * 60), major_seg=32, minor_seg=4)
        fb.apply_all(seam)
        cut_below(seam, 0.02)
        seam.scale = (1.0, 1.0, 0.36 / 0.47)
        parts.append(seam)
    bm = bmesh.new()
    nx, ny = 12, 6
    grid = []
    for j in range(ny + 1):
        row = []
        v = j / ny
        for i in range(nx + 1):
            u = i / nx * 2 - 1
            half = 0.19 * math.sqrt(max(0.0, 1 - (v * 0.92) ** 2)) + 0.005
            row.append(bm.verts.new((u * half, 0.12 + v * 0.2, 0.03 - 0.035 * (v ** 2) - 0.03 * (u ** 2) * v)))
        grid.append(row)
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    brim = fb.from_bmesh(bm, 'ivory', 'brim')
    s = brim.modifiers.new('solid', 'SOLIDIFY')
    s.thickness = 0.018
    s.offset = 0
    fb.bevel(brim, 0.006, 2)
    parts.append(brim)
    parts.append(fb.sphere(0.02, (0, 0, 0.178), 'navy', scale=(1, 1, 0.6), segments=16, rings=8))
    parts.append(fb.cylinder(0.238, 0.03, (0, 0, 0.015), 'suitDark', verts=32))
    logo_at = mathutils.Vector((0, 0.205, 0.09))
    parts.append(fb.cylinder(0.047, 0.012, tuple(logo_at), 'ivory', rot=(-52, 0, 0), verts=36, bevel_w=0.004))
    tri = fb.prism([(-0.014, -0.011), (0.014, -0.011), (0.0, 0.013)], 0.008, 'mustard', center=(-0.02, 0.0, 0.004))
    circ = fb.cylinder(0.011, 0.008, (0.0, 0.0, 0.016), 'coral', rot=(90, 0, 0), verts=20)
    sq = fb.box((0.02, 0.008, 0.02), (0.02, 0.0, -0.006), 'teal', 0.003, 2)
    for o in (tri, circ, sq):
        fb.apply_all(o)
        o.rotation_euler = (math.radians(38), 0, 0)
        o.location = logo_at + mathutils.Vector((0, 0.006, 0.004))
        parts.append(o)
    return fb.join('Cap', parts)


# ------------------------------------------------------------------------------------------ construir e exportar

objs = [torso(), pelvis(), collar(), thigh(), shin(), boot(), upper_arm(), forearm()]
g_open = glove_open()
g_fist = glove_fist()
objs += [g_open, fb.mirror_x(g_open, 'GloveL_Open'), g_fist, fb.mirror_x(g_fist, 'GloveL_Fist'), glove_bridge_left(), cap()]
fb.export_bytes(os.path.join(OUT, 'character.bytes'), objs)

# ------------------------------------------------------------------------------------------ pré-visualização

for o in objs:
    o.hide_render = True


def place(name, loc, rot=(0, 0, 0), scale=1.0):
    src = bpy.data.objects[name]
    d = src.copy()
    d.data = src.data
    d.hide_render = False
    bpy.context.collection.objects.link(d)
    d.location = loc
    d.rotation_euler = [math.radians(a) for a in rot]
    d.scale = (scale, scale, scale)
    return d


def figure(origin, arm_out=8.0):
    o = mathutils.Vector(origin)
    hips = o + mathutils.Vector((0, 0, HIPS))
    spine = hips + mathutils.Vector((0, 0, SPINE_UP))
    place('Pelvis', hips)
    place('Torso', spine)
    for side in (-1, 1):
        th = hips + mathutils.Vector((side * PIVOT_X, 0, -0.05))
        kn = th + mathutils.Vector((0, 0, -THIGH))
        an = kn + mathutils.Vector((0, 0, -SHIN))
        place('Thigh', th)
        place('Shin', kn)
        place('Boot', an)
        sh = spine + mathutils.Vector((side * SHOULDER[0], 0, SHOULDER[1]))
        rot = (0, -side * arm_out, 0)
        r = mathutils.Euler([math.radians(a) for a in rot]).to_matrix()
        el = sh + r @ mathutils.Vector((0, 0, -UPPER_ARM))
        wr = el + r @ mathutils.Vector((0, 0, -FOREARM))
        place('UpperArm', sh, rot)
        place('Forearm', el, rot)
        place('GloveR_Open' if side > 0 else 'GloveL_Open', wr, rot)
    neck = spine + mathutils.Vector((0, 0, TORSO_H))
    place('Collar', neck)
    head_at = neck + mathutils.Vector((0, 0, 0.03))
    hb = fb.box((0.46 * 1.12, 0.4 * 1.12, 0.43 * 1.12), head_at + mathutils.Vector((0, 0, 0.215 * 1.12)), 'teal', 0.08, 5)
    hb.hide_render = False
    for side in (-1, 1):
        e = fb.sphere(0.03, head_at + mathutils.Vector((side * 0.085 * 1.12, 0.225 * 1.12, 0.2 * 1.12)), 'eye', scale=(0.9, 0.5, 1.7))
        e.hide_render = False
    place('Cap', head_at + mathutils.Vector((0, 0, (0.43 - 0.05) * 1.12)), rot=(6, 0, 0), scale=1.12)


figure((0, 0, 0))
fb.setup_preview()
os.makedirs(PREV, exist_ok=True)
target = (0, 0, 0.78)
fb.render(os.path.join(PREV, 'char_front.png'), target, 3.6, 0, 8, lens=50)
fb.render(os.path.join(PREV, 'char_34.png'), target, 3.6, 35, 12, lens=50)
fb.render(os.path.join(PREV, 'char_side.png'), target, 3.6, 90, 5, lens=50)
fb.render(os.path.join(PREV, 'char_back.png'), target, 3.6, 180, 10, lens=50)

# Luvas de perto, com um taco de teste a passar no canal / no V.
for o in list(bpy.context.scene.objects):
    if o.type == 'MESH':
        o.hide_render = True
gf = place('GloveR_Fist', (0, 0, 0))
cue = fb.cylinder(0.011, 0.5, (0, 0, -GRIP), 'wood', rot=(90, 0, 0), verts=16)
fb.render(os.path.join(PREV, 'glove_fist.png'), (0, 0, -0.07), 0.55, 60, 25, lens=50)
fb.render(os.path.join(PREV, 'glove_fist_front.png'), (0, 0, -0.07), 0.55, 0, 5, lens=50)
gf.hide_render = True
cue.hide_render = True
gb = place('GloveL_Bridge', (0, 0, 0))
# No jogo: palma (+x canónico) para baixo, dedos (−z) para a frente (+y), polegar (+y canónico) para a direita (+x).
gb.matrix_world = mathutils.Matrix(((0, 1, 0, 0), (0, 0, -1, 0), (-1, 0, 0, 0), (0, 0, 0, 1)))
table = fb.box((0.5, 0.5, 0.02), (0, 0.1, -0.034), 'felt')
# V na posição canónica (−0.035, 0.042, −0.07) Blender → mundo pela mesma matriz.
groove = gb.matrix_world @ mathutils.Vector((-0.035, 0.042, -0.07))
cue2 = fb.cylinder(0.011, 0.5, tuple(groove), 'wood', rot=(90, 0, 0), verts=16)
fb.render(os.path.join(PREV, 'glove_bridge.png'), tuple(groove), 0.45, 200, 25, lens=50)
fb.render(os.path.join(PREV, 'glove_bridge_top.png'), tuple(groove), 0.45, 150, 55, lens=50)
