using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

public sealed record ThinSheetSolution(ReadOnlyCollection<double> East, ReadOnlyCollection<double> South,
    ReadOnlyCollection<double> Divergence, ReadOnlyCollection<double> EngineeringShear,
    int Iterations, double RelativeResidual, double Dissipation, double Work, double MeanVelocityError);

/// <summary>
/// Periodic MAC-grid membrane equilibrium with linear basal drag. In normalized
/// units: v - div(2 mu [epsilon + tr(epsilon) I]) = preferred velocity, where
/// spatial derivatives include the declared coupling length. This is a reduced
/// Newtonian thin-sheet model, not mantle convection, slab pull or rigid-plate
/// torque balance. Positive viscosity yields a symmetric positive-definite
/// operator. It filters no heights and changes no material by itself.
/// </summary>
public static class ThinSheetDeformation
{
    public const string AlgorithmId = "mac-viscous-sheet-basal-drag-v1";

    public static ThinSheetSolution Solve(IReadOnlyList<double> preferredEast, IReadOnlyList<double> preferredSouth,
        IReadOnlyList<double> viscosity, int side, double dx, double dz, double couplingLength,
        double relativeTolerance = 1e-12, int maximumIterations = 2000, ThinSheetSolution? warmStart = null)
    {
        ArgumentNullException.ThrowIfNull(preferredEast); ArgumentNullException.ThrowIfNull(preferredSouth);
        ArgumentNullException.ThrowIfNull(viscosity);
        if (side < 4 || side > 256 || preferredEast.Count != side * side || preferredSouth.Count != side * side || viscosity.Count != side * side ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 ||
            !double.IsFinite(couplingLength) || couplingLength < 0 || couplingLength > Math.Min(dx, dz) * side ||
            !double.IsFinite(relativeTolerance) || relativeTolerance < 1e-14 || relativeTolerance > 1e-6 || maximumIterations < 1 || maximumIterations > 10000 ||
            preferredEast.Any(v => !double.IsFinite(v)) || preferredSouth.Any(v => !double.IsFinite(v)) ||
            viscosity.Any(v => !double.IsFinite(v) || v <= 0 || v > 1e6)) throw new ArgumentException("Invalid thin-sheet geometry, forcing, viscosity or solver budget.");
        int n = side, count = n * n, size = 2 * count;
        if (warmStart is not null && (warmStart.East.Count != count || warmStart.South.Count != count ||
            warmStart.East.Any(v => !double.IsFinite(v)) || warmStart.South.Any(v => !double.IsFinite(v))))
            throw new ArgumentException("Warm start belongs to another grid or contains nonfinite velocities.");
        double hx = couplingLength / dx, hz = couplingLength / dz;
        var e = new int[count]; var w = new int[count]; var s = new int[count]; var north = new int[count];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x; e[i] = z * n + (x + 1) % n; w[i] = z * n + (x + n - 1) % n;
            s[i] = ((z + 1) % n) * n + x; north[i] = ((z + n - 1) % n) * n + x;
        }
        double[] mu = viscosity.ToArray(), corner = new double[count], diagonal = new double[size];
        for (int i = 0; i < count; i++) corner[i] = 4 / (1 / mu[i] + 1 / mu[e[i]] + 1 / mu[s[i]] + 1 / mu[s[e[i]]]);
        for (int i = 0; i < count; i++)
        {
            diagonal[i] = 1 + 4 * hx * hx * (mu[i] + mu[e[i]]) + hz * hz * (corner[i] + corner[north[i]]);
            diagonal[count + i] = 1 + 4 * hz * hz * (mu[i] + mu[s[i]]) + hx * hx * (corner[i] + corner[w[i]]);
        }
        var b = new double[size]; var v = new double[size];
        for (int i = 0; i < count; i++)
        {
            b[i] = preferredEast[i]; b[count + i] = preferredSouth[i];
            v[i] = warmStart?.East[i] ?? b[i]; v[count + i] = warmStart?.South[i] ?? b[count + i];
        }
        var xx = new double[count]; var zz = new double[count]; var xz = new double[count];
        var av = new double[size]; var r = new double[size]; var zvec = new double[size]; var pvec = new double[size]; var ap = new double[size];
        double norm = Math.Max(Math.Sqrt(Dot(b, b)), 1), target = relativeTolerance * norm;
        Apply(v, av);
        for (int i = 0; i < size; i++) { r[i] = b[i] - av[i]; zvec[i] = r[i] / diagonal[i]; pvec[i] = zvec[i]; }
        double rz = Dot(r, zvec), residual = Math.Sqrt(Dot(r, r)); int iterations = 0;
        while (residual > target && iterations < maximumIterations)
        {
            Apply(pvec, ap); double curvature = Dot(pvec, ap);
            if (!(curvature > 0) || !double.IsFinite(curvature) || !double.IsFinite(rz)) throw new ArithmeticException("Thin-sheet operator lost positive curvature.");
            double alpha = rz / curvature;
            for (int i = 0; i < size; i++) { v[i] += alpha * pvec[i]; r[i] -= alpha * ap[i]; }
            iterations++; residual = Math.Sqrt(Dot(r, r));
            if (residual <= target)
            {
                // Acceptance uses the actual force residual, not the CG recurrence.
                Apply(v, av);
                for (int i = 0; i < size; i++) r[i] = b[i] - av[i];
                residual = Math.Sqrt(Dot(r, r));
                if (residual <= target) break;
                for (int i = 0; i < size; i++) { zvec[i] = r[i] / diagonal[i]; pvec[i] = zvec[i]; }
                rz = Dot(r, zvec); continue;
            }
            for (int i = 0; i < size; i++) zvec[i] = r[i] / diagonal[i];
            double next = Dot(r, zvec), beta = next / rz;
            for (int i = 0; i < size; i++) pvec[i] = zvec[i] + beta * pvec[i];
            rz = next;
        }
        Apply(v, av);
        for (int i = 0; i < size; i++) r[i] = b[i] - av[i];
        residual = Math.Sqrt(Dot(r, r));
        if (!double.IsFinite(residual) || residual > target || v.Any(a => !double.IsFinite(a)))
            throw new ArithmeticException($"Thin-sheet equilibrium did not converge: {residual / norm:R}, iterations={iterations}. No kinematic fallback.");
        double dissipation = 0, work = 0, meanX = 0, meanZ = 0;
        var divergence = new double[count]; var shear = new double[count];
        for (int i = 0; i < count; i++)
        {
            double ex = (v[i] - v[w[i]]) * hx, ez = (v[count + i] - v[count + north[i]]) * hz;
            double gamma = (v[s[i]] - v[i]) * hz + (v[count + e[i]] - v[count + i]) * hx;
            dissipation += 2 * mu[i] * (ex * ex + ez * ez + (ex + ez) * (ex + ez)) + corner[i] * gamma * gamma;
            work += v[i] * (b[i] - v[i]) + v[count + i] * (b[count + i] - v[count + i]);
            meanX += v[i] - b[i]; meanZ += v[count + i] - b[count + i];
            divergence[i] = (v[i] - v[w[i]]) / dx + (v[count + i] - v[count + north[i]]) / dz;
            shear[i] = (v[s[i]] - v[i]) / dz + (v[count + e[i]] - v[count + i]) / dx;
        }
        return new(Array.AsReadOnly(v[..count]), Array.AsReadOnly(v[count..]), Array.AsReadOnly(divergence), Array.AsReadOnly(shear),
            iterations, residual / norm, dissipation, work, Math.Max(Math.Abs(meanX), Math.Abs(meanZ)) / count);

        void Apply(double[] a, double[] output)
        {
            for (int i = 0; i < count; i++)
            {
                double ex = (a[i] - a[w[i]]) * hx, ez = (a[count + i] - a[count + north[i]]) * hz;
                xx[i] = 2 * mu[i] * (2 * ex + ez); zz[i] = 2 * mu[i] * (ex + 2 * ez);
                xz[i] = corner[i] * ((a[s[i]] - a[i]) * hz + (a[count + e[i]] - a[count + i]) * hx);
            }
            for (int i = 0; i < count; i++)
            {
                output[i] = a[i] + hx * (xx[i] - xx[e[i]]) + hz * (xz[north[i]] - xz[i]);
                output[count + i] = a[count + i] + hz * (zz[i] - zz[s[i]]) + hx * (xz[w[i]] - xz[i]);
            }
        }
    }

    private static double Dot(double[] a, double[] b)
    {
        double sum = 0, correction = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double value = a[i] * b[i] - correction, next = sum + value;
            correction = (next - sum) - value; sum = next;
        }
        return sum;
    }
}
