[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabasePath,

    [Parameter(Mandatory = $true)]
    [string]$Open1LogPath,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,

    [Parameter(Mandatory = $true)]
    [string]$TestedCommit,

    [Parameter(Mandatory = $true)]
    [string]$CampaignId,

    [Parameter(Mandatory = $true)]
    [string]$SavegameIdentifier,

    [Parameter(Mandatory = $true)]
    [string]$MarkerId,

    [Parameter(Mandatory = $true)]
    [string]$InstanceId,

    [Parameter(Mandatory = $true)]
    [long]$WorldRunId,

    [Parameter(Mandatory = $true)]
    [int]$Open1EvidenceSequence,

    [Parameter(Mandatory = $true)]
    [string]$Open1CompletedUtc,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [ValidateSet('RecordOpen1', 'FinalizeOpen2', 'PostReopen')]
    [string]$ControllerPhase = 'RecordOpen1',
    [int]$ExpectedOpenCount = 1,
    [bool]$ExpectedIsNew = $true,

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
Import-Module (Join-Path $PSScriptRoot 'L00CActiveShutdownEvidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'L00CPersistedDatabaseEvidence.psm1') -Force

$resolvedDatabase = (Resolve-Path -LiteralPath $DatabasePath).Path
$resolvedOpen1Log = (Resolve-Path -LiteralPath $Open1LogPath).Path
$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "Persistence attestation output already exists and cannot be replayed or overwritten: $resolvedOutput"
}
if ($TestedCommit -notmatch '^[0-9a-f]{40}$' -or $CampaignId -notmatch '^[0-9a-f]{32}$' -or
    $MarkerId -notmatch '^[0-9a-f]{32}$' -or $InstanceId -notmatch '^[0-9a-f]{32}$' -or
    $SavegameIdentifier -notmatch '^[0-9a-fA-F-]{36}$' -or $WorldRunId -le 0 -or
    $Open1EvidenceSequence -le 0 -or $ExpectedOpenCount -le 0) {
    throw 'Persistence attestation identity fields are malformed.'
}
$repositoryHead = (& git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $repositoryHead -ne $TestedCommit) {
    throw "Persistence attestation candidate '$TestedCommit' is not the repository HEAD '$repositoryHead'."
}
$trackedStatus = (& git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot status --porcelain=v1 --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or @($trackedStatus).Count -ne 0) {
    throw 'Persistence attestation requires a clean tracked worktree for the exact candidate commit.'
}
$completedInstant = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse(
    $Open1CompletedUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind,
    [ref]$completedInstant)) {
    throw "Open1CompletedUtc is not a round-trip timestamp: $Open1CompletedUtc"
}
$completedInstant = $completedInstant.ToUniversalTime()
$attestedInstant = [DateTimeOffset]::UtcNow
if ($completedInstant -gt $attestedInstant) {
    throw 'Persistence attestation cannot precede open1 completion.'
}
$assemblyProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedAssembly).ProductVersion
if ($assemblyProductVersion -ne "1.0.0+$TestedCommit") {
    throw "Candidate assembly ProductVersion '$assemblyProductVersion' does not match commit '$TestedCommit'."
}
$open1Log = Get-Content -LiteralPath $resolvedOpen1Log -Raw
$escapedInstance = [regex]::Escape($InstanceId)
$escapedMarker = [regex]::Escape($MarkerId)
$escapedSave = [regex]::Escape($SavegameIdentifier)
$expectedIsNewText = if ($ExpectedIsNew) { 'True' } else { 'False' }
if ($open1Log -notmatch "L00C_ACTIVATED instance=$escapedInstance marker=$escapedMarker run=$WorldRunId open=$ExpectedOpenCount isnew=$expectedIsNewText save=$escapedSave ") {
    throw 'Session log does not prove the bound instance, marker, world run, open count, and save.'
}
[void](Assert-L00CActiveShutdownLog -Log $open1Log -InstanceId $InstanceId -MarkerId $MarkerId `
    -WorldRunId $WorldRunId -OpenCount $ExpectedOpenCount -IsNew $ExpectedIsNew)
if ($WorldHeight -le 0 -or $ChunkSize -le 0 -or $WorldHeight % $ChunkSize -ne 0) {
    throw "WorldHeight must be a positive multiple of ChunkSize: height=$WorldHeight chunk=$ChunkSize."
}
if ($Dimension -lt 0 -or $Dimension -gt 31) {
    throw "Dimension must fit the five-bit persisted chunk key: $Dimension."
}

Import-L00CSqliteRuntime $GamePath

function Get-MapChunkPosition([int]$X, [int]$Z, [int]$RequestedDimension) {
    return ([int64]$Z -shl 27) -bor ([int64]$RequestedDimension -shl 22) -bor [int64]$X
}

function Get-ChunkPosition([int]$X, [int]$Y, [int]$Z, [int]$RequestedDimension) {
    return ([int64]$Y -shl 54) -bor (Get-MapChunkPosition $X $Z $RequestedDimension)
}

$connectionString = "Data Source=$resolvedDatabase;Mode=ReadOnly;Pooling=False"
$connection = [Microsoft.Data.Sqlite.SqliteConnection]::new($connectionString)
$rows = [Collections.Generic.List[object]]::new()
$mapChunkCount = 0
$chunkCount = 0
$missingMapChunks = [Collections.Generic.List[string]]::new()
$missingChunks = [Collections.Generic.List[string]]::new()
$chunksPerColumn = $WorldHeight / $ChunkSize
$integrityCheck = $null
$journalMode = $null
$envelope = $null

foreach ($sidecar in @($resolvedDatabase + '-wal', $resolvedDatabase + '-shm')) {
    if (Test-Path -LiteralPath $sidecar) {
        throw "Persisted database snapshot is not autonomous because a SQLite sidecar exists: $sidecar"
    }
}

try {
    $connection.Open()
    $query = $connection.CreateCommand()
    $query.CommandText = 'PRAGMA integrity_check'
    $integrityCheck = [string]$query.ExecuteScalar()
    $query.CommandText = 'PRAGMA journal_mode'
    $journalMode = [string]$query.ExecuteScalar()
    if ($integrityCheck -ne 'ok' -or $journalMode -eq 'wal') {
        throw "Persisted database snapshot is not autonomous: integrity=$integrityCheck journal=$journalMode."
    }
    $envelope = Get-L00CPersistedMarkerEnvelope -Connection $connection -AssemblyPath $resolvedAssembly `
        -GamePath $GamePath -SavegameIdentifier $SavegameIdentifier -MarkerId $MarkerId `
        -ExpectedOpenCount $ExpectedOpenCount -FixtureChunkX $FixtureChunkX -FixtureChunkZ $FixtureChunkZ `
        -ChunkSize $ChunkSize -WorldHeight $WorldHeight
    $query.CommandText = 'SELECT length(data) FROM {0} WHERE position = $position'
    $positionParameter = $query.CreateParameter()
    $positionParameter.ParameterName = '$position'
    [void]$query.Parameters.Add($positionParameter)

    for ($deltaZ = -1; $deltaZ -le 1; $deltaZ++) {
        for ($deltaX = -1; $deltaX -le 1; $deltaX++) {
            $x = $FixtureChunkX + $deltaX
            $z = $FixtureChunkZ + $deltaZ
            $mapPosition = Get-MapChunkPosition $x $z $Dimension
            $query.CommandText = 'SELECT length(data) FROM mapchunk WHERE position = $position'
            $positionParameter.Value = $mapPosition
            $mapLengthValue = $query.ExecuteScalar()
            $mapPresent = $null -ne $mapLengthValue -and
                $mapLengthValue -ne [DBNull]::Value -and
                [int64]$mapLengthValue -gt 0
            if ($mapPresent) {
                $mapChunkCount++
            }
            else {
                [void]$missingMapChunks.Add("$x,$z")
            }

            $presentY = [Collections.Generic.List[int]]::new()
            for ($chunkY = 0; $chunkY -lt $chunksPerColumn; $chunkY++) {
                $query.CommandText = 'SELECT length(data) FROM chunk WHERE position = $position'
                $positionParameter.Value = Get-ChunkPosition $x $chunkY $z $Dimension
                $chunkLengthValue = $query.ExecuteScalar()
                if ($null -ne $chunkLengthValue -and
                    $chunkLengthValue -ne [DBNull]::Value -and
                    [int64]$chunkLengthValue -gt 0) {
                    $chunkCount++
                    [void]$presentY.Add($chunkY)
                }
                else {
                    [void]$missingChunks.Add("$x,$chunkY,$z")
                }
            }

            [void]$rows.Add([ordered]@{
                X = $x
                Z = $z
                MapPosition = $mapPosition
                MapPresent = $mapPresent
                MapBytes = if ($mapPresent) { [int64]$mapLengthValue } else { 0 }
                ChunkYs = @($presentY)
                ChunkCount = $presentY.Count
            })
        }
    }
}
finally {
    $connection.Close()
    $connection.Dispose()
}

