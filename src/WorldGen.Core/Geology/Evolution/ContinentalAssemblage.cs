using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Evolution;

public sealed record ContinentalProvince(int Id, int NucleusSite, int NucleusTerranes,
    double TargetAreaFraction, double ActualAreaFraction, double FabricAngleRadians,
    double AccretionAnisotropy, double InteriorThicknessKm);

/// <summary>
/// Explicit heterogeneous initial crust, not a simulation of the origin of Earth's
/// plates. Unequal composite provinces accrete adjacent Voronoi terranes along an
/// inherited fabric. Province IDs are NOT mechanical plate IDs. The plate history
/// later transports these materials without using the coast to assign a plate.
/// The prior is intentionally inspectable; there is no claim of Earth calibration.
/// </summary>
public sealed class ContinentalAssemblage
{
    public const string AlgorithmId = "continental-assemblages-area-budget-fabric-v1";
    public int Seed { get; }
    public int Side { get; }
    public double ReferenceWidth { get; }
    public double ReferenceLength { get; }
    public double MeanContinentalKm { get; }
    public double MarginWidthReferenceUnits { get; }
    public double ThicknessBudgetMultiplier { get; }
    public ReadOnlyCollection<double> ContinentalKm { get; }
    public ReadOnlyCollection<double> OceanicKm { get; }
    public ReadOnlyCollection<int> ProvinceIds { get; }
    public ReadOnlyCollection<int> TerraneIds { get; }
    public ReadOnlyCollection<ContinentalProvince> Provinces { get; }
    public string Checksum { get; }

