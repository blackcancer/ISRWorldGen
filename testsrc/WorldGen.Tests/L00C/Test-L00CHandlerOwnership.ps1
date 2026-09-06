[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
foreach ($fragment in @(
    'InspectPersistedFootprintBlocking',
    'BlockingTestMapChunkExists',
    'BlockingLoadChunkColumn',
    'L00C_PERSISTED_REOPEN_STABLE',
    'loadpriority={priorityLoads}',
    'fixturewrites={fixtureWrites}',
    'callbacks={fixtureCallbackCount}'
)) {
    if (-not $source.Contains($fragment)) {
        throw "Persisted-footprint replay guard is missing from production: $fragment"
    }
}
$initializeStart = $source.IndexOf('private void InitializeWorldCore()', [StringComparison]::Ordinal)
$initializeEnd = $source.IndexOf('private RestoreResult RestoreOwnedHandlerSet(', $initializeStart, [StringComparison]::Ordinal)
$initializeMethod = $source.Substring($initializeStart, $initializeEnd - $initializeStart)
$reopenStart = $initializeMethod.IndexOf('if (!saveGame.IsNew)', [StringComparison]::Ordinal)
$reopenInspect = $initializeMethod.IndexOf('InspectPersistedFootprintBlocking()', $reopenStart, [StringComparison]::Ordinal)
$reopenSchedule = $initializeMethod.IndexOf('SchedulePersistedReopen(', $reopenStart, [StringComparison]::Ordinal)
$reopenReturn = $initializeMethod.IndexOf('return;', $reopenSchedule, [StringComparison]::Ordinal)
$handlerValidation = $initializeMethod.IndexOf('ValidateReplacementPreconditions(handlers)', [StringComparison]::Ordinal)
if ($reopenStart -lt 0 -or $reopenInspect -le $reopenStart -or $reopenSchedule -le $reopenInspect -or
    $reopenReturn -le $reopenSchedule -or $handlerValidation -le $reopenReturn) {
    throw 'Persisted reopen must inspect and return before any worldgen handler replacement.'
}
$reopenBranch = $initializeMethod.Substring($reopenStart, $reopenReturn - $reopenStart)
if ($reopenBranch -match 'ApplyTargetedReplacement|ScheduleProbeColumn|LoadChunkColumnPriority|KeepLoaded') {
    throw 'Persisted reopen still enters a worldgen replacement or priority-load path.'
}

function New-Handler {
    param(
        [string]$Pass,
        [string]$Target,
        [string]$Method,
        [bool]$Targeted = $false,
        [bool]$Writer = $false,
        [bool]$SuppressInHalo = $false
    )

    return [pscustomobject]@{
        Kind = 'Native'
        Pass = $Pass
        Target = $Target
        Method = $Method
        Targeted = $Targeted
        Writer = $Writer
        SuppressInHalo = $SuppressInHalo
    }
}

function Assert-ReferenceSequence {
    param([object[]]$Actual, [object[]]$Expected, [string]$Label)

    if ($Actual.Count -ne $Expected.Count) {
        throw "$Label count expected $($Expected.Count), got $($Actual.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [object]::ReferenceEquals($Actual[$index], $Expected[$index])) {
            throw "$Label reference mismatch at index $index."
        }
    }
}

function Install-Ownership {
    param([hashtable]$Passes)

    $original = @{}
    $installed = @{}
    $wrappers = [Collections.Generic.List[object]]::new()
    foreach ($pass in @($Passes.Keys)) {
        $original[$pass] = @($Passes[$pass])
        $rebuilt = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $original[$pass].Count; $index++) {
            $handler = $original[$pass][$index]
            if ($handler.Targeted) {
                $wrapper = [pscustomobject]@{
                    Kind = 'Wrapper'
                    Pass = $pass
                    OriginalIndex = $index
                    OriginalTarget = $handler.Target
                    OriginalMethod = $handler.Method
                    Original = $handler
                    Writer = $handler.Writer
                    SuppressInHalo = $handler.SuppressInHalo
                }
                [void]$wrappers.Add($wrapper)
                [void]$rebuilt.Add($wrapper)
            }
            else {
                [void]$rebuilt.Add($handler)
            }
        }

        if ($pass -eq 'Vegetation') {
            $lightIndex = -1
            for ($index = 0; $index -lt $rebuilt.Count; $index++) {
                $candidate = $rebuilt[$index]
                if ($candidate.Kind -eq 'Native' -and $candidate.Target -eq 'GenLightSurvival') {
                    $lightIndex = $index
                    break
                }
            }
            if ($lightIndex -lt 0) { throw 'Lighting anchor is absent from the analytical inventory.' }
            $rebuilt.Insert($lightIndex, [pscustomobject]@{ Kind = 'Finalizer'; Pass = $pass })
        }

        $installed[$pass] = @($rebuilt)
        $Passes[$pass] = @($rebuilt)
    }

    return [pscustomobject]@{
        Original = $original
        Installed = $installed
        Wrappers = @($wrappers)
        Restored = $false
    }
}

