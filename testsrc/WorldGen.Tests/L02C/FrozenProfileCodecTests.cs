using System.Buffers.Binary;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Tests.L02C;

[TestClass]
public sealed class FrozenProfileCodecTests
{
    [TestMethod]
    public void CanonicalRoundTrip_ReproducesBytesAndGeographyHash()
    {
        FrozenScaleProfile original = ProfileTestSupport.FreezeBalanced();
        byte[] first = FrozenScaleProfileCodec.Serialize(original);
        FrozenScaleProfile restored = ProfileTestSupport.Success(FrozenScaleProfileCodec.Reload(
            first,
            original.GeographyConfigHash,
            ProfileTestSupport.QualifiedNativeConstraints()));
        byte[] second = FrozenScaleProfileCodec.Serialize(restored);

        CollectionAssert.AreEqual(first, second);
        Assert.AreEqual(original.GeographyConfigHash, restored.GeographyConfigHash);
        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void SerializedBytesAndInputsCannotMutateFrozenProfile()
    {
        ScaleProfileDefinition draft = ProfileTestSupport.Balanced();
        FrozenScaleProfile frozen = ProfileTestSupport.Success(ScaleProfileValidator.ValidateAndFreeze(
            draft,
            ProfileTestSupport.QualifiedNativeConstraints()));
        byte[] before = FrozenScaleProfileCodec.Serialize(frozen);

        draft = draft with { SeaLevelBlocks = 1 };
        byte[] callerBytes = FrozenScaleProfileCodec.Serialize(frozen);
        callerBytes[^1] ^= 0xff;

        CollectionAssert.AreEqual(before, FrozenScaleProfileCodec.Serialize(frozen));
        Assert.AreNotEqual(draft.SeaLevelBlocks, frozen.VerticalEnvelope.SeaLevel);
    }

    [TestMethod]
    public void CorruptionAndTrailingBytes_AreTypedFailures()
    {
        FrozenScaleProfile frozen = ProfileTestSupport.FreezeBalanced();
        byte[] valid = FrozenScaleProfileCodec.Serialize(frozen);
        byte[] corrupt = valid.ToArray();
        corrupt[^1] ^= 0xff;

        GenerationFailure<FrozenScaleProfile> checksumFailure = ProfileTestSupport.Failure(
            FrozenScaleProfileCodec.Reload(
                corrupt,
                frozen.GeographyConfigHash,
                ProfileTestSupport.QualifiedNativeConstraints()));
        GenerationFailure<FrozenScaleProfile> trailingFailure = ProfileTestSupport.Failure(
            FrozenScaleProfileCodec.Reload(
                [.. valid, 0],
                frozen.GeographyConfigHash,
                ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.CorruptData, checksumFailure.Error.Code);
        Assert.AreEqual("atlas.profile.manifest-checksum", checksumFailure.Error.Stage);
        Assert.AreEqual(GenerationFailureCode.CorruptData, trailingFailure.Error.Code);
    }

    [TestMethod]
    public void ValidDifferentManifest_IsRejectedAsFrozenWorldMutation()
    {
        FrozenScaleProfile expected = ProfileTestSupport.FreezeBalanced();
        FrozenScaleProfile different = ProfileTestSupport.Success(ScaleProfileValidator.ValidateAndFreeze(
            ProfileTestSupport.Balanced() with { SeaLevelBlocks = 167 },
            ProfileTestSupport.QualifiedNativeConstraints()));

        GenerationFailure<FrozenScaleProfile> failure = ProfileTestSupport.Failure(
            FrozenScaleProfileCodec.Reload(
                FrozenScaleProfileCodec.Serialize(different),
                expected.GeographyConfigHash,
                ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.CorruptData, failure.Error.Code);
        Assert.AreEqual("atlas.profile.frozen-mutation", failure.Error.Stage);
    }

    [TestMethod]
    public void UnsupportedEnvelopeAndProfileVersions_AreTypedFailures()
    {
        FrozenScaleProfile frozen = ProfileTestSupport.FreezeBalanced();
        byte[] unknownEnvelope = FrozenScaleProfileCodec.Serialize(frozen);
        BinaryPrimitives.WriteUInt32BigEndian(unknownEnvelope.AsSpan(8, 4), 999);

        GenerationFailure<FrozenScaleProfile> envelopeFailure = ProfileTestSupport.Failure(
            FrozenScaleProfileCodec.Reload(
                unknownEnvelope,
                frozen.GeographyConfigHash,
                ProfileTestSupport.QualifiedNativeConstraints()));
        GenerationFailure<FrozenScaleProfile> profileFailure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(
                ProfileTestSupport.Balanced() with { ProfileVersion = 999 },
                ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.UnsupportedVersion, envelopeFailure.Error.Code);
        Assert.AreEqual("atlas.profile.manifest-version", envelopeFailure.Error.Stage);
        Assert.AreEqual(GenerationFailureCode.UnsupportedVersion, profileFailure.Error.Code);
        Assert.AreEqual("atlas.profile.version", profileFailure.Error.Stage);
    }

    [TestMethod]
    public void Reload_RevalidatesCurrentNativeHeightSupport()
    {
        FrozenScaleProfile frozen = ProfileTestSupport.FreezeBalanced();
        var incompatibleNative = new NativeWorldConstraints(
            "test-native-v2",
            ruleSetVersion: 2,
            supportedHeights: new[] { 256, 512 },
            minimumHorizontalBlocks: 4_096,
            maximumHorizontalBlocks: 1_024_000,
            horizontalStepBlocks: 512);

        GenerationFailure<FrozenScaleProfile> failure = ProfileTestSupport.Failure(
            FrozenScaleProfileCodec.Reload(
                FrozenScaleProfileCodec.Serialize(frozen),
                frozen.GeographyConfigHash,
                incompatibleNative));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.profile.native-height", failure.Error.Stage);
    }
}
