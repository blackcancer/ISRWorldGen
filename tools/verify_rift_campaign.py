"""Verify executed rift and chronology witnesses on two platforms. Not a world gate."""
import json, math, sys
from pathlib import Path

def compare(root, output):
    if output.exists(): raise FileExistsError('Do not overwrite comparison evidence')
    dirs=sorted(p for p in root.iterdir() if p.is_dir())
    if len(dirs)!=2: raise ValueError('Expected exactly two platform artifacts')
    commits=[];records=[]; maximum=0.; numbers=0
    for d in dirs:
        if not (d/'COMPLETE.json').is_file() or (d/'FAILED.json').exists() or (d/'INCOMPLETE.json').exists():
            raise ValueError('Incomplete or rejected campaign')
        source=json.loads((d/'source.json').read_text(encoding='utf-8-sig'))
        if source['exits']!={'necking':0,'integration':0}:raise ValueError('Failed executable')
        commits.append(source['commit'])
    if commits[0]!=commits[1]:raise ValueError('Different code revisions')
    def same(a,b,path):
        nonlocal maximum,numbers
        if type(a)!=type(b):raise ValueError('Different type '+path)
        if isinstance(a,dict):
            if a.keys()!=b.keys():raise ValueError('Different fields '+path)
            for k in a:same(a[k],b[k],path+'/'+k)
        elif isinstance(a,list):
            if len(a)!=len(b):raise ValueError('Different array length '+path)
            for i,(x,y) in enumerate(zip(a,b)):same(x,y,path+'/'+str(i))
        elif type(a) in (int,float):
            if not math.isfinite(a) or not math.isfinite(b):raise ValueError('Nonfinite '+path)
            delta=abs(a-b);maximum=max(maximum,delta);numbers+=1
            if (type(a) is int and delta!=0) or delta>1e-8:raise ValueError(f'Divergence {path}: {delta}')
        elif a!=b:raise ValueError('Different identity/status '+path)
    for filename,status,count in [('necking.json','PASS_CONTROLLED_MODEL_ONLY',18),('integration.json','PASS_RIFT_CHRONOLOGY_INTEGRATION',10)]:
        data=[json.loads((d/filename).read_text(encoding='utf-8-sig')) for d in dirs]
        if any(d['status']!=status or len(d['checks'])!=count for d in data):raise ValueError('Missing required checks')
        if filename=='integration.json' and any(d['failures']!=0 or any(c['status']!='PASS' for c in d['checks']) for d in data):raise ValueError('Rejected integration')
        same(*data,filename);records.append({'file':filename,'checksPerPlatform':count})
    report={'commit':commits[0],'status':'PASS_CONTROLLED_MODELS_ONLY','comparedNumericValues':numbers,
            'maximumAbsoluteDifference':maximum,'absoluteTolerance':1e-8,'suites':records,
            'geographicAcceptance':'NOT_EVALUATED','erosion':'NOT_RUN'}
    output.write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2));return report
if __name__=='__main__':compare(Path(sys.argv[1]),Path(sys.argv[2]))
