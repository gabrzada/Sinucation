"""
Exporta as luvas de punho fechado (gloves.glove_fist) para Resources/Models/FS/gloves.bytes: GloveR_Fist e
GloveL_Fist. O jogo troca a luva aberta por esta quando a personagem segura o revólver (o punho da arma passa pelo
canal dos dedos, o polegar fecha por cima). Frame igual ao da luva do jogo: origem no pulso, dedos para baixo.
"""
import os
import sys

import bpy  # noqa: F401

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import fsblend as fb  # noqa: E402
import gloves as gl  # noqa: E402

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if len(argv) > 0 else os.path.join(os.path.dirname(__file__), 'out')
PREV = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(os.path.dirname(__file__), 'preview')

fb.reset()
fist = gl.glove_fist()
fist_l = fb.mirror_x(fist, 'GloveL_Fist')
fb.export_bytes(os.path.join(OUT, 'gloves.bytes'), [fist, fist_l])
print('channel', tuple(round(c, 4) for c in gl.CHANNEL), 'radius', gl.CHANNEL_R)

fist_l.location = (0.3, 0, 0)
fb.setup_preview()
os.makedirs(PREV, exist_ok=True)
fb.render(os.path.join(PREV, 'glove_fist.png'), (0.15, 0, -0.08), 0.7, 30, 15, lens=50)
