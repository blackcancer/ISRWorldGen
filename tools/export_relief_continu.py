#!/usr/bin/env python3
"""RCV-1.0 review exports from immutable C# fields; never a terrain generator.

Requires NumPy and Pillow OUTSIDE the mod. PNGs are encoded by the existing
standard-library encoder and decoded independently by Pillow. Output directories
are exclusively reserved; interrupted attempts stay marked INCOMPLETE. Retry in
another directory. An output is valid only with its verified COMPLETE.json.
"""
from __future__ import annotations
import argparse
import base64
import csv
import hashlib
import io
import json
import math
import os
from pathlib import Path
import platform
import struct
import sys
import zlib
import numpy as np
from PIL import Image, __version__ as pillow_version
from export_spatial_png import png

ALGORITHM = 'rcv-exact-solid-review-v1'
LO, HI, EPSILON = 0., 383., 1e-12
STOPS = np.array([[16,24,32],[48,70,90],[98,120,139],[164,180,191],[244,241,230]], dtype=float)


def check(ok, message):
    if not ok:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True, indent=2)+'\n').encode('utf-8')


def field_check(a, lo=LO, hi=HI):
    check(a.ndim == 2 and min(a.shape) >= 2 and max(a.shape) <= 4096, 'Invalid 2D raster budget')
    check(np.isfinite(a).all() and np.isfinite([lo,hi]).all() and lo < hi, 'Nonfinite raster/range')
    check(a.min() >= lo and a.max() <= hi, 'Out-of-range altitude: no clipping')


def codes(a, bits=16, lo=LO, hi=HI):
    field_check(a, lo, hi)
    check(bits in (8,16), 'Only 8/16 bit')
    return np.rint((a-lo)*((1 << bits)-1)/(hi-lo)).astype(np.uint16 if bits == 16 else np.uint8)


def palette():
    t = np.arange(256)/255*4
    k = np.minimum(t.astype(int),3)
    return np.rint(STOPS[k]+(STOPS[k+1]-STOPS[k])*(t-k)[:,None]).astype(np.uint8)


def inspect_png(raw):
    """Validate chunks/CRC/structure, independently of the encoder's decoder."""
    check(raw[:8] == b'\x89PNG\r\n\x1a\n', 'PNG signature')
    cursor, names, header = 8, [], None
    while cursor < len(raw):
        check(cursor+12 <= len(raw), 'Truncated PNG chunk')
        size, = struct.unpack_from('>I',raw,cursor)
        check(cursor+12+size <= len(raw), 'Truncated PNG payload')
        kind = raw[cursor+4:cursor+8]; data = raw[cursor+8:cursor+8+size]
        crc, = struct.unpack_from('>I',raw,cursor+8+size)
        check(crc == zlib.crc32(kind+data)&0xffffffff, 'PNG CRC')
        names.append(kind.decode('ascii'))
        if kind == b'IHDR':
            check(header is None and cursor == 8 and size == 13, 'Duplicate/misplaced PNG header')
            header = struct.unpack('>IIBBBBB',data)
        cursor += 12+size
        if kind == b'IEND':
            check(size == 0 and cursor == len(raw), 'PNG trailing data')
            break
    check(header is not None and names[-1] == 'IEND' and 'IDAT' in names, 'Incomplete PNG')
    check(not set(names)&{'gAMA','sRGB','iCCP','cICP','cHRM','tRNS','PLTE'}, 'Numeric PNG transforms/alpha/palette')
    return header, names


def render(a):
    q = codes(a); g = codes(a,8); nz,nx = a.shape
    rgb = np.repeat(g[...,None],3,axis=2)
    colored = palette()[g]
    out = {
        'height-16bit.png': png(nx,nz,q.astype('>u2').tobytes(),16,0,'RCV numeric solid height; Y0-383-v1'),
        'height-preview.png': png(nx,nz,rgb.tobytes(),8,2,'RCV global greyscale; no overlay; device RGB'),
        'height-continuous-color.png': png(nx,nz,colored.tobytes(),8,2,'RCV altitude-sequentielle-v1; no sea classification')}
    for name, raw in out.items():
        header,_ = inspect_png(raw)
        expected = q if '16bit' in name else rgb if name == 'height-preview.png' else colored
        check(header[:4] == (nx,nz,16,0) if '16bit' in name else header[:4] == (nx,nz,8,2), 'PNG format')
        with Image.open(io.BytesIO(raw)) as decoded:
            check(np.array_equal(np.asarray(decoded),expected), 'Pillow pixel mismatch')
    err = float(np.max(np.abs(a-q.astype(float)*(HI-LO)/65535-LO)))
    check(err <= (HI-LO)/131070+EPSILON, 'Quantization error')
    return out, err


