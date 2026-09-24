#!/usr/bin/env python3
"""Checks of executed material cuts. No terrain or automatic fracture is inferred."""
import argparse, hashlib, json, math, subprocess
from pathlib import Path
import verify_rift_raster as legacy
FIXTURES=('straight','crooked-pause','periodic')

def validate(root):
    complete=json.loads((root/'COMPLETE.json').read_text(encoding='utf-8-sig'))
    report=complete['report']
    if complete['status']!='TOPOLOGY_ONLY_NOT_WORLD_RELIEF' or report['status']!='PASS_TOPOLOGY_AND_PRESCRIBED_OPENING' or report['passed']!=34 or report['failed']!=0 or report['errors'] or len(set(report['names']))!=34:
        raise ValueError('Incomplete topology checks')
    if (root/'INCOMPLETE.json').exists() or (root/'FAILED.json').exists():raise ValueError('Incomplete topology outputs')

def verify_legacy(root):
    baseline=json.loads(Path(__file__).with_name('material_cut_legacy_baseline.json').read_text())
    for fixture,files in baseline['fixtures'].items():
        for name,digest in files.items():
            if hashlib.sha256((root/fixture/name).read_bytes()).hexdigest()!=digest:
                raise ValueError('Legacy field or geometry changed: '+fixture+'/'+name)

def export(root):
    validate(root)
    legacy.FIXTURES=FIXTURES
    legacy.export(root)

def compare(root,out):
    if out.exists():raise FileExistsError('No overwrite')
    dirs=[]
    for os in ('ubuntu-latest','windows-latest'):
        matches=list(root.glob('rift-raster-'+os+'-*'))
        if len(matches)!=1:raise ValueError('Missing platform')
        d=matches[0];validate(d/'topology');verify_legacy(d/'worlds')
        if json.loads((d/'topology-refusal.json').read_text())['preserved'] is not True:raise ValueError('Unverified preservation')
        dirs.append(d/'topology')
    records=[]
    for fixture in FIXTURES:
        ds=[d/fixture for d in dirs];ms=[json.loads((d/'manifest.json').read_text()) for d in ds]
        if ms[0]['commit']!=ms[1]['commit'] or any(set(m['fields'])!=legacy.FIELDS or m['altitudePresent'] is not False or m['worldGenerated'] is not False for m in ms):raise ValueError('Different provenance')
        errors={}
        for name in sorted(legacy.FIELDS):
            a,b=[legacy.field(d,m,name) for d,m in zip(ds,ms)];error=max(map(lambda v:abs(v[0]-v[1]),zip(a,b)))
            if error>1e-8:raise ValueError('Field mismatch '+name)
            errors[name]=error
        for name in ('continental-area-16bit.png','new-ocean-area-16bit.png','new-ocean-age-16bit.png'):
            if (ds[0]/'png'/name).read_bytes()!=(ds[1]/'png'/name).read_bytes():raise ValueError('PNG mismatch')
        pe=legacy.compare_tree(*[json.loads((d/'packets.json').read_text()) for d in ds])
        ge=legacy.compare_tree(*[json.loads((d/'geometry.json').read_text()) for d in ds])
        records.append({'fixture':fixture,'maximumFieldErrors':errors,'packetError':pe,'geometryError':ge})
    out.write_text(json.dumps({'status':'PASS_NUMERICAL_TOPOLOGY_ONLY','checksPerPlatform':149,'legacyBitwisePreserved':True,'records':records,'worldGenerated':False,'erosion':'NOT_RUN'},indent=2))

def reuse(root,project,out):
    if out.exists():raise FileExistsError('Existing receipt')
    def hashes():return {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob('*')) if p.is_file()}
    before=hashes()
    r=subprocess.run(['dotnet','run','--no-build','--project',project,'-c','Release','--',str(root)],capture_output=True,text=True,timeout=60)
    if r.returncode!=2 or 'REFUSED_EXISTING_EVIDENCE' not in r.stderr or before!=hashes():raise ValueError('Existing evidence not preserved')
    out.write_text(json.dumps({'preserved':True,'exitCode':r.returncode,'stderr':r.stderr,'files':len(before)},indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('mode',choices=['export','compare','legacy','reuse']);p.add_argument('root',type=Path);p.add_argument('extra',nargs='*');a=p.parse_args()
    if a.mode=='export':export(a.root)
    elif a.mode=='legacy':verify_legacy(a.root)
    elif a.mode=='compare':compare(a.root,Path(a.extra[0]))
    else:reuse(a.root,a.extra[0],Path(a.extra[1]))
