using System.Collections.ObjectModel;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Explicit effective mobility in reference²/(km² model Myr).
/// This is a basal-drag normalization, NOT a measured Earth viscosity.
/// Fixed 300 km compensation and 120 km thermal mantle are reduced-model priors.</summary>
public sealed record LithostaticReliefOptions
{
    public double Mobility { get; }
    public bool FinitePlateCooling { get; }
    public LithostaticReliefOptions(double mobility = 50000, bool finitePlateCooling = true)
    {
        if (!double.IsFinite(mobility) || mobility < 0 || mobility > 200000)
            throw new ArgumentOutOfRangeException(nameof(mobility));
        Mobility = mobility; FinitePlateCooling = finitePlateCooling;
    }
}

public readonly record struct LithostaticColumn(double ElevationKm, double PotentialKm2,
    double ColumnMassEquivalentKm, double ThermalOffsetKm);

/// <summary>
/// A shared density column supplies both solid elevation and the depth-integrated
/// pressure potential driving horizontal flow. Not erosion or a height filter.
/// Cold oceans, young spreading material, thickened continents and water loading
/// are evaluated with one datum. Cooling uses the transported mean ocean age:
/// unresolved within-cell thermal age distributions remain an explicit limitation.
/// </summary>
public static class LithostaticRelief
{
    public const string AlgorithmId = "shared-density-column-pressure-feedback-finite-plate-v1";
    public const double CompensationKm = 300, MantleThicknessKm = 120;
    public const double MantleDensity = 3300, ContinentalDensity = 2800;
    public const double OceanDensity = 2900, WaterDensity = 1030;
    public const double Alpha = 3e-5, MantleTemperature = 1330;
    public const double DiffusivityKm2PerMyr = 31.5576, ReferenceDeficitKm = 4.95;
    public const double RidgeThermalOffsetKm = 2.30;

    /// <summary>Integrated cooling contraction of a slab initially at Tm, with
    /// surface 0 and basal Tm held fixed. The short-time branch is the asymptotic
    /// half-space solution where the basal reflection is below roundoff.
    /// Older ages use the converged odd Fourier series, not an unbounded sqrt.</summary>
    public static double CoolingContractionKm(double ageMyr)
    {
        if (!double.IsFinite(ageMyr) || ageMyr < 0)
            throw new ArgumentOutOfRangeException(nameof(ageMyr));
        double fourier = DiffusivityKm2PerMyr * (ageMyr / MantleThicknessKm) / MantleThicknessKm;
        double deficit;
        if (fourier < .005)
            deficit = 2 * Math.Sqrt(DiffusivityKm2PerMyr * ageMyr / Math.PI);
        else
        {
            double sum = 0;
            for (int j = 1; j < 65; j += 2)
                sum += Math.Exp(-j * j * Math.PI * Math.PI * fourier) / (j * j);
            deficit = MantleThicknessKm * (.5 - 4 * sum / (Math.PI * Math.PI));
        }
        double result = Alpha * MantleTemperature * deficit;
        if (!double.IsFinite(result) || result < 0 ||
            result > Alpha * MantleTemperature * MantleThicknessKm / 2)
            throw new ArithmeticException("Invalid thermal column; no clipping.");
        return result;
    }

    public static LithostaticColumn Column(double c, double o, double age, bool finiteCooling = true)
    {
        if (!double.IsFinite(c) || c < 0 || !double.IsFinite(o) || o < 0 ||
            !(c + o > 0) || !double.IsFinite(c + o) || c + o > 150 ||
            !double.IsFinite(age) || age < 0)
            throw new ArgumentException("Unsupported finite material column.");
        double thermal = o / (c + o) * (finiteCooling
            ? RidgeThermalOffsetKm - CoolingContractionKm(age)
            : 1.6 - .25 * Math.Sqrt(age));
        double h = c * (1 - ContinentalDensity / MantleDensity)
            + o * (1 - OceanDensity / MantleDensity) - ReferenceDeficitKm + thermal;
        if (h < 0) h /= 1 - WaterDensity / MantleDensity;
        double mantleRho = 1 - thermal / MantleThicknessKm;
        double bottomThickness = CompensationKm + h - c - o - MantleThicknessKm;
        if (!(mantleRho > 0) || !(bottomThickness > 0))
            throw new ArithmeticException("Material reaches below compensation; no fallback.");
        // Integral rho/rho_m * (z + D) dz from -D to top, including water.
        double water = Math.Max(-h, 0);
        double potential = WaterDensity / MantleDensity * water * (CompensationKm - water / 2)
            + ContinentalDensity / MantleDensity * c * (CompensationKm + h - c / 2)
            + OceanDensity / MantleDensity * o * (CompensationKm + h - c - o / 2)
            + mantleRho * MantleThicknessKm * (CompensationKm + h - c - o - MantleThicknessKm / 2)
            + bottomThickness * bottomThickness / 2;
        double mass = WaterDensity / MantleDensity * water + ContinentalDensity / MantleDensity * c
            + OceanDensity / MantleDensity * o + mantleRho * MantleThicknessKm + bottomThickness;
        if (!double.IsFinite(potential) || Math.Abs(mass - (CompensationKm - ReferenceDeficitKm)) > 1e-10)
            throw new ArithmeticException("Column compensation does not balance.");
        return new(h, potential, mass, thermal);
    }

