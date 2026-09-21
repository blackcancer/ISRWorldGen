#!/usr/bin/env python3
"""Encode real material and mechanical fields at their OWN native resolutions."""
import argparse, hashlib, json, math, struct
from pathlib import Path
import export_tectonic_history as base
from export_continental_assembly import ALGORITHM as BASELINE

ALGORITHM = 'material-membrane-history-v1/material-bound-history-v3-carrier-resolved-origin-fluxes'
RANGES = {'resistance': (0, 12), 'east-velocity': (-4000, 4000), 'south-velocity': (-4000, 4000),
          'divergence': (-.1, .1), 'shear-rate': (-.1, .1)}

def export(root):
    directories = sorted(root.glob('seed-*'))
    if len(directories) not in (1,3): raise ValueError('Expected one seed or the complete campaign')
    for directory in directories:
        m=json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
        if m['algorithm']!=ALGORITHM or not m['mechanics']['initialMaterialsIdentical']:
            raise ValueError('Invalid mechanical comparison provenance')
        if m['mechanics']['maximumRelativeForceResidual']>1e-9: raise ValueError('Unsolved force balance')
    base.RANGES.update({'owner-fraction': (0,1), 'legacy-initial-height': (0,383),
        'initial-continental-thickness': (0,65), 'initial-provinces': (-1,5), 'baseline-height': (0,383)})
    base.export(root)
    for directory in directories:
        rootm=directory/'mechanics'; m=json.loads((rootm/'manifest.json').read_text(encoding='utf-8'))
        w,h=m['width'],m['height']; out=rootm/'png'; out.mkdir(exist_ok=False); report={}
        for name,field in m['fields'].items():
            raw=(rootm/field['path']).read_bytes()
            if len(raw)!=w*h*8 or hashlib.sha256(raw).hexdigest()!=field['sha256']: raise ValueError('Mechanical source hash/shape')
            values=[v for v, in struct.iter_unpack('<d',raw)]
            lo,hi=RANGES[name]
            if not all(math.isfinite(v) and lo<=v<=hi for v in values): raise ValueError('Mechanical field outside explicit encoding')
            codes=[round((v-lo)*65535/(hi-lo)) for v in values]
            pixels=b''.join(struct.pack('>H',v) for v in codes)
            image=base.png(w,h,pixels,16,0,'Real resolved mechanical field; not interpolated altitude')
            if base.read_pixels(image)!=(w,h,16,0,pixels): raise ValueError('Mechanical PNG roundtrip')
            (out/(name+'-16bit.png')).write_bytes(image)
            rgb=bytes(round(q*255/65535) for q in codes for _ in range(3))
            (out/(name+'-preview.png')).write_bytes(base.png(w,h,rgb,8,2,'Fixed native-resolution mechanical diagnostic'))
            report[name]={'decode':[lo,hi],'sourceSha256':field['sha256'],'maximumQuantizationError':max(abs(v-(lo+q*(hi-lo)/65535)) for v,q in zip(values,codes))}
        (out/'encoding.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    # Do not inherit the baseline page's obsolete description of kinematics.
    (root/'index.html').write_text("<!doctype html><html lang='fr'><meta charset='utf-8'><title>Membrane — comparaison</title>"
      "<style>body{font:17px system-ui;max-width:1100px;margin:auto;padding:25px}img{max-width:100%;image-rendering:pixelated}</style>"
      "<h1>Résistance des matériaux — candidat non accepté géographiquement</h1><p>Chaque image couvre le monde entier de 1 000 000 × 1 000 000 blocs. "
      "Heightmaps solides Y0..383, sans eau, fond marin conservé, aucune érosion. Matériaux 512² pour la campagne principale ; "
      "mécanique 128². Les vitesses de faces sont interpolées pour transporter les matériaux, pas les altitudes.</p>"+''.join(
        f"<h2>{d.name}</h2><h3>Référence cinématique, état initial identique</h3><img src='{d.name}/png/baseline-height-preview.png'>"
        f"<h3>Réponse visqueuse hétérogène</h3><img src='{d.name}/png/height-preview.png'><p><a href='{d.name}/png/height-16bit.png'>Heightmap 16 bits</a></p>"
        f"<h3>Résistance au pas mécanique natif</h3><img src='{d.name}/mechanics/png/resistance-preview.png'>" for d in directories)+"</html>",encoding='utf-8')

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);export(p.parse_args().root)
