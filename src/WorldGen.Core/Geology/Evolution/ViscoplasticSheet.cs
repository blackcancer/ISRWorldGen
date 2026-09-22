using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Explicit normalized membrane loads, NOT pascals, not the mass of a
/// plate, not a calibrated friction law. PostYieldRatio is a constitutive dashpot
/// regularization, not a solver tolerance or a viscosity clamp.</summary>
public sealed record SheetYieldOptions
{
    public bool Enabled { get; }
    public double ContinentalLoad { get; }
    public double OceanicLoad { get; }
    public double PostYieldRatio { get; }
    public SheetYieldOptions(bool enabled = true, double continentalLoad = 700,
        double oceanicLoad = 350, double postYieldRatio = .15)
    {
        if (!double.IsFinite(continentalLoad) || continentalLoad <= 0 || continentalLoad > 1e12 ||
            !double.IsFinite(oceanicLoad) || oceanicLoad <= 0 || oceanicLoad > 1e12 ||
            !double.IsFinite(postYieldRatio) || postYieldRatio < .02 || postYieldRatio > 1)
            throw new ArgumentException("Invalid normalized yield load or post-yield dashpot.");
        Enabled = enabled; ContinentalLoad = continentalLoad; OceanicLoad = oceanicLoad; PostYieldRatio = postYieldRatio;
    }
}

public readonly record struct SheetYieldResponse(double Stress, double Tangent, double Potential,
    double PlasticRate, double EffectiveViscosity);
public sealed record ViscoplasticSheetSolution(ThinSheetSolution Velocity,
    ReadOnlyCollection<double> PlasticRates, ReadOnlyCollection<double> YieldedFraction,
    ReadOnlyCollection<double> YieldLoads, int NonlinearIterations, double InitialPotential,
    double FinalPotential, double MaximumConstitutiveResidual);

/// <summary>Convex biviscous thin sheet on a periodic MAC grid. Four quadrature
/// points per cell couple normal and shear deformation with ONE invariant.
/// A semismooth Newton method solves the potential gradient; matrix-free PCG
/// solves its positive tangent. No stress is clipped AFTER solving velocities.
///
/// r=sqrt(.5 D:D), Dyy=-(Dxx+Dzz), derivatives scaled by the coupling length.
/// tau=2*mu*r below yield; above it tau=Y+2*a*mu*(r-Y/(2*mu)).
/// Plastic excess = r-tau/(2*mu). This regularized series law allows overstress;
/// it is not ideal perfect plasticity, a tensile crack, a pressure-dependent
/// Drucker-Prager law, or a rule for changing the topology of plates.</summary>
public static class ViscoplasticSheet
{
    public const string AlgorithmId = "convex-four-point-biviscous-sheet-v1";

    public static SheetYieldResponse Response(double rate, double viscosity, double load, double residualRatio)
    {
        if (!double.IsFinite(rate) || rate < 0 || !double.IsFinite(viscosity) || viscosity <= 0 ||
            !double.IsFinite(load) || load <= 0 || !double.IsFinite(residualRatio) || residualRatio < .02 || residualRatio > 1)
            throw new ArgumentException("Invalid constitutive response.");
        double critical = load / (2 * viscosity), excess = Math.Max(0, rate - critical);
        double stress = excess == 0 ? 2 * viscosity * rate : load + 2 * residualRatio * viscosity * excess;
        double energy = excess == 0 ? 2 * viscosity * rate * rate
            : 2 * viscosity * critical * critical + 2 * load * excess + 2 * residualRatio * viscosity * excess * excess;
        double plastic = (1 - residualRatio) * excess;
        var result = new SheetYieldResponse(stress, 2 * viscosity * (excess == 0 ? 1 : residualRatio), energy,
            plastic, rate == 0 ? viscosity : stress / (2 * rate));
        if (!double.IsFinite(result.Potential) || !double.IsFinite(result.Stress) || !double.IsFinite(result.EffectiveViscosity))
            throw new ArithmeticException("Constitutive overflow.");
        return result;
    }

