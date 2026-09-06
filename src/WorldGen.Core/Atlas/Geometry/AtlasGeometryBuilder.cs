using System.Numerics;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Geometry;

/// <summary>
/// Builds a bounded Voronoi diagram through exact half-plane clipping, then its planar Delaunay dual.
/// Cocircular dual faces are triangulated as a CCW fan rooted at their smallest StableId. Strictly
/// collinear input remains an explicit linear topology and never receives fabricated triangles.
/// </summary>
public static class AtlasGeometryBuilder
{
    public static GenerationResult<AtlasMesh> Build(
        GenerationIdentity identity,
        WorldBounds bounds,
        IEnumerable<AtlasSite> sites,
        AtlasGeometryBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(sites);
        options ??= AtlasGeometryBuildOptions.Default;

        if (!TryNormalizeSites(bounds, sites, out AtlasSite[]? canonicalSites, out CollapsedDuplicate[]? duplicates, out string? error))
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "atlas.geometry.validate", error!);
        }

        try
        {
            AtlasMesh mesh = BuildCore(bounds, canonicalSites!, duplicates!, options);
            return GenerationResult<AtlasMesh>.Success(mesh);
        }
        catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException)
        {
            return Failure(
                identity,
                GenerationFailureCode.GeometryFailure,
                "atlas.geometry.construct",
                exception.Message);
        }
    }

    private static AtlasMesh BuildCore(
        WorldBounds bounds,
        AtlasSite[] sites,
        CollapsedDuplicate[] duplicates,
        AtlasGeometryBuildOptions options)
    {
        HalfPlane[,]? precomputed = options.CacheMode == GeometryCacheMode.Precomputed
            ? PrecomputeHalfPlanes(sites)
            : null;
        var cellData = new CellData[sites.Length];
        var dualPolygons = new ExactPoint[sites.Length][];
        ClippingBox dualBox = ClippingBox.CreateDualProxy(bounds);
        Parallel.For(
            0,
            sites.Length,
            new ParallelOptions { MaxDegreeOfParallelism = options.Workers },
            siteIndex =>
            {
                cellData[siteIndex] = BuildCell(bounds, sites, siteIndex, precomputed);
                dualPolygons[siteIndex] = BuildPolygon(dualBox.Corners, sites, siteIndex, precomputed);
            });

        var neighborSets = Enumerable.Range(0, sites.Length)
            .Select(_ => new HashSet<int>())
            .ToArray();
        var edgeKeys = new HashSet<IndexEdge>();
        var segmentOwners = new Dictionary<ExactSegment, List<int>>();
        for (int cellIndex = 0; cellIndex < cellData.Length; cellIndex++)
        {
            IReadOnlyList<ExactPoint> polygon = dualPolygons[cellIndex];
            for (int vertexIndex = 0; vertexIndex < polygon.Count; vertexIndex++)
            {
                ExactPoint first = polygon[vertexIndex];
                ExactPoint second = polygon[(vertexIndex + 1) % polygon.Count];
                var segment = ExactSegment.Create(first, second);
                if (!segmentOwners.TryGetValue(segment, out List<int>? owners))
                {
                    owners = [];
                    segmentOwners.Add(segment, owners);
                }

                owners.Add(cellIndex);
            }
        }

        foreach ((ExactSegment segment, List<int> owners) in segmentOwners)
        {
            if (owners.Count == 2)
            {
                AddEdge(owners[0], owners[1], edgeKeys, neighborSets);
                continue;
            }

            if (owners.Count != 1 || !dualBox.IsBoundarySegment(segment))
            {
                throw new InvalidOperationException("Voronoi edge ownership is neither paired nor on the physical world boundary.");
            }
        }

        List<int[]> faces = ExtractPositiveFaces(sites, neighborSets);
        var triangles = new List<IndexTriangle>();
        foreach (int[] face in faces)
        {
            int[] canonicalFace = RotateToSmallestId(face, sites);
            for (int index = 1; index < canonicalFace.Length - 1; index++)
            {
                int a = canonicalFace[0];
                int b = canonicalFace[index];
                int c = canonicalFace[index + 1];
                int orientation = GeometryPredicates.Orientation(sites[a], sites[b], sites[c]);
                if (orientation <= 0)
                {
                    throw new InvalidOperationException("A planar Delaunay face is not strictly convex and CCW.");
                }

                triangles.Add(new IndexTriangle(a, b, c));
                AddEdge(a, b, edgeKeys, neighborSets);
                AddEdge(b, c, edgeKeys, neighborSets);
                AddEdge(c, a, edgeKeys, neighborSets);
            }
        }

        AtlasTopologyDimension dimension = DetermineDimension(sites);
        if (dimension != AtlasTopologyDimension.Planar && triangles.Count != 0)
        {
            throw new InvalidOperationException("A lower-dimensional atlas cannot publish triangles.");
        }

        AtlasEdge[] edges = edgeKeys
            .Select(edge => new AtlasEdge(sites[edge.First].Id, sites[edge.Second].Id))
            .Order(AtlasEdgeComparer.Instance)
            .ToArray();
        DelaunayTriangle[] publishedTriangles = triangles
            .Select(triangle => new DelaunayTriangle(
                sites[triangle.A].Id,
                sites[triangle.B].Id,
                sites[triangle.C].Id))
            .Order(DelaunayTriangleComparer.Instance)
            .ToArray();
        VoronoiCell[] cells = Enumerable.Range(0, sites.Length)
            .Select(index => new VoronoiCell(
                sites[index].Id,
                cellData[index].Vertices,
                cellData[index].Area,
                cellData[index].BoundaryMask,
                neighborSets[index].Select(neighbor => sites[neighbor].Id)))
            .ToArray();
        return new AtlasMesh(bounds, sites, edges, publishedTriangles, cells, duplicates, dimension);
    }

    private static bool TryNormalizeSites(
        WorldBounds bounds,
        IEnumerable<AtlasSite> input,
        out AtlasSite[]? sites,
        out CollapsedDuplicate[]? duplicates,
        out string? error)
    {
        sites = null;
        duplicates = null;
        error = null;
        AtlasSite[] materialized;
        try
        {
            materialized = input.ToArray();
        }
        catch (Exception exception)
        {
            error = $"Site enumeration failed: {exception.Message}";
            return false;
        }

        if (materialized.Length == 0)
        {
            error = "At least one site is required.";
            return false;
        }

        var coordinatesById = new Dictionary<StableId, (long X, long Z)>();
        foreach (AtlasSite site in materialized)
        {
            if (!bounds.Contains(site.X, site.Z))
            {
                error = $"Site {site.Id} lies outside semi-open world bounds.";
                return false;
            }

            if (coordinatesById.TryGetValue(site.Id, out (long X, long Z) existing) &&
                existing != (site.X, site.Z))
            {
                error = $"StableId {site.Id} is assigned to distinct coordinates.";
                return false;
            }

            coordinatesById[site.Id] = (site.X, site.Z);
        }

        var unique = new List<AtlasSite>();
        var collapsed = new List<CollapsedDuplicate>();
        foreach (IGrouping<(long X, long Z), AtlasSite> group in materialized
                     .Distinct()
                     .GroupBy(site => (site.X, site.Z)))
        {
            AtlasSite[] ordered = group.OrderBy(site => site.Id, StableIdOrdering.Instance).ToArray();
            unique.Add(ordered[0]);
            collapsed.AddRange(ordered.Skip(1).Select(site => new CollapsedDuplicate(site.Id, ordered[0].Id)));
        }

        sites = unique.OrderBy(site => site.Id, StableIdOrdering.Instance).ToArray();
        duplicates = collapsed
            .OrderBy(duplicate => duplicate.DuplicateId, StableIdOrdering.Instance)
            .ThenBy(duplicate => duplicate.CanonicalId, StableIdOrdering.Instance)
            .ToArray();
        return true;
    }

    private static HalfPlane[,] PrecomputeHalfPlanes(IReadOnlyList<AtlasSite> sites)
    {
        var result = new HalfPlane[sites.Count, sites.Count];
        for (int site = 0; site < sites.Count; site++)
        {
            for (int other = 0; other < sites.Count; other++)
            {
                if (site != other)
                {
                    result[site, other] = HalfPlane.Between(sites[site], sites[other]);
                }
            }
        }

        return result;
    }

    private static CellData BuildCell(
        WorldBounds bounds,
        IReadOnlyList<AtlasSite> sites,
        int siteIndex,
        HalfPlane[,]? precomputed)
    {
        ExactPoint[] worldCorners =
        [
            ExactPoint.FromInt64(bounds.MinX, bounds.MinZ),
            ExactPoint.FromInt64(bounds.MaxXExclusive, bounds.MinZ),
            ExactPoint.FromInt64(bounds.MaxXExclusive, bounds.MaxZExclusive),
            ExactPoint.FromInt64(bounds.MinX, bounds.MaxZExclusive),
        ];
        List<ExactPoint> polygon = BuildPolygon(worldCorners, sites, siteIndex, precomputed).ToList();
        ExactRational twiceArea = SignedDoubleArea(polygon);
        if (polygon.Count < 3 || twiceArea.Sign <= 0)
        {
            throw new InvalidOperationException($"Site {sites[siteIndex].Id} produced a degenerate Voronoi cell.");
        }

        ExactRational area = twiceArea / new ExactRational(2, BigInteger.One);
        WorldBoundaryMask mask = WorldBoundaryMask.None;
        ExactRational minX = ExactRational.FromInt64(bounds.MinX);
        ExactRational maxX = ExactRational.FromInt64(bounds.MaxXExclusive);
        ExactRational minZ = ExactRational.FromInt64(bounds.MinZ);
        ExactRational maxZ = ExactRational.FromInt64(bounds.MaxZExclusive);
        foreach (ExactPoint point in polygon)
        {
            if (point.X == minX)
            {
                mask |= WorldBoundaryMask.MinX;
            }
            if (point.X == maxX)
            {
                mask |= WorldBoundaryMask.MaxX;
            }
            if (point.Z == minZ)
            {
                mask |= WorldBoundaryMask.MinZ;
            }
            if (point.Z == maxZ)
            {
                mask |= WorldBoundaryMask.MaxZ;
            }
        }

        return new CellData(polygon.ToArray(), area, mask);
    }

    private static ExactPoint[] BuildPolygon(
        IReadOnlyList<ExactPoint> initialPolygon,
        IReadOnlyList<AtlasSite> sites,
        int siteIndex,
        HalfPlane[,]? precomputed)
    {
        List<ExactPoint> polygon = initialPolygon.ToList();
        for (int otherIndex = 0; otherIndex < sites.Count; otherIndex++)
        {
            if (otherIndex == siteIndex)
            {
                continue;
            }

            HalfPlane halfPlane = precomputed is null
                ? HalfPlane.Between(sites[siteIndex], sites[otherIndex])
                : precomputed[siteIndex, otherIndex];
            polygon = Clip(polygon, halfPlane);
            if (polygon.Count == 0)
            {
                throw new InvalidOperationException($"Site {sites[siteIndex].Id} produced an empty Voronoi cell.");
            }
        }

        polygon = NormalizePolygon(polygon);
        ExactRational twiceArea = SignedDoubleArea(polygon);
        if (polygon.Count < 3 || twiceArea.Sign <= 0)
        {
            throw new InvalidOperationException($"Site {sites[siteIndex].Id} produced a degenerate Voronoi cell.");
        }

        return polygon.ToArray();
    }

    private static List<ExactPoint> Clip(IReadOnlyList<ExactPoint> polygon, HalfPlane halfPlane)
    {
        var output = new List<ExactPoint>();
        for (int index = 0; index < polygon.Count; index++)
        {
            ExactPoint current = polygon[index];
            ExactPoint next = polygon[(index + 1) % polygon.Count];
            ExactRational currentValue = halfPlane.Evaluate(current);
            ExactRational nextValue = halfPlane.Evaluate(next);
            bool currentInside = currentValue.Sign <= 0;
            bool nextInside = nextValue.Sign <= 0;
            if (currentInside)
            {
                output.Add(current);
            }

            if (currentInside != nextInside)
            {
                ExactRational t = currentValue / (currentValue - nextValue);
                output.Add(new ExactPoint(
                    current.X + ((next.X - current.X) * t),
                    current.Z + ((next.Z - current.Z) * t)));
            }
        }

        return NormalizePolygon(output);
    }

    private static List<ExactPoint> NormalizePolygon(IEnumerable<ExactPoint> input)
    {
        var polygon = new List<ExactPoint>();
        foreach (ExactPoint point in input)
        {
            if (polygon.Count == 0 || polygon[^1] != point)
            {
                polygon.Add(point);
            }
        }

        if (polygon.Count > 1 && polygon[0] == polygon[^1])
        {
            polygon.RemoveAt(polygon.Count - 1);
        }

        bool changed;
        do
        {
            changed = false;
            for (int index = 0; polygon.Count >= 3 && index < polygon.Count; index++)
            {
                ExactPoint previous = polygon[(index - 1 + polygon.Count) % polygon.Count];
                ExactPoint current = polygon[index];
                ExactPoint next = polygon[(index + 1) % polygon.Count];
                if (Orientation(previous, current, next) == 0)
                {
                    polygon.RemoveAt(index);
                    changed = true;
                    break;
                }
            }
        }
        while (changed);

        if (polygon.Count >= 3 && SignedDoubleArea(polygon).Sign < 0)
        {
            polygon.Reverse();
        }

        if (polygon.Count > 0)
        {
            int first = 0;
            for (int index = 1; index < polygon.Count; index++)
            {
                if (polygon[index].CompareTo(polygon[first]) < 0)
                {
                    first = index;
                }
            }

            if (first != 0)
            {
                polygon = polygon.Skip(first).Concat(polygon.Take(first)).ToList();
            }
        }

        return polygon;
    }

    private static int Orientation(ExactPoint a, ExactPoint b, ExactPoint c) =>
        (((b.X - a.X) * (c.Z - a.Z)) - ((b.Z - a.Z) * (c.X - a.X))).Sign;

    private static ExactRational SignedDoubleArea(IReadOnlyList<ExactPoint> polygon)
    {
        ExactRational area = ExactRational.Zero;
        for (int index = 0; index < polygon.Count; index++)
        {
            ExactPoint current = polygon[index];
            ExactPoint next = polygon[(index + 1) % polygon.Count];
            area += (current.X * next.Z) - (current.Z * next.X);
        }

        return area;
    }

    private static List<int[]> ExtractPositiveFaces(
        IReadOnlyList<AtlasSite> sites,
        IReadOnlyList<HashSet<int>> neighborSets)
    {
        int[][] orderedNeighbors = Enumerable.Range(0, sites.Count)
            .Select(index => neighborSets[index]
                .OrderBy(neighbor => neighbor, new AngularNeighborComparer(sites, index))
                .ToArray())
            .ToArray();
        var visited = new HashSet<DirectedEdge>();
        var faces = new List<int[]>();
        int directedEdgeCount = neighborSets.Sum(neighbors => neighbors.Count);

        for (int from = 0; from < sites.Count; from++)
        {
            foreach (int to in orderedNeighbors[from])
            {
                var start = new DirectedEdge(from, to);
                if (visited.Contains(start))
                {
                    continue;
                }

                var face = new List<int>();
                DirectedEdge current = start;
                for (int step = 0; step <= directedEdgeCount; step++)
                {
                    if (!visited.Add(current) && current != start)
                    {
                        throw new InvalidOperationException("Planar dual face traversal revisited a directed edge prematurely.");
                    }

                    face.Add(current.From);
                    int[] nextCandidates = orderedNeighbors[current.To];
                    int incomingIndex = Array.IndexOf(nextCandidates, current.From);
                    if (incomingIndex < 0)
                    {
                        throw new InvalidOperationException("Delaunay adjacency is not symmetric.");
                    }

                    int next = nextCandidates[(incomingIndex - 1 + nextCandidates.Length) % nextCandidates.Length];
                    current = new DirectedEdge(current.To, next);
                    if (current == start)
                    {
                        break;
                    }

                    if (step == directedEdgeCount)
                    {
                        throw new InvalidOperationException("Planar dual face traversal did not close.");
                    }
                }

                if (face.Count >= 3 && SignedSiteDoubleArea(face, sites).Sign > 0)
                {
                    faces.Add(face.ToArray());
                }
            }
        }

        return faces;
    }

    private static BigInteger SignedSiteDoubleArea(IReadOnlyList<int> face, IReadOnlyList<AtlasSite> sites)
    {
        BigInteger area = BigInteger.Zero;
        for (int index = 0; index < face.Count; index++)
        {
            AtlasSite current = sites[face[index]];
            AtlasSite next = sites[face[(index + 1) % face.Count]];
            area += ((BigInteger)current.X * next.Z) - ((BigInteger)current.Z * next.X);
        }

        return area;
    }

    private static int[] RotateToSmallestId(int[] face, IReadOnlyList<AtlasSite> sites)
    {
        int first = 0;
        for (int index = 1; index < face.Length; index++)
        {
            if (StableIdOrdering.Instance.Compare(sites[face[index]].Id, sites[face[first]].Id) < 0)
            {
                first = index;
            }
        }

        return face.Skip(first).Concat(face.Take(first)).ToArray();
    }

    private static void AddEdge(
        int left,
        int right,
        ISet<IndexEdge> edges,
        IReadOnlyList<HashSet<int>> neighbors)
    {
        if (left == right)
        {
            throw new InvalidOperationException("A Delaunay self-edge is invalid.");
        }

        var edge = IndexEdge.Create(left, right);
        edges.Add(edge);
        neighbors[left].Add(right);
        neighbors[right].Add(left);
    }

    private static AtlasTopologyDimension DetermineDimension(IReadOnlyList<AtlasSite> sites)
    {
        if (sites.Count == 1)
        {
            return AtlasTopologyDimension.Point;
        }

        for (int index = 2; index < sites.Count; index++)
        {
            if (GeometryPredicates.Orientation(sites[0], sites[1], sites[index]) != 0)
            {
                return AtlasTopologyDimension.Planar;
            }
        }

        return AtlasTopologyDimension.Linear;
    }

    private static GenerationResult<AtlasMesh> Failure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details) =>
        GenerationResult<AtlasMesh>.Failure(
            new GenerationError(
                code,
                identity.NativeSeed,
                stage,
                StableId.Zero,
                identity.GeographyConfigHash,
                details,
                false));

    private readonly record struct HalfPlane(BigInteger A, BigInteger B, BigInteger C)
    {
        internal static HalfPlane Between(AtlasSite kept, AtlasSite other) => new(
            2 * ((BigInteger)other.X - kept.X),
            2 * ((BigInteger)other.Z - kept.Z),
            ((BigInteger)other.X * other.X) + ((BigInteger)other.Z * other.Z) -
            ((BigInteger)kept.X * kept.X) - ((BigInteger)kept.Z * kept.Z));

        internal ExactRational Evaluate(ExactPoint point) =>
            Scale(A, point.X) + Scale(B, point.Z) - new ExactRational(C, BigInteger.One);

        private static ExactRational Scale(BigInteger coefficient, ExactRational value) => new(
            coefficient * value.Numerator,
            value.Denominator);
    }

    private readonly record struct ExactSegment(ExactPoint First, ExactPoint Second)
    {
        internal static ExactSegment Create(ExactPoint first, ExactPoint second)
        {
            if (first == second)
            {
                throw new InvalidOperationException("Zero-length Voronoi edge survived polygon normalization.");
            }

            return first.CompareTo(second) < 0 ? new(first, second) : new(second, first);
        }
    }

    private readonly record struct CellData(
        ExactPoint[] Vertices,
        ExactRational Area,
        WorldBoundaryMask BoundaryMask);

    private sealed class ClippingBox
    {
        private ClippingBox(ExactRational minX, ExactRational minZ, ExactRational maxX, ExactRational maxZ)
        {
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
            Corners =
            [
                new ExactPoint(minX, minZ),
                new ExactPoint(maxX, minZ),
                new ExactPoint(maxX, maxZ),
                new ExactPoint(minX, maxZ),
            ];
        }

        internal ExactRational MinX { get; }

        internal ExactRational MinZ { get; }

        internal ExactRational MaxX { get; }

        internal ExactRational MaxZ { get; }

        internal ExactPoint[] Corners { get; }

        internal static ClippingBox CreateDualProxy(WorldBounds bounds)
        {
            BigInteger span = BigInteger.Max(bounds.Width, bounds.Length);
            // With integral sites translated into a box of span S, every finite circumcenter coordinate is
            // a determinant quotient with a non-zero integral denominator and a cubic numerator bound.
            // Eight (S+1)^3 therefore encloses all finite Voronoi vertices while preserving unbounded rays.
            BigInteger margin = 8 * BigInteger.Pow(span + BigInteger.One, 3);
            return new ClippingBox(
                new ExactRational((BigInteger)bounds.MinX - margin, BigInteger.One),
                new ExactRational((BigInteger)bounds.MinZ - margin, BigInteger.One),
                new ExactRational((BigInteger)bounds.MaxXExclusive + margin, BigInteger.One),
                new ExactRational((BigInteger)bounds.MaxZExclusive + margin, BigInteger.One));
        }

        internal bool IsBoundarySegment(ExactSegment segment) =>
            (segment.First.X == MinX && segment.Second.X == MinX) ||
            (segment.First.X == MaxX && segment.Second.X == MaxX) ||
            (segment.First.Z == MinZ && segment.Second.Z == MinZ) ||
            (segment.First.Z == MaxZ && segment.Second.Z == MaxZ);
    }

    private readonly record struct IndexEdge(int First, int Second)
    {
        internal static IndexEdge Create(int left, int right) =>
            left < right ? new(left, right) : new(right, left);
    }

    private readonly record struct DirectedEdge(int From, int To);

    private readonly record struct IndexTriangle(int A, int B, int C);

    private sealed class AngularNeighborComparer(IReadOnlyList<AtlasSite> sites, int origin) : IComparer<int>
    {
        public int Compare(int left, int right)
        {
            if (left == right)
            {
                return 0;
            }

            BigInteger leftX = (BigInteger)sites[left].X - sites[origin].X;
            BigInteger leftZ = (BigInteger)sites[left].Z - sites[origin].Z;
            BigInteger rightX = (BigInteger)sites[right].X - sites[origin].X;
            BigInteger rightZ = (BigInteger)sites[right].Z - sites[origin].Z;
            int leftHalf = Half(leftX, leftZ);
            int rightHalf = Half(rightX, rightZ);
            if (leftHalf != rightHalf)
            {
                return leftHalf.CompareTo(rightHalf);
            }

            int cross = ((leftX * rightZ) - (leftZ * rightX)).Sign;
            if (cross != 0)
            {
                return -cross;
            }

            BigInteger leftLength = (leftX * leftX) + (leftZ * leftZ);
            BigInteger rightLength = (rightX * rightX) + (rightZ * rightZ);
            int length = leftLength.CompareTo(rightLength);
            return length != 0 ? length : left.CompareTo(right);
        }

        private static int Half(BigInteger x, BigInteger z) =>
            z.Sign > 0 || (z.IsZero && x.Sign >= 0) ? 0 : 1;
    }

    private sealed class AtlasEdgeComparer : IComparer<AtlasEdge>
    {
        internal static AtlasEdgeComparer Instance { get; } = new();

        public int Compare(AtlasEdge x, AtlasEdge y)
        {
            int first = StableIdOrdering.Instance.Compare(x.A, y.A);
            return first != 0 ? first : StableIdOrdering.Instance.Compare(x.B, y.B);
        }
    }

    private sealed class DelaunayTriangleComparer : IComparer<DelaunayTriangle>
    {
        internal static DelaunayTriangleComparer Instance { get; } = new();

        public int Compare(DelaunayTriangle x, DelaunayTriangle y)
        {
            int first = StableIdOrdering.Instance.Compare(x.A, y.A);
            if (first != 0)
            {
                return first;
            }

            int second = StableIdOrdering.Instance.Compare(x.B, y.B);
            return second != 0 ? second : StableIdOrdering.Instance.Compare(x.C, y.C);
        }
    }
}
