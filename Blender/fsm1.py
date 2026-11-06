"""Leitura/escrita do formato FSM1 (Resources/Models/FS/*.bytes) sem Blender — para inspeccionar e extrair peças."""
import struct


def read(path):
    d = open(path, 'rb').read()
    assert d[:4] == b'FSM1'
    n = struct.unpack_from('<i', d, 4)[0]
    o = 8
    objs = []
    for _ in range(n):
        l = struct.unpack_from('<i', d, o)[0]; name = d[o + 4:o + 4 + l].decode(); o += 4 + l
        m = struct.unpack_from('<i', d, o)[0]; o += 4
        mats = []
        for _ in range(m):
            l2 = struct.unpack_from('<i', d, o)[0]; mats.append(d[o + 4:o + 4 + l2].decode()); o += 4 + l2
        v = struct.unpack_from('<i', d, o)[0]; o += 4
        pos = [struct.unpack_from('<3f', d, o + 12 * i) for i in range(v)]; o += 12 * v
        nor = [struct.unpack_from('<3f', d, o + 12 * i) for i in range(v)]; o += 12 * v
        uv = [struct.unpack_from('<2f', d, o + 8 * i) for i in range(v)]; o += 8 * v
        subs = []
        for _ in range(m):
            c = struct.unpack_from('<i', d, o)[0]; o += 4
            subs.append(list(struct.unpack_from('<%di' % c, d, o))); o += 4 * c
        objs.append({'name': name, 'mats': mats, 'pos': pos, 'nor': nor, 'uv': uv, 'subs': subs})
    return objs


def write(path, objs):
    out = bytearray(b'FSM1') + struct.pack('<i', len(objs))
    for ob in objs:
        b = ob['name'].encode(); out += struct.pack('<i', len(b)) + b
        out += struct.pack('<i', len(ob['mats']))
        for m in ob['mats']:
            b = m.encode(); out += struct.pack('<i', len(b)) + b
        out += struct.pack('<i', len(ob['pos']))
        for p in ob['pos']: out += struct.pack('<3f', *p)
        for p in ob['nor']: out += struct.pack('<3f', *p)
        for p in ob['uv']: out += struct.pack('<2f', *p)
        for s in ob['subs']:
            out += struct.pack('<i', len(s)) + struct.pack('<%di' % len(s), *s)
    open(path, 'wb').write(bytes(out))


def bounds(ob):
    xs, ys, zs = zip(*ob['pos'])
    return (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))
