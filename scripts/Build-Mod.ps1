[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'
$arguments = @('build', $projectPath, '--configuration', $Configuration, '--nologo')

if ($NoRestore) {
    $arguments += '--no-restore'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "ISRWorldGen $Configuration build failed with exit code $LASTEXITCODE."
}

$modDirectory = Join-Path $repoRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen"
Write-Output "Mod directory: $modDirectory"
