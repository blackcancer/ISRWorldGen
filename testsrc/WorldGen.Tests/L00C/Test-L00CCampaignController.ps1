[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$controllerPath = Join-Path $PSScriptRoot 'Invoke-L00CCampaignController.ps1'
$evidenceValidatorPath = Join-Path $PSScriptRoot 'Test-L00CEvidence.ps1'
$probeSourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
foreach ($path in @($controllerPath, $evidenceValidatorPath, $probeSourcePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Campaign controller test input is missing: $path" }
}
$probeSourceLines = @(Get-Content -LiteralPath $probeSourcePath)
$activatedSourceLine = @($probeSourceLines | Where-Object { $_.Contains('L00C_ACTIVATED') -and $_.Contains('isnew=False') })
$stableSourceLine = @($probeSourceLines | Where-Object { $_.Contains('L00C_PERSISTED_REOPEN_STABLE') })
if ($activatedSourceLine.Count -ne 1 -or $stableSourceLine.Count -ne 1) {
    throw 'Production log format extraction requires one persisted activated line and one persisted stable line.'
}
function Get-ProductionTemplate([string]$SourceLine, [string]$Marker) {
    $match = [regex]::Match($SourceLine, 'Log\(\$"(?<template>L00C_[^"]+)"\)(?:;|,)')
    if (-not $match.Success -or -not $match.Groups['template'].Value.StartsWith($Marker, [StringComparison]::Ordinal)) {
        throw "Unable to extract production template for $Marker."
    }
    return $match.Groups['template'].Value
}
function Get-TemplateTokens([string]$Template) {
    return @([regex]::Matches($Template, '\{(?<token>[^}]+)\}') | ForEach-Object { $_.Groups['token'].Value })
}
function Expand-ProductionTemplate([string]$Template, [hashtable]$Values) {
    return [regex]::Replace($Template, '\{(?<token>[^}]+)\}', {
        param($match)
        $token = $match.Groups['token'].Value
        if (-not $Values.ContainsKey($token)) { throw "Production log fixture lacks token value: $token" }
        return [string]$Values[$token]
    })
}
$activatedTemplate = Get-ProductionTemplate $activatedSourceLine[0] 'L00C_ACTIVATED'
$stableTemplate = Get-ProductionTemplate $stableSourceLine[0] 'L00C_PERSISTED_REOPEN_STABLE'
$expectedActivatedTokens = @('instanceId', 'marker!.MarkerId', 'runId', 'marker.OpenCount', 'saveGame.SavegameIdentifier', 'config.FixtureChunkX', 'config.FixtureChunkZ')
$expectedStableTokens = @('instanceId', 'marker!.MarkerId', 'runId', 'priorityLoads', 'transientRequests', 'refreshPasses', 'refreshedMapChunks', 'fixtureWrites', 'mapSnapshotWrites', 'fixtureCallbackCount', 'persistedSnapshot.Fixture.Hash', 'persistedSnapshot.Halo.Hash')
if (((Get-TemplateTokens $activatedTemplate) -join '|') -ne ($expectedActivatedTokens -join '|') -or
    ((Get-TemplateTokens $stableTemplate) -join '|') -ne ($expectedStableTokens -join '|') -or
    $stableTemplate.Contains(' open=', [StringComparison]::Ordinal)) {
    throw 'Campaign controller test fixture is not aligned with the production persisted-reopen log schema.'
}
$controllerSource = Get-Content -LiteralPath $controllerPath -Raw
$validatorSource = Get-Content -LiteralPath $evidenceValidatorPath -Raw
foreach ($fragment in @('Initialize', 'RecordOpen1', 'AuthorizeOpen2', 'Finalize', 'Test-L00CPersistedDatabase.ps1', 'New-L00CAutonomousDatabaseSnapshot', 'Open2SnapshotDatabase', 'Open2PersistenceReport', 'CreateNew', 'ControllerPhaseBefore', 'ControllerPhaseAfter')) {
    if (-not $controllerSource.Contains($fragment)) { throw "Campaign controller is missing required production wiring: $fragment" }
}
foreach ($fragment in @('CampaignControl', 'RecordOpen1', 'AuthorizeOpen2', 'Open2Database', 'Open2DatabaseReport', 'ControllerPhaseBefore', 'ControllerPhaseAfter')) {
    if (-not $validatorSource.Contains($fragment)) { throw "Final evidence validator is missing campaign phase wiring: $fragment" }
}

$head = (& git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve repository HEAD.' }
$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) { throw 'Debug candidate assembly is missing.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion -ne "1.0.0+$head") {
    throw 'Debug candidate must be rebuilt from the exact clean HEAD before testing the campaign controller.'
}

$selfTestRoot = Join-Path $RepositoryRoot ('.local\L00C\campaign-controller-selftest\' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $selfTestRoot)

$sqliteDirectory = 'D:\Jeux\Vintagestory\Lib'
[void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $sqliteDirectory 'e_sqlite3.dll'))
foreach ($assembly in @('SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'SQLitePCLRaw.batteries_v2.dll', 'Microsoft.Data.Sqlite.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $sqliteDirectory $assembly))
}
[SQLitePCL.Batteries_V2]::Init()
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path 'D:\Jeux\Vintagestory' $dependency))
}
$candidateAssembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$script:markerType = $candidateAssembly.GetType('ISRWorldGen.WorldgenProbe.ProbeMarker', $false)
$script:footprintType = $candidateAssembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapFootprintSnapshot', $false)
$script:mapSnapshotType = $candidateAssembly.GetType('ISRWorldGen.WorldgenProbe.PersistedMapChunkSnapshot', $false)
if ($null -eq $script:markerType -or $null -eq $script:footprintType -or $null -eq $script:mapSnapshotType) {
    throw 'Campaign database fixture cannot load the real Debug marker-envelope types.'
}

