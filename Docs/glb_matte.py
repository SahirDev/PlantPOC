"""Lower shine of glTF materials in a .glb in place: caps metallic, raises roughness floor.
Material names / order / count are not touched, so Unity sub-asset IDs and references stay the same."""
import json, struct, sys

METAL_MAX = 0.6        # fully metallic surfaces mirror the sky; cap it
METAL_ROUGH_MIN = 0.45 # metals: brushed / used steel instead of chrome
PAINT_ROUGH_MIN = 0.6  # painted / non-metal surfaces: satin paint, not glossy

def process(path):
    data = open(path, 'rb').read()
    magic, version, length = struct.unpack('<4sII', data[:12])
    assert magic == b'glTF'
    jlen, jtype = struct.unpack('<I4s', data[12:20]); assert jtype == b'JSON'
    j = json.loads(data[20:20 + jlen])
    rest = data[20 + jlen:]
    changed = 0
    for m in j.get('materials', []):
        pb = m.setdefault('pbrMetallicRoughness', {})
        metal = pb.get('metallicFactor', 1.0)
        rough = pb.get('roughnessFactor', 1.0)
        is_metal = metal > 0.5
        new_metal = min(metal, METAL_MAX)
        new_rough = max(rough, METAL_ROUGH_MIN if is_metal else PAINT_ROUGH_MIN)
        if abs(new_metal - metal) > 1e-6 or abs(new_rough - rough) > 1e-6:
            pb['metallicFactor'] = round(new_metal, 4)
            pb['roughnessFactor'] = round(new_rough, 4)
            changed += 1
    out = json.dumps(j, separators=(',', ':')).encode('utf-8')
    out += b' ' * ((4 - len(out) % 4) % 4)
    body = struct.pack('<I4s', len(out), b'JSON') + out + rest
    open(path, 'wb').write(struct.pack('<4sII', magic, version, 12 + len(body)) + body)
    return changed, len(j.get('materials', []))

for p in sys.argv[1:]:
    c, n = process(p)
    print(f'{c}/{n} materials changed: {p}')
