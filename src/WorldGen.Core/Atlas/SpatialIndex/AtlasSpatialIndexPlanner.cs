using System.Collections.ObjectModel;
using System.Numerics;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.SpatialIndex;

/// <summary>Conservative, deterministic allocation plan computed before atlas generation.</summary>
public sealed class AtlasMemoryEstimate
{
    internal AtlasMemoryEstimate(
        long worldColumnCount,
        long worldVoxelCount,
        int siteCount,
        int primitiveCount,
        int primitivePointCount,
        long placementReferenceCount,
        long estimatedCanonicalCaptureBytes,
        long estimatedSiteBytes,
        long estimatedCompactGraphBytes,
        long estimatedSpatialIndexBytes,
        long estimatedGeometryWorkingBytes,
        long estimatedSnapshotBytes,
        long estimatedPeakBuildBytes)
    {
        WorldColumnCount = worldColumnCount;
        WorldVoxelCount = worldVoxelCount;
        SiteCount = siteCount;
        PrimitiveCount = primitiveCount;
        PrimitivePointCount = primitivePointCount;
        PlacementReferenceCount = placementReferenceCount;
        EstimatedCanonicalCaptureBytes = estimatedCanonicalCaptureBytes;
        EstimatedSiteBytes = estimatedSiteBytes;
        EstimatedCompactGraphBytes = estimatedCompactGraphBytes;
        EstimatedSpatialIndexBytes = estimatedSpatialIndexBytes;
        EstimatedGeometryWorkingBytes = estimatedGeometryWorkingBytes;
        EstimatedSnapshotBytes = estimatedSnapshotBytes;
        EstimatedPeakBuildBytes = estimatedPeakBuildBytes;
    }

    public long WorldColumnCount { get; }

    public long WorldVoxelCount { get; }

    public int SiteCount { get; }

    public int PrimitiveCount { get; }

    public int PrimitivePointCount { get; }

    public long PlacementReferenceCount { get; }

    public long EstimatedCanonicalCaptureBytes { get; }

    public long EstimatedSiteBytes { get; }

    public long EstimatedCompactGraphBytes { get; }

    public long EstimatedSpatialIndexBytes { get; }

    public long EstimatedGeometryWorkingBytes { get; }

    public long EstimatedSnapshotBytes { get; }

    public long EstimatedPeakBuildBytes { get; }
}

public static class AtlasSpatialIndexPlanner
{
    private const long ManagedArrayHeaderBytes = 24;
    private const long ReadOnlyCollectionWrapperBytes = 32;
    private const long SnapshotHeaderReserveBytes = 2_048;
    private const long SiteGenerationWorkingBytesPerSite = 256;
    // L02-A exact rational clipping allocates heavily. This fixed planning reserve is deliberately
    // conservative for the qualified 64-bit coordinate profile; observed allocation remains reported separately.
    private const long GeometryWorkingBytesPerSitePair = 65_536;
    private const long GeometryFixedWorkingBytes = 16_777_216;

    public static GenerationResult<AtlasMemoryEstimate> Estimate(
        GenerationIdentity identity,
        AtlasIndexProfile profile,
        IReadOnlyList<SpatialPrimitiveDefinition> primitives)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(primitives);

        GenerationResult<AtlasPreCapturePlan> preflightResult = Preflight(
            identity,
            profile,
            primitives,
            enforceBuildBudgetFloor: false);
        if (preflightResult is GenerationFailure<AtlasPreCapturePlan> preflightFailure)
        {
            return GenerationResult<AtlasMemoryEstimate>.Failure(preflightFailure.Error);
        }

        AtlasPreCapturePlan preflight = ((GenerationSuccess<AtlasPreCapturePlan>)preflightResult).Snapshot;
        GenerationResult<CanonicalPrimitiveSet> captureResult = CanonicalPrimitiveSet.Capture(
            identity,
            primitives,
            preflight.PrimitiveCount);
        if (captureResult is GenerationFailure<CanonicalPrimitiveSet> captureFailure)
        {
            return GenerationResult<AtlasMemoryEstimate>.Failure(captureFailure.Error);
        }

