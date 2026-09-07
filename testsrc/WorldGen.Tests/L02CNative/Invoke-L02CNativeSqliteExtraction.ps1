[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceDatabasePath,
    [Parameter(Mandatory)]
    [string]$OutputPath,
    [Parameter(Mandatory)]
    [string]$SealedSourceDirectory,
    [Parameter(Mandatory)]
    [string]$TestedCommit,
    [Parameter(Mandatory)]
    [string]$SessionId,
    [Parameter(Mandatory)]
    [ValidateSet('new', 'reload', 'height', 'rectangle')]
    [string]$CaseRole,
    [Parameter(Mandatory)]
    [int]$ServerPid,
    [Parameter(Mandatory)]
    [string]$LogPath,
    [Parameter(Mandatory)]
    [string]$LogSha256,
    [Parameter(Mandatory)]
    [string]$SnapshotManifestPath,
    [Parameter(Mandatory)]
    [string]$SnapshotManifestSha256,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$VintageStoryPath = $env:VINTAGE_STORY
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$storageKey = 'isrworldgen:l02c:frozen-profile:v1'
$maximumLogBytes = 16 * 1024 * 1024
$maximumManifestBytes = 64 * 1024
$maximumGameDataBytes = 16 * 1024 * 1024
$maximumEnvelopeBytes = 8 * 1024
$maximumSourceBytes = [ordered]@{
    Main = 256GB
    Wal = 64GB
    Shm = 1GB
}
$clonePrefix = 'isrworldgen-l02c-sqlite-clone-'
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$cloneRoot = Join-Path $tempBase ($clonePrefix + [Guid]::NewGuid().ToString('N'))

function Assert-LeafFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Evidence)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Evidence file is missing: $Path"
    }
}

function Read-BoundedTextFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$MaximumBytes,
        [Parameter(Mandatory)][string]$Evidence
    )

    Assert-LeafFile $Path $Evidence
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -gt $MaximumBytes) {
        throw "$Evidence exceeds maximum byte length (observed=$($item.Length) maximum=$MaximumBytes)."
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Write-NewUtf8File {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Content)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}

function Get-TextSha256 {
    param([Parameter(Mandatory)][string]$Value)

    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($Value)))
}

function Get-NormalizedPathSha256 {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = [IO.Path]::GetFullPath($Path).Replace('/', '\').TrimEnd('\').ToUpperInvariant()
    return Get-TextSha256 $normalized
}

function Get-CurrentCommit {
    $result = & git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve repository HEAD: $($result -join [Environment]::NewLine)"
    }

    $commit = ([string]($result | Select-Object -Last 1)).Trim()
    if ($commit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Repository HEAD is not an exact lowercase 40-character commit.'
    }

    return $commit
}

