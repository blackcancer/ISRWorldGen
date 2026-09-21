#!/usr/bin/env python3
"""Compare real C# routing/discharge before and after indexing optimizations.

Only for a clean, explicitly disposable CI checkout. Two source files are
restored byte-for-byte in finally. No world, save, native DLL or task state.
Measurements are observations on synthetic graphs, not pass thresholds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

BASE = '69d42c7ad1aea6708af8400025b1e59363fdbb60'
SOURCES = (
    'src/WorldGen.Core/Hydrology/Depressions/DepressionTopology.cs',
    'src/WorldGen.Core/Hydrology/Discharge/DischargeAccumulator.cs',
)
WITNESS = 'RoutingAndDischargeWitnessRetainsCanonicalBytesAndRecordsMeasurements'
REFERENCE = 'TinyGraphsAgreeWithIndependentMinimaxRelaxation'
PREFIX = 'ISRWorldGen.Tests.L05A.DrainageScalingTests.'
SHAPES = {'isolated', 'star', 'separate-cups', 'flat-cup', 'alternating-cup', 'river'}
SIZES = {256, 1024, 4096}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def read_witness(path: Path) -> list[dict]:
    entries = list(ET.parse(path).iterfind('.//{*}UnitTestResult'))
    require(len(entries) == 2 and all(item.get('outcome') == 'Passed' for item in entries),
            'The actual C# witness and independent oracle must both execute and pass.')
    require({item.get('testName') for item in entries} == {WITNESS, REFERENCE},
            'Wrong or missing C# test selection.')
    text = ''.join(next(item for item in entries if item.get('testName') == WITNESS).itertext())
    marker = 'DRAINAGE_SCALING_WITNESS='
    require(text.count(marker) == 1, 'Missing or duplicate numerical witness.')
    data, _ = json.JSONDecoder().raw_decode(text.split(marker, 1)[1].lstrip())
    require(isinstance(data, list) and len(data) == 18, 'Incomplete fixture matrix.')
    require({(item['shape'], item['size']) for item in data} == {(s, n) for s in SHAPES for n in SIZES},
            'Duplicate, omitted or unexpected graph shape/size.')
    for item in data:
        require(len(item['sha256']) == 64 and all(c in '0123456789abcdef' for c in item['sha256']),
                'Invalid canonical payload fingerprint.')
        require(item['terminalFlow'] == item['cells'], 'The unit-flow graph lost or created water.')
        require(item['routingMs'] >= 0 and item['dischargeMs'] >= 0 and item['allocatedBytes'] >= 0,
                'Invalid measured diagnostic.')
    return sorted(data, key=lambda item: (item['shape'], item['size']))


def compare(before: list[dict], after: list[dict]) -> list[dict]:
    metrics = []
    require(len(before) == len(after) == 18, 'Incomplete before/after comparison.')
    for old, new in zip(before, after):
        for key in ('shape', 'size', 'cells', 'sha256', 'terminalFlow'):
            require(old[key] == new[key], f'Changed {key} for {old["shape"]}/{old["size"]}.')
        metrics.append({
            'shape': new['shape'], 'size': new['size'], 'cells': new['cells'], 'sha256': new['sha256'],
            'beforeRoutingMs': old['routingMs'], 'afterRoutingMs': new['routingMs'],
            'beforeDischargeMs': old['dischargeMs'], 'afterDischargeMs': new['dischargeMs'],
            'beforeAllocatedBytes': old['allocatedBytes'], 'afterAllocatedBytes': new['allocatedBytes'],
        })
    return metrics


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--configuration', choices=('Debug', 'Release'), required=True)
    parser.add_argument('--disposable-checkout', action='store_true')
    args = parser.parse_args()
    require(args.disposable_checkout, 'A disposable checkout must be explicitly authorized.')
    root = Path(__file__).resolve().parents[1]
    require(not subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=root).strip(),
            'Tracked changes present; no source will be replaced.')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    project = root / 'testsrc/WorldGen.ClimateHydrology.Tests/WorldGen.ClimateHydrology.Tests.csproj'
    reports = project.parent / 'TestResults' / f'drainage-{head}-{args.configuration}'
    require(not reports.exists(), 'The report directory already exists; do not overwrite it.')
    reports.mkdir(parents=True)
    candidate = {name: (root / name).read_bytes() for name in SOURCES}
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US')
    selected = '|'.join('FullyQualifiedName=' + PREFIX + name for name in (WITNESS, REFERENCE))

    def run(command: list[str], label: str) -> None:
        result = subprocess.run(command, cwd=root, env=env, text=True, encoding='utf-8', errors='replace',
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=300, check=False)
        (reports / (label + '.log')).write_text(result.stdout, encoding='utf-8')
        print(f'{label}: exit={result.returncode}', flush=True)
        require(result.returncode == 0, f'{label} failed; a build failure is not performance evidence.\n{result.stdout}')

    def build_and_test(label: str) -> list[dict]:
        run(['dotnet', 'build', str(project), '-c', args.configuration, '-t:Rebuild', '--nologo'], label + '-build')
        run(['dotnet', 'test', str(project), '-c', args.configuration, '--no-build', '--no-restore',
             '--filter', selected, '--logger', f'trx;LogFileName={label}.trx',
             '--results-directory', str(reports)], label)
        return read_witness(reports / (label + '.trx'))

    try:
        for name in SOURCES:
            (root / name).write_bytes(subprocess.check_output(['git', 'show', f'{BASE}:{name}'], cwd=root))
        before = build_and_test('baseline')
    finally:
        for name, data in candidate.items():
            (root / name).write_bytes(data)
        require(all((root / name).read_bytes() == data for name, data in candidate.items()),
                'Candidate source restoration failed.')
    after = build_and_test('candidate')
    metrics = compare(before, after)
    require(not subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=root).strip(),
            'Tracked repository state changed during the test.')
    report = {
        'status': 'PASS', 'scope': 'CORE_GRAPH_ROUTING_DISCHARGE_ONLY',
        'commit': head, 'baseline_source_commit': BASE, 'test_fixture_commit': head,
        'configuration': args.configuration, 'platform': os.name,
        'canonical_payloads_unchanged': True, 'graph_fixtures': len(metrics),
        'independent_minimax_cases_per_variant': 192, 'measurement_repetitions': 3,
        'measurement_statistic': 'median; no speed or allocation threshold is an acceptance criterion',
        'native_game': 'NOT_RUN', 'full_solution': 'NOT_RUN', 'independent_review': 'NOT_RUN',
        'sources_restored': True,
        'source_sha256': {name: hashlib.sha256(data).hexdigest() for name, data in candidate.items()},
        'measurements': metrics,
    }
    (reports / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
