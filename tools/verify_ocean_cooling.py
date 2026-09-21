#!/usr/bin/env python3
"""Strict existing platform gate plus thermal experiment provenance and balances."""
import argparse,json,math
from pathlib import Path
import verify_material_bound_platforms as base
from export_ocean_cooling import ALGORITHM

def verify(a,b):
    base.ALGORITHM=ALGORITHM
    r=base.verify(a,b)
    for root in (a,b):
        for directory in root.glob('seed-*'):
            m=json.loads((directory/'manifest.json').read_text())
            if m['thermalAlgorithm']!='ocean-finite-plate-carried-thermal-spectrum-v1' or not m['ThermalReceipts']:
                raise ValueError('Missing thermal provenance')
            if m['cooling']['RetainedOddModes']!=12 or m['cooling']['AnchorAgeMyr']!=50:
                raise ValueError('Uncontrolled thermal parameters')
            for s in m['ThermalReceipts']:
                if not all(math.isfinite(s[k]) for k in ('Time','MaximumBalanceResidual','OceanVolumeSum','ColdVolumeSum')):
                    raise ValueError('Nonfinite thermal receipt')
                if s['MaximumBalanceResidual']>2e-11 or s['ColdVolumeSum']<0 or s['ColdVolumeSum']>s['OceanVolumeSum']+1e-8:
                    raise ValueError('Thermal balance or carrier failure')
            if not all(s['sameCompleteAtlas'] and not s['crop'] for s in m['scaleChecks']):raise ValueError('Cropped atlas')
            for solve in m['mechanicalSolves']:
                if not math.isfinite(solve['RelativeResidual']) or solve['RelativeResidual']>1e-12:raise ValueError('Mechanical residual failed')
    r['scope']='FINITE_PLATE_THERMAL_NUMERICAL_COMPARISON_NOT_GEOGRAPHIC_ACCEPTANCE'
    return r
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('linux',type=Path);p.add_argument('windows',type=Path);p.add_argument('output',type=Path)
    a=p.parse_args()
    with a.output.open('x') as f:json.dump(verify(a.linux,a.windows),f,indent=2)
