using System.Collections.ObjectModel;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Geometry;

public readonly record struct CollapsedDuplicate(StableId DuplicateId, StableId CanonicalId);

public readonly record struct AtlasEdge
{
    internal AtlasEdge(StableId a, StableId b)
    {
        if (StableIdOrdering.Instance.Compare(a, b) >= 0)
        {
            throw new ArgumentException("Atlas edges require two distinct IDs in canonical ascending order.");
        }

        A = a;
        B = b;
    }

    public StableId A { get; }

    public StableId B { get; }
}

public readonly record struct DelaunayTriangle
{
    internal DelaunayTriangle(StableId a, StableId b, StableId c)
    {
        A = a;
        B = b;
        C = c;
    }

    public StableId A { get; }

    public StableId B { get; }

    public StableId C { get; }
}

public enum AtlasTopologyDimension
{
    Point = 0,
    Linear = 1,
    Planar = 2,
}

public sealed class VoronoiCell
{
    internal VoronoiCell(
        StableId siteId,
        IEnumerable<ExactPoint> vertices,
        ExactRational area,
        WorldBoundaryMask boundaryMask,
        IEnumerable<StableId> neighborIds)
    {
        ExactPoint[] vertexCopy = vertices.ToArray();
        StableId[] neighborCopy = neighborIds.ToArray();
        Array.Sort(neighborCopy, StableIdOrdering.Instance);
        SiteId = siteId;
        Vertices = Array.AsReadOnly(vertexCopy);
        Area = area;
        BoundaryMask = boundaryMask;
        NeighborIds = Array.AsReadOnly(neighborCopy);
    }

    public StableId SiteId { get; }

    public ReadOnlyCollection<ExactPoint> Vertices { get; }

    public ExactRational Area { get; }

    public WorldBoundaryMask BoundaryMask { get; }

    /// <summary>
    /// Canonical neighbors of the complete planar Delaunay dual. World clipping changes the visible cell polygon,
    /// not this topology; cocircular faces additionally contain the canonical zero-length-dual diagonal.
    /// </summary>
    public ReadOnlyCollection<StableId> NeighborIds { get; }
}

public sealed class AtlasMesh
{
    internal AtlasMesh(
        WorldBounds bounds,
        IEnumerable<AtlasSite> sites,
        IEnumerable<AtlasEdge> edges,
        IEnumerable<DelaunayTriangle> triangles,
        IEnumerable<VoronoiCell> cells,
        IEnumerable<CollapsedDuplicate> collapsedDuplicates,
        AtlasTopologyDimension topologyDimension)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        Bounds = bounds;
        Sites = Array.AsReadOnly(sites.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
        Triangles = Array.AsReadOnly(triangles.ToArray());
        Cells = Array.AsReadOnly(cells.ToArray());
        CollapsedDuplicates = Array.AsReadOnly(collapsedDuplicates.ToArray());
        TopologyDimension = topologyDimension;
    }

    public WorldBounds Bounds { get; }

    public ReadOnlyCollection<AtlasSite> Sites { get; }

    public ReadOnlyCollection<AtlasEdge> Edges { get; }

    public ReadOnlyCollection<DelaunayTriangle> Triangles { get; }

    public ReadOnlyCollection<VoronoiCell> Cells { get; }

    public ReadOnlyCollection<CollapsedDuplicate> CollapsedDuplicates { get; }

    /// <summary>Explicitly distinguishes a valid point/collinear dual from a full planar triangulation.</summary>
    public AtlasTopologyDimension TopologyDimension { get; }
}
