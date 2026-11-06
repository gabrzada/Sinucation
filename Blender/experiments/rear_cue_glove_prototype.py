"""Build a slimmer cue glove candidate without replacing any game resources."""
import json
import struct
import sys
from pathlib import Path

import bpy

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'Blender'))
import fsblend as fb
import gloves as gl

out = Path(__file__).resolve().parent / 'rear-cue-glove'
out.mkdir(parents=True, exist_ok=True)
fb.reset()

# Keep the hand and channel dimensions intact; make the flared cuff less bulky.
gl.CUFF_PROFILE = tuple((radius * .76, z) for radius, z in gl.CUFF_PROFILE)
def slimmer_rim(parts, matrix=None, scale=1.0):
    rim = fb.torus(.0466 * .76 * scale, .005 * scale, (0, 0, .0186 * scale),
                   'ivory', major_seg=40, minor_seg=12)
    fb.apply_all(rim)
    if matrix is not None:
        rim.data.transform(matrix)
    parts.append(rim)
gl.cuff_rim = slimmer_rim

glove = gl.glove_fist()
glove.name = 'GloveR_CueSlim'
fb.export_bytes(str(out / 'candidate.bytes'), [glove])
part = (out / 'candidate.bytes').read_bytes()
original = (root / 'Assets/_Project/Resources/Models/FS/gloves.bytes').read_bytes()
assert original[:4] == b'FSM1' and struct.unpack_from('<i', original, 4)[0] == 3
(out / 'gloves.bytes').write_bytes(b'FSM1' + struct.pack('<i', 4) + original[8:] + part[8:])
(out / 'report.json').write_text(json.dumps({'cuff_radius_factor': .76, 'channel_radius': gl.CHANNEL_R, 'channel_center': list(gl.CHANNEL)}, indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'candidate.blend'))
fb.setup_preview()
fb.render(str(out / 'candidate.png'), (0, 0, -.08), .7, 30, 15, lens=50)