function Assert-SafeFileName {
    param([Parameter(Mandatory)][string]$FileName)

    if ($FileName.Length -lt 1 -or $FileName.Length -gt 128 -or
        $FileName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $FileName.Contains('/', [StringComparison]::Ordinal) -or
        $FileName.Contains('\', [StringComparison]::Ordinal) -or
        $FileName -match '[\x00-\x1f\x7f]') {
        throw 'Source database file name is outside the bounded evidence schema.'
    }
}

function Get-SourceSet {
    param([Parameter(Mandatory)][string]$MainPath)

    Assert-LeafFile $MainPath 'source SQLite main database'
    $definitions = @(
        [ordered]@{ FileType = 'Main'; Path = $MainPath },
        [ordered]@{ FileType = 'Wal'; Path = "$MainPath-wal" },
        [ordered]@{ FileType = 'Shm'; Path = "$MainPath-shm" }
    )
    $result = @()
    foreach ($definition in $definitions) {
        if ($definition.FileType -ceq 'Main' -or (Test-Path -LiteralPath $definition.Path -PathType Leaf)) {
            $fileName = [IO.Path]::GetFileName($definition.Path)
            Assert-SafeFileName $fileName
            $result += [ordered]@{
                FileType = $definition.FileType
                FileName = $fileName
                Path = [IO.Path]::GetFullPath($definition.Path)
                PathSha256 = Get-NormalizedPathSha256 $definition.Path
            }
        }
    }

    return @($result)
}

function Get-SourceFileSeal {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateSet('Main', 'Wal', 'Shm')][string]$FileType
    )

    Assert-LeafFile $Path "source SQLite $FileType file"
    $before = Get-Item -LiteralPath $Path
    $maximum = [long]$maximumSourceBytes[$FileType]
    if ($before.Length -gt $maximum) {
        throw "Source SQLite $FileType file exceeds maximum byte length (observed=$($before.Length) maximum=$maximum)."
    }

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $sha256 = [Convert]::ToHexString($algorithm.ComputeHash($stream)) }
        finally { $algorithm.Dispose() }
    }
    finally { $stream.Dispose() }

    $after = Get-Item -LiteralPath $Path
    if ($after.Length -ne $before.Length -or $after.LastWriteTimeUtc -ne $before.LastWriteTimeUtc) {
        throw "Source SQLite $FileType file changed while its raw stream was sealed."
    }

    return [ordered]@{
        Length = [long]$before.Length
        LastWriteTimeUtc = ([DateTimeOffset]$before.LastWriteTimeUtc).ToUniversalTime().ToString('o')
        Sha256 = $sha256
    }
}

function Assert-SealEqual {
    param([Parameter(Mandatory)]$Expected, [Parameter(Mandatory)]$Actual, [Parameter(Mandatory)][string]$Label)

    foreach ($name in @('Length', 'LastWriteTimeUtc', 'Sha256')) {
        if ([string]$Expected[$name] -cne [string]$Actual[$name]) {
            throw "$Label source seal changed at $name."
        }
    }
}

function Copy-SealedSourceFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    $inputStream = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose() }
    }
    finally { $inputStream.Dispose() }
}

function Get-CloneFileSeal {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $sha256 = [Convert]::ToHexString($algorithm.ComputeHash($stream)) }
        finally { $algorithm.Dispose() }
    }
    finally { $stream.Dispose() }

    return [ordered]@{ Length = [long]$item.Length; Sha256 = $sha256 }
}

function Assert-SourceSetEqual {
    param([Parameter(Mandatory)][object[]]$Expected, [Parameter(Mandatory)][object[]]$Actual, [Parameter(Mandatory)][string]$Label)

    if ($Expected.Count -ne $Actual.Count) {
        throw "$Label source SQLite file set changed."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        foreach ($name in @('FileType', 'FileName', 'PathSha256')) {
            if ([string]$Expected[$index][$name] -cne [string]$Actual[$index][$name]) {
                throw "$Label source SQLite file set changed at $name."
            }
        }
    }
}

function Assert-PathWithinCloneRoot {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$RequiredCloneRoot)

    $databaseFull = [IO.Path]::GetFullPath($Path)
    $cloneRootFull = [IO.Path]::GetFullPath($RequiredCloneRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $databaseFull.StartsWith(
        $cloneRootFull + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SQLite query target is outside the temporary clone root.'
    }

    return $databaseFull
}

function Initialize-SqliteRuntime {
    if ([string]::IsNullOrWhiteSpace($VintageStoryPath)) {
        throw 'VintageStoryPath or VINTAGE_STORY must identify the audited installation.'
    }
    $libraryRoot = Join-Path $VintageStoryPath 'Lib'
    $nativeSqlite = Join-Path $libraryRoot 'e_sqlite3.dll'
    $assemblies = @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlite3.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll',
        'protobuf-net.dll'
    )
    foreach ($path in @($nativeSqlite) + @($assemblies | ForEach-Object { Join-Path $libraryRoot $_ })) {
        Assert-LeafFile $path 'local SQLite/protobuf runtime'
    }
    Assert-LeafFile (Join-Path $VintageStoryPath 'VintagestoryAPI.dll') 'Vintage Story API assembly'
    Assert-LeafFile (Join-Path $VintageStoryPath 'VintagestoryLib.dll') 'Vintage Story library assembly'

    [void][Runtime.InteropServices.NativeLibrary]::Load($nativeSqlite)
    foreach ($assembly in $assemblies) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $libraryRoot $assembly))
    }
    [SQLitePCL.Batteries_V2]::Init()
    [void][Reflection.Assembly]::LoadFrom((Join-Path $VintageStoryPath 'VintagestoryAPI.dll'))
    $script:saveGameType = [Reflection.Assembly]::LoadFrom(
        (Join-Path $VintageStoryPath 'VintagestoryLib.dll')).GetType('SaveGame', $true)
}

