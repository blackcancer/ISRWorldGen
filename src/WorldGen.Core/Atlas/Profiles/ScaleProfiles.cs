using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using ISRWorldGen.Core.Atlas.SpatialIndex;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Profiles;

/// <summary>
/// A proposed scale profile. It is a draft until explicit native constraints validate and freeze it.
/// No proposal is a player default.
/// </summary>
public sealed record ScaleProfileDefinition(
    string Id,
    uint ProfileVersion,
    long WidthBlocks,
    long LengthBlocks,
    int HeightBlocks,
    int AtlasResolutionBlocks,
    int AtlasTileSizeBlocks,
    int RequestedSiteCount,
    int SiteQuota,
    long AtlasMemoryBudgetBytes,
    long SeaLevelBlocks,
    long MinimumFloorThicknessBlocks,
    long MinimumCavernCoverBlocks,
    long HeightMarginBlocks);

/// <summary>Audited native limits supplied by the game adapter before world creation or reload.</summary>
public sealed class NativeWorldConstraints
{
    private const int MaximumSupportedHeightCount = 4_096;

    public NativeWorldConstraints(
        string ruleSetId,
        uint ruleSetVersion,
        IReadOnlyList<int> supportedHeights,
        long minimumHorizontalBlocks,
        long maximumHorizontalBlocks,
        int horizontalStepBlocks)
    {
        ArgumentNullException.ThrowIfNull(ruleSetId);
        ArgumentNullException.ThrowIfNull(supportedHeights);
        if (string.IsNullOrWhiteSpace(ruleSetId) ||
            !ruleSetId.IsNormalized(NormalizationForm.FormC) ||
            Encoding.UTF8.GetByteCount(ruleSetId) > 128)
        {
            throw new ArgumentException("Native rule-set ID must be canonical NFC text of at most 128 UTF-8 bytes.", nameof(ruleSetId));
        }

        if (ruleSetVersion == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ruleSetVersion), ruleSetVersion, "Rule-set version must be positive.");
        }

        if (supportedHeights.Count is < 1 or > MaximumSupportedHeightCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(supportedHeights),
                supportedHeights.Count,
                $"Supported height count must be in [1, {MaximumSupportedHeightCount}].");
        }

        if (minimumHorizontalBlocks <= 0 || maximumHorizontalBlocks < minimumHorizontalBlocks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumHorizontalBlocks),
                maximumHorizontalBlocks,
                "Native horizontal bounds must be positive and ordered.");
        }

        if (horizontalStepBlocks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalStepBlocks), horizontalStepBlocks, "Native horizontal step must be positive.");
        }

        int[] heights = new int[supportedHeights.Count];
        for (int index = 0; index < heights.Length; index++)
        {
            heights[index] = supportedHeights[index];
            if (heights[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(supportedHeights), heights[index], "Native heights must be positive.");
            }
        }

        Array.Sort(heights);
        for (int index = 1; index < heights.Length; index++)
        {
            if (heights[index - 1] == heights[index])
            {
                throw new ArgumentException("Native supported heights must be unique.", nameof(supportedHeights));
            }
        }

        RuleSetId = ruleSetId;
        RuleSetVersion = ruleSetVersion;
        SupportedHeights = Array.AsReadOnly(heights);
        MinimumHorizontalBlocks = minimumHorizontalBlocks;
        MaximumHorizontalBlocks = maximumHorizontalBlocks;
        HorizontalStepBlocks = horizontalStepBlocks;
    }

    public string RuleSetId { get; }

    public uint RuleSetVersion { get; }

    public ReadOnlyCollection<int> SupportedHeights { get; }

    public long MinimumHorizontalBlocks { get; }

    public long MaximumHorizontalBlocks { get; }

    public int HorizontalStepBlocks { get; }

    internal bool SupportsHeight(int height) => SupportedHeights.BinarySearch(height) >= 0;
}

public static class ScaleProfileCatalog
{
    public const uint SupportedProfileVersion = 1;

