"""Build a cue-hand candidate with an independent, forearm-mounted cuff."""
import struct
import sys
from pathlib import Path
import bpy

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'Blender'))
import fsblend as fb
import gloves as gl

out = Path(__file__).resolve().parent / 'rear-articulated-glove'
out.mkdir(parents=True, exist_ok=True)
fb.reset()

# Replace the stiff, wide cuff with a compact wrist volume.  The hand's finger
# channel stays at the original coordinates; the cuff will follow the arm.
def compact_wrist(matrix=None, scale=1.0):
    obj = gl.ellipsoid((.002 * scale, 0, -.009 * scale), (.032 * scale, .032 * scale, .018 * scale))
    fb.apply_all(obj)
    if matrix is not None:
        obj.data.transform(matrix)
    return obj
gl.cuff_solid = compact_wrist
def no_rim(parts, matrix=None, scale=1.0):
    bpy.context.view_layer.update()
gl.cuff_rim = no_rim
glove = gl.glove_fist()
glove.name = 'GloveR_CueArticulated'
fb.export_bytes(str(out / 'candidate.bytes'), [glove])
part = (out / 'candidate.bytes').read_bytes()
original = (root / 'Assets/_Project/Resources/Models/FS/gloves.bytes').read_bytes()
assert original[:4] == b'FSM1' and struct.unpack_from('<i', original, 4)[0] == 3
(out / 'gloves.bytes').write_bytes(b'FSM1' + struct.pack('<i', 4) + original[8:] + part[8:])
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'candidate.blend'))
fb.setup_preview()
fb.render(str(out / 'candidate.png'), (0, 0, -.08), .7, 30, 15, lens=50)
