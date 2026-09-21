#!/usr/bin/env python3
"""Lossless diagnostic PNGs from verified C# fields; Python standard library only.
No generation, smoothing, interpolation or edits to source data. Output is a new
png/ directory; existing results are never overwritten. Native-resolution rasters
and explicitly nearest-neighbour x4 viewing copies have separate paths.
"""
from __future__ import annotations
import argparse
import hashlib
import html
import json
import math
from pathlib import Path
import struct
import zlib


def check(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)


def png(width: int, height: int, pixels: bytes, depth: int, colour_type: int, note: str) -> bytes:
    check(0 < width <= 4096 and 0 < height <= 4096, 'Invalid raster size.')
    check((depth, colour_type) in {(8, 2), (16, 0)}, 'Unsupported PNG encoding.')
    stride = width * (3 if colour_type == 2 else 2)
    check(len(pixels) == stride * height, 'Pixel payload has wrong size.')
    filtered = b''.join(b'\x00' + pixels[y * stride:(y + 1) * stride] for y in range(height))
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, depth, colour_type, 0, 0, 0))
            + chunk(b'tEXt', b'Description\x00' + note.encode('ascii'))
            + chunk(b'IDAT', zlib.compress(filtered, 9)) + chunk(b'IEND', b''))


def read_pixels(data: bytes) -> tuple[int, int, int, int, bytes]:
    check(data[:8] == b'\x89PNG\r\n\x1a\n', 'Invalid PNG signature.')
    cursor = 8; compressed = bytearray(); header = None; ended = False
    while cursor < len(data):
        length = struct.unpack_from('>I', data, cursor)[0]
        kind = data[cursor + 4:cursor + 8]; payload = data[cursor + 8:cursor + 8 + length]
        check(struct.unpack_from('>I', data, cursor + 8 + length)[0] == zlib.crc32(kind + payload) & 0xffffffff, 'PNG CRC mismatch.')
        cursor += 12 + length
        if kind == b'IHDR': header = struct.unpack('>IIBBBBB', payload)
        elif kind == b'IDAT': compressed.extend(payload)
        elif kind == b'IEND': ended = True; break
    check(header is not None and ended and cursor == len(data), 'Incomplete or trailing PNG data.')
    w, h, depth, kind, compression, filtering, interlace = header
    check((depth, kind) in {(8, 2), (16, 0)} and (compression, filtering, interlace) == (0, 0, 0), 'Unexpected PNG header.')
    stride = w * (3 if kind == 2 else 2); raw = zlib.decompress(compressed)
    check(len(raw) == h * (stride + 1), 'Unexpected decoded PNG size.')
    check(all(raw[y * (stride + 1)] == 0 for y in range(h)), 'Unexpected row filter.')
    return w, h, depth, kind, b''.join(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)] for y in range(h))


def upscale(rgb: bytes, w: int, h: int, factor: int = 4) -> bytes:
    rows = []
    for y in range(h):
        row = b''.join(rgb[(y * w + x) * 3:(y * w + x + 1) * 3] * factor for x in range(w))
        rows.extend([row] * factor)
    return b''.join(rows)


def categorical(value: int) -> tuple[int, int, int]:
    digest = hashlib.sha256(('isr-categorical-v1:' + str(value)).encode('ascii')).digest()
    return tuple(48 + component % 176 for component in digest[:3])


def blend(a: tuple, b: tuple, fraction: float) -> tuple:
    t = min(1., max(0., fraction))
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def validate_fields(meta: dict, fields: dict) -> None:
    check(meta['scope'] == 'CORE_SPATIAL_DIAGNOSTIC_NOT_FINAL_WORLD' and meta['status'] == 'PASS', 'Unqualified source scope.')
    check(meta['nativeGame'] == 'NOT_RUN', 'A Core raster must not claim native qualification.')
    w, h = meta['width'], meta['height']; n = w * h
    check(w == h == 256 and meta['stepX'] > 0 and meta['stepZ'] > 0, 'Unexpected diagnostic grid.')
    required = {'height','routing_height','region','plate','landscape_family','ocean','temperature','rain','runoff','recharge','soil_moisture','discharge','drainage_area','receiver','basin'}
    check(set(fields) == required, 'Missing or unexpected fields.')
    check(all(len(values) == n for values in fields.values()), 'Truncated source raster.')
    for name, values in fields.items():
        check(all(isinstance(v, (int, float)) and math.isfinite(v) for v in values), 'Nonfinite field: ' + name)
    for i, receiver in enumerate(fields['receiver']):
        check(isinstance(receiver, int) and -1 <= receiver < n, 'Receiver outside raster.')
        if receiver >= 0:
            check(fields['routing_height'][receiver] <= fields['routing_height'][i], 'Uphill routing.')
            check(fields['basin'][receiver] == fields['basin'][i], 'A receiver crosses basin identity.')
        check(0 <= fields['height'][i] < meta['worldHeight'], 'Height out of envelope.')
        check(fields['routing_height'][i] >= fields['height'][i], 'Routing surface below physical ground.')
        check(0 <= fields['soil_moisture'][i] <= 1 and fields['discharge'][i] >= 0, 'Invalid hydric domain.')
        check(not fields['ocean'][i] or fields['height'][i] < meta['seaLevel'], 'Ocean above sea level.')


