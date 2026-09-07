Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

function Get-L00CFileRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $item = Get-Item -LiteralPath $resolved
    return [ordered]@{
        Path = $resolved
        Sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
        Length = $item.Length
        CreationTimeUtc = ([DateTimeOffset]$item.CreationTimeUtc).ToString('o')
        LastWriteTimeUtc = ([DateTimeOffset]$item.LastWriteTimeUtc).ToString('o')
    }
}

function Assert-L00CFileRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath ([string]$Record.Path) -PathType Leaf)) {
        throw "$Label is missing: $($Record.Path)"
    }
    $actual = Get-L00CFileRecord ([string]$Record.Path)
    foreach ($property in @('Path', 'Sha256', 'Length')) {
        if ([string]$actual.$property -ne [string]$Record.$property) {
            throw "$Label $property changed after it was recorded."
        }
    }
    foreach ($property in @('CreationTimeUtc', 'LastWriteTimeUtc')) {
        $actualInstant = ConvertTo-L00CUtcInstant $actual.$property "$Label actual $property"
        $recordedInstant = ConvertTo-L00CUtcInstant $Record.$property "$Label recorded $property"
        if ($actualInstant -ne $recordedInstant) {
            throw "$Label $property changed after it was recorded."
        }
    }
    return $actual
}

function Get-L00CCampaignReceiptId {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Receipt)

    $common = @(
        [string]$Receipt.SchemaVersion,
        [string]$Receipt.PhaseSequence,
        [string]$Receipt.Phase,
        [string]$Receipt.Status,
        [string]$Receipt.CampaignId,
        [string]$Receipt.Nonce,
        [string]$Receipt.TestedCommit,
        [string]$Receipt.AssemblySha256,
        [string]$Receipt.InitializedUtc,
        [string]$Receipt.PreviousReceiptId,
        [string]$Receipt.PreviousReceiptFileSha256
    )
    $phaseSpecific = switch ([string]$Receipt.Phase) {
        'Initialize' {
            @(
                [string]$Receipt.ControllerSha256,
                [string]$Receipt.AssemblyProductVersion,
                [string]$Receipt.Assembly.Path,
                [string]$Receipt.SaveDatabasePath,
                [string]$Receipt.SnapshotDatabasePath,
                [string]$Receipt.PersistenceReportPath,
                [string]$Receipt.Open2SnapshotDatabasePath,
                [string]$Receipt.Open2PersistenceReportPath,
                [string]$Receipt.Open1SessionPath,
                [string]$Receipt.Open1LogPath,
                [string]$Receipt.Open2SessionPath,
                [string]$Receipt.Open2LogPath,
                [string]$Receipt.EvidenceDirectory
            )
        }
        'RecordOpen1' {
            @(
                [string]$Receipt.RecordedUtc,
                [string]$Receipt.Open1StartedUtc,
                [string]$Receipt.Open1CompletedUtc,
                [string]$Receipt.Open1EvidenceSequence,
                [string]$Receipt.ExpectedOpen2EvidenceSequence,
                [string]$Receipt.SavegameIdentifier,
                [string]$Receipt.MarkerId,
                [string]$Receipt.InstanceId,
                [string]$Receipt.WorldRunId,
                [string]$Receipt.Open1Session.Sha256,
                [string]$Receipt.Open1Log.Sha256,
                [string]$Receipt.SnapshotDatabase.Sha256,
                [string]$Receipt.PersistenceReport.Sha256,
                [string]$Receipt.PersistenceAttestationId
            )
        }
        'AuthorizeOpen2' {
            @(
                [string]$Receipt.AuthorizedUtc,
                [string]$Receipt.ExpectedOpen2EvidenceSequence,
                [string]$Receipt.SavegameIdentifier,
                [string]$Receipt.MarkerId,
                [string]$Receipt.Open1SessionSha256,
                [string]$Receipt.Open1LogSha256,
                [string]$Receipt.SnapshotDatabaseSha256,
                [string]$Receipt.PersistenceReportSha256,
                [string]$Receipt.PersistenceAttestationId
            )
        }
        'Finalize' {
            @(
                [string]$Receipt.FinalizedUtc,
                [string]$Receipt.Open2StartedUtc,
                [string]$Receipt.Open2CompletedUtc,
                [string]$Receipt.Open2EvidenceSequence,
                [string]$Receipt.SavegameIdentifier,
                [string]$Receipt.MarkerId,
                [string]$Receipt.Open2InstanceId,
                [string]$Receipt.Open2WorldRunId,
                [string]$Receipt.Open2Session.Sha256,
                [string]$Receipt.Open2Log.Sha256,
                [string]$Receipt.Open2SnapshotDatabase.Sha256,
                [string]$Receipt.Open2PersistenceReport.Sha256,
                [string]$Receipt.Open2PersistenceAttestationId,
                [string]$Receipt.ExpectedNextOpenEvidenceSequence,
                [string]$Receipt.PersistenceAttestationId
            )
        }
        default { throw "Unknown campaign receipt phase: $($Receipt.Phase)" }
    }
    $canonical = (@($common) + @($phaseSpecific)) -join '|'
    $bytes = [Text.Encoding]::UTF8.GetBytes($canonical)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function Assert-L00CCampaignReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Receipt,
        [Parameter(Mandatory = $true)][ValidateSet('Initialize', 'RecordOpen1', 'AuthorizeOpen2', 'Finalize')][string]$ExpectedPhase
    )

    $expectedSequence = @{
        Initialize = 1
        RecordOpen1 = 2
        AuthorizeOpen2 = 3
        Finalize = 4
    }[$ExpectedPhase]
    if ([int]$Receipt.SchemaVersion -ne 1 -or [string]$Receipt.Phase -ne $ExpectedPhase -or
        [int]$Receipt.PhaseSequence -ne $expectedSequence -or [string]$Receipt.ReceiptId -notmatch '^[0-9A-F]{64}$') {
        throw "Campaign receipt shape is invalid for phase $ExpectedPhase."
    }
    if ([string]$Receipt.ReceiptId -ne (Get-L00CCampaignReceiptId $Receipt)) {
        throw "Campaign receipt $ExpectedPhase was modified after creation."
    }
    return $Receipt
}

function Read-L00CCampaignReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('Initialize', 'RecordOpen1', 'AuthorizeOpen2', 'Finalize')][string]$ExpectedPhase
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Campaign receipt is missing for phase ${ExpectedPhase}: $Path"
    }
    $receipt = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return Assert-L00CCampaignReceipt $receipt $ExpectedPhase
}

Export-ModuleMember -Function ConvertTo-L00CUtcInstant, Get-L00CFileRecord, Assert-L00CFileRecord, Get-L00CCampaignReceiptId, Assert-L00CCampaignReceipt, Read-L00CCampaignReceipt
