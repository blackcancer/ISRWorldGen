[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabasePath,

    [int]$FixtureChunkX = 31990,
    [int]$FixtureChunkZ = 31990,
    [int]$WorldHeight = 256,
    [int]$ChunkSize = 32,
    [int]$Dimension = 0,
    [string]$GamePath = 'D:\Jeux\Vintagestory',
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedDatabase = (Resolve-Path -LiteralPath $DatabasePath).Path
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
    DatabasePath = $resolvedDatabase
    DatabaseSha256 = (Get-FileHash -LiteralPath $resolvedDatabase -Algorithm SHA256).Hash
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

$json = $result | ConvertTo-Json -Depth 6
if ($OutputPath) {
    $directory = Split-Path -Parent $OutputPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
}

$json
if ($status -ne 'PASS') {
    throw "Persisted footprint is incomplete: mapchunks=$mapChunkCount/$expectedMapChunks chunks=$chunkCount/$expectedChunks."
}
