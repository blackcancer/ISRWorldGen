using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Explicit two-resolution coupling: finite-volume material cells and a
/// coarser mechanical MAC grid. Interpolated face velocities are NOT new solved
/// mechanical detail. The altitude field is always recomputed from transported
/// material on its own grid, never upscaled from a coarse heightmap.</summary>
public sealed class MembraneCoupling
{
    public const string AlgorithmId = "material-resistance-membrane-reference-compression-width-v2";
    /// <summary>Map a declared longitudinal attenuation length to the coefficient
    /// sqrt(mu/drag). In a uniform 35-km continental reference, the longitudinal
    /// equation is u-4*mu/drag*u_xx=u0, hence W=2*sqrt(mu/drag).
    /// This internal scale convention is not a measured Earth calibration.</summary>
    public static double CoefficientLengthForCompressionWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        return width / (2 * Math.Sqrt(LithosphereMembrane.MaterialResistance(35, 0, 0)));
    }
    public int MaterialSide { get; }
    public int SolveSide { get; }
    public double CouplingLength { get; }
    public MembraneSolution Solution { get; }
    public ReadOnlyCollection<double> Resistance { get; }
    public ReadOnlyCollection<double> East { get; }
    public ReadOnlyCollection<double> South { get; }
    public ReadOnlyCollection<double> Divergence { get; }
    public ReadOnlyCollection<double> CenterX { get; }
    public ReadOnlyCollection<double> CenterZ { get; }
    public double MaximumOutgoingRate { get; }

    private MembraneCoupling(int n, int coarse, double length, MembraneSolution solution,
        double[] resistance, double[] east, double[] south, double dx, double dz)
    {
        MaterialSide = n; SolveSide = coarse; CouplingLength = length; Solution = solution;
        Resistance = Array.AsReadOnly(resistance); East = Array.AsReadOnly(east); South = Array.AsReadOnly(south);
        var divergence = new double[n * n]; var x = new double[n * n]; var z = new double[n * n];
        double maximum = 0;
        for (int row = 0; row < n; row++) for (int col = 0; col < n; col++)
        {
            int i = row * n + col, w = row * n + (col + n - 1) % n, north = ((row + n - 1) % n) * n + col;
            divergence[i] = (east[i] - east[w]) / dx + (south[i] - south[north]) / dz;
            x[i] = .5 * (east[i] + east[w]); z[i] = .5 * (south[i] + south[north]);
            double rate = (Math.Max(east[i], 0) + Math.Max(-east[w], 0)) / dx
                + (Math.Max(south[i], 0) + Math.Max(-south[north], 0)) / dz;
            maximum = Math.Max(maximum, rate);
        }
        Divergence = Array.AsReadOnly(divergence); CenterX = Array.AsReadOnly(x); CenterZ = Array.AsReadOnly(z);
        MaximumOutgoingRate = maximum;
    }

    public static MembraneCoupling Evaluate(MaterialPlateCohorts state, IReadOnlyList<TectonicPlate> plates,
        double dx, double dz, double length, int maximumSolveSide = 128)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (maximumSolveSide is < 4 or > 512) throw new ArgumentOutOfRangeException(nameof(maximumSolveSide));
        int n = state.Side, coarse = Math.Min(n, maximumSolveSide);
        if (coarse < 4 || n % coarse != 0)
            throw new ArgumentException("Mechanical grid must divide the material grid exactly.");
        int factor = n / coarse;
        MaterialMotion raw = state.EvaluateMotion(plates, dx, dz, 0);
        double[][] fields = state.Aggregate(); int nc = coarse * coarse;
        var strength = new double[nc]; var u = new double[nc]; var v = new double[nc];
        for (int row = 0; row < n; row++) for (int col = 0; col < n; col++)
        {
            int i = row * n + col, j = row / factor * coarse + col / factor;
            double age = fields[1][i] > 0 ? fields[2][i] / fields[1][i] : 0;
            strength[j] += LithosphereMembrane.MaterialResistance(fields[0][i], fields[1][i], age) / (factor * factor);
            u[j] += raw.X[i] / (factor * factor); v[j] += raw.Z[i] / (factor * factor);
        }
        var targetEast = new double[nc]; var targetSouth = new double[nc];
        for (int row = 0; row < coarse; row++) for (int col = 0; col < coarse; col++)
        {
            int i = row * coarse + col, e = row * coarse + (col + 1) % coarse, s = ((row + 1) % coarse) * coarse + col;
            targetEast[i] = .5 * (u[i] + u[e]); targetSouth[i] = .5 * (v[i] + v[s]);
        }
        // Do not reuse a geometric deformation width as sqrt(mu/drag):
        // tensor coupling and the reference resistance change its physical reach.
        double coefficientLength = CoefficientLengthForCompressionWidth(length);
        MembraneSolution solved = LithosphereMembrane.Solve(coarse, dx * factor, dz * factor,
            coefficientLength, strength, targetEast, targetSouth);
        var east = new double[n * n]; var south = new double[n * n];
        for (int row = 0; row < n; row++) for (int col = 0; col < n; col++)
        {
            // Staggered face coordinates, not cell-centred resampling.
            int i = row * n + col;
            east[i] = Bilinear(solved.East, (col + 1d) / factor - 1, (row + .5) / factor - .5);
            south[i] = Bilinear(solved.South, (col + .5) / factor - .5, (row + 1d) / factor - 1);
        }
        return new(n, coarse, length, solved, strength, east, south, dx, dz);
        double Bilinear(IReadOnlyList<double> a, double gx, double gz)
        {
            int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz); double tx = gx - ix, tz = gz - iz;
            int x0 = (ix + coarse) % coarse, x1 = (ix + 1 + coarse) % coarse;
            int z0 = (iz + coarse) % coarse, z1 = (iz + 1 + coarse) % coarse;
            return (1 - tz) * ((1 - tx) * a[z0 * coarse + x0] + tx * a[z0 * coarse + x1])
                + tz * ((1 - tx) * a[z1 * coarse + x0] + tx * a[z1 * coarse + x1]);
        }
    }
}

public sealed record MembraneStep(double Time, int Iterations, double RelativeForceResidual,
    double DrivingWork, double DragDissipation, double ViscousDissipation, double MaximumOutgoingRate);
