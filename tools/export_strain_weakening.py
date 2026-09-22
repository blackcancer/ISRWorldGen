#!/usr/bin/env python3
"""True solid heights only. No sea mask, shade, per-map contrast or new terrain."""
import hashlib,json,math,struct,sys
from pathlib import Path
from export_tectonic_history import png,read_pixels

def export(root):
    root=Path(root);m=json.loads((root/'manifest.json').read_text())
    if m['waterSurfacePresent'] or m['seabedMasked'] or m['width']!=512 or m['height']!=512:
        raise ValueError('Unexpected fixed full-world provenance')
    if (root/'png').exists():raise FileExistsError('Never overwrite PNG evidence')
    out={}
    for name in ('initial-height','height'):
        f=m['fields'][name];raw=(root/f['path']).read_bytes()
        if len(raw)!=512*512*8 or hashlib.sha256(raw).hexdigest()!=f['sha256']:raise ValueError('Invalid height source')
        values=[v[0] for v in struct.iter_unpack('<d',raw)]
        if not all(math.isfinite(v) and 0<=v<=383 for v in values):raise ValueError('Height outside fixed domain; no clipping')
        pixels=b''.join(struct.pack('>H',round(v*65535/383)) for v in values)
        data=png(512,512,pixels,16,0,'Solid Y = pixel*383/65535. Native resolution; includes all seabed.')
        if read_pixels(data)!=(512,512,16,0,pixels):raise ValueError('Height encoding mismatch')
        out[name+'-16bit.png']=data
        preview=bytes(round(v*255/383) for v in values for _ in range(3))
        out[name+'-preview.png']=png(512,512,preview,8,2,'Fixed Y0-383 preview; source 512x512, not added detail')
    (root/'png').mkdir()
    for name,b in out.items():(root/'png'/name).write_bytes(b)
    (root/'PNG-COMPLETE.json').write_text(json.dumps({'commit':m['commit'],'seed':m['seed'],'mode':m['mode'],
        'decode':'Y=pixel*383/65535','heightSourceUnchanged':True,'files':{k:hashlib.sha256(v).hexdigest() for k,v in out.items()}},indent=2))
if __name__=='__main__':export(Path(sys.argv[1]))
