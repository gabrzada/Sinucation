"""Experimental rail bridge: fingers support beside the shaft rather than below it."""
import math
import struct
import sys
from pathlib import Path
import bpy
from mathutils import Vector

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'Blender'))
import fsblend as fb
import gloves as gl

out = Path(__file__).resolve().parent / 'rail-bridge'
out.mkdir(parents=True, exist_ok=True)
fb.reset()
m0 = gl.bridge_matrix(gl.BRIDGE_WRIST0)
wrist = gl.ellipsoid(*gl.WRIST_MASS)
fb.apply_all(wrist)
wrist.data.transform(m0)
parts = [wrist, gl.cuff_solid(m0),
         gl.ellipsoid((-.052, -.038, .015), (.035, .042, .019)),
         gl.ellipsoid_axes((-.058, -.058, .011), (1, -.3, 0), (.3, 1, 0), (.037, .04, .019))]
for x, y, length, radius in ((-.018, -.003, .055, .0125), (-.043, -.006, .053, .012),
                              (-.066, -.015, .047, .0115), (-.086, -.026, .04, .0105)):
    # Fingers fan out beside the shaft; the index curls slightly across its side.
    parts += gl.tube([Vector((x, y, .009)), Vector((x-.004, y+length*.55, .007)),
                      Vector((x-.008, y+length, .004))], (radius, radius*.95, radius*.9))
parts += gl.tube([Vector((-.035, -.067, .006)), Vector((-.006, -.045, .006)),
                  Vector((.016, -.018, .006)), Vector((.018, .012, .007))], (.016, .013, .0125, .0115))
hand = gl.sculpt('RailBridgeHand', parts, target_tris=3400, scale=gl.S)
for angle in gl.BRIDGE_TILTS:
    gl.carve(hand, gl.rod((0, 0, 0), gl.cue_axis(angle), gl.CUE_SHAFT_R+.0015, .6, verts=40))
parts = [hand]
gl.cuff_rim(parts, gl.bridge_matrix(), scale=gl.S)
obj = fb.join('GloveL_RailBridge', parts)
print('RAIL_CLEARANCE', gl.clearance_to_cue(obj))
obj.data.transform(gl.bridge_matrix().inverted())
bpy.context.view_layer.update()
fb.export_bytes(str(out/'rail.bytes'), [obj])
base = (Path(__file__).resolve().parent/'rear-split-grip-narrow/gloves.bytes').read_bytes()
part = (out/'rail.bytes').read_bytes()
assert base[:4] == b'FSM1' and struct.unpack_from('<i', base, 4)[0] == 5
(out/'gloves.bytes').write_bytes(b'FSM1'+struct.pack('<i', 6)+base[8:]+part[8:])
bpy.ops.wm.save_as_mainfile(filepath=str(out/'rail.blend'))
