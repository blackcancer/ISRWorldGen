#!/usr/bin/env python3
"""Keep existing height/PNG tolerances; verify candidate identity and per-origin balances."""
import argparse
import json
import math
from pathlib import Path
from verify_tectonic_platforms import compare
from export_material_bound_history import ALGORITHM

def verify(linux: Path, windows: Path) -> dict:
    # The existing gate is not weakened, bypassed or replaced.
    report = compare(linux, windows)
    for directory in sorted(linux.glob('seed-*')):
        pair = [json.loads((root/directory.name/'manifest.json').read_text(encoding='utf-8')) for root in (linux, windows)]
        for manifest in pair:
            if manifest['algorithm'] != ALGORITHM or manifest['waterSurfacePresent'] or manifest['seabedMasked']:
                raise ValueError('Wrong model or masked solid surface.')
            a, b = manifest['InitialContinentalByOrigin'], manifest['FinalContinentalByOrigin']
            if len(a) != len(b) or len(a) != manifest['settings']['PlateCount']:
                raise ValueError('Missing continental origin inventories.')
            for initial, final in zip(a,b):
                if not all(math.isfinite(v) and v>=0 for v in (initial,final)) or abs(initial-final)>1e-8+2e-12*abs(initial):
                    raise ValueError('Continental origin inventory is not conserved.')
        if pair[0]['UnresolvedInterfaceFaces'] != pair[1]['UnresolvedInterfaceFaces']:
            raise ValueError('Platform-dependent unresolved interface classification.')
    report['scope'] = 'MATERIAL_BOUND_CANDIDATE_NUMERICAL_ONLY'
    report['geographicAcceptance'] = 'NOT_ACCEPTED'
    return report

if __name__ == '__main__':
    p = argparse.ArgumentParser();p.add_argument('linux',type=Path);p.add_argument('windows',type=Path);p.add_argument('new_output',type=Path)
    a=p.parse_args();result=verify(a.linux,a.windows)
    with a.new_output.open('x',encoding='utf-8') as f: json.dump(result,f,indent=2)
