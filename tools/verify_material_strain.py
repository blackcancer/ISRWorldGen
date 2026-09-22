"""Compare actual C# passive material-strain observations, never geographic acceptance."""
import hashlib,json,math,struct,sys
from pathlib import Path


def verify(root,output):
    if output.exists(): raise FileExistsError('Previous comparison is immutable')
    reports=[]
    for seed in (-437287116,20260906,73):
        dirs=[]
        for os in ('ubuntu-latest','windows-latest'):
            matches=list(root.glob(f'strain-world-{os}-{seed}-*'))
            if len(matches)!=1: raise ValueError('Missing/ambiguous world')
            d=matches[0]/'world'
            if not (d/'COMPLETE.json').is_file() or (d/'FAILED.json').exists() or (d/'INCOMPLETE.json').exists(): raise ValueError('Incomplete world')
            c=json.loads((d/'COMPLETE.json').read_text())
            if c['checks']!=22 or c['samples']!=128**2 or c['createdRiftEvents']!=0: raise ValueError('Wrong observation scope')
            if json.loads((matches[0]/'reuse.json').read_text(encoding='utf-8-sig'))['preserved'] is not True: raise ValueError('No reuse preservation check')
            dirs.append(d)
        manifests=[json.loads((d/'manifest.json').read_text()) for d in dirs]
        if manifests[0]['commit']!=manifests[1]['commit']: raise ValueError('Different source')
        maximum=0.; numbers=0
        def compare(a,b,path):
            nonlocal maximum,numbers
            if type(a) is not type(b): raise ValueError('Type mismatch '+path)
            if isinstance(a,bool) or isinstance(a,str) or a is None:
                if a!=b: raise ValueError('Different classification '+path)
            elif isinstance(a,(int,float)):
                if not math.isfinite(a) or not math.isfinite(b): raise ValueError('Nonfinite '+path)
                delta=abs(a-b);maximum=max(maximum,delta);numbers+=1
                if (isinstance(a,int) and a!=b) or delta>1e-8: raise ValueError(f'Platform divergence {path}: {delta}')
            elif isinstance(a,list):
                if len(a)!=len(b): raise ValueError('Count mismatch')
                for i,(x,y) in enumerate(zip(a,b)):compare(x,y,path+'/'+str(i))
            elif isinstance(a,dict):
                if a.keys()!=b.keys(): raise ValueError('Schema mismatch')
                for k in a:compare(a[k],b[k],path+'/'+k)
            else: raise ValueError('Unsupported type')
        for name in ('markers.json','COMPLETE.json','checks.json'):
            compare(*(json.loads((d/name).read_text()) for d in dirs),name)
        field_errors={}
        for name in manifests[0]['fields']:
            values=[]
            for d,m in zip(dirs,manifests):
                f=m['fields'][name];raw=(d/f['path']).read_bytes()
                if f['path']!=name+'.f64le' or len(raw)!=512**2*8 or hashlib.sha256(raw).hexdigest()!=f['sha256']: raise ValueError('Invalid height evidence')
                values.append([v[0] for v in struct.iter_unpack('<d',raw)])
            error=max(abs(a-b) for a,b in zip(*values))
            if error>1e-8: raise ValueError('Height divergence')
            if name in ('height','initial-height') and any(round(a*65535/383)!=round(b*65535/383) for a,b in zip(*values)):raise ValueError('PNG16 pixel difference')
            field_errors[name]=error
        reports.append(dict(seed=seed,comparedNumbers=numbers,maximumMarkerDifference=maximum,heightDifferences=field_errors))
    report=dict(status='PASS_CSHARP_PASSIVE_KINEMATICS',absoluteTolerance=1e-8,worlds=reports,
                geographicAcceptance='NOT_ACCEPTED',erosion='NOT_RUN')
    output.write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':verify(Path(sys.argv[1]),Path(sys.argv[2]))
