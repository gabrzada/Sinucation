"""Build a smoother swept-cue grip as a non-production experiment."""
import json
import math
import struct
import sys
from pathlib import Path
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'Blender'))
import fsblend as fb
import gloves as gl

out = Path(__file__).resolve().parent / 'rear-hinge-smooth-lod'
out.mkdir(parents=True, exist_ok=True)
fb.reset()
glove = gl.glove_fist()

# Fuse many closely spaced cue cylinders into one cutter.  A single boolean
# avoids the stepped facets left by repeated cuts in the previous prototype.
rods = []
for deg in range(-25, 26):
    r = math.radians(deg)
    rods.append(gl.rod(gl.CHANNEL, (0, math.cos(r), math.sin(r)), gl.CUE_BUTT_R + .0025, .24, verts=48))
cutter = gl.sculpt('CueSweep', rods, voxel=.0008, smooth=2, target_tris=75000)
gl.carve(glove, cutter)
glove.name = 'GloveR_CueSmooth'
bpy.context.view_layer.update()
mod = glove.modifiers.new('decimate', 'DECIMATE')
mod.ratio = .3
fb.apply_all(glove)

# Sample nearest triangle surfaces along each cue centerline. This is a
# clearance diagnostic, not a proof against all intersections between samples.
mesh = glove.data
bvh = BVHTree.FromPolygons([v.co.copy() for v in mesh.vertices], [list(p.vertices) for p in mesh.polygons], all_triangles=False)
clearance = {}
for deg in range(-25, 26):
    r = math.radians(deg)
    axis = Vector((0, math.cos(r), math.sin(r)))
    distances = [bvh.find_nearest(gl.CHANNEL + axis * (step * .001))[3] for step in range(-70, 71)]
    clearance[str(deg)] = min(distances) - gl.CUE_BUTT_R
fb.export_bytes(str(out / 'candidate.bytes'), [glove])
part = (out / 'candidate.bytes').read_bytes()
original = (root / 'Assets/_Project/Resources/Models/FS/gloves.bytes').read_bytes()
assert original[:4] == b'FSM1' and struct.unpack_from('<i', original, 4)[0] == 3
(out / 'gloves.bytes').write_bytes(b'FSM1' + struct.pack('<i', 4) + original[8:] + part[8:])
(out / 'clearance.json').write_text(json.dumps(clearance, indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'candidate.blend'))
fb.setup_preview()
fb.render(str(out / 'candidate.png'), (0, 0, -.08), .7, 30, 15, lens=50)
