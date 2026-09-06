[CmdletBinding()]
param(
    [string]$GamePath = 'D:\Jeux\Vintagestory',

    [string]$TemplateProject,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Get-AssemblyRecord {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path
    $file = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($item.FullName)
    $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($item.FullName)
    $references = [System.Reflection.Assembly]::LoadFile($item.FullName).GetReferencedAssemblies()
    $systemRuntime = $references | Where-Object Name -eq 'System.Runtime' | Select-Object -First 1

    [ordered]@{
        name = $item.Name
        bytes = $item.Length
        file_version = $file.FileVersion
        product_version = $file.ProductVersion
        assembly_version = $assembly.Version.ToString()
        processor_architecture = $assembly.ProcessorArchitecture.ToString()
        system_runtime_reference = if ($null -eq $systemRuntime) { $null } else { $systemRuntime.Version.ToString() }
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
    }
}

$clientPath = Join-Path $GamePath 'Vintagestory.exe'
$serverPath = Join-Path $GamePath 'VintagestoryServer.exe'
$apiPath = Join-Path $GamePath 'VintagestoryAPI.dll'
$libPath = Join-Path $GamePath 'VintagestoryLib.dll'
$essentialsPath = Join-Path $GamePath 'Mods\VSEssentials.dll'
$survivalPath = Join-Path $GamePath 'Mods\VSSurvivalMod.dll'
$runtimeConfigPath = Join-Path $GamePath 'VintagestoryServer.runtimeconfig.json'

$requiredPaths = @(
    $clientPath,
    $serverPath,
    $apiPath,
    $libPath,
    $essentialsPath,
    $survivalPath,
    $runtimeConfigPath
)

foreach ($path in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Vintage Story artifact not found: $path"
    }
}

$clientVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($clientPath)
$serverVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($serverPath)
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$templateTargetFramework = $null

if ($TemplateProject) {
    if (-not (Test-Path -LiteralPath $TemplateProject -PathType Leaf)) {
        throw "Template project not found: $TemplateProject"
    }

    [xml]$templateXml = Get-Content -LiteralPath $TemplateProject -Raw
    $templateTargetFramework = [string]$templateXml.Project.PropertyGroup.TargetFramework
}

$result = [ordered]@{
    schema_version = 1
    collected_utc = [DateTime]::UtcNow.ToString('o')
    game = [ordered]@{
        client_product_version = $clientVersion.ProductVersion
        server_product_version = $serverVersion.ProductVersion
        runtime_tfm = $runtimeConfig.runtimeOptions.tfm
        runtime_framework = $runtimeConfig.runtimeOptions.framework.name
        runtime_version = $runtimeConfig.runtimeOptions.framework.version
    }
    template = [ordered]@{
        target_framework = $templateTargetFramework
    }
    assemblies = @(
        Get-AssemblyRecord -Path $apiPath
        Get-AssemblyRecord -Path $libPath
        Get-AssemblyRecord -Path $essentialsPath
        Get-AssemblyRecord -Path $survivalPath
    )
}

if ($result.game.client_product_version -ne $result.game.server_product_version) {
    throw "Client/server version mismatch: $($result.game.client_product_version) / $($result.game.server_product_version)"
}

if ($result.game.runtime_tfm -ne 'net10.0') {
    throw "Unexpected server runtime target: $($result.game.runtime_tfm)"
}

$apiRecord = $result.assemblies | Where-Object name -eq 'VintagestoryAPI.dll'
if ($apiRecord.system_runtime_reference -ne '10.0.0.0') {
    throw "Unexpected VintagestoryAPI System.Runtime reference: $($apiRecord.system_runtime_reference)"
}

if ($TemplateProject -and $templateTargetFramework -ne 'net7.0') {
    throw "Unexpected installed template target: $templateTargetFramework"
}

$json = $result | ConvertTo-Json -Depth 8
if ($OutputPath) {
    $parent = Split-Path -Parent $OutputPath
    if ($parent) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8
}

$json
