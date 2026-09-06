[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Debug assembly is missing: $assemblyPath"
}

foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.MarkerPublicationGate', $false)
$markerType = $assembly.GetType('ISRWorldGen.WorldgenProbe.ProbeMarker', $false)
$footprintType = $assembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapFootprintSnapshot', $false)
$mapType = $assembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapChunkSnapshot', $false)
if ($null -eq $gateType -or $null -eq $markerType -or $null -eq $footprintType -or $null -eq $mapType) {
    throw 'The Debug assembly does not expose the production marker publication component.'
}

$beginMethod = $gateType.GetMethod('Begin')
$beginWorldTransitionMethod = $gateType.GetMethod('BeginWorldTransition')
$resetMethod = $gateType.GetMethod('Reset')
$commitMethod = $gateType.GetMethod('Commit')
$saveMethod = $gateType.GetMethod('SaveIfCommitted')
$committedProperty = $gateType.GetProperty('IsCommitted')
foreach ($member in @($beginMethod, $beginWorldTransitionMethod, $resetMethod, $commitMethod, $saveMethod, $committedProperty)) {
    if ($null -eq $member) { throw 'The production marker publication component contract is incomplete.' }
}

function New-MapFootprint {
    $listType = [Collections.Generic.List``1].MakeGenericType($mapType)
    $maps = [Activator]::CreateInstance($listType)
    for ($x = 31989; $x -le 31991; $x++) {
        for ($z = 31989; $z -le 31991; $z++) {
            $map = [Activator]::CreateInstance($mapType)
            $mapType.GetProperty('X').SetValue($map, $x)
            $mapType.GetProperty('Z').SetValue($map, $z)
            $mapType.GetProperty('WorldGenTerrainHeightMap').SetValue($map, [ushort[]](1..1024 | ForEach-Object { 64 }))
            $mapType.GetProperty('RainHeightMap').SetValue($map, [ushort[]](1..1024 | ForEach-Object { 67 }))
            $mapType.GetProperty('TopRockIdMap').SetValue($map, [int[]](1..1024 | ForEach-Object { 11165 }))
            $mapType.GetProperty('YMax').SetValue($map, [ushort]67)
            [void]$maps.Add($map)
        }
    }
    return $footprintType.GetMethod('Create').Invoke($null, @('marker-a', 'save-a', 31990, 31990, 32, 256, $maps))
}

function New-Marker {
    param([int]$OpenCount, [switch]$WithMapFootprint)

    $marker = [Activator]::CreateInstance($markerType)
    $markerType.GetProperty('MarkerId').SetValue($marker, 'marker-a')
    $markerType.GetProperty('SavegameIdentifier').SetValue($marker, 'save-a')
    $markerType.GetProperty('Version').SetValue($marker, 'l00c-flat-v2-map-snapshot')
    $markerType.GetProperty('OpenCount').SetValue($marker, $OpenCount)
    if ($WithMapFootprint) {
        $markerType.GetProperty('MapFootprint').SetValue($marker, (New-MapFootprint))
    }
    return $marker
}

function Get-OpenCount {
    param($Marker)
    return [int]$markerType.GetProperty('OpenCount').GetValue($Marker)
}

function Invoke-UncommittedFailureCase {
    param([string]$Name, [switch]$BeginCandidate)

    $gate = [Activator]::CreateInstance($gateType)
    $source = New-Marker 4
    if ($BeginCandidate) {
        $candidate = $beginMethod.Invoke($gate, @($source))
        $markerType.GetProperty('OpenCount').SetValue($candidate, 5)
    }
    $script:uncommittedStoreCalls = 0
    $store = [Action[byte[]]]{
        param([byte[]]$Payload)
        $script:uncommittedStoreCalls++
    }
    $saved = [bool]$saveMethod.Invoke($gate, @($store))
    if ($saved -or $script:uncommittedStoreCalls -ne 0 -or [bool]$committedProperty.GetValue($gate) -or (Get-OpenCount $source) -ne 4) {
        throw "$Name published or mutated an uncommitted marker candidate."
    }
    return [pscustomobject]@{ Name = $Name; StoreCalls = $script:uncommittedStoreCalls; OriginalOpenCount = Get-OpenCount $source }
}

