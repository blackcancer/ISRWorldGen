using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Landscapes;

public sealed record ReliefBudgetRequest
{
    public ReliefBudgetRequest(
        long maximumOceanDepthBlocks,
        long minimumCavernInteriorHeightBlocks,
        long maximumReliefAboveSeaBlocks)
    {
        if (maximumOceanDepthBlocks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumOceanDepthBlocks), "Ocean depth must be positive.");
        }

        if (minimumCavernInteriorHeightBlocks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCavernInteriorHeightBlocks),
                "Cavern interior height must be positive.");
        }

        if (maximumReliefAboveSeaBlocks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReliefAboveSeaBlocks), "Relief must be positive.");
        }

        MaximumOceanDepthBlocks = maximumOceanDepthBlocks;
        MinimumCavernInteriorHeightBlocks = minimumCavernInteriorHeightBlocks;
        MaximumReliefAboveSeaBlocks = maximumReliefAboveSeaBlocks;
    }

    public long MaximumOceanDepthBlocks { get; }

    public long MinimumCavernInteriorHeightBlocks { get; }

    public long MaximumReliefAboveSeaBlocks { get; }
}

/// <summary>
/// One shared piecewise-linear altitude transform. Model zero is exactly sea level. The continuous transform is
/// strictly increasing; its 1/256-block boundary representation is necessarily non-decreasing.
/// </summary>
public sealed class AltitudeBlockTransform
{
    internal AltitudeBlockTransform(long seaLevelBlocks, long oceanDepthBlocks, long reliefAboveSeaBlocks)
    {
        SeaLevelBlocks = seaLevelBlocks;
        OceanDepthBlocks = oceanDepthBlocks;
        ReliefAboveSeaBlocks = reliefAboveSeaBlocks;
    }

    public long SeaLevelBlocks { get; }

    public long OceanDepthBlocks { get; }

    public long ReliefAboveSeaBlocks { get; }

    public double MapModelAltitudeToBlocks(double modelAltitudeNormalized)
    {
        if (!double.IsFinite(modelAltitudeNormalized) || modelAltitudeNormalized is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelAltitudeNormalized),
                modelAltitudeNormalized,
                "Model altitude must be finite and belong to [-1,1].");
        }

        double result = modelAltitudeNormalized < 0
            ? SeaLevelBlocks + (modelAltitudeNormalized * OceanDepthBlocks)
            : SeaLevelBlocks + (modelAltitudeNormalized * ReliefAboveSeaBlocks);
        return double.IsFinite(result)
            ? result
            : throw new OverflowException("Mapped altitude exceeded the finite block domain.");
    }

    public long MapModelAltitudeToQuantizedBlocks(double modelAltitudeNormalized) =>
        HeightQuantizer.QuantizeBlocks(MapModelAltitudeToBlocks(modelAltitudeNormalized));
}

public sealed class ReliefVerticalPlan
{
    internal ReliefVerticalPlan(
        FrozenScaleProfile profile,
        ReliefBudgetRequest request,
        long rockFloorTopBlocks,
        long deepestOceanFloorBlocks,
        long maximumCavernCeilingBlocks,
        long highestReliefBlocks,
        AltitudeBlockTransform transform)
    {
        ScaleProfileId = profile.Id;
        ScaleProfileVersion = profile.ProfileVersion;
        GeographyConfigHash = profile.GeographyConfigHash;
        WorldHeightBlocks = profile.VerticalEnvelope.WorldHeight;
        SeaLevelBlocks = profile.VerticalEnvelope.SeaLevel;
        RockFloorThicknessBlocks = profile.VerticalEnvelope.MinimumFloorThickness;
        MinimumCavernCoverBlocks = profile.VerticalEnvelope.MinimumCavernCover;
        UpperMarginBlocks = profile.VerticalEnvelope.HeightMargin;
        RockFloorTopBlocks = rockFloorTopBlocks;
        DeepestOceanFloorBlocks = deepestOceanFloorBlocks;
        MaximumCavernCeilingBlocks = maximumCavernCeilingBlocks;
        HighestReliefBlocks = highestReliefBlocks;
        MaximumOceanDepthBlocks = request.MaximumOceanDepthBlocks;
        MinimumCavernInteriorHeightBlocks = request.MinimumCavernInteriorHeightBlocks;
        MaximumReliefAboveSeaBlocks = request.MaximumReliefAboveSeaBlocks;
        Transform = transform;
    }

