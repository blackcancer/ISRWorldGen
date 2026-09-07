[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot 'L00CInitializationRefusalEvidence.psm1'
Import-Module $modulePath -Force

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Label)

    try {
        [void](& $Action)
    }
    catch {
        return
    }

    throw "$Label was accepted unexpectedly."
}

function New-RefusalLog([string]$InstanceId) {
    return @(
        "L00C_PROBE_READY instance=$InstanceId pid=12345 enabled=True autorun=False autoshutdown=True autoshutdowndelayms=0",
        "L00C_TRANSIENT_CALLBACK_RESET reason=world-initialize instance=$InstanceId cancelled=0 pending=0 exact=True",
        "L00C_HANDLERS phase=before instance=$InstanceId save=11111111-1111-1111-1111-111111111111 isnew=True samehandlerset=False staleprobe=0",
        "L00C_ERROR code=expected-handler-absent instance=$InstanceId message=expected",
        "L00C_INITIALIZATION_FAILED instance=$InstanceId attempt=1 type=System.InvalidOperationException message=expected",
        'Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API',
        'Entering runphase Shutdown',
        "L00C_TRANSIENT_CALLBACK_RESET reason=dispose instance=$InstanceId cancelled=0 pending=0 exact=True",
        "L00C_DISPOSED instance=$InstanceId removedowned=0 restorednative=0 exact=True callbacks=0 forwarded=0",
        'Stopped the server!',
        "L00C_INITIALIZATION_SHUTDOWN instance=$InstanceId attempt=1 accepted=True cleanupError=none shutdownError=none"
    ) -join [Environment]::NewLine
}

function New-EmptyRefusalDatabase([string]$Path) {
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$Path;Mode=ReadWriteCreate;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = @'
CREATE TABLE chunk (position integer PRIMARY KEY, data BLOB);
CREATE TABLE gamedata (savegameid integer PRIMARY KEY, data BLOB);
CREATE TABLE mapchunk (position integer PRIMARY KEY, data BLOB);
CREATE TABLE mapregion (position integer PRIMARY KEY, data BLOB);
INSERT INTO gamedata(savegameid, data) VALUES (1, X'01020304');
'@
            [void]$command.ExecuteNonQuery()
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }
}

$sqliteDirectory = Join-Path $GamePath 'Lib'
[void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $sqliteDirectory 'e_sqlite3.dll'))
foreach ($assembly in @('SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'SQLitePCLRaw.batteries_v2.dll', 'Microsoft.Data.Sqlite.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $sqliteDirectory $assembly))
}
[SQLitePCL.Batteries_V2]::Init()

$instance = 'a' * 32
$validLog = New-RefusalLog $instance
$validLogResult = Assert-L00CInitializationRefusalLog -WorldRole 'missing-handler' -Log $validLog -InstanceId $instance
if (-not $validLogResult.ExactShutdown -or -not $validLogResult.ZeroProbeMutation -or $validLogResult.WorldSaveCount -ne 0) {
    throw 'The real missing-handler log shape was not classified as an immediate fail-closed refusal.'
}

Assert-Rejected { Assert-L00CInitializationRefusalLog -WorldRole 'activated-primary' -Log $validLog -InstanceId $instance } 'Wrong role classification'
Assert-Rejected { Assert-L00CInitializationRefusalLog -WorldRole 'missing-handler' -Log ($validLog -replace 'Stopped the server!', 'stop missing') -InstanceId $instance } 'Missing Stopped marker'
Assert-Rejected { Assert-L00CInitializationRefusalLog -WorldRole 'missing-handler' -Log ($validLog + [Environment]::NewLine + 'World saved!') -InstanceId $instance } 'Impossible completed save during InitWorldGen refusal'
Assert-Rejected { Assert-L00CInitializationRefusalLog -WorldRole 'missing-handler' -Log ($validLog + [Environment]::NewLine + "L00C_MARKER_SAVED instance=$instance marker=$('b' * 32) open=1") -InstanceId $instance } 'Marker publication after refusal'
Assert-Rejected { Assert-L00CInitializationRefusalLog -WorldRole 'missing-handler' -Log ($validLog + [Environment]::NewLine + "L00C_FIXTURE_WRITTEN instance=$instance marker=$('b' * 32)") -InstanceId $instance } 'Fixture write after refusal'
Assert-Rejected { Assert-L00CInitializationRefusalLog -WorldRole 'missing-handler' -Log ($validLog + [Environment]::NewLine + "L00C_COLUMN_REQUEST instance=$instance") -InstanceId $instance } 'Chunk request after refusal'

$temporaryRoot = Join-Path $RepositoryRoot ('.local\L00C\initialization-refusal-selftest\' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
try {
    $databasePath = Join-Path $temporaryRoot 'refusal.vcdbs'
    New-EmptyRefusalDatabase $databasePath
    $databaseResult = Test-L00CInitializationRefusalDatabase -DatabasePath $databasePath -GamePath $GamePath
    if ($databaseResult.MapChunkRows -ne 0 -or $databaseResult.ChunkRows -ne 0 -or
        $databaseResult.MapRegionRows -ne 0 -or $databaseResult.MarkerEnvelopeOccurrences -ne 0) {
        throw 'Empty refusal database was not classified as zero-geography and zero-envelope.'
    }

    foreach ($tableName in @('mapchunk', 'chunk', 'mapregion')) {
        $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$databasePath;Mode=ReadWrite;Pooling=False")
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            try {
                $command.CommandText = "INSERT INTO [$tableName](position, data) VALUES (1, X'01')"
                [void]$command.ExecuteNonQuery()
            }
            finally {
                $command.Dispose()
            }
        }
        finally {
            $connection.Close()
            $connection.Dispose()
        }
        Assert-Rejected { Test-L00CInitializationRefusalDatabase -DatabasePath $databasePath -GamePath $GamePath } "$tableName geography"

        $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$databasePath;Mode=ReadWrite;Pooling=False")
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            try {
                $command.CommandText = "DELETE FROM [$tableName]"
                [void]$command.ExecuteNonQuery()
            }
            finally {
                $command.Dispose()
            }
        }
        finally {
            $connection.Close()
            $connection.Dispose()
        }
    }

    $markerBytes = [Text.Encoding]::UTF8.GetBytes('prefix-isrworldgen:l00c:marker:v1-suffix')
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$databasePath;Mode=ReadWrite;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'UPDATE gamedata SET data = $data WHERE savegameid = 1'
            [void]$command.Parameters.AddWithValue('$data', $markerBytes)
            [void]$command.ExecuteNonQuery()
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }
    Assert-Rejected { Test-L00CInitializationRefusalDatabase -DatabasePath $databasePath -GamePath $GamePath } 'Persisted marker envelope'
}
finally {
    [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

[ordered]@{
    TestId = 'L00-C-INITIALIZATION-REFUSAL-EVIDENCE'
    Status = 'PASS'
    RealRoleValidation = $true
    ImmediateShutdownWithoutWorldSave = $true
    StoppedRequired = $true
    ZeroProbeMutationRequired = $true
    ZeroGeographyRequired = $true
    ZeroEnvelopeRequired = $true
    NegativeCases = 10
} | ConvertTo-Json -Depth 4
