#!/usr/bin/env python3
"""Keep all historical material tolerances, add actual mechanical field checks."""
import argparse,json
from pathlib import Path
import verify_material_bound_platforms as material
from verify_tectonic_platforms import read_field
from export_spatial_png import read_pixels
from export_membrane_history import ALGORITHM

def verify(linux,windows):
    material.ALGORITHM=ALGORITHM
    report=material.verify(linux,windows)
    for record in report['results']:
        name='seed-'+str(record['seed']); values=[]
        for root in (linux,windows):
            m=json.loads((root/name/'manifest.json').read_text(encoding='utf-8'))
            if not m['mechanics']['initialMaterialsIdentical'] or m['mechanics']['maximumRelativeForceResidual']>1e-9:
                raise ValueError('Changed initial materials or failed force balance')
            if not all(s['sameCompleteAtlas'] and not s['crop'] for s in m['scaleChecks']): raise ValueError('Cropped map')
            mm=json.loads((root/name/'mechanics/manifest.json').read_text(encoding='utf-8'))
            if mm['algorithm']!='viscous-membrane-mac-drag-pcg-v1' or mm['residual']>1e-9: raise ValueError('Bad mechanical identity/residual')
            values.append((mm,{k:read_field(root,name+'/mechanics',f,mm['width']*mm['height']) for k,f in mm['fields'].items()}))
        a,b=values
        if a[0]['width']!=b[0]['width'] or a[1].keys()!=b[1].keys(): raise ValueError('Mechanical grids differ')
        record['mechanicalMaxDeltas']={k:max(abs(x-y) for x,y in zip(a[1][k],b[1][k])) for k in a[1]}
        if any(v>1e-8 for v in record['mechanicalMaxDeltas'].values()): raise ValueError('Platform-dependent mechanics')
        for layer in ('height','initial-height','baseline-height'):
            if read_pixels((linux/name/'png'/f'{layer}-16bit.png').read_bytes())!=read_pixels((windows/name/'png'/f'{layer}-16bit.png').read_bytes()):
                raise ValueError('Height pixels differ')
    report['scope']='MATERIAL_MEMBRANE_NUMERICAL_COMPARISON_NOT_GEOGRAPHIC_ACCEPTANCE'
    return report
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('linux',type=Path);p.add_argument('windows',type=Path);p.add_argument('output',type=Path)
    a=p.parse_args();r=verify(a.linux,a.windows)
    with a.output.open('x',encoding='utf-8') as f:json.dump(r,f,indent=2)
