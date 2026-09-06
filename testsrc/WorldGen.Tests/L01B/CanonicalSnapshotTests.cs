using System.Buffers.Binary;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L01B;

[TestClass]
public sealed class CanonicalSnapshotTests
{
    [TestMethod]
    public void EquivalentInsertionOrders_SerializeToIdenticalBytes()
    {
        HeightSampleInput[] forward = SnapshotTestData.Inputs.ToArray();
        HeightSampleInput[] reverse = forward.Reverse().ToArray();

        byte[] forwardBytes = HeightSnapshotBinaryCodec.Serialize(
            SnapshotTestData.CreateSnapshot(forward, SnapshotTestData.ParentHashes));
        byte[] reverseBytes = HeightSnapshotBinaryCodec.Serialize(
            SnapshotTestData.CreateSnapshot(reverse, SnapshotTestData.ParentHashes.Reverse()));

        CollectionAssert.AreEqual(forwardBytes, reverseBytes);
    }

    [TestMethod]
    public void RoundTripAndReserialization_AreByteIdentical()
    {
        HeightSnapshot original = SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs);
        byte[] first = HeightSnapshotBinaryCodec.Serialize(original);
        HeightSnapshot restored = HeightSnapshotBinaryCodec.Deserialize(first);
        byte[] second = HeightSnapshotBinaryCodec.Serialize(restored);

