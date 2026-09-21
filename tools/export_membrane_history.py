#!/usr/bin/env python3
"""Encode actual material/mechanical fields; display bounds are not solver tolerances."""
import argparse, hashlib, json, math, struct
from pathlib import Path
import export_tectonic_history as base

ALGORITHM = 'material-membrane-history-v1/material-bound-history-v3-carrier-resolved-origin-fluxes'
VELOCITY_LIMIT = 4000.0
ENCODING_POLICY = 'native-mac-derivative-bounds-v2'


def mechanical_ranges(history, manifest):
    width, height = manifest['width'], manifest['height']
    if type(width) is not int or type(height) is not int or not 4 <= width <= 512 or not 4 <= height <= 512:
        raise ValueError('Invalid native mechanical grid')
    dx, dz = history['ReferenceWidth'] / width, history['ReferenceLength'] / height
    if not all(math.isfinite(v) and v > 0 for v in (dx, dz)):
        raise ValueError('Invalid reference metric')
    # Each difference is bounded by 2*V. Use reference-unit spacing,
    # not resized game blocks and not the observed extrema of this seed.
    bound = 2 * VELOCITY_LIMIT * (1 / dx + 1 / dz)
    # C,O<=150 in the declared constitutive domain imply resistance<48.465.
    return {'resistance': (0, 50), 'east-velocity': (-VELOCITY_LIMIT, VELOCITY_LIMIT),
            'south-velocity': (-VELOCITY_LIMIT, VELOCITY_LIMIT),
            'divergence': (-bound, bound), 'shear-rate': (-bound, bound)}, dx, dz


def quantize(name, values, lo, hi):
    if not all(math.isfinite(v) and lo <= v <= hi for v in values):
        raise ValueError(f'Mechanical encoding domain {name}: declared [{lo},{hi}], '
                         f'observed [{min(values)},{max(values)}]; no clipping')
    codes = [round((v - lo) * 65535 / (hi - lo)) for v in values]
    return codes, b''.join(struct.pack('>H', v) for v in codes)


def verify_staggered_rates(fields, width, height, dx, dz):
    u, v = fields['east-velocity'], fields['south-velocity']
    maximum = 0.0
    for row in range(height):
        for col in range(width):
            i = row * width + col
            west = row * width + (col - 1) % width
            north = (row - 1) % height * width + col
            east = row * width + (col + 1) % width
            south = (row + 1) % height * width + col
            divergence = (u[i] - u[west]) / dx + (v[i] - v[north]) / dz
            shear = (u[south] - u[i]) / dz + (v[east] - v[i]) / dx
            maximum = max(maximum, abs(divergence - fields['divergence'][i]),
                          abs(shear - fields['shear-rate'][i]))
    if maximum > 1e-12:
        raise ValueError(f'Exported strain does not match the native velocity stencil: {maximum}')
    return maximum


