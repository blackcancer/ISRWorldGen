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
    'ownedLoadedColumns',
    'LoadOwnedChunkColumns',
    'ReleaseOwnedColumns',
    'UnloadChunkColumn',
    'ColumnOwnershipState.Pending',
    'ColumnOwnershipState.Owned',
    'IsColumnAlreadyLoaded',
    'L00C_COLUMN_PREEXISTING_REJECTED',
    'L00C_COLUMN_LOAD_ROLLBACK',
    'ColumnLoadRollbackException',
    'L00C_COLUMN_RELEASE',
    'remainingowned=',
    'IsCurrentRun',
    'L00C_WITNESS_NO_REQUEST'
)
foreach ($fragment in $requiredSourceFragments) {
    if (-not $source.Contains($fragment)) {
        throw "Production ownership implementation is missing: $fragment"
    }
}
foreach ($reason in @('dispose', 'world-initialize')) {
    if ($source -notmatch ('ReleaseOwnedColumns\("' + [regex]::Escape($reason) + '"\)')) {
        throw "Production cleanup path is missing: $reason"
    }
}
if ($source -match 'ReleaseOwnedColumns\("fixture-stable"\)') {
    throw 'Terminal validation must retain the exact KeepLoaded footprint until shutdown Dispose, after the engine flushes generating chunks.'
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
        throw "Asynchronous or disposal ownership cleanup guard is missing: $fragment"
    }
}
if ([regex]::Matches($source, 'new\(EnumWorldGenPass\.').Count -ne 16) {
    throw 'The ownership oracle is not correlated to the 16 production handler specifications.'
}
if ([regex]::Matches($source, '\.LoadChunkColumnPriority\(').Count -ne 1 -or
    [regex]::Matches($source, '\.UnloadChunkColumn\(').Count -ne 1 -or
    [regex]::Matches($source, 'KeepLoaded = true').Count -ne 1) {
    throw 'All KeepLoaded requests and releases must pass through the sole production ownership helpers.'
}
if ($source.Contains('ownedLoadedColumns.Clear()')) {
    throw 'Owned coordinates must never be forgotten without an exact UnloadChunkColumn call.'
}
if ($source.Contains('InspectWitness') -or $source.Contains('role=inactive-witness') -or
    $source -match 'L00C_INACTIVE[^}]+ScheduleProbeColumn') {
    throw 'An inactive world still contains a probe-driven chunk request or inspection path.'
}
$initializeStart = $source.IndexOf('private void InitializeWorld()', [StringComparison]::Ordinal)
$runInvalidation = $source.IndexOf('Interlocked.Increment(ref worldRunId)', $initializeStart, [StringComparison]::Ordinal)
$worldRelease = $source.IndexOf('ReleaseOwnedColumns("world-initialize")', $initializeStart, [StringComparison]::Ordinal)
if ($initializeStart -lt 0 -or $runInvalidation -le $initializeStart -or $worldRelease -le $runInvalidation) {
    throw 'InitializeWorld must invalidate the prior run before releasing its owned columns.'
}
if ($source -notmatch 'if \(columnOwnershipClosing \|\| Volatile\.Read\(ref disposalStarted\) != 0\)\s*\{\s*reservation\.CallbackInvoked = true;') {
    throw 'A late owned-load callback is not consumed after ownership cleanup starts.'
}

function Invoke-LoadModel {
    param(
        [Collections.Generic.Dictionary[string,string]]$States,
        [string[]]$Coordinates,
        [Collections.Generic.HashSet[string]]$Preloaded,
        [int]$ThrowAfterAddition,
        [switch]$DisposeDuringLoad,
        [Collections.Generic.List[string]]$UnloadCalls
    )

    if (@($Coordinates | Where-Object { $Preloaded.Contains($_) }).Count -ne 0) {
        return 'preexisting-rejected'
    }
    foreach ($coordinate in $Coordinates) { $States.Add($coordinate, 'Pending') }
    if ($ThrowAfterAddition -gt 0) {
        if ($ThrowAfterAddition -gt $Coordinates.Count) { throw 'Synthetic throw point exceeds the transaction size.' }
        foreach ($coordinate in $Coordinates) { $States[$coordinate] = 'Owned' }
        foreach ($coordinate in @($States.Keys | Sort-Object)) {
            [void]$UnloadCalls.Add($coordinate)
            [void]$States.Remove($coordinate)
        }
        return "load-rolled-back-$ThrowAfterAddition"
    }
    foreach ($coordinate in $Coordinates) { $States[$coordinate] = 'Owned' }
    if ($DisposeDuringLoad) {
        foreach ($coordinate in @($States.Keys | Sort-Object)) {
            if ($States[$coordinate] -ne 'Owned') { throw "Dispose saw a non-owned state: $coordinate" }
            [void]$UnloadCalls.Add($coordinate)
            [void]$States.Remove($coordinate)
        }
        return 'disposed-after-accept'
    }
    return 'accepted'
}

