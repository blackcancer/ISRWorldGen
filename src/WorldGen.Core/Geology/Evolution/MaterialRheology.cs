using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

public sealed record SheetRheologyOptions
{
    public const string AlgorithmId = "transported-composition-age-viscous-sheet-v1";
    public int MechanicalSide { get; }
    public double UpdateInterval { get; }
    public bool HomogeneousControl { get; }
    public SheetRheologyOptions(int mechanicalSide = 128, double updateInterval = 1, bool homogeneousControl = false)
    {
        if (mechanicalSide < 16 || mechanicalSide > 128 || (mechanicalSide & (mechanicalSide - 1)) != 0 ||
            !double.IsFinite(updateInterval) || updateInterval < .125 || updateInterval > 2)
            throw new ArgumentException("Unsupported reduced sheet resolution or mechanical interval.");
        MechanicalSide = mechanicalSide; UpdateInterval = updateInterval; HomogeneousControl = homogeneousControl;
    }
}

public sealed record SheetSolveReceipt(double Time, int MechanicalSide, int Iterations, double RelativeResidual,
    double Work, double Dissipation, double MeanVelocityError, double MinimumViscosity, double MaximumViscosity);

public sealed class MaterialDeformationFrame
{
    public int MechanicalSide { get; }
    public ThinSheetSolution Solution { get; }
    public ReadOnlyCollection<double> RelativeViscosity { get; }
    public ReadOnlyCollection<double> East { get; }
    public ReadOnlyCollection<double> South { get; }
    public ReadOnlyCollection<double> Divergence { get; }
    public ReadOnlyCollection<double> EngineeringShear { get; }
    internal readonly double[] EastData, SouthData, DivergenceData;
    internal MaterialDeformationFrame(int side, ThinSheetSolution solution, ReadOnlyCollection<double> viscosity,
        double[] east, double[] south, double[] divergence, double[] shear)
    {
        MechanicalSide = side; Solution = solution; RelativeViscosity = viscosity;
        EastData = east; SouthData = south; DivergenceData = divergence;
        East = Array.AsReadOnly(east); South = Array.AsReadOnly(south);
        Divergence = Array.AsReadOnly(divergence); EngineeringShear = Array.AsReadOnly(shear);
    }
}

/// <summary>
/// Explicit reduced constitutive PRIOR: a compositional harmonic mixture and a
/// bounded age-dependent oceanic viscosity. NOT calibrated creep laws, weight
/// ranking, a mantle thermal solver, or inherited-fault damage. Age and column
/// amounts are those transported by the material solver, not distance to a ridge.
/// </summary>
public static class MaterialRheology
{
    public static double RelativeViscosity(double continentalKm, double oceanicKm, double oceanAge)
    {
        if (!double.IsFinite(continentalKm) || continentalKm < 0 || !double.IsFinite(oceanicKm) || oceanicKm < 0 ||
            continentalKm + oceanicKm <= 0 || !double.IsFinite(continentalKm + oceanicKm) || !double.IsFinite(oceanAge) || oceanAge < 0)
            throw new ArgumentException("Invalid material rheology column.");
        double fraction = continentalKm / (continentalKm + oceanicKm);
        double continent = 2 + 2 * continentalKm / (continentalKm + 35);
        double ocean = .25 + 1.75 * oceanAge / (oceanAge + 40);
        return 1 / (fraction / continent + (1 - fraction) / ocean);
    }

