"""Furniture for the arena bar and lounge. Blender Z up; fronts point toward -Y.
Run with Blender 5: blender -b --factory-startup -P Blender/lounge_props.py
The export is staged in Blender/lounge-refined; install after reviewing in Unity.
"""
import math, sys, json
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT))
import fsblend as f
OUT=ROOT/'lounge-refined'; OUT.mkdir(exist_ok=True)
f.reset()

def box(parts,size,pos,mat,r=.015):
    parts.append(f.box(size,pos,mat,r,3))

def bolt(parts,pos,r=.012):
    parts.append(f.sphere(r,pos,'steel',scale=(1,.45,1),segments=12,rings=8))

def bar(end=False):
    p=[]
    box(p,(.96,.65,.88),(0,0,.46),'navy',.035)
    box(p,(.98,.68,.075),(0,0,.095),'dark',.015)
    box(p,(1,.72,.09),(0,0,.955),'wood',.025)
    box(p,(.96,.69,.028),(0,0,.918),'mustard',.01)
    # Recessed front panels and a horizontal foot rail.
    for x in (-.23,.23):
        box(p,(.41,.025,.63),(x,-.337,.49),'suitDark',.014)
        box(p,(.36,.017,.56),(x,-.355,.49),'navy',.012)
        for xx in (x-.15,x+.15):
            for z in (.25,.73): bolt(p,(xx,-.369,z),.009)
    for x in (-.46,.46): box(p,(.045,.045,.81),(x,-.35,.49),'mustard',.011)
    rail=f.cylinder(.018,.84,(0,-.39,.18),'steel',rot=(0,90,0),verts=16,bevel_w=.004);p.append(rail)
    for x in (-.38,.38): box(p,(.035,.11,.045),(x,-.36,.18),'steel',.009)
    if end:
        # A matching squared-off end keeps the counter modules aligned.
        box(p,(.025,.56,.65),(.49,0,.49),'suitDark',.012)
        for y in (-.22,.22):
            box(p,(.035,.045,.69),(.505,y,.49),'mustard',.01)
    return f.join('BarEnd' if end else 'Bar',p)

def fridge():
    p=[]
    box(p,(.68,.62,.94),(0,0,.49),'navy',.035)
    box(p,(.60,.035,.78),(0,-.32,.52),'suitDark',.025)
    box(p,(.55,.025,.72),(0,-.344,.52),'navy',.022)
    for x in (-.31,.31): box(p,(.045,.055,.83),(x,-.319,.49),'mustard',.014)
    box(p,(.56,.04,.035),(0,-.337,.31),'mustard',.009)
    box(p,(.035,.035,.26),(.20,-.37,.67),'steel',.012)
    box(p,(.035,.035,.15),(.20,-.37,.205),'steel',.012)
    for x in (-.26,.26):
        for z in (.13,.87): bolt(p,(x,-.355,z),.011)
    box(p,(.61,.57,.035),(0,0,.972),'mustard',.015)
    return f.join('Fridge',p)

def shelf():
    p=[]
    for x in (-.44,.44): box(p,(.075,.48,.98),(x,0,.49),'navy',.018)
    for z in (.045,.33,.64,.955):
        box(p,(.92,.48,.055),(0,0,z),'wood',.012)
        box(p,(.92,.025,.035),(0,-.25,z),'mustard',.007)
    for x in (-.44,.44):
        for z in (.09,.90): bolt(p,(x,-.253,z),.012)
    box(p,(.78,.025,.90),(0,.23,.49),'suitDark',.01)
    return f.join('Shelf',p)

