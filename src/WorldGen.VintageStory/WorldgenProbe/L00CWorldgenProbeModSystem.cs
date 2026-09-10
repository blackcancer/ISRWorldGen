#if DEBUG
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ISRWorldGen.WorldgenProbe;

/// <summary>
/// Debug-only, explicitly activated world-generation probe for the bounded L00-C laboratory fixture.
/// </summary>
public sealed class L00CWorldgenProbeModSystem : ModSystem
{
    private const string WorldType = "standard";
    private const string ConfigFileName = "isrworldgen-l00c.json";
    // The F5 transaction owns these values in the evaluated launch profile.
    // Do not fall back to a persisted mod-config value here: an old lab config
    // may have been written before active shutdown acquired its 10 s minimum.
    private const string LaboratorySwitchEnvironmentVariable = "ISR_L00C_LAB";
    private const string LaboratoryAutoShutdownEnvironmentVariable = "ISR_L00C_AUTOSHUTDOWN";
    private const string LaboratoryAutoShutdownDelayEnvironmentVariable = "ISR_L00C_AUTOSHUTDOWN_DELAY_MS";
    private const string MarkerKey = "isrworldgen:l00c:marker:v1";
    private const string LifecycleMarkerKey = "isrworldgen:l00c:lifecycle-marker:v1";
    private const string MarkerVersion = ProbeMarkerEnvelopeReader.CurrentMarkerVersion;
    private const int StableTickTarget = 40;
    private const int FixtureProtectionRadius = 1;
    private const string LightingAnchorType = "Vintagestory.ServerMods.GenLightSurvival";
    private const string LightingAnchorMethod = "OnChunkColumnGeneration";

