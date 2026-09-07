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

function Test-L00CInitializationRefusalDatabase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,

        [string]$GamePath = 'D:\Jeux\Vintagestory'
    )

    if (-not (Test-Path -LiteralPath $DatabasePath -PathType Leaf)) {
        throw "Missing-handler refusal database is missing: $DatabasePath"
    }
    $resolvedDatabase = (Resolve-Path -LiteralPath $DatabasePath).Path
    Import-L00CSqliteRuntime $GamePath

    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$resolvedDatabase;Mode=ReadOnly;Pooling=False")
    $counts = @{}
    $markerEnvelopeOccurrences = 0
    try {
        $connection.Open()
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

    if ($counts.mapchunk -ne 0 -or $counts.chunk -ne 0 -or $counts.mapregion -ne 0) {
        throw "Missing-handler refusal persisted geography: mapchunk=$($counts.mapchunk) chunk=$($counts.chunk) mapregion=$($counts.mapregion)."
    }
    if ($markerEnvelopeOccurrences -ne 0) {
        throw "Missing-handler refusal persisted $markerEnvelopeOccurrences L00-C marker envelope(s)."
    }

    return [pscustomobject]@{
        Status = 'PASS'
        OpenMode = 'ReadOnly'
        DatabasePath = $resolvedDatabase
        DatabaseSha256 = (Get-FileHash -LiteralPath $resolvedDatabase -Algorithm SHA256).Hash
        DatabaseLength = (Get-Item -LiteralPath $resolvedDatabase).Length
        MapChunkRows = [long]$counts.mapchunk
        ChunkRows = [long]$counts.chunk
        MapRegionRows = [long]$counts.mapregion
        MarkerEnvelopeOccurrences = $markerEnvelopeOccurrences
    }
}

Export-ModuleMember -Function Assert-L00CInitializationRefusalLog, Test-L00CInitializationRefusalDatabase
