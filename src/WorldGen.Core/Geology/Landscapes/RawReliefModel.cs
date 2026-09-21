using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;
using static ISRWorldGen.Core.Geology.Landscapes.RawReliefStructure;

namespace ISRWorldGen.Core.Geology.Landscapes;

/// <summary>Solid height only; never water, routing surface or eroded height.</summary>
public readonly record struct RawReliefSample(double HeightBlocks, double SignedHeightAboveSea,
    double ContinentalPotential, double Compression, double Extension);

/// <summary>
/// Opt-in structural bedrock candidate: metric continental margins, plate-contact
/// belts and inherited basement sutures. Noise modulates these structures; it does
/// not select their large-scale location. No erosion or native world mutation.
/// </summary>
public sealed class RawReliefModel
{
    public const string AlgorithmId = "raw-structural-relief-v2-metric-margins-contact-unions";
    private readonly int seed;
    private readonly WorldBounds bounds;
    private readonly Feature[] features;
    private readonly Belt[] belts;
    private readonly double scale;
    public ReliefVerticalPlan VerticalPlan { get; }
    public Hash256 ContentChecksum { get; }
    public int CollisionBelts { get; }
    public int DivergentBelts { get; }
    public int InheritedBelts { get; }

    private RawReliefModel(LandscapeModel basis, AtlasMesh atlas, PlateAtlasSnapshot plates,
        ContinentalFieldModel continents, Feature[] features, Belt[] belts)
    {
        seed = plates.Identity.NativeSeed; bounds = atlas.Bounds;
        this.features = features; this.belts = belts; VerticalPlan = basis.VerticalPlan;
        scale = Math.Min(1d, Math.Min(bounds.Width, bounds.Length) / 131072d);
        CollisionBelts = belts.Count(b => b.Kind == PlateBoundaryKind.Collision && !b.Inherited);
        DivergentBelts = belts.Count(b => b.Kind == PlateBoundaryKind.Divergence);
        InheritedBelts = belts.Count(b => b.Inherited);
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
        WorldBounds b = atlas.Bounds;
        if (b.Width > 1_024_000 || b.Length > 1_024_000 || b.Width < 4096 || b.Length < 4096 ||
            Math.Abs((double)b.MinX) > 4_000_000_000_000 || Math.Abs((double)b.MinZ) > 4_000_000_000_000)
            throw new ArgumentOutOfRangeException(nameof(atlas), "Raw-relief candidate exceeds its qualified domain.");
        int seed = plates.Identity.NativeSeed;
        double sx = b.Width, sz = b.Length, scale = Math.Min(1d, Math.Min(sx, sz) / 131072d);
        Feature[] features = continents.Features.OrderBy(f => f.FeatureId.High).ThenBy(f => f.FeatureId.Low).Select(f =>
        {
            double angle = Unit(f.FeatureId, 4) * Math.Tau;
            return new Feature(f.FeatureId, f.Scale == ContinentalFeatureScale.Macro,
                b.MinX + sx * f.CenterXPpm / 1e6, b.MinZ + sz * f.CenterZPpm / 1e6,
                sx * f.RadiusXPpm / 1e6, sz * f.RadiusZPpm / 1e6, Math.Cos(angle), Math.Sin(angle),
                Unit(f.FeatureId, 5) * Math.Tau, Unit(f.FeatureId, 6) * Math.Tau);
        }).ToArray();
        var polygons = atlas.Cells.ToDictionary(c => c.SiteId);
        var states = plates.Cells.ToDictionary(c => c.CellId);
        var contacts = new List<Contact>();
        foreach (PlateBoundaryRecord boundary in plates.Boundaries.OrderBy(v => v.CellA.High).ThenBy(v => v.CellA.Low)
            .ThenBy(v => v.CellB.High).ThenBy(v => v.CellB.Low))
        {
            if (boundary.Kind is not (PlateBoundaryKind.Collision or PlateBoundaryKind.Divergence)) continue;
            ExactPoint[] shared = polygons[boundary.CellA].Vertices.Intersect(polygons[boundary.CellB].Vertices).Order().ToArray();
            if (shared.Length < 2) continue;
            var segment = new Segment(shared[0].X.ToDouble(), shared[0].Z.ToDouble(), shared[^1].X.ToDouble(), shared[^1].Z.ToDouble());
            double length = Math.Sqrt(Math.Pow(segment.Bx - segment.Ax, 2) + Math.Pow(segment.Bz - segment.Az, 2));
            if (!(length > 0)) continue;
            double age = (states[boundary.CellA].RelativeAgePpm + states[boundary.CellB].RelativeAgePpm) / 2e6;
            double intensity = boundary.Kind == PlateBoundaryKind.Collision ? boundary.UpliftNormalized : boundary.SubsidenceNormalized;
            StableId first = Less(boundary.PlateA, boundary.PlateB) ? boundary.PlateA : boundary.PlateB;
            StableId second = first == boundary.PlateA ? boundary.PlateB : boundary.PlateA;
            contacts.Add(new Contact(first, second, boundary.Kind, segment, length, age, intensity));
        }
        var belts = new List<Belt>();
        foreach (var group in contacts.GroupBy(c => (c.First, c.Second, c.Kind)))
        {
            double total = group.Sum(c => c.Length), age = group.Sum(c => c.Length * c.Age) / total;
            double intensity = group.Sum(c => c.Length * c.Intensity) / total;
            StableId stream = StableId.Derive(RandomDomain.Geology, group.Key.First, group.Key.Second.High ^ group.Key.Second.Low);
            double width = (2600 + 2200 * age) * (.85 + .3 * Unit(stream, 400)) * scale;
            belts.Add(new Belt(group.Select(c => c.Segment).ToArray(), width,
                1 - Math.Exp(-6 * intensity), group.Key.Kind, false));
        }
        // Macro crust envelopes are inherited terranes. A sparse curved basement
        // suture is a declared structural prior, NOT a simulated plate history.
        // Its entire centreline is planned once; it does not depend on image pixels.
        foreach (Feature feature in features.Where(f => f.Macro && Unit(f.Id, 601) > .35))
        {
            var segments = new List<Segment>();
            (double X, double Z) At(double u)
            {
                double v = .12 * Math.Sin(3.2 * u + feature.Phase1) + .065 * Math.Sin(6.3 * u + feature.Phase2);
                return (feature.X + feature.Cos * u * feature.Rx - feature.Sin * v * feature.Rz,
                    feature.Z + feature.Sin * u * feature.Rx + feature.Cos * v * feature.Rz);
            }
            for (int i = 0; i < 20; i++)
            {
                var a = At(-.82 + i * 1.64 / 20); var z = At(-.82 + (i + 1) * 1.64 / 20);
                segments.Add(new Segment(a.X, a.Z, z.X, z.Z));
            }
            belts.Add(new Belt(segments.ToArray(), (2600 + 2200 * Unit(feature.Id, 602)) * scale,
                .30 + .25 * Unit(feature.Id, 603), PlateBoundaryKind.Collision, true));
        }
        return new RawReliefModel(basis, atlas, plates, continents, features, belts.ToArray());
        double Unit(StableId id, ulong counter) => (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, id, counter) >> 11) / 9007199254740992d;
    }

    public RawReliefSample Sample(long x, long z)
    {
        if (!bounds.Contains(x, z)) throw new ArgumentOutOfRangeException(nameof(x));
        double px = x - (double)bounds.MinX, pz = z - (double)bounds.MinZ;
        double wx = x + scale * (7200 * F(px, pz, 34000, 10) + 1900 * F(px, pz, 8500, 11));
        double wz = z + scale * (7200 * F(px, pz, 34000, 12) + 1900 * F(px, pz, 8500, 13));
        double signedMarginDistance = double.NegativeInfinity;
        foreach (Feature f in features)
        {
            double dx = wx - f.X, dz = wz - f.Z;
            double u = (f.Cos * dx + f.Sin * dz) / f.Rx, v = (-f.Sin * dx + f.Cos * dz) / f.Rz;
            double theta = Math.Atan2(v, u), radius = Math.Sqrt(u * u + v * v);
            double perturbation = Smooth(.15, .65, radius) * (.12 * Math.Sin(3 * theta + f.Phase1) + .07 * Math.Sin(7 * theta + f.Phase2));
            // Approximate metric distance normal to the crust envelope, not an
            // altitude. Small islands must not receive continent-sized shelves.
            double distance = (1 - radius + perturbation) * Math.Min(f.Rx, f.Rz);
            signedMarginDistance = SmoothMax(signedMarginDistance, distance, 1800 * scale);
        }
        signedMarginDistance += 1150 * scale * F(px, pz, 6700, 21);
        double land = Smooth(-700 * scale, 1400 * scale, signedMarginDistance);
        double compression = 0, extension = 0, axial = 0, inherited = 0;
        foreach (Belt belt in belts)
        {
            double distance2 = DistanceSquared(wx, wz, belt.Segments) / (belt.Width * belt.Width);
            if (distance2 >= 49) continue;
            double taper = 1 - distance2 / 49;
            double envelope = Math.Exp(-.5 * distance2) * taper * taper * belt.Strength;
            if (belt.Inherited) inherited = 1 - (1 - inherited) * (1 - envelope);
            else if (belt.Kind == PlateBoundaryKind.Collision) compression = 1 - (1 - compression) * (1 - envelope);
            else
            {
                extension = 1 - (1 - extension) * (1 - envelope);
                axial = Math.Max(axial, Math.Exp(-12 * distance2) * taper * taper * belt.Strength);
            }
        }
        double offshore = Math.Max(0d, -signedMarginDistance);
        double shelfWidth = scale * (1800 + 3000 * (.5 + .5 * F(px, pz, 28000, 22))) * (1 - .7 * compression);
        double slopeWidth = scale * (3200 + 2200 * (.5 + .5 * F(px, pz, 16000, 23)));
        double slope = Smooth(shelfWidth, shelfWidth + slopeWidth, offshore);
        double marine = -.055 * (1 - Math.Exp(-offshore / (900 * scale))) - .64 * slope;
        marine += (.014 + .025 * slope) * F(px, pz, 12000, 32);
        marine += .027 * slope * F(px, pz, 42000, 33);
        double continentalBase = (.052 + .055 * F(px, pz, 24000, 34)) * Smooth(0, 4200 * scale, signedMarginDistance);
        continentalBase += .025 * F(px, pz, 5800, 35) * land;
        // The reference coastline is not a cap or a change of physical surface.
        double initial = marine * (1 - land) + continentalBase * land;
        double mountains = Mountain(seed, px, pz, 5200 * scale, 41);
        double variation = .86 + .14 * (.5 + .5 * F(px, pz, 18000, 42));
        double active = Math.Pow(compression, .72) * (.24 + .76 * mountains) * variation;
        double old = inherited * (.20 + .80 * Mountain(seed, px, pz, 9000 * scale, 51));
        double deformation = 1 - (1 - active) * (1 - old);
        double value = initial + (.98 - initial) * deformation * (.30 + .70 * land);
        double deepOcean = (1 - land) * slope;
        value += (-.35 - value) * extension * .60 * deepOcean;
        value += (-.96 - value) * axial * .055 * deepOcean;
        value += (-.98 - value) * compression * .16 * (1 - land);
        if (!double.IsFinite(value) || value <= -1 || value >= 1)
            throw new InvalidOperationException("Unclamped raw relief exceeded its vertical envelope.");
        double height = VerticalPlan.Transform.MapModelAltitudeToBlocks(value);
        return new RawReliefSample(height, height - VerticalPlan.SeaLevelBlocks, signedMarginDistance / (10000 * scale), compression, extension);
    }

    private double F(double x, double z, double wavelength, ulong key) => Fractal(seed, x, z, wavelength * scale, key);
    private static bool Less(StableId a, StableId b) => a.High < b.High || (a.High == b.High && a.Low < b.Low);
    private static double SmoothMax(double a, double b, double k)
    {
        double h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Max(a, b) + h * h * k * .25;
    }
    private sealed record Feature(StableId Id, bool Macro, double X, double Z, double Rx, double Rz, double Cos, double Sin, double Phase1, double Phase2);
    private sealed record Belt(Segment[] Segments, double Width, double Strength, PlateBoundaryKind Kind, bool Inherited);
    private sealed record Contact(StableId First, StableId Second, PlateBoundaryKind Kind, Segment Segment, double Length, double Age, double Intensity);
}
