[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Initialize', 'RecordOpen1', 'AuthorizeOpen2', 'Finalize')]
    [string]$Phase,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,

    [string]$TestedCommit,
    [string]$AssemblyPath,
    [string]$SaveDatabasePath,
    [string]$SnapshotDatabasePath,
    [string]$PersistenceReportPath,
    [string]$Open1SessionPath,
    [string]$Open1LogPath,
    [string]$Open2SessionPath,
    [string]$Open2LogPath,
    [int]$FixtureChunkX = 31990,
    [int]$FixtureChunkZ = 31990,
    [int]$WorldHeight = 256,
    [int]$ChunkSize = 32,
    [int]$Dimension = 0,
    [string]$GamePath = 'D:\Jeux\Vintagestory',
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'L00CCampaignControl.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'L00CPersistenceAttestation.psm1') -Force

function Require-Value([string]$Value, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Label is required for phase $Phase."
    }
    return $Value
}

function Get-FullPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path)
}

function Assert-PathWithin([string]$Path, [string]$Root, [string]$Label) {
    $resolvedRoot = (Get-FullPath $Root).TrimEnd('\') + '\'
    $resolvedPath = Get-FullPath $Path
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must stay under $resolvedRoot"
    }
    return $resolvedPath
}

function Write-NewJson([string]$Path, $Value) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $parent)
    }
    $json = $Value | ConvertTo-Json -Depth 8
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $writer.Write($json) } finally { $writer.Dispose() }
    }
    finally {
        $stream.Dispose()
    }
}

function Add-ReceiptId($Receipt) {
    $normalized = $Receipt | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $Receipt.Add('ReceiptId', (Get-L00CCampaignReceiptId $normalized))
    return $Receipt
}

function Assert-NewPath([string]$Path, [string]$Label) {
    if (Test-Path -LiteralPath $Path) {
        throw "$Label must not exist before its campaign phase: $Path"
    }
}

function Assert-Candidate($InitializeReceipt) {
    $head = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -ne [string]$InitializeReceipt.TestedCommit) {
        throw "Campaign candidate is not current HEAD: expected=$($InitializeReceipt.TestedCommit) actual=$head"
    }
    $tracked = @(& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=no)
    if ($LASTEXITCODE -ne 0 -or $tracked.Count -ne 0) {
        throw 'Campaign controller requires a clean tracked worktree.'
    }
    [void](Assert-L00CFileRecord $InitializeReceipt.Assembly 'Candidate assembly')
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo([string]$InitializeReceipt.Assembly.Path).ProductVersion
    if ($productVersion -ne "1.0.0+$head") {
        throw "Candidate assembly ProductVersion '$productVersion' does not match HEAD '$head'."
    }
}

function Assert-ReceiptLink($Current, [string]$PreviousPath, [string]$PreviousPhase) {
    $previous = Read-L00CCampaignReceipt $PreviousPath $PreviousPhase
    if ([string]$Current.PreviousReceiptId -ne [string]$previous.ReceiptId -or
        [string]$Current.PreviousReceiptFileSha256 -ne (Get-FileHash -LiteralPath $PreviousPath -Algorithm SHA256).Hash) {
        throw "Campaign receipt chain is broken before $($Current.Phase)."
    }
    return $previous
}

function Assert-Open1Session($Session, $InitializeReceipt) {
    if ([int]$Session.EvidenceSequence -le 0 -or [int]$Session.OpenCount -ne 1 -or
        -not [bool]$Session.IsNew -or [string]$Session.WorldRole -ne 'activated-primary' -or
        [string]$Session.MarkerId -notmatch '^[0-9a-f]{32}$' -or
        [string]$Session.InstanceId -notmatch '^[0-9a-f]{32}$' -or [long]$Session.WorldRunId -le 0 -or
        [string]$Session.SavegameIdentifier -notmatch '^[0-9a-fA-F-]{36}$' -or
        [string]$Session.ControllerPhaseBefore -ne 'Initialize' -or
        [string]$Session.ControllerPhaseAfter -ne 'RecordOpen1') {
        throw 'Open1 session does not declare the required real primary-open1 identity and controller phases.'
    }
    $started = ConvertTo-L00CUtcInstant $Session.StartedUtc 'Open1 StartedUtc'
    $completed = ConvertTo-L00CUtcInstant $Session.CompletedUtc 'Open1 CompletedUtc'
    $initialized = ConvertTo-L00CUtcInstant $InitializeReceipt.InitializedUtc 'Campaign InitializedUtc'
    if ($initialized -ge $started -or $started -ge $completed -or $completed -gt [DateTimeOffset]::UtcNow) {
        throw 'Open1 timestamps must be after Initialize and completed before RecordOpen1.'
    }
    return [ordered]@{ Started = $started; Completed = $completed }
}

