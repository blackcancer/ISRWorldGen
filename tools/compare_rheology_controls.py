#!/usr/bin/env python3
"""Compare entire C# histories, verifying experimental controls before reporting differences.

No score in this report is a geographic acceptance criterion. All files retain
fixed physical/model units; there is no sea-level fitting or image rescaling.
"""
import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import sys

MODES = ('homogeneous', 'heterogeneous', 'powerlaw')
SEEDS = (-437287116, 20260906, 73)
INITIAL_FIELDS = ('initial-height', 'initial-continental-thickness', 'initial-provinces', 'legacy-initial-height')


def field(directory, manifest, key):
    item = manifest['fields'][key]
    path = Path(item['path'])
    if path.is_absolute() or len(path.parts) != 1:
        raise ValueError('Field must be a direct file in the seed directory')
    data = (directory / path).read_bytes()
    if hashlib.sha256(data).hexdigest() != item['sha256']:
        raise ValueError(f'Hash mismatch: {directory}/{key}')
    expected = manifest['width'] * manifest['height'] * 8
    if len(data) != expected:
        raise ValueError('Field geometry does not match the full atlas')
    values = array('d')
    values.frombytes(data)
    if sys.byteorder != 'little':
        values.byteswap()
    if not all(math.isfinite(v) for v in values):
        raise ValueError('Nonfinite diagnostic')
    return values


def require_controls(manifests):
    if set(manifests) != set(MODES):
        raise ValueError('Missing or duplicate constitutive control')
    baseline = manifests['heterogeneous']
    for mode, m in manifests.items():
        if m['mode'] != mode or m['algorithm'] != baseline['algorithm']:
            raise ValueError('Wrong constitutive provenance')
        for key in ('seed', 'commit', 'width', 'height', 'worldWidthBlocks', 'worldLengthBlocks',
                    'ReferenceWidth', 'ReferenceLength', 'referenceKmPerUnit', 'worldHeightBlocks',
                    'seaLevelReferenceBlocks', 'blocksPerModelKm', 'settings', 'mechanicalSide',
                    'mechanicalStepReferenceUnits', 'initialAssemblage', 'InitialMaterialChecksum',
                    'InitialContinentalByOrigin', 'plates'):
            if m[key] != baseline[key]:
                raise ValueError(f'Unpaired control {mode}/{key}')
        for key in ('MechanicalSide', 'UpdateInterval'):
            if m['rheology'][key] != baseline['rheology'][key]:
                raise ValueError('Changed mechanical sampling or update interval')
        if m['rheology']['HomogeneousControl'] != (mode == 'homogeneous'):
            raise ValueError('Uniform control has not been requested')
        power = m['rheology'].get('PowerLaw')
        if (power is not None) != (mode == 'powerlaw'):
            raise ValueError('Power-law switch does not match its label')
        if mode == 'powerlaw' and (power['StressExponent'] != 3 or power['ReferenceStrainRate'] != .02):
            raise ValueError('The predeclared constitutive prior was changed')
        times = [r['Time'] for r in m['mechanicalSolves']]
        if times != [r['Time'] for r in baseline['mechanicalSolves']]:
            raise ValueError('Different mechanical update histories')
        if m['seabedMasked'] or m['waterSurfacePresent'] or m['perImageAutoContrast']:
            raise ValueError('Masked or cosmetically rescaled heightmap')
    return baseline


def percentile(values, p):
    ordered = sorted(values)
    if not ordered:
        return None
    k = p * (len(ordered) - 1)
    i = int(k)
    f = k - i
    return ordered[i] if i == len(ordered) - 1 else ordered[i] * (1 - f) + ordered[i+1] * f


def statistics(directory, m):
    h = field(directory, m, 'height')
    sea = m['seaLevelReferenceBlocks']
    land = [v-sea for v in h if v >= sea]
    ocean = [v-sea for v in h if v < sea]
    receipts = m['mechanicalSolves']
    initial = m['initial']['ContinentalVolume']
    final = m['final']['ContinentalVolume']
    return {
        'landFraction': len(land)/len(h), 'solidRangeBlocks': [min(h), max(h)],
        'landMaximumAboveSeaBlocks': max(land) if land else None,
        'landP95AboveSeaBlocks': percentile(land, .95),
        'landMedianAboveSeaBlocks': percentile(land, .5),
        'oceanP05P50P95RelativeSeaBlocks': [percentile(ocean, p) for p in (.05, .5, .95)],
        'continentalRelativeBalanceError': abs(final-initial)/max(1, initial),
        'mechanicalSolves': len(receipts),
        'maximumActualForceResidual': max(r['RelativeResidual'] for r in receipts),
        'maximumNewtonIterations': max(r.get('NonlinearIterations', 0) for r in receipts),
        'maximumMechanicalCGIterations': max(r['Iterations'] for r in receipts),
        'minimumEffectiveViscosity': min(r['MinimumViscosity'] for r in receipts),
        'maximumEffectiveViscosity': max(r['MaximumViscosity'] for r in receipts),
        'historyChecksum': m['historyChecksum'],
        'heightSha256': m['fields']['height']['sha256'],
    }


def compare(root):
    records = []
    commits = set()
    for seed in SEEDS:
        paths = {mode: root/mode/f'seed-{seed}' for mode in MODES}
        manifests = {mode: json.loads((path/'manifest.json').read_text(encoding='utf-8'))
                     for mode, path in paths.items()}
        baseline = require_controls(manifests)
        if baseline['seed'] != seed:
            raise ValueError('Seed and evidence directory disagree')
        commits.add(baseline['commit'])
        for key in INITIAL_FIELDS:
            original = field(paths['heterogeneous'], baseline, key)
            for mode in MODES:
                other = field(paths[mode], manifests[mode], key)
                if other != original:
                    raise ValueError(f'Initial field changed {seed}/{mode}/{key}')
        fields = {mode: field(paths[mode], manifests[mode], 'height') for mode in MODES}
        deltas = {}
        for left, right in (('homogeneous','heterogeneous'), ('powerlaw','heterogeneous')):
            delta = [a-b for a,b in zip(fields[left], fields[right])]
            deltas[f'{left}-minus-{right}'] = {
                'minimumBlocks': min(delta), 'maximumBlocks': max(delta),
                'rmsBlocks': math.sqrt(math.fsum(d*d for d in delta)/len(delta))
            }
        records.append({'seed': seed, 'initialControlsMatchExactly': True,
                        'conditions': {k: baseline[k] for k in ('commit','width','height','mechanicalSide',
                                       'worldWidthBlocks','worldLengthBlocks','seaLevelReferenceBlocks','blocksPerModelKm')},
                        'modes': {mode: statistics(paths[mode], manifests[mode]) for mode in MODES},
                        'fieldDifferences': deltas})
    if len(commits) != 1:
        raise ValueError('A campaign mixes source versions')
    return {'status': 'PASS_PAIRED_EXPERIMENT_CONTROLS_ONLY', 'commit': commits.pop(),
            'geographicAcceptance': 'REQUIRES_FULL_MAP_REVIEW_NOT_IMPLIED_BY_NUMERICS',
            'erosion': 'NOT_RUN',
            'scope': 'Same initial crust, prescribed plates, material/mechanical resolution, length and update schedule. '
                     'Subsequent forcing follows the respective material states; it is not frozen after divergence.',
            'records': records}


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('root', type=Path)
    p.add_argument('output', type=Path)
    a = p.parse_args()
    result = compare(a.root)
    with a.output.open('x', encoding='utf-8') as f:
        json.dump(result, f, indent=2)
    print(json.dumps(result, indent=2))
