using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>A controlled rupture embedded in an unwrapped orthonormal map frame.
/// The normal coordinate belongs to RiftNecking; the tangent is (-NormalZ,NormalX).
/// This placement does not infer a fracture from plastic strain or coastlines.</summary>
public sealed record RiftPlacement(string Identity, RiftNecking History,
    double OriginX, double OriginZ, double NormalX, double NormalZ);

public sealed record RiftSourceVertex(double X, double Z, double AgeMyr);

public sealed record RiftRasterPatch(string Id, string HistoryChecksum, string Material,
    int OriginOrEventId, int Flank, double ThicknessKm, double AreaReference2,
    double MeanAgeMyr, ReadOnlyCollection<RiftSourceVertex> SourceVertices);

/// <summary>A resolved triangular footprint. Geometry and chronology must come
/// from an upstream material history, not a mask or inferred fracture threshold.</summary>
public sealed record ResolvedMaterialTriangle(string Id, string HistoryChecksum, string Material,
    int OriginOrEventId, int Flank, double ThicknessKm,
    RiftSourceVertex A, RiftSourceVertex B, RiftSourceVertex C);

/// <summary>One source contribution in one cell. Multiple disjoint sources remain
/// separate, including opposite flanks or different ages in a cut cell.</summary>
public sealed record RiftCellPacket(int Cell, int Patch, double AreaFraction,
    double MeanAgeMyr);

/// <summary>
/// Finite-area projection of the continental fragments AND the dated oceanic
/// polygons produced by actual RiftNecking histories. Clips polygons, not their
/// centres, and integrates affine age over each intersection. Partial cells are
/// not full-thickness ocean cells. Uncovered space is UNKNOWN, not empty ocean.
/// Explicit periodic wrapping preserves complete polygons and their inventories.
/// Overlapping material footprints are refused; collision/subduction must first
/// resolve them. This is a conservative geometry bridge, not a fracture solver,
/// mantle-melting model, new world initializer or heightmap generator.
/// </summary>
public sealed class RiftMaterialRaster
{
    public const string AlgorithmId = "rift-fragments-and-dated-area-packets-v1";
    public int Side { get; }
    public double WidthReference { get; }
    public double LengthReference { get; }
    public double ReferenceKmPerUnit { get; }
    public bool Periodic { get; }
    public double ObservationTimeMyr { get; }
    public ReadOnlyCollection<RiftRasterPatch> Patches { get; }
    public ReadOnlyCollection<RiftCellPacket> Packets { get; }
    public ReadOnlyCollection<double> ContinentalKm { get; }
    public ReadOnlyCollection<double> OceanicKm { get; }
    public ReadOnlyCollection<double> OceanAgeMomentKmMyr { get; }
    public ReadOnlyCollection<double> ContinentalFraction { get; }
    public ReadOnlyCollection<double> OceanFraction { get; }
    public string Checksum { get; }
    public double CellAreaKm2 => WidthReference / Side * (LengthReference / Side)
        * ReferenceKmPerUnit * ReferenceKmPerUnit;

    private readonly record struct Vertex(double X, double Z, double Age);
    private sealed record Piece(int Patch, Vertex[] Polygon);
    private const double RoundoffArea = 2e-12; // In CELL area units; not a pixel-sized buffer.
    private const int MaximumPackets = 2_000_000;