function Assert-Open2Session($Session, $Open1Session, $AuthorizationReceipt) {
    if ([int]$Session.EvidenceSequence -ne [int]$Open1Session.EvidenceSequence + 1 -or
        [int]$Session.OpenCount -ne 2 -or [bool]$Session.IsNew -or
        [string]$Session.WorldRole -ne 'activated-primary' -or
        [string]$Session.SavegameIdentifier -ne [string]$Open1Session.SavegameIdentifier -or
        [string]$Session.MarkerId -ne [string]$Open1Session.MarkerId -or
        [string]$Session.InstanceId -eq [string]$Open1Session.InstanceId -or
        [string]$Session.ControllerPhaseBefore -ne 'AuthorizeOpen2' -or
        [string]$Session.ControllerPhaseAfter -ne 'Finalize') {
        throw 'Open2 session does not declare the immediate same-world reopen and controller phases.'
    }
    $started = ConvertTo-L00CUtcInstant $Session.StartedUtc 'Open2 StartedUtc'
    $completed = ConvertTo-L00CUtcInstant $Session.CompletedUtc 'Open2 CompletedUtc'
    $authorized = ConvertTo-L00CUtcInstant $AuthorizationReceipt.AuthorizedUtc 'Open2 AuthorizedUtc'
    if ($authorized -ge $started -or $started -ge $completed -or $completed -gt [DateTimeOffset]::UtcNow) {
        throw 'Open2 timestamps must start strictly after authorization and complete before Finalize.'
    }
    return [ordered]@{ Started = $started; Completed = $completed }
}

$repositoryRootResolved = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$localRoot = Join-Path $repositoryRootResolved '.local'
$evidenceResolved = Get-FullPath $EvidenceDirectory
$controlDirectory = Join-Path $evidenceResolved 'campaign-control'
$initializePath = Join-Path $controlDirectory '01-initialize.json'
$recordPath = Join-Path $controlDirectory '02-record-open1.json'
$authorizePath = Join-Path $controlDirectory '03-authorize-open2.json'
$finalizePath = Join-Path $controlDirectory '04-finalize.json'

