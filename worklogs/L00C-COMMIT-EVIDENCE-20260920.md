# L00-C — Validate evidence at the SaveCommitted boundary

Baseline: `fa506251d9ee78e95ca83c728279aa2eb7d73306`.
Status: candidate for independent review; not a closure of L00-C or T00-06.

## Defect and correction

The native host polls `TryGetCompletedEvidence` before confirmation, but
`L00CLifecycleShutdownState.ConfirmSaveCommitted` could be called directly
without the internal marker or registration-release evidence. The historical
ordering oracle did exactly that in its otherwise successful fifteen sessions.
A subsequent caller could therefore clear the pending return and open the next
session without the barrier enforcing the complete contract itself.

Confirmation now requires `RequireCompletedEvidence` while holding the same
reentrant monitor and before changing `Committed` or clearing `pendingReturn`.
This preserves the existing API and native-return conditions. No game API calls,
new libraries, production symbols, world data or plan contracts are introduced.
The historical oracle now supplies complete synthetic evidence on its happy path;
its existing malformed-identity, ordering and native-proof refusals remain.

## Regression suite

`L00CLifecycleCommitEvidenceOracle` adds 21 scenarios: absent registrations,
absent marker, both absent, eight missing registration flags, four non-zero
counters in both signs, direct confirmation without prior polling, and two
concurrent confirmations. Rejections must preserve the pending reservation and
must not publish SaveCommitted or allow another session. Valid late registration
publication can complete the same reservation; replay cannot commit it again.

The existing Windows/Roslyn wrapper invokes the new oracle as well as its old
checks. A separate `net10.0` console project links the actual source and both
oracles, without referencing the game or changing the production solution.

```powershell
dotnet run --project testsrc/WorldGen.LifecycleEvidence.Tests/WorldGen.LifecycleEvidence.Tests.csproj --configuration Release
```

The GitHub workflow builds the pinned old source with the new regression and
requires its exact false-acceptance failure, then builds and runs the candidate.
The matrix covers Windows/Linux and Debug/Release. It has read-only repository
permissions, uses hosted runners and never starts Vintage Story or Visual Studio.
Detailed executed results belong to its job logs/artifacts, not to this proposal.

## Safety and limits

All fixtures are process-local C# state objects. Save paths are canonical strings
only; no save files are opened, created, deleted or modified. Fault injection
changes only internal objects owned by each synthetic fixture. No account,
credential, MCP operation, launch profile, worldconfig or runtime gate is changed.

The local authoring environment has neither .NET nor the user's Visual Studio
MCP. No local C# execution is claimed. CI results must be checked independently.
The VS-specific wrapper, whole-solution native builds and actual client campaign
remain NOT_RUN for this change. Existing state/worklogs are not overwritten;
independent human/agent review and native requalification are still required.
