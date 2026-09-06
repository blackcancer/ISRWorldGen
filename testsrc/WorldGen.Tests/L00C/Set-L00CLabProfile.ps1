[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('disabled-witness', 'activated-primary', 'activated-secondary', 'missing-handler')]
    [string]$WorldRole,

    [Parameter(Mandatory = $true)]
    [string]$TemplateConfigPath,

    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$SeedSavePath,
    [switch]$AllowExisting,
    [ValidateScript({ $_ -eq 0 -or ($_ -ge 50 -and $_ -le 60000) })]
    [int]$AutoShutdownDelayMilliseconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $TemplateConfigPath -PathType Leaf)) {
    throw "Template server config is missing: $TemplateConfigPath"
}

$localRoot = Join-Path $RepositoryRoot '.local\L00C'
$dataPath = Join-Path $RepositoryRoot '.local\VintagestoryData'
$saveDirectory = Join-Path $localRoot 'saves'
$modConfigDirectory = Join-Path $dataPath 'ModConfig'
$roleFileName = switch ($WorldRole) {
    'disabled-witness' { 'disabled-witness.vcdbs' }
    'activated-primary' { 'activated-primary.vcdbs' }
    'activated-secondary' { 'activated-secondary.vcdbs' }
    'missing-handler' { 'missing-handler.vcdbs' }
}
$savePath = Join-Path $saveDirectory $roleFileName

foreach ($directory in @($localRoot, $dataPath, $saveDirectory, $modConfigDirectory)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }
}

$labMarker = Join-Path $localRoot '.isrworldgen-lab'
if (-not (Test-Path -LiteralPath $labMarker -PathType Leaf)) {
    Set-Content -LiteralPath $labMarker -Value 'L00-C disposable laboratory data only' -Encoding UTF8
}

if ($SeedSavePath) {
    if (-not (Test-Path -LiteralPath $SeedSavePath -PathType Leaf)) {
        throw "Seed save is missing: $SeedSavePath"
    }
    if (Test-Path -LiteralPath $savePath) {
        throw "Refusing to overwrite existing lab save: $savePath"
    }
    Copy-Item -LiteralPath $SeedSavePath -Destination $savePath
}
elseif ((Test-Path -LiteralPath $savePath) -and -not $AllowExisting) {
    throw "Lab save already exists; pass -AllowExisting only for an intentional reload cycle: $savePath"
}

$serverConfig = Get-Content -LiteralPath $TemplateConfigPath -Raw | ConvertFrom-Json
$serverConfig.AdvertiseServer = $false
$serverConfig.Upnp = $false
$serverConfig.Ip = '127.0.0.1'
$serverConfig.Port = 45100
$serverConfig.Password = ''
$serverConfig.VerifyPlayerAuth = $false
$serverConfig.StartupCommands = ''
$serverConfig.ServerName = 'ISRWorldGen L00-C isolated lab'
$serverConfig.WorldConfig.SaveFileLocation = $savePath
$serverConfig.WorldConfig.WorldName = "ISRWorldGen L00-C $WorldRole"
$serverConfig.WorldConfig.Seed = '24681357'
$serverConfig.WorldConfig.WorldType = 'standard'
$serverConfig.WorldConfig.PlayStyle = 'surviveandbuild'
$serverConfig.WorldConfig.MapSizeY = 256

$serverConfigPath = Join-Path $dataPath 'serverconfig.json'
$serverConfig | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $serverConfigPath -Encoding UTF8

$probeConfig = [ordered]@{
    Enabled = ($WorldRole -ne 'disabled-witness')
    AutoRun = ($WorldRole -ne 'missing-handler')
    AutoShutdown = $true
    AutoShutdownDelayMilliseconds = $AutoShutdownDelayMilliseconds
    FixtureChunkX = 31990
    FixtureChunkZ = 31990
    ExpectedMissingHandlerTarget = if ($WorldRole -eq 'missing-handler') { 'Vintagestory.ServerMods.IntentionallyAbsentL00C' } else { $null }
}
$probeConfigPath = Join-Path $modConfigDirectory 'isrworldgen-l00c.json'
$probeConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $probeConfigPath -Encoding UTF8

[ordered]@{
    Status = 'READY'
    WorldRole = $WorldRole
    DataPath = $dataPath
    SavePath = $savePath
    SaveExists = Test-Path -LiteralPath $savePath -PathType Leaf
    ExistingCycle = [bool]$AllowExisting
    LabMarker = $labMarker
    ServerConfig = $serverConfigPath
    ProbeConfig = $probeConfigPath
    ClientLaunchPermitted = $false
} | ConvertTo-Json -Depth 5