function Write-NewJson([string]$Path, $Value) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [void](New-Item -ItemType Directory -Path $parent) }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($Value | ConvertTo-Json -Depth 6)) } finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}

function ConvertFrom-L00CInvariantRoundTripUtc([string]$Value) {
    $instant = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$instant)) {
        throw "L00-C fixture timestamp is not invariant round-trip UTC: $Value"
    }
    return $instant.ToUniversalTime()
}

function Get-StrictlyLaterUtc([DateTimeOffset]$Boundary) {
    do { $candidate = [DateTimeOffset]::UtcNow } while ($candidate -le $Boundary)
    return $candidate
}

function Assert-FrFrInvariantTimestampRegression {
    $previous = [Globalization.CultureInfo]::CurrentCulture
    try {
        [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
        $started = ConvertFrom-L00CInvariantRoundTripUtc '2026-09-10T01:02:03.0000000+00:00'
        $completed = ConvertFrom-L00CInvariantRoundTripUtc '2026-09-10T01:02:03.0000002+00:00'
        if ($started.Year -ne 2026 -or $started.Month -ne 9 -or $started.Day -ne 10 -or $started -ge $completed) {
            throw 'Invariant ISO parsing did not preserve September 10 and strict Start/Complete ordering under fr-FR.'
        }
    }
    finally { [Globalization.CultureInfo]::CurrentCulture = $previous }
}

function Write-Varint([IO.Stream]$Stream, [int]$Value) {
    [uint32]$remaining = $Value
    do {
        [byte]$current = $remaining -band 0x7f
        $remaining = $remaining -shr 7
        if ($remaining -ne 0) { $current = $current -bor 0x80 }
        $Stream.WriteByte($current)
    } while ($remaining -ne 0)
}

function Assert-WalProofInAutonomousSnapshot([string]$SnapshotPath) {
    if ((Test-Path -LiteralPath ($SnapshotPath + '-wal')) -or (Test-Path -LiteralPath ($SnapshotPath + '-shm'))) {
        throw 'WAL proof snapshot is not autonomous because a SQLite sidecar is present.'
    }

    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$SnapshotPath;Mode=ReadOnly;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'PRAGMA integrity_check'
            if ([string]$command.ExecuteScalar() -ne 'ok') { throw 'WAL proof snapshot failed integrity_check.' }
            $command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='walproof'"
            if ([int64]$command.ExecuteScalar() -ne 1) { throw 'WAL proof table is absent from the autonomous snapshot.' }
            $command.CommandText = 'SELECT COUNT(*) FROM walproof'
            if ([int64]$command.ExecuteScalar() -ne 1) { throw 'WAL proof snapshot does not contain exactly one proof row.' }
            $command.CommandText = 'SELECT COUNT(*) FROM walproof WHERE value=1'
            if ([int64]$command.ExecuteScalar() -ne 1) { throw 'WAL proof value=1 was not preserved in the autonomous snapshot.' }
        }
        finally { $command.Dispose() }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }
}

