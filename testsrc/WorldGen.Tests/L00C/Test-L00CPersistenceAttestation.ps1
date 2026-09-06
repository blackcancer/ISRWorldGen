[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'L00CPersistenceAttestation.psm1') -Force

$evidenceValidator = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-L00CEvidence.ps1') -Raw
$databaseOracle = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-L00CPersistedDatabase.ps1') -Raw
foreach ($fragment in @(
    'Assert-L00CPersistenceAttestation',
    'EvidenceSequence',
    'StartedUtc',
    'CompletedUtc',
    'Open1DatabaseReport',
    'Open1Database',
    'CampaignId',
    'WorldRunId'
)) {
    if (-not $evidenceValidator.Contains($fragment)) {
        throw "Final evidence validator is not wired to the persistence attestation: $fragment"
    }
}
foreach ($fragment in @(
    '[Parameter(Mandatory = $true)]',
    'Persistence attestation output already exists',
    'clean tracked worktree',
    'AssemblyProductVersion',
    'Open1LogSha256',
    'OracleSha256',
    'Open1EvidenceSequence',
    'Get-L00CAttestationId'
)) {
    if (-not $databaseOracle.Contains($fragment)) {
        throw "Pre-open2 database oracle is missing an attestation guard: $fragment"
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("isrworldgen-l00c-attestation-" + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $temporaryRoot)
$databasePath = Join-Path $temporaryRoot 'open1.vcdbs'
$logPath = Join-Path $temporaryRoot 'open1.log'
$reportPath = Join-Path $temporaryRoot 'open1-report.json'

$base = [DateTimeOffset]::UtcNow.AddMinutes(-20)
$open1Start = $base
$open1Complete = $base.AddMinutes(2)
$attested = $base.AddMinutes(3)
$open2Start = $base.AddMinutes(4)
$open2Complete = $base.AddMinutes(6)
$campaign = '11111111111111111111111111111111'
$commit = '2222222222222222222222222222222222222222'
$assemblyHash = 'A' * 64
$save = '33333333-3333-3333-3333-333333333333'
$marker = '44444444444444444444444444444444'
$instance = '55555555555555555555555555555555'

[IO.File]::WriteAllBytes($databasePath, [byte[]](1..64))
[IO.File]::WriteAllText($logPath, 'bound-open1-log', [Text.UTF8Encoding]::new($false))
[IO.File]::SetCreationTimeUtc($databasePath, $open1Complete.UtcDateTime)
[IO.File]::SetLastWriteTimeUtc($databasePath, $open1Complete.UtcDateTime)
[IO.File]::SetCreationTimeUtc($logPath, $open1Complete.UtcDateTime)
[IO.File]::SetLastWriteTimeUtc($logPath, $open1Complete.UtcDateTime)

$open1 = [pscustomobject]@{
    EvidenceSequence = 3
    StartedUtc = $open1Start.ToString('o')
    CompletedUtc = $open1Complete.ToString('o')
    WorldRole = 'activated-primary'
    SavegameIdentifier = $save
    MarkerId = $marker
    InstanceId = $instance
    WorldRunId = 1
    OpenCount = 1
    IsNew = $true
}
$open2 = [pscustomobject]@{
    EvidenceSequence = 4
    StartedUtc = $open2Start.ToString('o')
    CompletedUtc = $open2Complete.ToString('o')
    WorldRole = 'activated-primary'
    SavegameIdentifier = $save
    MarkerId = $marker
    InstanceId = '66666666666666666666666666666666'
    WorldRunId = 1
    OpenCount = 2
    IsNew = $false
}
$report = [pscustomobject][ordered]@{
    SchemaVersion = 1
    ControllerPhase = 'RecordOpen1'
    EvidenceOrder = 'open1-complete<attestation<open2-start'
    CampaignId = $campaign
    TestedCommit = $commit
    OracleSha256 = 'C' * 64
    AssemblySha256 = $assemblyHash
    SavegameIdentifier = $save
    MarkerId = $marker
    InstanceId = $instance
    WorldRunId = 1
    Open1EvidenceSequence = 3
    ExpectedOpen2EvidenceSequence = 4
    Open1CompletedUtc = $open1Complete.ToString('o')
    DatabaseSha256 = (Get-FileHash -LiteralPath $databasePath -Algorithm SHA256).Hash
    DatabaseLength = (Get-Item -LiteralPath $databasePath).Length
    Open1LogSha256 = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
    Open1LogLength = (Get-Item -LiteralPath $logPath).Length
    AttestedUtc = $attested.ToString('o')
    AttestationId = ''
}
$report.AttestationId = Get-L00CAttestationId $report
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8 -NoNewline
[IO.File]::SetCreationTimeUtc($reportPath, $attested.UtcDateTime)
[IO.File]::SetLastWriteTimeUtc($reportPath, $attested.UtcDateTime)

function Invoke-Validation($CandidateReport, $CandidateReportPath, $CandidateOpen1, $CandidateOpen2, $CandidateCampaign, $CandidateCommit, $CandidateAssembly) {
    return Assert-L00CPersistenceAttestation `
        -Report $CandidateReport `
        -ReportPath $CandidateReportPath `
        -DatabasePath $databasePath `
        -Open1LogPath $logPath `
        -Open1Session $CandidateOpen1 `
        -Open2Session $CandidateOpen2 `
        -CampaignId $CandidateCampaign `
        -TestedCommit $CandidateCommit `
        -AssemblySha256 $CandidateAssembly
}

function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    try {
        [void](& $Action)
    }
    catch {
        return
    }
    throw "$Label was unexpectedly accepted."
}

try {
    $baseline = Invoke-Validation $report $reportPath $open1 $open2 $campaign $commit $assemblyHash
    if ($baseline.Status -ne 'PASS') {
        throw 'The valid pre-open2 attestation did not pass.'
    }

    $staleReport = $report | ConvertTo-Json -Depth 4 | ConvertFrom-Json
    $staleReport.AttestedUtc = $open1Start.ToString('o')
    $staleReport.AttestationId = Get-L00CAttestationId $staleReport
    Assert-Rejected { Invoke-Validation $staleReport $reportPath $open1 $open2 $campaign $commit $assemblyHash } 'Stale pre-open1 report'

    $copiedPath = Join-Path $temporaryRoot 'copied-after-open2.json'
    Copy-Item -LiteralPath $reportPath -Destination $copiedPath
    [IO.File]::SetCreationTimeUtc($copiedPath, $open2Start.AddSeconds(1).UtcDateTime)
    [IO.File]::SetLastWriteTimeUtc($copiedPath, $open2Start.AddSeconds(1).UtcDateTime)
    Assert-Rejected { Invoke-Validation $report $copiedPath $open1 $open2 $campaign $commit $assemblyHash } 'Report copied after open2'

    $foreignOpen1 = $open1 | ConvertTo-Json -Depth 4 | ConvertFrom-Json
    $foreignOpen1.SavegameIdentifier = '77777777-7777-7777-7777-777777777777'
    Assert-Rejected { Invoke-Validation $report $reportPath $foreignOpen1 $open2 $campaign $commit $assemblyHash } 'Foreign-world replay'

    Assert-Rejected { Invoke-Validation $report $reportPath $open1 $open2 ('8' * 32) $commit $assemblyHash } 'Foreign campaign replay'
    Assert-Rejected { Invoke-Validation $report $reportPath $open1 $open2 $campaign ('9' * 40) $assemblyHash } 'Foreign candidate replay'
    Assert-Rejected { Invoke-Validation $report $reportPath $open1 $open2 $campaign $commit ('B' * 64) } 'Foreign assembly replay'

    [ordered]@{
        TestId = 'L00-C-PERSISTENCE-ATTESTATION'
        Status = 'PASS'
        BaselineAttestationId = $baseline.AttestationId
        StaleRejected = $true
        CopyAfterOpen2Rejected = $true
        ForeignWorldRejected = $true
        ForeignCampaignRejected = $true
        ForeignCandidateRejected = $true
        ForeignAssemblyRejected = $true
    } | ConvertTo-Json -Depth 4
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
