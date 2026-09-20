"""Execute the original L03B protocol tests, not the certified evidence campaign.

Requires an installed SDK and PowerShell 7. Operates only on a disposable checkout
when used in CI. No game assemblies, VS automation, account or save data are used.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

CAMPAIGN = 'ISRWorldGen.Tests.L03B.EvidenceArtifactTests.T0305AndT0306PublishAtomicBlindReviewEvidence'
REQUIRED = {
    'ExternalReviewerAndControllerCliEnforceAtomicReceiptBeforeReveal',
    'FailurePublisherReconcilesPostKeyTimeoutWithoutLeakingPartialOrSecretFiles',
    'ReviewerCliRejectsIncompleteRequestAndCoercibleButWrongAnswerTypes',
}


def run(args: list[str], root: Path, env: dict[str, str], log: Path) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(args, cwd=root, env=env, text=True, encoding='utf-8',
                            errors='replace', stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            timeout=300, check=False)
    log.write_text(result.stdout, encoding='utf-8')
    return result


def check(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def trx_results(path: Path) -> list[ET.Element]:
    return list(ET.parse(path).iterfind('.//{*}UnitTestResult'))


def fingerprint(directory: Path) -> dict[str, str]:
    if not directory.exists():
        return {}
    return {str(p.relative_to(directory)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in directory.rglob('*') if p.is_file()}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--configuration', choices=('Debug', 'Release'), required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    pwsh = shutil.which('pwsh')
    check(pwsh is not None, 'PowerShell 7 must be installed: missing prerequisite is not a passing test.')
    shell = str(Path(pwsh).resolve())
    reports = root / 'testsrc/WorldGen.L03B.Protocol.Tests/TestResults'
    reports.mkdir(parents=True, exist_ok=True)
    settings = root / 'tests/Development.runsettings'
    config = ET.parse(settings).getroot()
    check(config.findtext('RunConfiguration/TestCaseFilter') == f'FullyQualifiedName!={CAMPAIGN}',
          'The development profile must exclude only the dedicated publisher, not protocol regressions.')
    check(config.findtext('RunConfiguration/TreatNoTestsAsError') == 'true', 'Zero-test runs must fail.')
    env = dict(os.environ)
    # Never fabricate provenance and never inherit a configured evidence campaign.
    for key in list(env):
        if key.startswith('ISR_L03B_EVIDENCE_'):
            del env[key]
    invocation = [shell, '-NoLogo', '-NoProfile', '-NonInteractive', '-File',
                  str(root / 'tools/Invoke-DevelopmentTests.ps1'),
                  '-Configuration', args.configuration, '-Scope', 'L03BProtocol']
    invalid = run(invocation + ['-PreflightOnly', '-PowerShellPath', str(reports / 'missing/pwsh.exe')],
                  root, env, reports / 'invalid-explicit-shell.log')
    check(invalid.returncode != 0 and 'No fallback' in invalid.stdout,
          'Invalid explicit shell must fail before tests, not silently fall back.')
    ready = run(invocation + ['-PreflightOnly', '-PowerShellPath', shell], root, env, reports / 'preflight.log')
    check(ready.returncode == 0, ready.stdout)
    preflight = json.loads(ready.stdout)
    check(preflight['Tests'] == 'NOT_RUN' and preflight['Status'] == 'READY_FOR_TEST_INVOCATION',
          'Readiness must not claim that tests ran.')
    bootstrap = 'NOT_APPLICABLE'
    if os.name == 'nt':
        ps51 = shutil.which('powershell.exe')
        check(ps51 is not None, 'Windows bootstrap test requires Windows PowerShell.')
        result = run([ps51, '-NoProfile', '-NonInteractive', '-File',
                      str(root / 'tools/Invoke-DevelopmentTests.ps1'), '-PreflightOnly',
                      '-Scope', 'L03BProtocol', '-PowerShellPath', shell],
                     root, env, reports / 'powershell51-bootstrap.log')
        check(result.returncode == 0, result.stdout)
        check(json.loads(result.stdout)['PowerShellVersion'].split('.')[0].isdigit(), 'Invalid PowerShell version.')
        bootstrap = 'PASS'
    # Reproduce a VS process whose PATH has not received the PowerShell install
    # directory, but which can use the explicit installed executable. The wrapper
    # must make pwsh visible to dotnet/testhost and to the timeout grandchild.
    path_key = next(key for key in env if key.upper() == 'PATH')
    shell_dir = os.path.normcase(os.path.dirname(shell))
    env[path_key] = os.pathsep.join(p for p in env[path_key].split(os.pathsep)
                                  if os.path.normcase(os.path.abspath(p.strip('"'))) != shell_dir)
    suite = run(invocation + ['-PowerShellPath', shell], root, env, reports / 'protocol.log')
    check(suite.returncode == 0, suite.stdout)
    results = trx_results(reports / 'development.trx')
    check(bool(results), 'No protocol tests were executed.')
    check(all(r.get('outcome') == 'Passed' for r in results), 'A protocol test failed or was skipped.')
    names = {r.get('testName') for r in results}
    check(REQUIRED <= names, f'Missing original tests: {REQUIRED - names}')
    check('T0305AndT0306PublishAtomicBlindReviewEvidence' not in names, 'Campaign publisher ran in development.')
    # Prove strict provenance remains untouched. This is an expected rejection,
    # never a successful evidence campaign or blind human review.
    local_evidence = root / '.local/L03B'
    before = fingerprint(local_evidence)
    project = root / 'testsrc/WorldGen.L03B.Protocol.Tests/WorldGen.L03B.Protocol.Tests.csproj'
    rejected = run(['dotnet', 'test', str(project), '--no-build', '-c', args.configuration,
                    '--filter', f'FullyQualifiedName={CAMPAIGN}', '--logger',
                    'trx;LogFileName=unconfigured-campaign.trx', '--results-directory', str(reports)],
                   root, env, reports / 'unconfigured-campaign.log')
    check(rejected.returncode != 0 and 'Evidence requires ISR_L03B_EVIDENCE_COMMIT.' in rejected.stdout,
          'Unconfigured evidence publication must still fail at provenance, not compilation or another assertion.')
    rejected_results = trx_results(reports / 'unconfigured-campaign.trx')
    check(len(rejected_results) == 1 and rejected_results[0].get('outcome') == 'Failed',
          'Expected exactly the unconfigured campaign refusal.')
    check(before == fingerprint(local_evidence), 'Unconfigured publisher changed evidence files.')
    result = dict(status='PASS', scope='ORIGINAL_L03B_PROTOCOL_TESTS_AND_LAUNCH_PREREQUISITES',
                  configuration=args.configuration, protocol_tests=len(results),
                  reported_tests=sorted(REQUIRED), failed=0, skipped=0,
                  invalid_explicit_shell='REJECTED', powershell51_bootstrap=bootstrap,
                  unconfigured_campaign='EXPECTED_PROVENANCE_REFUSAL_NO_PUBLICATION',
                  certified_evidence_campaign='NOT_RUN', native_game='NOT_RUN',
                  full_solution_build='NOT_RUN', visual_studio='NOT_RUN')
    (reports / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
