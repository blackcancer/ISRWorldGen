#!/usr/bin/env python3
"""Run real Core climate/hydrology regressions on a disposable checkout only.

Temporarily restores the two production sources from the pinned baseline to
prove the precise defect and compare valid numerical output byte-for-byte.
Never run in a developer worktree: --disposable-checkout is mandatory, tracked
changes are rejected, restored sources are verified in finally, and no native
process, game assembly, certified evidence campaign or task registry is used.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

BASE = 'de4a0db6b2912f0bf95a8b28f3a7035985a1ccc4'
SOURCES = ('src/WorldGen.Core/Climate/Precipitation/PrecipitationModel.cs',
           'src/WorldGen.Core/Climate/WaterBudget/WaterBudgetModel.cs')
PREFIX = 'ISRWorldGen.Tests.L04C.WaterBudgetInputAssociationTests.'
RED = 'RejectsMisattributedPrecipitationInsteadOfBalancingTheWrongCell'
FINGERPRINT = 'ValidClimatePipelineProducesStableBaselineFingerprint'
NEW_TESTS = {
    RED, FINGERPRINT,
    'RejectsNonFiniteOrNegativeAtmosphericMoistureAtTheHandoff',
    'MissingRainAndInvalidRainRemainErrorsRatherThanDryCells',
    'ValidZeroRainAndZeroIdAreNotMistakenForMissingData',
    'InvalidSecondCellFailsBeforeAnyTransferEnumerationOrPublication',
    'ManyReservoirsKeepLocalConservationAndCanonicalTransferOrder',
    'OverdrawCannotBorrowTheRechargeOfAnotherIndexedReservoir',
    'LookupPreservesFullPayloadAtInt64ExtremesAndOnMiss',
    'SingleZeroIdFieldIsFoundButMissingIdsReturnDefault',
    'LargeSnapshotSupportsParallelReadsWithoutChangingPublishedFields',
    'ManyWindwardPortsRetainExactSupplyAndPermutationInvariantResults',
    'IndexedPortsStillRejectUnknownDuplicateLandAndDownwindEntries',
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def results(path: Path) -> list[ET.Element]:
    return list(ET.parse(path).iterfind('.//{*}UnitTestResult'))


def fingerprint(path: Path) -> str:
    entries = [item for item in results(path) if item.get('testName') == FINGERPRINT]
    require(len(entries) == 1 and entries[0].get('outcome') == 'Passed', 'Missing successful numerical witness.')
    text = ''.join(entries[0].itertext())
    match = re.search(r'CLIMATE_PIPELINE_SHA256=([0-9a-f]{64})', text)
    require(match is not None, 'Pipeline fingerprint was not emitted by the actual C# test.')
    return match.group(1)


def verify_diagnostics(directory: Path, commit: str, expected_hash: str) -> dict[str, bytes]:
    """A green TRX alone cannot hide missing Windows attachments or stale maps."""
    base = directory / 'climate-pipeline-analytic-v1'
    manifest = json.loads((base / 'manifest.json').read_text(encoding='utf-8'))
    require(manifest['commit'] == commit and manifest['scope'] == 'CORE_CLIMATE_WATER_ONLY',
            'Diagnostic provenance is stale or belongs to another scope.')
    require(manifest['fieldSha256'] == expected_hash and manifest['width'] == 16 and manifest['height'] == 8,
            'Unexpected numeric diagnostic or raster dimensions.')
    require(manifest['units'] == 'L/Ymod' and manifest['palette'] == 'linear-greyscale-0-to-16-v1',
            'Diagnostic units/palette changed.')
    require(manifest['nativeGame'] == 'NOT_RUN', 'Core fixture cannot qualify the native game.')
    payloads = {'fields.json': (base / 'fields.json').read_bytes()}
    require(hashlib.sha256(payloads['fields.json']).hexdigest() == expected_hash,
            'Retained field bytes differ from the C# fingerprint.')
    layers = manifest['layers']
    require(len(layers) == 3 and {layer['path'] for layer in layers} ==
            {'precipitation.svg', 'runoff.svg', 'recharge.svg'}, 'Incomplete or unexpected map bundle.')
    for layer in layers:
        data = (base / layer['path']).read_bytes()
        require(hashlib.sha256(data).hexdigest() == layer['sha256'], 'Retained SVG checksum mismatch.')
        svg = ET.fromstring(data)
        require(svg.get('viewBox') == '0 0 16 8' and len(svg.findall('{*}rect')) == 128,
                'Retained map does not contain the complete analytical grid.')
        payloads[layer['path']] = data
    return payloads


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--configuration', choices=('Debug', 'Release'), required=True)
    parser.add_argument('--disposable-checkout', action='store_true')
    args = parser.parse_args()
    require(args.disposable_checkout, 'Only an explicitly disposable CI checkout may run the before/after witness.')
    root = Path(__file__).resolve().parents[1]
    require(not subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=root).strip(),
            'Tracked changes present: no source will be replaced.')
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    project = root / 'testsrc/WorldGen.ClimateHydrology.Tests/WorldGen.ClimateHydrology.Tests.csproj'
    reports = project.parent / 'TestResults' / f'ci-{commit}-{args.configuration}'
    require(not reports.exists(), 'Report target already exists; never overwrite a previous run.')
    reports.mkdir(parents=True)
    candidate = {name: (root / name).read_bytes() for name in SOURCES}

    def run(command: list[str], label: str, source_commit: str = commit) -> subprocess.CompletedProcess[str]:
        env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US', GITHUB_SHA=source_commit,
                   ISR_L04A_EVIDENCE_COMMIT=source_commit,
                   ISR_CLIMATE_DIAGNOSTICS_ROOT=str(reports / 'diagnostics' / label))
        result = subprocess.run(command, cwd=root, env=env, text=True, encoding='utf-8', errors='replace',
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=300, check=False)
        (reports / (label + '.log')).write_text(result.stdout, encoding='utf-8')
        print(f'{label}: exit={result.returncode}', flush=True)
        return result

    def build(label: str, source_commit: str) -> None:
        result = run(['dotnet', 'build', str(project), '-c', args.configuration, '-t:Rebuild', '--nologo'],
                     label, source_commit)
        require(result.returncode == 0, f'{label} failed: this is not a valid red-test witness.\n{result.stdout}')

    def test(label: str, selected: str | None, source_commit: str) -> subprocess.CompletedProcess[str]:
        command = ['dotnet', 'test', str(project), '-c', args.configuration, '--no-build', '--no-restore',
                   '--logger', f'trx;LogFileName={label}.trx', '--results-directory', str(reports)]
        if selected is not None:
            command += ['--filter', selected]
        return run(command, label, source_commit)

    restored = False
    try:
        for name in SOURCES:
            (root / name).write_bytes(subprocess.check_output(['git', 'show', f'{BASE}:{name}'], cwd=root))
        build('baseline-build', BASE)
        red = test('baseline-red', 'FullyQualifiedName=' + PREFIX + RED, BASE)
        red_entries = results(reports / 'baseline-red.trx')
        require(red.returncode != 0 and len(red_entries) == 1 and red_entries[0].get('outcome') == 'Failed'
                and red_entries[0].get('testName') == RED
                and 'MISATTRIBUTED_PRECIPITATION_ACCEPTED' in ''.join(red_entries[0].itertext()),
                'Baseline did not execute and expose the exact wrong-cell acceptance.')
        valid = test('baseline-valid', 'FullyQualifiedName=' + PREFIX + FINGERPRINT, BASE)
        require(valid.returncode == 0, valid.stdout)
        before = fingerprint(reports / 'baseline-valid.trx')
        before_maps = verify_diagnostics(reports / 'diagnostics' / 'baseline-valid', BASE, before)
    finally:
        for name, data in candidate.items():
            (root / name).write_bytes(data)
        restored = all((root / name).read_bytes() == data for name, data in candidate.items())
        require(restored, 'Candidate sources were not restored byte-for-byte.')

    build('candidate-build', commit)
    suite = test('candidate', None, commit)
    require(suite.returncode == 0, suite.stdout)
    entries = results(reports / 'candidate.trx')
    require(bool(entries) and all(item.get('outcome') == 'Passed' for item in entries), 'Failed/skipped/missing test.')
    names = {item.get('testName') for item in entries}
    require(NEW_TESTS <= names, f'New regressions not all executed: {NEW_TESTS - names}')
    # Require original downstream consumers too, not just the new test classes.
    definitions = ET.parse(reports / 'candidate.trx')
    classes = {item.get('className', '') for item in definitions.iterfind('.//{*}TestMethod')}
    for lot in ('L04A', 'L04B', 'L04C', 'L05A', 'L05B', 'L05C'):
        require(any('.' + lot + '.' in name for name in classes), f'Original {lot} tests were not included.')
    after = fingerprint(reports / 'candidate.trx')
    require(before == after, 'Valid precipitation/water/transfer output changed from the pinned baseline.')
    after_maps = verify_diagnostics(reports / 'diagnostics' / 'candidate', commit, after)
    require(before_maps == after_maps, 'Valid fields/maps changed from the pinned baseline.')
    require(not subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=root).strip(),
            'CI tests modified tracked repository state.')
    result = dict(status='PASS', scope='REAL_CORE_CLIMATE_HYDROLOGY_REGRESSION', commit=commit, baseline=BASE,
                  configuration=args.configuration, tests=len(entries), new_tests=len(NEW_TESTS), failed=0, skipped=0,
                  expected_red='MISATTRIBUTED_PRECIPITATION_ACCEPTED',
                  valid_pipeline_sha256=after, baseline_pipeline_sha256=before, valid_output_unchanged=True,
                  diagnostics_verified=True, retained_maps_per_variant=3, field_and_map_bytes_unchanged=True,
                  sources_restored=restored,
                  source_sha256={name: hashlib.sha256(data).hexdigest() for name, data in candidate.items()},
                  native_game='NOT_RUN', full_solution='NOT_RUN', independent_review='NOT_RUN',
                  user_reported_prior_development_tests=346)
    (reports / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
