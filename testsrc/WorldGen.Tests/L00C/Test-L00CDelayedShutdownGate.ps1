[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
$gatePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\DelayedShutdownGate.cs'
$oraclePath = Join-Path $PSScriptRoot 'L00CDelayedShutdownGateOracle.cs'
$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$profilePath = Join-Path $PSScriptRoot 'Set-L00CLabProfile.ps1'
$f5ProfilePath = Join-Path $PSScriptRoot 'Set-L00CF5AuthenticatedProfile.ps1'
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach ($path in @($assemblyPath, $gatePath, $oraclePath, $sourcePath, $profilePath, $f5ProfilePath, $csc)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Delayed shutdown gate dependency is missing: $path" }
}

$out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-delayed-shutdown-gate-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $csc /nologo /target:library "/define:DEBUG,L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $gatePath $oraclePath
    if ($LASTEXITCODE -ne 0) { throw 'Production delayed-shutdown executable oracle compilation failed.' }
    $oracleAssembly = [Reflection.Assembly]::LoadFrom($out)
    $run = $oracleAssembly.GetType('ISRWorldGen.L00C.Laboratory.L00CDelayedShutdownGateOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $run -or $run.Invoke($null, @()) -ne 0) { throw 'Production delayed-shutdown executable oracle failed.' }
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }

foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.DelayedShutdownGate', $true)
$configType = $assembly.GetType('ISRWorldGen.WorldgenProbe.L00CProbeConfig', $true)
$schedule = $gateType.GetMethod('Schedule')
$cancel = $gateType.GetMethod('CancelAndUnregister')
$validateDelay = $gateType.GetMethod('ValidateDelayMilliseconds')
$validateActiveDelay = $gateType.GetMethod('ValidateActiveDelayMilliseconds')
if ($null -eq $schedule -or $null -eq $cancel -or $null -eq $validateDelay -or $null -eq $validateActiveDelay -or
    $null -ne $gateType.GetMethod('Begin') -or $null -ne $gateType.GetMethod('Attach') -or $null -ne $gateType.GetMethod('Cancel')) {
    throw 'Delayed shutdown gate must expose only the transactional scheduling/cancellation surface.'
}
$bindLaboratoryConfig = $configType.GetMethod('BindLaboratoryShutdownConfiguration', [Reflection.BindingFlags]'Static,NonPublic')
if ($null -eq $bindLaboratoryConfig) { throw 'Profile-to-probe shutdown binding is absent.' }

function Assert-Refused([scriptblock]$Operation, [string]$Label) {
    try { & $Operation } catch { return }
    throw "Expected refusal: $Label"
}
function Bind-LaboratoryConfig([string]$Lab, [string]$AutoShutdown, [string]$Delay) {
    $candidate = [Activator]::CreateInstance($configType)
    $configType.GetProperty('AutoShutdownDelayMilliseconds').SetValue($candidate, 0)
    return $bindLaboratoryConfig.Invoke($null, @($candidate, $Lab, $AutoShutdown, $Delay))
}
foreach ($valid in @(0, 50, 60000)) { if ([int]$validateDelay.Invoke($null, @($valid)) -ne $valid) { throw "Valid delay $valid drifted." } }
foreach ($invalid in @(1, 49, 60001)) { Assert-Refused { [void]$validateDelay.Invoke($null, @($invalid)) } "delay $invalid" }
foreach ($valid in @(10000, 15000, 60000)) { if ([int]$validateActiveDelay.Invoke($null, @($valid)) -ne $valid) { throw "Valid active delay $valid drifted." } }
foreach ($invalid in @(0, 50, 9999, 60001)) { Assert-Refused { [void]$validateActiveDelay.Invoke($null, @($invalid)) } "active delay $invalid" }

$bound = Bind-LaboratoryConfig '1' '1' '15000'
if (-not [bool]$configType.GetProperty('AutoShutdown').GetValue($bound) -or [int]$configType.GetProperty('AutoShutdownDelayMilliseconds').GetValue($bound) -ne 15000) {
    throw 'The evaluated F5 profile did not bind the effective active shutdown configuration.'
}
foreach ($case in @(
    @('1', '', '15000'), @('1', '0', '15000'), @('1', '1', ''), @('1', '1', '0'),
    @('1', '1', '9999'), @('1', '1', '60001'), @('1', '1', '015000'), @('1', '1', '15000ms'))) {
    Assert-Refused { [void](Bind-LaboratoryConfig $case[0] $case[1] $case[2]) } 'invalid profile binding'
}

$source = Get-Content -LiteralPath $sourcePath -Raw
$gateSource = Get-Content -LiteralPath $gatePath -Raw
foreach ($fragment in @(
    'delayedShutdown.Schedule(delayMilliseconds', 'serverApi.Event.RegisterCallback(', 'serverApi.Event.UnregisterCallback',
    'delayedShutdown.CancelAndUnregister()', 'RequestActiveShutdown(runId, "persisted-reopen-stable")',
    'RequestActiveShutdown(runId, "fixture-stable")', 'RequestInactiveWitnessShutdown(runId)')) {
    if (-not $source.Contains($fragment)) { throw "Delayed shutdown production wiring is missing: $fragment" }
}
foreach ($fragment in @('RegisterReturned', 'CallbackArrived', 'UnregisterInProgress', 'RollbackAfterUnregisterFailure',
    'registration.Unregister(registration.ListenerId)', 'poisoned = true')) {
    if (-not $gateSource.Contains($fragment)) { throw "Delayed shutdown transaction state is missing: $fragment" }
}
if ($source.Contains('delayedShutdown.Begin(') -or $source.Contains('delayedShutdown.Attach(') -or
    $source.Contains('long delayedListenerId = delayedShutdown.Cancel()') -or $source.Contains('serverApi.Event.UnregisterCallback(listenerId)')) {
    throw 'Caller still owns a split delayed-listener registration or compensation path.'
}
$lifecycleStart = $source.IndexOf('private void CompleteLifecycleOnly(', [StringComparison]::Ordinal)
$lifecycleEnd = $source.IndexOf('private void BeginWorldTransition()', $lifecycleStart, [StringComparison]::Ordinal)
$lifecycleMethod = $source.Substring($lifecycleStart, $lifecycleEnd - $lifecycleStart)
if ($lifecycleMethod.Contains('RequestActiveShutdown') -or $lifecycleMethod.Contains('delayedShutdown')) {
    throw 'T00-06 lifecycle-only flow must use native Save & Quit, not the delayed server shutdown gate.'
}
$profileSource = Get-Content -LiteralPath $profilePath -Raw
$f5ProfileSource = Get-Content -LiteralPath $f5ProfilePath -Raw
if (-not $profileSource.Contains('AutoShutdownDelayMilliseconds = $AutoShutdownDelayMilliseconds') -or
    -not $profileSource.Contains('[int]$AutoShutdownDelayMilliseconds = 15000')) { throw 'Isolated lab profile delay contract drifted.' }
foreach ($profileEnvironment in @("-NotePropertyName ISR_L00C_AUTOSHUTDOWN -NotePropertyValue '1'", "-NotePropertyName ISR_L00C_AUTOSHUTDOWN_DELAY_MS -NotePropertyValue '15000'")) {
    if (-not $f5ProfileSource.Contains($profileEnvironment)) { throw "F5 profile binding is missing: $profileEnvironment" }
}

[ordered]@{
    TestId='L00-C-DELAYED-SHUTDOWN-GATE'; Status='PASS'
    ExecutedCases='async;sync-before-attach;registrar-throw;invalid-id;close-between-register-attach;sync-close;unregister-fail-exact-retry;concurrent-cancel-no-overlap;unregister-fail-twice-deferred-exact-retry;cancel-late;caller-failure;duplicate'
    Compensation='gate-owned exact id; bounded immediate retry then retained exact-id retry; rollback suppresses fire and poisons reopen'
    ValidBounds='0|50|60000'; InvalidBounds='1|49|60001'; ValidActiveBounds='10000|15000|60000'; InvalidActiveBounds='0|50|9999|60001'
    ProfileToProbeContract='ISR_L00C_AUTOSHUTDOWN=1;ISR_L00C_AUTOSHUTDOWN_DELAY_MS=15000'; ProductionWiringInspected=$true
} | ConvertTo-Json -Depth 4
