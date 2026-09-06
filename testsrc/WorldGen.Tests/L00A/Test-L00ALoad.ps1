[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [Parameter(Mandatory = $true)]
    [string]$LogPath,

    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen"
$assemblyPath = Join-Path $packageRoot 'ISRWorldGen.dll'
$coreAssemblyPath = Join-Path $packageRoot 'ISRWorldGen.Core.dll'
$modInfoPath = Join-Path $packageRoot 'modinfo.json'

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "Game log not found: $LogPath"
}

if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Packaged assembly not found: $assemblyPath"
}

if (-not (Test-Path -LiteralPath $coreAssemblyPath -PathType Leaf)) {
    throw "Packaged core assembly not found: $coreAssemblyPath"
}

if (-not (Test-Path -LiteralPath $modInfoPath -PathType Leaf)) {
    throw "Packaged modinfo not found: $modInfoPath"
}

$allPackagedAssemblies = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter '*.dll')
$expectedAssemblyNames = @('ISRWorldGen.Core.dll', 'ISRWorldGen.dll')
$actualAssemblyNames = @($allPackagedAssemblies | ForEach-Object Name | Sort-Object)
if ($actualAssemblyNames.Count -ne $expectedAssemblyNames.Count -or
    (Compare-Object -ReferenceObject $expectedAssemblyNames -DifferenceObject $actualAssemblyNames).Count -ne 0) {
    throw "Expected exactly ISRWorldGen.dll and ISRWorldGen.Core.dll; found: $($actualAssemblyNames -join ', ')."
}

$log = Get-Content -LiteralPath $LogPath -Raw
$bootstrapMatches = @([regex]::Matches($log, 'L00A_BOOTSTRAP modid=isrworldgen instance=(?<instance>\d+) .*?assembly=ISRWorldGen\.dll sha256=(?<sha>[A-F0-9]{64}) runtime=(?<runtime>\S+) architecture=(?<architecture>\S+)'))
$readyMatches = @([regex]::Matches($log, 'L00A_SERVER_READY modid=isrworldgen instance=(?<instance>\d+)'))

if ($bootstrapMatches.Count -ne 1) {
    throw "Expected one L00A_BOOTSTRAP marker, found $($bootstrapMatches.Count)."
}

if ($readyMatches.Count -ne 1) {
    throw "Expected one L00A_SERVER_READY marker, found $($readyMatches.Count)."
}

if ($bootstrapMatches[0].Groups['instance'].Value -ne '1' -or $readyMatches[0].Groups['instance'].Value -ne '1') {
    throw 'The active ModSystem instance was not instance 1.'
}

$loadErrors = @([regex]::Matches($log, '(?im)^.*(?:Could not load file or assembly|ReflectionTypeLoadException|Failed to load.*isrworldgen|isrworldgen.*(?:load|reference).*(?:error|failed)).*$'))
if ($loadErrors.Count -ne 0) {
    throw "Detected $($loadErrors.Count) ISRWorldGen load/reference error(s)."
}

$actualHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
$coreHash = (Get-FileHash -LiteralPath $coreAssemblyPath -Algorithm SHA256).Hash
$loggedHash = $bootstrapMatches[0].Groups['sha'].Value
if ($actualHash -ne $loggedHash) {
    throw "Loaded hash $loggedHash differs from packaged hash $actualHash."
}

$modInfo = Get-Content -LiteralPath $modInfoPath -Raw | ConvertFrom-Json
if ($modInfo.modid -ne 'isrworldgen' -or $modInfo.name -ne 'ISRWorldGen') {
    throw "Unexpected package identity: $($modInfo.modid) / $($modInfo.name)"
}

$result = [ordered]@{
    schema_version = 1
    verified_utc = [DateTime]::UtcNow.ToString('o')
    configuration = $Configuration
    modid = $modInfo.modid
    name = $modInfo.name
    packaged_dll_count = $allPackagedAssemblies.Count
    bootstrap_marker_count = $bootstrapMatches.Count
    server_ready_marker_count = $readyMatches.Count
    load_reference_error_count = $loadErrors.Count
    assembly_sha256 = $actualHash
    core_assembly_sha256 = $coreHash
    runtime = $bootstrapMatches[0].Groups['runtime'].Value
    architecture = $bootstrapMatches[0].Groups['architecture'].Value
}

$json = $result | ConvertTo-Json -Depth 5
if ($OutputPath) {
    $parent = Split-Path -Parent $OutputPath
    if ($parent) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8
}

$json
