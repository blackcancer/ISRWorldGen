#!/usr/bin/env python3
"""Encode actual C# assemblage outputs. Never conceal the seabed or rescale each image."""
import argparse, json
from pathlib import Path
import export_tectonic_history as base

ALGORITHM = 'rheology-history-v1/material-bound-history-v3-carrier-resolved-origin-fluxes'

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
                        'initial-continental-thickness': (0, 65), 'initial-provinces': (-1, 5), 'relative-viscosity': (.25, 4), 'velocity-east': (-4000, 4000), 'velocity-south': (-4000, 4000), 'instant-divergence': (-.25, .25)})
    base.export(root)
    page = root / 'index.html'
    text = page.read_text(encoding='utf-8').replace('cinématique imposée,', 'tractions motrices paramétrées et équilibre visqueux réduit,')
    text = text.replace('<h1>', '<p>Résistance effective selon les matériaux transportés et leur âge ; pas de calibration terrestre. Grille mécanique 128² et grille matérielle 512² dans la campagne complète.</p><h1>', 1)
    page.write_text(text, encoding='utf-8')

if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('--root', type=Path, required=True)
    export(p.parse_args().root)