    private ContinentalAssemblage(int seed, int side, TectonicScalePlan scale, double budget,
        double margin, double multiplier, double[] c, double[] o, int[] labels, int[] terranes,
        ContinentalProvince[] provinces)
    {
        Seed = seed; Side = side; ReferenceWidth = scale.ReferenceWidth; ReferenceLength = scale.ReferenceLength;
        MeanContinentalKm = budget; MarginWidthReferenceUnits = margin; ThicknessBudgetMultiplier = multiplier;
        ContinentalKm = Array.AsReadOnly(c); OceanicKm = Array.AsReadOnly(o);
        ProvinceIds = Array.AsReadOnly(labels); TerraneIds = Array.AsReadOnly(terranes);
        Provinces = Array.AsReadOnly(provinces);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { AlgorithmId, seed, side,
            ReferenceWidth, ReferenceLength, budget, margin, multiplier, provinces })));
        Span<byte> bytes = stackalloc byte[8];
        foreach (var field in new[] { c, o }) foreach (double value in field)
        { BinaryPrimitives.WriteDoubleLittleEndian(bytes, value); hash.AppendData(bytes); }
        foreach (var field in new[] { labels, terranes }) foreach (int value in field)
        { BinaryPrimitives.WriteInt64LittleEndian(bytes, value); hash.AppendData(bytes); }
        Checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <param name="meanContinentalKm">Domain-wide volume per unit area. For an A/B comparison pass the old state's mean, not a newly chosen sea level.</param>
    /// <param name="provinceCount">Zero selects 3..6 provinces from the seed, independently of plate count.</param>
    public static ContinentalAssemblage Generate(int seed, TectonicScalePlan scale, int side,
        double meanContinentalKm = 12.6, int provinceCount = 0)
    {
        ArgumentNullException.ThrowIfNull(scale);
        if (side is < 32 or > 512 || (side & (side - 1)) != 0 ||
            !double.IsFinite(meanContinentalKm) || meanContinentalKm is < 6 or > 18 ||
            (provinceCount != 0 && provinceCount is < 2 or > 6))
            throw new ArgumentException("Unsupported continental initial-state budget.");
        int groups = provinceCount == 0 ? 3 + (int)(Unit(seed, 410, 0) * 4) : provinceCount;
        // The terrane graph is independent of output sampling density and of the
        // mechanical plates. Its near-isotropic spacing respects rectangular worlds.
        int nx = (int)Math.Round(32 * Math.Sqrt(scale.ReferenceWidth / scale.ReferenceLength));
        int nz = (int)Math.Round(32 * Math.Sqrt(scale.ReferenceLength / scale.ReferenceWidth));
        int sites = nx * nz; const int control = 256;
        double width = scale.ReferenceWidth, length = scale.ReferenceLength;
        var points = new (double X, double Z)[sites];
        for (int k = 0; k < sites; k++) points[k] = (
            (k % nx + .08 + .84 * Unit(seed, 411, (ulong)k * 2)) * width / nx,
            (k / nx + .08 + .84 * Unit(seed, 411, (ulong)k * 2 + 1)) * length / nz);
        var graph = Enumerable.Range(0, sites).Select(_ => new SortedSet<int>()).ToArray();
        var area = new double[sites]; var controlSites = new int[control * control];
        for (int z = 0; z < control; z++) for (int x = 0; x < control; x++)
        {
            int k = Nearest((x + .5) * width / control, (z + .5) * length / control);
            controlSites[z * control + x] = k; area[k] += 1d / (control * control);
        }
        for (int z = 0; z < control; z++) for (int x = 0; x < control; x++)
        {
            int a = controlSites[z * control + x];
            Connect(a, controlSites[z * control + (x + 1) % control]);
            Connect(a, controlSites[((z + 1) % control) * control + x]);
        }
        // A stochastic INITIAL material allocation, not a physical law of plate
        // sizes: positive minimum nuclei plus a broad unequal accretion budget.
        double[] weights = Enumerable.Range(0, groups).Select(p => Math.Pow(.15 + Unit(seed, 412, (ulong)p), 3)).ToArray();
        double total = weights.Sum();
        double[] targets = weights.Select(v => .36 * (.06 + (1 - .06 * groups) * v / total)).ToArray();
        double[] angles = Enumerable.Range(0, groups).Select(p => Math.Tau * Unit(seed, 413, (ulong)p)).ToArray();
        double[] anisotropy = Enumerable.Range(0, groups).Select(p => 1 + 2.5 * Unit(seed, 414, (ulong)p)).ToArray();
        double[] thickness = Enumerable.Range(0, groups).Select(p => 33 + 6 * Unit(seed, 415, (ulong)p)).ToArray();
        int[] roots = new int[groups], labels = Enumerable.Repeat(-1, sites).ToArray(), nucleusSizes = new int[groups];
        double[] grown = new double[groups];
        for (int p = 0; p < groups; p++)
        {
            int root = -1;
            for (int attempt = 0; attempt < 512; attempt++)
            {
                int k = (int)(Unit(seed, 416, (ulong)(p * 512 + attempt)) * sites);
                if (area[k] == 0 || labels[k] >= 0) continue;
                bool separated = true;
                for (int q = 0; q < p; q++)
                {
                    double dx = Delta(points[k].X - points[roots[q]].X, width);
                    double dz = Delta(points[k].Z - points[roots[q]].Z, length);
                    if (dx * dx + dz * dz < Math.Pow(.12 * Math.Min(width, length), 2)) { separated = false; break; }
                }
                if (separated) { root = k; break; }
            }
            if (root < 0) throw new InvalidOperationException("Initial nuclei could not be placed; no equal-spaced fallback.");
            roots[p] = root; labels[root] = p; grown[p] += area[root]; nucleusSizes[p] = 1;
        }
        // Composite nuclei grow from connected material terranes; larger quotas
        // may assemble more nuclei, but do not change the number of rigid plates.
        for (int p = 0; p < groups; p++)
        {
            int desired = 1 + (int)(Unit(seed, 417, (ulong)p) * 4);
            var front = new SortedSet<int>(graph[roots[p]]);
            while (nucleusSizes[p] < desired && front.Count > 0)
            {
                int k = front.OrderBy(v => Unit(seed, 418, (ulong)(p * sites + v))).ThenBy(v => v).First(); front.Remove(k);
                if (labels[k] >= 0 || grown[p] + area[k] > targets[p]) continue;
                labels[k] = p; grown[p] += area[k]; nucleusSizes[p]++;
                foreach (int neighbor in graph[k]) if (labels[neighbor] < 0) front.Add(neighbor);
            }
        }
        var queue = new PriorityQueue<(int Site, int Province, double Cost), (double Cost, int Site, int Province)>();
        for (int k = 0; k < sites; k++) if (labels[k] >= 0) Enqueue(k, labels[k], 0);
        while (queue.TryDequeue(out var next, out _))
        {
            int p = next.Province, k = next.Site;
            if (labels[k] >= 0 || grown[p] >= targets[p]) continue;
            labels[k] = p; grown[p] += area[k]; Enqueue(k, p, next.Cost);
        }
        if (Enumerable.Range(0, groups).Any(p => grown[p] < .80 * targets[p]))
            throw new InvalidOperationException("A province's accretion budget was inaccessible; keep the failed seed visible.");
        // Material fields, not coloured silhouettes. Diffuse initial unresolved
        // margins with positive metric weights BEFORE the tectonic history; no
        // height field is smoothed and no erosion is applied here.
        int count = side * side; var c = new double[count]; var o = new double[count];
        var rasterLabels = new int[count]; var rasterTerranes = new int[count];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x, k = Nearest((x + .5) * width / side, (z + .5) * length / side), p = labels[k];
            rasterLabels[i] = p; rasterTerranes[i] = k;
            c[i] = p < 0 ? 0 : thickness[p] + 2 * (Unit(seed, 419, (ulong)k) - .5);
            o[i] = p < 0 ? 7 : 0;
        }
        double margin = Math.Min(6000, Math.Min(width, length) / 40);
        c = Margins(c); o = Margins(o);
        double multiplier = meanContinentalKm * count / CrustTransport.Sum(c);
        if (!double.IsFinite(multiplier) || multiplier is < .5 or > 1.6)
            throw new ArithmeticException("Initial thickness normalization outside declared prior; never clamp.");
        for (int i = 0; i < count; i++) c[i] *= multiplier;
        CrustTransport.RequireBalance(meanContinentalKm * count, CrustTransport.Sum(c), "initial continental volume budget");
        if (c.Any(v => !double.IsFinite(v) || v < 0 || v > 65) || o.Any(v => !double.IsFinite(v) || v < 0) ||
            c.Where((v, i) => v + o[i] <= 0).Any()) throw new ArithmeticException("Invalid initial material columns.");
        var reports = Enumerable.Range(0, groups).Select(p => new ContinentalProvince(p, roots[p], nucleusSizes[p],
            targets[p], grown[p], angles[p], anisotropy[p], thickness[p] * multiplier)).ToArray();
        return new(seed, side, scale, meanContinentalKm, margin, multiplier, c, o, rasterLabels, rasterTerranes, reports);

        int Nearest(double x, double z)
        {
            int bx = (int)(x / width * nx), bz = (int)(z / length * nz), best = -1; double distance = double.PositiveInfinity;
            // Each jittered site remains inside its bin. With near-isotropic
            // bins, a 5x5 neighborhood contains the nearest site (checked in tests).
            for (int oz = -2; oz <= 2; oz++) for (int ox = -2; ox <= 2; ox++)
            {
                int k = Mod(bz + oz, nz) * nx + Mod(bx + ox, nx);
                double dx = Delta(x - points[k].X, width), dz = Delta(z - points[k].Z, length), d = dx * dx + dz * dz;
                if (d < distance || (d == distance && k < best)) { distance = d; best = k; }
            }
            return best;
        }
        void Connect(int a, int b) { if (a != b) { graph[a].Add(b); graph[b].Add(a); } }
        void Enqueue(int k, int p, double cost)
        {
            double ca = Math.Cos(angles[p]), sa = Math.Sin(angles[p]);
            foreach (int other in graph[k]) if (labels[other] < 0)
            {
                double dx = Delta(points[other].X - points[k].X, width), dz = Delta(points[other].Z - points[k].Z, length);
                double along = dx * ca + dz * sa, across = -dx * sa + dz * ca;
                double resistance = .8 + .4 * Unit(seed, 420, (ulong)(Math.Min(k, other) * sites + Math.Max(k, other)));
                double increment = Math.Sqrt(along * along / anisotropy[p] + across * across * anisotropy[p]) * resistance;
                double cst = cost + increment;
                queue.Enqueue((other, p, cst), (cst / Math.Sqrt(targets[p]), other, p));
            }
        }
        double[] Margins(double[] values)
        {
            double dx = width / side, dz = length / side, h = Math.Min(dx, dz);
            double ax = .125 * h * h / (dx * dx), az = .125 * h * h / (dz * dz), center = 1 - 2 * ax - 2 * az;
            int passes = (int)Math.Ceiling(4 * margin * margin / (h * h));
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new double[count];
                for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    next[i] = center * values[i] + ax * (values[z * side + (x + 1) % side] + values[z * side + (x + side - 1) % side])
                        + az * (values[((z + 1) % side) * side + x] + values[((z + side - 1) % side) * side + x]);
                }
                values = next;
            }
            return values;
        }
    }

    internal void RequireCompatible(int seed, TectonicScalePlan scale, int side)
    {
        if (Seed != seed || Side != side || Math.Abs(ReferenceWidth - scale.ReferenceWidth) > 1e-8 ||
            Math.Abs(ReferenceLength - scale.ReferenceLength) > 1e-8)
            throw new ArgumentException("Initial materials belong to another seed, resolution or entire atlas aspect.");
    }
    private static double Unit(int seed, ulong domain, ulong counter) =>
        (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, new StableId(0, domain), counter) >> 11) / 9007199254740992d;
    private static double Delta(double v, double p) => v - Math.Floor(v / p + .5) * p;
    private static int Mod(int v, int n) => (v % n + n) % n;
}
