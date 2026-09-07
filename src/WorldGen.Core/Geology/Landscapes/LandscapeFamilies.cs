using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Landscapes;

public enum LandscapeFamily { RuggedRanges, OldMassifs, Plateaus, SedimentaryBasins, Plains, VolcanicDomains }

/// <summary>Dimensionless morphology parameters. Wavelength fields describe extent, not a common wave basis.</summary>
public sealed class LandscapeFamilyProfile
{
    public const double MinimumWavelengthBlocks = 1;
    public const double MaximumWavelengthBlocks = 1_000_000_000;
    public const double MaximumWavelengthRatio = 1_000_000;
    public LandscapeFamilyProfile(LandscapeFamily family, double macro, double meso, double detail, double amplitude, double macroWeight, double mesoWeight, double detailWeight)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        if (!double.IsFinite(macro) || !double.IsFinite(meso) || !double.IsFinite(detail) || macro > MaximumWavelengthBlocks || detail < MinimumWavelengthBlocks || macro <= meso || meso <= detail || macro / detail > MaximumWavelengthRatio) throw new ArgumentOutOfRangeException(nameof(macro), "Wavelengths must be finite, operationally bounded, ordered, and have a bounded ratio.");
        if (!double.IsFinite(amplitude) || amplitude is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitude));
        if (!double.IsFinite(macroWeight) || !double.IsFinite(mesoWeight) || !double.IsFinite(detailWeight) || macroWeight < 0 || mesoWeight < 0 || detailWeight < 0 || Math.Abs((macroWeight + mesoWeight + detailWeight) - 1) > 1e-12) throw new ArgumentOutOfRangeException(nameof(macroWeight), "Weights must be finite, nonnegative, and sum to one.");
        Family = family; MacroWavelengthBlocks = macro; MesoWavelengthBlocks = meso; DetailWavelengthBlocks = detail; ReliefAmplitudeNormalized = amplitude; MacroWeight = macroWeight; MesoWeight = mesoWeight; DetailWeight = detailWeight; ParameterChecksum = Checksum(this);
    }
    public LandscapeFamily Family { get; }
    public double MacroWavelengthBlocks { get; }
    public double MesoWavelengthBlocks { get; }
    public double DetailWavelengthBlocks { get; }
    public double ReliefAmplitudeNormalized { get; }
    public double MacroWeight { get; }
    public double MesoWeight { get; }
    public double DetailWeight { get; }
    public Hash256 ParameterChecksum { get; }
    private static Hash256 Checksum(LandscapeFamilyProfile p) => Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('|', new[] { "ISRW-LANDSCAPE-FAMILY-V3-ANALYTIC-BOUNDS", ((int)p.Family).ToString(CultureInfo.InvariantCulture), Bits(p.MacroWavelengthBlocks), Bits(p.MesoWavelengthBlocks), Bits(p.DetailWavelengthBlocks), Bits(p.ReliefAmplitudeNormalized), Bits(p.MacroWeight), Bits(p.MesoWeight), Bits(p.DetailWeight) })));
    private static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture);
}

public static class LandscapeFamilyCatalog
{
    private static readonly ReadOnlyCollection<LandscapeFamilyProfile> Catalog = Array.AsReadOnly(new[]
    {
        new LandscapeFamilyProfile(LandscapeFamily.RuggedRanges, 34000, 8000, 1100, .28, .55, .30, .15),
        new LandscapeFamilyProfile(LandscapeFamily.OldMassifs, 30000, 7000, 1900, .20, .62, .26, .12),
        new LandscapeFamilyProfile(LandscapeFamily.Plateaus, 36000, 9000, 2400, .18, .72, .20, .08),
        new LandscapeFamilyProfile(LandscapeFamily.SedimentaryBasins, 38000, 10000, 1800, .16, .70, .22, .08),
        new LandscapeFamilyProfile(LandscapeFamily.Plains, 52000, 15000, 4500, .07, .64, .26, .10),
        new LandscapeFamilyProfile(LandscapeFamily.VolcanicDomains, 26000, 5500, 900, .26, .60, .30, .10),
    });
    public static ReadOnlyCollection<LandscapeFamilyProfile> Profiles => Catalog;
    public static LandscapeFamilyProfile Get(LandscapeFamily family) => Enum.IsDefined(family) ? Catalog[(int)family] : throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown landscape family.");
}

