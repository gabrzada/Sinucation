"""
Corpo das personagens v3 — bonequinho de vinil mais alto e esguio (o character sheet é referência; a mesa do jogo
é maior, por isso braços e pernas são mais compridos para a tacada caber sem atravessar nada).

Peças (origem na articulação do rig do jogo, ver AvatarVisual):
  Pelvis, Torso (suspensórios contínuos, cinto, bolsas, emblema), Neck (pescoço escuro + gola marfim que liga o
  tronco à cabeça — a cabeça assenta na gola, sem ficar a flutuar), Thigh, Shin, Boot, UpperArm, Forearm,
  luvas em três poses (gloves.py) e o boné.

Rig v3 (Unity, y para cima): ancas 0.56 · coluna +0.04 · pescoço 0.44 acima da coluna · cabeça +0.02
  ombros (±0.19, 0.37) · braço 0.18 · antebraço 0.17 · coxa (±0.085, −0.05) · coxa 0.21 · canela 0.195
  · tornozelo → sola 0.105.
Uso: blender -b --factory-startup -P character.py -- <pasta_saida_bytes> <pasta_previews>
"""
import json
import math
import os
import sys

import bmesh
import bpy
import mathutils
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import fsblend as fb  # noqa: E402
import gloves as gl  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if len(argv) > 0 else os.path.join(HERE, 'out')
PREV = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(HERE, 'preview')

fb.reset()

# Rig (Blender: x direita, y frente, z cima).
HIPS = 0.56
SPINE_UP = 0.04
NECK_UP = 0.44
HEAD_UP = 0.02
SHOULDER = (0.19, 0.37)
UPPER_ARM = 0.18
FOREARM = 0.17
PIVOT_X = 0.085
PIVOT_DROP = 0.05
THIGH = 0.21
SHIN = 0.195
ANKLE_GROUND = 0.105


# ------------------------------------------------------------------------------------------ utilitários

def signed_power(x, p):
    return math.copysign(abs(x) ** p, x)


