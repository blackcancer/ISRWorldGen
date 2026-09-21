#!/usr/bin/env python3
"""Cross-platform gate over actual material histories, not only green jobs."""
import argparse, hashlib, json, math, struct
from pathlib import Path
from export_spatial_png import read_pixels

def read_field(root, relative, field, shape):
    raw=(root/relative/field['path']).read_bytes()
    if len(raw)!=shape*8 or hashlib.sha256(raw).hexdigest()!=field['sha256']:
        raise ValueError('Source hash or dimensions mismatch')
    values=[v for (v,) in struct.iter_unpack('<d',raw)]
    if not all(math.isfinite(v) for v in values): raise ValueError('Nonfinite actual field')
    return values

def compare(a,b):
    results=[]
    dirs=sorted(p.name for p in a.glob('seed-*'))
    if len(dirs)!=3 or dirs!=sorted(p.name for p in b.glob('seed-*')): raise ValueError('Expected the same three seeds')
    for name in dirs:
        ma=json.loads((a/name/'manifest.json').read_text(encoding='utf-8'))
        mb=json.loads((b/name/'manifest.json').read_text(encoding='utf-8'))
        for key in ('seed','commit','width','height','settings','fields'):
            if key=='fields':
                if set(ma[key])!=set(mb[key]): raise ValueError('Layer set changed')
            elif ma[key]!=mb[key]: raise ValueError('Mixed provenance '+key)
        result={'seed':ma['seed'],'commit':ma['commit'],'layers':{},'status':'PASS'}
        fa={k:read_field(a,name,f,ma['width']*ma['height']) for k,f in ma['fields'].items()}
        fb={k:read_field(b,name,f,mb['width']*mb['height']) for k,f in mb['fields'].items()}
        for k in fa:
            if k=='ocean-age':
                # Mean age is undefined where the amount of oceanic crust is
                # numerically negligible. Compare its extensive moment too.
                pairs=[(x,y) for i,(x,y) in enumerate(zip(fa[k],fb[k])) if max(fa['oceanic-thickness'][i],fb['oceanic-thickness'][i])>1e-6]
                maximum=max((abs(x-y) for x,y in pairs),default=0); tolerance=1e-6
                moment=max(abs(x*fa['oceanic-thickness'][i]-y*fb['oceanic-thickness'][i]) for i,(x,y) in enumerate(zip(fa[k],fb[k])))
                if moment>1e-7: raise ValueError('Ocean-age moment differs between platforms')
                result['ageMomentMaxDelta']=moment
                result['meanAgeCellsCompared']=len(pairs)
                result['meanAgeNegligibleOceanCells']=len(fa[k])-len(pairs)
                result['ageMomentComparedEverywhere']=True
            else:
                maximum=max(abs(x-y) for x,y in zip(fa[k],fb[k]));tolerance=0 if k=='plates' else 1e-8
            result['layers'][k]={'maxDelta':maximum,'tolerance':tolerance}
            if maximum>tolerance: raise ValueError(f'Cross-platform divergence {name}/{k}: {maximum} > {tolerance}')
        for k in ('height','initial-height'):
            pa=read_pixels((a/name/'png'/f'{k}-16bit.png').read_bytes())
            pb=read_pixels((b/name/'png'/f'{k}-16bit.png').read_bytes())
            if pa!=pb: raise ValueError('Actual PNG16 pixels differ '+name+'/'+k)
        result['heightPng16PixelsIdentical']=True;results.append(result)
    return {'status':'PASS_BOUNDED_PLATFORM_COMPARISON','geographicAcceptance':'NOT_ACCEPTED','results':results}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('linux',type=Path);p.add_argument('windows',type=Path);p.add_argument('output',type=Path);args=p.parse_args()
    result=compare(args.linux,args.windows);args.output.write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,indent=2))
