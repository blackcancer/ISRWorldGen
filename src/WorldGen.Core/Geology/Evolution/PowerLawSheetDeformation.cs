using System.Collections.ObjectModel;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WorldGen.Rheology")]

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Regularized, monotone power-law PRIOR; strain rate is per MODEL time, not an Earth calibration.</summary>
public sealed record PowerLawSheetOptions
{
    public double StressExponent { get; }
    public double ReferenceStrainRate { get; }
    public double RelativeTolerance { get; }
    public int MaximumNewtonIterations { get; }
    public PowerLawSheetOptions(double stressExponent = 3, double referenceStrainRate = .02,
        double relativeTolerance = 1e-12, int maximumNewtonIterations = 50)
    {
        if (!double.IsFinite(stressExponent) || stressExponent < 1 || stressExponent > 8 ||
            !double.IsFinite(referenceStrainRate) || referenceStrainRate <= 0 ||
            !double.IsFinite(relativeTolerance) || relativeTolerance < 1e-14 || relativeTolerance > 1e-6 ||
            maximumNewtonIterations < 1 || maximumNewtonIterations > 100)
            throw new ArgumentException("Invalid power-law constitutive or nonlinear solver parameters.");
        StressExponent = stressExponent; ReferenceStrainRate = referenceStrainRate;
        RelativeTolerance = relativeTolerance; MaximumNewtonIterations = maximumNewtonIterations;
    }
}

public sealed record PowerLawSheetSolution(ThinSheetSolution Solution, ReadOnlyCollection<double> EffectiveViscosity,
    int NewtonIterations, int LinearIterations, double InitialEnergy, double FinalEnergy);

