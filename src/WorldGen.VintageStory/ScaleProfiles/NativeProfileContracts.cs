using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.ScaleProfiles;

internal enum NativeProfileState
{
    Uninitialized,
    Validating,
    Inactive,
    Frozen,
    Rejected
}

internal sealed record NativeWorldSnapshot
{
    internal NativeWorldSnapshot(
        string savegameIdentifier,
        bool isNew,
        int mapSizeX,
        int mapSizeY,
        int mapSizeZ,
        int chunkSize,
        int maximumWorldSizeXz,
        int maximumWorldSizeY,
        string nativeRuleSetId,
        uint nativeRuleSetVersion)
    {
        SavegameIdentifier = savegameIdentifier;
        IsNew = isNew;
        MapSizeX = mapSizeX;
        MapSizeY = mapSizeY;
        MapSizeZ = mapSizeZ;
        ChunkSize = chunkSize;
        MaximumWorldSizeXz = maximumWorldSizeXz;
        MaximumWorldSizeY = maximumWorldSizeY;
        NativeRuleSetId = nativeRuleSetId;
        NativeRuleSetVersion = nativeRuleSetVersion;
    }

    internal string SavegameIdentifier { get; init; }

    internal bool IsNew { get; init; }

    internal int MapSizeX { get; init; }

    internal int MapSizeY { get; init; }

    internal int MapSizeZ { get; init; }

    internal int ChunkSize { get; init; }

    internal int MaximumWorldSizeXz { get; init; }

    internal int MaximumWorldSizeY { get; init; }

    internal string NativeRuleSetId { get; init; }

    internal uint NativeRuleSetVersion { get; init; }

    internal NativeWorldConstraints CreateConstraints() => new(
        NativeRuleSetId,
        NativeRuleSetVersion,
        [MapSizeY],
        ChunkSize,
        MaximumWorldSizeXz,
        ChunkSize);
}

internal readonly record struct NativeProfileSelection
{
    internal NativeProfileSelection(bool isSpecified, string? profileId)
    {
        IsSpecified = isSpecified;
        ProfileId = profileId;
    }

    internal bool IsSpecified { get; }

    internal string? ProfileId { get; }
}

internal interface IFrozenProfileStore
{
    byte[]? Read();

    void Write(ReadOnlySpan<byte> content);
}

internal sealed record NativeProfileError(GenerationFailureCode Code, string Stage, string Details);

internal sealed class NativeProfileResult<T>
    where T : class
{
    private NativeProfileResult(T? value, NativeProfileError? error)
    {
        Value = value;
        Error = error;
    }

    internal bool IsSuccess => Value is not null;

    internal T? Value { get; }

    internal NativeProfileError? Error { get; }

    internal static NativeProfileResult<T> Success(T value) => new(value, null);

    internal static NativeProfileResult<T> Failure(
        GenerationFailureCode code,
        string stage,
        string details) => new(null, new NativeProfileError(code, stage, details));
}

internal sealed record NativeProfilePreparation(
    NativeProfileState State,
    FrozenScaleProfile? Profile,
    NativeProfileError? Error);

internal sealed record NativeWorldgenGateObservation(
    bool CanGenerate,
    NativeProfileState State,
    string? ProfileId);