    public static ViscoplasticSheetSolution Solve(IReadOnlyList<double> preferredEast, IReadOnlyList<double> preferredSouth,
        IReadOnlyList<double> viscosity, IReadOnlyList<double> loads, int side, double dx, double dz,
        double couplingLength, SheetYieldOptions options, ThinSheetSolution? warmStart = null,
        double relativeTolerance = 1e-12, int maximumNewtonIterations = 64)
    {
        ArgumentNullException.ThrowIfNull(preferredEast); ArgumentNullException.ThrowIfNull(preferredSouth);
        ArgumentNullException.ThrowIfNull(viscosity); ArgumentNullException.ThrowIfNull(loads); ArgumentNullException.ThrowIfNull(options);
        int count = checked(side * side);
        if (side < 4 || side > 128 || preferredEast.Count != count || preferredSouth.Count != count || viscosity.Count != count || loads.Count != count ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 || !double.IsFinite(couplingLength) || couplingLength < 0 ||
            couplingLength > side * Math.Min(dx, dz) || !double.IsFinite(relativeTolerance) || relativeTolerance < 1e-14 || relativeTolerance > 1e-9 ||
            maximumNewtonIterations < 1 || maximumNewtonIterations > 128 ||
            preferredEast.Any(v => !double.IsFinite(v)) || preferredSouth.Any(v => !double.IsFinite(v)) ||
            viscosity.Any(v => !double.IsFinite(v) || v <= 0 || v > 1e6) || loads.Any(v => !double.IsFinite(v) || v <= 0 || v > 1e12))
            throw new ArgumentException("Invalid bounded plastic-sheet inputs.");
        if (warmStart is not null && (warmStart.East.Count != count || warmStart.South.Count != count ||
            warmStart.East.Any(v => !double.IsFinite(v)) || warmStart.South.Any(v => !double.IsFinite(v))))
            throw new ArgumentException("Invalid plastic warm start.");
        double hx = couplingLength / dx, hz = couplingLength / dz;
        var east = new int[count]; var west = new int[count]; var south = new int[count]; var north = new int[count];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x;
            east[i] = z * side + (x + 1) % side; west[i] = z * side + (x + side - 1) % side;
            south[i] = ((z + 1) % side) * side + x; north[i] = ((z + side - 1) % side) * side + x;
        }
        int size = 2 * count;
        var b = preferredEast.Concat(preferredSouth).ToArray();
        var v = warmStart is null ? (double[])b.Clone() : warmStart.East.Concat(warmStart.South).ToArray();
        var gradient = new double[size]; var diagonal = new double[size];
        var ex = new double[count]; var ez = new double[count]; var gamma = new double[count];
        var muq = new double[4 * count]; var radial = new double[4 * count];
        var tx = new double[count]; var tz = new double[count]; var tg = new double[count];
        var de = new double[count]; var dn = new double[count]; var dg = new double[count];
        double norm = Math.Max(1, Math.Sqrt(Dot(b, b))), target = norm * relativeTolerance;
        double energy = Evaluate(v, gradient, true), initial = energy;
        int outer = 0, innerTotal = 0;
        while (Math.Sqrt(Dot(gradient, gradient)) > target && outer < maximumNewtonIterations)
        {
            // Positive majorant diagonal: the true radial tangent can be smaller.
            Array.Fill(diagonal, 1d);
            for (int i = 0; i < count; i++)
            {
                double sum = 0;
                for (int q = 0; q < 4; q++)
                {
                    double mu = muq[4 * i + q] * .25; sum += mu;
                    int j = Corner(i, q);
                    diagonal[j] += mu * hz * hz; diagonal[south[j]] += mu * hz * hz;
                    diagonal[count + j] += mu * hx * hx; diagonal[count + east[j]] += mu * hx * hx;
                }
                diagonal[i] += 4 * sum * hx * hx; diagonal[west[i]] += 4 * sum * hx * hx;
                diagonal[count + i] += 4 * sum * hz * hz; diagonal[count + north[i]] += 4 * sum * hz * hz;
            }
            var step = new double[size]; var r = gradient.Select(x => -x).ToArray();
            var zvec = new double[size]; var p = new double[size]; var hp = new double[size];
            for (int i = 0; i < size; i++) p[i] = zvec[i] = r[i] / diagonal[i];
            double rz = Dot(r, zvec), gnorm = Math.Sqrt(Dot(r, r));
            double linearTarget = Math.Max(target * .1, gnorm * .005);
            int inner = 0;
            while (Math.Sqrt(Dot(r, r)) > linearTarget && inner < 2000)
            {
                ApplyHessian(p, hp); double curvature = Dot(p, hp);
                if (!(curvature > 0) || !double.IsFinite(curvature)) throw new ArithmeticException("Plastic tangent lost positive curvature.");
                double alpha = rz / curvature;
                for (int i = 0; i < size; i++) { step[i] += alpha * p[i]; r[i] -= alpha * hp[i]; }
                inner++;
                if (Math.Sqrt(Dot(r, r)) <= linearTarget) break;
                for (int i = 0; i < size; i++) zvec[i] = r[i] / diagonal[i];
                double next = Dot(r, zvec), beta = next / rz;
                for (int i = 0; i < size; i++) p[i] = zvec[i] + beta * p[i];
                rz = next;
            }
            if (inner == 2000) throw new ArithmeticException("Plastic tangent budget exhausted; no fallback.");
            innerTotal += inner;
            double slope = Dot(gradient, step);
            if (!(slope < 0)) throw new ArithmeticException("Plastic Newton step is not descending.");
            var candidate = new double[size]; var trialGradient = new double[size]; bool accepted = false;
            for (int trial = 0; trial < 32; trial++)
            {
                double length = Math.ScaleB(1d, -trial);
                for (int i = 0; i < size; i++) candidate[i] = v[i] + length * step[i];
                double trialEnergy = Evaluate(candidate, trialGradient, false);
                // Rounding slack is only for line-search energy. Acceptance of a
                // solve ALWAYS uses its freshly assembled force residual.
                double slack = 32 * 2.220446049250313e-16 * Math.Max(1, Math.Abs(energy));
                if (trialEnergy <= energy + 1e-4 * length * slope ||
                    (trialEnergy <= energy + slack && Dot(trialGradient, trialGradient) < Dot(gradient, gradient)))
                { v = candidate; energy = trialEnergy; accepted = true; break; }
            }
            if (!accepted) throw new ArithmeticException("Plastic energy line search failed; no kinematic fallback.");
            energy = Evaluate(v, gradient, true); outer++;
        }
        energy = Evaluate(v, gradient, true);
        double residual = Math.Sqrt(Dot(gradient, gradient)) / norm;
        if (!double.IsFinite(residual) || residual > relativeTolerance || v.Any(x => !double.IsFinite(x)))
            throw new ArithmeticException($"Plastic equilibrium not resolved: {residual:R}, Newton={outer}.");
        var rates = new double[count]; var yielded = new double[count]; var div = new double[count]; var shear = new double[count];
        double work = 0, diss = 0, meanX = 0, meanZ = 0, lawError = 0;
        FillStrain(v, ex, ez, gamma);
        for (int i = 0; i < count; i++)
        {
            for (int q = 0; q < 4; q++)
            {
                int j = Corner(i, q); double rr = Rate(ex[i], ez[i], gamma[j]);
                var response = Law(rr, i);
                diss += .5 * response.Stress * rr;
                if (response.PlasticRate > 0) yielded[i] += .25;
                rates[i] += couplingLength > 0 ? .25 * response.PlasticRate / couplingLength : 0;
                lawError = Math.Max(lawError, Math.Abs(response.Stress - 2 * viscosity[i] * (rr - response.PlasticRate)) / Math.Max(1, response.Stress));
            }
            work += v[i] * (b[i] - v[i]) + v[count + i] * (b[count + i] - v[count + i]);
            meanX += v[i] - b[i]; meanZ += v[count + i] - b[count + i];
            div[i] = (v[i] - v[west[i]]) / dx + (v[count + i] - v[count + north[i]]) / dz;
            shear[i] = (v[south[i]] - v[i]) / dz + (v[count + east[i]] - v[count + i]) / dx;
        }
        if (Math.Abs(work - diss) > 1e-9 * Math.Max(1, Math.Abs(diss))) throw new ArithmeticException("Plastic work/dissipation imbalance.");
        var velocity = new ThinSheetSolution(Array.AsReadOnly(v[..count]), Array.AsReadOnly(v[count..]), Array.AsReadOnly(div),
            Array.AsReadOnly(shear), innerTotal, residual, diss, work, Math.Max(Math.Abs(meanX), Math.Abs(meanZ)) / count);
        return new(velocity, Array.AsReadOnly(rates), Array.AsReadOnly(yielded), Array.AsReadOnly(loads.ToArray()), outer, initial, energy, lawError);

