using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Evolution;

public readonly record struct TectonicPlate(int Id, double X, double Z, double Vx, double Vz);
public readonly record struct TectonicLedger(double Time, double ContinentalVolume, double OceanicVolume,
    double CreatedOceanicVolume, double RecycledOceanicVolume, double OceanAgeMoment,
    double MaximumContinentalThickness, int CollisionFaces, int SubductionFaces);

/// <summary>Frozen material state. Arrays never escape as mutable references.</summary>
public sealed class TectonicMaterialSnapshot
{
    public int Side { get; }
    public double Time { get; }
    public ReadOnlyCollection<double> ContinentalKm { get; }
    public ReadOnlyCollection<double> OceanicKm { get; }
    public ReadOnlyCollection<double> OceanAge { get; }
    public ReadOnlyCollection<double> InheritedOceanicKm { get; }
    public ReadOnlyCollection<double> ElevationKm { get; }
    public ReadOnlyCollection<double> AccumulatedCompression { get; }
    public ReadOnlyCollection<double> AccumulatedExtension { get; }
    public ReadOnlyCollection<double> AccumulatedShear { get; }
    public ReadOnlyCollection<int> PlateIds { get; }
    public string Checksum { get; }

    internal TectonicMaterialSnapshot(int side, double time, double[] continental, double[] oceanic,
        double[] ageMoment, double[] inherited, double[] compression, double[] extension, double[] shear, int[] plateIds)
    {
        Side = side; Time = time;
        ContinentalKm = Array.AsReadOnly((double[])continental.Clone()); OceanicKm = Array.AsReadOnly((double[])oceanic.Clone());
        InheritedOceanicKm = Array.AsReadOnly((double[])inherited.Clone());
        AccumulatedCompression = Array.AsReadOnly((double[])compression.Clone()); AccumulatedExtension = Array.AsReadOnly((double[])extension.Clone());
        AccumulatedShear = Array.AsReadOnly((double[])shear.Clone()); PlateIds = Array.AsReadOnly((int[])plateIds.Clone());
        double[] age = new double[continental.Length], heights = new double[continental.Length];
        for (int i = 0; i < age.Length; i++)
        {
            age[i] = oceanic[i] > 0 ? ageMoment[i] / oceanic[i] : 0;
            heights[i] = CrustResponse.ElevationKm(continental[i], oceanic[i], age[i]);
        }
        OceanAge = Array.AsReadOnly(age); ElevationKm = Array.AsReadOnly(heights);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, time); hash.AppendData(buffer);
        foreach (var field in new[] { ContinentalKm, OceanicKm, OceanAge, InheritedOceanicKm, ElevationKm,
            AccumulatedCompression, AccumulatedExtension, AccumulatedShear })
            foreach (double value in field) { BinaryPrimitives.WriteDoubleLittleEndian(buffer, value); hash.AppendData(buffer); }
        foreach (int value in plateIds) { BinaryPrimitives.WriteInt64LittleEndian(buffer, value); hash.AppendData(buffer); }
        Checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

/// <summary>
/// Experimental reduced kinematic thin-sheet history, NOT a force-balanced
/// mantle/Stokes solver. Moving periodic Voronoi steering domains prescribe
/// velocities. Crust material is transported conservatively, can thicken or thin,
/// and oceanic mass is created/recycled with an explicit ledger and ageing.
/// Domain boundaries are a model closure, not native player teleportation.
/// </summary>
public sealed class TectonicHistory
{
    public const string AlgorithmId = "tectonic-material-history-v1-full-reference-atlas";
    public const double ReferenceKmPerUnit = .01;
    public int Seed { get; }
    public double ReferenceWidth { get; }
    public double ReferenceLength { get; }
    public TectonicEvolutionSettings Settings { get; }
    public TectonicMaterialSnapshot Initial { get; }
    public TectonicMaterialSnapshot Final { get; }
    public ReadOnlyCollection<TectonicPlate> Plates { get; }
    public ReadOnlyCollection<TectonicLedger> Ledger { get; }
    public string Checksum { get; }