function New-MarkerPayload([int]$OpenCount) {
    $mapListType = [Collections.Generic.List``1].MakeGenericType($script:mapSnapshotType)
    $maps = [Activator]::CreateInstance($mapListType)
    for ($x = 31989; $x -le 31991; $x++) {
        for ($z = 31989; $z -le 31991; $z++) {
            $map = [Activator]::CreateInstance($script:mapSnapshotType)
            $script:mapSnapshotType.GetProperty('X').SetValue($map, $x)
            $script:mapSnapshotType.GetProperty('Z').SetValue($map, $z)
            $script:mapSnapshotType.GetProperty('WorldGenTerrainHeightMap').SetValue($map, [ushort[]](1..1024 | ForEach-Object { 64 }))
            $script:mapSnapshotType.GetProperty('RainHeightMap').SetValue($map, [ushort[]](1..1024 | ForEach-Object { 67 }))
            $script:mapSnapshotType.GetProperty('TopRockIdMap').SetValue($map, [int[]](1..1024 | ForEach-Object { 11165 }))
            $script:mapSnapshotType.GetProperty('YMax').SetValue($map, [ushort]67)
            [void]$maps.Add($map)
        }
    }
    $footprint = $script:footprintType.GetMethod('Create').Invoke($null, @(
        '44444444444444444444444444444444',
        '33333333-3333-3333-3333-333333333333',
        31990, 31990, 32, 256, $maps))
    $marker = [Activator]::CreateInstance($script:markerType)
    $script:markerType.GetProperty('MarkerId').SetValue($marker, '44444444444444444444444444444444')
    $script:markerType.GetProperty('SavegameIdentifier').SetValue($marker, '33333333-3333-3333-3333-333333333333')
    $script:markerType.GetProperty('Version').SetValue($marker, 'l00c-flat-v2-map-snapshot')
    $script:markerType.GetProperty('OpenCount').SetValue($marker, $OpenCount)
    $script:markerType.GetProperty('MapFootprint').SetValue($marker, $footprint)
    return ,[Text.Json.JsonSerializer]::SerializeToUtf8Bytes($marker, $script:markerType)
}

function New-GamedataBlob([int]$OpenCount) {
    [byte[]]$key = [Text.Encoding]::UTF8.GetBytes('isrworldgen:l00c:marker:v1')
    [byte[]]$payload = New-MarkerPayload $OpenCount
    $stream = [IO.MemoryStream]::new()
    try {
        $stream.WriteByte(0x0a)
        Write-Varint $stream $key.Length
        $stream.Write($key, 0, $key.Length)
        $stream.WriteByte(0x12)
        Write-Varint $stream $payload.Length
        $stream.Write($payload, 0, $payload.Length)
        return ,$stream.ToArray()
    }
    finally { $stream.Dispose() }
}

function New-CompleteDatabase([string]$Path, [int]$OpenCount = 1) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [void](New-Item -ItemType Directory -Path $parent) }
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$Path;Mode=ReadWriteCreate;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'CREATE TABLE mapchunk(position INTEGER PRIMARY KEY, data BLOB NOT NULL); CREATE TABLE chunk(position INTEGER PRIMARY KEY, data BLOB NOT NULL); CREATE TABLE gamedata(savegameid INTEGER PRIMARY KEY, data BLOB NOT NULL);'
        [void]$command.ExecuteNonQuery()
        $command.CommandText = 'INSERT INTO gamedata(savegameid, data) VALUES(1, $data)'
        [void]$command.Parameters.AddWithValue('$data', (New-GamedataBlob $OpenCount))
        [void]$command.ExecuteNonQuery()
        $command.Parameters.Clear()
        $transaction = $connection.BeginTransaction()
        try {
            $command.Transaction = $transaction
            $position = $command.CreateParameter()
            $position.ParameterName = '$position'
            [void]$command.Parameters.Add($position)
            for ($z = 31989; $z -le 31991; $z++) {
                for ($x = 31989; $x -le 31991; $x++) {
                    $command.CommandText = 'INSERT INTO mapchunk(position, data) VALUES($position, zeroblob(64))'
                    $position.Value = ([int64]$z -shl 27) -bor [int64]$x
                    [void]$command.ExecuteNonQuery()
                    $command.CommandText = 'INSERT INTO chunk(position, data) VALUES($position, zeroblob(64))'
                    for ($y = 0; $y -lt 8; $y++) {
                        $position.Value = ([int64]$y -shl 54) -bor ([int64]$z -shl 27) -bor [int64]$x
                        [void]$command.ExecuteNonQuery()
                    }
                }
            }
            $transaction.Commit()
        }
        finally { $transaction.Dispose() }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }
}

function Set-DatabaseMarkerOpenCount([string]$Path, [int]$OpenCount) {
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$Path;Mode=ReadWrite;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'UPDATE gamedata SET data = $data WHERE savegameid = 1'
            [void]$command.Parameters.AddWithValue('$data', (New-GamedataBlob $OpenCount))
            if ($command.ExecuteNonQuery() -ne 1) { throw 'Synthetic campaign marker update did not affect exactly one row.' }
        }
        finally { $command.Dispose() }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }
}

