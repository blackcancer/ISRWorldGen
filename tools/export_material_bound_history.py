#!/usr/bin/env python3
"""Export the actual C# material-bound campaign, not Python reference fixtures."""
import argparse
import json
from pathlib import Path
import export_tectonic_history as base

ALGORITHM = 'material-bound-history-v3-carrier-resolved-origin-fluxes'

def export(root: Path) -> None:
    directories = sorted(root.glob('seed-*'))
    if len(directories) != 3:
        raise ValueError('Expected the complete fixed three-seed C# campaign.')
    for directory in directories:
        manifest = json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
        if manifest['algorithm'] != ALGORITHM or manifest['scope'] != 'EXPERIMENTAL_MATERIAL_ATTACHED_KINEMATICS_NOT_FORCE_BALANCED':
            raise ValueError('Not a material-bound C# campaign.')
        if 'owner-fraction' not in manifest['fields'] or not manifest.get('FinalMaterialChecksum'):
            raise ValueError('Missing material provenance or mixing diagnostics.')
    base.RANGES['owner-fraction'] = (0, 1)
    base.export(root)

if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('--root', type=Path, required=True)
    export(p.parse_args().root)