def export(root):
    directories = sorted(root.glob('seed-*'))
    if len(directories) not in (1, 3):
        raise ValueError('Expected one seed or the complete campaign')
    prepared = []
    # Preflight the entire batch before creating a PNG directory. A failed
    # publication stays immutable; replay requires a fresh copy of raw fields.
    for directory in directories:
        if (directory / 'png').exists() or (directory / 'mechanics/png').exists():
            raise FileExistsError('Existing PNG evidence; use a fresh raw-field copy, never overwrite')
        m = json.loads((directory / 'manifest.json').read_text(encoding='utf-8'))
        if m['algorithm'] != ALGORITHM or not m['mechanics']['initialMaterialsIdentical']:
            raise ValueError('Invalid mechanical comparison provenance')
        if m['mechanics']['maximumRelativeForceResidual'] > 1e-9:
            raise ValueError('Unsolved force balance')
        rootm = directory / 'mechanics'
        mm = json.loads((rootm / 'manifest.json').read_text(encoding='utf-8'))
        ranges, dx, dz = mechanical_ranges(m, mm)
        w, h = mm['width'], mm['height']
        if set(mm['fields']) != set(ranges):
            raise ValueError('Mechanical field inventory mismatch')
        fields, encoded = {}, {}
        for name, field in mm['fields'].items():
            if field['path'] != name + '.f64le':
                raise ValueError('Unexpected mechanical source path')
            raw = (rootm / field['path']).read_bytes()
            if len(raw) != w * h * 8 or hashlib.sha256(raw).hexdigest() != field['sha256']:
                raise ValueError('Mechanical source hash/shape')
            values = [v for v, in struct.iter_unpack('<d', raw)]
            fields[name] = values
            encoded[name] = quantize(name, values, *ranges[name])
        rate_error = verify_staggered_rates(fields, w, h, dx, dz)
        prepared.append((rootm, mm, ranges, fields, encoded, rate_error))
    base.RANGES.update({'owner-fraction': (0, 1), 'legacy-initial-height': (0, 383),
        'initial-continental-thickness': (0, 65), 'initial-provinces': (-1, 5), 'baseline-height': (0, 383)})
    base.export(root)
    for rootm, m, ranges, values, encoded, rate_error in prepared:
        w, h = m['width'], m['height']
        out = rootm / 'png'; out.mkdir(exist_ok=False)
        report = {'policy': ENCODING_POLICY, 'nativeRateStencilError': rate_error, 'fields': {}}
        for name, field in m['fields'].items():
            lo, hi = ranges[name]; codes, pixels = encoded[name]
            image = base.png(w, h, pixels, 16, 0, 'Native mechanical field; fixed metric bounds; no height modification')
            if base.read_pixels(image) != (w, h, 16, 0, pixels):
                raise ValueError('Mechanical PNG roundtrip')
            (out / (name + '-16bit.png')).write_bytes(image)
            rgb = bytes(round(q * 255 / 65535) for q in codes for _ in range(3))
            (out / (name + '-preview.png')).write_bytes(base.png(w, h, rgb, 8, 2, 'Fixed native-resolution mechanical diagnostic'))
            report['fields'][name] = {'decode': [lo, hi], 'sourceSha256': field['sha256'],
                'maximumQuantizationError': max(abs(v - (lo + q * (hi - lo) / 65535)) for v, q in zip(values[name], codes))}
        (out / 'encoding.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    (root / 'index.html').write_text("<!doctype html><html lang='fr'><meta charset='utf-8'><title>Membrane — comparaison</title>"
      "<style>body{font:17px system-ui;max-width:1100px;margin:auto;padding:25px}img{max-width:100%;image-rendering:pixelated}</style>"
      "<h1>Résistance des matériaux — candidat non accepté géographiquement</h1><p>Chaque image couvre le monde entier de 1 000 000 × 1 000 000 blocs. "
      "Heightmaps solides Y0..383, sans eau, fond marin conservé, aucune érosion. Matériaux 512² pour la campagne principale ; "
      "mécanique 128². Les vitesses de faces sont interpolées pour transporter les matériaux, pas les altitudes. "
      "Les bornes des vitesses et déformations sont indiquées dans mechanics/png/encoding.json : ce ne sont pas des seuils physiques d'acceptation.</p>" + ''.join(
        f"<h2>{d.name}</h2><h3>Référence cinématique, état initial identique</h3><img src='{d.name}/png/baseline-height-preview.png'>"
        f"<h3>Réponse visqueuse hétérogène</h3><img src='{d.name}/png/height-preview.png'><p><a href='{d.name}/png/height-16bit.png'>Heightmap 16 bits</a></p>"
        f"<h3>Résistance au pas mécanique natif</h3><img src='{d.name}/mechanics/png/resistance-preview.png'>" for d in directories) + "</html>", encoding='utf-8')


def self_test():
    ranges, dx, dz = mechanical_ranges({'ReferenceWidth': 1000000, 'ReferenceLength': 1000000}, {'width': 128, 'height': 128})
    assert abs(ranges['shear-rate'][1] - 2.048) < 1e-14
    values = [-.11023116165482506, .15, -.15]
    codes, pixels = quantize('pinned-shear-regression', values, *ranges['shear-rate'])
    image = base.png(3, 1, pixels, 16, 0, 'Pinned former display failure')
    assert base.read_pixels(image) == (3, 1, 16, 0, pixels)
    lo, hi = ranges['shear-rate']
    assert max(abs(v - (lo + q * (hi - lo) / 65535)) for v, q in zip(values, codes)) <= (hi - lo) / 131070
    upper = .25 + 2.5 * 150 / 35 + 150 / 7 * 1.75
    quantize('constitutive-bound', [0, upper], *ranges['resistance'])
    for invalid in [float('nan'), float('inf'), hi + 1]:
        try: quantize('reject', [invalid], lo, hi)
        except ValueError: pass
        else: raise AssertionError('Invalid value clipped or accepted')
    assert verify_staggered_rates({'east-velocity': [2] * 16, 'south-velocity': [-3] * 16,
        'divergence': [0] * 16, 'shear-rate': [0] * 16}, 4, 4, 2, 3) == 0
    try: mechanical_ranges({'ReferenceWidth': float('nan'), 'ReferenceLength': 1000000}, {'width': 128, 'height': 128})
    except ValueError: pass
    else: raise AssertionError('Invalid metric accepted')
    print('Mechanical encoder regression checks passed; no solver tolerance changed')


if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('--root', type=Path); p.add_argument('--self-test', action='store_true')
    a = p.parse_args(); self_test()
    if a.root is not None: export(a.root)
    elif not a.self_test: p.error('--root or --self-test required')
