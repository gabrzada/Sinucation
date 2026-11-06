"""Experimental gripping glove with clearance for a tilted cue; never overwrites game assets."""
import json
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

out = Path(__file__).resolve().parent / 'rear-hinge-glove'
out.mkdir(parents=True, exist_ok=True)
fb.reset()
obj = gl.glove_fist()
for deg in range(-25, 26, 5):
    r = math.radians(deg)
    gl.carve(obj, gl.rod(gl.CHANNEL, (0, math.cos(r), math.sin(r)), gl.CUE_BUTT_R + .0015, .4, verts=48))
obj.name = 'GloveR_CueHinge'
clearance = {}
for deg in range(-25, 26, 5):
    r = math.radians(deg)
    axis = Vector((0, math.cos(r), math.sin(r)))
    distances = [(v.co - gl.CHANNEL - axis * (v.co - gl.CHANNEL).dot(axis)).length for v in obj.data.vertices]
    clearance[str(deg)] = min(distances) - gl.CUE_BUTT_R
fb.export_bytes(str(out / 'candidate.bytes'), [obj])
part = (out / 'candidate.bytes').read_bytes()
original = (root / 'Assets/_Project/Resources/Models/FS/gloves.bytes').read_bytes()
assert original[:4] == b'FSM1' and struct.unpack_from('<i', original, 4)[0] == 3
(out / 'gloves.bytes').write_bytes(b'FSM1' + struct.pack('<i', 4) + original[8:] + part[8:])
(out / 'clearance.json').write_text(json.dumps(clearance, indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'candidate.blend'))
fb.setup_preview()
fb.render(str(out / 'candidate.png'), (0, 0, -.08), .7, 30, 15, lens=50)
