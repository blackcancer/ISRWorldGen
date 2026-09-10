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
$mapSnapshotPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\PersistedMapFootprintSnapshot.cs'
$markerReaderPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\ProbeMarkerEnvelopeReader.cs'
$initializationGatePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\InitializationFailClosedGate.cs'
$delayedShutdownGatePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\DelayedShutdownGate.cs'
$reopenTransactionPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\PersistedReopenPublicationTransaction.cs'
$fixtureCoordinatePolicyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CFixtureCoordinatePolicy.cs'
$lifecycleShutdownBarrierPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleShutdownBarrier.cs'
$lifecycleRegistrationLedgerPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleRegistrationLedger.cs'
$levelFinalizeGatePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLevelFinalizeGate.cs'
$projectPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'

foreach ($path in @($sourcePath, $callbackGatePath, $mapSnapshotPath, $markerReaderPath, $initializationGatePath, $delayedShutdownGatePath, $reopenTransactionPath, $fixtureCoordinatePolicyPath, $lifecycleShutdownBarrierPath, $lifecycleRegistrationLedgerPath, $levelFinalizeGatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "L00-C source is missing: $path"
    }
}

$source = Get-Content -LiteralPath $sourcePath -Raw
$allProbeSource = @(
    $source
    Get-Content -LiteralPath $callbackGatePath -Raw
    Get-Content -LiteralPath $mapSnapshotPath -Raw
    Get-Content -LiteralPath $markerReaderPath -Raw
    Get-Content -LiteralPath $initializationGatePath -Raw
    Get-Content -LiteralPath $delayedShutdownGatePath -Raw
    Get-Content -LiteralPath $reopenTransactionPath -Raw
    Get-Content -LiteralPath $fixtureCoordinatePolicyPath -Raw
    Get-Content -LiteralPath $lifecycleShutdownBarrierPath -Raw
    Get-Content -LiteralPath $lifecycleRegistrationLedgerPath -Raw
    Get-Content -LiteralPath $levelFinalizeGatePath -Raw
) -join "`n"
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
    'PersistedMapFootprintSnapshot',
    'ProbeMarkerEnvelopeReader',
    'ValidateForCopy',
    'InitializationFailClosedGate',
    'CloseProbeStateAfterInitializationFailure',
    'L00C_INITIALIZATION_FAILED',
    'L00C_INITIALIZATION_SHUTDOWN',
    'DelayedShutdownGate',
    'AutoShutdownDelayMilliseconds',
    'L00C_DELAYED_SHUTDOWN_ARMED',
    'L00C_DELAYED_SHUTDOWN_FIRED',
    'ValidateActiveDelayMilliseconds',
    'RequestActiveShutdown',
    'PersistedReopenPublicationTransaction',
    'ValidateAndCopy',
    'L00C_MAP_SNAPSHOT_COMMITTED',
    'L00C_PERSISTED_MAP_SNAPSHOT_LOADED',
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

