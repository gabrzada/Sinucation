"""Tapered cue pieces for CueView. Unit rod local axis Blender Z / Unity Y,
range [-1,1], as for Unity's primitive cylinder; parent sets length and diameter.
Run: blender -b --factory-startup -P Blender/cue_props.py
"""
import math,sys,json
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'cue-refined';OUT.mkdir(exist_ok=True);f.reset()
f.PREVIEW.update({'shaft':(.86,.72,.48),'butt':(.15,.08,.05),'ivory':(.92,.87,.72),'brass':(.95,.7,.18),'grip':(.07,.04,.03)})

def spindle(name,profile,segments=32):
 verts=[(r*math.cos(math.tau*i/segments),r*math.sin(math.tau*i/segments),z) for z,r,mat in profile for i in range(segments)]
 faces=[];slots=[]
 mats=[]
 for _,_,m in profile:
  if m not in mats:mats.append(m)
 for j in range(len(profile)-1):
  for i in range(segments):
   k=(i+1)%segments;faces.append((j*segments+i,j*segments+k,(j+1)*segments+k,(j+1)*segments+i));slots.append(mats.index(profile[j][2]))
 faces.append(tuple(reversed(range(segments))));slots.append(mats.index(profile[0][2]))
 faces.append(tuple((len(profile)-1)*segments+i for i in range(segments)));slots.append(mats.index(profile[-1][2]))
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
 for mat in mats:mesh.materials.append(f.material(mat))
 for poly,slot in zip(mesh.polygons,slots):poly.material_index=slot;poly.use_smooth=len(poly.vertices)==4
 return obj
shaft=spindle('Shaft',[(-1,.50,'shaft'),(-.96,.50,'shaft'),(.93,.31,'shaft'),(1,.31,'shaft')])
butt=spindle('Butt',[(-1,.48,'ivory'),(-.96,.50,'ivory'),(-.94,.50,'brass'),(-.91,.495,'grip'),(-.70,.47,'grip'),(-.68,.47,'brass'),(-.65,.465,'butt'),(.78,.30,'butt'),(.82,.295,'ivory'),(.89,.285,'butt'),(1,.273,'butt')])
f.export_bytes(str(OUT/'cue.bytes'),[shaft,butt],box_uv=True)
report={}
for ob in [shaft,butt]:ob.data.calc_loop_triangles();report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
# Gallery: orient and scale pieces to depict a typical 1.1 m cue.
shaft.scale=(.012,.012,.4);shaft.location=(0,0,.7)
butt.scale=(.022,.022,.15);butt.location=(0,0,.15)
f.setup_preview();f.render(str(OUT/'gallery.png'),(0,0,.55),3,0,0,ortho=1.4,res=(600,1400))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'cue-props.blend'))
