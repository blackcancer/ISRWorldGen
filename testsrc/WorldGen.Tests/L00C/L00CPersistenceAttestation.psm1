Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-L00CAttestationId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Report
    )

    $canonical = @(
        [string]$Report.SchemaVersion,
        [string]$Report.CampaignId,
        [string]$Report.TestedCommit,
        [string]$Report.OracleSha256,
        [string]$Report.AssemblySha256,
        [string]$Report.SavegameIdentifier,
        [string]$Report.MarkerId,
        [string]$Report.InstanceId,
        [string]$Report.WorldRunId,
        [string]$Report.Open1EvidenceSequence,
        [string]$Report.ExpectedOpen2EvidenceSequence,
        [string]$Report.Open1CompletedUtc,
        [string]$Report.DatabaseSha256,
        [string]$Report.DatabaseLength,
        [string]$Report.Open1LogSha256,
        [string]$Report.Open1LogLength,
        [string]$Report.AttestedUtc
    ) -join '|'
    $bytes = [Text.Encoding]::UTF8.GetBytes($canonical)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function ConvertTo-L00CUtcInstant {
    param($Value, [string]$Label)

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

    foreach ($path in @($ReportPath, $DatabasePath, $Open1LogPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Persistence attestation input is missing: $path"
        }
    }
    if ([int]$Report.SchemaVersion -ne 1 -or [string]$Report.EvidenceOrder -ne 'open1-complete<attestation<open2-start') {
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
        @([string]$Report.Open1EvidenceSequence, [string]$Open1Session.EvidenceSequence, 'open1 evidence sequence'),
        @([string]$Report.ExpectedOpen2EvidenceSequence, [string]$Open2Session.EvidenceSequence, 'open2 evidence sequence')
    )) {
        if ($pair[0] -ne $pair[1]) {
            throw "Persistence attestation $($pair[2]) mismatch: '$($pair[0])' != '$($pair[1])'."
        }
    }
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

    $open1Started = ConvertTo-L00CUtcInstant $Open1Session.StartedUtc 'Open1 StartedUtc'
    $open1Completed = ConvertTo-L00CUtcInstant $Open1Session.CompletedUtc 'Open1 CompletedUtc'
    $attested = ConvertTo-L00CUtcInstant $Report.AttestedUtc 'Report AttestedUtc'
    $reportOpen1Completed = ConvertTo-L00CUtcInstant $Report.Open1CompletedUtc 'Report Open1CompletedUtc'
    $open2Started = ConvertTo-L00CUtcInstant $Open2Session.StartedUtc 'Open2 StartedUtc'
    $open2Completed = ConvertTo-L00CUtcInstant $Open2Session.CompletedUtc 'Open2 CompletedUtc'
    if ($open1Started -ge $open1Completed -or $open1Completed -ne $reportOpen1Completed -or
        $reportOpen1Completed -gt $attested -or $attested -ge $open2Started -or $open2Started -ge $open2Completed) {
        throw 'Persistence attestation timestamps do not prove open1-complete < attestation < open2-start.'
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
    $databaseCreated = [DateTimeOffset]$database.CreationTimeUtc
    $databaseWritten = [DateTimeOffset]$database.LastWriteTimeUtc
    if ($reportCreated -lt $open1Completed -or $reportCreated -ge $open2Started -or
        $reportWritten -lt $open1Completed -or $reportWritten -ge $open2Started -or
        $databaseCreated -ge $open2Started -or $databaseWritten -ge $open2Started) {
        throw 'Persistence database/report was created, copied, or modified after primary open2 started.'
    }

    [ordered]@{
        Status = 'PASS'
        AttestationId = [string]$Report.AttestationId
        Open1Sequence = [int]$Open1Session.EvidenceSequence
        Open2Sequence = [int]$Open2Session.EvidenceSequence
        AttestedUtc = $attested.ToString('o')
        ReportCreatedUtc = $reportCreated.ToString('o')
        DatabaseSha256 = $databaseHash
        Open1LogSha256 = $open1LogHash
    }
}

Export-ModuleMember -Function Get-L00CAttestationId, Assert-L00CPersistenceAttestation
