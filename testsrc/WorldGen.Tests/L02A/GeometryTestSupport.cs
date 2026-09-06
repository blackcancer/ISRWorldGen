using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02A;

internal static class GeometryTestSupport
{
    internal const string ConfigHash = "9e12605ff5e0e94ccccf28eac0ded526da1e687a1fd3a0650b782da7484906ed";
    internal const string AssetHash = "cfd370ef8c6540792e17507897d0db5a9d96b621a805d1ccc605abcf89460ddf";

    internal static GenerationIdentity Identity(int seed = 73) => new(
        seed,
        GenerationIdentity.SupportedAlgorithmVersion,
        GenerationIdentity.SupportedSchemaVersion,
        Hash256.Parse(ConfigHash),
        Hash256.Parse(AssetHash),
        "l02a-exact-planar-v1");

    internal static AtlasSite Site(ulong index, long x, long z) => new(
        StableId.Derive(RandomDomain.Sites, StableId.Zero, index),
        x,
        z);

    internal static AtlasMesh Success(GenerationResult<AtlasMesh> result)
    {
        Assert.IsInstanceOfType<GenerationSuccess<AtlasMesh>>(result);
        return ((GenerationSuccess<AtlasMesh>)result).Snapshot;
    }

    internal static void AssertValidTopology(AtlasMesh mesh)
    {
        Assert.IsGreaterThan(0, mesh.Sites.Count);
        Assert.HasCount(mesh.Sites.Count, mesh.Cells);

        ExactRational totalArea = ExactRational.Zero;
        var cells = mesh.Cells.ToDictionary(cell => cell.SiteId);
        foreach (VoronoiCell cell in mesh.Cells)
        {
            Assert.IsGreaterThan(2, cell.Vertices.Count, $"Cell {cell.SiteId} is not a polygon.");
            Assert.IsGreaterThan(ExactRational.Zero, cell.Area, $"Cell {cell.SiteId} has non-positive area.");
            totalArea += cell.Area;

            StableId[] sortedNeighbors = cell.NeighborIds.Order(StableIdComparer.Instance).ToArray();
            CollectionAssert.AreEqual(sortedNeighbors, cell.NeighborIds.ToArray());
            foreach (StableId neighbor in cell.NeighborIds)
            {
                CollectionAssert.Contains(cells[neighbor].NeighborIds, cell.SiteId);
            }

            foreach (ExactPoint vertex in cell.Vertices)
            {
                Assert.IsGreaterThanOrEqualTo(ExactRational.FromInt64(mesh.Bounds.MinX), vertex.X);
                Assert.IsLessThanOrEqualTo(ExactRational.FromInt64(mesh.Bounds.MaxXExclusive), vertex.X);
                Assert.IsGreaterThanOrEqualTo(ExactRational.FromInt64(mesh.Bounds.MinZ), vertex.Z);
                Assert.IsLessThanOrEqualTo(ExactRational.FromInt64(mesh.Bounds.MaxZExclusive), vertex.Z);
            }
        }

        Assert.AreEqual(mesh.Bounds.Area, totalArea, "Bounded Voronoi cells must exactly partition the world.");

        var edgeKeys = new HashSet<(StableId, StableId)>();
        foreach (AtlasEdge edge in mesh.Edges)
        {
            Assert.IsLessThan(0, StableIdComparer.Instance.Compare(edge.A, edge.B));
            Assert.IsTrue(edgeKeys.Add((edge.A, edge.B)), "Duplicate Delaunay edge.");
            CollectionAssert.Contains(cells[edge.A].NeighborIds, edge.B);
            CollectionAssert.Contains(cells[edge.B].NeighborIds, edge.A);
        }

        var neighborKeys = mesh.Cells
            .SelectMany(cell => cell.NeighborIds.Select(neighbor => CanonicalEdge(cell.SiteId, neighbor)))
            .ToHashSet();
        Assert.HasCount(edgeKeys.Count, neighborKeys, "Cell neighbor lists and the canonical Delaunay edge set must agree exactly.");
        Assert.IsTrue(edgeKeys.SetEquals(neighborKeys), "The proxy dual cannot publish adjacency absent from cell neighbor lists.");

        AtlasSite[] sites = mesh.Sites.ToArray();
        foreach (DelaunayTriangle triangle in mesh.Triangles)
        {
            AtlasSite a = sites.Single(site => site.Id == triangle.A);
            AtlasSite b = sites.Single(site => site.Id == triangle.B);
            AtlasSite c = sites.Single(site => site.Id == triangle.C);
            Assert.AreEqual(1, GeometryPredicates.Orientation(a, b, c), "Triangle orientation must be strictly CCW.");
            Assert.Contains(CanonicalEdge(triangle.A, triangle.B), edgeKeys);
            Assert.Contains(CanonicalEdge(triangle.B, triangle.C), edgeKeys);
            Assert.Contains(CanonicalEdge(triangle.C, triangle.A), edgeKeys);

            foreach (AtlasSite site in sites.Where(site => site.Id != a.Id && site.Id != b.Id && site.Id != c.Id))
            {
                Assert.IsLessThanOrEqualTo(
                    0,
                    GeometryPredicates.InCircle(a, b, c, site),
                    $"Site {site.Id} lies illegally inside a Delaunay circumcircle.");
            }
        }

        for (int first = 0; first < mesh.Edges.Count; first++)
        {
            for (int second = first + 1; second < mesh.Edges.Count; second++)
            {
                AtlasEdge left = mesh.Edges[first];
                AtlasEdge right = mesh.Edges[second];
                if (left.A == right.A || left.A == right.B || left.B == right.A || left.B == right.B)
                {
                    continue;
                }

                AtlasSite a = sites.Single(site => site.Id == left.A);
                AtlasSite b = sites.Single(site => site.Id == left.B);
                AtlasSite c = sites.Single(site => site.Id == right.A);
                AtlasSite d = sites.Single(site => site.Id == right.B);
                Assert.IsFalse(ProperlyCrosses(a, b, c, d), $"Edges {left} and {right} cross illegally.");
            }
        }

        if (mesh.TopologyDimension == AtlasTopologyDimension.Planar)
        {
            int hullVertices = ConvexHullVertexCount(sites);
            Assert.HasCount(
                (3 * sites.Length) - 3 - hullVertices,
                mesh.Edges,
                $"A planar Delaunay triangulation must cover the complete convex hull in {mesh.Bounds}.");
            Assert.HasCount(
                (2 * sites.Length) - 2 - hullVertices,
                mesh.Triangles,
                $"A planar Delaunay triangulation must expose every bounded face in {mesh.Bounds}.");
        }
    }