function New-Campaign([string]$Name) {
    $root = Join-Path $selfTestRoot $Name
    [void](New-Item -ItemType Directory -Path $root)
    $evidence = Join-Path $root 'evidence'
    $fixture = [ordered]@{
        Root = $root
        Evidence = $evidence
        Database = Join-Path $root 'data\Saves\fresh.vcdbs'
        Snapshot = Join-Path $evidence 'artifacts\open1.vcdbs'
        Report = Join-Path $evidence 'artifacts\open1-database.json'
        Open1Session = Join-Path $evidence 'sessions\open1\session.json'
        Open1Log = Join-Path $evidence 'sessions\open1\server-main.log'
        Open2Session = Join-Path $evidence 'sessions\open2\session.json'
        Open2Log = Join-Path $evidence 'sessions\open2\server-main.log'
    }
    $parameters = @{
        Phase = 'Initialize'
        EvidenceDirectory = $fixture.Evidence
        TestedCommit = $head
        AssemblyPath = $assemblyPath
        SaveDatabasePath = $fixture.Database
        SnapshotDatabasePath = $fixture.Snapshot
        PersistenceReportPath = $fixture.Report
        Open1SessionPath = $fixture.Open1Session
        Open1LogPath = $fixture.Open1Log
        Open2SessionPath = $fixture.Open2Session
        Open2LogPath = $fixture.Open2Log
        RepositoryRoot = $RepositoryRoot
    }
    $json = (& $controllerPath @parameters) -join [Environment]::NewLine
    $fixture.Add('Initialize', ($json | ConvertFrom-Json))
    return $fixture
}

function Complete-Open1(
    $Fixture,
    [switch]$WithWal,
    [ValidateSet('None', 'MissingChunk')][string]$DatabaseMutation = 'None',
    [ValidateSet('None', 'StartedAtInitialize')][string]$TimestampMutation = 'None') {
    $initialized = ConvertFrom-L00CInvariantRoundTripUtc ([string]$Fixture.Initialize.InitializedUtc)
    $initializeReceiptPath = Join-Path $Fixture.Evidence 'campaign-control\01-initialize.json'
    $initializeWritten = [DateTimeOffset](Get-Item -LiteralPath $initializeReceiptPath).LastWriteTimeUtc
    $startBoundary = if ($initialized -gt $initializeWritten) { $initialized } else { $initializeWritten }
    $started = Get-StrictlyLaterUtc $startBoundary
    New-CompleteDatabase $Fixture.Database
    if ($DatabaseMutation -eq 'MissingChunk') {
        $mutationConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$($Fixture.Database);Mode=ReadWrite;Pooling=False")
        try {
            $mutationConnection.Open()
            $mutationCommand = $mutationConnection.CreateCommand()
            try {
                $mutationCommand.CommandText = 'DELETE FROM chunk WHERE position = (SELECT position FROM chunk LIMIT 1)'
                if ($mutationCommand.ExecuteNonQuery() -ne 1) { throw 'Synthetic database mutation did not remove one chunk.' }
            }
            finally { $mutationCommand.Dispose() }
        }
        finally {
            $mutationConnection.Close()
            $mutationConnection.Dispose()
        }
    }
    $walConnection = $null
    if ($WithWal) {
        $walConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$($Fixture.Database);Mode=ReadWrite;Pooling=False")
        $walConnection.Open()
        $walCommand = $walConnection.CreateCommand()
        try {
            $walCommand.CommandText = 'PRAGMA journal_mode=WAL'
            if ([string]$walCommand.ExecuteScalar() -ne 'wal') { throw 'Synthetic source could not enter WAL mode.' }
            $walCommand.CommandText = 'PRAGMA wal_autocheckpoint=0'
            [void]$walCommand.ExecuteNonQuery()
            $walCommand.CommandText = 'CREATE TABLE walproof(value INTEGER NOT NULL); INSERT INTO walproof(value) VALUES(1);'
            [void]$walCommand.ExecuteNonQuery()
        }
        finally { $walCommand.Dispose() }
        $walPath = $Fixture.Database + '-wal'
        if (-not (Test-Path -LiteralPath $walPath -PathType Leaf) -or (Get-Item -LiteralPath $walPath).Length -le 0) {
            throw 'Synthetic persisted source did not retain a real WAL-backed update.'
        }
    }
    $completed = Get-StrictlyLaterUtc $started
    [IO.File]::SetCreationTimeUtc($Fixture.Database, $started.AddTicks(1).UtcDateTime)
    [IO.File]::SetLastWriteTimeUtc($Fixture.Database, $completed.AddTicks(-1).UtcDateTime)
    $save = '33333333-3333-3333-3333-333333333333'
    $marker = '44444444444444444444444444444444'
    $instance = '55555555555555555555555555555555'
    $log = @(
        "L00C_ACTIVATED instance=$instance marker=$marker run=1 open=1 isnew=True save=$save fixture=31990,31990"
        "L00C_TICKS_STABLE instance=$instance marker=$marker run=1 ticks=40 snapshot=$('A' * 64)"
        "L00C_MAP_SNAPSHOT_COMMITTED instance=$instance marker=$marker maps=9 checksum=$('C' * 64) writes=1"
        "L00C_DELAYED_SHUTDOWN_ARMED instance=$instance run=1 reason=fixture-stable delayms=15000 listener=41"
        "L00C_DELAYED_SHUTDOWN_FIRED instance=$instance run=1 reason=fixture-stable"
        "L00C_GRACEFUL_SHUTDOWN_REQUEST instance=$instance marker=$marker reason=fixture-stable"
        'Entering runphase Shutdown'
        "L00C_DISPOSED instance=$instance removedowned=17 restorednative=16 exact=True callbacks=1 forwarded=0"
        'Mods and systems notified, now saving everything...'
        'World saved!'
        'Stopped the server!'
    ) -join [Environment]::NewLine
    $logParent = Split-Path -Parent $Fixture.Open1Log
    if (-not (Test-Path -LiteralPath $logParent -PathType Container)) { [void](New-Item -ItemType Directory -Path $logParent) }
    [IO.File]::WriteAllText($Fixture.Open1Log, $log, [Text.UTF8Encoding]::new($false))
    $session = [ordered]@{
        EvidenceSequence = 3
        StartedUtc = $(if ($TimestampMutation -eq 'StartedAtInitialize') { $initialized.ToString('o') } else { $started.ToString('o') })
        CompletedUtc = $completed.ToString('o')
        WorldRole = 'activated-primary'
        SavegameIdentifier = $save
        MarkerId = $marker
        InstanceId = $instance
        WorldRunId = 1
        OpenCount = 1
        IsNew = $true
        ControllerPhaseBefore = 'Initialize'
        ControllerPhaseAfter = 'RecordOpen1'
    }
    Write-NewJson $Fixture.Open1Session $session
    try {
        $resultJson = (& $controllerPath -Phase RecordOpen1 -EvidenceDirectory $Fixture.Evidence -RepositoryRoot $RepositoryRoot) -join [Environment]::NewLine
        $Fixture.Add('RecordOpen1', ($resultJson | ConvertFrom-Json))
    }
    finally {
        if ($null -ne $walConnection) {
            $walConnection.Close()
            $walConnection.Dispose()
        }
    }
}

