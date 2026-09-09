[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$source = Get-Content -LiteralPath $sourcePath -Raw
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.InitializationFailClosedGate', $false)
if ($null -eq $gateType) { throw 'The Debug assembly does not expose the production initialization fail-closed gate.' }

$begin = $gateType.GetMethod('BeginAttempt')
$fail = $gateType.GetMethod('Fail')
foreach ($method in @($begin, $fail)) {
    if ($null -eq $method) { throw 'The production initialization fail-closed gate contract is incomplete.' }
}

function Invoke-Failure($Gate, [int]$Attempt, [Action]$Close, [Action]$Shutdown) {
    return [bool]$fail.Invoke($Gate, @($Attempt, $Close, $Shutdown))
}

$gate = [Activator]::CreateInstance($gateType)
$attempt = [int]$begin.Invoke($gate, @())
$script:closeCalls = 0
$script:shutdownCalls = 0
$first = Invoke-Failure $gate $attempt ([Action]{ $script:closeCalls++ }) ([Action]{ $script:shutdownCalls++ })
$duplicate = Invoke-Failure $gate $attempt ([Action]{ $script:closeCalls++ }) ([Action]{ $script:shutdownCalls++ })
if (-not $first -or $duplicate -or $script:closeCalls -ne 1 -or $script:shutdownCalls -ne 1) {
    throw 'The production initialization gate did not close and request shutdown exactly once.'
}

$throwingCleanupGate = [Activator]::CreateInstance($gateType)
$throwingCleanupAttempt = [int]$begin.Invoke($throwingCleanupGate, @())
$script:cleanupShutdownCalls = 0
$cleanupResult = Invoke-Failure $throwingCleanupGate $throwingCleanupAttempt `
    ([Action]{ throw [InvalidOperationException]::new('synthetic-cleanup-failure') }) `
    ([Action]{ $script:cleanupShutdownCalls++ })
if (-not $cleanupResult -or $script:cleanupShutdownCalls -ne 1 -or $null -eq $gateType.GetProperty('CleanupException').GetValue($throwingCleanupGate)) {
    throw 'A failing initialization cleanup prevented the one required shutdown request.'
}

$staleGate = [Activator]::CreateInstance($gateType)
$staleAttempt = [int]$begin.Invoke($staleGate, @())
$nextAttempt = [int]$begin.Invoke($staleGate, @())
$script:staleShutdownCalls = 0
$stale = Invoke-Failure $staleGate $staleAttempt ([Action]{}) ([Action]{ $script:staleShutdownCalls++ })
$current = Invoke-Failure $staleGate $nextAttempt ([Action]{}) ([Action]{ $script:staleShutdownCalls++ })
if ($stale -or -not $current -or $script:staleShutdownCalls -ne 1) {
    throw 'A stale initialization attempt could request shutdown or suppress the current fail-closed request.'
}

$initializeStart = $source.IndexOf('private void InitializeWorld()', [StringComparison]::Ordinal)
$initializeEnd = $source.IndexOf('private void InitializeWorldCore()', $initializeStart, [StringComparison]::Ordinal)
$initializeMethod = $source.Substring($initializeStart, $initializeEnd - $initializeStart)
if ($initializeMethod -notmatch 'catch \(Exception exception\)[\s\S]+HandleInitializationFailure\(attempt, exception\)' -or
    $initializeMethod -match 'catch \(Exception exception\)[\s\S]+throw;') {
    throw 'InitializeWorld does not convert every initialization exception into the production fail-closed path.'
}
$failureStart = $source.IndexOf('private void HandleInitializationFailure(', [StringComparison]::Ordinal)
$failureEnd = $source.IndexOf('private void InitializeWorldCore()', $failureStart, [StringComparison]::Ordinal)
$failureMethod = $source.Substring($failureStart, $failureEnd - $failureStart)
$closeCallIndex = $failureMethod.IndexOf('CloseProbeStateAfterInitializationFailure()', [StringComparison]::Ordinal)
$shutdownIndex = $failureMethod.IndexOf('.Server.ShutDown()', [StringComparison]::Ordinal)
if ($closeCallIndex -lt 0 -or $shutdownIndex -le $closeCallIndex) {
    throw 'Initialization failure does not close probe state before requesting shutdown.'
}
$closeStart = $source.IndexOf('private void CloseProbeStateAfterInitializationFailure()', [StringComparison]::Ordinal)
$closeEnd = $source.IndexOf('private void InitializeWorldCore()', $closeStart, [StringComparison]::Ordinal)
$closeMethod = $source.Substring($closeStart, $closeEnd - $closeStart)
$invalidateIndex = $closeMethod.IndexOf('Interlocked.Increment(ref worldRunId)', [StringComparison]::Ordinal)
$markerCloseIndex = $closeMethod.IndexOf('markerPublication.BeginWorldTransition()', [StringComparison]::Ordinal)
$delayedCloseIndex = $closeMethod.IndexOf('delayedShutdown.CancelAndUnregister()', [StringComparison]::Ordinal)
$callbackCloseIndex = $closeMethod.IndexOf('transientLoadCallbacks.Reset()', [StringComparison]::Ordinal)
$restoreIndex = $closeMethod.IndexOf('RestoreOwnedHandlerSet("initialization-failure")', [StringComparison]::Ordinal)
if ($invalidateIndex -lt 0 -or $markerCloseIndex -le $invalidateIndex -or $delayedCloseIndex -le $markerCloseIndex -or
    $callbackCloseIndex -le $delayedCloseIndex -or $restoreIndex -le $callbackCloseIndex -or
    $closeMethod -notmatch 'active = false' -or $closeMethod -notmatch 'marker = null') {
    throw 'Initialization failure does not close both callback gates before fallible external cleanup and handler restoration.'
}

[void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath 'Lib\Mono.Cecil.dll'))
$definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
try {
    $probeDefinition = $definition.MainModule.Types | Where-Object FullName -eq 'ISRWorldGen.WorldgenProbe.L00CWorldgenProbeModSystem' | Select-Object -First 1
    if ($null -eq $probeDefinition) { throw 'The built Debug assembly lacks the production L00-C ModSystem IL.' }
    $handleDefinition = $probeDefinition.Methods | Where-Object Name -eq 'HandleInitializationFailure' | Select-Object -First 1
    $closeDefinition = $probeDefinition.Methods | Where-Object Name -eq 'CloseProbeStateAfterInitializationFailure' | Select-Object -First 1
    $closureDefinitions = @($probeDefinition.NestedTypes | ForEach-Object {
        $_.Methods | Where-Object { $_.Name -like '*HandleInitializationFailure*' }
    })
    if ($null -eq $handleDefinition -or $null -eq $closeDefinition -or $closureDefinitions.Count -ne 2) {
        throw 'The built fail-closed IL shape is incomplete.'
    }
    $handleCalls = @(($handleDefinition.Body.Instructions | Where-Object { $_.OpCode.Name -like 'call*' } | ForEach-Object { [string]$_.Operand }))
    $closeIlCalls = @(($closeDefinition.Body.Instructions | Where-Object { $_.OpCode.Name -like 'call*' } | ForEach-Object { [string]$_.Operand }))
    $closureCalls = @(($closureDefinitions | ForEach-Object {
        $_.Body.Instructions | Where-Object { $_.OpCode.Name -like 'call*' } | ForEach-Object { [string]$_.Operand }
    }))
    if (@($handleCalls | Where-Object { $_ -match 'InitializationFailClosedGate::Fail' }).Count -ne 1 -or
        @($closureCalls | Where-Object { $_ -match 'IServerAPI::ShutDown' }).Count -ne 1 -or
        @($closeIlCalls | Where-Object { $_ -match 'MarkerPublicationGate::BeginWorldTransition' }).Count -ne 1 -or
        @($closeIlCalls | Where-Object { $_ -match 'DelayedShutdownGate::CancelAndUnregister' }).Count -ne 1 -or
        @($closeIlCalls | Where-Object { $_ -match 'TransientLoadCallbackGate::Reset' }).Count -ne 1 -or
        @($closeIlCalls | Where-Object { $_ -match 'RestoreOwnedHandlerSet' }).Count -ne 1) {
        throw 'The built fail-closed IL does not close publication/callbacks/handlers and call ShutDown exactly once.'
    }
    $forbiddenFailureCalls = @($handleCalls + $closeIlCalls + $closureCalls | Where-Object {
        $_ -match 'ScheduleProbeColumn|LoadTransientChunkColumns|ApplyTargetedReplacement|WriteCanonicalFixture|MarkerPublicationGate::Commit|MarkerPublicationGate::SaveIfCommitted'
    })
    if ($forbiddenFailureCalls.Count -ne 0) {
        throw "The built fail-closed IL can generate or publish after refusal: $($forbiddenFailureCalls -join '; ')"
    }
}
finally {
    $definition.Dispose()
}

[ordered]@{
    TestId = 'L00-C-INITIALIZATION-FAIL-CLOSED'
    Status = 'PASS'
    FirstFailureAccepted = $first
    DuplicateFailureAccepted = $duplicate
    CloseCalls = $script:closeCalls
    ShutdownCalls = $script:shutdownCalls
    ShutdownAfterCleanupFailure = $script:cleanupShutdownCalls
    StaleFailureAccepted = $stale
    CurrentFailureAccepted = $current
    ProductionWiringInspected = $true
    ProductionIlInspected = $true
    FailClosedShutdownIlCalls = 1
    FailClosedGenerationOrPublicationIlCalls = 0
} | ConvertTo-Json -Depth 4