& dotnet build $projectPath -c $Configuration --nologo "-p:VintageStoryPath=$GamePath"
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
$mapSnapshotType = 'ISRWorldGen.WorldgenProbe.PersistedMapFootprintSnapshot'
$markerReaderType = 'ISRWorldGen.WorldgenProbe.ProbeMarkerEnvelopeReader'
$initializationGateType = 'ISRWorldGen.WorldgenProbe.InitializationFailClosedGate'
$delayedShutdownGateType = 'ISRWorldGen.WorldgenProbe.DelayedShutdownGate'
$reopenTransactionType = 'ISRWorldGen.WorldgenProbe.PersistedReopenPublicationTransaction'
$fixtureCoordinatePolicyType = 'ISRWorldGen.WorldgenProbe.L00CFixtureCoordinatePolicy'
$lifecycleMarkerType = 'ISRWorldGen.WorldgenProbe.L00CLifecycleMarker'
$lifecycleShutdownBarrierType = 'ISRWorldGen.WorldgenProbe.L00CLifecycleShutdownBarrier'
$lifecycleRegistrationLedgerType = 'ISRWorldGen.WorldgenProbe.L00CLifecycleRegistrationLedger'
$levelFinalizeGateType = 'ISRWorldGen.WorldgenProbe.L00CLevelFinalizeGate'
$probePresent = @($typeNames | Where-Object { $_ -eq $probeType }).Count -eq 1
$markerGatePresent = @($typeNames | Where-Object { $_ -eq $markerGateType }).Count -eq 1
$callbackGatePresent = @($typeNames | Where-Object { $_ -eq $callbackGateType }).Count -eq 1
$mapSnapshotPresent = @($typeNames | Where-Object { $_ -eq $mapSnapshotType }).Count -eq 1
$markerReaderPresent = @($typeNames | Where-Object { $_ -eq $markerReaderType }).Count -eq 1
$initializationGatePresent = @($typeNames | Where-Object { $_ -eq $initializationGateType }).Count -eq 1
$delayedShutdownGatePresent = @($typeNames | Where-Object { $_ -eq $delayedShutdownGateType }).Count -eq 1
$reopenTransactionPresent = @($typeNames | Where-Object { $_ -eq $reopenTransactionType }).Count -eq 1
$fixtureCoordinatePolicyPresent = @($typeNames | Where-Object { $_ -eq $fixtureCoordinatePolicyType }).Count -eq 1
$lifecycleMarkerPresent = @($typeNames | Where-Object { $_ -eq $lifecycleMarkerType }).Count -eq 1
$lifecycleShutdownBarrierPresent = @($typeNames | Where-Object { $_ -eq $lifecycleShutdownBarrierType }).Count -eq 1
$lifecycleRegistrationLedgerPresent = @($typeNames | Where-Object { $_ -eq $lifecycleRegistrationLedgerType }).Count -eq 1
$levelFinalizeGatePresent = @($typeNames | Where-Object { $_ -eq $levelFinalizeGateType }).Count -eq 1
if ($Configuration -eq 'Debug' -and (-not $probePresent -or -not $markerGatePresent -or -not $callbackGatePresent -or -not $mapSnapshotPresent -or -not $markerReaderPresent -or -not $initializationGatePresent -or -not $delayedShutdownGatePresent -or -not $reopenTransactionPresent -or -not $fixtureCoordinatePolicyPresent -or -not $lifecycleMarkerPresent -or -not $lifecycleShutdownBarrierPresent -or -not $lifecycleRegistrationLedgerPresent -or -not $levelFinalizeGatePresent)) {
    throw 'Debug assembly does not contain every L00-C probe, marker, and transient callback type.'
}
if ($Configuration -eq 'Release' -and ($probePresent -or $markerGatePresent -or $callbackGatePresent -or $mapSnapshotPresent -or $markerReaderPresent -or $initializationGatePresent -or $delayedShutdownGatePresent -or $reopenTransactionPresent -or $fixtureCoordinatePolicyPresent -or $lifecycleMarkerPresent -or $lifecycleShutdownBarrierPresent -or $lifecycleRegistrationLedgerPresent -or $levelFinalizeGatePresent)) {
    throw 'Release assembly must not contain any L00-C probe, marker, or transient callback type.'
}

