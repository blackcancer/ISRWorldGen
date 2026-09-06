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
    }

    [TestMethod]
    public void NoSelection_KeepsExistingL00LaunchBehaviorInactive()
    {
        var store = new MemoryFrozenProfileStore();
        var host = new FakeNativeProfileHost(
            NativeProfileTestSupport.NewLaboratoryWorld(),
            NativeProfileTestSupport.NoSelection(),
            store);
        var bridge = new VintageStoryNativeProfileBridge(host);

        bridge.Register();
        host.FireGameReady();

        Assert.AreEqual(0, host.ShutdownCount);
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        Assert.AreEqual(1, host.InactiveCount);
        Assert.AreEqual(0, store.WriteCount);
        Assert.AreEqual(NativeProfileState.Inactive, bridge.LastPreparation!.State);
        Assert.IsFalse(bridge.Gate.CanGenerate);
    }

    [TestMethod]
    public void ValidGameReady_RegistersGateOnlyAfterStoredRereadAndFrozenPublication()
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

        Assert.AreEqual(1, store.WriteCount);
        Assert.AreEqual(2, store.ReadCount);
        Assert.AreEqual(1, host.WorldgenGateRegistrationCount);
        Assert.IsTrue(bridge.Gate.CanGenerate);
        Assert.AreEqual(NativeProfileState.Frozen, bridge.Gate.State);
        Assert.AreEqual(0, host.ShutdownCount);

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

        Assert.AreEqual(1, store.WriteCount);
        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual(0, host.WorldgenGateRegistrationCount);
        Assert.AreEqual("native-profile.world-mutation", bridge.LastPreparation!.Error!.Stage);
        Assert.IsFalse(bridge.Gate.CanGenerate);
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

        internal int ShutdownCount { get; private set; }

        internal int RejectionCount { get; private set; }

        internal int InactiveCount { get; private set; }

        internal int FrozenGateCount { get; private set; }

        internal int WorldgenGateRegistrationCount { get; private set; }

        public void RegisterGameReady(Action callback) => gameReady = callback;

        public void RegisterWorldgenGate(Action callback)
        {
            WorldgenGateRegistrationCount++;
            worldgenGate = callback;
        }

        public NativeWorldSnapshot CaptureWorld()
        {
            captureCount++;
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

        public void LogInactive(NativeWorldSnapshot world) => InactiveCount++;

        public void LogRejected(NativeProfileError error) => RejectionCount++;

        public void LogFrozenGate(string profileId) => FrozenGateCount++;

        public void ShutDown() => ShutdownCount++;

        internal void FireGameReady() =>
            (gameReady ?? throw new InvalidOperationException("GameReady was not registered."))();

        internal void FireWorldgenGate() =>
            (worldgenGate ?? throw new InvalidOperationException("Worldgen gate was not registered."))();
    }
}