        return EstimateOwned(
            identity,
            profile,
            ((GenerationSuccess<CanonicalPrimitiveSet>)captureResult).Snapshot,
            preflight);
    }

    internal static GenerationResult<AtlasMemoryEstimate> EstimateOwned(
        GenerationIdentity identity,
        AtlasIndexProfile profile,
        CanonicalPrimitiveSet primitives,
        AtlasPreCapturePlan preflight)
    {
        try
        {
            int pointCount = 0;
            long placementCount = 0;
            StableId previousId = StableId.Zero;
            bool hasPreviousId = false;
            foreach (SpatialPrimitiveDefinition primitive in primitives.Definitions)
            {
                if (hasPreviousId && primitive.Id == previousId)
                {
                    return Failure(
                        identity,
                        GenerationFailureCode.InvalidInput,
                        "atlas.spatial-index.validate",
                        $"Duplicate primitive StableId {primitive.Id}.");
                }

                previousId = primitive.Id;
                hasPreviousId = true;

                for (int pointIndex = 0; pointIndex < primitive.Points.Count; pointIndex++)
                {
                    SpatialPoint point = primitive.Points[pointIndex];
                    if (!profile.Contains(point))
                    {
                        return Failure(
                            identity,
                            GenerationFailureCode.InvalidInput,
                            "atlas.spatial-index.validate",
                            $"Primitive {primitive.Id} lies outside the finite world.");
                    }
                }

                pointCount = checked(pointCount + primitive.Points.Count);
                SpatialTileKey first = profile.GetTile(new SpatialPoint(primitive.Bounds.MinX, primitive.Bounds.MinZ));
                SpatialTileKey last = profile.GetTile(new SpatialPoint(
                    checked(primitive.Bounds.MaxXExclusive - 1),
                    checked(primitive.Bounds.MaxZExclusive - 1)));
                long width = checked(last.X - first.X + 1);
                long length = checked(last.Z - first.Z + 1);
                placementCount = checked(placementCount + checked(width * length));
            }

            long maximumPlacementReferences = Array.MaxLength - 1L;
            if (placementCount > maximumPlacementReferences)
            {
                return Failure(
                    identity,
                    GenerationFailureCode.BudgetExceeded,
                    "atlas.spatial-index.index-capacity",
                    $"Spatial index requires {placementCount} references; compact CSR supports at most {maximumPlacementReferences} so its terminal offsets remain array-representable.");
            }

            int siteCount = profile.RequestedSiteCount;
            long maximumEdges = siteCount switch
            {
                1 => 0,
                2 => 1,
                _ => checked((3L * siteCount) - 6),
            };
            long maximumNeighborReferences = checked(2 * maximumEdges);
            long siteBytes = ArrayBytes(siteCount, 32);
            long compactGraphBytes = checked(
                SnapshotHeaderReserveBytes +
                siteBytes +
                ArrayBytes(siteCount, 80) +
                ArrayBytes(maximumEdges, 8) +
                ArrayBytes(siteCount + 1L, 4) +
                ArrayBytes(maximumNeighborReferences, 4));
            long descriptionBytes = 0;
            foreach (SpatialPrimitiveDefinition primitive in primitives.Definitions)
            {
                descriptionBytes = checked(descriptionBytes + Encoding.UTF8.GetByteCount(primitive.Description));
            }

            long spatialIndexBytes = checked(
                SnapshotHeaderReserveBytes +
                ArrayBytes(primitives.Definitions.Count, 128) +
                ArrayBytes(pointCount, 16) +
                descriptionBytes +
                ArrayBytes(placementCount, 16) +
                ArrayBytes(placementCount + 1, 4) +
                ArrayBytes(placementCount, 4));
            long geometryWorkingBytes = checked(
                GeometryFixedWorkingBytes +
                checked(GeometryWorkingBytesPerSitePair * checked((long)siteCount * siteCount)));
            long snapshotBytes = checked(compactGraphBytes + spatialIndexBytes);
            long siteGenerationWorkingBytes = checked(65_536 + (SiteGenerationWorkingBytesPerSite * siteCount));
            long peakBuildBytes = checked(
                snapshotBytes +
                geometryWorkingBytes +
                siteGenerationWorkingBytes +
                preflight.EstimatedCanonicalCaptureBytes);
            return GenerationResult<AtlasMemoryEstimate>.Success(new AtlasMemoryEstimate(
                preflight.Dimensions.WorldColumnCount,
                preflight.Dimensions.WorldVoxelCount,
                siteCount,
                primitives.Definitions.Count,
                pointCount,
                placementCount,
                preflight.EstimatedCanonicalCaptureBytes,
                siteBytes,
                compactGraphBytes,
                spatialIndexBytes,
                geometryWorkingBytes,
                snapshotBytes,
                peakBuildBytes));
        }
        catch (Exception exception) when (exception is ArithmeticException or ArgumentOutOfRangeException)
        {
            return Failure(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.validate",
                exception.Message);
        }
    }

    internal static GenerationResult<AtlasPreCapturePlan> Preflight(
        GenerationIdentity identity,
        AtlasIndexProfile profile,
        IReadOnlyList<SpatialPrimitiveDefinition> primitives,
        bool enforceBuildBudgetFloor)
    {
        GenerationResult<ValidatedAtlasDimensions> dimensionsResult = ValidateDimensions(identity, profile);
        if (dimensionsResult is GenerationFailure<ValidatedAtlasDimensions> dimensionsFailure)
        {
            return GenerationResult<AtlasPreCapturePlan>.Failure(dimensionsFailure.Error);
        }

        if (profile.RequestedSiteCount > profile.SiteQuota)
        {
            return Failure<AtlasPreCapturePlan>(
                identity,
                GenerationFailureCode.BudgetExceeded,
                "atlas.spatial-index.quota",
                $"Requested {profile.RequestedSiteCount} sites exceeds quota {profile.SiteQuota}.");
        }

        int siteCount = profile.RequestedSiteCount;
        BigInteger maximumEdges = siteCount switch
        {
            1 => BigInteger.Zero,
            2 => BigInteger.One,
            _ => (3 * (BigInteger)siteCount) - 6,
        };
        BigInteger maximumNeighborReferences = 2 * maximumEdges;
        if ((BigInteger)siteCount + 1 > Array.MaxLength ||
            maximumEdges > Array.MaxLength ||
            maximumNeighborReferences > Array.MaxLength)
        {
            return Failure<AtlasPreCapturePlan>(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.array-capacity",
                $"Requested site topology exceeds CLR array capacity {Array.MaxLength}: sites={siteCount}, edges={maximumEdges}, neighbor references={maximumNeighborReferences}.");
        }

        int primitiveCount;
        try
        {
            primitiveCount = primitives.Count;
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or InvalidOperationException)
        {
            return Failure<AtlasPreCapturePlan>(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.validate",
                exception.Message);
        }

        if (primitiveCount < 0)
        {
            return Failure<AtlasPreCapturePlan>(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.validate",
                "Primitive collection reports a negative count.");
        }

        BigInteger captureBytes =
            ManagedArrayHeaderBytes +
            ((BigInteger)primitiveCount * IntPtr.Size) +
            ReadOnlyCollectionWrapperBytes;
        if (captureBytes > profile.MemoryBudgetBytes)
        {
            return Failure<AtlasPreCapturePlan>(
                identity,
                GenerationFailureCode.BudgetExceeded,
                "atlas.spatial-index.capture-budget",
                $"Canonical reference capture requires at least {captureBytes} bytes, exceeding budget {profile.MemoryBudgetBytes} bytes.");
        }

        if (primitiveCount > Array.MaxLength)
        {
            return Failure<AtlasPreCapturePlan>(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.array-capacity",
                $"Primitive collection reports {primitiveCount} items; canonical capture, memberships and owned primitives each support at most {Array.MaxLength} array elements.");
        }

        BigInteger minimumWorkingBytes =
            GeometryFixedWorkingBytes +
            ((BigInteger)GeometryWorkingBytesPerSitePair * siteCount * siteCount) +
            65_536 +
            ((BigInteger)SiteGenerationWorkingBytesPerSite * siteCount) +
            captureBytes +
            (2 * SnapshotHeaderReserveBytes);
        if (enforceBuildBudgetFloor && minimumWorkingBytes > profile.MemoryBudgetBytes)
        {
            return Failure<AtlasPreCapturePlan>(
                identity,
                GenerationFailureCode.BudgetExceeded,
                "atlas.spatial-index.budget",
                $"Unavoidable pre-allocation floor {minimumWorkingBytes} bytes exceeds budget {profile.MemoryBudgetBytes} bytes.");
        }

        return GenerationResult<AtlasPreCapturePlan>.Success(new AtlasPreCapturePlan(
            ((GenerationSuccess<ValidatedAtlasDimensions>)dimensionsResult).Snapshot,
            primitiveCount,
            checked((long)captureBytes)));
    }

    private static GenerationResult<ValidatedAtlasDimensions> ValidateDimensions(
        GenerationIdentity identity,
        AtlasIndexProfile profile)
    {
        BigInteger width = profile.WidthMagnitude;
        BigInteger length = profile.LengthMagnitude;
        BigInteger columns = width * length;
        BigInteger voxels = columns * profile.Domain.Height;
        BigInteger tileCountX = (width + profile.TileSize - 1) / profile.TileSize;
        BigInteger tileCountZ = (length + profile.TileSize - 1) / profile.TileSize;
        BigInteger tileCount = tileCountX * tileCountZ;
        if (width > long.MaxValue || length > long.MaxValue ||
            columns > long.MaxValue || voxels > long.MaxValue ||
            tileCountX > long.MaxValue || tileCountZ > long.MaxValue || tileCount > ulong.MaxValue)
        {
            return Failure<ValidatedAtlasDimensions>(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.dimensions",
                $"World dimensions cannot be represented safely: width={width}, length={length}, height={profile.Domain.Height}, columns={columns}, voxels={voxels}, tiles={tileCount}.");
        }

        return GenerationResult<ValidatedAtlasDimensions>.Success(new ValidatedAtlasDimensions(
            checked((long)width),
            checked((long)length),
            checked((long)columns),
            checked((long)voxels),
            checked((long)tileCountX),
            checked((long)tileCountZ)));
    }

    private static long ArrayBytes(long count, long elementBytes) =>
        checked(ManagedArrayHeaderBytes + checked(count * elementBytes));

    internal static GenerationResult<T> Failure<T>(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details)
        where T : class =>
        GenerationResult<T>.Failure(new GenerationError(
            code,
            identity.NativeSeed,
            stage,
            StableId.Zero,
            identity.GeographyConfigHash,
            details,
            false));

    private static GenerationResult<AtlasMemoryEstimate> Failure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details) =>
        Failure<AtlasMemoryEstimate>(identity, code, stage, details);
}

