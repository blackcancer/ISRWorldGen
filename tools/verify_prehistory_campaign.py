#!/usr/bin/env python3
"""Cross-platform exact fields and continuation provenance, no geographic PASS."""
import hashlib,json,sys
from pathlib import Path
import numpy as np

def verify(root,output):
    records=[]
    for seed in (-437287116,20260906,73):
        pairs=[]
        for os in ('ubuntu-latest','windows-latest'):
            found=list(root.glob(f'prehistory-{os}-{seed}-*'))
            if len(found)!=1: raise ValueError('Missing/ambiguous platform artifact')
            d=found[0]/'world';c=json.loads((d/'COMPLETE.json').read_text())
            if c['seed']!=seed or c['side']!=512 or c['status']!='NUMERIC_CONTINUATION_ONLY' or len(c['stages'])!=3: raise ValueError('Incomplete campaign')
            if json.loads((found[0]/'reuse-check.json').read_text(encoding='utf-8-sig'))['status']!='PASS': raise ValueError('Missing reuse refusal')
            for first,last in zip(c['stages'],c['stages'][1:]):
                if first['FinalMaterialChecksum']!=last['InitialMaterialChecksum'] or first['Checksum']!=last['ContinuationParentChecksum']: raise ValueError('Broken material/history lineage')
            pairs.append(d)
        for stage in (0,36,72):
            manifests=[json.loads((d/f'stage-{stage:03}/manifest.json').read_text()) for d in pairs]
            if manifests[0]['fields'].keys()!=manifests[1]['fields'].keys() or manifests[0]['commit']!=manifests[1]['commit']: raise ValueError('Different sources/fields')
            errors={}
            for name in manifests[0]['fields']:
                arrays=[]
                for d,m in zip(pairs,manifests):
                    field=m['fields'][name];b=(d/f'stage-{stage:03}'/field['path']).read_bytes()
                    if len(b)!=512*512*8 or hashlib.sha256(b).hexdigest()!=field['sha256']: raise ValueError('Field hash/length mismatch')
                    a=np.frombuffer(b,dtype='<f8')
                    if not np.isfinite(a).all(): raise ValueError('Nonfinite field')
                    arrays.append(a)
                error=float(np.max(np.abs(arrays[0]-arrays[1])))
                if error>1e-8: raise ValueError(f'Cross-platform divergence seed={seed} stage={stage} field={name} error={error}')
                if name=='height' and not np.array_equal(np.rint(arrays[0]*65535/383),np.rint(arrays[1]*65535/383)): raise ValueError('Different PNG16 height codes')
                errors[name]=error
            records.append(dict(seed=seed,time=stage,maximumErrors=errors))
    report=dict(status='PASS_NUMERIC_FIELDS_ONLY',records=records,tolerance=1e-8,geographicAcceptance='NOT_EVALUATED',erosion='NOT_RUN')
    with output.open('x') as f: json.dump(report,f,indent=2)
    print(json.dumps(report,indent=2))

if __name__=='__main__': verify(Path(sys.argv[1]),Path(sys.argv[2]))