function Read-BigEndianInt32 {
    param([Parameter(Mandatory)][byte[]]$Bytes, [Parameter(Mandatory)][ref]$Offset)

    if ($Offset.Value -lt 0 -or $Offset.Value + 4 -gt $Bytes.Length) {
        throw 'Envelope integer is truncated.'
    }
    $encoded = [byte[]]::new(4)
    [Array]::Copy($Bytes, $Offset.Value, $encoded, 0, 4)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($encoded) }
    $value = [BitConverter]::ToInt32($encoded, 0)
    $Offset.Value += 4
    return $value
}

function Read-BigEndianUInt32At {
    param([Parameter(Mandatory)][byte[]]$Bytes, [Parameter(Mandatory)][int]$Offset)

    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) {
        throw 'Envelope unsigned integer is truncated.'
    }
    $encoded = [byte[]]::new(4)
    [Array]::Copy($Bytes, $Offset, $encoded, 0, 4)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($encoded) }
    return [BitConverter]::ToUInt32($encoded, 0)
}

function Skip-LengthPrefixedEnvelopeField {
    param([Parameter(Mandatory)][byte[]]$Bytes, [Parameter(Mandatory)][ref]$Offset)

    $length = Read-BigEndianInt32 $Bytes $Offset
    if ($length -lt 0 -or $Offset.Value + $length -gt $Bytes.Length) {
        throw 'Envelope length-prefixed field is invalid.'
    }
    $Offset.Value += $length
}

function Get-EnvelopeState {
    param([Parameter(Mandatory)][byte[]]$Envelope)

    if ($Envelope.Length -lt 48 -or $Envelope.Length -gt $maximumEnvelopeBytes) {
        throw 'Envelope length is outside the canonical bound.'
    }
    $magic = [Text.Encoding]::ASCII.GetBytes('ISRNPF01')
    for ($index = 0; $index -lt $magic.Length; $index++) {
        if ($Envelope[$index] -ne $magic[$index]) {
            throw 'Envelope magic is invalid.'
        }
    }
    if ($magic.Length -ne 8) {
        throw 'Envelope magic is invalid.'
    }
    $version = Read-BigEndianUInt32At $Envelope 8
    if ($version -ne 1) {
        throw 'Envelope version is unsupported.'
    }
    $payloadOffset = 12
    $payloadLength = Read-BigEndianInt32 $Envelope ([ref]$payloadOffset)
    if ($payloadLength -lt 0 -or 16 + $payloadLength + 32 -ne $Envelope.Length) {
        throw 'Envelope payload length is invalid.'
    }
    $checksumOffset = $Envelope.Length - 32
    $checksumInput = [byte[]]::new($checksumOffset)
    [Array]::Copy($Envelope, 0, $checksumInput, 0, $checksumOffset)
    $expectedChecksum = [Security.Cryptography.SHA256]::HashData($checksumInput)
    for ($index = 0; $index -lt 32; $index++) {
        if ($Envelope[$checksumOffset + $index] -ne $expectedChecksum[$index]) {
            throw 'Envelope checksum is invalid.'
        }
    }

    $offset = 16
    Skip-LengthPrefixedEnvelopeField $Envelope ([ref]$offset)
    if ($offset + 16 -gt $checksumOffset) { throw 'Envelope dimensions are truncated.' }
    $offset += 16
    Skip-LengthPrefixedEnvelopeField $Envelope ([ref]$offset)
    if ($offset + 36 -gt $checksumOffset) { throw 'Envelope ruleset/hash fields are truncated.' }
    $offset += 36
    Skip-LengthPrefixedEnvelopeField $Envelope ([ref]$offset)
    if ($offset + 5 -gt $checksumOffset) { throw 'Envelope codec/state fields are truncated.' }
    $offset += 4
    $state = $Envelope[$offset]
    $offset++
    $profileLength = Read-BigEndianInt32 $Envelope ([ref]$offset)
    if ($profileLength -lt 1 -or $profileLength -gt 4096 -or $offset + $profileLength -ne $checksumOffset) {
        throw 'Envelope profile payload is invalid or has trailing data.'
    }
    switch ($state) {
        1 { $stateName = 'Pending' }
        2 { $stateName = 'Committed' }
        3 { $stateName = 'Rejected' }
        default { throw 'Envelope persistence state is invalid.' }
    }
    return $stateName
}

