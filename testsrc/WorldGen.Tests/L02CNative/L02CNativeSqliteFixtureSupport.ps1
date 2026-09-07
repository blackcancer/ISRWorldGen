Set-StrictMode -Version Latest

function Initialize-L02CSqliteFixtureRuntime {
    param([Parameter(Mandatory)][string]$VintageStoryPath)

    $libraryRoot = Join-Path $VintageStoryPath 'Lib'
    [void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $libraryRoot 'e_sqlite3.dll'))
    foreach ($name in @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlite3.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll',
        'protobuf-net.dll'
    )) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $libraryRoot $name))
    }
    [SQLitePCL.Batteries_V2]::Init()
    [void][Reflection.Assembly]::LoadFrom((Join-Path $VintageStoryPath 'VintagestoryAPI.dll'))
    $script:l02cFixtureSaveGameType = [Reflection.Assembly]::LoadFrom(
        (Join-Path $VintageStoryPath 'VintagestoryLib.dll')).GetType('SaveGame', $true)
}

function Write-L02CBigEndianInt32 {
    param([Parameter(Mandatory)][IO.Stream]$Stream, [Parameter(Mandatory)][int]$Value)

    $bytes = [BitConverter]::GetBytes([Net.IPAddress]::HostToNetworkOrder($Value))
    $Stream.Write($bytes, 0, $bytes.Length)
}

function Write-L02CLengthPrefixedBytes {
    param([Parameter(Mandatory)][IO.Stream]$Stream, [Parameter(Mandatory)][byte[]]$Bytes)

    Write-L02CBigEndianInt32 $Stream $Bytes.Length
    $Stream.Write($Bytes, 0, $Bytes.Length)
}

function New-L02CCommittedEnvelope {
    $utf8 = [Text.UTF8Encoding]::new($false)
    $payloadStream = [IO.MemoryStream]::new()
    try {
        Write-L02CLengthPrefixedBytes $payloadStream ($utf8.GetBytes('fixture-save'))
        foreach ($dimension in @(4096, 256, 4096, 32)) {
            Write-L02CBigEndianInt32 $payloadStream $dimension
        }
        Write-L02CLengthPrefixedBytes $payloadStream ($utf8.GetBytes('vintagestory-1.22.7-effective-world-v1'))
        Write-L02CBigEndianInt32 $payloadStream 1
        $payloadStream.Write(([byte[]](1..32)), 0, 32)
        Write-L02CLengthPrefixedBytes $payloadStream ($utf8.GetBytes('isrworldgen.core.frozen-scale-profile'))
        Write-L02CBigEndianInt32 $payloadStream 1
        $payloadStream.WriteByte(2)
        Write-L02CLengthPrefixedBytes $payloadStream ([byte[]](1..32))
        $payload = $payloadStream.ToArray()
    }
    finally { $payloadStream.Dispose() }

    $envelopeStream = [IO.MemoryStream]::new()
    try {
        $magic = [Text.Encoding]::ASCII.GetBytes('ISRNPF01')
        $envelopeStream.Write($magic, 0, $magic.Length)
        Write-L02CBigEndianInt32 $envelopeStream 1
        Write-L02CBigEndianInt32 $envelopeStream $payload.Length
        $envelopeStream.Write($payload, 0, $payload.Length)
        $checksum = [Security.Cryptography.SHA256]::HashData($envelopeStream.ToArray())
        $envelopeStream.Write($checksum, 0, $checksum.Length)
        return $envelopeStream.ToArray()
    }
    finally { $envelopeStream.Dispose() }
}

function New-L02CSaveGameBytes {
    param([AllowNull()][byte[]]$Envelope)

    $saveGame = [Activator]::CreateInstance($script:l02cFixtureSaveGameType)
    if ($null -eq $saveGame.ModData) {
        $field = $script:l02cFixtureSaveGameType.GetField('ModData')
        $field.SetValue($saveGame, [Activator]::CreateInstance($field.FieldType))
    }
    foreach ($entry in @(
        [pscustomobject]@{ Name = 'CreatedGameVersion'; Value = '1.22.7' },
        [pscustomobject]@{ Name = 'LastSavedGameVersion'; Value = '1.22.7' },
        [pscustomobject]@{ Name = 'LastSavedGameVersionWhenLoaded'; Value = '1.22.7' },
        [pscustomobject]@{ Name = 'WorldName'; Value = 'L02C fixture' },
        [pscustomobject]@{ Name = 'WorldType'; Value = 'standard' },
        [pscustomobject]@{ Name = 'PlayStyle'; Value = 'surviveandbuild' },
        [pscustomobject]@{ Name = 'PlayStyleLangCode'; Value = 'preset-surviveandbuild' },
        [pscustomobject]@{ Name = 'SavegameIdentifier'; Value = '00000000-0000-0000-0000-000000000001' }
    )) {
        $script:l02cFixtureSaveGameType.GetField(
            $entry.Name,
            [Reflection.BindingFlags]'Instance,Public,NonPublic').SetValue($saveGame, $entry.Value)
    }
    if ($null -ne $Envelope) {
        $saveGame.ModData['isrworldgen:l02c:frozen-profile:v1'] = $Envelope
    }
    $stream = [IO.MemoryStream]::new()
    try {
        [ProtoBuf.Serializer+NonGeneric]::Serialize($stream, $saveGame)
        return $stream.ToArray()
    }
    finally { $stream.Dispose() }
}

