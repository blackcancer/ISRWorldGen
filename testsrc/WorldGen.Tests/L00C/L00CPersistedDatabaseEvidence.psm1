Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Import-L00CSqliteRuntime {
    param([Parameter(Mandatory = $true)][string]$GamePath)

    if ($null -ne ('Microsoft.Data.Sqlite.SqliteConnection' -as [type])) {
        return
    }
    $sqliteDirectory = Join-Path $GamePath 'Lib'
    $nativeSqlite = Join-Path $sqliteDirectory 'e_sqlite3.dll'
    if (-not (Test-Path -LiteralPath $nativeSqlite -PathType Leaf)) {
        throw "Required local SQLite runtime is missing: $nativeSqlite"
    }
    [void][Runtime.InteropServices.NativeLibrary]::Load($nativeSqlite)
    foreach ($assembly in @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlite3.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll'
    )) {
        $path = Join-Path $sqliteDirectory $assembly
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required local SQLite assembly is missing: $path"
        }
        [void][Reflection.Assembly]::LoadFrom($path)
    }
    [SQLitePCL.Batteries_V2]::Init()
}

function New-L00CAutonomousDatabaseSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$GamePath
    )

    $source = (Resolve-Path -LiteralPath $SourcePath).Path
    $destination = [IO.Path]::GetFullPath($DestinationPath)
    if (Test-Path -LiteralPath $destination) {
        throw "Autonomous SQLite snapshot already exists: $destination"
    }
    $parent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $parent)
    }

    Import-L00CSqliteRuntime $GamePath
    $sourceConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$source;Mode=ReadOnly;Pooling=False")
    $destinationConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$destination;Mode=ReadWriteCreate;Pooling=False")
    try {
        $sourceConnection.Open()
        $destinationConnection.Open()
        $sourceConnection.BackupDatabase($destinationConnection)
        $command = $destinationConnection.CreateCommand()
        try {
            $command.CommandText = 'PRAGMA journal_mode=DELETE'
            [void]$command.ExecuteScalar()
        }
        finally {
            $command.Dispose()
        }
    }
    catch {
        throw
    }
    finally {
        if ($destinationConnection.State -ne [Data.ConnectionState]::Closed) { $destinationConnection.Close() }
        if ($sourceConnection.State -ne [Data.ConnectionState]::Closed) { $sourceConnection.Close() }
        $destinationConnection.Dispose()
        $sourceConnection.Dispose()
        [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    }

    foreach ($sidecar in @($destination + '-wal', $destination + '-shm')) {
        if (Test-Path -LiteralPath $sidecar) {
            throw "Autonomous SQLite snapshot unexpectedly depends on sidecar: $sidecar"
        }
    }
    return [pscustomobject]@{
        Path = $destination
        Sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        Length = (Get-Item -LiteralPath $destination).Length
        SourceHadWal = Test-Path -LiteralPath ($source + '-wal')
    }
}

function Assert-L00CAutonomousDatabaseSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$DatabasePath,
        [Parameter(Mandatory = $true)][string]$GamePath,
        [string]$ExpectedSha256,
        [long]$ExpectedLength = 0
    )

    $database = (Resolve-Path -LiteralPath $DatabasePath).Path
    foreach ($sidecar in @($database + '-wal', $database + '-shm')) {
        if (Test-Path -LiteralPath $sidecar) {
            throw "Persisted database snapshot is not autonomous because a SQLite sidecar exists: $sidecar"
        }
    }
    $actualSha256 = (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash
    $actualLength = (Get-Item -LiteralPath $database).Length
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256) -and
        -not $actualSha256.Equals($ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Persisted database snapshot SHA-256 mismatch: $actualSha256."
    }
    if ($ExpectedLength -gt 0 -and $actualLength -ne $ExpectedLength) {
        throw "Persisted database snapshot length mismatch: $actualLength."
    }

    Import-L00CSqliteRuntime $GamePath
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$database;Mode=ReadOnly;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'PRAGMA integrity_check'
            $integrity = [string]$command.ExecuteScalar()
            $command.CommandText = 'PRAGMA journal_mode'
            $journal = [string]$command.ExecuteScalar()
        }
        finally { $command.Dispose() }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }
    if ($integrity -ne 'ok' -or $journal -eq 'wal') {
        throw "Persisted database snapshot is not autonomous: integrity=$integrity journal=$journal."
    }
    return [pscustomobject]@{
        Path = $database
        Sha256 = $actualSha256
        Length = $actualLength
        IntegrityCheck = $integrity
        JournalMode = $journal
        Autonomous = $true
    }
}

