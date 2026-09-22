#!/usr/bin/env python3
"""Full paired fields, yield diagnostics and unchanged absolute tolerances."""
import array, hashlib, json, math, sys
from pathlib import Path
from export_tectonic_history import read_pixels

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def numbers(a,b):
    if type(a)!=type(b):
        if not isinstance(a,(int,float)) or not isinstance(b,(int,float)):raise ValueError('Different numeric schemas')
    if isinstance(a,bool):
        if a!=b:raise ValueError('Different decision')
        return 0
    if isinstance(a,(int,float)):
        if not math.isfinite(a) or not math.isfinite(b):raise ValueError('Nonfinite diagnostic')
        return abs(a-b)
    if isinstance(a,dict):
        if a.keys()!=b.keys():raise ValueError('Different fields')
        return max((numbers(a[k],b[k]) for k in a),default=0)
    if isinstance(a,list):
        if len(a)!=len(b):raise ValueError('Different diagnostic shapes')
        return max((numbers(x,y) for x,y in zip(a,b)),default=0)
    if a!=b:raise ValueError('Different metadata')
    return 0

def verify(root,out):
    reports=[];initial={}
    for seed in (-437287116,20260906,73):
        for mode in ('control','plastic'):
            folders=[];manifests=[]
            for os in ('ubuntu-latest','windows-latest'):
                hits=list(root.glob(f'plastic-world-{os}-{mode}-{seed}-*'))
                if len(hits)!=1:raise ValueError('Missing or ambiguous artifact')
                r=hits[0];d=r/'world';c=read(d/'COMPLETE.json');m=read(d/'manifest.json')
                if c['status']!='EXECUTED_NUMERIC_NOT_GEOGRAPHIC' or m['mode']!=mode or m['seed']!=seed:raise ValueError('Incomplete world')
                if not read(r/'reuse.json')['preserved']:raise ValueError('Missing preservation proof')
                if m['maximumForceResidual']>1e-10:raise ValueError('Unsolved actual force balance')
                old=initial.setdefault((seed,os),m['InitialMaterialChecksum'])
                if old!=m['InitialMaterialChecksum']:raise ValueError('Unmatched materials')
                folders.append(d);manifests.append(m)
            errors={}
            if manifests[0]['commit']!=manifests[1]['commit'] or manifests[0]['fields'].keys()!=manifests[1]['fields'].keys():raise ValueError('Different code or fields')
            for name in manifests[0]['fields']:
                vectors=[]
                for d,m in zip(folders,manifests):
                    f=m['fields'][name];raw=(d/f['path']).read_bytes()
                    if f['path']!=name+'.f64le' or len(raw)!=512*512*8 or hashlib.sha256(raw).hexdigest()!=f['sha256']:raise ValueError('Field hash or length')
                    a=array.array('d');a.frombytes(raw)
                    if sys.byteorder!='little':a.byteswap()
                    if not all(math.isfinite(x) for x in a):raise ValueError('Nonfinite field')
                    if mode=='control' and name=='strain-moment' and any(a):raise ValueError('Subyield control accumulated plastic memory')
                    vectors.append(a)
                errors[name]=max(abs(x-y) for x,y in zip(*vectors))
                if errors[name]>1e-8:raise ValueError(f'Platform divergence {name}: {errors[name]}')
            for name in ('initial-height','height'):
                pixels=[read_pixels((d/'png'/f'{name}-16bit.png').read_bytes()) for d in folders]
                if pixels[0]!=pixels[1]:raise ValueError('Different height pixels')
            native=[read(d/'last-mechanics.json') for d in folders]
            # Iteration counts are solver diagnostics, not fixed geographic fields.
            for k in native:
                k['fields'].pop('NewtonIterations',None);k['fields']['Native'].pop('Iterations',None)
            mech=numbers(*native)
            if mech>1e-8:raise ValueError('Mechanical platform divergence '+str(mech))
            reports.append(dict(seed=seed,mode=mode,errors=errors,mechanicalMaxError=mech,png16Identical=True))
    out.write_text(json.dumps(dict(status='PASS_NUMERICAL_ONLY',worlds=reports,geographicAcceptance='NOT_EVALUATED',erosion='NOT_RUN'),indent=2))
if __name__=='__main__':verify(Path(sys.argv[1]),Path(sys.argv[2]))
