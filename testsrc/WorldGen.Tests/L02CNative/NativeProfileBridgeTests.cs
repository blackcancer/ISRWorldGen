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
        StringAssert.Contains(host.LastRejectedError!.Details, "256");
        string sanitized = VintageStoryNativeProfileHost.SanitizeDiagnosticDetail(
            host.LastRejectedError.Details + "\r\nC:\\private\\secret");
        Assert.IsLessThanOrEqualTo(256, sanitized.Length);
        Assert.DoesNotContain('\r', sanitized);
        Assert.DoesNotContain('\n', sanitized);
        Assert.DoesNotContain('\\', sanitized);
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

        public void LogFrozen(NativeWorldSnapshot world, string profileId)
        {
        }

        public void LogInactive() => InactiveCount++;

        public void LogRejected(NativeProfileError error, NativeWorldSnapshot? world)
        {
            RejectionCount++;
            LastRejectedError = error;
            LastRejectedWorld = world;
        }

        public void LogFrozenGate(string profileId) => FrozenGateCount++;

        public void ShutDown() => ShutdownCount++;

        internal void FireGameReady() =>
            (gameReady ?? throw new InvalidOperationException("GameReady was not registered."))();

        internal void FireWorldgenGate() =>
            (worldgenGate ?? throw new InvalidOperationException("Worldgen gate was not registered."))();
    }
}
