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
$f5ProfilePath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L00C\Set-L00CF5AuthenticatedProfile.ps1'
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$source = Get-Content -LiteralPath $sourcePath -Raw
$profileSource = Get-Content -LiteralPath $profilePath -Raw
$f5ProfileSource = Get-Content -LiteralPath $f5ProfilePath -Raw
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.DelayedShutdownGate', $false)
if ($null -eq $gateType) { throw 'The Debug assembly does not expose the production delayed-shutdown gate.' }
$reservationType = $assembly.GetType('ISRWorldGen.WorldgenProbe.DelayedShutdownReservation', $false)
if ($null -eq $reservationType) { throw 'The Debug assembly does not expose the production delayed-shutdown reservation.' }
$configType = $assembly.GetType('ISRWorldGen.WorldgenProbe.L00CProbeConfig', $false)
if ($null -eq $configType) { throw 'The Debug assembly does not expose the production L00-C probe configuration.' }
$bindLaboratoryConfig = $configType.GetMethod('BindLaboratoryShutdownConfiguration', [Reflection.BindingFlags]'Static,NonPublic')
if ($null -eq $bindLaboratoryConfig) { throw 'The Debug assembly does not expose the profile-to-probe shutdown binding.' }

$open = $gateType.GetMethod('Open')
$begin = $gateType.GetMethod('Begin')
$attach = $gateType.GetMethod('Attach')
$complete = $gateType.GetMethod('Complete')
$reject = $gateType.GetMethod('Reject')
$cancel = $gateType.GetMethod('Cancel')
$validateDelay = $gateType.GetMethod('ValidateDelayMilliseconds')
$validateActiveDelay = $gateType.GetMethod('ValidateActiveDelayMilliseconds')
foreach ($method in @($open, $begin, $attach, $complete, $reject, $cancel, $validateDelay, $validateActiveDelay)) {
    if ($null -eq $method) { throw 'The production delayed-shutdown gate contract is incomplete.' }
}

function New-Gate { return [Activator]::CreateInstance($gateType) }
function Open-Gate($Gate) { [void]$open.Invoke($Gate, @()) }
function Begin-Reservation($Gate, [Action]$Callback) { return $begin.Invoke($Gate, @($Callback)) }
function Assert-Refused([scriptblock]$Operation, [string]$Label) {
    try { & $Operation } catch { return }
    throw "Expected refusal: $Label"
}
function Bind-LaboratoryConfig([string]$Lab, [string]$AutoShutdown, [string]$Delay) {
    $candidate = [Activator]::CreateInstance($configType)
    $configType.GetProperty('AutoShutdownDelayMilliseconds').SetValue($candidate, 0)
    return $bindLaboratoryConfig.Invoke($null, @($candidate, $Lab, $AutoShutdown, $Delay))
}

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
$script:secondArmRejected = $false
try { [void](Begin-Reservation $asyncGate ([Action]{})) } catch { $script:secondArmRejected = $true }
[void]$complete.Invoke($asyncGate, @($asyncReservation))
[void]$complete.Invoke($asyncGate, @($asyncReservation))
if (-not $asyncAttached -or $script:asyncFires -ne 1 -or -not $script:secondArmRejected) { throw 'Asynchronous delayed callback was not unique and exactly-once.' }

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
foreach ($valid in @(10000, 15000, 60000)) {
    if ([int]$validateActiveDelay.Invoke($null, @($valid)) -ne $valid) { throw "Valid active delay $valid drifted." }
}
foreach ($invalid in @(0, 50, 9999, 60001)) {
    try { [void]$validateActiveDelay.Invoke($null, @($invalid)) } catch { continue }
    throw "Invalid active delay $invalid was accepted."
}

# A persisted lab config written by an older run may contain delay=0. The
# one-shot F5 profile is authoritative for this transient safety setting, and
# must provide a canonical in-range value. No default or persisted zero can
# silently arm an active shutdown.
$bound = Bind-LaboratoryConfig '1' '1' '15000'
if (-not [bool]$configType.GetProperty('AutoShutdown').GetValue($bound) -or [int]$configType.GetProperty('AutoShutdownDelayMilliseconds').GetValue($bound) -ne 15000) {
    throw 'The evaluated F5 profile did not bind the effective active shutdown configuration.'
}
foreach ($case in @(
    @('1', '', '15000', 'missing enabled value'),
    @('1', '0', '15000', 'wrong enabled value'),
    @('1', '1', '', 'missing delay'),
    @('1', '1', '0', 'zero delay'),
    @('1', '1', '9999', 'below-minimum delay'),
    @('1', '1', '60001', 'above-maximum delay'),
    @('1', '1', '015000', 'noncanonical delay'),
    @('1', '1', '15000ms', 'non-numeric delay')
)) {
    Assert-Refused { [void](Bind-LaboratoryConfig $case[0] $case[1] $case[2]) } $case[3]
}