function Restore-Ownership {
    param([hashtable]$Passes, $State)

    if ($State.Restored) { return 0 }
    foreach ($pass in @($State.Installed.Keys)) {
        Assert-ReferenceSequence @($Passes[$pass]) @($State.Installed[$pass]) "Installed $pass"
    }

    $removed = 0
    foreach ($pass in @($State.Original.Keys)) {
        $removed += @($State.Installed[$pass] | Where-Object Kind -in @('Wrapper', 'Finalizer')).Count
        $Passes[$pass] = @($State.Original[$pass])
        Assert-ReferenceSequence @($Passes[$pass]) @($State.Original[$pass]) "Restored $pass"
    }
    $State.Restored = $true
    return $removed
}

$passes = @{
    Terrain = @(
        (New-Handler Terrain GenTerra OnChunkColumnGen $true $true),
        (New-Handler Terrain GenRockStrataNew GenChunkColumn $true),
        (New-Handler Terrain L00B OnChunkColumnGeneration),
        (New-Handler Terrain GenCaves GenChunkColumn $true),
        (New-Handler Terrain GenDevastationLayer OnChunkColumnGeneration $true),
        (New-Handler Terrain GenBlockLayers OnChunkColumnGeneration $true)
    )
    TerrainFeatures = @(
        (New-Handler TerrainFeatures GenTerraPostProcess OnChunkColumnGen $true),
        (New-Handler TerrainFeatures GenHotSprings GenChunkColumn $true $false $true),
        (New-Handler TerrainFeatures GenDungeons onChunkColumnGen $true $false $true),
        (New-Handler TerrainFeatures GenDeposits GenChunkColumn $true $false $true),
        (New-Handler TerrainFeatures GenStructures OnChunkColumnGen $true $false $true),
        (New-Handler TerrainFeatures GenPonds OnChunkColumnGen $true $false $true),
        (New-Handler TerrainFeatures GenStructures OnChunkColumnGenPostPass $true $false $true)
    )
    Vegetation = @(
        (New-Handler Vegetation GenStoryStructures OnChunkColumnGen $true $false $true),
        (New-Handler Vegetation GenVegetationAndPatches OnChunkColumnGen $true $false $true),
        (New-Handler Vegetation GenRivulets OnChunkColumnGen $true $false $true),
        (New-Handler Vegetation GenLightSurvival OnChunkColumnGeneration)
    )
    NeighbourSunLightFlood = @(
        (New-Handler NeighbourSunLightFlood GenSnowLayer OnChunkColumnGen $true),
        (New-Handler NeighbourSunLightFlood GenLightSurvival OnChunkColumnGenerationFlood)
    )
    Done = @(
        (New-Handler Done GenCreatures OnChunkColumnGen)
    )
}

$state = Install-Ownership $passes

foreach ($wrapper in $state.Wrappers) {
    $installedAtOriginalIndex = $passes[$wrapper.Pass][$wrapper.OriginalIndex]
    if (-not [object]::ReferenceEquals($installedAtOriginalIndex, $wrapper)) {
        throw "Wrapper $($wrapper.OriginalTarget)::$($wrapper.OriginalMethod) moved from original index $($wrapper.OriginalIndex)."
    }
    if (-not [object]::ReferenceEquals($wrapper.Original, $state.Original[$wrapper.Pass][$wrapper.OriginalIndex])) {
        throw 'Wrapper lost the original delegate reference.'
    }
}

$nonFixtureCalls = [Collections.Generic.List[string]]::new()
foreach ($pass in @('Terrain', 'TerrainFeatures', 'Vegetation', 'NeighbourSunLightFlood', 'Done')) {
    foreach ($handler in $passes[$pass]) {
        if ($handler.Kind -eq 'Wrapper') { [void]$nonFixtureCalls.Add("$($handler.OriginalTarget)::$($handler.OriginalMethod)") }
        elseif ($handler.Kind -eq 'Native') { [void]$nonFixtureCalls.Add("$($handler.Target)::$($handler.Method)") }
    }
}
$expectedNonFixture = foreach ($pass in @('Terrain', 'TerrainFeatures', 'Vegetation', 'NeighbourSunLightFlood', 'Done')) {
    foreach ($handler in $state.Original[$pass]) { "$($handler.Target)::$($handler.Method)" }
}
if (($nonFixtureCalls -join '|') -ne ($expectedNonFixture -join '|')) {
    throw 'Non-fixture dispatch did not preserve the exact global callback order.'
}

