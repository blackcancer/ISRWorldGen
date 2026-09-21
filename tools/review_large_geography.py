#!/usr/bin/env python3
"""Full-domain review plus complete major-catchment crops from verified Core output.
No world generation, local rerouting, new moisture, interpolation or smoothing.
Python 3.10+, standard library only. Existing reviews are never overwritten.
"""
from __future__ import annotations
import argparse
from collections import Counter, deque
import hashlib
import html
import json
import math
import struct
from pathlib import Path
import audit_spatial_quality as audit
from export_spatial_png import categorical, png, read_pixels


def coast_distances(ocean, width, height, step):
    result = [None] * len(ocean); queue = deque()
    for i, value in enumerate(ocean):
        if value: result[i] = 0; queue.append(i)
    while queue:
        i = queue.popleft()
        for j, dx, dz in audit.neighbours(i, width, height):
            if abs(dx) + abs(dz) == 1 and result[j] is None:
                result[j] = result[i] + step; queue.append(j)
    return result


def path_lengths(receiver, width, step):
    lengths = [None] * len(receiver)
    for start in range(len(receiver)):
        trail = []; cursor = start
        while lengths[cursor] is None:
            trail.append(cursor); destination = receiver[cursor]
            if destination < 0:
                lengths[cursor] = 0.; break
            cursor = destination
        for i in reversed(trail):
            destination = receiver[i]
            if destination >= 0:
                distance = math.hypot(i % width - destination % width, i // width - destination // width) * step
                lengths[i] = lengths[destination] + distance
    return lengths


def export(directory: Path):
    output = directory / 'large-review'
    if output.exists(): raise FileExistsError('Previous large-domain review will not be overwritten.')
    raw = (directory/'fields.json').read_bytes(); meta_raw = (directory/'manifest.json').read_bytes()
    meta = json.loads(meta_raw)
    audit.require(hashlib.sha256(raw).hexdigest() == meta['fieldsSha256'], 'Source field hash mismatch.')
    fields = json.loads(raw); audit.validate(meta, fields)
    audit.require(meta['status'] == 'PASS' and meta['nativeGame'] == 'NOT_RUN', 'Unexpected source qualification.')
    w, h, step = meta['width'], meta['height'], meta['stepX']
    audit.require(w == h == 512 and step == meta['stepZ'], 'Expected a real 512 squared square world raster.')
    audit.require(meta['extent']['minX'] == meta['extent']['minZ'] == 0 and
        meta['extent']['maxXExclusive'] == w*step and meta['extent']['maxZExclusive'] == h*step, 'Incomplete world extent.')
    audit.require(meta['smallMountainWindowIncluded'] is False, 'Large-domain campaign must not substitute a selected peak window.')
    report, _, _ = audit.measure(meta, fields)
    land = [i for i in range(w*h) if not fields['ocean'][i]]
    distances = coast_distances(fields['ocean'], w, h, step)
    lengths = path_lengths(fields['receiver'], w, step)
    inland = []
    for low, high in ((0, 8192), (8192, 32768), (32768, 131072), (131072, None)):
        ids = [i for i in land if (distances[i] is None and high is None) or
            (distances[i] is not None and distances[i] >= low and (high is None or distances[i] < high))]
        wet = sum(fields['runoff'][i] > 0 for i in ids)
        inland.append(dict(minimumDistanceBlocks=low, maximumDistanceBlocks=high, landCells=len(ids),
            positiveRunoffCells=wet, positiveRunoffFraction=wet/len(ids) if ids else None,
            positiveDischargeCells=sum(fields['discharge'][i] > 0 for i in ids)))
    directions = Counter()
    for i in land:
        j = fields['receiver'][i]
        if j >= 0 and fields['discharge'][i] > 0:
            directions[f'{j%w-i%w},{j//w-i//w}'] += 1
    groups = {}
    for i in land: groups.setdefault(fields['basin'][i], []).append(i)
    major = sorted(groups.items(), key=lambda pair: (-len(pair[1]), pair[0]))[:3]
    topo = [audit.colour(value-meta['seaLevel'], audit.SEA if fields['ocean'][i] else audit.LAND)
            for i, value in enumerate(fields['height'])]
    threshold = meta['displayMinimumDrainageArea']
    audit.require(type(threshold) in (int,float) and math.isfinite(threshold) and threshold>0,'Invalid display threshold.')
    report['water']['displayedWetLandNodes'] = sum(fields['discharge'][i]>0 and fields['drainage_area'][i]>=threshold for i in land)
    hydro = [((27,103,166) if fields['discharge'][i] > 0 else (159,109,60))
             if not fields['ocean'][i] and fields['drainage_area'][i] >= threshold else colour
             for i, colour in enumerate(topo)]
    outlines = list(topo)
    for i in land:
        if any(not fields['ocean'][j] and fields['basin'][j] != fields['basin'][i]
               for j, _, _ in audit.neighbours(i, w, h)):
            outlines[i] = (54,53,60)
    output.mkdir()
    layers = []
    def save(name, colours, width, height, description):
        pixels = b''.join(bytes(c) for c in colours)
        data = audit.encode_rgb(width, height, pixels); (output/name).write_bytes(data)
        layers.append(dict(path=name, width=width, height=height, sha256=hashlib.sha256(data).hexdigest(), description=description))
    save('full-heightmap.png', topo, w, h, 'Entire world; physical altitude, fixed colours; white is elevation, not snow.')
    save('full-hydrology.png', hydro, w, h, 'Entire world; blue = positive discharge, brown = dry potential axes; no channel width.')
    save('full-catchment-divides.png', outlines, w, h, 'Catchment identity transitions on the actual relief; raster outlines, not smoothed vectors.')
    save('full-regions.png', [categorical(value) for value in fields['region']], w, h,
        'Atlas ownership regions, not administrative regions or final biomes.')
    save('full-plates.png', [categorical(value) for value in fields['plate']], w, h,
        'Tectonic plate identities; names and IDs are in the original manifest.')
    rain_palette = [(0,(226,207,158)),(.05,(210,217,177)),(.2,(139,184,162)),(.5,(70,147,157)),(1,(33,104,143)),(2,(30,52,98))]
    save('full-precipitation.png', [audit.colour(value,rain_palette) for value in fields['rain']], w,h,
        'Fixed precipitation scale 0..2 L/Ymod, not independently normalized; exact values remain in fields.json.')
    height_pixels=b''.join(struct.pack('>H',round(value/meta['worldHeight']*65535)) for value in fields['height'])
    raw_height=png(w,h,height_pixels,16,0,'Actual Core physical heights; linear 0..worldHeight; no native game qualification')
    audit.require(read_pixels(raw_height)==(w,h,16,0,height_pixels),'Height PNG round-trip failure.')
    (output/'full-heightmap-16bit.png').write_bytes(raw_height)
    layers.append(dict(path='full-heightmap-16bit.png',width=w,height=h,sha256=hashlib.sha256(raw_height).hexdigest(),
        description='Quantized physical heights; numeric raster, not a coloured map.',bitDepth=16,minimum=0,maximum=meta['worldHeight']))
    catchments = []
    for rank, (basin, ids) in enumerate(major, 1):
        xs, zs = [i%w for i in ids], [i//w for i in ids]
        margin = max(2, math.ceil(max(max(xs)-min(xs)+1, max(zs)-min(zs)+1)*.08))
        x0, x1 = max(0,min(xs)-margin), min(w,max(xs)+margin+1)
        z0, z1 = max(0,min(zs)-margin), min(h,max(zs)+margin+1)
        indices = [z*w+x for z in range(z0,z1) for x in range(x0,x1)]
        selected = set(ids)
        crop = [hydro[i] if i in selected or fields['ocean'][i] else (226,222,210) for i in indices]
        name = f'complete-catchment-{rank}.png'
        save(name, crop, x1-x0, z1-z0, 'Whole bounding box of a largest land catchment; subset of the global raster, NOT locally regenerated.')
        catchments.append(dict(rank=rank, basinIndex=basin, landCells=len(ids), landAreaBlocksSquared=len(ids)*step*step,
            bounds=dict(minX=x0*step,minZ=z0*step,maxXExclusive=x1*step,maxZExclusive=z1*step),
            pixelBounds=[x0,z0,x1,z1], sourceStepBlocks=step, longestRouteBlocks=max(lengths[i] for i in ids), path=name))
    report.update(profile=meta['profile'], fullExtent=meta['extent'], atlasSites=meta['atlasSites'],
        nominalSiteSpacingBlocks=math.sqrt(w*h*step*step/meta['atlasSites']), minimumRidgeWidthBlocks=meta['minimumRidgeWidth'],
        ridgeWidthInPixels=meta['minimumRidgeWidth']/step if meta['minimumRidgeWidth'] else None,
        inlandBands=inland, coastDistanceMetric='four-neighbour Manhattan distance; no-ocean cells enter the last band',
        wetReceiverDirections=dict(sorted(directions.items())), majorCatchments=catchments,
        longestLandRouteBlocks=max((lengths[i] for i in land),default=0),
        displayMinimumDrainageArea=threshold, algorithms=meta['algorithms'], atmosphere=meta['atmosphere'],
        layers=layers, pixelsAreBlocks=False, precipitationPalette=rain_palette, precipitationAbovePalette=sum(value>2 for value in fields['rain']),
        regionsLegend=meta['legend'], reviewCodeSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        scopeWarning='A larger world profile is NOT a zoom or a density-matched experiment. Narrow ridges and small streams can be unresolved. Numerical PASS is not geographic acceptance.')
    cards = ''.join('<figure><h2>'+html.escape(layer['path'])+'</h2><a href="'+layer['path']+'"><img src="'+layer['path']+'"></a><p>'+html.escape(layer['description'])+'</p></figure>' for layer in layers)
    palette = ''.join(f'<span style="display:inline-block;padding:10px;background:rgb{colour}">{value:+g} blocs</span>' for value,colour in audit.LAND)
    page = '<!doctype html><html lang="fr"><meta charset="utf-8"><title>ISRWorldGen — grandes emprises</title><style>body{font:16px system-ui;max-width:1500px;margin:30px auto;padding:0 20px}figure{margin:45px 0}img{width:min(100%,1300px);image-rendering:pixelated}p{line-height:1.6}pre{white-space:pre-wrap}</style><h1>Analyse du monde entier — '+html.escape(meta['profile'])+'</h1><p>Seed '+str(meta['seed'])+' ; commit '+html.escape(meta['commit'])+f' ; {w*step:,} × {h*step:,} blocs. Grille réelle : {w} × {h}, pas de {step:,} blocs. Les images ne sont pas des captures du jeu.</p><p>La carte complète reste la référence. Les trois découpes incluent chacune un bassin entier, choisi par aire décroissante. Aucun recalcul local du climat ou du drainage. Les différents profils ne sont pas des zooms d’un même monde.</p><p>La palette ne change pas par carte. Blanc = altitude, pas neige ; vert = altitude, pas végétation. Les petits reliefs sous la résolution de calcul ne sont pas validés par ces vues.</p>'+palette+cards+'<details><summary>Mesures complètes</summary><pre>'+html.escape(json.dumps(report,ensure_ascii=False,indent=2))+'</pre></details></html>'
    (output/'index.html').write_text(page,encoding='utf-8')
    audit.require((directory/'fields.json').read_bytes()==raw and (directory/'manifest.json').read_bytes()==meta_raw,'Source changed during review.')
    (output/'large-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2,allow_nan=False),encoding='utf-8')
    return report


def self_test():
    import tempfile
    audit.require(coast_distances([True]+[False]*8,3,3,10)[8]==40,'Wrong metric coast distance.')
    audit.require(coast_distances([False]*4,2,2,10)==[None]*4,'Invented ocean.')
    audit.require(path_lengths([1,3,3,-1],2,10)==[20.,10.,10.,0.],'Wrong receiver path length.')
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp); (root/'large-review').mkdir(); marker=root/'large-review'/'old';marker.write_bytes(b'keep')
        try: export(root)
        except FileExistsError: pass
        else: raise AssertionError('Existing review overwritten.')
        audit.require(marker.read_bytes()==b'keep','Prior review changed.')
    print('Large-domain review self-tests PASS; no geographic acceptance implied.')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path);parser.add_argument('--self-test',action='store_true');args=parser.parse_args()
    if args.self_test: self_test()
    if args.root:
        dirs=sorted(path.parent for path in args.root.glob('*/manifest.json'))
        audit.require(bool(dirs),'No completed source fixtures.')
        for directory in dirs:
            report=export(directory)
            print(json.dumps(dict(profile=report['profile'],seed=report['seed'],commit=report['sourceCommit'],
                geographicAcceptance=report['geographicAcceptance'],output=str(directory/'large-review')),ensure_ascii=False))
    audit.require(args.root is not None or args.self_test,'Specify --root or --self-test.')
