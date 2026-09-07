using ISRWorldGen.Core.Contracts;
using ISRWorldGen.ScaleProfiles;

namespace ISRWorldGen.Tests.L02CNative;

[TestClass]
public sealed class NativeProfileBridgeTests
{
    [TestMethod]
    public void ExplicitRefusalAtGameReady_ShutsDownWithoutEnvelopeOrFrozenGate()
    {
        var store = new MemoryFrozenProfileStore();
        var host = new FakeNativeProfileHost(
            NativeProfileTestSupport.NewLaboratoryWorld() with { MapSizeZ = 8_192 },
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual(1, host.RejectionCount);
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        Assert.AreEqual(0, store.WriteCount);
        Assert.IsNotNull(bridge.LastPreparation);
        Assert.AreEqual(NativeProfileState.Rejected, bridge.LastPreparation.State);
        Assert.IsFalse(bridge.Gate.CanGenerate);
        Assert.AreEqual(NativeProfileState.Rejected, bridge.Gate.State);
        Assert.AreEqual(8_192, host.LastRejectedWorld!.MapSizeZ);
        StringAssert.Contains(host.LastRejectedError!.Details, "X/Z");
    }

    [TestMethod]
    public void NoSelectionAndNoEnvelope_NeverRunsStrictCaptureOrChangesL00Behavior()
    {
        var store = new MemoryFrozenProfileStore();
        var host = new FakeNativeProfileHost(
            NativeProfileTestSupport.NewLaboratoryWorld(),
            NativeProfileTestSupport.NoSelection(),
            store)
        {
            ThrowOnCapture = true
        };
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(0, host.ShutdownCount);
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        Assert.AreEqual(1, host.InactiveCount);
        Assert.AreEqual(0, store.WriteCount);
        Assert.AreEqual(1, store.ReadCount);
        Assert.AreEqual(0, host.CaptureCount);
        Assert.AreEqual(NativeProfileState.Inactive, bridge.LastPreparation!.State);
        Assert.IsFalse(bridge.Gate.CanGenerate);
    }

    [TestMethod]
    public void ValidGameReady_CommitsOnlyAfterPendingReadbackAndPublishesFrozenGate()
    {
        var store = new MemoryFrozenProfileStore();
        var host = new FakeNativeProfileHost(
            NativeProfileTestSupport.NewLaboratoryWorld(),
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        host.FireGameReady();

        Assert.AreEqual(2, store.WriteCount);
        Assert.AreEqual(2, store.ReadCount);
        Assert.AreEqual(1, host.WorldgenGateRegistrationCount);
        Assert.IsTrue(bridge.Gate.CanGenerate);
        Assert.AreEqual(NativeProfileState.Frozen, bridge.Gate.State);
        Assert.AreEqual(0, host.ShutdownCount);
        Assert.IsNotNull(host.LastFrozenPreparation);
        Assert.AreEqual(NativeProfilePreparationSource.New, host.LastFrozenPreparation.Evidence.Source);
        Assert.AreEqual(2, host.LastFrozenPreparation.Evidence.PersistenceWrites);
        Assert.AreEqual(store.GetStoredCopy().Length, host.LastFrozenPreparation.Evidence.EnvelopeBytes);
        Assert.IsTrue(host.LastFrozenPreparation.Evidence.GateCallbackRegistered);
        Assert.IsTrue(host.LastFrozenGateObservation!.CanGenerate);
        Assert.AreEqual(NativeProfileState.Frozen, host.LastFrozenGateObservation.State);
        Assert.IsTrue(host.LastFrozenPublishedProfile);
        NativeProfileResult<NativeFrozenProfileEnvelope> persisted =
            NativeFrozenProfileEnvelopeCodec.Decode(store.GetStoredCopy());
        Assert.AreEqual(NativeProfilePersistenceState.Committed, persisted.Value!.PersistenceState);

        host.FireWorldgenGate();
        Assert.AreEqual(1, host.FrozenGateCount);
        Assert.AreEqual(0, host.ShutdownCount);
    }

    [TestMethod]
    public void NativeMutationBeforeStore_ShutsDownWithoutPublishingOrWriting()
    {
        var store = new MemoryFrozenProfileStore();
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var host = new FakeNativeProfileHost(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            store)
        {
            RecapturedWorld = world with { MapSizeX = 8_192 }
        };
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual(0, store.WriteCount);
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        Assert.AreEqual("native-profile.world-mutation", bridge.LastPreparation!.Error!.Stage);
        Assert.IsFalse(bridge.Gate.CanGenerate);
    }

    [TestMethod]
    public void NativeMutationAfterStoredReread_NeverPublishesFrozenGate()
    {
        var store = new MemoryFrozenProfileStore();
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var host = new FakeNativeProfileHost(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            store)
        {
            SecondRecapturedWorld = world with { MapSizeY = 384 }
        };
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(2, store.WriteCount, "Pending is replaced with a Rejected tombstone.");
        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        Assert.AreEqual("native-profile.world-mutation", bridge.LastPreparation!.Error!.Stage);
        Assert.IsFalse(bridge.Gate.CanGenerate);

        NativeProfileResult<NativeFrozenProfileEnvelope> persisted =
            NativeFrozenProfileEnvelopeCodec.Decode(store.GetStoredCopy());
        Assert.AreEqual(NativeProfilePersistenceState.Rejected, persisted.Value!.PersistenceState);
        NativeProfilePreparation reopened = new NativeProfileCoordinator().Prepare(
            NativeProfileTestSupport.Existing(world),
            NativeProfileTestSupport.NoSelection(),
            store);
        Assert.AreEqual("native-profile.envelope-rejected", reopened.Error!.Stage);
    }

    [TestMethod]
    public void GateRegistrationFailure_PersistsRejectedTombstoneAndShutsDown()
    {
        var store = new MemoryFrozenProfileStore();
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var host = new FakeNativeProfileHost(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            store)
        {
            ThrowOnWorldgenRegistration = true
        };
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual("native-profile.worldgen-registration", bridge.LastPreparation!.Error!.Stage);
        Assert.IsFalse(bridge.Gate.CanGenerate);
        Assert.AreEqual(2, store.WriteCount);
        Assert.AreEqual(
            NativeProfilePersistenceState.Rejected,
            NativeFrozenProfileEnvelopeCodec.Decode(store.GetStoredCopy()).Value!.PersistenceState);
    }

    [TestMethod]
    public void CommitWriteFailure_ReplacesPendingWithRejectedTombstone()
    {
        var store = new MemoryFrozenProfileStore { ThrowOnWriteNumber = 2 };
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var host = new FakeNativeProfileHost(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual("native-profile.store-commit", bridge.LastPreparation!.Error!.Stage);
        Assert.IsFalse(bridge.Gate.CanGenerate);
        Assert.AreEqual(3, store.WriteCount);
        Assert.AreEqual(
            NativeProfilePersistenceState.Rejected,
            NativeFrozenProfileEnvelopeCodec.Decode(store.GetStoredCopy()).Value!.PersistenceState);

        NativeProfilePreparation reopened = new NativeProfileCoordinator().Prepare(
            NativeProfileTestSupport.Existing(world),
            NativeProfileTestSupport.NoSelection(),
            store);
        Assert.AreEqual("native-profile.envelope-rejected", reopened.Error!.Stage);
    }

    [TestMethod]
    public void HeightRefusalCarriesBoundedExplicitDiagnostics()
    {
        var store = new MemoryFrozenProfileStore();
        var host = new FakeNativeProfileHost(
            NativeProfileTestSupport.NewLaboratoryWorld(mapSizeY: 320),
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual(320, host.LastRejectedWorld!.MapSizeY);
        Assert.AreEqual(0, host.LastRejectedPreparation!.Evidence.PersistenceWrites);
        Assert.AreEqual(NativeProfileState.Rejected, host.LastRejectedGateObservation!.State);
        Assert.IsFalse(host.LastRejectedGateObservation.CanGenerate);
        StringAssert.Contains(host.LastRejectedError!.Details, "256");
        string sanitized = VintageStoryNativeProfileHost.SanitizeDiagnosticDetail(
            host.LastRejectedError.Details + "\r\nC:\\private\\secret");
        Assert.IsLessThanOrEqualTo(256, sanitized.Length);
        Assert.DoesNotContain('\r', sanitized);
        Assert.DoesNotContain('\n', sanitized);
        Assert.DoesNotContain('\\', sanitized);
        Assert.DoesNotContain("private", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void FrozenDiagnosticIsStructuredBoundedAndOmitsSaveIdentity()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld(
            savegameIdentifier: "C:\\private\\save-token");
        var store = new MemoryFrozenProfileStore();
        var coordinator = new NativeProfileCoordinator();
        NativeProfilePreparation preparation = coordinator.Prepare(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            store,
            beforeCommit: () => { });
        NativeWorldgenGateObservation gate = coordinator.ObserveWorldgenGate();

        string diagnostic = VintageStoryNativeProfileHost.CreateFrozenDiagnostic(
            world,
            preparation,
            gate,
            publishedProfile: coordinator.PublishedProfile is not null);

        StringAssert.Contains(diagnostic, "source=new");
        StringAssert.Contains(diagnostic, "persistencewrites=2");
        StringAssert.Contains(diagnostic, $"envelopebytes={store.GetStoredCopy().Length}");
        StringAssert.Contains(diagnostic, $"envelopesha256={Hash256.Compute(store.GetStoredCopy())}");
        StringAssert.Contains(diagnostic, "gatestate=Frozen");
        StringAssert.Contains(diagnostic, "gatecangenerate=true");
        StringAssert.Contains(diagnostic, "gatecallbackregistered=true");
        StringAssert.Contains(diagnostic, "publishedprofile=true");
        Assert.DoesNotContain(world.SavegameIdentifier, diagnostic);
        Assert.IsLessThanOrEqualTo(512, diagnostic.Length);
    }

    [TestMethod]
    public void ReloadDiagnosticProvesFrozenPublishedGateWithoutRegisteringNewWorldCallback()
    {
        NativeWorldSnapshot newWorld = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = new MemoryFrozenProfileStore();
        _ = new NativeProfileCoordinator().Prepare(
            newWorld,
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        var coordinator = new NativeProfileCoordinator();
        NativeWorldSnapshot reloadWorld = NativeProfileTestSupport.Existing(newWorld);
        NativeProfilePreparation preparation = coordinator.Prepare(
            reloadWorld,
            NativeProfileTestSupport.NoSelection(),
            store);

        string diagnostic = VintageStoryNativeProfileHost.CreateFrozenDiagnostic(
            reloadWorld,
            preparation,
            coordinator.ObserveWorldgenGate(),
            publishedProfile: coordinator.PublishedProfile is not null);

        StringAssert.Contains(diagnostic, "source=reload");
        StringAssert.Contains(diagnostic, "persistencewrites=0");
        StringAssert.Contains(diagnostic, "gatestate=Frozen");
        StringAssert.Contains(diagnostic, "gatecangenerate=true");
        StringAssert.Contains(diagnostic, "gatecallbackregistered=false");
        StringAssert.Contains(diagnostic, "publishedprofile=true");
    }

    [TestMethod]
    public void ReloadGameReady_LogsRealFrozenEvidenceWithoutRegisteringNewWorldGateCallback()
    {
        NativeWorldSnapshot newWorld = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = new MemoryFrozenProfileStore();
        _ = new NativeProfileCoordinator().Prepare(
            newWorld,
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        int writesBeforeReload = store.WriteCount;
        var host = new FakeNativeProfileHost(
            NativeProfileTestSupport.Existing(newWorld),
            NativeProfileTestSupport.NoSelection(),
            store);
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(writesBeforeReload, store.WriteCount);
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        Assert.AreEqual(NativeProfileState.Frozen, host.LastFrozenPreparation!.State);
        Assert.AreEqual(NativeProfilePreparationSource.Reload, host.LastFrozenPreparation.Evidence.Source);
        Assert.AreEqual(0, host.LastFrozenPreparation.Evidence.PersistenceWrites);
        Assert.IsFalse(host.LastFrozenPreparation.Evidence.GateCallbackRegistered);
        Assert.IsTrue(host.LastFrozenGateObservation!.CanGenerate);
        Assert.IsTrue(host.LastFrozenPublishedProfile);
    }

    [TestMethod]
    public void RejectedDiagnosticReportsHonestWriteCountAndOmitsSaveIdentity()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld(
            mapSizeY: 320,
            savegameIdentifier: "C:\\private\\refusal-token");
        var coordinator = new NativeProfileCoordinator();
        NativeProfilePreparation preparation = coordinator.Prepare(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            new MemoryFrozenProfileStore());

        string diagnostic = VintageStoryNativeProfileHost.CreateRejectedDiagnostic(
            preparation.Error!,
            world,
            preparation,
            coordinator.ObserveWorldgenGate());

        StringAssert.Contains(diagnostic, "source=new");
        StringAssert.Contains(diagnostic, "persistencewrites=0");
        StringAssert.Contains(diagnostic, "envelopebytes=0");
        StringAssert.Contains(diagnostic, "envelopesha256=none");
        StringAssert.Contains(diagnostic, "gatestate=Rejected");
        StringAssert.Contains(diagnostic, "gatecangenerate=false");
        StringAssert.Contains(diagnostic, "gatecallbackregistered=false");
        StringAssert.Contains(diagnostic, "dimensions=4096x320x4096");
        Assert.DoesNotContain(world.SavegameIdentifier, diagnostic);
        Assert.IsLessThanOrEqualTo(768, diagnostic.Length);
    }

    private sealed class FakeNativeProfileHost : INativeProfileHost
    {
        private readonly NativeWorldSnapshot firstWorld;
        private readonly NativeProfileSelection selection;
        private readonly IFrozenProfileStore store;
        private Action? gameReady;
        private Action? worldgenGate;
        private int captureCount;

        internal FakeNativeProfileHost(
            NativeWorldSnapshot firstWorld,
            NativeProfileSelection selection,
            IFrozenProfileStore store)
        {
            this.firstWorld = firstWorld;
            this.selection = selection;
            this.store = store;
        }

        internal NativeWorldSnapshot? RecapturedWorld { get; set; }

        internal NativeWorldSnapshot? SecondRecapturedWorld { get; set; }

        internal bool ThrowOnCapture { get; set; }

        internal bool ThrowOnWorldgenRegistration { get; set; }

        internal int CaptureCount => captureCount;

        internal int ShutdownCount { get; private set; }

        internal int RejectionCount { get; private set; }

        internal int InactiveCount { get; private set; }

        internal int FrozenGateCount { get; private set; }

        internal int WorldgenGateRegistrationCount { get; private set; }

        internal NativeProfileError? LastRejectedError { get; private set; }

        internal NativeWorldSnapshot? LastRejectedWorld { get; private set; }

        internal NativeProfilePreparation? LastFrozenPreparation { get; private set; }

        internal NativeWorldgenGateObservation? LastFrozenGateObservation { get; private set; }

        internal bool LastFrozenPublishedProfile { get; private set; }

        internal NativeProfilePreparation? LastRejectedPreparation { get; private set; }

        internal NativeWorldgenGateObservation? LastRejectedGateObservation { get; private set; }

        public void RegisterGameReady(Action callback) => gameReady = callback;

        public void RegisterWorldgenGate(Action callback)
        {
            if (ThrowOnWorldgenRegistration)
            {
                throw new InvalidOperationException("Injected gate registration failure.");
            }

            WorldgenGateRegistrationCount++;
            worldgenGate = callback;
        }

        public NativeWorldSnapshot CaptureWorld()
        {
            captureCount++;
            if (ThrowOnCapture)
            {
                throw new InvalidOperationException("Strict world capture is unavailable.");
            }

            return captureCount switch
            {
                1 => firstWorld,
                2 => RecapturedWorld ?? firstWorld,
                _ => SecondRecapturedWorld ?? RecapturedWorld ?? firstWorld
            };
        }

        public NativeProfileSelection ReadSelection() => selection;

        public IFrozenProfileStore CreateStore() => store;

        public void LogFrozen(
            NativeWorldSnapshot world,
            NativeProfilePreparation preparation,
            NativeWorldgenGateObservation gate,
            bool publishedProfile)
        {
            LastFrozenPreparation = preparation;
            LastFrozenGateObservation = gate;
            LastFrozenPublishedProfile = publishedProfile;
        }

        public void LogInactive() => InactiveCount++;

        public void LogRejected(
            NativeProfileError error,
            NativeWorldSnapshot? world,
            NativeProfilePreparation preparation,
            NativeWorldgenGateObservation gate)
        {
            RejectionCount++;
            LastRejectedError = error;
            LastRejectedWorld = world;
            LastRejectedPreparation = preparation;
            LastRejectedGateObservation = gate;
        }

        public void LogFrozenGate(string profileId) => FrozenGateCount++;

        public void ShutDown() => ShutdownCount++;

        internal void FireGameReady() =>
            (gameReady ?? throw new InvalidOperationException("GameReady was not registered."))();

        internal void FireWorldgenGate() =>
            (worldgenGate ?? throw new InvalidOperationException("Worldgen gate was not registered."))();
    }
}