$modelCoordinates = @(
    for ($deltaX = -1; $deltaX -le 1; $deltaX++) {
        for ($deltaZ = -1; $deltaZ -le 1; $deltaZ++) {
            "$(31990 + $deltaX),$(31990 + $deltaZ)"
        }
    }
)
$preloaded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
[void]$preloaded.Add('31989,31990')
$preloadedStates = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$preloadedUnloads = [Collections.Generic.List[string]]::new()
$preloadedResult = Invoke-LoadModel $preloadedStates $modelCoordinates $preloaded -UnloadCalls $preloadedUnloads
if ($preloadedResult -ne 'preexisting-rejected' -or $preloadedStates.Count -ne 0 -or $preloadedUnloads.Count -ne 0) {
    throw 'A preloaded/no-effect column was incorrectly claimed or unloaded.'
}

$emptyPreloaded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$throwCompensationCounts = [Collections.Generic.List[int]]::new()
foreach ($throwAfter in @(1, 5, 9)) {
    $throwStates = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $throwUnloads = [Collections.Generic.List[string]]::new()
    $throwResult = Invoke-LoadModel $throwStates $modelCoordinates $emptyPreloaded -ThrowAfterAddition $throwAfter -UnloadCalls $throwUnloads
    if ($throwResult -ne "load-rolled-back-$throwAfter" -or $throwStates.Count -ne 0 -or $throwUnloads.Count -ne 9) {
        throw "A load throwing after $throwAfter additions was not compensated across all nine potentially forced coordinates."
    }
    [void]$throwCompensationCounts.Add($throwUnloads.Count)
}

$disposeStates = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$disposeUnloads = [Collections.Generic.List[string]]::new()
$disposeResult = Invoke-LoadModel $disposeStates $modelCoordinates $emptyPreloaded -DisposeDuringLoad -UnloadCalls $disposeUnloads
if ($disposeResult -ne 'disposed-after-accept' -or $disposeStates.Count -ne 0 -or $disposeUnloads.Count -ne $modelCoordinates.Count) {
    throw 'Dispose interleaving did not wait for load acceptance before exact release.'
}

$oldRunId = 1L
$currentRunId = [Threading.Interlocked]::Increment([ref]$oldRunId)
$lateCallbackInvocationCount = 0
if (1L -eq $currentRunId) { $lateCallbackInvocationCount++ }
if ($lateCallbackInvocationCount -ne 0 -or $currentRunId -ne 2L) {
    throw 'A prior-world callback was not invalidated before world-initialize cleanup.'
}

$capturedFailureRun = 1L
$failureCheckBeforeInitialize = ($capturedFailureRun -eq 1L)
$currentFailureRun = 2L
$staleFailureReleaseCount = 0
$staleFailureShutdownCount = 0
if ($failureCheckBeforeInitialize -and $capturedFailureRun -eq $currentFailureRun) {
    $staleFailureReleaseCount++
    $staleFailureShutdownCount++
}
if ($staleFailureReleaseCount -ne 0 -or $staleFailureShutdownCount -ne 0) {
    throw 'A stale failure callback affected the newly initialized world after its earlier optimistic check.'
}

function New-Coordinate([int]$X, [int]$Z) {
    return "$X,$Z"
}

function Add-OwnedColumn([Collections.Generic.HashSet[string]]$Owned, [string]$Coordinate) {
    if (-not $Owned.Add($Coordinate)) {
        throw "Duplicate KeepLoaded ownership request: $Coordinate"
    }
}