function Get-CloneResultHash {
    param([Parameter(Mandatory)]$Snapshot)

    $canonical = @(
        [string]$Snapshot.Integrity,
        [string]$Snapshot.GameDataBytes,
        [string]$Snapshot.ModDataCount,
        [string]$Snapshot.KeyStatus,
        [string]$Snapshot.EnvelopeBytes,
        [string]$Snapshot.EnvelopeSha256,
        [string]$Snapshot.EnvelopeState,
        [string]$Snapshot.ChunkRows,
        [string]$Snapshot.MapChunkRows,
        [string]$Snapshot.MapRegionRows
    ) -join '|'
    return Get-TextSha256 $canonical
}

function Read-CloneDatabaseSnapshot {
    param([Parameter(Mandatory)][string]$DatabasePath, [Parameter(Mandatory)][string]$RequiredCloneRoot)

    $cloneDatabasePath = Assert-PathWithinCloneRoot $DatabasePath $RequiredCloneRoot
    $connectionString = "Data Source=$cloneDatabasePath;Mode=ReadOnly;Pooling=False"
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new($connectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'PRAGMA integrity_check'
        $integrity = [string]$command.ExecuteScalar()
        if ($integrity -cne 'ok') {
            throw 'Clone SQLite integrity_check did not return ok.'
        }

        $counts = @{}
        foreach ($table in @('chunk', 'mapchunk', 'mapregion')) {
            $command.CommandText = "SELECT count(*) FROM $table"
            $counts[$table] = [long]$command.ExecuteScalar()
        }

        $command.CommandText = 'SELECT length(data) FROM gamedata WHERE savegameid=1'
        $lengthValue = $command.ExecuteScalar()
        $gameDataBytes = if ($null -eq $lengthValue -or $lengthValue -eq [DBNull]::Value) { 0 } else { [long]$lengthValue }
        if ($gameDataBytes -lt 0 -or $gameDataBytes -gt $maximumGameDataBytes) {
            throw "Clone gamedata exceeds maximum byte length (observed=$gameDataBytes maximum=$maximumGameDataBytes)."
        }

        $modDataCount = 0
        $keyStatus = 'Absent'
        $envelopeBytes = 0
        $envelopeSha256 = $null
        $envelopeState = 'Absent'
        if ($gameDataBytes -gt 0) {
            $command.CommandText = 'SELECT data FROM gamedata WHERE savegameid=1'
            $raw = [byte[]]$command.ExecuteScalar()
            if ($raw.Length -ne $gameDataBytes) {
                throw 'Clone gamedata length changed between bounded length query and read.'
            }
            $stream = [IO.MemoryStream]::new($raw, $false)
            try { $saveGame = [ProtoBuf.Serializer+NonGeneric]::Deserialize($script:saveGameType, $stream) }
            finally { $stream.Dispose() }
            $modData = $saveGame.ModData
            $modDataCount = if ($null -eq $modData) { 0 } else { $modData.Count }
            if ($null -ne $modData -and $modData.ContainsKey($storageKey)) {
                $envelope = [byte[]]$modData[$storageKey]
                if ($envelope.Length -gt $maximumEnvelopeBytes) {
                    throw "Envelope exceeds maximum byte length (observed=$($envelope.Length) maximum=$maximumEnvelopeBytes)."
                }
                $keyStatus = 'Present'
                $envelopeBytes = $envelope.Length
                $envelopeSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($envelope))
                $envelopeState = Get-EnvelopeState $envelope
            }
        }

        $snapshot = [ordered]@{
            Integrity = $integrity
            GameDataBytes = $gameDataBytes
            ModDataCount = $modDataCount
            KeyStatus = $keyStatus
            EnvelopeBytes = $envelopeBytes
            EnvelopeSha256 = $envelopeSha256
            EnvelopeState = $envelopeState
            ChunkRows = [long]$counts.chunk
            MapChunkRows = [long]$counts.mapchunk
            MapRegionRows = [long]$counts.mapregion
        }
        $snapshot.ResultSha256 = Get-CloneResultHash $snapshot
        return $snapshot
    }
    finally {
        if ($null -ne $connection) { $connection.Dispose() }
        [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    }
}