if ($Phase -eq 'Initialize') {
    if (Test-Path -LiteralPath $evidenceResolved) {
        throw "Initialize requires a new evidence directory: $evidenceResolved"
    }
    [void](Assert-PathWithin $evidenceResolved $localRoot 'Evidence directory')
    $testedCommitValue = Require-Value $TestedCommit 'TestedCommit'
    $assemblyResolved = (Resolve-Path -LiteralPath (Require-Value $AssemblyPath 'AssemblyPath')).Path
    $saveResolved = Assert-PathWithin (Require-Value $SaveDatabasePath 'SaveDatabasePath') $localRoot 'Save database'
    $snapshotResolved = Assert-PathWithin (Require-Value $SnapshotDatabasePath 'SnapshotDatabasePath') $evidenceResolved 'Snapshot database'
    $reportResolved = Assert-PathWithin (Require-Value $PersistenceReportPath 'PersistenceReportPath') $evidenceResolved 'Persistence report'
    $open1SessionResolved = Assert-PathWithin (Require-Value $Open1SessionPath 'Open1SessionPath') $evidenceResolved 'Open1 session'
    $open1LogResolved = Assert-PathWithin (Require-Value $Open1LogPath 'Open1LogPath') $evidenceResolved 'Open1 log'
    $open2SessionResolved = Assert-PathWithin (Require-Value $Open2SessionPath 'Open2SessionPath') $evidenceResolved 'Open2 session'
    $open2LogResolved = Assert-PathWithin (Require-Value $Open2LogPath 'Open2LogPath') $evidenceResolved 'Open2 log'
    foreach ($newPath in @($saveResolved, $snapshotResolved, $reportResolved, $open1SessionResolved, $open1LogResolved, $open2SessionResolved, $open2LogResolved)) {
        Assert-NewPath $newPath 'Fresh campaign artifact'
    }
    if ($testedCommitValue -notmatch '^[0-9a-f]{40}$') { throw 'TestedCommit must be a full lowercase commit id.' }
    $assemblyRecord = Get-L00CFileRecord $assemblyResolved
    $temporaryInitialize = [ordered]@{
        TestedCommit = $testedCommitValue
        Assembly = $assemblyRecord
    }
    Assert-Candidate $temporaryInitialize
    [void](New-Item -ItemType Directory -Path $controlDirectory)
    $initialized = [DateTimeOffset]::UtcNow
    $receipt = [ordered]@{
        SchemaVersion = 1
        PhaseSequence = 1
        Phase = 'Initialize'
        Status = 'READY_FOR_OPEN1'
        CampaignId = [Guid]::NewGuid().ToString('N')
        Nonce = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        TestedCommit = $testedCommitValue
        AssemblySha256 = $assemblyRecord.Sha256
        InitializedUtc = $initialized.ToString('o')
        PreviousReceiptId = '0' * 64
        PreviousReceiptFileSha256 = '0' * 64
        ControllerSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        AssemblyProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyResolved).ProductVersion
        Assembly = $assemblyRecord
        SaveDatabasePath = $saveResolved
        SnapshotDatabasePath = $snapshotResolved
        PersistenceReportPath = $reportResolved
        Open1SessionPath = $open1SessionResolved
        Open1LogPath = $open1LogResolved
        Open2SessionPath = $open2SessionResolved
        Open2LogPath = $open2LogResolved
        EvidenceDirectory = $evidenceResolved
        Limitation = 'Fresh tamper-evident operational chain; no resistance is claimed against an actor able to rewrite every artifact and receipt.'
    }
    [void](Add-ReceiptId $receipt)
    Write-NewJson $initializePath $receipt
    $receipt | ConvertTo-Json -Depth 8
    return
}