def geometry(m):
    nx,nz = m['width'],m['height']
    check(type(nx) is int and type(nz) is int and 2 <= nx <= 4096 and 2 <= nz <= 4096, 'Raster dimensions')
    width,length = m['worldWidthBlocks'],m['worldLengthBlocks']
    check(all(isinstance(v,(int,float)) and math.isfinite(v) and v > 0 for v in (width,length)), 'World extent')
    boundary = m.get('boundary','')
    check(boundary.startswith('PERIODIC_PLANAR') or boundary == 'NON_PERIODIC', 'Undeclared boundary policy')
    return dict(nx=nx,nz=nz,width=width,length=length,dx=width/nx,dz=length/nz,
                periodic=boundary.startswith('PERIODIC'),xmin=0.,zmin=0.,axes='X right, Z down; cell centres')


def load(source, field):
    source = Path(source).resolve()
    manifest_raw = (source/'manifest.json').read_bytes(); m=json.loads(manifest_raw)
    check(field in ('height','initial-height'), 'Only solid height fields')
    g=geometry(m)
    check(m.get('waterSurfacePresent') is False and m.get('seabedMasked') is False, 'Source must explicitly be unmasked solid terrain')
    info=m['fields'][field]
    check('float64 little-endian row-major X right Z down' == info['encoding'], 'Unsupported source layout')
    check('solid Y' in info['units'], 'Not a solid altitude field')
    path=source/info['path']
    check(not Path(info['path']).is_absolute() and path.resolve().is_relative_to(source), 'Source path outside manifest directory')
    raw=path.read_bytes()
    check(len(raw)==g['nx']*g['nz']*8 and sha(raw)==info['sha256'], 'Source length/hash mismatch')
    a=np.frombuffer(raw,dtype='<f8').reshape(g['nz'],g['nx']);field_check(a)
    check(float(a.min())==info['minimum'] and float(a.max())==info['maximum'], 'Source extrema mismatch')
    return m,g,a,[(source/'manifest.json',manifest_raw),(path,raw)]


def gradients(a,g):
    if g['periodic']:
        return (np.roll(a,-1,1)-np.roll(a,1,1))/(2*g['dx']), (np.roll(a,-1,0)-np.roll(a,1,0))/(2*g['dz'])
    z,x=np.gradient(a,g['dz'],g['dx'],edge_order=1)
    return x,z


def sample(a,g,x,z):
    """Bilinear sample on cell-centred data. No extrapolation for open grids."""
    x,z=np.broadcast_arrays(np.asarray(x,dtype=float),np.asarray(z,dtype=float))
    check(np.isfinite(x).all() and np.isfinite(z).all(), 'Invalid sample coordinate')
    if g['periodic']:
        x=np.remainder(x,g['width']);z=np.remainder(z,g['length'])
    u=x/g['dx']-.5;v=z/g['dz']-.5
    # Roundoff-only snapping of coordinate transforms at exact cell centres;
    # never snap heights or introduce a finite geographic tolerance.
    def snap(coord):
        nearest=np.rint(coord);bound=8*np.finfo(float).eps*np.maximum(1,np.abs(coord))
        return np.where(np.abs(coord-nearest)<=bound,nearest,coord)
    u,v=snap(u),snap(v)
    if not g['periodic']:
        check((u>=0).all() and (u<=g['nx']-1).all() and (v>=0).all() and (v<=g['nz']-1).all(), 'Sample outside nonperiodic centre domain')
    ix=np.floor(u).astype(int);iz=np.floor(v).astype(int);tx=u-ix;tz=v-iz
    if g['periodic']:
        x0=ix%g['nx'];x1=(ix+1)%g['nx'];z0=iz%g['nz'];z1=(iz+1)%g['nz']
    else:
        x0=ix;x1=np.minimum(ix+1,g['nx']-1);z0=iz;z1=np.minimum(iz+1,g['nz']-1)
    return (1-tz)*((1-tx)*a[z0,x0]+tx*a[z0,x1])+tz*((1-tx)*a[z1,x0]+tx*a[z1,x1])


