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
    $readyPattern = if ($IsNew) {
        "L00C_MAP_SNAPSHOT_COMMITTED instance=$instance marker=$marker maps=9 checksum=[0-9A-F]{64} writes=1"
    }
    else {
        "L00C_PERSISTED_REOPEN_STABLE instance=$instance marker=$marker run=$run .* callbacks=0 center=[0-9A-F]{64} halo=[0-9A-F]{64}"
    }
    $armedPattern = "L00C_DELAYED_SHUTDOWN_ARMED instance=$instance run=$run reason=$reasonPattern delayms=([0-9]+) listener=([1-9][0-9]*)"
    $firedPattern = "L00C_DELAYED_SHUTDOWN_FIRED instance=$instance run=$run reason=$reasonPattern"
    $shutdownPattern = "L00C_GRACEFUL_SHUTDOWN_REQUEST instance=$instance marker=$marker reason=$reasonPattern"
    $markerSavedPattern = "L00C_MARKER_SAVED instance=$instance marker=$marker open=$open"

    if ($Log -match 'Server suspend requested, but reached max wait time') {
        throw 'Active shutdown encountered a server-suspension timeout.'
    }

    $ready = [regex]::Match($Log, $readyPattern)
    $armedRegex = [regex]::new($armedPattern)
    $firedRegex = [regex]::new($firedPattern)
    $shutdownRegex = [regex]::new($shutdownPattern)
    $markerSavedRegex = [regex]::new($markerSavedPattern)
    $worldSavedRegex = [regex]::new('World saved!')
    $stoppedRegex = [regex]::new('Stopped the server!')
    $armed = if ($ready.Success) { $armedRegex.Match($Log, $ready.Index + $ready.Length) } else { $armedRegex.Match($Log) }
    $fired = if ($armed.Success) { $firedRegex.Match($Log, $armed.Index + $armed.Length) } else { $firedRegex.Match($Log) }
    $shutdown = if ($fired.Success) { $shutdownRegex.Match($Log, $fired.Index + $fired.Length) } else { $shutdownRegex.Match($Log) }
    $markerSaved = if ($shutdown.Success) { $markerSavedRegex.Match($Log, $shutdown.Index + $shutdown.Length) } else { $markerSavedRegex.Match($Log) }
    $worldSaved = if ($markerSaved.Success) { $worldSavedRegex.Match($Log, $markerSaved.Index + $markerSaved.Length) } else { $worldSavedRegex.Match($Log) }
    $stopped = if ($worldSaved.Success) { $stoppedRegex.Match($Log, $worldSaved.Index + $worldSaved.Length) } else { $stoppedRegex.Match($Log) }
    foreach ($entry in @(
        [pscustomobject]@{ Label = 'ready'; Match = $ready },
        [pscustomobject]@{ Label = 'armed'; Match = $armed },
        [pscustomobject]@{ Label = 'fired'; Match = $fired },
        [pscustomobject]@{ Label = 'graceful shutdown request'; Match = $shutdown },
        [pscustomobject]@{ Label = 'marker save'; Match = $markerSaved },
        [pscustomobject]@{ Label = 'world save'; Match = $worldSaved },
        [pscustomobject]@{ Label = 'server stop'; Match = $stopped }
    )) {
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
        [regex]::Matches($Log, $shutdownPattern).Count -ne 1) {
        throw 'Active shutdown was not armed, fired, and requested exactly once.'
    }
    if ($ready.Index -ge $armed.Index -or $armed.Index -ge $fired.Index -or
        $fired.Index -ge $shutdown.Index -or $shutdown.Index -ge $markerSaved.Index -or
        $markerSaved.Index -ge $worldSaved.Index -or $worldSaved.Index -ge $stopped.Index) {
        throw 'Active shutdown did not prove READY < ARMED < FIRED < GRACEFUL < MARKER_SAVED < World saved < Stopped.'
    }

    [pscustomobject]@{
        Status = 'PASS'
        Reason = $reason
        DelayMilliseconds = $delayMilliseconds
        ListenerId = [long]$armed.Groups[2].Value
    }
}

Export-ModuleMember -Function Assert-L00CActiveShutdownLog
