"""Refine the shared costume against the CURRENT Unity rig, preserving hand geometry and cap.

blender -b --factory-startup -P Blender/character_refine.py
Outputs: Blender/refined/character.bytes, characters.blend, preview images, validation.json.
Copy the bytes into Resources/Models/FS only after reviewing the output.
The experimental character.py v2 remains independent of this compatible revision.
"""
import json
import math
import os
from pathlib import Path
import struct
import sys
import hashlib

import bpy
from mathutils import Vector, Euler

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
import fsblend as fb

BASE = ROOT / 'revisions/2026-10-27-before-character-detail/character.bytes'
OUT = ROOT / 'character-detail-v2'
OUT.mkdir(exist_ok=True)
fb.reset()


def read_parts(data):
    assert data[:4] == b'FSM1'
    offset = 4
    def integer():
        nonlocal offset
        n = struct.unpack_from('<i', data, offset)[0]
        offset += 4
        return n
    def string():
        nonlocal offset
        n = integer()
        s = data[offset:offset+n].decode()
        offset += n
        return s
    result = {}
    for _ in range(integer()):
        start = offset
        name = string()
        mats = [string() for _ in range(integer())]
        n = integer()
        values = struct.unpack_from('<%df' % (n * 8), data, offset)
        offset += n * 32
        subs = []
        for _ in mats:
            ni = integer()
            subs.append(struct.unpack_from('<%di' % ni, data, offset))
            offset += ni * 4
        result[name] = dict(raw=data[start:offset], mats=mats, values=values, count=n, subs=subs)
    assert offset == len(data)
    return result


def import_piece(name, part):
    n = part['count']
    v = part['values']
    verts = [(v[i*3], v[i*3+2], v[i*3+1]) for i in range(n)]
    faces, slots = [], []
    for m, indices in enumerate(part['subs']):
        for i in range(0, len(indices), 3):
            faces.append((indices[i], indices[i+2], indices[i+1]))
            slots.append(m)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for mat in part['mats']:
        mesh.materials.append(fb.material(mat))
    for poly, slot in zip(mesh.polygons, slots):
        poly.material_index = slot
        poly.use_smooth = True
    normals = [(v[n*3+i*3], v[n*3+i*3+2], v[n*3+i*3+1]) for i in range(n)]
    mesh.normals_split_custom_set_from_vertices(normals)
    return obj


def signed_power(x, p):
    return math.copysign(abs(x)**p, x)


def loft(name, profile, mat, exponent=2.6, y_center=0, segments=40):
    """Rounded cross sections with explicitly controlled silhouette and closed ends."""
    verts, faces = [], []
    for z, rx, ry in profile:
        for i in range(segments):
            a = i * math.tau / segments
            verts.append((rx*signed_power(math.cos(a), 2/exponent),
                          y_center+ry*signed_power(math.sin(a), 2/exponent), z))
    faces.append(tuple(reversed(range(segments))))
    for r in range(len(profile)-1):
        for i in range(segments):
            j = (i+1) % segments
            faces.append((r*segments+i, r*segments+j, (r+1)*segments+j, (r+1)*segments+i))
    faces.append(tuple((len(profile)-1)*segments+i for i in range(segments)))
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
    spline.points.add(len(points)-1)
    for p, co in zip(spline.points, points):
        p.co = (*co, 1)
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(fb.material(mat))
    return obj


BODY_PROFILE = [(-0.024, .126, .090), (-.016, .145, .105),
                (.008, .160, .115), (.065, .169, .122),
                (.14, .166, .119), (.22, .155, .111),
                (.29, .148, .105), (.335, .151, .103),
                (.365, .138, .094), (.385, .110, .078), (.397, .075, .057)]


def front(x, z):
    for (z0, rx0, ry0), (z1, rx1, ry1) in zip(BODY_PROFILE, BODY_PROFILE[1:]):
        if z <= z1:
            t = max(0, (z-z0)/(z1-z0))
            rx, ry = rx0+(rx1-rx0)*t, ry0+(ry1-ry0)*t
            return ry*max(0, 1-(abs(x)/rx)**2.6)**(1/2.6)
    return .025


