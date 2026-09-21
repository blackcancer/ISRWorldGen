using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Reduced, periodic, Newtonian thin-sheet stress balance with linear basal drag.
/// All equations are DIVIDED by the drag coefficient: mu/drag has units of
/// reference length squared. Velocities use reference units per model time.
/// This is not an SI-calibrated mantle/slab-force model or a fracture law.
/// A MAC grid and an energy-derived stencil retain cross-component stresses.
/// No topography is filtered, fitted or passed to this operator.
/// </summary>
public static class LithosphereMembrane
{
    public const string AlgorithmId = "viscous-membrane-mac-drag-pcg-v1";

    public static MembraneSolution Solve(int side, double dx, double dz, double couplingLength,
        IReadOnlyList<double> resistance, IReadOnlyList<double> driveEast, IReadOnlyList<double> driveSouth,
        int maximumIterations = 512)
    {
        ArgumentNullException.ThrowIfNull(resistance); ArgumentNullException.ThrowIfNull(driveEast);
        ArgumentNullException.ThrowIfNull(driveSouth);
        int count = checked(side * side);
        if (side is < 4 or > 512 || resistance.Count != count || driveEast.Count != count || driveSouth.Count != count ||
            !double.IsFinite(dx) || !double.IsFinite(dz) || dx <= 0 || dz <= 0 ||
            !double.IsFinite(couplingLength) || couplingLength < 0 || couplingLength > .5 * side * Math.Min(dx, dz) ||
            maximumIterations is < 1 or > 2048 || resistance.Any(v => !double.IsFinite(v) || v <= 0 || v > 1000) ||
            driveEast.Any(v => !double.IsFinite(v)) || driveSouth.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Invalid bounded membrane problem.");
        double[] rhs = driveEast.Concat(driveSouth).ToArray();
        var op = new Operator(side, dx, dz, couplingLength, resistance);
        var x = new double[rhs.Length]; var r = (double[])rhs.Clone();
        var z = new double[rhs.Length]; var p = new double[rhs.Length]; var ap = new double[rhs.Length];
        double rhsNorm = Dot(rhs, rhs), rz = 0; int iterations = 0;
        if (!double.IsFinite(rhsNorm)) throw new ArithmeticException("Forcing norm overflow.");
        if (rhsNorm > 0)
        {
            for (int i = 0; i < r.Length; i++) { z[i] = r[i] / op.Diagonal[i]; p[i] = z[i]; }
            rz = Dot(r, z);
            double target = rhsNorm * 1e-24;
            for (; iterations < maximumIterations; iterations++)
            {
                op.Apply(p, ap);
                double denom = Dot(p, ap);
                if (!(denom > 0) || !double.IsFinite(denom)) throw new ArithmeticException("Nonpositive membrane CG energy.");
                double alpha = rz / denom;
                for (int i = 0; i < x.Length; i++) { x[i] += alpha * p[i]; r[i] -= alpha * ap[i]; }
                if (Dot(r, r) <= target) { iterations++; break; }
                for (int i = 0; i < z.Length; i++) z[i] = r[i] / op.Diagonal[i];
                double next = Dot(r, z), beta = next / rz;
                for (int i = 0; i < p.Length; i++) p[i] = z[i] + beta * p[i];
                rz = next;
            }
        }
        // Recompute the ORIGINAL discrete force balance, never trust only the
        // recursively updated CG residual or a successful iteration counter.
        op.Apply(x, ap);
        double error = 0, scale = 1;
        for (int i = 0; i < x.Length; i++)
        {
            if (!double.IsFinite(x[i])) throw new ArithmeticException("Nonfinite solved velocity.");
            error = Math.Max(error, Math.Abs(ap[i] - rhs[i])); scale = Math.Max(scale, Math.Abs(rhs[i]));
        }
        double residual = error / scale;
        if (!double.IsFinite(residual) || residual > 1e-9)
            throw new ArithmeticException($"Membrane equilibrium not solved: relative infinity residual={residual:R}, iterations={iterations}.");
        double[] east = x[..count], south = x[count..], divergence = new double[count], shear = new double[count];
        for (int row = 0; row < side; row++) for (int col = 0; col < side; col++)
        {
            int i = row * side + col, w = row * side + (col + side - 1) % side, north = ((row + side - 1) % side) * side + col;
            int e = row * side + (col + 1) % side, s = ((row + 1) % side) * side + col;
            divergence[i] = (east[i] - east[w]) / dx + (south[i] - south[north]) / dz;
            shear[i] = (east[s] - east[i]) / dz + (south[e] - south[i]) / dx;
        }
        double work = Dot(x, rhs), drag = Dot(x, x), viscous = Dot(x, ap) - drag;
        if (viscous < -1e-9 * Math.Max(1, work)) throw new ArithmeticException("Negative viscous dissipation.");
        return new(side, east, south, divergence, shear, residual, iterations, work, drag, viscous);
    }

    /// <summary>Explicit effective-viscosity PRIOR, not a measured rock law.
    /// Continental thickness and age-bearing oceanic thickness contribute to
    /// depth-integrated resistance. Age increases ocean resistance smoothly.
    /// No sea level, plate total mass, random weight or geometric seed is used.</summary>
    public static double MaterialResistance(double continentalKm, double oceanicKm, double oceanAge)
    {
        if (!double.IsFinite(continentalKm) || !double.IsFinite(oceanicKm) || !double.IsFinite(oceanAge) ||
            continentalKm < 0 || oceanicKm < 0 || oceanAge < 0 || continentalKm + oceanicKm <= 0 ||
            continentalKm > 150 || oceanicKm > 150)
            throw new ArgumentException("Invalid material column for membrane resistance.");
        return .25 + 2.5 * continentalKm / 35 + oceanicKm / 7 * (.25 + 1.5 * oceanAge / (oceanAge + 50));
    }

    // Kept internal for exact small-matrix verification in the linked checks.
    internal sealed class Operator
    {
        private readonly int n, count;
        private readonly double ix, iz;
        private readonly double[] mu, corner;
        internal double[] Diagonal { get; }
        internal Operator(int side, double dx, double dz, double length, IReadOnlyList<double> resistance)
        {
            n = side; count = n * n; ix = 1 / dx; iz = 1 / dz;
            mu = resistance.Select(v => length * length * v).ToArray(); corner = new double[count];
            if (mu.Any(v => !double.IsFinite(v))) throw new ArithmeticException("Resistance overflow.");
            for (int row = 0; row < n; row++) for (int col = 0; col < n; col++)
            {
                int i = row * n + col, e = row * n + (col + 1) % n, s = ((row + 1) % n) * n + col, se = ((row + 1) % n) * n + (col + 1) % n;
                corner[i] = .25 * (mu[i] + mu[e] + mu[s] + mu[se]);
            }
            Diagonal = new double[2 * count];
            for (int row = 0; row < n; row++) for (int col = 0; col < n; col++)
            {
                int i = row * n + col, e = row * n + (col + 1) % n, s = ((row + 1) % n) * n + col;
                int w = row * n + (col + n - 1) % n, north = ((row + n - 1) % n) * n + col;
                Diagonal[i] = 1 + 4 * (mu[i] + mu[e]) * ix * ix + (corner[i] + corner[north]) * iz * iz;
                Diagonal[count + i] = 1 + 4 * (mu[i] + mu[s]) * iz * iz + (corner[i] + corner[w]) * ix * ix;
            }
        }
        internal void Apply(double[] input, double[] output)
        {
            Array.Copy(input, output, input.Length); // linear basal drag
            for (int row = 0; row < n; row++) for (int col = 0; col < n; col++)
            {
                int i = row * n + col, w = row * n + (col + n - 1) % n, north = ((row + n - 1) % n) * n + col;
                int e = row * n + (col + 1) % n, s = ((row + 1) % n) * n + col;
                double ex = (input[i] - input[w]) * ix, ez = (input[count + i] - input[count + north]) * iz;
                // Vertical incompressibility eliminated: Txx=2mu(2ex+ez), Tzz=2mu(ex+2ez).
                double xx = 2 * mu[i] * (2 * ex + ez), zz = 2 * mu[i] * (ex + 2 * ez);
                output[i] += xx * ix; output[w] -= xx * ix;
                output[count + i] += zz * iz; output[count + north] -= zz * iz;
                double xz = corner[i] * ((input[s] - input[i]) * iz + (input[count + e] - input[count + i]) * ix);
                output[s] += xz * iz; output[i] -= xz * iz;
                output[count + e] += xz * ix; output[count + i] -= xz * ix;
            }
        }
    }
    internal static double Dot(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double sum = 0, correction = 0;
        for (int i = 0; i < a.Count; i++)
        { double value = a[i] * b[i] - correction, next = sum + value; correction = (next - sum) - value; sum = next; }
        return sum;
    }
}

public sealed class MembraneSolution
{
    public int Side { get; }
    public ReadOnlyCollection<double> East { get; }
    public ReadOnlyCollection<double> South { get; }
    public ReadOnlyCollection<double> Divergence { get; }
    public ReadOnlyCollection<double> Shear { get; }
    public double RelativeForceResidual { get; }
    public int Iterations { get; }
    public double DrivingWork { get; }
    public double DragDissipation { get; }
    public double ViscousDissipation { get; }
    internal MembraneSolution(int side, double[] east, double[] south, double[] divergence, double[] shear,
        double residual, int iterations, double work, double drag, double viscous)
    {
        Side = side; East = Array.AsReadOnly(east); South = Array.AsReadOnly(south);
        Divergence = Array.AsReadOnly(divergence); Shear = Array.AsReadOnly(shear);
        RelativeForceResidual = residual; Iterations = iterations; DrivingWork = work;
        DragDissipation = drag; ViscousDissipation = viscous;
    }
}