/// <summary>
/// Energy-consistent generalized Newtonian extension of the existing MAC sheet.
/// mu=mu0*(1+Q/(2*(L*rate0)^2))^(-(1-1/n)/2). No yielding, damage, inherited
/// fractures, new plates, slab pull or height filtering is implied. Q includes
/// both normal strains and the four adjacent corner shears. Fixed harmonic
/// quadrature weights recover the exact old LINEAR stencil at n=1. The convex
/// dissipation potential supplies stresses AND their exact tangent; a Newton-CG
/// line search solves the actual nonlinear force balance, never a stale frozen
/// viscosity residual. No clipping of viscosity, velocity or terrain is used.
/// </summary>
public static class PowerLawSheetDeformation
{
    public const string AlgorithmId = "energy-consistent-mac-regularized-power-law-newton-v1";
    public static PowerLawSheetSolution Solve(IReadOnlyList<double> preferredEast, IReadOnlyList<double> preferredSouth,
        IReadOnlyList<double> viscosity, int side, double dx, double dz, double couplingLength,
        PowerLawSheetOptions options, ThinSheetSolution? warmStart = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var op = new PowerLawSheetOperator(preferredEast, preferredSouth, viscosity, side, dx, dz, couplingLength, options);
        int count = side * side, size = 2 * count;
        if (warmStart is not null && (warmStart.East.Count != count || warmStart.South.Count != count ||
            warmStart.East.Any(v => !double.IsFinite(v)) || warmStart.South.Any(v => !double.IsFinite(v))))
            throw new ArgumentException("Invalid nonlinear warm start.");
        // The limiting cases retain the old calculation, rather than only looking similar.
        if (options.StressExponent == 1 || couplingLength == 0)
        {
            var linear = ThinSheetDeformation.Solve(preferredEast, preferredSouth, viscosity, side, dx, dz, couplingLength,
                options.RelativeTolerance, warmStart: warmStart);
            double[] v0 = linear.East.Concat(linear.South).ToArray();
            var ev = op.Evaluate(v0);
            return new(linear, Array.AsReadOnly(viscosity.ToArray()), 0, linear.Iterations, ev.Energy, ev.Energy);
        }
        // A linear solution is a starting point, never an accepted fallback.
        var initial = warmStart ?? ThinSheetDeformation.Solve(preferredEast, preferredSouth, viscosity, side, dx, dz, couplingLength);
        double[] v = initial.East.Concat(initial.South).ToArray();
        var current = op.Evaluate(v); double initialEnergy = current.Energy;
        double norm = Math.Max(Math.Sqrt(PowerLawSheetOperator.Dot(op.Forcing, op.Forcing)), 1);
        double target = options.RelativeTolerance * norm;
        double residual = Math.Sqrt(PowerLawSheetOperator.Dot(current.Gradient, current.Gradient));
        int outer = 0, innerTotal = warmStart is null ? initial.Iterations : 0;
        while (residual > target && outer < options.MaximumNewtonIterations)
        {
            double[] direction = new double[size], rhs = current.Gradient.Select(a => -a).ToArray();
            double[] r = (double[])rhs.Clone(), diag = op.Diagonal(), z = new double[size], p = new double[size], ap = new double[size];
            for (int i = 0; i < size; i++) z[i] = p[i] = r[i] / diag[i];
            double rz = PowerLawSheetOperator.Dot(r, z);
            // Inexact Newton, increasingly accurate near equilibrium. Final
            // acceptance always recomputes the FULL nonlinear residual.
            double innerTarget = residual * Math.Min(.1, Math.Sqrt(residual / norm));
            innerTarget = Math.Max(innerTarget, target * .01);
            int iterations = 0; double innerResidual = residual;
            while (innerResidual > innerTarget && iterations < 2000)
            {
                op.Tangent(p, ap); double curvature = PowerLawSheetOperator.Dot(p, ap);
                if (!(curvature > 0) || !double.IsFinite(curvature) || !double.IsFinite(rz))
                    throw new ArithmeticException("Nonlinear tangent lost positive curvature.");
                double alpha = rz / curvature;
                for (int i = 0; i < size; i++) { direction[i] += alpha * p[i]; r[i] -= alpha * ap[i]; }
                iterations++;
                innerResidual = Math.Sqrt(PowerLawSheetOperator.Dot(r, r));
                if (innerResidual <= innerTarget)
                {
                    op.Tangent(direction, ap);
                    for (int i = 0; i < size; i++) r[i] = rhs[i] - ap[i];
                    innerResidual = Math.Sqrt(PowerLawSheetOperator.Dot(r, r));
                    if (innerResidual <= innerTarget) break;
                    for (int i = 0; i < size; i++) z[i] = p[i] = r[i] / diag[i];
                    rz = PowerLawSheetOperator.Dot(r, z); continue;
                }
                for (int i = 0; i < size; i++) z[i] = r[i] / diag[i];
                double next = PowerLawSheetOperator.Dot(r, z), beta = next / rz;
                for (int i = 0; i < size; i++) p[i] = z[i] + beta * p[i];
                rz = next;
            }
            innerTotal += iterations;
            if (!double.IsFinite(innerResidual) || innerResidual > innerTarget)
                throw new ArithmeticException("Nonlinear tangent iteration budget exhausted; no fallback.");
            double descent = PowerLawSheetOperator.Dot(current.Gradient, direction);
            if (!(descent < 0) || !double.IsFinite(descent)) throw new ArithmeticException("Nonlinear step is not an energy descent.");
            bool accepted = false; double step = 1;
            for (int line = 0; line < 32; line++, step *= .5)
            {
                double[] trial = new double[size];
                for (int i = 0; i < size; i++) trial[i] = v[i] + step * direction[i];
                var next = op.Evaluate(trial);
                double nextResidual = Math.Sqrt(PowerLawSheetOperator.Dot(next.Gradient, next.Gradient));
                // Near the minimum energy differences round away. Permit only
                // roundoff-sized energy changes AND a strictly lower true residual.
                double roundoff = 32 * 2.2204460492503131e-16 * Math.Max(1, Math.Abs(current.Energy));
                if (next.Energy <= current.Energy + .0001 * step * descent ||
                    (Math.Abs(next.Energy - current.Energy) <= roundoff && nextResidual < residual))
                { v = trial; current = next; residual = nextResidual; accepted = true; break; }
            }
            if (!accepted) throw new ArithmeticException("Nonlinear energy line search exhausted; no fallback.");
            outer++;
        }
        current = op.Evaluate(v);
        residual = Math.Sqrt(PowerLawSheetOperator.Dot(current.Gradient, current.Gradient));
        if (!double.IsFinite(residual) || residual > target)
            throw new ArithmeticException($"Nonlinear sheet did not converge: {residual / norm:R}, Newton={outer}.");
        double work = 0, meanX = 0, meanZ = 0;
        var divergence = new double[count]; var shear = new double[count];
        for (int i = 0; i < count; i++)
        {
            work += v[i] * (op.Forcing[i] - v[i]) + v[count + i] * (op.Forcing[count + i] - v[count + i]);
            meanX += v[i] - op.Forcing[i]; meanZ += v[count + i] - op.Forcing[count + i];
            divergence[i] = (v[i] - v[op.West[i]]) / dx + (v[count + i] - v[count + op.North[i]]) / dz;
            shear[i] = (v[op.South[i]] - v[i]) / dz + (v[count + op.East[i]] - v[count + i]) / dx;
        }
        if (Math.Abs(work - current.Dissipation) > 1e-8 * Math.Max(1, current.Dissipation))
            throw new ArithmeticException("Nonlinear basal work and viscous dissipation disagree.");
        var solution = new ThinSheetSolution(Array.AsReadOnly(v[..count]), Array.AsReadOnly(v[count..]),
            Array.AsReadOnly(divergence), Array.AsReadOnly(shear), innerTotal, residual / norm,
            current.Dissipation, work, Math.Max(Math.Abs(meanX), Math.Abs(meanZ)) / count);
        return new(solution, Array.AsReadOnly(op.EffectiveViscosity()), outer, innerTotal, initialEnergy, current.Energy);
    }
}

