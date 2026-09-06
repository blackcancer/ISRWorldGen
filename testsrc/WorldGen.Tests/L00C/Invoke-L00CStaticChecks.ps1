[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory',
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$callbackGatePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\TransientLoadCallbackGate.cs'
$projectPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'

foreach ($path in @($sourcePath, $callbackGatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "L00-C source is missing: $path"
    }
}

$source = Get-Content -LiteralPath $sourcePath -Raw
$allProbeSource = $source + "`n" + (Get-Content -LiteralPath $callbackGatePath -Raw)
$forbidden = @(
    'WipeAllHandlers',
    'Task.Run(',
    'GetHashCode(',
    'new Random(',
    'DateTime.Now',
    'DateTime.UtcNow',
    'ForwardNative(',
    'ProxyForPass(',
    'FilterFixturePass(',
    'removedHandlers',
    'EnumWorldGenPass.PreDone',
    'preDoneHandlers.Add(metadataFinalizerHandler)',
    'ownedLoadedColumns',
    'ownedColumnWorldManager',
    'ColumnOwnershipState',
    'ColumnLoadRollbackException',
    'ReleaseOwnedColumns',
    'KeepLoaded = true',
    '.UnloadChunkColumn('
)
foreach ($fragment in $forbidden) {
    if ($allProbeSource.Contains($fragment)) {
        throw "Forbidden L00-C fragment found: $fragment"
    }
}

$required = @(
    '#if DEBUG',
    'GetRegisteredWorldGenHandlers',
    'OnChunkColumnGen',
    'SetBlockUnsafe',
    'SetFluid',
    'WorldGenTerrainHeightMap',
    'RainHeightMap',
    'TopRockIdMap',
    'OwnedHandler',
    'OriginalIndex',
    'OriginalTarget',
    'OriginalMethod',
    'InvokeOwnedHandler',
    'RestoreOwnedHandlerSet',
    'ReferenceSequenceEqual',
    'lightingAnchorIndex',
    'preLightingSnapshot',
    'FixtureProtectionRadius',
    'SuppressInHalo',
    'IsProtectedFixtureRequest',
    'OnHaloColumnLoaded',
    'TransientLoadCallbackGate',
    'TransientLoadReservation',
    'IsColumnAlreadyLoaded',
    'runGate',
    'OnServerTickCore(long runId',
    'ScheduleInactiveWitness',
    'L00C_WITNESS_NO_REQUEST',
    'LoadTransientChunkColumns',
    'ResetTransientLoadCallbacks',
    'RefreshTransientFootprint',
    'KeepLoaded = false',
    'mapChunk.MarkFresh()',
    'L00C_HALO_PREPARE_COMPLETE',
    'L00C_HALO_NATIVE_FORWARD',
    'L00C_HALO_COLUMN_VALID',
    'L00C_HALO_STABLE',
    'L00C_TRANSIENT_CALLBACK_RESET',
    'L00C_TRANSIENT_PRECONDITION',
    'L00C_TRANSIENT_LOAD_ACCEPTED',
    'L00C_TRANSIENT_LOAD_REJECTED',
    'L00C_FOOTPRINT_REFRESH',
    'L00C_LIGHTING_STABLE',
    'L00C_RESTORE_RESULT',
    'InspectPersistedFootprintBlocking',
    'BlockingTestMapChunkExists',
    'BlockingLoadChunkColumn',
    'L00C_PERSISTED_REOPEN_STABLE',
    'run={runId}',
    'MarkerPublicationGate',
    'markerPublication.BeginWorldTransition()',
    'markerPublication.Begin(persistedMarker)',
    'markerPublication.Commit(',
    'markerPublication.SaveIfCommitted(',
    'chunk.MarkModified()',
    'mapChunk.MarkDirty()',
    'for (int y = 0; y < worldHeight; y++)',
    'chunk.Unpack_ReadOnly()',
    "canonical.Append(solid).Append(',').Append(fluid)",
    'StoreData',
    'ShutDown',
    'IsNew'
)
foreach ($fragment in $required) {
    if (-not $allProbeSource.Contains($fragment)) {
        throw "Required L00-C fragment is missing: $fragment"
    }
}

$writeStart = $source.IndexOf('private void WriteCanonicalFixture(', [StringComparison]::Ordinal)
$writeEnd = $source.IndexOf('private bool IsProtectedFixtureRequest(', $writeStart, [StringComparison]::Ordinal)
if ($writeStart -lt 0 -or $writeEnd -le $writeStart) {
    throw 'L00-C canonical write method boundary is unavailable.'
}
$writeMethod = $source.Substring($writeStart, $writeEnd - $writeStart)
if ($writeMethod -notmatch 'foreach \(IServerChunk chunk in request\.Chunks\)\s*\{\s*chunk\.MarkModified\(\);\s*\}' -or
    $writeMethod.IndexOf('chunk.MarkModified()', [StringComparison]::Ordinal) -le $writeMethod.LastIndexOf('SetFluid(', [StringComparison]::Ordinal) -or
    $writeMethod.IndexOf('mapChunk.MarkDirty()', [StringComparison]::Ordinal) -le $writeMethod.IndexOf('chunk.MarkModified()', [StringComparison]::Ordinal)) {
    throw 'Canonical voxel writes must mark every rewritten server chunk modified before marking map metadata dirty.'
}

& dotnet build $projectPath -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "L00-C $Configuration build failed with exit code $LASTEXITCODE."
}

