[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The S1 two-save/menu-cell proof of concept is superseded by the immutable S2
# native-path oracle plus the S4 composition oracle. Keep this entrypoint so
# older aggregate gates execute the stronger replacement instead of silently
# dropping coverage.
$native = & (Join-Path $PSScriptRoot 'Test-L00CNativeFixtureOracle.ps1') -GamePath $GamePath | ConvertFrom-Json
$composition = & (Join-Path $PSScriptRoot 'Test-L00CScenarioComposition.ps1') -RepositoryRoot $RepositoryRoot | ConvertFrom-Json
if ($native.Status -ne 'PASS' -or $composition.Status -ne 'PASS') { throw 'L00-C immutable menu-action replacement gates did not pass.' }

[ordered]@{
    TestId = 'L00-C-MENU-ACTION-POC-RETIRED'
    Status = 'PASS'
    Replacement = @($native.TestId, $composition.TestId)
    Contract = 'Direct canonical StartServerArgs create/reopen and native SaveQuit; exact 5 iterations, 15 sessions, 10 paths; SaveCommitted gated by server release.'
    Scope = 'Static installed-library audit and deterministic temporary-files composition only; no Vintage Story process, F5, or T00-06 PASS claim.'
} | ConvertTo-Json -Depth 4
