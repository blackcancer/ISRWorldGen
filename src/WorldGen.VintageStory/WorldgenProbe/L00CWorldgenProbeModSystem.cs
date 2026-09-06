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
    private const string MarkerKey = "isrworldgen:l00c:marker:v1";
    private const string MarkerVersion = "l00c-flat-v1";
    private const int StableTickTarget = 40;

    private static readonly ReplacementSpec[] ReplacementSpecs =
    [
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenTerra"),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenRockStrataNew"),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenCaves"),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenDevastationLayer"),
        new(EnumWorldGenPass.Terrain, "Vintagestory.ServerMods.GenBlockLayers"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenTerraPostProcess"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenHotSprings"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenDungeons"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenDeposits"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenStructures", "OnChunkColumnGen"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenPonds"),
        new(EnumWorldGenPass.TerrainFeatures, "Vintagestory.ServerMods.GenStructures", "OnChunkColumnGenPostPass"),
        new(EnumWorldGenPass.Vegetation, "Vintagestory.GameContent.GenStoryStructures"),
        new(EnumWorldGenPass.Vegetation, "Vintagestory.ServerMods.GenVegetationAndPatches"),
        new(EnumWorldGenPass.Vegetation, "Vintagestory.ServerMods.GenRivulets"),
        new(EnumWorldGenPass.NeighbourSunLightFlood, "Vintagestory.ServerMods.GenSnowLayer")
    ];

    private readonly string instanceId = Guid.NewGuid().ToString("N");
    private readonly ChunkColumnGenerationDelegate fixtureHandler;
    private readonly ChunkColumnGenerationDelegate terrainFeaturesHandler;
    private readonly ChunkColumnGenerationDelegate vegetationHandler;
    private readonly ChunkColumnGenerationDelegate neighbourFloodHandler;
    private readonly ChunkColumnGenerationDelegate metadataFinalizerHandler;
    private readonly List<RemovedHandler> removedHandlers = [];

    private ICoreServerAPI? api;
    private IWorldGenHandler? ownedHandlerSet;
    private L00CProbeConfig config = new();
    private ProbeMarker? marker;
    private FixtureSnapshot? initialSnapshot;
    private long tickListenerId;
    private int stableTickCount;
    private int fixtureCallbackCount;
    private readonly int[] forwardedColumnCounts = new int[6];
    private readonly int[] forwardLogIssued = new int[6];
    private int requestIssued;
    private int shutdownIssued;
    private bool active;

    /// <summary>Initializes the stable delegate identity used for targeted handler ownership.</summary>
    public L00CWorldgenProbeModSystem()
    {
        fixtureHandler = GenerateFixtureColumn;
        terrainFeaturesHandler = FilterTerrainFeatures;
        vegetationHandler = FilterVegetation;
        neighbourFloodHandler = FilterNeighbourFlood;
        metadataFinalizerHandler = FinalizeFixtureMetadata;
    }

    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    /// <inheritdoc />
    public override double ExecuteOrder() => 1.0;

    /// <inheritdoc />
    public override void StartServerSide(ICoreServerAPI serverApi)
    {
        api = serverApi;
        config = serverApi.LoadModConfig<L00CProbeConfig>(ConfigFileName) ?? new L00CProbeConfig();
        serverApi.Event.InitWorldGenerator(InitializeWorld, WorldType);
        serverApi.Event.GameWorldSave += OnGameWorldSave;
        tickListenerId = serverApi.Event.RegisterGameTickListener(OnServerTick, 50);

        Log($"L00C_PROBE_READY instance={instanceId} pid={Environment.ProcessId} enabled={config.Enabled} autorun={config.AutoRun} autoshutdown={config.AutoShutdown}");
    }

    private void InitializeWorld()
    {
        ICoreServerAPI serverApi = RequireApi();
        IWorldGenHandler handlers = serverApi.Event.GetRegisteredWorldGenHandlers(WorldType)
            ?? throw new InvalidOperationException("L00-C could not obtain the standard worldgen handler set.");

        bool sameHandlerSet = ReferenceEquals(ownedHandlerSet, handlers);
        int staleProbeHandlers = ResetOwnedHandlers(handlers, sameHandlerSet);
        int staleProbeHandlersAfterReset = CountFixtureHandlers(handlers);
        if (staleProbeHandlersAfterReset != 0)
        {
            throw new InvalidOperationException($"L00-C failed to remove {staleProbeHandlersAfterReset} stale fixture handler(s) before world initialization.");
        }

        ownedHandlerSet = handlers;
        fixtureCallbackCount = 0;
        requestIssued = 0;
        shutdownIssued = 0;
        stableTickCount = 0;
        Array.Clear(forwardedColumnCounts);
        Array.Clear(forwardLogIssued);
        initialSnapshot = null;

        ISaveGame saveGame = serverApi.WorldManager.SaveGame;
        ProbeMarker? persistedMarker = ReadMarker(saveGame);
        bool activationRequested = config.Enabled;

        LogInventory("before", handlers, saveGame, staleProbeHandlers, sameHandlerSet);

        if (saveGame.IsNew)
        {
            if (!activationRequested)
            {
                active = false;
                marker = null;
                Log($"L00C_INACTIVE instance={instanceId} reason=new-world-not-enabled save={saveGame.SavegameIdentifier}");
                ScheduleProbeColumn();
                return;
            }

            marker = new ProbeMarker
            {
                MarkerId = Guid.NewGuid().ToString("N"),
                SavegameIdentifier = saveGame.SavegameIdentifier,
                Version = MarkerVersion,
                OpenCount = 1
            };
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
            ScheduleProbeColumn();
            return;
        }
        else
        {
            ValidateMarker(persistedMarker, saveGame);
            persistedMarker.OpenCount++;
            marker = persistedMarker;
        }

        ValidateReplacementPreconditions(handlers);
        ResolveMaterials();
        ApplyTargetedReplacement(handlers);
        active = true;
        StoreMarker(saveGame, marker!);

        LogInventory("after", handlers, saveGame, staleProbeHandlers, sameHandlerSet);
        Log($"L00C_ACTIVATED instance={instanceId} marker={marker!.MarkerId} open={marker.OpenCount} isnew={saveGame.IsNew} save={saveGame.SavegameIdentifier} chunk=({config.FixtureChunkX},{config.FixtureChunkZ})");
        ScheduleProbeColumn();
    }

    private int ResetOwnedHandlers(IWorldGenHandler handlers, bool sameHandlerSet)
    {
        int removedProbeCount = 0;
        foreach (List<ChunkColumnGenerationDelegate>? passHandlers in handlers.OnChunkColumnGen)
        {
            if (passHandlers is null)
            {
                continue;
            }
            for (int index = passHandlers.Count - 1; index >= 0; index--)
            {
                ChunkColumnGenerationDelegate candidate = passHandlers[index];
                if (IsOwnedProxy(candidate))
                {
                    passHandlers.RemoveAt(index);
                    removedProbeCount++;
                }
            }
        }

        if (sameHandlerSet)
        {
            foreach (IGrouping<EnumWorldGenPass, RemovedHandler> group in removedHandlers.GroupBy(entry => entry.Pass))
            {
                List<ChunkColumnGenerationDelegate> passHandlers = GetPassHandlers(handlers, group.Key);
                foreach (RemovedHandler entry in group.OrderBy(item => item.Index))
                {
                    bool present = passHandlers.Any(candidate => ReferenceEquals(candidate, entry.Handler));
                    if (!present)
                    {
                        int insertionIndex = Math.Clamp(entry.Index, 0, passHandlers.Count);
                        passHandlers.Insert(insertionIndex, entry.Handler);
                    }
                }
            }
        }

        removedHandlers.Clear();
        return removedProbeCount;
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
    }

    private void ApplyTargetedReplacement(IWorldGenHandler handlers)
    {
        var unaffectedBefore = new Dictionary<EnumWorldGenPass, List<ChunkColumnGenerationDelegate>>();
        foreach (ReplacementSpec spec in ReplacementSpecs)
        {
            List<ChunkColumnGenerationDelegate> passHandlers = GetPassHandlers(handlers, spec.Pass);
            if (!unaffectedBefore.ContainsKey(spec.Pass))
            {
                unaffectedBefore[spec.Pass] = passHandlers
                    .Where(candidate => !ReplacementSpecs.Any(entry => entry.Pass == spec.Pass && Matches(candidate, entry)))
                    .ToList();
            }

            HandlerLocation location = FindHandlers(handlers, spec.TargetType, spec.Pass, spec.MethodName).Single();
            removedHandlers.Add(new RemovedHandler(spec.Pass, location.Index, location.Handler));
        }

        foreach (IGrouping<EnumWorldGenPass, RemovedHandler> group in removedHandlers.GroupBy(entry => entry.Pass))
        {
            List<ChunkColumnGenerationDelegate> passHandlers = GetPassHandlers(handlers, group.Key);
            foreach (RemovedHandler entry in group.OrderByDescending(item => item.Index))
            {
                if (!ReferenceEquals(passHandlers[entry.Index], entry.Handler))
                {
                    throw new InvalidOperationException("L00-C handler inventory changed between validation and targeted removal.");
                }
                passHandlers.RemoveAt(entry.Index);
            }
        }

        foreach (IGrouping<EnumWorldGenPass, RemovedHandler> group in removedHandlers.GroupBy(entry => entry.Pass))
        {
            List<ChunkColumnGenerationDelegate> passHandlers = GetPassHandlers(handlers, group.Key);
            int insertionIndex = group.Min(item => item.Index);
            passHandlers.Insert(Math.Clamp(insertionIndex, 0, passHandlers.Count), ProxyForPass(group.Key));
        }

        List<ChunkColumnGenerationDelegate> preDoneHandlers = GetPassHandlers(handlers, EnumWorldGenPass.PreDone);
        unaffectedBefore[EnumWorldGenPass.PreDone] = preDoneHandlers.ToList();
        preDoneHandlers.Add(metadataFinalizerHandler);

        foreach (KeyValuePair<EnumWorldGenPass, List<ChunkColumnGenerationDelegate>> entry in unaffectedBefore)
        {
            List<ChunkColumnGenerationDelegate> actual = GetPassHandlers(handlers, entry.Key)
                .Where(candidate => !IsOwnedProxy(candidate))
                .ToList();
            if (actual.Count != entry.Value.Count || actual.Where((candidate, index) => !ReferenceEquals(candidate, entry.Value[index])).Any())
            {
                throw new InvalidOperationException($"L00-C detected a third-party handler change in pass {entry.Key}; replacement aborted.");
            }
        }
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

    private void ScheduleProbeColumn()
    {
        if (!config.AutoRun)
        {
            Log($"L00C_AUTORUN_SKIPPED instance={instanceId} active={active}");
            return;
        }

        RequireApi().Event.ServerRunPhase(EnumServerRunPhase.RunGame, RequestProbeColumn);
    }

    private void RequestProbeColumn()
    {
        if (Interlocked.Exchange(ref requestIssued, 1) != 0)
        {
            return;
        }

        ICoreServerAPI serverApi = RequireApi();
        ValidateFixtureCoordinate(serverApi);
        Log($"L00C_COLUMN_REQUEST instance={instanceId} active={active} chunk=({config.FixtureChunkX},{config.FixtureChunkZ})");
        serverApi.WorldManager.LoadChunkColumnPriority(
            config.FixtureChunkX,
            config.FixtureChunkZ,
            new ChunkLoadOptions
            {
                KeepLoaded = false,
                OnLoaded = OnProbeColumnLoaded
            });
    }

    private void ValidateFixtureCoordinate(ICoreServerAPI serverApi)
    {
        int maxChunkX = serverApi.WorldManager.MapSizeX / serverApi.WorldManager.ChunkSize;
        int maxChunkZ = serverApi.WorldManager.MapSizeZ / serverApi.WorldManager.ChunkSize;
        if (config.FixtureChunkX < 1 || config.FixtureChunkX >= maxChunkX - 1 || config.FixtureChunkZ < 1 || config.FixtureChunkZ >= maxChunkZ - 1)
        {
            throw new InvalidOperationException($"L00-C fixture chunk ({config.FixtureChunkX},{config.FixtureChunkZ}) is outside the bounded interior map area ({maxChunkX},{maxChunkZ}).");
        }
    }

    private void GenerateFixtureColumn(IChunkColumnGenerateRequest request)
    {
        if (!active)
        {
            throw new InvalidOperationException("L00-C fixture handler ran while the world was inactive.");
        }
        if (request.ChunkX != config.FixtureChunkX || request.ChunkZ != config.FixtureChunkZ)
        {
            ForwardNative(EnumWorldGenPass.Terrain, request);
            return;
        }
        if (Interlocked.Increment(ref fixtureCallbackCount) != 1)
        {
            throw new InvalidOperationException("L00-C fixture handler ran more than once for the bounded column.");
        }

        WriteCanonicalFixture(request, "terrain");
    }

    private void FinalizeFixtureMetadata(IChunkColumnGenerateRequest request)
    {
        if (request.ChunkX != config.FixtureChunkX || request.ChunkZ != config.FixtureChunkZ)
        {
            return;
        }

        WriteCanonicalFixture(request, "predone");
    }

    private void WriteCanonicalFixture(IChunkColumnGenerateRequest request, string phase)
    {
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
        mapChunk.MarkDirty();

        Log($"L00C_FIXTURE_WRITTEN instance={instanceId} marker={marker!.MarkerId} phase={phase} chunk=({request.ChunkX},{request.ChunkZ}) chunksize={chunkSize} worldheight={worldHeight} rock={config.RockBlockId} fresh={config.FreshWaterBlockId} salt={config.SaltWaterBlockId} rocksurface={geometry.RockSurface} watersurface={geometry.WaterSurface} thread={Environment.CurrentManagedThreadId}");
    }

    private void FilterTerrainFeatures(IChunkColumnGenerateRequest request)
    {
        FilterFixturePass(EnumWorldGenPass.TerrainFeatures, request);
    }

    private void FilterVegetation(IChunkColumnGenerateRequest request)
    {
        FilterFixturePass(EnumWorldGenPass.Vegetation, request);
    }

    private void FilterNeighbourFlood(IChunkColumnGenerateRequest request)
    {
        FilterFixturePass(EnumWorldGenPass.NeighbourSunLightFlood, request);
    }

    private void FilterFixturePass(EnumWorldGenPass pass, IChunkColumnGenerateRequest request)
    {
        if (request.ChunkX != config.FixtureChunkX || request.ChunkZ != config.FixtureChunkZ)
        {
            ForwardNative(pass, request);
            return;
        }

        Log($"L00C_PASS_SUPPRESSED instance={instanceId} marker={marker!.MarkerId} pass={pass} chunk=({request.ChunkX},{request.ChunkZ}) delegates={removedHandlers.Count(item => item.Pass == pass)}");
    }

    private void ForwardNative(EnumWorldGenPass pass, IChunkColumnGenerateRequest request)
    {
        foreach (RemovedHandler entry in removedHandlers.Where(item => item.Pass == pass).OrderBy(item => item.Index))
        {
            entry.Handler(request);
        }

        int passIndex = (int)pass;
        Interlocked.Increment(ref forwardedColumnCounts[passIndex]);
        if (Interlocked.Exchange(ref forwardLogIssued[passIndex], 1) == 0)
        {
            Log($"L00C_NATIVE_FORWARD instance={instanceId} marker={marker!.MarkerId} pass={pass} firstchunk=({request.ChunkX},{request.ChunkZ}) delegates={removedHandlers.Count(item => item.Pass == pass)}");
        }
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

    private void OnProbeColumnLoaded()
    {
        try
        {
            if (active)
            {
                initialSnapshot = InspectFixture("loaded");
                stableTickCount = 0;
            }
            else
            {
                FixtureSnapshot witness = InspectWitness();
                Log($"L00C_WITNESS_LOADED instance={instanceId} chunk=({config.FixtureChunkX},{config.FixtureChunkZ}) snapshot={witness.Hash} solids={witness.SolidCount} fluids={witness.FluidCount} ymax={witness.YMax}");
                RequestShutdownIfConfigured("inactive-witness-complete");
            }
        }
        catch (Exception exception)
        {
            RequireApi().Logger.Error($"L00C_VALIDATION_ERROR instance={instanceId} type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
            RequestShutdownIfConfigured("validation-error");
            throw;
        }
    }

    private void OnServerTick(float deltaTime)
    {
        if (!active || initialSnapshot is null)
        {
            return;
        }

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

        Log($"L00C_TICKS_STABLE instance={instanceId} marker={marker!.MarkerId} ticks={StableTickTarget} snapshot={afterTicks.Hash} fluids={afterTicks.FluidCount} fresh={afterTicks.FreshCount} salt={afterTicks.SaltCount} unexpected={afterTicks.UnexpectedCount}");
        initialSnapshot = null;
        RequestShutdownIfConfigured("fixture-stable");
    }

    private FixtureSnapshot InspectFixture(string phase)
    {
        ICoreServerAPI serverApi = RequireApi();
        int chunkSize = serverApi.WorldManager.ChunkSize;
        int worldHeight = serverApi.WorldManager.MapSizeY;
        FixtureGeometry geometry = FixtureGeometry.Create(chunkSize, worldHeight);
        IMapChunk mapChunk = serverApi.WorldManager.GetMapChunk(config.FixtureChunkX, config.FixtureChunkZ)
            ?? throw new InvalidOperationException("L00-C fixture map chunk is not loaded.");

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
                    IServerChunk chunk = serverApi.WorldManager.GetChunk(config.FixtureChunkX, y / chunkSize, config.FixtureChunkZ)
                        ?? throw new InvalidOperationException($"L00-C fixture chunk y={y / chunkSize} is not loaded.");
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

    private FixtureSnapshot InspectWitness()
    {
        ICoreServerAPI serverApi = RequireApi();
        int chunkSize = serverApi.WorldManager.ChunkSize;
        int worldHeight = serverApi.WorldManager.MapSizeY;
        IMapChunk mapChunk = serverApi.WorldManager.GetMapChunk(config.FixtureChunkX, config.FixtureChunkZ)
            ?? throw new InvalidOperationException("L00-C witness map chunk is not loaded.");
        int solidCount = 0;
        int fluidCount = 0;
        int sampleYLimit = Math.Min(worldHeight, mapChunk.YMax + 1);

        for (int y = 0; y < sampleYLimit; y++)
        {
            IServerChunk chunk = serverApi.WorldManager.GetChunk(config.FixtureChunkX, y / chunkSize, config.FixtureChunkZ)
                ?? throw new InvalidOperationException($"L00-C witness chunk y={y / chunkSize} is not loaded.");
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    int index3d = MapUtil.Index3d(x, y % chunkSize, z, chunkSize, chunkSize);
                    if (chunk.Data.GetBlockId(index3d, BlockLayersAccess.Solid) != 0) solidCount++;
                    if (chunk.Data.GetBlockId(index3d, BlockLayersAccess.Fluid) != 0) fluidCount++;
                }
            }
        }

        string canonical = $"{chunkSize}|{worldHeight}|{solidCount}|{fluidCount}|{mapChunk.YMax}|{mapChunk.WorldGenTerrainHeightMap[0]}|{mapChunk.RainHeightMap[0]}|{mapChunk.TopRockIdMap[0]}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new FixtureSnapshot(hash, solidCount, fluidCount, 0, 0, 0, mapChunk.YMax);
    }

    private void RequestShutdownIfConfigured(string reason)
    {
        if (!config.AutoShutdown || Interlocked.Exchange(ref shutdownIssued, 1) != 0)
        {
            return;
        }

        Log($"L00C_GRACEFUL_SHUTDOWN_REQUEST instance={instanceId} marker={marker?.MarkerId ?? "none"} reason={reason}");
        RequireApi().Server.ShutDown();
    }

    private void OnGameWorldSave()
    {
        if (marker is not null)
        {
            StoreMarker(RequireApi().WorldManager.SaveGame, marker);
            Log($"L00C_MARKER_SAVED instance={instanceId} marker={marker.MarkerId} open={marker.OpenCount}");
        }
    }

    private static ProbeMarker? ReadMarker(ISaveGame saveGame)
    {
        byte[]? bytes = saveGame.GetData(MarkerKey);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProbeMarker>(bytes)
                ?? throw new InvalidOperationException("marker payload deserialized to null");
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

    private static void StoreMarker(ISaveGame saveGame, ProbeMarker marker)
    {
        saveGame.StoreData(MarkerKey, JsonSerializer.SerializeToUtf8Bytes(marker));
    }

    private void LogInventory(string phase, IWorldGenHandler handlers, ISaveGame saveGame, int staleProbeHandlers, bool sameHandlerSet)
    {
        string mapRegion = DescribeHandlers(handlers.OnMapRegionGen.Cast<Delegate>());
        string mapChunk = DescribeHandlers(handlers.OnMapChunkGen.Cast<Delegate>());
        string column = string.Join(";", handlers.OnChunkColumnGen.Select((items, index) => items is null ? $"{index}=[null]" : $"{index}=[{DescribeHandlers(items.Cast<Delegate>())}]"));
        Log($"L00C_HANDLERS phase={phase} instance={instanceId} save={saveGame.SavegameIdentifier} isnew={saveGame.IsNew} samehandlerset={sameHandlerSet} staleprobe={staleProbeHandlers} mapregion=[{mapRegion}] mapchunk=[{mapChunk}] column={column}");
    }

    private static string DescribeHandlers(IEnumerable<Delegate> handlers)
    {
        return string.Join(",", handlers.Select((handler, index) => $"{index}:{TargetType(handler)}::{handler.Method.Name}"));
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

    private int CountFixtureHandlers(IWorldGenHandler handlers)
    {
        return handlers.OnChunkColumnGen.Sum(items => items?.Count(IsOwnedProxy) ?? 0);
    }

    private ChunkColumnGenerationDelegate ProxyForPass(EnumWorldGenPass pass) => pass switch
    {
        EnumWorldGenPass.Terrain => fixtureHandler,
        EnumWorldGenPass.TerrainFeatures => terrainFeaturesHandler,
        EnumWorldGenPass.Vegetation => vegetationHandler,
        EnumWorldGenPass.NeighbourSunLightFlood => neighbourFloodHandler,
        _ => throw new InvalidOperationException($"L00-C has no targeted proxy for pass {pass}.")
    };

    private bool IsOwnedProxy(ChunkColumnGenerationDelegate candidate)
    {
        return ReferenceEquals(candidate.Target, this) &&
            (candidate.Method == fixtureHandler.Method ||
             candidate.Method == terrainFeaturesHandler.Method ||
             candidate.Method == vegetationHandler.Method ||
             candidate.Method == neighbourFloodHandler.Method ||
             candidate.Method == metadataFinalizerHandler.Method);
    }

    private static bool Matches(ChunkColumnGenerationDelegate candidate, ReplacementSpec spec)
    {
        return TargetType(candidate) == spec.TargetType && (spec.MethodName is null || candidate.Method.Name == spec.MethodName);
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
        if (api is not null)
        {
            try
            {
                api.Event.GameWorldSave -= OnGameWorldSave;
                if (tickListenerId != 0)
                {
                    api.Event.UnregisterGameTickListener(tickListenerId);
                    tickListenerId = 0;
                }
                if (ownedHandlerSet is not null)
                {
                    int nativeToRestore = removedHandlers.Count;
                    int removed = ResetOwnedHandlers(ownedHandlerSet, true);
                    Log($"L00C_DISPOSED instance={instanceId} removedprobe={removed} restorednative={nativeToRestore} callbacks={fixtureCallbackCount} forwarded={forwardedColumnCounts.Sum()}");
                }
            }
            catch (Exception exception)
            {
                api.Logger.Error($"L00C_DISPOSE_ERROR instance={instanceId} type={exception.GetType().FullName} message={Sanitize(exception.Message)}");
            }
        }

        ownedHandlerSet = null;
        removedHandlers.Clear();
        marker = null;
        initialSnapshot = null;
        active = false;
        api = null;
        base.Dispose();
    }

    private sealed record ReplacementSpec(EnumWorldGenPass Pass, string TargetType, string? MethodName = null);
    private sealed record HandlerLocation(EnumWorldGenPass Pass, int Index, ChunkColumnGenerationDelegate Handler);
    private sealed record RemovedHandler(EnumWorldGenPass Pass, int Index, ChunkColumnGenerationDelegate Handler);
    private sealed record FixtureSnapshot(string Hash, int SolidCount, int FluidCount, int FreshCount, int SaltCount, int UnexpectedCount, ushort YMax);

    private sealed class ProbeMarker
    {
        public string MarkerId { get; set; } = string.Empty;
        public string SavegameIdentifier { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int OpenCount { get; set; }
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

internal sealed class L00CProbeConfig
{
    public bool Enabled { get; set; }
    public bool AutoRun { get; set; }
    public bool AutoShutdown { get; set; }
    public int FixtureChunkX { get; set; } = 31990;
    public int FixtureChunkZ { get; set; } = 31990;
    public string? ExpectedMissingHandlerTarget { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int RockBlockId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int FreshWaterBlockId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int SaltWaterBlockId { get; set; }
}
#endif