    public string ScaleProfileId { get; }

    public uint ScaleProfileVersion { get; }

    public Hash256 GeographyConfigHash { get; }

    public long WorldHeightBlocks { get; }

    public long SeaLevelBlocks { get; }

    public long RockFloorThicknessBlocks { get; }

    public long MinimumCavernCoverBlocks { get; }

    public long UpperMarginBlocks { get; }

    public long RockFloorTopBlocks { get; }

    public long DeepestOceanFloorBlocks { get; }

    public long MaximumCavernCeilingBlocks { get; }

    public long HighestReliefBlocks { get; }

    public long MaximumOceanDepthBlocks { get; }

    public long MinimumCavernInteriorHeightBlocks { get; }

    public long MaximumReliefAboveSeaBlocks { get; }

    public AltitudeBlockTransform Transform { get; }
}

public static class ReliefVerticalBudgetPlanner
{
    public static GenerationResult<ReliefVerticalPlan> Create(
        GenerationIdentity identity,
        FrozenScaleProfile profile,
        ReliefBudgetRequest request)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (identity.GeographyConfigHash != profile.GeographyConfigHash)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.profile-hash",
                "Generation identity and frozen scale profile have different geography configuration hashes.");
        }

        VerticalEnvelope envelope = profile.VerticalEnvelope;
        long deepestOceanFloor;
        long maximumCavernCeiling;
        long cavernInteriorHeight;
        try
        {
            deepestOceanFloor = checked(envelope.SeaLevel - request.MaximumOceanDepthBlocks);
            maximumCavernCeiling = checked(deepestOceanFloor - envelope.MinimumCavernCover);
            cavernInteriorHeight = checked(maximumCavernCeiling - envelope.MinimumFloorThickness);
        }
        catch (OverflowException)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.landscapes.vertical-below-sea",
                "Below-sea reservations overflowed the supported block range.");
        }

        if (deepestOceanFloor <= envelope.MinimumFloorThickness ||
            cavernInteriorHeight < request.MinimumCavernInteriorHeightBlocks)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.landscapes.vertical-below-sea",
                $"Floor, cavern interior, cavern cover and ocean depth require more than sea level {envelope.SeaLevel} permits.");
        }

        long highestRelief;
        try
        {
            highestRelief = checked(envelope.SeaLevel + request.MaximumReliefAboveSeaBlocks);
        }
        catch (OverflowException)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.landscapes.vertical-above-sea",
                "Above-sea relief overflowed the supported block range.");
        }

        if (highestRelief >= envelope.MaximumExclusiveUsableLevel)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.landscapes.vertical-above-sea",
                $"Relief reaches {highestRelief}, outside the upper-margin bound {envelope.MaximumExclusiveUsableLevel} exclusive.");
        }

        var transform = new AltitudeBlockTransform(
            envelope.SeaLevel,
            request.MaximumOceanDepthBlocks,
            request.MaximumReliefAboveSeaBlocks);
        return GenerationResult<ReliefVerticalPlan>.Success(new ReliefVerticalPlan(
            profile,
            request,
            envelope.MinimumFloorThickness,
            deepestOceanFloor,
            maximumCavernCeiling,
            highestRelief,
            transform));
    }

    private static GenerationResult<ReliefVerticalPlan> Failure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details) => GenerationResult<ReliefVerticalPlan>.Failure(new GenerationError(
            code,
            identity.NativeSeed,
            stage,
            StableId.Zero,
            identity.GeographyConfigHash,
            details,
            false));
}
