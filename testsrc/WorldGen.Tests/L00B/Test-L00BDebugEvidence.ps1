[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "T00-03 evidence is missing: $EvidencePath"
}

$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
$evidenceDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $EvidencePath).Path

function Assert-Equal {
    param($Actual, $Expected, [string]$Label)
    if ($Actual -ne $Expected) {
        throw "$Label expected '$Expected' but was '$Actual'."
    }
}

Assert-Equal $evidence.TestId 'T00-03' 'TestId'
Assert-Equal $evidence.Status 'PASS' 'Status'
Assert-Equal $evidence.GameVersion '1.22.7' 'GameVersion'
Assert-Equal $evidence.Runtime 'net10.0/.NET 10.0.11' 'Runtime'
Assert-Equal $evidence.Debugger.Transport 'Visual Studio MCP launch' 'Debugger.Transport'
Assert-Equal $evidence.Debugger.StartServerSide.Observed $true 'StartServerSide.Observed'
Assert-Equal $evidence.Debugger.ColumnCallback.Observed $true 'ColumnCallback.Observed'
Assert-Equal $evidence.Debugger.ColumnCallback.CoordinateInspected $true 'ColumnCallback.CoordinateInspected'
Assert-Equal $evidence.Debugger.Exception.Observed $true 'Exception.Observed'
Assert-Equal $evidence.Debugger.Exception.DebugOnly $true 'Exception.DebugOnly'
Assert-Equal $evidence.Debugger.Exception.OffByDefault $true 'Exception.OffByDefault'
Assert-Equal $evidence.Debugger.Shutdown.DebuggerMode 'Design' 'Shutdown.DebuggerMode'
Assert-Equal $evidence.Debugger.Shutdown.ProcessAbsent $true 'Shutdown.ProcessAbsent'
Assert-Equal $evidence.Debugger.Shutdown.BreakpointsRemaining 0 'Shutdown.BreakpointsRemaining'
Assert-Equal $evidence.Debugger.Shutdown.SolutionClosed $true 'Shutdown.SolutionClosed'

if ([int]$evidence.Debugger.ProcessId -le 0) {
    throw 'Debugger.ProcessId must identify a real process.'
}

if ([int]$evidence.Debugger.ColumnCallback.CallStackDepth -lt 1) {
    throw 'ColumnCallback.CallStackDepth must contain at least the observed managed probe frame.'
}

if ([string]$evidence.TestedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'TestedCommit must be a full lowercase Git commit id.'
}

& git -C $RepositoryRoot cat-file -e "$($evidence.TestedCommit)^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "TestedCommit is not available in the local repository: $($evidence.TestedCommit)"
}

function Resolve-EvidenceArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "$Label must be a safe path relative to the evidence directory."
    }

    $resolved = [IO.Path]::GetFullPath((Join-Path $evidenceDirectory $RelativePath))
    $root = [IO.Path]::GetFullPath($evidenceDirectory).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label escapes the evidence directory."
    }

    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$Label is missing: $resolved"
    }

    return $resolved
}

function Assert-Artifact {
    param(
        [Parameter(Mandatory = $true)]
        $Artifact,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $path = Resolve-EvidenceArtifact -RelativePath ([string]$Artifact.Path) -Label $Label
    $file = Get-Item -LiteralPath $path
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    Assert-Equal $actualHash ([string]$Artifact.Sha256).ToUpperInvariant() "$Label.Sha256"
    Assert-Equal $file.Length ([long]$Artifact.LengthBytes) "$Label.LengthBytes"
    return $path
}

$assemblyPath = Assert-Artifact -Artifact $evidence.Artifacts.Assembly -Label 'Artifacts.Assembly'
$pdbPath = Assert-Artifact -Artifact $evidence.Artifacts.Symbols -Label 'Artifacts.Symbols'
$serverMainPath = Assert-Artifact -Artifact $evidence.Artifacts.ServerMainLog -Label 'Artifacts.ServerMainLog'
$serverWorldgenPath = Assert-Artifact -Artifact $evidence.Artifacts.ServerWorldgenLog -Label 'Artifacts.ServerWorldgenLog'

$artifactCommitDirectory = Split-Path -Leaf (Split-Path -Parent $assemblyPath)
Assert-Equal $artifactCommitDirectory $evidence.TestedCommit 'Assembly artifact commit directory'
Assert-Equal (Split-Path -Parent $assemblyPath) (Split-Path -Parent $pdbPath) 'DLL/PDB artifact directory'

$productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion
Assert-Equal $productVersion "1.0.0+$($evidence.TestedCommit)" 'Assembly ProductVersion/TestedCommit'
Assert-Equal $evidence.Debugger.ModuleSha256 $evidence.Artifacts.Assembly.Sha256 'Debugger.ModuleSha256'
Assert-Equal $evidence.Debugger.Symbols.PdbSha256 $evidence.Artifacts.Symbols.Sha256 'Debugger.Symbols.PdbSha256'

$pdbHeader = [IO.File]::ReadAllBytes($pdbPath)[0..3]
$pdbSignature = [Text.Encoding]::ASCII.GetString($pdbHeader)
Assert-Equal $pdbSignature 'BSJB' 'Portable PDB signature'

$mainLog = Get-Content -LiteralPath $serverMainPath -Raw
$escapedHash = [regex]::Escape([string]$evidence.Artifacts.Assembly.Sha256)
$escapedPid = [regex]::Escape([string]$evidence.Debugger.ProcessId)
$escapedCoordinate = [regex]::Escape([string]$evidence.Debugger.ColumnCallback.Coordinate)
$requiredLogPatterns = @(
    "L00A_BOOTSTRAP .*pid=$escapedPid .*sha256=$escapedHash",
    "L00B_DEBUG_PROBE_READY pid=$escapedPid module=ISRWorldGen\.dll",
    "L00B_PROBE_COLUMN_REQUEST chunk=$escapedCoordinate",
    "L00B_COLUMN_CALLBACK chunk=$escapedCoordinate",
    "L00B_CONTROLLED_EXCEPTION chunk=$escapedCoordinate",
    "L00B_PROBE_COLUMN_LOADED chunk=$escapedCoordinate"
)

foreach ($pattern in $requiredLogPatterns) {
    if (-not [regex]::IsMatch($mainLog, $pattern)) {
        throw "Server main log is missing required evidence pattern: $pattern"
    }
}

if ((Get-Item -LiteralPath $serverWorldgenPath).Length -le 0) {
    throw 'The preserved server-worldgen.log must contain the real run output.'
}

[ordered]@{
    TestId = 'T00-03-EVIDENCE-CHECK'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    Evidence = (Resolve-Path -LiteralPath $EvidencePath).Path
    TestedCommit = $evidence.TestedCommit
    ProcessId = $evidence.Debugger.ProcessId
    Coordinate = $evidence.Debugger.ColumnCallback.Coordinate
    CallStackDepth = $evidence.Debugger.ColumnCallback.CallStackDepth
    Assembly = $assemblyPath
    Symbols = $pdbPath
    ServerMainLog = $serverMainPath
    ServerWorldgenLog = $serverWorldgenPath
} | ConvertTo-Json -Depth 5