    // L02-B's qualified cold planner reserves 16 MiB plus 65,536 bytes per ordered
    // site pair. Counts 16/64/128 are the largest lower powers of two that keep at
    // least 25% of each declared budget available for graph/index/caller headroom.
    // AtlasResolutionBlocks is the rounded nominal sqrt(world area / site count).
    private static readonly ReadOnlyCollection<ScaleProfileDefinition> Catalog = Array.AsReadOnly(
    new[]
    {
        new ScaleProfileDefinition(
            "laboratory",
            SupportedProfileVersion,
            WidthBlocks: 4_096,
            LengthBlocks: 4_096,
            HeightBlocks: 256,
            AtlasResolutionBlocks: 1_024,
            AtlasTileSizeBlocks: 256,
            RequestedSiteCount: 16,
            SiteQuota: 32,
            AtlasMemoryBudgetBytes: 64L * 1024 * 1024,
            SeaLevelBlocks: 110,
            MinimumFloorThicknessBlocks: 8,
            MinimumCavernCoverBlocks: 12,
            HeightMarginBlocks: 16),
        new ScaleProfileDefinition(
            "balanced",
            SupportedProfileVersion,
            WidthBlocks: 131_072,
            LengthBlocks: 131_072,
            HeightBlocks: 384,
            AtlasResolutionBlocks: 16_384,
            AtlasTileSizeBlocks: 512,
            RequestedSiteCount: 64,
            SiteQuota: 128,
            AtlasMemoryBudgetBytes: 512L * 1024 * 1024,
            SeaLevelBlocks: 168,
            MinimumFloorThicknessBlocks: 12,
            MinimumCavernCoverBlocks: 24,
            HeightMarginBlocks: 24),
        new ScaleProfileDefinition(
            "vast-expeditions",
            SupportedProfileVersion,
            WidthBlocks: 1_024_000,
            LengthBlocks: 1_024_000,
            HeightBlocks: 512,
            AtlasResolutionBlocks: 90_000,
            AtlasTileSizeBlocks: 2_048,
            RequestedSiteCount: 128,
            SiteQuota: 256,
            AtlasMemoryBudgetBytes: 2L * 1024 * 1024 * 1024,
            SeaLevelBlocks: 224,
            MinimumFloorThicknessBlocks: 16,
            MinimumCavernCoverBlocks: 32,
            HeightMarginBlocks: 32),
    });

    /// <summary>Ordered proposals only. The player or caller must select one explicitly.</summary>
    public static ReadOnlyCollection<ScaleProfileDefinition> Proposals => Catalog;
}

/// <summary>A profile validated against explicitly supplied native constraints and frozen for one world.</summary>
public sealed class FrozenScaleProfile : IEquatable<FrozenScaleProfile>
{
    internal FrozenScaleProfile(
        ScaleProfileDefinition definition,
        string nativeRuleSetId,
        uint nativeRuleSetVersion,
        VerticalEnvelope verticalEnvelope,
        AtlasIndexProfile atlasIndexProfile,
        Hash256 geographyConfigHash)
    {
        Definition = definition;
        Id = definition.Id;
        ProfileVersion = definition.ProfileVersion;
        WidthBlocks = definition.WidthBlocks;
        LengthBlocks = definition.LengthBlocks;
        HeightBlocks = definition.HeightBlocks;
        AtlasResolutionBlocks = definition.AtlasResolutionBlocks;
        AtlasTileSizeBlocks = definition.AtlasTileSizeBlocks;
        RequestedSiteCount = definition.RequestedSiteCount;
        SiteQuota = definition.SiteQuota;
        AtlasMemoryBudgetBytes = definition.AtlasMemoryBudgetBytes;
        VerticalEnvelope = verticalEnvelope;
        AtlasIndexProfile = atlasIndexProfile;
        NativeRuleSetId = nativeRuleSetId;
        NativeRuleSetVersion = nativeRuleSetVersion;
        GeographyConfigHash = geographyConfigHash;
    }

    internal ScaleProfileDefinition Definition { get; }

    public string Id { get; }

