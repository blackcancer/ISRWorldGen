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

        GenerationResult<CanonicalPrimitiveSet> captureResult = CanonicalPrimitiveSet.Capture(identity, primitives);
        if (captureResult is GenerationFailure<CanonicalPrimitiveSet> captureFailure)
        {
            return GenerationResult<AtlasMemoryEstimate>.Failure(captureFailure.Error);
        }

        return EstimateOwned(
            identity,
            profile,
            ((GenerationSuccess<CanonicalPrimitiveSet>)captureResult).Snapshot);
    }

    internal static GenerationResult<AtlasMemoryEstimate> EstimateOwned(
        GenerationIdentity identity,
        AtlasIndexProfile profile,
        CanonicalPrimitiveSet primitives)
    {
        GenerationResult<ValidatedAtlasDimensions> dimensionsResult = ValidateDimensions(identity, profile);
        if (dimensionsResult is GenerationFailure<ValidatedAtlasDimensions> dimensionsFailure)
        {
            return GenerationResult<AtlasMemoryEstimate>.Failure(dimensionsFailure.Error);
        }

        ValidatedAtlasDimensions dimensions =
            ((GenerationSuccess<ValidatedAtlasDimensions>)dimensionsResult).Snapshot;
        if (profile.RequestedSiteCount > profile.SiteQuota)
        {
            return Failure(
                identity,
                GenerationFailureCode.BudgetExceeded,
                "atlas.spatial-index.quota",
                $"Requested {profile.RequestedSiteCount} sites exceeds quota {profile.SiteQuota}.");
        }

        try
        {
            int pointCount = 0;
            long placementCount = 0;
            var ids = new HashSet<StableId>();
            foreach (SpatialPrimitiveDefinition primitive in primitives.Definitions)
            {
                if (!ids.Add(primitive.Id))
                {
                    return Failure(
                        identity,
                        GenerationFailureCode.InvalidInput,
                        "atlas.spatial-index.validate",
                        $"Duplicate primitive StableId {primitive.Id}.");
                }

                foreach (SpatialPoint point in primitive.Points)
                {
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

            if (placementCount > int.MaxValue)
            {
                return Failure(
                    identity,
                    GenerationFailureCode.BudgetExceeded,
                    "atlas.spatial-index.index-capacity",
                    $"Spatial index requires {placementCount} references; compact CSR supports at most {int.MaxValue}.");
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
            long peakBuildBytes = checked(snapshotBytes + geometryWorkingBytes + siteGenerationWorkingBytes);
            return GenerationResult<AtlasMemoryEstimate>.Success(new AtlasMemoryEstimate(
                dimensions.WorldColumnCount,
                dimensions.WorldVoxelCount,
                siteCount,
                primitives.Definitions.Count,
                pointCount,
                placementCount,
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
        IReadOnlyList<SpatialPrimitiveDefinition> source)
    {
        try
        {
            var definitions = new List<SpatialPrimitiveDefinition>();
            foreach (SpatialPrimitiveDefinition? primitive in source)
            {
                if (primitive is null)
                {
                    return AtlasSpatialIndexPlanner.Failure<CanonicalPrimitiveSet>(
                        identity,
                        GenerationFailureCode.InvalidInput,
                        "atlas.spatial-index.validate",
                        "Primitive collection contains null.");
                }

                definitions.Add(new SpatialPrimitiveDefinition(
                    primitive.Id,
                    primitive.Kind,
                    primitive.Description,
                    primitive.Points));
            }

            SpatialPrimitiveDefinition[] canonical = definitions
                .OrderBy(primitive => primitive.Id, StableIdComparer.Instance)
                .ToArray();
            return GenerationResult<CanonicalPrimitiveSet>.Success(new CanonicalPrimitiveSet(canonical));
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

internal sealed record ValidatedAtlasDimensions(
    long Width,
    long Length,
    long WorldColumnCount,
    long WorldVoxelCount,
    long TileCountX,
    long TileCountZ);