        CollectionAssert.AreEqual(first, second);
        Assert.AreEqual(original.Header.ContentChecksum, restored.Header.ContentChecksum);
        Assert.AreEqual(original.Header.Identity, restored.Header.Identity);
        Assert.HasCount(original.Samples.Count, restored.Samples);
    }

    [TestMethod]
    public void Snapshot_DefensivelyCopiesInputsAndPublishesReadOnlyCollections()
    {
        HeightSampleInput[] mutableSamples = SnapshotTestData.Inputs.ToArray();
        Hash256[] mutableParents = SnapshotTestData.ParentHashes.ToArray();
        HeightSnapshot snapshot = SnapshotTestData.CreateSnapshot(mutableSamples, mutableParents);
        byte[] beforeMutation = HeightSnapshotBinaryCodec.Serialize(snapshot);

        mutableSamples[0] = new HeightSampleInput(StableId.Zero, long.MaxValue, long.MaxValue, 999);
        mutableParents[0] = Hash256.Zero;

        CollectionAssert.AreEqual(beforeMutation, HeightSnapshotBinaryCodec.Serialize(snapshot));
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<HeightSample>)snapshot.Samples)[0] = default);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<Hash256>)snapshot.Header.ParentHashes)[0] = default);
    }

    [TestMethod]
    public void DuplicateSampleIds_AreRejectedRegardlessOfPayloadEquality()
    {
        HeightSampleInput sample = SnapshotTestData.Inputs[0];

        Assert.ThrowsExactly<DuplicateStableIdException>(() =>
            SnapshotTestData.CreateSnapshot([sample, sample]));
        Assert.ThrowsExactly<DuplicateStableIdException>(() =>
            SnapshotTestData.CreateSnapshot(
            [
                sample,
                sample with { X = 0, HeightBlocks = sample.HeightBlocks + 1 },
            ]));
    }

    [TestMethod]
    public void DuplicateParentHashes_AreRejected()
    {
        Hash256 parent = SnapshotTestData.ParentHashes[0];
        Assert.ThrowsExactly<DuplicateParentHashException>(() =>
            SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs, [parent, parent]));
    }

    [TestMethod]
    public void NonFiniteHeights_AreRejected()
    {
        HeightSampleInput basis = SnapshotTestData.Inputs[0];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot([basis with { HeightBlocks = double.NaN }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot([basis with { HeightBlocks = double.PositiveInfinity }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot([basis with { HeightBlocks = double.NegativeInfinity }]));
    }

    [TestMethod]
    public void UnknownEnvelopeSchemaAndAlgorithmVersions_AreRejected()
    {
        byte[] valid = HeightSnapshotBinaryCodec.Serialize(SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs));
        byte[] unknownEnvelope = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(unknownEnvelope.AsSpan(HeightSnapshotBinaryCodec.MagicByteCount, 4), 999);
        byte[] unknownAlgorithm = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(unknownAlgorithm.AsSpan(16, 4), 999);
        byte[] unknownSchema = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(unknownSchema.AsSpan(20, 4), 999);

        Assert.ThrowsExactly<UnsupportedSnapshotVersionException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(unknownEnvelope));
        Assert.ThrowsExactly<UnsupportedAlgorithmVersionException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(unknownAlgorithm));
        Assert.ThrowsExactly<UnsupportedSnapshotVersionException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(unknownSchema));
    }

    [TestMethod]
    public void CorruptionAndTrailingData_AreRejected()
    {
        byte[] valid = HeightSnapshotBinaryCodec.Serialize(SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs));
        byte[] corrupt = valid.ToArray();
        corrupt[^1] ^= 0xff;

        Assert.ThrowsExactly<CorruptSnapshotException>(() => HeightSnapshotBinaryCodec.Deserialize(corrupt));
        Assert.ThrowsExactly<CorruptSnapshotException>(() =>
            HeightSnapshotBinaryCodec.Deserialize([.. valid, 0x00]));
    }

    [TestMethod]
    public void TruncatedPayloadAndForgedLengthsAndCounts_AreRejected()
    {
        HeightSnapshot snapshot = SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs);
        byte[] valid = HeightSnapshotBinaryCodec.Serialize(snapshot);

        Assert.ThrowsExactly<CorruptSnapshotException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(valid.AsSpan(0, valid.Length - 1)));

        byte[] forgedTextLength = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(forgedTextLength.AsSpan(88, 4), uint.MaxValue);
        Assert.ThrowsExactly<CorruptSnapshotException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(forgedTextLength));

        int parentCountOffset = FindParentCountOffset(valid);
        byte[] forgedParentCount = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(forgedParentCount.AsSpan(parentCountOffset, 4), uint.MaxValue);
        Assert.ThrowsExactly<CorruptSnapshotException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(forgedParentCount));

        int firstSampleOffset = FindFirstSampleOffset(valid, snapshot);
        byte[] forgedSampleCount = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(forgedSampleCount.AsSpan(firstSampleOffset - 4, 4), uint.MaxValue);
        Assert.ThrowsExactly<CorruptSnapshotException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(forgedSampleCount));

        byte[] forgedChecksum = valid.ToArray();
        forgedChecksum[firstSampleOffset - 4 - Hash256.ByteWidth] ^= 0x80;
        Assert.ThrowsExactly<CorruptSnapshotException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(forgedChecksum));
    }

    [TestMethod]
    public void EncodedDuplicateIdsAndNoncanonicalOrder_AreRejectedBeforeNormalization()
    {
        HeightSnapshot snapshot = SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs);
        byte[] valid = HeightSnapshotBinaryCodec.Serialize(snapshot);
        Span<byte> firstId = stackalloc byte[StableId.ByteWidth];
        snapshot.Samples[0].Id.WriteCanonicalBytes(firstId);
        int firstSampleOffset = FindFirstSampleOffset(valid, snapshot);

        byte[] duplicate = valid.ToArray();
        duplicate.AsSpan(firstSampleOffset, StableId.ByteWidth)
            .CopyTo(duplicate.AsSpan(firstSampleOffset + 40, StableId.ByteWidth));
        Assert.ThrowsExactly<DuplicateStableIdException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(duplicate));

        byte[] reversed = valid.ToArray();
        byte[] firstSample = reversed.AsSpan(firstSampleOffset, 40).ToArray();
        reversed.AsSpan(firstSampleOffset + 40, 40).CopyTo(reversed.AsSpan(firstSampleOffset, 40));
        firstSample.CopyTo(reversed.AsSpan(firstSampleOffset + 40, 40));
        Assert.ThrowsExactly<CorruptSnapshotException>(() =>
            HeightSnapshotBinaryCodec.Deserialize(reversed));
    }

    [TestMethod]
    public void DimensionsUnitsAndIdentity_RejectInvalidContractData()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SnapshotDimensions(0, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SnapshotDimensions(1, -1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SnapshotDimensions(1, 1, 0));
        Assert.ThrowsExactly<OverflowException>(() => new SnapshotDimensions(long.MaxValue, 1, 2));
        Assert.ThrowsExactly<ArgumentException>(() => new SnapshotUnits("", "block/256"));
        Assert.ThrowsExactly<ArgumentException>(() => new SnapshotUnits("block", "block\u0301"));
        Assert.ThrowsExactly<UnsupportedSnapshotVersionException>(() => SnapshotTestData.CreateIdentity(schemaVersion: 99));
        Assert.ThrowsExactly<UnsupportedAlgorithmVersionException>(() => SnapshotTestData.CreateIdentity(algorithmVersion: 99));
    }

    [TestMethod]
    public void GeographicIdentity_SurfaceExcludesTimePathsAccountsAndWorkerCount()
    {
        string[] expectedProperties =
        [
            nameof(GenerationIdentity.NativeSeed),
            nameof(GenerationIdentity.AlgorithmVersion),
            nameof(GenerationIdentity.SchemaVersion),
            nameof(GenerationIdentity.GeographyConfigHash),
            nameof(GenerationIdentity.GenerationAssetHash),
            nameof(GenerationIdentity.DeterminismProfileId),
        ];
        string[] actualProperties = typeof(GenerationIdentity)
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(expectedProperties, actualProperties);
    }

    [TestMethod]
    public void HeightQuantization_UsesOneOver256BlockAndTiesToEven()
    {
        Assert.AreEqual(0L, HeightQuantizer.QuantizeBlocks(0.5 / 256));
        Assert.AreEqual(2L, HeightQuantizer.QuantizeBlocks(1.5 / 256));
        Assert.AreEqual(0L, HeightQuantizer.QuantizeBlocks(-0.5 / 256));
        Assert.AreEqual(-2L, HeightQuantizer.QuantizeBlocks(-1.5 / 256));
        Assert.AreEqual(320L, HeightQuantizer.QuantizeBlocks(1.25));
        Assert.AreEqual(1.25, HeightQuantizer.DequantizeBlocks(320));
        Assert.ThrowsExactly<OverflowException>(() => HeightQuantizer.QuantizeBlocks(double.MaxValue));
    }

    [TestMethod]
    public void QuantizedSamples_RespectSemiOpenHorizontalAndVerticalBoundaries()
    {
        HeightSampleInput basis = SnapshotTestData.Inputs[0];
        const double halfQuantum = 0.5 / HeightQuantizer.UnitsPerBlock;

        _ = SnapshotTestData.CreateSnapshot([basis with { X = 0, Z = 0, HeightBlocks = -halfQuantum }]);
        _ = SnapshotTestData.CreateSnapshot(
            [basis with { X = 1, Z = 1, HeightBlocks = 384 - (0.5001 / HeightQuantizer.UnitsPerBlock) }]);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot([basis with { X = -1 }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot([basis with { X = 2 }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot([basis with { Z = 2 }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot(
                [basis with { HeightBlocks = -(0.5001 / HeightQuantizer.UnitsPerBlock) }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot(
                [basis with { HeightBlocks = 384 - halfQuantum }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SnapshotTestData.CreateSnapshot([basis with { HeightBlocks = 384 }]));
    }

    [TestMethod]
    public void GoldenSnapshot_HasAnnotatedFrozenCanonicalHash()
    {
        // GOLDEN L01B-SNAPSHOT-V1. Input is SnapshotTestData: seed 73, revision 4,
        // two sorted parent hashes, 4 height samples, units block and block/256.
        // Deliberately manual: this value must never be regenerated automatically.
        const string expectedCanonicalSha256 = "33102761b5193e0f2cb19c8e629f78d5e725ff722e3ac50a13d10c5c89a7f6a5";

        byte[] bytes = HeightSnapshotBinaryCodec.Serialize(SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs));
        Console.WriteLine($"L01B_GOLDEN_SHA256={Hash256.Compute(bytes)}");

        Assert.AreEqual(expectedCanonicalSha256, Hash256.Compute(bytes).ToString());
    }

    private static int FindFirstSampleOffset(byte[] bytes, HeightSnapshot snapshot)
    {
        Span<byte> firstId = stackalloc byte[StableId.ByteWidth];
        snapshot.Samples[0].Id.WriteCanonicalBytes(firstId);
        int offset = bytes.AsSpan().IndexOf(firstId);
        if (offset < 0)
        {
            Assert.Fail("The first canonical sample ID was not found in the serialized snapshot.");
        }

        return offset;
    }

    private static int FindParentCountOffset(byte[] bytes)
    {
        const int profileLengthOffset = 88;
        int profileLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(profileLengthOffset, 4)));
        int stageLengthOffset = checked(profileLengthOffset + 4 + profileLength);
        int stageLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(stageLengthOffset, 4)));
        return checked(stageLengthOffset + 4 + stageLength + 8);
    }
}

internal static class SnapshotTestData
{
    internal static readonly IReadOnlyList<Hash256> ParentHashes =
    [
        Hash256.Parse("ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100"),
        Hash256.Parse("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff"),
    ];

    internal static readonly IReadOnlyList<HeightSampleInput> Inputs =
    [
        Input(RandomDomain.Hydrology, 3, 1, 1, 63.501953125),
        Input(RandomDomain.Hydrology, 0, 0, 0, -0.001953125),
        Input(RandomDomain.Hydrology, 2, 0, 1, 63.498046875),
        Input(RandomDomain.Hydrology, 1, 1, 0, 64.25),
    ];

    internal static GenerationIdentity CreateIdentity(uint schemaVersion = 1, uint algorithmVersion = 1) =>
        new(
            nativeSeed: 73,
            algorithmVersion,
            schemaVersion,
            Hash256.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            Hash256.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
            "net10-x64-l01b-v1");

    internal static HeightSnapshot CreateSnapshot(
        IEnumerable<HeightSampleInput> inputs,
        IEnumerable<Hash256>? parents = null) =>
        HeightSnapshot.Create(
            CreateIdentity(),
            "l01b.synthetic-height",
            revision: 4,
            parents ?? ParentHashes,
            new SnapshotDimensions(width: 2, length: 2, height: 384),
            new SnapshotUnits("block", "block/256"),
            inputs);

    private static HeightSampleInput Input(RandomDomain domain, ulong index, long x, long z, double height) =>
        new(StableId.Derive(domain, StableId.Zero, index), x, z, height);
}
