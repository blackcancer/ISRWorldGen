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
L00C_PERSISTED_PRECHECK instance=$instance marker=$marker maps=9 exact=True
L00C_PERSISTED_REOPEN_STABLE instance=$instance marker=$marker run=1 loadpriority=0 transientrequests=0 refreshpasses=0 refreshedmapchunks=0 keeploaded=0 unload=0 fixturewrites=0 mapsnapshotwrites=0 callbacks=0 center=$('A' * 64) halo=$('B' * 64)
L00C_DELAYED_SHUTDOWN_ARMED instance=$instance run=1 reason=persisted-reopen-stable delayms=15000 listener=42
L00C_DELAYED_SHUTDOWN_FIRED instance=$instance run=1 reason=persisted-reopen-stable
L00C_GRACEFUL_SHUTDOWN_REQUEST instance=$instance marker=$marker reason=persisted-reopen-stable
Entering runphase Shutdown
L00C_DISPOSED instance=$instance removedowned=0 restorednative=0 exact=True callbacks=0 forwarded=0
Mods and systems notified, now saving everything...
World saved!
Stopped the server!
"@

$result = Assert-L00CActiveShutdownLog -Log $valid -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false
$withOptionalAutosave = $valid -replace 'World saved!', "L00C_MARKER_SAVED instance=$instance marker=$marker open=2`nWorld saved!"
$autosaveResult = Assert-L00CActiveShutdownLog -Log $withOptionalAutosave -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false
if ($autosaveResult.MarkerAutosaveCount -ne 1) { throw 'A correctly bound optional autosave was not reported.' }
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'L00C_DELAYED_SHUTDOWN_ARMED[^\r\n]+\r?\n', '') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'missing arm'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'L00C_PERSISTED_PRECHECK[^\r\n]+\r?\n', '') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'missing persisted precheck'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'delayms=15000', 'delayms=9999') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'unsafe active delay'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'World saved!', 'Server suspend requested, but reached max wait time.') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'suspend timeout'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'World saved!', "L00C_MARKER_SAVED instance=$instance marker=$marker open=2") -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'marker autosave substituted for durable world save'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'World saved!', "L00C_MARKER_SAVED instance=$instance marker=$marker open=99`nWorld saved!") -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'wrong optional autosave open count'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'World saved!\r?\nStopped the server!', "Stopped the server!`nWorld saved!") -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'world save after stop'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'Entering runphase Shutdown\r?\n', '') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'missing shutdown runphase'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'Mods and systems notified, now saving everything\.\.\.\r?\n', '') -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'missing mods-notified boundary'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'L00C_DISPOSED[^\r\n]+\r?\nMods and systems notified, now saving everything\.\.\.', "Mods and systems notified, now saving everything...`nL00C_DISPOSED instance=$instance removedowned=0 restorednative=0 exact=True callbacks=0 forwarded=0") -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'mods notified before dispose'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid -replace 'L00C_DISPOSED[^\r\n]+\r?\nMods and systems notified, now saving everything\.\.\.', "Mods and systems notified, now saving everything...`nWorld saved!`nL00C_DISPOSED instance=$instance removedowned=0 restorednative=0 exact=True callbacks=0 forwarded=0") -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'world save before dispose'
Assert-Rejected { Assert-L00CActiveShutdownLog -Log ($valid + "`nL00C_DISPOSE_ERROR instance=$instance stage=events error=synthetic") -InstanceId $instance -MarkerId $marker -WorldRunId 1 -OpenCount 2 -IsNew $false } 'dispose error'

$activationLine = "L00C_ACTIVATED instance=$instance marker=$marker run=1 open=3 isnew=False save=33333333-3333-3333-3333-333333333333 fixture=31990,31990"
$open3Valid = "L00C_PERSISTED_PRECHECK instance=$instance marker=$marker maps=9 exact=True`n$activationLine"
[void](Assert-L00CPersistedActivationOrder -Log $open3Valid -InstanceId $instance -MarkerId $marker -OpenCount 3)
Assert-Rejected { Assert-L00CPersistedActivationOrder -Log "$activationLine`nL00C_PERSISTED_PRECHECK instance=$instance marker=$marker maps=9 exact=True" -InstanceId $instance -MarkerId $marker -OpenCount 3 } 'open3 activation before persisted precheck'

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
    MissingWorldSaveRejected = $true
    MissingPersistedPrecheckRejected = $true
    MarkerSaveNotRequired = $true
    BoundMarkerAutosaveAccepted = $true
    MarkerSaveCannotSubstituteForDatabase = $true
    ReorderedTerminalEventsRejected = $true
    ShutdownLifecycleOrderRequired = $true
    DisposeErrorRejected = $true
    PersistedActivationOrderRequired = $true
    ObservedLogValidated = -not [string]::IsNullOrWhiteSpace($ObservedLogPath)
} | ConvertTo-Json -Depth 4