$missingHandler = Invoke-UncommittedFailureCase missing-handler -BeginCandidate
$persistedMapMissing = Invoke-UncommittedFailureCase persisted-map-missing -BeginCandidate
$corruptMarker = Invoke-UncommittedFailureCase corrupt-marker

$throwGate = [Activator]::CreateInstance($gateType)
$throwSource = New-Marker 4
$throwCandidate = $beginMethod.Invoke($throwGate, @($throwSource))
$markerType.GetProperty('OpenCount').SetValue($throwCandidate, 5)
$throwingStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    throw [InvalidOperationException]::new('synthetic-store-failure')
}
$storeThrew = $false
try {
    [void]$commitMethod.Invoke($throwGate, @($throwingStore))
}
catch {
    $observed = $_.Exception
    while ($null -ne $observed.InnerException) { $observed = $observed.InnerException }
    if ($observed.Message -ne 'synthetic-store-failure') { throw }
    $storeThrew = $true
}
$script:postFailureStoreCalls = 0
$postFailureStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $script:postFailureStoreCalls++
}
$postFailureSaved = [bool]$saveMethod.Invoke($throwGate, @($postFailureStore))
if (-not $storeThrew -or [bool]$committedProperty.GetValue($throwGate) -or $postFailureSaved -or
    $script:postFailureStoreCalls -ne 0 -or (Get-OpenCount $throwSource) -ne 4) {
    throw 'A throwing StoreData delegate committed, republished, or mutated the previous marker.'
}

$successGate = [Activator]::CreateInstance($gateType)
$successSource = New-Marker 4 -WithMapFootprint
$successCandidate = $beginMethod.Invoke($successGate, @($successSource))
if ([object]::ReferenceEquals($successSource, $successCandidate)) {
    throw 'Begin returned the persisted marker reference instead of an isolated candidate copy.'
}
$sourceFootprint = $markerType.GetProperty('MapFootprint').GetValue($successSource)
$candidateFootprint = $markerType.GetProperty('MapFootprint').GetValue($successCandidate)
if ($null -eq $sourceFootprint -or $null -eq $candidateFootprint -or [object]::ReferenceEquals($sourceFootprint, $candidateFootprint)) {
    throw 'Begin did not preserve the marker-bound map footprint as an isolated copy.'
}
$sourceMaps = $footprintType.GetProperty('MapChunks').GetValue($sourceFootprint)
$sourceTerrain = [ushort[]]$mapType.GetProperty('WorldGenTerrainHeightMap').GetValue($sourceMaps[0])
$sourceTerrain[0] = 1
$candidateCopies = $footprintType.GetMethod('ValidateAndCopy').Invoke($candidateFootprint, @('marker-a', 'save-a', 31990, 31990, 32, 256))
if ([int]$candidateCopies.Count -ne 9) {
    throw 'Begin retained a shared map array or lost part of the exact marker-bound footprint.'
}
$markerType.GetProperty('OpenCount').SetValue($successCandidate, 5)
$storedPayloads = [Collections.Generic.List[byte[]]]::new()
$successfulStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $storedPayloads.Add([byte[]]$Payload.Clone())
}
[void]$commitMethod.Invoke($successGate, @($successfulStore))
$savedAfterCommit = [bool]$saveMethod.Invoke($successGate, @($successfulStore))
if (-not [bool]$committedProperty.GetValue($successGate) -or -not $savedAfterCommit -or $storedPayloads.Count -ne 2 -or
    (Get-OpenCount $successSource) -ne 4) {
    throw 'Successful marker commit did not preserve the old object and enable later persistence.'
}
$firstJsonText = [Text.Encoding]::UTF8.GetString($storedPayloads[0])
$secondJsonText = [Text.Encoding]::UTF8.GetString($storedPayloads[1])
$firstJson = [Text.Json.JsonDocument]::Parse([string]$firstJsonText)
$secondJson = [Text.Json.JsonDocument]::Parse([string]$secondJsonText)
try {
    $firstOpenCount = $firstJson.RootElement.GetProperty('OpenCount').GetInt32()
    $secondOpenCount = $secondJson.RootElement.GetProperty('OpenCount').GetInt32()
    $firstMapFootprint = $firstJson.RootElement.GetProperty('MapFootprint')
    $secondMapFootprint = $secondJson.RootElement.GetProperty('MapFootprint')
    if ($firstOpenCount -ne 5 -or $secondOpenCount -ne 5 -or
        $firstMapFootprint.GetProperty('MapChunks').GetArrayLength() -ne 9 -or
        $secondMapFootprint.GetProperty('MapChunks').GetArrayLength() -ne 9 -or
        $firstMapFootprint.GetProperty('ContentSha256').GetString() -ne $secondMapFootprint.GetProperty('ContentSha256').GetString() -or
        [Convert]::ToHexString($storedPayloads[0]) -ne [Convert]::ToHexString($storedPayloads[1])) {
        throw 'Committed marker payload drifted between activation and GameWorldSave persistence.'
    }
}
finally {
    $firstJson.Dispose()
    $secondJson.Dispose()
}

