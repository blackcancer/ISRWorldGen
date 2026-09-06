using System.Buffers.Binary;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Runtime.Persistence;

namespace ISRWorldGen.Tests.L10A;

[TestClass]
public sealed class PersistenceFailureTests
{
    [TestMethod]
    public void VanillaWorldWithoutManifest_IsRefusedWithoutMutation()
    {
        var store = new InMemoryWorldSnapshotStore();
        var service = new WorldPersistenceService(store, PersistenceTestData.Limits);
        PersistenceFixture expected = PersistenceTestData.Create();

        PersistenceError error = PersistenceTestData.AssertFailure(
            service.Restore(expected.Compatibility),
            PersistenceErrorCode.WorldNotActivated);

        Assert.AreEqual(0, store.WriteCount);
        Assert.AreEqual(0, store.OpenReadCount);
        StringAssert.Contains(error.Details, "not activated");
    }

    [TestMethod]
    public void UnknownManifestVersion_IsRefusedWithoutMutation()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        byte[] manifest = fixture.Store.GetRaw(PersistenceKeys.Manifest);
        BinaryPrimitives.WriteUInt32BigEndian(manifest.AsSpan(PersistenceEnvelope.FormatVersionOffset, 4), 99);
        fixture.Store.PutRaw(PersistenceKeys.Manifest, manifest);
        int writesBefore = fixture.Store.WriteCount;

        PersistenceTestData.AssertFailure(
            fixture.Service.Restore(fixture.Compatibility),
            PersistenceErrorCode.UnsupportedVersion);