function Find-L00CByteSequenceOffsets {
    param([byte[]]$Data, [byte[]]$Needle)

    $offsets = [Collections.Generic.List[int]]::new()
    for ($index = 0; $index -le $Data.Length - $Needle.Length; $index++) {
        $matches = $true
        for ($needleIndex = 0; $needleIndex -lt $Needle.Length; $needleIndex++) {
            if ($Data[$index + $needleIndex] -ne $Needle[$needleIndex]) {
                $matches = $false
                break
            }
        }
        if ($matches) { [void]$offsets.Add($index) }
    }
    return @($offsets)
}

function Read-L00CBoundedVarint32 {
    param([byte[]]$Data, [ref]$Offset)

    [int64]$value = 0
    for ($byteIndex = 0; $byteIndex -lt 5; $byteIndex++) {
        if ($Offset.Value -ge $Data.Length) { throw 'L00-C marker protobuf length is truncated.' }
        [byte]$current = $Data[$Offset.Value]
        $Offset.Value++
        $value = $value -bor ([int64]($current -band 0x7f) -shl (7 * $byteIndex))
        if (($current -band 0x80) -eq 0) {
            if ($value -le 0 -or $value -gt [int]::MaxValue) { throw 'L00-C marker protobuf length is outside Int32 bounds.' }
            return [int]$value
        }
    }
    throw 'L00-C marker protobuf length exceeds five bytes.'
}

