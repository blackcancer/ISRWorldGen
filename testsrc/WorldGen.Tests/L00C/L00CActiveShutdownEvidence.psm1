Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-L00CActiveShutdownLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Log,
        [Parameter(Mandatory = $true)][string]$InstanceId,
        [Parameter(Mandatory = $true)][string]$MarkerId,
        [Parameter(Mandatory = $true)][long]$WorldRunId,
        [Parameter(Mandatory = $true)][int]$OpenCount,
        [Parameter(Mandatory = $true)][bool]$IsNew
    )

    $instance = [regex]::Escape($InstanceId)
    $marker = [regex]::Escape($MarkerId)
    $run = [regex]::Escape([string]$WorldRunId)
    $open = [regex]::Escape([string]$OpenCount)
    $reason = if ($IsNew) { 'fixture-stable' } else { 'persisted-reopen-stable' }
    $reasonPattern = [regex]::Escape($reason)
    $precheckPattern = if ($IsNew) { $null } else {
        "L00C_PERSISTED_PRECHECK instance=$instance marker=$marker maps=9 exact=True"
    }
    $readyPattern = if ($IsNew) {
        "L00C_MAP_SNAPSHOT_COMMITTED instance=$instance marker=$marker maps=9 checksum=[0-9A-F]{64} writes=1"
    }
    else {
        "L00C_PERSISTED_REOPEN_STABLE instance=$instance marker=$marker run=$run .* callbacks=0 center=[0-9A-F]{64} halo=[0-9A-F]{64}"
    }
    $armedPattern = "L00C_DELAYED_SHUTDOWN_ARMED instance=$instance run=$run reason=$reasonPattern delayms=([0-9]+) listener=([1-9][0-9]*)"
    $firedPattern = "L00C_DELAYED_SHUTDOWN_FIRED instance=$instance run=$run reason=$reasonPattern"
    $shutdownPattern = "L00C_GRACEFUL_SHUTDOWN_REQUEST instance=$instance marker=$marker reason=$reasonPattern"
    $runPhasePattern = 'Entering runphase Shutdown'
    $disposedPattern = "L00C_DISPOSED instance=$instance [^\r\n]*exact=True[^\r\n]*"
    $modsNotifiedPattern = 'Mods and systems notified, now saving everything\.\.\.'
    $markerSavedPattern = "L00C_MARKER_SAVED instance=$instance marker=$marker open=$open"
    $anyMarkerSavedPattern = "L00C_MARKER_SAVED instance=$instance marker=$marker open=([^\s]+)"

    if ($Log -match 'Server suspend requested, but reached max wait time') {
        throw 'Active shutdown encountered a server-suspension timeout.'
    }
    if ($Log -match 'L00C_DISPOSE_ERROR') {
        throw 'Active shutdown encountered a probe dispose error.'
    }

    $precheck = if ($IsNew) { $null } else { [regex]::Match($Log, $precheckPattern) }
    $readyRegex = [regex]::new($readyPattern)
    $ready = if ($IsNew -or $precheck.Success) {
        $readyRegex.Match($Log, $(if ($IsNew) { 0 } else { $precheck.Index + $precheck.Length }))
    }
    else {
        $readyRegex.Match([string]::Empty)
    }
    $armedRegex = [regex]::new($armedPattern)
    $firedRegex = [regex]::new($firedPattern)
    $shutdownRegex = [regex]::new($shutdownPattern)
    $runPhaseRegex = [regex]::new($runPhasePattern)
    $disposedRegex = [regex]::new($disposedPattern)
    $modsNotifiedRegex = [regex]::new($modsNotifiedPattern)
    $worldSavedRegex = [regex]::new('World saved!')
    $stoppedRegex = [regex]::new('Stopped the server!')
    $armed = if ($ready.Success) { $armedRegex.Match($Log, $ready.Index + $ready.Length) } else { $armedRegex.Match($Log) }
    $fired = if ($armed.Success) { $firedRegex.Match($Log, $armed.Index + $armed.Length) } else { $firedRegex.Match($Log) }
    $shutdown = if ($fired.Success) { $shutdownRegex.Match($Log, $fired.Index + $fired.Length) } else { $shutdownRegex.Match($Log) }
    $runPhase = if ($shutdown.Success) { $runPhaseRegex.Match($Log, $shutdown.Index + $shutdown.Length) } else { $runPhaseRegex.Match($Log) }
    $disposed = if ($runPhase.Success) { $disposedRegex.Match($Log, $runPhase.Index + $runPhase.Length) } else { $disposedRegex.Match($Log) }
    $modsNotified = if ($disposed.Success) { $modsNotifiedRegex.Match($Log, $disposed.Index + $disposed.Length) } else { $modsNotifiedRegex.Match($Log) }
    $worldSaved = if ($modsNotified.Success) { $worldSavedRegex.Match($Log, $modsNotified.Index + $modsNotified.Length) } else { $worldSavedRegex.Match($Log) }
    $stopped = if ($worldSaved.Success) { $stoppedRegex.Match($Log, $worldSaved.Index + $worldSaved.Length) } else { $stoppedRegex.Match($Log) }
    $required = @(
        [pscustomobject]@{ Label = 'ready'; Match = $ready },
        [pscustomobject]@{ Label = 'armed'; Match = $armed },
        [pscustomobject]@{ Label = 'fired'; Match = $fired },
        [pscustomobject]@{ Label = 'graceful shutdown request'; Match = $shutdown },
        [pscustomobject]@{ Label = 'shutdown runphase'; Match = $runPhase },
        [pscustomobject]@{ Label = 'probe disposal'; Match = $disposed },
        [pscustomobject]@{ Label = 'mods-notified boundary'; Match = $modsNotified },
        [pscustomobject]@{ Label = 'world save'; Match = $worldSaved },
        [pscustomobject]@{ Label = 'server stop'; Match = $stopped }
    )
    if (-not $IsNew) {
        $required = @([pscustomobject]@{ Label = 'persisted precheck'; Match = $precheck }) + $required
    }
    foreach ($entry in $required) {
        if (-not $entry.Match.Success) {
            throw "Active shutdown log is missing $($entry.Label)."
        }
    }

    $delayMilliseconds = [int]$armed.Groups[1].Value
    if ($delayMilliseconds -lt 10000 -or $delayMilliseconds -gt 60000) {
        throw "Active shutdown delay is outside the campaign-safe 10000..60000 ms range: $delayMilliseconds."
    }
    if ([regex]::Matches($Log, $armedPattern).Count -ne 1 -or
        [regex]::Matches($Log, $firedPattern).Count -ne 1 -or
        [regex]::Matches($Log, $shutdownPattern).Count -ne 1 -or
        [regex]::Matches($Log, $runPhasePattern).Count -ne 1 -or
        [regex]::Matches($Log, $disposedPattern).Count -ne 1 -or
        [regex]::Matches($Log, $modsNotifiedPattern).Count -ne 1 -or
        [regex]::Matches($Log, 'World saved!').Count -ne 1 -or
        [regex]::Matches($Log, 'Stopped the server!').Count -ne 1) {
        throw 'Active shutdown lifecycle events were not each observed exactly once.'
    }
    if ((-not $IsNew -and $precheck.Index -ge $ready.Index) -or
        $ready.Index -ge $armed.Index -or $armed.Index -ge $fired.Index -or
        $fired.Index -ge $shutdown.Index -or $shutdown.Index -ge $runPhase.Index -or
        $runPhase.Index -ge $disposed.Index -or $disposed.Index -ge $modsNotified.Index -or
        $modsNotified.Index -ge $worldSaved.Index -or $worldSaved.Index -ge $stopped.Index) {
        throw 'Active shutdown did not prove PRECHECK (reopen) < READY < ARMED < FIRED < GRACEFUL < Shutdown runphase < DISPOSED < Mods notified < World saved < Stopped.'
    }

    $anyMarkerSaves = [regex]::Matches($Log, $anyMarkerSavedPattern)
    $matchingMarkerSaves = [regex]::Matches($Log, $markerSavedPattern)
    if ($anyMarkerSaves.Count -ne $matchingMarkerSaves.Count) {
        throw 'An optional marker autosave has an unexpected OpenCount.'
    }

    [pscustomobject]@{
        Status = 'PASS'
        Reason = $reason
        DelayMilliseconds = $delayMilliseconds
        ListenerId = [long]$armed.Groups[2].Value
        MarkerAutosaveCount = $matchingMarkerSaves.Count
        DurableStateEvidence = 'database-required'
    }
}