    private RiftMaterialRaster(int side, double width, double length, double kmPerUnit,
        bool periodic, double time, List<RiftRasterPatch> patches, List<RiftCellPacket> packets,
        double[] c, double[] o, double[] q, double[] cf, double[] of)
    {
        Side = side; WidthReference = width; LengthReference = length;
        ReferenceKmPerUnit = kmPerUnit; Periodic = periodic; ObservationTimeMyr = time;
        Patches = patches.AsReadOnly(); Packets = packets.AsReadOnly();
        ContinentalKm = Array.AsReadOnly(c); OceanicKm = Array.AsReadOnly(o);
        OceanAgeMomentKmMyr = Array.AsReadOnly(q);
        ContinentalFraction = Array.AsReadOnly(cf); OceanFraction = Array.AsReadOnly(of);
        Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            AlgorithmId, side, width, length, kmPerUnit, periodic, time, patches, packets
        })))).ToLowerInvariant();
    }

    public static RiftMaterialRaster Project(IReadOnlyList<RiftPlacement> placements,
        TectonicScalePlan scale, int side, bool periodic = true)
    {
        ArgumentNullException.ThrowIfNull(placements); ArgumentNullException.ThrowIfNull(scale);
        if (side is < 8 or > 512 || (side & (side - 1)) != 0 || placements.Count is < 1 or > 32)
            throw new ArgumentException("Unsupported raster or placement budget.");
        double width = scale.ReferenceWidth, length = scale.ReferenceLength;
        double dx = width / side, dz = length / side;
        var ordered = placements.OrderBy(p => p?.Identity, StringComparer.Ordinal).ToArray();
        if (ordered.Any(p => p is null || p.History is null || string.IsNullOrWhiteSpace(p.Identity) || p.Identity.Length > 256
            || new[] { p.OriginX, p.OriginZ, p.NormalX, p.NormalZ }.Any(v => !double.IsFinite(v))
            || Math.Abs(p.NormalX * p.NormalX + p.NormalZ * p.NormalZ - 1) > 1e-12
            || Math.Abs(p.OriginX / width) > 1024 || Math.Abs(p.OriginZ / length) > 1024)
            || ordered.Select(p => p.Identity).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Invalid or duplicate unwrapped material placement.");
        double time = ordered[0].History.ObservationTimeMyr;
        double km = ordered[0].History.ReferenceKmPerUnit;
        if (ordered.Any(p => p.History.ObservationTimeMyr != time || p.History.ReferenceKmPerUnit != km))
            throw new ArgumentException("All material histories need the same observation time and units.");
        var sources = new List<ProjectionSource>();
        foreach (var placement in ordered)
        {
            var rift = placement.History;
            foreach (var parcel in rift.Sample(time).Parcels)
            {
                double l = parcel.LeftReference, r = parcel.RightReference;
                Add(placement, "continental", parcel.OriginId, parcel.Flank, parcel.CrustKm, 0,
                    (r - l) * rift.AlongRiftLengthReference,
                    [Point(l, 0), Point(r, 0), Point(r, rift.AlongRiftLengthReference), Point(l, rift.AlongRiftLengthReference)]);
            }
            foreach (var timeline in RiftSpreadingAdapter.ToTimelines(rift, placement.Identity,
                placement.OriginX, placement.OriginZ, placement.NormalX, placement.NormalZ))
            {
                int flank = timeline.Name.EndsWith("/left", StringComparison.Ordinal) ? -1 : 1;
                foreach (var episode in timeline.Episodes)
                {
                    double vx = episode.MaterialVelocityX - episode.RidgeVelocityX;
                    double vz = episode.MaterialVelocityZ - episode.RidgeVelocityZ;
                    double oldest = -episode.FirstBirthTimeMyr, youngest = -episode.LastBirthTimeMyr;
                    double area = Math.Abs((episode.Bx - episode.Ax) * vz - (episode.Bz - episode.Az) * vx)
                        * (oldest - youngest);
                    Add(placement, "new-ocean", episode.SourceEventId, flank, rift.NewOceanicThicknessKm,
                        .5 * (oldest + youngest), area,
                        [E(episode.Ax, episode.Az, oldest), E(episode.Bx, episode.Bz, oldest),
                         E(episode.Bx, episode.Bz, youngest), E(episode.Ax, episode.Az, youngest)]);
                    Vertex E(double x, double z, double age) => new(x + vx * age, z + vz * age, age);
                }
            }
            Vertex Point(double normal, double along) => new(
                placement.OriginX + placement.NormalX * normal - placement.NormalZ * along,
                placement.OriginZ + placement.NormalZ * normal + placement.NormalX * along, 0);
        }
        return Rasterize(sources, scale, side, periodic, time, km);

        void Add(RiftPlacement placement, string material, int source, int flank, double thickness,
            double meanAge, double expectedArea, Vertex[] polygon)
        {
            string id = FormattableString.Invariant($"{placement.Identity}/{material}/{source}/{flank}");
            sources.Add(new(id, placement.History.Checksum, material, source, flank,
                thickness, meanAge, expectedArea, polygon));
        }
    }

    /// <summary>Conservative bridge for resolved 2D fragments and affine-age
    /// triangles. This does not choose where a crack forms or fill unknown space.</summary>
    public static RiftMaterialRaster ProjectTriangles(IReadOnlyList<ResolvedMaterialTriangle> triangles,
        TectonicScalePlan scale, int side, double observationTimeMyr, double referenceKmPerUnit,
        bool periodic = true)
    {
        ArgumentNullException.ThrowIfNull(triangles); ArgumentNullException.ThrowIfNull(scale);
        if (side is < 8 or > 512 || (side & (side - 1)) != 0 || triangles.Count is < 1 or > 8192
            || !double.IsFinite(observationTimeMyr) || observationTimeMyr < 0
            || !double.IsFinite(referenceKmPerUnit) || referenceKmPerUnit <= 0)
            throw new ArgumentException("Invalid resolved triangle geometry, metric or time.");
        var ordered = triangles.OrderBy(t => t?.Id, StringComparer.Ordinal).ToArray();
        var sources = new List<ProjectionSource>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in ordered)
        {
            if (t is null || string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 512 || !ids.Add(t.Id)
                || string.IsNullOrWhiteSpace(t.HistoryChecksum) || t.HistoryChecksum.Length > 512
                || t.Material is not ("continental" or "new-ocean") || t.OriginOrEventId < 0
                || t.Flank < -1 || t.Flank > 1 || !double.IsFinite(t.ThicknessKm) || t.ThicknessKm <= 0
                || t.A is null || t.B is null || t.C is null)
                throw new ArgumentException("Invalid resolved material triangle identity or content.");
            var v = new[] { t.A, t.B, t.C }.Select(x => new Vertex(x.X, x.Z, x.AgeMyr)).ToArray();
            if (v.Any(x => !double.IsFinite(x.X) || !double.IsFinite(x.Z) || !double.IsFinite(x.Age)
                || x.Age < 0 || x.Age > observationTimeMyr || (t.Material == "continental" && x.Age != 0)
                || Math.Abs(x.X / scale.ReferenceWidth) > 1024 || Math.Abs(x.Z / scale.ReferenceLength) > 1024))
                throw new ArgumentException("Invalid resolved coordinates or dated age.");
            double area = Math.Abs(Cross(v[0], v[1], v[2])) / 2;
            if (!double.IsFinite(area) || area <= 0) throw new ArgumentException("Degenerate resolved triangle.");
            sources.Add(new(t.Id, t.HistoryChecksum, t.Material, t.OriginOrEventId, t.Flank,
                t.ThicknessKm, (v[0].Age + v[1].Age + v[2].Age) / 3, area, v));
        }
        return Rasterize(sources, scale, side, periodic, observationTimeMyr, referenceKmPerUnit);
    }

    private sealed record ProjectionSource(string Id, string HistoryChecksum, string Material,
        int Origin, int Flank, double Thickness, double MeanAge, double Area, Vertex[] Vertices);

    private static RiftMaterialRaster Rasterize(IReadOnlyList<ProjectionSource> sources, TectonicScalePlan scale,
        int side, bool periodic, double time, double km)
    {
        double width = scale.ReferenceWidth, length = scale.ReferenceLength;
        double dx = width / side, dz = length / side;
        var patches = new List<RiftRasterPatch>();
        var pieces = new Dictionary<int, List<Piece>>();
        var packets = new List<RiftCellPacket>();
        double[] c = new double[side * side], o = new double[c.Length], q = new double[c.Length];
        double[] cf = new double[c.Length], of = new double[c.Length];
        long candidateCells = 0;
        foreach (var source in sources) Add(source);
        // Stable packet order is independent of caller order. Sources are never
        // merged by cell or averaged into an invented single birth event.
        packets = packets.OrderBy(p => p.Cell).ThenBy(p => p.Patch).ToList();
        foreach (var packet in packets)
        {
            var p = patches[packet.Patch]; int i = packet.Cell;
            double value = packet.AreaFraction * p.ThicknessKm;
            if (p.Material == "continental") { c[i] += value; cf[i] += packet.AreaFraction; }
            else { o[i] += value; of[i] += packet.AreaFraction; q[i] += value * packet.MeanAgeMyr; }
        }
        for (int i = 0; i < c.Length; i++)
            if (!double.IsFinite(c[i] + o[i] + q[i]) || cf[i] + of[i] > 1 + RoundoffArea)
                throw new ArithmeticException("Unresolved material coverage; no clipping or renormalization.");
        return new(side, width, length, km, periodic, time, patches, packets, c, o, q, cf, of);

        void Add(ProjectionSource source)
        {
            string id = source.Id, material = source.Material;
            int origin = source.Origin, flank = source.Flank;
            double thickness = source.Thickness, meanAge = source.MeanAge, expectedArea = source.Area;
            Vertex[] polygon = source.Vertices;
            if (!(expectedArea > 0) || !double.IsFinite(expectedArea) || polygon.Any(p =>
                !double.IsFinite(p.X) || !double.IsFinite(p.Z) || !double.IsFinite(p.Age) || p.Age < 0))
                throw new ArithmeticException("Invalid material polygon.");
            int patchId = patches.Count;
            if (patchId >= 8192) throw new ArgumentException("Exceeded patch budget.");
            patches.Add(new(id, source.HistoryChecksum, material, origin, flank, thickness, expectedArea, meanAge,
                Array.AsReadOnly(polygon.Select(v => new RiftSourceVertex(v.X, v.Z, v.Age)).ToArray())));
            double minX = polygon.Min(v => v.X), maxX = polygon.Max(v => v.X);
            double minZ = polygon.Min(v => v.Z), maxZ = polygon.Max(v => v.Z);
            if (maxX - minX > width || maxZ - minZ > length)
                throw new ArgumentException("Footprint spans more than a periodic axis; explicitly resolve topology first.");
            if (!periodic && (minX < 0 || minZ < 0 || maxX > width || maxZ > length))
                throw new ArgumentException("Nonperiodic raster would crop a material inventory.");
            double shiftX = periodic ? -Math.Floor(minX / width) * width : 0;
            double shiftZ = periodic ? -Math.Floor(minZ / length) * length : 0;
            double sumArea = 0, sumAgeArea = 0;
            for (int iz = 0; iz < (periodic ? 2 : 1); iz++)
            for (int ix = 0; ix < (periodic ? 2 : 1); ix++)
            {
                double sx = shiftX - ix * width, sz = shiftZ - iz * length;
                int x0 = Math.Max(0, (int)Math.Floor((minX + sx) / dx));
                int z0 = Math.Max(0, (int)Math.Floor((minZ + sz) / dz));
                int x1 = Math.Min(side - 1, (int)Math.Floor((maxX + sx) / dx));
                int z1 = Math.Min(side - 1, (int)Math.Floor((maxZ + sz) / dz));
                if (x0 > x1 || z0 > z1) continue;
                candidateCells += (long)(x1 - x0 + 1) * (z1 - z0 + 1);
                if (candidateCells > 8_000_000) throw new ArgumentException("Exceeded bounded polygon-cell work.");
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    // Cell-local coordinates avoid subtracting large global
                    // products in the shoelace formula for coastal slivers.
                    var local = polygon.Select(v => new Vertex((v.X + sx - x * dx) / dx,
                        (v.Z + sz - z * dz) / dz, v.Age)).ToArray();
                    local = Clip(local, v => v.X); local = Clip(local, v => 1 - v.X);
                    local = Clip(local, v => v.Z); local = Clip(local, v => 1 - v.Z);
                    var integral = Integrate(local);
                    if (integral.Area == 0) continue;
                    if (SignedArea(local) < 0) Array.Reverse(local);
                    int cell = z * side + x;
                    if (!pieces.TryGetValue(cell, out var occupied)) pieces[cell] = occupied = new();
                    foreach (var other in occupied)
                    {
                        var overlap = local;
                        for (int j = 0; j < other.Polygon.Length && overlap.Length > 0; j++)
                        {
                            Vertex a = other.Polygon[j], b = other.Polygon[(j + 1) % other.Polygon.Length];
                            overlap = Clip(overlap, v => (b.X - a.X) * (v.Z - a.Z) - (b.Z - a.Z) * (v.X - a.X));
                        }
                        if (Integrate(overlap).Area > RoundoffArea)
                            throw new ArgumentException($"Overlapping material footprints at cell {cell}: {patches[other.Patch].Id} / {id}.");
                    }
                    if (occupied.Count >= 32 || packets.Count >= MaximumPackets)
                        throw new ArgumentException("Exceeded cut-cell packet budget.");
                    occupied.Add(new(patchId, local));
                    packets.Add(new(cell, patchId, integral.Area, integral.MeanAge));
                    sumArea += integral.Area * dx * dz;
                    sumAgeArea += integral.Area * dx * dz * integral.MeanAge;
                }
            }
            Require(expectedArea, sumArea, "patch surface");
            Require(expectedArea * meanAge, sumAgeArea, "dated area moment");
        }
    }

    private static Vertex[] Clip(Vertex[] source, Func<Vertex, double> distance)
    {
        if (source.Length == 0) return source;
        var output = new List<Vertex>(source.Length + 2);
        Vertex a = source[^1]; double da = distance(a);
        foreach (var b in source)
        {
            double db = distance(b);
            if ((da >= 0) != (db >= 0))
            {
                double t = da / (da - db);
                output.Add(new(a.X + t * (b.X - a.X), a.Z + t * (b.Z - a.Z), a.Age + t * (b.Age - a.Age)));
            }
            if (db >= 0) output.Add(b);
            a = b; da = db;
        }
        return output.ToArray();
    }

    private static double SignedArea(Vertex[] p)
    {
        double area = 0;
        for (int i = 1; i + 1 < p.Length; i++)
            area += Cross(p[0], p[i], p[i + 1]) / 2;
        return area;
    }
    private static double Cross(Vertex a, Vertex b, Vertex c)
        => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
    private static (double Area, double MeanAge) Integrate(Vertex[] p)
    {
        double area = 0, moment = 0;
        for (int i = 1; i + 1 < p.Length; i++)
        {
            double triangle = Math.Abs(Cross(p[0], p[i], p[i + 1])) / 2;
            area += triangle; moment += triangle * ((p[0].Age + p[i].Age + p[i + 1].Age) / 3);
        }
        if (!double.IsFinite(area) || !double.IsFinite(moment)) throw new ArithmeticException("Nonfinite polygon integral.");
        return (area, area > 0 ? moment / area : 0);
    }
    private static void Require(double expected, double actual, string name)
    {
        if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-9 * Math.Max(1, Math.Abs(expected)))
            throw new ArithmeticException($"Conservation failed for {name}: {actual:R} instead of {expected:R}.");
    }
}