def loft(name, profile, mat, exponent=2.6, y_center=0.0, segments=40):
    """Secções super-elípticas (z, rx, ry) com as pontas fechadas: silhueta controlada ao milímetro."""
    verts, faces = [], []
    for z, rx, ry in profile:
        for i in range(segments):
            a = i * math.tau / segments
            verts.append((rx * signed_power(math.cos(a), 2 / exponent), y_center + ry * signed_power(math.sin(a), 2 / exponent), z))
    faces.append(tuple(reversed(range(segments))))
    for r in range(len(profile) - 1):
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((r * segments + i, r * segments + j, (r + 1) * segments + j, (r + 1) * segments + i))
    faces.append(tuple((len(profile) - 1) * segments + i for i in range(segments)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return fb._finish(obj, mat)


def pipe(name, points, radius, mat):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.resolution_u = 1
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    spline = curve.splines.new('POLY')
    spline.points.add(len(points) - 1)
    for p, co in zip(spline.points, points):
        p.co = (*co, 1)
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(fb.material(mat))
    return obj


# ------------------------------------------------------------------------------------------ tronco

# (z acima da coluna, meia-largura, meia-profundidade): barriguinha em pera, cintura, peito e ombros redondos a
# fechar numa abertura do tamanho do pescoço.
BODY = [(-0.026, 0.113, 0.083), (-0.018, 0.131, 0.097), (0.009, 0.143, 0.106), (0.072, 0.150, 0.111),
        (0.154, 0.146, 0.108), (0.242, 0.138, 0.101), (0.319, 0.133, 0.096), (0.369, 0.134, 0.094),
        (0.402, 0.121, 0.085), (0.424, 0.096, 0.07), (0.438, 0.064, 0.052)]


def section(z):
    for (z0, rx0, ry0), (z1, rx1, ry1) in zip(BODY, BODY[1:]):
        if z <= z1:
            t = max(0.0, (z - z0) / (z1 - z0))
            return rx0 + (rx1 - rx0) * t, ry0 + (ry1 - ry0) * t
    return BODY[-1][1], BODY[-1][2]


def front(x, z):
    """Profundidade da superfície do tronco (y > 0) no ponto (x, z)."""
    rx, ry = section(z)
    return ry * max(0.0, 1 - (abs(x) / rx) ** 2.6) ** (1 / 2.6)


def strap(side):
    """Suspensório contínuo: frente, por cima do ombro, costas (fita com espessura)."""
    path_front = [(side * (0.078 - 0.013 * i / 24), 0.065 + 0.331 * i / 24) for i in range(25)]
    verts = []
    for x, z in path_front:
        for dx in (-0.018, 0.018):
            verts.append((x + dx, front(x + dx, z) + 0.005, z))
    end = verts[-2:]
    for i in range(1, 17):
        t = i / 16
        for x, y, z in end:
            verts.append((x, y * math.cos(math.pi * t), z + 0.03 * math.sin(math.pi * t)))
    for i in range(1, 25):
        z = 0.396 - 0.331 * i / 24
        x = side * (0.065 - 0.022 * i / 24)
        for dx in (-0.018, 0.018):
            verts.append((x + dx, -front(x + dx, z) - 0.005, z))
    faces = [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(len(verts) // 2 - 1)]
    mesh = bpy.data.meshes.new('HarnessRibbon')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new('HarnessRibbon', mesh)
    bpy.context.collection.objects.link(obj)
    fb._finish(obj, 'ivory')
    solid = obj.modifiers.new('thickness', 'SOLIDIFY')
    solid.thickness = 0.004
    solid.offset = 0
    fb.bevel(obj, 0.0015, 2, limit='ANGLE', angle=30)
    return obj


def torso():
    parts = [loft('Suit', BODY, 'suit')]
    for side in (-1, 1):
        parts.append(strap(side))
        # Mola dourada do suspensório.
        x, z = side * 0.07, 0.338
        y = front(x, z) + 0.012
        parts += [fb.box((0.044, 0.012, 0.045), (x, y, z), 'suitDark', 0.007, 3),
                  fb.cylinder(0.016, 0.01, (x, y + 0.007, z), 'brass', rot=(90, 0, 0), verts=24, bevel_w=0.003),
                  fb.cylinder(0.0075, 0.011, (x, y + 0.012, z), 'dark', rot=(90, 0, 0), verts=20)]
        # Costura fina junto à borda exterior do suspensório.
        pts = []
        for i in range(23):
            zz = 0.08 + 0.3 * i / 22
            xx = side * (0.094 - 0.013 * i / 22)
            pts.append((xx, front(xx, zz) + 0.0075, zz))
        parts.append(pipe('HarnessEdge', pts, 0.0011, 'ivoryDark'))
        # Passadores do cinto e bolsas de lado.
        parts.append(fb.box((0.017, 0.016, 0.052), (side * 0.1, front(side * 0.1, 0.03) + 0.004, 0.03), 'suitDark', 0.005, 3))
        parts += [fb.box((0.05, 0.07, 0.08), (side * 0.15, -0.022, 0.014), 'suitDark', 0.014, 4),
                  fb.box((0.054, 0.073, 0.025), (side * 0.151, -0.022, 0.052), 'leather', 0.009, 3),
                  fb.cylinder(0.0075, 0.006, (side * 0.18, -0.017, 0.035), 'brass', rot=(0, 90, 0), verts=16)]
    # Cinto de couro com fivela.
    parts.append(loft('Belt', [(-0.004, 0.136, 0.1), (0.002, 0.151, 0.115), (0.039, 0.154, 0.116), (0.045, 0.145, 0.109)], 'leather', 3))
    yb = front(0, 0.021)
    parts += [fb.box((0.06, 0.015, 0.047), (0, yb + 0.012, 0.021), 'brass', 0.008, 3),
              fb.box((0.036, 0.01, 0.025), (0, yb + 0.021, 0.021), 'suitDark', 0.004, 3),
              fb.box((0.006, 0.01, 0.028), (0, yb + 0.028, 0.021), 'brass', 0.002, 2)]
    # Emblema △○□ numa base cosida.
    z, y = 0.228, front(0, 0.228)
    parts += [fb.box((0.108, 0.02, 0.102), (0, y + 0.001, z), 'suitDark', 0.019, 4),
              fb.cylinder(0.043, 0.014, (0, y + 0.016, z), 'brass', rot=(90, 0, 0), verts=40, bevel_w=0.003),
              fb.cylinder(0.035, 0.014, (0, y + 0.023, z), 'navy', rot=(90, 0, 0), verts=40),
              fb.prism([(-0.018, -0.017), (0.004, -0.017), (-0.007, 0.007)], 0.008, 'mustard', center=(0, y + 0.032, z)),
              fb.sphere(0.0105, (0.004, y + 0.033, z + 0.013), 'coral', scale=(1, 0.4, 1), segments=20, rings=12),
              fb.box((0.017, 0.008, 0.019), (0.016, y + 0.032, z - 0.009), 'teal', 0.003, 3)]
    # Pala nas costas.
    parts.append(fb.box((0.075, 0.012, 0.026), (0, -front(0, 0.3) - 0.004, 0.3), 'leather', 0.007, 3))
    return fb.join('Torso', parts)


def pelvis():
    seat = loft('TrouserSeat', [(-0.092, 0.084, 0.067), (-0.085, 0.113, 0.087), (-0.054, 0.137, 0.101),
                                (-0.008, 0.138, 0.103), (0.03, 0.128, 0.095)], 'suit', 2.6)
    return fb.join('Pelvis', [seat])


def neck():
    """
    Pescoço (origem no pivô do pescoço): coluna escura que entra no tronco e sobe por dentro da cabeça (a cabeça
    assenta 2 cm acima), com uma gola marfim enrolada a tapar a junção — nunca fica espaço entre tronco e cabeça.
    """
    col = fb.cylinder(0.05, 0.1, (0, 0, 0.02), 'suitDark', verts=32, radius2=0.046)
    collar = fb.torus(0.056, 0.0125, (0, 0, 0.004), 'ivory', major_seg=40, minor_seg=14)
    band = fb.cylinder(0.061, 0.014, (0, 0, -0.006), 'ivory', verts=40, bevel_w=0.004)
    return fb.join('Neck', [col, collar, band])


# ------------------------------------------------------------------------------------------ pernas e botas

def thigh():
    return fb.join('Thigh', [fb.capsule(0.06, 0.052, THIGH - 0.04, (0, 0, -0.008), 'suit', verts=28)])


def shin():
    parts = [fb.capsule(0.052, 0.047, SHIN - 0.07, (0, 0, 0), 'suit', verts=28),
             fb.sphere(0.046, (0, 0.03, -0.02), 'suitDark', scale=(0.9, 0.48, 1), segments=24, rings=14),
             fb.sphere(0.036, (0, 0.046, -0.022), 'ivory', scale=(1, 0.32, 1.06), segments=24, rings=14)]
    return fb.join('Shin', parts)


def boot():
    dz = -(ANKLE_GROUND - 0.103)
    parts = [loft('RubberOutsole', [(-0.103, 0.061, 0.11), (-0.1, 0.075, 0.13), (-0.082, 0.078, 0.134), (-0.074, 0.074, 0.129)], 'tint', 3.0, 0.036),
             loft('IvoryWelt', [(-0.077, 0.075, 0.13), (-0.067, 0.076, 0.129), (-0.059, 0.071, 0.124)], 'ivory', 3.0, 0.036),
             loft('LeatherUpper', [(-0.063, 0.065, 0.118), (-0.049, 0.074, 0.121), (-0.017, 0.072, 0.116),
                                   (0.011, 0.064, 0.098), (0.033, 0.054, 0.072), (0.054, 0.047, 0.054)], 'suitDark', 2.5, 0.028)]
    parts.append(fb.box((0.087, 0.024, 0.021), (0, 0.16, -0.066), 'tint', 0.01, 4))
    parts.append(fb.box((0.065, 0.08, 0.018), (0, 0.047, 0.04), 'suit', 0.008, 3, rot=(-21, 0, 0)))
    for i in range(3):
        parts.append(fb.box((0.06, 0.009, 0.009), (0, 0.034 + i * 0.021, 0.053 - i * 0.008), 'ivory', 0.004, 3, rot=(-21, 0, 0)))
    parts += [fb.torus(0.05, 0.012, (0, 0, 0.058), 'ivory', major_seg=32, minor_seg=10),
              fb.box((0.108, 0.039, 0.029), (0, 0.048, 0.055), 'tint', 0.012, 4),
              fb.box((0.023, 0.018, 0.048), (0, -0.059, 0.038), 'tint', 0.007, 3)]
    for side in (-1, 1):
        parts.append(fb.cylinder(0.011, 0.005, (side * 0.068, 0.035, -0.007), 'brass', rot=(0, 90, 0), verts=20, bevel_w=0.002))
    obj = fb.join('Boot', parts)
    obj.data.transform(mathutils.Matrix.Translation((0, 0, dz)))
    return obj


# ------------------------------------------------------------------------------------------ braços

def upper_arm():
    parts = [fb.sphere(0.06, (0, 0, 0), 'suit', segments=28, rings=16),
             fb.capsule(0.054, 0.047, UPPER_ARM - 0.06, (0, 0, -0.02), 'suit', verts=28),
             fb.cylinder(0.05, 0.03, (0, 0, -UPPER_ARM + 0.04), 'suitDark', verts=32, bevel_w=0.007),
             fb.torus(0.049, 0.0045, (0, 0, -UPPER_ARM + 0.034), 'tint', major_seg=32, minor_seg=8)]
    return fb.join('UpperArm', parts)


def forearm():
    parts = [fb.capsule(0.046, 0.04, FOREARM - 0.05, (0, 0, 0), 'suit', verts=28),
             fb.cylinder(0.041, 0.02, (0, 0, -FOREARM + 0.022), 'suitDark', verts=32, bevel_w=0.006)]
    return fb.join('Forearm', parts)


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

def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


body = [torso(), pelvis(), neck(), thigh(), shin(), boot(), upper_arm(), forearm()]
g_open = gl.glove_open()
g_open_l = fb.mirror_x(g_open, 'GloveL_Open')
g_fist = gl.glove_fist()
fist_clearance = gl.channel_clearance(g_fist)
fist_wrap = gl.channel_wrap(g_fist)
g_fist_l = fb.mirror_x(g_fist, 'GloveL_Fist')
g_bridge, bridge_pre, bridge_post = gl.glove_bridge_left()
the_cap = cap()
objs = body + [g_open, g_open_l, g_fist, g_fist_l, g_bridge, the_cap]
fb.export_bytes(os.path.join(OUT, 'character.bytes'), objs)

bm_ = gl.bridge_matrix()
report = {
    'rig': dict(hips=HIPS, spine_up=SPINE_UP, neck_up=NECK_UP, head_up=HEAD_UP, shoulder=SHOULDER, upper_arm=UPPER_ARM,
                forearm=FOREARM, pivot_x=PIVOT_X, pivot_drop=PIVOT_DROP, thigh=THIGH, shin=SHIN, ankle_ground=ANKLE_GROUND),
    'triangles': {o.name: tris(o) for o in objs},
    'fist_channel_clearance_m': round(fist_clearance, 5),
    'fist_channel_wrap': round(fist_wrap, 3),
    'bridge_clearance_m_by_tilt_before_groove': {str(k): round(v, 5) for k, v in bridge_pre.items()},
    'bridge_clearance_m_by_tilt': {str(k): round(v, 5) for k, v in bridge_post.items()},
    'unity': {
        'fist_channel': [round(x, 6) for x in gl.unity_vec(gl.CHANNEL)],
        'bridge_hand_rotation_xyzw': [round(x, 6) for x in gl.unity_quat(bm_.to_3x3())],
        'bridge_wrist': [round(x, 6) for x in gl.unity_vec(gl.BRIDGE_WRIST)],
        'bridge_tips': [round(x, 6) for x in gl.unity_vec(gl.BRIDGE_TIPS)],
        'bridge_height': gl.BRIDGE_H,
    },
}
os.makedirs(PREV, exist_ok=True)
with open(os.path.join(PREV, 'character_report.json'), 'w') as f:
    json.dump(report, f, indent=2)
print('REPORT', json.dumps(report))
assert min(bridge_post.values()) > 0.0005, 'o taco toca na luva da ponte'
assert fist_clearance > 0.0005, 'o punho do taco toca na luva'

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


HEADS = None
head_archive = os.path.join(HERE, 'revisions', '2026-09-26-before-refinement', 'head-meshes.json')
if os.path.exists(head_archive):
    with open(head_archive) as f:
        HEADS = json.load(f)


def head(shape, at):
    """Cabeça real do jogo (arquivada do Unity pelo CharacterReview), com olhos e boné já encaixados."""
    if HEADS is None:
        fb.box((0.52, 0.45, 0.48), at + Vector((0, 0, 0.24)), 'teal', 0.08, 5).hide_render = False
        return
    for part in HEADS['parts']:
        if part['archetype'] != shape:
            continue
        verts = [(v['x'], v['z'], v['y']) for v in part['vertices']]
        faces, slots = [], []
        for slot, surface in enumerate(part['surfaces']):
            idx = surface['triangles']
            for i in range(0, len(idx), 3):
                faces.append((idx[i], idx[i + 2], idx[i + 1]))
                slots.append(slot)
        mesh = bpy.data.meshes.new(shape + part['name'])
        mesh.from_pydata(verts, [], faces)
        mesh.update()
        obj = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = at
        for slot, surface in enumerate(part['surfaces']):
            c = surface['color']
            mat = bpy.data.materials.new(shape + part['name'] + str(slot))
            mat.diffuse_color = (c['r'], c['g'], c['b'], 1)
            mesh.materials.append(mat)
        for poly, slot in zip(mesh.polygons, slots):
            poly.material_index = slot
            poly.use_smooth = True


def figure(origin, shape='square', arm_out=7.0):
    o = Vector(origin)
    hips = o + Vector((0, 0, HIPS))
    spine = hips + Vector((0, 0, SPINE_UP))
    place('Pelvis', hips)
    place('Torso', spine)
    for side in (-1, 1):
        th = hips + Vector((side * PIVOT_X, 0, -PIVOT_DROP))
        kn = th + Vector((0, 0, -THIGH))
        an = kn + Vector((0, 0, -SHIN))
        place('Thigh', th)
        place('Shin', kn)
        place('Boot', an)
        sh = spine + Vector((side * SHOULDER[0], 0, SHOULDER[1]))
        rot = (0, -side * arm_out, 0)
        r = mathutils.Euler([math.radians(a) for a in rot]).to_matrix()
        el = sh + r @ Vector((0, 0, -UPPER_ARM))
        wr = el + r @ Vector((0, 0, -FOREARM))
        place('UpperArm', sh, rot)
        place('Forearm', el, rot)
        place('GloveR_Open' if side > 0 else 'GloveL_Open', wr, rot)
    nk = spine + Vector((0, 0, NECK_UP))
    place('Neck', nk)
    head(shape, nk + Vector((0, 0, HEAD_UP)))


figure((0, 0, 0))
fb.setup_preview()
target = (0, 0, 0.8)
fb.render(os.path.join(PREV, 'char_front.png'), target, 3.8, 0, 8, lens=50)
fb.render(os.path.join(PREV, 'char_34.png'), target, 3.8, 35, 12, lens=50)
fb.render(os.path.join(PREV, 'char_side.png'), target, 3.8, 90, 5, lens=50)
fb.render(os.path.join(PREV, 'char_back.png'), target, 3.8, 180, 10, lens=50)
fb.render(os.path.join(PREV, 'char_neck.png'), (0, 0, 1.02), 1.2, 25, 6, lens=50)
