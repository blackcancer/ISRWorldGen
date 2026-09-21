#!/usr/bin/env python3
"""Read-only quality measurements and topographic PNGs from actual Core fields.

No world generation, resampling, filling, erosion, palette auto-stretch or water
injection. The source numeric PASS is not a geographical acceptance decision.
Python 3.10+, standard library only. Never overwrite a previous review directory.
"""
from __future__ import annotations
import argparse
from collections import Counter
import hashlib
import html
import json
import math
from pathlib import Path
import struct
import zlib

# Fixed elevations relative to the declared sea datum, in game blocks, NOT metres.
# White encodes altitude only. Ocean colour requires the independent ocean mask.
LAND = [(-64, (102, 133, 73)), (0, (80, 146, 75)), (16, (151, 183, 97)),
        (32, (216, 207, 131)), (64, (191, 151, 93)), (96, (137, 101, 75)),
        (112, (176, 167, 153)), (128, (242, 241, 232))]
SEA = [(-64, (15, 42, 86)), (-32, (32, 88, 133)), (-16, (68, 141, 176)),
       (0, (160, 209, 218))]
SLOPE = [(0, (241, 246, 231)), (.1, (189, 216, 159)), (1, (251, 220, 114)),
         (5, (222, 151, 66)), (15, (178, 87, 58)), (30, (98, 42, 57))]
ROUTING = {0: (20, 57, 92), 1: (116, 165, 111), 2: (207, 67, 62),
           3: (147, 99, 177), 4: (35, 35, 35), 5: (236, 216, 129)}
ROUTING_LABELS = {0: 'Ocean', 1: 'Descente maximale sur surface de routage',
                  2: 'Une pente descendante plus forte existe',
                  3: 'Terrain sous la surface virtuelle de routage',
                  4: 'Terminal terrestre explicite', 5: 'Aucune pente descendante stricte'}
FIELDS = {'height', 'routing_height', 'region', 'plate', 'landscape_family',
          'ocean', 'temperature', 'rain', 'runoff', 'recharge', 'soil_moisture',
          'discharge', 'drainage_area', 'receiver', 'basin'}


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def neighbours(i: int, w: int, h: int):
    x, z = i % w, i // w
    for dz in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if (dx or dz) and 0 <= x + dx < w and 0 <= z + dz < h:
                yield (z + dz) * w + x + dx, dx, dz


def validate(meta: dict, f: dict) -> None:
    require(meta['scope'] == 'CORE_SPATIAL_DIAGNOSTIC_NOT_FINAL_WORLD', 'Unexpected source scope.')
    w, h = meta['width'], meta['height']
    require(type(w) is int and type(h) is int and 2 <= w <= 1024 and 2 <= h <= 1024, 'Unsupported grid.')
    for k in ('stepX', 'stepZ', 'worldHeight'):
        require(type(meta[k]) in (int, float) and math.isfinite(meta[k]) and meta[k] > 0, 'Invalid spatial units.')
    require(math.isfinite(meta['seaLevel']), 'Invalid sea datum.')
    require(set(f) == FIELDS and all(len(v) == w * h for v in f.values()), 'Incomplete fields.')
    require(all(type(v) is bool for v in f['ocean']), 'Ocean mask must be Boolean, not height inferred.')
    for k in FIELDS - {'ocean'}:
        require(all(type(v) in (int, float) and math.isfinite(v) for v in f[k]), 'Nonfinite field: ' + k)
    for k in ('region', 'plate', 'landscape_family', 'basin'):
        require(all(type(v) is int and v >= 0 for v in f[k]), 'Invalid categorical ID: ' + k)
    for k in ('rain', 'runoff', 'recharge', 'discharge', 'drainage_area'):
        require(all(v >= 0 for v in f[k]), 'Negative water field: ' + k)
    for i, receiver in enumerate(f['receiver']):
        require(type(receiver) is int and -1 <= receiver < w * h, 'Invalid receiver.')
        require(0 <= f['height'][i] < meta['worldHeight'], 'Height outside world envelope.')
        require(f['routing_height'][i] >= f['height'][i], 'Routing surface below physical terrain.')
        require(0 <= f['soil_moisture'][i] <= 1, 'Soil moisture outside range.')
        require(not f['ocean'][i] or f['height'][i] < meta['seaLevel'], 'Ocean above datum.')
        if receiver >= 0:
            require(any(j == receiver for j, _, _ in neighbours(i, w, h)), 'Non-neighbour receiver.')
            require(f['routing_height'][receiver] <= f['routing_height'][i], 'Uphill virtual routing.')
            require(f['basin'][receiver] == f['basin'][i], 'Basin identity changes downstream.')
    # Functional-graph walk, O(n), including exact-height cycles that monotonicity misses.
    state = bytearray(w * h)
    for start in range(w * h):
        if state[start]:
            continue
        path = []; cursor = start
        while cursor >= 0 and state[cursor] == 0:
            state[cursor] = 1; path.append(cursor); cursor = f['receiver'][cursor]
        require(cursor < 0 or state[cursor] == 2, 'Receiver cycle.')
        for i in path:
            state[i] = 2


