using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.SpatialIndex;

/// <summary>
/// Executes the complete non-voxel atlas path: deterministic sites, exact L02-A mesh, compact graph projection,
/// and local primitive index. The conservative memory plan is checked before site or mesh allocation.
/// </summary>
public static class AtlasSpatialIndexBuilder
{
    public static GenerationResult<AtlasIndexBuildOutcome> Build(
        GenerationIdentity identity,
        AtlasIndexProfile profile,
        IReadOnlyList<SpatialPrimitiveDefinition> primitives,
        SpatialIndexBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(primitives);
        options ??= SpatialIndexBuildOptions.Default;

        GenerationResult<AtlasMemoryEstimate> estimateResult = AtlasSpatialIndexPlanner.Estimate(identity, profile, primitives);
        if (estimateResult is GenerationFailure<AtlasMemoryEstimate> estimateFailure)
        {
            return GenerationResult<AtlasIndexBuildOutcome>.Failure(estimateFailure.Error);
        }

        AtlasMemoryEstimate estimate = ((GenerationSuccess<AtlasMemoryEstimate>)estimateResult).Snapshot;
        if (estimate.EstimatedPeakBuildBytes > profile.MemoryBudgetBytes)
        {
            return AtlasSpatialIndexPlanner.Failure<AtlasIndexBuildOutcome>(
                identity,
                GenerationFailureCode.BudgetExceeded,
                "atlas.spatial-index.budget",
                $"Pre-allocation plan {estimate.EstimatedPeakBuildBytes} bytes exceeds budget {profile.MemoryBudgetBytes} bytes.");
        }

        try
        {
            var bounds = new WorldBounds(
                profile.Domain.X.MinInclusive,
                profile.Domain.Z.MinInclusive,
                profile.Domain.X.MaxExclusive,
                profile.Domain.Z.MaxExclusive);
            GenerationResult<GeneratedSiteSet> siteResult = AtlasSiteGenerator.Generate(
                identity,
                bounds,
                new AtlasSiteGenerationSettings(profile.RequestedSiteCount));
            if (siteResult is GenerationFailure<GeneratedSiteSet> siteFailure)
            {
                return GenerationResult<AtlasIndexBuildOutcome>.Failure(siteFailure.Error);
            }

            GeneratedSiteSet generatedSites = ((GenerationSuccess<GeneratedSiteSet>)siteResult).Snapshot;
            var geometryOptions = new AtlasGeometryBuildOptions(
                options.Workers,
                options.CacheMode == SpatialIndexCacheMode.Precomputed
                    ? GeometryCacheMode.Precomputed
                    : GeometryCacheMode.Cold);
            GenerationResult<AtlasMesh> meshResult = AtlasGeometryBuilder.Build(
                identity,
                bounds,
                generatedSites.Sites,
                geometryOptions);
            if (meshResult is GenerationFailure<AtlasMesh> meshFailure)
            {
                return GenerationResult<AtlasIndexBuildOutcome>.Failure(meshFailure.Error);
            }

            AtlasMesh mesh = ((GenerationSuccess<AtlasMesh>)meshResult).Snapshot;
            var graph = new CompactAtlasGraph(mesh);
            SpatialPrimitiveDefinition[] canonicalDefinitions = primitives
                .OrderBy(primitive => primitive.Id, StableIdComparer.Instance)
                .ToArray();
            SpatialTileKey[][] memberships = BuildMemberships(profile, canonicalDefinitions, options);
            long actualPlacementCount = memberships.Sum(membership => (long)membership.Length);
            if (actualPlacementCount != estimate.PlacementReferenceCount)
            {
                throw new InvalidOperationException(
                    $"Planned {estimate.PlacementReferenceCount} placement references but constructed {actualPlacementCount}.");
            }

            var index = new CompactSpatialIndex(profile, canonicalDefinitions, memberships);
            Hash256 checksum = CanonicalAtlasHash.Compute(identity, profile, graph, index);
            var header = new SnapshotHeader(
                identity,
                "atlas.spatial-index",
                revision: 1,
                parentHashes: [],
                new SnapshotDimensions(profile.Width, profile.Length, profile.Domain.Height),
                new SnapshotUnits("block", "block"),
                checksum);
            var snapshot = new IndexedAtlasSnapshot(header, graph, index);
            return GenerationResult<AtlasIndexBuildOutcome>.Success(new AtlasIndexBuildOutcome(snapshot, estimate));
        }
        catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException or ArgumentException)
        {
            return AtlasSpatialIndexPlanner.Failure<AtlasIndexBuildOutcome>(
                identity,
                GenerationFailureCode.GeometryFailure,
                "atlas.spatial-index.construct",
                exception.Message);
        }
    }

    private static SpatialTileKey[][] BuildMemberships(
        AtlasIndexProfile profile,
        IReadOnlyList<SpatialPrimitiveDefinition> definitions,
        SpatialIndexBuildOptions options)
    {
        var result = new SpatialTileKey[definitions.Count][];
        Parallel.For(
            0,
            definitions.Count,
            new ParallelOptions { MaxDegreeOfParallelism = options.Workers },
            index => result[index] = EnumerateTouchedTiles(profile, definitions[index].Bounds));
        return result;
    }

    private static SpatialTileKey[] EnumerateTouchedTiles(AtlasIndexProfile profile, SpatialBounds bounds)
    {
        SpatialTileKey first = profile.GetTile(new SpatialPoint(bounds.MinX, bounds.MinZ));
        SpatialTileKey last = profile.GetTile(new SpatialPoint(
            checked(bounds.MaxXExclusive - 1),
            checked(bounds.MaxZExclusive - 1)));
        int count = checked((int)checked(checked(last.X - first.X + 1) * checked(last.Z - first.Z + 1)));
        var result = new SpatialTileKey[count];
        int index = 0;
        for (long z = first.Z; z <= last.Z; z++)
        {
            for (long x = first.X; x <= last.X; x++)
            {
                result[index++] = new SpatialTileKey(x, z);
            }
        }

        return result;
    }
}