function Assert-L00CPersistedActivationOrder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Log,
        [Parameter(Mandatory = $true)][string]$InstanceId,
        [Parameter(Mandatory = $true)][string]$MarkerId,
        [Parameter(Mandatory = $true)][int]$OpenCount
    )

    $instance = [regex]::Escape($InstanceId)
    $marker = [regex]::Escape($MarkerId)
    $open = [regex]::Escape([string]$OpenCount)
    $precheckPattern = "L00C_PERSISTED_PRECHECK instance=$instance marker=$marker maps=9 exact=True"
    $activatedPattern = "L00C_ACTIVATED instance=$instance marker=$marker [^\r\n]* open=$open isnew=False "
    $prechecks = [regex]::Matches($Log, $precheckPattern)
    $activations = [regex]::Matches($Log, $activatedPattern)
    if ($prechecks.Count -ne 1 -or $activations.Count -ne 1 -or $prechecks[0].Index -ge $activations[0].Index) {
        throw "Persisted activation open $OpenCount did not prove one PRECHECK before one ACTIVATED marker."
    }

    [pscustomobject]@{
        Status = 'PASS'
        PrecheckIndex = $prechecks[0].Index
        ActivatedIndex = $activations[0].Index
    }
}

Export-ModuleMember -Function Assert-L00CActiveShutdownLog, Assert-L00CPersistedActivationOrder