def export(directory: Path) -> dict:
    meta = json.loads((directory / 'manifest.json').read_text(encoding='utf-8'))
    source = (directory / 'fields.json').read_bytes()
    check(hashlib.sha256(source).hexdigest() == meta['fieldsSha256'], 'Source checksum mismatch.')
    fields = json.loads(source); validate_fields(meta, fields)
    output = directory / 'png'; output.mkdir(exist_ok=False)
    (output / 'preview').mkdir()
    w, h = meta['width'], meta['height']; n = w * h
    note = f"ISRWorldGen Core test; commit={meta['commit']}; seed={meta['seed']}; grid={w}x{h}; not a game screenshot"
    layers = []

    def save(name: str, pixels: bytes, depth: int, kind: int, details: dict) -> None:
        encoded = png(w, h, pixels, depth, kind, note)
        check(read_pixels(encoded) == (w, h, depth, kind, pixels), 'PNG pixel round trip failed.')
        (output / (name + '.png')).write_bytes(encoded)
        if kind == 0:
            rgb = b''.join(bytes([value >> 8]) * 3 for (value,) in struct.iter_unpack('>H', pixels))
        else: rgb = pixels
        viewing = png(w * 4, h * 4, upscale(rgb, w, h), 8, 2, note + '; nearest-neighbour x4, no extra samples')
        (output / 'preview' / (name + '.png')).write_bytes(viewing)
        layers.append(dict(path=name + '.png', sha256=hashlib.sha256(encoded).hexdigest(),
                           preview='preview/' + name + '.png', previewSha256=hashlib.sha256(viewing).hexdigest(),
                           width=w, height=h, bitDepth=depth, **details))

    def scalar(name: str, field: str, low: float, high: float, unit: str, logarithmic: bool = False) -> None:
        values = fields[field]
        transform = math.log1p if logarithmic else float
        a, b = transform(low), transform(high)
        pixels = b''.join(struct.pack('>H', round(min(1., max(0., (transform(v) - a) / (b - a))) * 65535)) for v in values)
        save(name, pixels, 16, 0, dict(source=field, units=unit, palette='greyscale16-v1',
            paletteMin=low, paletteMax=high, logarithmic=logarithmic, observedMin=min(values), observedMax=max(values),
            valuesBelowPalette=sum(v < low for v in values), valuesAbovePalette=sum(v > high for v in values)))

    scalar('heightmap-16bit', 'height', 0, meta['worldHeight'], 'blocks')
    scalar('routing-height-16bit', 'routing_height', 0, meta['worldHeight'], 'blocks; virtual drainage surface')
    scalar('temperature-16bit', 'temperature', -40, 40, 'model Celsius')
    scalar('precipitation-16bit', 'rain', 0, 16, 'L/Ymod')
    scalar('runoff-16bit', 'runoff', 0, 16, 'L/Ymod')
    scalar('recharge-16bit', 'recharge', 0, 16, 'L/Ymod')
    scalar('soil-moisture-16bit', 'soil_moisture', 0, 1, 'fraction, not fertility')
    scalar('flow-accumulation-16bit', 'discharge', 0, 1e12, 'L^3/Ymod', True)
    for name, field in (('regions','region'), ('plates','plate'), ('landscape-families','landscape_family'), ('basins','basin')):
        pixels = b''.join(bytes((15, 45, 85) if name == 'basins' and fields['ocean'][i] else categorical(value)) for i, value in enumerate(fields[field]))
        save(name, pixels, 8, 2, dict(source=field, units='categorical ID', palette='sha256-isr-categorical-v1',
             oceanMasked=name == 'basins', legend={str(value): categorical(value) for value in sorted(set(fields[field]))}))
    relief = bytearray(); hydro = bytearray(); sea = meta['seaLevel']
    for i, altitude in enumerate(fields['height']):
        if fields['ocean'][i]: colour = blend((8, 25, 58), (65, 139, 177), altitude / sea)
        elif altitude < sea: colour = (144, 110, 66)
        else:
            t = (altitude - sea) / (meta['worldHeight'] - sea)
            colour = blend((72, 116, 68), (159, 123, 83), t * 2) if t < 0.5 else blend((159, 123, 83), (238, 235, 219), (t - 0.5) * 2)
        relief.extend(colour)
        gray = round(55 + 170 * altitude / meta['worldHeight'])
        if fields['ocean'][i]: river = (15, 45, 85)
        elif fields['drainage_area'][i] >= 8 * meta['stepX'] * meta['stepZ'] and fields['discharge'][i] > 0:
            river = blend((49, 143, 201), (5, 43, 107), math.log1p(fields['discharge'][i]) / math.log1p(1e12))
        else: river = (gray, gray, gray)
        hydro.extend(river)
    save('relief', bytes(relief), 8, 2, dict(source='height + ocean', units='blocks', palette='fixed-hypsometry-v1',
         seaLevel=sea, paletteMin=0, paletteMax=meta['worldHeight'], shading='none'))
    save('hydrology', bytes(hydro), 8, 2, dict(source='height + ocean + drainage_area + discharge', units='L^3/Ymod',
         palette='flow-blue-fixed-1e12-v1', minimumDisplayedArea=8 * meta['stepX'] * meta['stepZ'],
         geometry='actual raster nodes; not channel width, carved rivers or final erosion'))

    report = dict(schemaVersion=1, status='PASS', scope=meta['scope'], commit=meta['commit'], seed=meta['seed'],
                  sourceSha256=meta['fieldsSha256'], layers=layers, nativeGame='NOT_RUN',
                  previewResampling='nearest-neighbour x4; native raster remains 256x256')
    (output / 'manifest-png.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    intro = (f"Seed {meta['seed']} · commit {meta['commit']} · {w}×{h} mesures sur "
             f"{meta['extent']['maxXExclusive']}×{meta['extent']['maxZExclusive']} blocs. "
             f"Une mesure tous les {meta['stepX']} blocs. X vers la droite, +Z vers le bas.")
    warning = ('Résultats réels du Core sur une fixture de diagnostic. Relief initial, sans érosion finale ni '
               'matérialisation en jeu. Sol uniforme de test ; fertilité, minerais, végétation, neige et cavernes non représentés. '
               'Les régions sont les cellules Voronoï dominantes, pas les régions techniques natives. '
               'Les teintes claires du relief désignent une altitude, jamais une preuve de neige. '
               'Les PNG 16 bits utilisent les bornes fixes du manifeste. Les aperçus ×4 ne créent aucun détail. '
               'Ouvrir fields.json pour les valeurs exactes, manifest.json pour les paramètres, manifest-png.json pour les palettes.')
    cards = ''.join('<figure><a href="' + layer['path'] + '"><img src="' + layer['preview'] + '" alt="' + layer['path'] + '"></a><figcaption>'
                    + html.escape(layer['path'] + ' — ' + layer['units']) + '</figcaption></figure>' for layer in layers)
    page = '<!doctype html><meta charset="utf-8"><title>ISRWorldGen — cartes de test</title><style>body{font:16px system-ui;max-width:1500px;margin:30px auto;padding:0 20px}section{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:20px}figure{margin:0}img{width:100%;image-rendering:pixelated}p{line-height:1.6}</style><h1>ISRWorldGen — cartes de diagnostic</h1><p>' + html.escape(intro) + '</p><p>' + html.escape(warning) + '</p><section>' + cards + '</section>'
    (output / 'index.html').write_text(page, encoding='utf-8')
    (output / 'README.txt').write_text(intro + '\n\n' + warning + '\n', encoding='utf-8')
    check((directory / 'fields.json').read_bytes() == source, 'Exporter mutated source fields.')
    return dict(commit=meta['commit'], pngLayers=len(layers), output=str(output), sourceSha256=meta['fieldsSha256'])


