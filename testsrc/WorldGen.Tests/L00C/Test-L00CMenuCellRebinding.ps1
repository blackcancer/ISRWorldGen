[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Retired S1 oracle: production no longer rebinds GuiScreenSingleplayer cells.
# Its replacement directly audits the immutable StartServerArgs path mapping and
# executes the full S2/S3 5 x 3 scheduler composition.
$native = & (Join-Path $PSScriptRoot 'Test-L00CNativeFixtureOracle.ps1') -GamePath $GamePath | ConvertFrom-Json
$composition = & (Join-Path $PSScriptRoot 'Test-L00CScenarioComposition.ps1') -RepositoryRoot $RepositoryRoot | ConvertFrom-Json
if ($native.Status -ne 'PASS' -or $composition.Status -ne 'PASS') { throw 'L00-C direct-path replacement gates did not pass.' }

[ordered]@{
    TestId = 'L00-C-MENU-CELL-REBINDING-RETIRED'
    Status = 'PASS'
    Replacement = @($native.TestId, $composition.TestId)
    Contract = 'No menu cell/index is retained or rebound; canonical StartServerArgs paths drive ten saves and fifteen sessions.'
    Scope = 'Legacy oracle retirement backed by stronger direct-path and controlled-composition tests; no game process or T00-06 PASS claim.'
} | ConvertTo-Json -Depth 4 -Compress
