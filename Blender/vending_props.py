"""Static machine cabinets, local coordinates in metres, Blender Z up and front +Y.
Run: blender -b --factory-startup -P Blender/vending_props.py
The game supplies displays, transparent globe, balls, labels, lights and colliders.
"""
import sys,json
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'vending-refined';OUT.mkdir(exist_ok=True);f.reset()
f.PREVIEW['red']=(.78,.08,.10);f.PREVIEW['chrome']=(.75,.78,.81)

def box(p,size,pos,mat,r=.01): p.append(f.box(size,pos,mat,r,3))
def screw(p,pos,axis='Y',r=.012):
 p.append(f.sphere(r,pos,'chrome',scale=(.4,1,1) if axis=='X' else (1,.4,1),segments=12,rings=8))

def gumball():
 p=[]
 box(p,(.5,.5,.83),(0,0,.435),'red',.065)
 box(p,(.58,.58,.06),(0,0,.03),'chrome',.025)
 # Framing, service panel and coin mechanism remain below the transparent globe.
 for x in (-.23,.23): box(p,(.035,.035,.68),(x,.25,.44),'mustard',.012)
 box(p,(.34,.045,.32),(0,.26,.70),'dark',.025)
 box(p,(.30,.055,.26),(0,.29,.70),'chrome',.025)
 box(p,(.04,.012,.10),(.08,.323,.74),'dark',.007)
 p.append(f.cylinder(.06,.035,(-.06,.33,.70),'chrome',rot=(90,0,0),verts=24,bevel_w=.006))
 box(p,(.06,.012,.012),(-.06,.35,.70),'dark',.004)
 box(p,(.19,.07,.12),(0,.265,.49),'mustard',.018)
 box(p,(.15,.012,.07),(0,.305,.49),'dark',.015)
 for x in (-.13,.13):
  for z in (.61,.79): screw(p,(x,.322,z),r=.009)
 p.append(f.cylinder(.22,.06,(0,0,.88),'chrome',verts=32,bevel_w=.01))
 for z in (.86,.90): p.append(f.torus(.22,.008,(0,0,z),'mustard',major_seg=32,minor_seg=8))
 return f.join('GumballBase',p)

def vending():
 p=[]
 box(p,(1.1,.7,2.08),(0,0,1.06),'red',.065)
 box(p,(1.08,.68,.10),(0,0,.06),'dark',.025)
 # Vertical edge rails and lower access panel; leave the display and lettering clear.
 for x in (-.51,.51):
  box(p,(.055,.075,1.82),(x,.32,1.06),'mustard',.015)
  for z in (.18,.80,1.93): screw(p,(x,.364,z),r=.012)
 for side in (-1,1):
  box(p,(.008,.47,1.35),(side*.551,0,1.1),'dark',.025)
  box(p,(.004,.41,1.27),(side*.555,0,1.1),'red',.018)
  for y in (-.15,.15):
   for z in (.55,1.65): screw(p,(side*.556,y,z),'X',.008)
  for z in (.25,.30,.35): box(p,(.006,.30,.023),(side*.556,0,z),'dark',.007)
 box(p,(.93,.035,.51),(0,.354,.44),'dark',.022)
 box(p,(.88,.025,.46),(0,.38,.44),'red',.02)
 for x in (-.39,.39):
  for z in (.27,.61): screw(p,(x,.4,z),r=.01)
 # Existing tray is centred at y .40 and protrudes, so it remains exposed.
 return f.join('VendingBody',p)
models=[gumball(),vending()]
f.export_bytes(str(OUT/'vending.bytes'),models,box_uv=True)
report={}
for ob in models:
 ob.data.calc_loop_triangles();report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
models[0].location.x=-.8;models[1].location.x=.4
f.setup_preview();f.render(str(OUT/'gallery.png'),(0,0,1),5,30,12,ortho=3.5,res=(1400,1000))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'vending-props.blend'))