internal static class CanonicalAtlasHash
{
    private static readonly byte[] Magic = "ISRW-ATLAS-INDEX-V1"u8.ToArray();

    internal static Hash256 Compute(
        GenerationIdentity identity,
        AtlasIndexProfile profile,
        CompactAtlasGraph graph,
        CompactSpatialIndex index)
    {
        using var stream = new MemoryStream();
        stream.Write(Magic);
        WriteInt32(stream, identity.NativeSeed);
        WriteUInt32(stream, identity.AlgorithmVersion);
        WriteUInt32(stream, identity.SchemaVersion);
        WriteHash(stream, identity.GeographyConfigHash);
        WriteHash(stream, identity.GenerationAssetHash);
        WriteString(stream, identity.DeterminismProfileId);
        WriteInt64(stream, profile.Domain.X.MinInclusive);
        WriteInt64(stream, profile.Domain.X.MaxExclusive);
        WriteInt64(stream, profile.Domain.Z.MinInclusive);
        WriteInt64(stream, profile.Domain.Z.MaxExclusive);
        WriteInt32(stream, profile.Domain.Height);
        WriteInt64(stream, profile.Domain.SamplingOrigin.X);
        WriteInt64(stream, profile.Domain.SamplingOrigin.Z);
        WriteString(stream, profile.Scale.GeologicalUnitName);
        WriteUInt64(stream, unchecked((ulong)BitConverter.DoubleToInt64Bits(profile.Scale.BlocksPerGeologicalUnit)));
        WriteString(stream, profile.Scale.AltitudeUnitName);
        WriteUInt64(stream, unchecked((ulong)BitConverter.DoubleToInt64Bits(profile.Scale.BlocksPerAltitudeUnit)));
        WriteInt32(stream, profile.TileSize);
        WriteInt32(stream, profile.RequestedSiteCount);
        WriteInt32(stream, profile.SiteQuota);
        WriteInt32(stream, (int)graph.TopologyDimension);
        WriteInt32(stream, graph.SiteCount);
        foreach (CompactAtlasSite site in graph.Sites)
        {
            WriteStableId(stream, site.Id);
            WriteInt64(stream, site.X);
            WriteInt64(stream, site.Z);
            WriteBigInteger(stream, site.CellArea.Numerator);
            WriteBigInteger(stream, site.CellArea.Denominator);
            WriteInt32(stream, (int)site.BoundaryMask);
        }

        WriteInt32(stream, graph.EdgeCount);
        foreach (CompactAtlasEdge edge in graph.Edges)
        {
            WriteInt32(stream, edge.FirstSiteIndex);
            WriteInt32(stream, edge.SecondSiteIndex);
        }

        WriteInt32Span(stream, graph.NeighborOffsets);
        WriteInt32Span(stream, graph.NeighborSiteIndices);
        WriteInt32(stream, index.PrimitiveCount);
        foreach (OwnedSpatialPrimitive primitive in index.Primitives)
        {
            WriteStableId(stream, primitive.Id);
            WriteInt32(stream, (int)primitive.Kind);
            WriteString(stream, primitive.Description);
            WriteInt64(stream, primitive.Bounds.MinX);
            WriteInt64(stream, primitive.Bounds.MinZ);
            WriteInt64(stream, primitive.Bounds.MaxXExclusive);
            WriteInt64(stream, primitive.Bounds.MaxZExclusive);
            WriteInt64(stream, primitive.OwnerTile.X);
            WriteInt64(stream, primitive.OwnerTile.Z);
            WriteStableId(stream, primitive.OwnerId);
            WriteInt32(stream, primitive.Points.Count);
            foreach (SpatialPoint point in primitive.Points)
            {
                WriteInt64(stream, point.X);
                WriteInt64(stream, point.Z);
            }
        }

        WriteTileSpan(stream, index.TileKeys);
        WriteInt32Span(stream, index.TileOffsets);
        WriteInt32Span(stream, index.PrimitiveIndices);
        return Hash256.Compute(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    private static void WriteHash(Stream stream, Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[Hash256.ByteWidth];
        value.WriteCanonicalBytes(bytes);
        stream.Write(bytes);
    }

    private static void WriteStableId(Stream stream, StableId value)
    {
        Span<byte> bytes = stackalloc byte[StableId.ByteWidth];
        value.WriteCanonicalBytes(bytes);
        stream.Write(bytes);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteBigInteger(Stream stream, BigInteger value)
    {
        byte[] bytes = value.ToByteArray(isUnsigned: false, isBigEndian: true);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteTileSpan(Stream stream, ReadOnlySpan<SpatialTileKey> values)
    {
        WriteInt32(stream, values.Length);
        foreach (SpatialTileKey value in values)
        {
            WriteInt64(stream, value.X);
            WriteInt64(stream, value.Z);
        }
    }

    private static void WriteInt32Span(Stream stream, ReadOnlySpan<int> values)
    {
        WriteInt32(stream, values.Length);
        foreach (int value in values)
        {
            WriteInt32(stream, value);
        }
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value) => WriteUInt64(stream, unchecked((ulong)value));

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