$expectedMapChunks = 9
$expectedChunks = $expectedMapChunks * $chunksPerColumn
$status = if ($mapChunkCount -eq $expectedMapChunks -and $chunkCount -eq $expectedChunks) { 'PASS' } else { 'FAIL' }
$result = [ordered]@{
    TestId = 'L00-C-PERSISTED-DATABASE'
    Status = $status
    SchemaVersion = 2
    ControllerPhase = $ControllerPhase
    EvidenceOrder = if ($ControllerPhase -eq 'RecordOpen1') { 'open1-complete<attestation<open2-start' } else { 'reopen-complete<database-attestation<next-open' }
    CampaignId = $CampaignId
    TestedCommit = $TestedCommit
    OracleSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    AssemblySha256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash
    AssemblyProductVersion = $assemblyProductVersion
    SavegameIdentifier = $SavegameIdentifier
    MarkerId = $MarkerId
    InstanceId = $InstanceId
    WorldRunId = $WorldRunId
    ExpectedOpenCount = $ExpectedOpenCount
    ExpectedIsNew = $ExpectedIsNew
    Open1EvidenceSequence = $Open1EvidenceSequence
    ExpectedOpen2EvidenceSequence = $Open1EvidenceSequence + 1
    Open1CompletedUtc = $completedInstant.ToString('o')
    AttestedUtc = $attestedInstant.ToString('o')
    DatabasePath = $resolvedDatabase
    DatabaseSha256 = (Get-FileHash -LiteralPath $resolvedDatabase -Algorithm SHA256).Hash
    DatabaseLength = (Get-Item -LiteralPath $resolvedDatabase).Length
    Open1LogSha256 = (Get-FileHash -LiteralPath $resolvedOpen1Log -Algorithm SHA256).Hash
    Open1LogLength = (Get-Item -LiteralPath $resolvedOpen1Log).Length
    OpenMode = 'ReadOnly'
    Autonomous = $true
    IntegrityCheck = $integrityCheck
    JournalMode = $journalMode
    Packing = '(y<<54)|(z<<27)|(dimension<<22)|x'
    Dimension = $Dimension
    FixtureChunkX = $FixtureChunkX
    FixtureChunkZ = $FixtureChunkZ
    ChunkSize = $ChunkSize
    WorldHeight = $WorldHeight
    ChunksPerColumn = $chunksPerColumn
    ExpectedMapChunks = $expectedMapChunks
    ActualMapChunks = $mapChunkCount
    ExpectedChunks = $expectedChunks
    ActualChunks = $chunkCount
    MissingMapChunks = @($missingMapChunks)
    MissingChunks = @($missingChunks)
    Columns = @($rows)
    MarkerEnvelope = [ordered]@{
        MarkerId = $envelope.MarkerId
        SavegameIdentifier = $envelope.SavegameIdentifier
        Version = $envelope.MarkerVersion
        OpenCount = $envelope.OpenCount
        PayloadLength = $envelope.PayloadLength
        PayloadSha256 = $envelope.PayloadSha256
        MapFootprintVersion = $envelope.MapFootprintVersion
        MapFootprintMarkerId = $envelope.MapFootprintMarkerId
        MapFootprintSavegameIdentifier = $envelope.MapFootprintSavegameIdentifier
        MapFootprintChunkX = $envelope.MapFootprintChunkX
        MapFootprintChunkZ = $envelope.MapFootprintChunkZ
        MapFootprintChunkSize = $envelope.MapFootprintChunkSize
        MapFootprintWorldHeight = $envelope.MapFootprintWorldHeight
        MapFootprintMapChunks = $envelope.MapFootprintMapChunks
        MapFootprintCoordinates = @($envelope.MapFootprintCoordinates)
        MapFootprintSha256 = $envelope.MapFootprintSha256
    }
}
$attestationModule = Join-Path $PSScriptRoot 'L00CPersistenceAttestation.psm1'
Import-Module $attestationModule -Force
$result.Add('AttestationId', (Get-L00CAttestationId $result))

$json = $result | ConvertTo-Json -Depth 6
$directory = Split-Path -Parent $resolvedOutput
if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    [void](New-Item -ItemType Directory -Path $directory)
}
Set-Content -LiteralPath $resolvedOutput -Value $json -Encoding UTF8 -NoNewline

$json
if ($status -ne 'PASS') {
    throw "Persisted footprint is incomplete: mapchunks=$mapChunkCount/$expectedMapChunks chunks=$chunkCount/$expectedChunks."
}