function Initialize-L02CDatabaseContent {
    param(
        [Parameter(Mandatory)][Microsoft.Data.Sqlite.SqliteConnection]$Connection,
        [Parameter(Mandatory)][byte[]]$GameData,
        [Parameter(Mandatory)][int]$GeographyRows
    )

    $transaction = $Connection.BeginTransaction()
    try {
        $command = $Connection.CreateCommand()
        $command.Transaction = $transaction
        $command.CommandText = @'
CREATE TABLE gamedata (savegameid INTEGER PRIMARY KEY, data BLOB NOT NULL);
CREATE TABLE chunk (position INTEGER PRIMARY KEY, data BLOB NOT NULL);
CREATE TABLE mapchunk (position INTEGER PRIMARY KEY, data BLOB NOT NULL);
CREATE TABLE mapregion (position INTEGER PRIMARY KEY, data BLOB NOT NULL);
'@
        [void]$command.ExecuteNonQuery()
        $command.CommandText = 'INSERT INTO gamedata(savegameid, data) VALUES (1, $data)'
        $parameter = $command.CreateParameter()
        $parameter.ParameterName = '$data'
        $parameter.Value = $GameData
        [void]$command.Parameters.Add($parameter)
        [void]$command.ExecuteNonQuery()
        for ($index = 1; $index -le $GeographyRows; $index++) {
            foreach ($table in @('chunk', 'mapchunk', 'mapregion')) {
                $command.Parameters.Clear()
                $command.CommandText = "INSERT INTO $table(position, data) VALUES ($index, X'01')"
                [void]$command.ExecuteNonQuery()
            }
        }
        $transaction.Commit()
    }
    finally { $transaction.Dispose() }
}

function Copy-L02CFixtureFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    $inputStream = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose() }
    }
    finally { $inputStream.Dispose() }
}

function New-L02CSqliteSourceFixture {
    param(
        [Parameter(Mandatory)][string]$Path,
        [AllowNull()][byte[]]$Envelope,
        [Parameter(Mandatory)][int]$GeographyRows,
        [Parameter(Mandatory)][bool]$WalDependent
    )

    $resolved = [IO.Path]::GetFullPath($Path)
    $parent = Split-Path -Parent $resolved
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    if (Test-Path -LiteralPath $resolved) {
        throw "Fixture database already exists: $resolved"
    }
    $gameData = New-L02CSaveGameBytes $Envelope
    if (-not $WalDependent) {
        $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new(
            "Data Source=$resolved;Mode=ReadWriteCreate;Pooling=False")
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = 'PRAGMA journal_mode=DELETE'
            [void]$command.ExecuteScalar()
            Initialize-L02CDatabaseContent $connection $gameData $GeographyRows
        }
        finally {
            if ($null -ne $connection) { $connection.Dispose() }
            [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
        }
        return
    }

    $stagingRoot = Join-Path $parent ('wal-staging-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    $stagingMain = Join-Path $stagingRoot 'source.vcdbs'
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new(
        "Data Source=$stagingMain;Mode=ReadWriteCreate;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'PRAGMA journal_mode=WAL'
        if ([string]$command.ExecuteScalar() -cne 'wal') {
            throw 'Fixture SQLite did not enter WAL mode.'
        }
        $command.CommandText = 'PRAGMA wal_autocheckpoint=0'
        [void]$command.ExecuteScalar()
        Initialize-L02CDatabaseContent $connection $gameData $GeographyRows
        foreach ($source in @($stagingMain, "$stagingMain-wal", "$stagingMain-shm")) {
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Fixture WAL source set is incomplete: $source"
            }
        }
        Copy-L02CFixtureFile $stagingMain $resolved
        Copy-L02CFixtureFile "$stagingMain-wal" "$resolved-wal"
        Copy-L02CFixtureFile "$stagingMain-shm" "$resolved-shm"
    }
    finally {
        if ($null -ne $connection) { $connection.Dispose() }
        [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
}
