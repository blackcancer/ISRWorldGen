#!/usr/bin/env python3
"""Reuse the strict material/platform gate and audit the new initial-state provenance."""
import argparse, json, math
from pathlib import Path
import verify_material_bound_platforms as material
from export_continental_assembly import ALGORITHM

def verify(linux, windows):
    material.ALGORITHM = ALGORITHM
    report = material.verify(linux, windows)  # unchanged per-field / PNG16 tolerances
    for root in (linux, windows):
        for directory in root.glob('seed-*'):
            m = json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
            a = m['initialAssemblage']
            if a['algorithm'] != 'continental-assemblages-area-budget-fabric-v1' or not a['Checksum']:
                raise ValueError('Missing initial material provenance')
            expected = a['MeanContinentalKm'] * m['ReferenceWidth'] * m['ReferenceLength'] * m['referenceKmPerUnit']**2
            actual = m['initial']['ContinentalVolume']
            if not math.isfinite(actual) or abs(actual-expected) > abs(expected)*2e-12 + 1e-8:
                raise ValueError('Continental volume changed')
            if not all(s['sameCompleteAtlas'] and not s['crop'] for s in m['scaleChecks']):
                raise ValueError('A small world is only a crop')
    report['scope'] = 'CONTINENTAL_ASSEMBLAGE_NUMERICAL_COMPARISON_NOT_EARTH_VALIDATION'
    return report

if __name__ == '__main__':
    p=argparse.ArgumentParser(); p.add_argument('linux',type=Path); p.add_argument('windows',type=Path); p.add_argument('output',type=Path)
    a=p.parse_args(); result=verify(a.linux,a.windows)
    with a.output.open('x', encoding='utf-8') as f: json.dump(result,f,indent=2)
