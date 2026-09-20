# L03-B — explicit development invocation and missing PowerShell prerequisite

Base: `5bd3458a6c4c3b410e6364566c78545dfaeb97aa`.
User instruction: continue on `main`. No change to execution-state registries.

## Evidence and diagnosis

The four submitted testlogs show an absent `ISR_L03B_EVIDENCE_COMMIT` and three
process creation failures for `pwsh`. They do not establish an algorithm failure,
a failed atomicity assertion, or whether PowerShell is uninstalled versus absent
from the Visual Studio process PATH. The dedicated runner verifies detached clean
HEAD/tree, DLL hashes and other provenance. Preserve that contract.

## Changes and scope

- Explicit `tests/Development.runsettings`: exclude exactly the dedicated
  campaign publisher from a *selected development run*, never report it PASS.
  No automatic project/global selection and no changes to the original tests.
- PowerShell 5.1-compatible bootstrap selects installed PowerShell 7 Core,
  supports an absolute explicit executable, fails closed when unavailable or
  invalid, and fixes PATH only for the invocation/children (restored in finally).
  No automatic install, machine/user environment write or fallback to 5.1.
- Portable L03B protocol project links original test sources and real Core;
  same existing MSTest version, no new game dependency or solution rewrite.
- Windows/Linux Debug/Release CI executes the original protocol suite, verifies
  all three named failures are actually run without skips, tests the bootstrap,
  and checks that the original unconfigured evidence publisher still refuses
  provenance before publishing anything.

## Transition boundaries

Resolution and preflight precede test launch. Invalid explicit paths refuse before
running tests. The bootstrap child is synchronous; it receives only nonsecret
configuration/path/scope. PATH/location changes are process-local and restored.
Development failures propagate their exit code without retry or promotion.
Protocol fixtures own temporary directories and synthetic receipts as before;
original cleanup/assertions remain unchanged. CI is on disposable hosted runners,
with read-only repository permissions and uploads only test logs/TRX/result.json,
not `.local` or a real blind-review bundle. Production saves, accounts, F5, MCP,
launch profiles, state, contracts and publication gates are not touched.

## Validation limitations

Local XML and Python syntax checks only; local .NET, pwsh, Visual Studio and game
are unavailable. Executed CI results must be read from the commit-specific run,
not inferred from these definitions. Full solution/game tests and the certified
campaign remain NOT_RUN. No independent review or completed lot is claimed.
