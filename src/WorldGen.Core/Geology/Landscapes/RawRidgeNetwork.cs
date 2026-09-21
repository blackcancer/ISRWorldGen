using ISRWorldGen.Core.Foundation;
using static ISRWorldGen.Core.Geology.Landscapes.RawReliefStructure;

namespace ISRWorldGen.Core.Geology.Landscapes;

/// <summary>
/// A bounded structural mountain skeleton. Crest stations, saddles and branching
/// are planned in physical distance, never restarted at tessellation vertices.
/// Not erosion: no water, flow, sediment or raster update occurs here.
/// </summary>
internal sealed class RawRidgeNetwork
{
    internal const string AlgorithmId = "ridge-skeleton-v2-irregular-spurs-tapered-extents";
    internal sealed record Source(Segment[] Segments, double Width, double Strength);
    private readonly record struct Point(double X, double Z);
    private readonly record struct Ridge(Point A, Point B, double Ha, double Hb, double Wa, double Wb);
    private readonly Dictionary<(long X, long Z), Ridge[]> index;
    private readonly double tile;
    internal int SegmentCount { get; }
    private const int MaximumSegments = 200_000;
    private const int MaximumSources = 1024;
    private const long MaximumIndexReferences = 4_000_000;
    private const double CoordinateLimit = 4_000_000_000_000d;

