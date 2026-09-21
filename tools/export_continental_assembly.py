#!/usr/bin/env python3
"""Encode actual C# assemblage outputs. Never conceal the seabed or rescale each image."""
import argparse, json
from pathlib import Path
import export_tectonic_history as base

ALGORITHM = 'continental-assembly-history-v1/material-bound-history-v3-carrier-resolved-origin-fluxes'

def export(root):
    directories = sorted(root.glob('seed-*'))
    if len(directories) not in (1, 3):
        raise ValueError('Expected one explicit job seed or the complete three-seed campaign')
    for directory in directories:
        m = json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
        if m['algorithm'] != ALGORITHM or not m['initialAssemblage']['matchedContinentalVolume']:
            raise ValueError('Wrong algorithm or uncontrolled continental volume comparison')
        if m['seabedMasked'] or m['waterSurfacePresent']:
            raise ValueError('A water surface is not a heightmap')
    base.RANGES.update({'owner-fraction': (0, 1), 'legacy-initial-height': (0, 383),
                        'initial-continental-thickness': (0, 65), 'initial-provinces': (-1, 5)})
    base.export(root)

if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('--root', type=Path, required=True)
    export(p.parse_args().root)
