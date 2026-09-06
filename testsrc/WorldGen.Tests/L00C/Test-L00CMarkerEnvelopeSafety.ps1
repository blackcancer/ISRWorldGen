[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$readerType = $assembly.GetType('ISRWorldGen.WorldgenProbe.ProbeMarkerEnvelopeReader', $false)
$markerType = $assembly.GetType('ISRWorldGen.WorldgenProbe.ProbeMarker', $false)
$footprintType = $assembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapFootprintSnapshot', $false)
$mapType = $assembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapChunkSnapshot', $false)
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.MarkerPublicationGate', $false)
foreach ($type in @($readerType, $markerType, $footprintType, $mapType, $gateType)) {
    if ($null -eq $type) { throw 'The Debug assembly does not expose every production marker-envelope safety component.' }
}

$read = $readerType.GetMethod('ReadAndValidate')
$maximum = $readerType.GetMethod('ComputeMaximumPayloadBytes')
if ($null -eq $read -or $null -eq $maximum) { throw 'The production marker-envelope reader contract is incomplete.' }

function New-Maps {
    param([int]$Count = 9, [int]$MapLength = 1024)
    $listType = [Collections.Generic.List``1].MakeGenericType($mapType)
    $maps = [Activator]::CreateInstance($listType)
    $added = 0
    for ($x = 31989; $x -le 31991 -and $added -lt $Count; $x++) {
        for ($z = 31989; $z -le 31991 -and $added -lt $Count; $z++) {
            $map = [Activator]::CreateInstance($mapType)
            $mapType.GetProperty('X').SetValue($map, $x)
            $mapType.GetProperty('Z').SetValue($map, $z)
            $mapType.GetProperty('WorldGenTerrainHeightMap').SetValue($map, [ushort[]](1..$MapLength | ForEach-Object { 64 }))
            $mapType.GetProperty('RainHeightMap').SetValue($map, [ushort[]](1..$MapLength | ForEach-Object { 67 }))
            $mapType.GetProperty('TopRockIdMap').SetValue($map, [int[]](1..$MapLength | ForEach-Object { 11165 }))
            $mapType.GetProperty('YMax').SetValue($map, [ushort]67)
            [void]$maps.Add($map)
            $added++
        }
    }
    return ,$maps
}

function New-MarkerPayload {
    $maps = New-Maps
    $footprint = $footprintType.GetMethod('Create').Invoke($null, @('marker-a', 'save-a', 31990, 31990, 32, 256, $maps))
    $marker = [Activator]::CreateInstance($markerType)
    $markerType.GetProperty('MarkerId').SetValue($marker, 'marker-a')
    $markerType.GetProperty('SavegameIdentifier').SetValue($marker, 'save-a')
    $markerType.GetProperty('Version').SetValue($marker, 'l00c-flat-v2-map-snapshot')
    $markerType.GetProperty('OpenCount').SetValue($marker, 1)
    $markerType.GetProperty('MapFootprint').SetValue($marker, $footprint)
    return ,[Text.Json.JsonSerializer]::SerializeToUtf8Bytes($marker, $markerType)
}

function Invoke-Read([byte[]]$Payload) {
    $arguments = [object[]]::new(6)
    $arguments[0] = $Payload
    $arguments[1] = 'save-a'
    $arguments[2] = 31990
    $arguments[3] = 31990
    $arguments[4] = 32
    $arguments[5] = 256
    return $read.Invoke($null, $arguments)
}

function Assert-Rejected([scriptblock]$Action, [string]$Fragment) {
    try { [void](& $Action) }
    catch {
        $observed = $_.Exception
        $messages = [Collections.Generic.List[string]]::new()
        while ($null -ne $observed) {
            $messages.Add($observed.Message)
            $observed = $observed.InnerException
        }
        if (($messages -join ' | ') -notmatch [regex]::Escape($Fragment)) { throw }
        return
    }
    throw "Expected marker-envelope rejection containing '$Fragment'."
}

$validPayload = New-MarkerPayload
if ($validPayload -isnot [byte[]]) { throw "Valid payload type drifted: $($validPayload.GetType().FullName)." }
$maximumBytes = [int]$maximum.Invoke($null, @(32))
if ($validPayload.Length -ge $maximumBytes) { throw 'The calculated marker payload bound does not admit the exact valid envelope.' }
$marker = Invoke-Read $validPayload
if ($null -eq $marker -or $markerType.GetProperty('MarkerId').GetValue($marker) -ne 'marker-a') {
    throw 'The production reader rejected or changed a valid bounded marker envelope.'
}

Assert-Rejected { [void](Invoke-Read ([byte[]]::new($maximumBytes + 1))) } 'exceeds'
Assert-Rejected { [void](Invoke-Read $validPayload[0..($validPayload.Length - 3)]) } 'corrupt'

$shortMarker = Invoke-Read $validPayload
$shortFootprint = $markerType.GetProperty('MapFootprint').GetValue($shortMarker)
$shortMaps = $footprintType.GetProperty('MapChunks').GetValue($shortFootprint)
$shortMaps.RemoveAt($shortMaps.Count - 1)
$shortPayload = [Text.Json.JsonSerializer]::SerializeToUtf8Bytes($shortMarker, $markerType)
Assert-Rejected { [void](Invoke-Read $shortPayload) } 'exactly 9'

$lengthMarker = Invoke-Read $validPayload
$lengthFootprint = $markerType.GetProperty('MapFootprint').GetValue($lengthMarker)
$lengthMaps = $footprintType.GetProperty('MapChunks').GetValue($lengthFootprint)
$mapType.GetProperty('RainHeightMap').SetValue($lengthMaps[0], [ushort[]]@(67))
$lengthPayload = [Text.Json.JsonSerializer]::SerializeToUtf8Bytes($lengthMarker, $markerType)
Assert-Rejected { [void](Invoke-Read $lengthPayload) } 'length'

$gate = [Activator]::CreateInstance($gateType)
$hostileMarker = Invoke-Read $validPayload
$hostileFootprint = $markerType.GetProperty('MapFootprint').GetValue($hostileMarker)
$hostileMaps = $footprintType.GetProperty('MapChunks').GetValue($hostileFootprint)
$mapType.GetProperty('TopRockIdMap').SetValue($hostileMaps[0], [int[]]@(11165))
Assert-Rejected { [void]$gateType.GetMethod('Begin').Invoke($gate, @($hostileMarker)) } 'length'

[ordered]@{
    TestId = 'L00-C-MARKER-ENVELOPE-SAFETY'
    Status = 'PASS'
    ValidPayloadBytes = $validPayload.Length
    MaximumPayloadBytes = $maximumBytes
    HugePayloadRejected = $true
    TruncatedPayloadRejected = $true
    HostileCountRejected = $true
    HostileArrayLengthRejected = $true
    GateValidatedBeforeCopy = $true
} | ConvertTo-Json -Depth 4