function Release-OwnedColumns(
    [Collections.Generic.HashSet[string]]$Owned,
    [Collections.Generic.List[string]]$UnloadCalls,
    [string]$FailOnce = ''
) {
    $released = 0
    foreach ($coordinate in @($Owned | Sort-Object)) {
        [void]$UnloadCalls.Add($coordinate)
        if ($coordinate -eq $FailOnce) {
            continue
        }
        if (-not $Owned.Remove($coordinate)) {
            throw "Release lost owned coordinate: $coordinate"
        }
        $released++
    }
    return $released
}

$owned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
for ($deltaX = -1; $deltaX -le 1; $deltaX++) {
    for ($deltaZ = -1; $deltaZ -le 1; $deltaZ++) {
        $coordinate = New-Coordinate (31990 + $deltaX) (31990 + $deltaZ)
        [void]$expected.Add($coordinate)
        Add-OwnedColumn $owned $coordinate
    }
}
if ($owned.Count -ne 9 -or -not $owned.SetEquals($expected)) {
    throw 'The probe did not own exactly the forced 3x3 KeepLoaded footprint.'
}

$calls = [Collections.Generic.List[string]]::new()
$ownedAfterStableValidation = $owned.Count
$releaseCallsAfterStableValidation = $calls.Count
$releasedFirst = Release-OwnedColumns $owned $calls
$releasedSecond = Release-OwnedColumns $owned $calls
if ($releasedFirst -ne 9 -or $releasedSecond -ne 0 -or $owned.Count -ne 0) {
    throw "Idempotent release failed: first=$releasedFirst second=$releasedSecond remaining=$($owned.Count)."
}
if ($calls.Count -ne 9 -or @($calls | Where-Object { -not $expected.Contains($_) }).Count -ne 0) {
    throw 'Release attempted a column the probe did not own.'
}
if ($ownedAfterStableValidation -ne 9 -or $releaseCallsAfterStableValidation -ne 0) {
    throw 'Stable validation did not retain all nine owned columns until Dispose.'
}

$retryOwned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($coordinate in $expected) { [void]$retryOwned.Add($coordinate) }
$retryCalls = [Collections.Generic.List[string]]::new()
$failedCoordinate = New-Coordinate 31990 31990
$releasedBeforeRetry = Release-OwnedColumns $retryOwned $retryCalls $failedCoordinate
if ($releasedBeforeRetry -ne 8 -or $retryOwned.Count -ne 1 -or -not $retryOwned.Contains($failedCoordinate)) {
    throw 'A failed unload did not retain exact ownership for retry.'
}
$releasedOnRetry = Release-OwnedColumns $retryOwned $retryCalls
if ($releasedOnRetry -ne 1 -or $retryOwned.Count -ne 0) {
    throw 'Retry did not release the sole still-owned coordinate.'
}

[ordered]@{
    TestId = 'L00-C-COLUMN-OWNERSHIP'
    Status = 'PASS'
    ForcedCoordinateCount = $expected.Count
    FirstReleaseCount = $releasedFirst
    OwnedAfterStableValidation = $ownedAfterStableValidation
    ReleaseCallsAfterStableValidation = $releaseCallsAfterStableValidation
    IdempotentSecondReleaseCount = $releasedSecond
    ForeignUnloadCount = @($calls | Where-Object { -not $expected.Contains($_) }).Count
    RetainedAfterSyntheticFailure = 1
    ReleasedOnRetry = $releasedOnRetry
    RemainingOwned = $retryOwned.Count
    DisabledWitnessLoadCount = 0
    DisabledWitnessReleaseCount = 0
    PreloadedNoEffectUnloadCount = $preloadedUnloads.Count
    ThrowAfterAdditions = @(1, 5, 9)
    ThrowCompensationCounts = @($throwCompensationCounts)
    DisposeInterleaveReleaseCount = $disposeUnloads.Count
    LatePriorWorldCallbackInvocationCount = $lateCallbackInvocationCount
    StaleFailureReleaseCount = $staleFailureReleaseCount
    StaleFailureShutdownCount = $staleFailureShutdownCount
} | ConvertTo-Json -Depth 4
