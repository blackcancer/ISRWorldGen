using System.Collections.ObjectModel;
using System.Numerics;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.SpatialIndex;

public readonly record struct SpatialPoint(long X, long Z) : IComparable<SpatialPoint>
{
    public int CompareTo(SpatialPoint other)
    {
        int z = Z.CompareTo(other.Z);
        return z != 0 ? z : X.CompareTo(other.X);
    }
}

/// <summary>Zero-based tile coordinate relative to the finite world origin, ordered row-major by Z then X.</summary>
public readonly record struct SpatialTileKey(long X, long Z) : IComparable<SpatialTileKey>
{
    public int CompareTo(SpatialTileKey other)
    {
        int z = Z.CompareTo(other.Z);
        return z != 0 ? z : X.CompareTo(other.X);
    }
}

/// <summary>Non-empty semi-open horizontal bounding box in global block coordinates.</summary>
public sealed record SpatialBounds
{
    public SpatialBounds(long minX, long minZ, long maxXExclusive, long maxZExclusive)
    {
        if (minX >= maxXExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxXExclusive), "The X interval must be non-empty and semi-open.");
        }

        if (minZ >= maxZExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxZExclusive), "The Z interval must be non-empty and semi-open.");
        }

        MinX = minX;
        MinZ = minZ;
        MaxXExclusive = maxXExclusive;
        MaxZExclusive = maxZExclusive;
    }

    public long MinX { get; }

    public long MinZ { get; }

    public long MaxXExclusive { get; }

    public long MaxZExclusive { get; }
}

public enum SpatialPrimitiveKind
{
    River = 1,
    Cavern = 2,
}

/// <summary>Immutable input primitive. Its polyline and derived bounding box are expressed in global blocks.</summary>
public sealed class SpatialPrimitiveDefinition
{
    public SpatialPrimitiveDefinition(
        StableId id,
        SpatialPrimitiveKind kind,
        string description,
        IEnumerable<SpatialPoint> points)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(points);
        if (id == StableId.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "A spatial primitive requires a non-zero stable ID.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown spatial primitive kind.");
        }

        if (string.IsNullOrWhiteSpace(description) || !description.IsNormalized(NormalizationForm.FormC))
        {
            throw new ArgumentException("Description must be non-empty canonical NFC text.", nameof(description));
        }

        SpatialPoint[] copy = points.ToArray();
        if (copy.Length < 2 || copy.Distinct().Count() < 2)
        {
            throw new ArgumentException("A spatial primitive requires at least two distinct polyline points.", nameof(points));
        }

        long minX = copy.Min(point => point.X);
        long minZ = copy.Min(point => point.Z);
        long maxX = copy.Max(point => point.X);
        long maxZ = copy.Max(point => point.Z);
        Id = id;
        Kind = kind;
        Description = description;
        Points = Array.AsReadOnly(copy);
        Bounds = new SpatialBounds(minX, minZ, checked(maxX + 1), checked(maxZ + 1));
    }

    public StableId Id { get; }

    public SpatialPrimitiveKind Kind { get; }

    public string Description { get; }

    public ReadOnlyCollection<SpatialPoint> Points { get; }

    public SpatialBounds Bounds { get; }
}

public sealed class OwnedSpatialPrimitive : IEquatable<OwnedSpatialPrimitive>
{
    internal OwnedSpatialPrimitive(SpatialPrimitiveDefinition source, SpatialTileKey ownerTile, StableId ownerId)
    {
        Id = source.Id;
        Kind = source.Kind;
        Description = source.Description;
        Bounds = source.Bounds;
        // SpatialPrimitiveDefinition is sealed and already owns this read-only point storage.
        // Reuse it so publication does not create a second large polyline allocation.
        Points = source.Points;
        OwnerTile = ownerTile;
        OwnerId = ownerId;
    }

    public StableId Id { get; }

    public SpatialPrimitiveKind Kind { get; }

    public string Description { get; }

    public SpatialBounds Bounds { get; }

    public ReadOnlyCollection<SpatialPoint> Points { get; }

    public SpatialTileKey OwnerTile { get; }

    public StableId OwnerId { get; }

