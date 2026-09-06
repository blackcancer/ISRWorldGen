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
$footprintType = $assembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapFootprintSnapshot', $false)
$mapType = $assembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapChunkSnapshot', $false)
if ($null -eq $footprintType -or $null -eq $mapType) {
    throw 'The Debug assembly does not expose the production persisted-map snapshot component.'
}

$createMethod = $footprintType.GetMethod('Create')
$validateMethod = $footprintType.GetMethod('ValidateAndCopy')
$deepCopyMethod = $footprintType.GetMethod('DeepCopy')
foreach ($member in @($createMethod, $validateMethod, $deepCopyMethod)) {
    if ($null -eq $member) { throw 'The production persisted-map snapshot contract is incomplete.' }
}

function New-MapSnapshot([int]$X, [int]$Z) {
    $map = [Activator]::CreateInstance($mapType)
    $mapType.GetProperty('X').SetValue($map, $X)
    $mapType.GetProperty('Z').SetValue($map, $Z)
    $mapType.GetProperty('WorldGenTerrainHeightMap').SetValue($map, [ushort[]](1..1024 | ForEach-Object { 64 }))
    $mapType.GetProperty('RainHeightMap').SetValue($map, [ushort[]](1..1024 | ForEach-Object { 67 }))
    $mapType.GetProperty('TopRockIdMap').SetValue($map, [int[]](1..1024 | ForEach-Object { 11165 }))
    $mapType.GetProperty('YMax').SetValue($map, [ushort]67)
    return $map
}

function New-MapList([int]$Count = 9) {
    $listType = [Collections.Generic.List``1].MakeGenericType($mapType)
    $list = [Activator]::CreateInstance($listType)
    $added = 0
    for ($x = 31989; $x -le 31991 -and $added -lt $Count; $x++) {
        for ($z = 31989; $z -le 31991 -and $added -lt $Count; $z++) {
            [void]$list.Add((New-MapSnapshot $x $z))
            $added++
        }
    }
    return ,$list
}

function Invoke-Create($Maps) {
    return $createMethod.Invoke($null, @('marker-a', 'save-a', 31990, 31990, 32, 256, $Maps))
}

function Invoke-Validate($Snapshot) {
    return $validateMethod.Invoke($Snapshot, @('marker-a', 'save-a', 31990, 31990, 32, 256))
}

function Assert-InnerFailure([scriptblock]$Action, [string]$Fragment) {
    try {
        & $Action
    }
    catch {
        $observed = $_.Exception
        while ($null -ne $observed.InnerException) { $observed = $observed.InnerException }
        if ($observed.Message -notmatch [regex]::Escape($Fragment)) { throw }
        return
    }
    throw "Expected production snapshot rejection containing '$Fragment'."
}

$sourceMaps = New-MapList
$snapshot = Invoke-Create $sourceMaps
$validated = Invoke-Validate $snapshot
if ([int]$validated.Count -ne 9) {
    throw "Production snapshot returned $($validated.Count) map copies instead of 9."
}

$centerKey = '31990,31990'
$center = $validated[$centerKey]
if ($null -eq $center) { throw 'Production snapshot omitted the center map copy.' }
$terrainProperty = $mapType.GetProperty('WorldGenTerrainHeightMap')
$sourceCenter = $sourceMaps[4]
$sourceTerrain = [ushort[]]$terrainProperty.GetValue($sourceCenter)
$validatedTerrain = [ushort[]]$terrainProperty.GetValue($center)
$sourceTerrain[0] = 1
if ($validatedTerrain[0] -ne 64) { throw 'Create retained the caller heightmap array instead of a bounded copy.' }
$validatedTerrain[1] = 2
$secondValidation = Invoke-Validate $snapshot
$secondTerrain = [ushort[]]$terrainProperty.GetValue($secondValidation[$centerKey])
if ($secondTerrain[1] -ne 64) { throw 'ValidateAndCopy exposed the persisted heightmap array by reference.' }

$deepCopy = $deepCopyMethod.Invoke($snapshot, @())
$deepValidated = Invoke-Validate $deepCopy
if ([int]$deepValidated.Count -ne 9) { throw 'DeepCopy lost a persisted map column.' }

Assert-InnerFailure { [void](Invoke-Create (New-MapList 8)) } 'exactly 9'

$nullMaps = New-MapList 8
[void]$nullMaps.Add($null)
Assert-InnerFailure { [void](Invoke-Create $nullMaps) } 'null map chunk'

$duplicateMaps = New-MapList
$mapType.GetProperty('X').SetValue($duplicateMaps[8], 31989)
$mapType.GetProperty('Z').SetValue($duplicateMaps[8], 31989)
Assert-InnerFailure { [void](Invoke-Create $duplicateMaps) } 'duplicate'

$badLengthMaps = New-MapList
$terrainProperty.SetValue($badLengthMaps[0], [ushort[]]@(64))
Assert-InnerFailure { [void](Invoke-Create $badLengthMaps) } 'length'

$outsideMaps = New-MapList
$mapType.GetProperty('X').SetValue($outsideMaps[8], 31992)
Assert-InnerFailure { [void](Invoke-Create $outsideMaps) } 'escapes the exact 3x3 footprint'

$badYMaxMaps = New-MapList
$mapType.GetProperty('YMax').SetValue($badYMaxMaps[0], [ushort]256)
Assert-InnerFailure { [void](Invoke-Create $badYMaxMaps) } 'invalid YMax'

$identitySnapshot = Invoke-Create (New-MapList)
Assert-InnerFailure { [void]$validateMethod.Invoke($identitySnapshot, @('foreign-marker', 'save-a', 31990, 31990, 32, 256)) } 'identity or geometry'

$hashProperty = $footprintType.GetProperty('ContentSha256')
$hashProperty.SetValue($snapshot, '0' * 64)
Assert-InnerFailure { [void](Invoke-Validate $snapshot) } 'checksum'

[ordered]@{
    TestId = 'L00-C-PERSISTED-MAP-SNAPSHOT'
    Status = 'PASS'
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    ExactMapCopies = 9
    HeightmapLength = 1024
    CallerArraysCopied = $true
    ValidationArraysCopied = $true
    NegativeShortFootprint = 'PASS'
    NegativeNullMapChunk = 'PASS'
    NegativeDuplicateCoordinate = 'PASS'
    NegativeArrayLength = 'PASS'
    NegativeOutsideCoordinate = 'PASS'
    NegativeYMax = 'PASS'
    NegativeIdentity = 'PASS'
    NegativeChecksum = 'PASS'
} | ConvertTo-Json -Depth 4
