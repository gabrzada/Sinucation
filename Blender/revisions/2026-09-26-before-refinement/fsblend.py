"""
Utilitários partilhados pelos scripts de modelação do FriendSlop (Blender 5.x, modo script).

Os modelos são construídos por código (primitivas + bevel/subdivisão), exportados para um formato binário
simples (.bytes) que o jogo lê em Resources/Models/FS, e verificados com renders de pré-visualização.

Convenções: Blender Z para cima e a frente em +Y. Cada "peça" tem a origem no ponto de articulação do rig
do jogo. Na exportação: Unity (x, y, z) = Blender (x, z, y) e a ordem dos triângulos é invertida.
"""
import math
import os
import struct

import bmesh
import bpy
import mathutils
from mathutils import Matrix, Vector

# Cores só para a pré-visualização (no jogo cada nome de material é mapeado para a paleta/cor do jogador).
PREVIEW = {
    'suit': (0.10, 0.20, 0.34),
    'suitDark': (0.035, 0.06, 0.11),
    'ivory': (0.93, 0.87, 0.74),
    'ivoryDark': (0.72, 0.66, 0.55),
    'leather': (0.33, 0.19, 0.10),
    'brass': (0.95, 0.70, 0.18),
    'tint': (0.95, 0.36, 0.30),
    'mustard': (0.96, 0.72, 0.16),
    'coral': (0.94, 0.36, 0.30),
    'teal': (0.13, 0.72, 0.68),
    'navy': (0.07, 0.13, 0.24),
    'eye': (0.01, 0.01, 0.02),
    'shine': (1.0, 1.0, 1.0),
    'steel': (0.55, 0.58, 0.62),
    'rubber': (0.04, 0.04, 0.05),
    'wood': (0.42, 0.22, 0.11),
    'felt': (0.05, 0.55, 0.42),
    'goo': (0.45, 1.0, 0.25),
    'railMustard': (0.93, 0.66, 0.14),
    'rubber': (0.04, 0.04, 0.05),
    'cushion': (0.04, 0.5, 0.4),
    'red': (0.85, 0.12, 0.12),
    'dark': (0.02, 0.025, 0.04),
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        c = PREVIEW.get(name, (0.7, 0.7, 0.7))
        m.diffuse_color = (c[0], c[1], c[2], 1.0)
        m.roughness = 0.5
    return m


def _finish(obj, mat, smooth=True):
    obj.data.materials.clear()
    obj.data.materials.append(material(mat))
    if smooth:
        for p in obj.data.polygons:
            p.use_smooth = True
    return obj


def _active(obj):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_all(obj):
    """Aplica modificadores e transformações (a peça fica em coordenadas da articulação)."""
    _active(obj)
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def bevel(obj, width, segments=3, limit='NONE', angle=40):
    m = obj.modifiers.new('bevel', 'BEVEL')
    m.width = width
    m.segments = segments
    m.limit_method = limit
    if limit == 'ANGLE':
        m.angle_limit = math.radians(angle)
    m.profile = 0.5
    return obj


def subsurf(obj, levels=2):
    m = obj.modifiers.new('subsurf', 'SUBSURF')
    m.levels = levels
    m.render_levels = levels
    return obj


def box(size, loc=(0, 0, 0), mat='suit', radius=0.0, segments=3, rot=(0, 0, 0), sub=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[math.radians(a) for a in rot])
    o = bpy.context.active_object
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if radius > 0:
        bevel(o, radius, segments)
    if sub:
        subsurf(o, sub)
    return _finish(o, mat)


def sphere(radius, loc=(0, 0, 0), mat='suit', scale=(1, 1, 1), segments=32, rings=16, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, segments=segments, ring_count=rings, location=loc, rotation=[math.radians(a) for a in rot])
    o = bpy.context.active_object
    o.scale = scale
    return _finish(o, mat)


def cylinder(radius, depth, loc=(0, 0, 0), mat='suit', rot=(0, 0, 0), verts=32, bevel_w=0.0, segments=3, radius2=None):
    if radius2 is None:
        bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, vertices=verts, location=loc, rotation=[math.radians(a) for a in rot])
    else:
        bpy.ops.mesh.primitive_cone_add(radius1=radius, radius2=radius2, depth=depth, vertices=verts, location=loc, rotation=[math.radians(a) for a in rot])
    o = bpy.context.active_object
    if bevel_w > 0:
        bevel(o, bevel_w, segments, 'ANGLE', 50)
    return _finish(o, mat)