    internal RawRidgeNetwork(int seed, IEnumerable<Source> sources, double scale)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!double.IsFinite(scale) || scale is < .03125 or > 1) throw new ArgumentOutOfRangeException(nameof(scale));
        tile = 4096 * scale;
        var ridges = new List<Ridge>(); int sourceCount = 0;
        foreach (Source source in sources)
        {
            if (++sourceCount > MaximumSources) throw new ArgumentException("Ridge source budget exceeded.", nameof(sources));
            if (source is null || source.Segments is null || source.Segments.Length > 4096 ||
                !double.IsFinite(source.Width) || source.Width < scale || source.Width > 100_000 * scale ||
                !double.IsFinite(source.Strength) || source.Strength is < 0 or > 1)
                throw new ArgumentException("Invalid ridge source.", nameof(sources));
            foreach (Point[] path in Paths(source.Segments))
            {
                if (source.Strength == 0) continue;
                double[] cumulative = new double[path.Length];
                for (int i = 1; i < path.Length; i++) cumulative[i] = cumulative[i - 1] + Distance(path[i - 1], path[i]);
                double length = cumulative[^1];
                // Qualify work BEFORE converting counts or allocating stations.
                double steps = Math.Ceiling(length / (600 * scale));
                if (!double.IsFinite(steps) || steps > 65536) throw new ArgumentException("Ridge chain planning budget exceeded.");
                var key = new StableId(unchecked((ulong)BitConverter.DoubleToInt64Bits(path[0].X)),
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(path[0].Z)));
                double U(ulong counter) => (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, key, counter) >> 11) / 9007199254740992d;
                bool closed = path[0] == path[^1];
                (Point P, Point T) At(double distance)
                {
                    int end = Array.BinarySearch(cumulative, distance);
                    end = Math.Clamp(end < 0 ? ~end : end, 1, cumulative.Length - 1);
                    Point a = path[end - 1], b = path[end]; double span = cumulative[end] - cumulative[end - 1];
                    double t = (distance - cumulative[end - 1]) / span;
                    return (new(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t), new((b.X - a.X) / span, (b.Z - a.Z) / span));
                }
                double Elevation(double d)
                {
                    // Longitudinal saddles are correlated, not independent teeth
                    // at every station. Closed chains have matching end values.
                    double q = d / Math.Max(length, 1);
                    double variation = closed
                        ? .5 + .5 * Noise(seed, 2 * Math.Cos(q * Math.Tau), 2 * Math.Sin(q * Math.Tau), key.Low)
                        : .5 + .5 * Noise(seed, d / (5200 * scale), .371, key.Low);
                    double envelope = closed ? 1 : Smooth(0, Math.Min(1.8 * source.Width, .25 * length), Math.Min(d, length - d));
                    return source.Strength * (.50 + .46 * variation) * (.035 + .965 * envelope);
                }
                double Width(double d) => source.Width * (.33 + .14 * (.5 + .5 * Noise(seed, d / (7100 * scale), 6.317, key.High)));
                int count = Math.Max(2, (int)steps);
                // Keep every non-collinear knot: never draw a shortcut across a
                // geological bend just because two sample stations straddle it.
                double[] stations = Enumerable.Range(0, count + 1).Select(i => length * i / count)
                    .Concat(cumulative).Distinct().Order().ToArray();
                for (int i = 1; i < stations.Length; i++)
                    Add(new(At(stations[i - 1]).P, At(stations[i]).P,
                        Elevation(stations[i - 1]), Elevation(stations[i]), Width(stations[i - 1]), Width(stations[i])));
                ulong counter = 0x300000000;
                // Each branching event has its own side and obliquity. No paired
                // perpendicular ribs and no regular spacing tied to crest nodes.
                for (double d = (1000 + 1600 * U(counter++)) * scale; d < length - 700 * scale;)
                {
                    var station = At(d); double sign = U(counter++) < .5 ? -1 : 1;
                    double forward = -.35 + 1.05 * U(counter++);
                    double lateral = sign * Math.Sqrt(1 - forward * forward);
                    Point direction = new(station.T.X * forward - station.T.Z * lateral,
                        station.T.Z * forward + station.T.X * lateral);
                    double reach = source.Width * (.85 + 2.4 * U(counter++));
                    Grow(station.P, direction, reach, Elevation(d), source.Width * .31, 0, counter);
                    counter += 1024;
                    d += (1100 + 3500 * U(counter++)) * scale;
                }
                void Grow(Point origin, Point direction, double reach, double height, double width, int depth, ulong randomBase)
                {
                    if (depth > 2 || reach < 300 * scale || height <= 0) return;
                    double bend = (2 * U(randomBase + 1) - 1) * .65;
                    Point previous = origin; Point previousDirection = direction;
                    const int parts = 5;
                    for (int part = 1; part <= parts; part++)
                    {
                        double t = part / (double)parts, before = (part - 1d) / parts;
                        double side = reach * (bend * t * t + .09 * Math.Sin(Math.PI * t) * (2 * U(randomBase + 2) - 1));
                        Point next = new(origin.X + direction.X * reach * t - direction.Z * side,
                            origin.Z + direction.Z * reach * t + direction.X * side);
                        double h0 = height * Math.Pow(1 - before, 1.05), h1 = height * Math.Pow(1 - t, 1.05);
                        Add(new(previous, next, h0, h1, width * (1 - .72 * before), width * (1 - .72 * t)));
                        double span = Distance(previous, next);
                        if (span > 0) previousDirection = new((next.X - previous.X) / span, (next.Z - previous.Z) / span);
                        if (part is 2 or 4 && depth < 2 && U(randomBase + (ulong)part * 10) > .25)
                        {
                            ulong child = randomBase + (ulong)part * 100;
                            double sign = U(child) < .5 ? -1 : 1, along = .15 + .50 * U(child + 1);
                            double across = sign * Math.Sqrt(1 - along * along);
                            Point tangent = new(previousDirection.X * along - previousDirection.Z * across,
                                previousDirection.Z * along + previousDirection.X * across);
                            Grow(previous, tangent, reach * (.25 + .20 * U(child + 2)), h0, width * .5, depth + 1, child + 3);
                        }
                        previous = next;
                    }
                }
            }
        }
        SegmentCount = ridges.Count;
        var bins = new Dictionary<(long X, long Z), List<Ridge>>(); long references = 0;
        foreach (Ridge ridge in ridges)
        {
            double radius = 3 * Math.Max(ridge.Wa, ridge.Wb);
            long minX = Bin(Math.Min(ridge.A.X, ridge.B.X) - radius), maxX = Bin(Math.Max(ridge.A.X, ridge.B.X) + radius);
            long minZ = Bin(Math.Min(ridge.A.Z, ridge.B.Z) - radius), maxZ = Bin(Math.Max(ridge.A.Z, ridge.B.Z) + radius);
            long footprint = checked((maxX - minX + 1) * (maxZ - minZ + 1));
            references = checked(references + footprint);
            if (footprint > 16384 || references > MaximumIndexReferences) throw new ArgumentException("Ridge spatial-index budget exceeded.");
            for (long z = minZ; z <= maxZ; z++) for (long x = minX; x <= maxX; x++)
            {
                if (!bins.TryGetValue((x, z), out List<Ridge>? list)) bins.Add((x, z), list = new List<Ridge>());
                list.Add(ridge);
            }
        }
        index = bins.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        void Add(Ridge ridge)
        {
            if (ridges.Count >= MaximumSegments) throw new ArgumentException("Ridge segment budget exceeded.");
            if (ridge.A != ridge.B) ridges.Add(ridge);
        }
    }

    internal double Sample(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || Math.Abs(x) > CoordinateLimit || Math.Abs(z) > CoordinateLimit)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (!index.TryGetValue((Bin(x), Bin(z)), out Ridge[]? ridges)) return 0;
        double result = 0;
        foreach (Ridge ridge in ridges)
        {
            double dx = ridge.B.X - ridge.A.X, dz = ridge.B.Z - ridge.A.Z;
            double t = Math.Clamp(((x - ridge.A.X) * dx + (z - ridge.A.Z) * dz) / (dx * dx + dz * dz), 0d, 1d);
            double qx = x - ridge.A.X - t * dx, qz = z - ridge.A.Z - t * dz;
            double width = ridge.Wa + (ridge.Wb - ridge.Wa) * t;
            double d2 = (qx * qx + qz * qz) / (width * width);
            if (d2 >= 9) continue;
            double taper = 1 - d2 / 9;
            double height = ridge.Ha + (ridge.Hb - ridge.Ha) * t;
            // A broader shoulder supports the crest rather than placing a narrow
            // white wire on a low plain. Maximum, not addition, at intersections.
            double shape = .24 * Math.Exp(-.38 * d2) + .76 * Math.Exp(-1.6 * d2);
            result = Math.Max(result, height * shape * taper * taper);
        }
        return result;
    }

    private long Bin(double value) => checked((long)Math.Floor(value / tile));
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));
    private static int Compare(Point a, Point b) { int c = a.X.CompareTo(b.X); return c != 0 ? c : a.Z.CompareTo(b.Z); }
    private static IEnumerable<Point[]> Paths(IEnumerable<Segment> source)
    {
        var edges = source.Select(s =>
        {
            Point a = new(s.Ax, s.Az), b = new(s.Bx, s.Bz);
            if (!Valid(a) || !Valid(b) || a == b) throw new ArgumentException("Degenerate or unsupported ridge contact.", nameof(source));
            return Compare(a, b) < 0 ? (A: a, B: b) : (A: b, B: a);
        }).Distinct().OrderBy(e => e.A.X).ThenBy(e => e.A.Z).ThenBy(e => e.B.X).ThenBy(e => e.B.Z).ToArray();
        var adjacent = new Dictionary<Point, List<int>>();
        for (int i = 0; i < edges.Length; i++) foreach (Point p in new[] { edges[i].A, edges[i].B })
        { if (!adjacent.TryGetValue(p, out List<int>? list)) adjacent.Add(p, list = new List<int>()); list.Add(i); }
        var used = new bool[edges.Length];
        Point[] Walk(Point start, int edge)
        {
            var points = new List<Point> { start }; Point current = start;
            while (!used[edge])
            {
                used[edge] = true; var e = edges[edge]; current = e.A == current ? e.B : e.A;
                while (points.Count >= 2 && Collinear(points[^2], points[^1], current)) points.RemoveAt(points.Count - 1);
                points.Add(current);
                if (adjacent[current].Count != 2) break;
                int next = adjacent[current].FirstOrDefault(i => !used[i], -1);
                if (next < 0) break; edge = next;
            }
            return points.ToArray();
        }
        foreach (Point start in adjacent.Keys.Where(p => adjacent[p].Count != 2).OrderBy(p => p.X).ThenBy(p => p.Z))
            foreach (int edge in adjacent[start]) if (!used[edge]) yield return Walk(start, edge);
        for (int i = 0; i < edges.Length; i++) if (!used[i]) yield return Walk(edges[i].A, i);
    }
    private static bool Valid(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Z) && Math.Abs(p.X) <= CoordinateLimit && Math.Abs(p.Z) <= CoordinateLimit;
    private static bool Collinear(Point a, Point b, Point c)
    {
        double ux = b.X - a.X, uz = b.Z - a.Z, vx = c.X - b.X, vz = c.Z - b.Z;
        return ux * vx + uz * vz > 0 && Math.Abs(ux * vz - uz * vx) <= 1e-12 * Math.Sqrt((ux * ux + uz * uz) * (vx * vx + vz * vz));
    }
}