def quantile(values: list, p: float):
    if not values:
        return None
    v = sorted(values); index = (len(v) - 1) * p; lo = math.floor(index); hi = math.ceil(index)
    return v[lo] + (v[hi] - v[lo]) * (index - lo)


def slope_degrees(heights: list, i: int, w: int, h: int, sx: float, sz: float) -> float:
    x, z = i % w, i // w
    left, right = max(0, x - 1), min(w - 1, x + 1)
    top, bottom = max(0, z - 1), min(h - 1, z + 1)
    gx = (heights[z * w + right] - heights[z * w + left]) / ((right - left) * sx)
    gz = (heights[bottom * w + x] - heights[top * w + x]) / ((bottom - top) * sz)
    return math.degrees(math.atan(math.hypot(gx, gz)))


def measure(meta: dict, f: dict) -> tuple[dict, list, list]:
    validate(meta, f)
    w, h = meta['width'], meta['height']; sx, sz = meta['stepX'], meta['stepZ']
    land = [i for i, ocean in enumerate(f['ocean']) if not ocean]
    heights, routing, receivers = f['height'], f['routing_height'], f['receiver']
    slopes = [slope_degrees(heights, i, w, h, sx, sz) for i in range(w * h)]
    codes = [0] * (w * h); eligible = mismatches = uphill = 0
    for i in land:
        r = receivers[i]
        if r >= 0 and heights[r] > heights[i]:
            uphill += 1
        if r < 0:
            codes[i] = 4
        elif routing[i] > heights[i]:
            codes[i] = 3
        else:
            best = max((routing[i] - routing[j]) / math.hypot(dx * sx, dz * sz)
                       for j, dx, dz in neighbours(i, w, h))
            if best <= 0:
                codes[i] = 5
            else:
                eligible += 1
                distance = math.hypot((r % w - i % w) * sx, (r // w - i // w) * sz)
                actual = (routing[i] - routing[r]) / distance
                mismatch = actual < best - max(1e-14, abs(best) * 1e-12)
                mismatches += int(mismatch); codes[i] = 2 if mismatch else 1
    ratio = lambda count, total: count / total if total else None
    rain = [f['rain'][i] for i in land]; land_h = [heights[i] for i in land]
    land_s = [slopes[i] for i in land]
    terminal_count = sum(r < 0 for r in receivers)
    marine_terminals = sum(r < 0 and f['ocean'][i] for i, r in enumerate(receivers))
    report = dict(schemaVersion=1, sourceCommit=meta['commit'], seed=meta['seed'],
        sourceFieldsSha256=meta['fieldsSha256'], sourceNumericStatus=meta.get('status'),
        auditExecution='COMPLETED', geographicAcceptance='NOT_EVALUATED',
        resolution=dict(width=w, height=h, stepX=sx, stepZ=sz, units='blocks; not metres'),
        relief=dict(seaLevel=meta['seaLevel'], sampledMaximum=max(heights),
                    sampledMaximumAboveSea=max(heights) - meta['seaLevel'],
                    landMedianAboveSea=(quantile(land_h, .5) - meta['seaLevel']) if land else None,
                    landSlopeMedianDegrees=quantile(land_s, .5), landSlopeP99Degrees=quantile(land_s, .99),
                    warning='Slopes are resolved at raster spacing, not block-scale slopes or summit prominence.'),
        water=dict(landCells=len(land), landZeroRunoff=sum(f['runoff'][i] == 0 for i in land),
                   landZeroRunoffFraction=ratio(sum(f['runoff'][i] == 0 for i in land), len(land)),
                   landZeroDischarge=sum(f['discharge'][i] == 0 for i in land),
                   landMedianRain=quantile(rain, .5),
                   displayedWetLandNodes=sum(f['discharge'][i] > 0 and f['drainage_area'][i] >= 8 * sx * sz for i in land)),
        drainage=dict(terminalCount=terminal_count, marineTerminalCount=marine_terminals,
                      landOutletCount=len({f['basin'][i] for i in land}),
                      raisedLandCells=sum(routing[i] > heights[i] for i in land),
                      physicallyUphillLinks=uphill, steepestDescentEligible=eligible,
                      nonSteepestLinks=mismatches, nonSteepestFraction=ratio(mismatches, eligible),
                      slopeComparisonTolerance='max(1e-14, 1e-12 * best slope)',
                      warning='Comparison uses virtual-surface D8 slope only at unraised land nodes. '
                              'Virtual filling is not a physical lake or excavated channel.'),
        limits=['Numerical PASS is not realistic-geography acceptance.',
                'No generation parameters, water supply or heights are altered by this audit.',
                'Potential drainage area does not prove the existence of a wet river.',
                'White is elevation, not snow; green is elevation, not vegetation.'])
    if 'legend' in meta:
        report['regionFamilies'] = dict(sorted(Counter(x['family'] for x in meta['legend']).items()))
    return report, slopes, codes


def colour(value: float, stops: list) -> tuple:
    if value <= stops[0][0]:
        return stops[0][1]
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if value <= b:
            t = (value - a) / (b - a)
            return tuple(round(x + t * (y - x)) for x, y in zip(ca, cb))
    return stops[-1][1]


def encode_rgb(w: int, h: int, pixels: bytes) -> bytes:
    require(len(pixels) == w * h * 3, 'Incorrect RGB length.')
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    data = b''.join(b'\0' + pixels[y*w*3:(y+1)*w*3] for y in range(h))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(data, 9)) + chunk(b'IEND', b'')


def export(directory: Path) -> dict:
    raw = (directory / 'fields.json').read_bytes(); metadata = (directory / 'manifest.json').read_bytes()
    meta = json.loads(metadata)
    require(hashlib.sha256(raw).hexdigest() == meta['fieldsSha256'], 'Source hash mismatch.')
    f = json.loads(raw); report, slopes, codes = measure(meta, f)
    output = directory / 'review'; output.mkdir(exist_ok=False)
    w, h = meta['width'], meta['height']; layers = []
    topo = [colour(a - meta['seaLevel'], SEA if f['ocean'][i] else LAND) for i, a in enumerate(f['height'])]
    def save(name, rgb, description, legend):
        native = b''.join(bytes(c) for c in rgb); data = encode_rgb(w, h, native)
        (output / (name + '.png')).write_bytes(data)
        rows = [b''.join(bytes(c) * 4 for c in rgb[y*w:(y+1)*w]) for y in range(h)]
        preview = encode_rgb(w*4, h*4, b''.join(row*4 for row in rows))
        (output / (name + '-preview.png')).write_bytes(preview)
        layers.append(dict(path=name + '.png', preview=name + '-preview.png', description=description,
                           legend=legend, sha256=hashlib.sha256(data).hexdigest(),
                           previewSha256=hashlib.sha256(preview).hexdigest()))
    save('heightmap-topographic', topo, 'Altitude physique — couleurs fixes, sans ombrage ni exaggeration.',
         dict(relativeAltitudeBlocks=LAND, oceanDepthBlocks=SEA, datum=meta['seaLevel'], clampedOutside=[-64,128]))
    save('slope', [SEA[0][1] if f['ocean'][i] else colour(v, SLOPE) for i, v in enumerate(slopes)],
         'Pente resolue au pas de mesure ; differences centrales, bords unilateraux.', dict(degrees=SLOPE))
    save('routing-audit', [ROUTING[c] for c in codes],
         'Rouge : direction non maximale ; violet : routage au-dessus du terrain physique.',
         {str(c): dict(rgb=ROUTING[c], label=ROUTING_LABELS[c]) for c in ROUTING})
    # Make dry potential paths visible, without inventing wet streams.
    dry_colour, wet_colour = (159, 109, 60), (27, 103, 166)
    threshold = 8 * meta['stepX'] * meta['stepZ']; potentials = []
    for i, base in enumerate(topo):
        if not f['ocean'][i] and f['drainage_area'][i] >= threshold:
            potentials.append(wet_colour if f['discharge'][i] > 0 else dry_colour)
        else:
            potentials.append(base)
    save('drainage-potential-vs-wet', potentials, 'Brun : axe potentiel sec ; bleu : debit positif. Pas une largeur de riviere.',
         dict(minimumAreaBlocksSquared=threshold, dry=dry_colour, wet=wet_colour))
    save('runoff-production', [SEA[0][1] if f['ocean'][i] else (wet_colour if v > 0 else (215, 201, 165))
                              for i, v in enumerate(f['runoff'])],
         'Production locale : bleu si ruissellement strictement positif, beige si nul.',
         dict(zero=[215,201,165], positive=wet_colour, ocean=SEA[0][1]))
    report['layers'] = layers
    report['renderCodeSha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    report['palettePolicy'] = 'fixed sea-relative block anchors; no per-image normalization; no hillshade'
    report['heightPaletteClipping'] = dict(landBelow=sum(not f['ocean'][i] and a < meta['seaLevel'] - 64 for i, a in enumerate(f['height'])),
        landAbove=sum(not f['ocean'][i] and a > meta['seaLevel'] + 128 for i, a in enumerate(f['height'])),
        oceanBelow=sum(f['ocean'][i] and a < meta['seaLevel'] - 64 for i, a in enumerate(f['height'])))
    cards = ''.join('<figure><h2>' + html.escape(layer['path']) + '</h2><a href="' + layer['path'] + '"><img src="' + layer['preview'] + '"></a><p>' + html.escape(layer['description']) + '</p><pre>' + html.escape(json.dumps(layer['legend'], ensure_ascii=False, indent=2)) + '</pre></figure>' for layer in layers)
    swatches = ''.join(f'<span style="display:inline-block;background:rgb{rgb};padding:12px;color:#111">{level:+g} blocs</span>' for level, rgb in LAND)
    page = '<!doctype html><html lang="fr"><meta charset="utf-8"><title>ISRWorldGen : audit geographique</title><style>body{font:16px system-ui;max-width:1050px;margin:25px auto;padding:16px}img{max-width:100%;image-rendering:pixelated}pre{white-space:pre-wrap}figure{margin:32px 0}p{line-height:1.55}</style><h1>Mesures du Core, pas un monde valide</h1><p>Memes hauteurs et memes eaux que la source. Aucune correction geographique n\'est simulee ici. Blanc : altitude, pas neige. Vert : altitude, pas vegetation.</p><p>Source ' + html.escape(str(meta['commit'])) + ' ; seed ' + html.escape(str(meta['seed'])) + f' ; pas {meta["stepX"]} x {meta["stepZ"]} blocs. Apercus x4 au plus proche.</p><h2>Legende — altitude relative a la mer</h2>' + swatches + cards + '</html>'
    (output / 'index.html').write_text(page, encoding='utf-8')
    require((directory / 'fields.json').read_bytes() == raw and (directory / 'manifest.json').read_bytes() == metadata, 'Source changed during export.')
    # Completion marker LAST: interruption cannot leave a claimed complete report.
    (output / 'quality-report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False), encoding='utf-8')
    return report


def self_test() -> None:
    import tempfile
    w=h=3; f={k: [0]*9 for k in FIELDS}; f['ocean']=[False]*9
    # Centre 10 -> diagonal 8 is lower, but cardinal 8.5 has a steeper slope.
    f['height']=[12,8.5,12,12,10,12,12,12,8]; f['routing_height']=list(f['height'])
    f['receiver']=[-1]*9; f['receiver'][4]=8; f['basin']=[0]*9
    meta=dict(width=3,height=3,stepX=1,stepZ=1,worldHeight=64,seaLevel=0,
              scope='CORE_SPATIAL_DIAGNOSTIC_NOT_FINAL_WORLD',commit='synthetic',seed=0,
              fieldsSha256='synthetic',status='PASS')
    report,_,codes=measure(meta,f)
    require(codes[4]==2 and report['drainage']['nonSteepestLinks']==1, 'Missed distance-dependent slope defect.')
    f['receiver'][4]=1; require(measure(meta,f)[2][4]==1, 'Rejected steepest cardinal link.')
    ramp=[2*x+3*z for z in range(3) for x in range(3)]
    require(all(abs(slope_degrees(ramp,i,3,3,2,3)-math.degrees(math.atan(math.sqrt(2))))<1e-12 for i in range(9)), 'Physical spacing or border gradient wrong.')
    require(colour(0,LAND)==LAND[1][1] and colour(128,LAND)==LAND[-1][1], 'Palette datum or summit anchor changed.')
    require(colour(-16,LAND) != colour(-16,SEA), 'Sub-sea land incorrectly became ocean.')
    require(colour(-16,SEA) == SEA[2][1], 'Ocean palette anchor changed.')
    f['height'][1]=10;f['routing_height'][1]=10;f['receiver'][1]=4
    try: validate(meta,f)
    except ValueError as exc: require('cycle' in str(exc), 'Wrong cycle refusal.')
    else: raise AssertionError('Flat cycle accepted.')
    f['receiver'][1]=-1;f['receiver'][4]=-1
    # Real export refusal must preserve both input files and all old output bytes.
    with tempfile.TemporaryDirectory() as temp:
        d=Path(temp);raw=json.dumps(f).encode();meta['fieldsSha256']=hashlib.sha256(raw).hexdigest()
        (d/'fields.json').write_bytes(raw);(d/'manifest.json').write_text(json.dumps(meta),encoding='utf-8')
        export(d);before={p.relative_to(d):p.read_bytes() for p in d.rglob('*') if p.is_file()}
        try: export(d)
        except FileExistsError: pass
        else: raise AssertionError('Existing review overwritten.')
        require(before=={p.relative_to(d):p.read_bytes() for p in d.rglob('*') if p.is_file()}, 'Refusal changed prior evidence.')
    print('spatial-quality self-test: PASS (synthetic diagnostics only)')


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,help='Parent containing fixture directories (or one fixture).')
    parser.add_argument('--self-test',action='store_true')
    args=parser.parse_args()
    if args.self_test: self_test()
    if args.root:
        dirs=[args.root] if (args.root/'fields.json').is_file() else sorted(p.parent for p in args.root.glob('*/fields.json'))
        require(bool(dirs),'No source fields found.')
        for directory in dirs:
            result=export(directory)
            print(json.dumps(dict(sourceCommit=result['sourceCommit'],geographicAcceptance=result['geographicAcceptance'],
                                  output=str(directory/'review')),ensure_ascii=False))
    require(args.root is not None or args.self_test,'Specify --root or --self-test.')
