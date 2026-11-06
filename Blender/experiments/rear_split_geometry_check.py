"""Sample cue clearance and palm/finger continuity for the split-grip prototype."""
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'Blender'))
import gloves as gl

out = Path(__file__).resolve().parent / 'rear-split-grip'
bpy.ops.wm.open_mainfile(filepath=str(out / 'candidate.blend'))
palm = bpy.data.objects['GloveR_CuePalm'].data
fingers = bpy.data.objects['GloveR_CueFingers'].data

def tree(mesh, matrix):
    vertices = [matrix @ v.co for v in mesh.vertices]
    polygons = [list(p.vertices) for p in mesh.polygons]
    return BVHTree.FromPolygons(vertices, polygons, all_triangles=False), vertices

def inside(surface, point):
    direction = Vector((.371, .713, .595)).normalized()
    origin = point.copy()
    hits = 0
    for _ in range(100):
        hit, normal, index, distance = surface.ray_cast(origin, direction, 2.0)
        if hit is None:
            return hits % 2 == 1
        hits += 1
        origin = hit + direction * .00001
    raise RuntimeError('ray failed to terminate')

finger_tree, finger_vertices = tree(fingers, Matrix.Identity(4))
report = []
for deg in range(-25, 26):
    matrix = Matrix.Translation(gl.CHANNEL) @ Matrix.Rotation(math.radians(deg), 4, 'X') @ Matrix.Translation(-gl.CHANNEL)
    palm_tree, palm_vertices = tree(palm, matrix)
    row = {'degrees': deg, 'overlapping_face_pairs': len(palm_tree.overlap(finger_tree))}
    for name, surface in [('palm', palm_tree), ('fingers', finger_tree)]:
        points = [gl.CHANNEL + Vector((0, step * .001, 0)) for step in range(-70, 71)]
        row[name + '_clearance'] = min(surface.find_nearest(p)[3] for p in points) - gl.CUE_BUTT_R
        row[name + '_axis_inside_samples'] = sum(inside(surface, points[i]) for i in range(0, len(points), 5))
    if not row['overlapping_face_pairs']:
        row['nearest_vertex_surface'] = min(finger_tree.find_nearest(p)[3] for p in palm_vertices)
    report.append(row)
(out / 'geometry-samples.json').write_text(json.dumps(report, indent=2))
print('GEOMETRY', 'minimum palm clearance', min(x['palm_clearance'] for x in report),
      'minimum finger clearance', min(x['fingers_clearance'] for x in report),
      'axis inside sample count', sum(x['palm_axis_inside_samples'] + x['fingers_axis_inside_samples'] for x in report),
      'angles without intersecting surfaces', sum(x['overlapping_face_pairs'] == 0 for x in report))
