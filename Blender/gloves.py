"""
Luvas das personagens (v3) em três poses, com a geometria verificada contra o taco do jogo:
  GloveR_Open / GloveL_Open : mão relaxada (luva de desenho animado com quatro dedos gordinhos);
  GloveR_Fist / GloveL_Fist : punho fechado à volta de um canal cilíndrico (o punho do taco passa lá dentro);
  GloveL_Bridge             : mão esquerda em ponte, deitada no pano, com o "V" entre o polegar e o nó do
                              indicador onde o taco assenta (o resto da mão fica à esquerda da linha do taco).

Modelação: primitivas exactas (tubos com raio por ponto, esferas, elipsóides) fundidas por remesh voxel e
suavizadas (aspecto de vinil macio), depois reduzidas. O canal do punho e o sulco da ponte são recortados com o
volume real do taco, e as funções de validação medem as folgas.

As medidas abaixo estão em "unidades de luva" e são multiplicadas por S (luvas grandes de desenho animado); o
raio do canal e o raio do taco não escalam.

Frame canónico da mão (Blender): origem no pulso, dedos para −Z, polegar para +Y;
  mão direita: palma para −X (costas para +X); mão esquerda (espelho): palma para +X.
Unity (x, y, z) = Blender (x, z, y).
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import fsblend as fb

S = 1.18

# Taco do jogo (CueView): haste com 6 mm de raio, punho com 11 mm (a mão direita agarra no punho).
CUE_SHAFT_R = 0.006
CUE_BUTT_R = 0.011

# Canal do punho fechado (mão direita, frame canónico): eixo paralelo a +Y (para a ponta do taco).
CHANNEL = Vector((-0.006, 0.0, -0.086)) * S
CHANNEL_R = 0.0125

# Frame T da ponte (Blender): origem no ponto G do eixo do taco que assenta no "V"; x direita, y para a bola,
# z para cima; a palma assenta no plano z = −BRIDGE_H.
BRIDGE_H0 = 0.045
BRIDGE_H = BRIDGE_H0 * S
# Inclinações do taco em relação à mão (graus, positivo = taco a descer para a bola) que a ponte aguenta.
BRIDGE_TILTS = (-6.0, -3.0, 0.0, 3.0, 6.0, 9.0, 12.0)
# Antebraço a chegar ao pulso por trás, da esquerda e de cima (direcção cotovelo → mão, frame T).
BRIDGE_FOREARM = Vector((0.55, 0.68, -0.48)).normalized()
BRIDGE_WRIST0 = Vector((-0.068, -0.088, 0.004))
BRIDGE_WRIST = BRIDGE_WRIST0 * S
# Ponto de apoio das pontas dos dedos (pivô para levantar a ponte), frame T.
BRIDGE_TIPS = Vector((-0.05, 0.045, -BRIDGE_H0)) * S


def P(x, y, z):
    return Vector((x, y, z)) * S


# ------------------------------------------------------------------------------------------ primitivas

def ball(co, r, mat='ivory', segs=24):
    return fb.sphere(r, tuple(co), mat, segments=segs, rings=max(8, segs // 2))


def ellipsoid(co, half, mat='ivory', rot=(0, 0, 0)):
    return fb.sphere(1.0, tuple(co), mat, scale=tuple(half), segments=28, rings=16, rot=rot)


def ellipsoid_axes(center, ax, ay, half, mat='ivory'):
    """Elipsóide com semi-eixos half ao longo de ax, ay e ax × ay (frame arbitrário)."""
    o = fb.sphere(1.0, (0, 0, 0), mat, scale=tuple(half), segments=28, rings=16)
    fb.apply_all(o)
    x = Vector(ax).normalized()
    y = (Vector(ay) - x * Vector(ay).dot(x)).normalized()
    z = x.cross(y)
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2], m[i][3] = x[i], y[i], z[i], center[i]
    o.data.transform(m)
    return o


def lathe(profile, mat='ivory', segs=40, name='lathe', closed=False):
    """Superfície de revolução à volta de Z; profile = [(raio, z), ...] de cima para baixo (normais para fora)."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * j / segs), r * math.sin(2 * math.pi * j / segs), z))
                      for j in range(segs)])
    for k in range(len(rings) - 1):
        for j in range(segs):
            a, b = rings[k][j], rings[k][(j + 1) % segs]
            c, d = rings[k + 1][(j + 1) % segs], rings[k + 1][j]
            bm.faces.new((a, d, c, b))
    if closed:
        bm.faces.new(rings[0])
        bm.faces.new(list(reversed(rings[-1])))
    return fb.from_bmesh(bm, mat, name)


