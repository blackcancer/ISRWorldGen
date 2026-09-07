Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-L00CInitializationRefusalLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorldRole,

        [Parameter(Mandatory = $true)]
        [string]$Log,

        [Parameter(Mandatory = $true)]
        [string]$InstanceId
    )

    if ($WorldRole -ne 'missing-handler') {
        throw "Initialization-refusal evidence cannot classify role '$WorldRole'."
    }
    if ($InstanceId -notmatch '^[0-9a-f]{32}$') {
        throw 'Initialization-refusal instance id is malformed.'
    }

    $instance = [regex]::Escape($InstanceId)
    $errorToken = "L00C_ERROR code=expected-handler-absent instance=$InstanceId "
    $failedToken = "L00C_INITIALIZATION_FAILED instance=$InstanceId "
    $shutdownToken = 'Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API'
    $shutdownPhaseToken = 'Entering runphase Shutdown'
    $disposedToken = "L00C_DISPOSED instance=$InstanceId "
    $stoppedToken = 'Stopped the server!'
    $resultToken = "L00C_INITIALIZATION_SHUTDOWN instance=$InstanceId "

    if ([regex]::Matches($Log, "L00C_ERROR code=expected-handler-absent instance=$instance ").Count -ne 1 -or
        [regex]::Matches($Log, "L00C_INITIALIZATION_FAILED instance=$instance .* type=System\.InvalidOperationException ").Count -ne 1 -or
        [regex]::Matches($Log, "L00C_INITIALIZATION_SHUTDOWN instance=$instance .* accepted=True cleanupError=none shutdownError=none").Count -ne 1 -or
        [regex]::Matches($Log, [regex]::Escape($shutdownToken)).Count -ne 1 -or
        [regex]::Matches($Log, [regex]::Escape($stoppedToken)).Count -ne 1) {
        throw 'Missing-handler evidence lacks exactly one explicit refusal, fail-closed shutdown request, or terminal server stop.'
    }

    $orderedTokens = @($errorToken, $failedToken, $shutdownToken, $shutdownPhaseToken, $disposedToken, $stoppedToken, $resultToken)
    $priorIndex = -1
    foreach ($token in $orderedTokens) {
        $index = $Log.IndexOf($token, [StringComparison]::Ordinal)
        if ($index -le $priorIndex) {
            throw "Missing-handler fail-closed sequence is absent or out of order at '$token'."
        }
        $priorIndex = $index
    }

    if ($Log -notmatch "L00C_TRANSIENT_CALLBACK_RESET reason=dispose instance=$instance cancelled=0 pending=0 exact=True" -or
        $Log -notmatch "L00C_DISPOSED instance=$instance removedowned=0 restorednative=0 exact=True callbacks=0 forwarded=0") {
        throw 'Missing-handler shutdown retained a callback or modified/restored a handler.'
    }

    $forbiddenProbePattern = "L00C_(?:ACTIVATED|FIXTURE_WRITTEN|MARKER_SAVED|MAP_SNAPSHOT_COMMITTED|PERSISTED_[A-Z_]+|COLUMN_REQUEST|TRANSIENT_(?:PRECONDITION|LOAD_ACCEPTED|LOAD_REJECTED)|FOOTPRINT_REFRESH|HALO_[A-Z_]+|DELAYED_SHUTDOWN_(?:ARMED|FIRED)|GRACEFUL_SHUTDOWN_REQUEST) instance=$instance(?: |$)"
    if ($Log -match $forbiddenProbePattern) {
        throw 'Missing-handler refusal activated, generated, scheduled, or published probe state.'
    }

    # The refusal is raised inside InitWorldGen. In VS 1.22.7 the synchronous
    # fail-closed stop completes before the later load/save run-phase hook can
    # persist a valid world state, so a completed-save line is contradictory.
    $worldSaveCount = [regex]::Matches($Log, 'World saved!').Count
    if ($worldSaveCount -ne 0) {
        throw 'InitWorldGen refusal unexpectedly reported a completed world save.'
    }

    return [pscustomobject]@{
        Status = 'PASS'
        WorldRole = $WorldRole
        InstanceId = $InstanceId
        ExactShutdown = $true
        ZeroProbeMutation = $true
        WorldSaveCount = $worldSaveCount
    }
}