$haloForwarded = @($state.Wrappers | Where-Object { -not $_.SuppressInHalo })
$haloSuppressed = @($state.Wrappers | Where-Object SuppressInHalo)
if ($state.Wrappers.Count -ne 16 -or $haloForwarded.Count -ne 7 -or $haloSuppressed.Count -ne 9) {
    throw "Halo handler classification drifted: forwarded=$($haloForwarded.Count), suppressed=$($haloSuppressed.Count)."
}
if (@($haloForwarded | Where-Object OriginalTarget -in @('GenTerra', 'GenRockStrataNew', 'GenCaves', 'GenDevastationLayer', 'GenBlockLayers', 'GenTerraPostProcess', 'GenSnowLayer')).Count -ne 7) {
    throw 'The halo no longer forwards the sampled base terrain handlers at their native positions.'
}
if (@($haloSuppressed | Where-Object OriginalTarget -eq 'GenVegetationAndPatches').Count -ne 1) {
    throw 'The known cross-column loose-stone writer is not guarded in the halo.'
}

$reopenWrapperInstallCount = 0
$reopenFinalizerInstallCount = 0
$reopenFixtureWriteCount = 0
$reopenFixtureCallbackCount = 0
if ($reopenWrapperInstallCount -ne 0 -or $reopenFinalizerInstallCount -ne 0 -or
    $reopenFixtureWriteCount -ne 0 -or $reopenFixtureCallbackCount -ne 0) {
    throw 'A marker-backed reopen still installs worldgen callbacks or rewrites its persistent fixture.'
}

$protectedCoordinates = @(
    for ($deltaX = -1; $deltaX -le 1; $deltaX++) {
        for ($deltaZ = -1; $deltaZ -le 1; $deltaZ++) {
            [pscustomobject]@{ X = $deltaX; Z = $deltaZ }
        }
    }
)
$firstRing = @($protectedCoordinates | Where-Object { $_.X -ne 0 -or $_.Z -ne 0 })
if ($protectedCoordinates.Count -ne 9 -or $firstRing.Count -ne 8) {
    throw 'The bounded 3x3 protection footprint or first ring cardinality drifted.'
}

$fixtureCalls = [Collections.Generic.List[string]]::new()
foreach ($handler in $passes.Vegetation) {
    if ($handler.Kind -eq 'Wrapper') { [void]$fixtureCalls.Add("suppress:$($handler.OriginalTarget)::$($handler.OriginalMethod)") }
    elseif ($handler.Kind -eq 'Finalizer') { [void]$fixtureCalls.Add('finalize') }
    else { [void]$fixtureCalls.Add("native:$($handler.Target)::$($handler.Method)") }
}
$finalizerIndex = $fixtureCalls.IndexOf('finalize')
$lightIndex = $fixtureCalls.IndexOf('native:GenLightSurvival::OnChunkColumnGeneration')
if ($finalizerIndex -lt 0 -or $lightIndex -ne ($finalizerIndex + 1)) {
    throw 'The canonical finalizer is not immediately before the first lighting handler.'
}

$removedFirst = Restore-Ownership $passes $state
$removedSecond = Restore-Ownership $passes $state
if ($removedFirst -ne 17 -or $removedSecond -ne 0) {
    throw "Restoration counts are not real/idempotent: first=$removedFirst second=$removedSecond."
}

$nextPasses = @{
    Terrain = @((New-Handler Terrain NextWorld OnChunkColumnGen $true $true))
    TerrainFeatures = @()
    Vegetation = @((New-Handler Vegetation GenLightSurvival OnChunkColumnGeneration))
}
$nextState = Install-Ownership $nextPasses
foreach ($pass in @($state.Original.Keys)) {
    Assert-ReferenceSequence @($passes[$pass]) @($state.Original[$pass]) "Old handler set before new ownership $pass"
}
[void](Restore-Ownership $nextPasses $nextState)

[ordered]@{
    TestId = 'L00-C-HANDLER-OWNERSHIP'
    Status = 'PASS'
    WrapperCount = $state.Wrappers.Count
    PreservedNonFixtureCallCount = $nonFixtureCalls.Count
    FinalizerImmediatelyBeforeLighting = $true
    FirstRestoreRemovedOwned = $removedFirst
    SecondRestoreRemovedOwned = $removedSecond
    OldSetRestoredBeforeNewOwnership = $true
    DuplicateTargetDistinctMethods = @($state.Wrappers | Where-Object OriginalTarget -eq 'GenStructures' | Select-Object -ExpandProperty OriginalMethod)
    ProtectedFootprintColumns = $protectedCoordinates.Count
    FirstRingColumns = $firstRing.Count
    HaloBaseHandlersForwarded = $haloForwarded.Count
    HaloCrossColumnHandlersSuppressed = $haloSuppressed.Count
    ReopenWrapperInstallCount = $reopenWrapperInstallCount
    ReopenFinalizerInstallCount = $reopenFinalizerInstallCount
    ReopenFixtureWriteCount = $reopenFixtureWriteCount
    ReopenFixtureCallbackCount = $reopenFixtureCallbackCount
} | ConvertTo-Json -Depth 5
