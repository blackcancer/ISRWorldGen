[CmdletBinding()]
param(
    [string]$ObservedLogPath,
    [string]$ObservedInstanceId,
    [string]$ObservedMarkerId,
    [long]$ObservedWorldRunId = 1,
    [int]$ObservedOpenCount = 2,
    [bool]$ObservedIsNew = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'L00CActiveShutdownEvidence.psm1') -Force

function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    try { & $Action } catch { return }
    throw "Negative active-shutdown evidence was accepted: $Label"
}

$instance = '1' * 32
$marker = '2' * 32
$valid = @"
L00C_PERSISTED_REOPEN_STABLE instance=$instance marker=$marker run=1 loadpriority=0 transientrequests=0 refreshpasses=0 refreshedmapchunks=0 keeploaded=0 unload=0 fixturewrites=0 mapsnapshotwrites=0 callbacks=0 center=$('A' * 64) halo=$('B' * 64)
L00C_DELAYED_SHUTDOWN_ARMED instance=$instance run=1 reason=persisted-reopen-stable delayms=15000 listener=42
L00C_DELAYED_SHUTDOWN_FIRED instance=$instance run=1 reason=persisted-reopen-stable
L00C_GRACEFUL_SHUTDOWN_REQUEST instance=$instance marker=$marker reason=persisted-reopen-stable
L00C_MARKER_SAVED instance=$instance marker=$marker open=2
World saved!
Stopped the server!
"@

$result = Assert-L00CActiveShutdownLog -Log $valid -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'L00C_DELAYED_SHUTDOWN_ARMED[^\r\n]+\r?\n', '') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'missing arm'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'delayms=15000', 'delayms=9999') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'unsafe active delay'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'World saved!', 'Server suspend requested, but reached max wait time.') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'suspend timeout'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'L00C_MARKER_SAVED[^\r\n]+\r?\n', '') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'missing committed marker save'

if (-not [string]::IsNullOrWhiteSpace($ObservedLogPath)) {
    if (-not (Test-Path -LiteralPath $ObservedLogPath -PathType Leaf)) { throw "Observed log is missing: $ObservedLogPath" }
    if ($ObservedInstanceId -notmatch '^[0-9a-f]{32}$' -or $ObservedMarkerId -notmatch '^[0-9a-f]{32}$') {
        throw 'Observed evidence requires exact instance and marker identifiers.'
    }
    $observed = Get-Content -LiteralPath $ObservedLogPath -Raw
    [void](Assert-L00CActiveShutdownLog -Log $observed -InstanceId $ObservedInstanceId -MarkerId $ObservedMarkerId -WorldRunId $ObservedWorldRunId -OpenCount $ObservedOpenCount -IsNew $ObservedIsNew)
}

[ordered]@{
    TestId = 'L00-C-ACTIVE-SHUTDOWN-EVIDENCE'
    Status = 'PASS'
    DelayMilliseconds = $result.DelayMilliseconds
    SuspendTimeoutRejected = $true
    MissingSaveRejected = $true
    ObservedLogValidated = -not [string]::IsNullOrWhiteSpace($ObservedLogPath)
} | ConvertTo-Json -Depth 4
