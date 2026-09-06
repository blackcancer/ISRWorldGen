[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-Handler {
    param([string]$Pass, [string]$Target, [string]$Method, [bool]$Targeted = $false, [bool]$Writer = $false)

    return [pscustomobject]@{
        Kind = 'Native'
        Pass = $Pass
        Target = $Target
        Method = $Method
        Targeted = $Targeted
        Writer = $Writer
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
        (New-Handler Terrain L00B OnChunkColumnGeneration),
        (New-Handler Terrain GenCaves GenChunkColumn $true),
        (New-Handler Terrain ThirdParty Tail)
    )
    TerrainFeatures = @(
        (New-Handler TerrainFeatures GenStructures OnChunkColumnGen $true),
        (New-Handler TerrainFeatures GenPonds OnChunkColumnGen $true),
        (New-Handler TerrainFeatures GenStructures OnChunkColumnGenPostPass $true)
    )
    Vegetation = @(
        (New-Handler Vegetation GenStoryStructures OnChunkColumnGen $true),
        (New-Handler Vegetation GenVegetationAndPatches OnChunkColumnGen $true),
        (New-Handler Vegetation GenLightSurvival OnChunkColumnGeneration)
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
foreach ($pass in @('Terrain', 'TerrainFeatures', 'Vegetation')) {
    foreach ($handler in $passes[$pass]) {
        if ($handler.Kind -eq 'Wrapper') { [void]$nonFixtureCalls.Add("$($handler.OriginalTarget)::$($handler.OriginalMethod)") }
        elseif ($handler.Kind -eq 'Native') { [void]$nonFixtureCalls.Add("$($handler.Target)::$($handler.Method)") }
    }
}
$expectedNonFixture = foreach ($pass in @('Terrain', 'TerrainFeatures', 'Vegetation')) {
    foreach ($handler in $state.Original[$pass]) { "$($handler.Target)::$($handler.Method)" }
}
if (($nonFixtureCalls -join '|') -ne ($expectedNonFixture -join '|')) {
    throw 'Non-fixture dispatch did not preserve the exact global callback order.'
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
if ($removedFirst -ne 8 -or $removedSecond -ne 0) {
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
} | ConvertTo-Json -Depth 5