function Get-L00CPersistedMarkerEnvelope {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Microsoft.Data.Sqlite.SqliteConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$AssemblyPath,
        [Parameter(Mandatory = $true)][string]$GamePath,
        [Parameter(Mandatory = $true)][string]$SavegameIdentifier,
        [Parameter(Mandatory = $true)][string]$MarkerId,
        [Parameter(Mandatory = $true)][int]$ExpectedOpenCount,
        [Parameter(Mandatory = $true)][int]$FixtureChunkX,
        [Parameter(Mandatory = $true)][int]$FixtureChunkZ,
        [Parameter(Mandatory = $true)][int]$ChunkSize,
        [Parameter(Mandatory = $true)][int]$WorldHeight
    )

    foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
        $dependencyPath = Join-Path $GamePath $dependency
        if (-not (Test-Path -LiteralPath $dependencyPath -PathType Leaf)) { throw "L00-C marker dependency is missing: $dependencyPath" }
        [void][Reflection.Assembly]::LoadFrom($dependencyPath)
    }
    $candidateAssembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
    $readerType = $candidateAssembly.GetType('ISRWorldGen.WorldgenProbe.ProbeMarkerEnvelopeReader', $false)
    $reader = if ($null -eq $readerType) { $null } else { $readerType.GetMethod('ReadAndValidate') }
    if ($null -eq $readerType -or $null -eq $reader) {
        throw 'The exact Debug candidate has no production marker-envelope reader.'
    }

    $markerKey = [Text.Encoding]::UTF8.GetBytes('isrworldgen:l00c:marker:v1')
    $payloads = [Collections.Generic.List[byte[]]]::new()
    $command = $Connection.CreateCommand()
    try {
        $command.CommandText = 'SELECT data FROM gamedata WHERE data IS NOT NULL'
        $databaseReader = $command.ExecuteReader()
        try {
            while ($databaseReader.Read()) {
                [byte[]]$data = $databaseReader.GetValue(0)
                foreach ($keyOffset in @(Find-L00CByteSequenceOffsets $data $markerKey)) {
                    $valueTagOffset = $keyOffset + $markerKey.Length
                    if ($valueTagOffset -ge $data.Length -or $data[$valueTagOffset] -ne 0x12) {
                        throw 'L00-C marker key is not followed by its protobuf length-delimited value field.'
                    }
                    $payloadOffset = $valueTagOffset + 1
                    $payloadLength = Read-L00CBoundedVarint32 $data ([ref]$payloadOffset)
                    if ($payloadOffset + $payloadLength -gt $data.Length) {
                        throw 'L00-C marker envelope extends beyond the persisted gamedata blob.'
                    }
                    $payload = [byte[]]::new($payloadLength)
                    [Array]::Copy($data, $payloadOffset, $payload, 0, $payloadLength)
                    [void]$payloads.Add($payload)
                }
            }
        }
        finally {
            $databaseReader.Close()
            $databaseReader.Dispose()
        }
    }
    finally {
        $command.Dispose()
    }
    if ($payloads.Count -ne 1) {
        throw "Expected exactly one persisted L00-C marker envelope, found $($payloads.Count)."
    }

    try {
        $arguments = [object[]]@($payloads[0], $SavegameIdentifier, $FixtureChunkX, $FixtureChunkZ, $ChunkSize, $WorldHeight)
        $marker = $reader.Invoke($null, $arguments)
    }
    catch {
        $failure = $_.Exception
        while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
        throw "The production marker-envelope reader rejected persisted data: $($failure.Message)"
    }
    $markerType = $marker.GetType()
    $mapFootprint = $markerType.GetProperty('MapFootprint').GetValue($marker)
    $footprintType = $mapFootprint.GetType()
    $mapChunks = @($footprintType.GetProperty('MapChunks').GetValue($mapFootprint))
    $coordinates = @($mapChunks | ForEach-Object { "$(($_.GetType().GetProperty('X').GetValue($_))),$(($_.GetType().GetProperty('Z').GetValue($_)))" } | Sort-Object)
    $actualMarkerId = [string]$markerType.GetProperty('MarkerId').GetValue($marker)
    $actualOpenCount = [int]$markerType.GetProperty('OpenCount').GetValue($marker)
    if ($actualMarkerId -ne $MarkerId -or $actualOpenCount -ne $ExpectedOpenCount) {
        throw "Persisted marker identity/count mismatch: marker=$actualMarkerId open=$actualOpenCount expectedMarker=$MarkerId expectedOpen=$ExpectedOpenCount."
    }
    return [pscustomobject]@{
        MarkerId = $actualMarkerId
        SavegameIdentifier = [string]$markerType.GetProperty('SavegameIdentifier').GetValue($marker)
        MarkerVersion = [string]$markerType.GetProperty('Version').GetValue($marker)
        OpenCount = $actualOpenCount
        PayloadLength = $payloads[0].Length
        PayloadSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payloads[0]))
        MapFootprintVersion = [string]$footprintType.GetProperty('Version').GetValue($mapFootprint)
        MapFootprintMarkerId = [string]$footprintType.GetProperty('MarkerId').GetValue($mapFootprint)
        MapFootprintSavegameIdentifier = [string]$footprintType.GetProperty('SavegameIdentifier').GetValue($mapFootprint)
        MapFootprintChunkX = [int]$footprintType.GetProperty('FixtureChunkX').GetValue($mapFootprint)
        MapFootprintChunkZ = [int]$footprintType.GetProperty('FixtureChunkZ').GetValue($mapFootprint)
        MapFootprintChunkSize = [int]$footprintType.GetProperty('ChunkSize').GetValue($mapFootprint)
        MapFootprintWorldHeight = [int]$footprintType.GetProperty('WorldHeight').GetValue($mapFootprint)
        MapFootprintMapChunks = $mapChunks.Count
        MapFootprintCoordinates = $coordinates
        MapFootprintSha256 = [string]$footprintType.GetProperty('ContentSha256').GetValue($mapFootprint)
    }
}

Export-ModuleMember -Function Import-L00CSqliteRuntime, New-L00CAutonomousDatabaseSnapshot, Assert-L00CAutonomousDatabaseSnapshot, Get-L00CPersistedMarkerEnvelope