def self_test() -> None:
    for w, h in ((1, 1), (3, 2)):
        for depth, kind in ((16, 0), (8, 2)):
            pixels = bytes(i % 256 for i in range(w * h * (2 if kind == 0 else 3)))
            encoded = png(w, h, pixels, depth, kind, 'test')
            check(read_pixels(encoded) == (w, h, depth, kind, pixels), 'PNG roundtrip failed.')
            damaged = bytearray(encoded); damaged[-1] ^= 1
            try: read_pixels(bytes(damaged))
            except ValueError: pass
            else: raise AssertionError('Corrupt PNG accepted.')
    rgb = bytes([1, 2, 3, 4, 5, 6]); enlarged = upscale(rgb, 2, 1, 4)
    check(enlarged == (bytes([1, 2, 3]) * 4 + bytes([4, 5, 6]) * 4) * 4, 'Nearest-neighbour mismatch.')
    print('PNG_ENCODER_SELF_TESTS_PASS')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test: self_test()
    if args.root:
        manifests = sorted(args.root.glob('*/manifest.json'))
        check(bool(manifests), 'No completed C# spatial fixture was found.')
        for manifest in manifests: print(json.dumps(export(manifest.parent)))
    check(args.self_test or args.root is not None, 'Specify --self-test or --root.')