/// <summary>Stateless global-coordinate sampler. Each family has its own geometric primitive.</summary>
public static class LandscapeSignatureSampler
{
    public const double MaximumAbsoluteCoordinateBlocks = 4_000_000_000_000d;
    public static double Sample(LandscapeFamilyProfile p, double x, double z, int seed, ulong streamOrdinal)
        => Sample(p, x, z, seed, streamOrdinal, 0, 0);

    /// <summary>Samples relative to the owning site so every finite-world cell receives its local morphology.</summary>
    public static double Sample(LandscapeFamilyProfile p, double x, double z, int seed, ulong streamOrdinal, long anchorX, long anchorZ)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (!double.IsFinite(x) || !double.IsFinite(z) || Math.Abs(x) > MaximumAbsoluteCoordinateBlocks || Math.Abs(z) > MaximumAbsoluteCoordinateBlocks || Math.Abs((double)anchorX) > MaximumAbsoluteCoordinateBlocks || Math.Abs((double)anchorZ) > MaximumAbsoluteCoordinateBlocks) throw new ArgumentOutOfRangeException(nameof(x), "Landscape coordinates and anchors must be within the qualified long-world bound.");
        x -= anchorX; z -= anchorZ;
        StableId s = StableId.Derive(RandomDomain.Geology, StableId.Zero, streamOrdinal);
        return p.Family switch
        {
            LandscapeFamily.RuggedRanges => Ranges(x, z, seed, s, p),
            LandscapeFamily.OldMassifs => Massifs(x, z, seed, s, p),
            LandscapeFamily.Plateaus => Plateau(x, z, seed, s, p),
            LandscapeFamily.SedimentaryBasins => Basin(x, z, seed, s, p),
            LandscapeFamily.Plains => Plain(x, z, seed, s, p),
            LandscapeFamily.VolcanicDomains => Volcanoes(x, z, seed, s, p),
            _ => throw new ArgumentOutOfRangeException(nameof(p), p.Family, "Unknown landscape family."),
        };
    }
    private static double Ranges(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    {
        var q = Local(x, z, seed, s, 0, p.MacroWavelengthBlocks);
        double scale = p.MacroWavelengthBlocks / p.MesoWavelengthBlocks;
        double mainRidge = 1.18 * Math.Exp(-10 * q.V * q.V);
        double parallelRidge = .50 * Math.Exp(-15 * (q.V - .42) * (q.V - .42));
        double col = -.35 * Math.Exp(-18 * (q.U * q.U + q.V * q.V));
        // Two bounded high points break the otherwise endless ridge into a regional
        // chain while retaining one dominant ridge orientation.
        double ridgeKnots = .18 * Gaussian(q.U + .34, q.V - .03, .20, .18) +
                            .16 * Gaussian(q.U - .39, q.V + .04, .22, .20);
        double serration = .18 * (Noise(q.U * scale, q.V * scale, seed, s, 12) - .5);
        double crags = .12 * (Noise(q.U * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, q.V * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, seed, s, 13) - .5);
        return (p.MacroWeight * (mainRidge + parallelRidge + col + ridgeKnots - .55)) + (p.MesoWeight * serration) + (p.DetailWeight * crags);
    }

    /// <summary>
    /// Samples the primitive in its owning regional frame.  The frame's orientation,
    /// extent and variant are part of the deterministic plan, so a morphology adapts
    /// to its region rather than repeating at a global fixed scale.
    /// </summary>
    public static double SampleRegional(
        LandscapeFamilyProfile profile,
        long x,
        long z,
        int seed,
        ulong streamOrdinal,
        LandscapeRegionPlan region)
    {
        double dx = x - region.CenterX;
        double dz = z - region.CenterZ;
        double cos = Math.Cos(region.OrientationRadians);
        double sin = Math.Sin(region.OrientationRadians);
        // Region extents tune the macro scale only within a bounded interval.  This
        // prevents a tiny or unusually broad Voronoi cell from turning a family
        // primitive into an unrecognisable global wave while still making the
        // regional footprint a real input to the primitive.
        double regionalScale = (Math.Sqrt(region.TransitionExtentUBlocks * region.TransitionExtentVBlocks) +
            Math.Sqrt(region.CoreExtentUBlocks * region.CoreExtentVBlocks)) / 2d;
        double extentFactor = Math.Clamp(regionalScale / profile.MacroWavelengthBlocks, .75d, 1.25d);
        double scale = profile.MacroWavelengthBlocks * extentFactor;
        double u = ((cos * dx) + (sin * dz)) / scale;
        double v = ((-sin * dx) + (cos * dz)) / scale;
        if (!double.IsFinite(u) || !double.IsFinite(v))
        {
            throw new InvalidOperationException("Regional landscape frame exceeded finite coordinates.");
        }

        // Lower frequencies remain family-owned ratios, preserving analytic bounds.
        return Sample(
            profile,
            u * profile.MacroWavelengthBlocks,
            v * profile.MacroWavelengthBlocks,
            seed,
            streamOrdinal ^ region.VariantOrdinal,
            0,
            0);
    }

    private static double Massifs(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    {
        var q = Local(x, z, seed, s, 20, p.MacroWavelengthBlocks);
        double scale = p.MacroWavelengthBlocks / p.MesoWavelengthBlocks;
        double summitA = Gaussian(q.U + .28, q.V - .13, .34, .31);
        double summitB = .82 * Gaussian(q.U - .32, q.V + .20, .28, .36);
        double summitC = .56 * Gaussian((q.U + .04) * scale, (q.V + .38) * scale, .64, .58);
        double valley = -.45 * Gaussian(q.U, q.V, .24, .20);
        double weathering = .10 * (Noise(q.U * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, q.V * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, seed, s, 22) - .5);
        return (p.MacroWeight * (summitA + summitB + valley - .28)) + (p.MesoWeight * (summitC - .10)) + (p.DetailWeight * weathering);
    }

    private static double Plateau(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    {
        var q = Local(x, z, seed, s, 30, p.MacroWavelengthBlocks);
        double r = Math.Pow(Math.Abs(q.U), 4) + Math.Pow(Math.Abs(q.V / .76), 4);
        double top = 1 - Smooth(.56, .86, r);
        double escarpment = Math.Exp(-105 * (r - .73) * (r - .73));
        double mesoScale = p.MacroWavelengthBlocks / p.MesoWavelengthBlocks;
        double steppedEdge = .08 * (Noise(q.U * mesoScale, q.V * mesoScale, seed, s, 32) - .5) * escarpment;
        double interior = (Noise(q.U * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, q.V * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, seed, s, 31) - .5) * top;
        double foreland = .14 * (Noise(q.U * mesoScale, q.V * mesoScale, seed, s, 33) - .5);
        double rockTexture = .05 * (Noise(q.U * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, q.V * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, seed, s, 34) - .5);
        return (p.MacroWeight * (top - .36)) + (p.MesoWeight * (.46 * escarpment + steppedEdge + foreland - .11)) + (p.DetailWeight * ((.06 * interior) + rockTexture));
    }

    private static double Basin(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    {
        var q = Local(x, z, seed, s, 40, p.MacroWavelengthBlocks);
        double r = Math.Sqrt((q.U * q.U) + ((q.V / .67) * (q.V / .67)));
        double closedBowl = -Math.Exp(-3.7 * r * r);
        double enclosingRim = .58 * Math.Exp(-80 * (r - .77) * (r - .77));
        double mesoScale = p.MacroWavelengthBlocks / p.MesoWavelengthBlocks;
        double rimUndulation = .08 * (Noise(q.U * mesoScale, q.V * mesoScale, seed, s, 42) - .5) * enclosingRim;
        double floor = .06 * (Noise(q.U * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, q.V * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, seed, s, 41) - .5) * Math.Exp(-4 * r * r);
        return (p.MacroWeight * (closedBowl + .29)) + (p.MesoWeight * (enclosingRim + rimUndulation - .08)) + (p.DetailWeight * floor);
    }

    private static double Plain(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    {
        var q = Local(x, z, seed, s, 50, p.MacroWavelengthBlocks);
        double broad = Noise(q.U * 1.3, q.V * 1.3, seed, s, 51) - .5;
        double mesoScale = p.MacroWavelengthBlocks / p.MesoWavelengthBlocks;
        double gentleDrainage = Noise(q.U * mesoScale, q.V * mesoScale, seed, s, 52) - .5;
        double fine = Noise(q.U * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, q.V * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, seed, s, 53) - .5;
        return (p.MacroWeight * .38 * broad) + (p.MesoWeight * .20 * gentleDrainage) + (p.DetailWeight * .10 * fine);
    }

    private static double Volcanoes(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    {
        var q = Local(x, z, seed, s, 60, p.MacroWavelengthBlocks);
        double mainCone = Cone(q.U + .20, q.V - .10, .45);
        double mesoScale = p.MacroWavelengthBlocks / p.MesoWavelengthBlocks;
        double secondaryCone = .58 * Cone((q.U - .36) * mesoScale, (q.V + .24) * mesoScale, .25);
        double r = Math.Sqrt((q.U + .20) * (q.U + .20) + (q.V - .10) * (q.V - .10));
        double caldera = -.48 * Math.Exp(-125 * r * r) + .18 * Math.Exp(-210 * (r - .16) * (r - .16));
        double rift = .22 * (Noise(q.U * mesoScale, q.V * mesoScale, seed, s, 62) - .5);
        double lava = .26 * (Noise(q.U * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, q.V * p.MacroWavelengthBlocks / p.DetailWavelengthBlocks, seed, s, 63) - .5);
        return (p.MacroWeight * ((1.35 * mainCone) - .32)) + (p.MesoWeight * (secondaryCone + caldera + rift - .08)) + (p.DetailWeight * lava);
    }
    private static (double U, double V) Local(double x, double z, int seed, StableId s, ulong c, double scale) { double angle = Phase(seed, s, c); double cos = Math.Cos(angle); double sin = Math.Sin(angle); double ox = Signed(seed, s, c + 1) * scale * .45; double oz = Signed(seed, s, c + 2) * scale * .45; double dx = x - ox; double dz = z - oz; return (((cos * dx) + (sin * dz)) / scale, ((-sin * dx) + (cos * dz)) / scale); }
    private static double Cone(double u, double v, double radius) { double t = Math.Max(0, 1 - (Math.Sqrt((u * u) + (v * v)) / radius)); return t * t; }
    private static double Gaussian(double u, double v, double sx, double sy) => Math.Exp(-((u * u / (sx * sx)) + (v * v / (sy * sy))));
    // Hash-interpolated values are non-periodic: the integer lattice is only a lookup domain and is not a storage tile.
    private static double Noise(double u, double v, int seed, StableId s, ulong c)
    {
        if (!double.IsFinite(u) || !double.IsFinite(v) ||
            Math.Abs(u) > long.MaxValue - 2d || Math.Abs(v) > long.MaxValue - 2d)
        {
            throw new InvalidOperationException("Qualified landscape coordinates exceeded the noise lattice range.");
        }

        long ix = (long)Math.Floor(u); long iz = (long)Math.Floor(v);
        double fx = Fade(u - ix); double fz = Fade(v - iz);
        double a = Corner(ix, iz, seed, s, c); double b = Corner(ix + 1, iz, seed, s, c);
        double d = Corner(ix, iz + 1, seed, s, c); double e = Corner(ix + 1, iz + 1, seed, s, c);
        return Lerp(Lerp(a, b, fx), Lerp(d, e, fx), fz);
    }
    private static double Corner(long x, long z, int seed, StableId s, ulong c)
    {
        ulong mixed = unchecked(((ulong)x * 0x9E3779B185EBCA87UL) ^ ((ulong)z * 0xC2B2AE3D27D4EB4FUL) ^ c);
        return Unit(seed, s, mixed);
    }
    private static double Fade(double value) => value * value * (3 - (2 * value));
    private static double Lerp(double left, double right, double t) => left + ((right - left) * t);
    private static double Phase(int seed, StableId s, ulong c) => Math.Tau * Unit(seed, s, c);
    private static double Signed(int seed, StableId s, ulong c) => (2 * Unit(seed, s, c)) - 1;
    private static double Unit(int seed, StableId s, ulong c) => (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, s, c) >> 11) * (1.0 / (1UL << 53));
    private static double Smooth(double a, double b, double x) { double t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - (2 * t)); }
}