def profiles(a,g):
    result=[]
    for axis in ('X','Z'):
        for f in (.25,.5,.75):
            if axis=='X': x=(np.arange(g['nx'])+.5)*g['dx'];z=np.full_like(x,f*g['length']);distance=x
            else: z=(np.arange(g['nz'])+.5)*g['dz'];x=np.full_like(z,f*g['width']);distance=z
            y=sample(a,g,x,z)
            result.append(dict(id=axis+str(int(100*f)),axis=axis,fraction=f,x=x.tolist(),z=z.tolist(),
                               distance=distance.tolist(),height=y.tolist(),method='bilinear on cell centres; fraction fixed before review'))
    return result


def isoline(a,g,level):
    """Marching triangles, fixed NW-SE split. Segments in raster edge coordinates.
    Periodic seam cells are included and may extend 0.5 pixels beyond the frame.
    Viewer draws periodic translated copies clipped to frame. Equal values use >=;
    a completely level triangle has no unique contour and contributes no segment.
    """
    check(math.isfinite(level), 'Invalid reference level')
    nz,nx=a.shape;limitx=nx if g['periodic'] else nx-1;limitz=nz if g['periodic'] else nz-1
    aa=a[:limitz,:limitx];bb=np.roll(a,-1,1)[:limitz,:limitx]
    cc=np.roll(np.roll(a,-1,1),-1,0)[:limitz,:limitx];dd=np.roll(a,-1,0)[:limitz,:limitx]
    crossing=(np.minimum.reduce([aa,bb,cc,dd])<level)&(np.maximum.reduce([aa,bb,cc,dd])>=level)
    segments=[]
    for z,x in zip(*np.nonzero(crossing)):
        pts=[(x+.5,z+.5),(x+1.5,z+.5),(x+1.5,z+1.5),(x+.5,z+1.5)]
        vals=[aa[z,x],bb[z,x],cc[z,x],dd[z,x]]
        for tri in ((0,1,2),(0,2,3)):
            hits=[]
            for j in range(3):
                p,q=tri[j],tri[(j+1)%3]
                if (vals[p]>=level)!=(vals[q]>=level):
                    t=(level-vals[p])/(vals[q]-vals[p]);hits.append([float(pts[p][k]+t*(pts[q][k]-pts[p][k])) for k in (0,1)])
            if len(hits)==2 and hits[0]!=hits[1]:segments.append(hits)
    return segments


def metrics(a,g):
    gx,gz=gradients(a,g)
    if g['periodic']: ex=np.abs(np.roll(a,-1,1)-a);ez=np.abs(np.roll(a,-1,0)-a)
    else: ex=np.abs(np.diff(a,axis=1));ez=np.abs(np.diff(a,axis=0))
    thresholds=[1e-8,.01,.1,1.]
    unique,counts=np.unique(a,return_counts=True);j=int(np.argmax(counts))
    q=[0,.01,.05,.25,.5,.75,.95,.99,1]
    relief=[]
    for radius in (1,4,16):
        if 2*radius+1>min(a.shape):continue
        maxima=a.copy();minima=a.copy()
        for axis in (0,1):
            base_hi=maxima.copy();base_lo=minima.copy()
            for offset in range(-radius,radius+1):
                shifted_hi=np.roll(base_hi,offset,axis);shifted_lo=np.roll(base_lo,offset,axis)
                maxima=np.maximum(maxima,shifted_hi);minima=np.minimum(minima,shifted_lo)
        r=maxima-minima
        if not g['periodic']:r=r[radius:-radius,radius:-radius]
        relief.append(dict(radiusCells=radius,radiusXBlocks=radius*g['dx'],radiusZBlocks=radius*g['dz'],
                           quantiles=np.quantile(r,q).tolist(),
                           rangeAtMostFractions={str(t):float(np.mean(r<=t)) for t in thresholds}))
    return dict(quantileProbabilities=q,heightQuantiles=np.quantile(a,q).tolist(),
        slopeDegreesQuantiles=np.quantile(np.degrees(np.arctan(np.hypot(gx,gz))),q).tolist(),
        gradients='centred periodic or first-order one-sided at open edges; game-coordinate slopes',
        adjacentXQuantiles=np.quantile(ex,q).tolist(),adjacentZQuantiles=np.quantile(ez,q).tolist(),
        adjacentEqualFractions=[dict(toleranceBlocks=t,x=float(np.mean(ex<=t)),z=float(np.mean(ez<=t))) for t in thresholds],
        mostFrequentExactHeight=float(unique[j]),mostFrequentExactCount=int(counts[j]),
        uniqueFloat64Heights=int(len(unique)),uniquePreviewCodes=int(len(np.unique(codes(a,8)))),
        neighbourRelief=relief,topology=g['periodic'],seaClassificationUsed=False)