    private static readonly ReplacementSpec[] ReplacementSpecs =
    [
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenTerra", WritesFixture: true),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenRockStrataNew"),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenCaves"),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenDevastationLayer"),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenBlockLayers"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenTerraPostProcess"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenHotSprings", SuppressInHalo: true),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenDungeons", SuppressInHalo: true),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenDeposits", SuppressInHalo: true),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenStructures", "OnChunkColumnGen", SuppressInHalo: true),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenPonds", SuppressInHalo: true),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenStructures", "OnChunkColumnGenPostPass", SuppressInHalo: true),
        new(EnumWorldGenPass.Vegetation, "Vintagestory.GameContent.GenStoryStructures", SuppressInHalo: true),
        new(EnumWorldGenPass.Vegetation, "Vintagestory.ServerMods.GenVegetationAndPatches", SuppressInHalo: true),
        new(EnumWorldGenPass.Vegetation, "Vintagestory.ServerMods.GenRivulets", SuppressInHalo: true),
        new(EnumWorldGenPass.NeighbourSunLightFlood, "Vintagestory.ServerMods.GenSnowLayer")
    ];

    private readonly string instanceId = Guid.NewGuid().ToString("N");
    private readonly object runGate = new();
    private readonly TransientLoadCallbackGate transientLoadCallbacks = new();
    private readonly MarkerPublicationGate markerPublication = new();
    private readonly InitializationFailClosedGate initializationFailClosed = new();
    private readonly DelayedShutdownGate delayedShutdown = new();

    private ICoreServerAPI? api;
    private HandlerOwnershipState? ownershipState;
    private L00CProbeConfig config = new();
    private Exception? laboratoryConfigurationFailure;
    private ProbeMarker? marker;
    private L00CLifecycleMarker? lifecycleMarker;
    private L00CLifecycleShutdownIdentity? lifecycleShutdownIdentity;
    private L00CLifecycleShutdownLease? lifecycleShutdownLease;
    private L00CProbeCallbackOwnerLease? callbackOwnerLease;
    private FixtureSnapshot? preLightingSnapshot;
    private FixtureSnapshot? initialSnapshot;
    private HaloSnapshot? initialHaloSnapshot;
    private long tickListenerId;
    private int stableTickCount;
    private int haloPreparedCount;
    private int fixtureCallbackCount;
    private int fixtureWriteCount;
    private int priorityLoadInvocationCount;
    private int transientColumnRequestCount;
    private int footprintRefreshInvocationCount;
    private int refreshedMapChunkCount;
    private int mapSnapshotWriteCount;
    private int requestIssued;
    private int shutdownIssued;
    private int disposalStarted;
    private bool active;
    private long worldRunId;

    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    /// <inheritdoc />
    public override double ExecuteOrder() => 1.0;

    /// <inheritdoc />
    public override void StartServerSide(ICoreServerAPI serverApi)
    {
        api = serverApi;
        try
        {
            config = L00CProbeConfig.BindLaboratoryShutdownConfiguration(
                serverApi.LoadModConfig<L00CProbeConfig>(ConfigFileName) ?? new L00CProbeConfig(),
                Environment.GetEnvironmentVariable(LaboratorySwitchEnvironmentVariable),
                Environment.GetEnvironmentVariable(LaboratoryAutoShutdownEnvironmentVariable),
                Environment.GetEnvironmentVariable(LaboratoryAutoShutdownDelayEnvironmentVariable));
        }
        catch (Exception exception)
        {
            // StartServerSide has no guarded handler ownership yet. Preserve the
            // failure and take the normal initialization fail-closed path instead.
            laboratoryConfigurationFailure = exception;
        }
        var callbacks = new L00CProbeCallbackOwnerLease(this);
        callbackOwnerLease = callbacks;
        serverApi.Event.InitWorldGenerator(callbacks.InitializeWorld, WorldType);
        callbacks.Ledger.RecordRegistered(L00CLifecycleRegistrationKind.InitWorldGenerator);
        serverApi.Event.GameWorldSave += callbacks.GameWorldSave;
        callbacks.Ledger.RecordRegistered(L00CLifecycleRegistrationKind.GameWorldSave);
        tickListenerId = serverApi.Event.RegisterGameTickListener(callbacks.ServerTick, 50);
        callbacks.Ledger.RecordRegistered(L00CLifecycleRegistrationKind.Tick);

        Log($"L00C_PROBE_READY instance={instanceId} pid={Environment.ProcessId} enabled={config.Enabled} autorun={config.AutoRun} autoshutdown={config.AutoShutdown} autoshutdowndelayms={config.AutoShutdownDelayMilliseconds}");
    }

    private void InitializeWorld()
    {
        lock (runGate)
        {
            int attempt = initializationFailClosed.BeginAttempt();
            try
            {
                InitializeWorldCore();
            }
            catch (Exception exception)
            {
                HandleInitializationFailure(attempt, exception);
            }
        }
    }

    private void HandleInitializationFailure(int attempt, Exception exception)
    {
        ICoreServerAPI? serverApi = api;
        try
        {
            serverApi?.Logger.Error($"L00C_INITIALIZATION_FAILED instance={instanceId} attempt={attempt} type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
        }
        catch
        {
            // Logging must never prevent the fail-closed shutdown request.
        }

        bool accepted = initializationFailClosed.Fail(
            attempt,
            () =>
            {
                CloseProbeStateAfterInitializationFailure();
            },
            () =>
            {
                Interlocked.Exchange(ref shutdownIssued, 1);
                (serverApi ?? throw new InvalidOperationException("L00-C cannot request fail-closed shutdown without the server API."))
                    .Server.ShutDown();
            });

        try
        {
            serverApi?.Logger.Error($"L00C_INITIALIZATION_SHUTDOWN instance={instanceId} attempt={attempt} accepted={accepted} cleanupError={initializationFailClosed.CleanupException?.GetType().FullName ?? "none"} shutdownError={initializationFailClosed.ShutdownException?.GetType().FullName ?? "none"}");
        }
        catch
        {
            // The shutdown attempt and closed state are the durable outcome.
        }
    }

    private void CloseProbeStateAfterInitializationFailure()
    {
        Interlocked.Increment(ref worldRunId);
        CloseLifecycleShutdownBarrier();
        active = false;
        marker = null;
        lifecycleMarker = null;
        preLightingSnapshot = null;
        initialSnapshot = null;
        initialHaloSnapshot = null;
        markerPublication.BeginWorldTransition();

        // Close both asynchronous gates before any external API or handler
        // restoration can throw. Late callbacks are harmless from here on.
        Exception? cleanupFailure = null;
        try
        {
            delayedShutdown.CancelAndUnregister();
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }
        transientLoadCallbacks.Reset();

        try
        {
            RestoreOwnedHandlerSet("initialization-failure");
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null ? exception : new AggregateException(cleanupFailure, exception);
        }

        if (cleanupFailure is not null)
        {
            throw new InvalidOperationException("L00-C initialization cleanup completed with external restoration errors.", cleanupFailure);
        }
    }

    private void InitializeWorldCore()
    {
        ICoreServerAPI serverApi = RequireApi();
        if (laboratoryConfigurationFailure is not null)
        {
            throw new InvalidOperationException("L00-C laboratory shutdown configuration was refused before world initialization.", laboratoryConfigurationFailure);
        }
        long runId = Interlocked.Increment(ref worldRunId);
        BeginWorldTransition();
        DelayedShutdownGate.ValidateDelayMilliseconds(config.AutoShutdownDelayMilliseconds);
        delayedShutdown.Open();
        transientLoadCallbacks.Open();
        IWorldGenHandler handlers = serverApi.Event.GetRegisteredWorldGenHandlers(WorldType)
            ?? throw new InvalidOperationException("L00-C could not obtain the standard worldgen handler set.");

        HandlerOwnershipState? previousOwnership = ownershipState;
        bool sameHandlerSet = previousOwnership is not null && ReferenceEquals(previousOwnership.HandlerSet, handlers);
        RestoreResult priorRestore = RestoreOwnedHandlerSet("world-initialize");
        int staleProbeHandlersAfterReset = previousOwnership is null ? 0 : CountOwnedHandlers(handlers, previousOwnership);
        if (staleProbeHandlersAfterReset != 0)
        {
            throw new InvalidOperationException($"L00-C failed to remove {staleProbeHandlersAfterReset} stale fixture handler(s) before world initialization.");
        }

        fixtureCallbackCount = 0;
        fixtureWriteCount = 0;
        priorityLoadInvocationCount = 0;
        transientColumnRequestCount = 0;
        footprintRefreshInvocationCount = 0;
        refreshedMapChunkCount = 0;
        mapSnapshotWriteCount = 0;
        requestIssued = 0;
        shutdownIssued = 0;
        stableTickCount = 0;
        haloPreparedCount = 0;

        ISaveGame saveGame = serverApi.WorldManager.SaveGame;
        if (!config.SpatialFixtureEnabled)
        {
            InitializeLifecycleOnly(runId, handlers, saveGame, priorRestore, sameHandlerSet);
            return;
        }
        ProbeMarker? persistedMarker = ReadMarker(saveGame, serverApi.WorldManager);
        bool activationRequested = config.Enabled;

        LogInventory("before", handlers, saveGame, priorRestore.RemovedOwned, sameHandlerSet);

        if (saveGame.IsNew)
        {
            if (!activationRequested)
            {
                active = false;
                marker = null;
                Log($"L00C_INACTIVE instance={instanceId} reason=new-world-not-enabled save={saveGame.SavegameIdentifier}");
                LogInventory("inactive", handlers, saveGame, 0, sameHandlerSet);
                ScheduleInactiveWitness(runId);
                return;
            }

            marker = markerPublication.Begin(new ProbeMarker
            {
                MarkerId = Guid.NewGuid().ToString("N"),
                SavegameIdentifier = saveGame.SavegameIdentifier,
                Version = MarkerVersion,
                OpenCount = 1
            });
        }
        else if (persistedMarker is null)
        {
            active = false;
            marker = null;
            if (activationRequested)
            {
                Fail("activation-rejected-existing-world", "L00-C activation is restricted to a new world or a world already carrying its persistent marker.");
            }

            Log($"L00C_INACTIVE instance={instanceId} reason=existing-world-without-marker save={saveGame.SavegameIdentifier}");
            LogInventory("inactive", handlers, saveGame, 0, sameHandlerSet);
            ScheduleInactiveWitness(runId);
            return;
        }
        else
        {
            ValidateMarker(persistedMarker, saveGame);
            marker = markerPublication.Begin(persistedMarker);
        }

        // Active lab runs must let the server's chunk DB, compression, and
        // relight workers settle after RunGame before requesting suspension.
        // Invalid active timing fails closed here, while still Initializing.
        DelayedShutdownGate.ValidateActiveDelayMilliseconds(config.AutoShutdownDelayMilliseconds);

        if (!saveGame.IsNew)
        {
            ResolveMaterials();
            PersistedReopenPublicationTransaction.Execute(
                InspectPersistedFootprintBlocking,
                () =>
                {
                    marker!.OpenCount++;
                    active = true;
                },
                () => LogInventory("after", handlers, saveGame, priorRestore.RemovedOwned, sameHandlerSet),
                () => Log($"L00C_ACTIVATED instance={instanceId} marker={marker!.MarkerId} run={runId} open={marker.OpenCount} isnew=False save={saveGame.SavegameIdentifier} chunk=({config.FixtureChunkX},{config.FixtureChunkZ})"),
                persistedSnapshot => SchedulePersistedReopen(runId, persistedSnapshot),
                () => markerPublication.Commit(payload => saveGame.StoreData(MarkerKey, payload)));
            return;
        }

        ValidateReplacementPreconditions(handlers);
        ResolveMaterials();
        ApplyTargetedReplacement(handlers);
        active = true;

        LogInventory("after", handlers, saveGame, priorRestore.RemovedOwned, sameHandlerSet);
        Log($"L00C_ACTIVATED instance={instanceId} marker={marker!.MarkerId} run={runId} open={marker.OpenCount} isnew={saveGame.IsNew} save={saveGame.SavegameIdentifier} chunk=({config.FixtureChunkX},{config.FixtureChunkZ})");
        ScheduleProbeColumn(runId);
    }

    // T00-06 is a lifecycle contract, not a spatial probe.  It deliberately
    // creates and persists an independent marker without requesting a column,
    // installing handlers, reading map dimensions, or assuming a player/TP
    // position.  The T00-04/T00-05 fixture remains opt-in above.
    private void InitializeLifecycleOnly(
        long runId,
        IWorldGenHandler handlers,
        ISaveGame saveGame,
        RestoreResult priorRestore,
        bool sameHandlerSet)
    {
        lifecycleShutdownIdentity = L00CLifecycleShutdownIdentity.Create(runId, instanceId, saveGame.SavegameIdentifier);
        lifecycleShutdownLease = L00CLifecycleShutdownBarrier.Open(lifecycleShutdownIdentity);
        L00CLifecycleMarker? persisted = ReadLifecycleMarker(saveGame);
        if (saveGame.IsNew)
        {
            if (!config.Enabled)
            {
                active = false;
                lifecycleMarker = null;
                Log($"L00C_INACTIVE instance={instanceId} reason=lifecycle-new-world-not-enabled save={saveGame.SavegameIdentifier}");
                LogInventory("inactive", handlers, saveGame, priorRestore.RemovedOwned, sameHandlerSet);
                ScheduleInactiveWitness(runId);
                return;
            }
            lifecycleMarker = L00CLifecycleMarker.Create(saveGame.SavegameIdentifier, 1);
            LogLifecycleEvent("MarkerCreated", "new save receives counter 1 and a fresh internal marker", runId,
                $"marker={lifecycleMarker.MarkerId} guid={lifecycleMarker.SavegameIdentifier} open={lifecycleMarker.OpenCount} isnew=True");
        }
        else
        {
            if (persisted is null)
            {
                active = false;
                lifecycleMarker = null;
                if (config.Enabled)
                {
                    Fail("lifecycle-activation-rejected-existing-world", "L00-C lifecycle activation is restricted to a new world or a world carrying its persistent lifecycle marker.");
                }
                Log($"L00C_INACTIVE instance={instanceId} reason=lifecycle-existing-world-without-marker save={saveGame.SavegameIdentifier}");
                LogInventory("inactive", handlers, saveGame, priorRestore.RemovedOwned, sameHandlerSet);
                ScheduleInactiveWitness(runId);
                return;
            }
            lifecycleMarker = persisted.IncrementFor(saveGame.SavegameIdentifier);
            LogLifecycleEvent("MarkerReRead", "reopen re-read the same internal marker and increments exactly once", runId,
                $"marker={lifecycleMarker.MarkerId} guid={lifecycleMarker.SavegameIdentifier} open={lifecycleMarker.OpenCount} isnew=False");
        }

        active = true;
        saveGame.StoreData(LifecycleMarkerKey, lifecycleMarker.Serialize());
        LogLifecycleEvent("MarkerStaged", "StoreData is staged internal marker data and is not SaveCommitted proof", runId,
            $"marker={lifecycleMarker.MarkerId} guid={lifecycleMarker.SavegameIdentifier} open={lifecycleMarker.OpenCount} isnew={saveGame.IsNew}");
        LogInventory("lifecycle", handlers, saveGame, priorRestore.RemovedOwned, sameHandlerSet);
        Log($"L00C_LIFECYCLE_ACTIVATED instance={instanceId} marker={lifecycleMarker.MarkerId} run={runId} open={lifecycleMarker.OpenCount} isnew={saveGame.IsNew} save={saveGame.SavegameIdentifier} spatial=False requests=0 handlersowned=False");
        ScheduleLifecycleShutdown(runId);
    }

    private static L00CLifecycleMarker? ReadLifecycleMarker(ISaveGame saveGame)
    {
        byte[]? payload = saveGame.GetData(LifecycleMarkerKey);
        return payload is null || payload.Length == 0
            ? null
            : L00CLifecycleMarker.ReadAndValidate(payload, saveGame.SavegameIdentifier);
    }

    private void LogLifecycleEvent(string state,string invariant,long runId,string detail)
        => Log($"L00C_LIFECYCLE_EVENT run={runId} iteration=0 session=0 state={state} invariant={Sanitize(invariant)} {detail}");

    private void ScheduleLifecycleShutdown(long runId)
    {
        if (!config.AutoRun)
        {
            Log($"L00C_AUTORUN_SKIPPED instance={instanceId} active=True scenario=lifecycle");
            return;
        }
        RequireApi().Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => CompleteLifecycleOnly(runId));
    }

    private void CompleteLifecycleOnly(long runId)
    {
        lock (runGate)
        {
            try
            {
                if (!IsCurrentRun(runId))
                {
                    return;
                }
                if (!active || lifecycleMarker is null || marker is not null || ownershipState is not null || requestIssued != 0 ||
                    fixtureCallbackCount != 0 || fixtureWriteCount != 0 || transientLoadCallbacks.PendingCount != 0)
                {
                    throw new InvalidOperationException("L00-C lifecycle scenario acquired spatial state or lost its persistent marker.");
                }
                Log($"L00C_LIFECYCLE_STABLE instance={instanceId} marker={lifecycleMarker.MarkerId} run={runId} open={lifecycleMarker.OpenCount} requests=0 callbacks=0 writes=0 handlersowned=False");
                L00CLifecycleShutdownBarrier.Arm(lifecycleShutdownLease ?? throw new InvalidOperationException("L00-C lifecycle shutdown lease is absent."));
            }
            catch (Exception exception)
            {
                if (!IsCurrentRun(runId))
                {
                    return;
                }
                throw HandleAsynchronousFailure(runId, "lifecycle-stable-error", exception);
            }
        }
    }

    private void BeginWorldTransition()
    {
        CloseLifecycleShutdownBarrier();
        markerPublication.BeginWorldTransition();
        CancelDelayedShutdown("world-initialize");
        marker = null;
        lifecycleMarker = null;
        active = false;
        preLightingSnapshot = null;
        initialSnapshot = null;
        initialHaloSnapshot = null;
        ResetTransientLoadCallbacks("world-initialize");
    }

    private void CloseLifecycleShutdownBarrier()
    {
        L00CLifecycleShutdownLease? lease = lifecycleShutdownLease;
        lifecycleShutdownLease = null;
        lifecycleShutdownIdentity = null;
        if (lease is not null) _ = L00CLifecycleShutdownBarrier.Close(lease);
    }

    private RestoreResult RestoreOwnedHandlerSet(string reason)
    {
        HandlerOwnershipState? state = ownershipState;
        if (state is null)
        {
            return new RestoreResult(0, 0, true);
        }

        int removedOwned = 0;
        int restoredNative = 0;
        foreach (KeyValuePair<EnumWorldGenPass, List<ChunkColumnGenerationDelegate>> entry in state.OriginalInventories.OrderBy(item => item.Key))
        {
            List<ChunkColumnGenerationDelegate> current = GetPassHandlers(state.HandlerSet, entry.Key);
            List<ChunkColumnGenerationDelegate> original = entry.Value;
            List<ChunkColumnGenerationDelegate> installed = state.InstalledInventories[entry.Key];
            if (ReferenceSequenceEqual(current, original))
            {
                continue;
            }

            if (!ReferenceSequenceEqual(current, installed))
            {
                string message = $"L00-C cannot restore pass {entry.Key}: the installed handler sequence changed while owned.";
                Log($"L00C_RESTORE_ERROR reason={reason} instance={instanceId} pass={entry.Key} message={Sanitize(message)}");
                throw new InvalidOperationException(message);
            }

            removedOwned += current.Count(candidate => IsOwnedHandler(candidate, state));
            restoredNative += state.OwnedHandlers.Count(item => item.Pass == entry.Key && current.Any(candidate => ReferenceEquals(candidate, item.Wrapper)));
            current.Clear();
            current.AddRange(original);
            if (!ReferenceSequenceEqual(current, original))
            {
                string message = $"L00-C failed exact reference/order verification after restoring pass {entry.Key}.";
                Log($"L00C_RESTORE_ERROR reason={reason} instance={instanceId} pass={entry.Key} message={Sanitize(message)}");
                throw new InvalidOperationException(message);
            }
        }

        string inventory = FormatColumnInventory(state.HandlerSet);
        Log($"L00C_RESTORE_RESULT reason={reason} instance={instanceId} removedowned={removedOwned} restorednative={restoredNative} exact=True inventory={inventory}");
        ownershipState = null;
        return new RestoreResult(removedOwned, restoredNative, true);
    }

    private static bool ReferenceSequenceEqual(IReadOnlyList<ChunkColumnGenerationDelegate> left, IReadOnlyList<ChunkColumnGenerationDelegate> right)
    {
        return left.Count == right.Count && !left.Where((candidate, index) => !ReferenceEquals(candidate, right[index])).Any();
    }

    private void ValidateReplacementPreconditions(IWorldGenHandler handlers)
    {
        if (!string.IsNullOrWhiteSpace(config.ExpectedMissingHandlerTarget))
        {
            bool unexpectedMatch = FindHandlers(handlers, config.ExpectedMissingHandlerTarget!).Count != 0;
            if (!unexpectedMatch)
            {
                Fail("expected-handler-absent", $"Required handler target '{config.ExpectedMissingHandlerTarget}' was not registered; targeted replacement was not applied.");
            }

            Fail("missing-handler-test-invalid", $"The configured missing target '{config.ExpectedMissingHandlerTarget}' unexpectedly exists.");
        }

        foreach (ReplacementSpec spec in ReplacementSpecs)
        {
            List<HandlerLocation> matches = FindHandlers(handlers, spec.TargetType, spec.Pass, spec.MethodName);
            if (matches.Count != 1)
            {
                string method = spec.MethodName is null ? string.Empty : $"::{spec.MethodName}";
                Fail("expected-handler-cardinality", $"Expected exactly one {spec.TargetType}{method} handler in pass {spec.Pass}, found {matches.Count}; targeted replacement was not applied.");
            }
        }

        List<HandlerLocation> lightingAnchors = FindHandlers(
            handlers,
            LightingAnchorType,
            EnumWorldGenPass.Vegetation,
            LightingAnchorMethod);
        if (lightingAnchors.Count != 1)
        {
            Fail("lighting-anchor-cardinality", $"Expected exactly one {LightingAnchorType}::{LightingAnchorMethod} handler in pass Vegetation, found {lightingAnchors.Count}; canonical finalization cannot be positioned safely.");
        }
    }

    private void ApplyTargetedReplacement(IWorldGenHandler handlers)
    {
        if (ownershipState is not null)
        {
            throw new InvalidOperationException("L00-C cannot install handler ownership while another handler set is still owned.");
        }

        EnumWorldGenPass[] modifiedPasses = ReplacementSpecs
            .Select(spec => spec.Pass)
            .Append(EnumWorldGenPass.Vegetation)
            .Distinct()
            .OrderBy(pass => pass)
            .ToArray();
        var originalInventories = modifiedPasses.ToDictionary(
            pass => pass,
            pass => GetPassHandlers(handlers, pass).ToList());
        var ownedHandlers = new List<OwnedHandler>();
        long runId = Volatile.Read(ref worldRunId);
        foreach (ReplacementSpec spec in ReplacementSpecs)
        {
            HandlerLocation location = FindHandlers(handlers, spec.TargetType, spec.Pass, spec.MethodName).Single();
            ownedHandlers.Add(new OwnedHandler(this, runId, spec, location.Index, location.Handler));
        }

        var installedInventories = originalInventories.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToList());
        foreach (OwnedHandler owned in ownedHandlers)
        {
            installedInventories[owned.Pass][owned.OriginalIndex] = owned.Wrapper;
        }

        int lightingAnchorIndex = FindHandlers(
            handlers,
            LightingAnchorType,
            EnumWorldGenPass.Vegetation,
            LightingAnchorMethod).Single().Index;
        ChunkColumnGenerationDelegate lightingFinalizerHandler = request => InvokeLightingFinalizer(runId, request);
        installedInventories[EnumWorldGenPass.Vegetation].Insert(lightingAnchorIndex, lightingFinalizerHandler);

        var state = new HandlerOwnershipState(
            handlers,
            originalInventories,
            installedInventories,
            ownedHandlers,
            lightingAnchorIndex,
            lightingFinalizerHandler);
        var changedPasses = new List<EnumWorldGenPass>();

        try
        {
            foreach (EnumWorldGenPass pass in modifiedPasses)
            {
                List<ChunkColumnGenerationDelegate> actual = GetPassHandlers(handlers, pass);
                if (!ReferenceSequenceEqual(actual, originalInventories[pass]))
                {
                    throw new InvalidOperationException($"L00-C handler inventory changed before installing wrappers for pass {pass}.");
                }
                actual.Clear();
                actual.AddRange(installedInventories[pass]);
                changedPasses.Add(pass);
            }

            foreach (EnumWorldGenPass pass in modifiedPasses)
            {
                if (!ReferenceSequenceEqual(GetPassHandlers(handlers, pass), installedInventories[pass]))
                {
                    throw new InvalidOperationException($"L00-C failed exact reference/order verification after installing wrappers for pass {pass}.");
                }
            }
        }
        catch
        {
            foreach (EnumWorldGenPass pass in changedPasses)
            {
                List<ChunkColumnGenerationDelegate> actual = GetPassHandlers(handlers, pass);
                actual.Clear();
                actual.AddRange(originalInventories[pass]);
            }
            throw;
        }

        ownershipState = state;
    }

    private void ResolveMaterials()
    {
        ICoreServerAPI serverApi = RequireApi();
        config.RockBlockId = RequireBlock(serverApi, "game:rock-granite", false).Id;
        config.FreshWaterBlockId = RequireBlock(serverApi, "game:water-still-7", true).Id;
        config.SaltWaterBlockId = RequireBlock(serverApi, "game:saltwater-still-7", true).Id;
    }

    private static Block RequireBlock(ICoreServerAPI serverApi, string code, bool requireFluidLayer)
    {
        Block? block = serverApi.World.GetBlock(new AssetLocation(code));
        if (block is null || block.Id <= 0)
        {
            throw new InvalidOperationException($"L00-C required block '{code}' is unavailable.");
        }
        if (block.ForFluidsLayer != requireFluidLayer)
        {
            throw new InvalidOperationException($"L00-C block '{code}' fluid-layer flag was {block.ForFluidsLayer}, expected {requireFluidLayer}.");
        }
        return block;
    }

    private void ScheduleProbeColumn(long runId)
    {
        if (!config.AutoRun)
        {
            Log($"L00C_AUTORUN_SKIPPED instance={instanceId} active={active}");
            return;
        }

        RequireApi().Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => RequestProbeColumn(runId));
    }

    private void ScheduleInactiveWitness(long runId)
    {
        if (!config.AutoRun)
        {
            Log($"L00C_AUTORUN_SKIPPED instance={instanceId} active=False");
            return;
        }

        RequireApi().Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => CompleteInactiveWitness(runId));
    }

    private void SchedulePersistedReopen(long runId, PersistedFootprintSnapshot persistedSnapshot)
    {
        if (!config.AutoRun)
        {
            Log($"L00C_AUTORUN_SKIPPED instance={instanceId} active=True");
            return;
        }

        RequireApi().Event.ServerRunPhase(
            EnumServerRunPhase.RunGame,
            () => CompletePersistedReopen(runId, persistedSnapshot));
    }

    private void CompletePersistedReopen(long runId, PersistedFootprintSnapshot persistedSnapshot)
    {
        lock (runGate)
        {
            if (!IsCurrentRun(runId))
            {
                return;
            }
            int priorityLoads = Volatile.Read(ref priorityLoadInvocationCount);
            int transientRequests = Volatile.Read(ref transientColumnRequestCount);
            int refreshPasses = Volatile.Read(ref footprintRefreshInvocationCount);
            int refreshedMapChunks = Volatile.Read(ref refreshedMapChunkCount);
            int fixtureWrites = Volatile.Read(ref fixtureWriteCount);
            int mapSnapshotWrites = Volatile.Read(ref mapSnapshotWriteCount);
            bool transientPending = transientLoadCallbacks.PendingCount != 0;
            if (!active || requestIssued != 0 || fixtureCallbackCount != 0 || fixtureWrites != 0 ||
                priorityLoads != 0 || transientRequests != 0 || refreshPasses != 0 || refreshedMapChunks != 0 ||
                mapSnapshotWrites != 0 || transientPending || ownershipState is not null)
            {
                throw HandleAsynchronousFailure(
                    runId,
                    "persisted-reopen-state-error",
                    new InvalidOperationException($"L00-C persisted reopen mutated generation state: active={active}, requests={requestIssued}, callbacks={fixtureCallbackCount}, writes={fixtureWrites}, mapsnapshotwrites={mapSnapshotWrites}, priorityloads={priorityLoads}, transientrequests={transientRequests}, refreshpasses={refreshPasses}, refreshedmapchunks={refreshedMapChunks}, transientpending={transientPending}, handlersowned={ownershipState is not null}."));
            }

            Log($"L00C_PERSISTED_REOPEN_STABLE instance={instanceId} marker={marker!.MarkerId} run={runId} loadpriority={priorityLoads} transientrequests={transientRequests} refreshpasses={refreshPasses} refreshedmapchunks={refreshedMapChunks} keeploaded=0 unload=0 fixturewrites={fixtureWrites} mapsnapshotwrites={mapSnapshotWrites} callbacks={fixtureCallbackCount} center={persistedSnapshot.Fixture.Hash} halo={persistedSnapshot.Halo.Hash}");
            RequestActiveShutdown(runId, "persisted-reopen-stable");
        }
    }

    private void CompleteInactiveWitness(long runId)
    {
        lock (runGate)
        {
            try
            {
                if (!IsCurrentRun(runId))
                {
                    return;
                }
                Log($"L00C_WITNESS_NO_REQUEST instance={instanceId} save={RequireApi().WorldManager.SaveGame.SavegameIdentifier} loadrequests=0 transientrequests=0 refreshpasses=0 fixturewrites=0 markers=0");
                RequestInactiveWitnessShutdown(runId);
            }
            catch (Exception exception)
            {
                if (!IsCurrentRun(runId))
                {
                    return;
                }
                throw HandleAsynchronousFailure(runId, "inactive-witness-error", exception);
            }
        }
    }

    private void RequestInactiveWitnessShutdown(long runId)
    {
        RequestScheduledShutdown(runId, "inactive-witness-complete", requireActiveDelay: false);
    }

    private void RequestActiveShutdown(long runId, string reason)
    {
        RequestScheduledShutdown(runId, reason, requireActiveDelay: true);
    }

    private void RequestScheduledShutdown(long runId, string reason, bool requireActiveDelay)
    {
        if (!config.AutoShutdown)
        {
            return;
        }
        int delayMilliseconds = requireActiveDelay
            ? DelayedShutdownGate.ValidateActiveDelayMilliseconds(config.AutoShutdownDelayMilliseconds)
            : DelayedShutdownGate.ValidateDelayMilliseconds(config.AutoShutdownDelayMilliseconds);
        if (delayMilliseconds == 0)
        {
            RequestShutdownIfConfigured(reason);
            return;
        }

        ICoreServerAPI serverApi = RequireApi();
        try
        {
            delayedShutdown.Schedule(delayMilliseconds, (callback, delay) => serverApi.Event.RegisterCallback(_ => callback(), delay), serverApi.Event.UnregisterCallback, () => FireDelayedShutdown(runId, reason));
            Log($"L00C_DELAYED_SHUTDOWN_ARMED instance={instanceId} run={runId} reason={reason} delayms={delayMilliseconds}");
        }
        catch (Exception exception)
        {
            try
            {
                delayedShutdown.CancelAndUnregister();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(exception, cleanupException);
            }
            throw;
        }
    }

    private void FireDelayedShutdown(long runId, string reason)
    {
        lock (runGate)
        {
            try
            {
                if (!IsCurrentRun(runId))
                {
                    return;
                }
                Log($"L00C_DELAYED_SHUTDOWN_FIRED instance={instanceId} run={runId} reason={reason}");
                RequestShutdownIfConfigured(reason);
            }
            catch (Exception exception)
            {
                if (!IsCurrentRun(runId))
                {
                    return;
                }
                throw HandleAsynchronousFailure(runId, "delayed-shutdown-error", exception);
            }
        }
    }

    private void CancelDelayedShutdown(string reason)
    {
        delayedShutdown.CancelAndUnregister();
        api?.Logger.Notification($"L00C_DELAYED_SHUTDOWN_CANCELLED instance={instanceId} reason={reason}");
    }

    private void RequestProbeColumn(long runId)
    {
        lock (runGate)
        {
            try
            {
                RequestProbeColumnCore(runId);
            }
            catch (Exception exception)
            {
                if (!IsCurrentRun(runId))
                {
                    return;
                }
                throw HandleAsynchronousFailure(runId, "probe-request-error", exception);
            }
        }
    }

    private void RequestProbeColumnCore(long runId)
    {
        if (!IsCurrentRun(runId))
        {
            return;
        }
        if (Interlocked.Exchange(ref requestIssued, 1) != 0)
        {
            return;
        }

        ICoreServerAPI serverApi = RequireApi();
        ValidateFixtureCoordinate(serverApi);
        if (!active)
        {
            throw new InvalidOperationException("L00-C inactive worlds must never request a chunk column.");
        }

        List<TransientColumnRequest> footprint = BuildProtectedFootprintCoordinates()
            .Select(coordinate => new TransientColumnRequest(
                coordinate,
                coordinate.X == config.FixtureChunkX && coordinate.Z == config.FixtureChunkZ ? "fixture-center" : "halo"))
            .ToList();
        Log($"L00C_HALO_PREPARE_BEGIN instance={instanceId} marker={marker!.MarkerId} radius={FixtureProtectionRadius} columns={footprint.Count - 1}");
        foreach (TransientColumnRequest request in footprint.Where(item => item.Role == "halo"))
        {
            Log($"L00C_HALO_COLUMN_REQUEST instance={instanceId} marker={marker!.MarkerId} chunk=({request.Coordinate.X},{request.Coordinate.Z})");
        }
        Log($"L00C_COLUMN_REQUEST instance={instanceId} active=True chunk=({config.FixtureChunkX},{config.FixtureChunkZ})");
        LoadTransientChunkColumns(serverApi.WorldManager, footprint, () => OnHaloColumnLoaded(runId));
    }

    private void OnHaloColumnLoaded(long runId)
    {
        lock (runGate)
        {
            OnHaloColumnLoadedCore(runId);
        }
    }

    private void OnHaloColumnLoadedCore(long runId)
    {
        if (!IsCurrentRun(runId))
        {
            return;
        }

        try
        {
            for (int deltaX = -FixtureProtectionRadius; deltaX <= FixtureProtectionRadius; deltaX++)
            {
                for (int deltaZ = -FixtureProtectionRadius; deltaZ <= FixtureProtectionRadius; deltaZ++)
                {
                    if (deltaX == 0 && deltaZ == 0)
                    {
                        continue;
                    }
                    int prepared = Interlocked.Increment(ref haloPreparedCount);
                    Log($"L00C_HALO_COLUMN_PREPARED instance={instanceId} marker={marker!.MarkerId} chunk=({config.FixtureChunkX + deltaX},{config.FixtureChunkZ + deltaZ}) prepared={prepared}");
                }
            }
            Log($"L00C_HALO_PREPARE_COMPLETE instance={instanceId} marker={marker!.MarkerId} radius={FixtureProtectionRadius} columns={haloPreparedCount}");
            OnProbeColumnLoaded(runId);
        }
        catch (Exception exception)
        {
            if (!IsCurrentRun(runId))
            {
                return;
            }
            throw HandleAsynchronousFailure(runId, "halo-chain-error", exception);
        }
    }

    private void LoadTransientChunkColumns(IWorldManagerAPI worldManager, IReadOnlyList<TransientColumnRequest> requests, Action onLoaded)
    {
        if (Volatile.Read(ref disposalStarted) != 0)
        {
            throw new InvalidOperationException("L00-C refused a transient column request after disposal started.");
        }
        if (requests.Count != 9 || requests.Select(item => item.Coordinate).Distinct().Count() != requests.Count)
        {
            throw new InvalidOperationException($"L00-C requires the exact nine-column fixture footprint, got {requests.Count} request(s).");
        }

        foreach (TransientColumnRequest request in requests)
        {
            if (IsColumnAlreadyLoaded(worldManager, request.Coordinate))
            {
                Log($"L00C_COLUMN_PREEXISTING_REJECTED instance={instanceId} marker={marker?.MarkerId ?? "none"} role={request.Role} chunk=({request.Coordinate.X},{request.Coordinate.Z})");
                throw new InvalidOperationException($"L00-C refuses a non-deterministic transient request for preloaded column ({request.Coordinate.X},{request.Coordinate.Z}).");
            }
        }
        Log($"L00C_TRANSIENT_PRECONDITION instance={instanceId} marker={marker?.MarkerId ?? "none"} unloaded={requests.Count} exact=True");
        TransientLoadReservation reservation = transientLoadCallbacks.Begin(onLoaded);

        try
        {
            var options = new ChunkLoadOptions
            {
                KeepLoaded = false,
                OnLoaded = () => OnTransientLoadCompleted(reservation)
            };
            int minX = requests.Min(item => item.Coordinate.X);
            int minZ = requests.Min(item => item.Coordinate.Z);
            int maxX = requests.Max(item => item.Coordinate.X);
            int maxZ = requests.Max(item => item.Coordinate.Z);
            if ((maxX - minX + 1) * (maxZ - minZ + 1) != requests.Count)
            {
                throw new InvalidOperationException("L00-C transient footprint is not the exact requested rectangle.");
            }
            Interlocked.Increment(ref priorityLoadInvocationCount);
            Interlocked.Add(ref transientColumnRequestCount, requests.Count);
            worldManager.LoadChunkColumnPriority(minX, minZ, maxX, maxZ, options);
        }
        catch (Exception loadException)
        {
            transientLoadCallbacks.Reject(reservation);
            Log($"L00C_TRANSIENT_LOAD_REJECTED instance={instanceId} marker={marker?.MarkerId ?? "none"} columns={requests.Count} keeploaded=False pinned=0 callbackcancelled=True");
            throw new InvalidOperationException("L00-C transient range request failed without acquiring column ownership.", loadException);
        }

        Log($"L00C_TRANSIENT_LOAD_ACCEPTED instance={instanceId} marker={marker?.MarkerId ?? "none"} columns={requests.Count} keeploaded=False pinned=0 exact=True");
        transientLoadCallbacks.Accept(reservation);
    }

    private void OnTransientLoadCompleted(TransientLoadReservation reservation)
    {
        transientLoadCallbacks.Complete(reservation);
    }

    private static bool IsColumnAlreadyLoaded(IWorldManagerAPI worldManager, ChunkCoordinate coordinate)
    {
        if (worldManager.GetMapChunk(coordinate.X, coordinate.Z) is not null)
        {
            return true;
        }
        int verticalChunkCount = (worldManager.MapSizeY + worldManager.ChunkSize - 1) / worldManager.ChunkSize;
        for (int chunkY = 0; chunkY < verticalChunkCount; chunkY++)
        {
            if (worldManager.GetChunk(coordinate.X, chunkY, coordinate.Z) is not null)
            {
                return true;
            }
        }
        return false;
    }

    private void ResetTransientLoadCallbacks(string reason)
    {
        int cancelled = transientLoadCallbacks.Reset();
        Log($"L00C_TRANSIENT_CALLBACK_RESET reason={reason} instance={instanceId} cancelled={cancelled} pending={transientLoadCallbacks.PendingCount} exact={transientLoadCallbacks.PendingCount == 0}");
    }

    private bool IsCurrentRun(long runId) =>
        Volatile.Read(ref disposalStarted) == 0 && Volatile.Read(ref worldRunId) == runId;

    private Exception HandleAsynchronousFailure(long runId, string stage, Exception exception)
    {
        lock (runGate)
        {
            ICoreServerAPI? serverApi = api;
            if (serverApi is null || !IsCurrentRun(runId))
            {
                return exception;
            }

            Exception result = exception;
            try
            {
                serverApi.Logger.Error($"L00C_VALIDATION_ERROR instance={instanceId} stage={stage} type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
            }
            catch
            {
                // Validation shutdown and callback closure do not depend on logging.
            }

            try
            {
                delayedShutdown.CancelAndUnregister();
            }
            catch (Exception unregisterException)
            {
                result = new AggregateException(result, unregisterException);
            }
            int cancelled = transientLoadCallbacks.Reset();
            try
            {
                serverApi.Logger.Notification($"L00C_TRANSIENT_CALLBACK_RESET reason={stage} instance={instanceId} cancelled={cancelled} pending={transientLoadCallbacks.PendingCount} exact={transientLoadCallbacks.PendingCount == 0}");
            }
            catch
            {
                // Both callback gates are already closed.
            }
            try
            {
                RequestShutdownIfConfigured(stage);
            }
            catch (Exception shutdownException)
            {
                result = new AggregateException(result, shutdownException);
            }
            return result;
        }
    }

    private void ValidateFixtureCoordinate(ICoreServerAPI serverApi)
    {
        _ = L00CFixtureCoordinatePolicy.Validate(
            serverApi.WorldManager.MapSizeX,
            serverApi.WorldManager.MapSizeZ,
            serverApi.WorldManager.ChunkSize,
            config.FixtureChunkX,
            config.FixtureChunkZ,
            FixtureProtectionRadius);
    }

    private void InvokeOwnedHandler(OwnedHandler owned, IChunkColumnGenerateRequest request)
    {
        bool center;
        bool halo;
        lock (runGate)
        {
            if (!IsCurrentRun(owned.RunId))
            {
                return;
            }
            center = IsFixtureCenter(request);
            halo = !center && IsProtectedFixtureRequest(request);
        }

        if (!center && (!halo || !owned.Specification.SuppressInHalo))
        {
            try
            {
                owned.Original(request);
            }
            catch (Exception exception)
            {
                throw HandleAsynchronousFailure(owned.RunId, "worldgen-handler-error", exception);
            }
            lock (runGate)
            {
                if (!IsCurrentRun(owned.RunId))
                {
                    return;
                }
                Interlocked.Increment(ref owned.ForwardedCount);
                if (halo && Interlocked.Exchange(ref owned.HaloForwardLogIssued, 1) == 0)
                {
                    Log($"L00C_HALO_NATIVE_FORWARD instance={instanceId} marker={marker!.MarkerId} reason=local-only-handler pass={owned.Pass} index={owned.OriginalIndex} target={owned.OriginalTargetType} method={owned.OriginalMethod.Name} firstchunk=({request.ChunkX},{request.ChunkZ})");
                }
                else if (!halo && Interlocked.Exchange(ref owned.ForwardLogIssued, 1) == 0)
                {
                    Log($"L00C_NATIVE_FORWARD instance={instanceId} marker={marker!.MarkerId} pass={owned.Pass} index={owned.OriginalIndex} target={owned.OriginalTargetType} method={owned.OriginalMethod.Name} firstchunk=({request.ChunkX},{request.ChunkZ})");
                }
            }
            return;
        }

        lock (runGate)
        {
            if (!IsCurrentRun(owned.RunId))
            {
                return;
            }
            try
            {
                InvokeOwnedHandlerCore(owned, request, center, halo);
            }
            catch (Exception exception)
            {
                throw HandleAsynchronousFailure(owned.RunId, "worldgen-handler-error", exception);
            }
        }
    }

    private void InvokeOwnedHandlerCore(OwnedHandler owned, IChunkColumnGenerateRequest request, bool center, bool halo)
    {
        if (!active)
        {
            throw new InvalidOperationException("L00-C fixture handler ran while the world was inactive.");
        }

        if (center && owned.Specification.WritesFixture)
        {
            GenerateFixtureColumn(request);
            return;
        }

        string scope = center ? "center" : "halo";
        string reason = center ? "canonical-center" : "cross-column-write-guard";
        Log($"L00C_HANDLER_SUPPRESSED instance={instanceId} marker={marker!.MarkerId} scope={scope} reason={reason} pass={owned.Pass} index={owned.OriginalIndex} target={owned.OriginalTargetType} method={owned.OriginalMethod.Name} chunk=({request.ChunkX},{request.ChunkZ})");
    }

    private void GenerateFixtureColumn(IChunkColumnGenerateRequest request)
    {
        if (Interlocked.Increment(ref fixtureCallbackCount) != 1)
        {
            throw new InvalidOperationException("L00-C fixture handler ran more than once for the bounded column.");
        }

        WriteCanonicalFixture(request, "terrain");
    }

    private void FinalizeFixtureBeforeLighting(IChunkColumnGenerateRequest request)
    {
        if (!IsFixtureCenter(request))
        {
            return;
        }

        if (!active)
        {
            throw new InvalidOperationException("L00-C pre-lighting finalizer ran while the world was inactive.");
        }

        WriteCanonicalFixture(request, "prelighting");
        int chunkSize = RequireApi().WorldManager.ChunkSize;
        int worldHeight = request.Chunks.Length * chunkSize;
        PersistedMapChunkSnapshot mapSnapshot = PersistedMapChunkSnapshot.Capture(request.ChunkX, request.ChunkZ, request.Chunks[0].MapChunk);
        FixtureSnapshot snapshot = InspectFixtureData("prelighting", request.Chunks, mapSnapshot, chunkSize, worldHeight);
        Volatile.Write(ref preLightingSnapshot, snapshot);
    }

    private void InvokeLightingFinalizer(long runId, IChunkColumnGenerateRequest request)
    {
        lock (runGate)
        {
            if (!IsCurrentRun(runId))
            {
                return;
            }
            try
            {
                FinalizeFixtureBeforeLighting(request);
            }
            catch (Exception exception)
            {
                throw HandleAsynchronousFailure(runId, "lighting-finalizer-error", exception);
            }
        }
    }

    private void WriteCanonicalFixture(IChunkColumnGenerateRequest request, string phase)
    {
        Interlocked.Increment(ref fixtureWriteCount);
        int chunkSize = RequireApi().WorldManager.ChunkSize;
        int worldHeight = request.Chunks.Length * chunkSize;
        FixtureGeometry geometry = FixtureGeometry.Create(chunkSize, worldHeight);

        foreach (IServerChunk chunk in request.Chunks)
        {
            chunk.Data.ClearBlocksAndPrepare();
            chunk.Empty = true;
        }

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                bool wall = geometry.IsWall(x, z);
                int solidTop = wall ? geometry.WaterSurface : geometry.RockSurface;
                for (int y = 0; y <= solidTop; y++)
                {
                    SetSolid(request.Chunks, chunkSize, x, y, z, config.RockBlockId);
                }

                if (!wall)
                {
                    int fluidId = x < geometry.DividerX ? config.FreshWaterBlockId : config.SaltWaterBlockId;
                    for (int y = geometry.RockSurface + 1; y <= geometry.WaterSurface; y++)
                    {
                        SetFluid(request.Chunks, chunkSize, x, y, z, fluidId);
                    }
                }
            }
        }

        IMapChunk mapChunk = request.Chunks[0].MapChunk;
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                int index2d = MapUtil.Index2d(x, z, chunkSize);
                bool wall = geometry.IsWall(x, z);
                mapChunk.WorldGenTerrainHeightMap[index2d] = (ushort)(wall ? geometry.WaterSurface : geometry.RockSurface);
                mapChunk.RainHeightMap[index2d] = (ushort)geometry.WaterSurface;
                mapChunk.TopRockIdMap[index2d] = config.RockBlockId;
            }
        }
        mapChunk.YMax = (ushort)geometry.WaterSurface;
        foreach (IServerChunk chunk in request.Chunks)
        {
            chunk.MarkModified();
        }
        mapChunk.MarkDirty();

        Log($"L00C_FIXTURE_WRITTEN instance={instanceId} marker={marker!.MarkerId} phase={phase} center=({config.FixtureChunkX},{config.FixtureChunkZ}) radius={FixtureProtectionRadius} chunk=({request.ChunkX},{request.ChunkZ}) chunksize={chunkSize} worldheight={worldHeight} rock={config.RockBlockId} fresh={config.FreshWaterBlockId} salt={config.SaltWaterBlockId} rocksurface={geometry.RockSurface} watersurface={geometry.WaterSurface} thread={Environment.CurrentManagedThreadId}");
    }

    private bool IsProtectedFixtureRequest(IChunkColumnGenerateRequest request)
    {
        return Math.Abs(request.ChunkX - config.FixtureChunkX) <= FixtureProtectionRadius &&
            Math.Abs(request.ChunkZ - config.FixtureChunkZ) <= FixtureProtectionRadius;
    }

    private bool IsFixtureCenter(IChunkColumnGenerateRequest request)
    {
        return request.ChunkX == config.FixtureChunkX && request.ChunkZ == config.FixtureChunkZ;
    }

    private static void SetSolid(IServerChunk[] chunks, int chunkSize, int x, int y, int z, int blockId)
    {
        int chunkY = y / chunkSize;
        int localY = y % chunkSize;
        int index3d = MapUtil.Index3d(x, localY, z, chunkSize, chunkSize);
        chunks[chunkY].Data.SetBlockUnsafe(index3d, blockId);
        chunks[chunkY].Empty = false;
    }

    private static void SetFluid(IServerChunk[] chunks, int chunkSize, int x, int y, int z, int blockId)
    {
        int chunkY = y / chunkSize;
        int localY = y % chunkSize;
        int index3d = MapUtil.Index3d(x, localY, z, chunkSize, chunkSize);
        chunks[chunkY].Data.SetFluid(index3d, blockId);
        chunks[chunkY].Empty = false;
    }

    private void OnProbeColumnLoaded(long runId)
    {
        lock (runGate)
        {
            OnProbeColumnLoadedCore(runId);
        }
    }

    private void OnProbeColumnLoadedCore(long runId)
    {
        if (!IsCurrentRun(runId))
        {
            return;
        }

        try
        {
            if (!active)
            {
                throw new InvalidOperationException("L00-C received a probe-column callback for an inactive world.");
            }
            RefreshTransientFootprint(runId, "loaded", 0);
            initialSnapshot = InspectFixture("loaded");
            FixtureSnapshot? lightingSnapshot = Interlocked.Exchange(ref preLightingSnapshot, null);
            if (lightingSnapshot is not null)
            {
                if (!string.Equals(lightingSnapshot.Hash, initialSnapshot.Hash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"L00-C blocks or heightmaps changed after the native lighting handlers: {lightingSnapshot.Hash} -> {initialSnapshot.Hash}.");
                }
                Log($"L00C_LIGHTING_STABLE instance={instanceId} marker={marker!.MarkerId} prelighting={lightingSnapshot.Hash} postlighting={initialSnapshot.Hash}");
            }
            initialHaloSnapshot = InspectProtectionHalo("loaded");
            stableTickCount = 0;
        }
        catch (Exception exception)
        {
            if (!IsCurrentRun(runId))
            {
                return;
            }
            throw HandleAsynchronousFailure(runId, "probe-loaded-error", exception);
        }
    }

    private void RefreshTransientFootprint(long runId, string phase, int tick)
    {
        if (!IsCurrentRun(runId) || !active)
        {
            return;
        }

        IWorldManagerAPI worldManager = RequireApi().WorldManager;
        List<ChunkCoordinate> footprint = BuildProtectedFootprintCoordinates();
        if (footprint.Count != 9 || footprint.Distinct().Count() != footprint.Count)
        {
            throw new InvalidOperationException($"L00-C transient refresh requires the exact nine-column footprint, got {footprint.Count} coordinate(s).");
        }

        int sequence = Interlocked.Increment(ref footprintRefreshInvocationCount);
        int refreshed = 0;
        foreach (ChunkCoordinate coordinate in footprint)
        {
            if (!IsCurrentRun(runId))
            {
                return;
            }
            IMapChunk mapChunk = worldManager.GetMapChunk(coordinate.X, coordinate.Z)
                ?? throw new InvalidOperationException($"L00-C transient refresh could not find map chunk ({coordinate.X},{coordinate.Z}).");
            mapChunk.MarkFresh();
            Interlocked.Increment(ref refreshedMapChunkCount);
            refreshed++;
        }
        if (refreshed != 9)
        {
            throw new InvalidOperationException($"L00-C transient refresh touched {refreshed} map chunks instead of exactly nine.");
        }
        Log($"L00C_FOOTPRINT_REFRESH instance={instanceId} marker={marker!.MarkerId} phase={phase} tick={tick} sequence={sequence} columns={refreshed} exact=True");
    }

    private void OnServerTick(float deltaTime)
    {
        lock (runGate)
        {
            OnServerTickCore(Volatile.Read(ref worldRunId), deltaTime);
        }
    }

    private void OnServerTickCore(long runId, float deltaTime)
    {
        if (!IsCurrentRun(runId) || !active || initialSnapshot is null)
        {
            return;
        }

        try
        {
            RefreshTransientFootprint(runId, "tick", stableTickCount + 1);
            stableTickCount++;
            if (stableTickCount < StableTickTarget)
            {
                return;
            }

            FixtureSnapshot afterTicks = InspectFixture("afterticks");
            if (!string.Equals(initialSnapshot.Hash, afterTicks.Hash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"L00-C fixture changed after {StableTickTarget} ticks: {initialSnapshot.Hash} -> {afterTicks.Hash}.");
            }

            HaloSnapshot afterTicksHalo = InspectProtectionHalo("afterticks");
            if (initialHaloSnapshot is null || !string.Equals(initialHaloSnapshot.Hash, afterTicksHalo.Hash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"L00-C protected first ring changed after {StableTickTarget} ticks: {initialHaloSnapshot?.Hash ?? "none"} -> {afterTicksHalo.Hash}.");
            }

            Log($"L00C_TICKS_STABLE instance={instanceId} marker={marker!.MarkerId} run={runId} ticks={StableTickTarget} snapshot={afterTicks.Hash} fluids={afterTicks.FluidCount} fresh={afterTicks.FreshCount} salt={afterTicks.SaltCount} unexpected={afterTicks.UnexpectedCount}");
            Log($"L00C_HALO_STABLE instance={instanceId} marker={marker!.MarkerId} ticks={StableTickTarget} columns={afterTicksHalo.ColumnCount} snapshot={afterTicksHalo.Hash}");
            int priorityLoads = Volatile.Read(ref priorityLoadInvocationCount);
            int transientRequests = Volatile.Read(ref transientColumnRequestCount);
            int refreshPasses = Volatile.Read(ref footprintRefreshInvocationCount);
            int refreshedMapChunks = Volatile.Read(ref refreshedMapChunkCount);
            if (priorityLoads != 1 || transientRequests != 9 || refreshPasses != StableTickTarget + 1 || refreshedMapChunks != (StableTickTarget + 1) * 9)
            {
                throw new InvalidOperationException($"L00-C transient lifecycle counters diverged: loadpriority={priorityLoads}, transientrequests={transientRequests}, refreshpasses={refreshPasses}, refreshedmapchunks={refreshedMapChunks}.");
            }
            Log($"L00C_TRANSIENT_LIFECYCLE_STABLE instance={instanceId} marker={marker!.MarkerId} loadpriority={priorityLoads} transientrequests={transientRequests} refreshpasses={refreshPasses} refreshedmapchunks={refreshedMapChunks} keeploaded=0 unload=0");
            marker.MapFootprint = CaptureCurrentMapFootprint();
            markerPublication.Commit(payload => RequireApi().WorldManager.SaveGame.StoreData(MarkerKey, payload));
            int mapSnapshotWrites = Interlocked.Increment(ref mapSnapshotWriteCount);
            Log($"L00C_MAP_SNAPSHOT_COMMITTED instance={instanceId} marker={marker.MarkerId} maps={marker.MapFootprint.MapChunks.Count} checksum={marker.MapFootprint.ContentSha256} writes={mapSnapshotWrites}");
            initialSnapshot = null;
            initialHaloSnapshot = null;
            RequestActiveShutdown(runId, "fixture-stable");
        }
        catch (Exception exception)
        {
            initialSnapshot = null;
            initialHaloSnapshot = null;
            if (Volatile.Read(ref disposalStarted) != 0)
            {
                return;
            }
            throw HandleAsynchronousFailure(runId, "tick-validation-error", exception);
        }
    }

    private PersistedFootprintSnapshot InspectPersistedFootprintBlocking()
    {
        ICoreServerAPI serverApi = RequireApi();
        ValidateFixtureCoordinate(serverApi);
        IWorldManagerAPI worldManager = serverApi.WorldManager;
        List<ChunkCoordinate> footprint = BuildProtectedFootprintCoordinates();
        foreach (ChunkCoordinate coordinate in footprint)
        {
            if (!worldManager.BlockingTestMapChunkExists(coordinate.X, coordinate.Z))
            {
                string message = $"L00-C persisted footprint map chunk ({coordinate.X},{coordinate.Z}) does not exist before blocking load.";
                serverApi.Logger.Error($"L00C_ERROR code=persisted-map-missing instance={instanceId} marker={marker!.MarkerId} chunk=({coordinate.X},{coordinate.Z}) message={Sanitize(message)}");
                throw new InvalidOperationException(message);
            }
        }
        Log($"L00C_PERSISTED_PRECHECK instance={instanceId} marker={marker!.MarkerId} maps={footprint.Count} exact={footprint.Count == 9}");

        int chunkSize = worldManager.ChunkSize;
        int worldHeight = worldManager.MapSizeY;
        PersistedMapFootprintSnapshot mapFootprint = marker.MapFootprint
            ?? throw new InvalidOperationException("L00-C persistent marker does not contain the required bounded map snapshot.");
        IReadOnlyDictionary<string, PersistedMapChunkSnapshot> mapSnapshots = mapFootprint.ValidateAndCopy(
            marker.MarkerId,
            worldManager.SaveGame.SavegameIdentifier,
            config.FixtureChunkX,
            config.FixtureChunkZ,
            chunkSize,
            worldHeight);
        foreach (ChunkCoordinate coordinate in footprint)
        {
            PersistedMapChunkSnapshot mapSnapshot = mapSnapshots[PersistedMapFootprintSnapshot.CoordinateKey(coordinate.X, coordinate.Z)];
            Log($"L00C_PERSISTED_MAP_SNAPSHOT_LOADED instance={instanceId} marker={marker.MarkerId} chunk=({coordinate.X},{coordinate.Z}) ymax={mapSnapshot.YMax} checksum={mapFootprint.ContentSha256}");
        }
        int expectedChunksPerColumn = worldHeight / chunkSize;
        var loadedColumns = new Dictionary<ChunkCoordinate, IServerChunk[]>();
        PersistedFootprintSnapshot? persistedSnapshot = null;
        Exception? inspectionFailure = null;
        Exception? disposalFailure = null;
        int disposedColumns = 0;
        int disposedChunks = 0;

        try
        {
            for (int index = 0; index < footprint.Count; index++)
            {
                ChunkCoordinate coordinate = footprint[index];
                IServerChunk[] chunks = worldManager.BlockingLoadChunkColumn(coordinate.X, coordinate.Z)
                    ?? throw new InvalidOperationException($"L00-C blocking load returned null for persisted column ({coordinate.X},{coordinate.Z}).");
                loadedColumns.Add(coordinate, chunks);
                if (chunks.Length != expectedChunksPerColumn)
                {
                    throw new InvalidOperationException($"L00-C blocking load returned {chunks.Length} chunks for ({coordinate.X},{coordinate.Z}), expected {expectedChunksPerColumn}.");
                }
                Log($"L00C_PERSISTED_COLUMN_LOADED instance={instanceId} marker={marker!.MarkerId} chunk=({coordinate.X},{coordinate.Z}) sequence={index + 1} chunks={chunks.Length}");
            }

            foreach (IServerChunk[] chunks in loadedColumns.Values)
            {
                foreach (IServerChunk chunk in chunks)
                {
                    chunk.Unpack_ReadOnly();
                }
            }

            var center = new ChunkCoordinate(config.FixtureChunkX, config.FixtureChunkZ);
            IServerChunk[] centerChunks = loadedColumns[center];
            FixtureSnapshot fixture = InspectFixtureData(
                "loaded",
                centerChunks,
                mapSnapshots[PersistedMapFootprintSnapshot.CoordinateKey(center.X, center.Z)],
                chunkSize,
                worldHeight);
            HaloSnapshot halo = InspectProtectionHaloData("loaded", loadedColumns, mapSnapshots, chunkSize, worldHeight);
            persistedSnapshot = new PersistedFootprintSnapshot(fixture, halo);
        }
        catch (Exception exception)
        {
            inspectionFailure = exception;
        }
        finally
        {
            foreach (ChunkCoordinate coordinate in footprint)
            {
                if (!loadedColumns.TryGetValue(coordinate, out IServerChunk[]? chunks))
                {
                    continue;
                }

                bool columnDisposed = true;
                foreach (IServerChunk chunk in chunks)
                {
                    try
                    {
                        chunk.Dispose();
                        disposedChunks++;
                    }
                    catch (Exception exception)
                    {
                        columnDisposed = false;
                        disposalFailure = disposalFailure is null ? exception : new AggregateException(disposalFailure, exception);
                    }
                }
                if (columnDisposed)
                {
                    disposedColumns++;
                }
            }

            int expectedDisposedChunks = footprint.Count * expectedChunksPerColumn;
            bool exact = disposalFailure is null && loadedColumns.Count == footprint.Count &&
                disposedColumns == footprint.Count && disposedChunks == expectedDisposedChunks;
            Log($"L00C_PERSISTED_COLUMNS_DISPOSED instance={instanceId} marker={marker!.MarkerId} columns={disposedColumns} chunks={disposedChunks} exact={exact}");
        }

        if (inspectionFailure is not null && disposalFailure is not null)
        {
            throw new AggregateException(inspectionFailure, disposalFailure);
        }
        if (inspectionFailure is not null)
        {
            throw inspectionFailure;
        }
        if (disposalFailure is not null)
        {
            throw new InvalidOperationException("L00-C could not dispose every blocking-loaded persisted chunk.", disposalFailure);
        }
        return persistedSnapshot ?? throw new InvalidOperationException("L00-C persisted footprint inspection completed without a snapshot.");
    }

    private PersistedMapFootprintSnapshot CaptureCurrentMapFootprint()
    {
        ICoreServerAPI serverApi = RequireApi();
        var mapSnapshots = new List<PersistedMapChunkSnapshot>();
        foreach (ChunkCoordinate coordinate in BuildProtectedFootprintCoordinates())
        {
            IMapChunk mapChunk = serverApi.WorldManager.GetMapChunk(coordinate.X, coordinate.Z)
                ?? throw new InvalidOperationException($"L00-C cannot capture unloaded map chunk ({coordinate.X},{coordinate.Z}) after stable validation.");
            mapSnapshots.Add(PersistedMapChunkSnapshot.Capture(coordinate.X, coordinate.Z, mapChunk));
        }

        return PersistedMapFootprintSnapshot.Create(
            marker!.MarkerId,
            serverApi.WorldManager.SaveGame.SavegameIdentifier,
            config.FixtureChunkX,
            config.FixtureChunkZ,
            serverApi.WorldManager.ChunkSize,
            serverApi.WorldManager.MapSizeY,
            mapSnapshots);
    }

    private List<ChunkCoordinate> BuildProtectedFootprintCoordinates()
    {
        var footprint = new List<ChunkCoordinate>();
        for (int deltaX = -FixtureProtectionRadius; deltaX <= FixtureProtectionRadius; deltaX++)
        {
            for (int deltaZ = -FixtureProtectionRadius; deltaZ <= FixtureProtectionRadius; deltaZ++)
            {
                footprint.Add(new ChunkCoordinate(config.FixtureChunkX + deltaX, config.FixtureChunkZ + deltaZ));
            }
        }
        return footprint;
    }

    private FixtureSnapshot InspectFixture(string phase)
    {
        ICoreServerAPI serverApi = RequireApi();
        int chunkSize = serverApi.WorldManager.ChunkSize;
        int worldHeight = serverApi.WorldManager.MapSizeY;
        IMapChunk mapChunk = serverApi.WorldManager.GetMapChunk(config.FixtureChunkX, config.FixtureChunkZ)
            ?? throw new InvalidOperationException("L00-C fixture map chunk is not loaded.");
        int chunkCount = worldHeight / chunkSize;
        var chunks = new IServerChunk[chunkCount];
        for (int chunkY = 0; chunkY < chunkCount; chunkY++)
        {
            IServerChunk chunk = serverApi.WorldManager.GetChunk(config.FixtureChunkX, chunkY, config.FixtureChunkZ)
                ?? throw new InvalidOperationException($"L00-C fixture chunk y={chunkY} is not loaded.");
            chunk.Unpack_ReadOnly();
            chunks[chunkY] = chunk;
        }

        return InspectFixtureData(
            phase,
            chunks,
            PersistedMapChunkSnapshot.Capture(config.FixtureChunkX, config.FixtureChunkZ, mapChunk),
            chunkSize,
            worldHeight);
    }

    private HaloSnapshot InspectProtectionHalo(string phase)
    {
        ICoreServerAPI serverApi = RequireApi();
        int chunkSize = serverApi.WorldManager.ChunkSize;
        int worldHeight = serverApi.WorldManager.MapSizeY;
        int chunkCount = worldHeight / chunkSize;
        var columns = new Dictionary<ChunkCoordinate, IServerChunk[]>();
        var mapSnapshots = new Dictionary<string, PersistedMapChunkSnapshot>(StringComparer.Ordinal);

        for (int deltaX = -FixtureProtectionRadius; deltaX <= FixtureProtectionRadius; deltaX++)
        {
            for (int deltaZ = -FixtureProtectionRadius; deltaZ <= FixtureProtectionRadius; deltaZ++)
            {
                if (deltaX == 0 && deltaZ == 0)
                {
                    continue;
                }

                int chunkX = config.FixtureChunkX + deltaX;
                int chunkZ = config.FixtureChunkZ + deltaZ;
                IMapChunk mapChunk = serverApi.WorldManager.GetMapChunk(chunkX, chunkZ)
                    ?? throw new InvalidOperationException($"L00-C protected halo map chunk ({chunkX},{chunkZ}) is not loaded.");
                mapSnapshots.Add(
                    PersistedMapFootprintSnapshot.CoordinateKey(chunkX, chunkZ),
                    PersistedMapChunkSnapshot.Capture(chunkX, chunkZ, mapChunk));
                var chunks = new IServerChunk[chunkCount];
                for (int chunkY = 0; chunkY < chunkCount; chunkY++)
                {
                    IServerChunk chunk = serverApi.WorldManager.GetChunk(chunkX, chunkY, chunkZ)
                        ?? throw new InvalidOperationException($"L00-C protected halo chunk ({chunkX},{chunkY},{chunkZ}) is not loaded.");
                    chunk.Unpack_ReadOnly();
                    chunks[chunkY] = chunk;
                }
                columns.Add(new ChunkCoordinate(chunkX, chunkZ), chunks);
            }
        }

        return InspectProtectionHaloData(phase, columns, mapSnapshots, chunkSize, worldHeight);
    }

    private HaloSnapshot InspectProtectionHaloData(
        string phase,
        IReadOnlyDictionary<ChunkCoordinate, IServerChunk[]> columns,
        IReadOnlyDictionary<string, PersistedMapChunkSnapshot> mapSnapshots,
        int chunkSize,
        int worldHeight)
    {
        ICoreServerAPI serverApi = RequireApi();
        var aggregate = new StringBuilder();
        int inspectedColumns = 0;

        for (int deltaX = -FixtureProtectionRadius; deltaX <= FixtureProtectionRadius; deltaX++)
        {
            for (int deltaZ = -FixtureProtectionRadius; deltaZ <= FixtureProtectionRadius; deltaZ++)
            {
                if (deltaX == 0 && deltaZ == 0)
                {
                    continue;
                }

                int chunkX = config.FixtureChunkX + deltaX;
                int chunkZ = config.FixtureChunkZ + deltaZ;
                var coordinate = new ChunkCoordinate(chunkX, chunkZ);
                if (!columns.TryGetValue(coordinate, out IServerChunk[]? chunks))
                {
                    throw new InvalidOperationException($"L00-C protected halo column ({chunkX},{chunkZ}) is unavailable for inspection.");
                }
                int expectedChunkCount = worldHeight / chunkSize;
                if (chunks.Length != expectedChunkCount)
                {
                    throw new InvalidOperationException($"L00-C protected halo column ({chunkX},{chunkZ}) has {chunks.Length} vertical chunks, expected {expectedChunkCount}.");
                }
                PersistedMapChunkSnapshot mapChunk = mapSnapshots[PersistedMapFootprintSnapshot.CoordinateKey(chunkX, chunkZ)];

                var canonical = new StringBuilder();
                var blockIds = new HashSet<int>();
                int solidCount = 0;
                int fluidCount = 0;
                for (int z = 0; z < chunkSize; z++)
                {
                    for (int x = 0; x < chunkSize; x++)
                    {
                        int index2d = MapUtil.Index2d(x, z, chunkSize);
                        canonical.Append("m:")
                            .Append(mapChunk.WorldGenTerrainHeightMap[index2d]).Append(',')
                            .Append(mapChunk.RainHeightMap[index2d]).Append(',')
                            .Append(mapChunk.TopRockIdMap[index2d]).Append(';');
                        for (int y = 0; y < worldHeight; y++)
                        {
                            int index3d = MapUtil.Index3d(x, y % chunkSize, z, chunkSize, chunkSize);
                            IServerChunk chunk = chunks[y / chunkSize];
                            int solid = chunk.Data.GetBlockId(index3d, BlockLayersAccess.Solid);
                            int fluid = chunk.Data.GetBlockId(index3d, BlockLayersAccess.Fluid);
                            if (solid != 0) solidCount++;
                            if (fluid != 0) fluidCount++;
                            blockIds.Add(solid);
                            blockIds.Add(fluid);
                            canonical.Append(solid).Append(',').Append(fluid).Append(';');
                        }
                    }
                }

                if (solidCount == 0 || mapChunk.YMax == 0 || mapChunk.YMax >= worldHeight)
                {
                    throw new InvalidOperationException($"L00-C protected halo column ({chunkX},{chunkZ}) is empty or has invalid height metadata: solid={solidCount}, ymax={mapChunk.YMax}.");
                }
                foreach (int blockId in blockIds)
                {
                    if (serverApi.World.GetBlock(blockId) is null)
                    {
                        throw new InvalidOperationException($"L00-C protected halo column ({chunkX},{chunkZ}) contains unknown block id {blockId}.");
                    }
                }

                canonical.Append("ymax=").Append(mapChunk.YMax);
                string columnHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
                aggregate.Append(chunkX).Append(',').Append(chunkZ).Append(':').Append(columnHash).Append(';');
                inspectedColumns++;
                Log($"L00C_HALO_COLUMN_VALID instance={instanceId} marker={marker!.MarkerId} phase={phase} chunk=({chunkX},{chunkZ}) snapshot={columnHash} solids={solidCount} fluids={fluidCount} ymax={mapChunk.YMax} blockids={blockIds.Count}");
            }
        }

        int expectedColumns = (2 * FixtureProtectionRadius + 1) * (2 * FixtureProtectionRadius + 1) - 1;
        if (inspectedColumns != expectedColumns)
        {
            throw new InvalidOperationException($"L00-C protected halo inspected {inspectedColumns} columns instead of {expectedColumns}.");
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(aggregate.ToString())));
        Log($"L00C_HALO_VALID instance={instanceId} marker={marker!.MarkerId} phase={phase} radius={FixtureProtectionRadius} columns={inspectedColumns} snapshot={hash}");
        return new HaloSnapshot(hash, inspectedColumns);
    }

    private FixtureSnapshot InspectFixtureData(
        string phase,
        IReadOnlyList<IServerChunk> chunks,
        PersistedMapChunkSnapshot mapChunk,
        int chunkSize,
        int worldHeight)
    {
        FixtureGeometry geometry = FixtureGeometry.Create(chunkSize, worldHeight);

        int solidCount = 0;
        int fluidCount = 0;
        int freshCount = 0;
        int saltCount = 0;
        int unexpectedCount = 0;
        int terrainMapMismatchCount = 0;
        int rainMapMismatchCount = 0;
        int topRockMapMismatchCount = 0;
        int solidMismatchCount = 0;
        int fluidMismatchCount = 0;
        string firstMismatch = "none";
        var canonical = new StringBuilder();
        canonical.Append(chunkSize).Append('|').Append(worldHeight).Append('|')
            .Append(geometry.RockSurface).Append('|').Append(geometry.WaterSurface).Append('|')
            .Append(config.RockBlockId).Append('|').Append(config.FreshWaterBlockId).Append('|')
            .Append(config.SaltWaterBlockId).Append('|');

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                bool wall = geometry.IsWall(x, z);
                int expectedSolidTop = wall ? geometry.WaterSurface : geometry.RockSurface;
                int index2d = MapUtil.Index2d(x, z, chunkSize);
                int expectedTerrainHeight = expectedSolidTop;
                canonical.Append(mapChunk.WorldGenTerrainHeightMap[index2d]).Append(',')
                    .Append(mapChunk.RainHeightMap[index2d]).Append(',')
                    .Append(mapChunk.TopRockIdMap[index2d]).Append(';');
                if (mapChunk.WorldGenTerrainHeightMap[index2d] != expectedTerrainHeight)
                {
                    terrainMapMismatchCount++;
                    unexpectedCount++;
                }
                if (mapChunk.RainHeightMap[index2d] != geometry.WaterSurface)
                {
                    rainMapMismatchCount++;
                    unexpectedCount++;
                }
                if (mapChunk.TopRockIdMap[index2d] != config.RockBlockId)
                {
                    topRockMapMismatchCount++;
                    unexpectedCount++;
                }

                for (int y = 0; y < worldHeight; y++)
                {
                    IServerChunk chunk = chunks[y / chunkSize];
                    int index3d = MapUtil.Index3d(x, y % chunkSize, z, chunkSize, chunkSize);
                    int solid = chunk.Data.GetBlockId(index3d, BlockLayersAccess.Solid);
                    int fluid = chunk.Data.GetBlockId(index3d, BlockLayersAccess.Fluid);
                    canonical.Append(solid).Append(',').Append(fluid).Append(';');
                    if (solid != 0) solidCount++;
                    if (fluid != 0) fluidCount++;
                    if (fluid == config.FreshWaterBlockId) freshCount++;
                    else if (fluid == config.SaltWaterBlockId) saltCount++;

                    int expectedSolid = y <= expectedSolidTop ? config.RockBlockId : 0;
                    int expectedFluid = 0;
                    if (!wall && y > geometry.RockSurface && y <= geometry.WaterSurface)
                    {
                        expectedFluid = x < geometry.DividerX ? config.FreshWaterBlockId : config.SaltWaterBlockId;
                    }
                    if (solid != expectedSolid)
                    {
                        solidMismatchCount++;
                        unexpectedCount++;
                        if (firstMismatch == "none") firstMismatch = $"solid@{x},{y},{z}:actual={solid}:expected={expectedSolid}";
                    }
                    if (fluid != expectedFluid)
                    {
                        fluidMismatchCount++;
                        unexpectedCount++;
                        if (firstMismatch == "none") firstMismatch = $"fluid@{x},{y},{z}:actual={fluid}:expected={expectedFluid}";
                    }
                }
            }
        }

        if (mapChunk.YMax != geometry.WaterSurface || freshCount == 0 || saltCount == 0 || unexpectedCount != 0)
        {
            throw new InvalidOperationException($"L00-C fixture validation failed: ymax={mapChunk.YMax}, fresh={freshCount}, salt={saltCount}, unexpected={unexpectedCount}, terrainmap={terrainMapMismatchCount}, rainmap={rainMapMismatchCount}, toprockmap={topRockMapMismatchCount}, solid={solidMismatchCount}, fluid={fluidMismatchCount}, first={firstMismatch}.");
        }

        canonical.Append("ymax=").Append(mapChunk.YMax);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        var snapshot = new FixtureSnapshot(hash, solidCount, fluidCount, freshCount, saltCount, unexpectedCount, mapChunk.YMax);
        Log($"L00C_FIXTURE_INSPECTED instance={instanceId} marker={marker!.MarkerId} phase={phase} chunk=({config.FixtureChunkX},{config.FixtureChunkZ}) snapshot={hash} solids={solidCount} fluids={fluidCount} fresh={freshCount} salt={saltCount} terrain={geometry.RockSurface} water={geometry.WaterSurface} ymax={mapChunk.YMax} unexpected={unexpectedCount}");
        return snapshot;
    }

    private void RequestShutdownIfConfigured(string reason)
    {
        if (!config.AutoShutdown || Interlocked.Exchange(ref shutdownIssued, 1) != 0)
        {
            return;
        }

        try
        {
            Log($"L00C_GRACEFUL_SHUTDOWN_REQUEST instance={instanceId} marker={marker?.MarkerId ?? "none"} reason={reason}");
        }
        catch
        {
            // The shutdown request remains mandatory even if observability fails.
        }
        RequireApi().Server.ShutDown();
    }

    private void OnGameWorldSave()
    {
        lock (runGate)
        {
            OnGameWorldSaveCore();
        }
    }

    private void OnGameWorldSaveCore()
    {
        if (Volatile.Read(ref disposalStarted) == 0 && marker is not null &&
            markerPublication.SaveIfCommitted(payload => RequireApi().WorldManager.SaveGame.StoreData(MarkerKey, payload)))
        {
            Log($"L00C_MARKER_SAVED instance={instanceId} marker={marker.MarkerId} open={marker.OpenCount}");
        }
        else if (Volatile.Read(ref disposalStarted) == 0 && lifecycleMarker is not null)
        {
            RequireApi().WorldManager.SaveGame.StoreData(LifecycleMarkerKey, lifecycleMarker.Serialize());
            LogLifecycleEvent("GameWorldSaveStoreData", "native save callback re-staged the marker but does not alone prove SaveCommitted",
                Volatile.Read(ref worldRunId), $"instance={instanceId} marker={lifecycleMarker.MarkerId} open={lifecycleMarker.OpenCount}");
        }
    }

    private ProbeMarker? ReadMarker(ISaveGame saveGame, IWorldManagerAPI worldManager)
    {
        byte[]? bytes = saveGame.GetData(MarkerKey);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return ProbeMarkerEnvelopeReader.ReadAndValidate(
                bytes,
                saveGame.SavegameIdentifier,
                config.FixtureChunkX,
                config.FixtureChunkZ,
                worldManager.ChunkSize,
                worldManager.MapSizeY);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("L00-C persistent marker is corrupt; loading is stopped explicitly.", exception);
        }
    }

    private static void ValidateMarker(ProbeMarker marker, ISaveGame saveGame)
    {
        if (marker.Version != MarkerVersion || marker.SavegameIdentifier != saveGame.SavegameIdentifier || string.IsNullOrWhiteSpace(marker.MarkerId))
        {
            throw new InvalidOperationException("L00-C persistent marker is incompatible with this save; loading is stopped explicitly.");
        }
    }

    private void LogInventory(string phase, IWorldGenHandler handlers, ISaveGame saveGame, int staleProbeHandlers, bool sameHandlerSet)
    {
        string mapRegion = DescribeHandlers(handlers.OnMapRegionGen.Cast<Delegate>());
        string mapChunk = DescribeHandlers(handlers.OnMapChunkGen.Cast<Delegate>());
        string column = FormatColumnInventory(handlers);
        Log($"L00C_HANDLERS phase={phase} instance={instanceId} save={saveGame.SavegameIdentifier} isnew={saveGame.IsNew} samehandlerset={sameHandlerSet} staleprobe={staleProbeHandlers} mapregion=[{mapRegion}] mapchunk=[{mapChunk}] column={column}");
    }

    private string FormatColumnInventory(IWorldGenHandler handlers)
    {
        return string.Join(";", handlers.OnChunkColumnGen.Select((items, index) =>
            items is null ? $"{index}=[null]" : $"{index}=[{DescribeHandlers(items.Cast<Delegate>())}]"));
    }

    private string DescribeHandlers(IEnumerable<Delegate> handlers)
    {
        return string.Join(",", handlers.Select((handler, index) => $"{index}:{DescribeHandler(handler)}"));
    }

    private string DescribeHandler(Delegate handler)
    {
        HandlerOwnershipState? state = ownershipState;
        OwnedHandler? owned = state?.OwnedHandlers.FirstOrDefault(candidate => ReferenceEquals(candidate.Wrapper, handler));
        if (owned is not null)
        {
            return $"L00CWrapper(original={owned.OriginalTargetType}::{owned.OriginalMethod.Name}@index={owned.OriginalIndex})";
        }
        if (state is not null && ReferenceEquals(state.LightingFinalizer, handler))
        {
            return $"L00CFinalizer(before={LightingAnchorType}::{LightingAnchorMethod}@index={state.LightingAnchorIndex})";
        }
        return $"{TargetType(handler)}::{handler.Method.Name}";
    }

    private static string TargetType(Delegate handler)
    {
        return handler.Target?.GetType().FullName ?? handler.Method.DeclaringType?.FullName ?? "static";
    }

    private static List<HandlerLocation> FindHandlers(IWorldGenHandler handlers, string targetType, EnumWorldGenPass? pass = null, string? methodName = null)
    {
        var matches = new List<HandlerLocation>();
        IEnumerable<EnumWorldGenPass> passes = pass.HasValue
            ? [pass.Value]
            : Enum.GetValues<EnumWorldGenPass>().Where(value =>
                (int)value >= 0 &&
                (int)value < handlers.OnChunkColumnGen.Length &&
                handlers.OnChunkColumnGen[(int)value] is not null);
        foreach (EnumWorldGenPass passValue in passes)
        {
            List<ChunkColumnGenerationDelegate> passHandlers = GetPassHandlers(handlers, passValue);
            for (int index = 0; index < passHandlers.Count; index++)
            {
                if (TargetType(passHandlers[index]) == targetType && (methodName is null || passHandlers[index].Method.Name == methodName))
                {
                    matches.Add(new HandlerLocation(passValue, index, passHandlers[index]));
                }
            }
        }
        return matches;
    }

    private static List<ChunkColumnGenerationDelegate> GetPassHandlers(IWorldGenHandler handlers, EnumWorldGenPass pass)
    {
        int index = (int)pass;
        if (index < 0 || index >= handlers.OnChunkColumnGen.Length)
        {
            throw new InvalidOperationException($"L00-C pass {pass} index {index} is outside handler array length {handlers.OnChunkColumnGen.Length}.");
        }
        return handlers.OnChunkColumnGen[index]
            ?? throw new InvalidOperationException($"L00-C pass {pass} index {index} has no registered handler list.");
    }

    private static int CountOwnedHandlers(IWorldGenHandler handlers, HandlerOwnershipState state)
    {
        return handlers.OnChunkColumnGen.Sum(items => items?.Count(candidate => IsOwnedHandler(candidate, state)) ?? 0);
    }

    private static bool IsOwnedHandler(ChunkColumnGenerationDelegate candidate, HandlerOwnershipState state)
    {
        return ReferenceEquals(candidate, state.LightingFinalizer) ||
            state.OwnedHandlers.Any(owned => ReferenceEquals(candidate, owned.Wrapper));
    }

    private void Fail(string code, string message)
    {
        RequireApi().Logger.Error($"L00C_ERROR code={code} instance={instanceId} message={Sanitize(message)}");
        throw new InvalidOperationException(message);
    }

    private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

    private void Log(string message) => RequireApi().Logger.Notification(message);

    private ICoreServerAPI RequireApi() => api ?? throw new InvalidOperationException("L00-C server API is unavailable.");

    /// <inheritdoc />
    public override void Dispose()
    {
        lock (runGate)
        {
            DisposeCore();
        }
    }

    private void DisposeCore()
    {
        Interlocked.Exchange(ref disposalStarted, 1);
        Interlocked.Increment(ref worldRunId);
        L00CProbeCallbackOwnerLease? callbacks = callbackOwnerLease;
        callbacks?.BeginClosing();
        CloseLifecycleShutdownBarrier();
        ICoreServerAPI? serverApi = api;
        Exception? disposeFailure = null;
        if (serverApi is not null)
        {
            if (callbacks is not null)
            {
                try { callbacks.Ledger.RecordReleased(L00CLifecycleRegistrationKind.InitWorldGenerator, independentlyUnregistered: false); }
                catch (Exception exception) { disposeFailure = AppendDisposeFailure(serverApi, disposeFailure, "init-world-generator-release", exception); }

                try
                {
                    serverApi.Event.GameWorldSave -= callbacks.GameWorldSave;
                    callbacks.Ledger.RecordReleased(L00CLifecycleRegistrationKind.GameWorldSave, independentlyUnregistered: true);
                }
                catch (Exception exception)
                {
                    try { callbacks.Ledger.RecordReleased(L00CLifecycleRegistrationKind.GameWorldSave, independentlyUnregistered: false); } catch { }
                    disposeFailure = AppendDisposeFailure(serverApi, disposeFailure, "game-world-save-unregister", exception);
                }

                try
                {
                    if (tickListenerId != 0) serverApi.Event.UnregisterGameTickListener(tickListenerId);
                    tickListenerId = 0;
                    callbacks.Ledger.RecordReleased(L00CLifecycleRegistrationKind.Tick, independentlyUnregistered: true);
                }
                catch (Exception exception)
                {
                    try { callbacks.Ledger.RecordReleased(L00CLifecycleRegistrationKind.Tick, independentlyUnregistered: false); } catch { }
                    disposeFailure = AppendDisposeFailure(serverApi, disposeFailure, "tick-unregister", exception);
                }

                L00CLifecycleRegistrationSnapshot registrations = callbacks.Ledger.Snapshot();
                foreach (string diagnostic in registrations.Trace) serverApi.Logger.Notification("L00C_LIFECYCLE_REGISTRATION " + diagnostic);
                if (!registrations.Complete) disposeFailure = AppendDisposeFailure(serverApi, disposeFailure, "registration-release-proof", new InvalidOperationException("L00-C lifecycle registration release proof is incomplete."));
            }

            try
            {
                CancelDelayedShutdown("dispose");
            }
            catch (Exception exception)
            {
                serverApi.Logger.Error($"L00C_DISPOSE_ERROR instance={instanceId} stage=delayed-shutdown type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
                disposeFailure = disposeFailure is null ? exception : new AggregateException(disposeFailure, exception);
            }

            try
            {
                ResetTransientLoadCallbacks("dispose");
            }
            catch (Exception exception)
            {
                serverApi.Logger.Error($"L00C_DISPOSE_ERROR instance={instanceId} stage=transient-callbacks type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
                disposeFailure = disposeFailure is null ? exception : new AggregateException(disposeFailure, exception);
            }

            try
            {
                HandlerOwnershipState? state = ownershipState;
                int forwarded = state?.OwnedHandlers.Sum(item => item.ForwardedCount) ?? 0;
                RestoreResult restored = RestoreOwnedHandlerSet("dispose");
                Log($"L00C_DISPOSED instance={instanceId} removedowned={restored.RemovedOwned} restorednative={restored.RestoredNative} exact={restored.Exact} callbacks={fixtureCallbackCount} forwarded={forwarded}");
            }
            catch (Exception exception)
            {
                serverApi.Logger.Error($"L00C_DISPOSE_ERROR instance={instanceId} stage=restore type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
                disposeFailure = disposeFailure is null ? exception : new AggregateException(disposeFailure, exception);
            }
        }

        marker = null;
        lifecycleMarker = null;
        markerPublication.Reset();
        preLightingSnapshot = null;
        initialSnapshot = null;
        initialHaloSnapshot = null;
        active = false;
        callbackOwnerLease = null;
        api = null;
        base.Dispose();

        if (disposeFailure is not null)
        {
            throw new InvalidOperationException("L00-C disposal could not restore its handler ownership exactly.", disposeFailure);
        }
    }

    private Exception AppendDisposeFailure(ICoreServerAPI serverApi,Exception? prior,string stage,Exception exception)
    {
        serverApi.Logger.Error($"L00C_DISPOSE_ERROR instance={instanceId} stage={stage} type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
        return prior is null ? exception : new AggregateException(prior, exception);
    }

    private sealed record ReplacementSpec(
        EnumWorldGenPass Pass,
        string TargetType,
        string? MethodName = null,
        bool WritesFixture = false,
        bool SuppressInHalo = false);
    private sealed record HandlerLocation(EnumWorldGenPass Pass, int Index, ChunkColumnGenerationDelegate Handler);
    private sealed record HandlerOwnershipState(
        IWorldGenHandler HandlerSet,
        Dictionary<EnumWorldGenPass, List<ChunkColumnGenerationDelegate>> OriginalInventories,
        Dictionary<EnumWorldGenPass, List<ChunkColumnGenerationDelegate>> InstalledInventories,
        List<OwnedHandler> OwnedHandlers,
        int LightingAnchorIndex,
        ChunkColumnGenerationDelegate LightingFinalizer);
    private readonly record struct RestoreResult(int RemovedOwned, int RestoredNative, bool Exact);
    private readonly record struct ChunkCoordinate(int X, int Z);
    private readonly record struct TransientColumnRequest(ChunkCoordinate Coordinate, string Role);
    private sealed record FixtureSnapshot(string Hash, int SolidCount, int FluidCount, int FreshCount, int SaltCount, int UnexpectedCount, ushort YMax);
    private sealed record HaloSnapshot(string Hash, int ColumnCount);
    private sealed record PersistedFootprintSnapshot(FixtureSnapshot Fixture, HaloSnapshot Halo);

    private void DispatchLifecycleCallback(L00CLifecycleRegistrationKind kind, Action action)
    {
        lock (runGate)
        {
            if (Volatile.Read(ref disposalStarted) != 0)
            {
                callbackOwnerLease?.Ledger.RecordIgnoredCallback(kind, "callback reached owner after disposal began and was ignored");
                return;
            }
            action();
        }
    }

    // Registered delegates retain this small trampoline, never the ModSystem.
    // Closing clears the only weak owner link before any external unregistration.
    private sealed class L00CProbeCallbackOwnerLease
    {
        private readonly object gate = new();
        private WeakReference<L00CWorldgenProbeModSystem>? owner;
        private bool closing;

        internal L00CProbeCallbackOwnerLease(L00CWorldgenProbeModSystem value)
        {
            owner = new WeakReference<L00CWorldgenProbeModSystem>(value ?? throw new ArgumentNullException(nameof(value)));
        }

        internal L00CLifecycleRegistrationLedger Ledger { get; } = new();
        internal void InitializeWorld() => Dispatch(L00CLifecycleRegistrationKind.InitWorldGenerator, static value => value.InitializeWorld());
        internal void GameWorldSave() => Dispatch(L00CLifecycleRegistrationKind.GameWorldSave, static value => value.OnGameWorldSave());
        internal void ServerTick(float deltaTime) => Dispatch(L00CLifecycleRegistrationKind.Tick, value => value.OnServerTick(deltaTime));

        internal void BeginClosing()
        {
            lock (gate)
            {
                if (closing) return;
                closing = true;
                owner = null;
                Ledger.BeginClosing();
            }
        }

        private void Dispatch(L00CLifecycleRegistrationKind kind, Action<L00CWorldgenProbeModSystem> action)
        {
            L00CWorldgenProbeModSystem? target;
            lock (gate)
            {
                if (closing || owner is null || !owner.TryGetTarget(out target))
                {
                    Ledger.RecordIgnoredCallback(kind, "stale, duplicate, collected, or closing callback did not dispatch");
                    return;
                }
            }
            target.DispatchLifecycleCallback(kind, () => action(target));
        }
    }

    private sealed class OwnedHandler
    {
        private readonly L00CWorldgenProbeModSystem owner;

        public OwnedHandler(L00CWorldgenProbeModSystem owner, long runId, ReplacementSpec specification, int originalIndex, ChunkColumnGenerationDelegate original)
        {
            this.owner = owner;
            RunId = runId;
            Specification = specification;
            Pass = specification.Pass;
            OriginalIndex = originalIndex;
            Original = original;
            OriginalTarget = original.Target;
            OriginalTargetType = TargetType(original);
            OriginalMethod = original.Method;
            Wrapper = Invoke;
        }

        public ReplacementSpec Specification { get; }
        public long RunId { get; }
        public EnumWorldGenPass Pass { get; }
        public int OriginalIndex { get; }
        public ChunkColumnGenerationDelegate Original { get; }
        public object? OriginalTarget { get; }
        public string OriginalTargetType { get; }
        public System.Reflection.MethodInfo OriginalMethod { get; }
        public ChunkColumnGenerationDelegate Wrapper { get; }
        public int ForwardedCount;
        public int ForwardLogIssued;
        public int HaloForwardLogIssued;

        private void Invoke(IChunkColumnGenerateRequest request) => owner.InvokeOwnedHandler(this, request);
    }

    private readonly record struct FixtureGeometry(int ChunkSize, int RockSurface, int WaterSurface, int DividerX)
    {
        public static FixtureGeometry Create(int chunkSize, int worldHeight)
        {
            int rockSurface = Math.Clamp(worldHeight / 4, 8, worldHeight - 8);
            int waterSurface = rockSurface + 3;
            return new FixtureGeometry(chunkSize, rockSurface, waterSurface, chunkSize / 2);
        }

        public bool IsWall(int x, int z) => x == 0 || z == 0 || x == ChunkSize - 1 || z == ChunkSize - 1 || x == DividerX;
    }
}

internal sealed class MarkerPublicationGate
{
    private ProbeMarker? candidate;
    private byte[]? committedPayload;

    public bool IsCommitted => committedPayload is not null;

    public ProbeMarker Begin(ProbeMarker source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PersistedMapFootprintSnapshot? sourceMapFootprint = source.MapFootprint;
        sourceMapFootprint?.ValidateForCopy(
            source.MarkerId,
            source.SavegameIdentifier,
            sourceMapFootprint.FixtureChunkX,
            sourceMapFootprint.FixtureChunkZ,
            sourceMapFootprint.ChunkSize,
            sourceMapFootprint.WorldHeight);
        PersistedMapFootprintSnapshot? mapFootprintCopy = sourceMapFootprint?.DeepCopy();
        Reset();
        candidate = new ProbeMarker
        {
            MarkerId = source.MarkerId,
            SavegameIdentifier = source.SavegameIdentifier,
            Version = source.Version,
            OpenCount = source.OpenCount,
            MapFootprint = mapFootprintCopy
        };
        return candidate;
    }

    public void Commit(Action<byte[]> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (candidate is null)
        {
            throw new InvalidOperationException("L00-C cannot commit a marker before creating its isolated candidate.");
        }
        if (IsCommitted)
        {
            throw new InvalidOperationException("L00-C marker candidate was already committed.");
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(candidate);
        store((byte[])payload.Clone());
        committedPayload = payload;
    }

    public bool SaveIfCommitted(Action<byte[]> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        byte[]? payload = committedPayload;
        if (payload is null)
        {
            return false;
        }

        store((byte[])payload.Clone());
        return true;
    }

    public void Reset()
    {
        BeginWorldTransition();
    }

    public void BeginWorldTransition()
    {
        candidate = null;
        committedPayload = null;
    }
}

internal sealed class ProbeMarker
{
    public string MarkerId { get; set; } = string.Empty;
    public string SavegameIdentifier { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int OpenCount { get; set; }
    public PersistedMapFootprintSnapshot? MapFootprint { get; set; }
}

internal sealed class L00CLifecycleMarker
{
    private const string Version = "l00c-lifecycle-v1";

    public string MarkerId { get; set; } = string.Empty;
    public string SavegameIdentifier { get; set; } = string.Empty;
    public string MarkerVersion { get; set; } = Version;
    public int OpenCount { get; set; }

    public static L00CLifecycleMarker Create(string savegameIdentifier, int openCount)
    {
        ValidateIdentity(savegameIdentifier, openCount);
        return new L00CLifecycleMarker
        {
            MarkerId = Guid.NewGuid().ToString("N"),
            SavegameIdentifier = savegameIdentifier,
            MarkerVersion = Version,
            OpenCount = openCount
        };
    }

    public static L00CLifecycleMarker ReadAndValidate(byte[] payload, string expectedSavegameIdentifier)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0 || payload.Length > 1024)
        {
            throw new InvalidOperationException("L00-C lifecycle marker payload is outside the bounded range.");
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("L00-C lifecycle marker root must be an object.");
            }
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!values.TryAdd(property.Name, property.Value.Clone()))
                {
                    throw new InvalidOperationException("L00-C lifecycle marker contains a duplicate property.");
                }
            }
            if (values.Count != 4 || !values.ContainsKey(nameof(MarkerId)) || !values.ContainsKey(nameof(SavegameIdentifier)) ||
                !values.ContainsKey(nameof(MarkerVersion)) || !values.ContainsKey(nameof(OpenCount)) ||
                values[nameof(MarkerId)].ValueKind != JsonValueKind.String || values[nameof(SavegameIdentifier)].ValueKind != JsonValueKind.String ||
                values[nameof(MarkerVersion)].ValueKind != JsonValueKind.String || values[nameof(OpenCount)].ValueKind != JsonValueKind.Number ||
                !values[nameof(OpenCount)].TryGetInt32(out int openCount))
            {
                throw new InvalidOperationException("L00-C lifecycle marker fields are missing, unknown, or mistyped.");
            }
            var marker = new L00CLifecycleMarker
            {
                MarkerId = values[nameof(MarkerId)].GetString() ?? string.Empty,
                SavegameIdentifier = values[nameof(SavegameIdentifier)].GetString() ?? string.Empty,
                MarkerVersion = values[nameof(MarkerVersion)].GetString() ?? string.Empty,
                OpenCount = openCount
            };
            if (marker.MarkerVersion != Version || marker.SavegameIdentifier != expectedSavegameIdentifier ||
                marker.MarkerId.Length != 32 || !marker.MarkerId.All(char.IsAsciiHexDigit) || marker.MarkerId.Any(char.IsUpper))
            {
                throw new InvalidOperationException("L00-C lifecycle marker identity is incompatible with this save.");
            }
            ValidateIdentity(marker.SavegameIdentifier, marker.OpenCount);
            return marker;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("L00-C lifecycle marker payload is corrupt or truncated.", exception);
        }
    }

    public L00CLifecycleMarker IncrementFor(string expectedSavegameIdentifier)
    {
        if (SavegameIdentifier != expectedSavegameIdentifier || MarkerVersion != Version)
        {
            throw new InvalidOperationException("L00-C lifecycle marker cannot be incremented for another save or version.");
        }
        ValidateIdentity(SavegameIdentifier, OpenCount);
        if (OpenCount != 1)
        {
            throw new InvalidOperationException("L00-C lifecycle marker permits exactly one A reopen and never a third open.");
        }
        return new L00CLifecycleMarker
        {
            MarkerId = MarkerId,
            SavegameIdentifier = SavegameIdentifier,
            MarkerVersion = Version,
            OpenCount = checked(OpenCount + 1)
        };
    }

    public byte[] Serialize()
    {
        ValidateIdentity(SavegameIdentifier, OpenCount);
        if (MarkerVersion != Version || MarkerId.Length != 32 || !MarkerId.All(value => (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')))
        {
            throw new InvalidOperationException("L00-C lifecycle marker cannot serialize an invalid identity.");
        }
        return Encoding.UTF8.GetBytes($"{{\"MarkerId\":\"{MarkerId}\",\"SavegameIdentifier\":\"{SavegameIdentifier}\",\"MarkerVersion\":\"{Version}\",\"OpenCount\":{OpenCount}}}");
    }

    private static void ValidateIdentity(string savegameIdentifier, int openCount)
    {
        if (!Guid.TryParseExact(savegameIdentifier, "D", out Guid parsed) || parsed.ToString("D") != savegameIdentifier || openCount < 1 || openCount > 2)
        {
            throw new InvalidOperationException("L00-C lifecycle marker identity is outside the bounded contract.");
        }
    }
}

internal sealed class L00CProbeConfig
{
    public bool Enabled { get; set; }
    public bool AutoRun { get; set; }
    public bool AutoShutdown { get; set; }
    public int AutoShutdownDelayMilliseconds { get; set; } = 15_000;
    public bool SpatialFixtureEnabled { get; set; }

    internal static L00CProbeConfig BindLaboratoryShutdownConfiguration(
        L00CProbeConfig loaded,
        string? laboratorySwitch,
        string? autoShutdown,
        string? delayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (!string.Equals(laboratorySwitch, "1", StringComparison.Ordinal))
        {
            return loaded;
        }

        if (!string.Equals(autoShutdown, "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("L00-C laboratory active shutdown requires ISR_L00C_AUTOSHUTDOWN=1 in the evaluated launch profile.");
        }
        if (string.IsNullOrEmpty(delayMilliseconds) ||
            !int.TryParse(delayMilliseconds, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsedDelay) ||
            !string.Equals(delayMilliseconds, parsedDelay.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("L00-C laboratory active shutdown requires a canonical ISR_L00C_AUTOSHUTDOWN_DELAY_MS value in the evaluated launch profile.");
        }

        loaded.AutoShutdown = true;
        loaded.AutoShutdownDelayMilliseconds = DelayedShutdownGate.ValidateActiveDelayMilliseconds(parsedDelay);
        return loaded;
    }
    public int FixtureChunkX { get; set; } = L00CFixtureCoordinatePolicy.DefaultLaboratoryFixtureChunk;
    public int FixtureChunkZ { get; set; } = L00CFixtureCoordinatePolicy.DefaultLaboratoryFixtureChunk;
    public string? ExpectedMissingHandlerTarget { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int RockBlockId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int FreshWaterBlockId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int SaltWaterBlockId { get; set; }
}
#endif
