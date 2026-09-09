[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
$policySourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CFixtureCoordinatePolicy.cs'
$profilePath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L00C\Set-L00CLabProfile.ps1'
foreach ($path in @($assemblyPath, $policySourcePath, $profilePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C fixture-coordinate input is missing: $path" }
}
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$policyType = $assembly.GetType('ISRWorldGen.WorldgenProbe.L00CFixtureCoordinatePolicy', $false)
if ($null -eq $policyType) { throw 'The Debug assembly does not expose the L00-C fixture coordinate policy.' }
$validate = $policyType.GetMethod('Validate', [Reflection.BindingFlags]'Static, NonPublic')
if ($null -eq $validate) { throw 'The fixture coordinate policy does not expose its validation contract.' }

function Invoke-Policy([int]$MapX, [int]$MapZ, [int]$ChunkSize, [int]$FixtureX, [int]$FixtureZ, [int]$Radius = 1) {
    return $validate.Invoke($null, [object[]]@($MapX, $MapZ, $ChunkSize, $FixtureX, $FixtureZ, $Radius))
}

function Assert-Refused([scriptblock]$Action, [string]$Name) {
    try { & $Action } catch { return }
    throw "Fixture coordinate policy accepted invalid case: $Name"
}

$spatialPolicyMapSizeBlocks = 1024000
$chunkSize = 32
$fixtureChunk = 31990
$bounds = Invoke-Policy $spatialPolicyMapSizeBlocks $spatialPolicyMapSizeBlocks $chunkSize $fixtureChunk $fixtureChunk
if ([int]$bounds.MapChunkCountX -ne 32000 -or [int]$bounds.MapChunkCountZ -ne 32000 -or
    [int]$bounds.FixtureChunkX -ne $fixtureChunk -or [int]$bounds.FixtureChunkZ -ne $fixtureChunk -or
    [int]$bounds.ProtectionRadius -ne 1) {
    throw 'The effective laboratory fixture bounds do not preserve the intended chunk coordinate and exact block-to-chunk scale.'
}

$lowerEdge = Invoke-Policy $spatialPolicyMapSizeBlocks $spatialPolicyMapSizeBlocks $chunkSize 1 1
$upperEdge = Invoke-Policy $spatialPolicyMapSizeBlocks $spatialPolicyMapSizeBlocks $chunkSize 31998 31998
if ([int]$lowerEdge.FixtureChunkX -ne 1 -or [int]$upperEdge.FixtureChunkX -ne 31998) {
    throw 'The exact protected interior edges were not accepted.'
}
foreach ($case in @(
    @{ Name='lower-x'; X=0; Z=1; MapX=$spatialPolicyMapSizeBlocks; MapZ=$spatialPolicyMapSizeBlocks; Chunk=$chunkSize; Radius=1 },
    @{ Name='upper-z'; X=1; Z=31999; MapX=$spatialPolicyMapSizeBlocks; MapZ=$spatialPolicyMapSizeBlocks; Chunk=$chunkSize; Radius=1 },
    @{ Name='partial-map-x'; X=1; Z=1; MapX=1023999; MapZ=$spatialPolicyMapSizeBlocks; Chunk=$chunkSize; Radius=1 },
    @{ Name='zero-chunk-size'; X=1; Z=1; MapX=$spatialPolicyMapSizeBlocks; MapZ=$spatialPolicyMapSizeBlocks; Chunk=0; Radius=1 },
    @{ Name='zero-radius'; X=1; Z=1; MapX=$spatialPolicyMapSizeBlocks; MapZ=$spatialPolicyMapSizeBlocks; Chunk=$chunkSize; Radius=0 }
)) {
    Assert-Refused { Invoke-Policy $case.MapX $case.MapZ $case.Chunk $case.X $case.Z $case.Radius } $case.Name
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('isrworldgen-l00c-coordinate-' + [Guid]::NewGuid().ToString('N'))
try {
    [void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
    $templatePath = Join-Path $temporaryRoot 'template-serverconfig.json'
    [ordered]@{
        AdvertiseServer = $true
        Upnp = $true
        Ip = '0.0.0.0'
        Port = 0
        Password = 'template'
        VerifyPlayerAuth = $true
        StartupCommands = 'template'
        ServerName = 'template'
        WorldConfig = [ordered]@{
            SaveFileLocation = 'template.vcdbs'
            WorldName = 'template'
            Seed = '0'
            WorldType = 'standard'
            PlayStyle = 'creativebuilding'
            MapSizeY = 192
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $templatePath -Encoding UTF8
    $profileResult = & $profilePath -WorldRole 'disabled-witness' -TemplateConfigPath $templatePath -RepositoryRoot $temporaryRoot
    $profile = $profileResult | ConvertFrom-Json
    if ($profile.Status -cne 'READY' -or $profile.ClientLaunchPermitted) { throw 'The synthetic lab profile did not remain client-launch forbidden.' }
    $serverConfig = Get-Content -LiteralPath $profile.ServerConfig -Raw | ConvertFrom-Json
    $probeConfig = Get-Content -LiteralPath $profile.ProbeConfig -Raw | ConvertFrom-Json
    if ([int]$serverConfig.WorldConfig.MapSizeY -ne 256 -or
        [int]$probeConfig.FixtureChunkX -ne $fixtureChunk -or [int]$probeConfig.FixtureChunkZ -ne $fixtureChunk) {
        throw 'The effective generated laboratory config drifted from its bounded coordinate contract.'
    }
    if ([bool]$probeConfig.SpatialFixtureEnabled) {
        throw 'The lifecycle profile unexpectedly enabled the spatial fixture.'
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}

$source = Get-Content -LiteralPath $policySourcePath -Raw
if ($source -notmatch 'MapSizeX|MapSizeZ|chunk-column coordinates|block coordinates' -or $source -match 'MapSizeX /') {
    throw 'The production coordinate policy no longer documents or enforces the block-to-chunk boundary.'
}

[ordered]@{
    TestId = 'L00-C-FIXTURE-COORDINATE-POLICY'
    Status = 'PASS'
    SpatialPolicyMapBlocks = $spatialPolicyMapSizeBlocks
    EffectiveMapChunks = 32000
    FixtureChunk = $fixtureChunk
    AcceptedInteriorEdges = '1,31998'
    InvalidBoundaryCases = 5
    ProfileConfigObserved = $true
    LifecycleProfileIsCoordinateFree = $true
    Scope = 'Debug assembly plus disposable temporary profile fixture only; no Visual Studio, game process, AppData, or F5.'
} | ConvertTo-Json -Depth 4