def tube(points, radii, mat='ivory', caps=True):
    """Tubo suave (Bézier automática) com raio por ponto; pontas arredondadas por esferas."""
    cu = bpy.data.curves.new('tube', 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = 1.0
    cu.bevel_resolution = 5
    cu.use_fill_caps = True
    cu.resolution_u = 10
    sp = cu.splines.new('BEZIER')
    sp.bezier_points.add(len(points) - 1)
    for bp, p, r in zip(sp.bezier_points, points, radii):
        bp.co = tuple(p)
        bp.handle_left_type = 'AUTO'
        bp.handle_right_type = 'AUTO'
        bp.radius = r
    obj = bpy.data.objects.new('tube', cu)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(fb.material(mat))
    parts = [obj]
    if caps:
        parts.append(ball(points[0], radii[0], mat))
        parts.append(ball(points[-1], radii[-1], mat))
    return parts


def sculpt(name, parts, voxel=0.0014, smooth=6, target_tris=4200, mat='ivory', scale=1.0):
    """Funde as primitivas numa só superfície (remesh voxel), suaviza e reduz."""
    obj = fb.join(name, parts)
    if scale != 1.0:
        obj.data.transform(Matrix.Scale(scale, 4))
    m = obj.modifiers.new('remesh', 'REMESH')
    m.mode = 'VOXEL'
    m.voxel_size = voxel
    m.adaptivity = 0.0
    m.use_smooth_shade = True
    fb.apply_all(obj)
    if smooth:
        s = obj.modifiers.new('smooth', 'SMOOTH')
        s.factor = 0.5
        s.iterations = smooth
        fb.apply_all(obj)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris > target_tris:
        d = obj.modifiers.new('decimate', 'DECIMATE')
        d.ratio = target_tris / tris
        fb.apply_all(obj)
    fb._finish(obj, mat)
    return obj


def carve(obj, cutter):
    m = obj.modifiers.new('carve', 'BOOLEAN')
    m.operation = 'DIFFERENCE'
    m.solver = 'EXACT'
    m.object = cutter
    fb.apply_all(obj)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return obj


def rod(center, direction, radius, length, verts=40):
    """Cilindro (cortador) com centro, direcção e raio dados."""
    d = Vector(direction).normalized()
    o = fb.cylinder(radius, length, tuple(center), 'ivory', verts=verts)
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d)
    fb.apply_all(o)
    return o


CUFF_PROFILE = ((0.0478, 0.018), (0.0472, 0.011), (0.046, 0.004), (0.0444, -0.004), (0.0425, -0.012), (0.04, -0.02))
WRIST_MASS = ((0.002, 0.0, -0.021), (0.035, 0.041, 0.024))


def cuff_solid(matrix=None, scale=1.0):
    """Punho da luva em sino (mais largo em cima, a fechar para a mão): entra no remesh e funde-se com o pulso."""
    bell = lathe([(r * scale, z * scale) for r, z in CUFF_PROFILE], 'ivory', 40, 'cuff', closed=True)
    fb.apply_all(bell)
    if matrix is not None:
        bell.data.transform(matrix)
    return bell


def cuff_rim(parts, matrix=None, scale=1.0):
    """Borda enrolada do punho; tapa a aresta de cima (a manga entra no meio)."""
    rim = fb.torus(0.0466 * scale, 0.0064 * scale, (0, 0, 0.0186 * scale), 'ivory', major_seg=40, minor_seg=12)
    fb.apply_all(rim)
    if matrix is not None:
        rim.data.transform(matrix)
    parts.append(rim)


def bend_chain(start, angles_deg, lengths):
    """Cadeia de pontos: começa em start a descer (−Z) e dobra para −X (palma da mão direita)."""
    pts = [Vector(start)]
    for a, L in zip(angles_deg, lengths):
        r = math.radians(a)
        pts.append(pts[-1] + Vector((-math.sin(r), 0.0, -math.cos(r))) * L)
    return pts


# ------------------------------------------------------------------------------------------ mão aberta

FINGERS = (  # (y do nó, comprimento relativo, raio, abertura em y na ponta)
    (0.0295, 0.95, 0.0139, 0.004),
    (0.0098, 1.0, 0.0143, 0.001),
    (-0.0098, 0.94, 0.0137, -0.002),
    (-0.0285, 0.78, 0.0124, -0.006),
)