function Import-L00CSqliteRuntime {
    param([string]$GamePath)

    $sqliteDirectory = Join-Path $GamePath 'Lib'
    $nativeSqlite = Join-Path $sqliteDirectory 'e_sqlite3.dll'
    $assemblies = @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlite3.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll'
    )
    foreach ($path in @($nativeSqlite) + @($assemblies | ForEach-Object { Join-Path $sqliteDirectory $_ })) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required local SQLite runtime is missing: $path"
        }
    }

    [void][Runtime.InteropServices.NativeLibrary]::Load($nativeSqlite)
    foreach ($assembly in $assemblies) {
        if (-not ([AppDomain]::CurrentDomain.GetAssemblies().Location -contains (Join-Path $sqliteDirectory $assembly))) {
            [void][Reflection.Assembly]::LoadFrom((Join-Path $sqliteDirectory $assembly))
        }
    }
    [SQLitePCL.Batteries_V2]::Init()
}

function Find-L00CByteSequence {
    param([byte[]]$Data, [byte[]]$Needle)

    if ($Needle.Length -eq 0 -or $Data.Length -lt $Needle.Length) {
        return $false
    }
    $lastStart = $Data.Length - $Needle.Length
    for ($offset = 0; $offset -le $lastStart; $offset++) {
        $matches = $true
        for ($index = 0; $index -lt $Needle.Length; $index++) {
            if ($Data[$offset + $index] -ne $Needle[$index]) {
                $matches = $false
                break
            }
        }
        if ($matches) {
            return $true
        }
    }
    return $false
}

function New-L00CInitializationRefusalDatabaseSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDatabasePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationDatabasePath,

        [Parameter(Mandatory = $true)]
        [string]$StoppedLogPath,

        [Parameter(Mandatory = $true)]
        [string]$InstanceId,

        [string]$GamePath = 'D:\Jeux\Vintagestory'
    )

    if (-not (Test-Path -LiteralPath $SourceDatabasePath -PathType Leaf)) {
        throw "Missing-handler source database is missing: $SourceDatabasePath"
    }
    if (-not (Test-Path -LiteralPath $StoppedLogPath -PathType Leaf)) {
        throw "Missing-handler stopped log is missing: $StoppedLogPath"
    }
    $source = (Resolve-Path -LiteralPath $SourceDatabasePath).Path
    $stoppedLog = (Resolve-Path -LiteralPath $StoppedLogPath).Path
    $destination = [IO.Path]::GetFullPath($DestinationDatabasePath)
    $destinationParent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
        throw "Missing-handler snapshot directory is missing: $destinationParent"
    }
    foreach ($candidate in @($destination, $destination + '-wal', $destination + '-shm')) {
        if (Test-Path -LiteralPath $candidate) {
            throw "Missing-handler snapshot destination must be new: $candidate"
        }
    }

    $log = Get-Content -LiteralPath $stoppedLog -Raw
    [void](Assert-L00CInitializationRefusalLog -WorldRole 'missing-handler' -Log $log -InstanceId $InstanceId)
    Import-L00CSqliteRuntime $GamePath

    $sourceWal = $source + '-wal'
    $sourceWalPresent = Test-Path -LiteralPath $sourceWal -PathType Leaf
    $sourceWalLength = if ($sourceWalPresent) { (Get-Item -LiteralPath $sourceWal).Length } else { 0 }

    # Reserve the exact destination atomically. SQLite then initializes only
    # this owned, empty file; an existing artifact can never be overwritten.
    $reservation = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $reservation.Dispose()

    $sourceConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$source;Mode=ReadOnly;Pooling=False")
    $destinationConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$destination;Mode=ReadWriteCreate;Pooling=False")
    try {
        $sourceConnection.Open()
        $destinationConnection.Open()
        $sourceConnection.BackupDatabase($destinationConnection)
        $journalCommand = $destinationConnection.CreateCommand()
        try {
            $journalCommand.CommandText = 'PRAGMA journal_mode=DELETE'
            $journalMode = [string]$journalCommand.ExecuteScalar()
            if ($journalMode -ne 'delete') {
                throw "Autonomous snapshot could not leave WAL mode: $journalMode"
            }
        }
        finally {
            $journalCommand.Dispose()
        }
    }
    finally {
        $destinationConnection.Close()
        $destinationConnection.Dispose()
        $sourceConnection.Close()
        $sourceConnection.Dispose()
        [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    }

    $sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    $length = (Get-Item -LiteralPath $destination).Length
    $validation = Test-L00CInitializationRefusalDatabase -DatabasePath $destination `
        -ExpectedSha256 $sha256 -ExpectedLength $length -GamePath $GamePath

    return [pscustomobject]@{
        Status = 'PASS'
        CapturedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        SourceDatabasePath = $source
        SourceDatabaseLength = (Get-Item -LiteralPath $source).Length
        SourceWalPresent = $sourceWalPresent
        SourceWalLength = $sourceWalLength
        StoppedLogSha256 = (Get-FileHash -LiteralPath $stoppedLog -Algorithm SHA256).Hash
        DestinationDatabasePath = $destination
        Sha256 = $sha256
        Length = $length
        CreateNew = $true
        Autonomous = [bool]$validation.Autonomous
        IntegrityCheck = [string]$validation.IntegrityCheck
    }
}

function Test-L00CInitializationRefusalDatabase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha256,

        [Parameter(Mandatory = $true)]
        [long]$ExpectedLength,

        [string]$GamePath = 'D:\Jeux\Vintagestory'
    )

    if (-not (Test-Path -LiteralPath $DatabasePath -PathType Leaf)) {
        throw "Missing-handler refusal database is missing: $DatabasePath"
    }
    $resolvedDatabase = (Resolve-Path -LiteralPath $DatabasePath).Path
    if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or $ExpectedLength -le 0) {
        throw 'Missing-handler snapshot hash or length is malformed.'
    }
    $actualSha256 = (Get-FileHash -LiteralPath $resolvedDatabase -Algorithm SHA256).Hash
    $actualLength = (Get-Item -LiteralPath $resolvedDatabase).Length
    if (-not $actualSha256.Equals($ExpectedSha256, [StringComparison]::OrdinalIgnoreCase) -or $actualLength -ne $ExpectedLength) {
        throw "Missing-handler snapshot identity mismatch: sha256=$actualSha256 length=$actualLength."
    }
    foreach ($sidecar in @($resolvedDatabase + '-wal', $resolvedDatabase + '-shm')) {
        if (Test-Path -LiteralPath $sidecar) {
            throw "Missing-handler snapshot is not autonomous because a SQLite sidecar exists: $sidecar"
        }
    }
    Import-L00CSqliteRuntime $GamePath

    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$resolvedDatabase;Mode=ReadOnly;Pooling=False")
    $counts = @{}
    $markerEnvelopeOccurrences = 0
    $integrityCheck = $null
    $journalMode = $null
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'PRAGMA integrity_check'
            $integrityCheck = [string]$command.ExecuteScalar()
            $command.CommandText = 'PRAGMA journal_mode'
            $journalMode = [string]$command.ExecuteScalar()
        }
        finally {
            $command.Dispose()
        }
        foreach ($tableName in @('mapchunk', 'chunk', 'mapregion')) {
            $command = $connection.CreateCommand()
            try {
                $command.CommandText = "SELECT COUNT(*) FROM [$tableName]"
                $counts[$tableName] = [long]$command.ExecuteScalar()
            }
            finally {
                $command.Dispose()
            }
        }

        $markerKey = [Text.Encoding]::UTF8.GetBytes('isrworldgen:l00c:marker:v1')
        $markerVersion = [Text.Encoding]::UTF8.GetBytes('l00c-flat-v2-map-snapshot')
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'SELECT data FROM gamedata WHERE data IS NOT NULL'
            $reader = $command.ExecuteReader()
            try {
                while ($reader.Read()) {
                    $data = [byte[]]$reader.GetValue(0)
                    if ((Find-L00CByteSequence $data $markerKey) -or (Find-L00CByteSequence $data $markerVersion)) {
                        $markerEnvelopeOccurrences++
                    }
                }
            }
            finally {
                $reader.Close()
                $reader.Dispose()
            }
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }

    if ($integrityCheck -ne 'ok') {
        throw "Missing-handler snapshot failed SQLite integrity_check: $integrityCheck"
    }
    if ($journalMode -eq 'wal') {
        throw 'Missing-handler snapshot retains WAL journal mode and is not accepted as a standalone artifact.'
    }
    if ($counts.mapchunk -ne 0 -or $counts.chunk -ne 0 -or $counts.mapregion -ne 0) {
        throw "Missing-handler refusal persisted geography: mapchunk=$($counts.mapchunk) chunk=$($counts.chunk) mapregion=$($counts.mapregion)."
    }
    if ($markerEnvelopeOccurrences -ne 0) {
        throw "Missing-handler refusal persisted $markerEnvelopeOccurrences L00-C marker envelope(s)."
    }

    return [pscustomobject]@{
        Status = 'PASS'
        OpenMode = 'ReadOnly'
        Autonomous = $true
        IntegrityCheck = $integrityCheck
        JournalMode = $journalMode
        DatabasePath = $resolvedDatabase
        DatabaseSha256 = $actualSha256
        DatabaseLength = $actualLength
        MapChunkRows = [long]$counts.mapchunk
        ChunkRows = [long]$counts.chunk
        MapRegionRows = [long]$counts.mapregion
        MarkerEnvelopeOccurrences = $markerEnvelopeOccurrences
    }
}

Export-ModuleMember -Function Assert-L00CInitializationRefusalLog, New-L00CInitializationRefusalDatabaseSnapshot, Test-L00CInitializationRefusalDatabase
