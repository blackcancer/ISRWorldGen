using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.SpatialIndex;

/// <summary>Conservative, deterministic allocation plan computed before atlas generation.</summary>
public sealed class AtlasMemoryEstimate
{
    internal AtlasMemoryEstimate(
        long worldColumnCount,
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
            foreach (SpatialPrimitiveDefinition primitive in primitives)
            {
                if (primitive is null)
                {
                    return Failure(
                        identity,
                        GenerationFailureCode.InvalidInput,
                        "atlas.spatial-index.validate",
                        "Primitive collection contains null.");
                }

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
            long worldColumns = checked(profile.Width * profile.Length);
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
            long descriptionBytes = primitives.Sum(primitive => checked((long)Encoding.UTF8.GetByteCount(primitive.Description)));
            long spatialIndexBytes = checked(
                SnapshotHeaderReserveBytes +
                ArrayBytes(primitives.Count, 128) +
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
                worldColumns,
                siteCount,
                primitives.Count,
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
