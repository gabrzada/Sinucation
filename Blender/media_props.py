"""Lounge media set: DVD player and the low console under the arena's existing wall TV. Metres; Blender Z up, front -Y.
The TV itself is the one RoomBuilder already builds; Unity draws the screen content (video/cards) over it and supplies the
DVD display text, lights, audio, interaction and colliders.
Run: blender -b --factory-startup -P Blender/media_props.py
Install: copy Blender/media-refined/media.bytes to Assets/_Project/Resources/Models/FS/media.bytes
"""
import math,sys,json
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'media-refined';OUT.mkdir(exist_ok=True);f.reset()

def box(p,size,pos,mat,r=.01):p.append(f.box(size,pos,mat,r,3))
def rivet(p,pos,r=.011):p.append(f.sphere(r,pos,'steel',scale=(1,.45,1),segments=12,rings=8))

# DVD: origin at the centre of the base, front -Y. Display window at x=+.115, z=.042, y=-.152 (Unity text).
def dvd():
 p=[]
 box(p,(.43,.29,.07),(0,0,.043),'navy',.012)
 box(p,(.43,.012,.07),(0,-.146,.043),'suitDark',.008)
 box(p,(.44,.3,.008),(0,0,.079),'mustard',.004)
 box(p,(.24,.006,.012),(-.075,-.153,.058),'dark',.003)
 box(p,(.13,.006,.034),(.115,-.153,.042),'dark',.004)
 for i,(x,m) in enumerate(((-.155,'coral'),(-.115,'mustard'),(-.075,'steel'),(-.035,'steel'))):
  p.append(f.cylinder(.011,.008,(x,-.155,.03),m,rot=(90,0,0),verts=16,bevel_w=.002))
 for x in (-.2,.2):
  for z in (.022,.064):rivet(p,(x,-.155,z),.005)
 for x in (-.18,.18):
  for y in (-.11,.11):p.append(f.cylinder(.018,.008,(x,y,.004),'dark',verts=16))
 return f.join('DvdPlayer',p)

# Console: origin at the centre of the floor footprint, front -Y. Top surface at z=.52.
def console():
 p=[]
 box(p,(1.5,.42,.06),(0,0,.49),'wood',.018)
 box(p,(1.46,.4,.03),(0,0,.455),'mustard',.008)
 box(p,(1.42,.38,.36),(0,.0,.26),'navy',.02)
 for x in (-.36,.36):
  box(p,(.62,.02,.26),(x,-.19,.27),'dark',.01)
  box(p,(.66,.016,.03),(x,-.2,.41),'mustard',.006)
  box(p,(.66,.016,.03),(x,-.2,.13),'mustard',.006)
 box(p,(.04,.03,.3),(0,-.195,.27),'mustard',.008)
 for x in (-.68,.68):
  for z in (.13,.41):rivet(p,(x,-.2,z),.01)
 for x in (-.66,.66):
  for y in (-.15,.15):box(p,(.05,.05,.08),(x,y,.04),'steel',.01)
 # A row of DVD cases standing on the top, held by a steel bookend.
 for i,m in enumerate(('coral','teal','mustard','coral','ivory','teal')):
  box(p,(.018,.15,.2),(-.64+i*.026,.02,.62),m,.004)
 box(p,(.006,.12,.12),(-.64+6*.026,.02,.58),'steel',.002)
 box(p,(.05,.12,.006),(-.64+6*.026-.022,.02,.523),'steel',.002)
 return f.join('TvConsole',p)

models=[dvd(),console()];f.export_bytes(str(OUT/'media.bytes'),models,box_uv=True)
report={}
for ob in models:
 ob.data.calc_loop_triangles();report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
# Inspection gallery only: the DVD on top of the console, as in the arena.
models[0].location=(.2,-.03,.52)
f.PREVIEW.update({'dark':(.02,.025,.04)})
f.setup_preview()
f.render(str(OUT/'gallery.png'),(0,0,.4),4.2,200,14,ortho=1.8,res=(1300,800))
f.render(str(OUT/'gallery_front.png'),(.2,0,.56),2,180,4,ortho=.6,res=(1100,700))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'media-props.blend'))
