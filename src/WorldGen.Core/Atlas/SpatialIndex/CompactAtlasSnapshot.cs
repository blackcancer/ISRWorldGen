using System.Collections.ObjectModel;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.SpatialIndex;

public readonly record struct CompactAtlasSite(
    StableId Id,
    long X,
    long Z,
    ExactRational CellArea,
    WorldBoundaryMask BoundaryMask);

public readonly record struct CompactAtlasEdge(int FirstSiteIndex, int SecondSiteIndex);

/// <summary>Immutable array/CSR projection of the complete L02-A graph.</summary>
public sealed class CompactAtlasGraph
{
    private readonly int[] neighborOffsets;
    private readonly int[] neighborSiteIndices;

    internal CompactAtlasGraph(AtlasMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        AtlasSite[] orderedSites = mesh.Sites.OrderBy(site => site.Id, StableIdComparer.Instance).ToArray();
        var indexById = orderedSites
            .Select((site, index) => (site.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        var cellsById = mesh.Cells.ToDictionary(cell => cell.SiteId);
        var sites = new CompactAtlasSite[orderedSites.Length];
        neighborOffsets = new int[orderedSites.Length + 1];
        var neighbors = new List<int>(mesh.Edges.Count * 2);
        for (int index = 0; index < orderedSites.Length; index++)
        {
            AtlasSite site = orderedSites[index];
            VoronoiCell cell = cellsById[site.Id];
            sites[index] = new CompactAtlasSite(site.Id, site.X, site.Z, cell.Area, cell.BoundaryMask);
            neighborOffsets[index] = neighbors.Count;
            neighbors.AddRange(cell.NeighborIds.Select(id => indexById[id]).Order());
        }

        neighborOffsets[^1] = neighbors.Count;
        neighborSiteIndices = neighbors.ToArray();
        CompactAtlasEdge[] edges = mesh.Edges
            .Select(edge =>
            {
                int first = indexById[edge.A];
                int second = indexById[edge.B];
                return first < second ? new CompactAtlasEdge(first, second) : new CompactAtlasEdge(second, first);
            })
            .OrderBy(edge => edge.FirstSiteIndex)
            .ThenBy(edge => edge.SecondSiteIndex)
            .ToArray();
        Sites = Array.AsReadOnly(sites);
        Edges = Array.AsReadOnly(edges);
        TopologyDimension = mesh.TopologyDimension;
    }

    public ReadOnlyCollection<CompactAtlasSite> Sites { get; }

    public ReadOnlyCollection<CompactAtlasEdge> Edges { get; }

    public AtlasTopologyDimension TopologyDimension { get; }

    public int SiteCount => Sites.Count;

    public int EdgeCount => Edges.Count;

    public int NeighborReferenceCount => neighborSiteIndices.Length;

    public ReadOnlyCollection<StableId> GetNeighborIds(int siteIndex)
    {
        if ((uint)siteIndex >= (uint)Sites.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(siteIndex), siteIndex, "Site index is outside the compact graph.");
        }

        int start = neighborOffsets[siteIndex];
        int length = neighborOffsets[siteIndex + 1] - start;
        var result = new StableId[length];
        for (int index = 0; index < length; index++)
        {
            result[index] = Sites[neighborSiteIndices[start + index]].Id;
        }

        return Array.AsReadOnly(result);
    }

    internal ReadOnlySpan<int> NeighborOffsets => neighborOffsets;

    internal ReadOnlySpan<int> NeighborSiteIndices => neighborSiteIndices;
}

/// <summary>Compact local tile index. Only occupied tiles and their primitive references are stored.</summary>
public sealed class CompactSpatialIndex
{
    private readonly AtlasIndexProfile profile;
    private readonly SpatialTileKey[] tileKeys;
    private readonly int[] tileOffsets;
    private readonly int[] primitiveIndices;