internal sealed class CanonicalPrimitiveSet
{
    private CanonicalPrimitiveSet(SpatialPrimitiveDefinition[] definitions) =>
        Definitions = Array.AsReadOnly(definitions);

    internal ReadOnlyCollection<SpatialPrimitiveDefinition> Definitions { get; }

    internal static GenerationResult<CanonicalPrimitiveSet> Capture(
        GenerationIdentity identity,
        IReadOnlyList<SpatialPrimitiveDefinition> source,
        int expectedCount)
    {
        if (expectedCount < 0 || expectedCount > Array.MaxLength)
        {
            return AtlasSpatialIndexPlanner.Failure<CanonicalPrimitiveSet>(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.array-capacity",
                $"Canonical primitive capture count {expectedCount} is outside CLR array capacity [0, {Array.MaxLength}].");
        }

        try
        {
            var definitions = new SpatialPrimitiveDefinition[expectedCount];
            int index = 0;
            foreach (SpatialPrimitiveDefinition? primitive in source)
            {
                if (index >= definitions.Length)
                {
                    return AtlasSpatialIndexPlanner.Failure<CanonicalPrimitiveSet>(
                        identity,
                        GenerationFailureCode.InvalidInput,
                        "atlas.spatial-index.validate",
                        $"Primitive collection enumerated more than its reported count {expectedCount}.");
                }

                if (primitive is null)
                {
                    return AtlasSpatialIndexPlanner.Failure<CanonicalPrimitiveSet>(
                        identity,
                        GenerationFailureCode.InvalidInput,
                        "atlas.spatial-index.validate",
                        "Primitive collection contains null.");
                }

                // SpatialPrimitiveDefinition is sealed; its constructor already owns a private point array,
                // exposes it read-only and stores only immutable scalar/string values. Capturing the reference
                // therefore closes caller-list TOCTOU without duplicating an arbitrarily large polyline.
                definitions[index++] = primitive;
            }

            if (index != expectedCount)
            {
                return AtlasSpatialIndexPlanner.Failure<CanonicalPrimitiveSet>(
                    identity,
                    GenerationFailureCode.InvalidInput,
                    "atlas.spatial-index.validate",
                    $"Primitive collection enumerated {index} items after reporting {expectedCount}.");
            }

            Array.Sort(
                definitions,
                static (left, right) => StableIdComparer.Instance.Compare(left.Id, right.Id));
            return GenerationResult<CanonicalPrimitiveSet>.Success(new CanonicalPrimitiveSet(definitions));
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or InvalidOperationException)
        {
            return AtlasSpatialIndexPlanner.Failure<CanonicalPrimitiveSet>(
                identity,
                GenerationFailureCode.InvalidInput,
                "atlas.spatial-index.validate",
                exception.Message);
        }
    }
}

internal sealed record AtlasPreCapturePlan(
    ValidatedAtlasDimensions Dimensions,
    int PrimitiveCount,
    long EstimatedCanonicalCaptureBytes);

internal sealed record ValidatedAtlasDimensions(
    long Width,
    long Length,
    long WorldColumnCount,
    long WorldVoxelCount,
    long TileCountX,
    long TileCountZ);
