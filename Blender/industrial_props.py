"""Industrial storage and safety props; Blender Z up, exported in model-local metres.
Run: blender -b --factory-startup -P Blender/industrial_props.py
"""
import math, sys, json
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parent; sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'industrial-refined';OUT.mkdir(exist_ok=True)
f.reset()

def box(parts,size,pos,mat,r=.01): parts.append(f.box(size,pos,mat,r,2))
def bolt(parts,pos,axis='Y',r=.012):
    sc=(.42,1,1) if axis=='X' else (1,.42,1)
    parts.append(f.sphere(r,pos,'steel',scale=sc,segments=10,rings=6))

def container(name, color):
    p=[]
    box(p,(.92,2.05,.91),(0,0,.50),color,.025)
    # Corrugations run vertically on the long walls; corner posts frame them.
    for side in (-1,1):
        for i in range(13):
            y=-.91+i*.1517
            box(p,(.032,.065,.76),(side*.475,y,.50),color,.007)
        for y in (-1.0,1.0): box(p,(.075,.08,.98),(side*.46,y,.50),'mustard',.015)
        for z in (.07,.93): box(p,(.07,2.05,.075),(side*.46,0,z),'navy',.014)
        for y in (-.9,.9):
            for z in (.14,.86): bolt(p,(side*.50,y,z),'X')
        # Inset plate, bolts and triangle/circle/square motif on the accessible side.
        box(p,(.03,.37,.20),(side*.495,0,.55),'navy',.018)
        box(p,(.012,.30,.145),(side*.515,0,.55),'mustard',.01)
        for y in (-.14,.14):
            for z in (.51,.59): bolt(p,(side*.524,y,z),'X',.008)
    # Cargo doors on both ends, with paired locking bars.
    for side in (-1,1):
        box(p,(.83,.035,.78),(0,side*1.037,.5),'navy',.015)
        for x in (-.21,.21):
            box(p,(.38,.028,.69),(x,side*1.06,.5),color,.01)
            for dx in (-.11,.11):
                rod=f.cylinder(.012,.67,(x+dx,side*1.081,.5),'steel',verts=12,bevel_w=.002);p.append(rod)
                for z in (.18,.82): box(p,(.045,.035,.035),(x+dx,side*1.08,z),'mustard',.005)
    # Flat top and feet keep the existing stack contact points stable.
    box(p,(.91,2.02,.035),(0,0,.977),'navy',.012)
    return f.join(name,p)

def chest():
    p=[]
    box(p,(.89,.59,.57),(0,0,.32),'wood',.035)
    box(p,(.92,.62,.23),(0,0,.72),'navy',.045)
    for x in (-.36,.36):
        box(p,(.075,.63,.76),(x,0,.43),'mustard',.014)
        for z in (.16,.38,.68): bolt(p,(x,-.324,z),'Y')
    box(p,(.93,.64,.055),(0,0,.60),'dark',.015)
    box(p,(.20,.035,.19),(0,-.335,.58),'mustard',.015)
    box(p,(.07,.015,.09),(0,-.36,.58),'dark',.01)
    for x in (-.35,.35):
        for y in (-.22,.22): box(p,(.12,.12,.06),(x,y,.03),'dark',.012)
    return f.join('Chest',p)

def cone():
    p=[f.box((.73,.73,.065),(0,0,.0325),'dark',.025,3),
       f.cylinder(.28,.83,(0,0,.47),'coral',verts=32,bevel_w=.008,radius2=.06),
       f.cylinder(.204,.09,(0,0,.42),'ivory',verts=32,radius2=.181),
       f.cylinder(.132,.07,(0,0,.68),'ivory',verts=32,radius2=.114)]
    return f.join('Cone',p)

def warning():
    p=[]
    box(p,(.65,.43,.08),(0,0,.04),'dark',.02)
    box(p,(.075,.075,.80),(0,0,.44),'navy',.015)
    box(p,(.72,.10,.43),(0,0,.77),'mustard',.035)
    box(p,(.64,.025,.34),(0,-.059,.77),'navy',.015)
    for x in (-.27,.27):
        for z in (.64,.90): bolt(p,(x,-.075,z),'Y',.009)
    # Contrasting bands on the front keep the silhouette readable in motion.
    for x in (-.22,0,.22):
        q=f.box((.10,.015,.31),(x,-.077,.77),'mustard',.008,2,rot=(0,20,0));p.append(q)
    return f.join('Warning',p)

models=[container('ContainerA','coral'),container('ContainerC','teal'),chest(),cone(),warning()]
# Target proportions are filled from the Unity import survey; this keeps placement/collision.
targets=[(.3726,.34808,.8226),(.3726,.34808,.8226),(.2604,.257192,.272025),(.3,.306463,.3),(.4,1.551115,.4)]
for ob,(width,height,depth) in zip(models,targets):
    verts=ob.data.vertices
    lo=[min(v.co[i] for v in verts) for i in range(3)];hi=[max(v.co[i] for v in verts) for i in range(3)]
    target=(width/height,depth/height,1)
    factors=[target[i]/(hi[i]-lo[i]) for i in range(3)]
    for v in verts:
        for i in range(3): v.co[i]*=factors[i]
    ob.data.update()
f.export_bytes(str(OUT/'industrial.bytes'),models,box_uv=True)
report={}
for ob in models:
    ob.data.calc_loop_triangles();report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
for ob,pos in zip(models,[(-2.7,0,0),(-1.4,0,0),(.4,0,0),(1.4,0,0),(2.4,0,0)]): ob.location=pos
f.setup_preview();f.render(str(OUT/'gallery.png'),(0,0,.55),8,145,16,ortho=7,res=(1800,1000))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'industrial-props.blend'))
