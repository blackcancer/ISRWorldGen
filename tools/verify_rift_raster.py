#!/usr/bin/env python3
"""Validate actual C# area fields. These are never world terrain heights."""
import argparse,array,hashlib,json,math,struct,sys,zlib
from pathlib import Path

FIELDS={'continental-fraction','ocean-fraction','continental-thickness','oceanic-thickness','age-moment'}
FIXTURES=('axial','oblique','periodic')
RETAINED={
    'MaterialStrain':(22,'PASS_KINEMATICS_ONLY'),
    'RiftNecking':(18,'PASS_CONTROLLED_MODEL_ONLY'),
    'RiftSpreadingIntegration':(14,'PASS_RIFT_CHRONOLOGY_INTEGRATION'),
    'RiftRegression':(28,'PASS_LEGACY_SUBSET_AND_DIAGNOSTIC')}

def validate_retained(name,report):
    count,status=RETAINED[name]
    checks=report.get('checks')
    if report.get('status')!=status or not isinstance(checks,list) or len(checks)!=count or report.get('failures'):
        raise ValueError('Retained tests incomplete: '+name)
    if any(isinstance(c,dict) and (c.get('status')!='PASS' or c.get('error') is not None) for c in checks):
        raise ValueError('A retained assertion failed: '+name)


def field(root,m,name):
    f=m['fields'][name];b=(root/f['path']).read_bytes()
    if f['path']!=name+'.f64le' or len(b)!=m['width']*m['height']*8 or hashlib.sha256(b).hexdigest()!=f['sha256']:
        raise ValueError('Invalid field provenance '+name)
    a=array.array('d');a.frombytes(b)
    if sys.byteorder!='little':a.byteswap()
    if not all(map(math.isfinite,a)):raise ValueError('Nonfinite source')
    return a

def png(w,h,codes):
    def chunk(tag,b):return struct.pack('>I',len(b))+tag+b+struct.pack('>I',zlib.crc32(tag+b)&0xffffffff)
    pixels=b''.join(b'\0'+struct.pack('>'+str(w)+'H',*codes[z*w:(z+1)*w]) for z in range(h))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,16,0,0,0,0))+chunk(b'IDAT',zlib.compress(pixels))+chunk(b'IEND',b'')

def export(root):
    prepared=[]
    if not (root/'COMPLETE.json').is_file() or (root/'FAILED.json').exists():raise ValueError('Incomplete C# calculation')
    for name in FIXTURES:
        d=root/name;m=json.loads((d/'manifest.json').read_text());w,h=m['width'],m['height']
        if set(m['fields'])!=FIELDS or m['altitudePresent'] is not False:raise ValueError('Unexpected field contract')
        if (d/'png').exists():raise FileExistsError('No overwrite of PNG evidence')
        a={k:field(d,m,k) for k in m['fields']}
        cf,of=a['continental-fraction'],a['ocean-fraction'];c,o,q=a['continental-thickness'],a['oceanic-thickness'],a['age-moment']
        if any(v<0 or v>1+2e-12 for v in cf+of) or any(x+y>1+2e-12 for x,y in zip(cf,of)):raise ValueError('Invalid coverage')
        if any(v<0 for v in c+o+q) or any(x==0 and y!=0 for x,y in zip(o,q)):raise ValueError('Invalid carrier or age')
        age=[y/x if x else None for x,y in zip(o,q)]
        if any(v is not None and not 0<=v<=60 for v in age):raise ValueError('Age outside fixed range')
        maps={'continental-area-16bit.png':[round(v*65535) for v in cf],
              'new-ocean-area-16bit.png':[round(v*65535) for v in of],
              'new-ocean-age-16bit.png':[65535 if v is None else round(v*65534/60) for v in age]}
        for codes in maps.values():
            if any(v<0 or v>65535 for v in codes):raise ValueError('Encoding outside fixed range')
        prepared.append((d,w,h,maps))
    for d,w,h,maps in prepared:
        (d/'png').mkdir()
        hashes={}
        for name,codes in maps.items():
            raw=png(w,h,codes)
            with (d/'png'/name).open('xb') as f:f.write(raw)
            hashes[name]=hashlib.sha256(raw).hexdigest()
        (d/'png/encoding.json').write_text(json.dumps({'fraction':'code/65535','age':'code*60/65534 model Myr; 65535 is UNKNOWN',
            'altitudePresent':False,'sha256':hashes,'scope':'real C# controlled material footprints, not a procedural planet'},indent=2))

