using System.Text;
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

    [TestMethod]
    public void ContentAddressedSnapshots_PreserveEarlierRevisionContent()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        byte[] oldPayload = "old-generation"u8.ToArray();
        byte[] newPayload = "new-generation"u8.ToArray();
        var oldReference = new SnapshotReference(
            fixture.Parent.Id,
            fixture.Parent.Revision,
            Hash256.Compute(oldPayload),
            "content-addressed",
            []);
        var newReference = new SnapshotReference(
            fixture.Parent.Id,
            fixture.Parent.Revision,
            Hash256.Compute(newPayload),
            "content-addressed",
            []);
        var oldManifest = new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            [oldReference]);
        var newManifest = new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            [newReference]);
        var store = new InMemoryWorldSnapshotStore();
        var service = new WorldPersistenceService(store, PersistenceTestData.Limits);

        PersistenceTestData.AssertSuccess(service.WriteSnapshot(oldReference, oldPayload));
        PersistenceTestData.AssertSuccess(service.WriteManifest(oldManifest));
        CollectionAssert.AreEqual(
            oldPayload,
            PersistenceTestData.AssertSuccess(service.Restore(fixture.Compatibility)).Snapshots[0].GetPayloadCopy());

        PersistenceTestData.AssertSuccess(service.WriteSnapshot(newReference, newPayload));
        PersistenceTestData.AssertSuccess(service.WriteManifest(newManifest));
        CollectionAssert.AreEqual(
            newPayload,
            PersistenceTestData.AssertSuccess(service.Restore(fixture.Compatibility)).Snapshots[0].GetPayloadCopy());

        Assert.AreNotEqual(oldReference.StorageKey, newReference.StorageKey);
        CollectionAssert.AreNotEqual(store.GetRaw(oldReference.StorageKey), store.GetRaw(newReference.StorageKey));
        PersistenceTestData.AssertSuccess(service.WriteManifest(oldManifest));
        CollectionAssert.AreEqual(
            oldPayload,
            PersistenceTestData.AssertSuccess(service.Restore(fixture.Compatibility)).Snapshots[0].GetPayloadCopy());
    }

    [TestMethod]
    public void SameIdentityAndRevision_IsRejectedTwiceWithinOneManifest()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        var colliding = new SnapshotReference(
            fixture.Parent.Id,
            fixture.Parent.Revision,
            Hash256.Compute("collision"u8),
            fixture.Parent.Kind,
            []);

        Assert.ThrowsExactly<ArgumentException>(() => new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            [fixture.Parent, colliding]));
        Assert.ThrowsExactly<ArgumentException>(() => new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            [fixture.Parent, fixture.Parent]));
    }

    [TestMethod]
    public void WriteSnapshot_OwnsPayloadBeforeTheStoreCanMutateTheCallerBuffer()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        byte[] callerPayload = Encoding.UTF8.GetBytes("caller-owned-before-write");
        byte[] expectedPayload = callerPayload.ToArray();
        var reference = new SnapshotReference(
            fixture.Child.Id,
            revision: 17,
            Hash256.Compute(expectedPayload),
            "ownership",
            []);
        bool callbackObserved = false;
        var store = new InMemoryWorldSnapshotStore(key =>
        {
            if (string.Equals(key, reference.StorageKey, StringComparison.Ordinal))
            {
                callerPayload.AsSpan().Fill(0x5a);
                callbackObserved = true;
            }
        });
        var service = new WorldPersistenceService(store, PersistenceTestData.Limits);
        var manifest = new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            [reference]);

        PersistenceTestData.AssertSuccess(service.WriteSnapshot(reference, callerPayload));
        PersistenceTestData.AssertSuccess(service.WriteManifest(manifest));
        RestoredWorld restored = PersistenceTestData.AssertSuccess(service.Restore(fixture.Compatibility));

        Assert.IsTrue(callbackObserved);
        CollectionAssert.AreNotEqual(expectedPayload, callerPayload);
        CollectionAssert.AreEqual(expectedPayload, restored.Snapshots[0].GetPayloadCopy());
    }
}