    private TectonicHistory(int seed, TectonicScalePlan scale, TectonicEvolutionSettings settings,
        TectonicMaterialSnapshot initial, TectonicMaterialSnapshot final, TectonicPlate[] plates, List<TectonicLedger> ledger)
    {
        Seed = seed; ReferenceWidth = scale.ReferenceWidth; ReferenceLength = scale.ReferenceLength; Settings = settings;
        Initial = initial; Final = final; Plates = Array.AsReadOnly(plates); Ledger = ledger.AsReadOnly();
        string canonical = JsonSerializer.Serialize(new { AlgorithmId, seed, ReferenceWidth, ReferenceLength, settings,
            initial = initial.Checksum, final = final.Checksum, plates, ledger });
        Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static TectonicHistory Generate(int seed, TectonicScalePlan scale, TectonicEvolutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(settings);
        int n = settings.Side, count = n * n;
        double width = scale.ReferenceWidth, length = scale.ReferenceLength, dx = width / n, dz = length / n;
        double area = dx * dz * ReferenceKmPerUnit * ReferenceKmPerUnit;
        TectonicPlate[] plates = CreatePlates(seed, settings.PlateCount, width, length, settings.SpeedReferenceUnitsPerTime * settings.MotionSign);
        double[] c = InitialCrust(seed, n, width, length, settings.CratonCount);
        double[] o = c.Select(value => 7 * (1 - value / 35)).ToArray();
        double[] moment = o.Select(value => value * settings.InitialOceanAge).ToArray(), inherited = (double[])o.Clone();
        var compression = new double[count]; var extension = new double[count]; var shear = new double[count];
        var velocity = Velocities(plates, n, width, length, settings.DeformationWidth, 0);
        var initial = new TectonicMaterialSnapshot(n, 0, c, o, moment, inherited, compression, extension, shear, velocity.Owner);
        var ledger = new List<TectonicLedger> { new(0, CrustTransport.Sum(c) * area, CrustTransport.Sum(o) * area,
            0, 0, CrustTransport.Sum(moment) * area, c.Max(), 0, 0) };
        double initialC = CrustTransport.Sum(c), initialO = CrustTransport.Sum(o), created = 0, recycled = 0, time = 0;
        double speed = plates.Max(p => Math.Max(Math.Abs(p.Vx), Math.Abs(p.Vz)));
        double maxDt = Math.Min(1, speed > 0 ? .35 / (speed / dx + speed / dz) : 1);
        if (settings.LowerCrustMobility > 0) maxDt = Math.Min(maxDt, .40 / (settings.LowerCrustMobility * (2 / (dx * dx) + 2 / (dz * dz))));
        if (Math.Ceiling(settings.Duration / maxDt) > 4096) throw new ArgumentOutOfRangeException(nameof(settings), "History exceeds 4096-step budget.");
        while (time < settings.Duration)
        {
            double dt = Math.Min(maxDt, settings.Duration - time);
            velocity = Velocities(plates, n, width, length, settings.DeformationWidth, time + dt / 2);
            double[] east = new double[count], south = new double[count], divergence = new double[count];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x, e = z * n + (x + 1) % n, s = ((z + 1) % n) * n + x;
                east[i] = .5 * (velocity.X[i] + velocity.X[e]); south[i] = .5 * (velocity.Z[i] + velocity.Z[s]);
            }
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x, w = z * n + (x + n - 1) % n, prev = ((z + n - 1) % n) * n + x;
                int e = z * n + (x + 1) % n, s = ((z + 1) % n) * n + x;
                divergence[i] = (east[i] - east[w]) / dx + (south[i] - south[prev]) / dz;
                compression[i] += Math.Max(-divergence[i], 0) * dt; extension[i] += Math.Max(divergence[i], 0) * dt;
                shear[i] += Math.Abs((velocity.X[s] - velocity.X[prev]) / (2 * dz) + (velocity.Z[e] - velocity.Z[w]) / (2 * dx)) * dt / 2;
            }
            // Transport every extensive material/moment with exactly the same faces.
            double[] nc = CrustTransport.Advect(c, east, south, n, dx, dz, dt);
            double[] no = CrustTransport.Advect(o, east, south, n, dx, dz, dt);
            double[] nm = CrustTransport.Advect(moment, east, south, n, dx, dz, dt);
            double[] ni = CrustTransport.Advect(inherited, east, south, n, dx, dz, dt);
            double ageExpected = CrustTransport.Sum(moment) + dt * CrustTransport.Sum(o);
            for (int i = 0; i < count; i++) nm[i] += no[i] * dt;
            int collisionFaces = 0, subductionFaces = 0;
            bool[] sinks = SubductionMask(plates, velocity.Owner, c, o, moment, n, dx, dz,
                settings.DeformationWidth, width, length, time + dt / 2, ref collisionFaces, ref subductionFaces);
            double[] born = new double[count], removed = new double[count], removedAge = new double[count];
            for (int i = 0; i < count; i++)
            {
                // Only extending columns create new basalt, after real thinning.
                if (divergence[i] > 0 && nc[i] + no[i] < 7)
                { born[i] = 7 - nc[i] - no[i]; no[i] += born[i]; } // newborn age and inherited volume remain zero
                if (sinks[i] && divergence[i] < 0)
                {
                    double allowedOcean = 7 * Math.Max(0, 1 - nc[i] / 35);
                    double loss = Math.Max(0, no[i] - allowedOcean);
                    if (loss > 0)
                    {
                        double fraction = loss / no[i]; removed[i] = loss; removedAge[i] = nm[i] * fraction;
                        nm[i] *= 1 - fraction; ni[i] *= 1 - fraction; no[i] -= loss;
                    }
                }
            }
            nc = CrustTransport.RelaxThickCrust(nc, n, dx, dz, dt, settings.LowerCrustMobility);
            double stepBorn = CrustTransport.Sum(born), stepRemoved = CrustTransport.Sum(removed);
            created += stepBorn; recycled += stepRemoved;
            CrustTransport.RequireBalance(initialC, CrustTransport.Sum(nc), "continental history inventory");
            CrustTransport.RequireBalance(initialO + created - recycled, CrustTransport.Sum(no), "oceanic history inventory");
            CrustTransport.RequireBalance(ageExpected - CrustTransport.Sum(removedAge), CrustTransport.Sum(nm), "ocean-age moment");
            if (nc.Any(v => v > 150)) throw new ArithmeticException("Crust thicker than the experimental rheology domain; do not clamp heights.");
            c = nc; o = no; moment = nm; inherited = ni; time += dt;
            ledger.Add(new TectonicLedger(time, CrustTransport.Sum(c) * area, CrustTransport.Sum(o) * area,
                created * area, recycled * area, CrustTransport.Sum(moment) * area, c.Max(), collisionFaces, subductionFaces));
        }
        velocity = Velocities(plates, n, width, length, settings.DeformationWidth, time);
        var final = new TectonicMaterialSnapshot(n, time, c, o, moment, inherited, compression, extension, shear, velocity.Owner);
        return new TectonicHistory(seed, scale, settings, initial, final, plates, ledger);
    }

