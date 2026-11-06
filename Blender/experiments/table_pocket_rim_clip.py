"""Candidate: raised pocket trim stays outside the playing cloth."""
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root/'Blender'))
source = (root/'Blender/table.py').read_text(encoding='utf-8')
old = "        parts.append(fb.torus(r, 0.009, (x, y, RAIL_TOP + 0.004), 'steel', major_seg=40, minor_seg=8))"
new = """        rim = fb.torus(r, 0.009, (x, y, RAIL_TOP + 0.004), 'steel', major_seg=40, minor_seg=8)
        fb.apply_all(rim)
        # The high steel trim must not overhang the playing cloth or a cue resting above it.
        cloth_cut = fb.box((HL * 2, HW * 2, 0.35), (0, 0, RAIL_TOP), 'steel')
        boolean_cut(rim, cloth_cut)
        parts.append(rim)"""
if old in source:
    assert source.count(old) == 1
    source = source.replace(old, new)
else:
    assert "cloth_cut = fb.box((HL * 2, HW * 2, 0.35)" in source
out = Path(__file__).resolve().parent/'table-rim-clipped'
sys.argv = [str(root/'Blender/table.py'), '--', str(out), str(out/'preview'), '--no-render']
exec(compile(source, str(root/'Blender/table.py'), 'exec'), {'__file__':str(root/'Blender/table.py'), '__name__':'__main__'})