function Authorize-Open2($Fixture) {
    $json = (& $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $Fixture.Evidence -RepositoryRoot $RepositoryRoot) -join [Environment]::NewLine
    $Fixture.Add('AuthorizeOpen2', ($json | ConvertFrom-Json))
}

function Complete-Open2(
    $Fixture,
    [bool]$UseFalseStableFormat = $false,
    [int]$PersistedOpenCount = 2,
    [ValidateSet('None', 'MissingWorldSave', 'ReorderedTerminal')][string]$TerminalMutation = 'None') {
    $authorized = ConvertFrom-L00CInvariantRoundTripUtc ([string]$Fixture.AuthorizeOpen2.AuthorizedUtc)
    $authorizationReceiptPath = Join-Path $Fixture.Evidence 'campaign-control\03-authorize-open2.json'
    $authorizationWritten = [DateTimeOffset](Get-Item -LiteralPath $authorizationReceiptPath).LastWriteTimeUtc
    $startBoundary = if ($authorized -gt $authorizationWritten) { $authorized } else { $authorizationWritten }
    $started = Get-StrictlyLaterUtc $startBoundary
    $session = [ordered]@{
        EvidenceSequence = 4
        StartedUtc = $started.ToString('o')
        CompletedUtc = [DateTimeOffset]::MinValue.ToString('o')
        WorldRole = 'activated-primary'
        SavegameIdentifier = [string]$Fixture.RecordOpen1.SavegameIdentifier
        MarkerId = [string]$Fixture.RecordOpen1.MarkerId
        InstanceId = '66666666666666666666666666666666'
        WorldRunId = 1
        OpenCount = 2
        IsNew = $false
        ControllerPhaseBefore = 'AuthorizeOpen2'
        ControllerPhaseAfter = 'Finalize'
    }
    $logParent = Split-Path -Parent $Fixture.Open2Log
    if (-not (Test-Path -LiteralPath $logParent -PathType Container)) { [void](New-Item -ItemType Directory -Path $logParent) }
    $activatedLine = Expand-ProductionTemplate $activatedTemplate @{
        'instanceId' = [string]$session.InstanceId
        'marker!.MarkerId' = [string]$session.MarkerId
        'runId' = [string]$session.WorldRunId
        'marker.OpenCount' = '2'
        'saveGame.SavegameIdentifier' = [string]$session.SavegameIdentifier
        'config.FixtureChunkX' = '31990'
        'config.FixtureChunkZ' = '31990'
    }
    $stableLine = if ($UseFalseStableFormat) {
        "L00C_PERSISTED_REOPEN_STABLE instance=$($session.InstanceId) marker=$($session.MarkerId) run=$($session.WorldRunId) open=2 loadpriority=0 transientrequests=0 refreshpasses=0 refreshedmapchunks=0 keeploaded=0 unload=0 fixturewrites=0 callbacks=0 center=$('A' * 64) halo=$('B' * 64)"
    }
    else {
        Expand-ProductionTemplate $stableTemplate @{
            'instanceId' = [string]$session.InstanceId
            'marker!.MarkerId' = [string]$session.MarkerId
            'runId' = [string]$session.WorldRunId
            'priorityLoads' = '0'
            'transientRequests' = '0'
            'refreshPasses' = '0'
            'refreshedMapChunks' = '0'
            'fixtureWrites' = '0'
            'mapSnapshotWrites' = '0'
            'fixtureCallbackCount' = '0'
            'persistedSnapshot.Fixture.Hash' = 'A' * 64
            'persistedSnapshot.Halo.Hash' = 'B' * 64
        }
    }
    $open2Log = @(
        "L00C_PERSISTED_PRECHECK instance=$($session.InstanceId) marker=$($session.MarkerId) maps=9 exact=True",
        $activatedLine,
        $stableLine,
        "L00C_DELAYED_SHUTDOWN_ARMED instance=$($session.InstanceId) run=$($session.WorldRunId) reason=persisted-reopen-stable delayms=15000 listener=42",
        "L00C_DELAYED_SHUTDOWN_FIRED instance=$($session.InstanceId) run=$($session.WorldRunId) reason=persisted-reopen-stable",
        "L00C_GRACEFUL_SHUTDOWN_REQUEST instance=$($session.InstanceId) marker=$($session.MarkerId) reason=persisted-reopen-stable",
        'Entering runphase Shutdown',
        "L00C_DISPOSED instance=$($session.InstanceId) removedowned=0 restorednative=0 exact=True callbacks=0 forwarded=0",
        'Mods and systems notified, now saving everything...',
        'World saved!',
        'Stopped the server!') -join [Environment]::NewLine
    if ($TerminalMutation -eq 'MissingWorldSave') {
        $open2Log = $open2Log -replace '(?m)^World saved!\r?\n?', ''
    }
    elseif ($TerminalMutation -eq 'ReorderedTerminal') {
        $open2Log = $open2Log -replace 'World saved!\r?\nStopped the server!', "Stopped the server!`nWorld saved!"
    }
    [IO.File]::WriteAllText($Fixture.Open2Log, $open2Log, [Text.UTF8Encoding]::new($false))
    Set-DatabaseMarkerOpenCount $Fixture.Database $PersistedOpenCount
    $completed = Get-StrictlyLaterUtc $started
    $session.CompletedUtc = $completed.ToString('o')
    [IO.File]::SetLastWriteTimeUtc($Fixture.Database, $completed.AddTicks(-1).UtcDateTime)
    Write-NewJson $Fixture.Open2Session $session
    $json = (& $controllerPath -Phase Finalize -EvidenceDirectory $Fixture.Evidence -RepositoryRoot $RepositoryRoot) -join [Environment]::NewLine
    $Fixture.Add('Finalize', ($json | ConvertFrom-Json))
}