    public static MaterialDeformationFrame Solve(MaterialPlateCohorts state, IReadOnlyList<TectonicPlate> plates,
        double dx, double dz, double couplingLength, SheetRheologyOptions options, ThinSheetSolution? warmStart = null)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(plates); ArgumentNullException.ThrowIfNull(options);
        int n = state.Side, m = Math.Min(n, options.MechanicalSide), ratio = n / m;
        if (n % m != 0 || plates.Count != state.PlateCount || plates.Where((p, i) => p.Id != i || !double.IsFinite(p.Vx) || !double.IsFinite(p.Vz)).Any() ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0) throw new ArgumentException("Incompatible mechanical/material grid or plate table.");
        int count = m * m;
        var c = new double[count]; var o = new double[count]; var moment = new double[count];
        var px = new double[count]; var pz = new double[count];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, k = (z / ratio) * m + x / ratio;
            for (int p = 0; p < plates.Count; p++)
            {
                double ci = state.Value(p, 0, i), oi = state.Value(p, 1, i);
                c[k] += ci; o[k] += oi; moment[k] += state.Value(p, 2, i);
                px[k] += (ci + oi) * plates[p].Vx; pz[k] += (ci + oi) * plates[p].Vz;
            }
        }
        var viscosity = new double[count]; var east = new double[count]; var south = new double[count];
        for (int i = 0; i < count; i++)
        {
            double total = c[i] + o[i];
            if (!(total > 0) || !double.IsFinite(total)) throw new ArithmeticException("No material forcing on mechanical cell.");
            px[i] /= total; pz[i] /= total;
            viscosity[i] = options.HomogeneousControl ? 1 : RelativeViscosity(c[i] / (ratio * ratio), o[i] / (ratio * ratio), o[i] > 0 ? moment[i] / o[i] : 0);
        }
        for (int z = 0; z < m; z++) for (int x = 0; x < m; x++)
        {
            int i = z * m + x;
            east[i] = .5 * (px[i] + px[z * m + (x + 1) % m]);
            south[i] = .5 * (pz[i] + pz[((z + 1) % m) * m + x]);
        }
        ThinSheetSolution solved = ThinSheetDeformation.Solve(east, south, viscosity, m, dx * ratio, dz * ratio, couplingLength, warmStart: warmStart);
        double[] fe = new double[n * n], fs = new double[n * n], divergence = new double[n * n], shear = new double[n * n];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            // MAC face coordinates, not cell-centered interpolation. The
            // fine material resolution is NOT claimed as the mechanical one.
            fe[i] = Sample(solved.East, (x + 1d) / ratio - 1, (z + .5) / ratio - .5);
            fs[i] = Sample(solved.South, (x + .5) / ratio - .5, (z + 1d) / ratio - 1);
        }
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, w = z * n + (x + n - 1) % n, no = ((z + n - 1) % n) * n + x;
            int e = z * n + (x + 1) % n, s = ((z + 1) % n) * n + x;
            divergence[i] = (fe[i] - fe[w]) / dx + (fs[i] - fs[no]) / dz;
            shear[i] = (fe[s] - fe[i]) / dz + (fs[e] - fs[i]) / dx;
        }
        return new(m, solved, Array.AsReadOnly(viscosity), fe, fs, divergence, shear);
        double Sample(IReadOnlyList<double> a, double x, double z)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z); double tx = x - ix, tz = z - iz;
            int x0 = (ix % m + m) % m, x1 = (x0 + 1) % m, z0 = (iz % m + m) % m, z1 = (z0 + 1) % m;
            return (1 - tz) * ((1 - tx) * a[z0 * m + x0] + tx * a[z0 * m + x1]) + tz * ((1 - tx) * a[z1 * m + x0] + tx * a[z1 * m + x1]);
        }
    }

    public static double StableStep(IReadOnlyList<double> east, IReadOnlyList<double> south, int n, double dx, double dz)
    {
        ArgumentNullException.ThrowIfNull(east); ArgumentNullException.ThrowIfNull(south);
        if (n < 2 || n > 512 || !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 ||
            east.Count != n * n || south.Count != n * n || east.Any(v => !double.IsFinite(v)) || south.Any(v => !double.IsFinite(v))) throw new ArgumentException("Invalid face geometry.");
        double maximum = 0;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, w = z * n + (x + n - 1) % n, no = ((z + n - 1) % n) * n + x;
            double outgoing = (Math.Max(east[i], 0) + Math.Max(-east[w], 0)) / dx + (Math.Max(south[i], 0) + Math.Max(-south[no], 0)) / dz;
            if (!double.IsFinite(outgoing)) throw new ArithmeticException("Nonfinite mechanical face rate.");
            maximum = Math.Max(maximum, outgoing);
        }
        return maximum > 0 ? .35 / maximum : 1;
    }
}
