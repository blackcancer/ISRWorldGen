using ISRWorldGen.Core.Contracts;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ISRWorldGen.ScaleProfiles;

internal interface INativeProfileHost
{
    void RegisterGameReady(Action callback);

    void RegisterWorldgenGate(Action callback);

    NativeWorldSnapshot CaptureWorld();

    NativeProfileSelection ReadSelection();

    IFrozenProfileStore CreateStore();

    void LogFrozen(NativeWorldSnapshot world, string profileId);

    void LogInactive(NativeWorldSnapshot world);

    void LogRejected(NativeProfileError error);

    void LogFrozenGate(string profileId);

    void ShutDown();
}

internal sealed class VintageStoryNativeProfileBridge
{
    private readonly INativeProfileHost host;
    private readonly NativeProfileCoordinator coordinator = new();

    internal VintageStoryNativeProfileBridge(INativeProfileHost host) =>
        this.host = host ?? throw new ArgumentNullException(nameof(host));

    internal NativeProfilePreparation? LastPreparation { get; private set; }

    internal NativeWorldgenGateObservation Gate => coordinator.ObserveWorldgenGate();

    internal void Register() => host.RegisterGameReady(OnGameReady);

    private void OnGameReady()
    {
        try
        {
            NativeWorldSnapshot world = host.CaptureWorld();
            NativeProfilePreparation preparation = coordinator.Prepare(
                world,
                host.ReadSelection(),
                host.CreateStore(),
                host.CaptureWorld);
            LastPreparation = preparation;
            if (preparation.State == NativeProfileState.Frozen)
            {
                host.LogFrozen(world, preparation.Profile!.Id);
                host.RegisterWorldgenGate(OnWorldgenGate);
                return;
            }

            if (preparation.State == NativeProfileState.Inactive)
            {
                host.LogInactive(world);
                return;
            }

            RejectAndStop(preparation.Error!);
        }
        catch (Exception exception)
        {
            var error = new NativeProfileError(
                GenerationFailureCode.InvalidInput,
                "native-profile.game-ready",
                $"Native profile GameReady preparation failed: {exception.Message}");
            LastPreparation = coordinator.FailClosed(error);
            RejectAndStop(error);
        }
    }

    private void OnWorldgenGate()
    {
        NativeWorldgenGateObservation gate = coordinator.ObserveWorldgenGate();
        if (gate.CanGenerate)
        {
            host.LogFrozenGate(gate.ProfileId!);
            return;
        }

        RejectAndStop(new NativeProfileError(
            GenerationFailureCode.InvalidInput,
            "native-profile.worldgen-gate",
            $"World generation gate observed state {gate.State} instead of Frozen."));
    }

    private void RejectAndStop(NativeProfileError error)
    {
        try
        {
            host.LogRejected(error);
        }
        finally
        {
            host.ShutDown();
        }
    }
}

internal sealed class VintageStoryNativeProfileHost : INativeProfileHost
{
    internal const string SelectionConfigKey = "isrworldgenProfileId";
    internal const string NativeRuleSetId = "vintagestory-1.22.7-effective-world-v1";
    internal const uint NativeRuleSetVersion = 1;
    private static readonly Version AuditedApiVersion = new(1, 22, 7, 0);
    private readonly ICoreServerAPI api;
    private readonly ILogger logger;

    internal VintageStoryNativeProfileHost(ICoreServerAPI api, ILogger logger)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterGameReady(Action callback) =>
        api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, callback);

    public void RegisterWorldgenGate(Action callback) =>
        api.Event.InitWorldGenerator(callback, api.WorldManager.SaveGame.WorldType);

    public NativeWorldSnapshot CaptureWorld()
    {
        Version? actualVersion = typeof(ICoreServerAPI).Assembly.GetName().Version;
        if (actualVersion != AuditedApiVersion)
        {
            throw new InvalidOperationException(
                $"VintagestoryAPI assembly {actualVersion} is outside audited version {AuditedApiVersion}.");
        }

        IWorldManagerAPI worldManager = api.WorldManager;
        ISaveGame saveGame = worldManager.SaveGame;
        return new NativeWorldSnapshot(
            saveGame.SavegameIdentifier,
            saveGame.IsNew,
            worldManager.MapSizeX,
            worldManager.MapSizeY,
            worldManager.MapSizeZ,
            worldManager.ChunkSize,
            GlobalConstants.MaxWorldSizeXZ,
            GlobalConstants.MaxWorldSizeY,
            NativeRuleSetId,
            NativeRuleSetVersion);
    }

    public NativeProfileSelection ReadSelection()
    {
        if (!api.World.Config.HasAttribute(SelectionConfigKey))
        {
            return new NativeProfileSelection(isSpecified: false, profileId: null);
        }

        return new NativeProfileSelection(
            isSpecified: true,
            api.World.Config.GetString(SelectionConfigKey, string.Empty));
    }

    public IFrozenProfileStore CreateStore() => new VintageStoryFrozenProfileStore(api.WorldManager.SaveGame);

    public void LogFrozen(NativeWorldSnapshot world, string profileId) => logger.Notification(
        "L02C_NATIVE_PROFILE_FROZEN save={0} profile={1} x={2} y={3} z={4} chunk={5} rules={6}:{7}",
        world.SavegameIdentifier,
        profileId,
        world.MapSizeX,
        world.MapSizeY,
        world.MapSizeZ,
        world.ChunkSize,
        world.NativeRuleSetId,
        world.NativeRuleSetVersion);

    public void LogInactive(NativeWorldSnapshot world) => logger.Notification(
        "L02C_NATIVE_PROFILE_INACTIVE save={0} reason=no-explicit-selection",
        world.SavegameIdentifier);

    public void LogRejected(NativeProfileError error) => logger.Error(
        "L02C_NATIVE_PROFILE_REJECTED code={0} stage={1}",
        error.Code,
        error.Stage);

    public void LogFrozenGate(string profileId) => logger.Notification(
        "L02C_NATIVE_GATE_FROZEN profile={0}",
        profileId);

    public void ShutDown() => api.Server.ShutDown();
}

internal sealed class VintageStoryFrozenProfileStore : IFrozenProfileStore
{
    internal const string StorageKey = "isrworldgen:l02c:frozen-profile:v1";
    private readonly ISaveGame saveGame;

    internal VintageStoryFrozenProfileStore(ISaveGame saveGame) =>
        this.saveGame = saveGame ?? throw new ArgumentNullException(nameof(saveGame));

    public byte[]? Read() => saveGame.GetData(StorageKey)?.ToArray();

    public void Write(ReadOnlySpan<byte> content) => saveGame.StoreData(StorageKey, content.ToArray());
}