function Get-SourceSetHash {
    param([Parameter(Mandatory)][object[]]$SourceFiles)

    $rows = foreach ($file in $SourceFiles) {
        @(
            $file.FileType,
            $file.FileName,
            $file.PathSha256,
            [string]$file.PreCopy.Length,
            $file.PreCopy.LastWriteTimeUtc,
            $file.PreCopy.Sha256
        ) -join '|'
    }
    return Get-TextSha256 ($rows -join "`n")
}

if ($TestedCommit -cnotmatch '^[0-9a-f]{40}$' -or (Get-CurrentCommit) -cne $TestedCommit) {
    throw 'TestedCommit does not match the exact repository HEAD.'
}
if ($SessionId -cnotmatch '^[0-9a-f]{32}$' -or $ServerPid -le 0 -or
    $LogSha256 -cnotmatch '^[0-9A-F]{64}$' -or $SnapshotManifestSha256 -cnotmatch '^[0-9A-F]{64}$') {
    throw 'Extraction identity fields are malformed.'
}
if (Test-Path -LiteralPath $OutputPath) {
    throw "Extraction output already exists and cannot be replaced: $OutputPath"
}

$logContent = Read-BoundedTextFile $LogPath $maximumLogBytes 'runtime log'
if ((Get-FileHash -LiteralPath $LogPath -Algorithm SHA256).Hash -cne $LogSha256 -or
    -not $logContent.Contains("L00B_DEBUG_PROBE_READY pid=$ServerPid ", [StringComparison]::Ordinal) -or
    -not $logContent.Contains('Stopped the server!', [StringComparison]::Ordinal)) {
    throw 'Runtime log does not match the bound stopped server session.'
}
$null = Read-BoundedTextFile $SnapshotManifestPath $maximumManifestBytes 'prelaunch snapshot manifest'
if ((Get-FileHash -LiteralPath $SnapshotManifestPath -Algorithm SHA256).Hash -cne $SnapshotManifestSha256) {
    throw 'Prelaunch snapshot manifest hash does not match the extraction binding.'
}

$resolvedSource = [IO.Path]::GetFullPath($SourceDatabasePath)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$resolvedSealedSourceDirectory = [IO.Path]::GetFullPath($SealedSourceDirectory)
$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
if (Test-Path -LiteralPath $resolvedSealedSourceDirectory) {
    throw "Sealed source directory already exists and cannot be replaced: $resolvedSealedSourceDirectory"
}

