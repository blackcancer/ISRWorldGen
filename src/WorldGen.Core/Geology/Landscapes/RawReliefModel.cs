using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Core.Geology.Landscapes;

/// <summary>Solid height only; never water, routing surface or eroded height.</summary>
public readonly record struct RawReliefSample(double HeightBlocks, double SignedHeightAboveSea,
    double ContinentalPotential, double Compression, double Extension);

/// <summary>
/// Opt-in structural relief experiment. Continental envelopes locate crust and
/// actual plate boundaries locate deformation. Nested ridge detail is restricted
/// to orogenic belts. Both land and seabed use the same continuous height field.
/// This class never migrates saved worlds or invokes any erosion algorithm.
/// </summary>
public sealed class RawReliefModel
{
    public const string AlgorithmId = "raw-structural-relief-v1-unmasked-seafloor";
    private readonly int seed;
    private readonly WorldBounds bounds;
    private readonly Feature[] features;
    private readonly Belt[] belts;
    private readonly double warpScale;
    public ReliefVerticalPlan VerticalPlan { get; }
    public Hash256 ContentChecksum { get; }
    public int CollisionBelts { get; }
    public int DivergentBelts { get; }

    private RawReliefModel(LandscapeModel basis, AtlasMesh atlas, PlateAtlasSnapshot plates,
        ContinentalFieldModel continents, Feature[] features, Belt[] belts)
    {
        seed = plates.Identity.NativeSeed; bounds = atlas.Bounds;
        this.features = features; this.belts = belts; VerticalPlan = basis.VerticalPlan;
        warpScale = Math.Min(1d, Math.Min(bounds.Width, bounds.Length) / 131072d);
        CollisionBelts = belts.Count(b => b.Kind == PlateBoundaryKind.Collision);
        DivergentBelts = belts.Count(b => b.Kind == PlateBoundaryKind.Divergence);
        ContentChecksum = Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('|', AlgorithmId,
            basis.ContentChecksum, plates.ContentChecksum, continents.ContentChecksum, seed.ToString(CultureInfo.InvariantCulture))));
    }
    public static RawReliefModel Build(LandscapeModel basis, AtlasMesh atlas,
        PlateAtlasSnapshot plates, ContinentalFieldModel continents)
    {
        ArgumentNullException.ThrowIfNull(basis); ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(plates); ArgumentNullException.ThrowIfNull(continents);
        if (basis.PlateSnapshotChecksum != plates.ContentChecksum || plates.RecomputeContentChecksum() != plates.ContentChecksum ||
            basis.AtlasContentChecksum != plates.AtlasContentChecksum ||
            PlateAtlasProvenance.ComputeAtlasContentChecksum(atlas) != plates.AtlasContentChecksum ||
            continents.ContentChecksum != plates.ContinentalModelChecksum || continents.Bounds != atlas.Bounds)
            throw new ArgumentException("Raw relief requires one exact sealed geological provenance.");
        var b = atlas.Bounds;
        if (b.Width > 1_024_000 || b.Length > 1_024_000 || b.Width < 4096 || b.Length < 4096 ||
            Math.Abs((double)b.MinX) > 4_000_000_000_000 || Math.Abs((double)b.MinZ) > 4_000_000_000_000)
            throw new ArgumentOutOfRangeException(nameof(atlas), "Raw-relief candidate exceeds its qualified domain.");
        int seed = plates.Identity.NativeSeed;
        double sx = b.Width, sz = b.Length;
        var features = continents.Features.OrderBy(f => f.FeatureId.High).ThenBy(f => f.FeatureId.Low).Select(f =>
        {
            double angle = Unit(f.FeatureId, 4) * Math.Tau;
            return new Feature(b.MinX + sx * f.CenterXPpm / 1e6, b.MinZ + sz * f.CenterZPpm / 1e6,
                sx * f.RadiusXPpm / 1e6, sz * f.RadiusZPpm / 1e6, Math.Cos(angle), Math.Sin(angle),
                Unit(f.FeatureId, 5) * Math.Tau, Unit(f.FeatureId, 6) * Math.Tau);
        }).ToArray();
        var polygons = atlas.Cells.ToDictionary(c => c.SiteId);
        var states = plates.Cells.ToDictionary(c => c.CellId);
        var belts = new List<Belt>();
        foreach (var boundary in plates.Boundaries.OrderBy(v => v.CellA.High).ThenBy(v => v.CellA.Low)
            .ThenBy(v => v.CellB.High).ThenBy(v => v.CellB.Low))
        {
            if (boundary.Kind is not (PlateBoundaryKind.Collision or PlateBoundaryKind.Divergence)) continue;
            var shared = polygons[boundary.CellA].Vertices.Intersect(polygons[boundary.CellB].Vertices).Order().ToArray();
            if (shared.Length < 2) continue;
            double ax = shared[0].X.ToDouble(), az = shared[0].Z.ToDouble(), bx = shared[^1].X.ToDouble(), bz = shared[^1].Z.ToDouble();
            if (ax == bx && az == bz) continue;
            var stream = StableId.Derive(RandomDomain.Geology, boundary.CellA, boundary.CellB.High ^ boundary.CellB.Low);
            double age = (states[boundary.CellA].RelativeAgePpm + states[boundary.CellB].RelativeAgePpm) / 2e6;
            double scale = Math.Min(1d, Math.Min(sx, sz) / 131072d);
            double width = (1900 + 2400 * age) * (.75 + .5 * Unit(stream, 400)) * scale;
            double intensity = boundary.Kind == PlateBoundaryKind.Collision ? boundary.UpliftNormalized : boundary.SubsidenceNormalized;
            belts.Add(new Belt(ax, az, bx, bz, width, 1 - Math.Exp(-5 * intensity), boundary.Kind));
        }
        return new RawReliefModel(basis, atlas, plates, continents, features, belts.ToArray());
        double Unit(StableId id, ulong counter) => (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, id, counter) >> 11) / 9007199254740992d;
    }
    public RawReliefSample Sample(long x, long z)
    {
        if (!bounds.Contains(x, z)) throw new ArgumentOutOfRangeException(nameof(x));
        double px = x - (double)bounds.MinX, pz = z - (double)bounds.MinZ;
        double wx = x + warpScale * (3400 * Fractal(px, pz, 30000 * warpScale, 10) + 650 * Fractal(px, pz, 6500 * warpScale, 11));
        double wz = z + warpScale * (3400 * Fractal(px, pz, 30000 * warpScale, 12) + 650 * Fractal(px, pz, 6500 * warpScale, 13));
        double continental = double.NegativeInfinity;
        foreach (Feature f in features)
        {
            double dx = wx - f.X, dz = wz - f.Z;
            double u = (f.Cos * dx + f.Sin * dz) / f.Rx, v = (-f.Sin * dx + f.Cos * dz) / f.Rz;
            double theta = Math.Atan2(v, u), radius = Math.Sqrt(u * u + v * v);
            // The angular perturbation vanishes at the centre; no atan2 singularity.
            double shoreWeight = Smooth(.15, .65, radius);
            double potential = 1 - radius + shoreWeight * (.085 * Math.Sin(3 * theta + f.Phase1) + .035 * Math.Sin(7 * theta + f.Phase2));
            continental = SmoothMax(continental, potential, .10);
        }
        continental += .14 * Fractal(px, pz, 10500 * warpScale, 21);
        double land = Smooth(-.10, .12, continental);
        double inland = Math.Max(0, continental), depth = Math.Max(0, -continental);
        double initial = continental >= 0
            ? .18 * (1 - Math.Exp(-1.5 * inland)) + .095 * inland / (1 + inland) * Ridged(px, pz, 9000 * warpScale, 31)
            : -.08 * (1 - Math.Exp(-8 * depth)) - .67 * Smooth(.12, .85, depth);
        initial += (.022 + .018 * land) * Fractal(px, pz, 7200 * warpScale, 32);
        initial += .025 * (1 - land) * Fractal(px, pz, 24000 * warpScale, 33);
        double notCompression = 1, notExtension = 1, notAxial = 1;
        foreach (Belt belt in belts)
        {
            double dx = belt.Bx - belt.Ax, dz = belt.Bz - belt.Az;
            double t = Math.Clamp(((wx - belt.Ax) * dx + (wz - belt.Az) * dz) / (dx * dx + dz * dz), 0, 1);
            double qx = wx - (belt.Ax + t * dx), qz = wz - (belt.Az + t * dz);
            double d2 = (qx * qx + qz * qz) / (belt.Width * belt.Width);
            if (d2 >= 36) continue;
            double taper = 1 - d2 / 36;
            double envelope = Math.Exp(-.5 * d2) * taper * taper * belt.Strength;
            if (belt.Kind == PlateBoundaryKind.Collision) notCompression *= 1 - envelope;
            else
            {
                notExtension *= 1 - envelope;
                notAxial *= 1 - Math.Exp(-8 * d2) * taper * taper * belt.Strength;
            }
        }
        double compression = 1 - notCompression, extension = 1 - notExtension;
        double texture = Ridged(px, pz, 5400 * warpScale, 41);
        double variation = .70 + .30 * (.5 + .5 * Fractal(px, pz, 18000 * warpScale, 42));
        double uplift = compression * (.36 + .64 * texture) * variation * (.30 + .70 * land);
        double value = initial + (.98 - initial) * uplift;
        value += (.035 - value) * extension * .55 * (1 - land);
        value -= (1 - notAxial) * .055 * (1 - .2 * land);
        value += (-.98 - value) * compression * .18 * (1 - land);
        if (!double.IsFinite(value) || value <= -1 || value >= 1)
            throw new InvalidOperationException("Unclamped raw relief exceeded its vertical envelope.");
        double height = VerticalPlan.Transform.MapModelAltitudeToBlocks(value);
        return new RawReliefSample(height, height - VerticalPlan.SeaLevelBlocks, continental, compression, extension);
    }
    private double Fractal(double x, double z, double wavelength, ulong key)
    {
        double result = 0, weight = 4d / 7;
        x /= wavelength; z /= wavelength;
        for (int octave = 0; octave < 3; octave++)
        {
            result += weight * Noise(x, z, key * 8 + (ulong)octave);
            (x, z) = (1.6 * x - 1.2 * z + 19.3, 1.2 * x + 1.6 * z - 7.1); weight *= .5;
        }
        return result;
    }
    private double Ridged(double x, double z, double wavelength, ulong key)
    {
        double result = 0, weight = 8d / 15, feedback = 1;
        x /= wavelength; z /= wavelength;
        for (int octave = 0; octave < 4; octave++)
        {
            double ridge = 1 - Math.Abs(Noise(x, z, key * 8 + (ulong)octave)); ridge *= ridge;
            result += weight * ridge * feedback; feedback = Math.Min(1, 1.8 * ridge);
            (x, z) = (1.6 * x - 1.2 * z + 3.7, 1.2 * x + 1.6 * z + 13.1); weight *= .5;
        }
        return result;
    }
    private double Noise(double x, double z, ulong key)
    {
        long ix = (long)Math.Floor(x), iz = (long)Math.Floor(z);
        double u = Fade(x - ix), v = Fade(z - iz);
        double a = Unit(ix, iz), b = Unit(ix + 1, iz), c = Unit(ix, iz + 1), d = Unit(ix + 1, iz + 1);
        return 2 * ((a + u * (b - a)) * (1 - v) + (c + u * (d - c)) * v) - 1;
        double Unit(long i, long j) => (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology,
            new StableId(unchecked((ulong)i), unchecked((ulong)j)), key) >> 11) / 9007199254740992d;
    }
    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Smooth(double a, double b, double v) => Fade(Math.Clamp((v - a) / (b - a), 0, 1));
    private static double SmoothMax(double a, double b, double k)
    {
        double h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Max(a, b) + h * h * k * .25;
    }
    private sealed record Feature(double X, double Z, double Rx, double Rz, double Cos, double Sin, double Phase1, double Phase2);
    private sealed record Belt(double Ax, double Az, double Bx, double Bz, double Width, double Strength, PlateBoundaryKind Kind);
}