def compare_tree(a,b,path='root'):
    if isinstance(a,bool) or isinstance(a,str) or a is None:
        if a!=b:raise ValueError('Discrete provenance differs: '+path)
        return 0
    if isinstance(a,(int,float)):
        if not isinstance(b,(int,float)) or not math.isfinite(a) or not math.isfinite(b):raise ValueError('Invalid number: '+path)
        error=abs(a-b)
        if error>1e-8:raise ValueError('Provenance numeric difference: '+path)
        return error
    if isinstance(a,list):
        if not isinstance(b,list) or len(a)!=len(b):raise ValueError('Different packet count: '+path)
        return max((compare_tree(x,y,path+f'[{i}]') for i,(x,y) in enumerate(zip(a,b))),default=0)
    if not isinstance(b,dict) or a.keys()!=b.keys():raise ValueError('Different provenance schema: '+path)
    return max((compare_tree(a[k],b[k],path+'/'+k) for k in a),default=0)

def compare(root,out):
    if out.exists():raise FileExistsError('Refuse comparison overwrite')
    dirs=[]
    for os in ('ubuntu-latest','windows-latest'):
        matches=list(root.glob('rift-raster-'+os+'-*'))
        if len(matches)!=1:raise ValueError('Missing/ambiguous platform')
        d=matches[0];complete=json.loads((d/'worlds/COMPLETE.json').read_text())
        if complete['report']['passed']!=33 or complete['report']['failed']!=0:raise ValueError('Incomplete tests')
        if json.loads((d/'refusal.json').read_text())['preserved'] is not True:raise ValueError('Unverified reuse refusal')
        for name in RETAINED:
            report=json.loads((d/(name+'.json')).read_text(encoding='utf-8-sig'))
            validate_retained(name,report)
        dirs.append(d/'worlds')
    records=[]
    for fixture in FIXTURES:
        ds=[d/fixture for d in dirs];ms=[json.loads((d/'manifest.json').read_text()) for d in ds]
        if ms[0]['commit']!=ms[1]['commit'] or ms[0]['fields'].keys()!=ms[1]['fields'].keys():raise ValueError('Different source')
        errors={}
        for name in ms[0]['fields']:
            a,b=[field(d,m,name) for d,m in zip(ds,ms)];errors[name]=max(abs(x-y) for x,y in zip(a,b))
            if errors[name]>1e-8:raise ValueError('Cross-platform error '+name)
        for name in ('continental-area-16bit.png','new-ocean-area-16bit.png','new-ocean-age-16bit.png'):
            if (ds[0]/'png'/name).read_bytes()!=(ds[1]/'png'/name).read_bytes():raise ValueError('PNG byte difference')
        pe=compare_tree(*[json.loads((d/'packets.json').read_text()) for d in ds])
        records.append({'fixture':fixture,'maximumErrors':errors,'packetProvenanceMaximumError':pe})
    out.write_text(json.dumps({'status':'PASS','checksPerPlatform':115,'records':records,'geographicAcceptance':'NOT_EVALUATED','erosion':'NOT_RUN'},indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('mode',choices=['export','compare']);p.add_argument('root',type=Path);p.add_argument('output',type=Path,nargs='?');a=p.parse_args()
    if a.mode=='export':export(a.root)
    elif a.output is None:p.error('comparison output is required')
    else:compare(a.root,a.output)