def glove_open():
    parts = [
        ellipsoid((0.004, 0.0, -0.058), (0.025, 0.044, 0.036)),
        ellipsoid(*WRIST_MASS),
        cuff_solid(),
    ]
    for y, L, r, spread in FINGERS:
        pts = bend_chain((0.006, y, -0.086), (14, 32, 50), (0.024 * L, 0.019 * L, 0.014 * L))
        for i, p in enumerate(pts):
            p.y += spread * i / (len(pts) - 1)
        parts += tube(pts, (r, r * 0.97, r * 0.94, r * 0.92))
    parts += tube([Vector((-0.006, 0.026, -0.036)), Vector((-0.013, 0.048, -0.057)), Vector((-0.021, 0.056, -0.078))],
                  (0.0172, 0.015, 0.0134))
    hand = sculpt('GloveHandOpen', parts, target_tris=3000, scale=S)
    out = [hand]
    # Costuras nas costas da mão (luva de desenho animado).
    for y in (-0.016, 0.0, 0.016):
        seam = tube([P(0.0268, y, -0.047), P(0.0278, y, -0.064), P(0.0268, y, -0.08)],
                    (0.0017 * S,) * 3, 'ivoryDark', caps=False)
        seam[0].data.bevel_resolution = 1
        seam[0].data.resolution_u = 4
        out += seam
    cuff_rim(out, scale=S)
    return fb.join('GloveR_Open', out)


# ------------------------------------------------------------------------------------------ punho fechado

FIST_FINGERS = (  # (y, raio) em unidades de luva
    (0.036, 0.0128),
    (0.012, 0.0133),
    (-0.012, 0.0127),
    (-0.035, 0.0115),
)
FIST_ANGLES = (32, 0, -35, -70, -105, -140, -172, -202)


def glove_fist():
    """
    Dedos enrolados à volta do canal (nós nas costas da mão, em +X), por baixo e a subir pelo lado da palma,
    com as pontas a tocar na palma; o polegar fecha por cima do indicador, à frente. Tudo escala por S menos o
    canal, que continua à medida do punho do taco.
    """
    parts = [
        ellipsoid(P(0.006, 0.0, -0.052), Vector((0.027, 0.046, 0.03)) * S),
        ellipsoid(P(*WRIST_MASS[0]), Vector(WRIST_MASS[1]) * S),
        cuff_solid(scale=S),
    ]
    for y, r0 in FIST_FINGERS:
        r = r0 * S
        R = CHANNEL_R + r + 0.0006
        pts, radii = [], []
        for i, a in enumerate(FIST_ANGLES):
            t = i / (len(FIST_ANGLES) - 1)
            rr = R + (0.003 * S if i == 0 else 0.0)
            ar = math.radians(a)
            pts.append(Vector((CHANNEL.x + rr * math.cos(ar), y * S, CHANNEL.z + rr * math.sin(ar))))
            radii.append(r * (1.0 - 0.1 * t))
        parts += tube(pts, radii)
    parts += tube([P(-0.012, 0.036, -0.044), P(-0.032, 0.05, -0.066), P(-0.036, 0.047, -0.091)],
                  (0.0165 * S, 0.0142 * S, 0.0128 * S))
    hand = sculpt('GloveHandFist', parts, target_tris=3400)
    carve(hand, rod(CHANNEL, (0, 1, 0), CHANNEL_R, 0.4, verts=48))
    out = [hand]
    cuff_rim(out, scale=S)
    return fb.join('GloveR_Fist', out)


# ------------------------------------------------------------------------------------------ ponte (mão esquerda)

def bridge_matrix(wrist=BRIDGE_WRIST):
    """Canónico (esquerda) → frame T: dedos (−Z) ao longo do antebraço, palma (+X) para baixo."""
    f = BRIDGE_FOREARM
    zc = -f
    p = Vector((0.0, 0.0, -1.0))
    xc = (p - f * p.dot(f)).normalized()
    yc = zc.cross(xc)
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2] = xc[i], yc[i], zc[i]
        m[i][3] = wrist[i]
    return m


def cue_axis(tilt_deg):
    """Direcção do taco no frame T para uma inclinação relativa (a descer para +Y quando positiva)."""
    t = math.radians(tilt_deg)
    return Vector((0.0, math.cos(t), -math.sin(t)))


