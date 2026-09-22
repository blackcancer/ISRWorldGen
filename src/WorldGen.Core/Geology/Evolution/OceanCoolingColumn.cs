namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Constant-property, finite-thickness conductive plate. SI inputs, time in Myr.
/// This describes the mantle thermal column, NOT growth of the basaltic crust.
/// Defaults are an explicit idealized prior, not an inversion of Earth observations.
/// No crustal buoyancy, slab pull, dynamic topography or erosion is added here.
/// </summary>
public sealed record OceanCoolingParameters
{
    public double ThicknessMetres { get; }
    public double DiffusivityM2PerSecond { get; }
    public double ThermalExpansionPerKelvin { get; }
    public double TemperatureContrastKelvin { get; }
    public double MantleDensityKgM3 { get; }
    public double WaterDensityKgM3 { get; }
    public OceanCoolingParameters(double thicknessMetres = 125000,
        double diffusivityM2PerSecond = 1e-6, double thermalExpansionPerKelvin = 3.2e-5,
        double temperatureContrastKelvin = 1350, double mantleDensityKgM3 = 3300,
        double waterDensityKgM3 = 1030)
    {
        if (new[] { thicknessMetres, diffusivityM2PerSecond, thermalExpansionPerKelvin,
            temperatureContrastKelvin, mantleDensityKgM3 }.Any(v => !double.IsFinite(v) || v <= 0)
            || !double.IsFinite(waterDensityKgM3) || waterDensityKgM3 < 0
            || waterDensityKgM3 >= mantleDensityKgM3
            || thermalExpansionPerKelvin * temperatureContrastKelvin >= 1
            || !double.IsFinite(thicknessMetres * thicknessMetres) || thicknessMetres * thicknessMetres == 0)
            throw new ArgumentException("Invalid finite-plate thermal parameters or Boussinesq domain.");
        ThicknessMetres = thicknessMetres; DiffusivityM2PerSecond = diffusivityM2PerSecond;
        ThermalExpansionPerKelvin = thermalExpansionPerKelvin;
        TemperatureContrastKelvin = temperatureContrastKelvin;
        MantleDensityKgM3 = mantleDensityKgM3; WaterDensityKgM3 = waterDensityKgM3;
    }
}

public readonly record struct OceanCoolingResponse(double AgeMyr, double FourierNumber,
    double IntegratedTemperatureDeficitMetres, double DensityExcessKgPerSquareMetre,
    double WaterLoadedSubsidenceMetres, double TruncationBoundMetres, string Evaluation);

public static class OceanCoolingColumn
{
    public const string AlgorithmId = "finite-plate-integrated-thermal-deficit-v1";
    public const double SecondsPerMyr = 365.25 * 86400 * 1e6;
    private const double SeriesTolerance = 1e-14;

    /// <summary>
    /// Surface is cold, base is held hot; initially the column is hot.
    /// F = 1/2 - 4/pi² sum_{n odd} exp(-pi²*n²*Fo)/n².
    /// Density anomaly = rho*alpha*DeltaT*L*F. Positive output is subsidence
    /// relative to the hot column, not an absolute seabed elevation.
    /// Never subtract this from a height already compensated for cooling.
    /// </summary>
    public static OceanCoolingResponse Evaluate(double ageMyr, OceanCoolingParameters? parameters = null)
    {
        var p = parameters ?? new OceanCoolingParameters();
        if (!double.IsFinite(ageMyr) || ageMyr < 0)
            throw new ArgumentOutOfRangeException(nameof(ageMyr));
        double fo = p.DiffusivityM2PerSecond * (ageMyr * SecondsPerMyr) /
            (p.ThicknessMetres * p.ThicknessMetres);
        if (!double.IsFinite(fo) || (ageMyr > 0 && fo == 0))
            throw new ArithmeticException("Unrepresentable thermal Fourier number.");
        double f = 0, bound = 0; string method = "HOT_COLUMN";
        if (fo > 0 && fo < 1e-4)
        {
            // Image expansion of the same finite-plate solution. The omitted
            // correction is bounded by its first alternating image term.
            f = 2 * Math.Sqrt(fo / Math.PI);
            bound = 4 * Math.Sqrt(fo / Math.PI) * Math.Exp(-1 / (4 * fo));
            method = "SMALL_FOURIER_IMAGE_BOUND";
        }
        else if (fo >= 1e-4)
        {
            double a = Math.PI * Math.PI * fo, sum = 0;
            bool converged = false;
            for (int k = 0; k < 4096; k++)
            {
                double n = 2 * k + 1;
                sum += Math.Exp(-a * n * n) / (n * n);
                double next = n + 2;
                double ratio = Math.Exp(-4 * a * (next + 1));
                bound = 4 / (Math.PI * Math.PI) * Math.Exp(-a * next * next) /
                    (next * next * (1 - ratio));
                if (bound <= SeriesTolerance) { converged = true; break; }
            }
            if (!converged) throw new ArithmeticException("Finite-plate thermal series did not converge.");
            f = .5 - 4 / (Math.PI * Math.PI) * sum;
            method = "ODD_EIGENMODES_WITH_TAIL_BOUND";
        }
        if (!double.IsFinite(f) || f < 0 || f > .5)
            throw new ArithmeticException("Thermal column outside physical bounds; no clamp.");
        double deficit = p.ThicknessMetres * f;
        double mass = p.MantleDensityKgM3 * p.ThermalExpansionPerKelvin *
            p.TemperatureContrastKelvin * deficit;
        double subsidence = mass / (p.MantleDensityKgM3 - p.WaterDensityKgM3);
        if (!double.IsFinite(mass) || !double.IsFinite(subsidence))
            throw new ArithmeticException("Thermal response overflow.");
        return new(ageMyr, fo, deficit, mass, subsidence, p.ThicknessMetres * bound, method);
    }

    /// <summary>
    /// Average the thermal response of known cohorts, not the response at their
    /// mean age: these differ for a nonlinear cooling law. Weights are positive
    /// carrier amounts in any common unit; no reclassification from sea level.
    /// This utility does not reconstruct a missing age distribution.
    /// </summary>
    public static double MixedDensityExcess(IReadOnlyList<double> agesMyr,
        IReadOnlyList<double> carrierWeights, OceanCoolingParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(agesMyr); ArgumentNullException.ThrowIfNull(carrierWeights);
        if (agesMyr.Count == 0 || agesMyr.Count != carrierWeights.Count
            || carrierWeights.Any(w => !double.IsFinite(w) || w < 0)
            || agesMyr.Any(t => !double.IsFinite(t) || t < 0))
            throw new ArgumentException("Invalid thermal cohort mixture.");
        double max = carrierWeights.Max();
        if (!(max > 0)) throw new ArgumentException("Empty thermal carrier mixture.");
        double sum = 0, mass = 0;
        for (int i = 0; i < agesMyr.Count; i++)
        {
            double w = carrierWeights[i] / max;
            sum += w; mass += w * Evaluate(agesMyr[i], parameters).DensityExcessKgPerSquareMetre;
        }
        double result = mass / sum;
        if (!double.IsFinite(result)) throw new ArithmeticException("Thermal mixture overflow.");
        return result;
    }
}
