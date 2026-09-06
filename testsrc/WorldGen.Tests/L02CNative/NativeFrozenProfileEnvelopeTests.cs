using System.Buffers.Binary;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.ScaleProfiles;

namespace ISRWorldGen.Tests.L02CNative;

[TestClass]
public sealed class NativeFrozenProfileEnvelopeTests
{
    [TestMethod]
    public void CanonicalRoundTrip_PreservesWorldBindingHashAndProfileBytes()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        FrozenScaleProfile profile = FreezeLaboratory(world);
        byte[] profileBytes = FrozenScaleProfileCodec.Serialize(profile);

        byte[] first = NativeFrozenProfileEnvelopeCodec.Encode(world, profile);
        NativeProfileResult<NativeFrozenProfileEnvelope> decoded = NativeFrozenProfileEnvelopeCodec.Decode(first);

        Assert.IsTrue(decoded.IsSuccess);
        Assert.AreEqual(world.SavegameIdentifier, decoded.Value!.SavegameIdentifier);
        Assert.AreEqual(world.MapSizeX, decoded.Value.MapSizeX);
        Assert.AreEqual(world.MapSizeY, decoded.Value.MapSizeY);
        Assert.AreEqual(world.MapSizeZ, decoded.Value.MapSizeZ);
        Assert.AreEqual(world.ChunkSize, decoded.Value.ChunkSize);
        Assert.AreEqual(profile.GeographyConfigHash, decoded.Value.GeographyConfigHash);
        Assert.AreEqual(NativeFrozenProfileEnvelopeCodec.FrozenProfileCodecId, decoded.Value.ProfileCodecId);
        Assert.AreEqual(NativeFrozenProfileEnvelopeCodec.FrozenProfileCodecVersion, decoded.Value.ProfileCodecVersion);
        Assert.AreEqual(NativeProfilePersistenceState.Committed, decoded.Value.PersistenceState);
        CollectionAssert.AreEqual(profileBytes, decoded.Value.GetProfileBytesCopy());
        CollectionAssert.AreEqual(first, NativeFrozenProfileEnvelopeCodec.Encode(decoded.Value));
    }

    [TestMethod]
    public void EveryTruncationAndTrailingByte_IsRejected()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        byte[] valid = NativeFrozenProfileEnvelopeCodec.Encode(world, FreezeLaboratory(world));

        for (int length = 0; length < valid.Length; length++)
        {
            NativeProfileResult<NativeFrozenProfileEnvelope> result =
                NativeFrozenProfileEnvelopeCodec.Decode(valid.AsSpan(0, length));
            Assert.IsFalse(result.IsSuccess, $"Truncation length {length} unexpectedly succeeded.");
        }

        NativeProfileResult<NativeFrozenProfileEnvelope> trailing =
            NativeFrozenProfileEnvelopeCodec.Decode([.. valid, 0]);
        Assert.IsFalse(trailing.IsSuccess);
        Assert.AreEqual("native-profile.envelope-format", trailing.Error!.Stage);
    }

    [TestMethod]
    public void ChecksumCorruptionAndForgedLengths_AreTypedBoundedFailures()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        byte[] valid = NativeFrozenProfileEnvelopeCodec.Encode(world, FreezeLaboratory(world));
        byte[] corrupt = valid.ToArray();
        corrupt[^1] ^= 0xff;
        byte[] forged = valid.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(forged.AsSpan(12, 4), int.MaxValue);

        NativeProfileResult<NativeFrozenProfileEnvelope> corruptResult =
            NativeFrozenProfileEnvelopeCodec.Decode(corrupt);
        NativeProfileResult<NativeFrozenProfileEnvelope> forgedResult =
            NativeFrozenProfileEnvelopeCodec.Decode(forged);

        Assert.IsFalse(corruptResult.IsSuccess);
        Assert.AreEqual("native-profile.envelope-checksum", corruptResult.Error!.Stage);
        Assert.IsFalse(forgedResult.IsSuccess);
        Assert.AreEqual("native-profile.envelope-format", forgedResult.Error!.Stage);
    }

    [TestMethod]
    public void InvalidWorldIdentityAndUnknownVersion_AreRejected()
    {
        NativeWorldSnapshot world = NativeProfileTestSupport.NewLaboratoryWorld();
        FrozenScaleProfile profile = FreezeLaboratory(world);
        Assert.ThrowsExactly<ArgumentException>(() => NativeFrozenProfileEnvelopeCodec.Encode(
            world with { SavegameIdentifier = "\ud800" },
            profile));

        byte[] unknownVersion = NativeFrozenProfileEnvelopeCodec.Encode(world, profile);
        BinaryPrimitives.WriteUInt32BigEndian(unknownVersion.AsSpan(8, 4), 999);

        NativeProfileResult<NativeFrozenProfileEnvelope> result =
            NativeFrozenProfileEnvelopeCodec.Decode(unknownVersion);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(GenerationFailureCode.UnsupportedVersion, result.Error!.Code);
        Assert.AreEqual("native-profile.envelope-version", result.Error.Stage);
    }

    private static FrozenScaleProfile FreezeLaboratory(NativeWorldSnapshot world)
    {
        NativeWorldConstraints constraints = world.CreateConstraints();
        GenerationResult<FrozenScaleProfile> result = ScaleProfileValidator.ValidateAndFreeze(
            ScaleProfileCatalog.Proposals.Single(profile => profile.Id == "laboratory"),
            constraints);
        Assert.IsInstanceOfType<GenerationSuccess<FrozenScaleProfile>>(result);
        return ((GenerationSuccess<FrozenScaleProfile>)result).Snapshot;
    }
}