def glove_bridge_left():
    """
    Mão esquerda espalmada no pano (frame T): palma e dedos em leque à esquerda da linha do taco, nó do indicador
    levantado; o polegar corre por baixo do taco, da base (junto ao pulso) até à ponta, à direita da linha: o taco
    fica no "V" entre o nó do indicador e o polegar. O pulso fica atrás, à esquerda e levantado, com o punho
    virado para o antebraço. Modelada em unidades de luva e escalada por S à volta de G.
    """
    W = BRIDGE_WRIST0
    f = BRIDGE_FOREARM
    h = BRIDGE_H0
    m0 = bridge_matrix(W)
    wrist = ellipsoid(*WRIST_MASS)
    fb.apply_all(wrist)
    wrist.data.transform(m0)
    knuckles = Vector((-0.045, -0.012, -0.02))
    parts = [
        wrist,
        cuff_solid(m0),
        # Palma deitada no pano.
        ellipsoid((-0.05, -0.038, -h + 0.016), (0.035, 0.043, 0.016)),
        # Dorso largo (do pulso aos nós), inclinado a descer para os dedos.
        ellipsoid_axes((W + knuckles) * 0.5 + Vector((0.0, 0.0, -0.004)), (1.0, -0.3, 0.0), knuckles - W, (0.041, 0.047, 0.017)),
        # Almofada do polegar (tenar), do lado de dentro do pulso.
        ellipsoid((-0.03, -0.068, -0.018), (0.02, 0.024, 0.016)),
    ]
    # Dedos em leque: (nó, meio, ponta) e raios. O nó do indicador e o polegar ficam um pouco mais perto do eixo
    # do que o raio pede: ao escalar por S a folga para o taco (que não escala) fica ~1,5 mm.
    fingers = (
        ((-0.0128, -0.004, -0.0152), (-0.021, 0.024, -0.024), (-0.027, 0.048, -h + 0.0118), (0.0135, 0.0125, 0.0118)),
        ((-0.037, -0.008, -0.020), (-0.045, 0.020, -0.027), (-0.053, 0.046, -h + 0.0115), (0.013, 0.0122, 0.0115)),
        ((-0.058, -0.015, -0.022), (-0.068, 0.010, -0.028), (-0.078, 0.032, -h + 0.011), (0.0125, 0.0117, 0.011)),
        ((-0.076, -0.026, -0.025), (-0.087, -0.006, -0.030), (-0.096, 0.012, -h + 0.0102), (0.0115, 0.0108, 0.0102)),
    )
    for k, mid, tip, radii in fingers:
        parts += tube([Vector(k), Vector(mid), Vector(tip)], radii)
    # Polegar: da base junto ao pulso, por baixo e à direita do taco, até à ponta levantada ao lado do taco.
    parts += tube([Vector((-0.036, -0.072, -0.012)), Vector((-0.004, -0.051, -0.025)), Vector((0.0108, -0.018, -0.0165)),
                   Vector((0.0165, 0.018, -0.0108))], (0.017, 0.0135, 0.0126, 0.0122))
    hand = sculpt('GloveHandBridge', parts, target_tris=3400, scale=S)
    pre = clearance_to_cue(hand)
    # Sulco do taco: tira só o que toca no volume do taco nas inclinações suportadas.
    for tilt in BRIDGE_TILTS:
        carve(hand, rod((0, 0, 0), cue_axis(tilt), CUE_SHAFT_R + 0.0012, 0.6, verts=40))
    m = bridge_matrix()
    out = [hand]
    cuff_rim(out, m, scale=S)
    obj = fb.join('GloveL_Bridge', out)
    post = clearance_to_cue(obj)
    # Do frame T para o frame canónico da mão esquerda.
    obj.data.transform(m.inverted())
    return obj, pre, post


# ------------------------------------------------------------------------------------------ validação

def clearance_to_cue(obj, tilts=BRIDGE_TILTS):
    """Folga mínima (m) entre a superfície do taco (cada inclinação) e os vértices da luva, em frame T."""
    out = {}
    for tilt in tilts:
        d = cue_axis(tilt)
        best = 1e9
        for v in obj.data.vertices:
            w = v.co
            best = min(best, (w - d * w.dot(d)).length)
        out[tilt] = best - CUE_SHAFT_R
    return out


def channel_clearance(obj):
    """Folga mínima entre o punho do taco (raio CUE_BUTT_R, eixo do canal) e a luva (frame canónico)."""
    best = 1e9
    for v in obj.data.vertices:
        w = v.co - CHANNEL
        if abs(w.y) > 0.08:
            continue
        best = min(best, math.hypot(w.x, w.z))
    return best - CUE_BUTT_R


def channel_wrap(obj, samples=72):
    """Fracção do contorno à volta do canal (a meio do punho) tapada pela luva até 3,5 cm do eixo."""
    covered = 0
    for i in range(samples):
        a = 2 * math.pi * i / samples
        dx, dz = math.cos(a), math.sin(a)
        hit = False
        for v in obj.data.vertices:
            w = v.co - CHANNEL
            if abs(w.y) > 0.014:
                continue
            r = math.hypot(w.x, w.z)
            if 1e-6 < r < 0.035 and (w.x * dx + w.z * dz) / r > math.cos(math.radians(360 / samples)):
                hit = True
                break
        covered += hit
    return covered / samples


def unity_vec(v):
    return (v[0], v[2], v[1])


def unity_quat(m3):
    """Rotação (matriz 3x3 Blender) → quaternião Unity (x, y, z, w)."""
    s = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))
    q = (s @ m3 @ s).to_quaternion()
    return (q.x, q.y, q.z, q.w)