$initialize = Read-L00CCampaignReceipt $initializePath 'Initialize'
Assert-Candidate $initialize
if ([string]$initialize.ControllerSha256 -ne (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash) {
    throw 'Campaign controller changed after Initialize.'
}

if ($Phase -eq 'RecordOpen1') {
    foreach ($newPath in @($recordPath, $authorizePath, $finalizePath, [string]$initialize.Open2SessionPath, [string]$initialize.Open2LogPath)) {
        Assert-NewPath $newPath 'RecordOpen1 forbidden successor artifact'
    }
    foreach ($required in @([string]$initialize.SaveDatabasePath, [string]$initialize.Open1SessionPath, [string]$initialize.Open1LogPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "RecordOpen1 input is missing: $required" }
    }
    Assert-NewPath ([string]$initialize.SnapshotDatabasePath) 'Open1 snapshot'
    Assert-NewPath ([string]$initialize.PersistenceReportPath) 'Open1 persistence report'
    $open1 = Get-Content -LiteralPath ([string]$initialize.Open1SessionPath) -Raw | ConvertFrom-Json
    $open1Times = Assert-Open1Session $open1 $initialize
    $database = Get-Item -LiteralPath ([string]$initialize.SaveDatabasePath)
    $databaseCreated = [DateTimeOffset]$database.CreationTimeUtc
    $databaseWritten = [DateTimeOffset]$database.LastWriteTimeUtc
    if ($databaseCreated -lt $open1Times.Started -or $databaseCreated -gt $open1Times.Completed -or
        $databaseWritten -lt $open1Times.Started -or $databaseWritten -gt $open1Times.Completed) {
        throw 'Open1 database creation/write timestamps are not contained in the real open1 interval.'
    }
    $snapshotParent = Split-Path -Parent ([string]$initialize.SnapshotDatabasePath)
    if (-not (Test-Path -LiteralPath $snapshotParent -PathType Container)) { [void](New-Item -ItemType Directory -Path $snapshotParent) }
    [IO.File]::Copy([string]$initialize.SaveDatabasePath, [string]$initialize.SnapshotDatabasePath, $false)
    $oraclePath = Join-Path $PSScriptRoot 'Test-L00CPersistedDatabase.ps1'
    $oracleParameters = @{
        DatabasePath = [string]$initialize.SnapshotDatabasePath
        Open1LogPath = [string]$initialize.Open1LogPath
        AssemblyPath = [string]$initialize.Assembly.Path
        TestedCommit = [string]$initialize.TestedCommit
        CampaignId = [string]$initialize.CampaignId
        SavegameIdentifier = [string]$open1.SavegameIdentifier
        MarkerId = [string]$open1.MarkerId
        InstanceId = [string]$open1.InstanceId
        WorldRunId = [long]$open1.WorldRunId
        Open1EvidenceSequence = [int]$open1.EvidenceSequence
        Open1CompletedUtc = $open1Times.Completed.ToString('o')
        OutputPath = [string]$initialize.PersistenceReportPath
        FixtureChunkX = $FixtureChunkX
        FixtureChunkZ = $FixtureChunkZ
        WorldHeight = $WorldHeight
        ChunkSize = $ChunkSize
        Dimension = $Dimension
        GamePath = $GamePath
        RepositoryRoot = $repositoryRootResolved
    }
    [void](& $oraclePath @oracleParameters)
    if ($LASTEXITCODE -ne 0) { throw "Open1 persistence oracle failed with exit code $LASTEXITCODE." }
    $report = Get-Content -LiteralPath ([string]$initialize.PersistenceReportPath) -Raw | ConvertFrom-Json
    if ([string]$report.Status -ne 'PASS') { throw 'Open1 persistence report is not PASS.' }
    $recorded = [DateTimeOffset]::UtcNow
    $receipt = [ordered]@{
        SchemaVersion = 1
        PhaseSequence = 2
        Phase = 'RecordOpen1'
        Status = 'READY_FOR_OPEN2_AUTHORIZATION'
        CampaignId = [string]$initialize.CampaignId
        Nonce = [string]$initialize.Nonce
        TestedCommit = [string]$initialize.TestedCommit
        AssemblySha256 = [string]$initialize.AssemblySha256
        InitializedUtc = [string]$initialize.InitializedUtc
        PreviousReceiptId = [string]$initialize.ReceiptId
        PreviousReceiptFileSha256 = (Get-FileHash -LiteralPath $initializePath -Algorithm SHA256).Hash
        RecordedUtc = $recorded.ToString('o')
        Open1StartedUtc = $open1Times.Started.ToString('o')
        Open1CompletedUtc = $open1Times.Completed.ToString('o')
        Open1EvidenceSequence = [int]$open1.EvidenceSequence
        ExpectedOpen2EvidenceSequence = [int]$open1.EvidenceSequence + 1
        SavegameIdentifier = [string]$open1.SavegameIdentifier
        MarkerId = [string]$open1.MarkerId
        InstanceId = [string]$open1.InstanceId
        WorldRunId = [long]$open1.WorldRunId
        Open1Session = Get-L00CFileRecord ([string]$initialize.Open1SessionPath)
        Open1Log = Get-L00CFileRecord ([string]$initialize.Open1LogPath)
        SourceDatabase = Get-L00CFileRecord ([string]$initialize.SaveDatabasePath)
        SnapshotDatabase = Get-L00CFileRecord ([string]$initialize.SnapshotDatabasePath)
        PersistenceReport = Get-L00CFileRecord ([string]$initialize.PersistenceReportPath)
        PersistenceAttestationId = [string]$report.AttestationId
    }
    [void](Add-ReceiptId $receipt)
    Write-NewJson $recordPath $receipt
    $receipt | ConvertTo-Json -Depth 8
    return
}

$record = Read-L00CCampaignReceipt $recordPath 'RecordOpen1'
[void](Assert-ReceiptLink $record $initializePath 'Initialize')

if ($Phase -eq 'AuthorizeOpen2') {
    foreach ($newPath in @($authorizePath, $finalizePath, [string]$initialize.Open2SessionPath, [string]$initialize.Open2LogPath)) {
        Assert-NewPath $newPath 'AuthorizeOpen2 forbidden successor artifact'
    }
    [void](Assert-L00CFileRecord $record.Open1Session 'Recorded open1 session')
    [void](Assert-L00CFileRecord $record.Open1Log 'Recorded open1 log')
    [void](Assert-L00CFileRecord $record.SourceDatabase 'Open1 source database')
    [void](Assert-L00CFileRecord $record.SnapshotDatabase 'Open1 database snapshot')
    [void](Assert-L00CFileRecord $record.PersistenceReport 'Open1 persistence report')
    $open1 = Get-Content -LiteralPath ([string]$initialize.Open1SessionPath) -Raw | ConvertFrom-Json
    $report = Get-Content -LiteralPath ([string]$initialize.PersistenceReportPath) -Raw | ConvertFrom-Json
    $authorized = [DateTimeOffset]::UtcNow
    $preOpen2 = Assert-L00CPreOpen2Attestation `
        -Report $report `
        -ReportPath ([string]$initialize.PersistenceReportPath) `
        -DatabasePath ([string]$initialize.SnapshotDatabasePath) `
        -Open1LogPath ([string]$initialize.Open1LogPath) `
        -Open1Session $open1 `
        -CampaignId ([string]$initialize.CampaignId) `
        -TestedCommit ([string]$initialize.TestedCommit) `
        -AssemblySha256 ([string]$initialize.AssemblySha256) `
        -UpperBoundUtc $authorized.ToString('o')
    $receipt = [ordered]@{
        SchemaVersion = 1
        PhaseSequence = 3
        Phase = 'AuthorizeOpen2'
        Status = 'AUTHORIZED_OPEN2'
        CampaignId = [string]$initialize.CampaignId
        Nonce = [string]$initialize.Nonce
        TestedCommit = [string]$initialize.TestedCommit
        AssemblySha256 = [string]$initialize.AssemblySha256
        InitializedUtc = [string]$initialize.InitializedUtc
        PreviousReceiptId = [string]$record.ReceiptId
        PreviousReceiptFileSha256 = (Get-FileHash -LiteralPath $recordPath -Algorithm SHA256).Hash
        AuthorizedUtc = $authorized.ToString('o')
        ExpectedOpen2EvidenceSequence = [int]$record.ExpectedOpen2EvidenceSequence
        SavegameIdentifier = [string]$record.SavegameIdentifier
        MarkerId = [string]$record.MarkerId
        SourceDatabaseSha256 = [string]$record.SourceDatabase.Sha256
        Open1SessionSha256 = [string]$record.Open1Session.Sha256
        Open1LogSha256 = [string]$record.Open1Log.Sha256
        SnapshotDatabaseSha256 = [string]$record.SnapshotDatabase.Sha256
        PersistenceReportSha256 = [string]$record.PersistenceReport.Sha256
        PersistenceAttestationId = [string]$preOpen2.AttestationId
    }
    [void](Add-ReceiptId $receipt)
    Write-NewJson $authorizePath $receipt
    $receipt | ConvertTo-Json -Depth 8
    return
}

$authorization = Read-L00CCampaignReceipt $authorizePath 'AuthorizeOpen2'
[void](Assert-ReceiptLink $authorization $recordPath 'RecordOpen1')

if ($Phase -eq 'Finalize') {
    Assert-NewPath $finalizePath 'Finalize receipt'
    foreach ($required in @([string]$initialize.Open2SessionPath, [string]$initialize.Open2LogPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Finalize input is missing: $required" }
    }
    [void](Assert-L00CFileRecord $record.Open1Session 'Recorded open1 session')
    [void](Assert-L00CFileRecord $record.Open1Log 'Recorded open1 log')
    [void](Assert-L00CFileRecord $record.SnapshotDatabase 'Open1 database snapshot')
    [void](Assert-L00CFileRecord $record.PersistenceReport 'Open1 persistence report')
    $open1 = Get-Content -LiteralPath ([string]$initialize.Open1SessionPath) -Raw | ConvertFrom-Json
    $open2 = Get-Content -LiteralPath ([string]$initialize.Open2SessionPath) -Raw | ConvertFrom-Json
    $open2Times = Assert-Open2Session $open2 $open1 $authorization
    $open2Log = Get-Content -LiteralPath ([string]$initialize.Open2LogPath) -Raw
    $open2Instance = [regex]::Escape([string]$open2.InstanceId)
    $open2Marker = [regex]::Escape([string]$open2.MarkerId)
    $open2Save = [regex]::Escape([string]$open2.SavegameIdentifier)
    $open2Run = [regex]::Escape([string]$open2.WorldRunId)
    if ($open2Log -notmatch "L00C_ACTIVATED instance=$open2Instance marker=$open2Marker run=$open2Run open=2 isnew=False save=$open2Save " -or
        $open2Log -notmatch "L00C_PERSISTED_REOPEN_STABLE instance=$open2Instance marker=$open2Marker run=$open2Run loadpriority=0 transientrequests=0 refreshpasses=0 refreshedmapchunks=0 keeploaded=0 unload=0 fixturewrites=0 mapsnapshotwrites=0 callbacks=0 center=[0-9A-F]{64} halo=[0-9A-F]{64}") {
        throw 'Open2 log does not prove the bound persisted reopen session.'
    }
    $authorizationFile = Get-Item -LiteralPath $authorizePath
    if ([DateTimeOffset]$authorizationFile.CreationTimeUtc -ge $open2Times.Started -or
        [DateTimeOffset]$authorizationFile.LastWriteTimeUtc -ge $open2Times.Started) {
        throw 'Open2 began before its immutable authorization receipt existed.'
    }
    $report = Get-Content -LiteralPath ([string]$initialize.PersistenceReportPath) -Raw | ConvertFrom-Json
    $attestation = Assert-L00CPersistenceAttestation `
        -Report $report `
        -ReportPath ([string]$initialize.PersistenceReportPath) `
        -DatabasePath ([string]$initialize.SnapshotDatabasePath) `
        -Open1LogPath ([string]$initialize.Open1LogPath) `
        -Open1Session $open1 `
        -Open2Session $open2 `
        -CampaignId ([string]$initialize.CampaignId) `
        -TestedCommit ([string]$initialize.TestedCommit) `
        -AssemblySha256 ([string]$initialize.AssemblySha256)
    $finalized = [DateTimeOffset]::UtcNow
    $receipt = [ordered]@{
        SchemaVersion = 1
        PhaseSequence = 4
        Phase = 'Finalize'
        Status = 'COMPLETE'
        CampaignId = [string]$initialize.CampaignId
        Nonce = [string]$initialize.Nonce
        TestedCommit = [string]$initialize.TestedCommit
        AssemblySha256 = [string]$initialize.AssemblySha256
        InitializedUtc = [string]$initialize.InitializedUtc
        PreviousReceiptId = [string]$authorization.ReceiptId
        PreviousReceiptFileSha256 = (Get-FileHash -LiteralPath $authorizePath -Algorithm SHA256).Hash
        FinalizedUtc = $finalized.ToString('o')
        Open2StartedUtc = $open2Times.Started.ToString('o')
        Open2CompletedUtc = $open2Times.Completed.ToString('o')
        Open2EvidenceSequence = [int]$open2.EvidenceSequence
        SavegameIdentifier = [string]$open2.SavegameIdentifier
        MarkerId = [string]$open2.MarkerId
        Open2InstanceId = [string]$open2.InstanceId
        Open2WorldRunId = [long]$open2.WorldRunId
        Open2Session = Get-L00CFileRecord ([string]$initialize.Open2SessionPath)
        Open2Log = Get-L00CFileRecord ([string]$initialize.Open2LogPath)
        PersistenceAttestationId = [string]$attestation.AttestationId
    }
    [void](Add-ReceiptId $receipt)
    Write-NewJson $finalizePath $receipt
    $receipt | ConvertTo-Json -Depth 8
    return
}

throw "Unsupported campaign controller phase: $Phase"
