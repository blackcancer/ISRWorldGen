#!/usr/bin/env python3
"""Acquire measured ETOPO elevation subsets and compare without self-approval.
Only public bounded read requests; fresh directories; errors remain explicit.
"""
import argparse, array, gzip, hashlib, json, math, statistics, struct, sys
import urllib.parse, urllib.request
from pathlib import Path
from export_spatial_png import png, read_pixels
ENDPOINT='https://oceanwatch.pifsc.noaa.gov/erddap/griddap/ETOPO_2022_v1_60s.json'
REFERENCES=[('alps_po_ligurian',42,49,4,15),('andes_chile_trench',-38,-28,280,292),('norway_shelf_basin',57,69,0,15)]
def require(ok,why):
    if not ok:raise ValueError(why)
def sha(b):return hashlib.sha256(b).hexdigest()
def quantile(a,q):
    v=sorted(a);return v[round((len(v)-1)*q)]
def metrics(a,w,h,dx,dz,sea):
    require(len(a)==w*h and all(math.isfinite(v) for v in a),'Invalid DEM')
    q05,q50,q95=[quantile(a,q) for q in (.05,.5,.95)];span=q95-q05;increments=[]
    for divisor in (128,64,32,16):
        lag=min(w*dx,h*dz)/divisor;lx=max(1,round(lag/dx));lz=max(1,round(lag/dz));values=[]
        stride=max(1,min(w,h)//256)
        for y in range(0,h-lz,stride):
            for x in range(0,w-lx,stride):
                v=a[y*w+x];values.append(abs(a[y*w+x+lx]-v));values.append(abs(a[(y+lz)*w+x]-v))
        median=statistics.median(values)
        increments.append(dict(relativeLag=1/divisor,physicalLag=lag,medianDifference=median,p90Difference=quantile(values,.9),medianNormalized=median/span if span else None))
    slopes=[];axes=0;eligible=0;stride=max(1,min(w,h)//512)
    for y in range(1,h-1,stride):
        for x in range(1,w-1,stride):
            gx=(a[y*w+x+1]-a[y*w+x-1])/(2*dx);gz=(a[(y+1)*w+x]-a[(y-1)*w+x])/(2*dz)
            norm=math.hypot(gx,gz);slopes.append(math.degrees(math.atan(norm)))
            if norm>1e-9:
                eligible+=1;t=math.atan2(gz,gx)/(math.pi/4)
                if abs(t-round(t))<1/90:axes+=1
    below=[v for v in a if v<sea]
    return dict(minimum=min(a),maximum=max(a),q05=q05,median=q50,q95=q95,
        belowSeaFraction=len(below)/len(a),submarineRange=max(below)-min(below) if below else None,
        slopeMedianDegrees=statistics.median(slopes),slope95Degrees=quantile(slopes,.95),
        gradientWithinHalfDegreeOfGridAxes=axes/eligible if eligible else None,normalizedIncrementCurve=increments,
        meaning='Metres for Earth, blocks for game. Absolute slopes are NOT equivalent after vertical compression. Relative increments are exploratory, never a geographic PASS.')
def parse_reference(raw):
    table=json.loads(raw)['table'];require(table['columnNames']==['latitude','longitude','z'],'Unexpected ERDDAP columns')
    rows=table['rows'];require(10<len(rows)<=2_000_000,'Reference size outside budget')
    require(all(len(r)==3 and all(isinstance(v,(int,float)) and math.isfinite(v) for v in r) for r in rows),'Missing reference values')
    lats=sorted({r[0] for r in rows},reverse=True);lons=sorted({r[1] for r in rows})
    require(len(lats)*len(lons)==len(rows),'Incomplete reference grid')
    li={v:i for i,v in enumerate(lats)};lj={v:i for i,v in enumerate(lons)};a=[math.nan]*len(rows)
    for lat,lon,z in rows:
        pos=li[lat]*len(lons)+lj[lon];require(math.isnan(a[pos]),'Repeated reference cell');require(-12000<z<9000,'Fill value or unsupported height');a[pos]=z
    mid=sum(lats)/len(lats);r=6371008.8
    dx=r*math.cos(math.radians(mid))*math.radians(lons[1]-lons[0]);dz=r*math.radians(lats[0]-lats[1])
    return a,len(lons),len(lats),dx,dz,lons,lats
def fetch_reference(item,root):
    name,s,n,w,e=item;url=ENDPOINT+'?'+urllib.parse.quote(f'z[({s}):1:({n})][({w}):1:({e})]',safe='')
    folder=root/name;folder.mkdir(exist_ok=False)
    request=urllib.request.Request(url,headers={'User-Agent':'ISRWorldGen-terrain-reference/1.0'})
    with urllib.request.urlopen(request,timeout=120) as response:raw=response.read(80_000_001)
    require(len(raw)<=80_000_000,'Public response exceeds budget')
    (folder/'source.json.gz').write_bytes(gzip.compress(raw,mtime=0))
    a,nx,ny,dx,dz,lons,lats=parse_reference(raw);f64=array.array('d',a)
    if sys.byteorder!='little':f64.byteswap()
    (folder/'height.f64le').write_bytes(f64.tobytes())
    codes=[round((v+12000)*65535/21000) for v in a];pixels=b''.join(struct.pack('>H',v) for v in codes)
    encoded=png(nx,ny,pixels,16,0,'Observed ETOPO 2022 signed metres, full seabed, range -12000 to 9000')
    require(read_pixels(encoded)==(nx,ny,16,0,pixels),'Reference PNG roundtrip failed')
    (folder/'heightmap-16bit.png').write_bytes(encoded)
    rgb=b''.join(bytes([round(v*255/65535)])*3 for v in codes)
    (folder/'heightmap-grey.png').write_bytes(png(nx,ny,rgb,8,2,'Reference preview, fixed -12000 to 9000 metres, no sea mask'))
    report=dict(name=name,source='NOAA NCEI ETOPO 2022 v1 60 arc-second Ice Surface, EGM2008',sourceUrl=url,sourceSha256=sha(raw),
        sourceDatasetPage='https://www.ncei.noaa.gov/products/etopo-global-relief-model',datasetDoi='10.25921/fd45-gt74',
        license='Free reuse, no warranty; see NOAA metadata',requestedExtent=dict(south=s,north=n,west=w,east=e),
        width=nx,height=ny,rows='north to south',columns='west to east',dxMetresAtCentre=dx,dzMetres=dz,
        latitudeRange=[lats[-1],lats[0]],longitudeRange=[lons[0],lons[-1]],physicalWidthApproxMetres=nx*dx,physicalHeightApproxMetres=ny*dz,
        metricApproximation='Equirectangular at centre latitude for scalar metrics only; original heights unchanged; not an exact local projection.',
        limitations='Ice Surface not subglacial bedrock; present-day eroded surface; inland waters and heterogeneous bathymetric sources remain.',
        pngSha256=sha(encoded),decode='metres = -12000 + code * 21000 / 65535',maskedSea=False,
        metrics=metrics(a,nx,ny,dx,dz,0))
    (folder/'reference.json').write_text(json.dumps(report,indent=2),encoding='utf-8');return report

def self_test():
    require(metrics([10.]*64,8,8,1,1,0)['slope95Degrees']==0,'Flat slope wrong')
    a=[3*x+4*z for z in range(8) for x in range(8)]
    require(abs(metrics(a,8,8,3,4,0)['slopeMedianDegrees']-math.degrees(math.atan(math.sqrt(2))))<1e-10,'Metric slope wrong')
    raw=json.dumps({'table':{'columnNames':['latitude','longitude','z'],'rows':[[la,lo,-400+la*10+lo] for la in range(4) for lo in range(4)]}}).encode()
    a,w,h,*_=parse_reference(raw);require(a[0]==-370 and a[-1]==-397 and w==h==4,'Reference orientation or seabed wrong')
    print('Reference parser/metric checks passed; geographic approval NOT granted')
def main():
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--references',type=Path,required=True);args=p.parse_args();self_test()
    args.references.mkdir(exist_ok=False);refs=[];errors=[]
    for item in REFERENCES:
        try:refs.append(fetch_reference(item,args.references))
        except Exception as exc:errors.append(dict(reference=item[0],error=repr(exc)))
    sims=[]
    for d in sorted(args.root.glob('seed-*')):
        m=json.loads((d/'manifest.json').read_text());raw=(d/'height.f64le').read_bytes();require(sha(raw)==m['fieldsSha256'],'Candidate source changed')
        a=[v for v, in struct.iter_unpack('<d',raw)]
        sims.append(dict(name=d.name,sourceSha256=sha(raw),commit=m['commit'],metrics=metrics(a,m['width'],m['height'],m['stepX'],m['stepZ'],m['seaLevelReferenceBlocks'])))
    result=dict(schemaVersion=1,scope='EARTH_REFERENCE_MEASUREMENT_NOT_VISUAL_ACCEPTANCE',referenceAcquisition='COMPLETE' if not errors else 'INCOMPLETE',
        references=refs,failures=errors,candidates=sims,geographicAcceptance='REQUIRES_ASSISTANT_COMPARATIVE_REVIEW',erosionAuthorized=False,
        limitations=['Present-day Earth relief is already eroded; raw terrain is not expected to include finished drainage valleys.',
        'Metres and blocks are not silently equated. Relative-lag metrics are not a substitute for explicit scale calibration.',
        'Three large regions are not an exhaustive distribution of Earth relief.',
        'Green tests or numerical similarity never automatically authorize erosion.'])
    (args.root/'earth-comparison.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    if errors:raise RuntimeError('Reference acquisition incomplete; failures retained in report.')
    print('Observed ETOPO subsets fetched and measured; assistant geographic review still required')
if __name__=='__main__':main()
