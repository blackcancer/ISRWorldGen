Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-L00CAttestationId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Report
    )

    $open1Completed = ConvertTo-L00CUtcInstant $Report.Open1CompletedUtc 'Report Open1CompletedUtc'
    $attested = ConvertTo-L00CUtcInstant $Report.AttestedUtc 'Report AttestedUtc'
    $canonical = @(
        [string]$Report.SchemaVersion,
        [string]$Report.ControllerPhase,
        [string]$Report.CampaignId,
        [string]$Report.TestedCommit,
        [string]$Report.OracleSha256,
        [string]$Report.AssemblySha256,
        [string]$Report.SavegameIdentifier,
        [string]$Report.MarkerId,
        [string]$Report.InstanceId,
        [string]$Report.WorldRunId,
        [string]$Report.ExpectedOpenCount,
        [string]$Report.ExpectedIsNew,
        [string]$Report.Open1EvidenceSequence,
        [string]$Report.ExpectedOpen2EvidenceSequence,
        $open1Completed.ToString('o'),
        [string]$Report.DatabaseSha256,
        [string]$Report.DatabaseLength,
        [string]$Report.Open1LogSha256,
        [string]$Report.Open1LogLength,
        [string]$Report.Autonomous,
        [string]$Report.IntegrityCheck,
        [string]$Report.ActualMapChunks,
        [string]$Report.ActualChunks,
        [string]$Report.MarkerEnvelope.Version,
        [string]$Report.MarkerEnvelope.OpenCount,
        [string]$Report.MarkerEnvelope.PayloadSha256,
        [string]$Report.MarkerEnvelope.MapFootprintVersion,
        [string]$Report.MarkerEnvelope.MapFootprintSha256,
        $attested.ToString('o')
    ) -join '|'
    $bytes = [Text.Encoding]::UTF8.GetBytes($canonical)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function ConvertTo-L00CUtcInstant {
    param($Value, [string]$Label)

    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset]$Value).ToUniversalTime()
    }
    if ($Value -is [DateTime]) {
        return ([DateTimeOffset]([DateTime]$Value)).ToUniversalTime()
    }
    $instant = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        [string]$Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$instant)) {
        throw "$Label is not a round-trip timestamp: '$Value'."
    }
    return $instant.ToUniversalTime()
}

