#!/usr/bin/env python3
"""Paired causal and cross-platform receipts, not geographic acceptance."""
import hashlib,json,math,struct,sys
from pathlib import Path
from export_tectonic_history import read_pixels

def verify(root,output):
    root=Path(root);records=[];worlds={}
    for seed in (-437287116,20260906,73):
        for mode in ('control','weakening'):
            pair=[]
            for os in ('ubuntu-latest','windows-latest'):
                paths=list(root.glob(f'weakening-world-{os}-{mode}-{seed}-*'))
                if len(paths)!=1:raise ValueError('Missing/duplicate world artifact')
                d=paths[0]/'world';m=json.loads((d/'manifest.json').read_text());done=json.loads((d/'COMPLETE.json').read_text())
                if done['status']!='EXECUTED_NUMERIC_NOT_GEOGRAPHIC' or m['seed']!=seed or m['mode']!=mode or m['width']!=512 or m['mechanicalSide']!=128:raise ValueError('Invalid execution provenance')
                if not json.loads((paths[0]/'reuse.json').read_text(encoding='utf-8-sig'))['preserved']:raise ValueError('Missing overwrite refusal')
                if m['maximumForceResidual']>1e-12:raise ValueError('Unsolved mechanical balance')
                values={}
                for name,f in m['fields'].items():
                    if f['path']!=name+'.f64le':raise ValueError('Unexpected field path')
                    b=(d/f['path']).read_bytes()
                    if len(b)!=512*512*8 or hashlib.sha256(b).hexdigest()!=f['sha256']:raise ValueError('Field checksum/length')
                    a=[v[0] for v in struct.iter_unpack('<d',b)]
                    if not all(math.isfinite(v) for v in a):raise ValueError('Nonfinite field')
                    values[name]=a
                if max(abs(a-b) for a,b in zip(values['strain-carrier'],values['continental-thickness']))>1e-9:raise ValueError('Memory detached from continent')
                total=math.fsum(values['strain-moment']);expected=m['historyMoment']+m['producedMoment']
                if abs(total-expected)>1e-8+2e-12*abs(expected):raise ValueError('Memory production inventory')
                for name in ('initial-height','height'):
                    data=read_pixels((d/'png'/(name+'-16bit.png')).read_bytes())
                    expected=b''.join(struct.pack('>H',round(v*65535/383)) for v in values[name])
                    if data!=(512,512,16,0,expected):raise ValueError('False heightmap encoding')
                pair.append((m,values,d))
            if pair[0][0]['commit']!=pair[1][0]['commit']:raise ValueError('Different source versions')
            errors={name:max(abs(a-b) for a,b in zip(pair[0][1][name],pair[1][1][name])) for name in pair[0][1]}
            if max(errors.values())>1e-8:raise ValueError('Cross-platform field tolerance exceeded: '+str(errors))
            for name in ('initial-height','height'):
                if read_pixels((pair[0][2]/'png'/(name+'-16bit.png')).read_bytes())!=read_pixels((pair[1][2]/'png'/(name+'-16bit.png')).read_bytes()):raise ValueError('Platform height pixels differ')
            records.append({'seed':seed,'mode':mode,'errors':errors,'heightPixelsIdentical':True})
            worlds[(seed,mode)]=pair[0]
        a,b=worlds[(seed,'control')],worlds[(seed,'weakening')]
        if a[0]['InitialMaterialChecksum']!=b[0]['InitialMaterialChecksum'] or a[1]['initial-height']!=b[1]['initial-height']:raise ValueError('Paired initial state changed')
        delta=[y-x for x,y in zip(a[1]['height'],b[1]['height'])]
        if max(abs(v) for v in delta)<=1e-9:raise ValueError('Weakening did not feed back into terrain')
        records.append({'seed':seed,'scope':'SAME_INITIAL_STATE_MEMORY_FEEDBACK_ONLY',
            'maximumHeightDifferenceBlocks':max(abs(v) for v in delta),'rmsHeightDifferenceBlocks':math.sqrt(math.fsum(v*v for v in delta)/len(delta))})
    result={'status':'PASS_NUMERICAL_NOT_GEOGRAPHIC','records':records,'erosion':'NOT_RUN','geographicAcceptance':'NOT_ACCEPTED'}
    Path(output).write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
if __name__=='__main__':verify(sys.argv[1],sys.argv[2])
