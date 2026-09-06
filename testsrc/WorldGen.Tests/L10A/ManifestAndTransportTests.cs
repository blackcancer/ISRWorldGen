using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Runtime.Persistence;

namespace ISRWorldGen.Tests.L10A;

[TestClass]
public sealed class ManifestAndTransportTests
{
    [TestMethod]
    public void ManifestAndReferences_AreCanonicalImmutableAndNamespaced()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        byte[] firstManifestBytes = fixture.Store.GetRaw(PersistenceKeys.Manifest);
        byte[] mutableConfiguration = fixture.FrozenConfiguration.ToArray();
        SnapshotReference[] reversed = fixture.Manifest.Snapshots.Reverse().ToArray();
        var equivalent = new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            mutableConfiguration,
            reversed);
        var secondStore = new InMemoryWorldSnapshotStore();
        var secondService = new WorldPersistenceService(secondStore, PersistenceTestData.Limits);
        PersistenceTestData.AssertSuccess(secondService.WriteManifest(equivalent));

        mutableConfiguration[0] ^= 0xff;

        CollectionAssert.AreEqual(firstManifestBytes, secondStore.GetRaw(PersistenceKeys.Manifest));
        CollectionAssert.AreEqual(fixture.FrozenConfiguration, equivalent.GetFrozenConfigurationCopy());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<SnapshotReference>)equivalent.Snapshots)[0] = fixture.Child);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<SnapshotParentReference>)fixture.Child.Parents)[0] = default);
        StringAssert.StartsWith(fixture.Parent.StorageKey, PersistenceKeys.CanonicalSnapshotPrefix);
        StringAssert.StartsWith(PersistenceKeys.Manifest, PersistenceKeys.CanonicalPrefix);
        StringAssert.StartsWith(PersistenceKeys.DerivedCachePrefix, PersistenceKeys.RootNamespace);
        Assert.IsFalse(PersistenceKeys.DerivedCachePrefix.StartsWith(PersistenceKeys.CanonicalPrefix, StringComparison.Ordinal));
    }

    [TestMethod]
    public void CanonicalSaveCopy_RestoresWithoutDerivedCacheOrOriginalStore()
    {
        PersistenceFixture source = PersistenceTestData.Create();
        var cache = new RecordingDerivedSnapshotCache { EntryCount = 7 };
        DerivedCacheMaintenance.Purge(cache);
        var transportedStore = new InMemoryWorldSnapshotStore();
        source.Store.CopyCanonicalDataTo(transportedStore);
        var transportedService = new WorldPersistenceService(transportedStore, PersistenceTestData.Limits);

        RestoredWorld restored = PersistenceTestData.AssertSuccess(
            transportedService.Restore(source.Compatibility));

        Assert.AreEqual(1, cache.ClearCount);
        Assert.AreEqual(0, cache.EntryCount);
        Assert.HasCount(2, restored.Snapshots);
        CollectionAssert.AreEqual(source.ParentPayload, restored.GetSnapshot(source.Parent.Id, source.Parent.Revision).GetPayloadCopy());
        CollectionAssert.AreEqual(source.ChildPayload, restored.GetSnapshot(source.Child.Id, source.Child.Revision).GetPayloadCopy());
        CollectionAssert.AreEqual(source.FrozenConfiguration, restored.Manifest.GetFrozenConfigurationCopy());
    }

    [TestMethod]
    public void SnapshotHashMismatch_IsRejectedBeforeAnyCanonicalWrite()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        var wrongReference = new SnapshotReference(
            fixture.Parent.Id,
            fixture.Parent.Revision,
            Hash256.Zero,
            fixture.Parent.Kind,
            fixture.Parent.Parents);
        int writesBefore = fixture.Store.WriteCount;

        PersistenceError error = PersistenceTestData.AssertFailure(
            fixture.Service.WriteSnapshot(wrongReference, fixture.ParentPayload),
            PersistenceErrorCode.ChecksumMismatch);

        Assert.AreEqual(writesBefore, fixture.Store.WriteCount);
        Assert.AreEqual(wrongReference.StorageKey, error.StorageKey);
    }
}