$markerOracleStatus = 'NOT_APPLICABLE'
$mapSnapshotOracleStatus = 'NOT_APPLICABLE'
$callbackOracleStatus = 'NOT_APPLICABLE'
$persistenceAttestationOracleStatus = 'NOT_APPLICABLE'
$campaignControllerOracleStatus = 'NOT_APPLICABLE'
$markerEnvelopeOracleStatus = 'NOT_APPLICABLE'
$initializationFailClosedOracleStatus = 'NOT_APPLICABLE'
$initializationRefusalEvidenceOracleStatus = 'NOT_APPLICABLE'
$delayedShutdownOracleStatus = 'NOT_APPLICABLE'
$activeShutdownEvidenceOracleStatus = 'NOT_APPLICABLE'
$reopenTransactionOracleStatus = 'NOT_APPLICABLE'
$campaignStorageOracleStatus = 'NOT_APPLICABLE'
$f5TransactionOracleStatus = 'NOT_APPLICABLE'
$visualStudioConsumptionOracleStatus = 'NOT_APPLICABLE'
$fixtureCoordinatePolicyOracleStatus = 'NOT_APPLICABLE'
$lifecycleSpatialIsolationOracleStatus = 'NOT_APPLICABLE'
$lifecycleShutdownOrderingOracleStatus = 'NOT_APPLICABLE'
$lifecycleRegistrationReleaseOracleStatus = 'NOT_APPLICABLE'
$t00LifecycleValidatorOracleStatus = 'NOT_APPLICABLE'
if ($Configuration -eq 'Debug') {
    $lifecycleShutdownOrderingOraclePath = Join-Path $PSScriptRoot 'Test-L00CLifecycleShutdownOrdering.ps1'
    $lifecycleShutdownOrderingOracle = (& $lifecycleShutdownOrderingOraclePath -RepositoryRoot $RepositoryRoot | Out-String | ConvertFrom-Json)
    if ($lifecycleShutdownOrderingOracle.Status -ne 'PASS' -or $lifecycleShutdownOrderingOracle.Iterations -ne 5 -or
        $lifecycleShutdownOrderingOracle.FinalizedSessions -ne 15 -or $lifecycleShutdownOrderingOracle.DedicatedSaves -ne 10 -or
        [string]::IsNullOrWhiteSpace([string]$lifecycleShutdownOrderingOracle.Refusals)) {
        throw 'The lifecycle shutdown ordering oracle did not pass.'
    }
    $lifecycleShutdownOrderingOracleStatus = $lifecycleShutdownOrderingOracle.Status
    $lifecycleRegistrationReleaseOraclePath = Join-Path $PSScriptRoot 'Test-L00CLifecycleRegistrationRelease.ps1'
    $lifecycleRegistrationReleaseOracle = (& $lifecycleRegistrationReleaseOraclePath -RepositoryRoot $RepositoryRoot | Out-String | ConvertFrom-Json)
    if ($lifecycleRegistrationReleaseOracle.Status -ne 'PASS' -or $lifecycleRegistrationReleaseOracle.Registrations -ne 3) {
        throw 'The lifecycle registration/release oracle did not pass.'
    }
    $lifecycleRegistrationReleaseOracleStatus = $lifecycleRegistrationReleaseOracle.Status
    $t00LifecycleValidatorOraclePath = Join-Path $PSScriptRoot 'Test-L00CT00LifecycleValidator.ps1'
    $t00LifecycleValidatorOracle = (& $t00LifecycleValidatorOraclePath | Out-String | ConvertFrom-Json)
    if ($t00LifecycleValidatorOracle.Status -ne 'PASS' -or [string]::IsNullOrWhiteSpace([string]$t00LifecycleValidatorOracle.Negative)) {
        throw 'The standalone T00-06 lifecycle validator oracle did not pass.'
    }
    $t00LifecycleValidatorOracleStatus = $t00LifecycleValidatorOracle.Status
    $lifecycleSpatialIsolationOraclePath = Join-Path $PSScriptRoot 'Test-L00CLifecycleSpatialIsolation.ps1'
    $lifecycleSpatialIsolationOracle = (& $lifecycleSpatialIsolationOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($lifecycleSpatialIsolationOracle.Status -ne 'PASS' -or $lifecycleSpatialIsolationOracle.LifecycleSpatialCalls -ne 0 -or $lifecycleSpatialIsolationOracle.LifecycleTeleportCalls -ne 0 -or $lifecycleSpatialIsolationOracle.LifecycleProfileSpatialDefault) {
        throw 'The lifecycle/spatial separation oracle did not pass.'
    }
    $lifecycleSpatialIsolationOracleStatus = $lifecycleSpatialIsolationOracle.Status
    $fixtureCoordinatePolicyOraclePath = Join-Path $PSScriptRoot 'Test-L00CFixtureCoordinatePolicy.ps1'
    $fixtureCoordinatePolicyOracle = (& $fixtureCoordinatePolicyOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($fixtureCoordinatePolicyOracle.Status -ne 'PASS' -or -not $fixtureCoordinatePolicyOracle.ProfileConfigObserved -or $fixtureCoordinatePolicyOracle.InvalidBoundaryCases -lt 5) {
        throw 'The L00-C fixture coordinate policy oracle did not pass.'
    }
    $fixtureCoordinatePolicyOracleStatus = $fixtureCoordinatePolicyOracle.Status
    $f5TransactionOraclePath = Join-Path $PSScriptRoot 'Test-L00CF5AuthenticatedProfile.ps1'
    $f5TransactionOracle = (& $f5TransactionOraclePath | Out-String | ConvertFrom-Json)
    if ($f5TransactionOracle.Status -ne 'PASS' -or $f5TransactionOracle.Cases -lt 47) {
        throw 'The authenticated F5 transaction v2 oracle did not pass.'
    }
    $f5TransactionOracleStatus = $f5TransactionOracle.Status
    $visualStudioConsumptionOraclePath = Join-Path $PSScriptRoot 'Test-L00CVisualStudioProfileConsumption.ps1'
    $visualStudioConsumptionOracle = (& $visualStudioConsumptionOraclePath | Out-String | ConvertFrom-Json)
    if ($visualStudioConsumptionOracle.Status -ne 'PASS' -or $visualStudioConsumptionOracle.Cases -lt 16) {
        throw 'The bounded Visual Studio profile-consumption oracle did not pass.'
    }
    $visualStudioConsumptionOracleStatus = $visualStudioConsumptionOracle.Status
    $campaignStorageOraclePath = Join-Path $PSScriptRoot 'Test-L00CCampaignStorage.ps1'
    $campaignStorageOracle = (& $campaignStorageOraclePath | Out-String | ConvertFrom-Json)
    if ($campaignStorageOracle.Status -ne 'PASS' -or
        [string]::IsNullOrWhiteSpace([string]$campaignStorageOracle.LegacyRecovery)) {
        throw 'The current and legacy campaign-storage recovery oracle did not pass.'
    }
    $campaignStorageOracleStatus = $campaignStorageOracle.Status
    $markerOraclePath = Join-Path $PSScriptRoot 'Test-L00CMarkerPublication.ps1'
    $markerOracle = (& $markerOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($markerOracle.Status -ne 'PASS') {
        throw 'The production marker publication oracle did not pass against the freshly built Debug assembly.'
    }
    $markerOracleStatus = $markerOracle.Status
    $mapSnapshotOraclePath = Join-Path $PSScriptRoot 'Test-L00CPersistedMapSnapshot.ps1'
    $mapSnapshotOracle = (& $mapSnapshotOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($mapSnapshotOracle.Status -ne 'PASS' -or $mapSnapshotOracle.ExactMapCopies -ne 9) {
        throw 'The production persisted-map snapshot oracle did not pass against the freshly built Debug assembly.'
    }
    $mapSnapshotOracleStatus = $mapSnapshotOracle.Status
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
    if ($campaignControllerOracle.Status -ne 'PASS' -or
        -not $campaignControllerOracle.WalBackedOpen1SourceExercised -or
        -not $campaignControllerOracle.WalPayloadReadWithoutSource -or
        -not $campaignControllerOracle.MissingWalPayloadRejected) {
        throw 'The four-phase campaign controller oracle did not pass.'
    }
    $campaignControllerOracleStatus = $campaignControllerOracle.Status
    $markerEnvelopeOraclePath = Join-Path $PSScriptRoot 'Test-L00CMarkerEnvelopeSafety.ps1'
    $markerEnvelopeOracle = (& $markerEnvelopeOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($markerEnvelopeOracle.Status -ne 'PASS') {
        throw 'The bounded production marker-envelope oracle did not pass.'
    }
    $markerEnvelopeOracleStatus = $markerEnvelopeOracle.Status
    $initializationFailClosedOraclePath = Join-Path $PSScriptRoot 'Test-L00CInitializationFailClosed.ps1'
    $initializationFailClosedOracle = (& $initializationFailClosedOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($initializationFailClosedOracle.Status -ne 'PASS') {
        throw 'The production initialization fail-closed oracle did not pass.'
    }
    $initializationFailClosedOracleStatus = $initializationFailClosedOracle.Status
    $initializationRefusalEvidenceOraclePath = Join-Path $PSScriptRoot 'Test-L00CInitializationRefusalEvidence.ps1'
    $initializationRefusalEvidenceOracle = (& $initializationRefusalEvidenceOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($initializationRefusalEvidenceOracle.Status -ne 'PASS' -or
        -not $initializationRefusalEvidenceOracle.ImmediateShutdownWithoutWorldSave -or
        -not $initializationRefusalEvidenceOracle.ZeroGeographyRequired -or
        -not $initializationRefusalEvidenceOracle.ZeroEnvelopeRequired -or
        -not $initializationRefusalEvidenceOracle.WalBackedSourceRequired -or
        -not $initializationRefusalEvidenceOracle.AutonomousSnapshotRequired) {
        throw 'The role-specific initialization-refusal evidence oracle did not pass.'
    }
    $initializationRefusalEvidenceOracleStatus = $initializationRefusalEvidenceOracle.Status
    $delayedShutdownOraclePath = Join-Path $PSScriptRoot 'Test-L00CDelayedShutdownGate.ps1'
    $delayedShutdownOracle = (& $delayedShutdownOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($delayedShutdownOracle.Status -ne 'PASS') {
        throw 'The production delayed-shutdown oracle did not pass.'
    }
    $delayedShutdownOracleStatus = $delayedShutdownOracle.Status
    $activeShutdownEvidenceOraclePath = Join-Path $PSScriptRoot 'Test-L00CActiveShutdownEvidence.ps1'
    $activeShutdownEvidenceOracle = (& $activeShutdownEvidenceOraclePath | Out-String | ConvertFrom-Json)
    if ($activeShutdownEvidenceOracle.Status -ne 'PASS' -or
        -not $activeShutdownEvidenceOracle.SuspendTimeoutRejected -or
        -not $activeShutdownEvidenceOracle.MissingWorldSaveRejected -or
        -not $activeShutdownEvidenceOracle.MissingPersistedPrecheckRejected -or
        -not $activeShutdownEvidenceOracle.MarkerSaveNotRequired -or
        -not $activeShutdownEvidenceOracle.MarkerSaveCannotSubstituteForDatabase -or
        -not $activeShutdownEvidenceOracle.ReorderedTerminalEventsRejected -or
        -not $activeShutdownEvidenceOracle.ShutdownLifecycleOrderRequired -or
        -not $activeShutdownEvidenceOracle.DisposeErrorRejected -or
        -not $activeShutdownEvidenceOracle.PersistedActivationOrderRequired) {
        throw 'The active delayed-shutdown evidence oracle did not pass.'
    }
    $activeShutdownEvidenceOracleStatus = $activeShutdownEvidenceOracle.Status
    $reopenTransactionOraclePath = Join-Path $PSScriptRoot 'Test-L00CPersistedReopenTransaction.ps1'
    $reopenTransactionOracle = (& $reopenTransactionOraclePath -RepositoryRoot $RepositoryRoot -GamePath $GamePath | Out-String | ConvertFrom-Json)
    if ($reopenTransactionOracle.Status -ne 'PASS' -or -not $reopenTransactionOracle.CommitIsFinalCriticalOperation) {
        throw 'The production persisted-reopen transaction oracle did not pass.'
    }
    $reopenTransactionOracleStatus = $reopenTransactionOracle.Status
}

$result = [ordered]@{
    TestId = 'L00-C-STATIC'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    Configuration = $Configuration
    ProbeTypePresent = $probePresent
    MarkerGateTypePresent = $markerGatePresent
    TransientCallbackGateTypePresent = $callbackGatePresent
    PersistedMapSnapshotTypePresent = $mapSnapshotPresent
    MarkerEnvelopeReaderTypePresent = $markerReaderPresent
    InitializationFailClosedGateTypePresent = $initializationGatePresent
    DelayedShutdownGateTypePresent = $delayedShutdownGatePresent
    PersistedReopenTransactionTypePresent = $reopenTransactionPresent
    FixtureCoordinatePolicyTypePresent = $fixtureCoordinatePolicyPresent
    LifecycleMarkerTypePresent = $lifecycleMarkerPresent
    LifecycleShutdownBarrierTypePresent = $lifecycleShutdownBarrierPresent
    LifecycleRegistrationLedgerTypePresent = $lifecycleRegistrationLedgerPresent
    LevelFinalizeGateTypePresent = $levelFinalizeGatePresent
    MarkerPublicationOracle = $markerOracleStatus
    PersistedMapSnapshotOracle = $mapSnapshotOracleStatus
    TransientCallbackOracle = $callbackOracleStatus
    PersistenceAttestationOracle = $persistenceAttestationOracleStatus
    CampaignControllerOracle = $campaignControllerOracleStatus
    MarkerEnvelopeOracle = $markerEnvelopeOracleStatus
    InitializationFailClosedOracle = $initializationFailClosedOracleStatus
    InitializationRefusalEvidenceOracle = $initializationRefusalEvidenceOracleStatus
    DelayedShutdownOracle = $delayedShutdownOracleStatus
    ActiveShutdownEvidenceOracle = $activeShutdownEvidenceOracleStatus
    PersistedReopenTransactionOracle = $reopenTransactionOracleStatus
    CampaignStorageRecoveryOracle = $campaignStorageOracleStatus
    F5TransactionOracle = $f5TransactionOracleStatus
    VisualStudioConsumptionOracle = $visualStudioConsumptionOracleStatus
    FixtureCoordinatePolicyOracle = $fixtureCoordinatePolicyOracleStatus
    LifecycleSpatialIsolationOracle = $lifecycleSpatialIsolationOracleStatus
    LifecycleShutdownOrderingOracle = $lifecycleShutdownOrderingOracleStatus
    LifecycleRegistrationReleaseOracle = $lifecycleRegistrationReleaseOracleStatus
    T00LifecycleValidatorOracle = $t00LifecycleValidatorOracleStatus
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