def write(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    with path.open('xb') as f:
        f.write(data);f.flush();os.fsync(f.fileno())


def export(source, destination, field='height', level=168., exporter_commit='UNCOMMITTED', fault=None):
    """Exclusive output reservation + last atomic completion marker, no overwrite.
    fault is an injected test callback at explicit state transition boundaries.
    """
    m,g,a,inputs=load(source,field)
    destination=Path(destination).absolute();src=Path(source).resolve()
    check(not destination.resolve().is_relative_to(src) and not src.is_relative_to(destination.resolve()), 'Output/source trees must be disjoint')
    check(math.isfinite(level), 'Invalid overlay level')
    destination.mkdir(parents=True,exist_ok=False)
    write(destination/'INCOMPLETE.json',json_bytes(dict(status='INCOMPLETE',algorithm=ALGORITHM)))
    stage=destination/'.staging';stage.mkdir()
    def checkpoint(name):
        if fault: fault(name)
    checkpoint('reserved')
    raw=inputs[1][1];write(stage/'height.f64le',raw)
    write(stage/'source-manifest.json',inputs[0][1]);checkpoint('sources-copied')
    images,error=render(a)
    for name,data in images.items():write(stage/'png'/name,data)
    write(stage/'palette.rgb',palette().tobytes())
    gx,gz=gradients(a,g);slope=np.degrees(np.arctan(np.hypot(gx,gz)))
    write(stage/'slope-game-degrees.f64le',slope.astype('<f8').tobytes())
    pp=profiles(a,g)
    scale=m.get('blocksPerModelKm');datum=m.get('seaLevelReferenceBlocks')
    native_available=isinstance(scale,(int,float)) and scale>0 and math.isfinite(scale) and isinstance(datum,(int,float)) and math.isfinite(datum)
    native_note='NOT_EXPORTED_BY_GENERATOR'
    if native_available:
        native_note='AFFINE_INVERSE_RECONSTRUCTION_NOT_ORIGINAL_NATIVE_FLOAT64'
        write(stage/'elevation-model-reconstructed.f64le',((a-datum)/scale).astype('<f8').tobytes())
        km=m.get('referenceKmPerUnit')
        if isinstance(km,(int,float)) and math.isfinite(km) and km>0:
            modeldx=m['ReferenceWidth']*km/g['nx'];modeldz=m['ReferenceLength']*km/g['nz']
            modelslope=np.degrees(np.arctan(np.hypot(gx*g['dx']/scale/modeldx,gz*g['dz']/scale/modeldz)))
            write(stage/'slope-model-reconstructed-degrees.f64le',modelslope.astype('<f8').tobytes())
    for p in pp:
        s=io.StringIO(newline='');w=csv.writer(s);w.writerow(['x_blocks','z_blocks','distance_from_domain_origin_blocks','solid_Y_blocks','model_km_reconstructed'])
        for x,z,d,y in zip(p['x'],p['z'],p['distance'],p['height']):w.writerow([x,z,d,repr(y),repr((y-datum)/scale) if native_available else ''])
        write(stage/'profiles'/(p['id']+'.csv'),s.getvalue().encode('utf-8'))
    write(stage/'profiles.json',json_bytes(pp))
    contour=isoline(a,g,level)
    write(stage/'overlays'/'reference-level.json',json_bytes(dict(level=level,method='marching triangles fixed NW-SE; >= ties; periodic translated seam copies',
        units='pixel edge coordinates; x_blocks=x*dx, z_blocks=z*dz',segments=contour)))
    mm=metrics(a,g);write(stage/'metrics.json',json_bytes(mm));checkpoint('derived-written')
    exporter_path=Path(__file__);template=exporter_path.with_name('relief_continu_viewer.html')
    meta=dict(protocol='RCV-1.0',algorithm=ALGORITHM,exporterCommit=exporter_commit,
        exporterSha256=sha(exporter_path.read_bytes()),viewerSha256=sha(template.read_bytes()),
        encoderSha256=sha(Path(__file__).with_name('export_spatial_png.py').read_bytes()),
        generatorCommit=m['commit'],seed=m['seed'],mode=m.get('mode','unspecified'),sourceField=field,
        sourceSha256=sha(raw),sourceManifestSha256=sha(inputs[0][1]),geometry=g,
        referenceExtent=[m.get('ReferenceWidth'),m.get('ReferenceLength')],mechanicalSide=m.get('mechanicalSide'),
        stage='initial solid crust' if field=='initial-height' else 'post-tectonic solid crust before erosion',
        quantity='SOLID_ALTITUDE_NOT_WATER_OR_CRUST_THICKNESS',nativeStatus=native_note,
        nativeReconstruction=dict(datum=datum,blocksPerModelKm=scale),
        encoding=dict(profile='Y0-383-v1',lo=LO,hi=HI,rounding='nearest-ties-even',epsilon=EPSILON,maxError=error),
        palette=dict(id='altitude-sequentielle-v1',sha256=sha(palette().tobytes())),
        overlayLevel=level,waterSurfacePresent=False,seabedMasked=False,terrainRecomputed=False,
        runtime=dict(python=platform.python_version(),numpy=np.__version__,pillow=pillow_version,os=platform.system()),
        geographicAcceptance='NOT_EVALUATED',nativeGame='NOT_RUN',erosion='NOT_RUN',
        limitations=['Native altitude recovered by inverse affine conversion only when declared; no original native buffer',
                     'No geological feature identification or Earth calibration; no increased sampling density',
                     'Browser overlay/zoom qualification is separate from numeric export checks'])
    write(stage/'manifest.json',json_bytes(meta))
    payload=dict(meta=meta,raw=base64.b64encode(raw).decode(),profiles=pp,metrics=mm,
        grey=base64.b64encode(images['height-preview.png']).decode(),color=base64.b64encode(images['height-continuous-color.png']).decode(),
        png16=base64.b64encode(images['height-16bit.png']).decode())
    text=template.read_text(encoding='utf-8').replace('/*RCV_PAYLOAD*/',json.dumps(payload,ensure_ascii=True,allow_nan=False).replace('<','\\u003c'))
    check('/*RCV_PAYLOAD*/' not in text, 'Unfilled viewer')
    write(stage/'index.html',text.encode('utf-8'));checkpoint('viewer-written')
    for path,original in inputs:check(path.read_bytes()==original,'Source modified during export')
    files={str(p.relative_to(stage)):sha(p.read_bytes()) for p in sorted(stage.rglob('*')) if p.is_file()}
    write(stage/'verification.json',json_bytes(dict(status='PASS',scope='numeric export only; not geographic acceptance',
        independentDecoder='Pillow',pixelsVerified=a.size,maxQuantizationError=error,sourcePreserved=True,files=files)))
    checkpoint('verified')
    # Reserve parent first: another exporter cannot replace even an empty prior
    # destination. Only paths inside our exclusively-created destination change.
    stage.rename(destination/'bundle');checkpoint('bundle-moved')
    for path,original in inputs:check(path.read_bytes()==original,'Source changed before completion')
    complete=json_bytes(dict(status='COMPLETE',verificationSha256=sha((destination/'bundle/verification.json').read_bytes())))
    write(destination/'COMPLETE.pending',complete);checkpoint('completion-pending')
    os.replace(destination/'COMPLETE.pending',destination/'COMPLETE.json')
    (destination/'INCOMPLETE.json').unlink()
    return meta,mm


def verify_complete(destination):
    destination=Path(destination);receipt=json.loads((destination/'COMPLETE.json').read_bytes())
    check(receipt['status']=='COMPLETE','Incomplete receipt');root=destination/'bundle'
    raw=(root/'verification.json').read_bytes();check(sha(raw)==receipt['verificationSha256'],'Verification receipt corrupted')
    report=json.loads(raw)
    check(report['status']=='PASS','Not verified')
    for name,h in report['files'].items():
        p=root/name;check(p.resolve().is_relative_to(root.resolve()),'Invalid receipt path')
        check(sha(p.read_bytes())==h,'Output hash mismatch: '+name)
    return report


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source',type=Path);p.add_argument('--out',type=Path,required=True)
    p.add_argument('--field',choices=['height','initial-height'],default='height')
    p.add_argument('--level',type=float,default=168);p.add_argument('--exporter-commit',default='UNCOMMITTED')
    p.add_argument('--verify',action='store_true');args=p.parse_args()
    if args.verify:verify_complete(args.out)
    else:
        if args.source is None:p.error('--source required for export')
        export(args.source,args.out,args.field,args.level,args.exporter_commit)
    print('RCV numeric export verified; geographic acceptance NOT_GRANTED')
