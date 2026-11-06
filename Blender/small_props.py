"""Small bar/lounge props, Blender Z up; each mesh is normalized to imported asset bounds.
Run: blender -b --factory-startup -P Blender/small_props.py
"""
import math,sys,json
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'small-props-refined';OUT.mkdir(exist_ok=True);f.reset()
f.PREVIEW.update({'green':(.11,.42,.23),'leaf':(.24,.57,.31),'terracotta':(.62,.27,.16),'cream':(.93,.87,.72)})

def box(p,size,pos,mat,r=.01):p.append(f.box(size,pos,mat,r,3))
def rod(p,a,b,r,mat):
 a,b=Vector(a),Vector(b);o=f.cylinder(r,(b-a).length,(a+b)/2,mat,verts=16,bevel_w=.004);o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();p.append(o)
def lathe(profile,mat,segments=32,closed=False):
 verts=[(r*math.cos(math.tau*i/segments),r*math.sin(math.tau*i/segments),z) for r,z in profile for i in range(segments)]
 faces=[]
 for j in range(len(profile)-1):
  for i in range(segments):
   k=(i+1)%segments;faces.append((j*segments+i,j*segments+k,(j+1)*segments+k,(j+1)*segments+i))
 if closed: faces.extend([tuple(reversed(range(segments))),tuple((len(profile)-1)*segments+i for i in range(segments))])
 m=bpy.data.meshes.new('lathe');m.from_pydata(verts,[],faces);m.update();o=bpy.data.objects.new('lathe',m);bpy.context.collection.objects.link(o);return f._finish(o,mat)

def bottle(name,mat):
 p=[lathe([(.19,0),(.22,.03),(.22,.49),(.205,.57),(.12,.68),(.10,.73),(.10,.91),(.12,.93),(.12,.96)],mat,closed=True),
    f.cylinder(.12,.07,(0,0,.965),'mustard',verts=24,bevel_w=.008),
    f.cylinder(.224,.23,(0,0,.33),'cream',verts=32),
    f.torus(.225,.009,(0,0,.22),'mustard',major_seg=32,minor_seg=8),
    f.torus(.225,.009,(0,0,.44),'mustard',major_seg=32,minor_seg=8)]
 return f.join(name,p)

def radio():
 p=[]
 box(p,(1.15,.45,.72),(0,0,.43),'navy',.055)
 box(p,(1.05,.035,.60),(0,-.235,.43),'wood',.025)
 box(p,(.62,.025,.40),(-.17,-.26,.43),'dark',.02)
 for z in (.28,.34,.40,.46,.52,.58):box(p,(.53,.018,.018),(-.17,-.277,z),'mustard',.006)
 box(p,(.30,.025,.20),(.37,-.26,.53),'cream',.014)
 box(p,(.23,.012,.025),(.37,-.279,.53),'dark',.005)
 for x in (.27,.47):p.append(f.cylinder(.04,.02,(x,-.28,.32),'brass',rot=(90,0,0),verts=24,bevel_w=.004))
 for x in (-.48,.48):
  for z in (.2,.66):p.append(f.sphere(.012,(x,-.258,z),'steel',scale=(1,.4,1),segments=12,rings=8))
 rod(p,(-.43,0,.79),(-.43,0,.96),.018,'steel');rod(p,(-.43,0,.96),(.43,0,.96),.018,'steel');rod(p,(.43,0,.96),(.43,0,.79),.018,'steel')
 return f.join('Radio',p)

def cactus():
 p=[f.cylinder(.23,.30,(0,0,.15),'terracotta',verts=32,bevel_w=.018,radius2=.28),
    f.torus(.272,.024,(0,0,.28),'mustard',major_seg=32,minor_seg=8),
    f.cylinder(.24,.015,(0,0,.30),'dark',verts=32),
    f.sphere(.5,(0,0,.58),'green',scale=(.31,.31,.56),segments=24,rings=16)]
 for side in (-1,1):
  p.append(f.sphere(.5,(side*.18,0,.52),'leaf',scale=(.20,.20,.32),segments=20,rings=14,rot=(0,side*24,0)))
 for a in range(8):
  angle=math.tau*a/8
  for z in (.48,.60,.72):
   radius=.155*math.sqrt(max(0,1-((z-.58)/.28)**2))
   p.append(f.sphere(.008,(radius*math.cos(angle),radius*math.sin(angle),z),'cream',segments=8,rings=6))
 return f.join('Cactus',p)

def plant():
 p=[f.cylinder(.27,.30,(0,0,.15),'terracotta',verts=32,bevel_w=.02,radius2=.33),
    f.torus(.32,.025,(0,0,.29),'mustard',major_seg=32,minor_seg=8),
    f.cylinder(.29,.02,(0,0,.31),'dark',verts=32)]
 for i in range(7):
  a=math.tau*i/7; c,s=math.cos(a),math.sin(a);tip=(.23*c,.23*s,.88+(i%2)*.05)
  rod(p,(0,0,.30),tip,.014,'green')
  leaf=f.sphere(.5,(.22*c,.22*s,.78+(i%2)*.05),'leaf',scale=(.20,.10,.38),segments=20,rings=14)
  leaf.rotation_euler=(0,math.radians(30),a);p.append(leaf)
 p.append(f.sphere(.5,(0,0,.78),'green',scale=(.19,.19,.43),segments=20,rings=14))
 return f.join('PottedPlant',p)

def lamp():
 p=[f.cylinder(.27,.055,(0,0,.028),'navy',verts=32,bevel_w=.01),
    f.cylinder(.025,.74,(0,0,.41),'steel',verts=20,bevel_w=.004),
    f.cylinder(.06,.055,(0,0,.80),'mustard',verts=24,bevel_w=.01),
    lathe([(.26,.78),(.27,.80),(.20,.98),(.18,1.0),(.17,.98),(.24,.80)],'mustard',segments=40),
    f.torus(.255,.01,(0,0,.79),'navy',major_seg=40,minor_seg=8),
    f.sphere(.075,(0,0,.82),'glow',segments=20,rings=14)]
 return f.join('FloorLamp',p)

models=[bottle('Bottle','green'),bottle('BottleLarge','coral'),radio(),cactus(),plant(),lamp()]
targets=[(.052689,.153869,.060840),(.072447,.143869,.083655),(3.15,2.281911,.975),(.946608,1.4,.946608),(2.120593,6.540168,2.414619),(1.520394,8.6,1.7556)]
for ob,(width,height,depth) in zip(models,targets):
 verts=ob.data.vertices;lo=[min(v.co[i] for v in verts) for i in range(3)];hi=[max(v.co[i] for v in verts) for i in range(3)]
 target=(width/height,depth/height,1);factor=[target[i]/(hi[i]-lo[i]) for i in range(3)]
 for v in verts:
  for i in range(3):v.co[i]*=factor[i]
 ob.data.update()
f.export_bytes(str(OUT/'small-props.bytes'),models,box_uv=True)
report={}
for ob in models:
 ob.data.calc_loop_triangles();report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
for ob,x in zip(models,[-2,-1.3,-.35,.55,1.4,2.4]):ob.location.x=x
f.setup_preview();f.render(str(OUT/'gallery.png'),(0,0,.5),6,180,10,ortho=6,res=(1600,1000))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'small-props.blend'))
