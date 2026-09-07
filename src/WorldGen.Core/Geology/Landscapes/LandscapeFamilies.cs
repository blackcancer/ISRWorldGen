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
    internal LandscapeFamilyProfile(LandscapeFamily family, double macro, double meso, double detail, double amplitude, double macroWeight, double mesoWeight, double detailWeight)
    { Family = family; MacroWavelengthBlocks = macro; MesoWavelengthBlocks = meso; DetailWavelengthBlocks = detail; ReliefAmplitudeNormalized = amplitude; MacroWeight = macroWeight; MesoWeight = mesoWeight; DetailWeight = detailWeight; ParameterChecksum = Checksum(this); }
    public LandscapeFamily Family { get; }
    public double MacroWavelengthBlocks { get; }
    public double MesoWavelengthBlocks { get; }
    public double DetailWavelengthBlocks { get; }
    public double ReliefAmplitudeNormalized { get; }
    public double MacroWeight { get; }
    public double MesoWeight { get; }
    public double DetailWeight { get; }
    public Hash256 ParameterChecksum { get; }
    private static Hash256 Checksum(LandscapeFamilyProfile p) => Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('|', new[] { "ISRW-LANDSCAPE-FAMILY-V2", ((int)p.Family).ToString(CultureInfo.InvariantCulture), Bits(p.MacroWavelengthBlocks), Bits(p.MesoWavelengthBlocks), Bits(p.DetailWavelengthBlocks), Bits(p.ReliefAmplitudeNormalized), Bits(p.MacroWeight), Bits(p.MesoWeight), Bits(p.DetailWeight) })));
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
    public static double Sample(LandscapeFamilyProfile p, double x, double z, int seed, ulong streamOrdinal)
        => Sample(p, x, z, seed, streamOrdinal, 0, 0);

    /// <summary>Samples relative to the owning site so every finite-world cell receives its local morphology.</summary>
    public static double Sample(LandscapeFamilyProfile p, double x, double z, int seed, ulong streamOrdinal, long anchorX, long anchorZ)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (!double.IsFinite(x) || !double.IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(x), "Landscape coordinates must be finite.");
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
    { var q = Local(x, z, seed, s, 0, p.MacroWavelengthBlocks); double backbone = Math.Exp(-5.8 * q.V * q.V) * (.76 + (.24 * Noise(q.U * 2.7, q.V * 2.7, seed, s, 10))); double spurV = q.V - (.34 * Math.Sin((q.U * 3.4) + Phase(seed, s, 11))); double spurs = .45 * Math.Exp(-12 * spurV * spurV); double serration = .24 * (Noise(q.U * 9, q.V * 9, seed, s, 12) - .5); return Cap((backbone + spurs + serration) * 1.35 - .72); }
    private static double Massifs(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    { var q = Local(x, z, seed, s, 20, p.MacroWavelengthBlocks); double a = Gaussian(q.U + .18, q.V - .11, 1, .78); double b = .62 * Gaussian(q.U - .36, q.V + .27, .62, .55); return Cap((a + b + (.16 * (Noise(q.U * 3.2, q.V * 3.2, seed, s, 21) - .5))) * 1.18 - .78); }
    private static double Plateau(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    { var q = Local(x, z, seed, s, 30, p.MacroWavelengthBlocks); double r = Math.Pow(Math.Abs(q.U), 4) + Math.Pow(Math.Abs(q.V / .73), 4); double top = 1 - Smooth(.62, 1.06, r); double escarpment = Math.Exp(-42 * (r - 1) * (r - 1)); double interior = .035 * (Noise(q.U * 5, q.V * 5, seed, s, 31) - .5) * Smooth(.25, .85, top); return Cap(-.66 + (1.24 * top) + (.14 * escarpment) + interior); }
    private static double Basin(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    { var q = Local(x, z, seed, s, 40, p.MacroWavelengthBlocks); double r = Math.Sqrt((q.U * q.U) + ((q.V / .62) * (q.V / .62))); double bowl = -Math.Exp(-2.9 * r * r); double rim = .30 * Math.Exp(-35 * (r - .92) * (r - .92)); double strata = .07 * Math.Sin((18 * r) + Phase(seed, s, 41)) * Math.Exp(-2 * r * r); return Cap((bowl + rim + strata) * .98); }
    private static double Plain(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    { var q = Local(x, z, seed, s, 50, p.MacroWavelengthBlocks); return Cap((.70 * (Noise(q.U * 1.1, q.V * 1.1, seed, s, 51) - .5)) + (.30 * (Noise(q.U * 3.7, q.V * 3.7, seed, s, 52) - .5))); }
    private static double Volcanoes(double x, double z, int seed, StableId s, LandscapeFamilyProfile p)
    { var q = Local(x, z, seed, s, 60, p.MacroWavelengthBlocks); double cone = Cone(q.U + .23, q.V - .08, .48); double secondary = .55 * Cone(q.U - .39, q.V + .29, .27); double r = Math.Sqrt((q.U - .23) * (q.U - .23) + (q.V + .08) * (q.V + .08)); double caldera = (-.52 * Math.Exp(-90 * r * r)) + (.13 * Math.Exp(-160 * (r - .19) * (r - .19))); return Cap((cone + secondary + caldera) * 1.18 - .68); }
    private static (double U, double V) Local(double x, double z, int seed, StableId s, ulong c, double scale) { double angle = Phase(seed, s, c); double cos = Math.Cos(angle); double sin = Math.Sin(angle); double ox = Signed(seed, s, c + 1) * scale * .45; double oz = Signed(seed, s, c + 2) * scale * .45; double dx = x - ox; double dz = z - oz; return (((cos * dx) + (sin * dz)) / scale, ((-sin * dx) + (cos * dz)) / scale); }
    private static double Cone(double u, double v, double radius) { double t = Math.Max(0, 1 - (Math.Sqrt((u * u) + (v * v)) / radius)); return t * t; }
    private static double Gaussian(double u, double v, double sx, double sy) => Math.Exp(-((u * u / (sx * sx)) + (v * v / (sy * sy))));
    // Hash-interpolated values are non-periodic: the integer lattice is only a lookup domain and is not a storage tile.
    private static double Noise(double u, double v, int seed, StableId s, ulong c)
    {
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
    private static double Cap(double value) => Math.Clamp(value, -1, 1);
}
