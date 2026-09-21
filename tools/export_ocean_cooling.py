#!/usr/bin/env python3
"""Actual C# ocean thermal experiment. No terrain generation or per-map contrast."""
import argparse, json
from pathlib import Path
import export_tectonic_history as base
ALGORITHM = 'ocean-thermal-comparison-v1/material-bound-history-v3-carrier-resolved-origin-fluxes'

def export(root):
    for directory in root.glob('seed-*'):
        m=json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
        if m['algorithm']!=ALGORITHM or m['thermalAlgorithm']!='ocean-finite-plate-carried-thermal-spectrum-v1':
            raise ValueError('Wrong thermal experiment')
        if m['seabedMasked'] or m['waterSurfacePresent']:
            raise ValueError('Not a solid height field')
    base.RANGES.update({'owner-fraction':(0,1),'legacy-initial-height':(0,383),
      'initial-continental-thickness':(0,65),'initial-provinces':(-1,5),
      'relative-viscosity':(0,4),'velocity-east':(-4000,4000),'velocity-south':(-4000,4000),
      'instant-divergence':(-.25,.25),'legacy-height':(0,383),'mean-age-cooling-height':(0,383),
      'thermal-cold-fraction':(0,1),'initial-thermal-cold-fraction':(0,1),'elevation-model':(-14,18)})
    # These two height controls are checked independently as well: the historical
    # exporter only refuses saturation for its two historical height role names.
    import hashlib,struct,math
    for directory in root.glob('seed-*'):
        m=json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
        for name in ('height','initial-height','legacy-height','mean-age-cooling-height'):
            f=m['fields'][name]; raw=(directory/f['path']).read_bytes()
            if hashlib.sha256(raw).hexdigest()!=f['sha256']:raise ValueError('Height hash mismatch')
            values=[v for v, in struct.iter_unpack('<d',raw)]
            if not all(math.isfinite(v) and 0<=v<=383 for v in values):raise ValueError('Height out of fixed range')
    base.export(root)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);export(p.parse_args().root)
