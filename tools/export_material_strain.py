"""Actual full-world heights and passive marker diagnostics, not new rift geometry."""
import hashlib,json,math,struct,sys
from pathlib import Path
from export_spatial_png import png,read_pixels

def export(root):
    out=root/'png'
    if out.exists(): raise FileExistsError('Do not overwrite image evidence')
    if not (root/'COMPLETE.json').exists() or (root/'FAILED.json').exists():raise ValueError('No completed C sharp world')
    m=json.loads((root/'manifest.json').read_text()); markers=json.loads((root/'markers.json').read_text())
    w,h=m['width'],m['height']; samples=markers['Samples'];n=markers['Options']['MarkerSide']
    if len(samples)!=n*n or w!=512 or h!=512:raise ValueError('Wrong native dimensions')
    prepared={};heights=None
    for name in ('initial-height','height'):
        f=m['fields'][name];b=(root/f['path']).read_bytes()
        if len(b)!=w*h*8 or hashlib.sha256(b).hexdigest()!=f['sha256']:raise ValueError('Invalid source field')
        values=[v[0] for v in struct.iter_unpack('<d',b)]
        if not all(math.isfinite(v) and 0<=v<=383 for v in values):raise ValueError('Height outside fixed domain; never clip')
        pixels=b''.join(struct.pack('>H',round(v*65535/383)) for v in values)
        prepared[name+'-16bit.png']=png(w,h,pixels,16,0,'TRUE solid height Y=pixel*383/65535; no sea mask; passive observer changes no terrain')
        if read_pixels(prepared[name+'-16bit.png'])!=(w,h,16,0,pixels):raise ValueError('PNG roundtrip failed')
        grey=bytes(round(v*255/383) for v in values)
        prepared[name+'-preview.png']=png(w,h,bytes(v for g in grey for v in (g,g,g)),8,2,'Fixed Y0..383 8-bit preview of actual solid height')
        if name=='height': heights=grey
    area=[];overlay=bytearray(channel for grey in heights for channel in (grey,grey,grey))
    for i,p in enumerate(samples):
        if p['Id']!=i or not (0<=p['CurrentX']<markers['ReferenceWidth'] and 0<=p['CurrentZ']<markers['ReferenceLength']):raise ValueError('Invalid marker coordinates')
        f=p['F'];det=f['XX']*f['ZZ']-f['XZ']*f['ZX']
        if not math.isfinite(det) or det<=0 or abs(det-p['AreaRatio'])>1e-9*det:raise ValueError('Invalid finite strain evidence')
        # Fixed monotone auxiliary encoding valid for all positive J. NOT altitude.
        value=.5+math.atan(math.log2(det))/math.pi
        area.append(round(value*65535))
        if p['ContinentalThinningCandidate']:
            x=int(p['CurrentX']/markers['ReferenceWidth']*w);z=int(p['CurrentZ']/markers['ReferenceLength']*h)
            for oz,ox in ((0,0),(-1,0),(1,0),(0,-1),(0,1)):
                at=3*(((z+oz)%h)*w+(x+ox)%w);overlay[at:at+3]=bytes((220,70,45))
    prepared['reference-area-ratio-16bit.png']=png(n,n,b''.join(struct.pack('>H',v) for v in area),16,0,'NOT HEIGHT: initial material coordinates; J=2**tan(pi*(pixel/65535-.5)); endpoints are asymptotic')
    prepared['reference-area-ratio-preview.png']=png(n,n,bytes(round(v*255/65535) for v in area for _ in range(3)),8,2,'NOT HEIGHT: passive area stretch at initial material IDs; grey=J1, bright=extension, dark=compression')
    prepared['current-thinning-overlay.png']=png(w,h,bytes(overlay),8,2,'ANNOTATED NOT HEIGHTMAP: red=current positions of passive thinning candidates; not resolved fractures')
    out.mkdir()
    for name,b in prepared.items():(out/name).write_bytes(b)
    receipt=dict(sourceCommit=m['commit'],seed=m['seed'],seabedMasked=False,heightDecode='Y=pixel*383/65535',
        markerMap='INITIAL_MATERIAL_COORDINATES_NOT_CURRENT_WORLD_RASTER',
        overlay='CURRENT_CANDIDATE_POINTS_NOT_PLATE_BOUNDARIES',markerSha256=hashlib.sha256((root/'markers.json').read_bytes()).hexdigest(),
        files={n:hashlib.sha256(b).hexdigest() for n,b in prepared.items()})
    (root/'PNG-COMPLETE.json').write_text(json.dumps(receipt,indent=2))
    print(json.dumps(receipt,indent=2))
def self_test():
    # The repository writer supports RGB8 previews and true grayscale16 only.
    grey=bytes([0,127,255]);rgb=bytes(v for g in grey for v in (g,g,g))
    assert read_pixels(png(3,1,rgb,8,2,'Preview regression'))==(3,1,8,2,rgb)
    raw=b''.join(struct.pack('>H',v) for v in (0,32768,65535))
    assert read_pixels(png(3,1,raw,16,0,'True height regression'))==(3,1,16,0,raw)

if __name__=='__main__':
    self_test()
    if sys.argv[1:]!=['--self-test']:export(Path(sys.argv[1]))