    /// <summary>Declared bilinear sampling of the frozen material-height raster, not invented fine detail.</summary>
    public double SampleElevationKm(TectonicScalePlan scale, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(scale);
        if (Math.Abs(scale.ReferenceWidth - ReferenceWidth) > 1e-8 || Math.Abs(scale.ReferenceLength - ReferenceLength) > 1e-8)
            throw new ArgumentException("Aspect changed: generate the corresponding full atlas, do not crop or stretch.", nameof(scale));
        var p = scale.ToReference(x, z); int n = Settings.Side;
        double gx = p.X / ReferenceWidth * n - .5, gz = p.Z / ReferenceLength * n - .5;
        int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz); double tx = gx - ix, tz = gz - iz;
        int x0 = Mod(ix, n), x1 = Mod(ix + 1, n), z0 = Mod(iz, n), z1 = Mod(iz + 1, n);
        return (1 - tz) * ((1 - tx) * Final.ElevationKm[z0 * n + x0] + tx * Final.ElevationKm[z0 * n + x1])
            + tz * ((1 - tx) * Final.ElevationKm[z1 * n + x0] + tx * Final.ElevationKm[z1 * n + x1]);
    }

    private sealed record VelocityField(double[] X, double[] Z, int[] Owner);
    private static VelocityField Velocities(TectonicPlate[] plates, int n, double width, double length, double blendWidth, double time)
    {
        int count = n * n; var vx = new double[count]; var vz = new double[count]; var owner = new int[count];
        double[] distances = new double[plates.Length];
        var positions = plates.Select(p => (X: Wrap(p.X + p.Vx * time, width), Z: Wrap(p.Z + p.Vz * time, length))).ToArray();
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            double px = (x + .5) * width / n, pz = (z + .5) * length / n, nearest = double.PositiveInfinity;
            int i = z * n + x;
            for (int k = 0; k < plates.Length; k++)
            {
                double dx = Delta(px - positions[k].X, width), dz = Delta(pz - positions[k].Z, length);
                double d = Math.Sqrt(dx * dx + dz * dz); distances[k] = d;
                if (d < nearest) { nearest = d; owner[i] = k; }
            }
            double weight = 0;
            for (int k = 0; k < plates.Length; k++)
            {
                double w = Math.Exp(-(distances[k] - nearest) / blendWidth);
                vx[i] += w * plates[k].Vx; vz[i] += w * plates[k].Vz; weight += w;
            }
            vx[i] /= weight; vz[i] /= weight;
        }
        return new VelocityField(vx, vz, owner);
    }

    private static bool[] SubductionMask(TectonicPlate[] plates, int[] owners, double[] c, double[] o, double[] moment,
        int n, double dx, double dz, double width, double domainWidth, double domainLength, double time, ref int collisions, ref int subductions)
    {
        var mask = new bool[n * n];
        int radiusX = (int)Math.Ceiling(width / dx), radiusZ = (int)Math.Ceiling(width / dz);
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            for (int axis = 0; axis < 2; axis++)
            {
                int j = axis == 0 ? z * n + (x + 1) % n : ((z + 1) % n) * n + x;
                int a = owners[i], b = owners[j]; if (a == b) continue;
                double faceX = (x + (axis == 0 ? 1d : .5)) * dx;
                double faceZ = (z + (axis == 1 ? 1d : .5)) * dz;
                double closure = CrustResponse.ClosingSpeed(plates[a], plates[b], faceX, faceZ, domainWidth, domainLength, time);
                if (closure <= 0) continue;
                var choice = CrustResponse.Choose(a, c[i], o[i], o[i] > 0 ? moment[i] / o[i] : 0,
                    b, c[j], o[j], o[j] > 0 ? moment[j] / o[j] : 0);
                if (choice.Kind == TectonicContactKind.ContinentalCollision) { collisions++; continue; }
                subductions++;
                for (int oz = -radiusZ; oz <= radiusZ; oz++)
                for (int ox = -radiusX; ox <= radiusX; ox++)
                {
                    if (ox * ox * dx * dx + oz * oz * dz * dz > width * width) continue;
                    int target = Mod(z + oz, n) * n + Mod(x + ox, n);
                    if (owners[target] == choice.SubductingPlate) mask[target] = true;
                }
            }
        }
        return mask;
    }

    private static TectonicPlate[] CreatePlates(int seed, int count, double width, double length, double speed)
    {
        var points = new List<(double X, double Z)>();
        for (int i = 0; i < count; i++)
        {
            (double X, double Z) best = default; double score = -1;
            for (int j = 0; j < 32; j++)
            {
                double x = Unit(seed, 100, (ulong)(i * 64 + j * 2)) * width, z = Unit(seed, 100, (ulong)(i * 64 + j * 2 + 1)) * length;
                double nearest = points.Count == 0 ? 1 : points.Min(p => Math.Pow(Delta(x - p.X, width), 2) + Math.Pow(Delta(z - p.Z, length), 2));
                if (nearest > score) { score = nearest; best = (x, z); }
            }
            points.Add(best);
        }
        return points.Select((p, i) =>
        {
            double angle = Math.Tau * Unit(seed, 101, (ulong)i * 2), magnitude = speed * (.4 + .6 * Unit(seed, 101, (ulong)i * 2 + 1));
            return new TectonicPlate(i, p.X, p.Z, magnitude * Math.Cos(angle), magnitude * Math.Sin(angle));
        }).ToArray();
    }

    // Initial cratons are connected groups of small Voronoi terranes, not smooth
    // radial altitude envelopes. Their initial state is declared, then transported.
    private static double[] InitialCrust(int seed, int n, double width, double length, int cratons)
    {
        const int grid = 16, sites = grid * grid;
        var centers = Enumerable.Range(0, sites).Select(i => (X: (i % grid + .15 + .7 * Unit(seed, 200, (ulong)i * 2)) * width / grid,
            Z: (i / grid + .15 + .7 * Unit(seed, 200, (ulong)i * 2 + 1)) * length / grid)).ToArray();
        var raster = new int[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            double px = (x + .5) * width / n, pz = (z + .5) * length / n, best = double.PositiveInfinity;
            for (int k = 0; k < sites; k++)
            {
                double a = Delta(px - centers[k].X, width), b = Delta(pz - centers[k].Z, length), d = a * a + b * b;
                if (d < best) { best = d; raster[z * n + x] = k; }
            }
        }
        var neighbors = Enumerable.Range(0, sites).Select(_ => new SortedSet<int>()).ToArray();
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int a = raster[z * n + x];
            foreach (int b in new[] { raster[z * n + (x + 1) % n], raster[((z + 1) % n) * n + x] })
                if (a != b) { neighbors[a].Add(b); neighbors[b].Add(a); }
        }
        var roots = new List<int> { (int)(Unit(seed, 201, 0) * sites) };
        while (roots.Count < cratons)
        {
            int best = Enumerable.Range(0, sites).Where(i => !roots.Contains(i)).OrderByDescending(i => roots.Min(j =>
                Math.Pow(Delta(centers[i].X - centers[j].X, width), 2) + Math.Pow(Delta(centers[i].Z - centers[j].Z, length), 2))).ThenBy(i => i).First();
            roots.Add(best);
        }
        int[] labels = Enumerable.Repeat(-1, sites).ToArray(), sizes = new int[cratons];
        var queue = new PriorityQueue<(int Site, int Owner, double Cost), (double Cost, int Site, int Owner)>();
        for (int i = 0; i < cratons; i++) { labels[roots[i]] = i; sizes[i] = 1; }
        for (int i = 0; i < cratons; i++) Enqueue(roots[i], i, 0);
        int quota = (int)(sites * .36 / cratons);
        while (queue.TryDequeue(out var item, out _))
        {
            if (labels[item.Site] >= 0 || sizes[item.Owner] >= quota || neighbors[item.Site].Any(v => labels[v] >= 0 && labels[v] != item.Owner)) continue;
            labels[item.Site] = item.Owner; sizes[item.Owner]++; Enqueue(item.Site, item.Owner, item.Cost);
        }
        if (sizes.Any(size => size < 5)) throw new InvalidOperationException("Initial craton quota failed; no single-continent fallback.");
        double[] c = raster.Select(i => labels[i] >= 0 ? 35d : 0d).ToArray();
        int passes = Math.Clamp((int)Math.Ceiling(Math.Pow(7000 / Math.Min(width / n, length / n), 2)), 1, 24);
        for (int pass = 0; pass < passes; pass++)
        {
            double[] next = new double[c.Length];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++) next[z * n + x] = .5 * c[z * n + x] + .125 * (c[z * n + (x + 1) % n] + c[z * n + (x + n - 1) % n]
                + c[((z + 1) % n) * n + x] + c[((z + n - 1) % n) * n + x]);
            c = next;
        }
        return c;
        void Enqueue(int site, int owner, double cost)
        {
            foreach (int other in neighbors[site]) if (labels[other] < 0)
            {
                ulong key = (ulong)(Math.Min(site, other) * sites + Math.Max(site, other));
                double distance = Math.Sqrt(Math.Pow(Delta(centers[site].X - centers[other].X, width), 2) + Math.Pow(Delta(centers[site].Z - centers[other].Z, length), 2));
                double next = cost + distance * (.3 + 1.7 * Unit(seed, 202, key));
                queue.Enqueue((other, owner, next), (next, other, owner));
            }
        }
    }

    private static double Unit(int seed, ulong key, ulong counter) =>
        (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, new StableId(0, key), counter) >> 11) / 9007199254740992d;
    private static double Wrap(double value, double period) => value - Math.Floor(value / period) * period;
    private static double Delta(double value, double period) => value - Math.Floor(value / period + .5) * period;
    private static int Mod(int value, int n) => (value % n + n) % n;
}
