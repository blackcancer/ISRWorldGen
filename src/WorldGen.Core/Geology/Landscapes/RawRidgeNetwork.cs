using ISRWorldGen.Core.Foundation;
using static ISRWorldGen.Core.Geology.Landscapes.RawReliefStructure;

namespace ISRWorldGen.Core.Geology.Landscapes;

/// <summary>
/// Planned structural ridgelines and descending spurs, not a drainage or erosion
/// pass. A spatial index accelerates exactly the same continuous solid field.
/// </summary>
internal sealed class RawRidgeNetwork
{
    internal sealed record Source(Segment[] Segments, double Width, double Strength);
    private readonly record struct Point(double X, double Z);
    private readonly record struct Ridge(Point A, Point B, double Ha, double Hb, double Wa, double Wb);
    private readonly Dictionary<(long X, long Z), Ridge[]> index;
    private readonly double tile;
    internal int SegmentCount { get; }

    internal RawRidgeNetwork(int seed, IEnumerable<Source> sources, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0 || scale > 1) throw new ArgumentOutOfRangeException(nameof(scale));
        tile = 4096 * scale;
        var ridges = new List<Ridge>();
        foreach (Source source in sources)
        {
            if (!(source.Width > 0) || !double.IsFinite(source.Width) || !double.IsFinite(source.Strength) || source.Strength is < 0 or > 1)
                throw new ArgumentException("Invalid ridge source.", nameof(sources));
            foreach (Point[] path in Paths(source.Segments))
            {
                double[] cumulative = new double[path.Length];
                for (int i = 1; i < path.Length; i++) cumulative[i] = cumulative[i - 1] + Distance(path[i - 1], path[i]);
                double length = cumulative[^1];
                if (!(length > 0)) continue;
                var key = new StableId(unchecked((ulong)BitConverter.DoubleToInt64Bits(path[0].X)),
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(path[0].Z)));
                double U(ulong counter) => (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, key, counter) >> 11) / 9007199254740992d;
                (Point P, Point T) At(double distance)
                {
                    int end = 1;
                    while (end < cumulative.Length - 1 && cumulative[end] < distance) end++;
                    Point a = path[end - 1], b = path[end]; double span = cumulative[end] - cumulative[end - 1];
                    double t = (distance - cumulative[end - 1]) / span;
                    return (new Point(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t), new Point((b.X - a.X) / span, (b.Z - a.Z) / span));
                }
                var stations = new List<double> { 0 };
                while (stations[^1] < length)
                    stations.Add(Math.Min(length, stations[^1] + (1300 + 1900 * U(0x100000000UL + (ulong)stations.Count)) * scale));
                var heights = Enumerable.Range(0, stations.Count).Select(i => source.Strength * (.56 + .40 * U(0x200000000UL + (ulong)i))).ToArray();
                for (int i = 1; i < stations.Count; i++)
                    ridges.Add(new Ridge(At(stations[i - 1]).P, At(stations[i]).P, heights[i - 1], heights[i], .38 * source.Width, .38 * source.Width));
                for (int i = 1; i < stations.Count - 1; i++)
                {
                    var station = At(stations[i]);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        ulong k = 0x300000000UL + (ulong)i * 100 + (side < 0 ? 0UL : 50UL);
                        if (U(k) < .36) continue;
                        Point normal = new(-side * station.T.Z, side * station.T.X);
                        double reach = source.Width * (1.1 + 1.5 * U(k + 1));
                        double drift = (U(k + 2) - .5) * .9;
                        Point previous = station.P;
                        for (int part = 1; part <= 3; part++)
                        {
                            double t = part / 3d, before = (part - 1) / 3d;
                            double along = drift * reach * t + .13 * reach * Math.Sin(Math.PI * t) * (2 * U(k + 3) - 1);
                            Point next = new(station.P.X + normal.X * reach * t + station.T.X * along,
                                station.P.Z + normal.Z * reach * t + station.T.Z * along);
                            double h0 = heights[i] * Math.Pow(1 - before, 1.35), h1 = heights[i] * Math.Pow(1 - t, 1.35);
                            ridges.Add(new Ridge(previous, next, h0, h1, source.Width * (.26 - .17 * before), source.Width * (.26 - .17 * t)));
                            if (part == 2 && U(k + 4) > .3)
                            {
                                int branchSide = U(k + 5) < .5 ? -1 : 1;
                                double branchLength = reach * (.24 + .22 * U(k + 6));
                                Point end = new(previous.X + (normal.X * .4 + branchSide * station.T.X) * branchLength,
                                    previous.Z + (normal.Z * .4 + branchSide * station.T.Z) * branchLength);
                                ridges.Add(new Ridge(previous, end, h0, 0, source.Width * .15, source.Width * .05));
                            }
                            previous = next;
                        }
                    }
                }
            }
        }
        SegmentCount = ridges.Count;
        var bins = new Dictionary<(long X, long Z), List<Ridge>>();
        foreach (Ridge ridge in ridges)
        {
            double radius = 3 * Math.Max(ridge.Wa, ridge.Wb);
            long minX = Bin(Math.Min(ridge.A.X, ridge.B.X) - radius), maxX = Bin(Math.Max(ridge.A.X, ridge.B.X) + radius);
            long minZ = Bin(Math.Min(ridge.A.Z, ridge.B.Z) - radius), maxZ = Bin(Math.Max(ridge.A.Z, ridge.B.Z) + radius);
            for (long z = minZ; z <= maxZ; z++)
            for (long x = minX; x <= maxX; x++)
            {
                if (!bins.TryGetValue((x, z), out List<Ridge>? list)) bins.Add((x, z), list = new List<Ridge>());
                list.Add(ridge);
            }
        }
        index = bins.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    internal double Sample(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(x));
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
            result = Math.Max(result, height * Math.Exp(-d2) * taper * taper);
        }
        return result;
    }

    private long Bin(double value) => checked((long)Math.Floor(value / tile));
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));
    private static int Compare(Point a, Point b) { int c = a.X.CompareTo(b.X); return c != 0 ? c : a.Z.CompareTo(b.Z); }

    // Stitch exact shared vertices before placing stations. Collinear tessellation
    // subdivisions do not restart a mountain chain or repeat its random sequence.
    private static IEnumerable<Point[]> Paths(IEnumerable<Segment> source)
    {
        var edges = source.Select(s =>
        {
            Point a = new(s.Ax, s.Az), b = new(s.Bx, s.Bz);
            if (!double.IsFinite(a.X) || !double.IsFinite(a.Z) || !double.IsFinite(b.X) || !double.IsFinite(b.Z) || a == b)
                throw new ArgumentException("Degenerate or nonfinite ridge contact.", nameof(source));
            return Compare(a, b) < 0 ? (A: a, B: b) : (A: b, B: a);
        }).Distinct().OrderBy(e => e.A.X).ThenBy(e => e.A.Z).ThenBy(e => e.B.X).ThenBy(e => e.B.Z).ToArray();
        var adjacent = new Dictionary<Point, List<int>>();
        for (int i = 0; i < edges.Length; i++)
            foreach (Point p in new[] { edges[i].A, edges[i].B })
            { if (!adjacent.TryGetValue(p, out List<int>? list)) adjacent.Add(p, list = new List<int>()); list.Add(i); }
        var used = new bool[edges.Length];
        Point[] Walk(Point start, int edge)
        {
            var points = new List<Point> { start }; Point current = start;
            while (!used[edge])
            {
                used[edge] = true; var e = edges[edge]; current = e.A == current ? e.B : e.A; points.Add(current);
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
}