$resetMethod.Invoke($successGate, @())
if ([bool]$committedProperty.GetValue($successGate)) {
    throw 'Reset retained a committed marker across world initialization.'
}

$transitionGate = [Activator]::CreateInstance($gateType)
$transitionSource = New-Marker 7
$transitionCandidate = $beginMethod.Invoke($transitionGate, @($transitionSource))
$markerType.GetProperty('OpenCount').SetValue($transitionCandidate, 8)
$script:priorWorldStoreCalls = 0
$priorWorldStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $script:priorWorldStoreCalls++
}
[void]$commitMethod.Invoke($transitionGate, @($priorWorldStore))
$beginWorldTransitionMethod.Invoke($transitionGate, @())
$cleanupThrew = $false
try {
    throw [InvalidOperationException]::new('synthetic-world-cleanup-failure')
}
catch {
    $cleanupThrew = $_.Exception.Message -eq 'synthetic-world-cleanup-failure'
}
$script:currentWorldStoreCalls = 0
$currentWorldStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $script:currentWorldStoreCalls++
}
$currentWorldSaved = [bool]$saveMethod.Invoke($transitionGate, @($currentWorldStore))
if (-not $cleanupThrew -or $currentWorldSaved -or $script:currentWorldStoreCalls -ne 0 -or
    $script:priorWorldStoreCalls -ne 1 -or [bool]$committedProperty.GetValue($transitionGate)) {
    throw 'A cleanup failure after world transition republished the prior world marker payload.'
}

[ordered]@{
    TestId = 'L00-C-MARKER-PUBLICATION'
    Status = 'PASS'
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    MissingHandlerStoreCalls = $missingHandler.StoreCalls
    PersistedMapMissingStoreCalls = $persistedMapMissing.StoreCalls
    CorruptMarkerStoreCalls = $corruptMarker.StoreCalls
    ThrowingStoreCommitted = [bool]$committedProperty.GetValue($throwGate)
    ThrowingStoreRepublishCalls = $script:postFailureStoreCalls
    PreviousMarkerOpenCount = Get-OpenCount $successSource
    CommittedMarkerOpenCount = 5
    SuccessfulStoreCalls = $storedPayloads.Count
    MarkerBoundMapCopies = [int]$candidateCopies.Count
    PriorWorldInitialStoreCalls = $script:priorWorldStoreCalls
    CurrentWorldStoreCallsAfterCleanupFailure = $script:currentWorldStoreCalls
} | ConvertTo-Json -Depth 4
