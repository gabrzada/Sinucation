"""Shared arena props, in metres; Blender Z up -> Unity Y up via fsblend.
Run: blender -b --factory-startup -P Blender/arena_props.py
Pendant origin is the lower rim; the other models stand at z=0, height=1.
"""
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT))
import fsblend as fb
OUT=ROOT/'arena-refined'
OUT.mkdir(exist_ok=True)
fb.reset()


def lathe(name, profile, mat, closed=False, segments=48):
    verts=[]
    for r,z in profile:
        for i in range(segments):
            a=math.tau*i/segments
            verts.append((r*math.cos(a),r*math.sin(a),z))
    faces=[]
    rings=len(profile)
    for j in range(rings if closed else rings-1):
        k=(j+1)%rings
        for i in range(segments):
            n=(i+1)%segments
            faces.append((j*segments+i,j*segments+n,k*segments+n,k*segments+i))
    if not closed:
        faces += [tuple(reversed(range(segments))),tuple((rings-1)*segments+i for i in range(segments))]
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(verts,[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    return fb._finish(obj,mat)


def tube(a,b,r,mat):
    a,b=Vector(a),Vector(b)
    obj=fb.cylinder(r,(b-a).length,(a+b)/2,mat,verts=16,bevel_w=.004)
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return obj


def emblem(y,z,size=1):
    p=[fb.cylinder(.083*size,.013,(0,y,z),'brass',rot=(90,0,0),verts=32,bevel_w=.003),
       fb.cylinder(.070*size,.013,(0,y+.009,z),'navy',rot=(90,0,0),verts=32)]
    p.append(fb.prism([(-.041*size,-.026*size),(-.003*size,-.026*size),(-.022*size,.012*size)],.009,'mustard',center=(0,y+.021,z)))
    p.append(fb.sphere(.019*size,(.006*size,y+.022,z+.024*size),'coral',scale=(1,.35,1),segments=16,rings=10))
    p.append(fb.box((.031*size,.010,.033*size),(.033*size,y+.021,z-.016*size),'teal',.005,3))
    return p


def pendant():
    p=[lathe('EnamelShade',[(.49,0),(.502,.019),(.486,.056),(.446,.135),(.375,.23),(.28,.33),(.16,.411),(.09,.43),
        (.073,.409),(.143,.391),(.26,.31),(.353,.21),(.424,.12),(.464,.040),(.472,.007)],'mustard',True),
       fb.torus(.49,.014,(0,0,.012),'navy',major_seg=48,minor_seg=8),
       fb.cylinder(.462,.011,(0,0,.004),'glow',verts=48),
       fb.cylinder(.085,.12,(0,0,.467),'navy',verts=32,bevel_w=.012),
       fb.cylinder(.055,.085,(0,0,.551),'steel',verts=24,bevel_w=.006)]
    for z in (.437,.46,.483):
        p.append(fb.torus(.085,.008,(0,0,z),'dark',major_seg=24,minor_seg=8))
    for i in range(8):
        a=math.tau*i/8
        p.append(fb.sphere(.009,(.481*math.cos(a),.481*math.sin(a),.049),'steel',segments=12,rings=8))
    return fb.join('Pendant',p)


def barrel():
    p=[lathe('BarrelShell',[(.31,.024),(.333,.045),(.341,.10),(.35,.25),(.356,.5),(.35,.75),(.341,.9),(.333,.967),(.31,.981)],'navy'),
       fb.cylinder(.312,.02,(0,0,.973),'steel',verts=48,bevel_w=.005),
       fb.cylinder(.314,.017,(0,0,.03),'dark',verts=40)]
    for z in (.035,.964):
        p.append(fb.torus(.326,.021,(0,0,z),'mustard',major_seg=48,minor_seg=10))
    for z in (.21,.78):
        p.append(lathe('Hoop',[(.347,z-.023),(.367,z-.014),(.370,z),(.367,z+.014),(.347,z+.023)],'mustard'))
    p += [fb.torus(.286,.006,(0,0,.986),'dark',major_seg=48,minor_seg=6),
          fb.cylinder(.047,.018,(.13,.09,.984),'brass',verts=6,bevel_w=.004),
          fb.cylinder(.025,.010,(.13,.09,.996),'dark',verts=16),
          fb.cylinder(.028,.012,(-.15,-.09,.987),'steel',verts=6,bevel_w=.003)]
    p += emblem(.352,.49,1.1)
    return fb.join('Barrel',p)


def crate():
    p=[fb.box((.88,.75,.90),(0,0,.5),'wood',.022,3)]
    for side in (-1,1):
        for i in range(5):
            p.append(fb.box((.160,.027,.76),(-.344+i*.172,side*.38,.50),'wood',.008,2))
        for z in (.085,.915):
            p.append(fb.box((.93,.083,.080),(0,side*.369,z),'navy',.017,3))
        for x in (-.409,.409):
            p.append(fb.box((.077,.080,.82),(x,side*.371,.5),'mustard',.013,3))
            for z in (.16,.84):
                p.append(fb.cylinder(.017,.011,(x,side*.417,z),'steel',rot=(90,0,0),verts=8,bevel_w=.003))
        p.append(fb.box((.026,.28,.1),(side*.451,0,.68),'navy',.026,3))
        p.append(fb.box((.031,.18,.030),(side*.459,0,.686),'dark',.012,3))
        p.append(fb.box((.080,.8,.035),(side*.30,0,.958),'mustard',.009,3))
        for y in (-.29,.29):
            p.append(fb.box((.16,.15,.075),(side*.33,y,.0375),'dark',.015,3))
    p += [fb.box((.94,.79,.045),(0,0,.975),'navy',.013,3)]
    p += emblem(.414,.5,1.22)
    return fb.join('Crate',p)


def stool():
    p=[fb.cylinder(.305,.055,(0,0,.878),'navy',verts=40,bevel_w=.013),
       fb.sphere(.33,(0,0,.934),'coral',scale=(1,1,.20),segments=40,rings=18),
       fb.torus(.316,.009,(0,0,.932),'leather',major_seg=40,minor_seg=8),
       fb.torus(.265,.018,(0,0,.285),'steel',major_seg=40,minor_seg=8)]
    for i in range(4):
        a=math.pi*.25+math.tau*i/4
        lower=(.282*math.cos(a),.282*math.sin(a),.04)
        upper=(.185*math.cos(a),.185*math.sin(a),.86)
        p.append(tube(lower,upper,.024,'navy'))
        p.append(fb.cylinder(.034,.056,(lower[0],lower[1],.028),'dark',verts=16,bevel_w=.008))
        p.append(tube((.21*math.cos(a),.21*math.sin(a),.69),(0,0,.82),.015,'steel'))
        p.append(fb.cylinder(.011,.008,(.30*math.cos(a),.30*math.sin(a),.90),'brass',verts=12))
    return fb.join('Stool',p)


models=[pendant(),barrel(),crate(),stool()]
fb.export_bytes(str(OUT/'arena.bytes'),models,box_uv=True)
import bmesh,json
report={}
for model in models:
    mesh=model.data
    mesh.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(mesh)
    bounds=[Vector(corner) for corner in model.bound_box]
    report[model.name]={'vertices':len(mesh.vertices),'triangles':len(mesh.loop_triangles),
        'signed_volume':bm.calc_volume(signed=True),'height':max(v.z for v in bounds)-min(v.z for v in bounds)}
    assert report[model.name]['signed_volume']>0
    bm.free()
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
# Assembly for direct editing and quick visual inspection.
models[0].location=(-1.15,0,1.32)
models[1].location=(-.65,0,0)
models[2].location=(.38,0,0)
models[3].location=(1.35,0,0)
fb.setup_preview()
fb.render(str(OUT/'assets.png'),(.1,0,.84),5.5,25,16,ortho=3.8,res=(1600,1050))
cam_data=bpy.data.cameras.new('AssetCamera');cam_data.type='ORTHO';cam_data.ortho_scale=3.8
camera=bpy.data.objects.new('AssetCamera',cam_data);bpy.context.collection.objects.link(camera)
camera.location=(2,5,2.4);camera.rotation_euler=(Vector((.1,0,.84))-camera.location).to_track_quat('-Z','Y').to_euler()
bpy.context.scene.camera=camera
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'arena-props.blend'))
print('ARENA_PROPS_VALIDATED',json.dumps(report))
