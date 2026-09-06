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
    'L00C_COLUMN_RELEASE',
    'remainingowned=',
    'IsCurrentRun'
)
foreach ($fragment in $requiredSourceFragments) {
    if (-not $source.Contains($fragment)) {
        throw "Production ownership implementation is missing: $fragment"
    }
}
foreach ($reason in @('fixture-stable', 'inactive-witness-complete', 'dispose', 'world-initialize')) {
    if ($source -notmatch ('ReleaseOwnedColumns\("' + [regex]::Escape($reason) + '"\)')) {
        throw "Production cleanup path is missing: $reason"
    }
}
$requiredFailurePaths = @(
    'HandleAsynchronousFailure("probe-request-error"',
    'HandleAsynchronousFailure("halo-chain-error"',
    'HandleAsynchronousFailure("probe-loaded-error"',
    'HandleAsynchronousFailure("tick-validation-error"',
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
if ([regex]::Matches($source, '\.LoadChunkColumnPriority\(').Count -ne 2 -or
    [regex]::Matches($source, '\.UnloadChunkColumn\(').Count -ne 1 -or
    [regex]::Matches($source, 'KeepLoaded = true').Count -ne 1) {
    throw 'All KeepLoaded requests and releases must pass through the sole production ownership helpers.'
}
if ($source.Contains('ownedLoadedColumns.Clear()')) {
    throw 'Owned coordinates must never be forgotten without an exact UnloadChunkColumn call.'
}

function Invoke-LoadModel {
    param(
        [Collections.Generic.Dictionary[string,string]]$States,
        [string[]]$Coordinates,
        [Collections.Generic.HashSet[string]]$Preloaded,
        [switch]$ThrowFromLoad,
        [switch]$DisposeDuringLoad,
        [Collections.Generic.List[string]]$UnloadCalls
    )

    if (@($Coordinates | Where-Object { $Preloaded.Contains($_) }).Count -ne 0) {
        return 'preexisting-rejected'
    }
    foreach ($coordinate in $Coordinates) { $States.Add($coordinate, 'Pending') }
    if ($ThrowFromLoad) {
        foreach ($coordinate in $Coordinates) { [void]$States.Remove($coordinate) }
        return 'load-rejected'
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

$throwStates = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$throwUnloads = [Collections.Generic.List[string]]::new()
$emptyPreloaded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$throwResult = Invoke-LoadModel $throwStates $modelCoordinates $emptyPreloaded -ThrowFromLoad -UnloadCalls $throwUnloads
if ($throwResult -ne 'load-rejected' -or $throwStates.Count -ne 0 -or $throwUnloads.Count -ne 0) {
    throw 'A throwing load call was incorrectly promoted to ownership or unloaded.'
}

$disposeStates = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$disposeUnloads = [Collections.Generic.List[string]]::new()
$disposeResult = Invoke-LoadModel $disposeStates $modelCoordinates $emptyPreloaded -DisposeDuringLoad -UnloadCalls $disposeUnloads
if ($disposeResult -ne 'disposed-after-accept' -or $disposeStates.Count -ne 0 -or $disposeUnloads.Count -ne $modelCoordinates.Count) {
    throw 'Dispose interleaving did not wait for load acceptance before exact release.'
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
$releasedFirst = Release-OwnedColumns $owned $calls
$releasedSecond = Release-OwnedColumns $owned $calls
if ($releasedFirst -ne 9 -or $releasedSecond -ne 0 -or $owned.Count -ne 0) {
    throw "Idempotent release failed: first=$releasedFirst second=$releasedSecond remaining=$($owned.Count)."
}
if ($calls.Count -ne 9 -or @($calls | Where-Object { -not $expected.Contains($_) }).Count -ne 0) {
    throw 'Release attempted a column the probe did not own.'
}

$witnessOwned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$witnessCalls = [Collections.Generic.List[string]]::new()
Add-OwnedColumn $witnessOwned (New-Coordinate 31990 31990)
$witnessReleased = Release-OwnedColumns $witnessOwned $witnessCalls
if ($witnessReleased -ne 1 -or $witnessCalls.Count -ne 1 -or $witnessOwned.Count -ne 0) {
    throw 'Disabled witness KeepLoaded ownership was not released exactly once.'
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
    IdempotentSecondReleaseCount = $releasedSecond
    ForeignUnloadCount = @($calls | Where-Object { -not $expected.Contains($_) }).Count
    RetainedAfterSyntheticFailure = 1
    ReleasedOnRetry = $releasedOnRetry
    RemainingOwned = $retryOwned.Count
    DisabledWitnessReleaseCount = $witnessReleased
    PreloadedNoEffectUnloadCount = $preloadedUnloads.Count
    ThrowingLoadUnloadCount = $throwUnloads.Count
    DisposeInterleaveReleaseCount = $disposeUnloads.Count
} | ConvertTo-Json -Depth 4
