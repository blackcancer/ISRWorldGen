using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Landscapes;

public enum LandscapeFamily
{
    RuggedRanges = 0,
    OldMassifs = 1,
    Plateaus = 2,
    SedimentaryBasins = 3,
    Plains = 4,
    VolcanicDomains = 5,
}

/// <summary>
/// Dimensionless procedural signature plus explicit wavelengths in world blocks. These parameters describe
/// generated morphology; they are not measurements of real geology.
/// </summary>
public sealed class LandscapeFamilyProfile
{
    internal LandscapeFamilyProfile(
        LandscapeFamily family,
        double macroWavelengthBlocks,
        double mesoWavelengthBlocks,
        double detailWavelengthBlocks,
        double reliefAmplitudeNormalized,
        double macroWeight,
        double mesoWeight,
        double detailWeight)
    {
        Family = family;
        MacroWavelengthBlocks = macroWavelengthBlocks;
        MesoWavelengthBlocks = mesoWavelengthBlocks;
        DetailWavelengthBlocks = detailWavelengthBlocks;
        ReliefAmplitudeNormalized = reliefAmplitudeNormalized;
        MacroWeight = macroWeight;
        MesoWeight = mesoWeight;
        DetailWeight = detailWeight;
        ParameterChecksum = ComputeChecksum(this);
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

    private static Hash256 ComputeChecksum(LandscapeFamilyProfile profile)
    {
        string canonical = string.Join('|', new[]
        {
            "ISRW-LANDSCAPE-FAMILY-V1",
            ((int)profile.Family).ToString(CultureInfo.InvariantCulture),
            Bits(profile.MacroWavelengthBlocks),
            Bits(profile.MesoWavelengthBlocks),
            Bits(profile.DetailWavelengthBlocks),
            Bits(profile.ReliefAmplitudeNormalized),
            Bits(profile.MacroWeight),
            Bits(profile.MesoWeight),
            Bits(profile.DetailWeight),
        });
        return Hash256.Compute(Encoding.UTF8.GetBytes(canonical));
    }

    private static string Bits(double value) =>
        BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture);
}

public static class LandscapeFamilyCatalog
{
    private static readonly ReadOnlyCollection<LandscapeFamilyProfile> Catalog = Array.AsReadOnly(
    new[]
    {
        new LandscapeFamilyProfile(LandscapeFamily.RuggedRanges, 12_000, 3_000, 700, 0.28, 0.25, 0.35, 0.40),
        new LandscapeFamilyProfile(LandscapeFamily.OldMassifs, 18_000, 5_500, 1_400, 0.20, 0.50, 0.35, 0.15),
        new LandscapeFamilyProfile(LandscapeFamily.Plateaus, 24_000, 7_000, 1_600, 0.18, 0.50, 0.30, 0.20),
        new LandscapeFamilyProfile(LandscapeFamily.SedimentaryBasins, 28_000, 9_000, 2_200, 0.16, 0.55, 0.30, 0.15),
        new LandscapeFamilyProfile(LandscapeFamily.Plains, 42_000, 12_000, 3_200, 0.07, 0.65, 0.25, 0.10),
        new LandscapeFamilyProfile(LandscapeFamily.VolcanicDomains, 20_000, 4_500, 900, 0.26, 0.25, 0.45, 0.30),
    });

    public static ReadOnlyCollection<LandscapeFamilyProfile> Profiles => Catalog;

    public static LandscapeFamilyProfile Get(LandscapeFamily family)
    {
        if (!Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown landscape family.");
        }

        return Catalog[(int)family];
    }
}

/// <summary>Pure, global-coordinate morphology sampler with no mutable random sequence or storage-tile input.</summary>
public static class LandscapeSignatureSampler
{
    public static double Sample(
        LandscapeFamilyProfile profile,
        double xBlocks,
        double zBlocks,
        int seed,
        ulong streamOrdinal)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!double.IsFinite(xBlocks) || !double.IsFinite(zBlocks))
        {
            throw new ArgumentOutOfRangeException(nameof(xBlocks), "Landscape coordinates must be finite.");
        }

        StableId stream = StableId.Derive(RandomDomain.Geology, StableId.Zero, streamOrdinal);
        double macro = Wave(xBlocks, zBlocks, profile.MacroWavelengthBlocks, Phase(seed, stream, 0), Phase(seed, stream, 1));
        double meso = Wave(xBlocks, zBlocks, profile.MesoWavelengthBlocks, Phase(seed, stream, 2), Phase(seed, stream, 3));
        double detail = Wave(xBlocks, zBlocks, profile.DetailWavelengthBlocks, Phase(seed, stream, 4), Phase(seed, stream, 5));

        return profile.Family switch
        {
            LandscapeFamily.RuggedRanges =>
                (profile.MacroWeight * Ridge(macro)) +
                (profile.MesoWeight * Ridge(meso)) +
                (profile.DetailWeight * Ridge(detail)),
            LandscapeFamily.OldMassifs =>
                (profile.MacroWeight * macro) +
                (profile.MesoWeight * meso) +
                (profile.DetailWeight * detail),
            LandscapeFamily.Plateaus =>
                (profile.MacroWeight * Math.Tanh(3 * macro)) +
                (profile.MesoWeight * Math.Tanh(4 * meso)) +
                (profile.DetailWeight * detail),
            LandscapeFamily.SedimentaryBasins => -(
                (profile.MacroWeight * Basin(macro)) +
                (profile.MesoWeight * Basin(meso)) +
                (profile.DetailWeight * Basin(detail))),
            LandscapeFamily.Plains =>
                (profile.MacroWeight * macro) +
                (profile.MesoWeight * meso) +
                (profile.DetailWeight * detail),
            LandscapeFamily.VolcanicDomains =>
                (profile.MacroWeight * Cone(macro)) +
                (profile.MesoWeight * Cone(meso)) +
                (profile.DetailWeight * Ridge(detail)),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile.Family, "Unknown landscape family."),
        };
    }

    private static double Wave(double x, double z, double wavelength, double phaseX, double phaseZ) =>
        Math.Sin((Math.Tau * x / wavelength) + phaseX) *
        Math.Cos((Math.Tau * z / wavelength) + phaseZ);

    private static double Ridge(double value) => (2 * Math.Abs(value)) - 1;

    private static double Basin(double value) => (value + 1) / 2;

    private static double Cone(double value)
    {
        double positive = (value + 1) / 2;
        return (2 * positive * positive * positive) - 1;
    }

    private static double Phase(int seed, StableId stream, ulong counter) =>
        Math.Tau * ((StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, stream, counter) >> 11) *
                    (1.0 / (1UL << 53)));
}
