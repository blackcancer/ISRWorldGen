[CmdletBinding()]
param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$barrier = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleShutdownBarrier.cs'
$oracle = Join-Path $PSScriptRoot 'L00CLifecycleShutdownOrderingOracle.cs'
$evidenceOracle = Join-Path $PSScriptRoot 'L00CLifecycleCommitEvidenceOracle.cs'
$probe = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach ($path in @($barrier, $oracle, $evidenceOracle, $probe, $csc)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C lifecycle ordering oracle is missing $path" }
}

$probeText = Get-Content -LiteralPath $probe -Raw
$barrierText = Get-Content -LiteralPath $barrier -Raw
foreach ($required in @(
    'L00CLifecycleShutdownIdentity.Create(runId, instanceId, saveGame.SavegameIdentifier)',
    'lifecycleShutdownLease = L00CLifecycleShutdownBarrier.Open(lifecycleShutdownIdentity);',
    'L00CLifecycleShutdownBarrier.Arm(lifecycleShutdownLease',
    'CloseLifecycleShutdownBarrier();')) {
    if (-not $probeText.Contains($required)) { throw "Lifecycle shutdown server handoff lost required contract: $required" }
}
$completeStart = $probeText.IndexOf('private void CompleteLifecycleOnly(', [StringComparison]::Ordinal)
$completeEnd = $probeText.IndexOf('private void BeginWorldTransition()', $completeStart, [StringComparison]::Ordinal)
$completeMethod = $probeText.Substring($completeStart, $completeEnd - $completeStart)
if ($completeMethod.Contains('RequestActiveShutdown') -or $completeMethod.Contains('DelayedShutdownGate') -or $completeMethod.Contains('TryConsumeApprovedShutdown')) {
    throw 'Lifecycle-only completion must not schedule a competing server shutdown after native Save & Quit.'
}
foreach ($required in @('class L00CLifecycleSessionObservation', 'RunId { get; }', 'Iteration { get; }', 'SessionOrdinal { get; }',
    'CanonicalSavePath { get; }', 'CanonicalSavegameGuid { get; }', 'IsNew { get; }', 'EventKind { get; }',
    'ConfirmSaveCommitted', 'NativeActionCompleted', 'ServerStopped', 'MainMenuReady', 'TargetExclusivelyOpenable',
    'ReferenceEquals(ready,observation)', 'run=unbound iteration=0 session=0 state=')) {
    if (-not $barrierText.Contains($required)) { throw "Lifecycle immutable observation/proof contract lost: $required" }
}
foreach ($forbidden in @('activated-primary', 'activated-secondary', 'fixtureSequence', '1..8')) {
    if ($barrierText.Contains($forbidden)) { throw "Lifecycle barrier retained obsolete two-role/1..8 contract: $forbidden" }
}

$out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-lifecycle-shutdown-ordering-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $csc /nologo /target:library "/define:DEBUG,L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $barrier $oracle $evidenceOracle
    if ($LASTEXITCODE -ne 0) { throw 'L00-C lifecycle shutdown ordering oracle compilation failed.' }
    $assembly = [Reflection.Assembly]::LoadFrom($out)
    $method = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CLifecycleShutdownOrderingOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method -or $method.Invoke($null, @()) -ne 0) { throw 'L00-C lifecycle shutdown ordering model failed.' }
    $evidenceMethod = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CLifecycleCommitEvidenceOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $evidenceMethod) { throw 'L00-C commit evidence oracle entry point is absent.' }
    $evidenceCases = [int]$evidenceMethod.Invoke($null, @())
    if ($evidenceCases -ne 21) { throw 'L00-C commit evidence oracle did not execute its full case matrix.' }
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }

[ordered]@{
    TestId='L00-C-LIFECYCLE-SHUTDOWN-ORDERING'; Status='PASS'; Iterations=5; FinalizedSessions=15; DedicatedSaves=10
    CommitEvidenceCases=$evidenceCases
    Identity='immutable run/iteration/session/path/GUID/IsNew/eventKind plus process-local server lease'
    Refusals='malformed GUID;wrong path/ordinal;premature/duplicate/stale/reattributed/closing callback;next open before prior SaveCommitted;partial native close proof;missing/corrupt marker or registration evidence;concurrent/replayed commit'
    NativeBoundary='SaveCommitted only after native action completion, server stopped, main menu, exclusive target open, released server lease, internal marker and complete registration release proof'
    Scope='Executable deterministic production-state oracle and source wiring inspection; no Vintage Story process, F5, fixture coordinate, teleportation, or spatial execution.'
} | ConvertTo-Json -Depth 4