    internal CompactSpatialIndex(
        AtlasIndexProfile profile,
        IReadOnlyList<SpatialPrimitiveDefinition> definitions,
        IReadOnlyList<PrimitiveTileMembership> memberships)
    {
        this.profile = profile;
        var primitives = new OwnedSpatialPrimitive[definitions.Count];
        var placements = new SortedDictionary<SpatialTileKey, List<int>>(SpatialTileKeyComparer.Instance);
        for (int primitiveIndex = 0; primitiveIndex < definitions.Count; primitiveIndex++)
        {
            PrimitiveTileMembership membership = memberships[primitiveIndex];
            if (membership.CandidateTiles.Length == 0)
            {
                throw new InvalidOperationException("A spatial primitive must touch at least one finite-world tile.");
            }

            primitives[primitiveIndex] = new OwnedSpatialPrimitive(
                definitions[primitiveIndex],
                membership.OwnerTile,
                membership.OwnerId);
            foreach (SpatialTileKey tile in membership.CandidateTiles)
            {
                if (!placements.TryGetValue(tile, out List<int>? indices))
                {
                    indices = [];
                    placements.Add(tile, indices);
                }

                indices.Add(primitiveIndex);
            }
        }

        tileKeys = placements.Keys.ToArray();
        tileOffsets = new int[tileKeys.Length + 1];
        var references = new List<int>();
        for (int tileIndex = 0; tileIndex < tileKeys.Length; tileIndex++)
        {
            tileOffsets[tileIndex] = references.Count;
            references.AddRange(placements[tileKeys[tileIndex]].Order());
        }

        tileOffsets[^1] = references.Count;
        primitiveIndices = references.ToArray();
        Primitives = Array.AsReadOnly(primitives);
    }

    public ReadOnlyCollection<OwnedSpatialPrimitive> Primitives { get; }

    public int PrimitiveCount => Primitives.Count;

    public int OccupiedTileCount => tileKeys.Length;

    public int PlacementReferenceCount => primitiveIndices.Length;

    /// <summary>
    /// Derives the globally unique ID of one primitive's ownership decision from its ID and physical owner tile.
    /// </summary>
    public StableId GetOwnerId(StableId primitiveId, SpatialTileKey tile)
        => DeriveOwnerId(profile, primitiveId, tile);

    internal static StableId DeriveOwnerId(
        AtlasIndexProfile profile,
        StableId primitiveId,
        SpatialTileKey tile)
    {
        ulong ordinal = profile.GetTileOrdinal(tile);
        return StableId.Derive(RandomDomain.Sites, primitiveId, ordinal);
    }

    public ReadOnlyCollection<OwnedSpatialPrimitive> Query(SpatialTileKey tile)
    {
        if (!profile.Contains(tile))
        {
            return Array.AsReadOnly(Array.Empty<OwnedSpatialPrimitive>());
        }

        int tileIndex = Array.BinarySearch(tileKeys, tile, SpatialTileKeyComparer.Instance);
        if (tileIndex < 0)
        {
            return Array.AsReadOnly(Array.Empty<OwnedSpatialPrimitive>());
        }

        int start = tileOffsets[tileIndex];
        int length = tileOffsets[tileIndex + 1] - start;
        var result = new OwnedSpatialPrimitive[length];
        for (int index = 0; index < length; index++)
        {
            result[index] = Primitives[primitiveIndices[start + index]];
        }

        return Array.AsReadOnly(result);
    }

    internal ReadOnlySpan<SpatialTileKey> TileKeys => tileKeys;

    internal ReadOnlySpan<int> TileOffsets => tileOffsets;

    internal ReadOnlySpan<int> PrimitiveIndices => primitiveIndices;
}

public sealed class IndexedAtlasSnapshot
{
    internal IndexedAtlasSnapshot(SnapshotHeader header, CompactAtlasGraph graph, CompactSpatialIndex index)
    {
        Header = header;
        Graph = graph;
        Index = index;
    }

    public SnapshotHeader Header { get; }

    public CompactAtlasGraph Graph { get; }

    public CompactSpatialIndex Index { get; }
}

public sealed class AtlasIndexBuildOutcome
{
    internal AtlasIndexBuildOutcome(IndexedAtlasSnapshot snapshot, AtlasMemoryEstimate estimate)
    {
        Snapshot = snapshot;
        Estimate = estimate;
    }

    public IndexedAtlasSnapshot Snapshot { get; }

    public AtlasMemoryEstimate Estimate { get; }
}

internal sealed class StableIdComparer : IComparer<StableId>
{
    internal static StableIdComparer Instance { get; } = new();

    public int Compare(StableId x, StableId y)
    {
        int high = x.High.CompareTo(y.High);
        return high != 0 ? high : x.Low.CompareTo(y.Low);
    }
}

internal sealed class SpatialTileKeyComparer : IComparer<SpatialTileKey>
{
    internal static SpatialTileKeyComparer Instance { get; } = new();

    public int Compare(SpatialTileKey x, SpatialTileKey y) => x.CompareTo(y);
}
