"""Experimental rail bridge. Produces a candidate only; does not replace game assets."""
import sys,struct,json
from pathlib import Path
import bpy
from mathutils import Vector
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'Blender'))
import fsblend as f
import gloves as g
out=Path(__file__).resolve().parent/'rail-bridge';out.mkdir(parents=True,exist_ok=True)
f.reset();S=g.S;W=Vector((-.068,-.088,.045));m0=g.bridge_matrix(W)
wrist=g.ellipsoid(*g.WRIST_MASS);f.apply_all(wrist);wrist.data.transform(m0)
knuckles=Vector((-.045,-.012,.020))
parts=[wrist,g.cuff_solid(m0),g.ellipsoid((-.05,-.038,.009),(.035,.043,.019)),g.ellipsoid_axes((W+knuckles)*.5,(1.,-.3,0.),knuckles-W,(.041,.047,.018)),g.ellipsoid((-.03,-.068,.015),(.02,.024,.018))]
fingers=(((-.0128,-.004,.012),(-.021,.024,.006),(-.027,.048,-.003),( .0135,.0125,.0118)),((-.037,-.008,.010),(-.045,.020,.004),(-.053,.046,-.003),(.013,.0122,.0115)),((-.058,-.015,.008),(-.068,.010,.002),(-.078,.032,-.003),(.0125,.0117,.011)),((-.076,-.026,.006),(-.087,-.006,.001),(-.096,.012,-.003),(.0115,.0108,.0102)))
for k,mid,tip,r in fingers:parts+=g.tube([Vector(k)+Vector((0,0,.005)),Vector(mid)+Vector((0,0,.005)),Vector(tip)+Vector((0,0,.005))],r)
parts+=g.tube([Vector((-.036,-.072,.025)),Vector((-.004,-.051,.018)),Vector((.0108,-.018,.017)),Vector((.0165,.018,.0158))],(.017,.0135,.0126,.0122))
hand=g.sculpt('RailBridge',parts,target_tris=3400,scale=S)
for tilt in (-16.,-12.,-9.,-6.,-3.,0.,3.,6.,9.,12.,16.):g.carve(hand,g.rod((0,0,0),g.cue_axis(tilt),g.CUE_SHAFT_R+.0012,.6,verts=40))
m=g.bridge_matrix(W*S);outparts=[hand];g.cuff_rim(outparts,m,scale=S);obj=f.join('GloveL_RailBridge',outparts);clear=g.clearance_to_cue(obj,(-16.,-12.,-9.,-6.,-3.,0.,3.,6.,9.,12.,16.));bounds=[min(v.co.z for v in obj.data.vertices),max(v.co.z for v in obj.data.vertices)];obj.data.transform(m.inverted());groove=g.unity_vec(m.inverted()@Vector((0,0,0)));q=g.unity_quat(m.to_3x3())
f.export_bytes(str(out/'rail-bridge.bytes'),[obj]);part=(out/'rail-bridge.bytes').read_bytes();src=(root/'Assets/_Project/Resources/Models/FS/gloves.bytes').read_bytes();assert src[:4]==b'FSM1' and struct.unpack_from('<i',src,4)[0]==3
(out/'gloves.bytes').write_bytes(b'FSM1'+struct.pack('<i',4)+src[8:]+part[8:]);(out/'report.json').write_text(json.dumps({'bounds_T_z':bounds,'groove':groove,'rotation':q,'clearance':clear},indent=2));bpy.ops.wm.save_as_mainfile(filepath=str(out/'prototype.blend'));f.setup_preview();f.render(str(out/'prototype.png'),(0,0,-.04),.5,35,20,ortho=.35,res=(1000,1000));print('RAIL_PROTOTYPE',bounds,groove,clear)