$assemblyPath = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen\ISRWorldGen.dll"
$pdbPath = [IO.Path]::ChangeExtension($assemblyPath, '.pdb')
foreach ($path in @($assemblyPath, $pdbPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Build artifact is missing: $path"
    }
}

$stream = [IO.File]::OpenRead($assemblyPath)
try {
    $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        $typeNames = foreach ($handle in $metadata.TypeDefinitions) {
            $definition = $metadata.GetTypeDefinition($handle)
            $namespace = $metadata.GetString($definition.Namespace)
            $name = $metadata.GetString($definition.Name)
            if ($namespace) { "$namespace.$name" } else { $name }
        }
    }
    finally {
        $peReader.Dispose()
    }
}
finally {
    $stream.Dispose()
}

$probeType = 'ISRWorldGen.WorldgenProbe.L00CWorldgenProbeModSystem'
$markerGateType = 'ISRWorldGen.WorldgenProbe.MarkerPublicationGate'
$callbackGateType = 'ISRWorldGen.WorldgenProbe.TransientLoadCallbackGate'
$probePresent = @($typeNames | Where-Object { $_ -eq $probeType }).Count -eq 1
$markerGatePresent = @($typeNames | Where-Object { $_ -eq $markerGateType }).Count -eq 1
$callbackGatePresent = @($typeNames | Where-Object { $_ -eq $callbackGateType }).Count -eq 1
if ($Configuration -eq 'Debug' -and (-not $probePresent -or -not $markerGatePresent -or -not $callbackGatePresent)) {
    throw 'Debug assembly does not contain every L00-C probe, marker, and transient callback type.'
}
if ($Configuration -eq 'Release' -and ($probePresent -or $markerGatePresent -or $callbackGatePresent)) {
    throw 'Release assembly must not contain any L00-C probe, marker, or transient callback type.'
}

$markerOracleStatus = 'NOT_APPLICABLE'
$callbackOracleStatus = 'NOT_APPLICABLE'
$persistenceAttestationOracleStatus = 'NOT_APPLICABLE'
$campaignControllerOracleStatus = 'NOT_APPLICABLE'
if ($Configuration -eq 'Debug') {
    $markerOraclePath = Join-Path $PSScriptRoot 'Test-L00CMarkerPublication.ps1'
    $markerOracle = (& $markerOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($markerOracle.Status -ne 'PASS') {
        throw 'The production marker publication oracle did not pass against the freshly built Debug assembly.'
    }
    $markerOracleStatus = $markerOracle.Status
    $callbackOraclePath = Join-Path $PSScriptRoot 'Test-L00CTransientCallbackGate.ps1'
    $callbackOracle = (& $callbackOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($callbackOracle.Status -ne 'PASS') {
        throw 'The production transient callback oracle did not pass against the freshly built Debug assembly.'
    }
    $callbackOracleStatus = $callbackOracle.Status
    $persistenceOraclePath = Join-Path $PSScriptRoot 'Test-L00CPersistenceAttestation.ps1'
    $persistenceOracle = (& $persistenceOraclePath -RepositoryRoot $RepositoryRoot | Out-String | ConvertFrom-Json)
    if ($persistenceOracle.Status -ne 'PASS') {
        throw 'The pre-open2 persistence attestation oracle did not pass.'
    }
    $persistenceAttestationOracleStatus = $persistenceOracle.Status
    $campaignControllerOraclePath = Join-Path $PSScriptRoot 'Test-L00CCampaignController.ps1'
    $campaignControllerOracle = (& $campaignControllerOraclePath -RepositoryRoot $RepositoryRoot | Out-String | ConvertFrom-Json)
    if ($campaignControllerOracle.Status -ne 'PASS') {
        throw 'The four-phase campaign controller oracle did not pass.'
    }
    $campaignControllerOracleStatus = $campaignControllerOracle.Status
}

$result = [ordered]@{
    TestId = 'L00-C-STATIC'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    Configuration = $Configuration
    ProbeTypePresent = $probePresent
    MarkerGateTypePresent = $markerGatePresent
    TransientCallbackGateTypePresent = $callbackGatePresent
    MarkerPublicationOracle = $markerOracleStatus
    TransientCallbackOracle = $callbackOracleStatus
    PersistenceAttestationOracle = $persistenceAttestationOracleStatus
    CampaignControllerOracle = $campaignControllerOracleStatus
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    PdbSha256 = (Get-FileHash -LiteralPath $pdbPath -Algorithm SHA256).Hash
}

$json = $result | ConvertTo-Json -Depth 4
if ($OutputPath) {
    $directory = Split-Path -Parent $OutputPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
}

$json
