using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Core.Geology.Landscapes;

public sealed record TectonicReliefSettings
{
    public TectonicReliefSettings(double youngRidgeWidthBlocks, double maximumNormalizedCrest,
        int maximumBelts, int samplesPerBelt)
    {
        if (!double.IsFinite(youngRidgeWidthBlocks) || youngRidgeWidthBlocks is < 64 or > 10_000 ||
            !double.IsFinite(maximumNormalizedCrest) || maximumNormalizedCrest is <= .86 or >= 1 ||
            maximumBelts is < 1 or > 4096 || samplesPerBelt is < 4 or > 64)
            throw new ArgumentOutOfRangeException(nameof(youngRidgeWidthBlocks), "Invalid bounded tectonic-relief settings.");
        YoungRidgeWidthBlocks = youngRidgeWidthBlocks;
        MaximumNormalizedCrest = maximumNormalizedCrest;
        MaximumBelts = maximumBelts;
        SamplesPerBelt = samplesPerBelt;
    }
    public double YoungRidgeWidthBlocks { get; }
    public double MaximumNormalizedCrest { get; }
    public int MaximumBelts { get; }
    public int SamplesPerBelt { get; }
}

public readonly record struct RidgeNode(double X, double Z, double WidthBlocks, double Strength);
public sealed record RidgeBelt(StableId CellA, StableId CellB, ReadOnlyCollection<RidgeNode> Spine,
    ReadOnlyCollection<RidgeNode> SpurRoots, ReadOnlyCollection<RidgeNode> SpurEnds);
public readonly record struct TectonicReliefSample(double AltitudeBlocks, double ModelAltitudeNormalized,
    double BathymetryBlocks, double UpliftWeight, LandscapeSample Foundation);

/// <summary>
/// Continuous, globally owned orogenic belts derived from convergent plate
/// boundaries. The Voronoi edge locates the forcing, not a region-sized hill:
/// curved spines, uneven peaks/cols, age-dependent widths and tapered spurs are
/// sampled across cell boundaries. No image contrast, raster noise or height
/// clamping is used. The previous immutable landscape is retained as foundation.
/// This explicit revision is not substituted into existing saved-world identities.
/// </summary>
public sealed class TectonicReliefModel
{
    public const string AlgorithmId = "tectonic-relief-v2-continuous-continent-and-belts";
    private readonly LandscapeModel foundation;
    private readonly TectonicReliefSettings settings;
    private readonly RidgeBelt[] belts;
    private readonly ContinentalFieldModel? continents;