function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    try { [void](& $Action) } catch { return }
    throw "$Label was unexpectedly accepted."
}

function Assert-RejectedWithMessage([scriptblock]$Action, [string]$Label, [string]$ExpectedMessage) {
    try { [void](& $Action) }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "$Label failed for the wrong invariant: $($_.Exception.Message)"
        }
        return
    }
    throw "$Label was unexpectedly accepted."
}

try {
    Assert-FrFrInvariantTimestampRegression

    $preexistingRoot = Join-Path $selfTestRoot 'preexisting'
    [void](New-Item -ItemType Directory -Path $preexistingRoot)
    $preexistingDatabase = Join-Path $preexistingRoot 'fresh.vcdbs'
    [IO.File]::WriteAllBytes($preexistingDatabase, [byte[]](1..8))
    $preexistingEvidence = Join-Path $preexistingRoot 'evidence'
    Assert-Rejected {
        & $controllerPath -Phase Initialize -EvidenceDirectory $preexistingEvidence -TestedCommit $head -AssemblyPath $assemblyPath `
            -SaveDatabasePath $preexistingDatabase -SnapshotDatabasePath (Join-Path $preexistingEvidence 'snapshot.vcdbs') `
            -PersistenceReportPath (Join-Path $preexistingEvidence 'report.json') -Open1SessionPath (Join-Path $preexistingEvidence 'open1.json') `
            -Open1LogPath (Join-Path $preexistingEvidence 'open1.log') -Open2SessionPath (Join-Path $preexistingEvidence 'open2.json') `
            -Open2LogPath (Join-Path $preexistingEvidence 'open2.log') -RepositoryRoot $RepositoryRoot
    } 'Preexisting save database'

    $reversed = New-Campaign 'reversed'
    Assert-Rejected { & $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $reversed.Evidence -RepositoryRoot $RepositoryRoot } 'AuthorizeOpen2 before RecordOpen1'

    $invalidOpen1Time = New-Campaign 'invalid-open1-time'
    Assert-RejectedWithMessage { Complete-Open1 $invalidOpen1Time -TimestampMutation 'StartedAtInitialize' } `
        'Open1 started at Initialize instead of strictly after it' `
        'Open1 timestamps must be after Initialize and completed before RecordOpen1.'

    $stale = New-Campaign 'stale-renamed'
    $initialized = ConvertFrom-L00CInvariantRoundTripUtc ([string]$stale.Initialize.InitializedUtc)
    $started = $initialized.AddTicks(1)
    New-CompleteDatabase $stale.Database
    Start-Sleep -Milliseconds 2
    $completed = [DateTimeOffset]::UtcNow
    [IO.File]::SetCreationTimeUtc($stale.Database, $started.AddTicks(1).UtcDateTime)
    [IO.File]::SetLastWriteTimeUtc($stale.Database, $completed.AddTicks(-1).UtcDateTime)
    $logParent = Split-Path -Parent $stale.Open1Log
    [void](New-Item -ItemType Directory -Path $logParent)
    [IO.File]::WriteAllText($stale.Open1Log, 'stale', [Text.UTF8Encoding]::new($false))
    Write-NewJson $stale.Open1Session ([ordered]@{ EvidenceSequence = 3; StartedUtc = $started.ToString('o'); CompletedUtc = $completed.ToString('o'); WorldRole = 'activated-primary'; SavegameIdentifier = '33333333-3333-3333-3333-333333333333'; MarkerId = '4' * 32; InstanceId = '5' * 32; WorldRunId = 1; OpenCount = 1; IsNew = $true; ControllerPhaseBefore = 'Initialize'; ControllerPhaseAfter = 'RecordOpen1' })
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $stale.Report))
    [IO.File]::WriteAllText($stale.Report, '{"renamed":"stale"}', [Text.UTF8Encoding]::new($false))
    Assert-Rejected { & $controllerPath -Phase RecordOpen1 -EvidenceDirectory $stale.Evidence -RepositoryRoot $RepositoryRoot } 'Stale renamed report'

    $rewritten = New-Campaign 'rewritten-attestation'
    Complete-Open1 $rewritten
    $reportObject = Get-Content -LiteralPath $rewritten.Report -Raw | ConvertFrom-Json
    $reportObject.AttestedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Import-Module (Join-Path $PSScriptRoot 'L00CPersistenceAttestation.psm1') -Force
    $reportObject.AttestationId = Get-L00CAttestationId $reportObject
    $reportObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $rewritten.Report -Encoding UTF8 -NoNewline
    Assert-Rejected { & $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $rewritten.Evidence -RepositoryRoot $RepositoryRoot } 'Rewritten attestation'

    $premature = New-Campaign 'premature-open2'
    Complete-Open1 $premature
    Write-NewJson $premature.Open2Session ([ordered]@{ EvidenceSequence = 4 })
    Assert-Rejected { & $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $premature.Evidence -RepositoryRoot $RepositoryRoot } 'Open2 already exists'

    $sidecarDependent = New-Campaign 'sidecar-dependent'
    Complete-Open1 $sidecarDependent
    [IO.File]::WriteAllBytes($sidecarDependent.Snapshot + '-wal', [byte[]]@(1, 2, 3))
    Assert-Rejected { Authorize-Open2 $sidecarDependent } 'Snapshot requiring a sidecar'

    $hashMismatch = New-Campaign 'snapshot-hash-mismatch'
    Complete-Open1 $hashMismatch
    $snapshotBytes = [IO.File]::ReadAllBytes($hashMismatch.Snapshot)
    $snapshotBytes[$snapshotBytes.Length - 1] = $snapshotBytes[$snapshotBytes.Length - 1] -bxor 0xff
    [IO.File]::WriteAllBytes($hashMismatch.Snapshot, $snapshotBytes)
    Assert-Rejected { Authorize-Open2 $hashMismatch } 'Snapshot hash mismatch'

    $falseStable = New-Campaign 'false-stable-field'
    Complete-Open1 $falseStable
    Authorize-Open2 $falseStable
    Assert-Rejected { Complete-Open2 $falseStable $true } 'Invented open field on persisted stable marker'

    $staleOpenCount = New-Campaign 'stale-open-count'
    Complete-Open1 $staleOpenCount
    Authorize-Open2 $staleOpenCount
    Assert-Rejected { Complete-Open2 $staleOpenCount $false 1 } 'Open2 database did not persist incremented count'

    $incompleteOpen1 = New-Campaign 'incomplete-open1-database'
    Assert-Rejected { Complete-Open1 $incompleteOpen1 -DatabaseMutation 'MissingChunk' } 'Open1 database missing one persisted chunk'

    $missingWorldSave = New-Campaign 'missing-world-save'
    Complete-Open1 $missingWorldSave
    Authorize-Open2 $missingWorldSave
    Assert-Rejected { Complete-Open2 $missingWorldSave $false 2 'MissingWorldSave' } 'Marker database substituted for missing world save'

    $reorderedTerminal = New-Campaign 'reordered-terminal'
    Complete-Open1 $reorderedTerminal
    Authorize-Open2 $reorderedTerminal
    Assert-Rejected { Complete-Open2 $reorderedTerminal $false 2 'ReorderedTerminal' } 'World save after stop'

    $baseline = New-Campaign 'baseline'
    Complete-Open1 $baseline -WithWal
    $baselineReport = Get-Content -LiteralPath $baseline.Report -Raw | ConvertFrom-Json
    if (-not [bool]$baselineReport.Autonomous -or [string]$baselineReport.IntegrityCheck -ne 'ok' -or
        (Test-Path -LiteralPath ($baseline.Snapshot + '-wal')) -or (Test-Path -LiteralPath ($baseline.Snapshot + '-shm'))) {
        throw 'RecordOpen1 did not archive the WAL-backed source as a standalone intact database.'
    }
    Authorize-Open2 $baseline
    Complete-Open2 $baseline
    if ([string]$baseline.Initialize.Status -ne 'READY_FOR_OPEN1' -or
        [string]$baseline.RecordOpen1.Status -ne 'READY_FOR_OPEN2_AUTHORIZATION' -or
        [string]$baseline.AuthorizeOpen2.Status -ne 'AUTHORIZED_OPEN2' -or
        [string]$baseline.Finalize.Status -ne 'COMPLETE') {
        throw 'Valid controller campaign did not traverse all four production phases.'
    }

    [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    $sourceQuarantine = Join-Path $baseline.Root 'source-quarantine'
    [void](New-Item -ItemType Directory -Path $sourceQuarantine)
    foreach ($sourceComponent in @($baseline.Database, ($baseline.Database + '-wal'), ($baseline.Database + '-shm'))) {
        if (Test-Path -LiteralPath $sourceComponent -PathType Leaf) {
            Move-Item -LiteralPath $sourceComponent -Destination $sourceQuarantine
        }
    }
    foreach ($sourceComponent in @($baseline.Database, ($baseline.Database + '-wal'), ($baseline.Database + '-shm'))) {
        if (Test-Path -LiteralPath $sourceComponent) {
            throw "WAL proof source component was not quarantined: $sourceComponent"
        }
    }
    Assert-WalProofInAutonomousSnapshot $baseline.Snapshot

    $missingWalProof = Join-Path $baseline.Root 'missing-walproof.vcdbs'
    Copy-Item -LiteralPath $baseline.Snapshot -Destination $missingWalProof
    $missingWalConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$missingWalProof;Mode=ReadWrite;Pooling=False")
    try {
        $missingWalConnection.Open()
        $missingWalCommand = $missingWalConnection.CreateCommand()
        try {
            $missingWalCommand.CommandText = 'DELETE FROM walproof'
            if ([int]$missingWalCommand.ExecuteNonQuery() -ne 1) { throw 'Synthetic WAL negative could not remove its proof row.' }
        }
        finally { $missingWalCommand.Dispose() }
    }
    finally {
        $missingWalConnection.Close()
        $missingWalConnection.Dispose()
    }
    Assert-Rejected { Assert-WalProofInAutonomousSnapshot $missingWalProof } 'Autonomous snapshot missing the WAL-carried proof row'

    [ordered]@{
        TestId = 'L00-C-CAMPAIGN-CONTROLLER'
        Status = 'PASS'
        ValidPhaseOrder = @('Initialize', 'RecordOpen1', 'AuthorizeOpen2', 'Finalize')
        CampaignId = [string]$baseline.Initialize.CampaignId
        DatabasePreexistingRejected = $true
        StaleRenamedReportRejected = $true
        RewrittenAttestationRejected = $true
        Open2AlreadyExistsRejected = $true
        SidecarDependentSnapshotRejected = $true
        SnapshotHashMismatchRejected = $true
        ReversedOrderRejected = $true
        FrFrInvariantIsoTimestampParsing = $true
        InvalidOpen1TimestampOrderRejected = $true
        FalseStableOpenFieldRejected = $true
        Open2DatabaseIncrementRequired = $true
        IncompleteOpen1DatabaseRejected = $true
        DatabaseCannotSubstituteWorldSave = $true
        ReorderedTerminalEventsRejected = $true
        AutonomousOpen1AndOpen2SnapshotsRequired = $true
        WalBackedOpen1SourceExercised = $true
        WalPayloadReadWithoutSource = $true
        MissingWalPayloadRejected = $true
        ProductionLogSchemaDerived = $true
        OperationalGuarantee = 'fresh tamper-evident chain, not attacker-resistant'
    } | ConvertTo-Json -Depth 5
}
finally {
    [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    $resolvedSelfTest = [IO.Path]::GetFullPath($selfTestRoot)
    $resolvedAllowed = ([IO.Path]::GetFullPath((Join-Path $RepositoryRoot '.local\L00C\campaign-controller-selftest'))).TrimEnd('\') + '\'
    if ($resolvedSelfTest.StartsWith($resolvedAllowed, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedSelfTest)) {
        Remove-Item -LiteralPath $resolvedSelfTest -Recurse -Force
    }
}
