"""Build separate palm and cue-aligned fingers for a prototype articulated grip."""
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

out = Path(__file__).resolve().parent / 'rear-split-grip-narrow'
out.mkdir(parents=True, exist_ok=True)
fb.reset()
S = gl.S

palm = gl.sculpt('CuePalm', [
    gl.ellipsoid(gl.P(.006, 0, -.052), Vector((.027, .046, .03)) * S),
    gl.ellipsoid(gl.P(*gl.WRIST_MASS[0]), Vector(gl.WRIST_MASS[1]) * S),
    gl.cuff_solid(scale=.8 * S),
], target_tris=2600)
rods = []
for deg in range(-25, 26):
    r = math.radians(deg)
    rods.append(gl.rod(gl.CHANNEL, (0, math.cos(r), math.sin(r)), gl.CUE_BUTT_R + .0025, .24, verts=40))
cutter = gl.sculpt('CueSweep', rods, voxel=.0009, smooth=2, target_tris=60000)
gl.carve(palm, cutter)
bpy.context.view_layer.update()
mod = palm.modifiers.new('decimate', 'DECIMATE'); mod.ratio = .35; fb.apply_all(palm)
palm_parts = [palm]; gl.cuff_rim(palm_parts, scale=.8 * S)
palm = fb.join('GloveR_CuePalm', palm_parts)

finger_parts = []
for y, r0 in gl.FIST_FINGERS:
    radius = r0 * S
    R = gl.CHANNEL_R + radius + .0006
    pts, radii = [], []
    for i, angle in enumerate(gl.FIST_ANGLES):
        t = i / (len(gl.FIST_ANGLES) - 1)
        rr = R + (.003 * S if i == 0 else 0)
        a = math.radians(angle)
        pts.append(Vector((gl.CHANNEL.x + rr * math.cos(a), y * S, gl.CHANNEL.z + rr * math.sin(a))))
        radii.append(radius * (1 - .1 * t))
    finger_parts += gl.tube(pts, radii)
finger_parts += gl.tube([gl.P(-.012, .036, -.044), gl.P(-.032, .05, -.066), gl.P(-.036, .047, -.091)],
                        (.0165 * S, .0142 * S, .0128 * S))
fingers = gl.sculpt('CueFingers', finger_parts, target_tris=3200)
gl.carve(fingers, gl.rod(gl.CHANNEL, (0, 1, 0), gl.CHANNEL_R, .4, verts=48))
fingers.name = 'GloveR_CueFingers'
bpy.context.view_layer.update()
fb.export_bytes(str(out / 'candidate.bytes'), [palm, fingers])
part = (out / 'candidate.bytes').read_bytes()
original = (root / 'Assets/_Project/Resources/Models/FS/gloves.bytes').read_bytes()
assert original[:4] == b'FSM1' and struct.unpack_from('<i', original, 4)[0] == 3
(out / 'gloves.bytes').write_bytes(b'FSM1' + struct.pack('<i', 5) + original[8:] + part[8:])
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'candidate.blend'))
fb.setup_preview()
fb.render(str(out / 'candidate.png'), (0, 0, -.08), .7, 30, 15, lens=50)

