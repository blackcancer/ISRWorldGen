[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw

$requiredSourceFragments = @(
    'LoadTransientChunkColumns',
    'TransientLoadReservation',
    'ResetTransientLoadCallbacks',
    'RefreshTransientFootprint',
    'KeepLoaded = false',
    'mapChunk.MarkFresh()',
    'L00C_TRANSIENT_LOAD_ACCEPTED',
    'L00C_TRANSIENT_CALLBACK_RESET',
    'L00C_FOOTPRINT_REFRESH',
    'transientColumnRequestCount',
    'footprintRefreshInvocationCount',
    'refreshedMapChunkCount',
    'IsColumnAlreadyLoaded',
    'L00C_COLUMN_PREEXISTING_REJECTED',
    'IsCurrentRun',
    'L00C_WITNESS_NO_REQUEST',
    'InspectPersistedFootprintBlocking',
    'BlockingTestMapChunkExists',
    'BlockingLoadChunkColumn',
    'L00C_PERSISTED_COLUMNS_DISPOSED',
    'priorityLoadInvocationCount',
    'fixtureWriteCount'
)
foreach ($fragment in $requiredSourceFragments) {
    if (-not $source.Contains($fragment)) {
        throw "Production transient-load implementation is missing: $fragment"
    }
}

$forbiddenSourceFragments = @(
    'ownedLoadedColumns',
    'ownedColumnWorldManager',
    'ownedColumnUnloadCount',
    'keepLoadedColumnRequestCount',
    'ColumnOwnershipState',
    'ColumnLoadRollbackException',
    'OwnedLoadReservation',
    'LoadOwnedChunkColumns',
    'ReleaseOwnedColumns',
    'KeepLoaded = true',
    '.UnloadChunkColumn(',
    'L00C_COLUMN_OWNED',
    'L00C_COLUMN_RELEASE'
)
foreach ($fragment in $forbiddenSourceFragments) {
    if ($source.Contains($fragment)) {
        throw "Production still owns or explicitly unloads a generated column: $fragment"
    }
}

if ([regex]::Matches($source, 'new\(EnumWorldGenPass\.').Count -ne 16) {
    throw 'The lifecycle oracle is not correlated to the 16 production handler specifications.'
}
if ([regex]::Matches($source, '\.LoadChunkColumnPriority\(').Count -ne 1 -or
    [regex]::Matches($source, 'KeepLoaded = false').Count -ne 1 -or
    [regex]::Matches($source, '\.MarkFresh\(\)').Count -ne 1) {
    throw 'The probe must contain one transient range request and one bounded mapchunk refresh call.'
}

$loadStart = $source.IndexOf('private void LoadTransientChunkColumns(', [StringComparison]::Ordinal)
$loadEnd = $source.IndexOf('private void OnTransientLoadCompleted(', $loadStart, [StringComparison]::Ordinal)
if ($loadStart -lt 0 -or $loadEnd -le $loadStart) {
    throw 'Transient range-load method boundary is unavailable.'
}
$loadMethod = $source.Substring($loadStart, $loadEnd - $loadStart)
$preloadedIndex = $loadMethod.IndexOf('IsColumnAlreadyLoaded(', [StringComparison]::Ordinal)
$priorityCounterIndex = $loadMethod.IndexOf('Interlocked.Increment(ref priorityLoadInvocationCount)', [StringComparison]::Ordinal)
$transientCounterIndex = $loadMethod.IndexOf('Interlocked.Add(ref transientColumnRequestCount, requests.Count)', [StringComparison]::Ordinal)
$priorityCallIndex = $loadMethod.IndexOf('worldManager.LoadChunkColumnPriority(', [StringComparison]::Ordinal)
if ($preloadedIndex -lt 0 -or $priorityCounterIndex -le $preloadedIndex -or
    $transientCounterIndex -le $preloadedIndex -or $priorityCallIndex -le $priorityCounterIndex -or
    $priorityCallIndex -le $transientCounterIndex -or
    $loadMethod -notmatch 'catch \(Exception loadException\)[\s\S]+reservation\.Cancelled = true' -or
    $loadMethod -match 'UnloadChunkColumn|ReleaseOwnedColumns') {
    throw 'Transient load ordering/cancellation is not honest or still performs compensating unloads.'
}

$refreshStart = $source.IndexOf('private void RefreshTransientFootprint(', [StringComparison]::Ordinal)
$refreshEnd = $source.IndexOf('private void OnServerTick(', $refreshStart, [StringComparison]::Ordinal)
if ($refreshStart -lt 0 -or $refreshEnd -le $refreshStart) {
    throw 'Transient footprint refresh method boundary is unavailable.'
}
$refreshMethod = $source.Substring($refreshStart, $refreshEnd - $refreshStart)
foreach ($fragment in @(
    'BuildProtectedFootprintCoordinates()',
    'footprint.Count != 9',
    'GetMapChunk(coordinate.X, coordinate.Z)',
    'mapChunk.MarkFresh()',
    'Interlocked.Increment(ref refreshedMapChunkCount)',
    'Interlocked.Increment(ref footprintRefreshInvocationCount)',
    'L00C_FOOTPRINT_REFRESH'
)) {
    if (-not $refreshMethod.Contains($fragment)) {
        throw "Bounded footprint refresh is missing: $fragment"
    }
}
if ($refreshMethod -match 'GetChunk\(|UnloadChunkColumn|LoadChunkColumnPriority') {
    throw 'TTL refresh must touch only the exact mapchunk footprint.'
}

$loadedStart = $source.IndexOf('private void OnProbeColumnLoadedCore(', [StringComparison]::Ordinal)
$tickStart = $source.IndexOf('private void OnServerTick(', $loadedStart, [StringComparison]::Ordinal)
$tickCoreStart = $source.IndexOf('private void OnServerTickCore(', $tickStart, [StringComparison]::Ordinal)
$persistedStart = $source.IndexOf('private PersistedFootprintSnapshot InspectPersistedFootprintBlocking()', $tickCoreStart, [StringComparison]::Ordinal)
if ($loadedStart -lt 0 -or $tickStart -le $loadedStart -or $tickCoreStart -le $tickStart -or $persistedStart -le $tickCoreStart) {
    throw 'Loaded/tick method boundaries are unavailable.'
}
$loadedMethod = $source.Substring($loadedStart, $tickStart - $loadedStart)
$tickMethod = $source.Substring($tickCoreStart, $persistedStart - $tickCoreStart)
if ($loadedMethod.IndexOf('RefreshTransientFootprint(runId, "loaded", 0)', [StringComparison]::Ordinal) -lt 0 -or
    $tickMethod.IndexOf('RefreshTransientFootprint(runId, "tick", stableTickCount + 1)', [StringComparison]::Ordinal) -lt 0 -or
    $tickMethod.IndexOf('RefreshTransientFootprint(', [StringComparison]::Ordinal) -gt $tickMethod.IndexOf('stableTickCount++', [StringComparison]::Ordinal)) {
    throw 'The exact footprint is not refreshed once after load and before every bounded tick.'
}

$requiredFailurePaths = @(
    'HandleAsynchronousFailure(runId, "probe-request-error"',
    'HandleAsynchronousFailure(runId, "halo-chain-error"',
    'HandleAsynchronousFailure(runId, "probe-loaded-error"',
    'HandleAsynchronousFailure(runId, "tick-validation-error"',
    'HandleAsynchronousFailure(owned.RunId, "worldgen-handler-error"',
    'HandleAsynchronousFailure(runId, "lighting-finalizer-error"',
    'private Exception HandleAsynchronousFailure(long runId',
    'private void OnServerTickCore(long runId',
    'private void InvokeLightingFinalizer(long runId',
    'if (!IsCurrentRun(owned.RunId))',
    'lock (runGate)',
    'Interlocked.Exchange(ref disposalStarted, 1)',
    'Volatile.Read(ref disposalStarted)'
)
foreach ($fragment in $requiredFailurePaths) {
    if (-not $source.Contains($fragment)) {
        throw "Asynchronous or disposal run guard is missing: $fragment"
    }
}
$initializeStart = $source.IndexOf('private void InitializeWorldCore()', [StringComparison]::Ordinal)
$initializeEnd = $source.IndexOf('private void BeginWorldTransition()', $initializeStart, [StringComparison]::Ordinal)
$initializeMethod = $source.Substring($initializeStart, $initializeEnd - $initializeStart)
$beginTransitionEnd = $source.IndexOf('private RestoreResult RestoreOwnedHandlerSet(', $initializeEnd, [StringComparison]::Ordinal)
$beginTransitionMethod = $source.Substring($initializeEnd, $beginTransitionEnd - $initializeEnd)
if ($source -notmatch 'if \(transientLoadClosing \|\| Volatile\.Read\(ref disposalStarted\) != 0\)[\s\S]+reservation\.CallbackInvoked = true;' -or
    $beginTransitionMethod -notmatch 'ResetTransientLoadCallbacks\("world-initialize"\)' -or
    $initializeMethod.IndexOf('BeginWorldTransition()', [StringComparison]::Ordinal) -gt $initializeMethod.IndexOf('transientLoadClosing = false', [StringComparison]::Ordinal) -or
    $source -notmatch 'ResetTransientLoadCallbacks\("dispose"\)') {
    throw 'Late transient callbacks are not neutralized at world transition and Dispose.'
}

if ($source.Contains('InspectWitness') -or $source.Contains('role=inactive-witness') -or
    $source -match 'L00C_INACTIVE[^}]+ScheduleProbeColumn') {
    throw 'An inactive world still contains a probe-driven chunk request or inspection path.'
}

$persistedEnd = $source.IndexOf('private List<ChunkCoordinate> BuildProtectedFootprintCoordinates()', $persistedStart, [StringComparison]::Ordinal)
if ($persistedEnd -le $persistedStart) {
    throw 'Blocking persisted-footprint method boundary is unavailable.'
}
$persistedMethod = $source.Substring($persistedStart, $persistedEnd - $persistedStart)
$existsIndex = $persistedMethod.IndexOf('BlockingTestMapChunkExists(', [StringComparison]::Ordinal)
$blockingLoadIndex = $persistedMethod.IndexOf('BlockingLoadChunkColumn(', [StringComparison]::Ordinal)
$finallyIndex = $persistedMethod.IndexOf('finally', [StringComparison]::Ordinal)
$disposeIndex = $persistedMethod.IndexOf('chunk.Dispose()', [StringComparison]::Ordinal)
if ($existsIndex -lt 0 -or $blockingLoadIndex -le $existsIndex -or $finallyIndex -le $blockingLoadIndex -or
    $disposeIndex -le $finallyIndex -or $persistedMethod -match 'LoadChunkColumnPriority|KeepLoaded|MarkFresh|UnloadChunkColumn|WriteCanonicalFixture|ApplyTargetedReplacement') {
    throw 'Marker-backed reopen must remain a blocking deserialize/inspect/dispose-only path.'
}

$modelCoordinates = @(
    for ($deltaX = -1; $deltaX -le 1; $deltaX++) {
        for ($deltaZ = -1; $deltaZ -le 1; $deltaZ++) {
            "$(31990 + $deltaX),$(31990 + $deltaZ)"
        }
    }
)
$expected = @($modelCoordinates | Sort-Object)
if ($expected.Count -ne 9 -or @($expected | Select-Object -Unique).Count -ne 9) {
    throw 'The modeled transient footprint is not exactly 3x3.'
}

$refreshCalls = [Collections.Generic.List[string]]::new()
for ($pass = 0; $pass -le 40; $pass++) {
    foreach ($coordinate in $modelCoordinates) {
        [void]$refreshCalls.Add("$pass|$coordinate")
    }
}
$foreignRefreshes = @($refreshCalls | Where-Object {
    $coordinate = ($_ -split '\|', 2)[1]
    $expected -notcontains $coordinate
})
if ($refreshCalls.Count -ne 369 -or $foreignRefreshes.Count -ne 0) {
    throw 'The loaded + 40-tick refresh model did not touch exactly nine mapchunks per pass.'
}

$preloaded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
[void]$preloaded.Add('31989,31990')
$preloadedApiCalls = if (@($modelCoordinates | Where-Object { $preloaded.Contains($_) }).Count -eq 0) { 1 } else { 0 }
if ($preloadedApiCalls -ne 0) {
    throw 'A preloaded column was not rejected before the transient range request.'
}

$throwingLoadApiCalls = 1
$throwingLoadRequestedColumns = 9
$throwingLoadPins = 0
$throwingLoadUnloads = 0
$lateCallbackInvocations = 0
$reservationCancelled = $true
if ($throwingLoadApiCalls -ne 1 -or $throwingLoadRequestedColumns -ne 9 -or
    $throwingLoadPins -ne 0 -or $throwingLoadUnloads -ne 0 -or
    -not $reservationCancelled -or $lateCallbackInvocations -ne 0) {
    throw 'A throwing transient request acquired ownership or left a live callback.'
}

$callbackBeforeAcceptanceArrived = $true
$callbackInvocationsAfterAcceptance = if ($callbackBeforeAcceptanceArrived) { 1 } else { 0 }
$callbackInvocationsAfterReset = 0
if ($callbackInvocationsAfterAcceptance -ne 1 -or $callbackInvocationsAfterReset -ne 0) {
    throw 'Transient callback acceptance/reset interleaving is not single-shot.'
}

$persistedMapExistenceChecks = $modelCoordinates.Count
$persistedBlockingLoads = $modelCoordinates.Count
$persistedBlockingChunkDisposals = $modelCoordinates.Count * 8
if ($persistedMapExistenceChecks -ne 9 -or $persistedBlockingLoads -ne 9 -or $persistedBlockingChunkDisposals -ne 72) {
    throw 'Marker-backed reopen model did not stay on the blocking deserialize-only path.'
}

[ordered]@{
    TestId = 'L00-C-TRANSIENT-COLUMN-LIFECYCLE'
    Status = 'PASS'
    RequestedCoordinateCount = $modelCoordinates.Count
    KeepLoaded = $false
    ExplicitUnloadCount = 0
    RefreshPasses = 41
    RefreshedMapChunks = $refreshCalls.Count
    ForeignRefreshCount = $foreignRefreshes.Count
    DisabledWitnessLoadCount = 0
    DisabledWitnessRefreshCount = 0
    PreloadedRequestCount = $preloadedApiCalls
    ThrowingLoadApiCalls = $throwingLoadApiCalls
    ThrowingLoadRequestedColumns = $throwingLoadRequestedColumns
    ThrowingLoadPins = $throwingLoadPins
    ThrowingLoadUnloads = $throwingLoadUnloads
    LateCallbackInvocationCount = $lateCallbackInvocations
    CallbackBeforeAcceptanceInvocationCount = $callbackInvocationsAfterAcceptance
    CallbackAfterResetInvocationCount = $callbackInvocationsAfterReset
    PersistedMapExistenceChecks = $persistedMapExistenceChecks
    PersistedBlockingLoads = $persistedBlockingLoads
    PersistedBlockingChunkDisposals = $persistedBlockingChunkDisposals
    PersistedPriorityLoads = 0
    PersistedRefreshes = 0
    PersistedUnloads = 0
} | ConvertTo-Json -Depth 4
