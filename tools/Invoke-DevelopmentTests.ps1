[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('Development', 'L03BProtocol')][string]$Scope = 'Development',
    [string]$PowerShellPath,
    [switch]$PreflightOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This bootstrap is also parseable by Windows PowerShell 5.1. Only an installed
# PowerShell 7+ Core runs the tests. Never install software or emulate pwsh with 5.1.
function Resolve-PowerShellExecutable {
    param([string]$ExplicitPath)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not [IO.Path]::IsPathRooted($ExplicitPath) -or
            -not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf) -or
            [IO.Path]::GetFileName($ExplicitPath) -notin @('pwsh', 'pwsh.exe')) {
            throw 'PowerShellPath must name an existing absolute pwsh/pwsh.exe path. No fallback for an invalid explicit setting.'
        }
        return (Get-Item -LiteralPath $ExplicitPath).FullName
    }
    $command = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return $command.Source }
    $candidates = @()
    foreach ($root in @($env:ProgramW6432, $env:ProgramFiles)) {
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            $candidates += Join-Path $root 'PowerShell/7/pwsh.exe'
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($HOME)) {
        $name = if ($env:OS -eq 'Windows_NT') { 'pwsh.exe' } else { 'pwsh' }
        $candidates += Join-Path $HOME ".dotnet/tools/$name"
    }
    $candidates += '/opt/microsoft/powershell/7/pwsh', '/usr/bin/pwsh', '/usr/local/bin/pwsh'
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }
    throw 'PowerShell 7 (pwsh) is unavailable. Install Microsoft.PowerShell, restart Visual Studio, or pass -PowerShellPath with its absolute path. No tests were run.'
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pwsh = Resolve-PowerShellExecutable -ExplicitPath $PowerShellPath
$project = if ($Scope -eq 'L03BProtocol') {
    Join-Path $repository 'testsrc/WorldGen.L03B.Protocol.Tests/WorldGen.L03B.Protocol.Tests.csproj'
} else {
    Join-Path $repository 'testsrc/WorldGen.Tests/WorldGen.Tests.csproj'
}
$settings = Join-Path $repository 'tests/Development.runsettings'
foreach ($inputPath in @($project, $settings)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Missing test input: $inputPath" }
}

# Child processes (including the timeout test's grandchild) inherit this PATH.
# Restore the caller's process environment; never change user/machine settings.
$originalPath = $env:PATH
$separator = [IO.Path]::PathSeparator
$env:PATH = (Split-Path -Parent $pwsh) + $separator + $originalPath
Push-Location $repository
try {
    if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
        $forward = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $PSCommandPath,
            '-Configuration', $Configuration, '-Scope', $Scope, '-PowerShellPath', $pwsh)
        if ($PreflightOnly) { $forward += '-PreflightOnly' }
        & $pwsh @forward
        exit $LASTEXITCODE
    }
    # Validate an explicitly selected executable too; its name alone is not a version check.
    $versionProbe = & $pwsh -NoLogo -NoProfile -NonInteractive -Command 'if ($PSVersionTable.PSEdition -ne "Core" -or $PSVersionTable.PSVersion.Major -lt 7) { exit 7 }; $PSVersionTable.PSVersion.ToString()'
    if ($LASTEXITCODE -ne 0 -or @($versionProbe).Count -ne 1) { throw 'The selected executable is not a usable PowerShell 7+ Core.' }
    foreach ($tool in @('dotnet', 'git')) {
        if ($null -eq (Get-Command $tool -CommandType Application -ErrorAction SilentlyContinue)) {
            throw "Required test tool '$tool' is unavailable. No tests were run."
        }
    }
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'The SDK required by global.json could not be resolved. No tests were run.' }
    if ($PreflightOnly) {
        [ordered]@{
            Status = 'READY_FOR_TEST_INVOCATION'
            Scope = $Scope
            PowerShellPath = $pwsh
            PowerShellVersion = [string]$versionProbe
            DotnetSdk = [string]$sdk
            Project = $project
            Configuration = $Configuration
            Tests = 'NOT_RUN'
            EvidenceCampaign = 'NOT_RUN_USE_DEDICATED_RUNNER'
            NativeGame = 'NOT_RUN'
        } | ConvertTo-Json
        exit 0
    }
    Write-Warning 'DEVELOPMENT RUN ONLY: T03-05/T03-06 publication and independent blind review are NOT_RUN. Use Run-L03BEvidenceS.ps1 for the certified campaign.'
    if ($Scope -eq 'L03BProtocol') {
        Write-Warning 'Portable protocol project: original L03B C# tests and real PowerShell scripts, without Vintage Story assemblies. This is not a full mod build.'
    }
    $arguments = @('test', $project, '--configuration', $Configuration, '--settings', $settings,
        '--logger', 'trx;LogFileName=development.trx', '--results-directory', (Join-Path (Split-Path $project -Parent) 'TestResults'))
    if ($Scope -eq 'L03BProtocol') {
        $arguments += '--filter', 'FullyQualifiedName~ISRWorldGen.Tests.L03B.EvidenceProtocolTests'
    }
    & dotnet @arguments
    exit $LASTEXITCODE
} finally {
    Pop-Location
    $env:PATH = $originalPath
}
