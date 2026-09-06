[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$labRoot = Join-Path $repoRoot '.local\VintagestoryData'
$marker = Join-Path $labRoot '.isrworldgen-lab'

New-Item -ItemType Directory -Path $labRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $labRoot 'Mods') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $labRoot 'Saves') -Force | Out-Null

if (-not (Test-Path -LiteralPath $marker)) {
    New-Item -ItemType File -Path $marker | Out-Null
}

Write-Output "ISRWorldGen isolated data path: $labRoot"
