"""Jukebox cabinet and display records. Metres; Blender Z up, front -Y.
The Unity scene supplies the window, lamps, controls, audio, interaction and collider.
Run: blender -b --factory-startup -P Blender/jukebox_props.py
"""
import math,sys,json
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'jukebox-refined';OUT.mkdir(exist_ok=True);f.reset()
f.PREVIEW.update({'chrome':(.72,.75,.80),'woodDark':(.18,.08,.04),'record':(.025,.03,.04)})

def box(p,size,pos,mat,r=.01):p.append(f.box(size,pos,mat,r,3))
def screw(p,pos,axis='Y'):
 p.append(f.sphere(.009,pos,'chrome',scale=(.4,1,1) if axis=='X' else (1,.4,1),segments=12,rings=8))

def cabinet():
 p=[]
 box(p,(.95,.62,.12),(0,0,.06),'woodDark',.04)
 box(p,(.90,.56,1.35),(0,0,.80),'wood',.06)
 profile=[(.45*math.cos(math.pi*i/64), .45*math.sin(math.pi*i/64)) for i in range(65)]
 arch=f.prism(profile,.56,'wood',center=(0,0,1.46));f.bevel(arch,.045,3,'ANGLE',30);p.append(arch)
 # Frames sit outside the existing luminous window and grille.
 for x in (-.36,.36):
  box(p,(.04,.035,.47),(x,-.286,1.24),'mustard',.012)
  box(p,(.04,.03,.54),(x,-.286,.50),'mustard',.012)
  for z in (.27,.73,1.03,1.45):screw(p,(x,-.306,z))
 for z in (1.00,1.48): box(p,(.75,.035,.035),(0,-.286,z),'mustard',.01)
 for z in (.23,.77): box(p,(.75,.03,.035),(0,-.286,z),'mustard',.01)
 # Banded wooden cabinet sides, not extending beyond the original collider.
 for side in (-1,1):
  box(p,(.012,.43,1.15),(side*.453,0,.78),'woodDark',.015)
  box(p,(.008,.36,1.07),(side*.461,0,.78),'wood',.012)
  for y in (-.15,.15):
   for z in (.32,1.25):screw(p,(side*.468,y,z),'X')
 return f.join('JukeboxCabinet',p)

def records():
 p=[]
 for i in range(5):
  x=-.2+i*.1
  p.append(f.cylinder(.046,.012,(x,-.305,1.22),'record',rot=(90,0,0),verts=32,bevel_w=.002))
  p.append(f.torus(.034,.0016,(x,-.312,1.22),'chrome',rot=(90,0,0),major_seg=32,minor_seg=6))
  p.append(f.cylinder(.014,.014,(x,-.306,1.22),'mustard' if i%2==0 else 'coral',rot=(90,0,0),verts=20))
 return f.join('JukeboxRecords',p)
models=[cabinet(),records()];f.export_bytes(str(OUT/'jukebox.bytes'),models,box_uv=True)
report={}
for ob in models:
 ob.data.calc_loop_triangles();report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2));f.setup_preview()
f.render(str(OUT/'gallery.png'),(0,0,1),4,180,8,ortho=2.6,res=(900,1200))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'jukebox-props.blend'))