        Assert.AreEqual(writesBefore, fixture.Store.WriteCount);
    }

    [TestMethod]
    public void TruncatedManifestAndBrokenChecksums_AreExplicitAndNonDestructive()
    {
        PersistenceFixture truncatedFixture = PersistenceTestData.Create();
        byte[] truncated = truncatedFixture.Store.GetRaw(PersistenceKeys.Manifest)[..^1];
        truncatedFixture.Store.PutRaw(PersistenceKeys.Manifest, truncated);
        int truncatedWrites = truncatedFixture.Store.WriteCount;
        PersistenceTestData.AssertFailure(
            truncatedFixture.Service.Restore(truncatedFixture.Compatibility),
            PersistenceErrorCode.TruncatedData);
        Assert.AreEqual(truncatedWrites, truncatedFixture.Store.WriteCount);

        PersistenceFixture checksumFixture = PersistenceTestData.Create();
        byte[] manifest = checksumFixture.Store.GetRaw(PersistenceKeys.Manifest);
        manifest[^1] ^= 0x80;
        checksumFixture.Store.PutRaw(PersistenceKeys.Manifest, manifest);
        int checksumWrites = checksumFixture.Store.WriteCount;
        PersistenceTestData.AssertFailure(
            checksumFixture.Service.Restore(checksumFixture.Compatibility),
            PersistenceErrorCode.ChecksumMismatch);
        Assert.AreEqual(checksumWrites, checksumFixture.Store.WriteCount);

        PersistenceFixture snapshotFixture = PersistenceTestData.Create();
        byte[] snapshot = snapshotFixture.Store.GetRaw(snapshotFixture.Child.StorageKey);
        snapshot[^1] ^= 0x40;
        snapshotFixture.Store.PutRaw(snapshotFixture.Child.StorageKey, snapshot);
        int snapshotWrites = snapshotFixture.Store.WriteCount;
        PersistenceTestData.AssertFailure(
            snapshotFixture.Service.Restore(snapshotFixture.Compatibility),
            PersistenceErrorCode.ChecksumMismatch);
        Assert.AreEqual(snapshotWrites, snapshotFixture.Store.WriteCount);
    }

    [TestMethod]
    public void ReportedAndDeclaredLengths_AreRejectedBeforePayloadAllocationOrOpen()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        var oversizedStore = new InMemoryWorldSnapshotStore();
        oversizedStore.ReportLength(PersistenceKeys.Manifest, PersistenceTestData.Limits.MaxEnvelopeBytes + 1L);
        var oversizedService = new WorldPersistenceService(oversizedStore, PersistenceTestData.Limits);

        PersistenceTestData.AssertFailure(
            oversizedService.Restore(fixture.Compatibility),
            PersistenceErrorCode.PayloadTooLarge);
        Assert.AreEqual(0, oversizedStore.OpenReadCount);

        byte[] envelope = fixture.Store.GetRaw(PersistenceKeys.Manifest);
        BinaryPrimitives.WriteUInt64BigEndian(
            envelope.AsSpan(PersistenceEnvelope.DecodedLengthOffset, 8),
            checked((ulong)PersistenceTestData.Limits.MaxManifestDecodedBytes + 1UL));
        fixture.Store.PutRaw(PersistenceKeys.Manifest, envelope);
        PersistenceTestData.AssertFailure(
            fixture.Service.Restore(fixture.Compatibility),
            PersistenceErrorCode.PayloadTooLarge);
    }

    [TestMethod]
    public void UnknownEncodingAndOversizedStoredLength_AreRefusedBeforePayloadDecode()
    {
        PersistenceFixture encodingFixture = PersistenceTestData.Create();
        byte[] unknownEncoding = encodingFixture.Store.GetRaw(PersistenceKeys.Manifest);
        unknownEncoding[PersistenceEnvelope.EncodingOffset] = 7;
        encodingFixture.Store.PutRaw(PersistenceKeys.Manifest, unknownEncoding);
        int encodingWrites = encodingFixture.Store.WriteCount;

        PersistenceTestData.AssertFailure(
            encodingFixture.Service.Restore(encodingFixture.Compatibility),
            PersistenceErrorCode.UnsupportedEncoding);
        Assert.AreEqual(encodingWrites, encodingFixture.Store.WriteCount);

        PersistenceFixture lengthFixture = PersistenceTestData.Create();
        byte[] oversizedStored = lengthFixture.Store.GetRaw(PersistenceKeys.Manifest);
        BinaryPrimitives.WriteUInt64BigEndian(
            oversizedStored.AsSpan(PersistenceEnvelope.StoredLengthOffset, sizeof(ulong)),
            checked((ulong)PersistenceTestData.Limits.MaxEnvelopeBytes));
        lengthFixture.Store.PutRaw(PersistenceKeys.Manifest, oversizedStored);
        int lengthWrites = lengthFixture.Store.WriteCount;

        PersistenceTestData.AssertFailure(
            lengthFixture.Service.Restore(lengthFixture.Compatibility),
            PersistenceErrorCode.PayloadTooLarge);
        Assert.AreEqual(lengthWrites, lengthFixture.Store.WriteCount);
    }

    [TestMethod]
    public void MissingParentPayload_IsDistinguishedFromMissingLeafSnapshot()
    {
        PersistenceFixture missingParent = PersistenceTestData.Create(writeParentPayload: false);
        PersistenceError parentError = PersistenceTestData.AssertFailure(
            missingParent.Service.Restore(missingParent.Compatibility),
            PersistenceErrorCode.MissingParent);
        Assert.AreEqual(missingParent.Parent.StorageKey, parentError.StorageKey);

        PersistenceFixture missingLeaf = PersistenceTestData.Create(writeChildPayload: false);
        PersistenceError leafError = PersistenceTestData.AssertFailure(
            missingLeaf.Service.Restore(missingLeaf.Compatibility),
            PersistenceErrorCode.MissingSnapshot);
        Assert.AreEqual(missingLeaf.Child.StorageKey, leafError.StorageKey);
    }

    [TestMethod]
    public void ManifestWithParentReferenceAbsentFromManifest_IsRejected()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        var invalidManifest = new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            [fixture.Child]);
        int writesBefore = fixture.Store.WriteCount;

        PersistenceTestData.AssertFailure(
            fixture.Service.WriteManifest(invalidManifest),
            PersistenceErrorCode.MissingParent);

        Assert.AreEqual(writesBefore, fixture.Store.WriteCount);
    }

    [TestMethod]
    public void CyclicManifest_IsRejectedBeforeCanonicalWrite()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        var cyclic = new SnapshotReference(
            fixture.Parent.Id,
            fixture.Parent.Revision,
            fixture.Parent.PayloadHash,
            fixture.Parent.Kind,
            [new SnapshotParentReference(fixture.Parent.Id, fixture.Parent.Revision, fixture.Parent.PayloadHash)]);
        var invalidManifest = new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            [cyclic]);
        int writesBefore = fixture.Store.WriteCount;

        PersistenceTestData.AssertFailure(
            fixture.Service.WriteManifest(invalidManifest),
            PersistenceErrorCode.CorruptData);

        Assert.AreEqual(writesBefore, fixture.Store.WriteCount);
    }

    [TestMethod]
    public void InMemoryInputsOverStructuralCaps_AreRejectedBeforeCanonicalWrite()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        SnapshotReference[] excessiveSnapshots = Enumerable
            .Range(0, PersistenceTestData.Limits.MaxSnapshotCount + 1)
            .Select(index => new SnapshotReference(
                StableId.Derive(RandomDomain.Sites, StableId.Zero, (ulong)index),
                1,
                Hash256.Compute([(byte)index]),
                "bounded",
                []))
            .ToArray();
        var excessiveManifest = new WorldManifest(
            fixture.Manifest.Identity,
            fixture.Manifest.Units,
            fixture.FrozenConfiguration,
            excessiveSnapshots);
        byte[] oversizedPayload = new byte[checked((int)PersistenceTestData.Limits.MaxSnapshotDecodedBytes + 1)];
        int writesBefore = fixture.Store.WriteCount;

        PersistenceTestData.AssertFailure(
            fixture.Service.WriteManifest(excessiveManifest),
            PersistenceErrorCode.PayloadTooLarge);
        PersistenceTestData.AssertFailure(
            fixture.Service.WriteSnapshot(fixture.Parent, oversizedPayload),
            PersistenceErrorCode.PayloadTooLarge);

        Assert.AreEqual(writesBefore, fixture.Store.WriteCount);
    }

    [TestMethod]
    public void CompatibilityMismatches_AreTypedAndNonDestructive()
    {
        PersistenceFixture fixture = PersistenceTestData.Create();
        WorldCompatibilityProfile expected = fixture.Compatibility;
        (WorldCompatibilityProfile Profile, PersistenceErrorCode Code)[] cases =
        [
            (expected with { NativeSeed = -1 }, PersistenceErrorCode.IncompatibleSeed),
            (expected with { AlgorithmVersion = 99 }, PersistenceErrorCode.IncompatibleAlgorithm),
            (expected with { SnapshotSchemaVersion = 99 }, PersistenceErrorCode.IncompatibleSchema),
            (expected with { GeographyConfigHash = Hash256.Zero }, PersistenceErrorCode.IncompatibleConfiguration),
            (expected with { GenerationAssetHash = Hash256.Zero }, PersistenceErrorCode.IncompatibleAssets),
            (expected with { DeterminismProfileId = "another-profile" }, PersistenceErrorCode.IncompatibleDeterminismProfile),
            (expected with { Units = new SnapshotUnits("metre", "block/256") }, PersistenceErrorCode.IncompatibleUnits),
        ];
        int writesBefore = fixture.Store.WriteCount;

        foreach ((WorldCompatibilityProfile profile, PersistenceErrorCode code) in cases)
        {
            PersistenceTestData.AssertFailure(fixture.Service.Restore(profile), code);
        }

        Assert.AreEqual(writesBefore, fixture.Store.WriteCount);
    }
}