    public uint ProfileVersion { get; }

    public long WidthBlocks { get; }

    public long LengthBlocks { get; }

    public int HeightBlocks { get; }

    public int AtlasResolutionBlocks { get; }

    public int AtlasTileSizeBlocks { get; }

    public int RequestedSiteCount { get; }

    public int SiteQuota { get; }

    public long AtlasMemoryBudgetBytes { get; }

    public VerticalEnvelope VerticalEnvelope { get; }

    public AtlasIndexProfile AtlasIndexProfile { get; }

    public string NativeRuleSetId { get; }

    public uint NativeRuleSetVersion { get; }

    public Hash256 GeographyConfigHash { get; }

    public bool Equals(FrozenScaleProfile? other) =>
        other is not null &&
        Definition == other.Definition &&
        NativeRuleSetId == other.NativeRuleSetId &&
        NativeRuleSetVersion == other.NativeRuleSetVersion &&
        GeographyConfigHash == other.GeographyConfigHash;

    public override bool Equals(object? obj) => Equals(obj as FrozenScaleProfile);

    public override int GetHashCode() => HashCode.Combine(
        Definition,
        NativeRuleSetId,
        NativeRuleSetVersion,
        GeographyConfigHash);
}

public static class ScaleProfileValidator
{
    public static GenerationResult<FrozenScaleProfile> ValidateAndFreeze(
        ScaleProfileDefinition proposal,
        NativeWorldConstraints nativeConstraints)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(nativeConstraints);
        return ValidateAndFreeze(
            proposal,
            nativeConstraints,
            nativeConstraints.RuleSetId,
            nativeConstraints.RuleSetVersion);
    }

    internal static GenerationResult<FrozenScaleProfile> ValidateAndFreeze(
        ScaleProfileDefinition proposal,
        NativeWorldConstraints nativeConstraints,
        string persistedNativeRuleSetId,
        uint persistedNativeRuleSetVersion)
    {
        Hash256 inputHash = ProfileCanonicalEncoding.TryComputeConfigurationHash(proposal);
        if (proposal.ProfileVersion != ScaleProfileCatalog.SupportedProfileVersion)
        {
            return Fail(
                GenerationFailureCode.UnsupportedVersion,
                "atlas.profile.version",
                inputHash,
                $"Profile version {proposal.ProfileVersion.ToString(CultureInfo.InvariantCulture)} is unsupported.");
        }

        if (!ProfileCanonicalEncoding.IsCanonicalIdentifier(proposal.Id))
        {
            return Fail(GenerationFailureCode.InvalidInput, "atlas.profile.id", inputHash, "Profile ID is not bounded canonical NFC text.");
        }

        if (!HasValidDimensions(proposal, nativeConstraints, out string? dimensionError))
        {
            return Fail(GenerationFailureCode.InvalidInput, "atlas.profile.dimensions", inputHash, dimensionError!);
        }

        if (!nativeConstraints.SupportsHeight(proposal.HeightBlocks))
        {
            return Fail(
                GenerationFailureCode.InvalidInput,
                "atlas.profile.native-height",
                inputHash,
                $"Native rules do not support requested height {proposal.HeightBlocks.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (proposal.AtlasResolutionBlocks <= 0 || proposal.AtlasResolutionBlocks > Math.Min(proposal.WidthBlocks, proposal.LengthBlocks) ||
            proposal.AtlasTileSizeBlocks <= 0 || proposal.AtlasTileSizeBlocks > Math.Min(proposal.WidthBlocks, proposal.LengthBlocks))
        {
            return Fail(
                GenerationFailureCode.InvalidInput,
                "atlas.profile.resolution",
                inputHash,
                "Atlas resolution and tile size must be positive and fit the finite world.");
        }

        if (proposal.RequestedSiteCount <= 0 || proposal.SiteQuota <= 0 ||
            proposal.RequestedSiteCount >= Array.MaxLength || proposal.SiteQuota >= Array.MaxLength ||
            proposal.RequestedSiteCount > proposal.SiteQuota)
        {
            return Fail(
                GenerationFailureCode.BudgetExceeded,
                "atlas.profile.site-quota",
                inputHash,
                "Requested site count must be positive and cannot exceed the explicit quota.");
        }

        if (proposal.AtlasMemoryBudgetBytes <= 0)
        {
            return Fail(
                GenerationFailureCode.BudgetExceeded,
                "atlas.profile.memory-budget",
                inputHash,
                "Atlas memory budget must be positive.");
        }

        VerticalEnvelope verticalEnvelope;
        try
        {
            verticalEnvelope = new VerticalEnvelope(
                proposal.HeightBlocks,
                proposal.SeaLevelBlocks,
                proposal.MinimumFloorThicknessBlocks,
                proposal.MinimumCavernCoverBlocks,
                proposal.HeightMarginBlocks);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            return Fail(
                GenerationFailureCode.InvalidInput,
                "atlas.profile.vertical-envelope",
                inputHash,
                $"Vertical budget is impossible: {exception.Message}");
        }

        var domain = new WorldDomain(
            new Int64Interval(0, proposal.WidthBlocks),
            new Int64Interval(0, proposal.LengthBlocks),
            proposal.HeightBlocks,
            new WorldBlockPosition(0, 0));
        var indexProfile = new AtlasIndexProfile(
            domain,
            new ScaleModel("atlas-block", 1d, "block", 1d),
            proposal.AtlasTileSizeBlocks,
            proposal.RequestedSiteCount,
            proposal.SiteQuota,
            proposal.AtlasMemoryBudgetBytes);
        return GenerationResult<FrozenScaleProfile>.Success(new FrozenScaleProfile(
            proposal,
            persistedNativeRuleSetId,
            persistedNativeRuleSetVersion,
            verticalEnvelope,
            indexProfile,
            ProfileCanonicalEncoding.ComputeConfigurationHash(proposal)));
    }

    private static bool HasValidDimensions(
        ScaleProfileDefinition proposal,
        NativeWorldConstraints constraints,
        out string? error)
    {
        if (proposal.WidthBlocks < constraints.MinimumHorizontalBlocks ||
            proposal.WidthBlocks > constraints.MaximumHorizontalBlocks ||
            proposal.LengthBlocks < constraints.MinimumHorizontalBlocks ||
            proposal.LengthBlocks > constraints.MaximumHorizontalBlocks)
        {
            error = "Horizontal dimensions lie outside explicit native bounds.";
            return false;
        }

        if (proposal.WidthBlocks % constraints.HorizontalStepBlocks != 0 ||
            proposal.LengthBlocks % constraints.HorizontalStepBlocks != 0)
        {
            error = "Horizontal dimensions do not respect the explicit native step.";
            return false;
        }

        BigInteger volume = (BigInteger)proposal.WidthBlocks * proposal.LengthBlocks * proposal.HeightBlocks;
        if (volume <= BigInteger.Zero || volume > long.MaxValue)
        {
            error = "World volume cannot be represented by the qualified 64-bit planning model.";
            return false;
        }

        error = null;
        return true;
    }

    private static GenerationResult<FrozenScaleProfile> Fail(
        GenerationFailureCode code,
        string stage,
        Hash256 inputHash,
        string details) => GenerationResult<FrozenScaleProfile>.Failure(new GenerationError(
            code,
            nativeSeed: 0,
            stage,
            StableId.Zero,
            inputHash,
            details,
            canRetry: false));
}

internal static class ReadOnlyCollectionExtensions
{
    internal static int BinarySearch(this ReadOnlyCollection<int> values, int value)
    {
        int lower = 0;
        int upper = values.Count - 1;
        while (lower <= upper)
        {
            int midpoint = lower + ((upper - lower) / 2);
            int comparison = values[midpoint].CompareTo(value);
            if (comparison == 0)
            {
                return midpoint;
            }

            if (comparison < 0)
            {
                lower = midpoint + 1;
            }
            else
            {
                upper = midpoint - 1;
            }
        }

        return ~lower;
    }
}
