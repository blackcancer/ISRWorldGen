using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02C;

internal static class ProfileTestSupport
{
    internal static NativeWorldConstraints QualifiedNativeConstraints() => new(
        "fixture-native-v1",
        ruleSetVersion: 1,
        supportedHeights: new[] { 256, 384, 512 },
        minimumHorizontalBlocks: 4_096,
        maximumHorizontalBlocks: 1_024_000,
        horizontalStepBlocks: 512);

    internal static ScaleProfileDefinition Balanced() =>
        ScaleProfileCatalog.Proposals.Single(profile => profile.Id == "balanced");

    internal static FrozenScaleProfile FreezeBalanced() =>
        Success(ScaleProfileValidator.ValidateAndFreeze(Balanced(), QualifiedNativeConstraints()));

    internal static GenerationIdentity Identity() => new(
        nativeSeed: 73,
        GenerationIdentity.SupportedAlgorithmVersion,
        GenerationIdentity.SupportedSchemaVersion,
        Hash256.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
        Hash256.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
        "net10-x64-l02c-v1");

    internal static T Success<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationSuccess<T>>(result);
        return ((GenerationSuccess<T>)result).Snapshot;
    }

    internal static GenerationFailure<T> Failure<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationFailure<T>>(result);
        return (GenerationFailure<T>)result;
    }
}
