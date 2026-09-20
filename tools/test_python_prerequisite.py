"""Qualify Python discovery and the original four L05-D tests on CI fixtures.
No game assemblies or user saves; missing prerequisites are failures, not skips.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

BASELINE = 'dfcac47b05ce1affe0da07ce4e28e894ebec3daf'
SOURCE = 'testsrc/WorldGen.Tests/L05D/PlanAdoptionTests.cs'
REQUIRED = {
    'T05U_01_RealPreparePlanUpdatePreservesHistoricalStateAndUnknownTask',
    'T05U_01_RejectsBlankStateAndManifestThatAttemptsStateOrCode',
    'T05U_02_ExistingBoundaryPortCannotReceiveUnversionedSeasonalField',
    'T05U_03_ProspectiveL18AToL11BDependencyIsRejectedAsCycleWithoutWritingState',
}


def check(value: bool, message: str) -> None:
    if not value:
        raise AssertionError(message)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--configuration', choices=['Debug', 'Release'], required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    reports = root / 'testsrc/WorldGen.PlanAdoption.Tests/TestResults'
    reports.mkdir(parents=True, exist_ok=True)
    shell = shutil.which('pwsh')
    dotnet = shutil.which('dotnet')
    check(shell is not None and dotnet is not None, 'Installed PowerShell 7 and .NET SDK required.')
    env = dict(os.environ)
    env.pop('ISR_TEST_PYTHON', None)
    for name in list(env):
        if name.startswith('ISR_L03B_EVIDENCE_'):
            del env[name]

    def run(command: list[str], label: str, environment=env):
        result = subprocess.run(command, cwd=root, env=environment, text=True,
                                encoding='utf-8', errors='replace', stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=240, check=False)
        (reports / f'{label}.log').write_text(result.stdout, encoding='utf-8')
        return result

    def passed(result):
        check(result.returncode == 0, result.stdout)
        return result

    before_state = hashlib.sha256((root / 'registry/state.json').read_bytes()).hexdigest()
    before_settings = (root / 'tests/Development.runsettings').read_bytes()
    old = passed(run(['git', 'show', f'{BASELINE}:{SOURCE}'], 'baseline-source')).stdout
    current = (root / SOURCE).read_text(encoding='utf-8-sig')
    def methods(text):
        return text.split('[TestClass]', 1)[1].split('internal static class ContractGate', 1)[0]
    check(methods(old) == methods(current), 'The four original test methods/acceptance assertions must not be weakened.')
    wrapper = root / 'tools/Invoke-DevelopmentTests.ps1'
    invocation = [shell, '-NoLogo', '-NoProfile', '-NonInteractive', '-File', str(wrapper),
                  '-Scope', 'L05DProtocol', '-Configuration', args.configuration]
    with tempfile.TemporaryDirectory(prefix='ISR Python espace-') as tmp:
        temp = Path(tmp)
        # An installed venv with a space and an accent, deliberately outside PATH.
        venv = temp / 'Python configuré'
        passed(run([sys.executable, '-I', '-B', '-m', 'venv', '--without-pip', str(venv)], 'create-venv'))
        real = venv / ('Scripts/python.exe' if os.name == 'nt' else 'bin/python')
        check(real.is_file(), 'The private test interpreter was not created.')
        ready = passed(run(invocation + ['-PreflightOnly', '-PythonPath', str(real)], 'preflight'))
        info = json.loads(ready.stdout)
        check(info['Tests'] == 'NOT_RUN' and info['Status'] == 'READY_FOR_TEST_INVOCATION', 'Preflight must not claim executed tests.')
        check(os.path.normcase(info['PythonPath']) == os.path.normcase(str(real)), 'Explicit interpreter identity was lost.')
        check(tuple(map(int, info['PythonVersion'].split('.')[:2])) >= (3, 10), 'Python version was not validated.')
        invalid = run(invocation + ['-PreflightOnly', '-PythonPath', str(temp / 'missing-python')], 'invalid-explicit')
        check(invalid.returncode != 0 and 'PYTHON_PREREQUISITE_INVALID' in invalid.stdout, 'Invalid explicit settings must not fall back.')
        wrong = run(invocation + ['-PreflightOnly', '-PythonPath', dotnet], 'non-python-executable')
        check(wrong.returncode != 0 and 'PYTHON_PREREQUISITE_INVALID' in wrong.stdout, 'An existing non-Python executable must fail the probe.')
        if os.name == 'nt':
            ps51 = shutil.which('powershell.exe')
            check(ps51 is not None, 'Windows bootstrap requires PowerShell 5.1.')
            bootstrap = passed(run([ps51] + invocation[1:] + ['-PreflightOnly', '-PythonPath', str(real)], 'bootstrap51'))
            check(os.path.normcase(json.loads(bootstrap.stdout)['PythonPath']) == os.path.normcase(str(real)), '5.1 bootstrap lost PythonPath.')
        # Compiled unavailable-interpreter stand-in. Unlike the Store alias it
        # cannot open a store, install anything, or access non-fixture files.
        fixture = temp / 'alias-project'
        fixture.mkdir()
        (fixture / 'alias.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>python</AssemblyName></PropertyGroup></Project>', encoding='utf-8')
        (fixture / 'Program.cs').write_text('var marker = System.Environment.GetEnvironmentVariable("ISR_TEST_ALIAS_MARKER"); if (!string.IsNullOrEmpty(marker)) System.IO.File.AppendAllText(marker, "probe\\n"); System.Console.Error.WriteLine("CONTROLLED_UNAVAILABLE_PYTHON"); return 9009;', encoding='utf-8')
        alias_dir = temp / 'fake-bin'
        passed(run([dotnet, 'build', str(fixture / 'alias.csproj'), '-o', str(alias_dir), '--nologo'], 'build-alias-fixture'))
        path_key = next(k for k in env if k.upper() == 'PATH')
        masked = dict(env)
        marker = temp / 'alias-called.txt'
        masked['ISR_TEST_ALIAS_MARKER'] = str(marker)
        masked[path_key] = str(alias_dir) + os.pathsep + env[path_key]
        auto = passed(run(invocation + ['-PreflightOnly'], 'fallback-after-alias', masked))
        check(marker.exists(), 'Fallback case did not actually probe the unavailable alias.')
        check(Path(json.loads(auto.stdout)['PythonPath']).parent != alias_dir, 'The unavailable alias was accepted as Python.')
        probe_script = temp / 'probe.ps1'
        probe_script.write_text("param([string]$Module)\n$ErrorActionPreference='Stop'\n. $Module\nResolve-TestPython | ConvertTo-Json\n", encoding='utf-8')
        missing_env = dict(masked)
        missing_env[path_key] = str(alias_dir)
        missing = run([shell, '-NoProfile', '-NonInteractive', '-File', str(probe_script),
                       '-Module', str(root / 'tools/Resolve-TestPython.ps1')], 'alias-only-missing', missing_env)
        check(missing.returncode != 0 and 'PYTHON_PREREQUISITE_MISSING' in missing.stdout, 'An alias-only environment must fail discovery.')
        marker.unlink()
        # Both reported tests must execute the real tool despite a broken python
        # command first on PATH; the wrapper pins sys.executable for testhost.
        suite = passed(run(invocation + ['-PythonPath', str(real)], 'l05d-original-tests', masked))
        check(not marker.exists(), 'The L05-D test subprocess ignored the pinned interpreter.')
        results = list(ET.parse(reports / 'development.trx').iterfind('.//{*}UnitTestResult'))
        check({r.get('testName') for r in results} == REQUIRED and len(results) == 4, 'Not all four original L05-D tests were executed.')
        check(all(r.get('outcome') == 'Passed' for r in results), suite.stdout)
    check(before_state == hashlib.sha256((root / 'registry/state.json').read_bytes()).hexdigest(), 'Active state was modified.')
    check(before_settings == (root / 'tests/Development.runsettings').read_bytes(), 'Development filter was changed.')
    summary = dict(status='PASS', scope='PYTHON_PREREQUISITES_AND_ORIGINAL_L05D_TESTS',
                   configuration=args.configuration, original_tests=4, failed=0, skipped=0,
                   alias_rejected=True, alias_only_rejected=True, explicit_bad_path_rejected=True,
                   non_python_rejected=True, explicit_path_with_spaces_and_accent=True,
                   pinned_interpreter_in_testhost=True, original_assertions_preserved=True,
                   active_state_preserved=True, full_solution_build='NOT_RUN', native_game='NOT_RUN',
                   certified_evidence_campaign='NOT_RUN')
    (reports / 'result.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
