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

$resolvedDatabase = (Resolve-Path -LiteralPath $DatabasePath).Path
$resolvedOpen1Log = (Resolve-Path -LiteralPath $Open1LogPath).Path
$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "Persistence attestation output already exists and cannot be replayed or overwritten: $resolvedOutput"
}
if ($TestedCommit -notmatch '^[0-9a-f]{40}$' -or $CampaignId -notmatch '^[0-9a-f]{32}$' -or
    $MarkerId -notmatch '^[0-9a-f]{32}$' -or $InstanceId -notmatch '^[0-9a-f]{32}$' -or
    $SavegameIdentifier -notmatch '^[0-9a-fA-F-]{36}$' -or $WorldRunId -le 0 -or $Open1EvidenceSequence -le 0) {
    throw 'Persistence attestation identity fields are malformed.'
}
$repositoryHead = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $repositoryHead -ne $TestedCommit) {
    throw "Persistence attestation candidate '$TestedCommit' is not the repository HEAD '$repositoryHead'."
}
$trackedStatus = (& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=no)
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
if ($open1Log -notmatch "L00C_ACTIVATED instance=$escapedInstance marker=$escapedMarker run=$WorldRunId open=1 isnew=True save=$escapedSave " -or
    $open1Log -notmatch "L00C_TICKS_STABLE instance=$escapedInstance marker=$escapedMarker run=$WorldRunId ticks=40 " -or
    $open1Log -notmatch 'World saved!') {
    throw 'Open1 log does not prove the bound instance, marker, world run, save, stable fixture, and completed save.'
}
if ($WorldHeight -le 0 -or $ChunkSize -le 0 -or $WorldHeight % $ChunkSize -ne 0) {
    throw "WorldHeight must be a positive multiple of ChunkSize: height=$WorldHeight chunk=$ChunkSize."
}
if ($Dimension -lt 0 -or $Dimension -gt 31) {
    throw "Dimension must fit the five-bit persisted chunk key: $Dimension."
}

$sqliteDirectory = Join-Path $GamePath 'Lib'
$sqliteAssemblies = @(
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.e_sqlite3.dll',
    'SQLitePCLRaw.batteries_v2.dll',
    'Microsoft.Data.Sqlite.dll'
)
$nativeSqlite = Join-Path $sqliteDirectory 'e_sqlite3.dll'
foreach ($path in @($nativeSqlite) + @($sqliteAssemblies | ForEach-Object { Join-Path $sqliteDirectory $_ })) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required local SQLite runtime is missing: $path"
    }
}

[void][Runtime.InteropServices.NativeLibrary]::Load($nativeSqlite)
foreach ($assembly in $sqliteAssemblies) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $sqliteDirectory $assembly))
}
[SQLitePCL.Batteries_V2]::Init()

function Get-MapChunkPosition([int]$X, [int]$Z, [int]$RequestedDimension) {
    return ([int64]$Z -shl 27) -bor ([int64]$RequestedDimension -shl 22) -bor [int64]$X
}

function Get-ChunkPosition([int]$X, [int]$Y, [int]$Z, [int]$RequestedDimension) {
    return ([int64]$Y -shl 54) -bor (Get-MapChunkPosition $X $Z $RequestedDimension)
}

$connectionString = "Data Source=$resolvedDatabase;Mode=ReadOnly"
$connection = [Microsoft.Data.Sqlite.SqliteConnection]::new($connectionString)
$rows = [Collections.Generic.List[object]]::new()
$mapChunkCount = 0
$chunkCount = 0
$missingMapChunks = [Collections.Generic.List[string]]::new()
$missingChunks = [Collections.Generic.List[string]]::new()
$chunksPerColumn = $WorldHeight / $ChunkSize

try {
    $connection.Open()
    $query = $connection.CreateCommand()
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
    SchemaVersion = 1
    EvidenceOrder = 'open1-complete<attestation<open2-start'
    CampaignId = $CampaignId
    TestedCommit = $TestedCommit
    OracleSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    AssemblySha256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash
    AssemblyProductVersion = $assemblyProductVersion
    SavegameIdentifier = $SavegameIdentifier
    MarkerId = $MarkerId
    InstanceId = $InstanceId
    WorldRunId = $WorldRunId
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