def torus(major, minor, loc=(0, 0, 0), mat='suit', rot=(0, 0, 0), scale=(1, 1, 1), major_seg=40, minor_seg=14):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=major_seg, minor_segments=minor_seg,
                                     location=loc, rotation=[math.radians(a) for a in rot])
    o = bpy.context.active_object
    o.scale = scale
    return _finish(o, mat)


def capsule(r_top, r_bottom, length, top=(0, 0, 0), mat='suit', verts=24):
    """Tubo arredondado a descer de 'top' (eixo -Z), com raios diferentes em cima e em baixo."""
    bm = bmesh.new()
    rings = []
    steps_cap = 6
    profile = []
    # Hemisfério de cima.
    for i in range(steps_cap + 1):
        a = (math.pi / 2) * (1 - i / steps_cap)
        profile.append((r_top * math.cos(a), r_top * math.sin(a)))
    # Corpo.
    for i in range(1, 6):
        t = i / 6
        profile.append((r_top + (r_bottom - r_top) * t, -length * t))
    # Hemisfério de baixo.
    for i in range(steps_cap + 1):
        a = (math.pi / 2) * (i / steps_cap)
        profile.append((r_bottom * math.cos(a), -length - r_bottom * math.sin(a)))
    for (rad, z) in profile:
        if rad < 1e-6:
            pole = bm.verts.new((top[0], top[1], top[2] + z))
            rings.append([pole] * verts)
            continue
        ring = []
        for j in range(verts):
            ang = 2 * math.pi * j / verts
            ring.append(bm.verts.new((top[0] + rad * math.cos(ang), top[1] + rad * math.sin(ang), top[2] + z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for j in range(verts):
            a, b = rings[k][j], rings[k][(j + 1) % verts]
            c, d = rings[k + 1][(j + 1) % verts], rings[k + 1][j]
            quad = [a, d, c, b]
            uniq = []
            for v in quad:
                if v not in uniq:
                    uniq.append(v)
            if len(uniq) >= 3:
                bm.faces.new(uniq)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new('capsule')
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new('capsule', me)
    bpy.context.collection.objects.link(o)
    return _finish(o, mat)


def from_bmesh(bm, mat, name='mesh', smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return _finish(o, mat, smooth)


def prism(points2d, depth, mat, axis='Y', center=(0, 0, 0)):
    """Extrusão de um polígono 2D (x, z) ao longo de Y (profundidade centrada)."""
    bm = bmesh.new()
    front = [bm.verts.new((center[0] + p[0], center[1] - depth / 2, center[2] + p[1])) for p in points2d]
    back = [bm.verts.new((center[0] + p[0], center[1] + depth / 2, center[2] + p[1])) for p in points2d]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(points2d)
    for i in range(n):
        bm.faces.new((front[i], front[(i + 1) % n], back[(i + 1) % n], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return from_bmesh(bm, mat, 'prism', smooth=False)


def join(name, objs):
    objs = [o for o in objs if o is not None]
    for o in objs:
        apply_all(o)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def mirror_x(obj, name):
    """Cópia espelhada em X (luva esquerda a partir da direita)."""
    _active(obj)
    bpy.ops.object.duplicate()
    d = bpy.context.active_object
    d.scale = (-1, 1, 1)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    me = d.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    d.name = name
    d.data.name = name
    return d


# ---------------------------------------------------------------------------------------------- exportação

def export_bytes(path, objs, box_uv=False):
    """Formato FSM1: nome, materiais (submalhas), vértices (pos, normal, uv) e índices por submalha."""
    deps = bpy.context.evaluated_depsgraph_get()
    out = bytearray()
    out += b'FSM1'
    out += struct.pack('<i', len(objs))
    stats = []
    for obj in objs:
        ev = obj.evaluated_get(deps)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        normals = me.corner_normals
        uv_layer = me.uv_layers.active
        mats = [s.material.name if s.material else 'default' for s in obj.material_slots] or ['default']
        verts = []
        lookup = {}
        subs = [[] for _ in mats]
        for tri in me.loop_triangles:
            idx = []
            for li in tri.loops:
                v = me.vertices[me.loops[li].vertex_index].co
                n = normals[li].vector
                if box_uv:
                    # Projecção em caixa (1 unidade de UV = 1 m) para texturas repetidas (madeira, metal gasto).
                    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
                    uv = (v.x, v.y) if az >= ax and az >= ay else ((v.y, v.z) if ax >= ay else (v.x, v.z))
                else:
                    uv = uv_layer.data[li].uv if uv_layer else (0.0, 0.0)
                key = (round(v.x, 5), round(v.y, 5), round(v.z, 5), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(uv[0], 4), round(uv[1], 4))
                i = lookup.get(key)
                if i is None:
                    i = len(verts)
                    lookup[key] = i
                    verts.append((v.x, v.z, v.y, n.x, n.z, n.y, uv[0], uv[1]))
                idx.append(i)
            m = min(tri.material_index, len(subs) - 1)
            # Blender CCW → Unity CW: inverte a ordem.
            subs[m].extend((idx[0], idx[2], idx[1]))
        name = obj.name.encode('utf-8')
        out += struct.pack('<i', len(name)) + name
        used = [(mats[i], s) for i, s in enumerate(subs) if s]
        out += struct.pack('<i', len(used))
        for mname, _ in used:
            b = mname.encode('utf-8')
            out += struct.pack('<i', len(b)) + b
        out += struct.pack('<i', len(verts))
        for v in verts:
            out += struct.pack('<3f', v[0], v[1], v[2])
        for v in verts:
            out += struct.pack('<3f', v[3], v[4], v[5])
        for v in verts:
            out += struct.pack('<2f', v[6], v[7])
        for _, s in used:
            out += struct.pack('<i', len(s))
            out += struct.pack('<%di' % len(s), *s)
        stats.append((obj.name, len(verts), sum(len(s) for _, s in used) // 3))
        ev.to_mesh_clear()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(out)
    for s in stats:
        print('  %-18s %6d verts %6d tris' % s)
    print('exported', path, len(out), 'bytes')


# ---------------------------------------------------------------------------------------------- pré-visualização

def setup_preview(background=(0.93, 0.9, 0.84)):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    shading = scene.display.shading
    shading.light = 'STUDIO'
    shading.color_type = 'MATERIAL'
    shading.show_shadows = True
    shading.shadow_intensity = 0.35
    shading.show_cavity = True
    shading.cavity_type = 'BOTH'
    shading.show_object_outline = False
    shading.show_specular_highlight = True
    scene.display.render_aa = '16'
    world = bpy.data.worlds.new('w')
    scene.world = world
    world.color = background
    scene.render.film_transparent = False
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    return scene


def render(path, target, distance, yaw_deg, pitch_deg=12, lens=60, ortho=0.0, res=(900, 900)):
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = res
    cam_data = bpy.data.cameras.new('cam')
    cam_data.lens = lens
    if ortho > 0:
        cam_data.type = 'ORTHO'
        cam_data.ortho_scale = ortho
    cam = bpy.data.objects.new('cam', cam_data)
    bpy.context.collection.objects.link(cam)
    yaw = math.radians(yaw_deg)
    pitch = math.radians(pitch_deg)
    t = Vector(target)
    # yaw 0 = a olhar para a frente da personagem (câmara em +Y a olhar para -Y).
    offset = Vector((math.sin(yaw) * math.cos(pitch), math.cos(yaw) * math.cos(pitch), math.sin(pitch))) * distance
    cam.location = t + offset
    direction = t - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ---------------------------------------------------------------------------------------------- metaballs

_meta_count = [0]


def meta(name, elements, mat, resolution=0.005, threshold=0.6):
    """
    Forma orgânica feita de metaballs (fundem-se suavemente, como plasticina) convertida em malha.
    elements: dicts com type ('BALL' | 'CAPSULE' | 'ELLIPSOID'), co, radius, size (x, y, z) opcional,
    rot (graus XYZ) opcional, stiffness opcional, negative opcional.
    """
    _meta_count[0] += 1
    base = 'meta%03d' % _meta_count[0]
    mb = bpy.data.metaballs.new(base)
    mb.resolution = resolution
    mb.render_resolution = resolution
    mb.threshold = threshold
    obj = bpy.data.objects.new(base, mb)
    bpy.context.collection.objects.link(obj)
    for e in elements:
        el = mb.elements.new()
        el.type = e.get('type', 'BALL')
        el.co = e['co']
        el.radius = e['radius']
        el.stiffness = e.get('stiffness', 2.0)
        if 'size' in e:
            el.size_x, el.size_y, el.size_z = e['size']
        if 'rot' in e:
            el.rotation = mathutils.Euler([math.radians(a) for a in e['rot']]).to_quaternion()
        el.use_negative = e.get('negative', False)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    mesh_obj = bpy.context.view_layer.objects.active
    mesh_obj.name = name
    return _finish(mesh_obj, mat)