    internal static string Fingerprint(AtlasMesh mesh)
    {
        var builder = new StringBuilder();
        builder.Append("L02A-PROOF-V1\n");
        builder.Append(mesh.Bounds.MinX).Append(',').Append(mesh.Bounds.MinZ).Append(',')
            .Append(mesh.Bounds.MaxXExclusive).Append(',').Append(mesh.Bounds.MaxZExclusive).Append('\n');
        foreach (AtlasSite site in mesh.Sites)
        {
            builder.Append("S|").Append(site.Id).Append('|').Append(site.X).Append('|').Append(site.Z).Append('\n');
        }

        foreach (AtlasEdge edge in mesh.Edges)
        {
            builder.Append("E|").Append(edge.A).Append('|').Append(edge.B).Append('\n');
        }

        foreach (DelaunayTriangle triangle in mesh.Triangles)
        {
            builder.Append("T|").Append(triangle.A).Append('|').Append(triangle.B).Append('|').Append(triangle.C).Append('\n');
        }

        foreach (VoronoiCell cell in mesh.Cells)
        {
            builder.Append("C|").Append(cell.SiteId).Append('|').Append(cell.Area).Append('|')
                .Append((int)cell.BoundaryMask).Append('\n');
            foreach (ExactPoint vertex in cell.Vertices)
            {
                builder.Append("V|").Append(vertex.X).Append('|').Append(vertex.Z).Append('\n');
            }

            foreach (StableId neighbor in cell.NeighborIds)
            {
                builder.Append("N|").Append(neighbor).Append('\n');
            }
        }

        foreach (CollapsedDuplicate duplicate in mesh.CollapsedDuplicates)
        {
            builder.Append("D|").Append(duplicate.DuplicateId).Append('|').Append(duplicate.CanonicalId).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    internal static IReadOnlyList<AtlasSite> DeterminismCorpus() =>
    [
        Site(0, 0, 0),
        Site(1, 1, 3),
        Site(2, 17, 2),
        Site(3, 63, 1),
        Site(4, 4, 41),
        Site(5, 31, 29),
        Site(6, 62, 62),
        Site(7, 2, 63),
        Site(8, 45, 51),
    ];

    private static bool ProperlyCrosses(AtlasSite a, AtlasSite b, AtlasSite c, AtlasSite d)
    {
        int abc = GeometryPredicates.Orientation(a, b, c);
        int abd = GeometryPredicates.Orientation(a, b, d);
        int cda = GeometryPredicates.Orientation(c, d, a);
        int cdb = GeometryPredicates.Orientation(c, d, b);
        return abc * abd < 0 && cda * cdb < 0;
    }

    private static int ConvexHullVertexCount(IReadOnlyList<AtlasSite> sites)
    {
        AtlasSite[] ordered = sites.OrderBy(site => site.X).ThenBy(site => site.Z).ToArray();
        var lower = new List<AtlasSite>();
        foreach (AtlasSite site in ordered)
        {
            while (lower.Count >= 2 && GeometryPredicates.Orientation(lower[^2], lower[^1], site) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(site);
        }

        var upper = new List<AtlasSite>();
        foreach (AtlasSite site in ordered.Reverse())
        {
            while (upper.Count >= 2 && GeometryPredicates.Orientation(upper[^2], upper[^1], site) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(site);
        }

        AtlasSite[] extremeHull = lower.Take(lower.Count - 1)
            .Concat(upper.Take(upper.Count - 1))
            .ToArray();
        return sites.Count(site => Enumerable.Range(0, extremeHull.Length).Any(index =>
        {
            AtlasSite first = extremeHull[index];
            AtlasSite second = extremeHull[(index + 1) % extremeHull.Length];
            return GeometryPredicates.Orientation(first, second, site) == 0 &&
                site.X >= Math.Min(first.X, second.X) && site.X <= Math.Max(first.X, second.X) &&
                site.Z >= Math.Min(first.Z, second.Z) && site.Z <= Math.Max(first.Z, second.Z);
        }));
    }

    private static (StableId, StableId) CanonicalEdge(StableId left, StableId right) =>
        StableIdComparer.Instance.Compare(left, right) < 0 ? (left, right) : (right, left);

    internal sealed class StableIdComparer : IComparer<StableId>
    {
        internal static StableIdComparer Instance { get; } = new();

        public int Compare(StableId x, StableId y)
        {
            int high = x.High.CompareTo(y.High);
            return high != 0 ? high : x.Low.CompareTo(y.Low);
        }
    }
}
