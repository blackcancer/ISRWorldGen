[CmdletBinding()]
param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$barrier = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleShutdownBarrier.cs'
$oracle = Join-Path $PSScriptRoot 'L00CLifecycleShutdownOrderingOracle.cs'
$probe = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$bootstrap = Join-Path $PSScriptRoot 'L00CFixtureBootstrap.cs'
$hostPath = Join-Path $PSScriptRoot 'L00CMenuActionLaboratoryHost.cs'
$driver = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
$controller = Join-Path $PSScriptRoot 'L00CProcessCampaignController.cs'
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach ($path in @($barrier, $oracle, $probe, $bootstrap, $hostPath, $driver, $controller, $csc)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C lifecycle ordering oracle is missing $path" }
}

$probeText = Get-Content -LiteralPath $probe -Raw
$bootstrapText = Get-Content -LiteralPath $bootstrap -Raw
$hostText = Get-Content -LiteralPath $hostPath -Raw
$driverText = Get-Content -LiteralPath $driver -Raw
$controllerText = Get-Content -LiteralPath $controller -Raw
$barrierText = Get-Content -LiteralPath $barrier -Raw
foreach ($required in @(
    'L00CLifecycleShutdownIdentity.Create(runId, instanceId, saveGame.SavegameIdentifier, checked(++lifecycleAttestationSequence))',
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
foreach ($required in @('TryFindFinalizedWorldSession', 'PrepareReturn(', 'ClientSavegameGuid', 'StartServerSavePath',
    '() => L00CLifecycleShutdownBarrier.BeginNativeReturn(reservation)', 'AbortBeforeNativeReturn(reservation)')) {
    if (-not $bootstrapText.Contains($required) -and -not $hostText.Contains($required)) { throw "Client lifecycle return lost required contract: $required" }
}
foreach ($required in @('active.bootstrap?.SignalLevelFinalize(fixtureSequence);', 'active.host?.SignalLevelFinalize(fixtureSequence);')) {
    if (-not $controllerText.Contains($required)) { throw "LevelFinalize routing lost: $required" }
}
foreach ($required in @('ReadClientSavegameGuid(mainType, main)', 'SaveFileLocation', 'expectedIsNew', 'Guid.TryParseExact(clientSavegameGuid, "D"')) {
    if (-not $driverText.Contains($required)) { throw "Finalized client attestation lost: $required" }
}
if ($barrierText -match 'SavegameGuid\s*,\s*(expected|observed).*Path' -or $barrierText.Contains('SavegameGuid == SavePath')) {
    throw 'Savegame GUID and .vcdbs path must remain distinct attestation axes.'
}

$out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-lifecycle-shutdown-ordering-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $csc /nologo /target:library "/define:DEBUG,L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $barrier $oracle
    if ($LASTEXITCODE -ne 0) { throw 'L00-C lifecycle shutdown ordering oracle compilation failed.' }
    $assembly = [Reflection.Assembly]::LoadFrom($out)
    $method = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CLifecycleShutdownOrderingOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method -or $method.Invoke($null, @()) -ne 0) { throw 'L00-C lifecycle shutdown ordering model failed.' }
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }

[ordered]@{
    TestId='L00-C-LIFECYCLE-SHUTDOWN-ORDERING'; Status='PASS'; ControlledPrimaryReopenCycles=5; FinalizedSessions=8
    Identity='server/client canonical GUID plus exact run/instance/server sequence'; SavePath='separate canonical StartServerArgs.SaveFileLocation axis'
    Refusals='malformed/wrong GUID;wrong role/path/run/instance/server-sequence/fixture-sequence;premature/duplicate/stale/late return;duplicate/stale owner'
    NativeBoundary='all guards then BeginNativeReturn immediately before SendLeave;no post-Dispose server callback'
    Scope='Executable deterministic production-state oracle and source wiring inspection; no Vintage Story process, F5, fixture coordinate, teleportation, or spatial execution.'
} | ConvertTo-Json -Depth 4
