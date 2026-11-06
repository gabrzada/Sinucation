"""Throwables in metres, Blender Z up / Unity Y up. Stage export and previews before installing.
Run: blender -b --factory-startup -P Blender/throwables.py
"""
import math, sys, random, json
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'throwables-refined'; OUT.mkdir(exist_ok=True)
f.reset()
f.PREVIEW.update({'paper':(.94,.93,.86), 'duck':(1.0,.78,.08), 'duckShade':(.86,.60,.045), 'beak':(1.0,.32,.06)})

def paper():
    # Deterministic folded silhouette, small facets and no separate thin surfaces.
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=.044)
    obj=bpy.context.object
    for v in obj.data.vertices:
        p=v.co.normalized()
        k=1 + .065*math.sin(10*p.x+3*p.z)*math.sin(8*p.y-4*p.z) + .035*math.cos(15*p.z+5*p.x)
        v.co *= k
    f._finish(obj,'paper',smooth=False)
    return f.join('PaperBall',[obj])

def can():
    p=[f.cylinder(.034,.109,(0,0,0),'red',verts=32,bevel_w=.003),
       f.cylinder(.033,.007,(0,0,.057),'steel',verts=32,bevel_w=.002),
       f.cylinder(.033,.007,(0,0,-.057),'steel',verts=32,bevel_w=.002),
       f.torus(.030,.0018,(0,0,.061),'dark',major_seg=32,minor_seg=8),
       f.cylinder(.028,.002,(0,0,.061),'steel',verts=32),
       f.torus(.009,.0017,(0,0,.063),'dark',scale=(.65,1,1),major_seg=20,minor_seg=8),
       f.sphere(.002,(0,-.003,.064),'steel',segments=12,rings=8)]
    for z in (-.038,.038):
        p.append(f.torus(.034,.0015,(0,0,z),'ivory',major_seg=32,minor_seg=8))
    # Geometric mark, readable at game distance and matching the arena's visual language.
    p.append(f.prism([(-.010,-.008),(.010,-.008),(0,.009)],.002,'mustard',center=(0,-.035,0)))
    return f.join('Can',p)

def duck():
    p=[f.sphere(.5,(0,0,0),'duck',scale=(.12,.14,.09),segments=32,rings=20),
       f.sphere(.035,(0,.042,.058),'duck',segments=24,rings=16),
       f.sphere(.5,(0,.073,.052),'beak',scale=(.035,.026,.012),segments=20,rings=12)]
    for side in (-1,1):
        p.append(f.sphere(.5,(side*.054,-.008,.008),'duckShade',scale=(.016,.075,.04),segments=20,rings=12,rot=(0,side*12,0)))
        p.append(f.sphere(.0038,(side*.019,.069,.065),'dark',segments=12,rings=8))
        p.append(f.sphere(.0014,(side*.019,.072,.067),'ivory',segments=8,rings=6))
    p.append(f.sphere(.5,(0,-.058,.020),'duck',scale=(.048,.035,.036),segments=20,rings=12,rot=(18,0,0)))
    return f.join('Duck',p)

models=[paper(),can(),duck()]
f.export_bytes(str(OUT/'throwables.bytes'),models,box_uv=True)
report={}
for ob in models:
    ob.data.calc_loop_triangles()
    report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
for ob,x in zip(models,(-.16,0,.17)): ob.location.x=x
f.setup_preview()
f.render(str(OUT/'gallery.png'),(0,0,.015),.75,25,18,ortho=.52,res=(1400,800))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'throwables.blend'))