        int Corner(int i, int q) => q switch { 0 => i, 1 => west[i], 2 => north[i], _ => west[north[i]] };
        SheetYieldResponse Law(double r0, int i) => Response(r0, viscosity[i], loads[i], options.Enabled ? options.PostYieldRatio : 1);
        void FillStrain(double[] a, double[] xx, double[] zz, double[] g)
        {
            for (int i = 0; i < count; i++)
            {
                xx[i] = (a[i] - a[west[i]]) * hx; zz[i] = (a[count + i] - a[count + north[i]]) * hz;
                g[i] = (a[south[i]] - a[i]) * hz + (a[count + east[i]] - a[count + i]) * hx;
            }
        }
        void Adjoint(double[] xx, double[] zz, double[] gg, double[] output)
        {
            for (int i = 0; i < count; i++)
            {
                output[i] += hx * (xx[i] - xx[east[i]]) + hz * (gg[north[i]] - gg[i]);
                output[count + i] += hz * (zz[i] - zz[south[i]]) + hx * (gg[west[i]] - gg[i]);
            }
        }
        double Evaluate(double[] a, double[] g, bool cache)
        {
            FillStrain(a, de, dn, dg); Array.Clear(tx); Array.Clear(tz); Array.Clear(tg);
            double potential = 0;
            for (int k = 0; k < size; k++) { g[k] = a[k] - b[k]; potential += .5 * g[k] * g[k]; }
            for (int i = 0; i < count; i++) for (int q = 0; q < 4; q++)
            {
                int j = Corner(i, q), k = 4 * i + q; double rr = Rate(de[i], dn[i], dg[j]);
                var law = Law(rr, i); double mu = law.EffectiveViscosity;
                potential += .25 * law.Potential;
                tx[i] += .5 * mu * (2 * de[i] + dn[i]); tz[i] += .5 * mu * (de[i] + 2 * dn[i]); tg[j] += .25 * mu * dg[j];
                if (cache)
                {
                    muq[k] = mu;
                    radial[k] = law.PlasticRate > 0 ? (mu - options.PostYieldRatio * viscosity[i]) / (rr * rr) : 0;
                }
            }
            Adjoint(tx, tz, tg, g);
            if (cache) { Array.Copy(de, ex, count); Array.Copy(dn, ez, count); Array.Copy(dg, gamma, count); }
            if (!double.IsFinite(potential)) throw new ArithmeticException("Nonfinite plastic potential.");
            return potential;
        }
        void ApplyHessian(double[] direction, double[] output)
        {
            FillStrain(direction, de, dn, dg); Array.Clear(tx); Array.Clear(tz); Array.Clear(tg); Array.Copy(direction, output, size);
            for (int i = 0; i < count; i++) for (int q = 0; q < 4; q++)
            {
                int j = Corner(i, q), k = 4 * i + q; double mu = muq[k];
                double ax = 2 * ex[i] + ez[i], az = ex[i] + 2 * ez[i], ag = gamma[j] / 2;
                double dr = radial[k] * (ax * de[i] + az * dn[i] + ag * dg[j]);
                tx[i] += .25 * (2 * mu * (2 * de[i] + dn[i]) - dr * ax);
                tz[i] += .25 * (2 * mu * (de[i] + 2 * dn[i]) - dr * az);
                tg[j] += .25 * (mu * dg[j] - dr * ag);
            }
            Adjoint(tx, tz, tg, output);
        }
    }
    private static double Rate(double x, double z, double shear) => Math.Sqrt(.5 * (x*x + z*z + (x+z)*(x+z)) + .25 * shear*shear);
    private static double Dot(double[] a, double[] b)
    {
        double sum = 0, correction = 0;
        for (int i=0;i<a.Length;i++) { double term=a[i]*b[i]-correction,next=sum+term;correction=(next-sum)-term;sum=next; }
        return sum;
    }
}
