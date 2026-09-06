[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "T00-03 evidence is missing: $EvidencePath"
}

$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json

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

if ([int]$evidence.Debugger.ProcessId -le 0) {
    throw 'Debugger.ProcessId must identify a real process.'
}

if ([int]$evidence.Debugger.ColumnCallback.CallStackDepth -lt 1) {
    throw 'ColumnCallback.CallStackDepth must contain at least the observed managed probe frame.'
}

foreach ($property in @('TestedCommit', 'AssemblySha256', 'PdbSha256')) {
    $value = [string]$evidence.$property
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$property must be recorded."
    }
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
} | ConvertTo-Json -Depth 5
