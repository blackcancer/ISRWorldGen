[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$profilePath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L00C\Set-L00CLabProfile.ps1'
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$source = Get-Content -LiteralPath $sourcePath -Raw
$profileSource = Get-Content -LiteralPath $profilePath -Raw
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.DelayedShutdownGate', $false)
if ($null -eq $gateType) { throw 'The Debug assembly does not expose the production delayed-shutdown gate.' }
$reservationType = $assembly.GetType('ISRWorldGen.WorldgenProbe.DelayedShutdownReservation', $false)
if ($null -eq $reservationType) { throw 'The Debug assembly does not expose the production delayed-shutdown reservation.' }

$open = $gateType.GetMethod('Open')
$begin = $gateType.GetMethod('Begin')
$attach = $gateType.GetMethod('Attach')
$complete = $gateType.GetMethod('Complete')
$reject = $gateType.GetMethod('Reject')
$cancel = $gateType.GetMethod('Cancel')
$validateDelay = $gateType.GetMethod('ValidateDelayMilliseconds')
foreach ($method in @($open, $begin, $attach, $complete, $reject, $cancel, $validateDelay)) {
    if ($null -eq $method) { throw 'The production delayed-shutdown gate contract is incomplete.' }
}

function New-Gate { return [Activator]::CreateInstance($gateType) }
function Open-Gate($Gate) { [void]$open.Invoke($Gate, @()) }
function Begin-Reservation($Gate, [Action]$Callback) { return $begin.Invoke($Gate, @($Callback)) }

$syncGate = New-Gate
Open-Gate $syncGate
$script:syncFires = 0
$syncReservation = Begin-Reservation $syncGate ([Action]{ $script:syncFires++ })
[void]$complete.Invoke($syncGate, @($syncReservation))
$syncAttached = [bool]$attach.Invoke($syncGate, @($syncReservation, [long]101))
[void]$complete.Invoke($syncGate, @($syncReservation))
if (-not $syncAttached -or $script:syncFires -ne 1) { throw 'Synchronous delayed callback did not fire exactly once after listener attachment.' }

$asyncGate = New-Gate
Open-Gate $asyncGate
$script:asyncFires = 0
$asyncReservation = Begin-Reservation $asyncGate ([Action]{ $script:asyncFires++ })
$asyncAttached = [bool]$attach.Invoke($asyncGate, @($asyncReservation, [long]102))
[void]$complete.Invoke($asyncGate, @($asyncReservation))
[void]$complete.Invoke($asyncGate, @($asyncReservation))
if (-not $asyncAttached -or $script:asyncFires -ne 1) { throw 'Asynchronous delayed callback did not fire exactly once.' }

$transitionGate = New-Gate
Open-Gate $transitionGate
$script:transitionFires = 0
$transitionReservation = Begin-Reservation $transitionGate ([Action]{ $script:transitionFires++ })
[void]$attach.Invoke($transitionGate, @($transitionReservation, [long]103))
$transitionListener = [long]$cancel.Invoke($transitionGate, @())
[void]$complete.Invoke($transitionGate, @($transitionReservation))
if ($transitionListener -ne 103 -or $script:transitionFires -ne 0) { throw 'World transition did not cancel and expose the registered delayed listener exactly.' }

$errorGate = New-Gate
Open-Gate $errorGate
$script:errorFires = 0
$errorReservation = Begin-Reservation $errorGate ([Action]{ $script:errorFires++ })
[void]$reject.Invoke($errorGate, @($errorReservation))
$errorAttached = [bool]$attach.Invoke($errorGate, @($errorReservation, [long]104))
[void]$complete.Invoke($errorGate, @($errorReservation))
if ($errorAttached -or $script:errorFires -ne 0) { throw 'Rejected delayed registration remained attachable or fireable.' }

$disposeGate = New-Gate
Open-Gate $disposeGate
$script:disposeFires = 0
$disposeReservation = Begin-Reservation $disposeGate ([Action]{ $script:disposeFires++ })
[void]$attach.Invoke($disposeGate, @($disposeReservation, [long]105))
$disposeListener = [long]$cancel.Invoke($disposeGate, @())
[void]$complete.Invoke($disposeGate, @($disposeReservation))
if ($disposeListener -ne 105 -or $script:disposeFires -ne 0) { throw 'Dispose-style cancellation did not neutralize its delayed callback.' }

foreach ($valid in @(0, 50, 60000)) {
    if ([int]$validateDelay.Invoke($null, @($valid)) -ne $valid) { throw "Valid delay $valid drifted." }
}
foreach ($invalid in @(1, 49, 60001)) {
    try { [void]$validateDelay.Invoke($null, @($invalid)) } catch { continue }
    throw "Invalid delay $invalid was accepted."
}

foreach ($fragment in @(
    'AutoShutdownDelayMilliseconds',
    'RequestInactiveWitnessShutdown(runId)',
    'serverApi.Event.RegisterCallback(',
    'serverApi.Event.UnregisterCallback(listenerId)',
    'L00C_DELAYED_SHUTDOWN_ARMED',
    'L00C_DELAYED_SHUTDOWN_FIRED',
    'CancelDelayedShutdown("world-initialize")',
    'long delayedListenerId = delayedShutdown.Cancel()',
    'CancelDelayedShutdown("dispose")'
)) {
    if (-not $source.Contains($fragment)) { throw "Delayed shutdown production wiring is missing: $fragment" }
}
$disposeStart = $source.IndexOf('private void DisposeCore()', [StringComparison]::Ordinal)
$disposeEnd = $source.IndexOf('private sealed record ReplacementSpec(', $disposeStart, [StringComparison]::Ordinal)
$disposeMethod = $source.Substring($disposeStart, $disposeEnd - $disposeStart)
$disposeDelayed = $disposeMethod.IndexOf('CancelDelayedShutdown("dispose")', [StringComparison]::Ordinal)
$disposeDelayedCatch = $disposeMethod.IndexOf('stage=delayed-shutdown', $disposeDelayed, [StringComparison]::Ordinal)
$disposeTransient = $disposeMethod.IndexOf('ResetTransientLoadCallbacks("dispose")', $disposeDelayedCatch, [StringComparison]::Ordinal)
$disposeTransientCatch = $disposeMethod.IndexOf('stage=transient-callbacks', $disposeTransient, [StringComparison]::Ordinal)
if ($disposeDelayed -lt 0 -or $disposeDelayedCatch -le $disposeDelayed -or
    $disposeTransient -le $disposeDelayedCatch -or $disposeTransientCatch -le $disposeTransient) {
    throw 'Dispose does not isolate delayed-listener cancellation from transient callback reset.'
}
if (-not $profileSource.Contains('AutoShutdownDelayMilliseconds = $AutoShutdownDelayMilliseconds')) {
    throw 'The isolated lab profile cannot select the bounded delayed shutdown.'
}
$witnessStart = $source.IndexOf('private void CompleteInactiveWitness(', [StringComparison]::Ordinal)
$witnessEnd = $source.IndexOf('private void RequestInactiveWitnessShutdown(', $witnessStart, [StringComparison]::Ordinal)
$witnessMethod = $source.Substring($witnessStart, $witnessEnd - $witnessStart)
if ($witnessMethod.IndexOf('L00C_WITNESS_NO_REQUEST', [StringComparison]::Ordinal) -lt 0 -or
    $witnessMethod.IndexOf('RequestInactiveWitnessShutdown(runId)', [StringComparison]::Ordinal) -le $witnessMethod.IndexOf('L00C_WITNESS_NO_REQUEST', [StringComparison]::Ordinal)) {
    throw 'The inactive witness must attest zero action before arming delayed shutdown.'
}
$requestStart = $witnessEnd
$requestEnd = $source.IndexOf('private void FireDelayedShutdown(', $requestStart, [StringComparison]::Ordinal)
$requestMethod = $source.Substring($requestStart, $requestEnd - $requestStart)
if ($requestMethod -match 'LoadChunk|GetChunk|FIXTURE|ApplyTargetedReplacement|ChunkColumn') {
    throw 'Delayed witness shutdown contains a generation or chunk-access path.'
}
$completeStart = $source.IndexOf('private void CompleteInactiveWitness(', [StringComparison]::Ordinal)
$completeEnd = $source.IndexOf('private void RequestInactiveWitnessShutdown(', $completeStart, [StringComparison]::Ordinal)
$completeMethod = $source.Substring($completeStart, $completeEnd - $completeStart)
if ($completeMethod -notmatch 'catch \(Exception exception\)[\s\S]+HandleAsynchronousFailure\(runId, "inactive-witness-error", exception\)') {
    throw 'Inactive witness registration errors do not enter the bounded asynchronous failure path.'
}
$fireStart = $source.IndexOf('private void FireDelayedShutdown(', [StringComparison]::Ordinal)
$fireEnd = $source.IndexOf('private void CancelDelayedShutdown(', $fireStart, [StringComparison]::Ordinal)
$fireMethod = $source.Substring($fireStart, $fireEnd - $fireStart)
if ($fireMethod -notmatch 'catch \(Exception exception\)[\s\S]+HandleAsynchronousFailure\(runId, "delayed-shutdown-error", exception\)') {
    throw 'Delayed callback errors do not enter the bounded asynchronous failure path.'
}
$failureStart = $source.IndexOf('private Exception HandleAsynchronousFailure(', [StringComparison]::Ordinal)
$failureEnd = $source.IndexOf('private void ValidateFixtureCoordinate(', $failureStart, [StringComparison]::Ordinal)
$failureMethod = $source.Substring($failureStart, $failureEnd - $failureStart)
$failureDelayedClose = $failureMethod.IndexOf('delayedShutdown.Cancel()', [StringComparison]::Ordinal)
$failureTransientClose = $failureMethod.IndexOf('transientLoadCallbacks.Reset()', [StringComparison]::Ordinal)
$failureShutdown = $failureMethod.IndexOf('RequestShutdownIfConfigured(stage)', [StringComparison]::Ordinal)
if ($failureDelayedClose -lt 0 -or $failureTransientClose -le $failureDelayedClose -or $failureShutdown -le $failureTransientClose) {
    throw 'Asynchronous failure does not neutralize delayed and transient callbacks before shutdown.'
}

[ordered]@{
    TestId = 'L00-C-DELAYED-SHUTDOWN-GATE'
    Status = 'PASS'
    SynchronousFires = $script:syncFires
    AsynchronousFires = $script:asyncFires
    TransitionListenerId = $transitionListener
    LateTransitionFires = $script:transitionFires
    RejectedRegistrationAttached = $errorAttached
    RejectedFires = $script:errorFires
    DisposeListenerId = $disposeListener
    LateDisposeFires = $script:disposeFires
    ValidBounds = '0|50|60000'
    InvalidBounds = '1|49|60001'
    ProductionWiringInspected = $true
} | ConvertTo-Json -Depth 4