def seat(name,width):
    p=[]
    # Frame, upholstered back, individual seat cushions and piping-like edging.
    box(p,(width,.74,.20),(0,0,.20),'navy',.045)
    box(p,(width-.12,.20,.60),(0,.27,.61),'navy',.045)
    box(p,(width-.19,.14,.48),(0,.155,.66),'coral',.05)
    count=2 if width>1.1 else 1
    cw=(width-.23)/count
    for i in range(count):
        x=(i-(count-1)/2)*cw
        box(p,(cw-.018,.54,.16),(x,-.065,.38),'coral',.055)
        box(p,(cw-.035,.025,.023),(x,-.337,.38),'leather',.009)
    for x in (-width/2+.06,width/2-.06):
        box(p,(.12,.74,.41),(x,0,.38),'navy',.04)
        box(p,(.13,.72,.055),(x,0,.59),'mustard',.018)
        for y in (-.25,.25): box(p,(.09,.09,.13),(x,y,.065),'dark',.018)
    return f.join(name,p)

def coffee():
    p=[]
    box(p,(1,.62,.10),(0,0,.91),'wood',.025)
    box(p,(1.02,.64,.045),(0,0,.85),'navy',.015)
    for x in (-.43,.43):
        for y in (-.24,.24):
            box(p,(.065,.065,.83),(x,y,.415),'navy',.016)
            box(p,(.085,.085,.045),(x,y,.023),'mustard',.01)
    for x in (-.32,.32): bolt(p,(x,-.325,.85),.012)
    return f.join('CoffeeTable',p)

def speaker():
    p=[]
    box(p,(.44,.36,.96),(0,0,.49),'navy',.025)
    box(p,(.39,.025,.88),(0,-.19,.49),'suitDark',.015)
    for z,r in ((.31,.135),(.67,.095)):
        p.append(f.cylinder(r,.02,(0,-.211,z),'mustard',rot=(90,0,0),verts=32,bevel_w=.003))
        p.append(f.cylinder(r*.86,.025,(0,-.222,z),'dark',rot=(90,0,0),verts=32,bevel_w=.003))
        p.append(f.sphere(r*.36,(0,-.238,z),'steel',scale=(1,.22,1),segments=20,rings=10))
    for x in (-.17,.17):
        for z in (.09,.89): bolt(p,(x,-.211,z),.009)
    return f.join('Speaker',p)

models=[bar(),bar(True),fridge(),shelf(),seat('Sofa',1.65),seat('Chair',.82),coffee(),speaker()]
# Match the imported meshes' bounding boxes after PropLibrary normalizes them to height.
# This preserves the placement and collider footprints already used by RoomBuilder.
original = [(4.3,4.2,2.1),(1.01563,4.2,2.1),(4.3,9.2,2.91934),
            (4.0,8.8,2.5),(9.8,4.6,8.2),(4.9,4.6,4.1),
            (6.60996,2.3,4.0),(1.48,6.364,1.48)]
for ob, (width,height,depth) in zip(models, original):
    # These three are placed against the south wall with yaw 0; face the room (+Z in Unity).
    if ob.name in ('Fridge', 'Shelf', 'Speaker'):
        for v in ob.data.vertices:
            v.co.x = -v.co.x
            v.co.y = -v.co.y
        ob.data.update()
    verts=ob.data.vertices
    low=[min(v.co[i] for v in verts) for i in range(3)]
    high=[max(v.co[i] for v in verts) for i in range(3)]
    target=(width/height, depth/height, 1.0)
    factors=[target[i]/(high[i]-low[i]) for i in range(3)]
    for v in verts:
        for i in range(3): v.co[i] *= factors[i]
    ob.data.update()
f.export_bytes(str(OUT/'lounge.bytes'),models,box_uv=True)
report={}
for ob in models:
    ob.data.calc_loop_triangles(); bounds=[Vector(c) for c in ob.bound_box]
    report[ob.name]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles),
                     'size':[max(v[i] for v in bounds)-min(v[i] for v in bounds) for i in range(3)]}
(OUT/'geometry.json').write_text(json.dumps(report,indent=2))
# Inspection gallery only: exported models retain their joint-local coordinates.
for ob,x in zip(models,[-3.4,-2.3,-1.3,-.3,1.1,2.6,3.7,4.6]): ob.location.x=x
f.setup_preview()
f.render(str(OUT/'gallery.png'),(.4,0,.55),12,180,12,ortho=10,res=(2000,900))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lounge-props.blend'))
print('LOUNGE_PROPS',json.dumps(report))
