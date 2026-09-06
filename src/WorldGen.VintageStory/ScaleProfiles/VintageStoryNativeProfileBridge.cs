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

    void LogInactive();

    void LogRejected(NativeProfileError error, NativeWorldSnapshot? world);

    void LogFrozenGate(string profileId);

    void ShutDown();
}

internal sealed class VintageStoryNativeProfileBridge
{
    private readonly INativeProfileHost host;
    private readonly NativeProfileCoordinator coordinator = new();
    private NativeWorldSnapshot? capturedWorld;

    internal VintageStoryNativeProfileBridge(INativeProfileHost host) =>
        this.host = host ?? throw new ArgumentNullException(nameof(host));

    internal NativeProfilePreparation? LastPreparation { get; private set; }

    internal NativeWorldgenGateObservation Gate => coordinator.ObserveWorldgenGate();

    internal void Register() => host.RegisterGameReady(OnGameReady);

    private void OnGameReady()
    {
        try
        {
            NativeProfileSelection selection = host.ReadSelection();
            IFrozenProfileStore store = host.CreateStore();
            if (!selection.IsSpecified && store.Read() is null)
            {
                LastPreparation = coordinator.DeactivateUnmarked();
                TryLogInactive();
                return;
            }

            NativeWorldSnapshot world = host.CaptureWorld();
            capturedWorld = world;
            NativeProfilePreparation preparation = coordinator.Prepare(
                world,
                selection,
                store,
                host.CaptureWorld,
                () => host.RegisterWorldgenGate(OnWorldgenGate));
            LastPreparation = preparation;
            if (preparation.State == NativeProfileState.Frozen)
            {
                TryLogFrozen(world, preparation.Profile!.Id);
                return;
            }

            if (preparation.State == NativeProfileState.Inactive)
            {
                TryLogInactive();
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
            try
            {
                host.LogFrozenGate(gate.ProfileId!);
            }
            catch (Exception)
            {
                // Logging cannot be allowed to turn a successfully frozen gate into
                // an exception swallowed by Vintage Story's worldgen dispatcher.
            }

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
            host.LogRejected(error, capturedWorld);
        }
        finally
        {
            host.ShutDown();
        }
    }

    private void TryLogFrozen(NativeWorldSnapshot world, string profileId)
    {
        try
        {
            host.LogFrozen(world, profileId);
        }
        catch (Exception)
        {
            // The committed profile and registered gate are authoritative. A
            // logger failure must not convert them into a persisted rejection.
        }
    }

    private void TryLogInactive()
    {
        try
        {
            host.LogInactive();
        }
        catch (Exception)
        {
            // A non-participating world remains non-participating even if its
            // informational log sink is unavailable.
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

    public void LogInactive() => logger.Notification(
        "L02C_NATIVE_PROFILE_INACTIVE reason=no-explicit-selection-and-no-envelope");

    public void LogRejected(NativeProfileError error, NativeWorldSnapshot? world) => logger.Error(
        "L02C_NATIVE_PROFILE_REJECTED code={0} stage={1} details={2} dimensions={3} chunk={4} rules={5}",
        error.Code,
        error.Stage,
        SanitizeDiagnosticDetail(error.Details),
        world is null ? "unavailable" : $"{world.MapSizeX}x{world.MapSizeY}x{world.MapSizeZ}",
        world?.ChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable",
        world is null ? "unavailable" : $"{world.NativeRuleSetId}:{world.NativeRuleSetVersion}");

    public void LogFrozenGate(string profileId) => logger.Notification(
        "L02C_NATIVE_GATE_FROZEN profile={0}",
        profileId);

    public void ShutDown() => api.Server.ShutDown();

    internal static string SanitizeDiagnosticDetail(string details)
    {
        const int maximumLength = 256;
        int length = Math.Min(details.Length, maximumLength);
        char[] sanitized = new char[length];
        for (int index = 0; index < length; index++)
        {
            char character = details[index];
            sanitized[index] = char.IsLetterOrDigit(character) ||
                character is ' ' or '.' or ',' or '_' or '-' or '=' or '(' or ')' or '[' or ']'
                ? character
                : '_';
        }

        return new string(sanitized);
    }
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
