#!/usr/bin/env python3
"""Verify measured fields and decode numeric PNGs; never synthesizes terrain."""
import hashlib, json, math, struct, sys, zlib
from pathlib import Path

FIELDS = {'height','initial-height','elevation-model','continental-thickness','oceanic-thickness',
          'ocean-age','inherited-ocean','plastic-strain','pressure-potential','compression-history','extension-history'}

def png_pixels(path):
    data = Path(path).read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n': raise ValueError('Invalid PNG signature')
    pos=8; payload=b''; dimensions=None
    while pos < len(data):
        size=struct.unpack_from('>I',data,pos)[0]; tag=data[pos+4:pos+8]; block=data[pos+8:pos+8+size]
        if len(block)!=size or zlib.crc32(tag+block)&0xffffffff != struct.unpack_from('>I',data,pos+8+size)[0]:
            raise ValueError('PNG CRC mismatch')
        if tag==b'IHDR':
            w,h,bits,kind,c,f,i=struct.unpack('>IIBBBBB',block)
            if bits!=16 or kind!=0 or c or f or i: raise ValueError('Not a plain numeric grayscale16 PNG')
            dimensions=w,h
        elif tag==b'IDAT': payload+=block
        pos+=size+12
        if tag==b'IEND': break
    if pos!=len(data) or dimensions is None: raise ValueError('Incomplete or trailing PNG')
    w,h=dimensions; raw=zlib.decompress(payload); stride=2*w+1
    if len(raw)!=h*stride or any(raw[z*stride]!=0 for z in range(h)): raise ValueError('Invalid scanlines')
    pixels=[v[0] for z in range(h) for v in struct.iter_unpack('>H',raw[z*stride+1:(z+1)*stride])]
    return dimensions,pixels

def load(root):
    root=Path(root)
    if (root/'INCOMPLETE.json').exists() or (root/'FAILED.json').exists(): raise ValueError('Incomplete world')
    m=json.loads((root/'manifest.json').read_text(encoding='utf-8-sig'))
    complete=json.loads((root/'COMPLETE.json').read_text(encoding='utf-8-sig'))
    if complete['status']!='EXECUTED_FULL_WORLD_NOT_GEOGRAPHIC_PASS' or any(complete[k]!=m[k] for k in ('commit','seed','mode')):
        raise ValueError('Incomplete or inconsistent provenance')
    if set(m['fields'])!=FIELDS or complete['fieldCount']!=len(FIELDS): raise ValueError('Missing field')
    count=m['width']*m['height']; fields={}
    for name,r in m['fields'].items():
        if r['path']!=name+'.f64le': raise ValueError('Unexpected field path')
        b=(root/r['path']).read_bytes()
        if len(b)!=8*count or hashlib.sha256(b).hexdigest()!=r['sha256']: raise ValueError('Field hash/size mismatch')
        a=[x[0] for x in struct.iter_unpack('<d',b)]
        if not all(math.isfinite(x) for x in a): raise ValueError('Nonfinite field')
        fields[name]=a
    for name in ('height','initial-height'):
        a=fields[name]
        if not all(0<=v<=383 for v in a): raise ValueError('Out-of-domain solid altitude')
        size,pixels=png_pixels(root/'png'/(name+'-16bit.png'))
        if size!=(m['width'],m['height']) or pixels != [round(v*65535/383) for v in a]:
            raise ValueError('PNG changed measured altitudes')
    if max(abs(y-(168+12*h)) for y,h in zip(fields['height'],fields['elevation-model']))>1e-11:
        raise ValueError('Signed native altitude mapping changed')
    for a,b in zip(m['initialOrigins'],m['finalOrigins']):
        if abs(a-b)>1e-10*max(1,abs(a)): raise ValueError('Origin volume changed')
    if m['maximumForceResidual']>1.01e-12: raise ValueError('Unconverged mechanics')
    for name in ('continental-thickness','oceanic-thickness','ocean-age','plastic-strain'):
        if min(fields[name])<0: raise ValueError('Negative carrier or history')
    return m,fields

def compare(root,out):
    root,out=Path(root),Path(out)
    if out.exists(): raise FileExistsError('Never overwrite comparison')
    records=[]; by_seed={}; comparisons=0
    for seed in (-437287116,20260906,73):
        for mode in ('stationary','evolving'):
            dirs=[]
            for os in ('ubuntu-latest','windows-latest'):
                found=list(root.glob(f'integrated-world-{os}-{mode}-{seed}-*'))
                if len(found)!=1: raise ValueError('Missing/ambiguous platform world')
                receipt=json.loads((found[0]/'reuse.json').read_text(encoding='utf-8-sig'))
                if receipt['preserved'] is not True or receipt['nativeExitCode']==0: raise ValueError('Unverified overwrite refusal')
                dirs.append(found[0]/'world')
            (ma,a),(mb,b)=[load(d) for d in dirs]
            if ma['commit']!=mb['commit'] or ma['InitialMaterialChecksum']!=mb['InitialMaterialChecksum'] or ma['settings']!=mb['settings'] or ma['driving']!=mb['driving']:
                raise ValueError('Different execution inputs')
            errors={k:max(abs(x-y) for x,y in zip(a[k],b[k])) for k in sorted(FIELDS)}
            if any(e>1e-8 for e in errors.values()): raise ValueError('Platform numeric divergence: '+str(errors))
            for name in ('height','initial-height'):
                if png_pixels(dirs[0]/'png'/(name+'-16bit.png'))!=png_pixels(dirs[1]/'png'/(name+'-16bit.png')):
                    raise ValueError('PNG numeric codes differ')
            comparisons+=len(FIELDS)*ma['width']*ma['height']
            by_seed[seed,mode]=ma,a
            records.append({'seed':seed,'mode':mode,'maximumErrors':errors})
        (ms,s),(me,e)=by_seed[seed,'stationary'],by_seed[seed,'evolving']
        if ms['InitialMaterialChecksum']!=me['InitialMaterialChecksum'] or s['initial-height']!=e['initial-height']:
            raise ValueError('Stationary/evolving inputs are not paired')
    out.write_text(json.dumps({'status':'PASS_RAW_FIELDS_NOT_GEOGRAPHY','comparisons':comparisons,'records':records,
                              'geographicAcceptance':'NOT_ACCEPTED','erosion':'NOT_RUN'},indent=2))

if __name__=='__main__':
    if len(sys.argv)==3 and sys.argv[1]=='check':
        m,f=load(sys.argv[2]); print(json.dumps({'status':'VERIFIED_MEASURED_HEIGHTS','seed':m['seed'],'mode':m['mode'],'samples':len(f['height'])}))
    elif len(sys.argv)==4 and sys.argv[1]=='compare': compare(sys.argv[2],sys.argv[3])
    else: raise SystemExit('check WORLD | compare DOWNLOADS NEW_REPORT')