[IO.Directory]::CreateDirectory($cloneRoot) | Out-Null
try {
    $sourceSet = @(Get-SourceSet $resolvedSource)
    $preSeals = @{}
    foreach ($file in $sourceSet) {
        $preSeals[$file.FileType] = Get-SourceFileSeal $file.Path $file.FileType
    }

    [IO.Directory]::CreateDirectory($resolvedSealedSourceDirectory) | Out-Null
    $sealedNames = [ordered]@{ Main = 'source.vcdbs'; Wal = 'source.vcdbs-wal'; Shm = 'source.vcdbs-shm' }
    $cloneNames = [ordered]@{ Main = 'clone.vcdbs'; Wal = 'clone.vcdbs-wal'; Shm = 'clone.vcdbs-shm' }
    $sealedFiles = @()
    $cloneFiles = @()
    foreach ($file in $sourceSet) {
        $sealedPath = Join-Path $resolvedSealedSourceDirectory $sealedNames[$file.FileType]
        Copy-SealedSourceFile $file.Path $sealedPath
        $sealedSeal = Get-CloneFileSeal $sealedPath
        if ($preSeals[$file.FileType].Length -ne $sealedSeal.Length -or
            $preSeals[$file.FileType].Sha256 -cne $sealedSeal.Sha256) {
            throw "Sealed $($file.FileType) evidence does not match its source bytes."
        }
        $sealedFiles += [ordered]@{
            FileName = $sealedNames[$file.FileType]
            FileType = $file.FileType
            PathSha256 = Get-NormalizedPathSha256 $sealedPath
            Length = $sealedSeal.Length
            Sha256 = $sealedSeal.Sha256
        }

        $clonePath = Join-Path $cloneRoot $cloneNames[$file.FileType]
        Copy-SealedSourceFile $file.Path $clonePath
        $cloneSeal = Get-CloneFileSeal $clonePath
        if ($preSeals[$file.FileType].Length -ne $cloneSeal.Length -or
            $preSeals[$file.FileType].Sha256 -cne $cloneSeal.Sha256) {
            throw "Copied $($file.FileType) clone does not match its sealed source bytes."
        }
        $cloneFiles += [ordered]@{
            FileName = $cloneNames[$file.FileType]
            FileType = $file.FileType
            Length = $cloneSeal.Length
            Sha256 = $cloneSeal.Sha256
        }
    }

    $afterCopySet = @(Get-SourceSet $resolvedSource)
    Assert-SourceSetEqual $sourceSet $afterCopySet 'After-copy'
    $afterCopySeals = @{}
    foreach ($file in $afterCopySet) {
        $afterCopySeals[$file.FileType] = Get-SourceFileSeal $file.Path $file.FileType
        Assert-SealEqual $preSeals[$file.FileType] $afterCopySeals[$file.FileType] "After-copy $($file.FileType)"
    }

    Initialize-SqliteRuntime
    $cloneMainPath = Join-Path $cloneRoot $cloneNames.Main
    try {
        $fullSnapshot = Read-CloneDatabaseSnapshot $cloneMainPath $cloneRoot
    }
    catch {
        throw 'Clone SQLite snapshot could not be reconstructed from the sealed source set.'
    }
    $composition = @($cloneFiles | ForEach-Object { $_.FileType })
    $walContribution = 'Absent'
    $mainOnlyStatus = 'NotRun'
    $mainOnlyResultSha256 = $null
    if ($composition -ccontains 'Wal') {
        $baseOnlyRoot = Join-Path $cloneRoot 'base-only'
        [IO.Directory]::CreateDirectory($baseOnlyRoot) | Out-Null
        $baseOnlyMainPath = Join-Path $baseOnlyRoot 'clone.vcdbs'
        Copy-SealedSourceFile (Join-Path $resolvedSealedSourceDirectory $sealedNames.Main) $baseOnlyMainPath
        try {
            $mainOnly = Read-CloneDatabaseSnapshot $baseOnlyMainPath $cloneRoot
            $mainOnlyStatus = 'Readable'
            $mainOnlyResultSha256 = $mainOnly.ResultSha256
            $walContribution = if ($mainOnly.ResultSha256 -ceq $fullSnapshot.ResultSha256) {
                'PresentStateEquivalent'
            }
            else {
                'RequiredForObservedState'
            }
        }
        catch {
            $mainOnlyStatus = 'Unreadable'
            $walContribution = 'RequiredForObservedState'
        }
    }

    $postSet = @(Get-SourceSet $resolvedSource)
    Assert-SourceSetEqual $sourceSet $postSet 'Post-extraction'
    $postSeals = @{}
    foreach ($file in $postSet) {
        $postSeals[$file.FileType] = Get-SourceFileSeal $file.Path $file.FileType
        Assert-SealEqual $preSeals[$file.FileType] $postSeals[$file.FileType] "Post-extraction $($file.FileType)"
    }

    $sourceFiles = @()
    foreach ($file in $sourceSet) {
        $sourceFiles += [ordered]@{
            FileName = $file.FileName
            FileType = $file.FileType
            PathSha256 = $file.PathSha256
            PreCopy = $preSeals[$file.FileType]
            AfterCopy = $afterCopySeals[$file.FileType]
            PostExtraction = $postSeals[$file.FileType]
        }
    }

    $clone = [ordered]@{
        Files = $cloneFiles
        Integrity = $fullSnapshot.Integrity
        GameDataBytes = $fullSnapshot.GameDataBytes
        ModDataCount = $fullSnapshot.ModDataCount
        StorageKey = $storageKey
        KeyStatus = $fullSnapshot.KeyStatus
        EnvelopeBytes = $fullSnapshot.EnvelopeBytes
        EnvelopeSha256 = $fullSnapshot.EnvelopeSha256
        EnvelopeState = $fullSnapshot.EnvelopeState
        ChunkRows = $fullSnapshot.ChunkRows
        MapChunkRows = $fullSnapshot.MapChunkRows
        MapRegionRows = $fullSnapshot.MapRegionRows
        ResultSha256 = $fullSnapshot.ResultSha256
        WalEvidence = [ordered]@{
            Composition = $composition
            WalContribution = $walContribution
            FullResultSha256 = $fullSnapshot.ResultSha256
            MainOnlyStatus = $mainOnlyStatus
            MainOnlyResultSha256 = $mainOnlyResultSha256
        }
    }
    $report = [ordered]@{
        Schema = 'isrworldgen.t02-05.sqlite-extraction.v1'
        TestedCommit = $TestedCommit
        SessionId = $SessionId
        CaseRole = $CaseRole
        ServerPid = $ServerPid
        LogSha256 = $LogSha256
        SnapshotManifestSha256 = $SnapshotManifestSha256
        ExtractedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        SourceMainPathSha256 = Get-NormalizedPathSha256 $resolvedSource
        SourceSetSha256 = Get-SourceSetHash $sourceFiles
        SourceFiles = $sourceFiles
        SealedSourceSetSha256 = Get-TextSha256 (($sealedFiles | ForEach-Object {
            @($_.FileType, $_.FileName, $_.PathSha256, [string]$_.Length, $_.Sha256) -join '|'
        }) -join "`n")
        SealedFiles = $sealedFiles
        Clone = $clone
    }
    Write-NewUtf8File $resolvedOutput ($report | ConvertTo-Json -Depth 12)
    $report | ConvertTo-Json -Depth 12
}
finally {
    $resolvedClone = [IO.Path]::GetFullPath($cloneRoot)
    $cloneLeaf = Split-Path -Leaf $resolvedClone
    if (-not $resolvedClone.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
        $cloneLeaf -cnotmatch '^isrworldgen-l02c-sqlite-clone-[0-9a-f]{32}$') {
        throw "Refusing to clean unexpected SQLite clone path: $resolvedClone"
    }
    if (Test-Path -LiteralPath $resolvedClone) {
        Remove-Item -LiteralPath $resolvedClone -Recurse -Force
    }
}
