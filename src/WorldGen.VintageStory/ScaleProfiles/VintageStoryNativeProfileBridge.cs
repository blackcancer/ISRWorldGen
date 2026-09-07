using ISRWorldGen.Core.Contracts;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ISRWorldGen.ScaleProfiles;

internal interface INativeProfileHost
{
    void RegisterGameReady(Action callback);

    void RegisterWorldgenGate(Action callback);

    NativeWorldSnapshot CaptureWorld();

    NativeProfileSelection ReadSelection();

    IFrozenProfileStore CreateStore();

    void LogFrozen(
        NativeWorldSnapshot world,
        NativeProfilePreparation preparation,
        NativeWorldgenGateObservation gate,
        bool publishedProfile);

    void LogInactive();

    void LogRejected(
        NativeProfileError error,
        NativeWorldSnapshot? world,
        NativeProfilePreparation preparation,
        NativeWorldgenGateObservation gate);

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
                TryLogFrozen(world, preparation);
                return;
            }

            if (preparation.State == NativeProfileState.Inactive)
            {
                TryLogInactive();
                return;
            }

            RejectAndStop(preparation);
        }
        catch (Exception exception)
        {
            var error = new NativeProfileError(
                GenerationFailureCode.InvalidInput,
                "native-profile.game-ready",
                $"Native profile GameReady preparation failed ({exception.GetType().Name}).");
            LastPreparation = coordinator.FailClosed(error);
            RejectAndStop(LastPreparation);
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

        NativeProfilePreparation rejection = coordinator.FailClosed(new NativeProfileError(
            GenerationFailureCode.InvalidInput,
            "native-profile.worldgen-gate",
            $"World generation gate observed state {gate.State} instead of Frozen."));
        LastPreparation = rejection;
        RejectAndStop(rejection);
    }

    private void RejectAndStop(NativeProfilePreparation preparation)
    {
        try
        {
            host.LogRejected(
                preparation.Error!,
                capturedWorld,
                preparation,
                coordinator.ObserveWorldgenGate());
        }
        finally
        {
            host.ShutDown();
        }
    }

    private void TryLogFrozen(NativeWorldSnapshot world, NativeProfilePreparation preparation)
    {
        try
        {
            host.LogFrozen(
                world,
                preparation,
                coordinator.ObserveWorldgenGate(),
                coordinator.PublishedProfile is not null);
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

    public NativeProfileSelection ReadSelection() => ReadSelection(api.World.Config);

    internal static NativeProfileSelection ReadSelection(ITreeAttribute config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.HasAttribute(SelectionConfigKey))
        {
            return new NativeProfileSelection(isSpecified: false, profileId: null);
        }

        string profileId = config.GetString(SelectionConfigKey, string.Empty);
        return string.IsNullOrWhiteSpace(profileId)
            ? new NativeProfileSelection(isSpecified: false, profileId: null)
            : new NativeProfileSelection(isSpecified: true, profileId);
    }

    public IFrozenProfileStore CreateStore() => new VintageStoryFrozenProfileStore(api.WorldManager.SaveGame);

    public void LogFrozen(
        NativeWorldSnapshot world,
        NativeProfilePreparation preparation,
        NativeWorldgenGateObservation gate,
        bool publishedProfile) => logger.Notification(
            "{0}",
            CreateFrozenDiagnostic(world, preparation, gate, publishedProfile));

    public void LogInactive() => logger.Notification(
        "L02C_NATIVE_PROFILE_INACTIVE reason=no-explicit-selection-and-no-envelope");

    public void LogRejected(
        NativeProfileError error,
        NativeWorldSnapshot? world,
        NativeProfilePreparation preparation,
        NativeWorldgenGateObservation gate) => logger.Error(
            "{0}",
            CreateRejectedDiagnostic(error, world, preparation, gate));

    public void LogFrozenGate(string profileId) => logger.Notification(
        "L02C_NATIVE_GATE_FROZEN profile={0}",
        profileId);

    public void ShutDown() => api.Server.ShutDown();

    internal static string CreateFrozenDiagnostic(
        NativeWorldSnapshot world,
        NativeProfilePreparation preparation,
        NativeWorldgenGateObservation gate,
        bool publishedProfile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(gate);
        if (preparation.State != NativeProfileState.Frozen ||
            preparation.Profile is null ||
            preparation.Error is not null ||
            preparation.Evidence.EnvelopeBytes == 0 ||
            gate.State != NativeProfileState.Frozen ||
            !gate.CanGenerate ||
            !publishedProfile)
        {
            throw new ArgumentException("Frozen diagnostic requires a published Frozen profile and generating gate.");
        }

        string profileId = preparation.Profile.Id;
        NativeProfilePersistenceEvidence evidence = preparation.Evidence;
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "L02C_NATIVE_PROFILE_FROZEN profile={0} source={1} persistencewrites={2} envelopebytes={3} envelopesha256={4} gatestate={5} gatecangenerate={6} gatecallbackregistered={7} publishedprofile={8} dimensions={9}x{10}x{11} chunk={12} rules={13}:{14}",
            profileId,
            evidence.SourceToken,
            evidence.PersistenceWrites,
            evidence.EnvelopeBytes,
            evidence.EnvelopeSha256Token,
            gate.State,
            BooleanToken(gate.CanGenerate),
            BooleanToken(evidence.GateCallbackRegistered),
            BooleanToken(publishedProfile),
            world.MapSizeX,
            world.MapSizeY,
            world.MapSizeZ,
            world.ChunkSize,
            world.NativeRuleSetId,
            world.NativeRuleSetVersion);
    }

    internal static string CreateRejectedDiagnostic(
        NativeProfileError error,
        NativeWorldSnapshot? world,
        NativeProfilePreparation preparation,
        NativeWorldgenGateObservation gate)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(gate);
        if (preparation.State != NativeProfileState.Rejected ||
            preparation.Profile is not null ||
            preparation.Error is null ||
            preparation.Error != error ||
            gate.State != NativeProfileState.Rejected ||
            gate.CanGenerate)
        {
            throw new ArgumentException("Rejected diagnostic requires a rejected preparation and closed gate.");
        }

        NativeProfilePersistenceEvidence evidence = preparation.Evidence;
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "L02C_NATIVE_PROFILE_REJECTED code={0} stage={1} source={2} persistencewrites={3} envelopebytes={4} envelopesha256={5} gatestate={6} gatecangenerate={7} gatecallbackregistered={8} details={9} dimensions={10} chunk={11} rules={12}",
            error.Code,
            error.Stage,
            evidence.SourceToken,
            evidence.PersistenceWrites,
            evidence.EnvelopeBytes,
            evidence.EnvelopeSha256Token,
            gate.State,
            BooleanToken(gate.CanGenerate),
            BooleanToken(evidence.GateCallbackRegistered),
            SanitizeDiagnosticDetail(error.Details),
            world is null ? "unavailable" : $"{world.MapSizeX}x{world.MapSizeY}x{world.MapSizeZ}",
            world?.ChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable",
            world is null ? "unavailable" : $"{world.NativeRuleSetId}:{world.NativeRuleSetVersion}");
    }

    internal static string SanitizeDiagnosticDetail(string details)
    {
        const int maximumLength = 256;
        if (details.Contains('\\') ||
            details.Contains('/') ||
            details.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            (details.Length >= 2 && char.IsLetter(details[0]) && details[1] == ':'))
        {
            return "redacted-noncanonical-detail";
        }

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

    private static string BooleanToken(bool value) => value ? "true" : "false";
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