foreach ($fragment in @(
    'AutoShutdownDelayMilliseconds',
    'BindLaboratoryShutdownConfiguration',
    'ISR_L00C_AUTOSHUTDOWN',
    'ISR_L00C_AUTOSHUTDOWN_DELAY_MS',
    'RequestInactiveWitnessShutdown(runId)',
    'RequestActiveShutdown(runId, "persisted-reopen-stable")',
    'RequestActiveShutdown(runId, "fixture-stable")',
    'RequestScheduledShutdown(runId, reason, requireActiveDelay: true)',
    'DelayedShutdownGate.ValidateActiveDelayMilliseconds(config.AutoShutdownDelayMilliseconds)',
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
if (-not $profileSource.Contains('AutoShutdownDelayMilliseconds = $AutoShutdownDelayMilliseconds') -or
    -not $profileSource.Contains('[int]$AutoShutdownDelayMilliseconds = 15000')) {
    throw 'The isolated lab profile cannot select the bounded delayed shutdown.'
}
foreach ($profileEnvironment in @(
    "-NotePropertyName ISR_L00C_AUTOSHUTDOWN -NotePropertyValue '1'",
    "-NotePropertyName ISR_L00C_AUTOSHUTDOWN_DELAY_MS -NotePropertyValue '15000'"
)) {
    if (-not $f5ProfileSource.Contains($profileEnvironment)) { throw "The F5 profile does not bind $profileEnvironment exactly." }
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
$reopenStart = $source.IndexOf('private void CompletePersistedReopen(', [StringComparison]::Ordinal)
$reopenEnd = $source.IndexOf('private void CompleteInactiveWitness(', $reopenStart, [StringComparison]::Ordinal)
$reopenMethod = $source.Substring($reopenStart, $reopenEnd - $reopenStart)
if ($reopenMethod.IndexOf('L00C_PERSISTED_REOPEN_STABLE', [StringComparison]::Ordinal) -lt 0 -or
    $reopenMethod.IndexOf('RequestActiveShutdown(runId, "persisted-reopen-stable")', [StringComparison]::Ordinal) -le $reopenMethod.IndexOf('L00C_PERSISTED_REOPEN_STABLE', [StringComparison]::Ordinal) -or
    $reopenMethod.Contains('RequestShutdownIfConfigured("persisted-reopen-stable")')) {
    throw 'Persisted reopen does not arm its run-bound active shutdown after stable attestation.'
}
$tickStart = $source.IndexOf('private void OnServerTickCore(', [StringComparison]::Ordinal)
$tickEnd = $source.IndexOf('private PersistedFootprintSnapshot InspectPersistedFootprintBlocking(', $tickStart, [StringComparison]::Ordinal)
$tickMethod = $source.Substring($tickStart, $tickEnd - $tickStart)
if ($tickMethod.IndexOf('L00C_MAP_SNAPSHOT_COMMITTED', [StringComparison]::Ordinal) -lt 0 -or
    $tickMethod.IndexOf('RequestActiveShutdown(runId, "fixture-stable")', [StringComparison]::Ordinal) -le $tickMethod.IndexOf('L00C_MAP_SNAPSHOT_COMMITTED', [StringComparison]::Ordinal) -or
    $tickMethod.Contains('RequestShutdownIfConfigured("fixture-stable")')) {
    throw 'New-world fixture does not arm its run-bound active shutdown after snapshot commit.'
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
    ValidActiveBounds = '10000|15000|60000'
    InvalidActiveBounds = '0|50|9999|60001'
    ProfileToProbeContract = 'ISR_L00C_AUTOSHUTDOWN=1;ISR_L00C_AUTOSHUTDOWN_DELAY_MS=15000'
    ProfileRefusals = 'missing|wrong|zero|out-of-range|noncanonical'
    SecondArmRejected = $script:secondArmRejected
    ProductionWiringInspected = $true
} | ConvertTo-Json -Depth 4
