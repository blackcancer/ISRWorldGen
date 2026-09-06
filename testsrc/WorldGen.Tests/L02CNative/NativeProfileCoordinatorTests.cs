using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.ScaleProfiles;

namespace ISRWorldGen.Tests.L02CNative;

[TestClass]
public sealed class NativeProfileCoordinatorTests
{
    [TestMethod]
    public void NewWorld_ExplicitMatchingProfile_IsStoredRereadAndPublishedBeforeGate()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = new MemoryFrozenProfileStore();
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        NativeWorldgenGateObservation gate = coordinator.ObserveWorldgenGate();

        Assert.AreEqual(NativeProfileState.Frozen, preparation.State);
        Assert.IsNotNull(preparation.Profile);
        Assert.AreSame(preparation.Profile, coordinator.PublishedProfile);
        Assert.AreEqual(2, store.WriteCount, "Pending and Committed states are separate single-key writes.");
        Assert.AreEqual(2, store.ReadCount, "The initial absence check and post-write reread are both required.");
        Assert.IsTrue(gate.CanGenerate);
        Assert.AreEqual(NativeProfileState.Frozen, gate.State);
        Assert.AreEqual("laboratory", gate.ProfileId);
    }

    [TestMethod]
    public void ExistingWorld_ReloadsIdenticalFrozenProfileWithoutRewriting()
    {
        NativeWorldSnapshot newWorld = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = new MemoryFrozenProfileStore();
        var firstCoordinator = new NativeProfileCoordinator();
        NativeProfilePreparation first = firstCoordinator.Prepare(
            newWorld,
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        byte[] persisted = store.GetStoredCopy();

        var reopenCoordinator = new NativeProfileCoordinator();
        NativeProfilePreparation reopened = reopenCoordinator.Prepare(
            NativeProfileTestSupport.Existing(newWorld),
            NativeProfileTestSupport.NoSelection(),
            store);

        Assert.AreEqual(NativeProfileState.Frozen, reopened.State);
        Assert.AreEqual(first.Profile, reopened.Profile);
        Assert.AreEqual(2, store.WriteCount);
        CollectionAssert.AreEqual(persisted, store.GetStoredCopy());
    }

    [TestMethod]
    public void CorruptEnvelope_IsRejectedAndNeverPublished()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = CreateFrozenStore(world);
        byte[] corrupt = store.GetStoredCopy();
        corrupt[^1] ^= 0xff;
        store.Replace(corrupt);
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.Existing(world),
            NativeProfileTestSupport.NoSelection(),
            store);

        AssertRejected(preparation, coordinator, "native-profile.envelope-checksum");
        Assert.IsFalse(coordinator.ObserveWorldgenGate().CanGenerate);
    }

    [TestMethod]
    public void CorruptInnerProfileWithValidOuterChecksum_IsRejectedByCoreReload()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = CreateFrozenStore(world);
        NativeFrozenProfileEnvelope decoded = NativeFrozenProfileEnvelopeCodec.Decode(store.GetStoredCopy()).Value!;
        byte[] corruptProfile = decoded.GetProfileBytesCopy();
        corruptProfile[^1] ^= 0xff;
        var rewrapped = new NativeFrozenProfileEnvelope(
            decoded.SavegameIdentifier,
            decoded.MapSizeX,
            decoded.MapSizeY,
            decoded.MapSizeZ,
            decoded.ChunkSize,
            decoded.NativeRuleSetId,
            decoded.NativeRuleSetVersion,
            decoded.GeographyConfigHash,
            decoded.ProfileCodecId,
            decoded.ProfileCodecVersion,
            decoded.PersistenceState,
            corruptProfile);
        store.Replace(NativeFrozenProfileEnvelopeCodec.Encode(rewrapped));
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.Existing(world),
            NativeProfileTestSupport.NoSelection(),
            store);

        AssertRejected(preparation, coordinator, "atlas.profile.manifest-checksum");
        Assert.IsFalse(coordinator.ObserveWorldgenGate().CanGenerate);
    }

    [TestMethod]
    public void ExistingWorld_ExplicitDifferentSelection_IsRejectedAsMutation()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = CreateFrozenStore(world);
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.Existing(world),
            new NativeProfileSelection(isSpecified: true, "balanced"),
            store);

        AssertRejected(preparation, coordinator, "native-profile.selection-mutation");
        Assert.AreEqual(2, store.WriteCount);
    }

    [TestMethod]
    public void RectangularOrChangedDimensions_AreRejectedBeforeCoreCanAcceptBounds()
    {
        NativeWorldSnapshot laboratory = NativeProfileTestSupport.NewLaboratoryWorld();
        var newStore = new MemoryFrozenProfileStore();
        var newCoordinator = new NativeProfileCoordinator();

        NativeProfilePreparation newPreparation = newCoordinator.Prepare(
            laboratory with { MapSizeZ = 8_192 },
            NativeProfileTestSupport.LaboratorySelection(),
            newStore);

        AssertRejected(newPreparation, newCoordinator, "native-profile.effective-dimensions");
        Assert.AreEqual(0, newStore.WriteCount);

        var persistedStore = CreateFrozenStore(laboratory);
        var reopenCoordinator = new NativeProfileCoordinator();
        NativeProfilePreparation reopened = reopenCoordinator.Prepare(
            NativeProfileTestSupport.Existing(laboratory) with { MapSizeZ = 8_192 },
            NativeProfileTestSupport.NoSelection(),
            persistedStore);

        AssertRejected(reopened, reopenCoordinator, "native-profile.world-mutation");
    }

    [TestMethod]
    public void UnsupportedEffectiveHeight_UsesCoreTypedRefusalAndDoesNotStore()
    {
        var store = new MemoryFrozenProfileStore();
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.NewLaboratoryWorld(mapSizeY: 320),
            NativeProfileTestSupport.LaboratorySelection(),
            store);

        AssertRejected(preparation, coordinator, "atlas.profile.native-height");
        Assert.AreEqual(GenerationFailureCode.InvalidInput, preparation.Error!.Code);
        Assert.AreEqual(0, store.WriteCount);
    }

    [TestMethod]
    public void NoExplicitSelection_LeavesNewWorldInactiveWithoutDefaultOrWrite()
    {
        var store = new MemoryFrozenProfileStore();
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.NewLaboratoryWorld(),
            NativeProfileTestSupport.NoSelection(),
            store);

        Assert.AreEqual(NativeProfileState.Inactive, preparation.State);
        Assert.IsNull(preparation.Profile);
        Assert.IsNull(preparation.Error);
        Assert.IsNull(coordinator.PublishedProfile);
        Assert.AreEqual(0, store.WriteCount);
        Assert.IsFalse(coordinator.ObserveWorldgenGate().CanGenerate);
    }

    [TestMethod]
    public void ExistingWorldCannotBeActivatedWithoutFrozenEnvelope()
    {
        var store = new MemoryFrozenProfileStore();
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.Existing(NativeProfileTestSupport.NewLaboratoryWorld()),
            NativeProfileTestSupport.LaboratorySelection(),
            store);

        AssertRejected(preparation, coordinator, "native-profile.retroactive-activation");
        Assert.AreEqual(0, store.WriteCount);
    }

    [TestMethod]
    public void PostWriteRereadMismatch_IsRejectedWithoutPublication()
    {
        var store = new MemoryFrozenProfileStore { ReturnDifferentBytesAfterWrite = true };
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.NewLaboratoryWorld(),
            NativeProfileTestSupport.LaboratorySelection(),
            store);

        AssertRejected(preparation, coordinator, "native-profile.store-reread");
        Assert.AreEqual(2, store.WriteCount, "The second write is the rejected tombstone.");
        Assert.IsNull(coordinator.PublishedProfile);

        store.ReturnDifferentBytesAfterWrite = false;
        NativeProfileResult<NativeFrozenProfileEnvelope> persisted =
            NativeFrozenProfileEnvelopeCodec.Decode(store.GetStoredCopy());
        Assert.IsTrue(persisted.IsSuccess);
        Assert.AreEqual(NativeProfilePersistenceState.Rejected, persisted.Value!.PersistenceState);

        NativeProfilePreparation reopened = new NativeProfileCoordinator().Prepare(
            NativeProfileTestSupport.Existing(NativeProfileTestSupport.NewLaboratoryWorld()),
            NativeProfileTestSupport.NoSelection(),
            store);
        Assert.AreEqual(NativeProfileState.Rejected, reopened.State);
        Assert.AreEqual("native-profile.envelope-rejected", reopened.Error!.Stage);
    }

    [TestMethod]
    public void TombstoneWriteFailure_LeavesPendingEnvelopeNonActivatableOnRestart()
    {
        var store = new MemoryFrozenProfileStore
        {
            ReturnDifferentBytesAfterWrite = true,
            ThrowOnWriteNumber = 2
        };
        var coordinator = new NativeProfileCoordinator();

        NativeProfilePreparation preparation = coordinator.Prepare(
            NativeProfileTestSupport.NewLaboratoryWorld(),
            NativeProfileTestSupport.LaboratorySelection(),
            store);

        AssertRejected(preparation, coordinator, "native-profile.store-reread");
        store.ReturnDifferentBytesAfterWrite = false;
        NativeProfileResult<NativeFrozenProfileEnvelope> persisted =
            NativeFrozenProfileEnvelopeCodec.Decode(store.GetStoredCopy());
        Assert.IsTrue(persisted.IsSuccess);
        Assert.AreEqual(NativeProfilePersistenceState.Pending, persisted.Value!.PersistenceState);

        NativeProfilePreparation reopened = new NativeProfileCoordinator().Prepare(
            NativeProfileTestSupport.Existing(NativeProfileTestSupport.NewLaboratoryWorld()),
            NativeProfileTestSupport.NoSelection(),
            store);
        Assert.AreEqual(NativeProfileState.Rejected, reopened.State);
        Assert.AreEqual("native-profile.envelope-pending", reopened.Error!.Stage);
    }

    [TestMethod]
    public void StoreAndEnvelopeExposeOnlyDefensiveCopies()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        var store = CreateFrozenStore(world);
        byte[] original = store.GetStoredCopy();
        byte[] callerCopy = store.Read()!;
        callerCopy[0] ^= 0xff;

        CollectionAssert.AreEqual(original, store.GetStoredCopy());

        NativeProfileResult<NativeFrozenProfileEnvelope> decoded = NativeFrozenProfileEnvelopeCodec.Decode(original);
        Assert.IsTrue(decoded.IsSuccess);
        byte[] firstProfileCopy = decoded.Value!.GetProfileBytesCopy();
        firstProfileCopy[0] ^= 0xff;
        CollectionAssert.AreNotEqual(firstProfileCopy, decoded.Value.GetProfileBytesCopy());
    }

    private static MemoryFrozenProfileStore CreateFrozenStore(NativeWorldSnapshot world)
    {
        var store = new MemoryFrozenProfileStore();
        NativeProfilePreparation preparation = new NativeProfileCoordinator().Prepare(
            world,
            NativeProfileTestSupport.LaboratorySelection(),
            store);
        Assert.AreEqual(NativeProfileState.Frozen, preparation.State);
        return store;
    }

    private static void AssertRejected(
        NativeProfilePreparation preparation,
        NativeProfileCoordinator coordinator,
        string expectedStage)
    {
        Assert.AreEqual(NativeProfileState.Rejected, preparation.State);
        Assert.IsNotNull(preparation.Error);
        Assert.AreEqual(expectedStage, preparation.Error.Stage);
        Assert.IsNull(preparation.Profile);
        Assert.IsNull(coordinator.PublishedProfile);
    }
}
