"""Append the left bridge glove to the existing exported fist pair without changing them."""
import os,sys,struct,json
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
import fsblend as f
import gloves as g
out=ROOT/'cue-hands-refined';out.mkdir(exist_ok=True)
f.reset(); g.BRIDGE_TILTS = (*g.BRIDGE_TILTS, 16.0); glove,before,after=g.glove_bridge_left()
f.export_bytes(str(out/'bridge.bytes'),[glove])
source=(ROOT/'revisions/2026-10-26-cue-hands-baseline/gloves.bytes').read_bytes()
assert source[:4]==b'FSM1' and struct.unpack_from('<i',source,4)[0]==2
part=(out/'bridge.bytes').read_bytes();assert part[:4]==b'FSM1' and struct.unpack_from('<i',part,4)[0]==1
(out/'gloves.bytes').write_bytes(b'FSM1'+struct.pack('<i',3)+source[8:]+part[8:])
m=g.bridge_matrix();q=g.unity_quat(m.to_3x3());groove=g.unity_vec(m.inverted()@Vector((0,0,0)))
report={'hand_rotation_xyzw':q,'groove_local':groove,'clearance_before':before,'clearance_after':after}
(out/'bridge-report.json').write_text(json.dumps(report,indent=2));print('BRIDGE',json.dumps(report))
f.setup_preview();f.render(str(out/'bridge.png'),(0,0,-.08),.45,30,18,ortho=.32,res=(900,900))