def strap(side):
    # Continuous sampled ribbon: front, over the shoulder, down the back.
    path = []
    for i in range(25):
        z = .059 + .301*i/24
        path.append((side*(.087-.014*i/24), z, 1))
    verts = []
    for x, z, sign in path:
        for dx in (-.020, .020):
            verts.append((x+dx, front(x+dx, z)+.005, z))
    end = verts[-2:]
    for i in range(1, 17):
        t = i/16
        for x, y, z in end:
            verts.append((x, y*math.cos(math.pi*t), z+.033*math.sin(math.pi*t)))
    for i in range(1, 25):
        z = .36-.301*i/24
        x = side*(.073-.024*i/24)
        for dx in (-.020, .020):
            verts.append((x+dx, -front(x+dx, z)-.005, z))
    faces = [(2*i, 2*i+1, 2*i+3, 2*i+2) for i in range(len(verts)//2-1)]
    mesh = bpy.data.meshes.new('HarnessRibbon')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new('HarnessRibbon', mesh)
    bpy.context.collection.objects.link(obj)
    fb._finish(obj, 'ivory')
    solid = obj.modifiers.new('Woven strap thickness', 'SOLIDIFY')
    solid.thickness = .004
    solid.offset = 0
    fb.bevel(obj, .0015, 2, limit='ANGLE', angle=30)
    return [obj]


def torso():
    parts = [loft('TailoredSuit', BODY_PROFILE, 'suit')]
    for side in (-1, 1):
        parts += strap(side)
        x, z = side*.077, .307
        y = front(x,z)+.013
        parts += [fb.box((.048,.013,.049),(x,y,z),'suitDark',.008,3),
                  fb.cylinder(.017,.011,(x,y+.008,z),'brass',rot=(90,0,0),verts=24,bevel_w=.003),
                  fb.cylinder(.008,.012,(x,y+.014,z),'dark',rot=(90,0,0),verts=20)]
        # Fine seam running along the outer edge; modest enough for game scale.
        pts = []
        for i in range(23):
            zz = .073+.275*i/22
            xx = side*(.103-.014*i/22)
            pts.append((xx,front(xx,zz)+.008,zz))
        parts.append(pipe('HarnessEdge',pts,.0012,'ivoryDark'))
        # Waist loops and soft, closed pouch (kept inside the existing torso envelope).
        parts.append(fb.box((.019,.018,.056),(side*.112,.116,.031),'suitDark',.005,3))
    belt = loft('Belt',[(-.004,.151,.108),(.002,.167,.124),(.039,.170,.125),(.045,.160,.118)],'leather',3)
    parts.append(belt)
    parts += [fb.box((.064,.015,.049),(0,.132,.021),'brass',.008,3),
              fb.box((.038,.01,.026),(0,.142,.021),'suitDark',.004,3),
              fb.box((.006,.01,.029),(0,.15,.021),'brass',.002,2)]
    for side in (-1,1):
        parts += [fb.box((.055,.075,.085),(side*.169,-.024,.014),'suitDark',.015,4),
                  fb.box((.060,.078,.027),(side*.170,-.024,.054),'leather',.010,3),
                  fb.cylinder(.008,.006,(side*.201,-.018,.036),'brass',rot=(0,90,0),verts=16)]
    z, y = .205, front(0,.205)
    parts += [fb.box((.115,.02,.109),(0,y+.001,z),'suitDark',.020,4),
              fb.cylinder(.046,.014,(0,y+.017,z),'brass',rot=(90,0,0),verts=40,bevel_w=.003),
              fb.cylinder(.037,.014,(0,y+.024,z),'navy',rot=(90,0,0),verts=40),
              fb.prism([(-.019,-.018),(.004,-.018),(-.007,.007)],.008,'mustard',center=(0,y+.034,z)),
              fb.sphere(.011,(.004,y+.035,z+.014),'coral',scale=(1,.4,1),segments=20,rings=12),
              fb.box((.018,.008,.020),(.017,y+.034,z-.009),'teal',.003,3)]
    # Small tailored pocket panels with raised welts, kept within the torso silhouette.
    for side in (-1, 1):
        x,z=side*.125,.145
        y=front(x,z)
        parts.append(fb.box((.035,.009,.047),(x,y+.006,z),'suitDark',.006,3))
        parts.append(fb.box((.033,.008,.008),(x,y+.012,z+.018),'suit',.003,2))
        parts.append(fb.sphere(.0035,(x,y+.017,z+.018),'brass',segments=12,rings=8))
    # Collar piping and a small back yoke replace the jagged shrinkwrapped neckline.
    parts.append(loft('NeckBinding',[(.377,.080,.062),(.388,.086,.066),(.401,.078,.060)],'ivory'))
    parts.append(fb.box((.079,.012,.027),(0,-.11,.271),'leather',.007,3))
    return fb.join('Torso',parts)


def pelvis():
    return fb.join('Pelvis',[loft('TrouserSeat',[(-.090,.092,.071),(-.083,.126,.094),
        (-.052,.153,.110),(-.008,.155,.112),(.029,.144,.102)],'suit',2.6)])


def thigh():
    return fb.join('Thigh',[fb.capsule(.066,.058,.152,(0,0,-.008),'suit',verts=28)])


def shin():
    parts = [fb.capsule(.057,.052,.116,(0,0,0),'suit',verts=28),
             fb.sphere(.049,(0,.035,-.020),'suitDark',scale=(.90,.48,1),segments=24,rings=14),
             fb.sphere(.038,(0,.051,-.022),'ivory',scale=(1,.32,1.06),segments=24,rings=14)]
    return fb.join('Shin',parts)


def boot():
    parts = [loft('RubberOutsole',[(-.103,.063,.113),(-.100,.077,.133),(-.082,.080,.137),(-.074,.076,.132)],'tint',3.0,.038),
             loft('IvoryWelt',[(-.077,.077,.133),(-.067,.078,.132),(-.059,.073,.127)],'ivory',3.0,.038),
             loft('LeatherUpper',[(-.063,.067,.121),(-.049,.076,.124),(-.017,.074,.119),
                 (.011,.066,.101),(.033,.056,.074),(.054,.048,.056)],'suitDark',2.5,.03)]
    # A gently curved toe-cap seam gives the boots a finished, stitched construction.
    toe=[]
    for i in range(17):
        x=-.060+.120*i/16
        toe.append((x,.139-.024*(abs(x)/.060)**2,-.028-.008*(abs(x)/.060)**2))
    parts.append(pipe('ToeCapSeam',toe,.0017,'ivoryDark'))
    for side in (-1,1):
        parts.append(fb.sphere(.0035,(side*.058,.116,-.035),'brass',segments=12,rings=8))
    # Small colored toe bumper; dark rounded toe remains visible like the concept.
    parts.append(fb.box((.089,.025,.022),(0,.165,-.066),'tint',.010,4))
    parts.append(fb.box((.067,.082,.018),(0,.049,.041),'suit',.008,3,rot=(-21,0,0)))
    for i in range(3):
        parts.append(fb.box((.062,.009,.009),(0,.035+i*.022,.054-i*.008),'ivory',.004,3,rot=(-21,0,0)))
    parts += [fb.torus(.052,.012,(0,0,.059),'ivory',major_seg=32,minor_seg=10),
              fb.box((.111,.040,.030),(0,.050,.056),'tint',.012,4),
              fb.box((.024,.019,.050),(0,-.061,.039),'tint',.007,3)]
    for side in (-1,1):
        parts.append(fb.cylinder(.011,.005,(side*.070,.036,-.007),'brass',rot=(0,90,0),verts=20,bevel_w=.002))
    return fb.join('Boot',parts)


def upper_arm():
    parts = [fb.capsule(.066,.054,.102,(0,0,-.015),'suit',verts=28),
             fb.cylinder(.058,.032,(0,0,-.113),'suitDark',verts=32,bevel_w=.008),
             fb.torus(.056,.005,(0,0,-.119),'tint',major_seg=32,minor_seg=8)]
    # An enamel-like sleeve patch and fine cuff piping are shared by both arms.
    parts.append(fb.sphere(.023,(0,.058,-.048),'suitDark',scale=(1,.20,1),segments=20,rings=12))
    parts.append(fb.sphere(.014,(0,.062,-.048),'brass',scale=(1,.18,1),segments=20,rings=12))
    parts.append(fb.sphere(.006,(0,.065,-.048),'tint',scale=(1,.20,1),segments=16,rings=10))
    parts.append(fb.torus(.055,.002,(0,0,-.096),'ivoryDark',major_seg=32,minor_seg=8))
    return fb.join('UpperArm',parts)


def forearm():
    return fb.join('Forearm',[fb.capsule(.051,.045,.110,(0,0,0),'suit',verts=28),
        fb.cylinder(.050,.025,(0,0,-.131),'suitDark',verts=32,bevel_w=.007)])


base = read_parts(BASE.read_bytes())
replacements = [torso(),pelvis(),thigh(),shin(),boot(),upper_arm(),forearm()]
fb.export_bytes(str(OUT/'body-parts.bytes'),replacements)
new = read_parts((OUT/'body-parts.bytes').read_bytes())
changed = {"Torso", "Boot", "UpperArm"}
merged = {name:new[name] if name in changed else part for name,part in base.items()}
left = base['GloveL']
def volume(part):
    verts=part['values']
    total=0
    for indices in part['subs']:
        for t in range(0,len(indices),3):
            a,b,c=[Vector(verts[i*3:i*3+3]) for i in indices[t:t+3]]
            total+=a.dot(b.cross(c))/6
    return total

assert merged['GloveL']['raw'] == left['raw']
# Regression proof at the responsible helper: a newly mirrored mesh stays outward.
test_right=fb.box((.1,.2,.3))
test_left=fb.mirror_x(test_right,'MirrorRegression')
import bmesh
bm=bmesh.new()
bm.from_mesh(test_left.data)
assert bm.calc_volume(signed=True)>0
bm.free()
for obj in (test_left,test_right): bpy.data.objects.remove(obj,do_unlink=True)
data = b'FSM1'+struct.pack('<i',len(merged))+b''.join(p['raw'] for p in merged.values())
(OUT/'character.bytes').write_bytes(data)
report = {'baseline_sha256':hashlib.sha256(BASE.read_bytes()).hexdigest(),
          'output_sha256':hashlib.sha256(data).hexdigest(),
          'preserved_exactly':[name for name in base if merged[name]['raw']==base[name]['raw']],
          'replaced':sorted(changed),'vertices':{},'triangles':{},
          'left_glove_positions_unchanged':True,
          'left_glove_signed_volume_before':volume(left),
          'left_glove_signed_volume_after':volume(merged['GloveL']),
          'mirror_helper_outward':True}
for name,p in merged.items():
    assert all(math.isfinite(v) for v in p['values'])
    assert all(0<=i<p['count'] for sub in p['subs'] for i in sub)
    report['vertices'][name] = p['count']
    report['triangles'][name] = sum(len(s)//3 for s in p['subs'])
assert set(report['preserved_exactly']) == set(base) - changed
(OUT/'validation.json').write_text(json.dumps(report,indent=2))

# Editable pieces in a hidden source collection. Preview instances use the actual Unity pivots.
sources = bpy.data.collections.new('SOURCE - joint-local meshes')
bpy.context.scene.collection.children.link(sources)
objects = {o.name:o for o in replacements}
for name,p in merged.items():
    if name not in objects:
        objects[name] = import_piece(name,p)
for obj in objects.values():
    for collection in list(obj.users_collection): collection.objects.unlink(obj)
    sources.objects.link(obj)
sources.hide_render = True
sources.hide_viewport = True


def place(name,loc,rot=(0,0,0),tint='teal'):
    src = objects[name]
    obj = src.copy()
    obj.data = src.data.copy()
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = [math.radians(a) for a in rot]
    for i,mat in enumerate(obj.data.materials):
        if mat.name=='tint': obj.data.materials[i]=fb.material(tint)
    return obj


def figure(x,shape,tint):
    offset = Vector((x,0,0))
    hips = offset+Vector((0,0,.5))
    spine = hips+Vector((0,0,.04))
    place('Pelvis',hips,tint=tint)
    place('Torso',spine,tint=tint)
    for side in (-1,1):
        th = hips+Vector((side*.078,0,-.05))
        kn = th+Vector((0,0,-.19))
        an = kn+Vector((0,0,-.17))
        for name,at in [('Thigh',th),('Shin',kn),('Boot',an)]: place(name,at,tint=tint)
        sh = spine+Vector((side*.2,0,.34))
        rot = (0,-side*9,0)
        r = Euler([math.radians(a) for a in rot]).to_matrix()
        el = sh+r@Vector((0,0,-.15))
        wr = el+r@Vector((0,0,-.16))
        for name,at in [('UpperArm',sh),('Forearm',el),('GloveR' if side>0 else 'GloveL',wr)]: place(name,at,rot,tint)
    neck = spine+Vector((0,0,.4))
    place('Collar',neck,tint=tint)
    head = neck+Vector((0,0,.03))
    # Native Unity heads, eye normals and HatFit results, archived by CharacterReview.
    # Avoid approximations here: the .blend should show the game's actual three silhouettes.
    archive=json.loads((ROOT/'revisions/2026-09-26-before-refinement/head-meshes.json').read_text())
    for part in archive['parts']:
        if part['archetype']!=shape: continue
        verts=[(v['x'],v['z'],v['y']) for v in part['vertices']]
        normals=[(v['x'],v['z'],v['y']) for v in part['normals']]
        faces,slots=[],[]
        for slot,surface in enumerate(part['surfaces']):
            idx=surface['triangles']
            for i in range(0,len(idx),3):
                faces.append((idx[i],idx[i+2],idx[i+1]))
                slots.append(slot)
        mesh=bpy.data.meshes.new(shape+'_'+part['name'])
        mesh.from_pydata(verts,[],faces)
        mesh.update()
        obj=bpy.data.objects.new(mesh.name,mesh)
        bpy.context.collection.objects.link(obj)
        obj.location=head
        for slot,surface in enumerate(part['surfaces']):
            c=surface['color']
            mat=bpy.data.materials.new(shape+'_'+part['name']+'_'+str(slot))
            mat.diffuse_color=(c['r'],c['g'],c['b'],1)
            mesh.materials.append(mat)
        for poly,slot in zip(mesh.polygons,slots):
            poly.material_index=slot
            poly.use_smooth=True
        mesh.normals_split_custom_set_from_vertices(normals)


variants={}
for x,shape,tint in [(.88,'triangle','mustard'),(0,'circle','coral'),(-.88,'square','teal')]:
    existing=set(bpy.context.scene.objects)
    figure(x,shape,tint)
    group=bpy.data.collections.new(shape.upper()+' - assembled')
    bpy.context.scene.collection.children.link(group)
    variants[shape]=group
    for obj in set(bpy.context.scene.objects)-existing:
        for collection in list(obj.users_collection): collection.objects.unlink(obj)
        group.objects.link(obj)
scene=fb.setup_preview()
scene.render.engine='CYCLES'
scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.65,.72,.82,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.35
scene.view_settings.view_transform='AgX'
floor=fb.box((200,200,.04),(0,0,-.034),'stage')
fb.material('stage').diffuse_color=(.79,.75,.67,1)
def linear(c): return c/12.92 if c<.04045 else ((c+.055)/1.055)**2.4
for mat in bpy.data.materials:
    mat.use_nodes=True
    node=mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value=(*[linear(c) for c in mat.diffuse_color[:3]],1)
    node.inputs['Roughness'].default_value=.38 if 'HeadShape' in mat.name else .56
    if 'Eye' in mat.name: node.inputs['Roughness'].default_value=.20
    if mat.name=='brass':
        node.inputs['Metallic'].default_value=.55
        node.inputs['Roughness'].default_value=.30
for name,loc,energy,size in [('Key',(-3,4,5),650,4),('Fill',(3,2,3),400,3),('Rim',(0,-3,4),700,3)]:
    data_light=bpy.data.lights.new(name,'AREA')
    data_light.energy=energy
    data_light.shape='DISK'
    data_light.size=size
    light=bpy.data.objects.new(name,data_light)
    bpy.context.collection.objects.link(light)
    light.location=loc
    light.rotation_euler=(Vector((0,0,.7))-light.location).to_track_quat('-Z','Y').to_euler()
fb.render(str(OUT/'lineup.png'),(0,0,.78),5.5,0,8,ortho=3.4,res=(1700,1000))
variants['circle'].hide_render=True
variants['triangle'].hide_render=True
fb.render(str(OUT/'square-detail.png'),(-.88,0,.75),3.6,28,9,ortho=1.8,res=(1000,1000))
variants['circle'].hide_render=False
variants['triangle'].hide_render=False
fb.render(str(OUT/'back.png'),(0,0,.78),5.5,180,8,ortho=3.4,res=(1700,1000))
# A usable camera remains in the .blend for direct review/editing.
camera_data=bpy.data.cameras.new('LineupCamera')
camera_data.type='ORTHO'
camera_data.ortho_scale=3.4
camera=bpy.data.objects.new('LineupCamera',camera_data)
bpy.context.collection.objects.link(camera)
camera.location=(0,5,.95)
camera.rotation_euler=(Vector((0,0,.78))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.camera=camera
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'characters.blend'))
print('REFINEMENT VALIDATED',json.dumps(report))