    /// <summary>Staggered negative pressure gradients; periodic total forcing is
    /// zero by construction. Mobility is explicit; no percentile scaling/clamp.</summary>
    public static (double[] East, double[] South) Forcing(IReadOnlyList<double> potential,
        int side, double dx, double dz, double mobility)
    {
        ArgumentNullException.ThrowIfNull(potential);
        if (side < 4 || side > 512 || potential.Count != side * side ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 ||
            !double.IsFinite(mobility) || mobility < 0 || mobility > 200000 ||
            potential.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Invalid lithostatic forcing geometry.");
        double[] east = new double[side * side], south = new double[east.Length];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x, e = z * side + (x + 1) % side, s = ((z + 1) % side) * side + x;
            east[i] = -mobility * ((potential[e] - potential[i]) / dx);
            south[i] = -mobility * ((potential[s] - potential[i]) / dz);
            if (!double.IsFinite(east[i]) || !double.IsFinite(south[i]))
                throw new ArithmeticException("Unrepresentable pressure force.");
        }
        return (east, south);
    }
}

public sealed record LithostaticReliefWorld(StrainWeakeningResult MechanicalHistory,
    ReadOnlyCollection<double> InitialElevationKm, ReadOnlyCollection<double> ElevationKm,
    ReadOnlyCollection<double> PotentialKm2, LithostaticReliefOptions Options, string Checksum)
{
    /// <summary>End-to-end world generation. This invokes the actual coupled
    /// material evolution, including pressure feedback at EACH mechanical solve.
    /// The reference generator and saved worlds are not silently replaced.</summary>
    public static LithostaticReliefWorld Generate(int seed, TectonicScalePlan scale,
        TectonicEvolutionSettings settings, ContinentalAssemblage assemblage,
        LithostaticReliefOptions options, int mechanicalSide = 128, PlateDrivingSchedule? driving = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var mechanics = new StrainWeakeningOptions(mechanicalSide: mechanicalSide,
            yieldOptions: new SheetYieldOptions(), gravity: options, driving: driving);
        var result = ViscoplasticWorld.Generate(seed, scale, settings, assemblage, mechanics);
        var first = Evaluate(result.History.Initial);
        var last = Evaluate(result.History.Final);
        string checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { LithostaticRelief.AlgorithmId, options,
                history = result.History.Checksum, initialHeight = first.Height, finalHeight = last.Height, potential = last.Potential })))).ToLowerInvariant();
        return new(result, Array.AsReadOnly(first.Height), Array.AsReadOnly(last.Height),
            Array.AsReadOnly(last.Potential), options, checksum);

        (double[] Height, double[] Potential) Evaluate(MaterialBoundSnapshot snapshot)
        {
            int count = snapshot.Side * snapshot.Side;
            double[] height = new double[count], potential = new double[count];
            for (int i = 0; i < count; i++)
            {
                var column = LithostaticRelief.Column(snapshot.ContinentalKm[i], snapshot.OceanicKm[i],
                    snapshot.OceanAge[i], options.FinitePlateCooling);
                height[i] = column.ElevationKm; potential[i] = column.PotentialKm2;
            }
            return (height, potential);
        }
    }

    public double SampleElevationKm(TectonicScalePlan scale, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(scale);
        var history = MechanicalHistory.History;
        if (Math.Abs(scale.ReferenceWidth - history.ReferenceWidth) > 1e-8 ||
            Math.Abs(scale.ReferenceLength - history.ReferenceLength) > 1e-8)
            throw new ArgumentException("Aspect change needs another full atlas.");
        var p = scale.ToReference(x, z);
        int side = history.Settings.Side;
        return WeakeningFrame.Sample(ElevationKm, side,
            p.X / scale.ReferenceWidth * side - .5, p.Z / scale.ReferenceLength * side - .5);
    }
}