function Assert-L00CPreOpen2Attestation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Report,
        [Parameter(Mandatory = $true)][string]$ReportPath,
        [Parameter(Mandatory = $true)][string]$DatabasePath,
        [Parameter(Mandatory = $true)][string]$Open1LogPath,
        [Parameter(Mandatory = $true)]$Open1Session,
        [Parameter(Mandatory = $true)][string]$CampaignId,
        [Parameter(Mandatory = $true)][string]$TestedCommit,
        [Parameter(Mandatory = $true)][string]$AssemblySha256,
        [Parameter(Mandatory = $true)][string]$UpperBoundUtc
    )

    foreach ($path in @($ReportPath, $DatabasePath, $Open1LogPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Persistence attestation input is missing: $path"
        }
    }
    if ([int]$Report.SchemaVersion -ne 2 -or [string]$Report.ControllerPhase -ne 'RecordOpen1' -or
        [string]$Report.EvidenceOrder -ne 'open1-complete<attestation<open2-start') {
        throw 'Persistence attestation schema/order declaration is invalid.'
    }
    foreach ($hash in @($Report.OracleSha256, $Report.AssemblySha256, $Report.DatabaseSha256, $Report.Open1LogSha256, $Report.AttestationId)) {
        if ([string]$hash -notmatch '^[0-9A-F]{64}$') {
            throw 'Persistence attestation contains a malformed SHA-256 value.'
        }
    }
    foreach ($pair in @(
        @([string]$Report.CampaignId, $CampaignId, 'campaign'),
        @([string]$Report.TestedCommit, $TestedCommit, 'candidate commit'),
        @([string]$Report.AssemblySha256, $AssemblySha256, 'candidate assembly'),
        @([string]$Report.SavegameIdentifier, [string]$Open1Session.SavegameIdentifier, 'savegame'),
        @([string]$Report.MarkerId, [string]$Open1Session.MarkerId, 'marker'),
        @([string]$Report.InstanceId, [string]$Open1Session.InstanceId, 'open1 instance'),
        @([string]$Report.WorldRunId, [string]$Open1Session.WorldRunId, 'open1 world run'),
        @([string]$Report.Open1EvidenceSequence, [string]$Open1Session.EvidenceSequence, 'open1 evidence sequence')
    )) {
        if ($pair[0] -ne $pair[1]) {
            throw "Persistence attestation $($pair[2]) mismatch: '$($pair[0])' != '$($pair[1])'."
        }
    }
    if ([int]$Report.ExpectedOpen2EvidenceSequence -ne [int]$Open1Session.EvidenceSequence + 1 -or
        [int]$Open1Session.OpenCount -ne 1 -or -not [bool]$Open1Session.IsNew -or
        [int]$Report.ExpectedOpenCount -ne 1 -or -not [bool]$Report.ExpectedIsNew -or
        -not [bool]$Report.Autonomous -or [string]$Report.IntegrityCheck -ne 'ok' -or
        [string]$Report.JournalMode -eq 'wal' -or [int]$Report.ActualMapChunks -ne 9 -or
        [int]$Report.ActualChunks -ne 72 -or [string]$Report.MarkerEnvelope.MarkerId -ne [string]$Open1Session.MarkerId -or
        [string]$Report.MarkerEnvelope.SavegameIdentifier -ne [string]$Open1Session.SavegameIdentifier -or
        [string]$Report.MarkerEnvelope.Version -ne 'l00c-flat-v2-map-snapshot' -or
        [int]$Report.MarkerEnvelope.OpenCount -ne 1 -or
        [string]$Report.MarkerEnvelope.MapFootprintVersion -ne 'l00c-map-footprint-v1' -or
        [int]$Report.MarkerEnvelope.MapFootprintMapChunks -ne 9) {
        throw 'Persistence attestation is not bound to a new primary open1 and its immediate open2 sequence.'
    }

    $open1Started = ConvertTo-L00CUtcInstant $Open1Session.StartedUtc 'Open1 StartedUtc'
    $open1Completed = ConvertTo-L00CUtcInstant $Open1Session.CompletedUtc 'Open1 CompletedUtc'
    $attested = ConvertTo-L00CUtcInstant $Report.AttestedUtc 'Report AttestedUtc'
    $reportOpen1Completed = ConvertTo-L00CUtcInstant $Report.Open1CompletedUtc 'Report Open1CompletedUtc'
    $upperBound = ConvertTo-L00CUtcInstant $UpperBoundUtc 'Pre-open2 upper bound'
    if ($open1Started -ge $open1Completed -or $open1Completed -ne $reportOpen1Completed -or
        $reportOpen1Completed -gt $attested -or $attested -gt $upperBound) {
        throw "Persistence attestation timestamps do not prove open1-complete < attestation before the pre-open2 bound: start=$($open1Started.ToString('o')) completed=$($open1Completed.ToString('o')) reportCompleted=$($reportOpen1Completed.ToString('o')) attested=$($attested.ToString('o')) upper=$($upperBound.ToString('o'))."
    }

    $database = Get-Item -LiteralPath $DatabasePath
    $open1Log = Get-Item -LiteralPath $Open1LogPath
    $reportFile = Get-Item -LiteralPath $ReportPath
    $databaseHash = (Get-FileHash -LiteralPath $DatabasePath -Algorithm SHA256).Hash
    $open1LogHash = (Get-FileHash -LiteralPath $Open1LogPath -Algorithm SHA256).Hash
    foreach ($pair in @(
        @([string]$Report.DatabaseSha256, $databaseHash, 'database hash'),
        @([string]$Report.DatabaseLength, [string]$database.Length, 'database length'),
        @([string]$Report.Open1LogSha256, $open1LogHash, 'open1 log hash'),
        @([string]$Report.Open1LogLength, [string]$open1Log.Length, 'open1 log length'),
        @([string]$Report.AttestationId, (Get-L00CAttestationId $Report), 'attestation id')
    )) {
        if ($pair[0] -ne $pair[1]) {
            throw "Persistence attestation $($pair[2]) mismatch."
        }
    }

    $reportCreated = [DateTimeOffset]$reportFile.CreationTimeUtc
    $reportWritten = [DateTimeOffset]$reportFile.LastWriteTimeUtc
    if ($reportCreated -lt $open1Completed -or $reportCreated -gt $upperBound -or
        $reportWritten -lt $open1Completed -or $reportWritten -gt $upperBound -or
        [DateTimeOffset]$database.CreationTimeUtc -gt $upperBound -or
        [DateTimeOffset]$database.LastWriteTimeUtc -gt $upperBound) {
        throw 'Persistence database/report falls outside the pre-open2 attestation interval.'
    }

    [ordered]@{
        Status = 'PASS'
        AttestationId = [string]$Report.AttestationId
        Open1Sequence = [int]$Open1Session.EvidenceSequence
        AttestedUtc = $attested.ToString('o')
        ReportCreatedUtc = $reportCreated.ToString('o')
        DatabaseSha256 = $databaseHash
        Open1LogSha256 = $open1LogHash
    }
}