    private TectonicReliefModel(LandscapeModel foundation, TectonicReliefSettings settings, RidgeBelt[] belts, ContinentalFieldModel? continents)
    {
        this.foundation = foundation;
        this.settings = settings;
        this.belts = belts;
        this.continents = continents;
        Belts = Array.AsReadOnly(belts);
        var text = new StringBuilder(AlgorithmId).Append('|').Append(foundation.ContentChecksum)
            .Append('|').Append(continents?.ContentChecksum.ToString() ?? "legacy-cell-datum");
        Append(settings.YoungRidgeWidthBlocks); Append(settings.MaximumNormalizedCrest);
        text.Append('|').Append(settings.MaximumBelts.ToString(CultureInfo.InvariantCulture));
        text.Append('|').Append(settings.SamplesPerBelt.ToString(CultureInfo.InvariantCulture));
        foreach (RidgeBelt belt in belts)
        {
            text.Append('|').Append(belt.CellA).Append('|').Append(belt.CellB);
            foreach (RidgeNode point in belt.Spine.Concat(belt.SpurRoots).Concat(belt.SpurEnds))
            { Append(point.X); Append(point.Z); Append(point.WidthBlocks); Append(point.Strength); }
        }
        ContentChecksum = Hash256.Compute(Encoding.UTF8.GetBytes(text.ToString()));
        void Append(double value) => text.Append('|').Append(BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture));
    }

    public ReadOnlyCollection<RidgeBelt> Belts { get; }
    public Hash256 ContentChecksum { get; }
    public ReliefVerticalPlan VerticalPlan => foundation.VerticalPlan;

    public static TectonicReliefModel Build(LandscapeModel foundation, AtlasMesh atlas,
        PlateAtlasSnapshot plates, TectonicReliefSettings settings, ContinentalFieldModel? continents = null)
    {
        ArgumentNullException.ThrowIfNull(foundation); ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(plates); ArgumentNullException.ThrowIfNull(settings);
        if (foundation.PlateSnapshotChecksum != plates.ContentChecksum || plates.RecomputeContentChecksum() != plates.ContentChecksum ||
            foundation.AtlasContentChecksum != plates.AtlasContentChecksum ||
            PlateAtlasProvenance.ComputeAtlasContentChecksum(atlas) != plates.AtlasContentChecksum)
            throw new ArgumentException("Tectonic relief requires the exact sealed foundation, atlas and plate snapshot.");
        if (continents is not null && (continents.ContentChecksum != plates.ContinentalModelChecksum ||
            continents.Bounds != atlas.Bounds))
            throw new ArgumentException("Continuous relief requires the exact continental field sealed into the plate atlas.", nameof(continents));
        PlateBoundaryRecord[] forcing = plates.Boundaries.Where(boundary => boundary.Kind == PlateBoundaryKind.Collision && boundary.UpliftNormalized > 0d)
            .OrderBy(boundary => boundary.CellA.High).ThenBy(boundary => boundary.CellA.Low)
            .ThenBy(boundary => boundary.CellB.High).ThenBy(boundary => boundary.CellB.Low).ToArray();
        if (forcing.Length > settings.MaximumBelts)
            throw new ArgumentOutOfRangeException(nameof(settings), "Tectonic belt quota exceeded before construction.");
        var polygons = atlas.Cells.ToDictionary(cell => cell.SiteId);
        var states = plates.Cells.ToDictionary(cell => cell.CellId);
        var result = new List<RidgeBelt>();
        foreach (PlateBoundaryRecord boundary in forcing)
        {
            PlateCellState a = states[boundary.CellA], b = states[boundary.CellB];
            // Oceanic collisions remain in the geological forcing but do not all
            // become continental ranges; island-arc morphology is a separate job.
            if (a.ContinentalHeightPpm < -150_000 && b.ContinentalHeightPpm < -150_000) continue;
            ExactPoint[] shared = polygons[boundary.CellA].Vertices.Intersect(polygons[boundary.CellB].Vertices).Order().ToArray();
            if (shared.Length < 2) continue;
            ExactPoint first = shared[0], last = shared[^1];
            double ax = first.X.ToDouble(), az = first.Z.ToDouble(), bx = last.X.ToDouble(), bz = last.Z.ToDouble();
            double dx = bx - ax, dz = bz - az, length = Math.Sqrt(dx * dx + dz * dz);
            if (!double.IsFinite(length) || length <= 0d) continue;
            StableId stream = StableId.Derive(RandomDomain.Geology, boundary.CellA, boundary.CellB.High ^ boundary.CellB.Low);
            double age = .5d * (a.RelativeAgePpm + b.RelativeAgePpm) / 1_000_000d;
            double width = settings.YoungRidgeWidthBlocks * (.8d + .4d * Random(1)) * (1d + 1.6d * age);
            double strength = (1d - Math.Exp(-6d * boundary.UpliftNormalized)) * (1d - .3d * age);
            double bow = (2d * Random(2) - 1d) * Math.Min(length * .14d, width * 3d);
            double nx = -dz / length, nz = dx / length;
            var nodes = new RidgeNode[settings.SamplesPerBelt + 1];
            var roots = new List<RidgeNode>(); var ends = new List<RidgeNode>();
            for (int i = 0; i < nodes.Length; i++)
            {
                double t = i / (double)settings.SamplesPerBelt;
                double offset = 4d * t * (1d - t) * bow;
                double factor = .58d + .42d * Random((ulong)(10 + i));
                nodes[i] = new RidgeNode(ax + dx * t + nx * offset, az + dz * t + nz * offset,
                    width * (.75d + .5d * Random((ulong)(100 + i))), strength * factor);
                if (i == 0 || i == nodes.Length - 1 || i % 2 == 0) continue;
                double side = Random((ulong)(200 + i)) < .5d ? -1d : 1d;
                double reach = width * (3d + 3d * Random((ulong)(300 + i)));
                roots.Add(nodes[i] with { WidthBlocks = width * .65d, Strength = nodes[i].Strength * .8d });
                ends.Add(new RidgeNode(nodes[i].X + nx * reach * side + dx / length * reach * .35d,
                    nodes[i].Z + nz * reach * side + dz / length * reach * .35d, width * .25d, 0d));
            }
            result.Add(new RidgeBelt(boundary.CellA, boundary.CellB, Array.AsReadOnly(nodes),
                Array.AsReadOnly(roots.ToArray()), Array.AsReadOnly(ends.ToArray())));
            double Random(ulong counter) => (StatelessRandomV1.NextUInt64(plates.Identity.NativeSeed,
                RandomDomain.Geology, stream, counter) >> 11) * (1d / 9007199254740992d);
        }
        return new TectonicReliefModel(foundation, settings, result.ToArray(), continents);
    }

    public TectonicReliefSample Sample(long x, long z)
    {
        LandscapeSample basis = foundation.Sample(x, z);
        double remaining = 1d;
        foreach (RidgeBelt belt in belts)
        {
            double weight = 0d;
            for (int i = 1; i < belt.Spine.Count; i++)
                weight = Math.Max(weight, SegmentWeight(belt.Spine[i - 1], belt.Spine[i], x, z));
            for (int i = 0; i < belt.SpurRoots.Count; i++)
                weight = Math.Max(weight, SegmentWeight(belt.SpurRoots[i], belt.SpurEnds[i], x, z));
            remaining *= 1d - weight;
        }
        double uplift = 1d - remaining;
        // A continuous continental datum replaces the old region-centre land/ocean
        // jump when the exact sealed continental model is explicitly supplied.
        // No per-image normalization or output altitude clamp is involved.
        double initial = basis.ModelAltitudeNormalized;
        if (continents is not null)
        {
            double c = continents.SampleHeightPpm(x, z) / 1_000_000d;
            double datum = (c >= 0d ? .34d : .58d) * Math.Tanh(2.8d * c);
            double residual = basis.PrimaryResidualContributionNormalized + basis.ForeignResidualContributionNormalized;
            initial = datum + .65d * residual;
        }
        double normalized = initial + (settings.MaximumNormalizedCrest - initial) * uplift;
        if (!double.IsFinite(normalized) || normalized is < -1 or > 1)
            throw new InvalidOperationException("Tectonic relief violated its analytical vertical envelope.");
        double altitude = VerticalPlan.Transform.MapModelAltitudeToBlocks(normalized);
        return new TectonicReliefSample(altitude, normalized,
            Math.Max(0d, VerticalPlan.SeaLevelBlocks - altitude), uplift, basis);
    }

    private static double SegmentWeight(RidgeNode a, RidgeNode b, long x, long z)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        double t = Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / (dx * dx + dz * dz), 0d, 1d);
        double px = x - (a.X + t * dx), pz = z - (a.Z + t * dz);
        double smooth = t * t * (3d - 2d * t);
        double width = a.WidthBlocks + smooth * (b.WidthBlocks - a.WidthBlocks);
        double d2 = (px * px + pz * pz) / (width * width);
        if (d2 >= 36d) return 0d;
        double taper = 1d - d2 / 36d;
        double shape = (.82d * Math.Exp(-.5d * d2) + .18d * Math.Exp(-d2 / 12d)) * taper * taper;
        return shape * (a.Strength + smooth * (b.Strength - a.Strength));
    }
}