internal sealed class PowerLawSheetOperator
{
    internal sealed record Evaluation(double[] Gradient, double Energy, double Dissipation);
    internal readonly double[] Forcing;
    internal readonly int[] East, West, South, North;
    private readonly int count, size;
    private readonly double hx, hz, q0, exponent;
    private readonly double[] mu, harmonic, ex, ez, gamma, q, factor, derivative;
    private readonly double[] xx, zz, xz, de, dz_, dg, df;
    internal PowerLawSheetOperator(IReadOnlyList<double> east, IReadOnlyList<double> south, IReadOnlyList<double> viscosity,
        int side, double dx, double dz, double length, PowerLawSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(east); ArgumentNullException.ThrowIfNull(south); ArgumentNullException.ThrowIfNull(viscosity);
        ArgumentNullException.ThrowIfNull(options);
        if (side < 4 || side > 256 || east.Count != side * side || south.Count != side * side || viscosity.Count != side * side ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 ||
            !double.IsFinite(length) || length < 0 || length > Math.Min(dx, dz) * side ||
            east.Any(v => !double.IsFinite(v)) || south.Any(v => !double.IsFinite(v)) || viscosity.Any(v => !double.IsFinite(v) || v <= 0 || v > 1e6))
            throw new ArgumentException("Invalid power-law sheet geometry or material fields.");
        count = side * side; size = 2 * count; hx = length / dx; hz = length / dz;
        q0 = length == 0 ? 1 : 2 * Math.Pow(length * options.ReferenceStrainRate, 2);
        exponent = (1 - 1 / options.StressExponent) / 2;
        if (!double.IsFinite(q0) || q0 <= 0 || !double.IsFinite(hx) || !double.IsFinite(hz))
            throw new ArgumentException("Unrepresentable constitutive scale.");
        mu = viscosity.ToArray(); Forcing = east.Concat(south).ToArray();
        East = new int[count]; West = new int[count]; South = new int[count]; North = new int[count];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        { int i = z * side + x; East[i] = z * side + (x + 1) % side; West[i] = z * side + (x + side - 1) % side;
          South[i] = ((z + 1) % side) * side + x; North[i] = ((z + side - 1) % side) * side + x; }
        harmonic = new double[count];
        for (int i = 0; i < count; i++) harmonic[i] = 4 / (1 / mu[i] + 1 / mu[East[i]] + 1 / mu[South[i]] + 1 / mu[South[East[i]]]);
        ex = new double[count]; ez = new double[count]; gamma = new double[count]; q = new double[count];
        factor = new double[count]; derivative = new double[count]; xx = new double[count]; zz = new double[count]; xz = new double[count];
        de = new double[count]; dz_ = new double[count]; dg = new double[count]; df = new double[count];
    }
    internal Evaluation Evaluate(double[] v)
    {
        if (v.Length != size || v.Any(a => !double.IsFinite(a))) throw new ArgumentException("Invalid nonlinear trial velocity.");
        Strain(v, ex, ez, gamma);
        double energy = 0, dissipation = 0;
        for (int i = 0; i < count; i++)
        {
            double shear = 0;
            Add(i); Add(West[i]); Add(North[i]); Add(North[West[i]]);
            q[i] = ex[i] * ex[i] + ez[i] * ez[i] + (ex[i] + ez[i]) * (ex[i] + ez[i]) + shear / (8 * mu[i]);
            if (!double.IsFinite(q[i]) || q[i] < 0) throw new ArithmeticException("Unrepresentable strain invariant.");
            factor[i] = Math.Pow(1 + q[i] / q0, -exponent);
            derivative[i] = -exponent * factor[i] / (q0 + q[i]);
            energy += mu[i] * Potential(q[i]); dissipation += 2 * mu[i] * factor[i] * q[i];
            energy += .5 * ((v[i] - Forcing[i]) * (v[i] - Forcing[i]) + (v[count+i] - Forcing[count+i]) * (v[count+i] - Forcing[count+i]));
            void Add(int k) { shear += harmonic[k] * gamma[k] * gamma[k]; }
        }
        for (int i = 0; i < count; i++)
        { xx[i] = 2 * mu[i] * factor[i] * (2 * ex[i] + ez[i]); zz[i] = 2 * mu[i] * factor[i] * (ex[i] + 2 * ez[i]);
          xz[i] = harmonic[i] * Average(factor, i) * gamma[i]; }
        var gradient = new double[size]; Adjoint(v, gradient);
        for (int i = 0; i < size; i++) gradient[i] -= Forcing[i];
        if (!double.IsFinite(energy) || !double.IsFinite(dissipation) || gradient.Any(a => !double.IsFinite(a)))
            throw new ArithmeticException("Nonfinite nonlinear energy or force.");
        return new(gradient, energy, dissipation);
    }
    internal double[] EffectiveViscosity() => mu.Select((v, i) => v * factor[i]).ToArray();
    internal double[] Diagonal()
    {
        var d = new double[size];
        for (int i = 0; i < count; i++)
        { d[i] = 1 + 4 * hx * hx * (mu[i]*factor[i] + mu[East[i]]*factor[East[i]])
              + hz * hz * (harmonic[i]*Average(factor,i) + harmonic[North[i]]*Average(factor,North[i]));
          d[count+i] = 1 + 4 * hz * hz * (mu[i]*factor[i] + mu[South[i]]*factor[South[i]])
              + hx * hx * (harmonic[i]*Average(factor,i) + harmonic[West[i]]*Average(factor,West[i])); }
        return d;
    }
    internal void Tangent(double[] p, double[] output)
    {
        Strain(p, de, dz_, dg);
        for (int i = 0; i < count; i++)
        {
            double shear = 0; Add(i); Add(West[i]); Add(North[i]); Add(North[West[i]]);
            double dq = 2 * (2 * ex[i] + ez[i]) * de[i] + 2 * (ex[i] + 2 * ez[i]) * dz_[i] + shear / (4 * mu[i]);
            df[i] = derivative[i] * dq;
            void Add(int k) { shear += harmonic[k] * gamma[k] * dg[k]; }
        }
        for (int i = 0; i < count; i++)
        { xx[i] = 2 * mu[i] * (factor[i] * (2*de[i]+dz_[i]) + df[i] * (2*ex[i]+ez[i]));
          zz[i] = 2 * mu[i] * (factor[i] * (de[i]+2*dz_[i]) + df[i] * (ex[i]+2*ez[i]));
          xz[i] = harmonic[i] * (Average(factor,i) * dg[i] + Average(df,i) * gamma[i]); }
        Adjoint(p, output);
    }
    private void Strain(double[] v, double[] a, double[] b, double[] c)
    {
        for (int i = 0; i < count; i++)
        { a[i] = (v[i] - v[West[i]]) * hx; b[i] = (v[count+i] - v[count+North[i]]) * hz;
          c[i] = (v[South[i]] - v[i]) * hz + (v[count+East[i]] - v[count+i]) * hx; }
    }
    private void Adjoint(double[] v, double[] output)
    {
        for (int i = 0; i < count; i++)
        { output[i] = v[i] + hx*(xx[i]-xx[East[i]]) + hz*(xz[North[i]]-xz[i]);
          output[count+i] = v[count+i] + hz*(zz[i]-zz[South[i]]) + hx*(xz[West[i]]-xz[i]); }
    }
    private double Average(double[] a, int i) => .25 * (a[i] + a[East[i]] + a[South[i]] + a[South[East[i]]]);
    private double Potential(double value)
    {
        if (exponent == 0) return value;
        double x = value / q0;
        if (x < 1e-4) return value * (1 - exponent*x/2 + exponent*(exponent+1)*x*x/6 - exponent*(exponent+1)*(exponent+2)*x*x*x/24);
        return q0 * (Math.Pow(1+x, 1-exponent) - 1) / (1-exponent);
    }
    internal static double Dot(double[] a, double[] b)
    {
        double sum = 0, correction = 0;
        for (int i = 0; i < a.Length; i++)
        { double value = a[i] * b[i] - correction, next = sum + value; correction = (next-sum)-value; sum = next; }
        return sum;
    }
}