function Assert-L00CPersistenceAttestation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Report,
        [Parameter(Mandatory = $true)][string]$ReportPath,
        [Parameter(Mandatory = $true)][string]$DatabasePath,
        [Parameter(Mandatory = $true)][string]$Open1LogPath,
        [Parameter(Mandatory = $true)]$Open1Session,
        [Parameter(Mandatory = $true)]$Open2Session,
        [Parameter(Mandatory = $true)][string]$CampaignId,
        [Parameter(Mandatory = $true)][string]$TestedCommit,
        [Parameter(Mandatory = $true)][string]$AssemblySha256
    )

    $open2Started = ConvertTo-L00CUtcInstant $Open2Session.StartedUtc 'Open2 StartedUtc'
    $preOpen2 = Assert-L00CPreOpen2Attestation `
        -Report $Report `
        -ReportPath $ReportPath `
        -DatabasePath $DatabasePath `
        -Open1LogPath $Open1LogPath `
        -Open1Session $Open1Session `
        -CampaignId $CampaignId `
        -TestedCommit $TestedCommit `
        -AssemblySha256 $AssemblySha256 `
        -UpperBoundUtc $open2Started.ToString('o')
    if ([string]$Open2Session.SavegameIdentifier -ne [string]$Open1Session.SavegameIdentifier -or
        [string]$Open2Session.MarkerId -ne [string]$Open1Session.MarkerId -or
        [string]$Open2Session.InstanceId -eq [string]$Open1Session.InstanceId -or
        [int]$Open1Session.OpenCount -ne 1 -or [int]$Open2Session.OpenCount -ne 2 -or
        -not [bool]$Open1Session.IsNew -or [bool]$Open2Session.IsNew) {
        throw 'Persistence attestation is not bounded to primary open1 followed by open2 of the same world.'
    }
    if ([int]$Open2Session.EvidenceSequence -ne [int]$Open1Session.EvidenceSequence + 1) {
        throw 'Primary open2 is not the immediate monotonic successor of open1.'
    }

    $open1Completed = ConvertTo-L00CUtcInstant $Open1Session.CompletedUtc 'Open1 CompletedUtc'
    $attested = ConvertTo-L00CUtcInstant $Report.AttestedUtc 'Report AttestedUtc'
    $reportOpen1Completed = ConvertTo-L00CUtcInstant $Report.Open1CompletedUtc 'Report Open1CompletedUtc'
    $open2Completed = ConvertTo-L00CUtcInstant $Open2Session.CompletedUtc 'Open2 CompletedUtc'
    if ($open1Completed -ne $reportOpen1Completed -or $reportOpen1Completed -gt $attested -or
        $attested -ge $open2Started -or $open2Started -ge $open2Completed) {
        throw 'Persistence attestation timestamps do not prove open1-complete < attestation < open2-start.'
    }

    [ordered]@{
        Status = 'PASS'
        AttestationId = [string]$Report.AttestationId
        Open1Sequence = [int]$Open1Session.EvidenceSequence
        Open2Sequence = [int]$Open2Session.EvidenceSequence
        AttestedUtc = $attested.ToString('o')
        ReportCreatedUtc = $preOpen2.ReportCreatedUtc
        DatabaseSha256 = $preOpen2.DatabaseSha256
        Open1LogSha256 = $preOpen2.Open1LogSha256
    }
}

Export-ModuleMember -Function Get-L00CAttestationId, Assert-L00CPreOpen2Attestation, Assert-L00CPersistenceAttestation
