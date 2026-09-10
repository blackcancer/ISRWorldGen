[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$profilePath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L00C\Set-L00CLabProfile.ps1'
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$markerType = $assembly.GetType('ISRWorldGen.WorldgenProbe.L00CLifecycleMarker', $false)
if ($null -eq $markerType) { throw 'The Debug assembly does not expose the isolated lifecycle marker.' }
$create = $markerType.GetMethod('Create', [Reflection.BindingFlags]'Static, Public')
$read = $markerType.GetMethod('ReadAndValidate', [Reflection.BindingFlags]'Static, Public')
$increment = $markerType.GetMethod('IncrementFor', [Reflection.BindingFlags]'Instance, Public')
$serialize = $markerType.GetMethod('Serialize', [Reflection.BindingFlags]'Instance, Public')
if ($null -eq $create -or $null -eq $read -or $null -eq $increment -or $null -eq $serialize) { throw 'The lifecycle marker contract is incomplete.' }

$save = '11111111-1111-1111-1111-111111111111'
$created = $create.Invoke($null, @($save, 1))
$payload = [byte[]]$serialize.Invoke($created, @())
$readBack = $read.Invoke($null, @($payload, $save))
$reopened = $increment.Invoke($readBack, @($save))
if ([int]$markerType.GetProperty('OpenCount').GetValue($created) -ne 1 -or
    [int]$markerType.GetProperty('OpenCount').GetValue($reopened) -ne 2 -or
    [string]$markerType.GetProperty('MarkerId').GetValue($created) -cne [string]$markerType.GetProperty('MarkerId').GetValue($reopened)) {
    throw 'The lifecycle marker did not preserve identity and increment only its own open count.'
}

function Assert-Refused([scriptblock]$Action, [string]$Name) {
    try { & $Action } catch { return }
    throw "Lifecycle marker accepted invalid case: $Name"
}
Assert-Refused { $read.Invoke($null, @([byte[]]@(), $save)) } 'empty-payload'
Assert-Refused { $read.Invoke($null, @($payload, '22222222-2222-2222-2222-222222222222')) } 'copied-to-wrong-save'
Assert-Refused { $read.Invoke($null, @([Text.Encoding]::UTF8.GetBytes('{'), $save)) } 'corrupt-marker'
$duplicate = [Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($payload)).Replace('"OpenCount":1','"OpenCount":1,"OpenCount":1'))
Assert-Refused { $read.Invoke($null, @($duplicate, $save)) } 'duplicate-property'
Assert-Refused { $increment.Invoke($reopened, @($save)) } 'third-open-counter'

$source = Get-Content -LiteralPath $sourcePath -Raw
$initializeStart = $source.IndexOf('private void InitializeWorldCore()', [StringComparison]::Ordinal)
$lifecycleBranch = $source.IndexOf('if (!config.SpatialFixtureEnabled)', $initializeStart, [StringComparison]::Ordinal)
$readSpatialMarker = $source.IndexOf('ReadMarker(saveGame, serverApi.WorldManager)', $initializeStart, [StringComparison]::Ordinal)
$replace = $source.IndexOf('ValidateReplacementPreconditions(handlers)', $initializeStart, [StringComparison]::Ordinal)
if ($lifecycleBranch -lt 0 -or $readSpatialMarker -le $lifecycleBranch -or $replace -le $lifecycleBranch) {
    throw 'InitializeWorldCore no longer routes the lifecycle scenario before all spatial marker and handler work.'
}
$lifecycleStart = $source.IndexOf('private void InitializeLifecycleOnly(', [StringComparison]::Ordinal)
$lifecycleEnd = $source.IndexOf('private static L00CLifecycleMarker? ReadLifecycleMarker(', $lifecycleStart, [StringComparison]::Ordinal)
$lifecycleMethod = $source.Substring($lifecycleStart, $lifecycleEnd - $lifecycleStart)
foreach ($forbidden in @('FixtureChunk', 'MapSize', 'ValidateFixtureCoordinate', 'ApplyTargetedReplacement', 'ResolveMaterials', 'ScheduleProbeColumn', 'Teleport')) {
    if ($lifecycleMethod.Contains($forbidden)) { throw "Lifecycle-only initialization leaked spatial behavior: $forbidden" }
}
foreach ($required in @('LifecycleMarkerKey', 'StoreData', 'ScheduleLifecycleShutdown', 'requests=0', 'handlersowned=False')) {
    if (-not $lifecycleMethod.Contains($required)) { throw "Lifecycle-only initialization lost required contract evidence: $required" }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('isrworldgen-l00c-lifecycle-' + [Guid]::NewGuid().ToString('N'))
try {
    [void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
    $templatePath = Join-Path $temporaryRoot 'template-serverconfig.json'
    [ordered]@{ AdvertiseServer=$true; Upnp=$true; Ip='0.0.0.0'; Port=0; Password='x'; VerifyPlayerAuth=$true; StartupCommands='x'; ServerName='x'; WorldConfig=[ordered]@{ SaveFileLocation='x'; WorldName='x'; Seed='0'; WorldType='standard'; PlayStyle='creativebuilding'; MapSizeX=4096; MapSizeY=192; MapSizeZ=4096 } } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $templatePath -Encoding UTF8
    $default = (& $profilePath -WorldRole 'activated-primary' -TemplateConfigPath $templatePath -RepositoryRoot $temporaryRoot | ConvertFrom-Json)
    $defaultConfig = Get-Content -LiteralPath $default.ProbeConfig -Raw | ConvertFrom-Json
    if ([bool]$defaultConfig.SpatialFixtureEnabled) { throw 'The default L00-C lifecycle profile unexpectedly enables a spatial fixture.' }
    $spatial = (& $profilePath -WorldRole 'activated-primary' -TemplateConfigPath $templatePath -RepositoryRoot $temporaryRoot -EnableSpatialFixture -FixtureChunkX 64 -FixtureChunkZ 64 | ConvertFrom-Json)
    $spatialConfig = Get-Content -LiteralPath $spatial.ProbeConfig -Raw | ConvertFrom-Json
    $spatialServer = Get-Content -LiteralPath $spatial.ServerConfig -Raw | ConvertFrom-Json
    if (-not [bool]$spatialConfig.SpatialFixtureEnabled -or [int]$spatialConfig.FixtureChunkX -ne 64 -or [int]$spatialConfig.FixtureChunkZ -ne 64 -or [int]$spatialServer.WorldConfig.MapSizeX -ne 4096 -or [int]$spatialServer.WorldConfig.MapSizeZ -ne 4096) {
        throw 'The spatial opt-in did not remain explicit and bound to the caller-provided map extent.'
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}

[ordered]@{
    TestId = 'L00-C-LIFECYCLE-SPATIAL-ISOLATION'
    Status = 'PASS'
    LifecycleMarkerOpenCounts = '1->2'
    LifecycleSpatialCalls = 0
    LifecycleTeleportCalls = 0
    LifecycleProfileSpatialDefault = $false
    SpatialBoundsRemainFailClosed = $true
    Scope = 'Debug assembly and disposable temporary profiles only; no Visual Studio, game process, AppData, F5, coordinates, or teleportation.'
} | ConvertTo-Json -Depth 4