    public bool Equals(OwnedSpatialPrimitive? other) =>
        other is not null &&
        Id == other.Id &&
        Kind == other.Kind &&
        Description == other.Description &&
        Bounds == other.Bounds &&
        OwnerTile == other.OwnerTile &&
        OwnerId == other.OwnerId &&
        Points.SequenceEqual(other.Points);

    public override bool Equals(object? obj) => Equals(obj as OwnedSpatialPrimitive);

    public override int GetHashCode() => HashCode.Combine(Id, Kind, Description, Bounds, OwnerTile, OwnerId);
}

public sealed record AtlasIndexProfile
{
    public AtlasIndexProfile(
        WorldDomain domain,
        ScaleModel scale,
        int tileSize,
        int requestedSiteCount,
        int siteQuota,
        long memoryBudgetBytes)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(scale);
        if (tileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), tileSize, "Tile size must be positive.");
        }

        if (requestedSiteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSiteCount), requestedSiteCount, "Requested sites must be positive.");
        }

        if (siteQuota <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteQuota), siteQuota, "Site quota must be positive.");
        }

        if (memoryBudgetBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes), memoryBudgetBytes, "Memory budget must be positive.");
        }

        Domain = domain;
        Scale = scale;
        TileSize = tileSize;
        RequestedSiteCount = requestedSiteCount;
        SiteQuota = siteQuota;
        MemoryBudgetBytes = memoryBudgetBytes;
    }

    public WorldDomain Domain { get; }

    public ScaleModel Scale { get; }

    public int TileSize { get; }

    public int RequestedSiteCount { get; }

    public int SiteQuota { get; }

    public long MemoryBudgetBytes { get; }

    /// <summary>
    /// Validated signed 64-bit width. Planning validates the exact domain span before build code reads this property.
    /// </summary>
    public long Width => checked((long)WidthMagnitude);

    /// <summary>
    /// Validated signed 64-bit length. Planning validates the exact domain span before build code reads this property.
    /// </summary>
    public long Length => checked((long)LengthMagnitude);

    public long TileCountX => checked((long)((WidthMagnitude + TileSize - 1) / TileSize));

    public long TileCountZ => checked((long)((LengthMagnitude + TileSize - 1) / TileSize));

    internal BigInteger WidthMagnitude => (BigInteger)Domain.X.MaxExclusive - Domain.X.MinInclusive;

    internal BigInteger LengthMagnitude => (BigInteger)Domain.Z.MaxExclusive - Domain.Z.MinInclusive;

    internal bool Contains(SpatialPoint point) =>
        Domain.X.Contains(point.X) && Domain.Z.Contains(point.Z);

    internal SpatialTileKey GetTile(SpatialPoint point)
    {
        if (!Contains(point))
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, "Point lies outside the finite world.");
        }

        long x = (point.X - Domain.X.MinInclusive) / TileSize;
        long z = (point.Z - Domain.Z.MinInclusive) / TileSize;
        return new SpatialTileKey(x, z);
    }

    internal bool Contains(SpatialTileKey key) =>
        key.X >= 0 && key.X < TileCountX && key.Z >= 0 && key.Z < TileCountZ;

    internal ulong GetTileOrdinal(SpatialTileKey key)
    {
        if (!Contains(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "Tile lies outside the finite world.");
        }

        return checked((ulong)(((BigInteger)key.Z * TileCountX) + key.X));
    }
}

public enum SpatialIndexCacheMode
{
    Cold = 0,
    Precomputed = 1,
}

public sealed record SpatialIndexBuildOptions
{
    public SpatialIndexBuildOptions(int workers, SpatialIndexCacheMode cacheMode)
    {
        if (workers is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(workers), workers, "Workers must be in [1, 256].");
        }

        if (!Enum.IsDefined(cacheMode))
        {
            throw new ArgumentOutOfRangeException(nameof(cacheMode), cacheMode, "Unknown spatial-index cache mode.");
        }

        Workers = workers;
        CacheMode = cacheMode;
    }

    public int Workers { get; }

    public SpatialIndexCacheMode CacheMode { get; }

    public static SpatialIndexBuildOptions Default { get; } = new(1, SpatialIndexCacheMode.Cold);
}
